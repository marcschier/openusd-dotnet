// Copyright (c) marcschier. Licensed under the MIT License.

#include "openusd_mdl.h"

#include <cmath>
#include <cstring>
#include <filesystem>
#include <iostream>
#include <memory>
#include <stdexcept>
#include <string>
#include <vector>

#if defined(_WIN32)
#ifndef WIN32_LEAN_AND_MEAN
#define WIN32_LEAN_AND_MEAN
#endif
#include <windows.h>
#else
#include <dlfcn.h>
#endif

namespace
{
namespace fs = std::filesystem;

void Require(bool condition, const std::string& message)
{
    if (!condition)
    {
        throw std::runtime_error(message);
    }
}

openusd_mdl_string View(const std::string& value)
{
    return {value.data(), static_cast<uint32_t>(value.size())};
}

openusd_mdl_string View(const char* value)
{
    return {value, static_cast<uint32_t>(std::strlen(value))};
}

std::string Text(openusd_mdl_string value)
{
    return value.data == nullptr ? std::string() : std::string(value.data, value.size);
}

void* Symbol(void* library, const char* name)
{
#if defined(_WIN32)
    return reinterpret_cast<void*>(GetProcAddress(static_cast<HMODULE>(library), name));
#else
    return dlsym(library, name);
#endif
}

struct Api
{
    openusd_mdl_abi_version_fn abiVersion;
    openusd_mdl_capabilities_fn capabilities;
    openusd_mdl_adapter_create_fn create;
    openusd_mdl_adapter_destroy_fn destroy;
    openusd_mdl_adapter_distill_fn distill;
    openusd_mdl_adapter_release_result_fn releaseResult;

