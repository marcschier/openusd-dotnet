// Copyright (c) marcschier. Licensed under the MIT License.

using System.Text;
using OpenUsd.Editing;
using OpenUsd.Interop;

namespace OpenUsd.Tests;

public sealed partial class UsdLayerEditingNativeTests
{
    [Test]
    public async Task WeakerRootIsNotReviewBeforeStateAndUndoRedoReturnActualAuthoredState()
    {
        using NativeDocument document = await NativeDocument.OpenAsync();
        UsdStage stage = document.Stage;
        using UsdLayer root = stage.GetRootLayer();
        using UsdLayer review = stage.GetUserReviewLayer();
        var address = Default("/World.weight");
        string editTarget = stage.EditTargetLayerIdentifier;
        UsdLayerAuthoredSnapshot before = review.CaptureAuthored([address]);
        await Assert.That(root.CaptureAuthored([address]).Opinions[0].Value.AsDouble()).IsEqualTo(7d);
        await Assert.That(before.Opinions[0].PropertyKind).IsEqualTo(UsdLayerPropertyKind.Absent);
        await Assert.That(before.Opinions[0].Value.Kind).IsEqualTo(UsdLayerEditValueKind.Absent);
        await Assert.That(before.Opinions[0].TypeName).IsNull();
        await Assert.That(review.GetEditingState().Role).IsEqualTo(UsdLayerRole.UserReview);
        await Assert.That(review.GetEditingState().CanAttemptAuthoredEdits).IsTrue();
        UsdLayerEditResult applied = review.CompareAndApply(before,
            [UsdLayerEdit.Set(address, UsdLayerEditValue.FromDouble(47), "double")]);
        await Assert.That(applied.Outcome).IsEqualTo(UsdLayerEditOutcome.Applied);
        await Assert.That(applied.AfterSnapshot!.Opinions[0].Value.AsDouble()).IsEqualTo(47d);
        await Assert.That(applied.AfterSnapshot.Opinions[0].TypeName).IsEqualTo("double");
        await Assert.That(applied.AfterSnapshot.Opinions[0].Variability).IsEqualTo(UsdLayerEditVariability.Varying);
        await Assert.That(applied.AfterSnapshot.Opinions[0].Custom).IsFalse();
        await Assert.That(stage.GetPrim("/World").GetDouble("weight")).IsEqualTo(47d);
        await Assert.That(stage.EditTargetLayerIdentifier).IsEqualTo(editTarget);

        UsdLayerEditResult undone = review.CompareAndRestore(applied.AfterSnapshot, before);
        await Assert.That(undone.Outcome).IsEqualTo(UsdLayerEditOutcome.Applied);
        await Assert.That(undone.AfterSnapshot!.Opinions[0].PropertyKind).IsEqualTo(UsdLayerPropertyKind.Absent);
        await Assert.That(undone.AfterSnapshot.CopyBytes().SequenceEqual(review.CaptureAuthored([address]).CopyBytes()))
            .IsTrue();
        await Assert.That(stage.GetPrim("/World").GetDouble("weight")).IsEqualTo(7d);
        UsdLayerEditResult redone = review.CompareAndRestore(undone.AfterSnapshot, applied.AfterSnapshot);
        await Assert.That(redone.Outcome).IsEqualTo(UsdLayerEditOutcome.Applied);
        await Assert.That(redone.AfterSnapshot!.Opinions[0].Value.AsDouble()).IsEqualTo(47d);
        await Assert.That(redone.AfterSnapshot.CopyBytes().SequenceEqual(review.CaptureAuthored([address]).CopyBytes()))
            .IsTrue();
        await Assert.That(stage.EditTargetLayerIdentifier).IsEqualTo(editTarget);
    }

