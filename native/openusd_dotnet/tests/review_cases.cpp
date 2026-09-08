// Copyright (c) marcschier. Licensed under the MIT License.
#include "review_probe_support.h"

#include <iostream>

PXR_NAMESPACE_USING_DIRECTIVE
namespace ReviewProbe
{
void FilesystemDomain(const fs::path& output)
{
    const auto directory = output / "filesystem-domain";
    const auto originals = Composition(directory);
    const auto source = directory / "root.usda";
    ReviewApi api;
    auto stage = api.Open(source);
    const auto binding = api.Binding(stage.get());
    Packet b{binding};
    Require(b.U32() == 0x31425352 && b.U32() == 1 && b.U64() != 0, "Verified source binding v1.");
    const auto rootPath = b.Text();
    b.Text();
    const auto anchor = b.Text();
    Require(b.U32() == originals.size(), "Manifest includes source, sublayer, reference, payload, selected/inactive variants, and asset.");
    auto review = api.User(stage.get());
    api.Apply(review.get(), api.Capture(review.get(), {{"/World.weight"}}), {SetDouble({"/World.weight"}, 47)});
    auto native = api.Native(review.get());
    const auto prim = SdfCreatePrimInLayer(native, SdfPath("/World"));
    const auto image = SdfAttributeSpec::New(prim, "reviewImage", SdfValueTypeNames->Asset);
    image->SetDefaultValue(VtValue(SdfAssetPath("./textures/picture.bin")));
    const auto array = SdfAttributeSpec::New(prim, "images", SdfValueTypeNames->AssetArray);
    array->SetDefaultValue(VtValue(VtArray<SdfAssetPath>{
        SdfAssetPath("textures/picture.bin"), SdfAssetPath((directory / "textures" / "picture.bin").generic_u8string())}));
    prim->GetReferenceList().Prepend(SdfReference("layers/ref.usda", SdfPath("/Model"), SdfLayerOffset(-0.0, 1)));
    prim->GetPayloadList().Prepend(SdfPayload("layers/payload.usda", SdfPath("/Payload")));
    native->InsertSubLayerPath("layers/sub.usda");
    const auto variants = SdfVariantSetSpec::New(prim, "reviewLook");
    const auto variant = SdfVariantSpec::New(variants, "high-res");
    SdfAttributeSpec::New(variant->GetPrimSpec(), "variantNumber", SdfValueTypeNames->Double)
        ->SetDefaultValue(VtValue(23.0));
    prim->SetVariantSelection("reviewLook", "high-res");
    double start = 0, end = 0, rate = 0;
    api.Ok(openusd_stage_get_start_time_code(stage.get(), &start, &api.error));
    api.Ok(openusd_stage_get_end_time_code(stage.get(), &end, &api.error));
    api.Ok(openusd_stage_get_time_codes_per_second(stage.get(), &rate, &api.error));
    Require(start == 7 && end == 77 && rate == 60, "Review must not wrap or lose actual root metadata.");
    Require(api.Number(stage.get(), "/World", "weight") == 47
        && api.Number(stage.get(), "/World", "ref") == 11
        && api.Number(stage.get(), "/World", "payload") == 13
        && api.Number(stage.get(), "/World/Choice", "selected") == 17
        && api.Number(stage.get(), "/Sub", "sub") == 5, "Source composition remains intact under review.");
    const auto saved = api.Save(review.get(), binding, output / "elsewhere" / "review.urd");
    Require(saved.source == rootPath && saved.anchor == anchor && saved.id.size() == 36, "Capture keeps explicit root and original anchor lineage.");
    const auto rawAssets = native->GetField(SdfPath("/World.images"), SdfFieldKeys->Default);
    const auto rawReferences = native->GetField(SdfPath("/World"), SdfFieldKeys->References);
    review.reset(); stage.reset(); native = {};
    auto reopened = api.Open(source);
    api.Import(reopened.get(), saved.document, api.Binding(reopened.get()));
    auto loaded = api.User(reopened.get());
    const auto imported = api.Native(loaded.get());
    Require(imported->GetField(SdfPath("/World.images"), SdfFieldKeys->Default) == rawAssets
        && imported->GetField(SdfPath("/World"), SdfFieldKeys->References) == rawReferences,
        "Portable arrays and composition list operations preserve authored data.");
    Require(imported->HasSpec(SdfPath("/World{reviewLook=high-res}.variantNumber")),
        "Portable review preserves variant specs and their declaration.");
    const auto actual = ArGetResolver().Resolve(SdfComputeAssetPathRelativeToLayer(imported, "./textures/picture.bin"));
    Require(actual && fs::equivalent(fs::u8path(actual.GetPathString()), directory / "textures" / "picture.bin"),
        "Read/import with SaveAs elsewhere preserves dot-relative real-anchor resolution.");
    Require(api.Number(reopened.get(), "/World", "weight") == 47
        && api.Number(reopened.get(), "/World/Choice", "selected") == 17, "Imported review composes over the unchanged actual source.");
    for (const auto& file : originals) { Require(Read(file.first) == file.second, "Source/dependency bytes remain unchanged."); }
    std::cout << "PASS filesystem source/review: relative and absolute assets, root metadata, references,"
        " payloads, sublayers, variants, SaveAs-independent real anchor\n";
}

void AdditionalDependencies(const fs::path& output)
{
    const auto directory = output / "added-composition";
    const auto source = Basic(directory);
    const auto dependency = directory / "new" / "model.usda";
    const auto asset = directory / "new" / "image.bin";
    Write(dependency, "#usda 1.0\ndef \"Model\"\n{\n double extra = 71\n asset image = @./image.bin@\n}\n");
    Write(asset, "verified dependency asset");
    ReviewApi api;
    auto stage = api.Open(source);
    const auto binding = api.Binding(stage.get());
    // The same public factory admits a new dependency before an authoring API
    // can cause USD to load that dependency through an unverified ordinary open.
    auto dependencyLease = api.Open(dependency);
    auto review = api.User(stage.get());
    auto prim = SdfCreatePrimInLayer(api.Native(review.get()), SdfPath("/World"));
    prim->GetReferenceList().Prepend(SdfReference("new/model.usda", SdfPath("/Model")));
    const auto saved = Edited(api, stage.get(), review.get(), directory / "new-review.urd");
    Require(api.Number(stage.get(), "/World", "extra") == 71, "New pre-admitted review references compose normally.");
    const auto original = Read(asset);
    auto changed = original; changed.back() = '!';
    Write(asset, changed);
    RejectCapture(api, review.get(), binding, directory / "changed.urd");
    int32_t acknowledged = -1;
    Require(openusd_layer_review_acknowledge_saved(review.get(), saved.receipt.data(), saved.receipt.size(),
        &acknowledged, &api.error) == OPENUSD_STATUS_NATIVE_ERROR && acknowledged == 0,
        "New composition dependencies keep their original admitted asset-byte provenance through acknowledge.");
    Write(asset, original);
    review.reset(); stage.reset(); dependencyLease.reset(); prim = {};
    auto target = api.Open(source);
    api.Import(target.get(), saved.document, api.Binding(target.get()));
    Require(api.Number(target.get(), "/World", "extra") == 71 && api.Number(target.get(), "/World", "weight") == 47,
        "Portable import admits newly referenced files from saved byte images before attaching the review.");

    const auto unverified = directory / "unverified.usda";
    Write(unverified, "#usda 1.0\ndef \"Model\"\n{\n double unverified = 83\n}\n");
    auto live = api.User(target.get());
    SdfCreatePrimInLayer(api.Native(live.get()), SdfPath("/World"))->GetReferenceList()
        .Append(SdfReference("unverified.usda", SdfPath("/Model")));
    RejectCapture(api, live.get(), api.Binding(target.get()), directory / "unverified.urd");
    Require(std::string(api.message).find("Unverified cached") != std::string::npos,
        "New dependencies loaded by ordinary USD cannot acquire retrospective proof.");
    std::cout << "PASS new composition dependencies: pre-edit factory admission, original nested asset bytes,"
        " portable import, and explicit unverified-cache reconciliation\n";
}

void ResolverAnchors(const fs::path& output)
{
    const auto directory = output / "resolver-anchors";
    const auto original = directory / "original" / "root.usda";
    const auto alias = directory / "alias" / "root.usda";
    Write(original, "#usda 1.0\ndef \"World\"\n{\n asset image = @./image.bin@\n}\n");
    Write(original.parent_path() / "image.bin", "original anchor");
    Write(alias.parent_path() / "image.bin", "alias anchor");
    std::error_code error;
    fs::create_symlink(original, alias, error);
    if (error && !fs::is_symlink(alias))
    {
        std::cout << "SKIP symbolic-link anchor: host does not permit creating a fixture symlink\n";
        return;
    }
    std::string identifier;
    std::string resolvedAsset;
    {
        const auto normal = SdfLayer::FindOrOpen(alias.u8string());
        Require(static_cast<bool>(normal), "Ordinary OpenUSD opens the filesystem alias fixture.");
        identifier = normal->GetIdentifier();
        resolvedAsset = ArGetResolver().Resolve(SdfComputeAssetPathRelativeToLayer(normal, "./image.bin")).GetPathString();
    }
    ReviewApi api;
    openusd_stage* handle = nullptr;
    const auto status = openusd_stage_open_for_review(alias.u8string().c_str(), &handle, &api.error);
    if (status != OPENUSD_STATUS_OK)
    {
        Require(handle == nullptr && std::string(api.message).find("alias") != std::string::npos,
            "Unsupported alias anchoring is an explicit reconciliation, never a silent reanchor.");
        std::cout << "PASS resolver anchor: aliases requiring a different SDK anchor are explicitly refused\n";
        return;
    }
    Stage stage(handle, openusd_stage_release);
    auto root = api.Root(stage.get());
    const auto native = api.Native(root.get());
    Require(native->GetIdentifier() == identifier
        && ArGetResolver().Resolve(SdfComputeAssetPathRelativeToLayer(native, "./image.bin")).GetPathString() == resolvedAsset,
        "Verified byte admission preserves the SDK's original realpath anchor, not a dereferenced filesystem alias anchor.");
    auto review = api.User(stage.get());
    const auto saved = api.Save(review.get(), api.Binding(stage.get()), directory / "elsewhere.urd");
    Require(fs::equivalent(fs::u8path(saved.source), alias), "The explicit source remains the actual admitted file.");
    std::cout << "PASS resolver anchor: filesystem aliases preserve ordinary SDK identifier and asset anchoring semantics\n";
}
}
