// Copyright (c) marcschier. Licensed under the MIT License.

#if defined(_WIN32)
#define NOMINMAX
#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <psapi.h>
#endif

#include "openusd_dotnet.h"
#include "pxr/base/tf/errorMark.h"
#include "pxr/base/vt/arrayEditBuilder.h"
#include "pxr/usd/sdf/layer.h"
#include "pxr/usd/sdf/storageAdmission.h"
#include "pxr/usd/usd/stage.h"
#include "pxr/usd/usdGeom/camera.h"
#include "pxr/usd/usdRender/settings.h"
#include "pxr/usd/usdRender/product.h"

#include <cstdio>
#include <cstdlib>
#include <filesystem>
#include <fstream>
#include <future>
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

uint64_t Fingerprint(const std::filesystem::path& path)
{
    std::ifstream input(path, std::ios::binary);
    Require(input.good(), "Cannot open the probe-owned source for byte validation.");
    uint64_t hash = 14695981039346656037ull;
    char buffer[4096];
    while (input.read(buffer, sizeof(buffer)) || input.gcount() != 0)
        for (std::streamsize index = 0; index < input.gcount(); ++index)
            hash = (hash ^ static_cast<unsigned char>(buffer[index])) * 1099511628211ull;
    return hash;
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
            "Cannot measure query process memory.");
        _job = CreateJobObjectW(nullptr, nullptr);
        Require(_job != nullptr, "Cannot create the query-only memory limit.");
        JOBOBJECT_EXTENDED_LIMIT_INFORMATION limits{};
        limits.BasicLimitInformation.LimitFlags = JOB_OBJECT_LIMIT_PROCESS_MEMORY;
        limits.ProcessMemoryLimit = memory.PrivateUsage + headroom;
        Require(SetInformationJobObject(_job, JobObjectExtendedLimitInformation, &limits, sizeof(limits)) &&
            AssignProcessToJobObject(_job, GetCurrentProcess()), "Cannot activate query memory limit.");
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

constexpr size_t LargeCount = 8u << 20;
constexpr size_t LargeOperations = 600000;

void Create(const std::filesystem::path& path, const std::string& kind)
{
    Require(!std::filesystem::exists(path), "Refusing to overwrite a crate admission fixture.");
    const auto source = UsdStage::CreateNew(path.string());
    const auto settings = UsdRenderSettings::Define(source, SdfPath("/Settings"));
    const auto camera = UsdGeomCamera::Define(source, SdfPath("/Camera"));
    const auto product = UsdRenderProduct::Define(source, SdfPath("/Product"));
    settings.CreateCameraRel().SetTargets({camera.GetPath()});
    settings.CreateProductsRel().SetTargets({product.GetPath()});
    settings.CreateResolutionAttr().Set(GfVec2i(800, 400));
    settings.CreateIncludedPurposesAttr().Set(VtArray<TfToken>{TfToken("proxy"), TfToken("render")});
    if (kind == "large-array")
    {
        VtArray<TfToken> values(LargeCount, TfToken("render"));
        source->GetRootLayer()->SetField(SdfPath("/Settings.includedPurposes"),
            SdfFieldKeys->Default, VtValue::Take(values));
    }
    else if (kind == "large-encoded-edit")
    {
        VtArrayEditBuilder<TfToken> builder;
        for (size_t index = 0; index < LargeOperations; ++index) builder.Write(TfToken("guide"), 0);
        auto edit = builder.FinalizeAndReset();
        Require(edit.GetEncodedInstructions().size() > (1u << 20),
            "The encoded edit fixture does not exceed the cumulative item quota.");
        source->GetRootLayer()->SetField(SdfPath("/Settings.includedPurposes"),
            SdfFieldKeys->Default, VtValue::Take(edit));
    }
    else if (kind == "large-intermediate")
    {
        VtArrayEditBuilder<TfToken> builder;
        builder.SetSize(LargeCount).SetSize(2);
        source->GetRootLayer()->SetField(SdfPath("/Settings.includedPurposes"),
            SdfFieldKeys->Default, VtValue(builder.FinalizeAndReset()));
    }
    else Require(kind == "small" || kind == "nested", "Unknown crate fixture.");
    if (kind == "nested")
        settings.GetPrim().CreateAttribute(TfToken("sampleText"), SdfValueTypeNames->String)
            .Set(std::string("retained"));
    source->GetRootLayer()->Save();
}

