// Copyright (c) marcschier. Licensed under the MIT License.
//
// The optional MDL material adapter behind the project-owned openusd_mdl C
// ABI. It distils the authored USD input values of an accepted Omniverse MDL
// material into the renderer-neutral, UsdPreviewSurface-compatible record
// hdSilk publishes. It links no MDL SDK, opens no .mdl module, and evaluates
// no MDL expression; see ../README.md for exactly what that excludes.

#include "openusd_mdl.h"

#include "sdk_backend.h"

#include <algorithm>
#include <cctype>
#include <cstring>
#include <map>
#include <memory>
#include <mutex>
#include <new>
#include <string>
#include <vector>

namespace
{
constexpr float kUnauthored = -1.0F;

/// True only for a path the platform resolves without consulting any working
/// directory. A Windows drive-relative path such as "C:modules" is deliberately
/// not absolute: it resolves against that drive's own working directory.
bool
IsAbsolutePath(const std::string& path)
{
#if defined(_WIN32)
    if (path.size() >= 3 && path[1] == ':' && (path[2] == '\\' || path[2] == '/'))
    {
        return true;
    }
    return path.size() >= 2 && (path[0] == '\\' || path[0] == '/') &&
        (path[1] == '\\' || path[1] == '/');
#else
    return !path.empty() && path[0] == '/';
#endif
}

std::string
ToLowerAscii(const std::string& value)
{
    std::string lowered(value);
    for (char& character : lowered)
    {
        character = static_cast<char>(
            std::tolower(static_cast<unsigned char>(character)));
    }
    return lowered;
}

std::string
ToString(const openusd_mdl_string& value)
{
    if (value.data == nullptr || value.size == 0)
    {
        return std::string();
    }
    return std::string(value.data, value.size);
}

/// Reduces an authored `info:mdl:sourceAsset` to the module file name, lower
/// cased. Omniverse stages author the module as a bare `OmniPBR.mdl`, as a
/// search-path relative `Base/OmniPBR.mdl`, or as an absolute or omniverse://
/// URI, and all three name the same module. Matching on the file name alone is
/// what makes the accepted set independent of where the module was resolved
/// from -- which matters because this adapter never resolves it.
std::string
ModuleFileName(const std::string& moduleUri)
{
    const size_t separator = moduleUri.find_last_of("/\\");
    const std::string fileName = separator == std::string::npos
        ? moduleUri
        : moduleUri.substr(separator + 1);
    return ToLowerAscii(fileName);
}

/// The accepted (module, material) pairs. Each is narrow on purpose: a module
/// this adapter has not documented a mapping for is refused by name rather
/// than distilled by guesswork.
enum class AcceptedMaterial
{
    None,
    OmniPbr,
    OmniSurface,
    OmniGlass
};

AcceptedMaterial
ClassifyMaterial(const std::string& moduleUri, const std::string& materialName)
{
    const std::string module = ModuleFileName(moduleUri);
    const std::string material = ToLowerAscii(materialName);
    if (module == "omnipbr.mdl" && (material.empty() || material == "omnipbr"))
    {
        return AcceptedMaterial::OmniPbr;
    }
    if (module == "omnisurface.mdl" &&
        (material.empty() || material == "omnisurface"))
    {
        return AcceptedMaterial::OmniSurface;
    }
    if (module == "omniglass.mdl" && (material.empty() || material == "omniglass"))
    {
        return AcceptedMaterial::OmniGlass;
    }
    return AcceptedMaterial::None;
}

bool
IsAcceptedModule(const std::string& moduleUri)
{
    const std::string module = ModuleFileName(moduleUri);
    return module == "omnipbr.mdl" || module == "omnisurface.mdl" ||
        module == "omniglass.mdl";
}

/// The authored parameter table of one material, indexed by name.
///
/// A second, lower-priority layer holds the parameter defaults an MDL SDK
/// backend read out of the compiled module. The authored layer always wins:
/// what the stage says overrides what the module defaults to, which is what
/// keeps the SDK-backed adapter's answers a superset of the authored-value
/// adapter's rather than a different set.
class ParameterTable
{
public:
    ParameterTable(
        const openusd_mdl_parameter* parameters,
        uint32_t parameterCount)
    {
        _entries.reserve(parameterCount);
        for (uint32_t index = 0; index < parameterCount; ++index)
        {
            _entries.push_back(&parameters[index]);
            _consumed.push_back(false);
        }
    }

    /// Adds the module-default layer. The strings behind each parameter are
    /// owned by `storage`, which must outlive this table.
    void SetModuleDefaults(
        const std::map<std::string, openusd_mdl::SdkParameterValue>* defaults)
    {
        _moduleDefaults = defaults;
    }

    /// OPENUSD_MDL_ORIGIN_* for the entry the last successful Find returned.
    uint32_t LastOrigin() const { return _lastOrigin; }

    const openusd_mdl_parameter* Find(const char* name)
    {
        const size_t nameLength = std::strlen(name);
        for (size_t index = 0; index < _entries.size(); ++index)
        {
            const openusd_mdl_parameter* entry = _entries[index];
            if (entry->name.size != nameLength || entry->name.data == nullptr)
            {
                continue;
            }
            if (std::memcmp(entry->name.data, name, nameLength) == 0)
            {
                _consumed[index] = true;
                _lastOrigin = OPENUSD_MDL_ORIGIN_AUTHORED;
                return entry;
            }
        }
        return FindModuleDefault(name);
    }

