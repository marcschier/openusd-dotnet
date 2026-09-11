// Copyright (c) marcschier. Licensed under the MIT License.

using OpenUsd.Editing;

namespace OpenUsd.Viewer.Tests;

public sealed class ViewerDocumentSessionTests
{
    [Test]
    public async Task PreparingAnEmptyReviewEditorDoesNotReportAnUnsavedEdit()
    {
        string root = Path.Combine(AppContext.BaseDirectory, "document-session", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string path = Path.Combine(root, "source.usda");
        await File.WriteAllTextAsync(path, "#usda 1.0\ndef Xform \"Body\" {}\n");
        try
        {
            await using UsdStageScheduler scheduler = ViewerNativeTestStages.OpenSchedulerOrSkip(path);
            await using var editor = new ViewerAuthoredEditController(scheduler);
            await editor.ReadDocumentAsync();
            await editor.CaptureAsync([new("/Body.review:value", UsdLayerEditField.Default)]);
            ViewerDocumentObservation prepared = await editor.ReadDocumentAsync();

            await Assert.That(prepared.State.Review!.HasChanges).IsFalse();
            await Assert.That(prepared.OtherChangedLayers).IsEmpty();
            await Assert.That(prepared.HasChanges).IsFalse();
            await Assert.That(editor.CanUndo).IsFalse();
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    public async Task DeltaPublicationRefusesSourceForeignFilesMissingDirectoriesAndCancellationWithoutPartialOutput()
    {
        string root = Path.Combine(AppContext.BaseDirectory, "document-session", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string path = Path.Combine(root, "source.usda");
        string foreign = Path.Combine(root, "foreign.usda");
        string cancelled = Path.Combine(root, "cancelled.usda");
        const string source = "#usda 1.0\ndef Xform \"Body\" {}\n";
        await File.WriteAllTextAsync(path, source);
        await File.WriteAllTextAsync(foreign, "foreign bytes");
        try
        {
            await using UsdStageScheduler scheduler = ViewerNativeTestStages.OpenSchedulerOrSkip(path);
            await using var editor = new ViewerAuthoredEditController(scheduler);
            await editor.CaptureAsync([new("/Body.review:value", UsdLayerEditField.Default)]);
            UsdLayerCheckpoint checkpoint = await editor.CaptureReviewCheckpointAsync();

            await Assert.That(() => ViewerReviewLayerExporter.ExportNewAsync(
                checkpoint, path, path, CancellationToken.None)).Throws<ArgumentException>();
            await Assert.That(() => ViewerReviewLayerExporter.ExportNewAsync(
                checkpoint, path, foreign, CancellationToken.None)).Throws<IOException>();
            await Assert.That(() => ViewerReviewLayerExporter.ExportNewAsync(
                checkpoint, path, Path.Combine(root, "missing", "review.usda"), CancellationToken.None))
                .Throws<DirectoryNotFoundException>();
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            await Assert.That(() => ViewerReviewLayerExporter.ExportNewAsync(
                checkpoint, path, cancelled, cancellation.Token)).Throws<OperationCanceledException>();

            await Assert.That(await File.ReadAllTextAsync(path)).IsEqualTo(source);
            await Assert.That(await File.ReadAllTextAsync(foreign)).IsEqualTo("foreign bytes");
            await Assert.That(File.Exists(cancelled)).IsFalse();
            await Assert.That(Directory.GetFiles(root, ".review-*.tmp")).IsEmpty();
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    public async Task ExplicitDeltaExportReopensExactReviewOpinionsWithoutSavingSourceOrAcknowledgingTheDocument()
    {
        string root = Path.Combine(AppContext.BaseDirectory, "document-session", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string path = Path.Combine(root, "source.usda");
        string destination = Path.Combine(root, "review.usda");
        const string source = "#usda 1.0\ndef Xform \"Body\" {}\n";
        await File.WriteAllTextAsync(path, source);
        try
        {
            await using UsdStageScheduler scheduler = ViewerNativeTestStages.OpenSchedulerOrSkip(path);
            await using var editor = new ViewerAuthoredEditController(scheduler);
            var address = new UsdLayerEditAddress("/Body.review:value", UsdLayerEditField.Default);
            ViewerAuthoredEditCapture before = await editor.CaptureAsync([address]);
            await editor.ApplyAsync(before,
                [UsdLayerEdit.Set(address, UsdLayerEditValue.FromDouble(42), "double")], "Value");
            UsdLayerCheckpoint checkpoint = await editor.CaptureReviewCheckpointAsync();
            await ViewerReviewLayerExporter.ExportNewAsync(checkpoint, path, destination, CancellationToken.None);

            await using UsdStageScheduler exported = ViewerNativeTestStages.OpenSchedulerOrSkip(destination);
            await Assert.That(await exported.InvokeAsync(static stage => stage.GetPrim("/Body")
                .GetDouble("review:value"))).IsEqualTo(42d);
            await Assert.That(await File.ReadAllTextAsync(path)).IsEqualTo(source);
            await Assert.That((await editor.ReadDocumentAsync()).State.Review!.HasChanges).IsTrue();
            await Assert.That(editor.UndoDepth).IsEqualTo(1);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    public async Task TransitionAdmissionRejectsStaleDecisionsAndBlocksEditsUntilCancellationResumesTheDocument()
    {
        string root = Path.Combine(AppContext.BaseDirectory, "document-session", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string path = Path.Combine(root, "source.usda");
        await File.WriteAllTextAsync(path, "#usda 1.0\ndef Xform \"Body\" {}\n");
        try
        {
            await using UsdStageScheduler scheduler = ViewerNativeTestStages.OpenSchedulerOrSkip(path);
            await using var editor = new ViewerAuthoredEditController(scheduler);
            var address = new UsdLayerEditAddress("/Body.review:value", UsdLayerEditField.Default);
            ViewerAuthoredEditCapture capture = await editor.CaptureAsync([address]);
            ViewerDocumentObservation stale = await editor.ReadDocumentAsync();
            await editor.ApplyAsync(capture,
                [UsdLayerEdit.Set(address, UsdLayerEditValue.FromDouble(4), "double")], "Value");
            await Assert.That(await editor.TrySuspendAsync(stale)).IsFalse();
            await Assert.That(editor.CanUndo).IsTrue();

            ViewerDocumentObservation current = await editor.ReadDocumentAsync();
            await Assert.That(await editor.TrySuspendAsync(current)).IsTrue();
            await Assert.That(editor.CanUndo).IsFalse();
            ViewerAuthoredEditResult blocked = await editor.UndoAsync();
            await Assert.That(blocked.Outcome).IsEqualTo(UsdLayerEditOutcome.NotEditable);
            await Assert.That(editor.UndoDepth).IsEqualTo(1);
            editor.Resume();
            await Assert.That((await editor.UndoAsync()).Outcome).IsEqualTo(UsdLayerEditOutcome.Applied);
            await Assert.That((await editor.ReadDocumentAsync()).State.Source.HasChanges).IsFalse();
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    public async Task InspectingDirtinessDoesNotCreateAReviewLayerAndEditsIdentifyTheExactDirtyTarget()
    {
        string root = Path.Combine(AppContext.BaseDirectory, "document-session", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string path = Path.Combine(root, "source.usda");
        const string source = "#usda 1.0\ndef Xform \"Body\" {}\n";
        await File.WriteAllTextAsync(path, source);
        try
        {
            await using UsdStageScheduler scheduler = ViewerNativeTestStages.OpenSchedulerOrSkip(path);
            await using var editor = new ViewerAuthoredEditController(scheduler);
            string[] originalLayers = await scheduler.InvokeAsync(static stage => stage.GetLayerStackIdentifiers());
            ViewerDocumentObservation initial = await editor.ReadDocumentAsync();
            await Assert.That(initial.State.Review).IsNull();
            await Assert.That(initial.State.ReviewAbsenceConfirmed).IsTrue();
            await Assert.That(initial.HasChanges).IsFalse();
            string[] inspectedLayers = await scheduler.InvokeAsync(static stage => stage.GetLayerStackIdentifiers());
            await Assert.That(inspectedLayers.SequenceEqual(originalLayers)).IsTrue();

            var address = new UsdLayerEditAddress("/Body.review:value", UsdLayerEditField.Default);
            ViewerAuthoredEditCapture captured = await editor.CaptureAsync([address]);
            await editor.ApplyAsync(captured,
                [UsdLayerEdit.Set(address, UsdLayerEditValue.FromDouble(3), "double")], "Review value");
            ViewerDocumentObservation changed = await editor.ReadDocumentAsync();

            await Assert.That(changed.State.Review!.Identifier).IsEqualTo(captured.LayerIdentifier);
            await Assert.That(changed.State.Review.HasChanges).IsTrue();
            await Assert.That(changed.State.Source.HasChanges).IsFalse();
            await Assert.That(changed.HasChanges).IsTrue();
            await Assert.That(initial.Matches(changed)).IsFalse();
            await Assert.That(await File.ReadAllTextAsync(path)).IsEqualTo(source);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
