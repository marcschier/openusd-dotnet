// Copyright (c) marcschier. Licensed under the MIT License.

#include "openusd_mdl.h"

#include <algorithm>
#include <cmath>
#include <cstring>
#include <filesystem>
#include <fstream>
#include <initializer_list>
#include <iostream>
#include <memory>
#include <stdexcept>
#include <string>
#include <vector>

namespace
{
namespace fs = std::filesystem;

openusd_mdl_string View(const std::string& text)
{
    return {text.data(), static_cast<uint32_t>(text.size())};
}

openusd_mdl_string View(const char* text)
{
    return {text, static_cast<uint32_t>(std::strlen(text))};
}

std::string Text(openusd_mdl_string text)
{
    return text.data == nullptr ? std::string() : std::string(text.data, text.size);
}

void Require(bool condition, const std::string& message)
{
    if (!condition)
    {
        throw std::runtime_error(message);
    }
}

using Adapter = std::unique_ptr<openusd_mdl_adapter, decltype(&openusd_mdl_adapter_destroy)>;

uint32_t Configure(
    openusd_mdl_adapter* adapter,
    const std::vector<std::string>& roots,
    uint64_t generation)
{
    std::vector<openusd_mdl_string> paths;
    for (const std::string& root : roots)
    {
        paths.push_back(View(root));
    }
    openusd_mdl_adapter_options options{};
    options.struct_size = sizeof(options);
    options.module_search_paths = paths.data();
    options.module_search_path_count = static_cast<uint32_t>(paths.size());
    options.cache_generation = generation;
    return openusd_mdl_adapter_configure(adapter, &options);
}

Adapter Create(const std::vector<std::string>& roots, uint64_t generation = 1)
{
    openusd_mdl_adapter* instance = nullptr;
    Require(openusd_mdl_adapter_create(nullptr, &instance) == OPENUSD_MDL_STATUS_OK,
        "adapter creation failed");
    Adapter adapter(instance, openusd_mdl_adapter_destroy);
    Require(Configure(adapter.get(), roots, generation) == OPENUSD_MDL_STATUS_OK,
        "adapter configuration failed");
    return adapter;
}

struct ReleaseResult
{
    openusd_mdl_adapter* adapter;
    void operator()(const openusd_mdl_distilled_material* result) const
    {
        openusd_mdl_adapter_release_result(adapter, result);
    }
};

struct Evaluation
{
    uint32_t status;
    std::unique_ptr<const openusd_mdl_distilled_material, ReleaseResult> result;
};

Evaluation Distill(
    openusd_mdl_adapter* adapter,
    const std::string& module,
    const std::string& material,
    const std::vector<openusd_mdl_parameter>& parameters = {})
{
    const std::string path = "/WrapperProbe/" + material;
    openusd_mdl_material_request request{};
    request.struct_size = sizeof(request);
    request.module_uri = View(module);
    request.material_name = View(material);
    request.material_path = View(path);
    request.parameters = parameters.data();
    request.parameter_count = static_cast<uint32_t>(parameters.size());
    const openusd_mdl_distilled_material* result = nullptr;
    const uint32_t status = openusd_mdl_adapter_distill(adapter, &request, &result);
    return {status, {result, ReleaseResult{adapter}}};
}

const openusd_mdl_distilled_material& Success(const Evaluation& value)
{
    const std::string diagnostic = value.result ? Text(value.result->diagnostic) : "(no result)";
    Require(value.status == OPENUSD_MDL_STATUS_OK, "distillation failed: " + diagnostic);
    Require(value.result != nullptr && value.result->status == value.status &&
        value.result->struct_size == sizeof(openusd_mdl_distilled_material),
        "invalid public result header");
    Require((value.result->scalar_count == 0) == (value.result->scalars == nullptr) &&
        (value.result->texture_count == 0) == (value.result->textures == nullptr) &&
        (value.result->unsupported_parameter_count == 0) ==
            (value.result->unsupported_parameters == nullptr),
        "invalid public result table ownership");
    return *value.result;
}

void Failure(const Evaluation& value, uint32_t status, const std::string& namedReason)
{
    Require(value.status == status && value.result != nullptr &&
        value.result->status == status, "wrong refusal status for " + namedReason +
            (value.result ? ": " + Text(value.result->diagnostic) : ""));
    Require(value.result->scalar_count == 0 && value.result->scalars == nullptr &&
        value.result->texture_count == 0 && value.result->textures == nullptr,
        "a refused material still publishes shading data");
    Require(Text(value.result->diagnostic).find(namedReason) != std::string::npos,
        "refusal did not name " + namedReason + ": " + Text(value.result->diagnostic));
}

bool Reported(const openusd_mdl_distilled_material& value, const std::string& name)
{
    for (uint32_t index = 0; index < value.unsupported_parameter_count; ++index)
    {
        if (Text(value.unsupported_parameters[index]) == name)
        {
            return true;
        }
    }
    return false;
}

void ReportWrapper(const std::string& name, const openusd_mdl_distilled_material& value)
{
    std::cout << "WAREHOUSE_MDL_WRAPPER name=" << name << " scalars=" << value.scalar_count
        << " textures=" << value.texture_count << " unsupported=";
    for (uint32_t index = 0; index < value.unsupported_parameter_count; ++index)
    {
        if (index != 0)
        {
            std::cout << ',';
        }
        std::cout << Text(value.unsupported_parameters[index]);
    }
    std::cout << '\n';
}

void Scalar(
    const openusd_mdl_distilled_material& value,
    uint32_t input,
    std::initializer_list<float> expected,
    uint32_t origin = OPENUSD_MDL_ORIGIN_MODULE_DEFAULT)
{
    const openusd_mdl_distilled_scalar* found = nullptr;
    for (uint32_t index = 0; index < value.scalar_count; ++index)
    {
        if (value.scalars[index].surface_input == input)
        {
            Require(found == nullptr, "duplicate scalar input " + std::to_string(input));
            found = &value.scalars[index];
        }
    }
    Require(found != nullptr, "missing scalar input " + std::to_string(input));
    Require(found->component_count == expected.size() && found->origin == origin,
        "wrong scalar shape/origin for input " + std::to_string(input));
    size_t component = 0;
    for (float literal : expected)
    {
        Require(std::isfinite(found->value[component]) &&
            std::fabs(found->value[component] - literal) < 0.00001F,
            "wrong scalar value for input " + std::to_string(input));
        ++component;
    }
}

void Texture(
    const openusd_mdl_distilled_material& value,
    uint32_t input,
    const fs::path& expected,
    uint32_t channel,
    uint32_t colorSpace,
    uint32_t origin = OPENUSD_MDL_ORIGIN_MODULE_DEFAULT)
{
    const openusd_mdl_distilled_texture* found = nullptr;
    for (uint32_t index = 0; index < value.texture_count; ++index)
    {
        if (value.textures[index].surface_input == input)
        {
            Require(found == nullptr, "duplicate texture input " + std::to_string(input));
            found = &value.textures[index];
        }
    }
    Require(found != nullptr, "missing texture input " + std::to_string(input));
    const fs::path actual = fs::u8path(Text(found->asset));
    std::error_code error;
    Require(actual.is_absolute() && fs::equivalent(actual, expected, error) && !error,
        "wrong texture path: '" + actual.u8string() + "', expected '" + expected.u8string() + "'");
    Require(found->component_count == (channel == OPENUSD_MDL_CHANNEL_RGB ? 3u : 1u) &&
        found->output_channel == channel && found->color_space == colorSpace &&
        found->origin == origin && found->wrap_s == OPENUSD_MDL_WRAP_REPEAT &&
        found->wrap_t == OPENUSD_MDL_WRAP_REPEAT,
        "wrong texture channel/shape/color space/origin/wrap for " + expected.u8string());
    for (size_t component = 0; component < 4; ++component)
    {
        Require(found->scale[component] == 1.0F && found->bias[component] == 0.0F,
            "unexpected texture scale/bias");
    }
}

openusd_mdl_parameter Number(const char* name, float value)
{
    openusd_mdl_parameter result{};
    result.name = View(name);
    result.kind = OPENUSD_MDL_VALUE_FLOAT;
    result.component_count = 1;
    result.value[0] = value;
    return result;
}

openusd_mdl_parameter Boolean(const char* name, bool value)
{
    openusd_mdl_parameter result{};
    result.name = View(name);
    result.kind = OPENUSD_MDL_VALUE_BOOL;
    result.component_count = 1;
    result.integer_value = value ? 1 : 0;
    return result;
}

openusd_mdl_parameter Color(const char* name, float red, float green, float blue)
{
    openusd_mdl_parameter result{};
    result.name = View(name);
    result.kind = OPENUSD_MDL_VALUE_FLOAT3;
    result.component_count = 3;
    result.value[0] = red;
    result.value[1] = green;
    result.value[2] = blue;
    return result;
}

openusd_mdl_parameter Asset(const char* name, const std::string& asset)
{
    openusd_mdl_parameter result{};
    result.name = View(name);
    result.kind = OPENUSD_MDL_VALUE_ASSET;
    result.text = View(asset);
    return result;
}

void Write(const fs::path& file, const std::string& source)
{
    fs::create_directories(file.parent_path());
    std::ofstream stream(file, std::ios::binary | std::ios::trunc);
    stream << source;
    stream.close();
    Require(static_cast<bool>(stream), "could not write probe-owned fixture " + file.u8string());
}

std::string MutableVariant(const char* roughness)
{
    return std::string("mdl 1.7;\nusing ::OmniPBR import OmniPBR;\n") +
        "export material mutable_variant(*) = OmniPBR("
        "reflection_roughness_constant: " + roughness + ");\n";
}

struct Probe
{
    unsigned int passed = 0;
    unsigned int failed = 0;

