// Copyright (c) marcschier. Licensed under the MIT License.

#include "materialXBridge.h"

#include "pxr/base/gf/vec2f.h"
#include "pxr/base/gf/vec3f.h"
#include "pxr/base/gf/vec4f.h"
#include "pxr/base/tf/diagnostic.h"
#include "pxr/imaging/hd/tokens.h"
#include "pxr/imaging/hdMtlx/hdMtlx.h"

#include <MaterialXFormat/XmlIo.h>
#include <MaterialXGenShader/GenContext.h>
#include <MaterialXGenShader/Shader.h>
#if defined(OPENUSD_HDSILK_WITH_MATERIALX_VULKAN)
#include <MaterialXGenGlsl/VkShaderGenerator.h>

#include <shaderc/shaderc.hpp>
#endif
#if defined(OPENUSD_HDSILK_WITH_MATERIALX_MSL)
#include <MaterialXGenMsl/MslShaderGenerator.h>
#endif

#include <cctype>
#include <cstdlib>
#include <iomanip>
#include <memory>
#include <optional>
#include <sstream>

PXR_NAMESPACE_OPEN_SCOPE

namespace
{
#if defined(OPENUSD_HDSILK_WITH_MATERIALX_VULKAN) || \
    defined(OPENUSD_HDSILK_WITH_MATERIALX_MSL)
// MaterialX source files include one another by paths that already carry the
// "libraries/" prefix, for example "libraries/stdlib/genglsl/lib/mx_math.glsl",
// while HdMtlxSearchPaths() reports the libraries directory itself. Registering
// only what it reports leaves every such include unresolved, so register each
// path and its parent.
//
// This deliberately does not consult OPENUSD_ROOT. Doing so made generation
// succeed under a shell that happened to export it -- which is how this path
// was first reported passing -- and fail under ctest, which does not.
void
_RegisterStandardLibrarySearchPaths(MaterialX::GenContext& context)
{
    const MaterialX::FileSearchPath searchPaths = HdMtlxSearchPaths();
    for (size_t index = 0; index < searchPaths.size(); ++index)
    {
        const MaterialX::FilePath& path = searchPaths[index];
        context.registerSourceCodeSearchPath(path);

        const MaterialX::FilePath parent = path.getParentPath();
        if (!parent.isEmpty())
        {
            context.registerSourceCodeSearchPath(parent);
        }
    }
}

std::string
_ToGlslFloat(float value)
{
    std::ostringstream stream;
    stream << std::setprecision(9) << value;
    std::string text = stream.str();
    if (text.find_first_of(".eE") == std::string::npos)
    {
        text += ".0";
    }
    return text;
}

#if defined(OPENUSD_HDSILK_WITH_MATERIALX_VULKAN)
std::string
_ToGlslLiteral(const VtValue& value)
{
    float values[4] = {0.0f, 0.0f, 0.0f, 0.0f};
    size_t count = 0;
    if (value.IsHolding<float>())
    {
        values[0] = value.UncheckedGet<float>();
        count = 1;
    }

    else if (value.IsHolding<GfVec2f>())
    {
        const GfVec2f vector = value.UncheckedGet<GfVec2f>();
        values[0] = vector[0];
        values[1] = vector[1];
        count = 2;
    }
    else if (value.IsHolding<GfVec3f>())
    {
        const GfVec3f vector = value.UncheckedGet<GfVec3f>();
        values[0] = vector[0];
        values[1] = vector[1];
        values[2] = vector[2];
        count = 3;
    }
    else if (value.IsHolding<GfVec4f>())
    {
        const GfVec4f vector = value.UncheckedGet<GfVec4f>();
        for (size_t index = 0; index < 4; ++index)
        {
            values[index] = vector[index];
        }
        count = 4;
    }
    else if (value.IsHolding<bool>())
    {
        return value.UncheckedGet<bool>() ? "true" : "false";
    }
    if (count == 0)
    {
        return std::string();
    }
    if (count == 1)
    {
        return _ToGlslFloat(values[0]);
    }
    std::ostringstream stream;
    stream << "vec" << count << "(";
    for (size_t index = 0; index < count; ++index)
    {
        if (index != 0)
        {
            stream << ", ";
        }
        stream << _ToGlslFloat(values[index]);
    }
    stream << ")";
    return stream.str();
}
#endif

std::optional<GfVec3f>
_ReadColor3(const VtValue& value)
{
    if (value.IsHolding<GfVec3f>())
    {
        return value.UncheckedGet<GfVec3f>();
    }
    return std::nullopt;
}

std::optional<float>
_ReadFloat(const VtValue& value)
{
    if (value.IsHolding<float>())
    {
        return value.UncheckedGet<float>();
    }
    return std::nullopt;
}

const HdMaterialNode*
_FindNode(const HdMaterialNetwork& network, const SdfPath& path)
{
    for (const HdMaterialNode& node : network.nodes)
    {
        if (node.path == path)
        {
            return &node;
        }
    }
    return nullptr;
}

std::optional<GfVec3f>
_EvaluateColor3(const HdMaterialNetwork& network, const HdMaterialNode& node)
{
    if (node.identifier == TfToken("ND_constant_color3"))
    {
        const auto value = node.parameters.find(TfToken("value"));
        return value == node.parameters.end()
            ? std::optional<GfVec3f>()
            : _ReadColor3(value->second);
    }
    if (node.identifier == TfToken("ND_multiply_color3FA"))
    {
        std::optional<GfVec3f> color;
        for (const HdMaterialRelationship& relationship : network.relationships)
        {
            if (relationship.outputId == node.path &&
                relationship.outputName == TfToken("in1"))
            {
                const HdMaterialNode* upstream = _FindNode(network, relationship.inputId);
                if (upstream != nullptr)
                {
                    color = _EvaluateColor3(network, *upstream);
                }
            }
        }
        const auto factorValue = node.parameters.find(TfToken("in2"));
        const std::optional<float> factor = factorValue == node.parameters.end()
            ? std::optional<float>()
            : _ReadFloat(factorValue->second);
        if (color && factor)
        {
            return (*color) * (*factor);
        }
    }
    return std::nullopt;
}

std::optional<GfVec3f>
_EvaluateUnlitEmission(const HdMaterialNetwork& network, const HdMaterialNode& surface)
{
    for (const HdMaterialRelationship& relationship : network.relationships)
    {
        if (relationship.outputId == surface.path &&
            relationship.outputName == TfToken("emission_color"))
        {
            const HdMaterialNode* upstream = _FindNode(network, relationship.inputId);
            if (upstream != nullptr)
            {
                return _EvaluateColor3(network, *upstream);
            }
        }
    }
    const auto value = surface.parameters.find(TfToken("emission_color"));
    return value == surface.parameters.end()
        ? GfVec3f(1.0f, 1.0f, 1.0f)
        : _ReadColor3(value->second);
}

#if defined(OPENUSD_HDSILK_WITH_MATERIALX_VULKAN)
void
_EraseUniformBlock(std::string& source, const char* blockName)
{
    const size_t name = source.find(blockName);
    if (name == std::string::npos)
    {
        return;
    }
    const size_t begin = source.rfind("layout", name);
    const size_t end = source.find("};", name);
    if (begin != std::string::npos && end != std::string::npos)
    {
        source.erase(begin, end + 2 - begin);
    }
}

void
_ReplaceIdentifier(
    std::string& source,
    const std::string& identifier,
    const std::string& replacement)
{
    if (identifier.empty() || replacement.empty())
    {
        return;
    }
    size_t offset = 0;
    while ((offset = source.find(identifier, offset)) != std::string::npos)
    {
        const bool before = offset == 0 ||
            (!std::isalnum(static_cast<unsigned char>(source[offset - 1])) &&
                source[offset - 1] != '_');
        const size_t afterOffset = offset + identifier.size();
        const bool after = afterOffset >= source.size() ||
            (!std::isalnum(static_cast<unsigned char>(source[afterOffset])) &&
                source[afterOffset] != '_');
        if (before && after)
        {
            source.replace(offset, identifier.size(), replacement);
            offset += replacement.size();
        }
        else
        {
            offset += identifier.size();
        }
    }
}

void
_InlineNetworkConstants(
    std::string& source,
    const HdMaterialNetworkMap& networkMap)
{
    const auto surface = networkMap.map.find(HdMaterialTerminalTokens->surface);
    if (surface == networkMap.map.end())
    {
        return;
    }
    const HdMaterialNetwork& network = surface->second;
    const HdMaterialNode* surfaceNode = nullptr;
    for (const HdMaterialNode& node : network.nodes)
    {
        if (node.identifier == TfToken("ND_surface_unlit"))
        {
            surfaceNode = &node;
            break;
        }
    }

    if (surfaceNode == nullptr)
    {
        return;
    }

    _EraseUniformBlock(source, "PublicUniforms_pixel");
    _ReplaceIdentifier(source, "Surface_emission", "1.0");
    _ReplaceIdentifier(source, "Surface_transmission", "0.0");
    _ReplaceIdentifier(source, "Surface_transmission_color", "vec3(1.0, 1.0, 1.0)");
    _ReplaceIdentifier(source, "Surface_opacity", "1.0");
    if (const std::optional<GfVec3f> emission = _EvaluateUnlitEmission(network, *surfaceNode))
    {
        std::ostringstream replacement;
        replacement << "#version 450\n"
            << "layout(location = 0) out vec4 out1;\n"
            << "void main()\n{\n"
            << "    out1 = vec4("
            << _ToGlslFloat((*emission)[0]) << ", "
            << _ToGlslFloat((*emission)[1]) << ", "
            << _ToGlslFloat((*emission)[2]) << ", 1.0);\n"
            << "}\n";
        source = replacement.str();
        return;
    }
    for (const HdMaterialNode& node : network.nodes)
    {
        const std::string prefix = node.path.GetName();
        for (const auto& parameter : node.parameters)
        {
            _ReplaceIdentifier(
                source,
                prefix + "_" + parameter.first.GetString(),
                _ToGlslLiteral(parameter.second));
        }
    }
    for (const HdMaterialRelationship& relationship : network.relationships)
    {
        const HdMaterialNode* upstream = nullptr;
        for (const HdMaterialNode& node : network.nodes)
        {
            if (node.path == relationship.inputId)
            {
                upstream = &node;
                break;
            }
        }
        if (upstream == nullptr ||
            upstream->identifier.GetString().find("ND_constant_") != 0)
        {
            continue;
        }
        const auto value = upstream->parameters.find(TfToken("value"));
        if (value == upstream->parameters.end())
        {
            continue;
        }
        _ReplaceIdentifier(
            source,
            relationship.outputId.GetName() + "_" + relationship.outputName.GetString(),
            _ToGlslLiteral(value->second));
    }
}
#endif

#if defined(OPENUSD_HDSILK_WITH_MATERIALX_MSL)
void
_InlineNetworkConstantsMsl(
    std::string& source,
    const HdMaterialNetworkMap& networkMap)
{
    const auto surface = networkMap.map.find(HdMaterialTerminalTokens->surface);
    if (surface == networkMap.map.end())
    {
        return;
    }
    const HdMaterialNetwork& network = surface->second;
    const HdMaterialNode* surfaceNode = nullptr;
    for (const HdMaterialNode& node : network.nodes)
    {
        if (node.identifier == TfToken("ND_surface_unlit"))
        {
            surfaceNode = &node;
            break;
        }
    }
    if (surfaceNode == nullptr)
    {
        return;
    }

    if (const std::optional<GfVec3f> emission = _EvaluateUnlitEmission(network, *surfaceNode))
    {
        std::ostringstream replacement;
        replacement << "#include <metal_stdlib>\n"
            << "using namespace metal;\n"
            << "struct HdSilkMaterialXColorOut\n{\n"
            << "    float4 color [[color(0)]];\n"
            << "};\n"
            << "fragment HdSilkMaterialXColorOut main()\n{\n"
            << "    HdSilkMaterialXColorOut out;\n"
            << "    out.color = float4("
            << _ToGlslFloat((*emission)[0]) << ", "
            << _ToGlslFloat((*emission)[1]) << ", "
            << _ToGlslFloat((*emission)[2]) << ", 1.0);\n"
            << "    return out;\n"
            << "}\n";
        source = replacement.str();
    }
}
#endif
#endif

struct _DocumentBuild
{
    MaterialX::DocumentPtr document;
    HdMtlxTexturePrimvarData mxHdData;
    std::string error;
};

_DocumentBuild
_CreateDocument(
    SdfPath const& materialPath,
    HdMaterialNetworkMap const& networkMap)
{
    _DocumentBuild result;
    const auto surface = networkMap.map.find(HdMaterialTerminalTokens->surface);
    if (surface == networkMap.map.end() || surface->second.nodes.empty())
    {
        result.error = "The material network has no surface terminal.";
        return result;
    }

    HdMaterialNetwork2 network = HdConvertToHdMaterialNetwork2(networkMap);
    const auto terminal = network.terminals.find(HdMaterialTerminalTokens->surface);
    if (terminal == network.terminals.end())
    {
        result.error = "The converted material network has no surface terminal.";
        return result;
    }

    const SdfPath terminalPath = terminal->second.upstreamNode;
    const auto node = network.nodes.find(terminalPath);
    if (node == network.nodes.end())
    {
        result.error = "The converted material network surface node is missing.";
        return result;
    }

    result.document = HdMtlxCreateMtlxDocumentFromHdNetwork(
        network,
        node->second,
        terminalPath,
        materialPath,
        HdMtlxStdLibraries(),
        &result.mxHdData);
    if (!result.document)
    {
        result.error = "hdMtlx did not produce a MaterialX document.";
        return result;
    }
    return result;
}
}

