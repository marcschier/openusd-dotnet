// Copyright (c) marcschier. Licensed under the MIT License.

using OpenUsd.Editing;

namespace OpenUsd.Viewer.Tests;

public sealed class ViewerReviewPersistenceTests
{
    [Test]
    public async Task InspectedReviewOpensOnTheExplicitVerifiedSourceWithFreshCleanHistory()
    {
        using ViewerPortableReviewFixture files = await ViewerPortableReviewFixture.CreateAsync();
        await using (UsdStageScheduler scheduler = UsdStageScheduler.OpenForReview(files.SourcePath))
        {
            UsdReviewSourceBinding binding = await scheduler.InvokeAsync(
                static stage => stage.CaptureReviewSourceBinding());
            await using var editor = new ViewerAuthoredEditController(scheduler, sourceBinding: binding);
            var address = new UsdLayerEditAddress("/Body.review:value", UsdLayerEditField.Default);
            await editor.ApplyAsync(await editor.CaptureAsync([address]),
                [UsdLayerEdit.Set(address, UsdLayerEditValue.FromDouble(47), "double")], "Value");
            await editor.PublishReviewSaveAsync(await editor.PrepareReviewSaveAsync(files.DestinationPath));
        }
        await using ViewerReviewDocumentFile file =
            await ViewerReviewDocumentFile.InspectAsync(files.DestinationPath, CancellationToken.None);
        await Assert.That(Path.GetFullPath(file.Info.SourceRootPath)).IsEqualTo(files.SourcePath);
        UsdReviewDocument document = await file.ReadAsync(files.SourcePath, CancellationToken.None);
        await using ViewerPreparedDocument prepared = await ViewerPreparedDocument.OpenReviewAsync(
            document, files.SourcePath, file.Identity, recovery: false, CancellationToken.None);
        await prepared.RevalidateSourceAsync(CancellationToken.None);
        await using var reopened = new ViewerAuthoredEditController(prepared.Scheduler, prepared.SourceBinding);
        reopened.InitializeImportedReview(prepared);

        await Assert.That(reopened.SavedReviewPath).IsEqualTo(files.DestinationPath);
        await Assert.That(reopened.UndoDepth).IsEqualTo(0);
        await Assert.That(reopened.RedoDepth).IsEqualTo(0);
        await Assert.That((await reopened.ReadDocumentAsync()).HasChanges).IsFalse();
        await Assert.That(await prepared.Scheduler.InvokeAsync(
            static stage => stage.GetPrim("/Body").GetDouble("review:value"))).IsEqualTo(47d);
        await files.AssertOriginalsUnchangedAsync();
    }

    [Test]
    public async Task ASecondSaveReplacesOnlyTheObservedNamedReviewAndAcknowledgesTheNewRevision()
    {
        using ViewerPortableReviewFixture files = await ViewerPortableReviewFixture.CreateAsync();
        await using UsdStageScheduler scheduler = UsdStageScheduler.OpenForReview(files.SourcePath);
        UsdReviewSourceBinding binding = await scheduler.InvokeAsync(
            static stage => stage.CaptureReviewSourceBinding());
        await using var editor = new ViewerAuthoredEditController(scheduler, sourceBinding: binding);
        var address = new UsdLayerEditAddress("/Body.review:value", UsdLayerEditField.Default);
        await editor.ApplyAsync(await editor.CaptureAsync([address]),
            [UsdLayerEdit.Set(address, UsdLayerEditValue.FromDouble(47), "double")], "First", Guid.NewGuid());
        ViewerReviewSaveResult first =
            await editor.PublishReviewSaveAsync(await editor.PrepareReviewSaveAsync(files.DestinationPath));
        await editor.ApplyAsync(await editor.CaptureAsync([address]),
            [UsdLayerEdit.Set(address, UsdLayerEditValue.FromDouble(55))], "Second", Guid.NewGuid());

        ViewerReviewSaveResult second =
            await editor.PublishReviewSaveAsync(await editor.PrepareReviewSaveAsync(files.DestinationPath));

        await Assert.That(second.Acknowledged).IsTrue();
        await Assert.That(first.Document.HasSamePayload(second.Document)).IsFalse();
        await Assert.That(UsdReviewDocument.Read(
            await File.ReadAllBytesAsync(files.DestinationPath), files.SourcePath).HasSamePayload(second.Document))
            .IsTrue();
        await Assert.That((await editor.ReadDocumentAsync()).HasChanges).IsFalse();
        await Assert.That(editor.UndoDepth).IsEqualTo(2);
        await files.AssertOriginalsUnchangedAsync();
    }