    template<class F>
    void Run(const char* name, F test)
    {
        try
        {
            test();
            ++passed;
            std::cout << "PASS " << name << '\n';
        }
        catch (const std::exception& error)
        {
            ++failed;
            std::cerr << "FAIL " << name << ": " << error.what() << '\n';
        }
    }
};

void Rejections(Probe& probe, const fs::path& fixtures)
{
    auto adapter = Create({fixtures.u8string()});
    probe.Run("UnknownVariantRootIsRefused", [&] {
        auto value = Distill(adapter.get(), "rejections.mdl", "unknown_root");
        Failure(value, OPENUSD_MDL_STATUS_EXPRESSION_UNSUPPORTED, "unrelated");
        Require(Reported(*value.result, "body:unrelated"), "unknown root body was not named");
    });
    probe.Run("UnsupportedBodyIsRefused", [&] {
        auto value = Distill(adapter.get(), "rejections.mdl", "unsupported_body");
        Failure(value, OPENUSD_MDL_STATUS_EXPRESSION_UNSUPPORTED, "unsupported_body");
        Require(Reported(*value.result, "body:unsupported_body"), "unsupported body was not named");
    });
    probe.Run("LegacyDefaultsAreExplicitlyNotBodyProjection", [&] {
        auto value = Distill(adapter.get(), "rejections.mdl", "unrelated");
        const auto& result = Success(value);
        Scalar(result, OPENUSD_MDL_SURFACE_DIFFUSE_COLOR, {0.9F, 0.8F, 0.7F});
        Scalar(result, OPENUSD_MDL_SURFACE_ROUGHNESS, {0.15F});
        Require(result.scalar_count == 2 && Reported(result, "body:unrelated"),
            "legacy defaults were claimed as a clean known material or gained glass opacity");
    });
    probe.Run("SparseFormalAliasUsesAuthoredInput", [&] {
        auto value = Distill(adapter.get(), "rejections.mdl", "sparse_defaults",
            {Number("caller_level", 0.73F)});
        const auto& result = Success(value);
        Scalar(result, OPENUSD_MDL_SURFACE_DIFFUSE_COLOR, {0.42F, 0.42F, 0.42F});
        Scalar(result, OPENUSD_MDL_SURFACE_ROUGHNESS, {0.73F}, OPENUSD_MDL_ORIGIN_AUTHORED);
        Require(!Reported(result, "caller_level") &&
            !Reported(result, "reflection_roughness_constant"),
            "a successfully projected authored alias was reported as dropped");
    });
    probe.Run("ConstructorAliasUsesAuthoredInput", [&] {
        auto value = Distill(adapter.get(), "rejections.mdl", "constructor_defaults",
            {Number("caller_level", 0.67F)});
        const auto& result = Success(value);
        Scalar(result, OPENUSD_MDL_SURFACE_DIFFUSE_COLOR, {0.67F, 0.67F, 0.67F},
            OPENUSD_MDL_ORIGIN_AUTHORED);
        Scalar(result, OPENUSD_MDL_SURFACE_ROUGHNESS, {0.2F});
        Require(!Reported(result, "caller_level") &&
            !Reported(result, "diffuse_color_constant"),
            "the SDK constructor's authored source was reported as dropped");
    });
    probe.Run("RequestArgumentAndTextBounds", [&] {
        std::vector<openusd_mdl_parameter> arguments(256, Number("extra", 0.1F));
        auto boundary = Distill(adapter.get(), "rejections.mdl", "unrelated", arguments);
        Scalar(Success(boundary), OPENUSD_MDL_SURFACE_ROUGHNESS, {0.15F});
        arguments.push_back(Number("extra", 0.1F));
        auto excess = Distill(adapter.get(), "rejections.mdl", "unrelated", arguments);
        Require(excess.status == OPENUSD_MDL_STATUS_INVALID_ARGUMENT && !excess.result,
            "257 authored inputs were not refused");
        const std::string text(4096, 'x');
        arguments = {Asset("extra", text)};
        auto textBoundary = Distill(adapter.get(), "rejections.mdl", "unrelated", arguments);
        Scalar(Success(textBoundary), OPENUSD_MDL_SURFACE_ROUGHNESS, {0.15F});
        const std::string oversized(4097, 'x');
        arguments = {Asset("extra", oversized)};
        auto textExcess = Distill(adapter.get(), "rejections.mdl", "unrelated", arguments);
        Require(textExcess.status == OPENUSD_MDL_STATUS_INVALID_ARGUMENT && !textExcess.result,
            "an oversized text range was not refused");
        arguments.assign(64, Asset("extra", text));
        auto totalExcess = Distill(adapter.get(), "rejections.mdl", "unrelated", arguments);
        Require(totalExcess.status == OPENUSD_MDL_STATUS_INVALID_ARGUMENT && !totalExcess.result,
            "the aggregate text bound was not enforced");
        const std::string embedded("a\0b", 3);
        arguments = {Asset("extra", embedded)};
        auto nul = Distill(adapter.get(), "rejections.mdl", "unrelated", arguments);
        Require(nul.status == OPENUSD_MDL_STATUS_INVALID_ARGUMENT && !nul.result,
            "embedded NUL was silently shortened");
    });
    probe.Run("MissingModuleAndMaterialRemainDistinct", [&] {
        auto absent = Distill(adapter.get(), "absent-on-purpose.mdl", "unknown");
        Failure(absent, OPENUSD_MDL_STATUS_MODULE_NOT_FOUND, "absent-on-purpose");
        auto missing = Distill(adapter.get(), "rejections.mdl", "unrelated_suffix");
        Failure(missing, OPENUSD_MDL_STATUS_UNSUPPORTED_MATERIAL, "unrelated_suffix");
    });
    probe.Run("FoundInvalidModuleReportsCompilerFailure", [&] {
        auto value = Distill(adapter.get(), "invalid_module.mdl", "invalid_module");
        Failure(value, OPENUSD_MDL_STATUS_MODULE_COMPILE_FAILED, "missing_material_constructor");
    });
}

void Synthetic(
    Probe& probe,
    const fs::path& fixtures,
    const fs::path& core,
    const fs::path& work)
{
    const std::vector<std::string> roots{core.u8string(), fixtures.u8string()};
    auto adapter = Create(roots);
    const std::string variants = "wrappers\\variants.mdl";
    const fs::path textures = fixtures / "wrappers" / "textures";
    probe.Run("VariantBodyConstantsReplaceBaseDefaults", [&] {
        auto value = Distill(adapter.get(), variants, "constants");
        const auto& result = Success(value);
        Scalar(result, OPENUSD_MDL_SURFACE_DIFFUSE_COLOR, {0.12F, 0.34F, 0.56F});
        Scalar(result, OPENUSD_MDL_SURFACE_ROUGHNESS, {0.21F});
        Scalar(result, OPENUSD_MDL_SURFACE_METALLIC, {0.84F});
        Require(result.scalar_count == 3 && result.texture_count == 0,
            "PBR variant gained unrelated glass/surface outputs");
    });
    probe.Run("AuthoredInputsOverrideVariantDefaults", [&] {
        auto value = Distill(adapter.get(), variants, "constants", {
            Number("reflection_roughness_constant", 0.93F),
            Color("diffuse_color_constant", 0.71F, 0.52F, 0.33F)});
        const auto& result = Success(value);
        Scalar(result, OPENUSD_MDL_SURFACE_DIFFUSE_COLOR, {0.71F, 0.52F, 0.33F},
            OPENUSD_MDL_ORIGIN_AUTHORED);
        Scalar(result, OPENUSD_MDL_SURFACE_ROUGHNESS, {0.93F}, OPENUSD_MDL_ORIGIN_AUTHORED);
        Scalar(result, OPENUSD_MDL_SURFACE_METALLIC, {0.84F});
        Require(!Reported(result, "reflection_roughness_constant"),
            "an authored override was reported as unsupported");
        auto repeated = Distill(adapter.get(), variants, "constants");
        Scalar(Success(repeated), OPENUSD_MDL_SURFACE_ROUGHNESS, {0.21F});
    });
    probe.Run("LocalTextureOrmNormalAndGamma", [&] {
        auto value = Distill(adapter.get(), variants, "textured");
        const auto& result = Success(value);
        Require(result.texture_count == 5, "expected diffuse, three ORM channels, and normal");
        Texture(result, OPENUSD_MDL_SURFACE_DIFFUSE_COLOR, textures / "albedo.ppm",
            OPENUSD_MDL_CHANNEL_RGB, OPENUSD_MDL_COLOR_SPACE_RAW);
        Texture(result, OPENUSD_MDL_SURFACE_OCCLUSION, textures / "orm.ppm",
            OPENUSD_MDL_CHANNEL_R, OPENUSD_MDL_COLOR_SPACE_RAW);
        Texture(result, OPENUSD_MDL_SURFACE_ROUGHNESS, textures / "orm.ppm",
            OPENUSD_MDL_CHANNEL_G, OPENUSD_MDL_COLOR_SPACE_RAW);
        Texture(result, OPENUSD_MDL_SURFACE_METALLIC, textures / "orm.ppm",
            OPENUSD_MDL_CHANNEL_B, OPENUSD_MDL_COLOR_SPACE_RAW);
        Texture(result, OPENUSD_MDL_SURFACE_NORMAL, textures / "normal.ppm",
            OPENUSD_MDL_CHANNEL_RGB, OPENUSD_MDL_COLOR_SPACE_RAW);
    });
    probe.Run("InheritedTextureRetainsDeclaringModuleOwner", [&] {
        auto value = Distill(adapter.get(), "wrappers\\derived\\inherited.mdl", "inherited");
        const auto& result = Success(value);
        Texture(result, OPENUSD_MDL_SURFACE_DIFFUSE_COLOR, textures / "albedo.ppm",
            OPENUSD_MDL_CHANNEL_RGB, OPENUSD_MDL_COLOR_SPACE_RAW);
        Texture(result, OPENUSD_MDL_SURFACE_NORMAL, textures / "normal.ppm",
            OPENUSD_MDL_CHANNEL_RGB, OPENUSD_MDL_COLOR_SPACE_RAW);
    });
    probe.Run("AuthoredTextureAndGateOriginsStaySeparate", [&] {
        const std::string asset = (textures / "albedo.ppm").u8string();
        auto value = Distill(adapter.get(), variants, "influenced",
            {Asset("reflectionroughness_texture", asset), Asset("metallic_texture", asset)});
        const auto& result = Success(value);
        Texture(result, OPENUSD_MDL_SURFACE_ROUGHNESS, fs::u8path(asset),
            OPENUSD_MDL_CHANNEL_R, OPENUSD_MDL_COLOR_SPACE_RAW, OPENUSD_MDL_ORIGIN_AUTHORED);
        Texture(result, OPENUSD_MDL_SURFACE_METALLIC, fs::u8path(asset),
            OPENUSD_MDL_CHANNEL_R, OPENUSD_MDL_COLOR_SPACE_RAW, OPENUSD_MDL_ORIGIN_AUTHORED);
        auto gates = Distill(adapter.get(), variants, "constants", {
            Boolean("enable_opacity", true), Number("opacity_constant", 0.44F),
            Boolean("enable_emission", true), Color("emissive_color", 0.24F, 0.35F, 0.46F)});
        const auto& gateResult = Success(gates);
        Scalar(gateResult, OPENUSD_MDL_SURFACE_OPACITY, {0.44F}, OPENUSD_MDL_ORIGIN_AUTHORED);
        Scalar(gateResult, OPENUSD_MDL_SURFACE_EMISSIVE_COLOR, {0.24F, 0.35F, 0.46F},
            OPENUSD_MDL_ORIGIN_AUTHORED);
    });
    probe.Run("AuthoredTextureColorSpaceWins", [&] {
        const std::string asset = (textures / "normal.ppm").u8string();
        const std::string srgb = "srgb";
        auto metadata = Asset("colorSpace:diffuse_texture", srgb);
        metadata.kind = OPENUSD_MDL_VALUE_STRING;
        auto value = Distill(adapter.get(), variants, "textured",
            {Asset("diffuse_texture", asset), metadata});
        Texture(Success(value), OPENUSD_MDL_SURFACE_DIFFUSE_COLOR, fs::u8path(asset),
            OPENUSD_MDL_CHANNEL_RGB, OPENUSD_MDL_COLOR_SPACE_SRGB, OPENUSD_MDL_ORIGIN_AUTHORED);
    });
    probe.Run("EmptyDefaultTexturesAreAbsenceNotMissingFiles", [&] {
        auto value = Distill(adapter.get(), variants, "constants");
        const auto& result = Success(value);
        Require(result.texture_count == 0, "an unset texture was materialized");
        for (const char* name : {"diffuse_texture", "reflectionroughness_texture", "metallic_texture",
                 "ORM_texture", "normalmap_texture", "ao_texture", "emissive_color_texture",
                 "emissive_mask_texture", "detail_normalmap_texture", "opacity_texture"})
        {
            Require(!Reported(result, name), std::string("unset default reported as missing: ") + name);
        }
        const std::string empty;
        auto authored = Distill(adapter.get(), variants, "constants", {Asset("diffuse_texture", empty)});
        Require(Reported(Success(authored), "diffuse_texture"),
            "the absence exception incorrectly swallowed an empty authored asset");
    });
    probe.Run("NonemptyMissingTextureIsNamed", [&] {
        auto value = Distill(adapter.get(), "wrappers\\missing_texture.mdl", "missing_texture");
        const auto& result = Success(value);
        Require(result.texture_count == 0 && Reported(result, "diffuse_texture") &&
            Text(result.diagnostic).find("absent-on-purpose.png") != std::string::npos,
            "an unresolved nonempty texture was silently treated as an empty default");
    });
    probe.Run("AbsoluteUrisAndSpacesDoNotCrossPollute", [&] {
        const std::string first = (fixtures / "wrappers" / "Asset One" / "paint wrapper.mdl").u8string();
        const std::string second = (fixtures / "wrappers" / "Asset Two" / "paint wrapper.mdl").u8string();
        auto a = Distill(adapter.get(), first, "paint");
        auto b = Distill(adapter.get(), second, "paint");
        auto again = Distill(adapter.get(), first, "paint");
        Scalar(Success(a), OPENUSD_MDL_SURFACE_DIFFUSE_COLOR, {0.11F, 0.22F, 0.33F});
        Scalar(Success(b), OPENUSD_MDL_SURFACE_DIFFUSE_COLOR, {0.61F, 0.72F, 0.83F});
        Scalar(Success(again), OPENUSD_MDL_SURFACE_ROUGHNESS, {0.27F});
        auto relative = Distill(adapter.get(), "wrappers\\Asset Two\\paint wrapper.mdl", "paint");
        Scalar(Success(relative), OPENUSD_MDL_SURFACE_ROUGHNESS, {0.87F});
    });
    probe.Run("NestedKnownVariantsRetainNamedOverrides", [&] {
        auto value = Distill(adapter.get(), variants, "chain_8");
        const auto& result = Success(value);
        Scalar(result, OPENUSD_MDL_SURFACE_DIFFUSE_COLOR, {0.12F, 0.34F, 0.56F});
        Scalar(result, OPENUSD_MDL_SURFACE_ROUGHNESS, {0.68F});
        Scalar(result, OPENUSD_MDL_SURFACE_METALLIC, {0.31F});
        // The SDK normalizes these source chains to a single proven prototype link.
        auto normalized = Distill(adapter.get(), variants, "chain_9");
        Scalar(Success(normalized), OPENUSD_MDL_SURFACE_ROUGHNESS, {0.79F});
    });
    probe.Run("GlassVariantUsesOnlyGlassMapping", [&] {
        auto value = Distill(adapter.get(), variants, "glass");
        const auto& result = Success(value);
        Scalar(result, OPENUSD_MDL_SURFACE_DIFFUSE_COLOR, {0.32F, 0.57F, 0.81F});
        Scalar(result, OPENUSD_MDL_SURFACE_IOR, {1.64F});
        Scalar(result, OPENUSD_MDL_SURFACE_ROUGHNESS, {0.16F});
        Scalar(result, OPENUSD_MDL_SURFACE_OPACITY, {0.2F});
        Require(result.scalar_count == 4 && result.texture_count == 0, "wrong glass output shape");
        auto overridden = Distill(adapter.get(), variants, "glass", {Number("glass_ior", 1.72F)});
        Scalar(Success(overridden), OPENUSD_MDL_SURFACE_IOR, {1.72F}, OPENUSD_MDL_ORIGIN_AUTHORED);
    });
    probe.Run("UnsupportedCallsAndModifiersAreNamed", [&] {
        auto value = Distill(adapter.get(), variants, "unsupported_call");
        const auto& result = Success(value);
        Require(Reported(result, "reflection_roughness_constant") &&
            Text(result.diagnostic).find("math::abs") != std::string::npos,
            "unsupported SDK call was not named");
        Require(result.scalar_count == 2, "unevaluated roughness was replaced by a base default");
        auto overridden = Distill(adapter.get(), variants, "unsupported_call",
            {Number("reflection_roughness_constant", 0.92F)});
        const auto& replacement = Success(overridden);
        Scalar(replacement, OPENUSD_MDL_SURFACE_ROUGHNESS, {0.92F}, OPENUSD_MDL_ORIGIN_AUTHORED);
        Require(!Reported(replacement, "reflection_roughness_constant") &&
            Text(replacement.diagnostic).find("math::abs") == std::string::npos,
            "overridden expression was still reported as dropped");
        auto modifier = Distill(adapter.get(), variants, "unsupported_modifier");
        const auto& partial = Success(modifier);
        Require(Reported(partial, "diffuse_tint") && Reported(partial, "texture_scale"),
            "unmapped named root arguments silently vanished");
        auto body = Distill(adapter.get(), variants, "unproven_body");
        Failure(body, OPENUSD_MDL_STATUS_EXPRESSION_UNSUPPORTED, "unproven_body");
    });
    probe.Run("ResultOwnershipRepeatReleaseAndReconfiguration", [&] {
        auto value = Distill(adapter.get(), variants, "textured");
        auto other = Create(roots, 2);
        openusd_mdl_adapter_release_result(other.get(), value.result.get());
        Texture(Success(value), OPENUSD_MDL_SURFACE_NORMAL, textures / "normal.ppm",
            OPENUSD_MDL_CHANNEL_RGB, OPENUSD_MDL_COLOR_SPACE_RAW);
        Require(Configure(adapter.get(), roots, 3) == OPENUSD_MDL_STATUS_OK, "reconfigure failed");
        Texture(Success(value), OPENUSD_MDL_SURFACE_DIFFUSE_COLOR, textures / "albedo.ppm",
            OPENUSD_MDL_CHANNEL_RGB, OPENUSD_MDL_COLOR_SPACE_RAW);
        const auto* released = value.result.release();
        openusd_mdl_adapter_release_result(adapter.get(), released);
        openusd_mdl_adapter_release_result(adapter.get(), released);
        for (unsigned int repeat = 0; repeat < 12; ++repeat)
        {
            auto current = Distill(adapter.get(), variants, "constants");
            Scalar(Success(current), OPENUSD_MDL_SURFACE_ROUGHNESS, {0.21F});
        }
    });
    probe.Run("CacheGenerationAndAdapterConfigurationsAreIsolated", [&] {
        const fs::path a = work / "configuration-a" / "mutable_variant.mdl";
        const fs::path b = work / "configuration-b" / "mutable_variant.mdl";
        Write(a, MutableVariant("0.26"));
        Write(b, MutableVariant("0.86"));
        const std::vector<std::string> rootsA{core.u8string(), a.parent_path().u8string()};
        const std::vector<std::string> rootsB{core.u8string(), b.parent_path().u8string()};
        auto first = Create(rootsA, 101);
        auto before = Distill(first.get(), "mutable_variant.mdl", "mutable_variant");
        Scalar(Success(before), OPENUSD_MDL_SURFACE_ROUGHNESS, {0.26F});
        Write(a, MutableVariant("0.46"));
        auto cached = Distill(first.get(), "mutable_variant.mdl", "mutable_variant");
        Scalar(Success(cached), OPENUSD_MDL_SURFACE_ROUGHNESS, {0.26F});
        Require(Configure(first.get(), rootsA, 102) == OPENUSD_MDL_STATUS_OK, "generation change failed");
        auto after = Distill(first.get(), "mutable_variant.mdl", "mutable_variant");
        Scalar(Success(after), OPENUSD_MDL_SURFACE_ROUGHNESS, {0.46F});
        auto second = Create(rootsB, 102);
        auto fromB = Distill(second.get(), "mutable_variant.mdl", "mutable_variant");
        Scalar(Success(fromB), OPENUSD_MDL_SURFACE_ROUGHNESS, {0.86F});
        auto fromA = Distill(first.get(), "mutable_variant.mdl", "mutable_variant");
        Scalar(Success(fromA), OPENUSD_MDL_SURFACE_ROUGHNESS, {0.46F});
        const std::vector<std::string> both{core.u8string(), a.parent_path().u8string(),
            b.parent_path().u8string()};
        Require(Configure(first.get(), both, 103) == OPENUSD_MDL_STATUS_OK, "multi-root config failed");
        auto absoluteB = Distill(first.get(), b.u8string(), "mutable_variant");
        Scalar(Success(absoluteB), OPENUSD_MDL_SURFACE_ROUGHNESS, {0.86F});
        auto qualifiedA = Distill(first.get(), "::mutable_variant", "mutable_variant");
        Scalar(Success(qualifiedA), OPENUSD_MDL_SURFACE_ROUGHNESS, {0.46F});
        auto relativeA = Distill(first.get(), "mutable_variant.mdl", "mutable_variant");
        Scalar(Success(relativeA), OPENUSD_MDL_SURFACE_ROUGHNESS, {0.46F});
        Require(fs::remove(a) && fs::remove(b), "probe-owned mutable fixtures were not removed");
        fs::remove(a.parent_path());
        fs::remove(b.parent_path());
    });
}

void Warehouse(Probe& probe, const fs::path& core, const fs::path& source)
{
    auto adapter = Create({core.u8string(), source.u8string()});
    probe.Run("RealWarehouseBodyConstantsAndParentRelativeTextures", [&] {
        const fs::path directory = source / "Props" / "modular" / "Modular_Warehouse" /
            "Library" / "Material Library" / "Metal" / "Painted";
        const std::string name = "opaque__metal__painted_gray_matte_a";
        auto value = Distill(adapter.get(), (directory / (name + ".mdl")).u8string(), name);
        const auto& result = Success(value);
        Scalar(result, OPENUSD_MDL_SURFACE_DIFFUSE_COLOR, {0.2F, 0.2F, 0.2F});
        Scalar(result, OPENUSD_MDL_SURFACE_ROUGHNESS, {0.19F});
        Scalar(result, OPENUSD_MDL_SURFACE_METALLIC, {0.0F});
        const fs::path textures = directory.parent_path() / "Textures";
        Require(result.texture_count == 5, "actual named MDL texture overrides were lost");
        Texture(result, OPENUSD_MDL_SURFACE_DIFFUSE_COLOR, textures / "T_MetalPainted_Gray_Worn_Albedo.png",
            OPENUSD_MDL_CHANNEL_RGB, OPENUSD_MDL_COLOR_SPACE_SRGB);
        Texture(result, OPENUSD_MDL_SURFACE_OCCLUSION, textures / "T_metalpainted_gray_metallic_a_ORM.png",
            OPENUSD_MDL_CHANNEL_R, OPENUSD_MDL_COLOR_SPACE_RAW);
        Texture(result, OPENUSD_MDL_SURFACE_ROUGHNESS, textures / "T_metalpainted_gray_metallic_a_ORM.png",
            OPENUSD_MDL_CHANNEL_G, OPENUSD_MDL_COLOR_SPACE_RAW);
        Texture(result, OPENUSD_MDL_SURFACE_METALLIC, textures / "T_metalpainted_gray_metallic_a_ORM.png",
            OPENUSD_MDL_CHANNEL_B, OPENUSD_MDL_COLOR_SPACE_RAW);
        Texture(result, OPENUSD_MDL_SURFACE_NORMAL, textures / "T_Aluminium_Raw_A_Normal.png",
            OPENUSD_MDL_CHANNEL_RGB, OPENUSD_MDL_COLOR_SPACE_RAW);
        Require(Reported(result, "texture_scale") && Reported(result, "diffuse_tint"),
            "actual unsupported root modifiers were not named");
        ReportWrapper(name, result);
    });
    probe.Run("RealWarehouseMetalGlossyLocalTextures", [&] {
        const fs::path directory = source / "Props" / "general" /
            "SM_ConvertibleSteelHandTruck_A01_Red_01" / "materials";
        auto value = Distill(adapter.get(), (directory / "Metal_Glossy_A.mdl").u8string(), "Metal_Glossy_A");
        const auto& result = Success(value);
        Scalar(result, OPENUSD_MDL_SURFACE_DIFFUSE_COLOR, {0.2F, 0.2F, 0.2F});
        Scalar(result, OPENUSD_MDL_SURFACE_ROUGHNESS, {0.5F});
        const fs::path textures = directory / "textures" / "Metal_Glossy_A";
        Require(result.scalar_count == 3 && result.texture_count == 5,
            "actual OmniPBR wrapper gained unrelated outputs or lost textures");
        Texture(result, OPENUSD_MDL_SURFACE_DIFFUSE_COLOR, textures / "T_Metal_Glossy_A_Albedo_1.png",
            OPENUSD_MDL_CHANNEL_RGB, OPENUSD_MDL_COLOR_SPACE_SRGB);
        Texture(result, OPENUSD_MDL_SURFACE_ROUGHNESS, textures / "T_Metal_Glossy_A_ORM_1.png",
            OPENUSD_MDL_CHANNEL_G, OPENUSD_MDL_COLOR_SPACE_RAW);
        Texture(result, OPENUSD_MDL_SURFACE_NORMAL, textures / "T_Metal_Glossy_A_Normal_1.png",
            OPENUSD_MDL_CHANNEL_RGB, OPENUSD_MDL_COLOR_SPACE_RAW);
        Require(Reported(result, "texture_scale"), "actual UV scale was silently ignored");
        ReportWrapper("Metal_Glossy_A", result);
    });
}
}

