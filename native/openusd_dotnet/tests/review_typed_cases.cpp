// Copyright (c) marcschier. Licensed under the MIT License.
#include "review_probe_support.h"
#include "pxr/usd/sdf/relationshipSpec.h"
#include "pxr/usd/sdf/timeCode.h"
#include "pxr/base/gf/matrix4d.h"
#include "pxr/base/gf/quatd.h"
#include "pxr/base/gf/quath.h"
#include "pxr/base/gf/vec3f.h"

#include <iostream>

PXR_NAMESPACE_USING_DIRECTIVE
namespace ReviewProbe
{
namespace
{
double Bits(uint64_t bits)
{
    double value; std::memcpy(&value, &bits, 8); return value;
}
uint64_t Bits(double value)
{
    uint64_t bits; std::memcpy(&bits, &value, 8); return bits;
}
}
void TypedExactness(const fs::path& output)
{
    ReviewApi api;
    const auto source = Basic(output / "typed-exact");
    auto stage = api.Open(source);
    auto review = api.User(stage.get());
    auto native = api.Native(review.get());
    const double nan = Bits(uint64_t{0x7ff8000000000123});
    const double minusZero = Bits(uint64_t{0x8000000000000000});
    const auto prim = SdfCreatePrimInLayer(native, SdfPath("/World"));
    const auto attr = [&](const char* name, const SdfValueTypeName& type, VtValue value)
    {
        const auto attribute = SdfAttributeSpec::New(prim, name, type);
        if (!value.IsEmpty()) { attribute->SetDefaultValue(value); }
        return attribute;
    };
    attr("weight", SdfValueTypeNames->Double, VtValue(minusZero));
    const auto samples = attr("sampled", SdfValueTypeNames->Double, VtValue(SdfValueBlock()));
    native->SetTimeSample(samples->GetPath(), 1.0, VtValue(nan));
    native->SetTimeSample(samples->GetPath(), 2.0, VtValue(minusZero));
    native->SetTimeSample(samples->GetPath(), 3.0, VtValue(SdfValueBlock()));
    attr("noDefault", SdfValueTypeNames->Double, {});
    attr("vectorArray", SdfValueTypeNames->Float3Array, VtValue(VtArray<GfVec3f>{GfVec3f(1, -0.0f, 3), GfVec3f(4, 5, 6)}));
    attr("matrixArray", SdfValueTypeNames->Matrix4dArray, VtValue(VtArray<GfMatrix4d>{GfMatrix4d(1)}));
    attr("quaternion", SdfValueTypeNames->Quatd, VtValue(GfQuatd(minusZero, 1, 2, 3)));
    GfHalf half;
    half.setBits(0xfe01);
    attr("halfPayload", SdfValueTypeNames->Half, VtValue(half));
    attr("halfQuaternion", SdfValueTypeNames->Quath, VtValue(GfQuath(half, GfHalf(1), GfHalf(2), GfHalf(3))));
    attr("unsigned", SdfValueTypeNames->UInt64, VtValue(uint64_t{0xf123456789abcdef}));
    attr("timeValue", SdfValueTypeNames->TimeCode, VtValue(SdfTimeCode(minusZero)));
    VtDictionary dictionary;
    dictionary["nested"] = VtValue(VtDictionary{{"zero", VtValue(minusZero)}, {"nan", VtValue(nan)}});
    dictionary["words"] = VtValue(VtArray<std::string>{"zero", "one"});
    native->SetField(SdfPath::AbsoluteRootPath(), SdfFieldKeys->CustomLayerData, VtValue(dictionary));
    SdfPathListOp targets;
    targets.SetAddedItems({SdfPath("/Added")});
    targets.SetPrependedItems({SdfPath("/Prepended")});
    targets.SetAppendedItems({SdfPath("/Appended")});
    targets.SetDeletedItems({SdfPath("/Deleted")});
    targets.SetOrderedItems({SdfPath("/Appended"), SdfPath("/Prepended")});
    const auto rel = SdfRelationshipSpec::New(prim, "targets", false);
    native->SetField(rel->GetPath(), SdfFieldKeys->TargetPaths, VtValue(targets));
    SdfTokenListOp tokens;
    tokens.SetExplicitItems({});
    native->SetField(SdfPath("/World"), TfToken("apiSchemas"), VtValue(tokens));
    const auto saved = api.Save(review.get(), api.Binding(stage.get()), output / "typed.urd");
    auto target = api.Open(source);
    api.Import(target.get(), saved.document, api.Binding(target.get()));
    auto loaded = api.User(target.get());
    const auto layer = api.Native(loaded.get());
    Require(Bits(layer->GetField(SdfPath("/World.weight"), SdfFieldKeys->Default).UncheckedGet<double>()) == 0x8000000000000000,
        "Portable import preserves negative-zero bits, not numeric equality.");
    VtValue sample;
    Require(layer->QueryTimeSample(SdfPath("/World.sampled"), 1, &sample)
        && sample.IsHolding<double>() && Bits(sample.UncheckedGet<double>()) == 0x7ff8000000000123,
        "Portable import preserves a particular NaN payload.");
    Require(layer->QueryTimeSample(SdfPath("/World.sampled"), 3, &sample) && sample.IsHolding<SdfValueBlock>(),
        "Portable import preserves exact sampled value blocks.");
    Require(layer->GetField(SdfPath("/World.sampled"), SdfFieldKeys->Default).IsHolding<SdfValueBlock>()
        && !layer->HasField(SdfPath("/World.noDefault"), SdfFieldKeys->Default),
        "Blocked and absent defaults stay distinct.");
    Require(layer->HasField(SdfPath("/World.weight"), SdfFieldKeys->Custom)
        && layer->HasField(SdfPath("/World.weight"), SdfFieldKeys->Variability),
        "Authored fallback-valued declaration fields cannot be elided.");
    Require(layer->GetField(SdfPath("/World.targets"), SdfFieldKeys->TargetPaths).UncheckedGet<SdfPathListOp>() == targets
        && layer->GetField(SdfPath("/World"), TfToken("apiSchemas")).UncheckedGet<SdfTokenListOp>().IsExplicit(),
        "Every list-op bucket and explicit-empty mode is preserved.");
    Require(layer->GetField(SdfPath("/World.halfPayload"), SdfFieldKeys->Default).UncheckedGet<GfHalf>().bits() == 0xfe01,
        "Half IEEE payloads remain exact.");
    const auto importedDictionary = layer->GetField(SdfPath::AbsoluteRootPath(), SdfFieldKeys->CustomLayerData).UncheckedGet<VtDictionary>();
    const auto nested = importedDictionary.find("nested");
    Require(nested != importedDictionary.end() && nested->second.IsHolding<VtDictionary>(), "Nested metadata is present.");
    const auto& entries = nested->second.UncheckedGet<VtDictionary>();
    const auto value = entries.find("nan");
    Require(value != entries.end() && value->second.IsHolding<double>()
        && Bits(value->second.UncheckedGet<double>()) == 0x7ff8000000000123,
        "Nested metadata preserves IEEE bits.");
    const auto resaved = api.Save(loaded.get(), api.Binding(target.get()), output / "typed-again.urd");
    // The native format stores typed review bytes after variable-length metadata.
    const auto reviewBytes = [](const Bytes& document)
    {
        Packet p{document, 48};
        for (int i = 0; i < 6; ++i) { p.Text(); }
        const auto count = p.U32();
        for (uint32_t i = 0; i < count; ++i) { p.Text(); p.Text(); p.U64(); p.U32(); }
        return p.Blob();
    };
    Require(reviewBytes(saved.document) == reviewBytes(resaved.document) && saved.id == resaved.id,
        "Re-capture after import preserves complete exact typed payload and portable lineage.");
    std::cout << "PASS exact types: IEEE double/half payloads, signed zero, blocks/absent/default/samples,"
        " fallback declaration presence, every list bucket, explicit empty, arrays/quaternions/matrices/metadata\n";
}

void ProcessMode(const fs::path& output, const std::string& mode)
{
    const auto directory = output / "separate-process";
    const auto path = directory / "root.usda";
    const auto documentPath = directory / "document.urd";
    ReviewApi api;
    if (mode == "write")
    {
        Composition(directory);
        auto stage = api.Open(path);
        auto review = api.User(stage.get());
        const auto prim = SdfCreatePrimInLayer(api.Native(review.get()), SdfPath("/World"));
        SdfAttributeSpec::New(prim, "reviewImage", SdfValueTypeNames->Asset)
            ->SetDefaultValue(VtValue(SdfAssetPath("./textures/picture.bin")));
        const auto saved = Edited(api, stage.get(), review.get(), documentPath);
        Write(documentPath, std::string_view(reinterpret_cast<const char*>(saved.document.data()), saved.document.size()));
        Write(directory / "old-receipt.bin", std::string_view(reinterpret_cast<const char*>(saved.receipt.data()), saved.receipt.size()));
        Write(directory / "document-id.txt", saved.id);
        const auto state = api.State(review.get());
        Write(directory / "old-state.bin", std::string_view(reinterpret_cast<const char*>(state.data()), state.size()));
        std::cout << "PASS separate-process writer: public capture bytes physically published by caller, receipt out-of-file\n";
    }
    else
    {
        Require(mode == "read", "Unknown process probe mode.");
        const auto bytes = Read(documentPath);
        const Bytes document(bytes.begin(), bytes.end());
        fs::create_directories(directory / "different-cwd");
        fs::current_path(directory / "different-cwd");
        openusd_edit_buffer* owner = nullptr;
        openusd_edit_buffer_view view{};
        const auto expected = path.u8string();
        api.Ok(openusd_review_document_read(document.data(), document.size(), expected.data(), expected.size(), &owner, &view, &api.error));
        const auto read = Api::Copy(owner, view);
        Require(ReadU32(read, 12 + ReadU32(read, 8)) == 0, "New process reader cannot reconstruct a receipt.");
        auto stage = api.Open(path);
        const auto state = api.Import(stage.get(), document, api.Binding(stage.get()));
        const auto previous = Read(directory / "old-state.bin");
        const Bytes oldState(previous.begin(), previous.end());
        Require(ReadU64(state, 12) != ReadU64(oldState, 12) && ReadU64(state, 20) != ReadU64(oldState, 20),
            "Separate process import creates new stage/history identities.");
        auto review = api.User(stage.get());
        const auto native = api.Native(review.get());
        Require(api.Number(stage.get(), "/World", "weight") == 47
            && api.Number(stage.get(), "/World/Choice", "selected") == 17,
            "Separate process preserves source composition and review.");
        const auto raw = native->GetField(SdfPath("/World.reviewImage"), SdfFieldKeys->Default).UncheckedGet<SdfAssetPath>().GetAuthoredPath();
        Require(raw == "./textures/picture.bin", "Separate process leaves raw relative assets unchanged.");
        const auto resolved = ArGetResolver().Resolve(SdfComputeAssetPathRelativeToLayer(native, raw));
        Require(resolved && fs::equivalent(fs::u8path(resolved.GetPathString()), directory / "textures" / "picture.bin"),
            "Separate process uses real saved anchor despite a different working directory.");
        const auto oldReceipt = Read(directory / "old-receipt.bin");
        int32_t acknowledged = -1;
        api.Ok(openusd_layer_review_acknowledge_saved(review.get(), reinterpret_cast<const uint8_t*>(oldReceipt.data()),
            oldReceipt.size(), &acknowledged, &api.error));
        Require(acknowledged == 0, "Receipt is process-local, not part of the persisted portable document.");
        const auto saved = api.Save(review.get(), api.Binding(stage.get()), directory / "another-save-location" / "new.urd");
        Require(saved.id == Read(directory / "document-id.txt"), "Document lineage survives separate-process SaveAs.");
        Require(Read(documentPath) == bytes, "Reader/importer never rewrites the persisted source document.");
        std::cout << "PASS separate-process reader: explicit source, new history, old receipt rejection,"
            " unchanged raw assets, different CWD, anchored source composition and portable lineage\n";
    }
}
}
