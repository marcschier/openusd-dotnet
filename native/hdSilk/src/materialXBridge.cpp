// Copyright (c) marcschier. Licensed under the MIT License.

#include "materialXBridge.h"

#include "pxr/base/tf/diagnostic.h"
#include "pxr/imaging/hd/tokens.h"
#include "pxr/imaging/hdMtlx/hdMtlx.h"

#include <MaterialXFormat/XmlIo.h>
#if defined(OPENUSD_HDSILK_WITH_MATERIALX_VULKAN)
#include <MaterialXGenGlsl/VkShaderGenerator.h>
#include <MaterialXGenShader/GenContext.h>
#include <MaterialXGenShader/Shader.h>

#include <shaderc/shaderc.hpp>
#endif

#if defined(OPENUSD_HDSILK_WITH_MATERIALX_VULKAN)
#include <cstdlib>
#include <memory>
#endif

PXR_NAMESPACE_OPEN_SCOPE

namespace
{
#if defined(OPENUSD_HDSILK_WITH_MATERIALX_VULKAN)
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
#if !defined(OPENUSD_HDSILK_WITH_MATERIALX_VULKAN)
    result.error = "hdSilk was built without the Vulkan MaterialX generator.";
    return result;
#else
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
    result.success = !result.fragmentSpirv.empty();
    return result;
#endif
}

PXR_NAMESPACE_CLOSE_SCOPE
