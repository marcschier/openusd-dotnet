// Copyright (c) marcschier. Licensed under the MIT License.
#include "review_probe_support.h"
#include "pxr/usd/sdf/changeBlock.h"

#include <iostream>

#if defined(OPENUSD_DOTNET_ENABLE_TEST_HOOKS)
extern "C" OPENUSD_DOTNET_API void openusd_review_test_fail_after(int32_t phase);
extern "C" OPENUSD_DOTNET_API void openusd_review_test_mutate_published_source();
#endif

PXR_NAMESPACE_USING_DIRECTIVE
namespace ReviewProbe
{
void SourceFreshness(const fs::path& output)
{
    ReviewApi api;
#if defined(OPENUSD_DOTNET_ENABLE_TEST_HOOKS)
    {
        const auto path = Basic(output / "published-source-race");
        const auto original = Read(path);
        openusd_review_test_mutate_published_source();
        openusd_stage* rejected = nullptr;
        Require(openusd_stage_open_for_review(path.u8string().c_str(), &rejected, &api.error)
            == OPENUSD_STATUS_NATIVE_ERROR && rejected == nullptr && Read(path) == original,
            "Origin proof is recorded before registry publication, never after a competing resident mutation.");
    }
    {
        const auto directory = output / "composition-budget";
        for (int i = 0; i < 24; ++i)
        {
            std::string text = "#usda 1.0\ndef \"Model\"\n{\n";
            if (i != 23)
            {
                const auto next = "part" + std::to_string(i + 1) + ".usda";
                text += " def \"Left\" (references = @" + next + "@</Model>) {}\n";
                text += " def \"Right\" (references = @" + next + "@</Model>) {}\n";
            }
            text += "}\n";
            Write(directory / ("part" + std::to_string(i) + ".usda"), text);
        }
        // The sentinel stops before UsdStage::Open if bounded admission regresses,
        // so this negative test can never materialize its exponential scene.
        openusd_review_test_fail_after(4);
        openusd_stage* rejected = nullptr;
        const auto status = openusd_stage_open_for_review((directory / "part0.usda").u8string().c_str(), &rejected, &api.error);
        openusd_review_test_fail_after(-1);
        Require(status == OPENUSD_STATUS_NATIVE_ERROR && rejected == nullptr
            && std::string(api.message).find("composition budget") != std::string::npos,
            "Admit compact exponential reference graphs before SDK scene materialization.");
    }
#endif
    {
        const auto path = Basic(output / "unverified-cache");
        const auto original = Read(path);
        const auto cached = SdfLayer::FindOrOpen(path.u8string());
        Require(cached && !cached->IsDirty(), "Fixture has a clean but unverified resident source.");
        openusd_stage* unexpected = reinterpret_cast<openusd_stage*>(uintptr_t{1});
        Require(openusd_stage_open_for_review(path.u8string().c_str(), &unexpected, &api.error)
            == OPENUSD_STATUS_NATIVE_ERROR && unexpected == nullptr
            && std::string(api.message).find("Unverified cached") != std::string::npos,
            "Even matching clean cached bytes cannot acquire retrospective origin proof.");
        openusd_stage* raw = nullptr;
        api.Ok(openusd_stage_open(path.u8string().c_str(), &raw, &api.error));
        Stage legacy(raw, openusd_stage_release);
        openusd_edit_buffer* owner = nullptr;
        openusd_edit_buffer_view view{};
        Require(openusd_stage_review_source_binding(legacy.get(), &owner, &view, &api.error)
            == OPENUSD_STATUS_NATIVE_ERROR && owner == nullptr && view.size == 0,
            "A legacy stage cannot be retroactively bound.");
        const auto time = fs::last_write_time(path);
        auto changed = original;
        changed[changed.find("42")] = '9';
        Write(path, changed); fs::last_write_time(path, time);
        Require(openusd_stage_open_for_review(path.u8string().c_str(), &unexpected, &api.error)
            == OPENUSD_STATUS_NATIVE_ERROR && unexpected == nullptr,
            "Unverified stale cache is not repaired using size/mtime/current hash.");
        Require(api.Number(legacy.get(), "/World", "weight") == 42 && !cached->IsDirty()
            && Read(path) == changed, "Failure leaves shared cached source and externally changed bytes untouched.");
    }
    {
        const auto path = Basic(output / "resident-edit");
        const auto original = Read(path);
        auto stage = api.Open(path);
        auto review = api.User(stage.get());
        const auto binding = api.Binding(stage.get());
        auto root = api.Root(stage.get());
        const auto native = api.Native(root.get());
        native->SetField(SdfPath("/World.weight"), SdfFieldKeys->Default, VtValue(99.0));
        native->SetField(SdfPath("/World.weight"), SdfFieldKeys->Default, VtValue(42.0));
        RejectCapture(api, review.get(), binding, output / "resident-edit.urd");
        Require(Read(path) == original && api.Number(stage.get(), "/World", "weight") == 42,
            "Resident dirty/reverted origins are refused without mutating shared source.");
    }
    const std::vector<fs::path> changedNames{
        "root.usda", fs::path("layers") / "sub.usda", fs::path("layers") / "ref.usda",
        fs::path("layers") / "payload.usda", fs::path("layers") / "variant.usd",
        fs::path("layers") / "inactive.usda", fs::path("textures") / "picture.bin"};
    for (size_t i = 0; i < changedNames.size(); ++i)
    {
        const auto directory = output / ("changed-byte-image-" + std::to_string(i));
        const auto originals = Composition(directory);
        auto stage = api.Open(directory / "root.usda");
        auto review = api.User(stage.get());
        const auto binding = api.Binding(stage.get());
        const auto saved = Edited(api, stage.get(), review.get(), directory / "review.urd");
        auto target = api.Open(directory / "root.usda");
        const auto targetBinding = api.Binding(target.get());
        const auto changedPath = directory / changedNames[i];
        const auto time = fs::last_write_time(changedPath);
        auto bytes = Read(changedPath);
        bytes.back() = bytes.back() == ' ' ? '\n' : ' ';
        Write(changedPath, bytes); fs::last_write_time(changedPath, time);
        Require(Read(changedPath).size() == originals.at(changedPath).size(), "Freshness test preserves length and mtime.");
        RejectCapture(api, review.get(), binding, directory / "other.urd");
        api.Import(target.get(), saved.document, targetBinding, OPENUSD_EDIT_CONFLICT);
        int32_t acknowledged = -1;
        Require(openusd_layer_review_acknowledge_saved(review.get(), saved.receipt.data(), saved.receipt.size(),
            &acknowledged, &api.error) == OPENUSD_STATUS_NATIVE_ERROR && acknowledged == 0,
            "Save acknowledgement revalidates every source/dependency byte image.");
        Require((ReadU32(api.State(review.get()), 48) & 2u) != 0,
            "Changed source cannot silently mark current review saved.");
        Require(api.Number(stage.get(), "/World", "weight") == 47 && Read(changedPath) == bytes,
            "Conflict never reloads shared source or modifies changed filesystem bytes.");
    }
    {
        const auto source = Basic(output / "ordinary-crate");
        const auto crate = source.parent_path() / "source.usdc";
        {
            const auto layer = SdfLayer::FindOrOpen(source.u8string());
            Require(layer->Export(crate.u8string()), "Create owned native crate fixture.");
        }
        openusd_stage* raw = nullptr;
        api.Ok(openusd_stage_open(crate.u8string().c_str(), &raw, &api.error));
        Stage normal(raw, openusd_stage_release);
        Require(api.Number(normal.get(), "/World", "weight") == 42, "Ordinary crate Open remains unchanged.");
        Require(openusd_stage_open_for_review(crate.u8string().c_str(), &raw, &api.error)
            == OPENUSD_STATUS_NATIVE_ERROR && raw == nullptr, "Portable factory explicitly refuses crate.");
    }
    std::cout << "PASS source proof: unknown clean/stale cache, legacy binding, resident edits/reverts,"
        " root/all dependencies changed with same length+mtime, capture/import/ack, ordinary crate Open\n";
}

void ImportIsolation(const fs::path& output)
{
    ReviewApi api;
    const auto source = Basic(output / "isolation");
    auto author = api.Open(source);
    auto review = api.User(author.get());
    const auto saved = Edited(api, author.get(), review.get(), output / "isolation.urd");
    {
        auto physicsFirst = api.Open(source);
        openusd_layer* p = nullptr;
        openusd_layer* u = nullptr;
        api.Ok(openusd_stage_session_overlay_normalize(physicsFirst.get(), &p, &u, &api.error));
        Layer physics(p, openusd_layer_release), user(u, openusd_layer_release);
        const auto physicsFirstReview = Edited(api, physicsFirst.get(), u, output / "physics-first.urd");
        auto target = api.Open(source);
        api.Import(target.get(), physicsFirstReview.document, api.Binding(target.get()));
        Require(api.Number(target.get(), "/World", "weight") == 47,
            "OpenForReview supplies an anchored review even when physics initializes before editing.");
    }
    for (int scenario = 0; scenario < 6; ++scenario)
    {
        auto target = api.Open(source);
        const auto binding = api.Binding(target.get());
        auto session = api.Session(target.get());
        const auto nativeSession = api.Native(session.get());
        Layer user(nullptr, openusd_layer_release), physics(nullptr, openusd_layer_release);
        SdfLayerRefPtr foreign;
        if (scenario == 0)
        {
            nativeSession->SetField(SdfPath::AbsoluteRootPath(), SdfFieldKeys->Documentation, VtValue(std::string("foreign metadata")));
            user = api.User(target.get());
        }
        else if (scenario == 1)
        {
            SdfCreatePrimInLayer(nativeSession, SdfPath("/Foreign"));
            user = api.User(target.get());
        }
        else if (scenario == 2)
        {
            foreign = SdfLayer::CreateAnonymous("foreign.usda");
            nativeSession->InsertSubLayerPath(foreign->GetIdentifier());
            user = api.User(target.get());
        }
        else if (scenario == 3)
        {
            user = api.User(target.get());
            api.Apply(user.get(), api.Capture(user.get(), {{"/World.weight"}}), {SetDouble({"/World.weight"}, 99)});
        }
        else if (scenario == 4)
        {
            user = api.User(target.get());
            openusd_layer* p = nullptr;
            openusd_layer* u = nullptr;
            api.Ok(openusd_stage_session_overlay_normalize(target.get(), &p, &u, &api.error));
            physics.reset(p); openusd_layer_release(u);
            SdfAttributeSpec::New(SdfCreatePrimInLayer(api.Native(p), SdfPath("/PhysicsOnly")), "weight", SdfValueTypeNames->Double)
                ->SetDefaultValue(VtValue(500.0));
        }
        else { nativeSession->SetPermissionToEdit(false); }
        std::string before;
        nativeSession->ExportToString(&before);
        api.Import(target.get(), saved.document, binding, scenario == 5 ? OPENUSD_EDIT_NOT_EDITABLE : OPENUSD_EDIT_CONFLICT);
        std::string after;
        nativeSession->ExportToString(&after);
        Require(before == after, "Import refusals cannot clobber ambient session opinions or topology.");
        if (scenario == 3) { Require(api.Number(target.get(), "/World", "weight") == 99, "Foreign review edits survive refusal."); }
        if (scenario == 4) { Require(api.Native(physics.get())->HasSpec(SdfPath("/PhysicsOnly.weight")), "Physics survives import refusal."); }
    }
    {
        auto target = api.Open(source);
        const auto binding = api.Binding(target.get());
        api.Import(target.get(), saved.document, api.Binding(author.get()), OPENUSD_EDIT_STALE_TARGET);
        api.Import(target.get(), saved.document, binding);
        auto loaded = api.User(target.get());
        int32_t acknowledged = -1;
        api.Ok(openusd_layer_review_acknowledge_saved(loaded.get(), saved.receipt.data(), saved.receipt.size(),
            &acknowledged, &api.error));
        Require(acknowledged == 0, "Captured receipt cannot mark an imported target saved.");
        api.Import(target.get(), saved.document, binding, OPENUSD_EDIT_CONFLICT);
        Require(api.Number(target.get(), "/World", "weight") == 47, "Non-pristine repeat import preserves current review.");
    }
    {
        const auto other = Basic(output / "foreign-document");
        auto target = api.Open(other);
        api.Import(target.get(), saved.document, api.Binding(target.get()), OPENUSD_EDIT_CONFLICT);
        openusd_edit_buffer* owner = nullptr;
        openusd_edit_buffer_view view{};
        const auto expected = other.u8string();
        Require(openusd_review_document_read(saved.document.data(), saved.document.size(),
            expected.data(), expected.size(), &owner, &view, &api.error) == OPENUSD_STATUS_NATIVE_ERROR
            && owner == nullptr && view.size == 0, "Reader requires explicit matching source provenance.");
    }
    {
        openusd_layer* p = nullptr;
        openusd_layer* u = nullptr;
        api.Ok(openusd_stage_session_overlay_normalize(author.get(), &p, &u, &api.error));
        Layer physics(p, openusd_layer_release); openusd_layer_release(u);
        SdfAttributeSpec::New(SdfCreatePrimInLayer(api.Native(p), SdfPath("/PhysicsOnly")), "weight", SdfValueTypeNames->Double)
            ->SetDefaultValue(VtValue(500.0));
        const auto reviewOnly = api.Save(review.get(), api.Binding(author.get()), output / "without-physics.urd");
        auto target = api.Open(source);
        api.Import(target.get(), reviewOnly.document, api.Binding(target.get()));
        auto loaded = api.User(target.get());
        Require(!api.Native(loaded.get())->HasSpec(SdfPath("/PhysicsOnly"))
            && api.Number(target.get(), "/World", "weight") == 47, "Portable capture/import excludes physics entirely.");
    }
    std::cout << "PASS import isolation: direct session/foreign sublayers/dirty review/physics refusal,"
        " permissions, foreign source/binding, pristine requirement, non-reconstructible receipts\n";
}

void ImportRollback(const fs::path& output)
{
#if defined(OPENUSD_DOTNET_ENABLE_TEST_HOOKS)
    ReviewApi api;
    const auto source = Basic(output / "rollback");
    const auto original = Read(source);
    auto author = api.Open(source);
    auto review = api.User(author.get());
    const auto saved = Edited(api, author.get(), review.get(), output / "rollback.urd");
    for (int phase = 1; phase <= 3; ++phase)
    {
        auto target = api.Open(source);
        const auto binding = api.Binding(target.get());
        auto empty = api.User(target.get());
        api.Ok(openusd_stage_set_edit_target_layer(target.get(), empty.get(), &api.error));
        auto session = api.Session(target.get());
        const auto beforeState = api.State(empty.get());
        std::string before;
        api.Native(session.get())->ExportToString(&before);
        openusd_edit_buffer* owner = nullptr;
        openusd_edit_buffer_view view{};
        int32_t outcome = -1;
        openusd_review_test_fail_after(phase);
        Require(openusd_stage_review_import(target.get(), saved.document.data(), saved.document.size(),
            binding.data(), binding.size(), &outcome, &owner, &view, &api.error) == OPENUSD_STATUS_NATIVE_ERROR,
            "Injected import failure reports an actionable native error.");
        Require(outcome != OPENUSD_EDIT_APPLIED && owner == nullptr && view.data == nullptr && view.size == 0,
            "Injected rollback never returns success-shaped state.");
        std::string after;
        api.Native(session.get())->ExportToString(&after);
        Require(before == after && beforeState == api.State(empty.get()), "Rollback restores exact prior topology and history target.");
        char id[4097]{};
        size_t required = 0;
        api.Ok(openusd_stage_get_edit_target_layer_identifier(target.get(), id, sizeof(id), &required, &api.error));
        Require(api.Native(empty.get())->GetIdentifier() == id, "Rollback preserves prior edit target.");
        api.Import(target.get(), saved.document, binding);
        Require(api.Number(target.get(), "/World", "weight") == 47 && Read(source) == original,
            "Retry after injected rollback succeeds with unchanged source.");
    }
    std::cout << "PASS injected portable import rollback: attachment, registration, pre-publication; exact prior identity/topology\n";
#else
    (void)output;
#endif
}
}