    [Test]
    public async Task OwnedCreationCleanupPreservesUnrelatedDataAndRefusesUnaddressedSamples()
    {
        using NativeDocument document = await NativeDocument.OpenAsync();
        UsdStage stage = document.Stage;
        using UsdLayer review = stage.GetUserReviewLayer();
        var address = Default("/Owned/Nested.value");
        UsdLayerAuthoredSnapshot before = review.CaptureAuthored([address]);
        UsdLayerAuthoredSnapshot created = ApplyDouble(review, before, 1);
        await Assert.That(stage.HasPrim("/Owned/Nested")).IsTrue();
        UsdLayerEditResult removed = review.CompareAndRestore(created, before);
        await Assert.That(removed.Outcome).IsEqualTo(UsdLayerEditOutcome.Applied);
        await Assert.That(stage.HasPrim("/Owned")).IsFalse();

        created = ApplyDouble(review, removed.AfterSnapshot!, 2);
        stage.SetEditTarget(review);
        stage.GetPrim("/Owned/Nested").SetDouble("other", 8);
        removed = review.CompareAndRestore(created, before);
        await Assert.That(removed.Outcome).IsEqualTo(UsdLayerEditOutcome.Applied);
        await Assert.That(stage.HasPrim("/Owned/Nested")).IsTrue();
        await Assert.That(stage.GetPrim("/Owned/Nested").GetDouble("other")).IsEqualTo(8d);
        created = ApplyDouble(review, removed.AfterSnapshot!, 3);
        stage.GetPrim("/Owned/Nested").SetDouble("value", 9, 2);
        UsdLayerEditResult destructiveUndo = review.CompareAndRestore(created, before);
        await Assert.That(destructiveUndo.Outcome).IsEqualTo(UsdLayerEditOutcome.Conflict);
        await Assert.That(destructiveUndo.AfterSnapshot).IsNull();
        await Assert.That(destructiveUndo.Diagnostic).Contains("foreign");
        await Assert.That(review.CaptureAuthored([address]).Opinions[0].Value.AsDouble()).IsEqualTo(3d);
        await Assert.That(stage.GetPrim("/Owned/Nested").GetDouble("value", 2)).IsEqualTo(9d);
    }

    [Test]
    public async Task DefaultsBlocksExactTimesAndAllListBucketsRemainDistinct()
    {
        using NativeDocument document = await NativeDocument.OpenAsync();
        using UsdLayer review = document.Stage.GetUserReviewLayer();
        UsdLayerEditAddress[] addresses =
        [
            Default("/World.value"),
            new("/World.value", UsdLayerEditField.TimeSample, 1.25),
            new("/World.input", UsdLayerEditField.AttributeConnections),
            new("/World.link", UsdLayerEditField.RelationshipTargets)
        ];
        var targets = new UsdPathListEdit(false, addedItems: ["/Added"], prependedItems: ["/Prepended"],
            appendedItems: ["/Appended"], deletedItems: ["/Deleted"], orderedItems: ["/Ordered"]);
        var connections = new UsdPathListEdit(false, addedItems: ["/Added.out"],
            prependedItems: ["/Prepended.out"], appendedItems: ["/Appended.out"],
            deletedItems: ["/Deleted.out"], orderedItems: ["/Ordered.out"]);
        UsdLayerAuthoredSnapshot before = review.CaptureAuthored(addresses);
        UsdLayerEditResult result = review.CompareAndApply(before,
        [
            UsdLayerEdit.Block(addresses[0], "double"),
            UsdLayerEdit.Block(addresses[1], "double"),
            UsdLayerEdit.Set(addresses[2], UsdLayerEditValue.FromPathList(connections), "double"),
            UsdLayerEdit.Set(addresses[3], UsdLayerEditValue.FromPathList(targets))
        ]);
        await Assert.That(result.Outcome).IsEqualTo(UsdLayerEditOutcome.Applied);
        UsdLayerAuthoredSnapshot after = review.CaptureAuthored(addresses);
        await Assert.That(after.Opinions[0].Value.Kind).IsEqualTo(UsdLayerEditValueKind.Block);
        await Assert.That(after.Opinions[1].Value.Kind).IsEqualTo(UsdLayerEditValueKind.Block);
        await Assert.That(after.Opinions[2].Value.Equals(UsdLayerEditValue.FromPathList(connections))).IsTrue();
        await Assert.That(after.Opinions[3].Value.Equals(UsdLayerEditValue.FromPathList(targets))).IsTrue();
        await Assert.That(review.CaptureAuthored([new("/World.value", UsdLayerEditField.TimeSample, 2)])
            .Opinions[0].Value.Kind).IsEqualTo(UsdLayerEditValueKind.Absent);

        result = review.CompareAndApply(after,
        [
            UsdLayerEdit.Clear(addresses[0]),
            UsdLayerEdit.Set(addresses[1], UsdLayerEditValue.FromDouble(-0.0)),
            UsdLayerEdit.Set(addresses[2], UsdLayerEditValue.FromPathList(new UsdPathListEdit(true))),
            UsdLayerEdit.Set(addresses[3], UsdLayerEditValue.FromPathList(new UsdPathListEdit(true, ["/Explicit"])))
        ]);
        await Assert.That(result.Outcome).IsEqualTo(UsdLayerEditOutcome.Applied);
        after = result.AfterSnapshot!;
        await Assert.That(after.Opinions[0].PropertyKind).IsEqualTo(UsdLayerPropertyKind.Attribute);
        await Assert.That(after.Opinions[0].TypeName).IsEqualTo("double");
        await Assert.That(after.Opinions[0].Value.Kind).IsEqualTo(UsdLayerEditValueKind.Absent);
        await Assert.That(BitConverter.DoubleToUInt64Bits(after.Opinions[1].Value.AsDouble()))
            .IsEqualTo(0x8000000000000000ul);
        await Assert.That(after.Opinions[2].Value.AsPathList().IsExplicit).IsTrue();
        await Assert.That(after.Opinions[2].Value.AsPathList().ExplicitItems.Count).IsEqualTo(0);
        await Assert.That(after.Opinions[3].Value.AsPathList().ExplicitItems[0]).IsEqualTo("/Explicit");
        await Assert.That(review.CompareAndRestore(after, before).Outcome).IsEqualTo(UsdLayerEditOutcome.Applied);
        await Assert.That(review.CaptureAuthored(addresses).Opinions.All(opinion =>
            opinion.PropertyKind == UsdLayerPropertyKind.Absent)).IsTrue();
    }

