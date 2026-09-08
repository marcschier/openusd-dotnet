// Copyright (c) marcschier. Licensed under the MIT License.

#if defined(_WIN32)
#define NOMINMAX
#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <psapi.h>
#endif

#include "openusd_dotnet.h"

#include "pxr/base/vt/value.h"
#include "pxr/base/vt/arrayEditBuilder.h"
#include "pxr/usd/sdf/layer.h"
#include "pxr/usd/sdf/listOp.h"
#include "pxr/usd/usd/editContext.h"
#include "pxr/usd/usd/stage.h"
#include "pxr/usd/usd/inherits.h"
#include "pxr/usd/usd/variantSets.h"
#include "pxr/usd/usdGeom/camera.h"
#include "pxr/usd/usdRender/product.h"
#include "pxr/usd/usdRender/settings.h"
#include "pxr/usd/usdRender/var.h"

#include <filesystem>
#include <cstdio>
#include <cstdlib>
#include <iostream>
#include <memory>
#include <stdexcept>
#include <string>

PXR_NAMESPACE_USING_DIRECTIVE

namespace
{
void Require(bool condition, const char* message)
{
    if (!condition)
    {
        throw std::runtime_error(message);
    }
}

#if defined(_WIN32)
class QueryMemoryLimit
{
public:
    explicit QueryMemoryLimit(size_t headroom)
    {
        PROCESS_MEMORY_COUNTERS_EX memory{};
        memory.cb = sizeof(memory);
        Require(GetProcessMemoryInfo(GetCurrentProcess(),
            reinterpret_cast<PROCESS_MEMORY_COUNTERS*>(&memory), sizeof(memory)) != 0,
            "Could not measure native admission process memory.");
        _job = CreateJobObjectW(nullptr, nullptr);
        Require(_job != nullptr, "Could not create a process-local admission memory limit.");
        JOBOBJECT_EXTENDED_LIMIT_INFORMATION limits{};
        limits.BasicLimitInformation.LimitFlags = JOB_OBJECT_LIMIT_PROCESS_MEMORY;
        limits.ProcessMemoryLimit = memory.PrivateUsage + headroom;
        Require(SetInformationJobObject(_job, JobObjectExtendedLimitInformation, &limits, sizeof(limits)) != 0 &&
            AssignProcessToJobObject(_job, GetCurrentProcess()) != 0,
            "Could not apply the query-only admission memory limit.");
    }

