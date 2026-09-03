// Copyright (c) marcschier. Licensed under the MIT License.
//
// Native probe for the openusd_mdl adapter, including its MDL SDK-backed mode.
//
// It drives the adapter through its own C ABI, loading whichever adapter the
// operator points it at, so it proves the shipped boundary rather than internal
// C++ that no consumer ever reaches. Without an SDK-backed adapter, or without a
// module search path, it reports the honest unavailable state and exits
// successfully: a probe that fails when an optional dependency is absent is a
// probe nobody can run.

#include "openusd_mdl.h"

#include <array>
#include <cstring>
#include <iostream>
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
struct Adapter
{
    openusd_mdl_abi_version_fn abiVersion = nullptr;
    openusd_mdl_capabilities_fn capabilities = nullptr;
    openusd_mdl_describe_fn describe = nullptr;
    openusd_mdl_adapter_create_fn create = nullptr;
    openusd_mdl_adapter_destroy_fn destroy = nullptr;
    openusd_mdl_adapter_configure_fn configure = nullptr;
    openusd_mdl_adapter_distill_fn distill = nullptr;
    openusd_mdl_adapter_release_result_fn releaseResult = nullptr;
};

void*
OpenLibrary(const char* path)
{
#if defined(_WIN32)
    return LoadLibraryExA(
        path,
        nullptr,
        LOAD_LIBRARY_SEARCH_DLL_LOAD_DIR | LOAD_LIBRARY_SEARCH_DEFAULT_DIRS);
#else
    return dlopen(path, RTLD_NOW | RTLD_LOCAL);
#endif
}

void*
Symbol(void* handle, const char* name)
{
#if defined(_WIN32)
    return reinterpret_cast<void*>(
        GetProcAddress(static_cast<HMODULE>(handle), name));
#else
    return dlsym(handle, name);
#endif
}

bool
Bind(void* handle, Adapter* adapter)
{
    adapter->abiVersion = reinterpret_cast<openusd_mdl_abi_version_fn>(
        Symbol(handle, "openusd_mdl_abi_version"));
    adapter->capabilities = reinterpret_cast<openusd_mdl_capabilities_fn>(
        Symbol(handle, "openusd_mdl_capabilities"));
    adapter->describe = reinterpret_cast<openusd_mdl_describe_fn>(
        Symbol(handle, "openusd_mdl_describe"));
    adapter->create = reinterpret_cast<openusd_mdl_adapter_create_fn>(
        Symbol(handle, "openusd_mdl_adapter_create"));
    adapter->destroy = reinterpret_cast<openusd_mdl_adapter_destroy_fn>(
        Symbol(handle, "openusd_mdl_adapter_destroy"));
    adapter->configure = reinterpret_cast<openusd_mdl_adapter_configure_fn>(
        Symbol(handle, "openusd_mdl_adapter_configure"));
    adapter->distill = reinterpret_cast<openusd_mdl_adapter_distill_fn>(
        Symbol(handle, "openusd_mdl_adapter_distill"));
    adapter->releaseResult =
        reinterpret_cast<openusd_mdl_adapter_release_result_fn>(
            Symbol(handle, "openusd_mdl_adapter_release_result"));
    return adapter->abiVersion != nullptr && adapter->capabilities != nullptr &&
        adapter->create != nullptr && adapter->destroy != nullptr &&
        adapter->configure != nullptr && adapter->distill != nullptr &&
        adapter->releaseResult != nullptr;
}

openusd_mdl_string
View(const std::string& text)
{
    openusd_mdl_string value{};
    value.data = text.c_str();
    value.size = static_cast<uint32_t>(text.size());
    return value;
}

const openusd_mdl_distilled_scalar*
FindScalar(const openusd_mdl_distilled_material* result, uint32_t input)
{
    for (uint32_t index = 0; index < result->scalar_count; ++index)
    {
        if (result->scalars[index].surface_input == input)
        {
            return &result->scalars[index];
        }
    }
    return nullptr;
}

