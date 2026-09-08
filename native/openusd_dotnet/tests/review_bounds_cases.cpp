// Copyright (c) marcschier. Licensed under the MIT License.
#include "review_probe_support.h"
#include "pxr/usd/sdf/relationshipSpec.h"

#include <array>
#include <iostream>
#include <iomanip>
#include <sstream>

#if defined(OPENUSD_DOTNET_ENABLE_TEST_HOOKS)
extern "C" OPENUSD_DOTNET_API void openusd_review_test_fail_after(int32_t phase);
#endif

#if defined(_WIN32)
#ifndef NOMINMAX
#define NOMINMAX
#endif
#include <Windows.h>
#include <bcrypt.h>
#endif

PXR_NAMESPACE_USING_DIRECTIVE
namespace ReviewProbe
{
namespace
{
void Set32(Bytes& bytes, size_t offset, uint32_t value)
{
    Require(offset <= bytes.size() && bytes.size() - offset >= 4, "Bounded test packet mutation.");
    for (size_t i = 0; i < 4; ++i) { bytes[offset + i] = static_cast<uint8_t>(value >> (8 * i)); }
}
#if defined(_WIN32)
std::array<uint8_t, 32> IndependentHash(const uint8_t* bytes, size_t size)
{
    std::array<uint8_t, 32> hash{};
    Require(size <= ULONG_MAX && BCryptHash(BCRYPT_SHA256_ALG_HANDLE, nullptr, 0,
        const_cast<PUCHAR>(bytes), static_cast<ULONG>(size), hash.data(), static_cast<ULONG>(hash.size())) >= 0,
        "Independent OS SHA-256 oracle.");
    return hash;
}
void Rechecksum(Bytes& document)
{
    Require(document.size() >= 48, "Checksum fixture has URD1 header.");
    Set32(document, 8, static_cast<uint32_t>(document.size() - 48));
    const auto hash = IndependentHash(document.data() + 48, document.size() - 48);
    std::copy(hash.begin(), hash.end(), document.begin() + 16);
}
#endif
size_t ReviewOffset(const Bytes& document)
{
    Packet p{document, 48};
    for (int i = 0; i < 6; ++i) { p.Text(); }
    const auto count = p.U32();
    for (uint32_t i = 0; i < count; ++i) { p.Text(); p.Text(); p.U64(); p.U32(); }
    p.U32();
    return p.offset;
}
}

void InspectionOnly(const fs::path& output)
{
    ReviewApi api;
    const auto source = Basic(output / "inspection-only");
    Saved saved;
    {
        auto stage = api.Open(source);
        auto review = api.User(stage.get());
        saved = Edited(api, stage.get(), review.get(), output / "inspection-only.urd");
    }
    const auto original = Read(source);
    fs::remove(source);
    openusd_edit_buffer* owner = nullptr;
    openusd_edit_buffer_view view{};
#if defined(OPENUSD_DOTNET_ENABLE_TEST_HOOKS)
    openusd_review_test_fail_after(5);
#endif
    const auto status = openusd_review_document_inspect(
        saved.document.data(), saved.document.size(), &owner, &view, &api.error);
#if defined(OPENUSD_DOTNET_ENABLE_TEST_HOOKS)
    openusd_review_test_fail_after(-1);
#endif
    api.Ok(status);
    const auto metadata = Api::Copy(owner, view);
    Packet reader{metadata};
    Require(reader.U32() == 0x31494452 && reader.U32() == 1 &&
        reader.U32() == saved.document.size(), "Inspection returns only versioned RDI1 metadata.");
    Require(reader.Text() == saved.id && reader.Text() == saved.source &&
        reader.Text() == saved.fingerprint && reader.Text() == saved.originalTarget &&
        reader.Text() == saved.target && reader.Text() == saved.anchor,
        "Inspection must reveal exact recorded claims without source access or a receipt.");
    const auto files = reader.U32();
    for (uint32_t i = 0; i < files; ++i) { reader.Text(); reader.Text(); reader.U64(); reader.U32(); }
    Require(reader.offset == metadata.size(), "Inspection metadata contains no document/receipt payload.");
    Require(!fs::exists(source) && !fs::exists(output / "inspection-only.urd"),
        "Inspection cannot create source or publication files.");
#if defined(_WIN32)
    {
        const std::string unknownRoot = "Z:/review-inspection-missing/source.usda";
        const std::string unknownAnchor = "Z:/review-inspection-missing";
        const std::string remoteTarget = "//review-inspection.invalid/share/review.urd";
        const auto body = ReviewOffset(saved.document);
        Bytes forged(saved.document.begin(), saved.document.begin() + 48);
        Text(forged, saved.id); Text(forged, unknownRoot); Text(forged, saved.fingerprint);
        Text(forged, saved.originalTarget); Text(forged, remoteTarget); Text(forged, unknownAnchor);
        U32(forged, 1);
        Text(forged, unknownRoot); Text(forged, saved.fingerprint);
        U32(forged, static_cast<uint32_t>(original.size())); U32(forged, 0); U32(forged, 0);
        U32(forged, static_cast<uint32_t>(saved.document.size() - body));
        forged.insert(forged.end(), saved.document.begin() + body, saved.document.end());
        Rechecksum(forged);
#if defined(OPENUSD_DOTNET_ENABLE_TEST_HOOKS)
        openusd_review_test_fail_after(5);
#endif
        const auto inspected = openusd_review_document_inspect(
            forged.data(), forged.size(), &owner, &view, &api.error);
#if defined(OPENUSD_DOTNET_ENABLE_TEST_HOOKS)
        openusd_review_test_fail_after(-1);
#endif
        api.Ok(inspected);
        const auto claims = Api::Copy(owner, view);
        Packet claimed{claims, 12};
        claimed.Text();
        Require(claimed.Text() == unknownRoot, "Inspection reports untrusted missing-source claims without resolving them.");
        claimed.Text(); claimed.Text();
        Require(claimed.Text() == remoteTarget, "Inspection must not contact an untrusted network publication path.");
    }
#endif
    Write(source, original);
    const auto explicitSource = source.u8string();
    api.Ok(openusd_review_document_read(saved.document.data(), saved.document.size(),
        explicitSource.data(), explicitSource.size(), &owner, &view, &api.error));
    Api::Copy(owner, view);
    auto target = api.Open(source);
    api.Import(target.get(), saved.document, api.Binding(target.get()));
    Require(api.Number(target.get(), "/World", "weight") == 47,
        "Discovered source claims can subsequently follow explicit validated import.");
    std::cout << "PASS inspection: metadata-only source discovery, no filesystem scope/access, no payload/receipt, explicit import remains separate\n";
}
void MalformedAndBounds(const fs::path& output)
{
    ReviewApi api;
    const auto source = Basic(output / "malformed");
    auto stage = api.Open(source);
    auto review = api.User(stage.get());
    const auto binding = api.Binding(stage.get());
    const auto saved = Edited(api, stage.get(), review.get(), output / "malformed.urd");
    const auto expected = source.u8string();
    const auto reject = [&](const uint8_t* bytes, size_t count)
    {
        openusd_edit_buffer* owner = reinterpret_cast<openusd_edit_buffer*>(uintptr_t{1});
        openusd_edit_buffer_view view{reinterpret_cast<const uint8_t*>(uintptr_t{1}), 1};
        Require(openusd_review_document_inspect(bytes, count, &owner, &view, &api.error)
            == OPENUSD_STATUS_NATIVE_ERROR && owner == nullptr && view.data == nullptr && view.size == 0,
            "Inspection must preserve malformed/checksum/depth/unsupported-opinion admission.");
        Require(openusd_review_document_read(bytes, count, expected.data(), expected.size(),
            &owner, &view, &api.error) == OPENUSD_STATUS_NATIVE_ERROR,
            "Malformed document read must fail before producing an envelope.");
        Require(owner == nullptr && view.data == nullptr && view.size == 0 && api.message[0],
            "Malformed read outputs are empty and actionable.");
        int32_t outcome = -1;
        Require(openusd_stage_review_import(stage.get(), bytes, count, binding.data(), binding.size(),
            &outcome, &owner, &view, &api.error) == OPENUSD_STATUS_NATIVE_ERROR
            && outcome != OPENUSD_EDIT_APPLIED && owner == nullptr && view.size == 0,
            "Malformed import cannot return success-shaped state.");
    };
    for (const size_t size : {size_t{0}, size_t{1}, size_t{7}, size_t{12}, size_t{47}, size_t{48}, saved.document.size() - 1})
    {
        reject(saved.document.data(), size);
    }
    reject(nullptr, saved.document.size());
    reject(saved.document.data(), OPENUSD_REVIEW_MAX_DOCUMENT_BYTES + 1ull);
    reject(saved.document.data(), SIZE_MAX);
    for (const size_t offset : {size_t{0}, size_t{4}, size_t{8}, size_t{12}, size_t{16}, saved.document.size() - 1})
    {
        auto damaged = saved.document; damaged[offset] ^= 0x80;
        reject(damaged.data(), damaged.size());
    }
    auto trailing = saved.document; trailing.push_back(0);
    reject(trailing.data(), trailing.size());
#if defined(_WIN32)
    for (bool concrete : {false, true})
    {
        const auto layer = api.Native(review.get());
        VtDictionary clipSet;
        if (concrete)
        {
            Write(source.parent_path() / "clip.usda",
                "#usda 1.0\ndef \"Clip\"\n{\n asset nested = @clip-image.bin@\n}\n");
            Write(source.parent_path() / "clip-image.bin", "untracked clip dependency");
            clipSet["assetPaths"] = VtValue(VtArray<SdfAssetPath>{SdfAssetPath("clip.usda")});
        }
        else { clipSet["templateAssetPath"] = VtValue(std::string("sequence.###.usda")); }
        const VtValue clips(VtDictionary{{"default", VtValue(clipSet)}});
        const SdfPath world("/World");
        layer->SetField(world, TfToken("clipx"), clips);
        auto document = api.Save(review.get(), binding, output / "clip-admission.urd").document;
        Bytes fieldName;
        Text(fieldName, "clipx");
        const auto field = std::search(document.begin() + ReviewOffset(document), document.end(),
            fieldName.begin(), fieldName.end());
        Require(field != document.end(), "Locate the independently forged clip metadata field.");
        *(field + static_cast<ptrdiff_t>(fieldName.size()) - 1) = 's';
        Rechecksum(document);
        reject(document.data(), document.size());
        layer->EraseField(world, TfToken("clipx"));
        layer->SetField(world, TfToken("clips"), clips);
        RejectCapture(api, review.get(), binding, output / "captured-clips.urd");
        Require(layer->GetField(world, TfToken("clips")) == clips,
            "Refused clip capture must retain the unsupported authored opinion.");
        layer->EraseField(world, TfToken("clips"));
    }
    {
        const auto layer = api.Native(review.get());
        const auto relationship = SdfRelationshipSpec::New(
            SdfCreatePrimInLayer(layer, SdfPath("/World")), "depthTargets");
        SdfPathListOp shallow;
        shallow.SetExplicitItems({SdfPath("/DepthSeed")});
        layer->SetField(relationship->GetPath(), SdfFieldKeys->TargetPaths, VtValue(shallow));
        const auto shallowDocument = api.Save(review.get(), binding, output / "path-depth.urd").document;
        for (const size_t depth : {size_t{33}, size_t{1024}})
        {
            std::string deep;
            for (size_t i = 0; i < depth; ++i) { deep += "/x"; }
            auto document = shallowDocument;
            const size_t body = ReviewOffset(document);
            Bytes oldPath, newPath;
            Text(oldPath, "/DepthSeed"); Text(newPath, deep);
            const auto found = std::search(document.begin() + body, document.end(), oldPath.begin(), oldPath.end());
            Require(found != document.end(), "Locate the forged portable path-list value.");
            const auto offset = static_cast<size_t>(found - document.begin());
            document.erase(document.begin() + offset, document.begin() + offset + oldPath.size());
            document.insert(document.begin() + offset, newPath.begin(), newPath.end());
            Set32(document, body - 4, static_cast<uint32_t>(document.size() - body));
            Rechecksum(document);
            reject(document.data(), document.size());
            SdfPathListOp tooDeep;
            tooDeep.SetExplicitItems({SdfPath(deep)});
            layer->SetField(relationship->GetPath(), SdfFieldKeys->TargetPaths, VtValue(tooDeep));
            RejectCapture(api, review.get(), binding, output / "captured-depth.urd");
        }
        layer->SetField(relationship->GetPath(), SdfFieldKeys->TargetPaths, VtValue(shallow));
    }
    {
        Packet sourcePacket{binding, 16};
        sourcePacket.Text();
        const auto fingerprint = sourcePacket.Text();
        const auto bytes = Read(source);
        const auto hash = IndependentHash(reinterpret_cast<const uint8_t*>(bytes.data()), bytes.size());
        std::ostringstream text;
        text << std::hex << std::setfill('0');
        for (const auto byte : hash) { text << std::setw(2) << static_cast<unsigned>(byte); }
        Require(fingerprint == text.str(), "Binding SHA-256 matches independent OS hashing of the actual file.");
        const auto documentHash = IndependentHash(saved.document.data() + 48, saved.document.size() - 48);
        Require(std::equal(documentHash.begin(), documentHash.end(), saved.document.begin() + 16),
            "URD1 checksum matches an independent SHA-256 implementation.");
    }
    for (const uint8_t invalid : {uint8_t{0}, uint8_t{0xc0}, uint8_t{0xff}})
    {
        auto invalidUtf8 = saved.document; invalidUtf8[52] = invalid; Rechecksum(invalidUtf8);
        reject(invalidUtf8.data(), invalidUtf8.size());
    }
    {
        auto oversizedText = saved.document; Set32(oversizedText, 48, 4097); Rechecksum(oversizedText);
        reject(oversizedText.data(), oversizedText.size());
        auto tooManySpecs = saved.document;
        const auto offset = ReviewOffset(tooManySpecs);
        Set32(tooManySpecs, offset + 8, 4097); Rechecksum(tooManySpecs);
        reject(tooManySpecs.data(), tooManySpecs.size());
        Packet first{saved.document, offset + 12};
        first.Text(); first.U32();
        auto tooManyFields = saved.document;
        Set32(tooManyFields, first.offset, 129); Rechecksum(tooManyFields);
        reject(tooManyFields.data(), tooManyFields.size());
        first.U32(); first.Text();
        auto unknownType = saved.document;
        Set32(unknownType, first.offset, UINT32_MAX); Rechecksum(unknownType);
        reject(unknownType.data(), unknownType.size());
    }
#endif
    {
        openusd_edit_buffer* owner = nullptr;
        openusd_edit_buffer_view view{};
        int32_t outcome = -1;
        auto malformedBinding = binding;
        malformedBinding.push_back(0);
        Require(openusd_stage_review_import(stage.get(), saved.document.data(), saved.document.size(),
            malformedBinding.data(), malformedBinding.size(), &outcome, &owner, &view, &api.error)
            == OPENUSD_STATUS_NATIVE_ERROR && owner == nullptr && view.size == 0, "Binding parser rejects trailing bytes.");
        Require(openusd_stage_review_import(stage.get(), saved.document.data(), saved.document.size(),
            binding.data(), OPENUSD_REVIEW_MAX_ENVELOPE_BYTES + 1ull, &outcome, &owner, &view, &api.error)
            == OPENUSD_STATUS_NATIVE_ERROR && owner == nullptr && view.size == 0, "Oversized binding fails before access/materialization.");
        const char invalidUtf8[] = {'x', static_cast<char>(0xc0), static_cast<char>(0xaf), '\0'};
        openusd_stage* invalid = nullptr;
        Require(openusd_stage_open_for_review(invalidUtf8, &invalid, &api.error) == OPENUSD_STATUS_NATIVE_ERROR && invalid == nullptr,
            "Factory path requires strict UTF-8.");
        Require(openusd_review_document_read(saved.document.data(), saved.document.size(), nullptr, 0,
            &owner, &view, &api.error) == OPENUSD_STATUS_NATIVE_ERROR && owner == nullptr,
            "An explicit expected source path is mandatory.");
    }
    {
        const auto path = output / "oversized-source" / "source.usda";
        fs::create_directories(path.parent_path());
        {
            std::ofstream file(path, std::ios::binary);
            file << "#usda 1.0\n"; file.seekp(OPENUSD_REVIEW_MAX_SOURCE_FILE_BYTES); file.put('\n');
        }
        openusd_stage* rejected = nullptr;
        Require(openusd_stage_open_for_review(path.u8string().c_str(), &rejected, &api.error)
            == OPENUSD_STATUS_NATIVE_ERROR && rejected == nullptr
            && std::string(api.message).find("16 MiB") != std::string::npos,
            "Source size admission runs before allocation and SDK parsing.");
    }
    {
        const auto path = Basic(output / "bounded-review");
        auto target = api.Open(path);
        auto layer = api.User(target.get());
        const auto sourceBinding = api.Binding(target.get());
        const auto native = api.Native(layer.get());
        VtDictionary values;
        for (int i = 0; i < 1020; ++i)
        {
            std::ostringstream key; key << std::setw(4) << std::setfill('0') << i;
            values[key.str()] = VtValue(std::string(4096, 'x'));
        }
        native->SetField(SdfPath::AbsoluteRootPath(), SdfFieldKeys->CustomLayerData, VtValue(values));
        const auto large = api.Save(layer.get(), sourceBinding, output / "near-review-limit.urd");
        Require(large.envelopeSize > OPENUSD_LAYER_EDIT_MAX_BYTES
            && large.envelopeSize <= OPENUSD_REVIEW_MAX_ENVELOPE_BYTES,
            "Portable publication has a separate bound and can exceed the old 4 MiB packet cap.");
        auto receiver = api.Open(path);
        api.Import(receiver.get(), large.document, api.Binding(receiver.get()));
        native->SetField(SdfPath::AbsoluteRootPath(), SdfFieldKeys->Documentation, VtValue(std::string("extra")));
        RejectCapture(api, layer.get(), sourceBinding, output / "over-review-limit.urd");
        native->EraseField(SdfPath::AbsoluteRootPath(), SdfFieldKeys->Documentation);
        native->EraseField(SdfPath::AbsoluteRootPath(), SdfFieldKeys->CustomLayerData);
        const auto prim = SdfCreatePrimInLayer(native, SdfPath("/World"));
        const auto array = SdfAttributeSpec::New(prim, "tooMany", SdfValueTypeNames->DoubleArray);
        array->SetDefaultValue(VtValue(VtArray<double>(4097)));
        RejectCapture(api, layer.get(), sourceBinding, output / "over-items.urd");
        array->SetDefaultValue(VtValue(VtArray<double>()));
        for (int i = 0; i < 129; ++i)
        {
            native->SetField(prim->GetPath(), TfToken("field" + std::to_string(i)), VtValue(1.0));
        }
        RejectCapture(api, layer.get(), sourceBinding, output / "over-fields.urd");
    }
    const auto native = api.Native(review.get());
    const auto image = SdfAttributeSpec::New(SdfCreatePrimInLayer(native, SdfPath("/World")), "unsupportedAsset", SdfValueTypeNames->Asset);
    for (const std::string raw : {"textures/<UDIM>.exr", "textures/${name}.exr", "https://example.invalid/image.exr", "package.usdz[image.exr]"})
    {
        image->SetDefaultValue(VtValue(SdfAssetPath(raw)));
        RejectCapture(api, review.get(), binding, output / "unsupported.urd");
    }
    Require(api.Number(stage.get(), "/World", "weight") == 47, "Rejected inputs never disturb unrelated review opinions.");
    std::cout << "PASS malformed/bounds: checksums, strict UTF-8/NUL, versions, truncated/oversized/trailing bytes,"
        " spec/field/items/4MiB review limits, separately bounded large envelopes, source cap, unsupported asset domains\n";
}
}
