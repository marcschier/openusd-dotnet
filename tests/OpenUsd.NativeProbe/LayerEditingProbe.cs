// Copyright (c) marcschier. Licensed under the MIT License.

using OpenUsd.Editing;

namespace OpenUsd.NativeProbe;

internal static class LayerEditingProbe
{
    internal static async Task RunAsync(string directory)
    {
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, $"layer-editing-aot-{Guid.NewGuid():N}.usda");
        string destination = Path.Combine(directory, $"layer-editing-delta-{Guid.NewGuid():N}.usda");
        try
        {
            await File.WriteAllTextAsync(path,
                """
                #usda 1.0
                def Xform "World"
                {
                    double weight = 7
                }
                def Xform "OnlyRoot" {}
                """).ConfigureAwait(false);
            UsdLayerEditAddress[] addresses =
            [
                new("/World.weight", UsdLayerEditField.Default),
                new("/World.tokens", UsdLayerEditField.Default),
                new("/World.matrix", UsdLayerEditField.Default),
                new("/World.targets", UsdLayerEditField.RelationshipTargets),
                new("/World.weight", UsdLayerEditField.TimeSample, 2.5)
            ];
            string[] tokens = ["first", "second"];
            UsdLayerEditValue tokenValue = UsdLayerEditValue.FromTokenArray(tokens);
            tokens[0] = "caller mutation";
            UsdLayerCheckpoint checkpoint;
            UsdLayerEditResult applied;
            await using (UsdStageScheduler scheduler = UsdStageScheduler.Open(path))
            {
                UsdLayerAuthoredSnapshot before = await scheduler.InvokeAsync(stage =>
                {
                    using UsdLayer review = stage.GetUserReviewLayer();
                    using UsdLayer resolved = stage.GetLocalLayer(review.Identifier);
                    using UsdLayer root = stage.GetRootLayer();
                    if (resolved.GetEditingState().Identity != review.GetEditingState().Identity ||
                        root.GetEditingState().CanAttemptAuthoredEdits ||
                        root.CaptureAuthored([addresses[0]]).Opinions[0].Value.AsDouble() != 7)
                    {
                        throw new InvalidOperationException(
                            "Layer resolution or generic capture-only state is incorrect.");
                    }
                    UsdLayerAuthoredSnapshot snapshot = review.CaptureAuthored(addresses);
                    if (snapshot.Opinions.Any(opinion => opinion.PropertyKind != UsdLayerPropertyKind.Absent))
                    {
                        throw new InvalidOperationException("Weaker root opinions leaked into target-layer capture.");
                    }
                    return snapshot;
                }).ConfigureAwait(false);
                applied = await scheduler.InvokeAsync(stage =>
                {
                    using UsdLayer review = stage.GetUserReviewLayer();
                    string editTarget = stage.EditTargetLayerIdentifier;
                    UsdLayerEditResult result = review.CompareAndApply(before,
                    [
                        UsdLayerEdit.Set(addresses[0], UsdLayerEditValue.FromDouble(47), "double"),
                        UsdLayerEdit.Set(addresses[1], tokenValue, "token[]"),
                        UsdLayerEdit.Set(addresses[2],
                            UsdLayerEditValue.FromMatrix4d(UsdMatrix4d.CreateTranslation(1, 2, 3)), "matrix4d"),
                        UsdLayerEdit.Set(addresses[3], UsdLayerEditValue.FromPathList(new UsdPathListEdit(false,
                            addedItems: ["/Added"], prependedItems: ["/Prepended"], appendedItems: ["/Appended"],
                            deletedItems: ["/Deleted"], orderedItems: ["/Ordered"]))),
                        UsdLayerEdit.Block(addresses[4], "double")
                    ]);
                    if (stage.EditTargetLayerIdentifier != editTarget)
                    {
                        throw new InvalidOperationException("Authored transaction changed the stage edit target.");
                    }
                    RequireApplied(result);
                    return result;
                }).ConfigureAwait(false);
                UsdLayerEditResult undone = await scheduler.InvokeAsync(stage =>
                {
                    using UsdLayer review = stage.GetUserReviewLayer();
                    using UsdSessionOverlay overlay = stage.NormalizeSessionOverlay();
                    if (overlay.UserLayerIdentifier != review.Identifier ||
                        review.GetEditingState().Identity != before.Identity)
                    {
                        throw new InvalidOperationException(
                            "Starting simulation replaced the authored review history target.");
                    }
                    UsdLayerEditResult result = review.CompareAndRestore(applied.AfterSnapshot!, before);
                    RequireApplied(result);
                    if (result.AfterSnapshot!.Opinions.Any(
                        opinion => opinion.PropertyKind != UsdLayerPropertyKind.Absent) ||
                        !result.AfterSnapshot.CopyBytes().AsSpan().SequenceEqual(
                            review.CaptureAuthored(addresses).CopyBytes()))
                    {
                        throw new InvalidOperationException("Undo did not return actual resulting authored absence.");
                    }
                    if (stage.GetPrim("/World").GetDouble("weight") != 7)
                    {
                        throw new InvalidOperationException(
                            "Review undo after simulation start did not reveal the source.");
                    }
                    return result;
                }).ConfigureAwait(false);
                checkpoint = await scheduler.InvokeAsync(stage =>
                {
                    using UsdLayer review = stage.GetUserReviewLayer();
                    RequireApplied(review.CompareAndRestore(undone.AfterSnapshot!, applied.AfterSnapshot!));
                    UsdLayerCheckpoint saved = review.CaptureCheckpoint();
                    if (!review.AcknowledgeSaved(saved) || review.GetEditingState().IsDirty)
                    {
                        throw new InvalidOperationException("Matching checkpoint acknowledgement failed.");
                    }
                    return saved;
                }).ConfigureAwait(false);
            }
            if (applied.AfterSnapshot!.Opinions[0].Value.AsDouble() != 47 ||
                applied.AfterSnapshot.Opinions[1].Value.AsTokenArray()[0] != "first" ||
                applied.AfterSnapshot.Opinions[2].Value.AsMatrix4d().M30 != 1 ||
                applied.AfterSnapshot.Opinions[3].Value.AsPathList().OrderedItems[0] != "/Ordered" ||
                applied.AfterSnapshot.Opinions[4].Value.Kind != UsdLayerEditValueKind.Block)
            {
                throw new InvalidOperationException("Detached authored values did not survive scheduler disposal.");
            }
            checkpoint.CopyBytes().AsSpan().Clear();
            if (checkpoint.CopyBytes()[0] != 0x55)
            {
                throw new InvalidOperationException("Checkpoint exposed mutable packet storage.");
            }
            byte[] exported = checkpoint.ExportBytes(destination);
            if (File.Exists(destination))
            {
                throw new InvalidOperationException("Native export published a file instead of returning bytes.");
            }
            await File.WriteAllBytesAsync(destination, exported).ConfigureAwait(false);
            using (UsdStage reopened = UsdStage.Open(destination))
            using (UsdLayer root = reopened.GetRootLayer())
            {
                UsdLayerAuthoredSnapshot actual = root.CaptureAuthored(addresses);
                if (reopened.HasPrim("/OnlyRoot") || actual.Opinions[0].Value.AsDouble() != 47 ||
                    actual.Opinions[1].Value.AsTokenArray()[0] != "first" ||
                    actual.Opinions[4].Value.Kind != UsdLayerEditValueKind.Block)
                {
                    throw new InvalidOperationException(
                        "Native review delta reopen lost authored state or flattened the source.");
                }
            }
            Console.WriteLine(
                "LAYER_EDITING_MANAGED_OK: exact capture, typed CAS, overlay history continuity, " +
                "undo/redo, detached scheduler DTOs, checkpoint export/reopen");
        }
        finally
        {
            File.Delete(destination);
            File.Delete(path);
        }
    }

    private static void RequireApplied(UsdLayerEditResult result)
    {
        if (result.Outcome != UsdLayerEditOutcome.Applied || result.AfterSnapshot is null)
        {
            throw new InvalidOperationException($"Expected Applied, got {result.Outcome}: {result.Diagnostic}");
        }
    }
}
