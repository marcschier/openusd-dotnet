// Copyright (c) marcschier. Licensed under the MIT License.

using System.Runtime.Versioning;
using OpenUsd.Editing;

namespace OpenUsd.Viewer.Tests;

[SupportedOSPlatform("windows")]
public sealed class ViewerReviewPublicationTests
{
    [Test]
    public async Task SourceDependencyAndAncestorGuardsRemainHeldUntilPublicationIsDisposed()
    {
        using ViewerPortableReviewFixture files = await ViewerPortableReviewFixture.CreateAsync();
        await using UsdStageScheduler scheduler = UsdStageScheduler.OpenForReview(files.SourcePath);
        UsdReviewSourceBinding binding = await scheduler.InvokeAsync(
            static stage => stage.CaptureReviewSourceBinding());
        await using var editor = new ViewerAuthoredEditController(scheduler, sourceBinding: binding);
        ViewerReviewSaveCapture capture = await editor.PrepareReviewSaveAsync(files.DestinationPath);
        string alias = Path.Combine(files.Root, "source-alias.usda");
        ViewerPortableReviewFixture.CreateHardLink(alias, files.SourcePath);
        string moved = files.Root + "-moved";
        await using (ViewerReviewDocumentPublication publication =
            await ViewerReviewDocumentPublication.StageAsync(capture, CancellationToken.None))
        {
            await Assert.That(async () => await File.WriteAllTextAsync(files.SourcePath, "changed"))
                .Throws<IOException>();
            await Assert.That(async () => await File.WriteAllTextAsync(alias, "changed"))
                .Throws<IOException>();
            await Assert.That(async () => await File.WriteAllTextAsync(
                Path.Combine(files.Root, "texture.png"), "changed")).Throws<IOException>();
            await Assert.That(() => Directory.Move(files.Root, moved)).Throws<IOException>();
            await files.AssertOriginalsUnchangedAsync();
            await Assert.That(File.Exists(files.DestinationPath)).IsFalse();
        }
        Directory.Move(files.Root, moved);
        Directory.Move(moved, files.Root);
        await Assert.That(Directory.GetFiles(files.Root, "*.tmp")).IsEmpty();
        await files.AssertOriginalsUnchangedAsync();
    }

    [Test]
    [Arguments("source.usda")]
    [Arguments("texture.png")]
    public async Task ASourceHardLinkIntroducedAfterSelectionIsRefusedAtPublication(string protectedName)
    {
        using ViewerPortableReviewFixture files = await ViewerPortableReviewFixture.CreateAsync();
        await using UsdStageScheduler scheduler = UsdStageScheduler.OpenForReview(files.SourcePath);
        UsdReviewSourceBinding binding = await scheduler.InvokeAsync(
            static stage => stage.CaptureReviewSourceBinding());
        await using var editor = new ViewerAuthoredEditController(scheduler, sourceBinding: binding);
        ViewerReviewSaveCapture capture = await editor.PrepareReviewSaveAsync(files.DestinationPath);
        string protectedPath = Path.Combine(files.Root, protectedName);
        byte[] expected = await File.ReadAllBytesAsync(protectedPath);
        ViewerPortableReviewFixture.CreateHardLink(files.DestinationPath, protectedPath);

        await Assert.That(async () => await editor.PublishReviewSaveAsync(capture)).Throws<IOException>();

        await Assert.That(editor.SavedReviewPath).IsNull();
        await Assert.That((await File.ReadAllBytesAsync(files.DestinationPath)).SequenceEqual(expected)).IsTrue();
        await Assert.That(Directory.GetFiles(files.Root, "*.tmp")).IsEmpty();
        await files.AssertOriginalsUnchangedAsync();
    }

