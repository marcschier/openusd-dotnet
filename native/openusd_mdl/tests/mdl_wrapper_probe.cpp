// Copyright (c) marcschier. Licensed under the MIT License.

#include "openusd_mdl.h"

#include <algorithm>
#include <cmath>
#include <cstring>
#include <filesystem>
#include <fstream>
#include <initializer_list>
#include <iostream>
#include <limits>
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
    uint32_t origin = OPENUSD_MDL_ORIGIN_MODULE_DEFAULT,
    std::initializer_list<float> expected_scale = {},
    std::initializer_list<float> expected_bias = {},
    uint32_t composite = OPENUSD_MDL_COMPOSITE_NONE)
{
    const openusd_mdl_distilled_texture* found = nullptr;
    for (uint32_t index = 0; index < value.texture_count; ++index)
    {
        if (value.textures[index].surface_input == input && value.textures[index].composite_operator == composite)
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
        const float scale = expected_scale.size() == 4 ? *(expected_scale.begin() + component)
            : input == OPENUSD_MDL_SURFACE_NORMAL && component == 1 ? -1.0F : 1.0F;
        const float bias = expected_bias.size() == 4 ? *(expected_bias.begin() + component)
            : input == OPENUSD_MDL_SURFACE_NORMAL && component == 1 ? 1.0F : 0.0F;
        Require((expected_scale.size() == 0 || expected_scale.size() == 4) &&
            (expected_bias.size() == 0 || expected_bias.size() == 4) &&
            std::fabs(found->scale[component] - scale) < 0.00001F &&
            std::fabs(found->bias[component] - bias) < 0.00001F,
            "unexpected texture scale/bias");
    }
}

