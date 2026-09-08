// Copyright (c) marcschier. Licensed under the MIT License.
#include "layer_edit_probe_support.h"
#include "layer_edit_cases.h"
#include "pxr/base/plug/registry.h"
#include "pxr/usd/sdf/attributeSpec.h"
#include "pxr/usd/sdf/primSpec.h"
#include "pxr/usd/usd/stage.h"

#include <iostream>

PXR_NAMESPACE_USING_DIRECTIVE
using namespace EditProbe;

int main(int argc, char** argv)
{
    try
    {
        Require(argc == 3, "Expected plugin directory and owned output directory.");
        PlugRegistry::GetInstance().RegisterPlugins(argv[1]);
        const std::string file = std::string(argv[2]) + "/layer-edit-root.usda";
        const auto root = SdfLayer::CreateNew(file);
        Require(root && root->ImportFromString(
            "#usda 1.0\n def \"World\"\n{\n double weight = 42\n}\n"),
            "Create weaker root opinion.");
        Require(root->Save(), "Save root fixture.");
        Api api;
        openusd_stage* stage = nullptr;
        api.Ok(openusd_stage_open(file.c_str(), &stage, &api.error));
        openusd_layer* review = nullptr;
        api.Ok(openusd_stage_edit_get_user_layer(stage, &review, &api.error));
        Require(review != nullptr, "An owned review layer must be returned.");
        const auto native = api.Native(review);
        Require((ReadU32(api.State(review), 48) & 2u) == 0,
            "A newly created empty review layer must start at a logical saved baseline.");
        char originalTarget[4097]{};
        size_t targetRequired = 0;
        api.Ok(openusd_stage_get_edit_target_layer_identifier(stage, originalTarget,
            sizeof(originalTarget), &targetRequired, &api.error));
        const Address weight{"/World.weight"};
        const auto captured = api.Capture(review, {weight});
        const size_t opinion = 48 + 4 + weight.path.size() + 12;
        Require(ReadU32(captured, opinion) == 0,
            "Weaker root property must NOT count as a review property spec.");
        Require(ReadU32(captured, opinion + 16) == 0,
            "Absent target default must NOT capture the weaker root value.");
        auto after = api.Apply(review, captured, {SetDouble(weight, 47)});
        Require(ReadU32(after, opinion) == 1, "Expected authored attribute spec.");
        Require(native->GetField(SdfPath(weight.path), SdfFieldKeys->Default) == VtValue(47.0),
            "Typed default must be authored on the review layer.");
        Require(root->GetField(SdfPath(weight.path), SdfFieldKeys->Default) == VtValue(42.0),
            "Review editing must never modify the weaker root.");
        const auto undone = api.Restore(review, after, captured);
        Require(!native->HasSpec(SdfPath("/World")),
            "Undo must remove its owned empty ancestor and property.");
        after = api.Restore(review, undone, after);
        Require(native->HasSpec(SdfPath(weight.path)), "Redo must recreate exact authored opinion.");

        SdfAttributeSpec::New(SdfCreatePrimInLayer(native, SdfPath("/World")), "other",
            SdfValueTypeNames->Double)->SetDefaultValue(VtValue(71.0));
        after = api.Apply(review, after, {SetDouble(weight, 48)});
        Require(native->GetField(SdfPath("/World.other"), SdfFieldKeys->Default) == VtValue(71.0),
            "Disjoint external edits must survive compare/apply.");
        native->SetField(SdfPath(weight.path), SdfFieldKeys->Default, VtValue(99.0));
        api.Apply(review, after, {SetDouble(weight, 49)}, OPENUSD_EDIT_CONFLICT);
        after = api.Capture(review, {weight});
        native->SetField(SdfPath(weight.path), SdfFieldKeys->Documentation, VtValue(std::string("foreign")));
        api.Restore(review, after, captured, OPENUSD_EDIT_CONFLICT);
        Require(native->GetField(SdfPath(weight.path), SdfFieldKeys->Documentation)
            == VtValue(std::string("foreign")), "Undo must refuse destructive foreign-field deletion.");

        const Address one{"/Owned/Child.one"};
        const Address two{"/Owned/Child.two"};
        const auto beforePair = api.Capture(review, {one, two});
#if defined(OPENUSD_DOTNET_ENABLE_TEST_HOOKS)
        const auto mutations = MutationPacket({SetDouble(one, 1), SetDouble(two, 2)});
        openusd_layer_edit_test_fail_after(1);
        openusd_edit_buffer* owner = nullptr;
        openusd_edit_buffer_view view{};
        int32_t outcome = -1;
        Require(openusd_layer_edit_apply(review, beforePair.data(), beforePair.size(),
            mutations.data(), mutations.size(), &outcome, &owner, &view, &api.error)
            == OPENUSD_STATUS_NATIVE_ERROR, "Injected multi-property failure must report failure.");
        Require(owner == nullptr && view.data == nullptr && view.size == 0,
            "A rolled back failure cannot return an applied snapshot.");
        Require(!native->HasSpec(SdfPath("/Owned")), "Rollback must remove only newly owned specs.");
        Require(native->HasSpec(SdfPath("/World.other")), "Rollback must preserve disjoint opinions.");
#endif
        const auto afterPair = api.Apply(review, beforePair, {SetDouble(one, 1), SetDouble(two, 2)});
        Require(!afterPair.empty(), "Expected successful multi-property transaction after failure reset.");
        SdfCreatePrimInLayer(native, SdfPath("/Owned/Foreign"));
        native->SetField(SdfPath("/Owned"), SdfFieldKeys->Documentation,
            VtValue(std::string("keep ancestor metadata")));
        const auto pairUndone = api.Restore(review, afterPair, beforePair);
        Require(!native->HasSpec(SdfPath("/Owned/Child"))
            && native->HasSpec(SdfPath("/Owned/Foreign"))
            && native->GetField(SdfPath("/Owned"), SdfFieldKeys->Documentation)
                == VtValue(std::string("keep ancestor metadata")),
            "Owned ancestor cleanup must preserve external children and metadata.");
        api.Restore(review, pairUndone, afterPair);
        char currentTarget[4097]{};
        api.Ok(openusd_stage_get_edit_target_layer_identifier(stage, currentTarget,
            sizeof(currentTarget), &targetRequired, &api.error));
        Require(std::string(currentTarget) == originalTarget,
            "Success, conflict and rollback must preserve the exact prior edit target.");
        const auto state = api.State(review);
        Require(ReadU32(state, 44) == 2, "Review role must use owned identity, not a human label.");
        Require((ReadU32(state, 48) & 3u) == 3u, "Edited anonymous review layer must be logically dirty.");
        const auto checkpoint = api.Checkpoint(review);
        Require(ReadU32(checkpoint, 8) == 4, "Expected bounded review-only checkpoint.");
        RunAuthoredKinds(api, review);
        RunSampleBatchBounds(api, review);
        RunUnsupportedAddresses(api, review);
        RunPersistence(api, review, argv[2]);
        RunBitExactCheckpoint(api, stage, review);
        RunRollbackInventories(api, review);
        RunOverlayContinuity(api, argv[2]);
        RunOverlayLifecycles(api, argv[2]);
        RunOverlayRefusals(api, argv[2]);
        RunOverlayRollback(api, argv[2]);
        RunOverlayAdmission(api, argv[2]);
        RunIdentityAndBounds(api, stage, review, file, argv[2]);

        openusd_stage_release(stage);
        Require(ReadU32(api.State(review), 44) == 2, "Owned layer handle must retain its stage context.");
        openusd_layer_release(review);
        std::cout << "PASS layer-edit: exact authored values/list ops, CAS/undo/rollback, ownership/roles,"
            " identities/reload/detach, budgets, checkpoint/export/reopen/save acknowledgement\n";
        return 0;
    }
    catch (const std::exception& error)
    {
        std::cerr << "FAIL layer-edit: " << error.what() << '\n';
        return 1;
    }
}
