// Copyright (c) marcschier. Licensed under the MIT License.

#include "material.h"

#include "openusd_hdsilk.h"
#include "renderDelegate.h"
#include "sceneState.h"

#include "pxr/base/gf/vec2f.h"
#include "pxr/base/gf/vec3f.h"
#include "pxr/base/gf/vec4f.h"
#include "pxr/base/tf/diagnostic.h"
#include "pxr/base/tf/staticTokens.h"
#include "pxr/imaging/hd/sceneDelegate.h"
#include "pxr/imaging/hd/tokens.h"
#include "pxr/usd/sdf/assetPath.h"

#include <algorithm>
#include <map>
#include <utility>

PXR_NAMESPACE_OPEN_SCOPE

// clang-format off
// The double-parenthesis tuple form is required: with a bare (name) element the
// TF_PP_IS_TUPLE expansion leaves a variadic macro argument empty, which Clang
// rejects under -Werror,-Wvariadic-macro-arguments-omitted on the macOS build.
TF_DEFINE_PRIVATE_TOKENS(
    _tokens,
    ((UsdPreviewSurface, "UsdPreviewSurface"))
    ((UsdUVTexture, "UsdUVTexture"))
    ((diffuseColor, "diffuseColor"))
    ((emissiveColor, "emissiveColor"))
    ((specularColor, "specularColor"))
    ((metallic, "metallic"))
    ((roughness, "roughness"))
    ((clearcoat, "clearcoat"))
    ((clearcoatRoughness, "clearcoatRoughness"))
    ((opacity, "opacity"))
    ((opacityThreshold, "opacityThreshold"))
    ((ior, "ior"))
    ((normal, "normal"))
    ((displacement, "displacement"))
    ((occlusion, "occlusion"))
    ((useSpecularWorkflow, "useSpecularWorkflow"))
    ((file, "file"))
    ((st, "st"))
    ((varname, "varname"))
    ((wrapS, "wrapS"))
    ((wrapT, "wrapT"))
    ((scale, "scale"))
    ((bias, "bias"))
    ((fallback, "fallback"))
    ((sourceColorSpace, "sourceColorSpace"))
    ((clamp, "clamp"))
    ((repeat, "repeat"))
    ((mirror, "mirror"))
    ((raw, "raw"))
    ((sRGB, "sRGB"))
);
// clang-format on

