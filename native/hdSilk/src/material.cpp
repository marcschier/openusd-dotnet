// Copyright (c) marcschier. Licensed under the MIT License.

#include "material.h"

#include "materialXBridge.h"
#include "mdlAdapter.h"
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
#include <atomic>
#include <cmath>
#include <cstddef>
#include <cstring>
#include <map>
#include <string>
#include <utility>
#include <vector>

PXR_NAMESPACE_OPEN_SCOPE

namespace {

// Counts ND_surface_unlit generation failures. The surface kind no longer
// records one -- a failure keeps OPENUSD_SILK_SURFACE_MATERIALX_GENERATED with an
// empty payload, because the material is unlit whether or not a fragment was
// produced -- so the failure needs somewhere of its own to be observed from. It
// is read by the native probe, which is the only place that can drive a real
// generation failure through the real code path.
std::atomic<uint64_t> _generatedSurfaceFailureCount{0};

// Forces the next generation to fail, so a probe can drive the real failure path
// in a build whose MaterialX generation works.
std::atomic<bool> _forceGeneratedSurfaceFailure{false};

}  // namespace

// clang-format off
// The double-parenthesis tuple form is required: with a bare (name) element the
// TF_PP_IS_TUPLE expansion leaves a variadic macro argument empty, which Clang
// rejects under -Werror,-Wvariadic-macro-arguments-omitted on the macOS build.
TF_DEFINE_PRIVATE_TOKENS(
    _tokens,
    ((UsdPreviewSurface, "UsdPreviewSurface"))
    ((UsdUVTexture, "UsdUVTexture"))
    ((MtlxStandardSurface, "ND_standard_surface_surfaceshader"))
    ((MtlxOpenPbrSurface, "ND_open_pbr_surface_surfaceshader"))
    ((MtlxSurfaceUnlit, "ND_surface_unlit"))
    ((MtlxNormalMap, "ND_normalmap"))
    ((MtlxPlace2d, "ND_place2d_vector2"))
    ((MtlxGeomPropValue2, "ND_geompropvalue_vector2"))
    ((MtlxTexCoord2, "ND_texcoord_vector2"))
    ((UsdTransform2d, "UsdTransform2d"))
    ((UsdPrimvarReaderFloat2, "UsdPrimvarReader_float2"))
    ((diffuseColor, "diffuseColor"))
    ((base_color, "base_color"))
    ((base, "base"))
    ((base_weight, "base_weight"))
    ((base_metalness, "base_metalness"))
    ((base_diffuse_roughness, "base_diffuse_roughness"))
    ((diffuse_roughness, "diffuse_roughness"))
    ((emissiveColor, "emissiveColor"))
    ((emission_color, "emission_color"))
    ((emission, "emission"))
    ((emission_luminance, "emission_luminance"))
    ((specularColor, "specularColor"))
    ((specular, "specular"))
    ((specular_weight, "specular_weight"))
    ((specular_color, "specular_color"))
    ((specular_IOR, "specular_IOR"))
    ((specular_ior, "specular_ior"))
    ((specular_anisotropy, "specular_anisotropy"))
    ((specular_rotation, "specular_rotation"))
    ((specular_roughness_anisotropy, "specular_roughness_anisotropy"))
    ((transmission, "transmission"))
    ((transmission_weight, "transmission_weight"))
    ((subsurface, "subsurface"))
    ((subsurface_weight, "subsurface_weight"))
    ((sheen, "sheen"))
    ((fuzz_weight, "fuzz_weight"))
    ((metallic, "metallic"))
    ((metalness, "metalness"))
    ((roughness, "roughness"))
    ((specular_roughness, "specular_roughness"))
    ((clearcoat, "clearcoat"))
    ((clearcoatRoughness, "clearcoatRoughness"))
    ((coat, "coat"))
    ((coat_weight, "coat_weight"))
    ((coat_roughness, "coat_roughness"))
    ((coat_color, "coat_color"))
    ((coat_IOR, "coat_IOR"))
    ((coat_ior, "coat_ior"))
    ((coat_normal, "coat_normal"))
    ((coat_anisotropy, "coat_anisotropy"))
    ((coat_roughness_anisotropy, "coat_roughness_anisotropy"))
    ((coat_darkening, "coat_darkening"))
    ((coat_affect_color, "coat_affect_color"))
    ((coat_affect_roughness, "coat_affect_roughness"))
    ((thin_film_thickness, "thin_film_thickness"))
    ((thin_film_weight, "thin_film_weight"))
    ((thin_walled, "thin_walled"))
    ((tangent, "tangent"))
    ((geometry_opacity, "geometry_opacity"))
    ((geometry_normal, "geometry_normal"))
    ((geometry_thin_walled, "geometry_thin_walled"))
    ((geometry_coat_normal, "geometry_coat_normal"))
    ((geometry_tangent, "geometry_tangent"))
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
    ((rotation, "rotation"))
    ((translation, "translation"))
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
    ((value, "value"))
    ((index, "index"))
    ((wrapS, "wrapS"))
    ((wrapT, "wrapT"))
    ((uaddressmode, "uaddressmode"))
    ((vaddressmode, "vaddressmode"))
    ((defaultValue, "default"))
    ((scale, "scale"))
    ((bias, "bias"))
    ((fallback, "fallback"))
    ((sourceColorSpace, "sourceColorSpace"))
    ((clamp, "clamp"))
    ((repeat, "repeat"))
    ((mirror, "mirror"))
    ((useMetadata, "useMetadata"))
    ((constant, "constant"))
    ((periodic, "periodic"))
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
        {_tokens->occlusion, OPENUSD_SILK_MATERIAL_OCCLUSION, 1},
        {_tokens->useSpecularWorkflow,
            OPENUSD_SILK_MATERIAL_USE_SPECULAR_WORKFLOW, 1}};
    return inputs;
}

/// The binding of the displacement input.
///
/// Deliberately *not* a member of `_PreviewSurfaceInputs()`. Displacement is a
/// separate material terminal in USD: the material's `outputs:displacement` is
/// connected to a shader's `outputs:displacement`, and Hydra publishes that as
/// its own entry in the network map. Reading `inputs:displacement` off whatever
/// node happens to terminate the *surface* network would displace a material
/// whose author never connected a displacement output at all, and would read a
/// stale authored value Hydra leaves behind a connection. See
/// `_ResolveDisplacementTerminal`.
const _InputBinding&
_DisplacementInput()
{
    static const _InputBinding binding{
        _tokens->displacement, OPENUSD_SILK_MATERIAL_DISPLACEMENT, 1};
    return binding;
}

/// One MaterialX surface-shader input projected onto a wire parameter.
///
/// `weight` names the nodedef input that scales this one -- MaterialX states
/// the scaling as a multiply inside the shader's own implementation graph, so
/// applying it here is a transport of the nodedef, not an interpretation. Both
/// `standard_surface` and `open_pbr_surface` default their emission weight to
/// zero, which is why an emission colour cannot be projected on its own: the
/// authored colour of a material that emits nothing would otherwise light up.
///
/// `monochromeColor` marks an input the nodedef types as `color3` while the
/// wire carries a single float. The projection accepts it only from a constant
/// whose three channels agree, because no per-texel scale can turn a
/// three-channel image into the one channel the renderer binds.
struct _ProjectedInput
{
    _InputBinding binding;
    const TfToken* weight;
    float weightDefault;
    bool monochromeColor;
};

/// One MaterialX input this projection deliberately does not carry, with the
/// nodedef default that says whether the author asked for it. Reporting is
/// limited to inputs the author moved off that default, or connected, so a
/// material that leaves a lobe alone stays silent.
///
/// `componentCount` of zero marks an input whose nodedef default is a geometric
/// stream rather than a value, so only a connection is reportable.
struct _UnsupportedInput
{
    const TfToken& name;
    uint32_t componentCount;
    float defaultValue[4];
    const char* reason;
};

/// The complete projection of one MaterialX surface shader.
struct _SurfaceProjection
{
    const TfToken& identifier;
    const std::vector<_ProjectedInput>& inputs;
    const std::vector<_UnsupportedInput>& unsupported;
};

const std::vector<_ProjectedInput>&
_MaterialXStandardSurfaceInputs()
{
    static const std::vector<_ProjectedInput> inputs = {
        {{_tokens->base_color, OPENUSD_SILK_MATERIAL_DIFFUSE_COLOR, 3},
            &_tokens->base, 1.0f, false},
        {{_tokens->emission_color, OPENUSD_SILK_MATERIAL_EMISSIVE_COLOR, 3},
            &_tokens->emission, 0.0f, false},
        {{_tokens->metalness, OPENUSD_SILK_MATERIAL_METALLIC, 1},
            nullptr, 1.0f, false},
        {{_tokens->specular_roughness, OPENUSD_SILK_MATERIAL_ROUGHNESS, 1},
            nullptr, 1.0f, false},
        {{_tokens->roughness, OPENUSD_SILK_MATERIAL_ROUGHNESS, 1},
            nullptr, 1.0f, false},
        {{_tokens->specular_IOR, OPENUSD_SILK_MATERIAL_IOR, 1},
            nullptr, 1.0f, false},
        {{_tokens->coat, OPENUSD_SILK_MATERIAL_CLEARCOAT, 1},
            nullptr, 1.0f, false},
        {{_tokens->coat_roughness,
             OPENUSD_SILK_MATERIAL_CLEARCOAT_ROUGHNESS, 1},
            nullptr, 1.0f, false},
        {{_tokens->opacity, OPENUSD_SILK_MATERIAL_OPACITY, 1},
            nullptr, 1.0f, true},
        {{_tokens->normal, OPENUSD_SILK_MATERIAL_NORMAL, 3},
            nullptr, 1.0f, false}};
    return inputs;
}

const std::vector<_UnsupportedInput>&
_MaterialXStandardSurfaceExclusions()
{
    static const std::vector<_UnsupportedInput> exclusions = {
        {_tokens->specular, 1, {1.0f, 0.0f, 0.0f, 0.0f},
            "hdSilk has no specular weight; the renderer derives its dielectric "
            "reflectance from the index of refraction alone"},
        {_tokens->specular_color, 3, {1.0f, 1.0f, 1.0f, 0.0f},
            "MaterialX specular_color is an edge tint layered over the base, not "
            "the normal-incidence reflectance UsdPreviewSurface's specularColor "
            "carries, so the two are not the same quantity"},
        {_tokens->specular_anisotropy, 1, {0.0f, 0.0f, 0.0f, 0.0f},
            "hdSilk evaluates an isotropic specular lobe"},
        {_tokens->specular_rotation, 1, {0.0f, 0.0f, 0.0f, 0.0f},
            "hdSilk evaluates an isotropic specular lobe"},
        {_tokens->diffuse_roughness, 1, {0.0f, 0.0f, 0.0f, 0.0f},
            "hdSilk evaluates a Lambertian diffuse lobe"},
        {_tokens->transmission, 1, {0.0f, 0.0f, 0.0f, 0.0f},
            "hdSilk has no transmission lobe, and opacity is a different "
            "quantity that would render a different picture"},
        {_tokens->subsurface, 1, {0.0f, 0.0f, 0.0f, 0.0f},
            "hdSilk has no subsurface lobe"},
        {_tokens->sheen, 1, {0.0f, 0.0f, 0.0f, 0.0f},
            "hdSilk has no sheen lobe"},
        {_tokens->coat_color, 3, {1.0f, 1.0f, 1.0f, 0.0f},
            "hdSilk's clearcoat is an untinted layer"},
        {_tokens->coat_IOR, 1, {1.5f, 0.0f, 0.0f, 0.0f},
            "hdSilk's clearcoat has a fixed index of refraction, and the wire's "
            "single ior parameter already carries the base dielectric"},
        {_tokens->coat_normal, 0, {0.0f, 0.0f, 0.0f, 0.0f},
            "hdSilk shades the clearcoat with the surface normal"},
        {_tokens->coat_anisotropy, 1, {0.0f, 0.0f, 0.0f, 0.0f},
            "hdSilk evaluates an isotropic clearcoat lobe"},
        {_tokens->coat_affect_color, 1, {0.0f, 0.0f, 0.0f, 0.0f},
            "hdSilk's clearcoat does not modulate the base"},
        {_tokens->coat_affect_roughness, 1, {0.0f, 0.0f, 0.0f, 0.0f},
            "hdSilk's clearcoat does not modulate the base"},
        {_tokens->thin_film_thickness, 1, {0.0f, 0.0f, 0.0f, 0.0f},
            "hdSilk has no thin-film interference term"},
        {_tokens->thin_walled, 1, {0.0f, 0.0f, 0.0f, 0.0f},
            "hdSilk has no thin-walled shading mode"},
        {_tokens->tangent, 0, {0.0f, 0.0f, 0.0f, 0.0f},
            "hdSilk evaluates an isotropic specular lobe, so no tangent frame "
            "reaches the shader"}};
    return exclusions;
}