    ~QueryMemoryLimit()
    {
        // Closing the last job handle does not remove limits from an assigned process.
        // Release this probe's quota before USD's asynchronous stage/data teardown.
        JOBOBJECT_EXTENDED_LIMIT_INFORMATION limits{};
        if (!SetInformationJobObject(_job, JobObjectExtendedLimitInformation, &limits, sizeof(limits)))
        {
            std::fputs("Could not release the probe's query-only memory limit.\n", stderr);
            std::_Exit(70);
        }
        CloseHandle(_job);
    }
    QueryMemoryLimit(const QueryMemoryLimit&) = delete;
    QueryMemoryLimit& operator=(const QueryMemoryLimit&) = delete;

private:
    HANDLE _job = nullptr;
};
#endif

void Run(const char* pluginPath, const char* stagePath, const std::string& mode)
{
    Require(openusd_get_abi_version() == OPENUSD_DATA_ABI_VERSION,
        "The admission probe loaded a DLL with a different public data ABI.");
    char diagnostic[4096]{};
    openusd_error_buffer error{diagnostic, sizeof(diagnostic), 0};
    size_t plugins = 0;
    Require(openusd_register_plugins(pluginPath, &plugins, &error) == OPENUSD_STATUS_OK, diagnostic);
    Require(!std::filesystem::exists(stagePath), "Admission probe refuses to overwrite an existing stage.");
    UsdStageRefPtr source = UsdStage::CreateNew(stagePath);
    const UsdGeomCamera camera = UsdGeomCamera::Define(source, SdfPath("/Camera"));
    const UsdRenderSettings settings = UsdRenderSettings::Define(source, SdfPath("/Settings"));
    const UsdRenderProduct product = UsdRenderProduct::Define(source, SdfPath("/Product"));
    const UsdRenderVar variable = UsdRenderVar::Define(source, SdfPath("/Var"));
    variable.CreateSourceNameAttr().Set(std::string("seed"));
    settings.CreateCameraRel().SetTargets({camera.GetPath()});
    settings.CreateProductsRel().SetTargets({product.GetPath()});
    settings.CreateIncludedPurposesAttr().Set(VtArray<TfToken>{TfToken("default"), TfToken("render")});
    product.CreateOrderedVarsRel().SetTargets({variable.GetPath()});
    source->GetRootLayer()->Save();
    openusd_stage* rawStage = nullptr;
    Require(openusd_stage_open(stagePath, &rawStage, &error) == OPENUSD_STATUS_OK, diagnostic);
    std::unique_ptr<openusd_stage, decltype(&openusd_stage_release)> stage(rawStage, openusd_stage_release);
    openusd_render_specification* owner = nullptr;
    openusd_render_specification_view view{};
    view.struct_size = sizeof(view);
    view.version = OPENUSD_RENDER_SPECIFICATION_VIEW_VERSION;
    Require(openusd_render_get_specification(stage.get(), "/Settings", &owner, &view, &error) ==
        OPENUSD_STATUS_OK, diagnostic);
    openusd_render_specification_release(owner);
    owner = nullptr;

    std::string explicitPath;
    SdfPathVector retainedTargetInput;
    if (mode == "expression")
    {
        std::string expression(64u << 20, 'x');
        const VtValue resident = VtValue::Take(expression);
        source->GetRootLayer()->SetField(SdfPath("/Var.sourceName"), SdfFieldKeys->Default, resident);
        const VtValue shared = source->GetRootLayer()->GetField(SdfPath("/Var.sourceName"), SdfFieldKeys->Default);
        Require(shared.UncheckedGet<std::string>().data() == resident.UncheckedGet<std::string>().data(),
            "The expression fixture was not resident shared VtValue storage.");
    }
    else if (mode == "cstring")
    {
        explicitPath.assign(64u << 20, 'x');
    }
    else if (mode == "purpose-edit")
    {
        const UsdRenderSettings weak = UsdRenderSettings::Define(source, SdfPath("/WeakPurposes"));
        weak.CreateIncludedPurposesAttr().Set(VtArray<TfToken>{TfToken("default")});
        settings.GetPrim().GetInherits().AddInherit(weak.GetPath());
        VtArrayEditBuilder<TfToken> builder;
        builder.SetSize(8388608);
        auto edit = builder.FinalizeAndReset();
        source->GetRootLayer()->SetField(
            SdfPath("/Settings.includedPurposes"), SdfFieldKeys->Default, VtValue::Take(edit));
    }
    else if (mode == "source-site" || mode == "source-site-cached")
    {
        const SdfPath classPath = SdfPath::AbsoluteRootPath().AppendChild(
            TfToken(std::string(64u << 20, 'c')));
        const UsdPrim inherited = source->CreateClassPrim(classPath);
        Require(inherited.CreateAttribute(TfToken("products"), SdfValueTypeNames->Int).Set(1) &&
            settings.GetPrim().GetInherits().AddInherit(classPath),
            "Could not author the long inherited source-site/property-kind conflict.");
        if (mode == "source-site-cached")
        {
            Require(classPath.GetString().size() == (64u << 20) + 1,
                "The source-site fixture did not populate the encoded path cache.");
        }
    }
    else if (mode == "source-variant-set" || mode == "source-variant-selection")
    {
        const UsdPrim inherited = source->CreateClassPrim(SdfPath("/VariantClass"));
        const std::string hugeName(64u << 20, 'v');
        const std::string setName("render");
        const std::string selected("selected");
        UsdVariantSet variants = inherited.GetVariantSets().AddVariantSet(
            mode == "source-variant-set" ? hugeName : setName);
        const std::string& selection = mode == "source-variant-selection" ? hugeName : selected;
        Require(variants.AddVariant(selection) && variants.SetVariantSelection(selection),
            "Could not author the long source-site variant component.");
        {
            const UsdEditContext context = variants.GetVariantEditContext();
            Require(inherited.CreateAttribute(TfToken("products"), SdfValueTypeNames->Int).Set(1),
                "Could not author the conflicting property in the inherited variant.");
        }
        Require(settings.GetPrim().GetInherits().AddInherit(inherited.GetPath()),
            "Could not inherit the variant source-site fixture.");
    }
    else if (mode == "source-site-shared")
    {
        const SdfPath classPath = SdfPath::AbsoluteRootPath().AppendChild(
            TfToken(std::string(2u << 20, 'c')));
        const UsdPrim inherited = source->CreateClassPrim(classPath);
        Require(inherited.CreateAttribute(TfToken("pixelAspectRatio"), SdfValueTypeNames->Float).Set(1.0f) &&
            classPath.GetString().size() == (2u << 20) + 1,
            "Could not prepare the resident, cached shared source-site path.");
        SdfPathVector products;
        for (size_t index = 0; index < 4; ++index)
        {
            const UsdRenderProduct output = UsdRenderProduct::Define(
                source, SdfPath("/SharedProduct" + std::to_string(index)));
            Require(output.GetPrim().GetInherits().AddInherit(classPath),
                "Could not share the admitted source site across render products.");
            products.push_back(output.GetPath());
        }
        Require(settings.GetProductsRel().SetTargets(products), "Could not target the shared-source products.");
    }
    else if (mode == "dual-role")
    {
        const UsdRelationship forwarding = product.GetPrim().CreateRelationship(TfToken("forwardCamera"));
        Require(forwarding.SetTargets({camera.GetPath()}) &&
            settings.GetCameraRel().SetTargets({forwarding.GetPath()}),
            "Could not first expose the product as a forwarding-only prim.");
        const UsdAttribute resolution = product.CreateResolutionAttr();
        std::string invalidDefault(64u << 20, 'x');
        const VtValue resident = VtValue::Take(invalidDefault);
        source->GetRootLayer()->SetField(resolution.GetPath(), SdfFieldKeys->Default, resident);
        const VtValue shared = source->GetRootLayer()->GetField(resolution.GetPath(), SdfFieldKeys->Default);
        Require(shared.UncheckedGet<std::string>().data() == resident.UncheckedGet<std::string>().data(),
            "The dual-role malformed product default was not resident shared storage.");
    }
    else if (mode == "properties" || mode == "property-order")
    {
        TfTokenVector names(4u << 20, TfToken("camera"));
        const VtValue resident = VtValue::Take(names);
        const TfToken field = mode == "properties" ?
            SdfChildrenKeys->PropertyChildren : SdfFieldKeys->PropertyOrder;
        source->GetRootLayer()->SetField(SdfPath("/Settings"), field, resident);
        const VtValue shared = source->GetRootLayer()->GetField(SdfPath("/Settings"), field);
        Require(shared.UncheckedGet<TfTokenVector>().data() == resident.UncheckedGet<TfTokenVector>().data(),
            "The property fixture was not resident shared VtValue storage.");
    }
    else if (mode == "targets" || mode == "targets-deleted" || mode == "targets-ordered" || mode == "walk")
    {
        const size_t count = mode == "walk" ? 20000 : 262145;
        SdfPath prefix = SdfPath::AbsoluteRootPath();
        if (mode == "walk")
        {
            for (size_t index = 0; index < 64; ++index)
            {
                prefix = prefix.AppendChild(TfToken("p"));
            }
        }
        retainedTargetInput.reserve(count);
        for (size_t index = 0; index < count; ++index)
        {
            retainedTargetInput.push_back(prefix.AppendChild(TfToken("Target" + std::to_string(index))));
        }
        SdfPathListOp targets;
        if (mode == "targets-deleted")
        {
            targets.SetDeletedItems(retainedTargetInput);
        }
        else if (mode == "targets-ordered")
        {
            targets.SetOrderedItems(retainedTargetInput);
        }
        else
        {
            targets.SetExplicitItems(retainedTargetInput);
        }
        const VtValue resident = VtValue::Take(targets);
        source->GetRootLayer()->SetField(SdfPath("/Settings.products"), SdfFieldKeys->TargetPaths, resident);
        const VtValue shared = source->GetRootLayer()->GetField(
            SdfPath("/Settings.products"), SdfFieldKeys->TargetPaths);
        Require(shared.UncheckedGet<SdfPathListOp>().GetExplicitItems().data() ==
            resident.UncheckedGet<SdfPathListOp>().GetExplicitItems().data(),
            "The target fixture was not resident shared VtValue storage.");
    }
    else
    {
        throw std::invalid_argument("Unknown native admission probe mode.");
    }

#if defined(_WIN32)
    // Scene storage and its authoring allocations predate this limit. The old query's
    // extra string/vector materialization cannot fit in the remaining 1 MiB.
    const size_t headroom = mode == "walk" ? (16u << 20) : (1u << 20);
    QueryMemoryLimit limit(headroom);
#endif
    view = {};
    view.struct_size = sizeof(view);
    view.version = OPENUSD_RENDER_SPECIFICATION_VIEW_VERSION;
    const openusd_status status = openusd_render_get_specification(
        stage.get(), mode == "cstring" ? explicitPath.c_str() : "/Settings", &owner, &view, &error);
    const bool reset = owner == nullptr && view.has_settings == 0 && view.products == nullptr &&
        view.variables == nullptr && view.data == nullptr;
    openusd_render_specification_release(owner);
    if (status != OPENUSD_STATUS_NATIVE_ERROR || !reset)
    {
        std::cerr << "status=" << status << ", reset=" << reset << ", diagnostic=" << diagnostic << '\n';
    }
    Require(status == OPENUSD_STATUS_NATIVE_ERROR && reset, "Oversized admission did not fail with reset outputs.");
    Require(std::string(diagnostic).find("budget") != std::string::npos,
        diagnostic[0] == '\0' ? "Admission failed without a budget diagnostic." : diagnostic);
    if (mode == "walk")
    {
        Require(std::string(diagnostic).find("source-admission walk budget") != std::string::npos,
            "Source traversal was not bounded before composed target materialization.");
    }
    if (mode == "source-site" || mode == "source-site-cached")
    {
        Require(std::string(diagnostic).find("path exceeds the UTF-8 byte budget") != std::string::npos,
            "The inherited source-site path was not rejected before property-kind diagnostics.");
    }
    if (mode == "source-variant-set" || mode == "source-variant-selection")
    {
        Require(std::string(diagnostic).find("UTF-8 byte budget") != std::string::npos,
            "A source-site variant component was copied before its byte-budget admission.");
    }
    if (mode == "source-site-shared")
    {
        Require(std::string(diagnostic).find("paths exceed the UTF-8 byte budget") != std::string::npos,
            "Reusing a cached source-site path bypassed the aggregate byte budget.");
    }
    if (mode == "dual-role")
    {
        Require(std::string(diagnostic).find("concrete schema-typed default opinions") != std::string::npos,
            "A cached forwarding prim bypassed later product-default admission.");
    }
    std::cout << "RENDER_ADMISSION_OK: " << mode << ", resident fixture, budget failure";
#if defined(_WIN32)
    std::cout << ", query-only extra memory limited to " << (headroom >> 20) << " MiB";
#endif
    std::cout << '\n';
}
}

int main(int argc, char** argv)
{
    if (argc != 4)
    {
        std::cerr << "Usage: openusd_render_admission_probe <plugin-path> <new-stage-path> <case>\n";
        return 2;
    }
    try
    {
        Run(argv[1], argv[2], argv[3]);
        Require(std::filesystem::remove(argv[2]), "Could not remove the native admission fixture.");
        return 0;
    }
    catch (const std::exception& exception)
    {
        std::cerr << exception.what() << '\n';
        return 1;
    }
}