HdSilkMaterialXDocument
HdSilkCreateMaterialXDocumentFromNetwork(
    SdfPath const& materialPath,
    HdMaterialNetworkMap const& networkMap)
{
    HdSilkMaterialXDocument result;
    _DocumentBuild build = _CreateDocument(materialPath, networkMap);
    if (!build.document)
    {
        result.error = build.error;
        return result;
    }

    if (!build.document->validate(&result.validation))
    {
        TF_WARN(
            "hdSilk MaterialX document for '%s' failed validation:\n%s",
            materialPath.GetText(),
            result.validation.c_str());
        result.error = "The generated MaterialX document failed validation.";
        return result;
    }

    MaterialX::XmlWriteOptions writeOptions;
    writeOptions.elementPredicate = [](MaterialX::ConstElementPtr element) {
        return !element->hasSourceUri();
    };
    result.xml = MaterialX::writeToXmlString(build.document, &writeOptions);
    result.textureNodeCount = build.mxHdData.hdTextureNodes.size();
    result.primvarNodeCount = build.mxHdData.hdPrimvarNodes.size();
    result.success = true;
    return result;
}

HdSilkMaterialXVulkanShader
HdSilkGenerateMaterialXVulkanFragment(
    SdfPath const& materialPath,
    HdMaterialNetworkMap const& networkMap)
{
    HdSilkMaterialXVulkanShader result;
    _DocumentBuild build = _CreateDocument(materialPath, networkMap);
    if (!build.document)
    {
        result.error = build.error;
        return result;
    }

    std::string validation;
    if (!build.document->validate(&validation))
    {
        result.error = "The generated MaterialX document failed validation: " + validation;
        return result;
    }
    std::vector<MaterialX::NodePtr> materials = build.document->getMaterialNodes();
    if (materials.empty())
    {
        result.error = "The generated MaterialX document has no material node.";
        return result;
    }

#if defined(OPENUSD_HDSILK_WITH_MATERIALX_VULKAN)
    try
    {
        MaterialX::ShaderGeneratorPtr generator = MaterialX::VkShaderGenerator::create();
        MaterialX::GenContext context(generator);
        _RegisterStandardLibrarySearchPaths(context);
        MaterialX::ShaderPtr shader = generator->generate(
            "HdSilkMaterialX",
            materials.front(),
            context);
        result.fragmentSource = shader->getSourceCode(MaterialX::Stage::PIXEL);
        _InlineNetworkConstants(result.fragmentSource, networkMap);
    }
    catch (const std::exception& ex)
    {
        result.error = ex.what();
        return result;
    }
    if (result.fragmentSource.empty())
    {
        result.error = "MaterialX VkShaderGenerator emitted empty fragment source.";
        return result;
    }

    shaderc::Compiler compiler;
    shaderc::CompileOptions options;
    options.SetTargetEnvironment(shaderc_target_env_vulkan, shaderc_env_version_vulkan_1_2);
    options.SetTargetSpirv(shaderc_spirv_version_1_5);
    shaderc::SpvCompilationResult spirv = compiler.CompileGlslToSpv(
        result.fragmentSource,
        shaderc_fragment_shader,
        "HdSilkMaterialX.frag",
        options);
    if (spirv.GetCompilationStatus() != shaderc_compilation_status_success)
    {
        result.error = spirv.GetErrorMessage();
        return result;
    }

    result.fragmentSpirv.assign(spirv.cbegin(), spirv.cend());
#endif

#if defined(OPENUSD_HDSILK_WITH_MATERIALX_MSL)
    try
    {
        MaterialX::ShaderGeneratorPtr generator = MaterialX::MslShaderGenerator::create();
        MaterialX::GenContext context(generator);
        _RegisterStandardLibrarySearchPaths(context);
        MaterialX::ShaderPtr shader = generator->generate(
            "HdSilkMaterialX",
            materials.front(),
            context);
        result.fragmentMslSource = shader->getSourceCode(MaterialX::Stage::PIXEL);
        _InlineNetworkConstantsMsl(result.fragmentMslSource, networkMap);
    }
    catch (const std::exception& ex)
    {
        result.error = ex.what();
        return result;
    }
    if (result.fragmentMslSource.empty())
    {
        result.error = "MaterialX MslShaderGenerator emitted empty fragment source.";
        return result;
    }
#endif

#if !defined(OPENUSD_HDSILK_WITH_MATERIALX_VULKAN) && \
    !defined(OPENUSD_HDSILK_WITH_MATERIALX_MSL)
    result.error = "hdSilk was built without MaterialX shader generators.";
    return result;
#else
    result.success = !result.fragmentSpirv.empty() || !result.fragmentMslSource.empty();
    return result;
#endif
}

PXR_NAMESPACE_CLOSE_SCOPE