    [Test]
    public async Task DisjointFieldsAndSamplesIgnoreGlobalRevisionButAffectedChangesConflict()
    {
        using NativeDocument document = await NativeDocument.OpenAsync();
        UsdStage stage = document.Stage;
        using UsdLayer review = stage.GetUserReviewLayer();
        var firstAddress = Default("/World.first");
        var secondAddress = Default("/World.second");
        UsdLayerAuthoredSnapshot first = review.CaptureAuthored([firstAddress]);
        UsdLayerAuthoredSnapshot second = review.CaptureAuthored([secondAddress]);
        UsdLayerAuthoredSnapshot firstAfter = ApplyDouble(review, first, 1);
        UsdLayerAuthoredSnapshot secondAfter = ApplyDouble(review, second, 2);
        await Assert.That(secondAfter.Revision > second.Revision).IsTrue();
        UsdLayerEditResult conflicting = review.CompareAndApply(first,
            [UsdLayerEdit.Set(firstAddress, UsdLayerEditValue.FromDouble(8), "double")]);
        await Assert.That(conflicting.Outcome).IsEqualTo(UsdLayerEditOutcome.Conflict);
        await Assert.That(conflicting.AfterSnapshot).IsNull();
        await Assert.That(conflicting.Diagnostic).Contains("changed");

        var one = new UsdLayerEditAddress("/World.first", UsdLayerEditField.TimeSample, 1);
        var two = new UsdLayerEditAddress("/World.first", UsdLayerEditField.TimeSample, 2);
        UsdLayerAuthoredSnapshot oneBefore = review.CaptureAuthored([one]);
        UsdLayerAuthoredSnapshot twoBefore = review.CaptureAuthored([two]);
        UsdLayerAuthoredSnapshot oneAfter = ApplyDouble(review, oneBefore, 10);
        _ = ApplyDouble(review, twoBefore, 20);
        await Assert.That(review.CompareAndRestore(oneAfter, oneBefore).Outcome).IsEqualTo(UsdLayerEditOutcome.Applied);
        await Assert.That(review.CaptureAuthored([two]).Opinions[0].Value.AsDouble()).IsEqualTo(20d);
        stage.SetEditTarget(review);
        stage.GetPrim("/World").SetDouble("first", 99);
        conflicting = review.CompareAndRestore(firstAfter, first);
        await Assert.That(conflicting.Outcome).IsEqualTo(UsdLayerEditOutcome.Conflict);
        await Assert.That(review.CaptureAuthored([firstAddress]).Opinions[0].Value.AsDouble()).IsEqualTo(99d);
        await Assert.That(review.CaptureAuthored([secondAddress]).Opinions[0].Value.AsDouble()).IsEqualTo(2d);
    }

    [Test]
    public async Task InvalidSecondMutationMakesNoWritesAndLeavesTheEditTargetUnchanged()
    {
        using NativeDocument document = await NativeDocument.OpenAsync();
        using UsdLayer review = document.Stage.GetUserReviewLayer();
        UsdLayerEditAddress[] addresses = [Default("/World.first"), Default("/World.second")];
        UsdLayerAuthoredSnapshot before = review.CaptureAuthored(addresses);
        string target = document.Stage.EditTargetLayerIdentifier;
        OpenUsdNativeException? error = await Assert.That(() => review.CompareAndApply(before,
        [
            UsdLayerEdit.Set(addresses[0], UsdLayerEditValue.FromDouble(1), "double"),
            UsdLayerEdit.Set(addresses[1], UsdLayerEditValue.FromInt32(2), "double")
        ])).Throws<OpenUsdNativeException>();
        await Assert.That(error!.Message).Contains("type");
        await Assert.That(review.CaptureAuthored(addresses).CopyBytes().SequenceEqual(before.CopyBytes())).IsTrue();
        await Assert.That(document.Stage.EditTargetLayerIdentifier).IsEqualTo(target);
    }