namespace
{
/// One UsdPreviewSurface input, with the wire parameter id and how many float
/// components it occupies. Kept as a table rather than a switch so adding an
/// input is one line and the scalar and texture paths cannot disagree.
struct _InputBinding
{
    const TfToken& name;
    uint32_t parameter;
    uint32_t componentCount;
};

const std::vector<_InputBinding>&
_PreviewSurfaceInputs()
{
    static const std::vector<_InputBinding> inputs = {
        {_tokens->diffuseColor, OPENUSD_SILK_MATERIAL_DIFFUSE_COLOR, 3},
        {_tokens->emissiveColor, OPENUSD_SILK_MATERIAL_EMISSIVE_COLOR, 3},
        {_tokens->specularColor, OPENUSD_SILK_MATERIAL_SPECULAR_COLOR, 3},
        {_tokens->metallic, OPENUSD_SILK_MATERIAL_METALLIC, 1},
        {_tokens->roughness, OPENUSD_SILK_MATERIAL_ROUGHNESS, 1},
        {_tokens->clearcoat, OPENUSD_SILK_MATERIAL_CLEARCOAT, 1},
        {_tokens->clearcoatRoughness,
            OPENUSD_SILK_MATERIAL_CLEARCOAT_ROUGHNESS, 1},
        {_tokens->opacity, OPENUSD_SILK_MATERIAL_OPACITY, 1},
        {_tokens->opacityThreshold, OPENUSD_SILK_MATERIAL_OPACITY_THRESHOLD, 1},
        {_tokens->ior, OPENUSD_SILK_MATERIAL_IOR, 1},
        {_tokens->normal, OPENUSD_SILK_MATERIAL_NORMAL, 3},
        {_tokens->displacement, OPENUSD_SILK_MATERIAL_DISPLACEMENT, 1},
        {_tokens->occlusion, OPENUSD_SILK_MATERIAL_OCCLUSION, 1},
        {_tokens->useSpecularWorkflow,
            OPENUSD_SILK_MATERIAL_USE_SPECULAR_WORKFLOW, 1}};
    return inputs;
}

/// Reads a VtValue into up to four floats, returning how many it filled.
/// Returns zero when the value holds a type this delegate cannot represent,
/// which leaves the parameter absent from the wire rather than guessed at.
uint32_t _ReadFloats(const VtValue& value, float (&out)[4])
{
    out[0] = out[1] = out[2] = out[3] = 0.0f;
    if (value.IsHolding<float>())
    {
        out[0] = value.UncheckedGet<float>();
        return 1;
    }
    if (value.IsHolding<double>())
    {
        out[0] = static_cast<float>(value.UncheckedGet<double>());
        return 1;
    }
    if (value.IsHolding<int>())
    {
        out[0] = static_cast<float>(value.UncheckedGet<int>());
        return 1;
    }
    if (value.IsHolding<bool>())
    {
        out[0] = value.UncheckedGet<bool>() ? 1.0f : 0.0f;
        return 1;
    }
    if (value.IsHolding<GfVec2f>())
    {
        const GfVec2f vector = value.UncheckedGet<GfVec2f>();
        out[0] = vector[0];
        out[1] = vector[1];
        return 2;
    }
    if (value.IsHolding<GfVec3f>())
    {
        const GfVec3f vector = value.UncheckedGet<GfVec3f>();
        out[0] = vector[0];
        out[1] = vector[1];
        out[2] = vector[2];
        return 3;
    }
    if (value.IsHolding<GfVec4f>())
    {
        const GfVec4f vector = value.UncheckedGet<GfVec4f>();
        for (int index = 0; index < 4; ++index)
        {
            out[index] = vector[index];
        }
        return 4;
    }
    return 0;
}

uint32_t _ReadWrap(const std::map<TfToken, VtValue>& parameters, const TfToken& name)
{
    const auto entry = parameters.find(name);
    if (entry == parameters.end() || !entry->second.IsHolding<TfToken>())
    {
        // UsdUVTexture leaves wrap at useMetadata, whose practical fallback in
        // the absence of texture metadata is black.
        return OPENUSD_SILK_WRAP_BLACK;
    }
    const TfToken wrap = entry->second.UncheckedGet<TfToken>();
    if (wrap == _tokens->clamp)
    {
        return OPENUSD_SILK_WRAP_CLAMP;
    }
    if (wrap == _tokens->repeat)
    {
        return OPENUSD_SILK_WRAP_REPEAT;
    }
    if (wrap == _tokens->mirror)
    {
        return OPENUSD_SILK_WRAP_MIRROR;
    }
    return OPENUSD_SILK_WRAP_BLACK;
}

uint32_t _ReadColorSpace(const std::map<TfToken, VtValue>& parameters)
{
    const auto entry = parameters.find(_tokens->sourceColorSpace);
    if (entry == parameters.end() || !entry->second.IsHolding<TfToken>())
    {
        return OPENUSD_SILK_COLOR_SPACE_AUTO;
    }
    const TfToken space = entry->second.UncheckedGet<TfToken>();
    if (space == _tokens->raw)
    {
        return OPENUSD_SILK_COLOR_SPACE_RAW;
    }
    return space == _tokens->sRGB
        ? OPENUSD_SILK_COLOR_SPACE_SRGB
        : OPENUSD_SILK_COLOR_SPACE_AUTO;
}

void _ReadVector4(
    const std::map<TfToken, VtValue>& parameters,
    const TfToken& name,
    float (&out)[4])
{
    const auto entry = parameters.find(name);
    if (entry == parameters.end())
    {
        return;
    }
    float values[4] = {0.0f, 0.0f, 0.0f, 0.0f};
    const uint32_t count = _ReadFloats(entry->second, values);
    for (uint32_t index = 0; index < count; ++index)
    {
        out[index] = values[index];
    }
}

const HdMaterialNode* _FindNode(
    const HdMaterialNetwork& network,
    const SdfPath& path)
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

/// Finds the surface terminal. The terminal is the node no other node consumes,
/// which for a UsdPreviewSurface network is the last node.
const HdMaterialNode* _FindSurface(const HdMaterialNetwork& network)
{
    for (auto node = network.nodes.rbegin(); node != network.nodes.rend(); ++node)
    {
        if (node->identifier == _tokens->UsdPreviewSurface)
        {
            return &(*node);
        }
    }
    return nullptr;
}

/// Resolves the UV primvar a texture reads, by following its st connection to a
/// UsdPrimvarReader_float2 and taking that reader's varname.
std::string _ResolveUvPrimvar(
    const HdMaterialNetwork& network,
    const SdfPath& texturePath)
{
    for (const HdMaterialRelationship& relationship : network.relationships)
    {
        if (relationship.outputId != texturePath ||
            relationship.outputName != _tokens->st)
        {
            continue;
        }
        const HdMaterialNode* reader = _FindNode(network, relationship.inputId);
        if (reader == nullptr)
        {
            continue;
        }
        const auto varname = reader->parameters.find(_tokens->varname);
        if (varname == reader->parameters.end())
        {
            continue;
        }
        if (varname->second.IsHolding<TfToken>())
        {
            return varname->second.UncheckedGet<TfToken>().GetString();
        }
        if (varname->second.IsHolding<std::string>())
        {
            return varname->second.UncheckedGet<std::string>();
        }
    }
    return std::string();
}

std::string _ResolveAssetPath(const std::map<TfToken, VtValue>& parameters)
{
    const auto entry = parameters.find(_tokens->file);
    if (entry == parameters.end())
    {
        return std::string();
    }
    if (entry->second.IsHolding<SdfAssetPath>())
    {
        const SdfAssetPath asset = entry->second.UncheckedGet<SdfAssetPath>();
        // The resolved path is what a consumer can actually open; the authored
        // path may be relative to a layer the consumer never sees.
        return asset.GetResolvedPath().empty()
            ? asset.GetAssetPath()
            : asset.GetResolvedPath();
    }
    return entry->second.IsHolding<std::string>()
        ? entry->second.UncheckedGet<std::string>()
        : std::string();
}
}