bool
Reported(const openusd_mdl_distilled_material* result, const char* name)
{
    const size_t length = std::strlen(name);
    for (uint32_t index = 0; index < result->unsupported_parameter_count; ++index)
    {
        const openusd_mdl_string& entry = result->unsupported_parameters[index];
        if (entry.size == length && entry.data != nullptr &&
            std::memcmp(entry.data, name, length) == 0)
        {
            return true;
        }
    }
    return false;
}

bool
NearlyEqual(float left, float right)
{
    const float delta = left - right;
    return delta > -1e-5F && delta < 1e-5F;
}

std::string
Diagnostic(const openusd_mdl_distilled_material* result)
{
    if (result == nullptr || result->diagnostic.data == nullptr)
    {
        return std::string();
    }
    return std::string(result->diagnostic.data, result->diagnostic.size);
}
}

int
main(int argc, char** argv)
{
    if (argc < 2 || argc > 3)
    {
        std::cerr << "Usage: mdl_sdk_probe <adapter-path> [module-search-path]\n";
        return 2;
    }

    void* handle = OpenLibrary(argv[1]);
    if (handle == nullptr)
    {
        std::cerr << "Could not load the adapter at " << argv[1] << "\n";
        return 3;
    }
    Adapter adapter;
    if (!Bind(handle, &adapter))
    {
        std::cerr << "The adapter at " << argv[1]
                  << " does not export the openusd_mdl ABI.\n";
        return 3;
    }
    if (adapter.abiVersion() != OPENUSD_MDL_ABI_VERSION)
    {
        std::cerr << "The adapter implements ABI " << adapter.abiVersion()
                  << ", this probe expects " << OPENUSD_MDL_ABI_VERSION << ".\n";
        return 3;
    }

    std::array<char, 512> description{};
    const uint32_t required =
        adapter.describe(description.data(), static_cast<uint32_t>(description.size()));
    if (required > description.size())
    {
        description[0] = '\0';
    }
    const uint32_t capabilities = adapter.capabilities();
    std::cout << "adapter: " << description.data() << "\n"
              << "capabilities: 0x" << std::hex << capabilities << std::dec << "\n";

    if ((capabilities & OPENUSD_MDL_CAPABILITY_AUTHORED_SUBSET) == 0)
    {
        std::cerr << "Every adapter must report the authored subset capability.\n";
        return 4;
    }
    const bool sdkBacked =
        (capabilities & OPENUSD_MDL_CAPABILITY_MODULE_DEFAULTS) != 0;

    const std::string searchPath = argc == 3 ? std::string(argv[2]) : std::string();
    std::vector<openusd_mdl_string> searchPaths;
    if (!searchPath.empty())
    {
        searchPaths.push_back(View(searchPath));
    }
    openusd_mdl_adapter_options options{};
    options.struct_size = static_cast<uint32_t>(sizeof(options));
    options.module_search_paths = searchPaths.empty() ? nullptr : searchPaths.data();
    options.module_search_path_count = static_cast<uint32_t>(searchPaths.size());
    options.cache_generation = 1;

    openusd_mdl_adapter* instance = nullptr;
    if (adapter.create(&options, &instance) != OPENUSD_MDL_STATUS_OK ||
        instance == nullptr)
    {
        std::cerr << "The adapter refused the probe's configuration.\n";
        return 4;
    }

    int exitCode = 0;
    auto fail = [&exitCode](const std::string& message) {
        std::cerr << message << "\n";
        exitCode = 4;
    };

    // A relative search path must be refused outright, in every adapter, SDK
    // backed or not. Resolving one against the process working directory is the
    // search this whole slice refuses to perform.
    {
        const std::string relative = "modules";
        const openusd_mdl_string relativeView = View(relative);
        openusd_mdl_adapter_options bad{};
        bad.struct_size = static_cast<uint32_t>(sizeof(bad));
        bad.module_search_paths = &relativeView;
        bad.module_search_path_count = 1;
        if (adapter.configure(instance, &bad) != OPENUSD_MDL_STATUS_INVALID_ARGUMENT)
        {
            fail("The adapter accepted a relative module search path.");
        }
    }

    // More search paths than the ABI declares must be refused rather than
    // silently truncated.
    if (!searchPath.empty())
    {
        std::vector<openusd_mdl_string> tooMany(
            OPENUSD_MDL_MAX_SEARCH_PATHS + 1, View(searchPath));
        openusd_mdl_adapter_options bad{};
        bad.struct_size = static_cast<uint32_t>(sizeof(bad));
        bad.module_search_paths = tooMany.data();
        bad.module_search_path_count = static_cast<uint32_t>(tooMany.size());
        if (adapter.configure(instance, &bad) != OPENUSD_MDL_STATUS_INVALID_ARGUMENT)
        {
            fail("The adapter accepted more search paths than it declares.");
        }
    }

    // Restore the real configuration the rest of the probe relies on.
    if (adapter.configure(instance, &options) != OPENUSD_MDL_STATUS_OK)
    {
        std::cerr << "The adapter refused to restore the probe configuration.\n";
        adapter.destroy(instance);
        return 4;
    }

    // The authored fast path answers identically whether or not an SDK is
    // present. This is what an operator relies on when they deploy the
    // SDK-backed adapter with no module at all.
    {
        const std::string name = "diffuse_color_constant";
        openusd_mdl_parameter authored{};
        authored.name = View(name);
        authored.kind = OPENUSD_MDL_VALUE_FLOAT3;
        authored.component_count = 3;
        authored.value[0] = 0.72F;
        authored.value[1] = 0.28F;
        authored.value[2] = 0.12F;

        const std::string module = "OmniPBR.mdl";
        const std::string material = "OmniPBR";
        const std::string path = "/World/Looks/Authored";
        openusd_mdl_material_request request{};
        request.struct_size = static_cast<uint32_t>(sizeof(request));
        request.module_uri = View(module);
        request.material_name = View(material);
        request.material_path = View(path);
        request.parameters = &authored;
        request.parameter_count = 1;

        const openusd_mdl_distilled_material* result = nullptr;
        const uint32_t status = adapter.distill(instance, &request, &result);
        if (status != OPENUSD_MDL_STATUS_OK || result == nullptr)
        {
            fail("The authored fast path did not distil an accepted OmniPBR input: " +
                Diagnostic(result));
        }
        else
        {
            const openusd_mdl_distilled_scalar* diffuse =
                FindScalar(result, OPENUSD_MDL_SURFACE_DIFFUSE_COLOR);
            if (diffuse == nullptr || !NearlyEqual(diffuse->value[0], 0.72F) ||
                diffuse->origin != OPENUSD_MDL_ORIGIN_AUTHORED)
            {
                fail("The authored value did not survive as an authored-origin scalar.");
            }
        }
        if (result != nullptr)
        {
            adapter.releaseResult(instance, result);
        }
    }

    if (!sdkBacked || searchPath.empty())
    {
        std::cout << (sdkBacked
                          ? "no module search path supplied; "
                          : "no MDL SDK backend in this adapter; ")
                  << "module evaluation checks skipped\n";
        adapter.destroy(instance);
        return exitCode;
    }

    // Module defaults. Nothing is authored, so every published value has to have
    // come out of the compiled module, and each must say so.
    {
        const std::string module = "openusd_probe.mdl";
        const std::string material = "openusd_probe_defaults";
        const std::string path = "/World/Looks/Defaults";
        openusd_mdl_material_request request{};
        request.struct_size = static_cast<uint32_t>(sizeof(request));
        request.module_uri = View(module);
        request.material_name = View(material);
        request.material_path = View(path);

        const openusd_mdl_distilled_material* result = nullptr;
        const uint32_t status = adapter.distill(instance, &request, &result);
        if (status != OPENUSD_MDL_STATUS_OK || result == nullptr)
        {
            fail("The SDK backend did not distil the synthetic defaults material: " +
                Diagnostic(result));
        }
        else
        {
            const openusd_mdl_distilled_scalar* diffuse =
                FindScalar(result, OPENUSD_MDL_SURFACE_DIFFUSE_COLOR);
            const openusd_mdl_distilled_scalar* roughness =
                FindScalar(result, OPENUSD_MDL_SURFACE_ROUGHNESS);
            const openusd_mdl_distilled_scalar* metallic =
                FindScalar(result, OPENUSD_MDL_SURFACE_METALLIC);
            const openusd_mdl_distilled_scalar* emissive =
                FindScalar(result, OPENUSD_MDL_SURFACE_EMISSIVE_COLOR);
            if (diffuse == nullptr || roughness == nullptr || metallic == nullptr ||
                emissive == nullptr)
            {
                fail("A module default this adapter accepts was not published.");
            }
            else if (!NearlyEqual(diffuse->value[0], 0.25F) ||
                !NearlyEqual(diffuse->value[1], 0.5F) ||
                !NearlyEqual(diffuse->value[2], 0.75F) ||
                !NearlyEqual(roughness->value[0], 0.375F) ||
                !NearlyEqual(metallic->value[0], 0.125F) ||
                !NearlyEqual(emissive->value[2], 0.3F))
            {
                fail("A module default was published with the wrong value.");
            }
            else if (diffuse->origin != OPENUSD_MDL_ORIGIN_MODULE_DEFAULT ||
                roughness->origin != OPENUSD_MDL_ORIGIN_MODULE_DEFAULT)
            {
                fail("A module default was not marked as coming from the module.");
            }
        }
        if (result != nullptr)
        {
            adapter.releaseResult(instance, result);
        }
    }

    // An authored value must still beat the module default for the same input,
    // and inputs the stage says nothing about must still come from the module.
    // That mixture is the whole point of layering the two.
    {
        const std::string name = "reflection_roughness_constant";
        openusd_mdl_parameter authored{};
        authored.name = View(name);
        authored.kind = OPENUSD_MDL_VALUE_FLOAT;
        authored.component_count = 1;
        authored.value[0] = 0.9F;

        const std::string module = "openusd_probe.mdl";
        const std::string material = "openusd_probe_defaults";
        const std::string path = "/World/Looks/Mixed";
        openusd_mdl_material_request request{};
        request.struct_size = static_cast<uint32_t>(sizeof(request));
        request.module_uri = View(module);
        request.material_name = View(material);
        request.material_path = View(path);
        request.parameters = &authored;
        request.parameter_count = 1;

        const openusd_mdl_distilled_material* result = nullptr;
        const uint32_t status = adapter.distill(instance, &request, &result);
        if (status != OPENUSD_MDL_STATUS_OK || result == nullptr)
        {
            fail("The mixed authored/default material did not distil: " +
                Diagnostic(result));
        }
        else
        {
            const openusd_mdl_distilled_scalar* roughness =
                FindScalar(result, OPENUSD_MDL_SURFACE_ROUGHNESS);
            const openusd_mdl_distilled_scalar* metallic =
                FindScalar(result, OPENUSD_MDL_SURFACE_METALLIC);
            if (roughness == nullptr || !NearlyEqual(roughness->value[0], 0.9F) ||
                roughness->origin != OPENUSD_MDL_ORIGIN_AUTHORED)
            {
                fail("The authored value did not override the module default.");
            }
            else if (metallic == nullptr ||
                !NearlyEqual(metallic->value[0], 0.125F) ||
                metallic->origin != OPENUSD_MDL_ORIGIN_MODULE_DEFAULT)
            {
                fail("An unauthored input did not fall back to the module default.");
            }
        }
        if (result != nullptr)
        {
            adapter.releaseResult(instance, result);
        }
    }

    // Constant expression defaults: a broadcast constructor and an alias that
    // forwards another parameter's default.
    {
        const std::string module = "openusd_probe.mdl";
        const std::string material = "openusd_probe_expressions";
        const std::string path = "/World/Looks/Expressions";
        openusd_mdl_material_request request{};
        request.struct_size = static_cast<uint32_t>(sizeof(request));
        request.module_uri = View(module);
        request.material_name = View(material);
        request.material_path = View(path);

        const openusd_mdl_distilled_material* result = nullptr;
        const uint32_t status = adapter.distill(instance, &request, &result);
        if (status != OPENUSD_MDL_STATUS_OK || result == nullptr)
        {
            fail("The constant-expression material did not distil: " +
                Diagnostic(result));
        }
        else
        {
            const openusd_mdl_distilled_scalar* diffuse =
                FindScalar(result, OPENUSD_MDL_SURFACE_DIFFUSE_COLOR);
            const openusd_mdl_distilled_scalar* roughness =
                FindScalar(result, OPENUSD_MDL_SURFACE_ROUGHNESS);
            const openusd_mdl_distilled_scalar* opacity =
                FindScalar(result, OPENUSD_MDL_SURFACE_OPACITY);
            if (diffuse == nullptr || !NearlyEqual(diffuse->value[0], 0.6F) ||
                !NearlyEqual(diffuse->value[1], 0.6F) ||
                !NearlyEqual(diffuse->value[2], 0.6F))
            {
                fail("A broadcast colour constructor did not fold to its value.");
            }
            else if (roughness == nullptr || !NearlyEqual(roughness->value[0], 0.4F))
            {
                fail("A parameter alias did not resolve to the aliased default.");
            }
            else if (opacity == nullptr || !NearlyEqual(opacity->value[0], 0.75F))
            {
                fail("A gated opacity default did not reach the record.");
            }
        }
        if (result != nullptr)
        {
            adapter.releaseResult(instance, result);
        }
    }

    // Expressions this adapter does not evaluate must distil to nothing and be
    // reported by name, never folded into a plausible-looking value.
    {
        const std::string module = "openusd_probe.mdl";
        const std::string material = "openusd_probe_unsupported";
        const std::string path = "/World/Looks/Unsupported";
        openusd_mdl_material_request request{};
        request.struct_size = static_cast<uint32_t>(sizeof(request));
        request.module_uri = View(module);
        request.material_name = View(material);
        request.material_path = View(path);

        const openusd_mdl_distilled_material* result = nullptr;
        const uint32_t status = adapter.distill(instance, &request, &result);
        if (status == OPENUSD_MDL_STATUS_OK)
        {
            fail("A material of unevaluated expressions reported success.");
        }
        else if (result == nullptr)
        {
            fail("A refused material returned no result to report with.");
        }
        else if (result->scalar_count != 0 || result->texture_count != 0)
        {
            fail("A refused material still published shading data.");
        }
        else if (!Reported(result, "diffuse_color_constant") ||
            !Reported(result, "reflection_roughness_constant"))
        {
            fail("An unevaluated default was not reported by name.");
        }
        if (result != nullptr)
        {
            adapter.releaseResult(instance, result);
        }
    }

    // A module no configured search path contains is a distinct, fixable state.
    {
        const std::string module = "openusd_absent_on_purpose.mdl";
        const std::string material = "whatever";
        const std::string path = "/World/Looks/Absent";
        openusd_mdl_material_request request{};
        request.struct_size = static_cast<uint32_t>(sizeof(request));
        request.module_uri = View(module);
        request.material_name = View(material);
        request.material_path = View(path);

        const openusd_mdl_distilled_material* result = nullptr;
        const uint32_t status = adapter.distill(instance, &request, &result);
        if (status != OPENUSD_MDL_STATUS_MODULE_NOT_FOUND)
        {
            fail("A missing module was not reported as module-not-found.");
        }
        if (result != nullptr)
        {
            adapter.releaseResult(instance, result);
        }
    }

    adapter.destroy(instance);
    if (exitCode == 0)
    {
        std::cout << "mdl_sdk_probe: all checks passed\n";
    }
    return exitCode;
}