bool Cleared(const openusd_render_specification_view& view)
{
    return view.has_settings == 0 && view.reserved == 0 &&
        view.products == nullptr && view.products_size == 0 && view.product_count == 0 &&
        view.variables == nullptr && view.variables_size == 0 && view.variable_count == 0 &&
        view.render_var_indices == nullptr && view.render_var_indices_size == 0 &&
        view.render_var_index_count == 0 && view.data == nullptr && view.data_size == 0 &&
        view.offsets == nullptr && view.offsets_size == 0 && view.string_count == 0 &&
        view.included_purpose_count == 0 && view.material_binding_purpose_count == 0 &&
        view.namespaced_setting_count == 0;
}

void Query(openusd_stage* stage, bool success, const char* reason = "budget",
    const char* settingsPath = "/Settings")
{
    char diagnostic[1024]{};
    openusd_error_buffer error{diagnostic, sizeof(diagnostic), 0};
    openusd_render_specification* owner = nullptr;
    openusd_render_specification_view view{};
    view.struct_size = sizeof(view);
    view.version = OPENUSD_RENDER_SPECIFICATION_VIEW_VERSION;
    const auto status = openusd_render_get_specification(stage, settingsPath, &owner, &view, &error);
    std::unique_ptr<openusd_render_specification, decltype(&openusd_render_specification_release)>
        result(owner, openusd_render_specification_release);
    if (success && settingsPath == nullptr)
        Require(status == OPENUSD_STATUS_OK && owner == nullptr && Cleared(view),
            "Actual unauthored settings were not returned as clean absence outside an admission scope.");
    else if (success)
        Require(status == OPENUSD_STATUS_OK && owner != nullptr && view.product_count == 1 &&
            view.included_purpose_count == 2 && view.products[0].width == 800 &&
            std::string_view(view.data + view.offsets[2]) == "proxy",
            "Actual source store did not produce the admitted original specification.");
    else
        Require(status == OPENUSD_STATUS_NATIVE_ERROR && owner == nullptr && Cleared(view) &&
            std::string_view(diagnostic).find(reason) != std::string_view::npos,
            "Refused render input published a successful or partial specification.");
}

void ReadText(openusd_stage* stage)
{
    char diagnostic[1024]{};
    openusd_error_buffer error{diagnostic, sizeof(diagnostic), 0};
    char text[32]{};
    size_t required = 0;
    Require(openusd_stage_get_string(stage, "/Settings", "sampleText", 0, 0,
        text, sizeof(text), &required, &error) == OPENUSD_STATUS_OK &&
        std::string_view(text) == "retained", "The authoritative scope could not perform its admitted direct read.");
}

void NestedScope(openusd_stage* stage, const std::string& mode)
{
    {
        SdfStorageAdmissionLimits limits;
        if (mode != "nested-generous" && mode != "nested-accounting")
        {
            limits.maximumItems = 0;
            limits.maximumUtf8Bytes = 0;
            limits.maximumDecodeWork = 0;
            limits.maximumPeakMaterializationBytes = 0;
            limits.maximumTotalMaterializationBytes = 0;
        }
        SdfStorageAdmissionScope outer(limits);
        if (mode == "nested-accounting") ReadText(stage);
        const auto before = outer.GetStatistics();
        if (mode == "nested-threads")
        {
            auto worker = std::async(std::launch::async, [&]
            {
                Require(SdfStorageAdmissionScope::GetCurrent() == nullptr,
                    "A worker inherited another thread's enclosing admission scope.");
                Query(stage, true);
                {
                    SdfStorageAdmissionScope independent(SdfStorageAdmissionLimits{});
                    ReadText(stage);
                    Require(SdfStorageAdmissionScope::GetCurrent() == &independent &&
                        independent.GetStatistics().readCount != 0,
                        "The worker did not own an independent thread-local admission scope.");
                }
                Require(SdfStorageAdmissionScope::GetCurrent() == nullptr,
                    "The worker's scope leaked after release.");
                Query(stage, true);
            });
            worker.get();
        }
        if (mode == "nested-sticky")
        {
            char diagnostic[1024]{};
            openusd_error_buffer error{diagnostic, sizeof(diagnostic), 0};
            char text[32]{};
            size_t required = 0;
            Require(openusd_stage_get_string(stage, "/Settings", "sampleText", 0, 0,
                text, sizeof(text), &required, &error) == OPENUSD_STATUS_NATIVE_ERROR &&
                std::string_view(diagnostic).find("budget") != std::string_view::npos,
                "The actual C ABI string getter did not consume the enclosing zero budget.");
            bool failed = false;
            try { outer.RequireSucceeded(); }
            catch (const SdfStorageAdmissionError& failure)
            {
                failed = failure.GetStatus() == SdfStorageAdmissionStatus::QuotaExceeded;
            }
            Require(failed, "The enclosing scope did not retain its native getter failure.");
        }
        const char* settingsPath = mode == "nested-absence" ? nullptr : "/Settings";
        Query(stage, false, mode == "nested-sticky" ? "budget" : "nested", settingsPath);
        Require(SdfStorageAdmissionScope::GetCurrent() == &outer,
            "A refused nested query replaced or removed the enclosing scope.");
        if (mode != "nested-sticky")
        {
            outer.RequireSucceeded();
            const auto after = outer.GetStatistics();
            Require(after.items == before.items && after.utf8Bytes == before.utf8Bytes &&
                after.decodeWork == before.decodeWork && after.readCount == before.readCount &&
                after.peakMaterializationBytes == before.peakMaterializationBytes &&
                after.totalMaterializationBytes == before.totalMaterializationBytes,
                "A refused nested query or worker changed the enclosing accounting.");
            if (mode == "nested-accounting")
            {
                ReadText(stage);
                Require(outer.GetStatistics().readCount > before.readCount,
                    "The enclosing scope was no longer authoritative after failed nested construction.");
            }
        }
        else
        {
            bool failed = false;
            try { outer.RequireSucceeded(); }
            catch (const SdfStorageAdmissionError& failure)
            {
                failed = failure.GetStatus() == SdfStorageAdmissionStatus::QuotaExceeded;
            }
            Require(failed, "The refused nested query cleared the enclosing sticky failure.");
        }
    }
    Require(SdfStorageAdmissionScope::GetCurrent() == nullptr,
        "The enclosing admission scope was not released.");
    Query(stage, true);
    Query(stage, true);
    Query(stage, true, "budget", nullptr);
}