int main(int argc, char** argv)
{
    if (argc < 3)
    {
        std::cerr << "Usage: mdl_wrapper_probe --rejections <fixtures> | "
            "--synthetic <fixtures> <real-core> <work> | --warehouse <real-core> <source>\n";
        return 2;
    }
    if (openusd_mdl_abi_version() != OPENUSD_MDL_ABI_VERSION ||
        (openusd_mdl_capabilities() & OPENUSD_MDL_CAPABILITY_MODULE_DEFAULTS) == 0)
    {
        std::cerr << "A matching SDK adapter is required; this probe never substitutes or skips it.\n";
        return 2;
    }
    Probe probe;
    try
    {
        const std::string mode = argv[1];
        if (mode == "--rejections" && argc == 3)
        {
            Rejections(probe, fs::u8path(argv[2]));
        }
        else if (mode == "--synthetic" && argc == 5)
        {
            Synthetic(probe, fs::u8path(argv[2]), fs::u8path(argv[3]), fs::u8path(argv[4]));
        }
        else if (mode == "--warehouse" && argc == 4)
        {
            Warehouse(probe, fs::u8path(argv[2]), fs::u8path(argv[3]));
        }
        else
        {
            std::cerr << "Invalid probe mode or argument count.\n";
            return 2;
        }
    }
    catch (const std::exception& error)
    {
        std::cerr << "Probe setup failed: " << error.what() << '\n';
        return 1;
    }
    std::cout << "MDL_WRAPPER_PROBE passed=" << probe.passed << " failed=" << probe.failed << '\n';
    return probe.failed == 0 && probe.passed != 0 ? 0 : 1;
}
