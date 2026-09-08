// Copyright (c) marcschier. Licensed under the MIT License.

#include "pxr/pxr.h"
#include "pxr/usd/sdf/storageAdmission.h"
#include "pxr/usd/sdf/layer.h"
#include "pxr/usd/sdf/listOp.h"
#include "pxr/usd/usd/stage.h"
#include "pxr/usd/usd/attribute.h"
#include "pxr/base/vt/array.h"
#include "pxr/imaging/hd/renderSettings.h"

#include <filesystem>
#include <fstream>
#include <cstring>
#include <iostream>
#include <limits>
#include <stdexcept>
#include <string>

PXR_NAMESPACE_USING_DIRECTIVE

namespace
{
void Require(bool value, const char* message)
{
    if (!value) throw std::runtime_error(message);
}
void ListComposition()
{
    SdfPathListOp paths;
    paths.SetExplicitItems({SdfPath("/A"), SdfPath("/B")});
    SdfPathVector result;
    SdfStorageAdmissionLimits limits;
    limits.maximumPeakMaterializationBytes = 128;
    SdfStorageAdmissionScope admission(limits);
    bool refused = false;
    try { paths.ApplyOperations(&result); }
    catch (const SdfStorageAdmissionError& error)
    {
        refused = error.GetStatus() == SdfStorageAdmissionStatus::QuotaExceeded;
    }
    Require(refused && result.empty(),
        "Native list-op composition allocated output before materialization admission.");
}

void NestedConstruction()
{
    SdfStorageAdmissionScope outer(SdfStorageAdmissionLimits{});
    bool refused = false;
    try
    {
        SdfStorageAdmissionScope nested(SdfStorageAdmissionLimits{});
    }
    catch (const std::logic_error& error)
    {
        refused = std::string_view(error.what()).find("nested") != std::string_view::npos;
    }
    Require(refused && SdfStorageAdmissionScope::GetCurrent() == &outer,
        "The public SDK constructor allowed a nested scope or replaced the enclosing TLS state.");
    outer.ReserveExternalMaterialization(1);
    outer.RequireSucceeded();
}

void SerializedLengths(const std::filesystem::path& directory)
{
    for (const bool overflow : {false, true})
    {
        const auto path = directory / (overflow ? "sdk-count-overflow.usdc" : "sdk-count-outside.usdc");
        Require(!std::filesystem::exists(path), "Refusing to overwrite a serialized length fixture.");
        {
            const auto stage = UsdStage::CreateNew(path.string());
            stage->DefinePrim(SdfPath("/Settings"))
                .CreateAttribute(TfToken("includedPurposes"), SdfValueTypeNames->TokenArray)
                .Set(VtArray<TfToken>(257, TfToken("length_marker")));
            stage->GetRootLayer()->Save();
        }
        std::vector<char> bytes;
        {
            std::ifstream input(path, std::ios::binary);
            bytes.assign(std::istreambuf_iterator<char>(input), std::istreambuf_iterator<char>());
        }
        // Locate the unique serialized fixture payload, not a C++ object layout.
        // Its known 257 identical token IDs make accidental matches detectable.
        size_t payload = 0, matches = 0;
        for (size_t offset = 0; offset + 8 + 257 * 4 <= bytes.size(); ++offset)
        {
            uint64_t count = 0;
            std::memcpy(&count, bytes.data() + offset, sizeof(count));
            if (count != 257) continue;
            bool repeated = true;
            for (size_t index = 1; index < 257; ++index)
                repeated = repeated && std::memcmp(bytes.data() + offset + 8,
                    bytes.data() + offset + 8 + index * 4, 4) == 0;
            if (repeated) { payload = offset; ++matches; }
        }
        Require(matches == 1 && bytes.size() < 4096, "Serialized length fixture was not unique and bounded.");
        const uint64_t declared = overflow ? std::numeric_limits<uint64_t>::max() : 1024;
        std::memcpy(bytes.data() + payload, &declared, sizeof(declared));
        {
            std::ofstream output(path, std::ios::binary | std::ios::trunc);
            output.write(bytes.data(), static_cast<std::streamsize>(bytes.size()));
        }
        auto layer = SdfLayer::FindOrOpen(path.string());
        Require(static_cast<bool>(layer), "The structural crate inventory could not open the malformed payload fixture.");
        {
            SdfStorageAdmissionScope admission(SdfStorageAdmissionLimits{});
            bool refused = false;
            try { (void)layer->GetField(SdfPath("/Settings.includedPurposes"), SdfFieldKeys->Default); }
            catch (const SdfStorageAdmissionError& error)
            {
                refused = error.GetStatus() == (overflow ? SdfStorageAdmissionStatus::QuotaExceeded :
                    SdfStorageAdmissionStatus::InvalidData);
            }
            const auto info = admission.GetLastResult();
            Require(refused && info.countKnown && info.itemCount == declared,
                "A forged serialized length was allocated, lost, or reported as complete/unknown zero.");
            bool sticky = false;
            try { admission.RequireSucceeded(); }
            catch (const SdfStorageAdmissionError&) { sticky = true; }
            Require(sticky, "A failed serialized admission could be published as a successful empty result.");
        }
        layer.Reset();
        std::filesystem::remove(path);
    }
}

void CompressedIntegers(const std::filesystem::path& directory)
{
    const auto path = directory / "sdk-compressed-integers.usdc";
    Require(!std::filesystem::exists(path), "Refusing to overwrite the decompression fixture.");
    {
        const auto stage = UsdStage::CreateNew(path.string());
        VtArray<int64_t> values(512);
        for (size_t index = 0; index < values.size(); ++index) values[index] = static_cast<int64_t>(index * 2);
        stage->DefinePrim(SdfPath("/Settings"))
            .CreateAttribute(TfToken("indexes"), SdfValueTypeNames->Int64Array).Set(values);
        stage->GetRootLayer()->Save();
    }
    Require(std::filesystem::file_size(path) < 512 * sizeof(int64_t),
        "The fixture was not stored using the native integer compression path.");
    auto layer = SdfLayer::FindOrOpen(path.string());
    const SdfPath property("/Settings.indexes");
    {
        SdfStorageAdmissionScope admission(SdfStorageAdmissionLimits{});
        const VtValue value = layer->GetField(property, SdfFieldKeys->Default);
        const auto& values = value.UncheckedGet<VtArray<int64_t>>();
        Require(values.size() == 512 && values[511] == 1022 && !admission.GetLastResult().resident &&
            admission.GetStatistics().peakMaterializationBytes > 12000,
            "Compressed integer admission did not include decode workspace and actual values.");
    }
    for (const bool workLimit : {false, true})
    {
        SdfStorageAdmissionLimits limits;
        if (workLimit) limits.maximumDecodeWork = 1000;
        else limits.maximumPeakMaterializationBytes = 12000;
        SdfStorageAdmissionScope admission(limits);
        bool refused = false;
        try { (void)layer->GetField(property, SdfFieldKeys->Default); }
        catch (const SdfStorageAdmissionError& error)
        {
            refused = error.GetStatus() == SdfStorageAdmissionStatus::QuotaExceeded;
        }
        Require(refused, "Compressed decode work/workspace bypassed admission.");
    }
    layer.Reset();
    std::filesystem::remove(path);
}

void Run(const std::filesystem::path& directory)
{
    Require(SdfStorageAdmissionScope::GetApiVersion() == 1, "SDK admission accessor version mismatch.");
    NestedConstruction();
    ListComposition();
    {
        SdfStorageAdmissionLimits limits;
        limits.maximumPeakMaterializationBytes = 128;
        limits.maximumTotalMaterializationBytes = 256;
        SdfStorageAdmissionScope admission(limits);
        admission.ReserveExternalMaterialization(96);
        bool refused = false;
        try { admission.ReserveExternalMaterialization(96); }
        catch (const SdfStorageAdmissionError& error)
        {
            refused = error.GetStatus() == SdfStorageAdmissionStatus::QuotaExceeded;
        }
        Require(refused, "Simultaneously retained reservations bypassed peak materialization admission.");
    }
    std::filesystem::create_directories(directory);
    SerializedLengths(directory);
    CompressedIntegers(directory);
    const auto path = directory / "sdk-storage.usdc";
    Require(!std::filesystem::exists(path), "SDK storage probe refuses to overwrite an input.");
    {
        const auto stage = UsdStage::CreateNew(path.string());
        const auto prim = stage->DefinePrim(SdfPath("/Settings"));
        prim.CreateAttribute(TfToken("includedPurposes"), SdfValueTypeNames->TokenArray)
            .Set(VtArray<TfToken>{TfToken("proxy"), TfToken("render")});
        stage->GetRootLayer()->Save();
    }
    auto stage = UsdStage::Open(path.string());
    auto layer = stage->GetRootLayer();
    const SdfPath property("/Settings.includedPurposes");
    uint64_t identity = 0, generation = 0;
    {
        SdfStorageAdmissionLimits limits;
        limits.maximumItems = 2;
        SdfStorageAdmissionScope admission(limits);
        const VtValue value = layer->GetField(property, SdfFieldKeys->Default);
        Require(value.IsHolding<VtArray<TfToken>>() && value.UncheckedGet<VtArray<TfToken>>().size() == 2,
            "A bounded serialized token array did not materialize.");
        const auto info = admission.GetLastResult();
        Require(info.status == SdfStorageAdmissionStatus::Ready && info.countKnown &&
            info.itemCount == 2 && !info.resident, "Serialized storage facts were not returned.");
        identity = info.storeIdentity;
        generation = info.storeGeneration;
        bool refused = false;
        try { (void)layer->GetField(property, SdfFieldKeys->Default); }
        catch (const SdfStorageAdmissionError& error)
        {
            refused = error.GetStatus() == SdfStorageAdmissionStatus::QuotaExceeded;
        }
        Require(refused, "A repeated native read bypassed cumulative item admission.");
        Require(admission.GetStatistics().items == 2, "Failed admission returned false successful read counts.");
    }
    layer->SetField(property, SdfFieldKeys->Default, VtValue(VtArray<TfToken>{TfToken("guide")}));
    {
        SdfStorageAdmissionScope admission(SdfStorageAdmissionLimits{});
        const auto value = layer->GetField(property, SdfFieldKeys->Default);
        const auto info = admission.GetLastResult();
        Require(value.UncheckedGet<VtArray<TfToken>>()[0] == TfToken("guide") &&
            info.resident && info.storeIdentity == identity && info.storeGeneration > generation,
            "Dirty in-memory overrides were replaced by original pathname contents.");
        layer->SetField(property, SdfFieldKeys->Default, VtValue(VtArray<TfToken>{TfToken("render")}));
        bool refused = false;
        try { (void)layer->GetField(property, SdfFieldKeys->Default); }
        catch (const SdfStorageAdmissionError& error)
        {
            refused = error.GetStatus() == SdfStorageAdmissionStatus::Stale;
        }
        Require(refused, "Storage generation changes were accepted within a preparation scope.");
    }
    {
        SdfStorageAdmissionLimits limits;
        limits.maximumPeakMaterializationBytes = 128;
        SdfStorageAdmissionScope admission(limits);
        bool refused = false;
        try { (void)layer->GetField(property, SdfFieldKeys->Default); }
        catch (const SdfStorageAdmissionError& error)
        {
            refused = error.GetStatus() == SdfStorageAdmissionStatus::QuotaExceeded;
        }
        Require(refused, "Peak materialization bounds were not enforced before copying.");
        Require(!admission.GetLastResult().countKnown,
            "A refusal before reading the value header invented a known zero count.");
    }
    Require(SdfStorageAdmissionScope::GetCurrent() == nullptr, "SDK admission scope leaked after release.");
    HdRenderSettings::RenderProduct product{};
    const auto before = hash_value(product);
    product.name = TfToken("changed");
    Require(before != hash_value(product), "The strict-build render-product hash prerequisite did not work.");
    layer.Reset();
    stage.Reset();
    std::filesystem::remove(path);
    std::cout << "SDK_STORAGE_ADMISSION_OK: serialized/dirty/generation/repeat/retained-peak/"
        "list-composition/forged-lengths/unknown-count/native-hash\n";
}
}

int main(int argc, char** argv)
{
    try
    {
        Require(argc == 2, "Expected owned work directory.");
        Run(argv[1]);
        return 0;
    }
    catch (const std::exception& error)
    {
        std::cerr << error.what() << '\n';
        return 1;
    }
}
