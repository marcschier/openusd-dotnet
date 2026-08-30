// Copyright (c) marcschier. Licensed under the MIT License.

#include "material.h"

#include "materialXBridge.h"
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
#include <cmath>
#include <cstddef>
#include <map>
#include <utility>
#include <vector>

PXR_NAMESPACE_OPEN_SCOPE

// clang-format off
// The double-parenthesis tuple form is required: with a bare (name) element the
// TF_PP_IS_TUPLE expansion leaves a variadic macro argument empty, which Clang
// rejects under -Werror,-Wvariadic-macro-arguments-omitted on the macOS build.
TF_DEFINE_PRIVATE_TOKENS(
    _tokens,
    ((UsdPreviewSurface, "UsdPreviewSurface"))
    ((UsdUVTexture, "UsdUVTexture"))
    ((MtlxStandardSurface, "ND_standard_surface_surfaceshader"))
    ((MtlxSurfaceUnlit, "ND_surface_unlit"))
    ((MtlxNormalMap, "ND_normalmap"))
    ((MtlxPlace2d, "ND_place2d_vector2"))
    ((diffuseColor, "diffuseColor"))
    ((base_color, "base_color"))
    ((emissiveColor, "emissiveColor"))
    ((emission_color, "emission_color"))
    ((specularColor, "specularColor"))
    ((metallic, "metallic"))
    ((metalness, "metalness"))
    ((roughness, "roughness"))
    ((specular_roughness, "specular_roughness"))
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
    ((texcoord, "texcoord"))
    ((varname, "varname"))
    ((geomprop, "geomprop"))
    ((pivot, "pivot"))
    ((rotate, "rotate"))
    ((offset, "offset"))
    ((operationorder, "operationorder"))
    ((in, "in"))
    ((in1, "in1"))
    ((in2, "in2"))
    ((low, "low"))
    ((high, "high"))
    ((bg, "bg"))
    ((fg, "fg"))
    ((mix, "mix"))
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
    ((r, "r"))
    ((g, "g"))
    ((b, "b"))
    ((a, "a"))
    ((rgb, "rgb"))
    ((out, "out"))
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

const std::vector<_InputBinding>&
_MaterialXStandardSurfaceInputs()
{
    static const std::vector<_InputBinding> inputs = {
        {_tokens->base_color, OPENUSD_SILK_MATERIAL_DIFFUSE_COLOR, 3},
        {_tokens->emission_color, OPENUSD_SILK_MATERIAL_EMISSIVE_COLOR, 3},
        {_tokens->metalness, OPENUSD_SILK_MATERIAL_METALLIC, 1},
        {_tokens->specular_roughness, OPENUSD_SILK_MATERIAL_ROUGHNESS, 1},
        {_tokens->roughness, OPENUSD_SILK_MATERIAL_ROUGHNESS, 1},
        {_tokens->normal, OPENUSD_SILK_MATERIAL_NORMAL, 3}};
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

const HdMaterialRelationship* _FindInputConnection(
    const HdMaterialNetwork& network,
    const SdfPath& nodePath,
    const TfToken& inputName)
{
    for (const HdMaterialRelationship& relationship : network.relationships)
    {
        if (relationship.outputId == nodePath &&
            relationship.outputName == inputName)
        {
            return &relationship;
        }
    }
    return nullptr;
}

const HdMaterialNode* _FindInputNode(
    const HdMaterialNetwork& network,
    const SdfPath& nodePath,
    const TfToken& inputName)
{
    const HdMaterialRelationship* relationship =
        _FindInputConnection(network, nodePath, inputName);
    return relationship == nullptr
        ? nullptr
        : _FindNode(network, relationship->inputId);
}

bool _IsMaterialXImage(const HdMaterialNode& node)
{
    return node.identifier.GetString().rfind("ND_image_", 0) == 0;
}

/// Resolves the connected output port of a texture node into the bounded wire
/// channel enum. The token is authored data, not a guess: UsdUVTexture declares
/// exactly the r/g/b/a/rgb outputs, and a MaterialX image node declares exactly
/// one "out" port whose width is the input's width. Returns false, leaving
/// *channel untouched, for any token this delegate does not model or any token
/// that cannot drive an input of this width, so the caller can reject the entry
/// with a diagnostic instead of publishing a channel nobody authored.
bool _TryResolveOutputChannel(
    const HdMaterialNode& texture,
    const TfToken& outputName,
    uint32_t componentCount,
    uint32_t* channel)
{
    if (_IsMaterialXImage(texture))
    {
        if (outputName != _tokens->out)
        {
            return false;
        }
        // A MaterialX image node has a single output whose type already matches
        // the input it feeds. A colour or vector image occupies rgb; a float
        // image decodes into the red channel.
        if (componentCount >= 3)
        {
            *channel = OPENUSD_SILK_TEXTURE_CHANNEL_RGB;
            return true;
        }
        if (componentCount == 1)
        {
            *channel = OPENUSD_SILK_TEXTURE_CHANNEL_R;
            return true;
        }
        return false;
    }

    if (outputName == _tokens->rgb)
    {
        if (componentCount < 3)
        {
            return false;
        }
        *channel = OPENUSD_SILK_TEXTURE_CHANNEL_RGB;
        return true;
    }
    if (componentCount != 1)
    {
        return false;
    }
    if (outputName == _tokens->r)
    {
        *channel = OPENUSD_SILK_TEXTURE_CHANNEL_R;
        return true;
    }
    if (outputName == _tokens->g)
    {
        *channel = OPENUSD_SILK_TEXTURE_CHANNEL_G;
        return true;
    }
    if (outputName == _tokens->b)
    {
        *channel = OPENUSD_SILK_TEXTURE_CHANNEL_B;
        return true;
    }
    if (outputName == _tokens->a)
    {
        *channel = OPENUSD_SILK_TEXTURE_CHANNEL_A;
        return true;
    }
    return false;
}

bool _IdentifierHasPrefix(const HdMaterialNode& node, const char* prefix)
{
    return node.identifier.GetString().rfind(prefix, 0) == 0;
}

void _BroadcastFloats(float (&values)[4], uint32_t count, uint32_t componentCount)
{
    if (count == 1)
    {
        for (uint32_t index = 1; index < componentCount; ++index)
        {
            values[index] = values[0];
        }
    }
}

/// Finds the surface terminal. The terminal is the node no other node consumes,
/// which for a UsdPreviewSurface network is the last node.
const HdMaterialNode* _FindSurface(const HdMaterialNetwork& network)
{
    for (auto node = network.nodes.rbegin(); node != network.nodes.rend(); ++node)
    {
        if (node->identifier == _tokens->UsdPreviewSurface ||
            node->identifier == _tokens->MtlxStandardSurface ||
            node->identifier == _tokens->MtlxSurfaceUnlit)
        {
            return &(*node);
        }
    }
    return nullptr;
}

/// Reads the UV primvar a coordinate node names. UsdPrimvarReader_float2 authors
/// varname; MaterialX geompropvalue authors geomprop. Returns an empty string
/// when the node names neither, so the caller can keep looking rather than
/// publish a primvar nobody authored.
std::string _ReadPrimvarName(const HdMaterialNode& reader)
{
    const auto varname = reader.parameters.find(_tokens->varname);
    if (varname != reader.parameters.end() &&
        varname->second.IsHolding<TfToken>())
    {
        return varname->second.UncheckedGet<TfToken>().GetString();
    }
    if (varname != reader.parameters.end() &&
        varname->second.IsHolding<std::string>())
    {
        return varname->second.UncheckedGet<std::string>();
    }
    const auto geomprop = reader.parameters.find(_tokens->geomprop);
    if (geomprop != reader.parameters.end() &&
        geomprop->second.IsHolding<std::string>())
    {
        return geomprop->second.UncheckedGet<std::string>();
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

bool _ReadParameterFloats(
    const HdMaterialNode& node,
    const TfToken& name,
    uint32_t componentCount,
    float (&out)[4])
{
    const auto entry = node.parameters.find(name);
    if (entry == node.parameters.end())
    {
        return false;
    }
    const uint32_t count = _ReadFloats(entry->second, out);
    if (count == 0)
    {
        return false;
    }
    _BroadcastFloats(out, count, componentCount);
    return true;
}

/// The identity affine, stored row-major as (m00, m01, m10, m11, tx, ty).
const float _identityUvTransform[6] = {1.0f, 0.0f, 0.0f, 1.0f, 0.0f, 0.0f};

/// How a texture reads its coordinates: which primvar supplies them and the
/// constant affine applied to them. `supported` is false when the graph does
/// state a UV transform but one this projection cannot fold, which the caller
/// must report rather than silently sample with untransformed coordinates.
struct _UvBinding
{
    std::string primvar = "st";
    float transform[6] = {1.0f, 0.0f, 0.0f, 1.0f, 0.0f, 0.0f};
    bool supported = true;
    std::string unsupportedReason;
};

bool _UvTransformsEqual(const float (&left)[6], const float (&right)[6])
{
    for (size_t index = 0; index < 6; ++index)
    {
        if (left[index] != right[index])
        {
            return false;
        }
    }
    return true;
}

bool _IsIdentityUvTransform(const float (&transform)[6])
{
    return _UvTransformsEqual(transform, _identityUvTransform);
}

/// Reads one constant place2d input. A connected input is rejected rather than
/// read from the node's authored fallback, because that fallback is not what the
/// graph asks to be rendered.
bool _ReadPlace2dInput(
    const HdMaterialNetwork& network,
    const HdMaterialNode& node,
    const TfToken& name,
    uint32_t componentCount,
    const float (&fallback)[4],
    float (&out)[4])
{
    if (_FindInputConnection(network, node.path, name) != nullptr)
    {
        return false;
    }
    if (!_ReadParameterFloats(node, name, componentCount, out))
    {
        for (uint32_t index = 0; index < 4; ++index)
        {
            out[index] = fallback[index];
        }
    }
    return true;
}

/// Folds a MaterialX place2d node with constant inputs into one affine.
///
/// This reproduces NG_place2d_vector2 exactly rather than approximating it: SRT
/// is (-pivot, divide by scale, rotate, -offset, +pivot) and TRS is (-pivot,
/// -offset, rotate, divide by scale, +pivot), with rotate2d's clockwise matrix
/// for a positive angle in degrees. Returns false, with a reason, for anything
/// outside the projection so the caller can diagnose it.
bool _TryFoldPlace2d(
    const HdMaterialNetwork& network,
    const HdMaterialNode& node,
    float (&out)[6],
    std::string* reason)
{
    const float zero[4] = {0.0f, 0.0f, 0.0f, 0.0f};
    const float one[4] = {1.0f, 1.0f, 1.0f, 1.0f};
    float pivot[4] = {0.0f, 0.0f, 0.0f, 0.0f};
    float scale[4] = {1.0f, 1.0f, 0.0f, 0.0f};
    float rotate[4] = {0.0f, 0.0f, 0.0f, 0.0f};
    float offset[4] = {0.0f, 0.0f, 0.0f, 0.0f};
    float order[4] = {0.0f, 0.0f, 0.0f, 0.0f};
    if (!_ReadPlace2dInput(network, node, _tokens->pivot, 2, zero, pivot) ||
        !_ReadPlace2dInput(network, node, _tokens->scale, 2, one, scale) ||
        !_ReadPlace2dInput(network, node, _tokens->rotate, 1, zero, rotate) ||
        !_ReadPlace2dInput(network, node, _tokens->offset, 2, zero, offset) ||
        !_ReadPlace2dInput(network, node, _tokens->operationorder, 1, zero, order))
    {
        *reason = "one of its inputs is connected rather than constant";
        return false;
    }
    if (scale[0] == 0.0f || scale[1] == 0.0f)
    {
        *reason = "its scale has a zero component";
        return false;
    }
    if (order[0] != 0.0f && order[0] != 1.0f)
    {
        *reason = "its operationorder is neither SRT nor TRS";
        return false;
    }

    const float radians = rotate[0] * 0.01745329251994329577f;
    const float sine = std::sin(radians);
    const float cosine = std::cos(radians);
    // rotate2d: (ca*x + sa*y, -sa*x + ca*y).
    const float rotation[4] = {cosine, sine, -sine, cosine};
    const float inverseScale[2] = {1.0f / scale[0], 1.0f / scale[1]};

    float linear[4];
    float pre[2];
    if (order[0] == 0.0f)
    {
        // SRT: rotation * diag(1/scale), then -offset in rotated space.
        linear[0] = rotation[0] * inverseScale[0];
        linear[1] = rotation[1] * inverseScale[1];
        linear[2] = rotation[2] * inverseScale[0];
        linear[3] = rotation[3] * inverseScale[1];
        pre[0] = pivot[0];
        pre[1] = pivot[1];
    }
    else
    {
        // TRS: diag(1/scale) * rotation, with the offset removed before rotating.
        linear[0] = inverseScale[0] * rotation[0];
        linear[1] = inverseScale[0] * rotation[1];
        linear[2] = inverseScale[1] * rotation[2];
        linear[3] = inverseScale[1] * rotation[3];
        pre[0] = pivot[0] + offset[0];
        pre[1] = pivot[1] + offset[1];
    }

    out[0] = linear[0];
    out[1] = linear[1];
    out[2] = linear[2];
    out[3] = linear[3];
    out[4] = pivot[0] - ((linear[0] * pre[0]) + (linear[1] * pre[1]));
    out[5] = pivot[1] - ((linear[2] * pre[0]) + (linear[3] * pre[1]));
    if (order[0] == 0.0f)
    {
        out[4] -= offset[0];
        out[5] -= offset[1];
    }
    return true;
}

/// Resolves how one texture reads its coordinates, following the texcoord/st
/// connection through an optional MaterialX place2d node to the coordinate node
/// that names the primvar.
_UvBinding _ResolveUvBinding(
    const HdMaterialNetwork& network,
    const SdfPath& texturePath)
{
    _UvBinding binding;
    for (const HdMaterialRelationship& relationship : network.relationships)
    {
        if (relationship.outputId != texturePath ||
            (relationship.outputName != _tokens->st &&
                relationship.outputName != _tokens->texcoord))
        {
            continue;
        }
        const HdMaterialNode* reader = _FindNode(network, relationship.inputId);
        if (reader == nullptr)
        {
            continue;
        }
        if (reader->identifier == _tokens->MtlxPlace2d)
        {
            if (!_TryFoldPlace2d(
                    network, *reader, binding.transform, &binding.unsupportedReason))
            {
                binding.supported = false;
                return binding;
            }
            const HdMaterialNode* coordinate =
                _FindInputNode(network, reader->path, _tokens->texcoord);
            if (coordinate != nullptr)
            {
                const std::string name = _ReadPrimvarName(*coordinate);
                if (!name.empty())
                {
                    binding.primvar = name;
                }
            }
            return binding;
        }
        const std::string name = _ReadPrimvarName(*reader);
        if (!name.empty())
        {
            binding.primvar = name;
            return binding;
        }
    }
    return binding;
}

bool _EvaluateMaterialXConstant(
    const HdMaterialNetwork& network,
    const HdMaterialNode& node,
    uint32_t componentCount,
    float (&out)[4],
    uint32_t depth);

bool _EvaluateMaterialXInput(
    const HdMaterialNetwork& network,
    const HdMaterialNode& node,
    const TfToken& inputName,
    uint32_t componentCount,
    const float (&fallback)[4],
    float (&out)[4],
    uint32_t depth)
{
    const HdMaterialNode* upstream =
        _FindInputNode(network, node.path, inputName);
    if (upstream != nullptr)
    {
        return _EvaluateMaterialXConstant(
            network, *upstream, componentCount, out, depth + 1);
    }
    if (_ReadParameterFloats(node, inputName, componentCount, out))
    {
        return true;
    }
    for (uint32_t index = 0; index < componentCount; ++index)
    {
        out[index] = fallback[index];
    }
    return true;
}

bool _EvaluateMaterialXConstant(
    const HdMaterialNetwork& network,
    const HdMaterialNode& node,
    uint32_t componentCount,
    float (&out)[4],
    uint32_t depth)
{
    if (depth > 8 || _IsMaterialXImage(node) ||
        node.identifier == _tokens->MtlxNormalMap)
    {
        return false;
    }
    if (_ReadParameterFloats(node, _tokens->in, componentCount, out))
    {
        return true;
    }

    float zero[4] = {0.0f, 0.0f, 0.0f, 0.0f};
    float one[4] = {1.0f, 1.0f, 1.0f, 1.0f};
    float a[4] = {0.0f, 0.0f, 0.0f, 0.0f};
    float b[4] = {0.0f, 0.0f, 0.0f, 0.0f};
    const bool multiply = _IdentifierHasPrefix(node, "ND_multiply_");
    const bool add = _IdentifierHasPrefix(node, "ND_add_");
    const bool subtract = _IdentifierHasPrefix(node, "ND_subtract_");
    if (multiply || add || subtract)
    {
        if (!_EvaluateMaterialXInput(
                network, node, _tokens->in1, componentCount, zero, a, depth) ||
            !_EvaluateMaterialXInput(
                network, node, _tokens->in2, componentCount, multiply ? one : zero, b, depth))
        {
            return false;
        }
        for (uint32_t index = 0; index < componentCount; ++index)
        {
            out[index] = multiply ? a[index] * b[index]
                : add       ? a[index] + b[index]
                            : a[index] - b[index];
        }
        return true;
    }
    if (_IdentifierHasPrefix(node, "ND_clamp_"))
    {
        if (!_EvaluateMaterialXInput(
                network, node, _tokens->in, componentCount, zero, a, depth) ||
            !_EvaluateMaterialXInput(
                network, node, _tokens->low, componentCount, zero, b, depth))
        {
            return false;
        }
        float high[4] = {1.0f, 1.0f, 1.0f, 1.0f};
        if (!_EvaluateMaterialXInput(
                network, node, _tokens->high, componentCount, one, high, depth))
        {
            return false;
        }
        for (uint32_t index = 0; index < componentCount; ++index)
        {
            out[index] = std::clamp(a[index], b[index], high[index]);
        }
        return true;
    }
    if (_IdentifierHasPrefix(node, "ND_mix_"))
    {
        float mix[4] = {0.5f, 0.5f, 0.5f, 0.5f};
        if (!_EvaluateMaterialXInput(
                network, node, _tokens->bg, componentCount, zero, a, depth) ||
            !_EvaluateMaterialXInput(
                network, node, _tokens->fg, componentCount, zero, b, depth) ||
            !_EvaluateMaterialXInput(
                network, node, _tokens->mix, componentCount, mix, mix, depth))
        {
            return false;
        }
        for (uint32_t index = 0; index < componentCount; ++index)
        {
            out[index] = (a[index] * (1.0f - mix[index])) + (b[index] * mix[index]);
        }
        return true;
    }
    return false;
}

bool _TryCreateTextureEntry(
    const HdMaterialNetwork& network,
    const HdMaterialNode& texture,
    const TfToken& outputName,
    const _InputBinding& input,
    HdSilkMaterialTexture& entry)
{
    if (!_IsMaterialXImage(texture) && texture.identifier != _tokens->UsdUVTexture)
    {
        return false;
    }
    const std::string asset = _ResolveAssetPath(texture.parameters);
    if (asset.empty())
    {
        return false;
    }
    uint32_t channel = OPENUSD_SILK_TEXTURE_CHANNEL_RGB;
    if (!_TryResolveOutputChannel(texture, outputName, input.componentCount, &channel))
    {
        TF_WARN(
            "hdSilk material input '%s' is connected to texture output '%s', which "
            "cannot drive a %u-component input; leaving the input at its default.",
            input.name.GetText(),
            outputName.GetText(),
            input.componentCount);
        return false;
    }
    entry.parameter = input.parameter;
    entry.componentCount = input.componentCount;
    entry.outputChannel = channel;
    entry.wrapS = _ReadWrap(texture.parameters, _tokens->wrapS);
    entry.wrapT = _ReadWrap(texture.parameters, _tokens->wrapT);
    entry.sourceColorSpace = _ReadColorSpace(texture.parameters);
    entry.scale[0] = entry.scale[1] = entry.scale[2] = entry.scale[3] = 1.0f;
    _ReadVector4(texture.parameters, _tokens->scale, entry.scale);
    _ReadVector4(texture.parameters, _tokens->bias, entry.bias);
    _ReadVector4(texture.parameters, _tokens->fallback, entry.fallback);
    entry.asset = asset;
    const _UvBinding uv = _ResolveUvBinding(network, texture.path);
    if (!uv.supported)
    {
        TF_WARN(
            "hdSilk material input '%s' reads a MaterialX place2d UV transform "
            "this projection cannot fold because %s; leaving the input at its "
            "default.",
            input.name.GetText(),
            uv.unsupportedReason.c_str());
        return false;
    }
    entry.uvPrimvar = uv.primvar;
    for (size_t index = 0; index < 6; ++index)
    {
        entry.uvTransform[index] = uv.transform[index];
    }
    return true;
}

/// Settles the one UV transform this material publishes.
///
/// The renderer samples every texture of a material through a single coordinate
/// stream -- one primvar and one transform -- so a material whose images ask for
/// different transforms is outside the projection. The first texture in the
/// fixed input order states the material transform; a later texture that asks
/// for a different one is dropped with a diagnostic rather than sampled with a
/// transform it did not author or silently stripped of the one it did.
void _ReconcileUvTransforms(HdSilkMaterialRecord& record)
{
    if (record.textures.empty())
    {
        return;
    }
    for (size_t index = 0; index < 6; ++index)
    {
        record.uvTransform[index] = record.textures.front().uvTransform[index];
    }
    if (_IsIdentityUvTransform(record.uvTransform))
    {
        // Nothing to reconcile against unless some texture asks for a transform.
        bool anyTransform = false;
        for (const HdSilkMaterialTexture& texture : record.textures)
        {
            anyTransform = anyTransform || !_IsIdentityUvTransform(texture.uvTransform);
        }
        if (!anyTransform)
        {
            return;
        }
    }

    std::vector<HdSilkMaterialTexture> kept;
    kept.reserve(record.textures.size());
    for (HdSilkMaterialTexture& texture : record.textures)
    {
        if (_UvTransformsEqual(texture.uvTransform, record.uvTransform))
        {
            kept.push_back(std::move(texture));
            continue;
        }
        TF_WARN(
            "hdSilk material '%s' texture for parameter %u asks for a different "
            "MaterialX UV transform than the material's first texture; hdSilk "
            "carries one UV transform per material, so this input is left at its "
            "default.",
            record.path.c_str(),
            texture.parameter);
    }
    record.textures = std::move(kept);
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
        TF_WARN(
            "hdSilk material '%s' has no supported surface terminal; expected "
            "UsdPreviewSurface or a supported MaterialX surface shader.",
            record.path.c_str());
        return record;
    }
    record.surfaceKind = OPENUSD_SILK_SURFACE_PREVIEW_SURFACE;

    if (surface->identifier == _tokens->MtlxStandardSurface)
    {
        record.surfaceKind = OPENUSD_SILK_SURFACE_MATERIALX_PROJECTED;
        for (const _InputBinding& input : _MaterialXStandardSurfaceInputs())
        {
            const HdMaterialRelationship* connection =
                _FindInputConnection(network, surface->path, input.name);
            const HdMaterialNode* upstream = connection == nullptr
                ? nullptr
                : _FindNode(network, connection->inputId);
            if (upstream != nullptr)
            {
                const HdMaterialNode* texture = upstream;
                TfToken outputName = connection->inputName;
                if (upstream->identifier == _tokens->MtlxNormalMap)
                {
                    const HdMaterialRelationship* normalConnection =
                        _FindInputConnection(network, upstream->path, _tokens->in);
                    texture = normalConnection == nullptr
                        ? nullptr
                        : _FindNode(network, normalConnection->inputId);
                    if (normalConnection != nullptr)
                    {
                        outputName = normalConnection->inputName;
                    }
                }
                HdSilkMaterialTexture textureEntry;
                if (texture != nullptr &&
                    _TryCreateTextureEntry(
                        network, *texture, outputName, input, textureEntry))
                {
                    record.textures.push_back(std::move(textureEntry));
                    continue;
                }

                float values[4] = {0.0f, 0.0f, 0.0f, 0.0f};
                if (_EvaluateMaterialXConstant(
                        network, *upstream, input.componentCount, values, 0))
                {
                    HdSilkMaterialScalar scalar;
                    scalar.parameter = input.parameter;
                    scalar.componentCount = input.componentCount;
                    for (uint32_t index = 0; index < scalar.componentCount; ++index)
                    {
                        scalar.value[index] = values[index];
                    }
                    record.scalars.push_back(scalar);
                    continue;
                }

                TF_WARN(
                    "hdSilk material '%s' MaterialX input '%s' is connected to "
                    "unsupported node '%s'; leaving the input at its default.",
                    record.path.c_str(),
                    input.name.GetText(),
                    upstream->identifier.GetText());
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
                    "hdSilk material '%s' MaterialX input '%s' holds an "
                    "unsupported value type; leaving the input at its default.",
                    record.path.c_str(),
                    input.name.GetText());
                continue;
            }
            _BroadcastFloats(values, count, input.componentCount);
            HdSilkMaterialScalar scalar;
            scalar.parameter = input.parameter;
            scalar.componentCount = input.componentCount;
            for (uint32_t index = 0; index < scalar.componentCount; ++index)
            {
                scalar.value[index] = values[index];
            }
            record.scalars.push_back(scalar);
        }
        _ReconcileUvTransforms(record);
        return record;
    }

    if (surface->identifier == _tokens->MtlxSurfaceUnlit)
    {
        const HdSilkMaterialXVulkanShader shader =
            HdSilkGenerateMaterialXVulkanFragment(id, networkMap);
        if (!shader.success)
        {
            TF_WARN(
                "hdSilk material '%s' MaterialX Vulkan generation failed: %s",
                record.path.c_str(),
                shader.error.c_str());
            record.surfaceKind = OPENUSD_SILK_SURFACE_UNSUPPORTED;
            return record;
        }
        record.surfaceKind = OPENUSD_SILK_SURFACE_MATERIALX_GENERATED;
        record.generatedFragmentSpirv = shader.fragmentSpirv;
        record.generatedFragmentMslSource = shader.fragmentMslSource;
        return record;
    }

    for (const _InputBinding& input : _PreviewSurfaceInputs())
    {
        // A connected input is described by the texture table; its authored
        // constant, if any, is only a fallback the texture entry already
        // carries, so it must not also appear as a scalar.
        const HdMaterialNode* texture = nullptr;
        TfToken outputName;
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
                // The output port the surface input is wired to is the only
                // authored statement of which channel of the file drives it.
                outputName = relationship.inputName;
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
            uint32_t outputChannel = OPENUSD_SILK_TEXTURE_CHANNEL_RGB;
            if (!_TryResolveOutputChannel(
                    *texture, outputName, input.componentCount, &outputChannel))
            {
                TF_WARN(
                    "hdSilk material '%s' input '%s' is connected to UsdUVTexture "
                    "output '%s', which is not one of r, g, b, a, rgb or cannot "
                    "drive a %u-component input; leaving the input at its default.",
                    record.path.c_str(),
                    input.name.GetText(),
                    outputName.GetText(),
                    input.componentCount);
                continue;
            }
            HdSilkMaterialTexture entry;
            entry.parameter = input.parameter;
            entry.componentCount = input.componentCount;
            entry.outputChannel = outputChannel;
            entry.wrapS = _ReadWrap(texture->parameters, _tokens->wrapS);
            entry.wrapT = _ReadWrap(texture->parameters, _tokens->wrapT);
            entry.sourceColorSpace = _ReadColorSpace(texture->parameters);
            entry.scale[0] = entry.scale[1] = entry.scale[2] = entry.scale[3] = 1.0f;
            _ReadVector4(texture->parameters, _tokens->scale, entry.scale);
            _ReadVector4(texture->parameters, _tokens->bias, entry.bias);
            _ReadVector4(texture->parameters, _tokens->fallback, entry.fallback);
            entry.asset = asset;
            const _UvBinding uv = _ResolveUvBinding(network, texture->path);
            if (!uv.supported)
            {
                TF_WARN(
                    "hdSilk material '%s' input '%s' reads a MaterialX place2d UV "
                    "transform this projection cannot fold because %s; leaving the "
                    "input at its default.",
                    record.path.c_str(),
                    input.name.GetText(),
                    uv.unsupportedReason.c_str());
                continue;
            }
            entry.uvPrimvar = uv.primvar;
            for (size_t index = 0; index < 6; ++index)
            {
                entry.uvTransform[index] = uv.transform[index];
            }
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

    _ReconcileUvTransforms(record);
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
