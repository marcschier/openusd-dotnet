// Copyright (c) marcschier. Licensed under the MIT License.

#if defined(_WIN32)
#define NOMINMAX
#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <psapi.h>
#endif

#include "openusd_property_inspection.h"
#include "pxr/base/vt/arrayEditBuilder.h"
#include "pxr/base/tf/diagnosticMgr.h"
#include "pxr/usd/sdf/layer.h"
#include "pxr/usd/pcp/layerStack.h"
#include "pxr/usd/pcp/primIndex.h"
#include "pxr/usd/usd/attribute.h"
#include "pxr/usd/usd/editContext.h"
#include "pxr/usd/usd/inherits.h"
#include "pxr/usd/usd/relationship.h"
#include "pxr/usd/usd/stage.h"
#include "pxr/usd/usd/variantSets.h"

#include <cstdlib>
#include <filesystem>
#include <iostream>
#include <memory>
#include <stdexcept>
#include <string>

PXR_NAMESPACE_USING_DIRECTIVE

namespace
{
void Require(bool value, const char* message)
{
    if (!value) throw std::runtime_error(message);
}

#if defined(_WIN32)
class QueryMemoryLimit
{
public:
    explicit QueryMemoryLimit(size_t headroom = 1u << 20)
    {
        PROCESS_MEMORY_COUNTERS_EX memory{};
        memory.cb = sizeof(memory);
        Require(GetProcessMemoryInfo(GetCurrentProcess(),
            reinterpret_cast<PROCESS_MEMORY_COUNTERS*>(&memory), sizeof(memory)) != 0,
            "Could not measure property probe memory.");
        _job = CreateJobObjectW(nullptr, nullptr);
        Require(_job != nullptr, "Could not create a process-local property admission memory limit.");
        JOBOBJECT_EXTENDED_LIMIT_INFORMATION limits{};
        limits.BasicLimitInformation.LimitFlags = JOB_OBJECT_LIMIT_PROCESS_MEMORY;
        limits.ProcessMemoryLimit = memory.PrivateUsage + headroom;
        Require(SetInformationJobObject(_job, JobObjectExtendedLimitInformation, &limits, sizeof(limits)) != 0 &&
            AssignProcessToJobObject(_job, GetCurrentProcess()) != 0,
            "Could not apply the property query-only memory limit.");
    }

    ~QueryMemoryLimit()
    {
        JOBOBJECT_EXTENDED_LIMIT_INFORMATION limits{};
        if (!SetInformationJobObject(_job, JobObjectExtendedLimitInformation, &limits, sizeof(limits)))
            std::_Exit(70);
        CloseHandle(_job);
    }

    QueryMemoryLimit(const QueryMemoryLimit&) = delete;
    QueryMemoryLimit& operator=(const QueryMemoryLimit&) = delete;

private:
    HANDLE _job = nullptr;
};
#endif

struct Owned
{
    openusd_property_snapshot* owner = nullptr;
    openusd_property_view view{};
    Owned()
    {
        view.struct_size = sizeof(view);
        view.version = OPENUSD_PROPERTY_INSPECTION_VERSION;
    }
    ~Owned() { openusd_property_snapshot_release(owner); }