    /// Names the authored parameters no distillation rule looked at.
    ///
    /// `colorSpace:<input>` entries are excluded: they are the caller's
    /// transport for a texture input's authored colour-space metadata, not an
    /// MDL input of their own, so reporting one would name something the stage
    /// never authored as an input.
    std::vector<std::string> Unconsumed() const
    {
        std::vector<std::string> names;
        for (size_t index = 0; index < _entries.size(); ++index)
        {
            if (_consumed[index])
            {
                continue;
            }
            std::string name = ToString(_entries[index]->name);
            if (name.rfind("colorSpace:", 0) == 0)
            {
                continue;
            }
            names.push_back(std::move(name));
        }
        return names;
    }

private:
    const openusd_mdl_parameter* FindModuleDefault(const char* name)
    {
        if (_moduleDefaults == nullptr)
        {
            return nullptr;
        }
        const auto entry = _moduleDefaults->find(std::string(name));
        if (entry == _moduleDefaults->end())
        {
            return nullptr;
        }
        // Materialized into a stable slot so the returned pointer outlives this
        // call the way an authored entry's does.
        _materialized.push_back(std::make_unique<Materialized>());
        Materialized& slot = *_materialized.back();
        slot.name = entry->first;
        slot.text = entry->second.text;
        slot.parameter.name.data = slot.name.c_str();
        slot.parameter.name.size = static_cast<uint32_t>(slot.name.size());
        slot.parameter.kind = entry->second.kind;
        slot.parameter.component_count = entry->second.componentCount;
        for (size_t index = 0; index < 4; ++index)
        {
            slot.parameter.value[index] = entry->second.value[index];
        }
        slot.parameter.integer_value = entry->second.integerValue;
        slot.parameter.text.data = slot.text.c_str();
        slot.parameter.text.size = static_cast<uint32_t>(slot.text.size());
        _lastOrigin = entry->second.origin;
        return &slot.parameter;
    }

    struct Materialized
    {
        std::string name;
        std::string text;
        openusd_mdl_parameter parameter{};
    };

    std::vector<const openusd_mdl_parameter*> _entries;
    std::vector<bool> _consumed;
    const std::map<std::string, openusd_mdl::SdkParameterValue>* _moduleDefaults =
        nullptr;
    std::vector<std::unique_ptr<Materialized>> _materialized;
    uint32_t _lastOrigin = OPENUSD_MDL_ORIGIN_AUTHORED;
};

bool
ReadFloat(const openusd_mdl_parameter* parameter, float* out)
{
    if (parameter == nullptr || out == nullptr)
    {
        return false;
    }
    if (parameter->kind == OPENUSD_MDL_VALUE_FLOAT)
    {
        *out = parameter->value[0];
        return true;
    }
    if (parameter->kind == OPENUSD_MDL_VALUE_INT)
    {
        *out = static_cast<float>(parameter->integer_value);
        return true;
    }
    if (parameter->kind == OPENUSD_MDL_VALUE_BOOL)
    {
        *out = parameter->integer_value != 0 ? 1.0F : 0.0F;
        return true;
    }
    return false;
}

bool
ReadBool(const openusd_mdl_parameter* parameter, bool* out)
{
    if (parameter == nullptr || out == nullptr)
    {
        return false;
    }
    if (parameter->kind == OPENUSD_MDL_VALUE_BOOL ||
        parameter->kind == OPENUSD_MDL_VALUE_INT)
    {
        *out = parameter->integer_value != 0;
        return true;
    }
    if (parameter->kind == OPENUSD_MDL_VALUE_FLOAT)
    {
        *out = parameter->value[0] != 0.0F;
        return true;
    }
    return false;
}

bool
ReadColor(const openusd_mdl_parameter* parameter, float (&out)[3])
{
    if (parameter == nullptr)
    {
        return false;
    }
    if (parameter->kind == OPENUSD_MDL_VALUE_FLOAT3 ||
        parameter->kind == OPENUSD_MDL_VALUE_FLOAT4)
    {
        out[0] = parameter->value[0];
        out[1] = parameter->value[1];
        out[2] = parameter->value[2];
        return true;
    }
    if (parameter->kind == OPENUSD_MDL_VALUE_FLOAT)
    {
        out[0] = parameter->value[0];
        out[1] = parameter->value[0];
        out[2] = parameter->value[0];
        return true;
    }
    return false;
}

bool
ReadAsset(const openusd_mdl_parameter* parameter, std::string* out)
{
    if (parameter == nullptr || out == nullptr)
    {
        return false;
    }
    if (parameter->kind != OPENUSD_MDL_VALUE_ASSET &&
        parameter->kind != OPENUSD_MDL_VALUE_STRING)
    {
        return false;
    }
    *out = ToString(parameter->text);
    return !out->empty();
}

/// The mutable form of a distilled material, built before it is frozen into
/// the pointer-stable ABI structs.
struct DistillationResult
{
    std::vector<openusd_mdl_distilled_scalar> scalars;
    std::vector<openusd_mdl_distilled_texture> textures;
    std::vector<std::string> textureAssets;
    std::vector<std::string> unsupported;
    std::string diagnostic;
    uint32_t status = OPENUSD_MDL_STATUS_OK;
    /// Where the value the next Add* call publishes came from. Each Try* helper
    /// sets it from the layer that answered, and every Add* call in this file
    /// immediately follows its Try*, so the pairing is local and auditable.
    uint32_t currentOrigin = OPENUSD_MDL_ORIGIN_AUTHORED;

