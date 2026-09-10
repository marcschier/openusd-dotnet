// Copyright (c) marcschier. Licensed under the MIT License.

#include "openusd_mdl.h"

#include <cmath>
#include <iostream>
#include <memory>
#include <string>

namespace
{
openusd_mdl_string View(const std::string& value)
{
    return {value.c_str(), static_cast<uint32_t>(value.size())};
}

bool HasDefault(const openusd_mdl_distilled_material& material, uint32_t input, float expected)
{
    for (uint32_t index = 0; index < material.scalar_count; ++index)
    {
        const auto& scalar = material.scalars[index];
        if (scalar.surface_input == input && scalar.origin == OPENUSD_MDL_ORIGIN_MODULE_DEFAULT &&
            std::fabs(scalar.value[0] - expected) < 0.00001F)
        {
            return true;
        }
    }
    return false;
}
}

int main(int argc, char** argv)
{
    if (argc != 2)
    {
        std::cerr << "Usage: mdl_module_probe <absolute-pinned-module-directory>\n";
        return 2;
    }
    if (openusd_mdl_abi_version() != OPENUSD_MDL_ABI_VERSION ||
        (openusd_mdl_capabilities() & OPENUSD_MDL_CAPABILITY_MODULE_DEFAULTS) == 0)
    {
        std::cerr << "A matching SDK-backed adapter is required; no optional-dependency skip.\n";
        return 2;
    }
    const std::string root = argv[1];
    const openusd_mdl_string path = View(root);
    openusd_mdl_adapter_options options{};
    options.struct_size = sizeof(options);
    options.module_search_paths = &path;
    options.module_search_path_count = 1;
    options.cache_generation = 1;
    openusd_mdl_adapter* created = nullptr;
    if (openusd_mdl_adapter_create(&options, &created) != OPENUSD_MDL_STATUS_OK || created == nullptr)
    {
        std::cerr << "The adapter refused the pinned module configuration.\n";
        return 2;
    }
    const std::unique_ptr<openusd_mdl_adapter, decltype(&openusd_mdl_adapter_destroy)>
        adapter(created, openusd_mdl_adapter_destroy);
    bool passed = true;
    for (const std::string name : {"OmniPBR", "OmniGlass"})
    {
        const std::string uri = name + ".mdl";
        const std::string material_path = "/WarehouseProbe/" + name;
        openusd_mdl_material_request request{};
        request.struct_size = sizeof(request);
        request.module_uri = View(uri);
        request.material_name = View(name);
        request.material_path = View(material_path);
        const openusd_mdl_distilled_material* result = nullptr;
        const uint32_t status = openusd_mdl_adapter_distill(adapter.get(), &request, &result);
        const auto release = [&adapter](const openusd_mdl_distilled_material* value)
        {
            if (value != nullptr)
            {
                openusd_mdl_adapter_release_result(adapter.get(), value);
            }
        };
        const std::unique_ptr<const openusd_mdl_distilled_material, decltype(release)> owner(result, release);
        std::cout << "WAREHOUSE_MDL_MODULE name=" << name << " status=" << status;
        if (result == nullptr)
        {
            std::cout << " result=absent\n";
            passed = false;
            continue;
        }
        std::cout << " scalars=" << result->scalar_count
            << " unsupportedParameters=" << result->unsupported_parameter_count << '\n';
        if (result->diagnostic.data != nullptr)
        {
            std::cout << std::string(result->diagnostic.data, result->diagnostic.size) << '\n';
        }
        for (uint32_t index = 0; index < result->unsupported_parameter_count; ++index)
        {
            const auto& parameter = result->unsupported_parameters[index];
            std::cout << "unsupported=" << std::string(parameter.data, parameter.size) << '\n';
        }
        const bool defaults = name == "OmniPBR"
            ? HasDefault(*result, OPENUSD_MDL_SURFACE_DIFFUSE_COLOR, 0.2F) &&
                HasDefault(*result, OPENUSD_MDL_SURFACE_ROUGHNESS, 0.5F)
            : HasDefault(*result, OPENUSD_MDL_SURFACE_IOR, 1.491F);
        if (status != OPENUSD_MDL_STATUS_OK || !defaults)
        {
            std::cerr << "Actual module defaults were not available for " << name << ".\n";
            passed = false;
        }
    }
    if (passed)
    {
        std::cout << "WAREHOUSE_MDL_REAL_MODULE_DEFAULTS=passed\n"
            "This verifies compiled parameter defaults, not complete material shading or BSDF support.\n";
    }
    return passed ? 0 : 1;
}