    [Test]
    public async Task GenericLayersStayCaptureOnlyAndLocalLookupNeverOpensForeignLayers()
    {
        using NativeDocument document = await NativeDocument.OpenAsync();
        using NativeDocument foreign = await NativeDocument.OpenAsync();
        UsdStage stage = document.Stage;
        using UsdLayer root = stage.GetRootLayer();
        using UsdLayer sameRoot = stage.GetLocalLayer(root.Identifier);
        UsdLayerEditingState state = root.GetEditingState();
        await Assert.That(sameRoot.GetEditingState().Identity).IsEqualTo(state.Identity);
        await Assert.That(state.PermissionToEdit).IsTrue();
        await Assert.That(state.CanAttemptAuthoredEdits).IsFalse();
        await Assert.That(state.EditingRestriction).Contains("capture-only");
        UsdLayerAuthoredSnapshot before = root.CaptureAuthored([Default("/World.weight")]);
        UsdLayerEditResult result = root.CompareAndApply(before,
            [UsdLayerEdit.Set(before.Addresses[0], UsdLayerEditValue.FromDouble(8))]);
        await Assert.That(result.Outcome).IsEqualTo(UsdLayerEditOutcome.NotEditable);
        await Assert.That(result.AfterSnapshot).IsNull();
        await Assert.That(root.CaptureAuthored([Default("/World.weight")]).Opinions[0].Value.AsDouble()).IsEqualTo(7d);
        await Assert.That(() => root.CaptureCheckpoint()).Throws<OpenUsdNativeException>();
        OpenUsdNativeException? error = await Assert.That(() => stage.GetLocalLayer(foreign.Stage.RootLayerIdentifier))
            .Throws<OpenUsdNativeException>();
        await Assert.That(error!.Status).IsEqualTo(OpenUsdNativeStatus.NotFound);
        root.AddSublayer(foreign.Stage.RootLayerIdentifier);
        using UsdLayer local = stage.GetLocalLayer(foreign.Stage.RootLayerIdentifier);
        await Assert.That(local.GetEditingState().Role).IsEqualTo(UsdLayerRole.Local);
        await Assert.That(local.GetEditingState().CanAttemptAuthoredEdits).IsFalse();
        await Assert.That(local.CaptureAuthored([Default("/World.weight")]).Opinions[0].Value.AsDouble()).IsEqualTo(7d);
        root.RemoveSublayer(foreign.Stage.RootLayerIdentifier);
    }

    [Test]
    public async Task DetachAndReattachPermanentlyInvalidateOldGenerations()
    {
        using NativeDocument document = await NativeDocument.OpenAsync();
        UsdStage stage = document.Stage;
        using UsdLayer review = stage.GetUserReviewLayer();
        using UsdLayer session = stage.GetSessionLayer();
        UsdLayerAuthoredSnapshot before = review.CaptureAuthored([Default("/World.weight")]);
        string identifier = review.Identifier;
        session.RemoveSublayer(identifier);
        UsdLayerEditingState detached = review.GetEditingState();
        await Assert.That(detached.CurrentlyLocal).IsFalse();
        await Assert.That(detached.Identity.Generation != before.Identity.Generation).IsTrue();
        UsdLayerEditResult result = review.CompareAndApply(before,
            [UsdLayerEdit.Set(before.Addresses[0], UsdLayerEditValue.FromDouble(8), "double")]);
        await Assert.That(result.Outcome).IsEqualTo(UsdLayerEditOutcome.StaleTarget);
        await Assert.That(result.AfterSnapshot).IsNull();
        await Assert.That(() => stage.GetUserReviewLayer()).Throws<OpenUsdNativeException>();
        session.AddSublayer(identifier);
        await Assert.That(review.GetEditingState().CurrentlyLocal).IsTrue();
        result = review.CompareAndRestore(before, before);
        await Assert.That(result.Outcome).IsEqualTo(UsdLayerEditOutcome.StaleTarget);
        UsdLayerAuthoredSnapshot fresh = review.CaptureAuthored([before.Addresses[0]]);
        await Assert.That(fresh.Identity.Generation != before.Identity.Generation).IsTrue();
        await Assert.That(ApplyDouble(review, fresh, 9).Opinions[0].Value.AsDouble()).IsEqualTo(9d);
    }

    [Test]
    public async Task OwnedLayerHandleRetainsItsStageButRemainsDisposableAndStageBound()
    {
        using NativeDocument document = await NativeDocument.OpenAsync();
        using UsdLayer review = document.Stage.GetUserReviewLayer();
        UsdLayerEditingState before = review.GetEditingState();
        document.Stage.Dispose();
        await Assert.That(review.GetEditingState().Identity).IsEqualTo(before.Identity);
        UsdLayerCheckpoint checkpoint = review.CaptureCheckpoint();
        review.Dispose();
        await Assert.That(() => review.GetEditingState()).Throws<ObjectDisposedException>();
        await Assert.That(checkpoint.CopyBytes().Length > 0).IsTrue();
    }