    explicit Api(const fs::path& path)
    {
        Require(path.is_absolute(), "the adapter library path must be absolute");
#if defined(_WIN32)
        void* library = LoadLibraryExW(
            path.c_str(), nullptr,
            LOAD_LIBRARY_SEARCH_DLL_LOAD_DIR | LOAD_LIBRARY_SEARCH_DEFAULT_DIRS);
#else
        void* library = dlopen(path.c_str(), RTLD_NOW | RTLD_LOCAL);
#endif
        Require(library != nullptr, "could not load adapter '" + path.u8string() + "'");
        abiVersion = reinterpret_cast<openusd_mdl_abi_version_fn>(
            Symbol(library, "openusd_mdl_abi_version"));
        capabilities = reinterpret_cast<openusd_mdl_capabilities_fn>(
            Symbol(library, "openusd_mdl_capabilities"));
        create = reinterpret_cast<openusd_mdl_adapter_create_fn>(
            Symbol(library, "openusd_mdl_adapter_create"));
        destroy = reinterpret_cast<openusd_mdl_adapter_destroy_fn>(
            Symbol(library, "openusd_mdl_adapter_destroy"));
        distill = reinterpret_cast<openusd_mdl_adapter_distill_fn>(
            Symbol(library, "openusd_mdl_adapter_distill"));
        releaseResult = reinterpret_cast<openusd_mdl_adapter_release_result_fn>(
            Symbol(library, "openusd_mdl_adapter_release_result"));
        Require(abiVersion != nullptr && capabilities != nullptr && create != nullptr &&
            destroy != nullptr && distill != nullptr && releaseResult != nullptr,
            "adapter is missing a public C ABI export");
        Require(abiVersion() == OPENUSD_MDL_ABI_VERSION &&
            (capabilities() & OPENUSD_MDL_CAPABILITY_MODULE_DEFAULTS) != 0,
            "a matching SDK-backed adapter is required; no skip or replacement");
        std::cout << "ADAPTER=" << path.u8string() << '\n';
    }
};

openusd_mdl_parameter Asset(const char* name, const std::string& path)
{
    openusd_mdl_parameter parameter{};
    parameter.name = View(name);
    parameter.kind = OPENUSD_MDL_VALUE_ASSET;
    parameter.text = View(path);
    return parameter;
}

openusd_mdl_parameter Metadata(const char* name, const char* text)
{
    openusd_mdl_parameter parameter{};
    parameter.name = View(name);
    parameter.kind = OPENUSD_MDL_VALUE_STRING;
    parameter.text = View(text);
    return parameter;
}

bool Reported(const openusd_mdl_distilled_material& result, const std::string& name)
{
    for (uint32_t index = 0; index < result.unsupported_parameter_count; ++index)
    {
        if (Text(result.unsupported_parameters[index]) == name)
        {
            return true;
        }
    }
    return false;
}

void CheckAlias(
    const Api& api,
    openusd_mdl_adapter* adapter,
    const char* material,
    const std::vector<openusd_mdl_parameter>& parameters,
    const fs::path& asset,
    uint32_t input,
    uint32_t colorSpace,
    uint32_t origin = OPENUSD_MDL_ORIGIN_AUTHORED)
{
    openusd_mdl_material_request request{};
    request.struct_size = sizeof(request);
    request.module_uri = View("alias_color.mdl");
    request.material_name = View(material);
    request.material_path = View("/AliasColorProbe");
    request.parameters = parameters.data();
    request.parameter_count = static_cast<uint32_t>(parameters.size());
    const openusd_mdl_distilled_material* result = nullptr;
    const uint32_t status = api.distill(adapter, &request, &result);
    const auto release = [&](const openusd_mdl_distilled_material* value) {
        api.releaseResult(adapter, value);
    };
    const std::unique_ptr<const openusd_mdl_distilled_material, decltype(release)> owner(result, release);
    Require(status == OPENUSD_MDL_STATUS_OK && result != nullptr,
        "distillation failed: " + (result == nullptr ? "(no result)" : Text(result->diagnostic)));
    Require(result->status == status && result->struct_size == sizeof(*result) &&
        result->texture_count == 1 && result->textures != nullptr &&
        result->scalar_count == 1 && result->scalars != nullptr,
        "unexpected public result shape");
    Require(result->scalars[0].surface_input == OPENUSD_MDL_SURFACE_ROUGHNESS &&
        result->scalars[0].component_count == 1 &&
        result->scalars[0].origin == OPENUSD_MDL_ORIGIN_MODULE_DEFAULT &&
        std::fabs(result->scalars[0].value[0] - 0.25F) < 0.00001F,
        "the fixture's literal roughness was not retained");
    Require(Reported(*result, std::string("body:") + material) &&
        !Reported(*result, "source_texture") &&
        !Reported(*result, "colorSpace:source_texture"),
        "default-only body qualification or consumed alias provenance changed");
    const openusd_mdl_distilled_texture& texture = result->textures[0];
    const uint32_t components = input == OPENUSD_MDL_SURFACE_ROUGHNESS ? 1u : 3u;
    const uint32_t channel = input == OPENUSD_MDL_SURFACE_ROUGHNESS
        ? OPENUSD_MDL_CHANNEL_R : OPENUSD_MDL_CHANNEL_RGB;
    Require(texture.surface_input == input && texture.origin == origin &&
        texture.component_count == components && texture.output_channel == channel &&
        texture.wrap_s == OPENUSD_MDL_WRAP_REPEAT && texture.wrap_t == OPENUSD_MDL_WRAP_REPEAT,
        "texture input/shape/channel/origin/wrap changed");
    std::error_code error;
    Require(fs::equivalent(fs::u8path(Text(texture.asset)), asset, error) && !error,
        "alias changed the texture filename");
    for (size_t component = 0; component < 4; ++component)
    {
        Require(texture.scale[component] == 1.0F && texture.bias[component] == 0.0F,
            "texture scale/bias changed");
    }
    std::cout << "ALIAS material=" << material << " input=" << input
        << " expected=" << colorSpace << " actual=" << texture.color_space
        << " origin=" << texture.origin << '\n';
    Require(texture.color_space == colorSpace,
        "color_space expected=" + std::to_string(colorSpace) +
            " actual=" + std::to_string(texture.color_space) +
            " input=" + std::to_string(input) + " origin=" + std::to_string(texture.origin));
}

struct Cases
{
    std::string filter;
    unsigned int passed = 0;
    unsigned int failed = 0;

