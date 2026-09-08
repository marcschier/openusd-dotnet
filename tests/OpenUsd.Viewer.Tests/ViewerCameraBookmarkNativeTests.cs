// Copyright (c) marcschier. Licensed under the MIT License.

using System.Security.Cryptography;
using OpenUsd.Editing;
using OpenUsd.Rendering;

namespace OpenUsd.Viewer.Tests;

[NotInParallel]
public sealed partial class ViewerCameraBookmarkNativeTests
{
    private const string BookmarkProperty = "/__OpenUsdViewerReview.openusdViewer:cameraBookmarks";

    [Test]
    public async Task ReviewPropertyRoundTripsThroughVerifiedUrdAndSharedExactHistory()
    {
        RequireNativeJourney();
        using ViewerCameraBookmarkFixture files = await ViewerCameraBookmarkFixture.CreateAsync();
        var address = new UsdLayerEditAddress(BookmarkProperty, UsdLayerEditField.Default);
        var other = new UsdLayerEditAddress("/Body.review:other", UsdLayerEditField.Default);
        string secondRecord;
        await using (UsdStageScheduler scheduler = UsdStageScheduler.OpenForReview(files.SourcePath))
        {
            UsdReviewSourceBinding binding = await scheduler.InvokeAsync(
                static stage => stage.CaptureReviewSourceBinding());
            await Assert.That(Path.GetFullPath(binding.SourceRootPath)).IsEqualTo(files.SourcePath);
            await Assert.That(binding.SourceFingerprint).IsEqualTo(Convert.ToHexString(
                SHA256.HashData(await File.ReadAllBytesAsync(files.SourcePath))).ToLowerInvariant());
            await Assert.That(binding.Dependencies.Count).IsGreaterThan(1);
            ViewerCameraBookmark bookmark = ViewerCameraBookmark.CreateFree(
                Guid.Parse("a77aa89c-c77e-45fb-a057-5298f4688853"), "Hero",
                ViewerCameraBookmarkCatalog.CreateSourceStamp(binding), 12.5,
                new ViewportDimensions(800, 600), ViewerCameraNavigationState.CreateLegacy(false, 800f / 600));
            string firstRecord = ViewerCameraBookmarkCodec.EncodeCatalog([bookmark])[0];
            secondRecord = ViewerCameraBookmarkCodec.EncodeCatalog([bookmark.Rename("Side")])[0];
            await using var editor = new ViewerAuthoredEditController(scheduler, binding);
            string editTarget = await scheduler.InvokeAsync(static stage => stage.EditTargetLayerIdentifier);
            ViewerAuthoredEditCapture absent = await editor.CaptureAsync([address]);
            await Assert.That(absent.Snapshot.Opinions[0].PropertyKind).IsEqualTo(UsdLayerPropertyKind.Absent);
            ViewerAuthoredEditResult added = await editor.ApplyAsync(absent,
                [UsdLayerEdit.Set(address, UsdLayerEditValue.FromStringArray([firstRecord]), "string[]",
                    UsdLayerEditVariability.Uniform, creationCustom: true)], "Add saved view", Guid.NewGuid());
            await Assert.That(added.Outcome).IsEqualTo(UsdLayerEditOutcome.Applied);
            await Assert.That(await scheduler.InvokeAsync(stage =>
            {
                using UsdLayer review = stage.GetLocalLayer(absent.LayerIdentifier);
                return review.GetEditingState().Role;
            })).IsEqualTo(UsdLayerRole.UserReview);
            await AssertCatalogAsync(editor, address, firstRecord);
            await Assert.That((await editor.UndoAsync()).Outcome).IsEqualTo(UsdLayerEditOutcome.Applied);
            await Assert.That((await editor.CaptureAsync([address])).Snapshot.Opinions[0].PropertyKind)
                .IsEqualTo(UsdLayerPropertyKind.Absent);
            await Assert.That((await editor.RedoAsync()).Outcome).IsEqualTo(UsdLayerEditOutcome.Applied);

            ViewerAuthoredEditCapture beforeRename = await editor.CaptureAsync([address]);
            await editor.ApplyAsync(await editor.CaptureAsync([other]),
                [UsdLayerEdit.Set(other, UsdLayerEditValue.FromDouble(17), "double")],
                "Disjoint property edit", Guid.NewGuid());
            ViewerAuthoredEditResult renamed = await editor.ApplyAsync(beforeRename,
                [UsdLayerEdit.Set(address, UsdLayerEditValue.FromStringArray([secondRecord]))],
                "Rename saved view", Guid.NewGuid());
            await Assert.That(renamed.Outcome).IsEqualTo(UsdLayerEditOutcome.Applied);
            await Assert.That(editor.UndoDepth).IsEqualTo(3);
            await Assert.That((await editor.UndoAsync()).Outcome).IsEqualTo(UsdLayerEditOutcome.Applied);
            await AssertCatalogAsync(editor, address, firstRecord);
            await Assert.That((await editor.CaptureAsync([other])).Snapshot.Opinions[0].Value.AsDouble())
                .IsEqualTo(17d);
            await Assert.That((await editor.UndoAsync()).Outcome).IsEqualTo(UsdLayerEditOutcome.Applied);
            await Assert.That((await editor.UndoAsync()).Outcome).IsEqualTo(UsdLayerEditOutcome.Applied);
            await Assert.That((await editor.CaptureAsync([address])).Snapshot.Opinions[0].PropertyKind)
                .IsEqualTo(UsdLayerPropertyKind.Absent);
            for (int index = 0; index < 3; index++)
            {
                await Assert.That((await editor.RedoAsync()).Outcome).IsEqualTo(UsdLayerEditOutcome.Applied);
            }
            await AssertCatalogAsync(editor, address, secondRecord);
            ViewerReviewSaveResult saved =
                await editor.PublishReviewSaveAsync(await editor.PrepareReviewSaveAsync(files.DestinationPath));
            await Assert.That(saved.Acknowledged).IsTrue();
            await Assert.That((await editor.ReadDocumentAsync()).HasChanges).IsFalse();
            await Assert.That(editor.UndoDepth).IsEqualTo(3);
            await Assert.That(await scheduler.InvokeAsync(static stage => stage.EditTargetLayerIdentifier))
                .IsEqualTo(editTarget);
            await files.AssertOriginalsUnchangedAsync();
        }

        await using ViewerReviewDocumentFile file =
            await ViewerReviewDocumentFile.InspectAsync(files.DestinationPath, CancellationToken.None);
        UsdReviewDocument document = await file.ReadAsync(files.SourcePath, CancellationToken.None);
        await using ViewerPreparedDocument prepared = await ViewerPreparedDocument.OpenReviewAsync(
            document, files.SourcePath, file.Identity, recovery: false, CancellationToken.None);
        await prepared.RevalidateSourceAsync(CancellationToken.None);
        await using var reopened = new ViewerAuthoredEditController(prepared.Scheduler, prepared.SourceBinding);
        reopened.InitializeImportedReview(prepared);
        await AssertCatalogAsync(reopened, address, secondRecord);
        await Assert.That(reopened.UndoDepth).IsEqualTo(0);
        await Assert.That((await reopened.ReadDocumentAsync()).HasChanges).IsFalse();
        await Assert.That((await reopened.CaptureAsync([other])).Snapshot.Opinions[0].Value.AsDouble())
            .IsEqualTo(17d);
        await files.AssertOriginalsUnchangedAsync();
    }

