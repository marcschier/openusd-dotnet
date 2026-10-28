// Copyright (c) marcschier. Licensed under the MIT License.
//
// The optional MDL material adapter behind the project-owned openusd_mdl C
// ABI. It distils the authored USD input values of an accepted Omniverse MDL
// material into the renderer-neutral, UsdPreviewSurface-compatible record
// hdSilk publishes. Its optional SDK backend resolves module defaults and
// compiler-proven OmniPBR/OmniGlass/OmniSurface material variants behind the same C ABI.

#include "openusd_mdl.h"

#include "sdk_backend.h"

#include <algorithm>
#include <cctype>
#include <cmath>
#include <cstring>
#include <map>
#include <memory>
#include <mutex>
#include <new>
#include <set>
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
        const std::map<std::string, openusd_mdl::SdkParameterValue>* defaults,
        const std::vector<std::string>* unresolved,
        bool reportUnused)
    {
        _moduleDefaults = defaults;
        _unresolved = unresolved;
        _reportUnusedDefaults = reportUnused;
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
                _consumedDefaults.emplace(name);
                _lastOrigin = OPENUSD_MDL_ORIGIN_AUTHORED;
                return entry;
            }
        }
        return FindModuleDefault(name);
    }

    bool Has(const char* name) const
    {
        for (const openusd_mdl_parameter* entry : _entries)
        {
            if (ToString(entry->name) == name)
            {
                return true;
            }
        }
        return (_moduleDefaults != nullptr && _moduleDefaults->count(name) != 0) ||
            (_unresolved != nullptr &&
             std::find(_unresolved->begin(), _unresolved->end(), name) != _unresolved->end());
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
        if (_reportUnusedDefaults && _moduleDefaults != nullptr)
        {
            for (const auto& entry : *_moduleDefaults)
            {
                if (_consumedDefaults.count(entry.first) == 0 &&
                    entry.first.rfind("colorSpace:", 0) != 0 &&
                    !(entry.second.kind == OPENUSD_MDL_VALUE_ASSET && entry.second.text.empty()))
                {
                    names.push_back(entry.first);
                }
            }
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
        _consumedDefaults.insert(entry->first);
        for (const std::string& source : entry->second.authoredSources)
        {
            for (size_t index = 0; index < _entries.size(); ++index)
            {
                if (ToString(_entries[index]->name) == source)
                {
                    _consumed[index] = true;
                    _consumedDefaults.insert(source);
                }
            }
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
    const std::vector<std::string>* _unresolved = nullptr;
    std::set<std::string> _consumedDefaults;
    bool _reportUnusedDefaults = false;
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
    bool hasUvTransform = false;
    uint32_t uvTransformOrigin = OPENUSD_MDL_ORIGIN_MODULE_DEFAULT;
    float uvTransform[6] = {1, 0, 0, 1, 0, 0};

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
    if (parameter->kind == OPENUSD_MDL_VALUE_ASSET &&
        parameter->text.size == 0 &&
        parameters.LastOrigin() != OPENUSD_MDL_ORIGIN_AUTHORED)
    {
        return false;
    }
    result.unsupported.emplace_back(name);
    return false;
}

bool ReadUvPair(ParameterTable& parameters, const char* name, DistillationResult& result, float (&out)[2])
{
    const openusd_mdl_parameter* value = parameters.Find(name);
    if (!value)
    {
        return !parameters.Has(name);
    }
    if (value->kind != OPENUSD_MDL_VALUE_FLOAT2 || value->component_count != 2 ||
        !std::isfinite(value->value[0]) || !std::isfinite(value->value[1]))
    {
        result.unsupported.emplace_back(name);
        return false;
    }
    out[0] = value->value[0];
    out[1] = value->value[1];
    if (parameters.LastOrigin() == OPENUSD_MDL_ORIGIN_AUTHORED ||
        result.uvTransformOrigin == OPENUSD_MDL_ORIGIN_AUTHORED)
    {
        result.uvTransformOrigin = OPENUSD_MDL_ORIGIN_AUTHORED;
    }
    else if (parameters.LastOrigin() == OPENUSD_MDL_ORIGIN_MODULE_EXPRESSION)
    {
        result.uvTransformOrigin = OPENUSD_MDL_ORIGIN_MODULE_EXPRESSION;
    }
    return true;
}

void ReadTextureUv(
    ParameterTable& parameters, DistillationResult& result, const std::string& prefix)
{
    const std::string scaleName = prefix + "texture_scale";
    const std::string translationName = prefix + "texture_translate";
    const std::string rotationName = prefix + "texture_rotate";
    bool valid = true;
    float scale[2] = {1, 1};
    float translation[2] = {0, 0};
    valid = ReadUvPair(parameters, scaleName.c_str(), result, scale) && valid;
    valid = ReadUvPair(parameters, translationName.c_str(), result, translation) && valid;
    float degrees = 0;
    if (TryFloat(parameters, rotationName.c_str(), result, &degrees))
    {
        if (parameters.LastOrigin() == OPENUSD_MDL_ORIGIN_AUTHORED ||
            result.uvTransformOrigin == OPENUSD_MDL_ORIGIN_AUTHORED)
        {
            result.uvTransformOrigin = OPENUSD_MDL_ORIGIN_AUTHORED;
        }
        else if (parameters.LastOrigin() == OPENUSD_MDL_ORIGIN_MODULE_EXPRESSION)
        {
            result.uvTransformOrigin = OPENUSD_MDL_ORIGIN_MODULE_EXPRESSION;
        }
        valid = std::isfinite(degrees) && valid;
    }
    else if (parameters.Has(rotationName.c_str())) { valid = false; }
    if (!valid)
    {
        for (const std::string& name : {scaleName, translationName, rotationName})
        {
            if (parameters.Has(name.c_str())) { result.unsupported.emplace_back(name); }
        }
        return;
    }
    const float radians = degrees / 180.0F * 3.14159265358979323846F;
    const float cosine = std::cos(radians);
    const float sine = std::sin(radians);
    // The pinned OmniPBR helper is column-major scale/translation * clockwise rotation.
    result.uvTransform[0] = scale[0] * cosine;
    result.uvTransform[1] = scale[0] * sine;
    result.uvTransform[2] = -scale[1] * sine;
    result.uvTransform[3] = scale[1] * cosine;
    result.uvTransform[4] = translation[0];
    result.uvTransform[5] = translation[1];
    result.hasUvTransform = true;
}

void DistillOmniPbrUv(ParameterTable& parameters, DistillationResult& result)
{
    bool projected = false;
    const bool valid = TryBool(parameters, "project_uvw", result, &projected) || !parameters.Has("project_uvw");
    if (projected)
    {
        result.unsupported.emplace_back("project_uvw");
        return;
    }
    const openusd_mdl_parameter* space = parameters.Find("uv_space_index");
    if ((space && (space->kind != OPENUSD_MDL_VALUE_INT || space->integer_value != 0)) ||
        (!space && parameters.Has("uv_space_index")))
    {
        result.unsupported.emplace_back("uv_space_index");
        return;
    }
    if (!valid) { return; }
    (void)parameters.Find("world_or_object");
    ReadTextureUv(parameters, result, "");
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

/// A fractional texture/constant blend fits the existing affine texture record
/// when both the influence and constant stay within the byte-texture range.
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
    DistillationResult& result,
    float* factor)
{
    *factor = kUnauthored;
    float influence = kUnauthored;
    if (!TryFloat(parameters, influenceName, result, &influence))
    {
        // Preserve the authored-only profile when no module default is available.
        // SDK-backed requests resolve the actual module's default before reaching here.
        if (parameters.Has(influenceName)) { return TextureInfluence::Partial; }
        *factor = 1.0F;
        return TextureInfluence::Full;
    }
    *factor = influence;
    if (influence == 0.0F)
    {
        return TextureInfluence::Off;
    }
    if (influence == 1.0F)
    {
        return TextureInfluence::Full;
    }
    return TextureInfluence::Partial;
}

void
ReportUnmappedScalarInfluence(
    ParameterTable& parameters,
    DistillationResult& result,
    const char* influenceName)
{
    if (!parameters.Has(influenceName))
    {
        return;
    }
    float factor = kUnauthored;
    // Only exact zero proves that an unavailable lookup cannot change the constant.
    if (ClassifyInfluence(parameters, influenceName, result, &factor) != TextureInfluence::Off)
    {
        result.unsupported.emplace_back(influenceName);
    }
}

void
ProjectScalarTexture(
    ParameterTable& parameters,
    DistillationResult& result,
    const char* influenceName,
    uint32_t surfaceInput,
    uint32_t channel,
    bool hasConstant,
    float constant,
    uint32_t textureOrigin,
    uint32_t colorSpace,
    const std::string& asset)
{
    float factor = kUnauthored;
    const TextureInfluence influence = ClassifyInfluence(parameters, influenceName, result, &factor);
    if (influence == TextureInfluence::Off)
    {
        return;
    }
    const bool affine = influence == TextureInfluence::Partial && factor > 0 && factor < 1 &&
        hasConstant && std::isfinite(constant) && constant >= 0 && constant <= 1;
    if (influence != TextureInfluence::Full && !affine)
    {
        result.unsupported.emplace_back(influenceName);
        return;
    }
    result.currentOrigin = textureOrigin;
    result.AddTexture(surfaceInput, 1, channel, colorSpace, asset);
    if (affine)
    {
        result.textures.back().scale[channel] = factor;
        result.textures.back().bias[channel] = (1.0F - factor) * constant;
    }
}

void
ProjectAlbedoGrading(ParameterTable& parameters, DistillationResult& result, bool hasTexture)
{
    float brightness = 1.0F;
    float addition = 0.0F;
    bool valid = TryFloat(parameters, "albedo_brightness", result, &brightness) ||
        !parameters.Has("albedo_brightness");
    valid = (TryFloat(parameters, "albedo_add", result, &addition) || !parameters.Has("albedo_add")) && valid;
    valid = valid && std::isfinite(brightness) && std::isfinite(addition);
    float scale[3]{};
    float bias[3]{};
    if (valid && hasTexture)
    {
        const auto& texture = result.textures.back();
        for (size_t component = 0; component < 3; ++component)
        {
            // The module applies brightness/addition to the lookup, then multiplies by tint.
            scale[component] = brightness * texture.scale[component];
            bias[component] = addition * texture.scale[component];
            valid = valid && std::isfinite(scale[component]) && std::isfinite(bias[component]) &&
                std::isfinite(scale[component] + bias[component]);
        }
    }
    if (!valid)
    {
        for (const char* name : {"albedo_brightness", "albedo_add"})
        {
            if (parameters.Has(name)) { result.unsupported.emplace_back(name); }
        }
        return;
    }
    if (hasTexture)
    {
        for (size_t component = 0; component < 3; ++component)
        {
            result.textures.back().scale[component] = scale[component];
            result.textures.back().bias[component] = bias[component];
        }
    }
}

void
ProjectOpacityTexture(ParameterTable& parameters, DistillationResult& result,
    const std::string& asset, uint32_t textureOrigin)
{
    const openusd_mdl_parameter* mode = parameters.Find("opacity_mode");
    uint32_t channel = OPENUSD_MDL_CHANNEL_AVERAGE_RGB;
    if (mode)
    {
        if (mode->kind != OPENUSD_MDL_VALUE_INT || mode->integer_value < 0 || mode->integer_value > 3)
        {
            result.unsupported.emplace_back("opacity_mode");
            return;
        }
        const uint32_t channels[] = {OPENUSD_MDL_CHANNEL_A, OPENUSD_MDL_CHANNEL_AVERAGE_RGB,
            OPENUSD_MDL_CHANNEL_LUMINANCE_RGB, OPENUSD_MDL_CHANNEL_MAXIMUM_RGB};
        channel = channels[mode->integer_value];
    }
    else if (parameters.Has("opacity_mode"))
    {
        result.unsupported.emplace_back("opacity_mode");
        return;
    }
    result.currentOrigin = textureOrigin;
    result.AddTexture(OPENUSD_MDL_SURFACE_OPACITY, 1, channel,
        ResolveColorSpace(parameters, "opacity_texture", OPENUSD_MDL_COLOR_SPACE_RAW), asset);
}

void
DistillOmniPbrEmission(ParameterTable& parameters, DistillationResult& result, bool radianceProfile)
{
    bool enabled = false;
    const bool hasEnabled = TryBool(parameters, "enable_emission", result, &enabled);
    float color[3] = {0, 0, 0};
    const bool hasColor = TryColor(parameters, "emissive_color", result, color);
    const uint32_t colorOrigin = result.currentOrigin;
    std::string asset;
    const bool hasTexture = TryAsset(parameters, "emissive_color_texture", result, &asset);
    const uint32_t textureOrigin = result.currentOrigin;
    float intensity = 1;
    const bool hasIntensity = TryFloat(parameters, "emissive_intensity", result, &intensity);
    const uint32_t intensityOrigin = result.currentOrigin;
    if (!hasEnabled || !enabled)
    {
        if (hasColor) { result.unsupported.emplace_back("emissive_color"); }
        if (hasTexture) { result.unsupported.emplace_back("emissive_color_texture"); }
        return;
    }
    if (!radianceProfile)
    {
        if (hasColor)
        {
            result.currentOrigin = colorOrigin;
            result.AddScalar(OPENUSD_MDL_SURFACE_EMISSIVE_COLOR, 3, color);
        }
        if (hasTexture)
        {
            result.currentOrigin = textureOrigin;
            result.AddTexture(OPENUSD_MDL_SURFACE_EMISSIVE_COLOR, 3, OPENUSD_MDL_CHANNEL_RGB,
                ResolveColorSpace(parameters, "emissive_color_texture", OPENUSD_MDL_COLOR_SPACE_SRGB), asset);
        }
        if (hasIntensity && intensity != 1)
        {
            result.unsupported.emplace_back("emissive_intensity");
        }
        return;
    }
    if (!hasIntensity || !std::isfinite(intensity) || intensity < 0)
    {
        result.unsupported.emplace_back("emissive_intensity");
        return;
    }
    if (intensity == 0)
    {
        const float zero[3] = {0, 0, 0};
        result.currentOrigin = intensityOrigin;
        result.AddScalar(OPENUSD_MDL_SURFACE_EMISSIVE_COLOR, 3, zero);
        return;
    }

    // Pinned OmniPBRBase uses radiant exitance and diffuse_edf, whose value is 1/pi.
    constexpr float inversePi = 0.31830988618379067154F;
    const float factor = intensity * inversePi;
    float radiance[3] = {0, 0, 0};
    if (hasColor && !hasTexture)
    {
        for (size_t component = 0; component < 3; ++component)
        {
            const float exitance = color[component] * intensity;
            radiance[component] = exitance * inversePi;
            if (!std::isfinite(exitance) || !std::isfinite(radiance[component]))
            {
                result.unsupported.emplace_back("emissive_color");
                result.unsupported.emplace_back("emissive_intensity");
                return;
            }
        }
        result.currentOrigin = colorOrigin == OPENUSD_MDL_ORIGIN_AUTHORED ||
            intensityOrigin == OPENUSD_MDL_ORIGIN_AUTHORED ? OPENUSD_MDL_ORIGIN_AUTHORED
            : colorOrigin == OPENUSD_MDL_ORIGIN_MODULE_EXPRESSION ||
                intensityOrigin == OPENUSD_MDL_ORIGIN_MODULE_EXPRESSION
                ? OPENUSD_MDL_ORIGIN_MODULE_EXPRESSION : OPENUSD_MDL_ORIGIN_MODULE_DEFAULT;
        result.AddScalar(OPENUSD_MDL_SURFACE_EMISSIVE_COLOR, 3, radiance);
    }
    else if (!hasTexture)
    {
        result.unsupported.emplace_back("emissive_color");
        return;
    }
    std::string mask;
    const bool hasMask = TryAsset(parameters, "emissive_mask_texture", result, &mask);
    const uint32_t maskOrigin = result.currentOrigin;
    if (hasTexture)
    {
        result.currentOrigin = textureOrigin;
        result.AddTexture(OPENUSD_MDL_SURFACE_EMISSIVE_COLOR, 3, OPENUSD_MDL_CHANNEL_RGB,
            ResolveColorSpace(parameters, "emissive_color_texture", OPENUSD_MDL_COLOR_SPACE_SRGB), asset);
        for (size_t component = 0; component < 3; ++component)
        {
            result.textures.back().scale[component] = factor;
        }
    }
    if (hasMask)
    {
        result.currentOrigin = maskOrigin;
        result.AddTexture(OPENUSD_MDL_SURFACE_EMISSIVE_COLOR, 3, OPENUSD_MDL_CHANNEL_RGB,
            ResolveColorSpace(parameters, "emissive_mask_texture", OPENUSD_MDL_COLOR_SPACE_RAW), mask);
        if (hasTexture)
        {
            result.textures.back().composite_operator = OPENUSD_MDL_COMPOSITE_MULTIPLY;
        }
        else
        {
            for (size_t component = 0; component < 3; ++component)
            {
                result.textures.back().scale[component] = radiance[component];
            }
        }
    }
}

void AppendNormalTexture(
    ParameterTable& parameters, DistillationResult& result, const char* input,
    const std::string& asset, float strength, bool flipU, bool flipV, uint32_t origin,
    uint32_t surfaceInput = OPENUSD_MDL_SURFACE_NORMAL)
{
    result.currentOrigin = origin;
    result.AddTexture(surfaceInput, 3, OPENUSD_MDL_CHANNEL_RGB,
        ResolveColorSpace(parameters, input, OPENUSD_MDL_COLOR_SPACE_RAW), asset);
    const float scaleU = flipU ? -strength : strength;
    const float scaleV = flipV ? -strength : strength;
    result.textures.back().scale[0] = scaleU;
    result.textures.back().scale[1] = scaleV;
    result.textures.back().bias[0] = 0.5F - 0.5F * scaleU;
    result.textures.back().bias[1] = 0.5F - 0.5F * scaleV;
}

void DistillDetailNormal(ParameterTable& parameters, DistillationResult& result, bool neutralBase)
{
    const auto reportDetailInputs = [&] {
        for (const char* name : {"detail_normalmap_texture", "detail_bump_factor",
                 "detail_texture_scale", "detail_texture_translate", "detail_texture_rotate"})
        {
            if (parameters.Has(name)) { result.unsupported.emplace_back(name); }
        }
    };
    std::string asset;
    if (!TryAsset(parameters, "detail_normalmap_texture", result, &asset)) { return; }
    const uint32_t origin = result.currentOrigin;
    float strength = 0.3F;
    if ((!TryFloat(parameters, "detail_bump_factor", result, &strength) &&
            parameters.Has("detail_bump_factor")) || !std::isfinite(strength))
    {
        result.unsupported.emplace_back("detail_bump_factor");
        result.unsupported.emplace_back("detail_normalmap_texture");
        return;
    }
    if (strength == 0) { return; }
    DistillationResult detailUv;
    ReadTextureUv(parameters, detailUv, "detail_");
    const bool sharedUv = std::equal(
        detailUv.uvTransform, detailUv.uvTransform + 6, result.uvTransform);
    if (!detailUv.hasUvTransform)
    {
        result.unsupported.insert(result.unsupported.end(), detailUv.unsupported.begin(), detailUv.unsupported.end());
        reportDetailInputs();
        return;
    }
    bool flipU = false, flipV = false;
    if ((!TryBool(parameters, "flip_tangent_u", result, &flipU) && parameters.Has("flip_tangent_u")) ||
        (!TryBool(parameters, "flip_tangent_v", result, &flipV) && parameters.Has("flip_tangent_v")))
    {
        reportDetailInputs();
        return;
    }
    if (neutralBase && (result.textures.empty() || sharedUv))
    {
        if (result.textures.empty())
        {
            std::copy_n(detailUv.uvTransform, 6, result.uvTransform);
            result.uvTransformOrigin = detailUv.uvTransformOrigin;
        }
        // RNM over state::normal() is exactly the detail normal.
        AppendNormalTexture(parameters, result, "detail_normalmap_texture", asset, strength, flipU, flipV, origin);
        return;
    }
    AppendNormalTexture(parameters, result, "detail_normalmap_texture", asset, strength, flipU, flipV, origin,
        OPENUSD_MDL_SURFACE_DETAIL_NORMAL);
    result.currentOrigin = detailUv.uvTransformOrigin;
    const float row0[] = {detailUv.uvTransform[0], detailUv.uvTransform[1], detailUv.uvTransform[4]};
    const float row1[] = {detailUv.uvTransform[2], detailUv.uvTransform[3], detailUv.uvTransform[5]};
    result.AddScalar(OPENUSD_MDL_SURFACE_DETAIL_UV_ROW0, 3, row0);
    result.AddScalar(OPENUSD_MDL_SURFACE_DETAIL_UV_ROW1, 3, row1);
}

void
DistillOmniPbr(ParameterTable& parameters, DistillationResult& result, bool radianceProfile = false)
{
    if (radianceProfile)
    {
        float level = 0.5F;
        if (TryFloat(parameters, "specular_level", result, &level))
        {
            if (std::isfinite(level) && level >= 0 && level <= 1)
            {
                result.AddScalar(OPENUSD_MDL_SURFACE_OMNIPBR_SPECULAR_LEVEL, level);
            }
            else
            {
                result.unsupported.emplace_back("specular_level");
            }
        }
    }
    DistillOmniPbrUv(parameters, result);
    float color[3] = {0.0F, 0.0F, 0.0F};
    const bool hasColor = TryColor(parameters, "diffuse_color_constant", result, color);
    uint32_t colorOrigin = result.currentOrigin;
    float tint[3] = {1.0F, 1.0F, 1.0F};
    bool hasTint = TryColor(parameters, "diffuse_tint", result, tint);
    const uint32_t tintOrigin = result.currentOrigin;
    std::string asset;
    const bool hasTexture = TryAsset(parameters, "diffuse_texture", result, &asset);
    const uint32_t diffuseTextureOrigin = result.currentOrigin;
    if (hasTint && (!std::isfinite(tint[0]) || !std::isfinite(tint[1]) || !std::isfinite(tint[2])))
    {
        result.unsupported.emplace_back("diffuse_tint");
        hasTint = false;
    }
    if (hasColor && hasTint)
    {
        float tinted[3];
        bool finite = true;
        for (size_t component = 0; component < 3; ++component)
        {
            tinted[component] = color[component] * tint[component];
            finite = finite && std::isfinite(tinted[component]);
        }
        if (finite)
        {
            for (size_t component = 0; component < 3; ++component)
            {
                color[component] = tinted[component];
            }
            if (tint[0] != 1.0F || tint[1] != 1.0F || tint[2] != 1.0F)
            {
                colorOrigin = colorOrigin == OPENUSD_MDL_ORIGIN_AUTHORED ||
                    tintOrigin == OPENUSD_MDL_ORIGIN_AUTHORED ? OPENUSD_MDL_ORIGIN_AUTHORED
                    : colorOrigin == OPENUSD_MDL_ORIGIN_MODULE_EXPRESSION ||
                        tintOrigin == OPENUSD_MDL_ORIGIN_MODULE_EXPRESSION
                        ? OPENUSD_MDL_ORIGIN_MODULE_EXPRESSION : OPENUSD_MDL_ORIGIN_MODULE_DEFAULT;
            }
        }
        else
        {
            result.unsupported.emplace_back("diffuse_tint");
            hasTint = false;
        }
    }
    if (hasColor)
    {
        result.currentOrigin = colorOrigin;
        result.AddScalar(OPENUSD_MDL_SURFACE_DIFFUSE_COLOR, 3, color);
    }

    if (hasTexture)
    {
        result.currentOrigin = diffuseTextureOrigin;
        result.AddTexture(
            OPENUSD_MDL_SURFACE_DIFFUSE_COLOR,
            3,
            OPENUSD_MDL_CHANNEL_RGB,
            ResolveColorSpace(
                parameters, "diffuse_texture", OPENUSD_MDL_COLOR_SPACE_SRGB),
            asset);
        if (hasTint)
        {
            for (size_t component = 0; component < 3; ++component)
            {
                result.textures.back().scale[component] = tint[component];
            }
        }
    }
    else if (hasTint && !hasColor && (tint[0] != 1.0F || tint[1] != 1.0F || tint[2] != 1.0F))
    {
        // The authored-only adapter has no proven unauthored base color to multiply.
        result.unsupported.emplace_back("diffuse_tint");
    }

    ProjectAlbedoGrading(parameters, result, hasTexture);

    float desaturation = 0;
    if (TryFloat(parameters, "albedo_desaturation", result, &desaturation))
    {
        if (!std::isfinite(desaturation))
        {
            result.unsupported.emplace_back("albedo_desaturation");
        }
        else if (hasTexture && desaturation != 0)
        {
            const float mix[4] = {0.212671F, 0.715160F, 0.072169F, desaturation};
            result.AddScalar(OPENUSD_MDL_SURFACE_DIFFUSE_TEXTURE_LUMINANCE_MIX, 4, mix);
        }
    }

    bool ormEnabled = false;
    const bool ormAuthored = TryBool(parameters, "enable_ORM_texture", result, &ormEnabled);
    float roughness = 0.0F;
    const bool hasRoughness = TryFloat(parameters, "reflection_roughness_constant", result, &roughness);
    if (hasRoughness)
    {
        result.AddScalar(OPENUSD_MDL_SURFACE_ROUGHNESS, roughness);
    }
    if (TryAsset(parameters, "reflectionroughness_texture", result, &asset) && !(ormAuthored && ormEnabled))
    {
        const uint32_t textureOrigin = result.currentOrigin;
        ProjectScalarTexture(parameters, result, "reflection_roughness_texture_influence",
            OPENUSD_MDL_SURFACE_ROUGHNESS, OPENUSD_MDL_CHANNEL_R, hasRoughness, roughness, textureOrigin,
            ResolveColorSpace(parameters, "reflectionroughness_texture", OPENUSD_MDL_COLOR_SPACE_RAW), asset);
    }
    else if (!(ormAuthored && ormEnabled))
    {
        ReportUnmappedScalarInfluence(parameters, result, "reflection_roughness_texture_influence");
    }

    float metallic = 0.0F;
    const bool hasMetallic = TryFloat(parameters, "metallic_constant", result, &metallic);
    if (hasMetallic)
    {
        result.AddScalar(OPENUSD_MDL_SURFACE_METALLIC, metallic);
    }
    if (TryAsset(parameters, "metallic_texture", result, &asset) && !(ormAuthored && ormEnabled))
    {
        const uint32_t textureOrigin = result.currentOrigin;
        ProjectScalarTexture(parameters, result, "metallic_texture_influence",
            OPENUSD_MDL_SURFACE_METALLIC, OPENUSD_MDL_CHANNEL_R, hasMetallic, metallic, textureOrigin,
            ResolveColorSpace(parameters, "metallic_texture", OPENUSD_MDL_COLOR_SPACE_RAW), asset);
    }
    else if (!(ormAuthored && ormEnabled))
    {
        ReportUnmappedScalarInfluence(parameters, result, "metallic_texture_influence");
    }

    float aoWeight = 1.0F;
    const bool hasAoWeight = TryFloat(parameters, "ao_to_diffuse", result, &aoWeight);
    const bool aoDisabled = hasAoWeight && aoWeight == 0.0F;
    if (hasAoWeight && !aoDisabled)
    {
        // Nonzero MDL AO modifies diffuse color, not only ambient lighting.
        result.unsupported.emplace_back("ao_to_diffuse");
    }

    // The ORM convention packs occlusion, roughness and metallic into one
    // texture's R, G and B. hdSilk binds a channel per surface input, so the
    // three inputs read the same asset through three entries rather than
    // needing a packed-texture concept on the wire.
    if (TryAsset(parameters, "ORM_texture", result, &asset))
    {
        if (ormAuthored && ormEnabled)
        {
            const uint32_t ormOrigin = result.currentOrigin;
            const uint32_t ormColorSpace = ResolveColorSpace(
                parameters, "ORM_texture", OPENUSD_MDL_COLOR_SPACE_RAW);
            if (!aoDisabled)
            {
                result.AddTexture(
                    OPENUSD_MDL_SURFACE_OCCLUSION,
                    1,
                    OPENUSD_MDL_CHANNEL_R,
                    ormColorSpace,
                    asset);
            }
            ProjectScalarTexture(parameters, result, "reflection_roughness_texture_influence",
                OPENUSD_MDL_SURFACE_ROUGHNESS, OPENUSD_MDL_CHANNEL_G, hasRoughness, roughness,
                ormOrigin, ormColorSpace, asset);
            ProjectScalarTexture(parameters, result, "metallic_texture_influence",
                OPENUSD_MDL_SURFACE_METALLIC, OPENUSD_MDL_CHANNEL_B, hasMetallic, metallic,
                ormOrigin, ormColorSpace, asset);
        }
        else
        {
            result.unsupported.emplace_back("ORM_texture");
        }
    }
    else if (ormAuthored && ormEnabled)
    {
        ReportUnmappedScalarInfluence(parameters, result, "reflection_roughness_texture_influence");
        ReportUnmappedScalarInfluence(parameters, result, "metallic_texture_influence");
    }

    bool opacityEnabled = false;
    const bool opacityAuthored =
        TryBool(parameters, "enable_opacity", result, &opacityEnabled);
    float opacity = 0.0F;
    const bool opacityConstantAuthored =
        TryFloat(parameters, "opacity_constant", result, &opacity);
    const uint32_t opacityOrigin = result.currentOrigin;
    const bool opacityTextureAuthored =
        TryAsset(parameters, "opacity_texture", result, &asset);
    const uint32_t opacityTextureOrigin = result.currentOrigin;
    bool opacityTextureEnabled = false;
    const bool opacityTextureGateAuthored = TryBool(
        parameters, "enable_opacity_texture", result, &opacityTextureEnabled);
    if (opacityAuthored && opacityEnabled)
    {
        if (opacityConstantAuthored)
        {
            if (std::isfinite(opacity) && opacity >= 0 && opacity <= 1)
            {
                result.currentOrigin = opacityOrigin;
                result.AddScalar(OPENUSD_MDL_SURFACE_OPACITY, opacity);
            }
            else { result.unsupported.emplace_back("opacity_constant"); }
        }
        if (opacityTextureAuthored && opacityTextureGateAuthored &&
            opacityTextureEnabled)
        {
            ProjectOpacityTexture(parameters, result, asset, opacityTextureOrigin);
        }
        else if (opacityTextureAuthored || (opacityTextureGateAuthored && opacityTextureEnabled))
        {
            result.unsupported.emplace_back("opacity_texture");
        }
        float threshold = 0.0F;
        if (TryFloat(parameters, "opacity_threshold", result, &threshold))
        {
            if (std::isfinite(threshold) && threshold >= 0 && threshold <= 1)
            {
                result.AddScalar(OPENUSD_MDL_SURFACE_OPACITY_THRESHOLD, threshold);
            }
            else { result.unsupported.emplace_back("opacity_threshold"); }
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

    DistillOmniPbrEmission(parameters, result, radianceProfile);

    const openusd_mdl_parameter* baseNormal = parameters.Find("normalmap_texture");
    bool neutralBase = !parameters.Has("normalmap_texture") ||
        (baseNormal && baseNormal->kind == OPENUSD_MDL_VALUE_ASSET && baseNormal->text.size == 0);
    if (TryAsset(parameters, "normalmap_texture", result, &asset))
    {
        const uint32_t normalOrigin = result.currentOrigin;
        float strength = 1.0F;
        const bool hasStrength = TryFloat(parameters, "bump_factor", result, &strength);
        if ((!hasStrength && parameters.Has("bump_factor")) || !std::isfinite(strength))
        {
            result.unsupported.emplace_back("bump_factor");
            strength = 1.0F;
        }
        bool flipU = false;
        bool flipV = false;
        (void)TryBool(parameters, "flip_tangent_u", result, &flipU);
        (void)TryBool(parameters, "flip_tangent_v", result, &flipV);
        neutralBase = strength == 0;
        if (!neutralBase)
        {
            AppendNormalTexture(parameters, result, "normalmap_texture",
                asset, strength, flipU, flipV, normalOrigin);
        }
    }
    const bool hasBaseNormal = std::any_of(result.textures.begin(), result.textures.end(),
        [](const auto& texture) { return texture.surface_input == OPENUSD_MDL_SURFACE_NORMAL; });
    if (radianceProfile && (neutralBase || hasBaseNormal) && result.hasUvTransform)
    {
        DistillDetailNormal(parameters, result, neutralBase);
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
DistillOmniGlass(
    ParameterTable& parameters,
    DistillationResult& result,
    bool knownMaterial = true)
{
    const size_t initialScalars = result.scalars.size();
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
    if (knownMaterial || result.scalars.size() != initialScalars)
    {
        result.AddScalar(OPENUSD_MDL_SURFACE_OPACITY, glassOpacity);
    }
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
            if (!IsAbsolutePath(path) || path.find('\0') != std::string::npos)
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
    std::sort(result.unsupported.begin(), result.unsupported.end());
    result.unsupported.erase(
        std::unique(result.unsupported.begin(), result.unsupported.end()),
        result.unsupported.end());
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
    storage->view.has_uv_transform = result.hasUvTransform ? 1u : 0u;
    storage->view.uv_transform_origin = result.uvTransformOrigin;
    for (size_t index = 0; index < 6; ++index)
    {
        storage->view.uv_transform[index] = result.uvTransform[index];
    }

    const openusd_mdl_distilled_material* view = &storage->view;
    adapter->live.push_back(std::move(storage));
    return view;
}

bool
ValidRequestText(const openusd_mdl_string& text, size_t* total)
{
    if (text.size > openusd_mdl::kMaxSdkTextBytes ||
        (text.size != 0 && (text.data == nullptr || std::memchr(text.data, '\0', text.size) != nullptr)))
    {
        return false;
    }
    *total += text.size;
    return *total <= openusd_mdl::kMaxSdkTotalTextBytes;
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
            "expressions; bounded SDK-proven OmniPBR/OmniGlass/OmniSurface variants";
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
    if (request->parameter_count > openusd_mdl::kMaxSdkParameters ||
        (request->parameter_count != 0 && request->parameters == nullptr))
    {
        return OPENUSD_MDL_STATUS_INVALID_ARGUMENT;
    }
    size_t textBytes = 0;
    if (!ValidRequestText(request->module_uri, &textBytes) ||
        !ValidRequestText(request->material_name, &textBytes) ||
        !ValidRequestText(request->material_path, &textBytes))
    {
        return OPENUSD_MDL_STATUS_INVALID_ARGUMENT;
    }
    for (uint32_t index = 0; index < request->parameter_count; ++index)
    {
        const openusd_mdl_parameter& parameter = request->parameters[index];
        if (!ValidRequestText(parameter.name, &textBytes) ||
            !ValidRequestText(parameter.text, &textBytes) || parameter.component_count > 4)
        {
            return OPENUSD_MDL_STATUS_INVALID_ARGUMENT;
        }
    }

    const std::string moduleUri = ToString(request->module_uri);
    const std::string materialName = ToString(request->material_name);
    const std::string materialPath = ToString(request->material_path);

    DistillationResult distilled;
    AcceptedMaterial accepted = ClassifyMaterial(moduleUri, materialName);

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
        std::map<std::string, openusd_mdl::SdkParameterValue> authored;
        for (uint32_t index = 0; index < request->parameter_count; ++index)
        {
            const openusd_mdl_parameter& parameter = request->parameters[index];
            openusd_mdl::SdkParameterValue value;
            value.kind = parameter.kind;
            value.componentCount = parameter.component_count;
            std::copy_n(parameter.value, 4, value.value);
            value.integerValue = parameter.integer_value;
            value.text = ToString(parameter.text);
            value.origin = OPENUSD_MDL_ORIGIN_AUTHORED;
            authored.emplace(ToString(parameter.name), std::move(value));
        }
        // Decoding belongs to the asset and must survive SDK aliases/copies.
        // Keep the text intact; ResolveColorSpace retains destination precedence
        // and the existing handling of unrecognized metadata.
        for (auto& entry : authored)
        {
            if (entry.second.kind == OPENUSD_MDL_VALUE_ASSET ||
                entry.second.kind == OPENUSD_MDL_VALUE_STRING)
            {
                const auto metadata = authored.find("colorSpace:" + entry.first);
                if (metadata != authored.end())
                {
                    entry.second.colorSpace = metadata->second.text;
                }
            }
        }
        moduleResolution = openusd_mdl::SdkBackend::Instance().ResolveMaterial(
            moduleUri, materialName, adapter->searchPaths, adapter->cacheGeneration, authored);
        moduleConsulted = true;
        if (accepted == AcceptedMaterial::None && moduleResolution.status == OPENUSD_MDL_STATUS_OK)
        {
            if (moduleResolution.kind == openusd_mdl::SdkMaterialKind::OmniPbr)
            {
                accepted = AcceptedMaterial::OmniPbr;
            }
            else if (moduleResolution.kind == openusd_mdl::SdkMaterialKind::OmniGlass)
            {
                accepted = AcceptedMaterial::OmniGlass;
            }
            else if (moduleResolution.kind == openusd_mdl::SdkMaterialKind::OmniSurface)
            {
                accepted = AcceptedMaterial::OmniSurface;
            }
        }
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
        parameters.SetModuleDefaults(
            &moduleResolution.defaults, &moduleResolution.unresolved, moduleResolution.isVariant);
        distilled.diagnostic = moduleResolution.diagnostic;
    }

    switch (accepted)
    {
        case AcceptedMaterial::OmniPbr:
            DistillOmniPbr(parameters, distilled,
                moduleConsulted && moduleResolution.status == OPENUSD_MDL_STATUS_OK &&
                    moduleResolution.kind == openusd_mdl::SdkMaterialKind::OmniPbr);
            break;
        case AcceptedMaterial::OmniSurface:
            DistillOmniSurface(parameters, distilled);
            break;
        case AcceptedMaterial::OmniGlass:
            DistillOmniGlass(parameters, distilled);
            break;
        case AcceptedMaterial::None:
            // Legacy parameter-default projection is not wrapper admission:
            // the SDK resolution explicitly reports the unproven body.
            DistillOmniPbr(parameters, distilled);
            DistillOmniSurface(parameters, distilled);
            DistillOmniGlass(parameters, distilled, false);
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
        distilled.status = accepted == AcceptedMaterial::None || moduleResolution.isVariant
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