    [Test]
    public async Task StaleSaveAsReceiptDoesNotAcknowledgeEditsThatWereUndoneAfterCapture()
    {
        using ViewerPortableReviewFixture files = await ViewerPortableReviewFixture.CreateAsync();
        await using UsdStageScheduler scheduler = UsdStageScheduler.OpenForReview(files.SourcePath);
        UsdReviewSourceBinding binding = await scheduler.InvokeAsync(
            static stage => stage.CaptureReviewSourceBinding());
        await using var editor = new ViewerAuthoredEditController(scheduler, sourceBinding: binding);
        var address = new UsdLayerEditAddress("/Body.review:value", UsdLayerEditField.Default);
        await editor.ApplyAsync(await editor.CaptureAsync([address]),
            [UsdLayerEdit.Set(address, UsdLayerEditValue.FromDouble(47), "double")], "First value", Guid.NewGuid());
        await editor.PublishReviewSaveAsync(await editor.PrepareReviewSaveAsync(files.DestinationPath));
        await editor.ApplyAsync(await editor.CaptureAsync([address]),
            [UsdLayerEdit.Set(address, UsdLayerEditValue.FromDouble(55))], "Second value", Guid.NewGuid());
        string secondPath = Path.Combine(files.Root, "second.urd");
        ViewerReviewSaveCapture captured = await editor.PrepareReviewSaveAsync(secondPath);
        await Assert.That((await editor.UndoAsync()).After!.Opinions[0].Value.AsDouble()).IsEqualTo(47d);

        ViewerReviewSaveResult saved = await editor.PublishReviewSaveAsync(captured);

        await Assert.That(saved.Acknowledged).IsFalse();
        await Assert.That(saved.Message).Contains("later edits");
        await Assert.That(editor.SavedReviewPath).IsEqualTo(secondPath);
        await Assert.That((await editor.ReadDocumentAsync()).HasChanges).IsTrue();
        await Assert.That((await editor.CaptureAsync([address])).Snapshot.Opinions[0].Value.AsDouble())
            .IsEqualTo(47d);
        await Assert.That(editor.RedoDepth).IsEqualTo(1);
        await Assert.That(UsdReviewDocument.Read(
            await File.ReadAllBytesAsync(secondPath), files.SourcePath).HasSamePayload(captured.Document)).IsTrue();
        await files.AssertOriginalsUnchangedAsync();
    }

    [Test]
    public async Task AFileAppearingAfterCaptureIsNotOverwrittenOrAcknowledged()
    {
        using ViewerPortableReviewFixture files = await ViewerPortableReviewFixture.CreateAsync();
        await using UsdStageScheduler scheduler = UsdStageScheduler.OpenForReview(files.SourcePath);
        UsdReviewSourceBinding binding = await scheduler.InvokeAsync(
            static stage => stage.CaptureReviewSourceBinding());
        await using var editor = new ViewerAuthoredEditController(scheduler, sourceBinding: binding);
        var address = new UsdLayerEditAddress("/Body.review:value", UsdLayerEditField.Default);
        await editor.ApplyAsync(await editor.CaptureAsync([address]),
            [UsdLayerEdit.Set(address, UsdLayerEditValue.FromDouble(47), "double")], "Value");
        ViewerReviewSaveCapture captured = await editor.PrepareReviewSaveAsync(files.DestinationPath);
        await File.WriteAllTextAsync(files.DestinationPath, "foreign file");

        await Assert.That(async () => await editor.PublishReviewSaveAsync(captured)).Throws<IOException>();

        await Assert.That(await File.ReadAllTextAsync(files.DestinationPath)).IsEqualTo("foreign file");
        await Assert.That(editor.SavedReviewPath).IsNull();
        await Assert.That((await editor.ReadDocumentAsync()).HasChanges).IsTrue();
        await Assert.That(Directory.GetFiles(files.Root, "*.tmp")).IsEmpty();
        await files.AssertOriginalsUnchangedAsync();
    }