    [Test]
    public async Task NormalizedRealUserLayerIsSeparateFromPhysicsAndSessionContainer()
    {
        using NativeDocument document = await NativeDocument.OpenAsync();
        UsdStage stage = document.Stage;
        stage.SetEditTargetToSessionLayer();
        stage.GetPrim("/World").SetDouble("weight", 21);
        using UsdSessionOverlay overlay = stage.NormalizeSessionOverlay();
        using UsdLayer review = stage.GetUserReviewLayer();
        using UsdLayer physics = stage.GetLocalLayer(overlay.PhysicsLayerIdentifier);
        using UsdLayer session = stage.GetSessionLayer();
        await Assert.That(review.Identifier).IsEqualTo(overlay.UserLayerIdentifier);
        await Assert.That(review.GetEditingState().Role).IsEqualTo(UsdLayerRole.UserReview);
        await Assert.That(physics.GetEditingState().Role).IsEqualTo(UsdLayerRole.Physics);
        await Assert.That(physics.GetEditingState().CanAttemptAuthoredEdits).IsFalse();
        await Assert.That(session.GetEditingState().Role).IsEqualTo(UsdLayerRole.SessionContainer);
        stage.SetEditTarget(physics);
        stage.GetPrim("/World").SetDouble("weight", 99);
        var address = Default("/World.weight");
        UsdLayerAuthoredSnapshot userBefore = review.CaptureAuthored([address]);
        await Assert.That(userBefore.Opinions[0].Value.AsDouble()).IsEqualTo(21d);
        await Assert.That(physics.CaptureAuthored([address]).Opinions[0].Value.AsDouble()).IsEqualTo(99d);
        await Assert.That(session.CaptureAuthored([address]).Opinions[0].PropertyKind)
            .IsEqualTo(UsdLayerPropertyKind.Absent);
        await Assert.That(ApplyDouble(review, userBefore, 22).Opinions[0].Value.AsDouble()).IsEqualTo(22d);
        await Assert.That(stage.GetPrim("/World").GetDouble("weight")).IsEqualTo(99d);
        await Assert.That(review.CaptureCheckpoint().Identifier).IsEqualTo(overlay.UserLayerIdentifier);
        stage.SetEditTargetToRootLayer();
        overlay.Dispose();
        await Assert.That(stage.GetPrim("/World").GetDouble("weight")).IsEqualTo(22d);
    }

    [Test]
    public async Task StartingSimulationOverlayPreservesExistingReviewHistoryIdentity()
    {
        using NativeDocument document = await NativeDocument.OpenAsync();
        UsdStage stage = document.Stage;
        using UsdLayer review = stage.GetUserReviewLayer();
        var address = Default("/World.weight");
        UsdLayerAuthoredSnapshot before = review.CaptureAuthored([address]);
        UsdLayerAuthoredSnapshot edited = ApplyDouble(review, before, 4);

        using UsdSessionOverlay overlay = stage.NormalizeSessionOverlay();
        using UsdLayer current = stage.GetUserReviewLayer();
        await Assert.That(current.Identifier).IsEqualTo(review.Identifier);
        await Assert.That(overlay.UserLayerIdentifier).IsEqualTo(review.Identifier);
        await Assert.That(current.GetEditingState().Identity).IsEqualTo(before.Identity);
        await Assert.That(current.GetEditingState().Role).IsEqualTo(UsdLayerRole.UserReview);
        await Assert.That(stage.GetPrim("/World").GetDouble("weight")).IsEqualTo(4d);

        UsdLayerEditResult undone = current.CompareAndRestore(edited, before);
        await Assert.That(undone.Outcome).IsEqualTo(UsdLayerEditOutcome.Applied);
        await Assert.That(undone.AfterSnapshot!.Opinions[0].PropertyKind).IsEqualTo(UsdLayerPropertyKind.Absent);
        await Assert.That(stage.GetPrim("/World").GetDouble("weight")).IsEqualTo(7d);
    }