    template<class Test>
    void Run(const char* name, Test test)
    {
        if (!filter.empty() && filter != name)
        {
            return;
        }
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
}

int main(int argc, char** argv)
{
    if (argc != 3 && argc != 4)
    {
        std::cerr << "Usage: mdl_alias_color_probe <absolute-adapter-path> <fixture-root> [case-name]\n";
        return 2;
    }
    std::cout << std::unitbuf;
    try
    {
        const Api api(fs::u8path(argv[1]));
        const std::string root = fs::u8path(argv[2]).u8string();
        const openusd_mdl_string rootView = View(root);
        openusd_mdl_adapter_options options{};
        options.struct_size = sizeof(options);
        options.module_search_paths = &rootView;
        options.module_search_path_count = 1;
        options.cache_generation = 1;
        openusd_mdl_adapter* created = nullptr;
        Require(api.create(&options, &created) == OPENUSD_MDL_STATUS_OK && created != nullptr,
            "adapter creation failed");
        const std::unique_ptr<openusd_mdl_adapter, openusd_mdl_adapter_destroy_fn>
            adapter(created, api.destroy);
        const fs::path file = fs::u8path(root) / "wrappers" / "textures" / "albedo.ppm";
        const std::string asset = file.u8string();
        Cases cases{argc == 4 ? argv[3] : ""};
        cases.Run("RawSourceAliasToDiffuse", [&] {
            CheckAlias(api, adapter.get(), "diffuse_alias",
                {Asset("source_texture", asset), Metadata("colorSpace:source_texture", "raw")},
                file, OPENUSD_MDL_SURFACE_DIFFUSE_COLOR, OPENUSD_MDL_COLOR_SPACE_RAW);
        });
        cases.Run("SrgbSourceAliasToNormal", [&] {
            CheckAlias(api, adapter.get(), "normal_alias",
                {Asset("source_texture", asset), Metadata("colorSpace:source_texture", "srgb")},
                file, OPENUSD_MDL_SURFACE_NORMAL, OPENUSD_MDL_COLOR_SPACE_SRGB);
        });
        cases.Run("SrgbSourceAliasToRoughnessRetainsAuthoredOrigin", [&] {
            CheckAlias(api, adapter.get(), "roughness_alias",
                {Asset("source_texture", asset), Metadata("colorSpace:source_texture", "srgb")},
                file, OPENUSD_MDL_SURFACE_ROUGHNESS, OPENUSD_MDL_COLOR_SPACE_SRGB);
        });
        cases.Run("NestedParameterAliasesPreserveColorSpace", [&] {
            CheckAlias(api, adapter.get(), "nested_alias",
                {Asset("source_texture", asset), Metadata("colorSpace:source_texture", "raw")},
                file, OPENUSD_MDL_SURFACE_DIFFUSE_COLOR, OPENUSD_MDL_COLOR_SPACE_RAW);
        });
        cases.Run("CopyConstructorAliasesPreserveColorSpace", [&] {
            CheckAlias(api, adapter.get(), "copy_alias",
                {Asset("source_texture", asset), Metadata("colorSpace:source_texture", "raw")},
                file, OPENUSD_MDL_SURFACE_DIFFUSE_COLOR, OPENUSD_MDL_COLOR_SPACE_RAW);
        });
        cases.Run("DestinationMetadataOverridesSource", [&] {
            CheckAlias(api, adapter.get(), "diffuse_alias",
                {Metadata("colorSpace:diffuse_texture", "srgb"), Asset("source_texture", asset),
                 Metadata("colorSpace:source_texture", "raw")},
                file, OPENUSD_MDL_SURFACE_DIFFUSE_COLOR, OPENUSD_MDL_COLOR_SPACE_SRGB);
            CheckAlias(api, adapter.get(), "normal_alias",
                {Asset("source_texture", asset), Metadata("colorSpace:source_texture", "srgb"),
                 Metadata("colorSpace:normalmap_texture", "raw")},
                file, OPENUSD_MDL_SURFACE_NORMAL, OPENUSD_MDL_COLOR_SPACE_RAW);
        });
        cases.Run("AbsentSourceMetadataUsesDestinationFallbacks", [&] {
            CheckAlias(api, adapter.get(), "diffuse_alias", {Asset("source_texture", asset)},
                file, OPENUSD_MDL_SURFACE_DIFFUSE_COLOR, OPENUSD_MDL_COLOR_SPACE_SRGB);
            CheckAlias(api, adapter.get(), "normal_alias", {Asset("source_texture", asset)},
                file, OPENUSD_MDL_SURFACE_NORMAL, OPENUSD_MDL_COLOR_SPACE_RAW);
        });
        cases.Run("ExplicitAutoMetadataSurvivesAliases", [&] {
            CheckAlias(api, adapter.get(), "diffuse_alias",
                {Asset("source_texture", asset), Metadata("colorSpace:source_texture", "auto")},
                file, OPENUSD_MDL_SURFACE_DIFFUSE_COLOR, OPENUSD_MDL_COLOR_SPACE_AUTO);
            CheckAlias(api, adapter.get(), "normal_alias",
                {Asset("source_texture", asset), Metadata("colorSpace:source_texture", "auto")},
                file, OPENUSD_MDL_SURFACE_NORMAL, OPENUSD_MDL_COLOR_SPACE_AUTO);
        });
        cases.Run("UnknownOrEmptySourceMetadataKeepsExistingFallbacks", [&] {
            for (const char* metadata : {"linear", "", "unknown"})
            {
                CheckAlias(api, adapter.get(), "diffuse_alias",
                    {Asset("source_texture", asset), Metadata("colorSpace:source_texture", metadata)},
                    file, OPENUSD_MDL_SURFACE_DIFFUSE_COLOR, OPENUSD_MDL_COLOR_SPACE_SRGB);
                CheckAlias(api, adapter.get(), "normal_alias",
                    {Asset("source_texture", asset), Metadata("colorSpace:source_texture", metadata)},
                    file, OPENUSD_MDL_SURFACE_NORMAL, OPENUSD_MDL_COLOR_SPACE_RAW);
            }
        });
        cases.Run("InvalidDestinationMetadataUsesOwnFallback", [&] {
            for (const char* metadata : {"unknown", ""})
            {
                CheckAlias(api, adapter.get(), "diffuse_alias",
                    {Asset("source_texture", asset), Metadata("colorSpace:source_texture", "raw"),
                     Metadata("colorSpace:diffuse_texture", metadata)},
                    file, OPENUSD_MDL_SURFACE_DIFFUSE_COLOR, OPENUSD_MDL_COLOR_SPACE_SRGB);
            }
            auto invalid = Metadata("colorSpace:normalmap_texture", "");
            invalid.kind = OPENUSD_MDL_VALUE_FLOAT;
            invalid.component_count = 1;
            invalid.value[0] = 0.5F;
            CheckAlias(api, adapter.get(), "normal_alias",
                {Asset("source_texture", asset), Metadata("colorSpace:source_texture", "srgb"), invalid},
                file, OPENUSD_MDL_SURFACE_NORMAL, OPENUSD_MDL_COLOR_SPACE_RAW);
        });
        cases.Run("SourceMetadataOrderAndCaseArePreserved", [&] {
            CheckAlias(api, adapter.get(), "diffuse_alias",
                {Metadata("colorSpace:source_texture", "RaW"), Asset("source_texture", asset)},
                file, OPENUSD_MDL_SURFACE_DIFFUSE_COLOR, OPENUSD_MDL_COLOR_SPACE_RAW);
        });
        cases.Run("ModuleTextureMetadataSurvivesAlias", [&] {
            CheckAlias(api, adapter.get(), "module_raw_alias", {},
                file, OPENUSD_MDL_SURFACE_DIFFUSE_COLOR, OPENUSD_MDL_COLOR_SPACE_RAW,
                OPENUSD_MDL_ORIGIN_MODULE_DEFAULT);
            CheckAlias(api, adapter.get(), "module_srgb_alias", {},
                file, OPENUSD_MDL_SURFACE_NORMAL, OPENUSD_MDL_COLOR_SPACE_SRGB,
                OPENUSD_MDL_ORIGIN_MODULE_DEFAULT);
        });
        cases.Run("AuthoredAssetDoesNotInheritReplacedModuleGamma", [&] {
            CheckAlias(api, adapter.get(), "module_raw_alias", {Asset("source_texture", asset)},
                file, OPENUSD_MDL_SURFACE_DIFFUSE_COLOR, OPENUSD_MDL_COLOR_SPACE_SRGB);
            CheckAlias(api, adapter.get(), "module_srgb_alias", {Asset("source_texture", asset)},
                file, OPENUSD_MDL_SURFACE_NORMAL, OPENUSD_MDL_COLOR_SPACE_RAW);
        });
        cases.Run("DestinationMetadataOverridesModuleGammaWithoutChangingOrigin", [&] {
            CheckAlias(api, adapter.get(), "module_raw_alias",
                {Metadata("colorSpace:diffuse_texture", "srgb")},
                file, OPENUSD_MDL_SURFACE_DIFFUSE_COLOR, OPENUSD_MDL_COLOR_SPACE_SRGB,
                OPENUSD_MDL_ORIGIN_MODULE_DEFAULT);
            CheckAlias(api, adapter.get(), "module_srgb_alias",
                {Metadata("colorSpace:normalmap_texture", "raw")},
                file, OPENUSD_MDL_SURFACE_NORMAL, OPENUSD_MDL_COLOR_SPACE_RAW,
                OPENUSD_MDL_ORIGIN_MODULE_DEFAULT);
        });
        cases.Run("StringAssetAliasPreservesAuthoredMetadata", [&] {
            auto source = Asset("source_texture", asset);
            source.kind = OPENUSD_MDL_VALUE_STRING;
            CheckAlias(api, adapter.get(), "diffuse_alias",
                {source, Metadata("colorSpace:source_texture", "raw")},
                file, OPENUSD_MDL_SURFACE_DIFFUSE_COLOR, OPENUSD_MDL_COLOR_SPACE_RAW);
        });
        std::cout << "MDL_ALIAS_COLOR_PROBE passed=" << cases.passed << " failed=" << cases.failed << '\n';
        return cases.failed == 0 && cases.passed != 0 ? 0 : 1;
    }
    catch (const std::exception& error)
    {
        std::cerr << "FAIL setup: " << error.what() << '\n';
        return 1;
    }
}