    [Test]
    public async Task VerifiedReviewSavePublishesBeforeAcknowledgingAndReopensTheOriginalSource()
    {
        using ViewerPortableReviewFixture files = await ViewerPortableReviewFixture.CreateAsync();
        await using (UsdStageScheduler scheduler = UsdStageScheduler.OpenForReview(files.SourcePath))
        {
            UsdReviewSourceBinding binding = await scheduler.InvokeAsync(
                static stage => stage.CaptureReviewSourceBinding());
            await using var editor = new ViewerAuthoredEditController(scheduler, sourceBinding: binding);
            var address = new UsdLayerEditAddress("/Body.review:value", UsdLayerEditField.Default);
            ViewerAuthoredEditCapture before = await editor.CaptureAsync([address]);
            await editor.ApplyAsync(before,
                [UsdLayerEdit.Set(address, UsdLayerEditValue.FromDouble(47), "double")], "Value");
            await editor.UndoAsync();
            await editor.RedoAsync();

            ViewerReviewSaveCapture capture = await editor.PrepareReviewSaveAsync(files.DestinationPath);
            await Assert.That(File.Exists(files.DestinationPath)).IsFalse();
            await Assert.That((await editor.ReadDocumentAsync()).HasChanges).IsTrue();
            ViewerReviewSaveResult saved = await editor.PublishReviewSaveAsync(capture);
            await Assert.That(saved.Acknowledged).IsTrue();
            await Assert.That((await editor.ReadDocumentAsync()).HasChanges).IsFalse();
            await Assert.That(editor.UndoDepth).IsEqualTo(1);
            await Assert.That(saved.Destination.FullPath).IsEqualTo(files.DestinationPath);
            await Assert.That(Path.GetFullPath(saved.Document.SourceRootPath)).IsEqualTo(files.SourcePath);
        }

        UsdReviewDocument read = UsdReviewDocument.Read(
            await File.ReadAllBytesAsync(files.DestinationPath), files.SourcePath);
        await using (UsdStageScheduler reopened = UsdStageScheduler.OpenForReview(files.SourcePath))
        {
            await reopened.InvokeAsync(stage =>
            {
                UsdReviewDocumentImportResult imported =
                    stage.ImportReviewDocument(read, stage.CaptureReviewSourceBinding());
                if (imported.Outcome != UsdLayerEditOutcome.Applied)
                {
                    throw new InvalidOperationException(imported.Diagnostic);
                }
            });
            await Assert.That(await reopened.InvokeAsync(
                static stage => stage.GetPrim("/Body").GetDouble("review:value"))).IsEqualTo(47d);
            await Assert.That(await reopened.InvokeAsync(static stage => stage.HasPrim("/FromSublayer"))).IsTrue();
            await Assert.That(await reopened.InvokeAsync(static stage => stage.GetDefaultPrim().Path))
                .IsEqualTo("/Body");
            await Assert.That(await reopened.InvokeAsync(static stage =>
            {
                using UsdLayer root = stage.GetRootLayer();
                return root.GetMetadataString("marker");
            })).IsEqualTo("original source");
        }
        await files.AssertOriginalsUnchangedAsync();
    }

    [Test]
    public async Task TimeSampleOpinionSurvivesPublishAndVerifiedReopen()
    {
        using ViewerPortableReviewFixture files = await ViewerPortableReviewFixture.CreateAsync();
        UsdLayerEditAddress defaultAddress = new("/Body.review:value", UsdLayerEditField.Default);
        UsdLayerEditAddress sampleAddress = new("/Body.review:value", UsdLayerEditField.TimeSample, 2);

        await using (UsdStageScheduler scheduler = UsdStageScheduler.OpenForReview(files.SourcePath))
        {
            UsdReviewSourceBinding binding = await scheduler.InvokeAsync(
                static stage => stage.CaptureReviewSourceBinding());
            await using var editor = new ViewerAuthoredEditController(scheduler, sourceBinding: binding);
            ViewerAuthoredEditCapture capture = await editor.CaptureAsync([defaultAddress, sampleAddress]);

            ViewerAuthoredEditResult applied = await editor.ApplyAsync(capture,
            [
                UsdLayerEdit.Set(defaultAddress, UsdLayerEditValue.FromDouble(47), "double", creationCustom: true),
                UsdLayerEdit.Set(sampleAddress, UsdLayerEditValue.FromDouble(12), "double", creationCustom: true)
            ], "Set default and sampled values");

            await Assert.That(applied.Outcome).IsEqualTo(UsdLayerEditOutcome.Applied);
            ViewerReviewSaveResult saved = await editor.PublishReviewSaveAsync(
                await editor.PrepareReviewSaveAsync(files.DestinationPath));
            await Assert.That(saved.Acknowledged).IsTrue();
            await Assert.That((await editor.ReadDocumentAsync()).HasChanges).IsFalse();
        }

        await using ViewerReviewDocumentFile file =
            await ViewerReviewDocumentFile.InspectAsync(files.DestinationPath, CancellationToken.None);
        UsdReviewDocument document = await file.ReadAsync(files.SourcePath, CancellationToken.None);
        await using ViewerPreparedDocument prepared = await ViewerPreparedDocument.OpenReviewAsync(
            document, files.SourcePath, file.Identity, recovery: false, CancellationToken.None);
        await using var reopened = new ViewerAuthoredEditController(prepared.Scheduler, prepared.SourceBinding);
        reopened.InitializeImportedReview(prepared);

        ViewerAuthoredEditCapture reopenedCapture =
            await reopened.CaptureAsync([defaultAddress, sampleAddress]);
        await Assert.That(reopenedCapture.Snapshot.Opinions[0].Value.AsDouble()).IsEqualTo(47d);
        await Assert.That(reopenedCapture.Snapshot.Opinions[1].Address).IsEqualTo(sampleAddress);
        await Assert.That(reopenedCapture.Snapshot.Opinions[1].Value.AsDouble()).IsEqualTo(12d);
        await Assert.That(await prepared.Scheduler.InvokeAsync(
            static stage => stage.GetPrim("/Body").GetDouble("review:value"))).IsEqualTo(47d);
        await Assert.That(await prepared.Scheduler.InvokeAsync(
            static stage => stage.GetPrim("/Body").GetDouble("review:value", 2))).IsEqualTo(12d);
        await files.AssertOriginalsUnchangedAsync();
    }