    void AddScalar(uint32_t input, uint32_t componentCount, const float* values)
    {
        openusd_mdl_distilled_scalar scalar{};
        scalar.surface_input = input;
        scalar.component_count = componentCount;
        scalar.origin = currentOrigin;
        for (uint32_t index = 0; index < componentCount && index < 4; ++index)
        {
            scalar.value[index] = values[index];
        }
        scalars.push_back(scalar);
    }

    void AddScalar(uint32_t input, float value)
    {
        AddScalar(input, 1, &value);
    }

    void AddTexture(
        uint32_t input,
        uint32_t componentCount,
        uint32_t channel,
        uint32_t colorSpace,
        const std::string& asset)
    {
        openusd_mdl_distilled_texture texture{};
        texture.surface_input = input;
        texture.component_count = componentCount;
        texture.output_channel = channel;
        texture.wrap_s = OPENUSD_MDL_WRAP_REPEAT;
        texture.wrap_t = OPENUSD_MDL_WRAP_REPEAT;
        texture.color_space = colorSpace;
        texture.origin = currentOrigin;
        for (uint32_t index = 0; index < 4; ++index)
        {
            texture.scale[index] = 1.0F;
            texture.bias[index] = 0.0F;
        }
        textures.push_back(texture);
        textureAssets.push_back(asset);
    }
};

/// Reads one authored parameter of the expected kind. A parameter that is
/// present but holds a value this ABI cannot express is reported by name
/// instead of being dropped, because a dropped input renders as the
/// consumer's default and looks like a value the author never wrote.
bool
TryFloat(
    ParameterTable& parameters,
    const char* name,
    DistillationResult& result,
    float* out)
{
    const openusd_mdl_parameter* parameter = parameters.Find(name);
    if (parameter == nullptr)
    {
        return false;
    }
    if (ReadFloat(parameter, out))
    {
        result.currentOrigin = parameters.LastOrigin();
        return true;
    }
    result.unsupported.emplace_back(name);
    return false;
}

bool
TryBool(
    ParameterTable& parameters,
    const char* name,
    DistillationResult& result,
    bool* out)
{
    const openusd_mdl_parameter* parameter = parameters.Find(name);
    if (parameter == nullptr)
    {
        return false;
    }
    if (ReadBool(parameter, out))
    {
        result.currentOrigin = parameters.LastOrigin();
        return true;
    }
    result.unsupported.emplace_back(name);
    return false;
}

bool
TryColor(
    ParameterTable& parameters,
    const char* name,
    DistillationResult& result,
    float (&out)[3])
{
    const openusd_mdl_parameter* parameter = parameters.Find(name);
    if (parameter == nullptr)
    {
        return false;
    }
    if (ReadColor(parameter, out))
    {
        result.currentOrigin = parameters.LastOrigin();
        return true;
    }
    result.unsupported.emplace_back(name);
    return false;
}

bool
TryAsset(
    ParameterTable& parameters,
    const char* name,
    DistillationResult& result,
    std::string* out)
{
    const openusd_mdl_parameter* parameter = parameters.Find(name);
    if (parameter == nullptr)
    {
        return false;
    }
    if (ReadAsset(parameter, out))
    {
        result.currentOrigin = parameters.LastOrigin();
        return true;
    }
    result.unsupported.emplace_back(name);
    return false;
}

/// Resolves the colour space a distilled texture must be decoded in.
///
/// Hydra's scene-index adapter carries a texture input's authored `colorSpace`
/// metadata as a sibling parameter named `colorSpace:<input>`. An authored
/// value wins over the per-input default below, because the author stating
/// "raw" on a colour texture is a statement about the file, not a mistake.
uint32_t
ResolveColorSpace(
    ParameterTable& parameters,
    const std::string& textureInput,
    uint32_t fallback)
{
    const std::string key = "colorSpace:" + textureInput;
    const openusd_mdl_parameter* parameter = parameters.Find(key.c_str());
    if (parameter == nullptr)
    {
        return fallback;
    }
    const std::string value = ToLowerAscii(ToString(parameter->text));
    if (value == "raw")
    {
        return OPENUSD_MDL_COLOR_SPACE_RAW;
    }
    if (value == "srgb")
    {
        return OPENUSD_MDL_COLOR_SPACE_SRGB;
    }
    if (value == "auto")
    {
        return OPENUSD_MDL_COLOR_SPACE_AUTO;
    }
    return fallback;
}

/// Publishes a texture only when its authored influence is exactly on. OmniPBR
/// blends a texture against its constant by `*_texture_influence`; hdSilk binds
/// one source per surface input and cannot blend, so a partial influence is
/// reported rather than rendered as if it were full.
enum class TextureInfluence
{
    Off,
    Full,
    Partial
};

TextureInfluence
ClassifyInfluence(
    ParameterTable& parameters,
    const char* influenceName,
    DistillationResult& result)
{
    float influence = kUnauthored;
    if (!TryFloat(parameters, influenceName, result, &influence))
    {
        // OmniPBR defaults every *_texture_influence to 1.0, so an unauthored
        // influence means the authored texture drives the input outright.
        return TextureInfluence::Full;
    }
    if (influence <= 0.0F)
    {
        return TextureInfluence::Off;
    }
    if (influence >= 1.0F)
    {
        return TextureInfluence::Full;
    }
    return TextureInfluence::Partial;
}

void
DistillOmniPbr(ParameterTable& parameters, DistillationResult& result)
{
    float color[3] = {0.0F, 0.0F, 0.0F};
    if (TryColor(parameters, "diffuse_color_constant", result, color))
    {
        result.AddScalar(OPENUSD_MDL_SURFACE_DIFFUSE_COLOR, 3, color);
    }

    std::string asset;
    if (TryAsset(parameters, "diffuse_texture", result, &asset))
    {
        result.AddTexture(
            OPENUSD_MDL_SURFACE_DIFFUSE_COLOR,
            3,
            OPENUSD_MDL_CHANNEL_RGB,
            ResolveColorSpace(
                parameters, "diffuse_texture", OPENUSD_MDL_COLOR_SPACE_SRGB),
            asset);
    }

    float roughness = 0.0F;
    if (TryFloat(parameters, "reflection_roughness_constant", result, &roughness))
    {
        result.AddScalar(OPENUSD_MDL_SURFACE_ROUGHNESS, roughness);
    }
    if (TryAsset(parameters, "reflectionroughness_texture", result, &asset))
    {
        const TextureInfluence influence = ClassifyInfluence(
            parameters, "reflection_roughness_texture_influence", result);
        if (influence == TextureInfluence::Full)
        {
            result.AddTexture(
                OPENUSD_MDL_SURFACE_ROUGHNESS,
                1,
                OPENUSD_MDL_CHANNEL_R,
                ResolveColorSpace(
                    parameters,
                    "reflectionroughness_texture",
                    OPENUSD_MDL_COLOR_SPACE_RAW),
                asset);
        }
        else
        {
            result.unsupported.emplace_back("reflectionroughness_texture");
        }
    }

    float metallic = 0.0F;
    if (TryFloat(parameters, "metallic_constant", result, &metallic))
    {
        result.AddScalar(OPENUSD_MDL_SURFACE_METALLIC, metallic);
    }
    if (TryAsset(parameters, "metallic_texture", result, &asset))
    {
        const TextureInfluence influence =
            ClassifyInfluence(parameters, "metallic_texture_influence", result);
        if (influence == TextureInfluence::Full)
        {
            result.AddTexture(
                OPENUSD_MDL_SURFACE_METALLIC,
                1,
                OPENUSD_MDL_CHANNEL_R,
                ResolveColorSpace(
                    parameters, "metallic_texture", OPENUSD_MDL_COLOR_SPACE_RAW),
                asset);
        }
        else
        {
            result.unsupported.emplace_back("metallic_texture");
        }
    }

    // The ORM convention packs occlusion, roughness and metallic into one
    // texture's R, G and B. hdSilk binds a channel per surface input, so the
    // three inputs read the same asset through three entries rather than
    // needing a packed-texture concept on the wire.
    bool ormEnabled = false;
    const bool ormAuthored =
        TryBool(parameters, "enable_ORM_texture", result, &ormEnabled);
    if (TryAsset(parameters, "ORM_texture", result, &asset))
    {
        if (ormAuthored && ormEnabled)
        {
            const uint32_t ormColorSpace = ResolveColorSpace(
                parameters, "ORM_texture", OPENUSD_MDL_COLOR_SPACE_RAW);
            result.AddTexture(
                OPENUSD_MDL_SURFACE_OCCLUSION,
                1,
                OPENUSD_MDL_CHANNEL_R,
                ormColorSpace,
                asset);
            result.AddTexture(
                OPENUSD_MDL_SURFACE_ROUGHNESS,
                1,
                OPENUSD_MDL_CHANNEL_G,
                ormColorSpace,
                asset);
            result.AddTexture(
                OPENUSD_MDL_SURFACE_METALLIC,
                1,
                OPENUSD_MDL_CHANNEL_B,
                ormColorSpace,
                asset);
        }
        else
        {
            result.unsupported.emplace_back("ORM_texture");
        }
    }

    bool opacityEnabled = false;
    const bool opacityAuthored =
        TryBool(parameters, "enable_opacity", result, &opacityEnabled);
    float opacity = 0.0F;
    const bool opacityConstantAuthored =
        TryFloat(parameters, "opacity_constant", result, &opacity);
    const bool opacityTextureAuthored =
        TryAsset(parameters, "opacity_texture", result, &asset);
    bool opacityTextureEnabled = false;
    const bool opacityTextureGateAuthored = TryBool(
        parameters, "enable_opacity_texture", result, &opacityTextureEnabled);
    if (opacityAuthored && opacityEnabled)
    {
        if (opacityConstantAuthored)
        {
            result.AddScalar(OPENUSD_MDL_SURFACE_OPACITY, opacity);
        }
        if (opacityTextureAuthored && opacityTextureGateAuthored &&
            opacityTextureEnabled)
        {
            result.AddTexture(
                OPENUSD_MDL_SURFACE_OPACITY,
                1,
                OPENUSD_MDL_CHANNEL_R,
                ResolveColorSpace(
                    parameters, "opacity_texture", OPENUSD_MDL_COLOR_SPACE_RAW),
                asset);
        }
        else if (opacityTextureAuthored)
        {
            result.unsupported.emplace_back("opacity_texture");
        }
        float threshold = 0.0F;
        if (TryFloat(parameters, "opacity_threshold", result, &threshold))
        {
            result.AddScalar(OPENUSD_MDL_SURFACE_OPACITY_THRESHOLD, threshold);
        }
    }
    else
    {
        // OmniPBR ignores every opacity input while enable_opacity is off.
        // Publishing the authored constant anyway would make an opaque
        // material translucent, so the inputs are consumed and reported.
        if (opacityConstantAuthored)
        {
            result.unsupported.emplace_back("opacity_constant");
        }
        if (opacityTextureAuthored)
        {
            result.unsupported.emplace_back("opacity_texture");
        }
        float threshold = 0.0F;
        if (TryFloat(parameters, "opacity_threshold", result, &threshold))
        {
            result.unsupported.emplace_back("opacity_threshold");
        }
    }

    bool emissionEnabled = false;
    const bool emissionAuthored =
        TryBool(parameters, "enable_emission", result, &emissionEnabled);
    float emissive[3] = {0.0F, 0.0F, 0.0F};
    const bool emissiveAuthored =
        TryColor(parameters, "emissive_color", result, emissive);
    const bool emissiveTextureAuthored =
        TryAsset(parameters, "emissive_color_texture", result, &asset);
    float intensity = 1.0F;
    const bool intensityAuthored =
        TryFloat(parameters, "emissive_intensity", result, &intensity);
    if (emissionAuthored && emissionEnabled)
    {
        if (emissiveAuthored)
        {
            result.AddScalar(OPENUSD_MDL_SURFACE_EMISSIVE_COLOR, 3, emissive);
        }
        if (emissiveTextureAuthored)
        {
            result.AddTexture(
                OPENUSD_MDL_SURFACE_EMISSIVE_COLOR,
                3,
                OPENUSD_MDL_CHANNEL_RGB,
                ResolveColorSpace(
                    parameters,
                    "emissive_color_texture",
                    OPENUSD_MDL_COLOR_SPACE_SRGB),
                asset);
        }
        // emissive_intensity is a photometric multiplier on the emitted
        // radiance. UsdPreviewSurface's emissiveColor carries no unit, so a
        // multiplier other than one has no faithful destination here and is
        // reported instead of being folded into the colour.
        if (intensityAuthored && intensity != 1.0F)
        {
            result.unsupported.emplace_back("emissive_intensity");
        }
    }
    else
    {
        if (emissiveAuthored)
        {
            result.unsupported.emplace_back("emissive_color");
        }
        if (emissiveTextureAuthored)
        {
            result.unsupported.emplace_back("emissive_color_texture");
        }
    }

    if (TryAsset(parameters, "normalmap_texture", result, &asset))
    {
        result.AddTexture(
            OPENUSD_MDL_SURFACE_NORMAL,
            3,
            OPENUSD_MDL_CHANNEL_RGB,
            ResolveColorSpace(
                parameters, "normalmap_texture", OPENUSD_MDL_COLOR_SPACE_RAW),
            asset);
    }
}

void
DistillOmniSurface(ParameterTable& parameters, DistillationResult& result)
{
    float color[3] = {0.0F, 0.0F, 0.0F};
    const bool colorAuthored =
        TryColor(parameters, "diffuse_reflection_color", result, color);
    float weight = 1.0F;
    const bool weightAuthored =
        TryFloat(parameters, "diffuse_reflection_weight", result, &weight);
    if (colorAuthored || weightAuthored)
    {
        // OmniSurface multiplies the base colour by the base weight before it
        // reaches the diffuse lobe, so the product is the exact value a
        // PreviewSurface diffuseColor must carry. An unauthored side of the
        // product uses the module's documented default of 1.0 for the weight
        // and, when only the weight is authored, the module's white base.
        float base[3] = {
            colorAuthored ? color[0] : 1.0F,
            colorAuthored ? color[1] : 1.0F,
            colorAuthored ? color[2] : 1.0F};
        const float scale = weightAuthored ? weight : 1.0F;
        base[0] *= scale;
        base[1] *= scale;
        base[2] *= scale;
        result.AddScalar(OPENUSD_MDL_SURFACE_DIFFUSE_COLOR, 3, base);
    }

    float metalness = 0.0F;
    if (TryFloat(parameters, "metalness", result, &metalness))
    {
        result.AddScalar(OPENUSD_MDL_SURFACE_METALLIC, metalness);
    }
    float roughness = 0.0F;
    if (TryFloat(parameters, "specular_reflection_roughness", result, &roughness))
    {
        result.AddScalar(OPENUSD_MDL_SURFACE_ROUGHNESS, roughness);
    }
    float ior = 0.0F;
    if (TryFloat(parameters, "specular_reflection_ior", result, &ior))
    {
        result.AddScalar(OPENUSD_MDL_SURFACE_IOR, ior);
    }

    bool emissionEnabled = false;
    const bool emissionAuthored =
        TryBool(parameters, "enable_emission", result, &emissionEnabled);
    float emissive[3] = {0.0F, 0.0F, 0.0F};
    const bool emissiveAuthored =
        TryColor(parameters, "emission_color", result, emissive);
    float intensity = 1.0F;
    const bool intensityAuthored =
        TryFloat(parameters, "emission_intensity", result, &intensity);
    if (emissionAuthored && emissionEnabled && emissiveAuthored)
    {
        result.AddScalar(OPENUSD_MDL_SURFACE_EMISSIVE_COLOR, 3, emissive);
        if (intensityAuthored && intensity != 1.0F)
        {
            result.unsupported.emplace_back("emission_intensity");
        }
    }
    else if (emissiveAuthored)
    {
        result.unsupported.emplace_back("emission_color");
    }

    float opacity = 0.0F;
    if (TryFloat(parameters, "geometry_opacity", result, &opacity))
    {
        result.AddScalar(OPENUSD_MDL_SURFACE_OPACITY, opacity);
    }
    float threshold = 0.0F;
    if (TryFloat(parameters, "geometry_opacity_threshold", result, &threshold))
    {
        result.AddScalar(OPENUSD_MDL_SURFACE_OPACITY_THRESHOLD, threshold);
    }
}

void
DistillOmniGlass(ParameterTable& parameters, DistillationResult& result)
{
    float color[3] = {0.0F, 0.0F, 0.0F};
    if (TryColor(parameters, "glass_color", result, color))
    {
        result.AddScalar(OPENUSD_MDL_SURFACE_DIFFUSE_COLOR, 3, color);
    }
    float ior = 0.0F;
    if (TryFloat(parameters, "glass_ior", result, &ior))
    {
        result.AddScalar(OPENUSD_MDL_SURFACE_IOR, ior);
    }
    float roughness = 0.0F;
    if (TryFloat(parameters, "frosting_roughness", result, &roughness))
    {
        result.AddScalar(OPENUSD_MDL_SURFACE_ROUGHNESS, roughness);
    }

    // OmniGlass is a specular transmission model and UsdPreviewSurface has no
    // transmission input. The interchange convention NVIDIA's own Apache-2.0
    // usd-exchange library writes for the PreviewSurface half of a glass
    // material is a constant opacity of 0.2, so that exact value is what this
    // distillation publishes rather than an opaque surface that would hide
    // whatever is behind the glass.
    const float glassOpacity = 0.2F;
    result.AddScalar(OPENUSD_MDL_SURFACE_OPACITY, glassOpacity);
}
}