    [Test]
    public async Task ConditionalSavedAcknowledgementCannotHideInterveningOrRevertedEdits()
    {
        using NativeDocument document = await NativeDocument.OpenAsync();
        using UsdLayer review = document.Stage.GetUserReviewLayer();
        var address = Default("/World.weight");
        UsdLayerAuthoredSnapshot zero = review.CaptureAuthored([address]);
        UsdLayerAuthoredSnapshot first = ApplyDouble(review, zero, 1);
        UsdLayerCheckpoint saved = review.CaptureCheckpoint();
        await Assert.That(review.GetEditingState().IsDirty).IsTrue();
        await Assert.That(review.AcknowledgeSaved(saved)).IsTrue();
        await Assert.That(review.GetEditingState().IsDirty).IsFalse();
        await Assert.That(review.GetEditingState().NativeIsDirty).IsTrue();
        UsdLayerAuthoredSnapshot second = ApplyDouble(review, first, 2);
        await Assert.That(review.AcknowledgeSaved(saved)).IsFalse();
        await Assert.That(review.GetEditingState().IsDirty).IsTrue();
        UsdLayerEditResult reverted = review.CompareAndRestore(second, first);
        await Assert.That(reverted.Outcome).IsEqualTo(UsdLayerEditOutcome.Applied);
        await Assert.That(reverted.AfterSnapshot!.Opinions[0].Value.AsDouble()).IsEqualTo(1d);
        await Assert.That(review.AcknowledgeSaved(saved)).IsFalse();
        await Assert.That(review.GetEditingState().IsDirty).IsTrue();
        await Assert.That(review.AcknowledgeSaved(review.CaptureCheckpoint())).IsTrue();
    }