    [Test]
    public async Task AFileAppearingAfterCaptureCanBeRemovedAndFreshCaptureRetryReopensTheExactOpinion()
    {
        using ViewerPortableReviewFixture files = await ViewerPortableReviewFixture.CreateAsync();
        var address = new UsdLayerEditAddress("/Body.review:value", UsdLayerEditField.Default);
        const string foreignContents = "foreign file";

        await using (UsdStageScheduler scheduler = UsdStageScheduler.OpenForReview(files.SourcePath))
        {
            UsdReviewSourceBinding binding = await scheduler.InvokeAsync(
                static stage => stage.CaptureReviewSourceBinding());
            await using var editor = new ViewerAuthoredEditController(scheduler, sourceBinding: binding);
            await editor.ApplyAsync(await editor.CaptureAsync([address]),
                [UsdLayerEdit.Set(address, UsdLayerEditValue.FromDouble(47), "double")], "Value");
            await Assert.That(File.Exists(files.DestinationPath)).IsFalse();
            ViewerReviewSaveCapture staleCapture = await editor.PrepareReviewSaveAsync(files.DestinationPath);
            await File.WriteAllTextAsync(files.DestinationPath, foreignContents);

            await Assert.That(async () => await editor.PublishReviewSaveAsync(staleCapture)).Throws<IOException>();

            await Assert.That(await File.ReadAllTextAsync(files.DestinationPath)).IsEqualTo(foreignContents);
            await Assert.That(editor.SavedReviewPath).IsNull();
            await Assert.That((await editor.ReadDocumentAsync()).HasChanges).IsTrue();
            await files.AssertOriginalsUnchangedAsync();

            File.Delete(files.DestinationPath);
            await Assert.That(File.Exists(files.DestinationPath)).IsFalse();

            ViewerReviewSaveCapture retryCapture = await editor.PrepareReviewSaveAsync(files.DestinationPath);
            ViewerReviewSaveResult saved = await editor.PublishReviewSaveAsync(retryCapture);

            await Assert.That(saved.Acknowledged).IsTrue();
            await Assert.That(saved.Destination.FullPath).IsEqualTo(files.DestinationPath);
            await Assert.That(editor.SavedReviewPath).IsEqualTo(files.DestinationPath);
            await Assert.That((await editor.ReadDocumentAsync()).HasChanges).IsFalse();
            await files.AssertOriginalsUnchangedAsync();
        }

        await using ViewerReviewDocumentFile file =
            await ViewerReviewDocumentFile.InspectAsync(files.DestinationPath, CancellationToken.None);
        await Assert.That(Path.GetFullPath(file.Info.SourceRootPath)).IsEqualTo(files.SourcePath);
        UsdReviewDocument document = await file.ReadAsync(files.SourcePath, CancellationToken.None);
        await using ViewerPreparedDocument prepared = await ViewerPreparedDocument.OpenReviewAsync(
            document, files.SourcePath, file.Identity, recovery: false, CancellationToken.None);
        await prepared.RevalidateSourceAsync(CancellationToken.None);
        await using var reopened = new ViewerAuthoredEditController(prepared.Scheduler, prepared.SourceBinding);
        reopened.InitializeImportedReview(prepared);

        await Assert.That(reopened.SavedReviewPath).IsEqualTo(files.DestinationPath);
        await Assert.That((await reopened.ReadDocumentAsync()).HasChanges).IsFalse();
        ViewerAuthoredEditCapture reopenedCapture = await reopened.CaptureAsync([address]);
        UsdLayerAuthoredOpinion opinion = reopenedCapture.Snapshot.Opinions[0];
        await Assert.That(opinion.Address).IsEqualTo(address);
        await Assert.That(opinion.PropertyKind).IsEqualTo(UsdLayerPropertyKind.Attribute);
        await Assert.That(opinion.Value.Kind).IsEqualTo(UsdLayerEditValueKind.Double);
        await Assert.That(opinion.Value.AsDouble()).IsEqualTo(47d);
        await Assert.That(await prepared.Scheduler.InvokeAsync(
            static stage => stage.GetPrim("/Body").GetDouble("review:value"))).IsEqualTo(47d);
        await files.AssertOriginalsUnchangedAsync();
    }
}