HdSilkMaterial::HdSilkMaterial(SdfPath const& id)
    : HdMaterial(id)
{
}

HdDirtyBits
HdSilkMaterial::GetInitialDirtyBitsMask() const
{
    return HdMaterial::AllDirty;
}

HdSilkMaterialRecord
HdSilkMaterial::Resolve(
    SdfPath const& id,
    HdMaterialNetworkMap const& networkMap)
{
    HdSilkMaterialRecord record;
    record.path = id.GetString();
    record.surfaceKind = OPENUSD_SILK_SURFACE_UNSUPPORTED;

    const auto surfaceNetwork = networkMap.map.find(HdMaterialTerminalTokens->surface);
    if (surfaceNetwork == networkMap.map.end())
    {
        return record;
    }
    const HdMaterialNetwork& network = surfaceNetwork->second;
    const HdMaterialNode* surface = _FindSurface(network);
    if (surface == nullptr)
    {
        // Publishing the unsupported record rather than nothing is deliberate:
        // the consumer can then say which material it could not shade.
        return record;
    }
    record.surfaceKind = OPENUSD_SILK_SURFACE_PREVIEW_SURFACE;

    for (const _InputBinding& input : _PreviewSurfaceInputs())
    {
        // A connected input is described by the texture table; its authored
        // constant, if any, is only a fallback the texture entry already
        // carries, so it must not also appear as a scalar.
        const HdMaterialNode* texture = nullptr;
        for (const HdMaterialRelationship& relationship : network.relationships)
        {
            if (relationship.outputId != surface->path ||
                relationship.outputName != input.name)
            {
                continue;
            }
            const HdMaterialNode* upstream =
                _FindNode(network, relationship.inputId);
            if (upstream != nullptr &&
                upstream->identifier == _tokens->UsdUVTexture)
            {
                texture = upstream;
            }
            break;
        }

        if (texture != nullptr)
        {
            const std::string asset = _ResolveAssetPath(texture->parameters);
            if (asset.empty())
            {
                TF_WARN(
                    "hdSilk material '%s' input '%s' has a texture with no "
                    "resolvable file; leaving the input at its default.",
                    record.path.c_str(),
                    input.name.GetText());
                continue;
            }
            HdSilkMaterialTexture entry;
            entry.parameter = input.parameter;
            entry.componentCount = input.componentCount;
            entry.wrapS = _ReadWrap(texture->parameters, _tokens->wrapS);
            entry.wrapT = _ReadWrap(texture->parameters, _tokens->wrapT);
            entry.sourceColorSpace = _ReadColorSpace(texture->parameters);
            entry.scale[0] = entry.scale[1] = entry.scale[2] = entry.scale[3] = 1.0f;
            _ReadVector4(texture->parameters, _tokens->scale, entry.scale);
            _ReadVector4(texture->parameters, _tokens->bias, entry.bias);
            _ReadVector4(texture->parameters, _tokens->fallback, entry.fallback);
            entry.asset = asset;
            entry.uvPrimvar = _ResolveUvPrimvar(network, texture->path);
            record.textures.push_back(std::move(entry));
            continue;
        }

        const auto authored = surface->parameters.find(input.name);
        if (authored == surface->parameters.end())
        {
            continue;
        }
        float values[4] = {0.0f, 0.0f, 0.0f, 0.0f};
        const uint32_t count = _ReadFloats(authored->second, values);
        if (count == 0)
        {
            TF_WARN(
                "hdSilk material '%s' input '%s' holds an unsupported value "
                "type; leaving the input at its default.",
                record.path.c_str(),
                input.name.GetText());
            continue;
        }
        HdSilkMaterialScalar scalar;
        scalar.parameter = input.parameter;
        scalar.componentCount = std::min(count, input.componentCount);
        for (uint32_t index = 0; index < scalar.componentCount; ++index)
        {
            scalar.value[index] = values[index];
        }
        record.scalars.push_back(scalar);
    }

    return record;
}