const std::vector<_ProjectedInput>&
_MaterialXOpenPbrSurfaceInputs()
{
    static const std::vector<_ProjectedInput> inputs = {
        {{_tokens->base_color, OPENUSD_SILK_MATERIAL_DIFFUSE_COLOR, 3},
            &_tokens->base_weight, 1.0f, false},
        {{_tokens->emission_color, OPENUSD_SILK_MATERIAL_EMISSIVE_COLOR, 3},
            &_tokens->emission_luminance, 0.0f, false},
        {{_tokens->base_metalness, OPENUSD_SILK_MATERIAL_METALLIC, 1},
            nullptr, 1.0f, false},
        {{_tokens->specular_roughness, OPENUSD_SILK_MATERIAL_ROUGHNESS, 1},
            nullptr, 1.0f, false},
        {{_tokens->specular_ior, OPENUSD_SILK_MATERIAL_IOR, 1},
            nullptr, 1.0f, false},
        {{_tokens->coat_weight, OPENUSD_SILK_MATERIAL_CLEARCOAT, 1},
            nullptr, 1.0f, false},
        {{_tokens->coat_roughness,
             OPENUSD_SILK_MATERIAL_CLEARCOAT_ROUGHNESS, 1},
            nullptr, 1.0f, false},
        {{_tokens->geometry_opacity, OPENUSD_SILK_MATERIAL_OPACITY, 1},
            nullptr, 1.0f, false},
        {{_tokens->geometry_normal, OPENUSD_SILK_MATERIAL_NORMAL, 3},
            nullptr, 1.0f, false}};
    return inputs;
}

const std::vector<_UnsupportedInput>&
_MaterialXOpenPbrSurfaceExclusions()
{
    static const std::vector<_UnsupportedInput> exclusions = {
        {_tokens->specular_weight, 1, {1.0f, 0.0f, 0.0f, 0.0f},
            "hdSilk has no specular weight; the renderer derives its dielectric "
            "reflectance from the index of refraction alone"},
        {_tokens->specular_color, 3, {1.0f, 1.0f, 1.0f, 0.0f},
            "OpenPBR specular_color is the physical edge tint of the base, not "
            "the normal-incidence reflectance UsdPreviewSurface's specularColor "
            "carries, so the two are not the same quantity"},
        {_tokens->specular_roughness_anisotropy, 1, {0.0f, 0.0f, 0.0f, 0.0f},
            "hdSilk evaluates an isotropic specular lobe"},
        {_tokens->base_diffuse_roughness, 1, {0.0f, 0.0f, 0.0f, 0.0f},
            "hdSilk evaluates a Lambertian diffuse lobe"},
        {_tokens->transmission_weight, 1, {0.0f, 0.0f, 0.0f, 0.0f},
            "hdSilk has no transmission lobe, and opacity is a different "
            "quantity that would render a different picture"},
        {_tokens->subsurface_weight, 1, {0.0f, 0.0f, 0.0f, 0.0f},
            "hdSilk has no subsurface lobe"},
        {_tokens->fuzz_weight, 1, {0.0f, 0.0f, 0.0f, 0.0f},
            "hdSilk has no fuzz lobe"},
        {_tokens->coat_color, 3, {1.0f, 1.0f, 1.0f, 0.0f},
            "hdSilk's clearcoat is an untinted layer"},
        {_tokens->coat_ior, 1, {1.6f, 0.0f, 0.0f, 0.0f},
            "hdSilk's clearcoat has a fixed index of refraction, and the wire's "
            "single ior parameter already carries the base dielectric"},
        {_tokens->coat_roughness_anisotropy, 1, {0.0f, 0.0f, 0.0f, 0.0f},
            "hdSilk evaluates an isotropic clearcoat lobe"},
        {_tokens->coat_darkening, 1, {1.0f, 0.0f, 0.0f, 0.0f},
            "hdSilk's clearcoat does not darken the base"},
        {_tokens->thin_film_weight, 1, {0.0f, 0.0f, 0.0f, 0.0f},
            "hdSilk has no thin-film interference term"},
        {_tokens->geometry_thin_walled, 1, {0.0f, 0.0f, 0.0f, 0.0f},
            "hdSilk has no thin-walled shading mode"},
        {_tokens->geometry_coat_normal, 0, {0.0f, 0.0f, 0.0f, 0.0f},
            "hdSilk shades the clearcoat with the surface normal"},
        {_tokens->geometry_tangent, 0, {0.0f, 0.0f, 0.0f, 0.0f},
            "hdSilk evaluates an isotropic specular lobe, so no tangent frame "
            "reaches the shader"}};
    return exclusions;
}

/// The projection for one surface identifier, or nullptr when the identifier is
/// not a MaterialX surface shader this delegate projects.
const _SurfaceProjection* _FindSurfaceProjection(const TfToken& identifier)
{
    static const std::vector<_SurfaceProjection> projections = {
        {_tokens->MtlxStandardSurface,
            _MaterialXStandardSurfaceInputs(),
            _MaterialXStandardSurfaceExclusions()},
        {_tokens->MtlxOpenPbrSurface,
            _MaterialXOpenPbrSurfaceInputs(),
            _MaterialXOpenPbrSurfaceExclusions()}};
    for (const _SurfaceProjection& projection : projections)
    {
        if (projection.identifier == identifier)
        {
            return &projection;
        }
    }
    return nullptr;
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

/// Reads one UsdUVTexture wrap token onto the wire enum.
///
/// OPENUSD_SILK_WRAP_BLACK is the value for an authored `black` and for any
/// token this delegate does not recognise, which is USD's documented fallback.
/// OPENUSD_SILK_WRAP_USE_METADATA is the value for an authored `useMetadata` and
/// for an unauthored `wrap`, which is the same thing: `useMetadata` is the
/// schema default. The two stay distinct because they record different authored
/// intent. See OPENUSD_SILK_WRAP_BLACK in openusd_hdsilk.h.
uint32_t _ReadWrap(const std::map<TfToken, VtValue>& parameters, const TfToken& name)
{
    const auto entry = parameters.find(name);
    if (entry == parameters.end() || !entry->second.IsHolding<TfToken>())
    {
        // UsdUVTexture's schema default for `wrap` is `useMetadata`, so an
        // unauthored wrap is that value rather than an authored `black`. It is
        // published as such: this delegate reads no image metadata, so a
        // consumer resolves it to black addressing, but it must be able to say
        // that it did so because no metadata was read.
        return OPENUSD_SILK_WRAP_USE_METADATA;
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
    if (wrap == _tokens->useMetadata)
    {
        return OPENUSD_SILK_WRAP_USE_METADATA;
    }
    return OPENUSD_SILK_WRAP_BLACK;
}

/// Reads a string-valued MaterialX parameter. usdMtlx carries MaterialX
/// `string` inputs as either std::string or TfToken depending on how the layer
/// was authored, so both are accepted rather than one being silently ignored.
bool _TryReadStringParameter(
    const std::map<TfToken, VtValue>& parameters,
    const TfToken& name,
    std::string* out)
{
    const auto entry = parameters.find(name);
    if (entry == parameters.end())
    {
        return false;
    }
    if (entry->second.IsHolding<TfToken>())
    {
        *out = entry->second.UncheckedGet<TfToken>().GetString();
        return true;
    }
    if (entry->second.IsHolding<std::string>())
    {
        *out = entry->second.UncheckedGet<std::string>();
        return true;
    }
    return false;
}

/// Maps one MaterialX `uaddressmode`/`vaddressmode` value onto the wire wrap
/// enum.
///
/// The MaterialX default is `periodic`, not the `black` that UsdUVTexture's
/// unauthored `wrap` resolves to, so an image node with no authored address
/// mode tiles. Reading the UsdUVTexture token names here instead published
/// `black` for every MaterialX image, which clamped a tiled texture to its edge
/// row of texels.
///
/// `constant` is carried as OPENUSD_SILK_WRAP_BLACK, and that is an
/// approximation rather than an exact transport. MaterialX returns the node's
/// `default` value outside the unit range; the wire carries no border colour and
/// the current renderer resolves the black mode to clamp-to-edge, so an authored
/// `constant` renders the edge texel instead. It is still the right value to
/// publish: it is the only mode that records "the author did not ask for
/// periodic or mirrored addressing", which is what a consumer implementing
/// border sampling would need. Anything else is reported so the caller can
/// refuse the entry rather than tile a texture the graph asked to clamp.
bool _TryReadMaterialXAddressMode(
    const std::map<TfToken, VtValue>& parameters,
    const TfToken& name,
    uint32_t* wrap,
    std::string* unsupportedValue)
{
    std::string mode;
    if (!_TryReadStringParameter(parameters, name, &mode) || mode.empty())
    {
        // MaterialX defaults both address modes to periodic.
        *wrap = OPENUSD_SILK_WRAP_REPEAT;
        return true;
    }
    if (mode == _tokens->periodic.GetString())
    {
        *wrap = OPENUSD_SILK_WRAP_REPEAT;
        return true;
    }
    if (mode == _tokens->clamp.GetString())
    {
        *wrap = OPENUSD_SILK_WRAP_CLAMP;
        return true;
    }
    if (mode == _tokens->mirror.GetString())
    {
        *wrap = OPENUSD_SILK_WRAP_MIRROR;
        return true;
    }
    if (mode == _tokens->constant.GetString())
    {
        *wrap = OPENUSD_SILK_WRAP_BLACK;
        return true;
    }
    *unsupportedValue = mode;
    return false;
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
            node->identifier == _tokens->MtlxSurfaceUnlit ||
            _FindSurfaceProjection(node->identifier) != nullptr)
        {
            return &(*node);
        }
    }
    return nullptr;
}

/// The identifier prefix HdSilk_MdlMaterialSceneIndexPlugin stamps on a shader
/// node whose implementation is an MDL source asset. It is not an Sdr
/// identifier; see mdlMaterialSceneIndexPlugin.h for why one is synthesized.
const char* const _mdlIdentifierPrefix = "mdl:";

/// The sibling parameter names Hydra's scene-index adapter synthesizes to carry
/// a material node parameter's metadata. `colorSpace:<input>` is forwarded to
/// the adapter, which is the only place that knows which inputs name textures;
/// `typeName:<input>` states a type the VtValue already carries.
const char* const _mdlTypeNamePrefix = "typeName:";

/// Finds an MDL surface terminal. Only reached when no UsdPreviewSurface or
/// projected MaterialX terminal was found, so an authored universal or
/// MaterialX context always wins over the MDL one.
const HdMaterialNode* _FindMdlSurface(const HdMaterialNetwork& network)
{
    for (auto node = network.nodes.rbegin(); node != network.nodes.rend(); ++node)
    {
        if (_IdentifierHasPrefix(*node, _mdlIdentifierPrefix))
        {
            return &(*node);
        }
    }
    return nullptr;
}

/// Splits "mdl:<module>:<material>" back into its two halves. The module may
/// itself contain colons, as an omniverse:// URI does, so the split is on the
/// last separator rather than the first.
void _ParseMdlIdentifier(
    const TfToken& identifier,
    std::string* module,
    std::string* material)
{
    const std::string text = identifier.GetString();
    const size_t start = std::strlen(_mdlIdentifierPrefix);
    if (text.size() <= start)
    {
        return;
    }
    const std::string body = text.substr(start);
    const size_t separator = body.rfind(':');
    if (separator == std::string::npos)
    {
        *module = body;
        return;
    }
    *module = body.substr(0, separator);
    *material = body.substr(separator + 1);
}

/// Converts one authored MDL input into the value kinds the openusd_mdl C ABI
/// carries. Returns false for anything else, so an input this bridge cannot
/// express is reported by name instead of being narrowed into a different type.
bool _TryConvertMdlParameter(
    const TfToken& name,
    const VtValue& value,
    HdSilkMdlParameter* parameter)
{
    parameter->name = name.GetString();
    if (value.IsHolding<bool>())
    {
        parameter->kind = OPENUSD_MDL_VALUE_BOOL;
        parameter->componentCount = 1;
        parameter->integerValue = value.UncheckedGet<bool>() ? 1 : 0;
        return true;
    }
    if (value.IsHolding<int>())
    {
        parameter->kind = OPENUSD_MDL_VALUE_INT;
        parameter->componentCount = 1;
        parameter->integerValue = value.UncheckedGet<int>();
        return true;
    }
    if (value.IsHolding<SdfAssetPath>())
    {
        const SdfAssetPath asset = value.UncheckedGet<SdfAssetPath>();
        parameter->kind = OPENUSD_MDL_VALUE_ASSET;
        // The resolved path is what the consumer can open; the authored path is
        // relative to a layer it never sees. This mirrors the UsdUVTexture
        // resolution rule already used for the preview-surface path.
        parameter->text = asset.GetResolvedPath().empty()
            ? asset.GetAssetPath()
            : asset.GetResolvedPath();
        return !parameter->text.empty();
    }
    if (value.IsHolding<std::string>())
    {
        parameter->kind = OPENUSD_MDL_VALUE_STRING;
        parameter->text = value.UncheckedGet<std::string>();
        return true;
    }
    if (value.IsHolding<TfToken>())
    {
        parameter->kind = OPENUSD_MDL_VALUE_STRING;
        parameter->text = value.UncheckedGet<TfToken>().GetString();
        return true;
    }
    float floats[4] = {0.0f, 0.0f, 0.0f, 0.0f};
    const uint32_t count = _ReadFloats(value, floats);
    if (count == 0)
    {
        return false;
    }
    switch (count)
    {
        case 1:
            parameter->kind = OPENUSD_MDL_VALUE_FLOAT;
            break;
        case 2:
            parameter->kind = OPENUSD_MDL_VALUE_FLOAT2;
            break;
        case 3:
            parameter->kind = OPENUSD_MDL_VALUE_FLOAT3;
            break;
        default:
            parameter->kind = OPENUSD_MDL_VALUE_FLOAT4;
            break;
    }
    parameter->componentCount = count;
    for (uint32_t index = 0; index < count; ++index)
    {
        parameter->value[index] = floats[index];
    }
    return true;
}