    private static async Task AssertCatalogAsync(
        ViewerAuthoredEditController editor, UsdLayerEditAddress address, string expected)
    {
        UsdLayerAuthoredOpinion opinion = (await editor.CaptureAsync([address])).Snapshot.Opinions[0];
        await Assert.That(opinion.PropertyKind).IsEqualTo(UsdLayerPropertyKind.Attribute);
        await Assert.That(opinion.TypeName).IsEqualTo("string[]");
        await Assert.That(opinion.Variability).IsEqualTo(UsdLayerEditVariability.Uniform);
        await Assert.That(opinion.Custom).IsTrue();
        await Assert.That(opinion.Value.AsStringArray().SequenceEqual([expected])).IsTrue();
    }

    [Test]
    public async Task CatalogUsesExactReviewOwnershipAndRefusesConflictingOrMalformedLists()
    {
        RequireNativeJourney();
        using ViewerCameraBookmarkFixture files = await ViewerCameraBookmarkFixture.CreateAsync();
        await using UsdStageScheduler scheduler = UsdStageScheduler.OpenForReview(files.SourcePath);
        UsdReviewSourceBinding binding = await scheduler.InvokeAsync(
            static stage => stage.CaptureReviewSourceBinding());
        await using var editor = new ViewerAuthoredEditController(scheduler, binding);
        var catalog = new ViewerCameraBookmarkCatalog(editor);
        ViewerCameraBookmarkCapture empty = await catalog.ReadAsync();
        var viewport = new ViewportDimensions(800, 600);
        ViewerCameraBookmark first = ViewerCameraBookmark.CreateFree(
            Guid.NewGuid(), "Hero", empty.SourceStamp, 12.5, viewport,
            ViewerCameraNavigationState.CreateLegacy(false, 800f / 600));
        await Assert.That((await catalog.ApplyAsync(empty, [first], "Add saved view")).Outcome)
            .IsEqualTo(UsdLayerEditOutcome.Applied);
        ViewerCameraBookmarkCapture beforeRename = await catalog.ReadAsync();
        await Assert.That(beforeRename.Items.Single().Camera).IsEqualTo(first.Camera);
        await Assert.That((await catalog.ApplyAsync(beforeRename, [first.Rename("Side")], "Rename saved view")).Outcome)
            .IsEqualTo(UsdLayerEditOutcome.Applied);
        await Assert.That((await catalog.ApplyAsync(beforeRename, [], "Remove saved view")).Outcome)
            .IsEqualTo(UsdLayerEditOutcome.Conflict);
        await Assert.That((await catalog.ReadAsync()).Items.Single().Name).IsEqualTo("Side");
        await Assert.That((await editor.UndoAsync()).Outcome).IsEqualTo(UsdLayerEditOutcome.Applied);
        await Assert.That((await catalog.ReadAsync()).Items.Single().Name).IsEqualTo("Hero");

        await scheduler.EditAsync(static stage =>
        {
            using UsdLayer review = stage.GetUserReviewLayer();
            var address = new UsdLayerEditAddress(BookmarkProperty, UsdLayerEditField.Default);
            return review.CompareAndApply(review.CaptureAuthored([address]),
                [UsdLayerEdit.Set(address, UsdLayerEditValue.FromStringArray(["{\"v\":999}"]))]);
        }, UsdStageInvalidationKind.Property);
        await Assert.That(async () => await catalog.ReadAsync()).Throws<InvalidDataException>();
        await Assert.That(async () => await catalog.ApplyAsync(beforeRename, [], "Replace malformed saved views"))
            .Throws<InvalidDataException>();
        await Assert.That(await scheduler.InvokeAsync(static stage =>
        {
            using UsdLayer review = stage.GetUserReviewLayer();
            var address = new UsdLayerEditAddress(BookmarkProperty, UsdLayerEditField.Default);
            return review.CaptureAuthored([address]).Opinions[0].Value.AsStringArray().Single();
        })).IsEqualTo("{\"v\":999}");
        await files.AssertOriginalsUnchangedAsync();
    }

    private static void RequireNativeJourney()
    {
        if (!OperatingSystem.IsWindows() ||
            Environment.GetEnvironmentVariable("OPENUSD_VIEWER_CAMERA_BOOKMARKS_SMOKE") != "1")
        {
            Skip.Test("Run camera-bookmarks with the matched runtime on a Windows desktop.");
        }
    }
}