    [Test]
    public async Task CheckpointRestoreIsExplicitAndInvalidatesPerEditHistory()
    {
        using NativeDocument document = await NativeDocument.OpenAsync();
        using UsdLayer review = document.Stage.GetUserReviewLayer();
        var address = Default("/World.weight");
        UsdLayerCheckpoint empty = review.CaptureCheckpoint();
        UsdLayerAuthoredSnapshot before = review.CaptureAuthored([address]);
        UsdLayerAuthoredSnapshot after = ApplyDouble(review, before, 47);
        UsdLayerCheckpoint edited = review.CaptureCheckpoint();
        await Assert.That(review.RestoreCheckpoint(empty, edited).Outcome).IsEqualTo(UsdLayerEditOutcome.Conflict);
        UsdLayerCheckpointRestoreResult restored = review.RestoreCheckpoint(edited, empty);
        await Assert.That(restored.Outcome).IsEqualTo(UsdLayerEditOutcome.Applied);
        await Assert.That(restored.AfterCheckpoint!.Identity.Generation != edited.Identity.Generation).IsTrue();
        await Assert.That(review.CaptureAuthored([address]).Opinions[0].PropertyKind)
            .IsEqualTo(UsdLayerPropertyKind.Absent);
        await Assert.That(review.CompareAndRestore(after, before).Outcome).IsEqualTo(UsdLayerEditOutcome.StaleTarget);
        await Assert.That(review.AcknowledgeSaved(edited)).IsFalse();
        UsdLayerCheckpointRestoreResult redone = review.RestoreCheckpoint(restored.AfterCheckpoint, edited);
        await Assert.That(redone.Outcome).IsEqualTo(UsdLayerEditOutcome.Applied);
        await Assert.That(review.CaptureAuthored([address]).Opinions[0].Value.AsDouble()).IsEqualTo(47d);
        await Assert.That(redone.AfterCheckpoint!.CopyBytes().SequenceEqual(review.CaptureCheckpoint().CopyBytes()))
            .IsTrue();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task CheckpointRestoresSignedZeroBitsRatherThanNumericalEquality(bool timeSample)
    {
        using NativeDocument document = await NativeDocument.OpenAsync();
        using UsdLayer review = document.Stage.GetUserReviewLayer();
        UsdLayerEditAddress address = timeSample
            ? new("/World.zero", UsdLayerEditField.TimeSample, 1)
            : Default("/World.zero");
        UsdLayerAuthoredSnapshot plus = ApplyDouble(review, review.CaptureAuthored([address]), 0);
        UsdLayerCheckpoint positive = review.CaptureCheckpoint();
        _ = ApplyDouble(review, plus, BitConverter.UInt64BitsToDouble(0x8000000000000000));
        UsdLayerCheckpoint negative = review.CaptureCheckpoint();
        await Assert.That(review.AcknowledgeSaved(negative)).IsTrue();

        UsdLayerCheckpointRestoreResult restored = review.RestoreCheckpoint(negative, positive);
        await Assert.That(restored.Outcome).IsEqualTo(UsdLayerEditOutcome.Applied);
        await Assert.That(BitConverter.DoubleToUInt64Bits(
            review.CaptureAuthored([address]).Opinions[0].Value.AsDouble())).IsEqualTo(0ul);
        await Assert.That(restored.AfterCheckpoint!.HasSamePayload(review.CaptureCheckpoint())).IsTrue();
        await Assert.That(review.GetEditingState().IsDirty).IsTrue();

        UsdLayerCheckpointRestoreResult redone = review.RestoreCheckpoint(restored.AfterCheckpoint, negative);
        await Assert.That(redone.Outcome).IsEqualTo(UsdLayerEditOutcome.Applied);
        await Assert.That(BitConverter.DoubleToUInt64Bits(
            review.CaptureAuthored([address]).Opinions[0].Value.AsDouble())).IsEqualTo(0x8000000000000000ul);
        await Assert.That(redone.AfterCheckpoint!.HasSamePayload(review.CaptureCheckpoint())).IsTrue();
    }

    [Test]
    public async Task CheckpointExportsReviewOnlyBytesForCallerPublicationAndNativeReopen()
    {
        using NativeDocument document = await NativeDocument.OpenAsync();
        using UsdLayer review = document.Stage.GetUserReviewLayer();
        var address = Default("/World.weight");
        _ = ApplyDouble(review, review.CaptureAuthored([address]), 47);
        UsdLayerCheckpoint checkpoint = review.CaptureCheckpoint();
        string destination = document.NewPath("published");
        byte[] exported = checkpoint.ExportBytes(destination);
        await Assert.That(File.Exists(destination)).IsFalse();
        await Assert.That(Encoding.UTF8.GetString(exported)).StartsWith("#usda 1.0");
        await File.WriteAllBytesAsync(destination, exported);
        exported.AsSpan().Clear();
        using UsdStage reopened = UsdStage.Open(destination);
        using UsdLayer reopenedRoot = reopened.GetRootLayer();
        await Assert.That(reopened.HasPrim("/OnlyRoot")).IsFalse();
        UsdLayerAuthoredOpinion actual = reopenedRoot.CaptureAuthored([address]).Opinions[0];
        await Assert.That(actual.TypeName).IsEqualTo("double");
        await Assert.That(actual.Value.AsDouble()).IsEqualTo(47d);
        await Assert.That(review.AcknowledgeSaved(checkpoint)).IsTrue();
        await Assert.That(checkpoint.ExportBytes(document.NewPath("another"))[0]).IsEqualTo((byte)'#');
    }

    [Test]
    public async Task MetadataAndNonfiniteCheckpointsStayLosslessButUnsupportedTextExportFails()
    {
        using NativeDocument document = await NativeDocument.OpenAsync();
        using UsdLayer review = document.Stage.GetUserReviewLayer();
        UsdLayerCheckpoint empty = review.CaptureCheckpoint();
        review.SetMetadata("unrecognized:entry", "preserved");
        UsdLayerCheckpoint metadata = review.CaptureCheckpoint();
        await Assert.That(() => metadata.ExportBytes(document.NewPath("metadata"))).Throws<OpenUsdNativeException>();
        await Assert.That(review.GetMetadataString("unrecognized:entry")).IsEqualTo("preserved");
        await Assert.That(review.RestoreCheckpoint(metadata, empty).Outcome).IsEqualTo(UsdLayerEditOutcome.Applied);
        var address = Default("/World.nan");
        double nan = BitConverter.UInt64BitsToDouble(0x7ff8000000001234);
        _ = ApplyDouble(review, review.CaptureAuthored([address]), nan);
        UsdLayerCheckpoint nonfinite = review.CaptureCheckpoint();
        double capturedNan = review.CaptureAuthored([address]).Opinions[0].Value.AsDouble();
        await Assert.That(BitConverter.DoubleToUInt64Bits(capturedNan))
            .IsEqualTo(0x7ff8000000001234ul);
        await Assert.That(() => nonfinite.ExportBytes(document.NewPath("nonfinite"))).Throws<OpenUsdNativeException>();
        await Assert.That(nonfinite.CopyBytes().Length > 0).IsTrue();
    }

    [Test]
    public async Task RelativeAssetsCannotBeSilentlyReanchoredInACheckpoint()
    {
        using NativeDocument document = await NativeDocument.OpenAsync();
        using UsdLayer review = document.Stage.GetUserReviewLayer();
        var address = Default("/World.texture");
        UsdLayerEditResult edit = review.CompareAndApply(review.CaptureAuthored([address]),
            [UsdLayerEdit.Set(address, UsdLayerEditValue.FromAssetPath("relative.exr"), "asset")]);
        await Assert.That(edit.Outcome).IsEqualTo(UsdLayerEditOutcome.Applied);
        await Assert.That(edit.AfterSnapshot!.Opinions[0].Value.AsAssetPath().AuthoredPath).IsEqualTo("relative.exr");
        OpenUsdNativeException? error = await Assert.That(() => review.CaptureCheckpoint())
            .Throws<OpenUsdNativeException>();
        await Assert.That(error!.Message).Contains("anchor");
    }

    [Test]
    public async Task DetachedSnapshotsResultsAndCheckpointsLeaveTheSchedulerAndSurviveDisposal()
    {
        using NativeDocument document = await NativeDocument.OpenAsync();
        var address = Default("/World.weight");
        var scheduler = UsdStageScheduler.Open(document.Path);
        UsdLayerCheckpoint checkpoint;
        UsdLayerAuthoredSnapshot before;
        UsdLayerEditResult applied;
        try
        {
            before = await scheduler.InvokeAsync(stage =>
            {
                using UsdLayer layer = stage.GetUserReviewLayer();
                return layer.CaptureAuthored([address]);
            });
            applied = await scheduler.InvokeAsync(stage =>
            {
                using UsdLayer layer = stage.GetUserReviewLayer();
                return layer.CompareAndApply(before,
                    [UsdLayerEdit.Set(address, UsdLayerEditValue.FromDouble(47), "double")]);
            });
            checkpoint = await scheduler.InvokeAsync(stage =>
            {
                using UsdLayer layer = stage.GetUserReviewLayer();
                return layer.CaptureCheckpoint();
            });
            UsdLayerEditingState state = await scheduler.InvokeAsync(stage =>
            {
                using UsdLayer layer = stage.GetUserReviewLayer();
                return layer.GetEditingState();
            });
            await Assert.That(state.Identity).IsEqualTo(checkpoint.Identity);
            await Assert.That(async () => await scheduler.InvokeAsync(static stage => stage.GetUserReviewLayer()))
                .Throws<UsdStageBoundResultException>();
            bool called = false;
            await Assert.That(async () => await scheduler.InvokeAsync(_ => called = true, new CancellationToken(true)))
                .Throws<OperationCanceledException>();
            await Assert.That(called).IsFalse();
        }
        finally
        {
            await scheduler.DisposeAsync();
        }
        document.Stage.Dispose();
        await Assert.That(before.Opinions[0].PropertyKind).IsEqualTo(UsdLayerPropertyKind.Absent);
        await Assert.That(applied.AfterSnapshot!.Opinions[0].Value.AsDouble()).IsEqualTo(47d);
        await Assert.That(checkpoint.ExportBytes(document.NewPath("detached"))[0]).IsEqualTo((byte)'#');
        await Assert.That(checkpoint.CopyBytes().Length > 0).IsTrue();
    }

    private static UsdLayerEditAddress Default(string path) => new(path, UsdLayerEditField.Default);

    private static UsdLayerAuthoredSnapshot ApplyDouble(
        UsdLayer layer, UsdLayerAuthoredSnapshot expected, double value)
    {
        UsdLayerEditResult result = layer.CompareAndApply(expected,
            [UsdLayerEdit.Set(expected.Addresses[0], UsdLayerEditValue.FromDouble(value), "double")]);
        if (result.Outcome != UsdLayerEditOutcome.Applied || result.AfterSnapshot is null)
        {
            throw new InvalidOperationException($"Expected Applied, got {result.Outcome}: {result.Diagnostic}");
        }
        return result.AfterSnapshot;
    }

    private sealed class NativeDocument : IDisposable
    {
        private readonly List<string> _paths = [];

        private NativeDocument(string path, UsdStage stage)
        {
            Path = path;
            Stage = stage;
            _paths.Add(path);
        }

        internal string Path { get; }
        internal UsdStage Stage { get; }

        internal static async Task<NativeDocument> OpenAsync()
        {
            string? plugins = Environment.GetEnvironmentVariable("OPENUSD_TEST_PLUGIN_PATH");
            if (string.IsNullOrWhiteSpace(plugins))
            {
                Skip.Test(
                    "Set OPENUSD_TEST_PLUGIN_PATH and stage the ABI19 runtime to execute authored editing tests.");
            }
            _ = OpenUsdNativeRuntime.RegisterPlugins(plugins!);
            string root = Environment.GetEnvironmentVariable("OPENUSD_TEST_WORK_ROOT")
                ?? System.IO.Path.Combine(AppContext.BaseDirectory, "native-work");
            Directory.CreateDirectory(root);
            string path = System.IO.Path.Combine(root, $"layer-edit-{Guid.NewGuid():N}.usda");
            await File.WriteAllTextAsync(path,
                """
                #usda 1.0
                def Xform "World"
                {
                    double weight = 7
                }
                def Xform "OnlyRoot" {}
                """);
            try
            {
                return new NativeDocument(path, UsdStage.Open(path));
            }
            catch
            {
                File.Delete(path);
                throw;
            }
        }

        internal string NewPath(string name)
        {
            string path = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(Path)!,
                $"layer-edit-{name}-{Guid.NewGuid():N}.usda");
            _paths.Add(path);
            return path;
        }

        public void Dispose()
        {
            Stage.Dispose();
            foreach (string path in _paths)
            {
                File.Delete(path);
            }
        }
    }
}