    const openusd_property_entry& Find(const char* name) const
    {
        for (size_t index = 0; index < view.entry_count; ++index)
        {
            const auto& entry = view.entries[index];
            if (std::string_view(view.data + view.offsets[entry.name]) == name) return entry;
        }
        throw std::runtime_error("Expected property admission row is missing.");
    }
};

void Run(const char* plugins, const char* stagePath, const std::string& mode)
{
    Require(!std::filesystem::exists(stagePath), "Property probe refuses to overwrite an existing source.");
    char message[4096]{};
    openusd_error_buffer error{message, sizeof(message), 0};
    size_t registered = 0;
    Require(openusd_register_plugins(plugins, &registered, &error) == OPENUSD_STATUS_OK, message);
    UsdStageRefPtr source = UsdStage::CreateNew(stagePath);
    const UsdPrim prim = source->DefinePrim(SdfPath("/Subject"));
    Require(prim.CreateAttribute(TfToken("answer"), SdfValueTypeNames->Int).Set(7), "Could not author seed value.");
    source->GetRootLayer()->Save();
    openusd_stage* raw = nullptr;
    Require(openusd_stage_open(stagePath, &raw, &error) == OPENUSD_STATUS_OK, message);
    std::unique_ptr<openusd_stage, decltype(&openusd_stage_release)> stage(raw, openusd_stage_release);
    {
        Owned warm;
        Require(openusd_stage_get_prim_property_snapshot(stage.get(), "/Subject", 1, 2, nullptr,
            &warm.owner, &warm.view, &error) == OPENUSD_STATUS_OK, message);
    }
    const auto& layer = source->GetRootLayer();
    std::string longInput;
    std::vector<std::string> retainedErrorPaths;
    if (mode == "string" || mode == "mismatched-default")
    {
        prim.CreateAttribute(TfToken("large"), mode == "string" ? SdfValueTypeNames->String : SdfValueTypeNames->Double);
        std::string text(64u << 20, 'x');
        const VtValue resident = VtValue::Take(text);
        layer->SetField(SdfPath("/Subject.large"), SdfFieldKeys->Default, resident);
        const VtValue shared = layer->GetField(SdfPath("/Subject.large"), SdfFieldKeys->Default);
        Require(shared.UncheckedGet<std::string>().data() == resident.UncheckedGet<std::string>().data(),
            "Large string fixture was not resident shared storage.");
    }
    else if (mode == "array")
    {
        prim.CreateAttribute(TfToken("large"), SdfValueTypeNames->IntArray);
        VtArray<int> values(16u << 20, 7);
        const VtValue resident = VtValue::Take(values);
        layer->SetField(SdfPath("/Subject.large"), SdfFieldKeys->Default, resident);
        const VtValue shared = layer->GetField(SdfPath("/Subject.large"), SdfFieldKeys->Default);
        Require(shared.UncheckedGet<VtArray<int>>().cdata() == resident.UncheckedGet<VtArray<int>>().cdata(),
            "Large array fixture was not resident shared storage.");
    }
    else if (mode == "properties" || mode == "property-order")
    {
        TfTokenVector names(4u << 20, TfToken("answer"));
        const VtValue resident = VtValue::Take(names);
        layer->SetField(prim.GetPath(), mode == "properties" ?
            SdfChildrenKeys->PropertyChildren : SdfFieldKeys->PropertyOrder, resident);
    }
    else if (mode == "times")
    {
        prim.CreateAttribute(TfToken("large"), SdfValueTypeNames->Double);
        SdfTimeSampleMap samples;
        for (size_t index = 0; index < 262144; ++index) samples.emplace(static_cast<double>(index), VtValue(7.0));
        layer->SetField(SdfPath("/Subject.large"), SdfFieldKeys->TimeSamples, VtValue::Take(samples));
    }
    else if (mode == "array-edit" || mode == "sample-array-edit")
    {
        prim.CreateAttribute(TfToken("large"), SdfValueTypeNames->IntArray);
        VtArrayEditBuilder<int> builder;
        builder.SetSize(16u << 20);
        auto edit = builder.FinalizeAndReset();
        if (mode == "array-edit")
        {
            layer->SetField(SdfPath("/Subject.large"), SdfFieldKeys->Default, VtValue::Take(edit));
        }
        else
        {
            SdfTimeSampleMap samples{{1, VtValue(VtArray<int>{7})}, {3, VtValue::Take(edit)}};
            layer->SetField(SdfPath("/Subject.large"), SdfFieldKeys->TimeSamples, VtValue::Take(samples));
        }
    }
    else if (mode == "cstring")
    {
        longInput.assign(64u << 20, 'x');
    }
    else if (mode == "source-path")
    {
        const auto inherited = source->CreateClassPrim(
            SdfPath::AbsoluteRootPath().AppendChild(TfToken(std::string(64u << 20, 's'))));
        inherited.CreateAttribute(TfToken("answer"), SdfValueTypeNames->Int).Set(3);
        prim.GetInherits().AddInherit(inherited.GetPath());
    }
    else if (mode == "variant-set" || mode == "variant-selection")
    {
        const auto inherited = source->CreateClassPrim(SdfPath("/VariantClass"));
        const std::string huge(64u << 20, 'v');
        UsdVariantSet variants = inherited.GetVariantSets().AddVariantSet(mode == "variant-set" ? huge : "look");
        const std::string selection = mode == "variant-selection" ? huge : "selected";
        Require(variants.AddVariant(selection) && variants.SetVariantSelection(selection), "Could not author variant.");
        {
            UsdEditContext context = variants.GetVariantEditContext();
            inherited.CreateAttribute(TfToken("answer"), SdfValueTypeNames->Int).Set(3);
        }
        prim.GetInherits().AddInherit(inherited.GetPath());
    }
    else if (mode == "targets")
    {
        prim.CreateRelationship(TfToken("large"));
        SdfPathVector targets;
        targets.reserve(262145);
        for (size_t index = 0; index < 262145; ++index) targets.emplace_back("/Target" + std::to_string(index));
        SdfPathListOp operation;
        operation.SetExplicitItems(targets);
        layer->SetField(SdfPath("/Subject.large"), SdfFieldKeys->TargetPaths, VtValue::Take(operation));
    }
    else if (mode == "prim-errors")
    {
        SdfReferenceListOp operation;
        operation.SetExplicitItems({SdfReference(std::string(),
            SdfPath::AbsoluteRootPath().AppendChild(TfToken(std::string(4u << 20, 'm'))))});
        TfDiagnosticMgr::GetInstance().SetQuiet(true);
        layer->SetField(prim.GetPath(), SdfFieldKeys->References, VtValue::Take(operation));
        TfDiagnosticMgr::GetInstance().SetQuiet(false);
    }
    else if (mode == "layer-errors")
    {
        const std::vector<std::string> paths{std::string(4u << 20, 'm') + ".usda"};
        TfDiagnosticMgr::GetInstance().SetQuiet(true);
        layer->SetSubLayerPaths(paths);
        TfDiagnosticMgr::GetInstance().SetQuiet(false);
    }
    else if (mode == "layer-error-list")
    {
        retainedErrorPaths.reserve(131072);
        for (size_t index = 0; index < 131072; ++index)
            retainedErrorPaths.push_back("property-admission-missing-" + std::to_string(index) + ".usda");
        TfDiagnosticMgr::GetInstance().SetQuiet(true);
        layer->SetSubLayerPaths(retainedErrorPaths);
        TfDiagnosticMgr::GetInstance().SetQuiet(false);
    }
    else throw std::invalid_argument("Unknown property admission case.");

    const bool composition = mode == "prim-errors" || mode == "layer-errors" || mode == "layer-error-list";
    const bool refusal = mode == "properties" || mode == "targets" || mode == "cstring" ||
        mode == "source-path" || mode == "variant-set" || mode == "variant-selection" || composition;
    Owned snapshot;
    {
#if defined(_WIN32)
        // Fixture storage and composition predate this limit. An extra whole
        // string/array/property/target/sample materialization cannot fit.
        QueryMemoryLimit limit;
#endif
        const auto status = openusd_stage_get_prim_property_snapshot(stage.get(),
            mode == "cstring" ? longInput.c_str() : "/Subject", 1, mode == "sample-array-edit" ? 1 : 2, nullptr,
            &snapshot.owner, &snapshot.view, &error);
        if (refusal)
        {
            Require(status == OPENUSD_STATUS_NATIVE_ERROR && snapshot.owner == nullptr &&
                snapshot.view.entries == nullptr && snapshot.view.entry_count == 0 &&
                snapshot.view.data == nullptr && snapshot.view.data_size == 0,
                "Oversized property query did not fail with cleared outputs.");
            Require(std::string_view(message).find(composition ? "composition" : "quota") != std::string_view::npos,
                "Property admission allocated too early instead of reporting a quota.");
            if (composition)
            {
                Require(std::string_view(message) ==
                    "Property inventory is unavailable because the prim has stored composition errors." ||
                    std::string_view(message) ==
                    "Property inventory is unavailable because a layer stack has stored composition errors.",
                    "Stored SDK error objects were formatted instead of using the fixed diagnostic.");
            }
        }
        else
        {
            Require(status == OPENUSD_STATUS_OK, message);
            if (mode != "property-order")
            {
                const auto& entry = snapshot.Find("large");
                if (mode == "array")
                    Require(entry.value.total_count == (16u << 20) && entry.value.count == 16,
                        "A 64MiB array was not prefix-read under the query-only memory limit.");
                else if (mode == "string")
                    Require(entry.value.total_count == 1 && entry.value.count == 1 &&
                        entry.value.status == OPENUSD_PROPERTY_TRUNCATED,
                        "A 64MiB string was not prefix-read under the query-only memory limit.");
                else if (mode == "mismatched-default")
                    Require(entry.value.status == OPENUSD_PROPERTY_UNSUPPORTED && entry.value.count == 0,
                        "A malformed raw string was presented as a typed composed double.");
                else if (mode == "times")
                    Require(entry.time_samples.total_count == 262144 && entry.time_samples.count == 16,
                        "Large sample metadata was materialized rather than counted and prefix-read.");
                else if (mode == "sample-array-edit")
                    Require(entry.value.status == OPENUSD_PROPERTY_COMPLETE &&
                        entry.time_samples.status == OPENUSD_PROPERTY_DEFERRED &&
                        entry.time_samples.total_count == OPENUSD_PROPERTY_UNKNOWN_COUNT,
                        "Array-edit samples outside the exact value time were misreported as a complete composed domain.");
                else
                    Require(entry.value.status == OPENUSD_PROPERTY_DEFERRED &&
                        entry.value.reason == OPENUSD_PROPERTY_REASON_ARRAY_EDIT,
                        "Array-edit composition was expanded instead of explicitly deferred.");
            }
        }
#if defined(_WIN32)
        if (mode == "layer-error-list")
        {
            bool copyRejected = false;
            try
            {
                // The negative control intentionally exercises the old copying
                // SDK getter under the SAME limit as the successful C query.
                const auto copied = prim.GetPrimIndex().GetRootNode().GetLayerStack()->GetLocalErrors();
                volatile size_t observedCopyCount = copied.size();
                (void)observedCopyCount;
            }
            catch (const std::bad_alloc&)
            {
                copyRejected = true;
            }
            Require(copyRejected, "The by-value error-vector negative control did not exceed the 1MiB limit.");
        }
#endif
    }
    if (composition)
    {
        // Fixture inspection is outside the query-only memory limit. This
        // intentionally copying SDK API must not be used by the actual query.
        const auto& index = prim.GetPrimIndex();
        const size_t errors = mode == "prim-errors" ? index.GetLocalErrors().size() :
            index.GetRootNode().GetLayerStack()->GetLocalErrors().size();
        Require(errors != 0, "The resident large-diagnostic composition-error fixture was not established.");
        if (mode == "layer-error-list")
        {
            Require(errors == 131072, "The large resident composition-error list was not established.");
#if defined(_WIN32)
            std::cout << "PROPERTY_ERROR_VECTOR_NEGATIVE_CONTROL_OK: entries=" << errors
                << " entry_bytes=" << sizeof(PcpErrorBasePtr)
                << " copy_bytes=" << errors * sizeof(PcpErrorBasePtr)
                << " headroom_bytes=" << (1u << 20) << " by_value_copy=bad_alloc bounded_query=refusal\n";
#endif
        }
        else
        {
            std::cout << "PROPERTY_LONG_DIAGNOSTIC_GUARD_OK: domain=" << mode
                << " stored_error_entries=" << errors << " diagnostic_claim_bytes=" << (4u << 20)
                << " bounded_query=fixed_diagnostic_refusal\n";
        }
    }
    source.Reset();
    stage.reset();
    Require(std::filesystem::remove(stagePath), "Could not remove property admission fixture.");
    std::cout << "PROPERTY_ADMISSION_OK: " << mode << ", resident source";
#if defined(_WIN32)
    std::cout << ", query-only extra memory <=1MiB";
#endif
    std::cout << '\n';
}
}

int main(int argc, char** argv)
{
    try
    {
        Require(argc == 4, "Expected plugin directory, fresh source path and case.");
        Run(argv[1], argv[2], argv[3]);
        return 0;
    }
    catch (const std::exception& exception)
    {
        std::cerr << exception.what() << '\n';
        return 1;
    }
}