/// The frozen, pointer-stable form handed back across the ABI. It owns every
/// byte the caller may read until the matching release call.
struct openusd_mdl_distilled_material_storage
{
    openusd_mdl_distilled_material view{};
    std::vector<openusd_mdl_distilled_scalar> scalars;
    std::vector<openusd_mdl_distilled_texture> textures;
    std::vector<std::string> textureAssets;
    std::vector<std::string> unsupportedNames;
    std::vector<openusd_mdl_string> unsupportedViews;
    std::string diagnostic;
};

struct openusd_mdl_adapter
{
    std::vector<std::unique_ptr<openusd_mdl_distilled_material_storage>> live;
    /// The configuration this instance was created or reconfigured with. Held
    /// per instance rather than globally so two instances can disagree about
    /// search paths without one silently answering from the other's modules.
    std::vector<std::string> searchPaths;
    uint64_t cacheGeneration = 0;
};

namespace
{
/// Validates and copies a caller's options. Every search path must be absolute
/// and within the declared bounds; a relative path is refused rather than
/// resolved against the process working directory, which is the search neither
/// this adapter nor its loader will perform.
uint32_t
ApplyOptions(
    openusd_mdl_adapter* adapter,
    const openusd_mdl_adapter_options* options)
{
    std::vector<std::string> paths;
    uint64_t generation = 0;
    if (options != nullptr)
    {
        if (options->struct_size != sizeof(openusd_mdl_adapter_options))
        {
            return OPENUSD_MDL_STATUS_INVALID_ARGUMENT;
        }
        if (options->module_search_path_count > OPENUSD_MDL_MAX_SEARCH_PATHS)
        {
            return OPENUSD_MDL_STATUS_INVALID_ARGUMENT;
        }
        if (options->module_search_path_count != 0 &&
            options->module_search_paths == nullptr)
        {
            return OPENUSD_MDL_STATUS_INVALID_ARGUMENT;
        }
        for (uint32_t index = 0; index < options->module_search_path_count; ++index)
        {
            const openusd_mdl_string& entry = options->module_search_paths[index];
            if (entry.data == nullptr || entry.size == 0 ||
                entry.size > OPENUSD_MDL_MAX_PATH_BYTES)
            {
                return OPENUSD_MDL_STATUS_INVALID_ARGUMENT;
            }
            std::string path(entry.data, entry.size);
            if (!IsAbsolutePath(path))
            {
                return OPENUSD_MDL_STATUS_INVALID_ARGUMENT;
            }
            paths.push_back(std::move(path));
        }
        generation = options->cache_generation;
    }

    adapter->searchPaths = std::move(paths);
    adapter->cacheGeneration = generation;
    if (openusd_mdl::SdkBackend::IsCompiledIn())
    {
        std::string diagnostic;
        // A backend that cannot configure is not an error here. The adapter
        // still answers from authored values; only module evaluation is lost,
        // and the distill call reports that against the material it affects.
        (void)openusd_mdl::SdkBackend::Instance().Configure(
            adapter->searchPaths, adapter->cacheGeneration, &diagnostic);
    }
    return OPENUSD_MDL_STATUS_OK;
}

const openusd_mdl_distilled_material*
Freeze(openusd_mdl_adapter* adapter, DistillationResult& result)
{
    auto storage = std::make_unique<openusd_mdl_distilled_material_storage>();
    storage->scalars = std::move(result.scalars);
    storage->textures = std::move(result.textures);
    storage->textureAssets = std::move(result.textureAssets);
    storage->unsupportedNames = std::move(result.unsupported);
    storage->diagnostic = std::move(result.diagnostic);

    for (size_t index = 0; index < storage->textures.size(); ++index)
    {
        const std::string& asset = storage->textureAssets[index];
        storage->textures[index].asset.data = asset.c_str();
        storage->textures[index].asset.size =
            static_cast<uint32_t>(asset.size());
    }
    storage->unsupportedViews.reserve(storage->unsupportedNames.size());
    for (const std::string& name : storage->unsupportedNames)
    {
        openusd_mdl_string view{};
        view.data = name.c_str();
        view.size = static_cast<uint32_t>(name.size());
        storage->unsupportedViews.push_back(view);
    }

    storage->view.struct_size =
        static_cast<uint32_t>(sizeof(openusd_mdl_distilled_material));
    storage->view.status = result.status;
    storage->view.diagnostic.data = storage->diagnostic.c_str();
    storage->view.diagnostic.size =
        static_cast<uint32_t>(storage->diagnostic.size());
    storage->view.scalars =
        storage->scalars.empty() ? nullptr : storage->scalars.data();
    storage->view.scalar_count = static_cast<uint32_t>(storage->scalars.size());
    storage->view.textures =
        storage->textures.empty() ? nullptr : storage->textures.data();
    storage->view.texture_count =
        static_cast<uint32_t>(storage->textures.size());
    storage->view.unsupported_parameters = storage->unsupportedViews.empty()
        ? nullptr
        : storage->unsupportedViews.data();
    storage->view.unsupported_parameter_count =
        static_cast<uint32_t>(storage->unsupportedViews.size());

    const openusd_mdl_distilled_material* view = &storage->view;
    adapter->live.push_back(std::move(storage));
    return view;
}
}