void UvTransform(const openusd_mdl_distilled_material& result, std::initializer_list<float> expected,
    uint32_t origin = OPENUSD_MDL_ORIGIN_MODULE_DEFAULT)
{
    Require(result.has_uv_transform == 1 && expected.size() == 6 && result.uv_transform_origin == origin,
        "missing UV transform or wrong provenance");
    size_t index = 0;
    for (float value : expected)
    {
        Require(std::isfinite(result.uv_transform[index]) &&
            std::fabs(result.uv_transform[index] - value) < 0.00001F, "wrong UV affine value");
        ++index;
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

openusd_mdl_parameter Pair(const char* name, float x, float y)
{
    auto result = Number(name, x);
    result.kind = OPENUSD_MDL_VALUE_FLOAT2;
    result.component_count = 2;
    result.value[1] = y;
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
    probe.Run("ExactSdkMaterialSignatureResolvesWithoutDiscardingTypes", [&] {
        auto value = Distill(adapter.get(), "rejections.mdl", "unrelated(color,float)");
        const auto& result = Success(value);
        Scalar(result, OPENUSD_MDL_SURFACE_DIFFUSE_COLOR, {0.9F, 0.8F, 0.7F});
        Scalar(result, OPENUSD_MDL_SURFACE_ROUGHNESS, {0.15F});
    });
    for (const char* name : {"unrelated(color,int)", "unrelated(color)", "unrelated(color,float)extra"})
    {
        probe.Run(name, [&] {
            auto value = Distill(adapter.get(), "rejections.mdl", name);
            Failure(value, OPENUSD_MDL_STATUS_UNSUPPORTED_MATERIAL, "declares no material");
        });
    }
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
        Scalar(result, OPENUSD_MDL_SURFACE_OMNIPBR_SPECULAR_LEVEL, {0.5F});
        Require(result.scalar_count == 4 && result.texture_count == 0,
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
        auto value = Distill(adapter.get(), variants, "textured", {Number("ao_to_diffuse", 1.0F)});
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
    probe.Run("DetailOnlyNormalUsesItsOwnStrengthAndPreservesMetadata", [&] {
        auto value = Distill(adapter.get(), variants, "detail_only");
        const auto& result = Success(value);
        Texture(result, OPENUSD_MDL_SURFACE_NORMAL, textures / "normal.ppm",
            OPENUSD_MDL_CHANNEL_RGB, OPENUSD_MDL_COLOR_SPACE_RAW, OPENUSD_MDL_ORIGIN_MODULE_DEFAULT,
            {0.3F, -0.3F, 1, 1}, {0.35F, 0.65F, 0, 0});
        Require(!Reported(result, "detail_normalmap_texture") && !Reported(result, "detail_bump_factor"),
            "an exact neutral-base detail normal was reported as dropped");
    });
    probe.Run("ZeroStrengthBaseDoesNotDiscardItsActiveDetail", [&] {
        auto value = Distill(adapter.get(), variants, "detail_on_zero_base");
        Texture(Success(value), OPENUSD_MDL_SURFACE_NORMAL, textures / "normal.ppm",
            OPENUSD_MDL_CHANNEL_RGB, OPENUSD_MDL_COLOR_SPACE_RAW, OPENUSD_MDL_ORIGIN_MODULE_DEFAULT,
            {0.3F, -0.3F, 1, 1}, {0.35F, 0.65F, 0, 0});
    });
    probe.Run("DetailOnlyIndependentUvIsExactWhenNoOtherMapUsesTheAffine", [&] {
        auto value = Distill(adapter.get(), variants, "detail_only",
            {Pair("detail_texture_scale", 2, 3), Number("detail_texture_rotate", 90),
             Pair("detail_texture_translate", 0.2F, -0.4F)});
        const auto& result = Success(value);
        Require(result.texture_count == 1, "detail map was lost");
        UvTransform(result, {0, 2, -3, 0, 0.2F, -0.4F}, OPENUSD_MDL_ORIGIN_AUTHORED);
    });
    probe.Run("CompatibleDiffuseAndDetailPreserveBothMapsAndSharedUv", [&] {
        auto value = Distill(adapter.get(), variants, "detail_with_diffuse",
            {Pair("texture_scale", 2, 3), Pair("detail_texture_scale", 2, 3)});
        const auto& result = Success(value);
        Require(result.texture_count == 2 && !Reported(result, "detail_normalmap_texture"),
            "a compatible detail normal displaced or lost the existing diffuse map");
        Texture(result, OPENUSD_MDL_SURFACE_DIFFUSE_COLOR, textures / "albedo.ppm",
            OPENUSD_MDL_CHANNEL_RGB, OPENUSD_MDL_COLOR_SPACE_RAW);
        Texture(result, OPENUSD_MDL_SURFACE_NORMAL, textures / "normal.ppm",
            OPENUSD_MDL_CHANNEL_RGB, OPENUSD_MDL_COLOR_SPACE_RAW, OPENUSD_MDL_ORIGIN_MODULE_DEFAULT,
            {0.3F, -0.3F, 1, 1}, {0.35F, 0.65F, 0, 0});
        UvTransform(result, {2, 0, 0, 3, 0, 0}, OPENUSD_MDL_ORIGIN_AUTHORED);
    });
    probe.Run("ActiveBaseAndIndependentDetailUvsPreserveEveryMap", [&] {
        auto active = Distill(adapter.get(), variants, "detail_on_active_base");
        Texture(Success(active), OPENUSD_MDL_SURFACE_NORMAL, textures / "normal.ppm",
            OPENUSD_MDL_CHANNEL_RGB, OPENUSD_MDL_COLOR_SPACE_RAW, OPENUSD_MDL_ORIGIN_MODULE_DEFAULT,
            {0.6F, -0.6F, 1, 1}, {0.2F, 0.8F, 0, 0});
        Texture(Success(active), OPENUSD_MDL_SURFACE_DETAIL_NORMAL, textures / "normal.ppm",
            OPENUSD_MDL_CHANNEL_RGB, OPENUSD_MDL_COLOR_SPACE_RAW, OPENUSD_MDL_ORIGIN_MODULE_DEFAULT,
            {0.3F, -0.3F, 1, 1}, {0.35F, 0.65F, 0, 0});
        auto divergent = Distill(adapter.get(), variants, "detail_with_diffuse",
            {Pair("detail_texture_scale", 3, 2)});
        const auto& result = Success(divergent);
        Require(result.texture_count == 2 && !Reported(result, "detail_normalmap_texture") &&
            !Reported(result, "detail_bump_factor"),
            "independent detail coordinates lost a material input");
        Scalar(result, OPENUSD_MDL_SURFACE_DETAIL_UV_ROW0, {3, 0, 0}, OPENUSD_MDL_ORIGIN_AUTHORED);
        Scalar(result, OPENUSD_MDL_SURFACE_DETAIL_UV_ROW1, {0, 2, 0}, OPENUSD_MDL_ORIGIN_AUTHORED);
        UvTransform(result, {1, 0, 0, 1, 0, 0});
    });
    probe.Run("TwoActiveNormalsKeepSeparateTextures", [&] {
        auto value = Distill(adapter.get(), variants, "detail_on_active_base");
        const auto& result = Success(value);
        Require(result.texture_count == 2,
            "two active normal maps must both reach the renderer");
        Require(!Reported(result, "detail_normalmap_texture") &&
            !Reported(result, "detail_bump_factor"),
            "a supported detail normal must not remain a dropped input");
        Texture(result, OPENUSD_MDL_SURFACE_NORMAL, textures / "normal.ppm",
            OPENUSD_MDL_CHANNEL_RGB, OPENUSD_MDL_COLOR_SPACE_RAW, OPENUSD_MDL_ORIGIN_MODULE_DEFAULT,
            {0.6F, -0.6F, 1, 1}, {0.2F, 0.8F, 0, 0});
        Texture(result, OPENUSD_MDL_SURFACE_DETAIL_NORMAL, textures / "normal.ppm",
            OPENUSD_MDL_CHANNEL_RGB, OPENUSD_MDL_COLOR_SPACE_RAW, OPENUSD_MDL_ORIGIN_MODULE_DEFAULT,
            {0.3F, -0.3F, 1, 1}, {0.35F, 0.65F, 0, 0});
        Scalar(result, OPENUSD_MDL_SURFACE_DETAIL_UV_ROW0, {1, 0, 0});
        Scalar(result, OPENUSD_MDL_SURFACE_DETAIL_UV_ROW1, {0, 1, 0});
        for (uint32_t index = 0; index < result.texture_count; ++index)
        {
            Require(result.textures[index].composite_operator == OPENUSD_MDL_COMPOSITE_NONE,
                "detail normals must not consume the emission composite slot");
        }
    });
    probe.Run("NeutralBaseDetailValidatesStrengthAndKeepsZeroNeutral", [&] {
        auto zero = Distill(adapter.get(), variants, "detail_only", {Number("detail_bump_factor", 0)});
        Require(Success(zero).texture_count == 0, "zero detail strength introduced a normal map");
        auto invalid = Distill(adapter.get(), variants, "detail_only",
            {Number("detail_bump_factor", std::numeric_limits<float>::infinity())});
        Require(Success(invalid).texture_count == 0 && Reported(Success(invalid), "detail_bump_factor"),
            "invalid detail strength was accepted or silently defaulted");
    });
    probe.Run("NeutralBaseDetailKeepsSignedStrengthAndTangentFlips", [&] {
        auto value = Distill(adapter.get(), variants, "detail_only",
            {Number("detail_bump_factor", -2.0F),
             Boolean("flip_tangent_u", true), Boolean("flip_tangent_v", false)});
        Texture(Success(value), OPENUSD_MDL_SURFACE_NORMAL, textures / "normal.ppm",
            OPENUSD_MDL_CHANNEL_RGB, OPENUSD_MDL_COLOR_SPACE_RAW, OPENUSD_MDL_ORIGIN_MODULE_DEFAULT,
            {2, -2, 1, 1}, {-0.5F, 1.5F, 0, 0});
    });
    probe.Run("UnresolvedBaseAndUnsupportedCoordinatesCannotBecomeDetailOnly", [&] {
        auto unresolved = Distill(adapter.get(), variants, "detail_on_unresolved_base");
        Require(Success(unresolved).texture_count == 0 &&
            Reported(Success(unresolved), "detail_normalmap_texture"),
            "an unresolved base was treated as a proven neutral normal");
        auto projected = Distill(adapter.get(), variants, "detail_only", {Boolean("project_uvw", true)});
        Require(Success(projected).texture_count == 0 &&
            Reported(Success(projected), "project_uvw") &&
            Reported(Success(projected), "detail_normalmap_texture"),
            "projected coordinates silently became authored UVs");
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
        constexpr float inversePi = 0.31830988618379067154F;
        Scalar(gateResult, OPENUSD_MDL_SURFACE_EMISSIVE_COLOR,
            {0.24F * 40 * inversePi, 0.35F * 40 * inversePi, 0.46F * 40 * inversePi},
            OPENUSD_MDL_ORIGIN_AUTHORED);
    });
    probe.Run("ProvenDiffuseEmissionProjectsRadianceAndExplicitMaskMultiplication", [&] {
        constexpr float inversePi = 0.31830988618379067154F;
        auto constant = Distill(adapter.get(), variants, "emission");
        Scalar(Success(constant), OPENUSD_MDL_SURFACE_EMISSIVE_COLOR,
            {inversePi, 2 * inversePi, 4 * inversePi}, OPENUSD_MDL_ORIGIN_MODULE_DEFAULT);
        Require(!Reported(Success(constant), "emissive_intensity"), "proven exitance was not projected to radiance");
        auto authored = Distill(adapter.get(), variants, "emission", {Number("emissive_intensity", 8)});
        Scalar(Success(authored), OPENUSD_MDL_SURFACE_EMISSIVE_COLOR,
            {2 * inversePi, 4 * inversePi, 8 * inversePi}, OPENUSD_MDL_ORIGIN_AUTHORED);
        auto image = Distill(adapter.get(), variants, "emission_texture");
        Texture(Success(image), OPENUSD_MDL_SURFACE_EMISSIVE_COLOR, textures / "albedo.ppm",
            OPENUSD_MDL_CHANNEL_RGB, OPENUSD_MDL_COLOR_SPACE_RAW, OPENUSD_MDL_ORIGIN_MODULE_DEFAULT,
            {4 * inversePi, 4 * inversePi, 4 * inversePi, 1});
        auto mask = Distill(adapter.get(), variants, "emission_mask");
        Texture(Success(mask), OPENUSD_MDL_SURFACE_EMISSIVE_COLOR, textures / "orm.ppm",
            OPENUSD_MDL_CHANNEL_RGB, OPENUSD_MDL_COLOR_SPACE_RAW, OPENUSD_MDL_ORIGIN_MODULE_DEFAULT,
            {inversePi, 2 * inversePi, 4 * inversePi, 1});
        auto both = Distill(adapter.get(), variants, "emission_both");
        Require(Success(both).texture_count == 2, "emission masking did not retain both owned operands");
        Texture(Success(both), OPENUSD_MDL_SURFACE_EMISSIVE_COLOR, textures / "albedo.ppm",
            OPENUSD_MDL_CHANNEL_RGB, OPENUSD_MDL_COLOR_SPACE_RAW, OPENUSD_MDL_ORIGIN_MODULE_DEFAULT,
            {4 * inversePi, 4 * inversePi, 4 * inversePi, 1});
        Texture(Success(both), OPENUSD_MDL_SURFACE_EMISSIVE_COLOR, textures / "orm.ppm",
            OPENUSD_MDL_CHANNEL_RGB, OPENUSD_MDL_COLOR_SPACE_RAW, OPENUSD_MDL_ORIGIN_MODULE_DEFAULT,
            {}, {}, OPENUSD_MDL_COMPOSITE_MULTIPLY);
        auto off = Distill(adapter.get(), variants, "emission_both", {Boolean("enable_emission", false)});
        Require(Success(off).texture_count == 0, "disabled emission allocated images");
        auto zero = Distill(adapter.get(), variants, "emission_both", {Number("emissive_intensity", 0)});
        Scalar(Success(zero), OPENUSD_MDL_SURFACE_EMISSIVE_COLOR, {0, 0, 0}, OPENUSD_MDL_ORIGIN_AUTHORED);
        Require(Success(zero).texture_count == 0, "zero emission retained inactive images");
        for (float invalid : {-0.1F, std::numeric_limits<float>::infinity(),
            std::numeric_limits<float>::quiet_NaN()})
        {
            auto refused = Distill(adapter.get(), variants, "emission", {Number("emissive_intensity", invalid)});
            Require(Reported(Success(refused), "emissive_intensity"), "invalid emission intensity was hidden");
        }
        auto overflow = Distill(adapter.get(), variants, "emission",
            {Color("emissive_color", std::numeric_limits<float>::max(), 1, 1)});
        Require(Reported(Success(overflow), "emissive_color"), "overflowed exitance was silently projected");
    });
    probe.Run("OpacityModeDefaultsAndAuthoredEnumsPreserveChannelsAndGates", [&] {
        const uint32_t channels[] = {OPENUSD_MDL_CHANNEL_A, OPENUSD_MDL_CHANNEL_AVERAGE_RGB,
            OPENUSD_MDL_CHANNEL_LUMINANCE_RGB, OPENUSD_MDL_CHANNEL_MAXIMUM_RGB};
        auto defaults = Distill(adapter.get(), variants, "opacity_modes");
        Texture(Success(defaults), OPENUSD_MDL_SURFACE_OPACITY, textures / "albedo.ppm",
            OPENUSD_MDL_CHANNEL_AVERAGE_RGB, OPENUSD_MDL_COLOR_SPACE_RAW);
        for (int index = 0; index < 4; ++index)
        {
            auto mode = Number("opacity_mode", 0);
            mode.kind = OPENUSD_MDL_VALUE_INT;
            mode.integer_value = index;
            auto projected = Distill(adapter.get(), variants, "opacity_modes",
                {mode, Number("opacity_threshold", 0.5F)});
            Texture(Success(projected), OPENUSD_MDL_SURFACE_OPACITY, textures / "albedo.ppm",
                channels[index], OPENUSD_MDL_COLOR_SPACE_RAW);
            Scalar(Success(projected), OPENUSD_MDL_SURFACE_OPACITY_THRESHOLD, {0.5F},
                OPENUSD_MDL_ORIGIN_AUTHORED);
            Require(!Reported(Success(projected), "opacity_mode"), "known opacity mode was refused");
        }
        for (const char* gate : {"enable_opacity", "enable_opacity_texture"})
        {
            auto disabled = Distill(adapter.get(), variants, "opacity_modes", {Boolean(gate, false)});
            Require(Success(disabled).texture_count == 0, "an inactive opacity map was published");
        }
        for (int invalid : {-1, 4})
        {
            auto mode = Number("opacity_mode", 0);
            mode.kind = OPENUSD_MDL_VALUE_INT;
            mode.integer_value = invalid;
            auto refused = Distill(adapter.get(), variants, "opacity_modes", {mode});
            Require(Reported(Success(refused), "opacity_mode") && Success(refused).texture_count == 0,
                "an invalid enum was guessed as a known opacity mode");
        }
        auto malformed = Distill(adapter.get(), variants, "opacity_modes", {Number("opacity_mode", 1.5F)});
        Require(Reported(Success(malformed), "opacity_mode"), "fractional opacity enum was narrowed");
        auto missing = Distill(adapter.get(), variants, "constants",
            {Boolean("enable_opacity", true), Boolean("enable_opacity_texture", true)});
        Require(Reported(Success(missing), "opacity_texture") && Success(missing).texture_count == 0,
            "an enabled unavailable opacity map silently became a constant");
        for (const char* control : {"opacity_constant", "opacity_threshold"})
        {
            for (float invalid : {-0.1F, 1.1F, std::numeric_limits<float>::quiet_NaN()})
            {
                auto refused = Distill(adapter.get(), variants, "opacity_modes", {Number(control, invalid)});
                Require(Reported(Success(refused), control), "invalid opacity control was silently applied");
            }
        }
    });
    probe.Run("ScalarTextureInfluencePreservesZeroFractionalFullAndOrmPriority", [&] {
        auto fractional = Distill(adapter.get(), variants, "influenced", {
            Number("reflection_roughness_constant", 0.2F),
            Number("reflection_roughness_texture_influence", 0.25F),
            Number("metallic_constant", 0.8F), Number("metallic_texture_influence", 0.5F)});
        const auto& direct = Success(fractional);
        Require(direct.texture_count == 2, "fractional maps were dropped");
        Texture(direct, OPENUSD_MDL_SURFACE_ROUGHNESS, textures / "orm.ppm",
            OPENUSD_MDL_CHANNEL_R, OPENUSD_MDL_COLOR_SPACE_RAW, OPENUSD_MDL_ORIGIN_MODULE_DEFAULT,
            {0.25F, 1, 1, 1}, {0.15F, 0, 0, 0});
        Texture(direct, OPENUSD_MDL_SURFACE_METALLIC, textures / "orm.ppm",
            OPENUSD_MDL_CHANNEL_R, OPENUSD_MDL_COLOR_SPACE_RAW, OPENUSD_MDL_ORIGIN_MODULE_DEFAULT,
            {0.5F, 1, 1, 1}, {0.4F, 0, 0, 0});
        Require(!Reported(direct, "reflection_roughness_texture_influence") &&
            !Reported(direct, "metallic_texture_influence"), "supported fractional controls were reported as lost");
        auto off = Distill(adapter.get(), variants, "influenced", {
            Number("reflection_roughness_texture_influence", 0), Number("metallic_texture_influence", 0)});
        Require(Success(off).texture_count == 0, "zero influence published a texture instead of its constant");
        Scalar(Success(off), OPENUSD_MDL_SURFACE_ROUGHNESS, {0.5F});
        Scalar(Success(off), OPENUSD_MDL_SURFACE_METALLIC, {0});
        const std::string orm_asset = (textures / "orm.ppm").u8string();
        auto packed = Distill(adapter.get(), variants, "influenced", {
            Boolean("enable_ORM_texture", true), Asset("ORM_texture", orm_asset),
            Number("ao_to_diffuse", 1.0F),
            Number("reflection_roughness_constant", 0.2F),
            Number("reflection_roughness_texture_influence", 0.25F),
            Number("metallic_constant", 0.8F), Number("metallic_texture_influence", 0.5F)});
        const auto& orm = Success(packed);
        Require(orm.texture_count == 3, "ORM and standalone maps were both published or an ORM channel was dropped");
        Texture(orm, OPENUSD_MDL_SURFACE_OCCLUSION, textures / "orm.ppm",
            OPENUSD_MDL_CHANNEL_R, OPENUSD_MDL_COLOR_SPACE_RAW, OPENUSD_MDL_ORIGIN_AUTHORED);
        Texture(orm, OPENUSD_MDL_SURFACE_ROUGHNESS, textures / "orm.ppm",
            OPENUSD_MDL_CHANNEL_G, OPENUSD_MDL_COLOR_SPACE_RAW, OPENUSD_MDL_ORIGIN_AUTHORED,
            {1, 0.25F, 1, 1}, {0, 0.15F, 0, 0});
        Texture(orm, OPENUSD_MDL_SURFACE_METALLIC, textures / "orm.ppm",
            OPENUSD_MDL_CHANNEL_B, OPENUSD_MDL_COLOR_SPACE_RAW, OPENUSD_MDL_ORIGIN_AUTHORED,
            {1, 1, 0.5F, 1}, {0, 0, 0.4F, 0});
        for (float outside : {-0.1F, 1.1F,
            std::numeric_limits<float>::infinity(), std::numeric_limits<float>::quiet_NaN()})
        {
            auto invalid = Distill(adapter.get(), variants, "influenced", {
                Number("reflection_roughness_texture_influence", outside),
                Number("metallic_texture_influence", outside)});
            const auto& refused = Success(invalid);
            Require(refused.texture_count == 0 && Reported(refused, "reflection_roughness_texture_influence") &&
                Reported(refused, "metallic_texture_influence"), "invalid texture influence was silently clamped");
        }
    });
    probe.Run("ZeroAoWeightOmitsOnlyTheOrmOcclusionChannel", [&] {
        const std::string orm_asset = (textures / "orm.ppm").u8string();
        auto disabled = Distill(adapter.get(), variants, "influenced", {
            Boolean("enable_ORM_texture", true), Asset("ORM_texture", orm_asset),
            Number("ao_to_diffuse", 0.0F)});
        const auto& result = Success(disabled);
        Require(result.texture_count == 2, "zero AO weight still published occlusion or removed another ORM channel");
        Texture(result, OPENUSD_MDL_SURFACE_ROUGHNESS, textures / "orm.ppm",
            OPENUSD_MDL_CHANNEL_G, OPENUSD_MDL_COLOR_SPACE_RAW, OPENUSD_MDL_ORIGIN_AUTHORED);
        Texture(result, OPENUSD_MDL_SURFACE_METALLIC, textures / "orm.ppm",
            OPENUSD_MDL_CHANNEL_B, OPENUSD_MDL_COLOR_SPACE_RAW, OPENUSD_MDL_ORIGIN_AUTHORED);
        Require(!Reported(result, "ao_to_diffuse"), "zero AO weight was reported unsupported");
        auto inheritedZero = Distill(adapter.get(), variants, "influenced", {
            Boolean("enable_ORM_texture", true), Asset("ORM_texture", orm_asset)});
        Require(Success(inheritedZero).texture_count == 2 && !Reported(Success(inheritedZero), "ao_to_diffuse"),
            "the SDK-proven zero AO default did not suppress the unused occlusion channel");
        for (float weight : {0.25F, 1.0F})
        {
            auto partial = Distill(adapter.get(), variants, "influenced", {
                Boolean("enable_ORM_texture", true), Asset("ORM_texture", orm_asset),
                Number("ao_to_diffuse", weight)});
            Require(Success(partial).texture_count == 3 && Reported(Success(partial), "ao_to_diffuse"),
                "nonzero AO weight was silently claimed as exact diffuse multiplication");
        }
    });
    probe.Run("DiffuseTintMultipliesConstantsAndTextureRgbWithoutChangingAlphaOrAssetOrigin", [&] {
        auto value = Distill(adapter.get(), variants, "constants",
            {Color("diffuse_tint", 0.5F, 0.25F, 1.5F)});
        const auto& constant = Success(value);
        Scalar(constant, OPENUSD_MDL_SURFACE_DIFFUSE_COLOR, {0.06F, 0.085F, 0.84F},
            OPENUSD_MDL_ORIGIN_AUTHORED);
        Require(!Reported(constant, "diffuse_tint"), "supported constant tint was reported as dropped");
        auto textured = Distill(adapter.get(), variants, "textured",
            {Color("diffuse_tint", 0.5F, 0.25F, 0.75F)});
        const auto& texture = Success(textured);
        Scalar(texture, OPENUSD_MDL_SURFACE_DIFFUSE_COLOR, {0.1F, 0.05F, 0.15F},
            OPENUSD_MDL_ORIGIN_AUTHORED);
        Texture(texture, OPENUSD_MDL_SURFACE_DIFFUSE_COLOR, textures / "albedo.ppm",
            OPENUSD_MDL_CHANNEL_RGB, OPENUSD_MDL_COLOR_SPACE_RAW,
            OPENUSD_MDL_ORIGIN_MODULE_DEFAULT, {0.5F, 0.25F, 0.75F, 1});
        Require(!Reported(texture, "diffuse_tint"), "supported texture tint was reported as dropped");
        auto zero = Distill(adapter.get(), variants, "constants", {Color("diffuse_tint", 0, 0, 0)});
        Scalar(Success(zero), OPENUSD_MDL_SURFACE_DIFFUSE_COLOR, {0, 0, 0}, OPENUSD_MDL_ORIGIN_AUTHORED);
        auto scalar_tint = Distill(adapter.get(), variants, "constants", {Number("diffuse_tint", 0.5F)});
        Scalar(Success(scalar_tint), OPENUSD_MDL_SURFACE_DIFFUSE_COLOR,
            {0.06F, 0.17F, 0.28F}, OPENUSD_MDL_ORIGIN_AUTHORED);
        auto invalid = Distill(adapter.get(), variants, "constants", {Boolean("diffuse_tint", true)});
        Require(Reported(Success(invalid), "diffuse_tint"), "a malformed tint was silently accepted");
        Scalar(Success(invalid), OPENUSD_MDL_SURFACE_DIFFUSE_COLOR, {0.12F, 0.34F, 0.56F});
        const float maximum = std::numeric_limits<float>::max();
        auto overflow = Distill(adapter.get(), variants, "constants",
            {Color("diffuse_color_constant", maximum, maximum, maximum), Color("diffuse_tint", 2, 2, 2)});
        Require(Reported(Success(overflow), "diffuse_tint"), "overflowed tint was not diagnosed");
        Scalar(Success(overflow), OPENUSD_MDL_SURFACE_DIFFUSE_COLOR,
            {maximum, maximum, maximum}, OPENUSD_MDL_ORIGIN_AUTHORED);
        for (float outside : {-0.1F, 1.1F})
        {
            auto extended = Distill(adapter.get(), variants, "textured",
                {Color("diffuse_tint", outside, 0.5F, 0.5F)});
            Require(!Reported(Success(extended), "diffuse_tint"), "finite float-texture tint was refused");
            Texture(Success(extended), OPENUSD_MDL_SURFACE_DIFFUSE_COLOR, textures / "albedo.ppm",
                OPENUSD_MDL_CHANNEL_RGB, OPENUSD_MDL_COLOR_SPACE_RAW,
                OPENUSD_MDL_ORIGIN_MODULE_DEFAULT, {outside, 0.5F, 0.5F, 1});
        }
        for (float outside : {std::numeric_limits<float>::infinity(), std::numeric_limits<float>::quiet_NaN()})
        {
            auto clipped = Distill(adapter.get(), variants, "textured",
                {Color("diffuse_tint", outside, 0.5F, 0.5F)});
            Require(Reported(Success(clipped), "diffuse_tint"), "nonfinite texture tint was silently accepted");
            Texture(Success(clipped), OPENUSD_MDL_SURFACE_DIFFUSE_COLOR, textures / "albedo.ppm",
                OPENUSD_MDL_CHANNEL_RGB, OPENUSD_MDL_COLOR_SPACE_RAW);
        }
    });
    probe.Run("AlbedoGradingAppliesBeforeTintOnlyToTexturesAndPreservesAlpha", [&] {
        auto graded = Distill(adapter.get(), variants, "graded");
        Texture(Success(graded), OPENUSD_MDL_SURFACE_DIFFUSE_COLOR, textures / "albedo.ppm",
            OPENUSD_MDL_CHANNEL_RGB, OPENUSD_MDL_COLOR_SPACE_RAW, OPENUSD_MDL_ORIGIN_MODULE_DEFAULT,
            {1, 0.5F, 3, 1}, {-0.125F, -0.0625F, -0.375F, 0});
        Require(!Reported(Success(graded), "albedo_add") && !Reported(Success(graded), "albedo_brightness"),
            "finite texture grading was reported as dropped");
        auto authored = Distill(adapter.get(), variants, "graded",
            {Number("albedo_brightness", -0.5F), Number("albedo_add", 0.125F)});
        Texture(Success(authored), OPENUSD_MDL_SURFACE_DIFFUSE_COLOR, textures / "albedo.ppm",
            OPENUSD_MDL_CHANNEL_RGB, OPENUSD_MDL_COLOR_SPACE_RAW, OPENUSD_MDL_ORIGIN_MODULE_DEFAULT,
            {-0.25F, -0.125F, -0.75F, 1}, {0.0625F, 0.03125F, 0.1875F, 0});
        auto constant = Distill(adapter.get(), variants, "constants",
            {Number("albedo_brightness", 3), Number("albedo_add", -0.5F), Color("diffuse_tint", 2, 2, 2)});
        Scalar(Success(constant), OPENUSD_MDL_SURFACE_DIFFUSE_COLOR,
            {0.24F, 0.68F, 1.12F}, OPENUSD_MDL_ORIGIN_AUTHORED);
        Require(!Reported(Success(constant), "albedo_add") && !Reported(Success(constant), "albedo_brightness"),
            "inactive constant grading was reported as dropped");
        auto zero = Distill(adapter.get(), variants, "graded", {Number("albedo_brightness", 0)});
        Texture(Success(zero), OPENUSD_MDL_SURFACE_DIFFUSE_COLOR, textures / "albedo.ppm",
            OPENUSD_MDL_CHANNEL_RGB, OPENUSD_MDL_COLOR_SPACE_RAW, OPENUSD_MDL_ORIGIN_MODULE_DEFAULT,
            {0, 0, 0, 1}, {-0.125F, -0.0625F, -0.375F, 0});
        for (const char* name : {"albedo_brightness", "albedo_add"})
        {
            for (float invalid : {std::numeric_limits<float>::infinity(),
                std::numeric_limits<float>::quiet_NaN(), std::numeric_limits<float>::max()})
            {
                auto refused = Distill(adapter.get(), variants, "graded", {Number(name, invalid)});
                Require(Reported(Success(refused), "albedo_brightness") && Reported(Success(refused), "albedo_add"),
                    "nonfinite or overflowing grading was silently applied");
                Texture(Success(refused), OPENUSD_MDL_SURFACE_DIFFUSE_COLOR, textures / "albedo.ppm",
                    OPENUSD_MDL_CHANNEL_RGB, OPENUSD_MDL_COLOR_SPACE_RAW, OPENUSD_MDL_ORIGIN_MODULE_DEFAULT,
                    {0.5F, 0.25F, 1.5F, 1});
            }
            auto malformed = Distill(adapter.get(), variants, "graded", {Pair(name, 1, 2)});
            Require(Reported(Success(malformed), name), "malformed grading was silently accepted");
        }
        auto desaturated = Distill(adapter.get(), variants, "graded", {Number("albedo_desaturation", 0.5F)});
        Scalar(Success(desaturated), OPENUSD_MDL_SURFACE_DIFFUSE_TEXTURE_LUMINANCE_MIX,
            {0.212671F, 0.715160F, 0.072169F, 0.5F}, OPENUSD_MDL_ORIGIN_AUTHORED);
        Require(!Reported(Success(desaturated), "albedo_desaturation"),
            "supported desaturation was reported as dropped");
    });
    probe.Run("DesaturationMixRetainsLuminanceWeightsAndRejectsNonfiniteValues", [&] {
        for (float amount : {-0.3F, 0.25F, 1.0F, 1.3F})
        {
            auto mixed = Distill(adapter.get(), variants, "textured", {Number("albedo_desaturation", amount)});
            Scalar(Success(mixed), OPENUSD_MDL_SURFACE_DIFFUSE_TEXTURE_LUMINANCE_MIX,
                {0.212671F, 0.715160F, 0.072169F, amount}, OPENUSD_MDL_ORIGIN_AUTHORED);
            Texture(Success(mixed), OPENUSD_MDL_SURFACE_DIFFUSE_COLOR, textures / "albedo.ppm",
                OPENUSD_MDL_CHANNEL_RGB, OPENUSD_MDL_COLOR_SPACE_RAW);
        }
        for (const char* variant : {"constants", "textured"})
        {
            auto inactive = Distill(adapter.get(), variants, variant, {Number("albedo_desaturation", 0)});
            const auto& result = Success(inactive);
            for (uint32_t index = 0; index < result.scalar_count; ++index)
            {
                Require(result.scalars[index].surface_input != OPENUSD_MDL_SURFACE_DIFFUSE_TEXTURE_LUMINANCE_MIX,
                    "inactive desaturation unnecessarily changes texture storage");
            }
        }
        auto constant = Distill(adapter.get(), variants, "constants", {Number("albedo_desaturation", 1)});
        Scalar(Success(constant), OPENUSD_MDL_SURFACE_DIFFUSE_COLOR, {0.12F, 0.34F, 0.56F});
        Require(!Reported(Success(constant), "albedo_desaturation"), "inactive constant desaturation was reported");
        for (float invalid : {std::numeric_limits<float>::infinity(),
            std::numeric_limits<float>::quiet_NaN()})
        {
            auto refused = Distill(adapter.get(), variants, "textured", {Number("albedo_desaturation", invalid)});
            Require(Reported(Success(refused), "albedo_desaturation"), "invalid desaturation was silently applied");
        }
        auto malformed = Distill(adapter.get(), variants, "textured", {Pair("albedo_desaturation", 1, 2)});
        Require(Reported(Success(malformed), "albedo_desaturation"), "nonscalar desaturation was silently applied");
    });
    probe.Run("AuthoredUvAffinePreservesScaleClockwiseRotationTranslationAndModeRefusal", [&] {
        auto identity = Distill(adapter.get(), variants, "constants");
        UvTransform(Success(identity), {1, 0, 0, 1, 0, 0});
        auto transformed = Distill(adapter.get(), variants, "textured", {
            Pair("texture_scale", 2, 3), Number("texture_rotate", 90), Pair("texture_translate", 0.25F, -0.5F)});
        const auto& result = Success(transformed);
        UvTransform(result, {0, 2, -3, 0, 0.25F, -0.5F}, OPENUSD_MDL_ORIGIN_AUTHORED);
        Require(!Reported(result, "texture_scale") && !Reported(result, "texture_rotate") &&
            !Reported(result, "texture_translate"), "supported UV transform was reported as dropped");
        auto mirrored = Distill(adapter.get(), variants, "constants",
            {Pair("texture_scale", -2, 0), Number("texture_rotate", 360)});
        UvTransform(Success(mirrored), {-2, 0, 0, 0, 0, 0}, OPENUSD_MDL_ORIGIN_AUTHORED);
        auto projected = Distill(adapter.get(), variants, "textured",
            {Boolean("project_uvw", true), Pair("texture_scale", 2, 3)});
        Require(Success(projected).has_uv_transform == 0 && Reported(Success(projected), "project_uvw") &&
            Reported(Success(projected), "texture_scale"), "unimplemented projected UVs were treated as authored st");
        auto alternate = Number("uv_space_index", 1);
        alternate.kind = OPENUSD_MDL_VALUE_INT;
        alternate.integer_value = 1;
        auto wrong_space = Distill(adapter.get(), variants, "textured", {alternate});
        Require(Success(wrong_space).has_uv_transform == 0 && Reported(Success(wrong_space), "uv_space_index"),
            "alternate UV set silently used set zero");
        auto malformed = Distill(adapter.get(), variants, "textured",
            {Pair("texture_scale", std::numeric_limits<float>::infinity(), 1), Number("texture_rotate", 45)});
        Require(Success(malformed).has_uv_transform == 0 && Reported(Success(malformed), "texture_scale") &&
            Reported(Success(malformed), "texture_rotate"), "partial invalid UV transform was applied");
    });
    probe.Run("NormalStrengthAndTangentFlipsPreserveSignedUnclippedAffine", [&] {
        for (float strength : {0.25F, 1.0F, 3.0F, -2.0F})
        {
            auto value = Distill(adapter.get(), variants, "textured", {
                Number("bump_factor", strength), Boolean("flip_tangent_u", true),
                Boolean("flip_tangent_v", false)});
            const auto& result = Success(value);
            Texture(result, OPENUSD_MDL_SURFACE_NORMAL, textures / "normal.ppm",
                OPENUSD_MDL_CHANNEL_RGB, OPENUSD_MDL_COLOR_SPACE_RAW, OPENUSD_MDL_ORIGIN_MODULE_DEFAULT,
                {-strength, strength, 1, 1}, {0.5F + 0.5F * strength, 0.5F - 0.5F * strength, 0, 0});
            Require(!Reported(result, "bump_factor") && !Reported(result, "flip_tangent_u") &&
                !Reported(result, "flip_tangent_v"), "supported normal controls were reported as lost");
        }
        auto disabled = Distill(adapter.get(), variants, "textured", {Number("bump_factor", 0)});
        for (uint32_t index = 0; index < Success(disabled).texture_count; ++index)
        {
            Require(Success(disabled).textures[index].surface_input != OPENUSD_MDL_SURFACE_NORMAL,
                "zero normal strength still published a normal map");
        }
        auto invalid = Distill(adapter.get(), variants, "textured",
            {Number("bump_factor", std::numeric_limits<float>::infinity())});
        Require(Reported(Success(invalid), "bump_factor"), "nonfinite normal strength was silently accepted");
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
        Scalar(result, OPENUSD_MDL_SURFACE_OMNIPBR_SPECULAR_LEVEL, {0.5F});
        Require(result.scalar_count == 3, "unevaluated roughness was replaced by a base default");
        auto overridden = Distill(adapter.get(), variants, "unsupported_call",
            {Number("reflection_roughness_constant", 0.92F)});
        const auto& replacement = Success(overridden);
        Scalar(replacement, OPENUSD_MDL_SURFACE_ROUGHNESS, {0.92F}, OPENUSD_MDL_ORIGIN_AUTHORED);
        Require(!Reported(replacement, "reflection_roughness_constant") &&
            Text(replacement.diagnostic).find("math::abs") == std::string::npos,
            "overridden expression was still reported as dropped");
        auto modifier = Distill(adapter.get(), variants, "unsupported_modifier");
        const auto& partial = Success(modifier);
        Scalar(partial, OPENUSD_MDL_SURFACE_DIFFUSE_COLOR, {0.08F, 0.1F, 0.12F});
        UvTransform(partial, {2, 0, 0, 3, 0, 0});
        Require(!Reported(partial, "diffuse_tint") && !Reported(partial, "texture_scale"),
            "supported tint and UV controls were reported as lost");
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
    probe.Run("RejectedSdkConfigurationDoesNotPoisonThePreviousAdapter", [&] {
        const fs::path cold = work / "configuration-recovery" / "cold.mdl";
        Write(cold, MutableVariant("0.37"));
        auto valid = Create({core.u8string(), cold.parent_path().u8string()}, 201);
        auto warm = Distill(valid.get(), (core / "OmniPBR.mdl").u8string(), "OmniPBR");
        Scalar(Success(warm), OPENUSD_MDL_SURFACE_ROUGHNESS, {0.5F});
        const fs::path missing = work / "absent-configuration-root";
        Require(!fs::exists(missing), "the rejection fixture directory unexpectedly exists");
        auto invalid = Create({missing.u8string()}, 201);
        Failure(Distill(invalid.get(), cold.u8string(), "mutable_variant"),
            OPENUSD_MDL_STATUS_INVALID_ARGUMENT, "rejected the module search path");
        auto recovered = Distill(valid.get(), cold.u8string(), "mutable_variant");
        Scalar(Success(recovered), OPENUSD_MDL_SURFACE_ROUGHNESS, {0.37F});
        Require(fs::remove(cold), "the recovery fixture was not removed");
        fs::remove(cold.parent_path());
    });
}

void SpecularLevel(Probe& probe, const fs::path& core)
{
    auto adapter = Create({core.u8string()});
    probe.Run("ExactOmniPbrSignatureKeepsSdkProvenProfile", [&] {
        auto value = Distill(adapter.get(), "OmniPBR.mdl",
            "OmniPBR(color,texture_2d,float,float,float,color,float,float,texture_2d,"
            "float,float,texture_2d,float,bool,texture_2d,float,texture_2d,bool,color,"
            "texture_2d,texture_2d,float,bool,texture_2d,float,bool,::base::mono_mode,"
            "float,float,float,texture_2d,float,texture_2d,bool,bool,bool,bool,int,"
            "float2,float,float2,float2,float,float2,float,float,bool)");
        Scalar(Success(value), OPENUSD_MDL_SURFACE_OMNIPBR_SPECULAR_LEVEL, {0.5F});
    });
    probe.Run("SpecularLevelDefaultKeepsBothDielectricFresnelEndpoints", [&] {
        auto value = Distill(adapter.get(), "OmniPBR.mdl", "OmniPBR");
        Scalar(Success(value), OPENUSD_MDL_SURFACE_OMNIPBR_SPECULAR_LEVEL, {0.5F});
        Require(!Reported(Success(value), "specular_level"), "the supported default was reported as dropped");
    });
    for (float level : {0.0F, 0.25F, 0.5F, 1.0F})
    {
        probe.Run(("AuthoredSpecularLevel_" + std::to_string(level)).c_str(), [&] {
            auto value = Distill(adapter.get(), "OmniPBR.mdl", "OmniPBR", {Number("specular_level", level)});
            Scalar(Success(value), OPENUSD_MDL_SURFACE_OMNIPBR_SPECULAR_LEVEL, {level}, OPENUSD_MDL_ORIGIN_AUTHORED);
            Require(!Reported(Success(value), "specular_level"), "authored curve weight was reported as dropped");
        });
    }
    for (float level : {-0.1F, 1.1F})
    {
        probe.Run(("UnboundedSpecularLevelIsReported_" + std::to_string(level)).c_str(), [&] {
            auto value = Distill(adapter.get(), "OmniPBR.mdl", "OmniPBR", {Number("specular_level", level)});
            const auto& result = Success(value);
            Require(Reported(result, "specular_level"), "an unsupported weight was silently clamped");
            for (uint32_t index = 0; index < result.scalar_count; ++index)
            {
                Require(result.scalars[index].surface_input != OPENUSD_MDL_SURFACE_OMNIPBR_SPECULAR_LEVEL,
                    "an unsupported curve weight reached the surface");
            }
        });
    }
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
        Scalar(result, OPENUSD_MDL_SURFACE_DIFFUSE_COLOR, {0.2F, 0.199998F, 0.199998F});
        Scalar(result, OPENUSD_MDL_SURFACE_ROUGHNESS, {0.19F});
        Scalar(result, OPENUSD_MDL_SURFACE_METALLIC, {0.0F});
        const fs::path textures = directory.parent_path() / "Textures";
        Require(result.texture_count == 4, "actual enabled MDL texture overrides were lost");
        Texture(result, OPENUSD_MDL_SURFACE_DIFFUSE_COLOR, textures / "T_MetalPainted_Gray_Worn_Albedo.png",
            OPENUSD_MDL_CHANNEL_RGB, OPENUSD_MDL_COLOR_SPACE_SRGB,
            OPENUSD_MDL_ORIGIN_MODULE_DEFAULT, {1, 0.99999F, 0.99999F, 1});
        Texture(result, OPENUSD_MDL_SURFACE_ROUGHNESS, textures / "T_metalpainted_gray_metallic_a_ORM.png",
            OPENUSD_MDL_CHANNEL_G, OPENUSD_MDL_COLOR_SPACE_RAW, OPENUSD_MDL_ORIGIN_MODULE_DEFAULT,
            {1, 0.45F, 1, 1}, {0, 0.1045F, 0, 0});
        Texture(result, OPENUSD_MDL_SURFACE_METALLIC, textures / "T_metalpainted_gray_metallic_a_ORM.png",
            OPENUSD_MDL_CHANNEL_B, OPENUSD_MDL_COLOR_SPACE_RAW);
        Texture(result, OPENUSD_MDL_SURFACE_NORMAL, textures / "T_Aluminium_Raw_A_Normal.png",
            OPENUSD_MDL_CHANNEL_RGB, OPENUSD_MDL_COLOR_SPACE_RAW);
        UvTransform(result, {4, 0, 0, 4, 0, 0});
        Require(!Reported(result, "texture_scale") && !Reported(result, "diffuse_tint"),
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
        Scalar(result, OPENUSD_MDL_SURFACE_OMNIPBR_SPECULAR_LEVEL, {0.5F});
        Require(result.scalar_count == 4 && result.texture_count == 4,
            "actual OmniPBR wrapper gained unrelated outputs or lost textures");
        Texture(result, OPENUSD_MDL_SURFACE_DIFFUSE_COLOR, textures / "T_Metal_Glossy_A_Albedo_1.png",
            OPENUSD_MDL_CHANNEL_RGB, OPENUSD_MDL_COLOR_SPACE_SRGB);
        Texture(result, OPENUSD_MDL_SURFACE_ROUGHNESS, textures / "T_Metal_Glossy_A_ORM_1.png",
            OPENUSD_MDL_CHANNEL_G, OPENUSD_MDL_COLOR_SPACE_RAW);
        Texture(result, OPENUSD_MDL_SURFACE_NORMAL, textures / "T_Metal_Glossy_A_Normal_1.png",
            OPENUSD_MDL_CHANNEL_RGB, OPENUSD_MDL_COLOR_SPACE_RAW);
        UvTransform(result, {2, 0, 0, 2, 0, 0});
        Require(!Reported(result, "texture_scale"), "supported actual UV scale was reported as lost");
        ReportWrapper("Metal_Glossy_A", result);
    });
}

void OmniSurface(Probe& probe, const fs::path& modules, const fs::path& source)
{
    auto adapter = Create({modules.u8string(), source.u8string()});
    const std::string wrapper = (source / "Props" / "general" /
        "SM_ClosedHeadPail_A01_Natural_01" / "materials" / "Natural_Plastic_E.mdl").u8string();
    probe.Run("RealOmniSurfaceVariantKeepsConstantsAndNamesUnsupportedLobes", [&] {
        auto value = Distill(adapter.get(), wrapper, "Natural_Plastic_Translucent_A");
        const auto& result = Success(value);
        Scalar(result, OPENUSD_MDL_SURFACE_DIFFUSE_COLOR, {1.0F, 1.0F, 1.0F});
        Scalar(result, OPENUSD_MDL_SURFACE_METALLIC, {0.05F});
        Scalar(result, OPENUSD_MDL_SURFACE_ROUGHNESS, {0.3F});
        Scalar(result, OPENUSD_MDL_SURFACE_IOR, {2.0F});
        Require(Reported(result, "specular_reflection_weight") &&
            Reported(result, "specular_transmission_weight") &&
            Reported(result, "subsurface_weight"),
            "partial OmniSurface projection hid unsupported reflection/transmission/subsurface controls");
        for (uint32_t index = 0; index < result.scalar_count; ++index)
        {
            Require(result.scalars[index].surface_input != OPENUSD_MDL_SURFACE_OMNIPBR_SPECULAR_LEVEL,
                "an OmniSurface variant acquired the unrelated OmniPBR dielectric curve");
        }
        ReportWrapper("Natural_Plastic_Translucent_A", result);
    });
    probe.Run("RealOmniSurfaceVariantAuthoredInputsOverrideDefaults", [&] {
        auto value = Distill(adapter.get(), wrapper, "Natural_Plastic_Translucent_A",
            {Color("diffuse_reflection_color", 0.2F, 0.4F, 0.6F),
             Number("diffuse_reflection_weight", 0.25F),
             Number("specular_reflection_roughness", 0.7F)});
        const auto& result = Success(value);
        Scalar(result, OPENUSD_MDL_SURFACE_DIFFUSE_COLOR, {0.05F, 0.1F, 0.15F},
            OPENUSD_MDL_ORIGIN_AUTHORED);
        Scalar(result, OPENUSD_MDL_SURFACE_ROUGHNESS, {0.7F}, OPENUSD_MDL_ORIGIN_AUTHORED);
        Scalar(result, OPENUSD_MDL_SURFACE_METALLIC, {0.05F});
        Require(Reported(result, "specular_transmission_weight"),
            "authored basic inputs hid unrepresented transmission");
    });
    probe.Run("RealOmniSurfaceWrongSignatureStillRefusesWithoutShading", [&] {
        Failure(Distill(adapter.get(), wrapper, "Natural_Plastic_Translucent_A(float)"),
            OPENUSD_MDL_STATUS_UNSUPPORTED_MATERIAL, "Natural_Plastic_Translucent_A(float)");
    });
}

const std::string& LegacyPbrSignature()
{
    static const std::string signature =
        "OmniPBR(color,texture_2d,float,float,float,color,float,float,texture_2d,float,float,"
        "texture_2d,float,bool,texture_2d,float,texture_2d,bool,color,texture_2d,texture_2d,"
        "float,bool,bool,float,texture_2d,::base::mono_mode,float,float,texture_2d,float,"
        "texture_2d,bool,bool,bool,bool,int,float2,float,float2,float2,float,float2)";
    return signature;
}

void LegacyPbr(Probe& probe, const fs::path& legacy, const fs::path& current)
{
    const std::string& signature = LegacyPbrSignature();
    auto legacyAdapter = Create({legacy.u8string()});
    auto currentAdapter = Create({current.u8string()});
    const std::string legacyModule = (legacy / "OmniPBR.mdl").u8string();
    const std::string currentModule = (current / "OmniPBR.mdl").u8string();
    probe.Run("ExactLegacyPbrInterfaceResolvesWithoutSignatureRewriting", [&] {
        auto value = Distill(legacyAdapter.get(), legacyModule, signature);
        const auto& result = Success(value);
        Scalar(result, OPENUSD_MDL_SURFACE_DIFFUSE_COLOR, {0.2F, 0.2F, 0.2F});
        Scalar(result, OPENUSD_MDL_SURFACE_ROUGHNESS, {0.5F});
        Scalar(result, OPENUSD_MDL_SURFACE_METALLIC, {0.0F});
        Scalar(result, OPENUSD_MDL_SURFACE_OMNIPBR_SPECULAR_LEVEL, {0.5F});
    });
    probe.Run("ExactLegacyPbrAuthoredControlsRemainTyped", [&] {
        auto value = Distill(legacyAdapter.get(), legacyModule, signature,
            {Color("diffuse_color_constant", 0.2F, 0.4F, 0.6F),
             Color("diffuse_tint", 0.5F, 0.25F, 0.75F),
             Number("reflection_roughness_constant", 0.7F), Number("specular_level", 0.0F)});
        const auto& result = Success(value);
        Scalar(result, OPENUSD_MDL_SURFACE_DIFFUSE_COLOR, {0.1F, 0.1F, 0.45F},
            OPENUSD_MDL_ORIGIN_AUTHORED);
        Scalar(result, OPENUSD_MDL_SURFACE_ROUGHNESS, {0.7F}, OPENUSD_MDL_ORIGIN_AUTHORED);
        Scalar(result, OPENUSD_MDL_SURFACE_OMNIPBR_SPECULAR_LEVEL, {0.0F}, OPENUSD_MDL_ORIGIN_AUTHORED);
    });
    probe.Run("LegacyAndCurrentModuleContextsDoNotLeakAcrossAdapters", [&] {
        Failure(Distill(currentAdapter.get(), currentModule, signature),
            OPENUSD_MDL_STATUS_UNSUPPORTED_MATERIAL, "the SDK declares");
        Success(Distill(legacyAdapter.get(), legacyModule, signature));
        Failure(Distill(currentAdapter.get(), currentModule, signature),
            OPENUSD_MDL_STATUS_UNSUPPORTED_MATERIAL, "the SDK declares");
        Success(Distill(legacyAdapter.get(), legacyModule, signature));
    });
    probe.Run("LegacyPbrStillRejectsMalformedSignatures", [&] {
        Failure(Distill(legacyAdapter.get(), legacyModule, signature + "suffix"),
            OPENUSD_MDL_STATUS_UNSUPPORTED_MATERIAL, signature + "suffix");
    });
}

void LegacyRouting(Probe& probe, const fs::path& current, bool invalidConfiguration)
{
    auto adapter = Create({current.u8string()});
    const std::string module = (current / "OmniPBR.mdl").u8string();
    const std::string& signature = LegacyPbrSignature();
    probe.Run("SuccessfulCurrentRequestsNeverUseCompatibilityRouting", [&] {
        auto value = Distill(adapter.get(), module, "OmniPBR");
        const auto& result = Success(value);
        Scalar(result, OPENUSD_MDL_SURFACE_OMNIPBR_SPECULAR_LEVEL, {0.5F});
        Require(Text(result.diagnostic).find("compatibility") == std::string::npos,
            "a successful current material was routed to another module");
    });
    if (invalidConfiguration)
    {
        probe.Run("InvalidCompatibilityConfigurationIsAnExplicitRefusal", [&] {
            Failure(Distill(adapter.get(), module, signature),
                OPENUSD_MDL_STATUS_UNSUPPORTED_MATERIAL, "OPENUSD_MDL_LEGACY_PBR_ROOT");
        });
        return;
    }
    probe.Run("ConfiguredLegacyRootResolvesOnlyTheExactMissingSignature", [&] {
        auto value = Distill(adapter.get(), module, signature,
            {Number("specular_level", 0.0F), Number("reflection_roughness_constant", 0.7F)});
        const auto& result = Success(value);
        Scalar(result, OPENUSD_MDL_SURFACE_OMNIPBR_SPECULAR_LEVEL, {0.0F}, OPENUSD_MDL_ORIGIN_AUTHORED);
        Scalar(result, OPENUSD_MDL_SURFACE_ROUGHNESS, {0.7F}, OPENUSD_MDL_ORIGIN_AUTHORED);
        Require(Text(result.diagnostic).find("exact-signature compatibility") != std::string::npos,
            "legacy module selection was not disclosed");
        auto currentAgain = Distill(adapter.get(), module, "OmniPBR");
        const auto& restored = Success(currentAgain);
        Scalar(restored, OPENUSD_MDL_SURFACE_OMNIPBR_SPECULAR_LEVEL, {0.5F});
        Require(Text(restored.diagnostic).find("compatibility") == std::string::npos,
            "compatibility lookup contaminated the following current request");
    });
    probe.Run("CompatibilityCannotMakeAnInvalidSignatureSucceed", [&] {
        Failure(Distill(adapter.get(), module, "OmniPBR(float)"),
            OPENUSD_MDL_STATUS_UNSUPPORTED_MATERIAL, "compatibility");
        Failure(Distill(adapter.get(), module, signature + "suffix"),
            OPENUSD_MDL_STATUS_UNSUPPORTED_MATERIAL, signature + "suffix");
    });
    probe.Run("UnqualifiedNamesRemainStrict", [&] {
        auto value = Distill(adapter.get(), module, "MissingMaterial");
        Failure(value, OPENUSD_MDL_STATUS_UNSUPPORTED_MATERIAL, "MissingMaterial");
        Require(Text(value.result->diagnostic).find("compatibility") == std::string::npos,
            "an unqualified missing name triggered version substitution");
    });
    probe.Run("MissingPrimaryModulesAreNotReplacedByCompatibility", [&] {
        Failure(Distill(adapter.get(), (current / "not-present" / "OmniPBR.mdl").u8string(), signature),
            OPENUSD_MDL_STATUS_MODULE_NOT_FOUND, "not-present");
    });
}

void Udim(Probe& probe, const fs::path& core, const fs::path& work)
{
    const fs::path directory = work / "udim";
    const fs::path module = directory / "udim_probe.mdl";
    Write(directory / "tiles" / "color.1001.ppm", "P3\n1 1\n255\n255 0 0\n");
    Write(directory / "tiles" / "color.1002.ppm", "P3\n1 1\n255\n0 255 0\n");
    Write(directory / "tiles" / "sparse.1001.ppm", "P3\n1 1\n255\n255 0 0\n");
    Write(directory / "tiles" / "sparse.1999.ppm", "P3\n1 1\n255\n0 255 0\n");
    std::vector<fs::path> boundedTiles;
    for (int index = 0; index < 257; ++index)
    {
        const int number = 1001 + index % 8 + 10 * (index / 8);
        for (const char* prefix : {"grid", "overcount"})
        {
            if (index == 256 && std::strcmp(prefix, "grid") == 0) { continue; }
            const fs::path tile = directory / "tiles" / (std::string(prefix) + "." + std::to_string(number) + ".ppm");
            Write(tile, "P3\n1 1\n255\n255 0 0\n");
            boundedTiles.push_back(tile);
        }
    }
    Write(module, R"(mdl 1.7;
using ::OmniPBR import OmniPBR;
import ::tex::*;
export material tiled(*) = OmniPBR(
    diffuse_texture: texture_2d("./tiles/color.<UDIM>.ppm", ::tex::gamma_linear));
export material inherited(*) = tiled(diffuse_tint: color(0.5, 0.25, 1.0));
export material sparse(*) = OmniPBR(
    diffuse_texture: texture_2d("./tiles/sparse.<UDIM>.ppm", ::tex::gamma_linear));
export material missing(*) = OmniPBR(
    diffuse_texture: texture_2d("./tiles/missing.<UDIM>.ppm", ::tex::gamma_linear));
export material grid(*) = OmniPBR(
    diffuse_texture: texture_2d("./tiles/grid.<UDIM>.ppm", ::tex::gamma_linear));
export material overcount(*) = OmniPBR(
    diffuse_texture: texture_2d("./tiles/overcount.<UDIM>.ppm", ::tex::gamma_linear));
)");
    auto adapter = Create({core.u8string(), directory.u8string()});
    for (const char* name : {"tiled", "inherited"})
    {
        probe.Run(name, [&] {
            auto value = Distill(adapter.get(), module.u8string(), name);
            const auto& result = Success(value);
            Require(result.texture_count == 1 && !Reported(result, "diffuse_texture"),
                "a resolved UDIM set was dropped instead of retaining its full mask: " +
                    Text(result.diagnostic));
            const auto& texture = result.textures[0];
            Require(fs::u8path(Text(texture.asset)) == fs::absolute(directory / "tiles" / "color.<UDIM>.ppm"),
                "the UDIM mask lost its declaring module directory");
            Require(texture.surface_input == OPENUSD_MDL_SURFACE_DIFFUSE_COLOR &&
                texture.output_channel == OPENUSD_MDL_CHANNEL_RGB &&
                texture.color_space == OPENUSD_MDL_COLOR_SPACE_RAW &&
                texture.origin == OPENUSD_MDL_ORIGIN_MODULE_DEFAULT,
                "UDIM texture metadata changed");
            const float red = std::strcmp(name, "inherited") == 0 ? 0.5F : 1.0F;
            Require(std::fabs(texture.scale[0] - red) < 1e-5F,
                "an inherited UDIM texture lost its tint");
        });
    }
    probe.Run("exact_256_tile_and_cell_limit", [&] {
        auto value = Distill(adapter.get(), module.u8string(), "grid");
        const auto& result = Success(value);
        Require(result.texture_count == 1 && !Reported(result, "diffuse_texture"),
            "the exact 256-tile/cell boundary was rejected: " + Text(result.diagnostic));
    });
    probe.Run("authored_single_tile_overrides_udim_default", [&] {
        const std::string asset = (directory / "tiles" / "color.1002.ppm").u8string();
        auto value = Distill(adapter.get(), module.u8string(), "tiled", {Asset("diffuse_texture", asset)});
        const auto& result = Success(value);
        Texture(result, OPENUSD_MDL_SURFACE_DIFFUSE_COLOR, fs::u8path(asset),
            OPENUSD_MDL_CHANNEL_RGB, OPENUSD_MDL_COLOR_SPACE_SRGB, OPENUSD_MDL_ORIGIN_AUTHORED);
    });
    for (const char* name : {"missing", "sparse", "overcount"})
    {
        probe.Run(name, [&] {
            auto value = Distill(adapter.get(), module.u8string(), name);
            const auto& result = Success(value);
            Require(result.texture_count == 0 && Reported(result, "diffuse_texture") &&
                !Text(result.diagnostic).empty(),
                "an unresolved or oversized UDIM set silently published a partial texture");
        });
    }
    for (const auto& tile : boundedTiles)
    {
        Require(fs::remove(tile), "probe-owned bounded UDIM tile was not removed");
    }
    for (const char* name : {"color.1001.ppm", "color.1002.ppm", "sparse.1001.ppm", "sparse.1999.ppm"})
    {
        Require(fs::remove(directory / "tiles" / name), "probe-owned UDIM tile was not removed");
    }
    Require(fs::remove(module), "probe-owned UDIM module was not removed");
    fs::remove(directory / "tiles");
    fs::remove(directory);
}
}

int main(int argc, char** argv)
{
    if (argc < 3)
    {
        std::cerr << "Usage: mdl_wrapper_probe --rejections <fixtures> | "
            "--synthetic <fixtures> <real-core> <work> | --warehouse <real-core> <source> | "
            "--omnisurface <surface-modules> <source> | --legacy-pbr <legacy-core> <current-core>\n";
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
        else if (mode == "--specular" && argc == 3)
        {
            SpecularLevel(probe, fs::u8path(argv[2]));
        }
        else if (mode == "--omnisurface" && argc == 4)
        {
            OmniSurface(probe, fs::u8path(argv[2]), fs::u8path(argv[3]));
        }
        else if (mode == "--legacy-pbr" && argc == 4)
        {
            LegacyPbr(probe, fs::u8path(argv[2]), fs::u8path(argv[3]));
        }
        else if ((mode == "--legacy-routing" || mode == "--legacy-routing-invalid") && argc == 3)
        {
            LegacyRouting(probe, fs::u8path(argv[2]), mode == "--legacy-routing-invalid");
        }
        else if (mode == "--udim" && argc == 4)
        {
            Udim(probe, fs::u8path(argv[2]), fs::u8path(argv[3]));
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