void
HdSilkMaterial::Sync(
    HdSceneDelegate* sceneDelegate,
    HdRenderParam* renderParam,
    HdDirtyBits* dirtyBits)
{
    if (sceneDelegate == nullptr || dirtyBits == nullptr)
    {
        return;
    }
    SdfPath const& id = GetId();
    if ((*dirtyBits & HdMaterial::DirtyResource) == 0)
    {
        *dirtyBits = HdMaterial::Clean;
        return;
    }

    auto* silkRenderParam = static_cast<HdSilkRenderParam*>(renderParam);
    const std::shared_ptr<HdSilkSceneState> sceneState =
        silkRenderParam != nullptr ? silkRenderParam->GetSceneStatePtr() : nullptr;
    if (!sceneState)
    {
        *dirtyBits = HdMaterial::Clean;
        return;
    }

    const VtValue resource = sceneDelegate->GetMaterialResource(id);
    if (!resource.IsHolding<HdMaterialNetworkMap>())
    {
        // No resource at all is a different condition from an unsupported
        // graph, and is reported as unsupported so the consumer still knows the
        // material exists and cannot be shaded.
        HdSilkMaterialRecord record;
        record.path = id.GetString();
        record.surfaceKind = OPENUSD_SILK_SURFACE_UNSUPPORTED;
        sceneState->ReplaceMaterial(std::move(record));
        *dirtyBits = HdMaterial::Clean;
        return;
    }

    sceneState->ReplaceMaterial(
        Resolve(id, resource.UncheckedGet<HdMaterialNetworkMap>()));
    *dirtyBits = HdMaterial::Clean;
}

PXR_NAMESPACE_CLOSE_SCOPE