    [Test]
    public async Task AReparseAncestorInsertedAfterSelectionCannotRedirectPublicationIntoTheSource()
    {
        using ViewerPortableReviewFixture files = await ViewerPortableReviewFixture.CreateAsync();
        await using UsdStageScheduler scheduler = UsdStageScheduler.OpenForReview(files.SourcePath);
        UsdReviewSourceBinding binding = await scheduler.InvokeAsync(
            static stage => stage.CaptureReviewSourceBinding());
        await using var editor = new ViewerAuthoredEditController(scheduler, sourceBinding: binding);
        string output = Path.Combine(files.Root, "output");
        Directory.CreateDirectory(output);
        ViewerReviewSaveCapture capture = await editor.PrepareReviewSaveAsync(Path.Combine(output, "source.usda"));
        Directory.Move(output, output + "-old");
        await ViewerPortableReviewFixture.CreateJunctionAsync(output, files.Root);
        try
        {
            await Assert.That(async () => await editor.PublishReviewSaveAsync(capture))
                .Throws<NotSupportedException>();
            await files.AssertOriginalsUnchangedAsync();
        }
        finally
        {
            Directory.Delete(output);
        }
    }

    [Test]
    public async Task ASharingFailureDuringAtomicReplacePreservesThePreviousFileAndDoesNotAcknowledge()
    {
        using ViewerPortableReviewFixture files = await ViewerPortableReviewFixture.CreateAsync();
        await using UsdStageScheduler scheduler = UsdStageScheduler.OpenForReview(files.SourcePath);
        UsdReviewSourceBinding binding = await scheduler.InvokeAsync(
            static stage => stage.CaptureReviewSourceBinding());
        await using var editor = new ViewerAuthoredEditController(scheduler, sourceBinding: binding);
        await editor.PublishReviewSaveAsync(await editor.PrepareReviewSaveAsync(files.DestinationPath));
        byte[] before = await File.ReadAllBytesAsync(files.DestinationPath);
        var address = new UsdLayerEditAddress("/Body.review:value", UsdLayerEditField.Default);
        await editor.ApplyAsync(await editor.CaptureAsync([address]),
            [UsdLayerEdit.Set(address, UsdLayerEditValue.FromDouble(47), "double")], "Value");
        ViewerReviewSaveCapture capture = await editor.PrepareReviewSaveAsync(files.DestinationPath);
        await using (var foreignReader = new FileStream(
            files.DestinationPath, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            await Assert.That(async () => await editor.PublishReviewSaveAsync(capture)).Throws<IOException>();
        }
        await Assert.That((await File.ReadAllBytesAsync(files.DestinationPath)).SequenceEqual(before)).IsTrue();
        await Assert.That((await editor.ReadDocumentAsync()).HasChanges).IsTrue();
        await Assert.That(Directory.GetFiles(files.Root, "*.tmp")).IsEmpty();
        await files.AssertOriginalsUnchangedAsync();
    }

    [Test]
    public async Task AStagingCreateCollisionDoesNotTruncateTheForeignFile()
    {
        using ViewerPortableReviewFixture files = await ViewerPortableReviewFixture.CreateAsync();
        const string name = ".review-existing.tmp";
        string path = Path.Combine(files.Root, name);
        await File.WriteAllTextAsync(path, "foreign staging");
        await Assert.That(() =>
        {
            using FileStream rejected = ViewerWindowsFiles.CreateStaging(files.Root, name);
        }).Throws<IOException>();
        await Assert.That(await File.ReadAllTextAsync(path)).IsEqualTo("foreign staging");
    }

    [Test]
    public async Task AnExternalEditAfterStagingIsObservedBeforeReplacingTheReview()
    {
        using ViewerPortableReviewFixture files = await ViewerPortableReviewFixture.CreateAsync();
        await using UsdStageScheduler scheduler = UsdStageScheduler.OpenForReview(files.SourcePath);
        UsdReviewSourceBinding binding = await scheduler.InvokeAsync(
            static stage => stage.CaptureReviewSourceBinding());
        await using var editor = new ViewerAuthoredEditController(scheduler, sourceBinding: binding);
        await editor.PublishReviewSaveAsync(await editor.PrepareReviewSaveAsync(files.DestinationPath));
        ViewerReviewSaveCapture capture = await editor.PrepareReviewSaveAsync(files.DestinationPath);
        await using (ViewerReviewDocumentPublication publication =
            await ViewerReviewDocumentPublication.StageAsync(capture, CancellationToken.None))
        {
            await File.WriteAllTextAsync(files.DestinationPath, "external revision");
            await Assert.That(async () => await publication.PublishAsync(CancellationToken.None))
                .Throws<IOException>();
        }
        await Assert.That(await File.ReadAllTextAsync(files.DestinationPath)).IsEqualTo("external revision");
        await Assert.That(Directory.GetFiles(files.Root, "*.tmp")).IsEmpty();
        await files.AssertOriginalsUnchangedAsync();
    }

    [Test]
    public async Task CancellationAfterStagingPreservesThePreviousReviewAndForeignTemporaryFiles()
    {
        using ViewerPortableReviewFixture files = await ViewerPortableReviewFixture.CreateAsync();
        await using UsdStageScheduler scheduler = UsdStageScheduler.OpenForReview(files.SourcePath);
        UsdReviewSourceBinding binding = await scheduler.InvokeAsync(
            static stage => stage.CaptureReviewSourceBinding());
        await using var editor = new ViewerAuthoredEditController(scheduler, sourceBinding: binding);
        await editor.PublishReviewSaveAsync(await editor.PrepareReviewSaveAsync(files.DestinationPath));
        byte[] previous = await File.ReadAllBytesAsync(files.DestinationPath);
        string foreign = Path.Combine(files.Root, ".review-foreign.tmp");
        await File.WriteAllTextAsync(foreign, "not owned");
        ViewerReviewSaveCapture capture = await editor.PrepareReviewSaveAsync(files.DestinationPath);
        await using (ViewerReviewDocumentPublication publication =
            await ViewerReviewDocumentPublication.StageAsync(capture, CancellationToken.None))
        {
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            await Assert.That(async () => await publication.PublishAsync(cancellation.Token))
                .Throws<OperationCanceledException>();
        }
        await Assert.That((await File.ReadAllBytesAsync(files.DestinationPath)).SequenceEqual(previous)).IsTrue();
        await Assert.That(await File.ReadAllTextAsync(foreign)).IsEqualTo("not owned");
        await Assert.That(Directory.GetFiles(files.Root, "*.tmp").Length).IsEqualTo(1);
        await files.AssertOriginalsUnchangedAsync();
    }

    [Test]
    public async Task ReplacingTheParentDirectoryAfterSelectionInvalidatesTheObservedDestination()
    {
        using ViewerPortableReviewFixture files = await ViewerPortableReviewFixture.CreateAsync();
        await using UsdStageScheduler scheduler = UsdStageScheduler.OpenForReview(files.SourcePath);
        UsdReviewSourceBinding binding = await scheduler.InvokeAsync(
            static stage => stage.CaptureReviewSourceBinding());
        await using var editor = new ViewerAuthoredEditController(scheduler, sourceBinding: binding);
        string parent = Path.Combine(files.Root, "output");
        Directory.CreateDirectory(parent);
        string destination = Path.Combine(parent, "review.urd");
        ViewerReviewSaveCapture capture = await editor.PrepareReviewSaveAsync(destination);
        Directory.Move(parent, parent + "-old");
        Directory.CreateDirectory(parent);

        await Assert.That(async () => await editor.PublishReviewSaveAsync(capture)).Throws<IOException>();

        await Assert.That(File.Exists(destination)).IsFalse();
        await Assert.That(Directory.GetFiles(parent, "*.tmp")).IsEmpty();
        await files.AssertOriginalsUnchangedAsync();
    }

    [Test]
    public async Task SameProcessPublicationForOnePhysicalDirectoryEntryIsSerialized()
    {
        var key = new ViewerReviewPublicationKey(new ViewerPhysicalFileIdentity(1, 2, 3), "REVIEW.URD");
        ViewerReviewPublicationGate first =
            await ViewerReviewPublicationGate.AcquireAsync(key, CancellationToken.None);
        Task<ViewerReviewPublicationGate> second =
            ViewerReviewPublicationGate.AcquireAsync(key, CancellationToken.None);
        try
        {
            await Assert.That(second.IsCompleted).IsFalse();
        }
        finally
        {
            first.Dispose();
        }
        using ViewerReviewPublicationGate acquired = await second.WaitAsync(TimeSpan.FromSeconds(5));
    }
}