void Run(const char* pluginPath, const std::filesystem::path& directory, const std::string& mode)
{
    std::filesystem::create_directories(directory);
    const auto path = directory / ("crate-admission-" + mode + ".usdc");
    const bool originalLarge = mode == "original-large" || mode == "dirty-small" ||
        mode == "large-array";
    const bool nested = mode.rfind("nested-", 0) == 0;
    const auto kind = nested ? std::string("nested") : originalLarge ? std::string("large-array") :
        (mode == "large-encoded-edit" || mode == "large-intermediate" ? mode : "small");
    char diagnostic[1024]{};
    openusd_error_buffer error{diagnostic, sizeof(diagnostic), 0};
    size_t registered = 0;
    Require(openusd_register_plugins(pluginPath, &registered, &error) == OPENUSD_STATUS_OK, diagnostic);
    Create(path, kind);
    const auto originalHash = Fingerprint(path);
    openusd_stage* raw = nullptr;
    Require(openusd_stage_open(path.string().c_str(), &raw, &error) == OPENUSD_STATUS_OK, diagnostic);
    std::unique_ptr<openusd_stage, decltype(&openusd_stage_release)> stage(raw, openusd_stage_release);
    auto layer = SdfLayer::Find(path.string());
    Require(static_cast<bool>(layer), "The query did not retain the original crate layer.");
    const SdfPath property("/Settings.includedPurposes");
    const bool replacement = mode == "original-small" || mode == "original-large";
    auto retained = path;
    if (replacement)
    {
        const auto next = directory / ("crate-replacement-" + mode + ".usdc");
        Create(next, originalLarge ? "small" : "large-array");
        retained = directory / ("crate-original-" + mode + ".usdc");
        Require(!std::filesystem::exists(retained), "Refusing to overwrite an original mapping fixture.");
        std::filesystem::rename(path, retained);
        std::filesystem::rename(next, path);
        Require(Fingerprint(path) != originalHash && Fingerprint(retained) == originalHash,
            "The original resident mapping and current pathname are not distinct fixtures.");
    }
    if (mode == "dirty-small")
        layer->SetField(property, SdfFieldKeys->Default,
            VtValue(VtArray<TfToken>{TfToken("proxy"), TfToken("render")}));
    else if (mode == "dirty-large")
        layer->SetField(property, SdfFieldKeys->Default, VtValue(VtArray<TfToken>(LargeCount, TfToken("render"))));
    const auto currentHash = Fingerprint(path);
    uint64_t serial = 0;
    Require(openusd_stage_get_change_serial(stage.get(), &serial, &error) == OPENUSD_STATUS_OK, diagnostic);
    const bool memory = mode.rfind("large-", 0) == 0;
    if (nested)
    {
        NestedScope(stage.get(), mode);
    }
    else if (memory)
    {
#if defined(_WIN32)
        const auto direct = mode == "large-intermediate" ? UsdStage::Open(layer) : UsdStageRefPtr{};
        const auto attribute = direct ? direct->GetAttributeAtPath(property) : UsdAttribute{};
        // The same active query-only window is used for bounded refusal and the
        // real SDK materializing getter, not a synthetic allocation substitute.
        bool copyFailed = false;
        const char* failureKind = "none";
        {
            QueryMemoryLimit limit(1u << 20);
            Query(stage.get(), false);
            TfErrorMark errors;
            try
            {
                if (mode == "large-intermediate")
                {
                    VtArray<TfToken> value;
                    copyFailed = !attribute.Get(&value);
                    if (copyFailed) failureKind = "getter returned false";
                }
                else
                {
                    const VtValue value = layer->GetField(property, SdfFieldKeys->Default);
                    copyFailed = value.IsEmpty();
                    if (copyFailed) failureKind = "getter returned empty";
                }
            }
            catch (const std::bad_alloc&) { copyFailed = true; failureKind = "bad_alloc"; }
            if (!errors.IsClean()) { copyFailed = true; failureKind = "SDK TfError"; errors.Clear(); }
        }
        Require(copyFailed, "The actual SDK negative control did not fail under 1-MiB headroom.");
        const VtValue after = layer->GetField(property, SdfFieldKeys->Default);
        if (mode == "large-array")
            Require(after.IsHolding<VtArray<TfToken>>() &&
                after.UncheckedGet<VtArray<TfToken>>().size() == LargeCount,
                "The unconstrained native getter did not prove the large original array.");
        else
        {
            Require(after.IsHolding<VtArrayEdit<TfToken>>() &&
                (mode != "large-encoded-edit" ||
                    after.UncheckedGet<VtArrayEdit<TfToken>>().GetEncodedInstructions().size() > (1u << 20)),
                "The unconstrained native getter did not prove the original encoded edit.");
            if (mode == "large-intermediate")
            {
                VtArray<TfToken> value;
                Require(attribute.Get(&value) && value.size() == 2,
                    "The identical native composition getter did not recover after memory-limit release.");
            }
        }
        std::cout << "MEMORY_CONTROL: headroom=1048576, bounded C ABI quota refusal; actual SDK getter "
            "fails while constrained (" << failureKind << ") and succeeds after release; ";
        if (mode == "large-array")
            std::cout << "storedArrayElements=" << after.UncheckedGet<VtArray<TfToken>>().size() <<
                ", materializedArrayBytes=" << LargeCount * sizeof(TfToken);
        else if (mode == "large-encoded-edit")
            std::cout << "storedInstructionWords=" <<
                after.UncheckedGet<VtArrayEdit<TfToken>>().GetEncodedInstructions().size() <<
                ", instructionBytes=" <<
                after.UncheckedGet<VtArrayEdit<TfToken>>().GetEncodedInstructions().size() * sizeof(int64_t);
        else
            std::cout << "nativeIntermediateElements=" << LargeCount <<
                ", nativeIntermediateBytes=" << LargeCount * sizeof(TfToken) << ", finalElements=2";
        std::cout << '\n';
#else
        throw std::runtime_error("The memory negative control requires the Windows job-limit runner.");
#endif
    }
    else
    {
#if defined(_WIN32)
        QueryMemoryLimit limit(1u << 20);
#endif
        Query(stage.get(), mode == "original-small" || mode == "dirty-small");
    }
    uint64_t after = 0;
    Require(openusd_stage_get_change_serial(stage.get(), &after, &error) == OPENUSD_STATUS_OK &&
        serial == after && Fingerprint(path) == currentHash && Fingerprint(retained) == originalHash,
        "Preparation mutated the source bytes or stage revision.");
    layer.Reset();
    stage.reset();
    std::filesystem::remove(path);
    if (replacement) std::filesystem::remove(retained);
    std::cout << "RENDER_CRATE_ADMISSION_OK: " << mode << '\n';
}
}

int main(int argc, char** argv)
{
    try
    {
        Require(argc == 4, "Expected plugin path, owned work directory and case.");
        Run(argv[1], argv[2], argv[3]);
        return 0;
    }
    catch (const std::exception& error)
    {
        std::cerr << error.what() << '\n';
        return 1;
    }
}
