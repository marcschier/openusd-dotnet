// Copyright (c) marcschier. Licensed under the MIT License.

using OpenUsd.Editing;
using OpenUsd.Interop;
using OpenUsd.Rendering;

namespace OpenUsd.Viewer.Tests;

public sealed partial class ViewerCameraBookmarkNativeTests
{
    [Test]
    public async Task ReservedNamespaceAndWrongDeclarationsCannotBeClaimedAsSavedViews()
    {
        RequireNativeJourney();
        using ViewerCameraBookmarkFixture files = await ViewerCameraBookmarkFixture.CreateAsync();
        string occupied = Path.Combine(files.Root, "occupied.usda");
        const string source = """
            #usda 1.0
            (subLayers = [@source.usda@])
            def Scope "__OpenUsdViewerReview" {}
            """;
        await File.WriteAllTextAsync(occupied, source);
        await using (UsdStageScheduler scheduler = UsdStageScheduler.OpenForReview(occupied))
        {
            UsdReviewSourceBinding binding = await scheduler.InvokeAsync(
                static stage => stage.CaptureReviewSourceBinding());
            await using var editor = new ViewerAuthoredEditController(scheduler, binding);
            await Assert.That(async () => await new ViewerCameraBookmarkCatalog(editor).ReadAsync())
                .Throws<InvalidDataException>();
            await Assert.That(editor.UndoDepth).IsEqualTo(0);
            await Assert.That((await editor.ReadDocumentAsync()).HasChanges).IsFalse();
        }
        await Assert.That(await File.ReadAllTextAsync(occupied)).IsEqualTo(source);
        await using (UsdStageScheduler scheduler = UsdStageScheduler.OpenForReview(files.SourcePath))
        {
            UsdReviewSourceBinding binding = await scheduler.InvokeAsync(
                static stage => stage.CaptureReviewSourceBinding());
            await scheduler.EditAsync(static stage =>
            {
                using UsdLayer review = stage.GetUserReviewLayer();
                return review.CompareAndApply(review.CaptureAuthored([ViewerCameraBookmarkCatalog.Address]),
                    [UsdLayerEdit.Set(ViewerCameraBookmarkCatalog.Address,
                        UsdLayerEditValue.FromStringArray([]), "string[]")]);
            }, UsdStageInvalidationKind.Property);
            await using var editor = new ViewerAuthoredEditController(scheduler, binding);
            await Assert.That(async () => await new ViewerCameraBookmarkCatalog(editor).ReadAsync())
                .Throws<InvalidDataException>();
            await Assert.That(editor.UndoDepth).IsEqualTo(0);
        }
        await files.AssertOriginalsUnchangedAsync();
    }

    [Test]
    public async Task SessionOnlyCatalogDoesNotPretendToOfferPersistence()
    {
        RequireNativeJourney();
        using ViewerCameraBookmarkFixture files = await ViewerCameraBookmarkFixture.CreateAsync();
        await using UsdStageScheduler scheduler = UsdStageScheduler.Open(files.SourcePath);
        await using var editor = new ViewerAuthoredEditController(scheduler);
        await Assert.That(editor.CanSaveReview).IsFalse();
        await Assert.That(async () => await new ViewerCameraBookmarkCatalog(editor).ReadAsync())
            .Throws<NotSupportedException>();
        await Assert.That(editor.UndoDepth).IsEqualTo(0);
        await Assert.That((await editor.ReadDocumentAsync()).HasChanges).IsFalse();
        await files.AssertOriginalsUnchangedAsync();
    }

    [Test]
    public async Task SaveCancellationAndLateCatalogEditsNeverAcknowledgeTheWrongSavedState()
    {
        RequireNativeJourney();
        using ViewerCameraBookmarkFixture files = await ViewerCameraBookmarkFixture.CreateAsync();
        await using UsdStageScheduler scheduler = UsdStageScheduler.OpenForReview(files.SourcePath);
        UsdReviewSourceBinding binding = await scheduler.InvokeAsync(
            static stage => stage.CaptureReviewSourceBinding());
        await using var editor = new ViewerAuthoredEditController(scheduler, binding);
        var catalog = new ViewerCameraBookmarkCatalog(editor);
        ViewerCameraBookmarkCapture empty = await catalog.ReadAsync();
        ViewerCameraBookmark first = ViewerCameraBookmark.CreateFree(
            Guid.NewGuid(), "Hero", empty.SourceStamp, 2.25, new ViewportDimensions(800, 600),
            ViewerCameraNavigationState.CreateLegacy(false, 800f / 600));
        await catalog.ApplyAsync(empty, [first], "Add saved view");
        ViewerReviewSaveCapture capture = await editor.PrepareReviewSaveAsync(files.DestinationPath);
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        await Assert.That(async () => await editor.PublishReviewSaveAsync(capture, cancelled.Token))
            .Throws<OperationCanceledException>();
        await Assert.That(File.Exists(files.DestinationPath)).IsFalse();
        await Assert.That((await editor.ReadDocumentAsync()).HasChanges).IsTrue();
        await catalog.ApplyAsync(await catalog.ReadAsync(), [first.Rename("Later")], "Rename saved view");
        ViewerReviewSaveResult saved = await editor.PublishReviewSaveAsync(capture);
        await Assert.That(saved.Acknowledged).IsFalse();
        await Assert.That((await editor.ReadDocumentAsync()).HasChanges).IsTrue();
        await Assert.That((await catalog.ReadAsync()).Items.Single().Name).IsEqualTo("Later");
        await Assert.That(editor.UndoDepth).IsEqualTo(2);
        UsdReviewDocument document = UsdReviewDocument.Read(
            await File.ReadAllBytesAsync(files.DestinationPath), files.SourcePath);
        await using ViewerPreparedDocument reopened = await ViewerPreparedDocument.OpenReviewAsync(
            document, files.SourcePath, null, recovery: false, CancellationToken.None);
        await using var freshEditor = new ViewerAuthoredEditController(reopened.Scheduler, reopened.SourceBinding);
        await Assert.That((await new ViewerCameraBookmarkCatalog(freshEditor).ReadAsync()).Items.Single().Name)
            .IsEqualTo("Hero");
        await files.AssertOriginalsUnchangedAsync();
    }