/// Reports the inputs an MDL material states that this runtime did not put on
/// the wire, naming each one. Bounded so a pathological material cannot flood
/// the diagnostic stream.
void _WarnUnsupportedMdlInputs(
    const std::string& materialPath,
    const std::vector<std::string>& names)
{
    constexpr size_t kMaxReported = 8;
    if (names.empty())
    {
        return;
    }
    std::string joined;
    for (size_t index = 0; index < names.size() && index < kMaxReported; ++index)
    {
        if (!joined.empty())
        {
            joined += ", ";
        }
        joined += names[index];
    }
    if (names.size() > kMaxReported)
    {
        joined += ", ... (" + std::to_string(names.size() - kMaxReported) +
            " more)";
    }
    TF_WARN(
        "hdSilk material '%s' authors MDL inputs this runtime does not carry: "
        "%s. Each is left at its UsdPreviewSurface-equivalent default rather "
        "than folded into another parameter.",
        materialPath.c_str(),
        joined.c_str());
}

/// Distils an MDL-only surface terminal through the optional openusd_mdl
/// adapter. Every failure path leaves the record at
/// OPENUSD_SILK_SURFACE_MDL_UNAVAILABLE with empty tables and warns against the
/// material by name, because the alternative -- publishing a supported record
/// with nothing in it -- is exactly the silent default grey this branch exists
/// to remove.
void _ResolveMdlSurface(
    const HdMaterialNetwork& network,
    const HdMaterialNode& surface,
    HdSilkMaterialRecord& record)
{
    record.surfaceKind = OPENUSD_SILK_SURFACE_MDL_UNAVAILABLE;

    std::string module;
    std::string material;
    _ParseMdlIdentifier(surface.identifier, &module, &material);
    if (module.empty())
    {
        TF_WARN(
            "hdSilk material '%s' has an MDL surface terminal that names no "
            "module; it cannot be distilled and is not shaded.",
            record.path.c_str());
        return;
    }

    std::vector<std::string> unsupported;
    std::vector<HdSilkMdlParameter> parameters;
    parameters.reserve(surface.parameters.size());
    for (const auto& entry : surface.parameters)
    {
        // Hydra's scene-index adapter flattens a material node parameter's
        // metadata into sibling entries named "typeName:<input>" and
        // "colorSpace:<input>". The type name states nothing this bridge needs
        // -- the VtValue already carries the type -- so it is dropped rather
        // than reported as an authored MDL input the runtime ignored.
        const std::string name = entry.first.GetString();
        if (name.rfind(_mdlTypeNamePrefix, 0) == 0)
        {
            continue;
        }
        // An input a node drives is not the value Hydra leaves behind it in
        // `parameters`; that leftover is the value the author replaced. The
        // adapter distils constants only, so a connected input is reported
        // rather than read.
        if (_FindInputConnection(network, surface.path, entry.first) != nullptr)
        {
            unsupported.push_back(name);
            continue;
        }
        HdSilkMdlParameter parameter;
        if (!_TryConvertMdlParameter(entry.first, entry.second, &parameter))
        {
            unsupported.push_back(name);
            continue;
        }
        parameters.push_back(std::move(parameter));
    }

    const HdSilkMdlDistillation distillation =
        HdSilkMdlAdapter::Distill(record.path, module, material, parameters);
    unsupported.insert(
        unsupported.end(),
        distillation.unsupportedParameters.begin(),
        distillation.unsupportedParameters.end());

    if (!distillation.succeeded)
    {
        TF_WARN(
            "hdSilk material '%s' binds MDL module '%s' material '%s', which "
            "this runtime did not distil: %s. The material is published as "
            "MDL-unavailable and is not shaded.",
            record.path.c_str(),
            module.c_str(),
            material.c_str(),
            distillation.diagnostic.empty() ? "no reason reported"
                                            : distillation.diagnostic.c_str());
        _WarnUnsupportedMdlInputs(record.path, unsupported);
        return;
    }

    for (const HdSilkMdlDistilledScalar& source : distillation.scalars)
    {
        HdSilkMaterialScalar scalar;
        scalar.parameter = source.parameter;
        scalar.componentCount = source.componentCount;
        for (uint32_t index = 0; index < scalar.componentCount; ++index)
        {
            scalar.value[index] = source.value[index];
        }
        record.scalars.push_back(scalar);
    }
    for (const HdSilkMdlDistilledTexture& source : distillation.textures)
    {
        HdSilkMaterialTexture texture;
        texture.parameter = source.parameter;
        texture.componentCount = source.componentCount;
        texture.outputChannel = source.outputChannel;
        texture.wrapS = source.wrapS;
        texture.wrapT = source.wrapT;
        texture.sourceColorSpace = source.colorSpace;
        for (size_t index = 0; index < 4; ++index)
        {
            texture.scale[index] = source.scale[index];
            texture.bias[index] = source.bias[index];
        }
        texture.asset = source.asset;
        texture.uvPrimvar = _tokens->st.GetString();
        record.textures.push_back(std::move(texture));
    }
    record.surfaceKind = OPENUSD_SILK_SURFACE_MDL_DISTILLED;
    _WarnUnsupportedMdlInputs(record.path, unsupported);
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

/// Whether two folded UV affines are the same transform.
///
/// Comparison is tolerant rather than exact because these values are *computed*,
/// not copied: a quarter turn folds through std::sin/std::cos and a chain folds
/// through a matrix product, so two nodes that state the same transform by
/// different routes differ in the last bits. Exact equality made
/// `_ReconcileUvBindings` drop a texture whose transform agreed with the
/// material's to within a rounding step -- a 360-degree rotation whose cosine is
/// 0.99999994 is the same transform as the identity, and was treated as a
/// divergent one. The tolerance is relative for the linear terms so a large
/// scale is not held to an absolute epsilon it can never meet.
bool _UvTransformsEqual(const float (&left)[6], const float (&right)[6])
{
    constexpr float absoluteTolerance = 1e-6f;
    constexpr float relativeTolerance = 1e-6f;
    for (size_t index = 0; index < 6; ++index)
    {
        if (!std::isfinite(left[index]) || !std::isfinite(right[index]))
        {
            return false;
        }
        const float magnitude =
            std::max(std::fabs(left[index]), std::fabs(right[index]));
        const float tolerance =
            std::max(absoluteTolerance, relativeTolerance * magnitude);
        if (std::fabs(left[index] - right[index]) > tolerance)
        {
            return false;
        }
    }
    return true;
}

/// Reads one constant UV transform input. A connected input is rejected rather
/// than read from the node's authored fallback, because that fallback is not
/// what the graph asks to be rendered.
bool _ReadConstantTransformInput(
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
    if (!_ReadConstantTransformInput(network, node, _tokens->pivot, 2, zero, pivot) ||
        !_ReadConstantTransformInput(network, node, _tokens->scale, 2, one, scale) ||
        !_ReadConstantTransformInput(network, node, _tokens->rotate, 1, zero, rotate) ||
        !_ReadConstantTransformInput(network, node, _tokens->offset, 2, zero, offset) ||
        !_ReadConstantTransformInput(
            network, node, _tokens->operationorder, 1, zero, order))
    {
        *reason = "one of its place2d inputs is connected rather than constant";
        return false;
    }
    if (scale[0] == 0.0f || scale[1] == 0.0f)
    {
        *reason = "its place2d scale has a zero component";
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

/// Folds a UsdPreviewSurface UsdTransform2d node with constant inputs into one
/// affine.
///
/// UsdPreviewSurface defines the node as translation + rotate(rotation) applied
/// to (scale * in), with rotation counter-clockwise in degrees. That is the
/// opposite rotation sense to MaterialX rotate2d, so the two nodes are folded
/// separately rather than sharing one matrix builder.
bool _TryFoldTransform2d(
    const HdMaterialNetwork& network,
    const HdMaterialNode& node,
    float (&out)[6],
    std::string* reason)
{
    const float zero[4] = {0.0f, 0.0f, 0.0f, 0.0f};
    const float one[4] = {1.0f, 1.0f, 1.0f, 1.0f};
    float rotation[4] = {0.0f, 0.0f, 0.0f, 0.0f};
    float scale[4] = {1.0f, 1.0f, 0.0f, 0.0f};
    float translation[4] = {0.0f, 0.0f, 0.0f, 0.0f};
    if (!_ReadConstantTransformInput(
            network, node, _tokens->rotation, 1, zero, rotation) ||
        !_ReadConstantTransformInput(network, node, _tokens->scale, 2, one, scale) ||
        !_ReadConstantTransformInput(
            network, node, _tokens->translation, 2, zero, translation))
    {
        *reason = "one of its UsdTransform2d inputs is connected rather than constant";
        return false;
    }

    const float radians = rotation[0] * 0.01745329251994329577f;
    const float sine = std::sin(radians);
    const float cosine = std::cos(radians);
    // Counter-clockwise: (ca*x - sa*y, sa*x + ca*y), then scale before rotation.
    out[0] = cosine * scale[0];
    out[1] = -sine * scale[1];
    out[2] = sine * scale[0];
    out[3] = cosine * scale[1];
    out[4] = translation[0];
    out[5] = translation[1];
    return true;
}

/// Composes two affines so the result applies "inner" first and then "outer",
/// which is the order a coordinate travels a node chain: the node nearest the
/// image is applied last.
void _ComposeUvTransforms(
    const float (&outer)[6],
    const float (&inner)[6],
    float (&out)[6])
{
    const float composed[6] = {
        (outer[0] * inner[0]) + (outer[1] * inner[2]),
        (outer[0] * inner[1]) + (outer[1] * inner[3]),
        (outer[2] * inner[0]) + (outer[3] * inner[2]),
        (outer[2] * inner[1]) + (outer[3] * inner[3]),
        (outer[0] * inner[4]) + (outer[1] * inner[5]) + outer[4],
        (outer[2] * inner[4]) + (outer[3] * inner[5]) + outer[5]};
    for (size_t index = 0; index < 6; ++index)
    {
        out[index] = composed[index];
    }
}

bool _IsCoordinateNode(const HdMaterialNode& node)
{
    return node.identifier == _tokens->UsdPrimvarReaderFloat2 ||
        node.identifier == _tokens->MtlxGeomPropValue2 ||
        node.identifier == _tokens->MtlxTexCoord2;
}

/// Accepts only the first texture-coordinate set of a MaterialX texcoord node.
///
/// `ND_texcoord_vector2` carries a uniform integer `index` naming which UV set
/// to read, defaulting to zero. hdSilk publishes one coordinate stream per
/// material and names it from the primvar the coordinate node states, and a
/// texcoord node states no primvar name at all, so index 0 is the only value
/// that resolves to the documented default primvar. A non-zero index used to
/// fall through to that same default and silently sample the first UV set.
bool _TryValidateTexCoordIndex(
    const HdMaterialNetwork& network,
    const HdMaterialNode& node,
    std::string* reason)
{
    if (_FindInputConnection(network, node.path, _tokens->index) != nullptr)
    {
        *reason = "its texture-coordinate node has a connected 'index', which "
                  "selects a UV set per pixel";
        return false;
    }
    const auto entry = node.parameters.find(_tokens->index);
    if (entry == node.parameters.end())
    {
        return true;
    }
    float values[4] = {0.0f, 0.0f, 0.0f, 0.0f};
    if (_ReadFloats(entry->second, values) == 0)
    {
        *reason = "its texture-coordinate node has an 'index' this projection "
                  "cannot read";
        return false;
    }
    if (values[0] != 0.0f)
    {
        *reason = "its texture-coordinate node reads UV set " +
            std::to_string(static_cast<int>(values[0])) +
            ", and hdSilk carries one texture-coordinate stream per material";
        return false;
    }
    return true;
}

/// The longest texture-coordinate node chain this projection walks. A bound is
/// required rather than nice: the network is authored data and may contain a
/// cycle, and an unbounded walk would hang the render delegate on one.
constexpr uint32_t _maxUvChainDepth = 8;

/// Resolves how one texture reads its coordinates, walking the texcoord/st
/// connection through a bounded chain of constant UV transform nodes to the
/// coordinate node that names the primvar.
///
/// Every node on the chain must be one this projection models. A chain that
/// passes through anything else is reported unsupported rather than resolved to
/// the outermost transform over the default primvar, because applying only part
/// of an authored chain renders coordinates the graph never asked for.
_UvBinding _ResolveUvBinding(
    const HdMaterialNetwork& network,
    const SdfPath& texturePath)
{
    _UvBinding binding;
    const HdMaterialRelationship* connection =
        _FindInputConnection(network, texturePath, _tokens->st);
    if (connection == nullptr)
    {
        connection = _FindInputConnection(network, texturePath, _tokens->texcoord);
    }
    if (connection == nullptr)
    {
        // No coordinate connection at all is the documented default, not a
        // dropped chain: the texture reads the default primvar untransformed.
        return binding;
    }

    SdfPath current = connection->inputId;
    for (uint32_t depth = 0; depth <= _maxUvChainDepth; ++depth)
    {
        const HdMaterialNode* node = _FindNode(network, current);
        if (node == nullptr)
        {
            binding.supported = false;
            binding.unsupportedReason =
                "its texture-coordinate connection names a node the network does "
                "not contain";
            return binding;
        }
        if (_IsCoordinateNode(*node))
        {
            if (node->identifier == _tokens->MtlxTexCoord2 &&
                !_TryValidateTexCoordIndex(network, *node, &binding.unsupportedReason))
            {
                // A non-zero texcoord index names a second UV set. hdSilk carries
                // one texture-coordinate stream per material and its name comes
                // from the primvar the coordinate node states, so resolving a
                // non-zero index to "st" would sample the first UV set while the
                // graph asked for another.
                binding.supported = false;
                return binding;
            }
            const std::string name = _ReadPrimvarName(*node);
            if (!name.empty())
            {
                binding.primvar = name;
            }
            return binding;
        }

        float folded[6] = {1.0f, 0.0f, 0.0f, 1.0f, 0.0f, 0.0f};
        TfToken upstreamInput;
        if (node->identifier == _tokens->MtlxPlace2d)
        {
            if (!_TryFoldPlace2d(network, *node, folded, &binding.unsupportedReason))
            {
                binding.supported = false;
                return binding;
            }
            upstreamInput = _tokens->texcoord;
        }
        else if (node->identifier == _tokens->UsdTransform2d)
        {
            if (!_TryFoldTransform2d(network, *node, folded, &binding.unsupportedReason))
            {
                binding.supported = false;
                return binding;
            }
            upstreamInput = _tokens->in;
        }
        else
        {
            binding.supported = false;
            binding.unsupportedReason =
                "its texture-coordinate chain passes through unsupported node '" +
                node->identifier.GetString() + "'";
            return binding;
        }

        _ComposeUvTransforms(binding.transform, folded, binding.transform);
        const HdMaterialRelationship* upstream =
            _FindInputConnection(network, node->path, upstreamInput);
        if (upstream == nullptr)
        {
            // The transform's own coordinate input is an authored constant, so
            // the chain never reaches a primvar and the folded affine has
            // nothing to transform.
            binding.supported = false;
            binding.unsupportedReason =
                "its texture-coordinate chain ends at node '" +
                node->identifier.GetString() +
                "' without reaching a coordinate node";
            return binding;
        }
        current = upstream->inputId;
    }

    binding.supported = false;
    binding.unsupportedReason = "its texture-coordinate chain is longer than the "
                                "bounded depth this projection walks";
    return binding;
}

bool _EvaluateMaterialXConstant(
    const HdMaterialNetwork& network,
    const HdMaterialNode& node,
    uint32_t componentCount,
    float (&out)[4],
    uint32_t depth);

/// Evaluates one input of a node that this projection is folding to a constant.
///
/// A *connected* input is never read from the node's authored parameter. Hydra
/// leaves the nodedef's authored value in `parameters` even when the input is
/// connected, so falling back to it silently folds the graph to a value the
/// author replaced with a connection. The connection is followed instead, and a
/// connection this projection cannot evaluate -- including one whose upstream
/// node is missing from the network -- fails the fold so the caller diagnoses it.
bool _EvaluateMaterialXInput(
    const HdMaterialNetwork& network,
    const HdMaterialNode& node,
    const TfToken& inputName,
    uint32_t componentCount,
    const float (&fallback)[4],
    float (&out)[4],
    uint32_t depth)
{
    const HdMaterialRelationship* connection =
        _FindInputConnection(network, node.path, inputName);
    if (connection != nullptr)
    {
        const HdMaterialNode* upstream = _FindNode(network, connection->inputId);
        return upstream != nullptr &&
            _EvaluateMaterialXConstant(
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

/// Reads a node input that must be an authored constant, refusing a connection.
///
/// Used for the pass-through `in` shortcut and for `ND_constant_*`: both would
/// otherwise read the nodedef value Hydra leaves behind a connection and report
/// the whole sub-graph as that constant.
bool _ReadUnconnectedParameter(
    const HdMaterialNetwork& network,
    const HdMaterialNode& node,
    const TfToken& inputName,
    uint32_t componentCount,
    float (&out)[4])
{
    return _FindInputConnection(network, node.path, inputName) == nullptr &&
        _ReadParameterFloats(node, inputName, componentCount, out);
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
    if (_IdentifierHasPrefix(node, "ND_constant_"))
    {
        // ND_constant_* states its value on `value`, not on `in`, so without this
        // the node fell through every operator branch and was reported as an
        // unsupported upstream node.
        return _ReadUnconnectedParameter(
            network, node, _tokens->value, componentCount, out);
    }

    float zero[4] = {0.0f, 0.0f, 0.0f, 0.0f};
    float one[4] = {1.0f, 1.0f, 1.0f, 1.0f};
    float a[4] = {0.0f, 0.0f, 0.0f, 0.0f};
    float b[4] = {0.0f, 0.0f, 0.0f, 0.0f};
    const bool multiply = _IdentifierHasPrefix(node, "ND_multiply_");
    const bool add = _IdentifierHasPrefix(node, "ND_add_");
    const bool subtract = _IdentifierHasPrefix(node, "ND_subtract_");
    const bool clampNode = _IdentifierHasPrefix(node, "ND_clamp_");
    const bool mixNode = _IdentifierHasPrefix(node, "ND_mix_");

    // The pass-through shortcut is for nodes that carry `in` and that this
    // projection does not otherwise model, such as `dot`. It must not pre-empt a
    // modelled operator: `ND_clamp_*` also declares `in`, so taking the shortcut
    // first returned the *unclamped* value and made the operator's meaning depend
    // on whether its input happened to be a connection.
    if (!multiply && !add && !subtract && !clampNode && !mixNode &&
        _ReadUnconnectedParameter(network, node, _tokens->in, componentCount, out))
    {
        return true;
    }

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
    if (clampNode)
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
    if (mixNode)
    {
        // The MaterialX nodedef defaults `mix` to 0, which returns `bg`
        // unchanged. Defaulting it to 0.5 here folded an unauthored mix to the
        // midpoint of two operands the graph never asked to blend. The result
        // array is separate from the fallback array on purpose: passing one
        // array as both arguments aliases a const reference onto the output.
        const float mixFallback[4] = {0.0f, 0.0f, 0.0f, 0.0f};
        float mix[4] = {0.0f, 0.0f, 0.0f, 0.0f};
        if (!_EvaluateMaterialXInput(
                network, node, _tokens->bg, componentCount, zero, a, depth) ||
            !_EvaluateMaterialXInput(
                network, node, _tokens->fg, componentCount, zero, b, depth) ||
            !_EvaluateMaterialXInput(
                network, node, _tokens->mix, componentCount, mixFallback, mix, depth))
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

/// One image reached through a chain of constant arithmetic, together with the
/// affine that chain applies to the sampled value.
///
/// The renderer already carries a per-texture scale and bias that it applies per
/// texel in linear space after decode, so an authored chain that is affine in the
/// image is transported exactly by folding it there instead of inventing a new
/// per-pixel shader path. Affine operations commute with bilinear filtering, so
/// folding before the sample and folding after it are the same picture.
struct _AffineOverImage
{
    const HdMaterialNode* image = nullptr;
    TfToken outputName;
    float scale[4] = {1.0f, 1.0f, 1.0f, 1.0f};
    float bias[4] = {0.0f, 0.0f, 0.0f, 0.0f};
    std::string unsupportedReason;
};

bool _TryFoldAffineOverImage(
    const HdMaterialNetwork& network,
    const HdMaterialNode& node,
    const TfToken& outputName,
    uint32_t componentCount,
    uint32_t depth,
    _AffineOverImage* result);

/// Descends one operand of an arithmetic node. Returns true when that operand
/// resolves to the image sub-chain, false when it is a constant this projection
/// can evaluate, and reports an unsupported reason for anything else.
///
/// *isImage distinguishes the two success cases; *constant carries the folded
/// value when the operand is constant.
bool _TryClassifyAffineOperand(
    const HdMaterialNetwork& network,
    const HdMaterialNode& node,
    const TfToken& inputName,
    uint32_t componentCount,
    const float (&fallback)[4],
    uint32_t depth,
    bool* isImage,
    float (&constant)[4],
    _AffineOverImage* result)
{
    const HdMaterialRelationship* connection =
        _FindInputConnection(network, node.path, inputName);
    if (connection == nullptr)
    {
        *isImage = false;
        if (_ReadParameterFloats(node, inputName, componentCount, constant))
        {
            return true;
        }
        for (uint32_t index = 0; index < 4; ++index)
        {
            constant[index] = fallback[index];
        }
        return true;
    }

    const HdMaterialNode* upstream = _FindNode(network, connection->inputId);
    if (upstream == nullptr)
    {
        result->unsupportedReason = "an operand names a node the network does not contain";
        return false;
    }
    if (_EvaluateMaterialXConstant(network, *upstream, componentCount, constant, depth))
    {
        *isImage = false;
        return true;
    }
    *isImage = true;
    return _TryFoldAffineOverImage(
        network, *upstream, connection->inputName, componentCount, depth + 1, result);
}

/// Folds a chain of constant multiply/add/subtract/mix nodes over exactly one
/// image into a single per-component affine.
///
/// Only affine operators are walked, and only one operand of each may reach the
/// image: two images cannot be combined here, because the renderer binds one
/// texture per surface input. Everything else is reported so the caller can
/// diagnose it rather than approximate the graph.
bool _TryFoldAffineOverImage(
    const HdMaterialNetwork& network,
    const HdMaterialNode& node,
    const TfToken& outputName,
    uint32_t componentCount,
    uint32_t depth,
    _AffineOverImage* result)
{
    if (depth > 8)
    {
        result->unsupportedReason = "the arithmetic chain above the image is deeper "
                                    "than the bounded depth this projection folds";
        return false;
    }
    if (_IsMaterialXImage(node))
    {
        result->image = &node;
        result->outputName = outputName;
        return true;
    }

    const float zero[4] = {0.0f, 0.0f, 0.0f, 0.0f};
    const float one[4] = {1.0f, 1.0f, 1.0f, 1.0f};
    const bool multiply = _IdentifierHasPrefix(node, "ND_multiply_");
    const bool add = _IdentifierHasPrefix(node, "ND_add_");
    const bool subtract = _IdentifierHasPrefix(node, "ND_subtract_");
    const bool mix = _IdentifierHasPrefix(node, "ND_mix_");
    if (!multiply && !add && !subtract && !mix)
    {
        result->unsupportedReason = "node '" + node.identifier.GetString() +
            "' above the image is not one of the affine multiply, add, subtract, "
            "or mix operators this projection folds";
        return false;
    }

    // The operator's own affine in the image branch: value = branch * k + c.
    float k[4] = {1.0f, 1.0f, 1.0f, 1.0f};
    float c[4] = {0.0f, 0.0f, 0.0f, 0.0f};
    if (mix)
    {
        float background[4] = {0.0f, 0.0f, 0.0f, 0.0f};
        float foreground[4] = {0.0f, 0.0f, 0.0f, 0.0f};
        // The MaterialX nodedef defaults `mix` to 0, which selects `bg`.
        float factor[4] = {0.0f, 0.0f, 0.0f, 0.0f};
        const float factorFallback[4] = {0.0f, 0.0f, 0.0f, 0.0f};
        bool backgroundIsImage = false;
        bool foregroundIsImage = false;
        bool factorIsImage = false;
        if (!_TryClassifyAffineOperand(
                network, node, _tokens->mix, componentCount, factorFallback, depth,
                &factorIsImage, factor, result))
        {
            return false;
        }
        if (factorIsImage)
        {
            result->unsupportedReason =
                "the mix factor is driven by an image, which is a per-pixel blend "
                "this projection cannot fold into a constant";
            return false;
        }
        if (!_TryClassifyAffineOperand(
                network, node, _tokens->bg, componentCount, zero, depth,
                &backgroundIsImage, background, result))
        {
            return false;
        }
        if (!_TryClassifyAffineOperand(
                network, node, _tokens->fg, componentCount, zero, depth,
                &foregroundIsImage, foreground, result))
        {
            return false;
        }
        if (backgroundIsImage == foregroundIsImage)
        {
            result->unsupportedReason = backgroundIsImage
                ? "it mixes two images, and the renderer binds one texture per "
                  "surface input"
                : "neither mix operand resolves to an image";
            return false;
        }
        for (uint32_t index = 0; index < componentCount; ++index)
        {
            k[index] = foregroundIsImage ? factor[index] : 1.0f - factor[index];
            c[index] = foregroundIsImage
                ? background[index] * (1.0f - factor[index])
                : foreground[index] * factor[index];
        }
    }
    else
    {
        float first[4] = {0.0f, 0.0f, 0.0f, 0.0f};
        float second[4] = {0.0f, 0.0f, 0.0f, 0.0f};
        bool firstIsImage = false;
        bool secondIsImage = false;
        if (!_TryClassifyAffineOperand(
                network, node, _tokens->in1, componentCount, zero, depth,
                &firstIsImage, first, result))
        {
            return false;
        }
        if (!_TryClassifyAffineOperand(
                network, node, _tokens->in2, componentCount, multiply ? one : zero,
                depth, &secondIsImage, second, result))
        {
            return false;
        }
        if (firstIsImage == secondIsImage)
        {
            result->unsupportedReason = firstIsImage
                ? "it combines two images, and the renderer binds one texture per "
                  "surface input"
                : "neither operand resolves to an image";
            return false;
        }
        const float* constant = firstIsImage ? second : first;
        for (uint32_t index = 0; index < componentCount; ++index)
        {
            if (multiply)
            {
                k[index] = constant[index];
                c[index] = 0.0f;
            }
            else if (add)
            {
                k[index] = 1.0f;
                c[index] = constant[index];
            }
            else
            {
                // subtract: the image may be either operand, and "constant minus
                // image" is affine with a negative slope rather than unsupported.
                k[index] = firstIsImage ? 1.0f : -1.0f;
                c[index] = firstIsImage ? -constant[index] : constant[index];
            }
        }
    }

    // Compose inner-first: the recursion has already folded the branch below this
    // node into scale/bias, so this node's own affine is applied to that result.
    for (uint32_t index = 0; index < componentCount; ++index)
    {
        result->bias[index] = (result->bias[index] * k[index]) + c[index];
        result->scale[index] = result->scale[index] * k[index];
    }
    return true;
}

/// Composes the folded arithmetic onto the entry's own authored scale and bias:
/// the entry maps the texel with entryScale/entryBias, then the graph applies
/// its affine to that result.
void _ComposeTextureAffine(
    HdSilkMaterialTexture& entry,
    const _AffineOverImage& affine)
{
    for (uint32_t index = 0; index < 4; ++index)
    {
        entry.bias[index] = (entry.bias[index] * affine.scale[index]) + affine.bias[index];
        entry.scale[index] = entry.scale[index] * affine.scale[index];
    }
}

/// Whether the composed affine keeps a unit-range texel inside the unit range.
///
/// The consumer applies scale and bias per texel in linear space and stores the
/// result back in the decoded image's format, so an eight-bit source clamps
/// anything the affine pushes outside [0, 1]. A clamped base colour is not a
/// rounding difference: it changes the lit result at every light intensity below
/// saturation. Folding is therefore restricted to the range where the transport
/// is exact, and a brighten or darken that leaves the range is reported instead.
bool _AffineKeepsUnitRange(
    const HdSilkMaterialTexture& entry,
    uint32_t componentCount)
{
    constexpr float tolerance = 1e-6f;
    for (uint32_t index = 0; index < componentCount && index < 4; ++index)
    {
        const float low = std::min(entry.bias[index], entry.scale[index] + entry.bias[index]);
        const float high = std::max(entry.bias[index], entry.scale[index] + entry.bias[index]);
        if (!std::isfinite(low) || !std::isfinite(high) ||
            low < -tolerance || high > 1.0f + tolerance)
        {
            return false;
        }
    }
    return true;
}

/// How the nodedef's own weight input scales a projected input.
enum class _WeightState
{
    /// The weight is one, so the input projects unchanged.
    Unit,
    /// The weight is zero, so the lobe is off whatever the input carries.
    Zero,
    /// The weight is a constant other than zero or one.
    Scaled,
    /// The weight is outside this projection; the refusal is already reported.
    Unsupported
};

/// Resolves the constant the nodedef's weight input holds for this material.
///
/// The weight is read from the surface node rather than assumed, and its
/// nodedef default is used when the author left it alone. A *connected* weight
/// is refused rather than approximated: the multiply MaterialX states is
/// per-pixel, and this projection carries no per-pixel weight.
_WeightState _ResolveProjectedWeight(
    const HdMaterialNetwork& network,
    const HdMaterialNode& surface,
    const std::string& materialPath,
    const _ProjectedInput& input,
    float* weight)
{
    *weight = 1.0f;
    if (input.weight == nullptr)
    {
        return _WeightState::Unit;
    }
    if (_FindInputConnection(network, surface.path, *input.weight) != nullptr)
    {
        TF_WARN(
            "hdSilk material '%s' MaterialX input '%s' is scaled by '%s', which "
            "the graph connects; hdSilk projects only a constant weight, so the "
            "input is left at its default.",
            materialPath.c_str(),
            input.binding.name.GetText(),
            input.weight->GetText());
        return _WeightState::Unsupported;
    }
    const auto authored = surface.parameters.find(*input.weight);
    if (authored == surface.parameters.end())
    {
        *weight = input.weightDefault;
    }
    else
    {
        float values[4] = {0.0f, 0.0f, 0.0f, 0.0f};
        if (_ReadFloats(authored->second, values) == 0)
        {
            TF_WARN(
                "hdSilk material '%s' MaterialX weight '%s' holds an unsupported "
                "value type; leaving input '%s' at its default.",
                materialPath.c_str(),
                input.weight->GetText(),
                input.binding.name.GetText());
            return _WeightState::Unsupported;
        }
        *weight = values[0];
    }
    if (!std::isfinite(*weight))
    {
        TF_WARN(
            "hdSilk material '%s' MaterialX weight '%s' is not finite; leaving "
            "input '%s' at its default.",
            materialPath.c_str(),
            input.weight->GetText(),
            input.binding.name.GetText());
        return _WeightState::Unsupported;
    }
    if (*weight == 0.0f)
    {
        return _WeightState::Zero;
    }
    if (std::fabs(*weight - 1.0f) <= 1e-6f)
    {
        *weight = 1.0f;
        return _WeightState::Unit;
    }
    return _WeightState::Scaled;
}

/// Folds a constant nodedef weight into a texture entry's own scale and bias.
///
/// The weight multiplies the sampled value, which is affine in the texel, so it
/// composes with the entry's existing transport exactly. The unit-range guard is
/// the same one the arithmetic fold uses, because the reason is the same: the
/// eight-bit upload path clamps anything the affine pushes out of range.
bool _ScaleTextureEntryByWeight(
    HdSilkMaterialTexture& entry,
    float weight,
    const std::string& materialPath,
    const _ProjectedInput& input)
{
    for (uint32_t index = 0; index < 4; ++index)
    {
        entry.scale[index] = entry.scale[index] * weight;
        entry.bias[index] = entry.bias[index] * weight;
    }
    if (_AffineKeepsUnitRange(entry, input.binding.componentCount))
    {
        return true;
    }
    TF_WARN(
        "hdSilk material '%s' MaterialX input '%s' scaled by '%s' leaves a "
        "texture scale/bias outside the unit range, which the eight-bit upload "
        "path would clamp; leaving the input at its default.",
        materialPath.c_str(),
        input.binding.name.GetText(),
        input.weight == nullptr ? "" : input.weight->GetText());
    return false;
}

/// Collapses a colour-typed constant onto the single float the wire carries.
///
/// Only a constant whose three channels agree has one value; a per-channel
/// colour does not, and picking one channel would render a picture the author
/// did not author. Returns false after reporting when the channels disagree.
bool _CollapseMonochromeConstant(
    float (&values)[4],
    uint32_t count,
    const std::string& materialPath,
    const TfToken& name)
{
    if (count < 2)
    {
        return true;
    }
    constexpr float tolerance = 1e-6f;
    for (uint32_t index = 1; index < count && index < 3; ++index)
    {
        if (std::fabs(values[index] - values[0]) > tolerance)
        {
            TF_WARN(
                "hdSilk material '%s' MaterialX input '%s' holds a per-channel "
                "colour, and hdSilk binds one opacity channel; leaving the input "
                "at its default.",
                materialPath.c_str(),
                name.GetText());
            return false;
        }
    }
    return true;
}

/// Reports the MaterialX inputs this projection deliberately does not carry.
///
/// A material that leaves a lobe at its nodedef default asked for nothing and is
/// not reported. An authored value or a connection is a statement the renderer
/// cannot honour, so it is named individually together with the reason, rather
/// than folded into one "unsupported material" line that says nothing about
/// which part of the look is missing.
void _DiagnoseUnsupportedInputs(
    const HdMaterialNetwork& network,
    const HdMaterialNode& surface,
    const std::string& materialPath,
    const _SurfaceProjection& projection)
{
    for (const _UnsupportedInput& excluded : projection.unsupported)
    {
        if (_FindInputConnection(network, surface.path, excluded.name) != nullptr)
        {
            TF_WARN(
                "hdSilk material '%s' connects MaterialX input '%s', which this "
                "projection does not carry because %s; the input is ignored.",
                materialPath.c_str(),
                excluded.name.GetText(),
                excluded.reason);
            continue;
        }
        if (excluded.componentCount == 0)
        {
            continue;
        }
        const auto authored = surface.parameters.find(excluded.name);
        if (authored == surface.parameters.end())
        {
            continue;
        }
        float values[4] = {0.0f, 0.0f, 0.0f, 0.0f};
        const uint32_t count = _ReadFloats(authored->second, values);
        if (count == 0)
        {
            continue;
        }
        _BroadcastFloats(values, count, excluded.componentCount);
        bool authoredAway = false;
        for (uint32_t index = 0; index < excluded.componentCount && index < 4;
             ++index)
        {
            if (std::fabs(values[index] - excluded.defaultValue[index]) > 1e-6f)
            {
                authoredAway = true;
                break;
            }
        }
        if (!authoredAway)
        {
            continue;
        }
        TF_WARN(
            "hdSilk material '%s' authors MaterialX input '%s', which this "
            "projection does not carry because %s; the input is ignored.",
            materialPath.c_str(),
            excluded.name.GetText(),
            excluded.reason);
    }
}

/// Two images combined per pixel by one constant operator, each reached through
/// its own chain of constant arithmetic.
///
/// This is the one shape that genuinely cannot fold into a single texture entry:
/// the product, sum, difference or blend of two images is not affine in either
/// one, so no per-texel scale and bias can carry it. The renderer therefore binds
/// a second image and evaluates the operator in the fragment shader, in floating
/// point after both decodes.
///
/// Each branch is still folded independently, so constant arithmetic *inside* a
/// branch keeps its exact per-texel transport and only the one operator that
/// joins them costs a shader sample.
struct _TwoImageComposite
{
    _AffineOverImage primary;
    _AffineOverImage composite;
    uint32_t op = OPENUSD_SILK_COMPOSITE_NONE;
    float factor = 0.0f;
    std::string unsupportedReason;
};

/// Resolves one operand of an arithmetic node to an image sub-chain.
///
/// Returns false when the operand is absent, is a constant this projection can
/// evaluate, or is a chain this projection cannot fold. Only the last of those is
/// an error, so the caller distinguishes them through *branch->unsupportedReason.
bool _TryResolveImageBranch(
    const HdMaterialNetwork& network,
    const HdMaterialNode& node,
    const TfToken& inputName,
    uint32_t componentCount,
    _AffineOverImage* branch)
{
    const HdMaterialRelationship* connection =
        _FindInputConnection(network, node.path, inputName);
    if (connection == nullptr)
    {
        return false;
    }
    const HdMaterialNode* upstream = _FindNode(network, connection->inputId);
    if (upstream == nullptr)
    {
        branch->unsupportedReason =
            "an operand names a node the network does not contain";
        return false;
    }
    float constant[4] = {0.0f, 0.0f, 0.0f, 0.0f};
    if (_EvaluateMaterialXConstant(network, *upstream, componentCount, constant, 0))
    {
        return false;
    }
    return _TryFoldAffineOverImage(
               network,
               *upstream,
               connection->inputName,
               componentCount,
               0,
               branch) &&
        branch->image != nullptr;
}

/// Folds one arithmetic node whose *both* operands reach images into a pair of
/// texture entries joined by a per-pixel operator.
///
/// Operand order is preserved because it is observable: `subtract` is not
/// commutative, and `mix` selects the background at factor zero. `mix` requires a
/// constant scalar factor -- an image-driven factor is a third sampled input the
/// renderer has no slot for, and MaterialX types the input as a float, so a
/// per-component factor cannot be authored in the first place.
bool _TryFoldTwoImageComposite(
    const HdMaterialNetwork& network,
    const HdMaterialNode& node,
    uint32_t componentCount,
    _TwoImageComposite* result)
{
    const bool multiply = _IdentifierHasPrefix(node, "ND_multiply_");
    const bool add = _IdentifierHasPrefix(node, "ND_add_");
    const bool subtract = _IdentifierHasPrefix(node, "ND_subtract_");
    const bool mix = _IdentifierHasPrefix(node, "ND_mix_");
    if (!multiply && !add && !subtract && !mix)
    {
        return false;
    }

    const TfToken& primaryInput = mix ? _tokens->bg : _tokens->in1;
    const TfToken& compositeInput = mix ? _tokens->fg : _tokens->in2;
    if (!_TryResolveImageBranch(
            network, node, primaryInput, componentCount, &result->primary) ||
        !_TryResolveImageBranch(
            network, node, compositeInput, componentCount, &result->composite))
    {
        // Not a two-image node at all, or a branch this projection cannot fold.
        // Either way the single-image path has already reported what it found.
        result->unsupportedReason = result->primary.unsupportedReason.empty()
            ? result->composite.unsupportedReason
            : result->primary.unsupportedReason;
        return false;
    }
    if (result->primary.image == result->composite.image)
    {
        // One image wired to both operands is affine in that image after all, so
        // the single-image fold owns it and would be exact where this is not.
        result->unsupportedReason =
            "both operands resolve to the same image, which the affine fold "
            "already carries exactly";
        return false;
    }

    if (mix)
    {
        if (_FindInputConnection(network, node.path, _tokens->mix) != nullptr)
        {
            result->unsupportedReason =
                "the mix factor is connected, and the renderer has no slot for a "
                "third sampled input";
            return false;
        }
        float factor[4] = {0.0f, 0.0f, 0.0f, 0.0f};
        // MaterialX defaults the factor to 0, which selects the background.
        if (!_ReadParameterFloats(node, _tokens->mix, 1, factor))
        {
            factor[0] = 0.0f;
        }
        if (!std::isfinite(factor[0]))
        {
            result->unsupportedReason = "the mix factor is not finite";
            return false;
        }
        result->op = OPENUSD_SILK_COMPOSITE_MIX;
        result->factor = factor[0];
        return true;
    }

    result->op = multiply ? OPENUSD_SILK_COMPOSITE_MULTIPLY
        : add             ? OPENUSD_SILK_COMPOSITE_ADD
                          : OPENUSD_SILK_COMPOSITE_SUBTRACT;
    result->factor = 0.0f;
    return true;
}

/// Builds the texture entry for one MaterialX input.
///
/// *diagnosed is set when this function already reported why it refused the
/// entry. The caller must then stop rather than fall through to its generic
/// "connected to unsupported node" warning, which would name the image node
/// even when the image is fine and its coordinate chain is what failed.
/// Why one image could not be published as a texture entry.
///
/// The reason is structured rather than a bare false because the callers do not
/// all want the same thing from a failure. Displacement, in particular, converts
/// an image that resolves *no file* into the node's authored fallback, which is
/// what UsdUVTexture says the reader produces then -- and must not do that for a
/// graph it simply cannot evaluate, because publishing a fallback there would
/// displace a surface by a value nobody asked for.
enum class _TextureEntryFailure
{
    None,
    NotAnImage,
    NoResolvableFile,
    UnsupportedGraph
};

bool _TryCreateTextureEntry(
    const HdMaterialNetwork& network,
    const std::string& materialPath,
    const HdMaterialNode& texture,
    const TfToken& outputName,
    const _InputBinding& input,
    HdSilkMaterialTexture& entry,
    bool* diagnosed,
    _TextureEntryFailure* failure = nullptr)
{
    *diagnosed = false;
    if (failure != nullptr)
    {
        *failure = _TextureEntryFailure::None;
    }
    auto fail = [&](_TextureEntryFailure reason)
    {
        if (failure != nullptr)
        {
            *failure = reason;
        }
        return false;
    };
    if (!_IsMaterialXImage(texture) && texture.identifier != _tokens->UsdUVTexture)
    {
        return fail(_TextureEntryFailure::NotAnImage);
    }
    const std::string asset = _ResolveAssetPath(texture.parameters);
    if (asset.empty())
    {
        TF_WARN(
            "hdSilk material '%s' input '%s' is connected to image '%s', which has "
            "no resolvable file; leaving the input at its default.",
            materialPath.c_str(),
            input.name.GetText(),
            texture.path.GetText());
        *diagnosed = true;
        return fail(_TextureEntryFailure::NoResolvableFile);
    }
    uint32_t channel = OPENUSD_SILK_TEXTURE_CHANNEL_RGB;
    if (!_TryResolveOutputChannel(texture, outputName, input.componentCount, &channel))
    {
        TF_WARN(
            "hdSilk material '%s' input '%s' is connected to texture output '%s', "
            "which cannot drive a %u-component input; leaving the input at its "
            "default.",
            materialPath.c_str(),
            input.name.GetText(),
            outputName.GetText(),
            input.componentCount);
        *diagnosed = true;
        return fail(_TextureEntryFailure::UnsupportedGraph);
    }
    entry.parameter = input.parameter;
    entry.componentCount = input.componentCount;
    entry.outputChannel = channel;
    if (_IsMaterialXImage(texture))
    {
        // MaterialX states addressing on uaddressmode/vaddressmode, defaulting to
        // periodic; UsdUVTexture states it on wrapS/wrapT, defaulting to black.
        // Reading the UsdUVTexture names from a MaterialX image found nothing and
        // published black, so a MaterialX texture that the graph tiles was clamped
        // to its edge texels instead.
        std::string unsupportedMode;
        if (!_TryReadMaterialXAddressMode(
                texture.parameters, _tokens->uaddressmode, &entry.wrapS,
                &unsupportedMode) ||
            !_TryReadMaterialXAddressMode(
                texture.parameters, _tokens->vaddressmode, &entry.wrapT,
                &unsupportedMode))
        {
            TF_WARN(
                "hdSilk material '%s' input '%s' reads a MaterialX image whose "
                "address mode '%s' this projection does not model; leaving the "
                "input at its default.",
                materialPath.c_str(),
                input.name.GetText(),
                unsupportedMode.c_str());
            *diagnosed = true;
            return false;
        }
    }
    else
    {
        entry.wrapS = _ReadWrap(texture.parameters, _tokens->wrapS);
        entry.wrapT = _ReadWrap(texture.parameters, _tokens->wrapT);
    }
    entry.sourceColorSpace = _ReadColorSpace(texture.parameters);
    // A connected scale, bias, or fallback is a per-pixel value the wire's four
    // constant floats cannot carry, and Hydra leaves the nodedef value in
    // `parameters` behind the connection, so reading it would publish a constant
    // the author replaced.
    const TfToken fallbackName = _IsMaterialXImage(texture)
        ? _tokens->defaultValue
        : _tokens->fallback;
    for (const TfToken& constantInput :
         {_tokens->scale, _tokens->bias, fallbackName})
    {
        if (_FindInputConnection(network, texture.path, constantInput) != nullptr)
        {
            TF_WARN(
                "hdSilk material '%s' input '%s' reads an image whose '%s' is "
                "connected rather than constant, which the texture entry cannot "
                "carry; leaving the input at its default.",
                materialPath.c_str(),
                input.name.GetText(),
                constantInput.GetText());
            *diagnosed = true;
            return fail(_TextureEntryFailure::UnsupportedGraph);
        }
    }
    entry.scale[0] = entry.scale[1] = entry.scale[2] = entry.scale[3] = 1.0f;
    _ReadVector4(texture.parameters, _tokens->scale, entry.scale);
    _ReadVector4(texture.parameters, _tokens->bias, entry.bias);
    // The value the node produces when the file cannot be read: UsdUVTexture
    // calls it `fallback`, MaterialX calls it `default`. The schema default is
    // (0, 0, 0, 1), not four zeroes, so the alpha is seeded opaque before the
    // authored components overwrite it -- otherwise an `outputs:a` reading an
    // unauthored fallback would produce a transparent zero where the schema says
    // one. Only the components the node authors are overwritten, so a color3
    // default keeps that opaque alpha.
    entry.fallback[0] = entry.fallback[1] = entry.fallback[2] = 0.0f;
    entry.fallback[3] = 1.0f;
    _ReadVector4(texture.parameters, fallbackName, entry.fallback);
    entry.asset = asset;
    const _UvBinding uv = _ResolveUvBinding(network, texture.path);
    if (!uv.supported)
    {
        TF_WARN(
            "hdSilk material '%s' input '%s' reads its texture coordinates through "
            "a chain this projection cannot fold because %s; leaving the input at "
            "its default.",
            materialPath.c_str(),
            input.name.GetText(),
            uv.unsupportedReason.c_str());
        *diagnosed = true;
        return fail(_TextureEntryFailure::UnsupportedGraph);
    }
    entry.uvPrimvar = uv.primvar;
    for (size_t index = 0; index < 6; ++index)
    {
        entry.uvTransform[index] = uv.transform[index];
    }
    return true;
}

/// Builds and publishes the two entries of a per-pixel two-image input.
///
/// Both entries are appended, or neither is: the composite operand is the second
/// half of one authored expression, so a lone primary would render one of two
/// images and look like an ordinary single-texture input. Each entry carries its
/// own branch's folded affine, which must still keep the unit range because that
/// affine is applied per texel when the image is decoded into its own storage
/// format. The joining operator is not subject to that restriction: the consumer
/// evaluates it in the shader in floating point, after both decodes.
bool _TryPublishCompositeEntries(
    const HdMaterialNetwork& network,
    const _InputBinding& input,
    const _TwoImageComposite& composite,
    HdSilkMaterialRecord& record)
{
    HdSilkMaterialTexture primaryEntry;
    HdSilkMaterialTexture compositeEntry;
    bool primaryDiagnosed = false;
    bool compositeDiagnosed = false;
    if (!_TryCreateTextureEntry(
            network,
            record.path,
            *composite.primary.image,
            composite.primary.outputName,
            input,
            primaryEntry,
            &primaryDiagnosed) ||
        !_TryCreateTextureEntry(
            network,
            record.path,
            *composite.composite.image,
            composite.composite.outputName,
            input,
            compositeEntry,
            &compositeDiagnosed))
    {
        if (!primaryDiagnosed && !compositeDiagnosed)
        {
            TF_WARN(
                "hdSilk material '%s' MaterialX input '%s' combines two images "
                "this projection cannot bind; leaving the input at its default.",
                record.path.c_str(),
                input.name.GetText());
        }
        return true;
    }

    _ComposeTextureAffine(primaryEntry, composite.primary);
    _ComposeTextureAffine(compositeEntry, composite.composite);
    if (!_AffineKeepsUnitRange(primaryEntry, input.componentCount) ||
        !_AffineKeepsUnitRange(compositeEntry, input.componentCount))
    {
        TF_WARN(
            "hdSilk material '%s' MaterialX input '%s' combines two images where "
            "one branch folds to a texture scale/bias that leaves the unit range, "
            "which the eight-bit upload path would clamp; leaving the input at its "
            "default.",
            record.path.c_str(),
            input.name.GetText());
        return true;
    }

    compositeEntry.compositeOp = composite.op;
    compositeEntry.compositeFactor = composite.factor;
    record.textures.push_back(std::move(primaryEntry));
    record.textures.push_back(std::move(compositeEntry));
    return true;
}

/// Settles the one texture-coordinate stream this material publishes.
///
/// The renderer builds exactly one coordinate stream per material: it takes the
/// material's UV transform from the wire's per-material affine, and its primvar
/// from the first texture entry that names one, then samples every texture of the
/// material through that pair. A material whose images ask for different
/// transforms, or for different primvars, is therefore outside the projection.
///
/// The first texture in the fixed input order states both. A texture that asks
/// for a different transform or a different primvar is dropped with a diagnostic
/// naming which of the two diverged, rather than sampled through coordinates it
/// did not author. The primvar half matters as much as the transform half and was
/// previously unreconciled: a base colour on `uvSet0` beside a normal map on
/// `uvSet1` published both entries, and the consumer sampled both through
/// `uvSet0` with nothing reported.
///
/// Entries are dropped a whole parameter at a time. Since ABI v15 one surface
/// input may carry two entries -- a primary and a composite operand -- and those
/// are two halves of one authored expression: keeping the primary of a pair whose
/// composite diverged would render one of two authored images, which is a
/// plausible picture the author never asked for.
///
/// Returns whether anything was dropped, so the caller can re-settle the stream
/// against the survivors.
/// Whether one texture entry belongs to the material's single surface
/// texture-coordinate stream.
///
/// Displacement does not. It is sampled per vertex, from its own authored
/// coordinate set and through its own folded affine, so it neither constrains
/// the surface stream nor is constrained by it -- a material whose surface
/// samples `st` and whose height field samples `st2` is exactly representable,
/// and reconciling the two would drop one of them for no reason.
bool _BelongsToSurfaceUvStream(const HdSilkMaterialTexture& texture)
{
    return texture.parameter != OPENUSD_SILK_MATERIAL_DISPLACEMENT;
}

bool _ReconcileUvBindingsOnce(HdSilkMaterialRecord& record)
{
    const HdSilkMaterialTexture* first = nullptr;
    for (const HdSilkMaterialTexture& texture : record.textures)
    {
        if (_BelongsToSurfaceUvStream(texture))
        {
            first = &texture;
            break;
        }
    }
    if (first == nullptr)
    {
        return false;
    }
    for (size_t index = 0; index < 6; ++index)
    {
        record.uvTransform[index] = first->uvTransform[index];
    }

    // The material primvar is the first non-empty one, which is exactly how the
    // consumer derives its single stream. An entry that names no primvar asks
    // for nothing and is sampled through that stream either way.
    std::string primvar;
    for (const HdSilkMaterialTexture& texture : record.textures)
    {
        if (_BelongsToSurfaceUvStream(texture) && !texture.uvPrimvar.empty())
        {
            primvar = texture.uvPrimvar;
            break;
        }
    }

    std::vector<uint32_t> divergent;
    for (const HdSilkMaterialTexture& texture : record.textures)
    {
        if (!_BelongsToSurfaceUvStream(texture))
        {
            continue;
        }
        const bool transformAgrees =
            _UvTransformsEqual(texture.uvTransform, record.uvTransform);
        const bool primvarAgrees =
            texture.uvPrimvar.empty() || texture.uvPrimvar == primvar;
        if (transformAgrees && primvarAgrees)
        {
            continue;
        }
        if (std::find(divergent.begin(), divergent.end(), texture.parameter) !=
            divergent.end())
        {
            continue;
        }
        divergent.push_back(texture.parameter);
        if (!primvarAgrees)
        {
            TF_WARN(
                "hdSilk material '%s' texture for parameter %u reads primvar '%s' "
                "while the material's coordinate stream is '%s'; hdSilk carries "
                "one texture-coordinate stream per material, so this input is "
                "left at its default.",
                record.path.c_str(),
                texture.parameter,
                texture.uvPrimvar.c_str(),
                primvar.c_str());
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
    if (divergent.empty())
    {
        return false;
    }

    std::vector<HdSilkMaterialTexture> kept;
    kept.reserve(record.textures.size());
    for (HdSilkMaterialTexture& texture : record.textures)
    {
        if (std::find(divergent.begin(), divergent.end(), texture.parameter) ==
            divergent.end())
        {
            kept.push_back(std::move(texture));
        }
    }
    record.textures = std::move(kept);
    return true;
}

/// Runs the stream reconciliation to a fixed point.
///
/// One pass is not enough once entries are dropped a parameter at a time: the
/// dropped parameter can be the one that stated the stream, in which case the
/// survivors were measured against a stream the material no longer publishes.
/// The loop is bounded by the number of parameters because every pass that
/// changes anything removes at least one whole parameter.
void _ReconcileUvBindings(HdSilkMaterialRecord& record)
{
    const size_t bound = record.textures.size() + 1;
    for (size_t pass = 0; pass < bound; ++pass)
    {
        if (!_ReconcileUvBindingsOnce(record))
        {
            return;
        }
    }
}

/// Settles the one composite texture this material publishes.
///
/// The renderer binds a single composite image per material rather than one per
/// surface input, because a per-input composite would need a second sampler and
/// texture for each of the eleven material slots. A graph that composites a
/// second parameter therefore has *both* entries of that parameter dropped:
/// publishing only its primary would render one of two authored images and look
/// like an ordinary single-texture input.
///
/// The first composited parameter in the fixed input order wins, which makes the
/// choice deterministic rather than dependent on table order.
void _ReconcileComposites(HdSilkMaterialRecord& record)
{
    bool haveComposite = false;
    std::vector<uint32_t> dropped;
    for (const HdSilkMaterialTexture& texture : record.textures)
    {
        if (texture.compositeOp == OPENUSD_SILK_COMPOSITE_NONE)
        {
            continue;
        }
        if (!haveComposite)
        {
            haveComposite = true;
            continue;
        }
        dropped.push_back(texture.parameter);
        TF_WARN(
            "hdSilk material '%s' composites two images into parameter %u as well "
            "as into an earlier input; hdSilk binds one composite texture per "
            "material, so this input is left at its default rather than rendering "
            "one of its two authored images.",
            record.path.c_str(),
            texture.parameter);
    }
    if (dropped.empty())
    {
        return;
    }

    std::vector<HdSilkMaterialTexture> kept;
    kept.reserve(record.textures.size());
    for (HdSilkMaterialTexture& texture : record.textures)
    {
        if (std::find(dropped.begin(), dropped.end(), texture.parameter) ==
            dropped.end())
        {
            kept.push_back(std::move(texture));
        }
    }
    record.textures = std::move(kept);
}

/// Resolves the constant a displacement falls back to when its connected image
/// cannot be published as a texture entry.
///
/// UsdUVTexture defines `fallback` as the value the reader produces when the file
/// cannot be read, so an image with no resolvable file is not the same thing as
/// an unauthored displacement: the graph still states a height, and it is the
/// authored fallback read through the connected output channel and the node's own
/// `scale` and `bias`. Returns false only when the node authors none of that in a
/// form this delegate can read.
bool
_TryResolveDisplacementFallbackConstant(
    const HdMaterialNetwork& network,
    const HdMaterialNode& texture,
    const TfToken& outputName,
    const _InputBinding& input,
    float* value)
{
    uint32_t channel = OPENUSD_SILK_TEXTURE_CHANNEL_RGB;
    if (!_TryResolveOutputChannel(texture, outputName, input.componentCount, &channel))
    {
        return false;
    }
    const TfToken& fallbackName = _IsMaterialXImage(texture)
        ? _tokens->defaultValue
        : _tokens->fallback;
    for (const TfToken& constantInput :
         {_tokens->scale, _tokens->bias, fallbackName})
    {
        if (_FindInputConnection(network, texture.path, constantInput) != nullptr)
        {
            return false;
        }
    }
    float scale[4] = {1.0F, 1.0F, 1.0F, 1.0F};
    float bias[4] = {0.0F, 0.0F, 0.0F, 0.0F};
    // UsdUVTexture's schema default for `fallback` is (0, 0, 0, 1). Seeding four
    // zeroes would make an `outputs:a` connection read a transparent zero where
    // the schema says one.
    float fallback[4] = {0.0F, 0.0F, 0.0F, 1.0F};
    _ReadVector4(texture.parameters, _tokens->scale, scale);
    _ReadVector4(texture.parameters, _tokens->bias, bias);
    _ReadVector4(texture.parameters, fallbackName, fallback);
    const size_t component =
        channel >= OPENUSD_SILK_TEXTURE_CHANNEL_RGB ? 0u : static_cast<size_t>(channel);
    const float resolved = (fallback[component] * scale[component]) + bias[component];
    if (!std::isfinite(resolved))
    {
        return false;
    }
    *value = resolved;
    return true;
}

/// Finds the terminal node of a displacement network: the node no other node of
/// that network consumes.
///
/// Unlike `_FindSurface` this does not look for a known identifier, because the
/// point of the check is to *report* a terminal this renderer cannot evaluate
/// rather than to skip past it and resolve some other node.
const HdMaterialNode*
_FindDisplacementTerminal(const HdMaterialNetwork& network)
{
    for (auto node = network.nodes.rbegin(); node != network.nodes.rend(); ++node)
    {
        bool consumed = false;
        for (const HdMaterialRelationship& relationship : network.relationships)
        {
            if (relationship.inputId == node->path)
            {
                consumed = true;
                break;
            }
        }
        if (!consumed)
        {
            return &(*node);
        }
    }
    return nullptr;
}

/// Resolves the authored `displacement` material terminal.
///
/// The terminal is required: a material that connects no `outputs:displacement`
/// publishes no displacement, whatever `inputs:displacement` its surface shader
/// happens to carry. The terminal node must be a `UsdPreviewSurface`, because
/// that is the only shader whose displacement output this renderer evaluates,
/// and its `inputs:displacement` must be either unconnected -- in which case the
/// authored constant is published -- or driven by a `UsdUVTexture`. Anything
/// else is a graph hdSilk cannot evaluate per vertex and is reported by name
/// instead of being collapsed to whichever constant Hydra left behind the
/// connection.
void
_ResolveDisplacementTerminal(
    const HdMaterialNetworkMap& networkMap,
    HdSilkMaterialRecord& record)
{
    const auto entry = networkMap.map.find(HdMaterialTerminalTokens->displacement);
    if (entry == networkMap.map.end() || entry->second.nodes.empty())
    {
        return;
    }
    const HdMaterialNetwork& network = entry->second;
    const HdMaterialNode* terminal = _FindDisplacementTerminal(network);
    if (terminal == nullptr)
    {
        TF_WARN(
            "hdSilk material '%s' has a displacement terminal network with no "
            "unconsumed node; leaving the material undisplaced.",
            record.path.c_str());
        return;
    }
    if (terminal->identifier != _tokens->UsdPreviewSurface)
    {
        TF_WARN(
            "hdSilk material '%s' connects outputs:displacement to node '%s' of "
            "type '%s'; hdSilk evaluates only UsdPreviewSurface displacement, so "
            "the material is left undisplaced.",
            record.path.c_str(),
            terminal->path.GetText(),
            terminal->identifier.GetText());
        return;
    }

    const _InputBinding& input = _DisplacementInput();
    const HdMaterialRelationship* connection =
        _FindInputConnection(network, terminal->path, input.name);
    if (connection != nullptr)
    {
        const HdMaterialNode* upstream = _FindNode(network, connection->inputId);
        if (upstream == nullptr)
        {
            TF_WARN(
                "hdSilk material '%s' displacement is connected to a node the "
                "displacement network does not contain; leaving the material "
                "undisplaced rather than reading the value the author replaced.",
                record.path.c_str());
            return;
        }
        HdSilkMaterialTexture texture;
        bool diagnosed = false;
        _TextureEntryFailure failure = _TextureEntryFailure::None;
        if (_TryCreateTextureEntry(
                network,
                record.path,
                *upstream,
                connection->inputName,
                input,
                texture,
                &diagnosed,
                &failure))
        {
            record.textures.push_back(std::move(texture));
            return;
        }
        // Only a genuinely absent or unresolvable file becomes the authored
        // fallback: UsdUVTexture defines `fallback` as what the reader produces
        // when it cannot read the file, and says nothing about a graph this
        // renderer cannot evaluate. Publishing a fallback for an unsupported
        // coordinate chain, a connected scale, or an output port that cannot
        // drive the input would displace the surface by a value nobody authored
        // for that condition, which is the plausible-but-wrong result the whole
        // refusal vocabulary exists to prevent.
        float fallbackAmount = 0.0F;
        if (failure == _TextureEntryFailure::NoResolvableFile &&
            _TryResolveDisplacementFallbackConstant(
                network,
                *upstream,
                connection->inputName,
                input,
                &fallbackAmount))
        {
            TF_WARN(
                "hdSilk material '%s' displacement image '%s' could not be "
                "published; the authored fallback %f is published as a constant "
                "displacement instead.",
                record.path.c_str(),
                upstream->path.GetText(),
                static_cast<double>(fallbackAmount));
            HdSilkMaterialScalar fallbackScalar;
            fallbackScalar.parameter = input.parameter;
            fallbackScalar.componentCount = 1;
            fallbackScalar.value[0] = fallbackAmount;
            record.scalars.push_back(fallbackScalar);
            return;
        }
        if (!diagnosed)
        {
            TF_WARN(
                "hdSilk material '%s' drives displacement from node '%s' of type "
                "'%s', which hdSilk cannot evaluate per vertex; the material is "
                "left undisplaced rather than displaced by the value the author "
                "replaced.",
                record.path.c_str(),
                upstream->path.GetText(),
                upstream->identifier.GetText());
        }
        return;
    }

    const auto authored = terminal->parameters.find(input.name);
    if (authored == terminal->parameters.end())
    {
        return;
    }
    float values[4] = {0.0F, 0.0F, 0.0F, 0.0F};
    const uint32_t count = _ReadFloats(authored->second, values);
    if (count == 0)
    {
        TF_WARN(
            "hdSilk material '%s' displacement holds an unsupported value type; "
            "leaving the material undisplaced.",
            record.path.c_str());
        return;
    }
    HdSilkMaterialScalar scalar;
    scalar.parameter = input.parameter;
    scalar.componentCount = 1;
    scalar.value[0] = values[0];
    record.scalars.push_back(scalar);
}

/// Settles the one texture-coordinate affine a displacement may sample through.
///
/// The wire carries one folded affine per material, because the renderer builds
/// one surface coordinate stream. A displacement samples its own primvar, which
/// the wire does carry per texture, but it cannot carry a second affine. When the
/// material publishes no surface texture the displacement's own affine becomes
/// the material's -- nothing else reads it -- and when it publishes one, a
/// displacement asking for a *different* affine is genuinely unrepresentable and
/// is reported rather than sampled through coordinates it did not author.
void _ReconcileDisplacementTransform(HdSilkMaterialRecord& record)
{
    HdSilkMaterialTexture* displacement = nullptr;
    bool haveSurfaceTexture = false;
    for (HdSilkMaterialTexture& texture : record.textures)
    {
        if (_BelongsToSurfaceUvStream(texture))
        {
            haveSurfaceTexture = true;
        }
        else if (texture.compositeOp == OPENUSD_SILK_COMPOSITE_NONE)
        {
            displacement = &texture;
        }
    }
    if (displacement == nullptr)
    {
        return;
    }
    if (!haveSurfaceTexture)
    {
        for (size_t index = 0; index < 6; ++index)
        {
            record.uvTransform[index] = displacement->uvTransform[index];
        }
        return;
    }
    if (_UvTransformsEqual(displacement->uvTransform, record.uvTransform))
    {
        return;
    }
    TF_WARN(
        "hdSilk material '%s' displacement asks for a different folded texture "
        "coordinate transform than the material's surface textures; hdSilk "
        "carries one transform per material, so the displacement is left "
        "unpublished rather than sampled through coordinates it did not author.",
        record.path.c_str());
    std::vector<HdSilkMaterialTexture> kept;
    kept.reserve(record.textures.size());
    for (HdSilkMaterialTexture& texture : record.textures)
    {
        if (_BelongsToSurfaceUvStream(texture))
        {
            kept.push_back(std::move(texture));
        }
    }
    record.textures = std::move(kept);
}

/// Finishes any resolved record: resolves the displacement terminal, then
/// reconciles the single texture-coordinate stream and the single composite the
/// wire carries.
///
/// Called on every exit of `Resolve`, including the ones that could not classify
/// a surface at all. Displacement is a separate material terminal in USD, so a
/// material whose surface is unsupported, MDL-only, or a generated MaterialX
/// fragment can still author a displacement this renderer evaluates exactly, and
/// dropping it because the *surface* was unshaded would silently flatten geometry
/// the author asked to move.
void
_FinishRecord(const HdMaterialNetworkMap& networkMap, HdSilkMaterialRecord& record)
{
    _ResolveDisplacementTerminal(networkMap, record);
    _ReconcileUvBindings(record);
    _ReconcileDisplacementTransform(record);
    _ReconcileComposites(record);
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
        _FinishRecord(networkMap, record);
        return record;
    }
    const HdMaterialNetwork& network = surfaceNetwork->second;
    const HdMaterialNode* surface = _FindSurface(network);
    if (surface == nullptr)
    {
        // An MDL-only material reaches this delegate because the render
        // delegate asks for the `mdl` render context after the universal one.
        // It has no UsdPreviewSurface and no projected MaterialX terminal, so
        // it lands here rather than in the branch above.
        if (const HdMaterialNode* mdlSurface = _FindMdlSurface(network))
        {
            _ResolveMdlSurface(network, *mdlSurface, record);
            _FinishRecord(networkMap, record);
            return record;
        }
        // Publishing the unsupported record rather than nothing is deliberate:
        // the consumer can then say which material it could not shade.
        TF_WARN(
            "hdSilk material '%s' has no supported surface terminal; expected "
            "UsdPreviewSurface or a supported MaterialX surface shader.",
            record.path.c_str());
        _FinishRecord(networkMap, record);
        return record;
    }
    record.surfaceKind = OPENUSD_SILK_SURFACE_PREVIEW_SURFACE;

    if (const _SurfaceProjection* projection =
            _FindSurfaceProjection(surface->identifier))
    {
        record.surfaceKind = OPENUSD_SILK_SURFACE_MATERIALX_PROJECTED;
        _DiagnoseUnsupportedInputs(network, *surface, record.path, *projection);
        for (const _ProjectedInput& projected : projection->inputs)
        {
            const _InputBinding& input = projected.binding;
            const HdMaterialRelationship* connection =
                _FindInputConnection(network, surface->path, input.name);
            const HdMaterialNode* upstream = connection == nullptr
                ? nullptr
                : _FindNode(network, connection->inputId);
            if (connection != nullptr && upstream == nullptr)
            {
                // The input is connected to a node the network does not carry.
                // Falling through would publish the authored value Hydra leaves
                // in `parameters` behind the connection, which is the value the
                // author replaced.
                TF_WARN(
                    "hdSilk material '%s' MaterialX input '%s' is connected to a "
                    "node the network does not contain; leaving the input at its "
                    "default.",
                    record.path.c_str(),
                    input.name.GetText());
                continue;
            }
            const auto authored = surface->parameters.find(input.name);
            if (upstream == nullptr && authored == surface->parameters.end())
            {
                // Neither authored nor connected: the material states nothing
                // about this input, so the renderer's own default stands and no
                // weight can turn that silence into a published value.
                continue;
            }

            float weight = 1.0f;
            const _WeightState weightState = _ResolveProjectedWeight(
                network, *surface, record.path, projected, &weight);
            if (weightState == _WeightState::Unsupported)
            {
                continue;
            }
            if (weightState == _WeightState::Zero)
            {
                // The nodedef multiplies this input by zero, so the lobe is off
                // whatever the input carries. Publishing the zero is exact and
                // is what keeps an authored emission colour from lighting up a
                // material whose nodedef default emits nothing.
                HdSilkMaterialScalar scalar;
                scalar.parameter = input.parameter;
                scalar.componentCount = input.componentCount;
                for (uint32_t index = 0; index < scalar.componentCount; ++index)
                {
                    scalar.value[index] = 0.0f;
                }
                record.scalars.push_back(scalar);
                continue;
            }

            if (projected.monochromeColor && upstream != nullptr)
            {
                // The nodedef types this input as a colour while the wire binds
                // one channel. A constant can be checked for agreement; an image
                // or a graph cannot, and reading one of its channels would render
                // a picture the author never authored.
                TF_WARN(
                    "hdSilk material '%s' MaterialX input '%s' is a colour the "
                    "graph connects, and hdSilk binds one opacity channel; only a "
                    "constant whose channels agree projects, so the input is left "
                    "at its default.",
                    record.path.c_str(),
                    input.name.GetText());
                continue;
            }
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
                bool diagnosed = false;
                if (texture != nullptr &&
                    _TryCreateTextureEntry(
                        network,
                        record.path,
                        *texture,
                        outputName,
                        input,
                        textureEntry,
                        &diagnosed))
                {
                    if (weightState == _WeightState::Scaled &&
                        !_ScaleTextureEntryByWeight(
                            textureEntry, weight, record.path, projected))
                    {
                        continue;
                    }
                    record.textures.push_back(std::move(textureEntry));
                    continue;
                }
                if (diagnosed)
                {
                    // The image itself is understood and the refusal has already
                    // been reported against the real cause, so naming the image
                    // node again as "unsupported" would misdirect the reader.
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
                        scalar.value[index] = values[index] * weight;
                    }
                    record.scalars.push_back(scalar);
                    continue;
                }

                // A chain of constant arithmetic over exactly one image is affine
                // in the sampled value, so it folds into the texture entry's own
                // scale and bias instead of needing a per-pixel shader path. The
                // normal input is excluded on purpose: scaling a tangent-space
                // normal is not the colour operation this fold models.
                if (input.name != _tokens->normal)
                {
                    _AffineOverImage affine;
                    if (_TryFoldAffineOverImage(
                            network,
                            *upstream,
                            connection->inputName,
                            input.componentCount,
                            0,
                            &affine) &&
                        affine.image != nullptr)
                    {
                        HdSilkMaterialTexture folded;
                        bool foldDiagnosed = false;
                        if (_TryCreateTextureEntry(
                                network,
                                record.path,
                                *affine.image,
                                affine.outputName,
                                input,
                                folded,
                                &foldDiagnosed))
                        {
                            _ComposeTextureAffine(folded, affine);
                            if (!_AffineKeepsUnitRange(folded, input.componentCount))
                            {
                                TF_WARN(
                                    "hdSilk material '%s' MaterialX input '%s' folds to a "
                                    "texture scale/bias that leaves the unit range, which "
                                    "the eight-bit upload path would clamp; leaving the "
                                    "input at its default.",
                                    record.path.c_str(),
                                    input.name.GetText());
                                continue;
                            }
                            if (weightState == _WeightState::Scaled &&
                                !_ScaleTextureEntryByWeight(
                                    folded, weight, record.path, projected))
                            {
                                continue;
                            }
                            record.textures.push_back(std::move(folded));
                            continue;
                        }
                        if (foldDiagnosed)
                        {
                            continue;
                        }
                    }
                    else
                    {
                        // Two images joined by one constant operator is the one
                        // shape no per-texel scale and bias can carry, because the
                        // result is not affine in either image. The renderer binds
                        // the second image and evaluates the operator per pixel.
                        _TwoImageComposite composite;
                        if (_TryFoldTwoImageComposite(
                                network, *upstream, input.componentCount, &composite))
                        {
                            if (weightState == _WeightState::Scaled)
                            {
                                // Scaling both branches would square a multiply
                                // and scaling one would change an add, so there
                                // is no single branch this weight belongs to.
                                TF_WARN(
                                    "hdSilk material '%s' MaterialX input '%s' combines "
                                    "two images and is scaled by a non-unit weight, which "
                                    "no per-texel scale of one branch carries; leaving the "
                                    "input at its default.",
                                    record.path.c_str(),
                                    input.name.GetText());
                                continue;
                            }
                            _TryPublishCompositeEntries(
                                network, input, composite, record);
                            continue;
                        }
                        if (!composite.unsupportedReason.empty())
                        {
                            TF_WARN(
                                "hdSilk material '%s' MaterialX input '%s' combines "
                                "two images in a way this projection cannot bind "
                                "because %s; leaving the input at its default.",
                                record.path.c_str(),
                                input.name.GetText(),
                                composite.unsupportedReason.c_str());
                            continue;
                        }
                        if (!affine.unsupportedReason.empty())
                        {
                            TF_WARN(
                                "hdSilk material '%s' MaterialX input '%s' is driven by "
                                "arithmetic over an image that this projection cannot "
                                "fold because %s; leaving the input at its default.",
                                record.path.c_str(),
                                input.name.GetText(),
                                affine.unsupportedReason.c_str());
                            continue;
                        }
                    }
                }

                TF_WARN(
                    "hdSilk material '%s' MaterialX input '%s' is connected to "
                    "unsupported node '%s'; leaving the input at its default.",
                    record.path.c_str(),
                    input.name.GetText(),
                    upstream->identifier.GetText());
                continue;
            }

            // The input is stated as a constant: the connection cases above have
            // all returned, so the authored value is the one the graph asks for.
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
            if (projected.monochromeColor &&
                !_CollapseMonochromeConstant(
                    values, count, record.path, input.name))
            {
                continue;
            }
            _BroadcastFloats(values, count, input.componentCount);
            HdSilkMaterialScalar scalar;
            scalar.parameter = input.parameter;
            scalar.componentCount = input.componentCount;
            for (uint32_t index = 0; index < scalar.componentCount; ++index)
            {
                scalar.value[index] = values[index] * weight;
            }
            record.scalars.push_back(scalar);
        }
        _FinishRecord(networkMap, record);
        return record;
    }

    if (surface->identifier == _tokens->MtlxSurfaceUnlit)
    {
        // ND_surface_unlit is unlit by authored intent, and that is a property of
        // the *material*, not of whether this build could generate a fragment for
        // it. Downgrading a generation failure to OPENUSD_SILK_SURFACE_UNSUPPORTED
        // used to hand the prim to the shaded fallback, which lit an unlit surface
        // -- with direct lights, and with the prefiltered environment -- and so
        // turned a missing shader into a wrong image rather than a missing one.
        //
        // The kind is therefore preserved and the payload is left empty. The
        // consumer draws the unlit placeholder it already draws while generation
        // is pending, which is the authored appearance minus the generated
        // fragment's own colour, and the failure is reported on its own rather
        // than by mutating the surface kind.
        record.surfaceKind = OPENUSD_SILK_SURFACE_MATERIALX_GENERATED;
        HdSilkMaterialXVulkanShader shader =
            HdSilkGenerateMaterialXVulkanFragment(id, networkMap);
        if (_forceGeneratedSurfaceFailure.load())
        {
            shader.success = false;
            shader.fragmentSpirv.clear();
            shader.fragmentMslSource.clear();
            shader.error = "forced by a test hook";
        }
        if (!shader.success)
        {
            TF_WARN(
                "hdSilk material '%s' MaterialX Vulkan generation failed: %s "
                "The surface stays unlit, which is what ND_surface_unlit "
                "authors; it is drawn through the unlit placeholder without a "
                "generated fragment.",
                record.path.c_str(),
                shader.error.c_str());
            ++_generatedSurfaceFailureCount;
            _FinishRecord(networkMap, record);
            return record;
        }
        record.generatedFragmentSpirv = shader.fragmentSpirv;
        record.generatedFragmentMslSource = shader.fragmentMslSource;
        _FinishRecord(networkMap, record);
        return record;
    }

    for (const _InputBinding& input : _PreviewSurfaceInputs())
    {
        // A connected input is described by the texture table; its authored
        // constant, if any, is only a fallback the texture entry already
        // carries, so it must not also appear as a scalar.
        const HdMaterialNode* texture = nullptr;
        TfToken outputName;
        bool danglingConnection = false;
        for (const HdMaterialRelationship& relationship : network.relationships)
        {
            if (relationship.outputId != surface->path ||
                relationship.outputName != input.name)
            {
                continue;
            }
            const HdMaterialNode* upstream =
                _FindNode(network, relationship.inputId);
            danglingConnection = upstream == nullptr;
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

        if (danglingConnection)
        {
            // A connection to a node the network does not carry is still a
            // connection, so the authored value Hydra leaves in `parameters`
            // behind it is not what the graph asks to be rendered.
            TF_WARN(
                "hdSilk material '%s' input '%s' is connected to a node the "
                "network does not contain; leaving the input at its default.",
                record.path.c_str(),
                input.name.GetText());
            continue;
        }

        if (texture != nullptr)
        {
            // Built by the shared helper rather than inline. The duplicate that
            // used to live here drifted: it read scale, bias and fallback without
            // checking whether the author had replaced them with a connection,
            // which published a constant the graph does not ask for.
            HdSilkMaterialTexture entry;
            bool diagnosed = false;
            if (_TryCreateTextureEntry(
                    network, record.path, *texture, outputName, input, entry, &diagnosed))
            {
                record.textures.push_back(std::move(entry));
            }
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

    _FinishRecord(networkMap, record);
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

uint64_t
HdSilkMaterial::GetGeneratedSurfaceFailureCountForTesting()
{
    return _generatedSurfaceFailureCount.load();
}

void
HdSilkMaterial::SetGeneratedSurfaceFailureForTesting(bool fail)
{
    _forceGeneratedSurfaceFailure.store(fail);
}

PXR_NAMESPACE_CLOSE_SCOPE

#if defined(OPENUSD_HDSILK_ENABLE_TEST_HOOKS)
extern "C" OPENUSD_HDSILK_API uint64_t
openusd_hdsilk_test_get_generated_surface_failure_count(void)
{
    return PXR_NS::HdSilkMaterial::GetGeneratedSurfaceFailureCountForTesting();
}

extern "C" OPENUSD_HDSILK_API void
openusd_hdsilk_test_set_generated_surface_failure(int32_t fail)
{
    PXR_NS::HdSilkMaterial::SetGeneratedSurfaceFailureForTesting(fail != 0);
}
#endif