extern "C" {

uint32_t
openusd_mdl_abi_version(void)
{
    return OPENUSD_MDL_ABI_VERSION;
}

uint32_t
openusd_mdl_capabilities(void)
{
    uint32_t capabilities = OPENUSD_MDL_CAPABILITY_AUTHORED_SUBSET;
    if (openusd_mdl::SdkBackend::IsCompiledIn())
    {
        capabilities |= OPENUSD_MDL_CAPABILITY_MODULE_DEFAULTS |
            OPENUSD_MDL_CAPABILITY_CONSTANT_EXPRESSIONS |
            OPENUSD_MDL_CAPABILITY_TEXTURE_RESOLUTION;
    }
    return capabilities;
}

uint32_t
openusd_mdl_describe(char* buffer, uint32_t capacity)
{
    std::string description =
        "openusd_mdl authored-value distiller; accepts OmniPBR.mdl, "
        "OmniSurface.mdl and OmniGlass.mdl";
    if (openusd_mdl::SdkBackend::IsCompiledIn())
    {
        description = "openusd_mdl SDK-backed distiller (" +
            openusd_mdl::SdkBackend::Instance().Describe() +
            "); authored values first, then module defaults and constant "
            "expressions";
    }
    else
    {
        description += "; no MDL SDK linked";
    }
    const uint32_t required = static_cast<uint32_t>(description.size() + 1);
    if (buffer != nullptr && capacity >= required)
    {
        std::memcpy(buffer, description.c_str(), required);
    }
    else if (buffer != nullptr && capacity > 0)
    {
        buffer[0] = '\0';
    }
    return required;
}

uint32_t
openusd_mdl_adapter_create(
    const openusd_mdl_adapter_options* options,
    openusd_mdl_adapter** adapter)
{
    if (adapter == nullptr)
    {
        return OPENUSD_MDL_STATUS_INVALID_ARGUMENT;
    }
    *adapter = new (std::nothrow) openusd_mdl_adapter();
    if (*adapter == nullptr)
    {
        return OPENUSD_MDL_STATUS_OUT_OF_MEMORY;
    }
    const uint32_t status = ApplyOptions(*adapter, options);
    if (status != OPENUSD_MDL_STATUS_OK)
    {
        delete *adapter;
        *adapter = nullptr;
    }
    return status;
}

uint32_t
openusd_mdl_adapter_configure(
    openusd_mdl_adapter* adapter,
    const openusd_mdl_adapter_options* options)
{
    if (adapter == nullptr)
    {
        return OPENUSD_MDL_STATUS_INVALID_ARGUMENT;
    }
    return ApplyOptions(adapter, options);
}

void
openusd_mdl_adapter_destroy(openusd_mdl_adapter* adapter)
{
    delete adapter;
}

uint32_t
openusd_mdl_adapter_distill(
    openusd_mdl_adapter* adapter,
    const openusd_mdl_material_request* request,
    const openusd_mdl_distilled_material** result)
{
    if (adapter == nullptr || request == nullptr || result == nullptr)
    {
        return OPENUSD_MDL_STATUS_INVALID_ARGUMENT;
    }
    *result = nullptr;
    if (request->struct_size != sizeof(openusd_mdl_material_request))
    {
        return OPENUSD_MDL_STATUS_INVALID_ARGUMENT;
    }
    if (request->parameter_count != 0 && request->parameters == nullptr)
    {
        return OPENUSD_MDL_STATUS_INVALID_ARGUMENT;
    }

    const std::string moduleUri = ToString(request->module_uri);
    const std::string materialName = ToString(request->material_name);
    const std::string materialPath = ToString(request->material_path);

    DistillationResult distilled;
    const AcceptedMaterial accepted = ClassifyMaterial(moduleUri, materialName);

    // The module is compiled only when this build has an SDK backend, an
    // operator configured a search path, and the module is actually needed --
    // either to fill inputs the stage left unauthored, or because the module is
    // outside the accepted set and only its own declared parameter names can
    // say what it is. The authored-value answer is never withheld waiting for a
    // module: if the SDK cannot resolve one, the accepted authored subset still
    // distils exactly as it does in the dependency-free adapter.
    openusd_mdl::SdkMaterialResolution moduleResolution;
    bool moduleConsulted = false;
    if (openusd_mdl::SdkBackend::IsCompiledIn() && !adapter->searchPaths.empty())
    {
        moduleResolution = openusd_mdl::SdkBackend::Instance().ResolveMaterial(
            moduleUri, materialName);
        moduleConsulted = true;
    }

    if (accepted == AcceptedMaterial::None && !moduleConsulted)
    {
        distilled.status = IsAcceptedModule(moduleUri)
            ? OPENUSD_MDL_STATUS_UNSUPPORTED_MATERIAL
            : OPENUSD_MDL_STATUS_UNSUPPORTED_MODULE;
        distilled.diagnostic = "material '" + materialPath + "' names MDL module '" +
            moduleUri + "' material '" + materialName +
            "', which is outside the accepted distillation set (OmniPBR.mdl, "
            "OmniSurface.mdl, OmniGlass.mdl)" +
            (openusd_mdl::SdkBackend::IsCompiledIn()
                 ? "; no MDL module search path is configured, so the module "
                   "itself could not be consulted"
                 : "");
        *result = Freeze(adapter, distilled);
        return *result == nullptr ? OPENUSD_MDL_STATUS_OUT_OF_MEMORY
                                  : distilled.status;
    }
    if (accepted == AcceptedMaterial::None &&
        moduleResolution.status != OPENUSD_MDL_STATUS_OK)
    {
        // Outside the accepted set *and* the module could not be read, or was
        // read and produced nothing this adapter reduces. There is nothing left
        // to distil from, and the SDK's own reason is the useful one to report.
        distilled.status = moduleResolution.status;
        distilled.diagnostic = "material '" + materialPath + "' names MDL module '" +
            moduleUri + "' material '" + materialName + "': " +
            (moduleResolution.diagnostic.empty()
                 ? std::string("the MDL SDK backend reported no reason")
                 : moduleResolution.diagnostic);
        // The parameters whose defaults the backend could not reduce are still
        // named. A caller that is told only "unsupported" cannot say which input
        // it lost; a caller that is told the names can.
        for (const std::string& name : moduleResolution.unresolved)
        {
            distilled.unsupported.push_back(name);
        }
        *result = Freeze(adapter, distilled);
        return *result == nullptr ? OPENUSD_MDL_STATUS_OUT_OF_MEMORY
                                  : distilled.status;
    }

    ParameterTable parameters(request->parameters, request->parameter_count);
    if (moduleResolution.status == OPENUSD_MDL_STATUS_OK)
    {
        parameters.SetModuleDefaults(&moduleResolution.defaults);
    }

    switch (accepted)
    {
        case AcceptedMaterial::OmniPbr:
            DistillOmniPbr(parameters, distilled);
            break;
        case AcceptedMaterial::OmniSurface:
            DistillOmniSurface(parameters, distilled);
            break;
        case AcceptedMaterial::OmniGlass:
            DistillOmniGlass(parameters, distilled);
            break;
        case AcceptedMaterial::None:
            // A module the SDK read but this adapter has no module-level mapping
            // for. Its public parameter names are matched against the same
            // accepted name tables; the three sets are disjoint, so running all
            // three cannot make one module's name mean another's input. A module
            // that declares none of them distils to nothing and is reported.
            DistillOmniPbr(parameters, distilled);
            DistillOmniSurface(parameters, distilled);
            DistillOmniGlass(parameters, distilled);
            break;
    }

    for (std::string& name : parameters.Unconsumed())
    {
        distilled.unsupported.push_back(std::move(name));
    }
    for (const std::string& name : moduleResolution.unresolved)
    {
        // A default this backend could not reduce -- a call it does not fold, a
        // layered BSDF, or a texture the SDK could not resolve -- is named so
        // the caller can say which input it dropped.
        distilled.unsupported.push_back(name);
    }

    if (distilled.scalars.empty() && distilled.textures.empty())
    {
        // An accepted material that produced nothing is a failure, not an
        // empty success: shading it would draw the renderer's own defaults
        // while the stage says something else entirely.
        distilled.status = accepted == AcceptedMaterial::None
            ? OPENUSD_MDL_STATUS_EXPRESSION_UNSUPPORTED
            : OPENUSD_MDL_STATUS_DISTILLATION_FAILED;
        distilled.diagnostic = "material '" + materialPath + "' produced no MDL '" +
            materialName +
            "' input inside the accepted distillation subset, from either the "
            "authored values or the module defaults";
    }

    *result = Freeze(adapter, distilled);
    if (*result == nullptr)
    {
        return OPENUSD_MDL_STATUS_OUT_OF_MEMORY;
    }
    return distilled.status;
}

void
openusd_mdl_adapter_release_result(
    openusd_mdl_adapter* adapter,
    const openusd_mdl_distilled_material* result)
{
    if (adapter == nullptr || result == nullptr)
    {
        return;
    }
    for (auto entry = adapter->live.begin(); entry != adapter->live.end(); ++entry)
    {
        if (&(*entry)->view == result)
        {
            adapter->live.erase(entry);
            return;
        }
    }
}
}