    [Test]
    public async Task ChangedVerifiedSourceRefusesCatalogMutationWithoutRebinding()
    {
        RequireNativeJourney();
        using ViewerCameraBookmarkFixture files = await ViewerCameraBookmarkFixture.CreateAsync();
        await using UsdStageScheduler scheduler = UsdStageScheduler.OpenForReview(files.SourcePath);
        UsdReviewSourceBinding binding = await scheduler.InvokeAsync(
            static stage => stage.CaptureReviewSourceBinding());
        await using var editor = new ViewerAuthoredEditController(scheduler, binding);
        var catalog = new ViewerCameraBookmarkCatalog(editor);
        ViewerCameraBookmarkCapture empty = await catalog.ReadAsync();
        ViewerCameraBookmark bookmark = ViewerCameraBookmark.CreateFree(
            Guid.NewGuid(), "Hero", empty.SourceStamp, 1, new ViewportDimensions(800, 600),
            ViewerCameraNavigationState.CreateLegacy(false, 800f / 600));
        await catalog.ApplyAsync(empty, [bookmark], "Add saved view");
        ViewerCameraBookmarkCapture before = await catalog.ReadAsync();
        await scheduler.EditAsync(static stage =>
        {
            using UsdLayer source = stage.GetRootLayer();
            source.SetMetadata("outsideWriter", true);
        }, UsdStageInvalidationKind.Property);
        await Assert.That(async () => await catalog.ApplyAsync(before, [], "Remove saved view"))
            .Throws<OpenUsdNativeException>();
        await Assert.That(editor.UndoDepth).IsEqualTo(1);
        await Assert.That(await scheduler.InvokeAsync(static stage =>
        {
            using UsdLayer review = stage.GetUserReviewLayer();
            return review.CaptureAuthored([ViewerCameraBookmarkCatalog.Address])
                .Opinions[0].Value.AsStringArray().Length;
        })).IsEqualTo(1);
        await files.AssertOriginalsUnchangedAsync();
    }

    [Test]
    public async Task SavedViewPropertySurvivesRecoveryAndOrdinaryDeltaWithoutAcknowledgingASave()
    {
        RequireNativeJourney();
        using ViewerCameraBookmarkFixture files = await ViewerCameraBookmarkFixture.CreateAsync();
        await using UsdStageScheduler scheduler = UsdStageScheduler.OpenForReview(files.SourcePath);
        UsdReviewSourceBinding binding = await scheduler.InvokeAsync(
            static stage => stage.CaptureReviewSourceBinding());
        await using var editor = new ViewerAuthoredEditController(scheduler, binding);
        var catalog = new ViewerCameraBookmarkCatalog(editor);
        ViewerCameraBookmarkCapture empty = await catalog.ReadAsync();
        ViewerCameraBookmark bookmark = ViewerCameraBookmark.CreateFree(
            Guid.NewGuid(), "Recover", empty.SourceStamp, 1.25, new ViewportDimensions(800, 600),
            ViewerCameraNavigationState.CreateLegacy(false, 800f / 600));
        await catalog.ApplyAsync(empty, [bookmark], "Add saved view");
        UsdReviewDocument recovery = await editor.CaptureRecoveryDocumentAsync(
            Path.Combine(files.Root, "recovery.urd"), retiring: false, CancellationToken.None);
        await using ViewerPreparedDocument recovered = await ViewerPreparedDocument.OpenReviewAsync(
            recovery, files.SourcePath, null, recovery: true, CancellationToken.None);
        await using var recoveredEditor =
            new ViewerAuthoredEditController(recovered.Scheduler, recovered.SourceBinding);
        recoveredEditor.InitializeImportedReview(recovered);
        ViewerCameraBookmark restored = (await new ViewerCameraBookmarkCatalog(recoveredEditor).ReadAsync())
            .Items.Single();
        await Assert.That(restored.HasSameContent(bookmark)).IsTrue();
        await Assert.That((await recoveredEditor.ReadDocumentAsync()).HasChanges).IsTrue();
        string deltaPath = Path.Combine(files.Root, "views-delta.usda");
        await ViewerReviewLayerExporter.ExportNewAsync(
            await editor.CaptureReviewCheckpointAsync(), files.SourcePath, deltaPath, CancellationToken.None);
        await Assert.That((await editor.ReadDocumentAsync()).HasChanges).IsTrue();
        await Assert.That(editor.SavedReviewPath).IsNull();
        await using UsdStageScheduler delta = UsdStageScheduler.Open(deltaPath);
        string[] records = await delta.InvokeAsync(static stage =>
        {
            using UsdLayer root = stage.GetRootLayer();
            return root.CaptureAuthored([ViewerCameraBookmarkCatalog.Address])
                .Opinions[0].Value.AsStringArray();
        });
        ViewerCameraBookmark exported = ViewerCameraBookmarkCodec.DecodeCatalog(records, empty.SourceStamp).Single();
        await Assert.That(exported.HasSameContent(bookmark)).IsTrue();
        await files.AssertOriginalsUnchangedAsync();
    }
}
