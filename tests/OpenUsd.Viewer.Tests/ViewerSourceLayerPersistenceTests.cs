// Copyright (c) marcschier. Licensed under the MIT License.

using System.Runtime.Versioning;
using OpenUsd.Editing;

namespace OpenUsd.Viewer.Tests;

[SupportedOSPlatform("windows")]
public sealed class ViewerSourceLayerPersistenceTests
{
    [Test]
    public async Task SetSourceEditOptIn_IsRequiredAndScopedToTheCurrentDocument()
    {
        using ViewerPortableReviewFixture files = await ViewerPortableReviewFixture.CreateAsync();
        string secondRoot = Path.Combine(files.Root, "second-document");
        Directory.CreateDirectory(secondRoot);
        string secondSource = Path.Combine(secondRoot, "source.usda");
        await File.WriteAllTextAsync(secondSource, ViewerPortableReviewFixture.SourceText);
        await File.WriteAllTextAsync(
            Path.Combine(secondRoot, "sublayer.usda"), ViewerPortableReviewFixture.SublayerText);
        await File.WriteAllBytesAsync(
            Path.Combine(secondRoot, "texture.png"), ViewerPortableReviewFixture.AssetBytes);

        await using UsdStageScheduler firstScheduler = UsdStageScheduler.OpenForReview(files.SourcePath);
        await using UsdStageScheduler secondScheduler = UsdStageScheduler.OpenForReview(secondSource);
        UsdReviewSourceBinding firstBinding = await firstScheduler.InvokeAsync(
            static stage => stage.CaptureReviewSourceBinding());
        UsdReviewSourceBinding secondBinding = await secondScheduler.InvokeAsync(
            static stage => stage.CaptureReviewSourceBinding());
        await using var first = new ViewerAuthoredEditController(firstScheduler, firstBinding);
        await using var second = new ViewerAuthoredEditController(secondScheduler, secondBinding);
        await MarkSourceDirtyAsync(firstScheduler, "first document edit");
        await MarkSourceDirtyAsync(secondScheduler, "second document edit");

        await Assert.That(first.SourceEditOptIn).IsFalse();
        await Assert.That(second.SourceEditOptIn).IsFalse();
        await Assert.That(async () => await first.PrepareSourceLayerSaveAsync())
            .Throws<InvalidOperationException>();
        await first.SetSourceEditOptInAsync(true);

        await Assert.That(first.SourceEditOptIn).IsTrue();
        await Assert.That(second.SourceEditOptIn).IsFalse();
        await Assert.That(async () => await second.PrepareSourceLayerSaveAsync())
            .Throws<InvalidOperationException>();
        await Assert.That((await File.ReadAllBytesAsync(files.SourcePath))
            .SequenceEqual(System.Text.Encoding.UTF8.GetBytes(ViewerPortableReviewFixture.SourceText))).IsTrue();
        await Assert.That((await File.ReadAllBytesAsync(secondSource))
            .SequenceEqual(System.Text.Encoding.UTF8.GetBytes(ViewerPortableReviewFixture.SourceText))).IsTrue();
    }

    [Test]
    public async Task PublishSourceLayerSave_RejectsCaptureOwnedByAnotherDocument()
    {
        using ViewerPortableReviewFixture files = await ViewerPortableReviewFixture.CreateAsync();
        await using UsdStageScheduler scheduler = UsdStageScheduler.OpenForReview(files.SourcePath);
        UsdReviewSourceBinding binding = await scheduler.InvokeAsync(
            static stage => stage.CaptureReviewSourceBinding());
        await using var editor = new ViewerAuthoredEditController(scheduler, binding);
        await editor.SetSourceEditOptInAsync(true);
        string rootIdentifier = await scheduler.InvokeAsync(static stage => stage.RootLayerIdentifier);
        ulong revision = await scheduler.InvokeAsync(stage =>
        {
            using UsdLayer root = stage.GetRootLayer();
            return root.GetEditingState().Revision;
        });
        ViewerSourceLayerPublication publication = await ViewerSourceLayerPublication.BeginAsync(
            files.SourcePath, binding.SourceFingerprint, CancellationToken.None);
        await using var foreignCapture = new ViewerSourceLayerSaveCapture(
            Guid.NewGuid(), rootIdentifier, revision, files.SourcePath, binding, publication);
        byte[] sourceBefore = await File.ReadAllBytesAsync(files.SourcePath);

        await Assert.That(async () => await editor.PublishSourceLayerSaveAsync(foreignCapture))
            .Throws<InvalidOperationException>();

        await Assert.That((await File.ReadAllBytesAsync(files.SourcePath)).SequenceEqual(sourceBefore)).IsTrue();
        await Assert.That(File.Exists(publication.StagingPath)).IsTrue();
    }

    [Test]
    public async Task PublishSourceLayerSave_RejectsCaptureBoundToAnotherSource()
    {
        using ViewerPortableReviewFixture files = await ViewerPortableReviewFixture.CreateAsync();
        string secondRoot = Path.Combine(files.Root, "second-source");
        Directory.CreateDirectory(secondRoot);
        string secondSource = Path.Combine(secondRoot, "source.usda");
        await File.WriteAllTextAsync(secondSource, ViewerPortableReviewFixture.SourceText);
        await File.WriteAllTextAsync(
            Path.Combine(secondRoot, "sublayer.usda"), ViewerPortableReviewFixture.SublayerText);
        await File.WriteAllBytesAsync(
            Path.Combine(secondRoot, "texture.png"), ViewerPortableReviewFixture.AssetBytes);
        await using UsdStageScheduler scheduler = UsdStageScheduler.OpenForReview(files.SourcePath);
        await using UsdStageScheduler foreignScheduler = UsdStageScheduler.OpenForReview(secondSource);
        UsdReviewSourceBinding binding = await scheduler.InvokeAsync(
            static stage => stage.CaptureReviewSourceBinding());
        UsdReviewSourceBinding foreignBinding = await foreignScheduler.InvokeAsync(
            static stage => stage.CaptureReviewSourceBinding());
        await using var editor = new ViewerAuthoredEditController(scheduler, binding);
        await editor.SetSourceEditOptInAsync(true);
        await MarkSourceDirtyAsync(scheduler, "binding must match");
        byte[] sourceBefore = await File.ReadAllBytesAsync(files.SourcePath);
        await using ViewerSourceLayerSaveCapture valid = await editor.PrepareSourceLayerSaveAsync();
        ViewerSourceLayerSaveCapture foreign =
            valid with { SourceBinding = foreignBinding };

        await Assert.That(async () => await editor.PublishSourceLayerSaveAsync(foreign))
            .Throws<InvalidOperationException>();

        await Assert.That((await File.ReadAllBytesAsync(files.SourcePath)).SequenceEqual(sourceBefore)).IsTrue();
        await Assert.That(File.Exists(valid.Publication.StagingPath)).IsTrue();
    }

    [Test]
    public async Task PublishSourceLayerSave_RejectsCaptureForNonRootLayerWithoutPublishingBytes()
    {
        using ViewerPortableReviewFixture files = await ViewerPortableReviewFixture.CreateAsync();
        await using UsdStageScheduler scheduler = UsdStageScheduler.OpenForReview(files.SourcePath);
        UsdReviewSourceBinding binding = await scheduler.InvokeAsync(
            static stage => stage.CaptureReviewSourceBinding());
        await using var editor = new ViewerAuthoredEditController(scheduler, binding);
        await editor.SetSourceEditOptInAsync(true);
        await MarkSourceDirtyAsync(scheduler, "root only");
        byte[] sourceBefore = await File.ReadAllBytesAsync(files.SourcePath);
        await using ViewerSourceLayerSaveCapture valid = await editor.PrepareSourceLayerSaveAsync();
        ViewerSourceLayerSaveCapture nonRoot = valid with { LayerIdentifier = "review-layer" };

        await Assert.That(async () => await editor.PublishSourceLayerSaveAsync(nonRoot))
            .Throws<IOException>();

        await Assert.That((await File.ReadAllBytesAsync(files.SourcePath)).SequenceEqual(sourceBefore)).IsTrue();
        await Assert.That(File.Exists(valid.Publication.StagingPath)).IsTrue();
    }

    [Test]
    public async Task PrepareSourceLayerSave_RequiresOptInAndCancellationLeavesNoPartialOutput()
    {
        using ViewerPortableReviewFixture files = await ViewerPortableReviewFixture.CreateAsync();
        await using UsdStageScheduler scheduler = UsdStageScheduler.OpenForReview(files.SourcePath);
        UsdReviewSourceBinding binding = await scheduler.InvokeAsync(
            static stage => stage.CaptureReviewSourceBinding());
        await using var editor = new ViewerAuthoredEditController(scheduler, binding);
        await MarkSourceDirtyAsync(scheduler, "cancelled edit");
        byte[] original = await File.ReadAllBytesAsync(files.SourcePath);

        await Assert.That(async () => await editor.PrepareSourceLayerSaveAsync())
            .Throws<InvalidOperationException>();
        await editor.SetSourceEditOptInAsync(true);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.That(async () => await editor.PrepareSourceLayerSaveAsync(cancellation.Token))
            .Throws<OperationCanceledException>();

        await Assert.That((await File.ReadAllBytesAsync(files.SourcePath)).SequenceEqual(original)).IsTrue();
        await Assert.That(Directory.GetFiles(files.Root, ".openusd-source-*")).IsEmpty();
    }

    [Test]
    public async Task PublishSourceLayerSave_RejectsChangedLayerRevisionWithoutPublishingBytes()
    {
        using ViewerPortableReviewFixture files = await ViewerPortableReviewFixture.CreateAsync();
        await using UsdStageScheduler scheduler = UsdStageScheduler.OpenForReview(files.SourcePath);
        UsdReviewSourceBinding binding = await scheduler.InvokeAsync(
            static stage => stage.CaptureReviewSourceBinding());
        await using var editor = new ViewerAuthoredEditController(scheduler, binding);
        await editor.SetSourceEditOptInAsync(true);
        await MarkSourceDirtyAsync(scheduler, "captured edit");
        byte[] original = await File.ReadAllBytesAsync(files.SourcePath);
        await using ViewerSourceLayerSaveCapture capture = await editor.PrepareSourceLayerSaveAsync();
        await MarkSourceDirtyAsync(scheduler, "revision changed after capture");

        await Assert.That(async () => await editor.PublishSourceLayerSaveAsync(capture))
            .Throws<IOException>();

        await Assert.That((await File.ReadAllBytesAsync(files.SourcePath)).SequenceEqual(original)).IsTrue();
        await Assert.That(File.Exists(capture.Publication.StagingPath)).IsTrue();
    }

    [Test]
    public async Task PrepareSourceLayerSave_RejectsStaleVerifiedSourceFingerprintWithoutOverwrite()
    {
        using ViewerPortableReviewFixture files = await ViewerPortableReviewFixture.CreateAsync();
        await using UsdStageScheduler scheduler = UsdStageScheduler.OpenForReview(files.SourcePath);
        UsdReviewSourceBinding binding = await scheduler.InvokeAsync(
            static stage => stage.CaptureReviewSourceBinding());
        await using var editor = new ViewerAuthoredEditController(scheduler, binding);
        await editor.SetSourceEditOptInAsync(true);
        await MarkSourceDirtyAsync(scheduler, "stale fingerprint");
        byte[] external = "#usda 1.0\n# changed after source verification\n"u8.ToArray();
        ViewerSourceLayerPublicationTests.ReplaceFileByHandle(files.SourcePath, external);

        await Assert.That(async () => await editor.PrepareSourceLayerSaveAsync()).Throws<IOException>();

        await Assert.That((await File.ReadAllBytesAsync(files.SourcePath)).SequenceEqual(external)).IsTrue();
        await Assert.That(Directory.GetFiles(files.Root, ".openusd-source-*")).IsEmpty();
    }

    [Test]
    public async Task PublishSourceLayerSave_RefusesExternalConflictAndFreshCaptureCanRetry()
    {
        using ViewerPortableReviewFixture files = await ViewerPortableReviewFixture.CreateAsync();
        await using UsdStageScheduler scheduler = UsdStageScheduler.OpenForReview(files.SourcePath);
        UsdReviewSourceBinding binding = await scheduler.InvokeAsync(
            static stage => stage.CaptureReviewSourceBinding());
        await using var editor = new ViewerAuthoredEditController(scheduler, binding);
        await editor.SetSourceEditOptInAsync(true);
        await MarkSourceDirtyAsync(scheduler, "retry edit");
        byte[] original = await File.ReadAllBytesAsync(files.SourcePath);
        await using ViewerSourceLayerSaveCapture stale = await editor.PrepareSourceLayerSaveAsync();
        byte[] external = "#usda 1.0\n# external writer wins\n"u8.ToArray();
        ViewerSourceLayerPublicationTests.ReplaceFileByHandle(files.SourcePath, external);

        await Assert.That(async () => await editor.PublishSourceLayerSaveAsync(stale)).Throws<IOException>();
        await Assert.That((await File.ReadAllBytesAsync(files.SourcePath)).SequenceEqual(external)).IsTrue();
        await stale.DisposeAsync();

        ViewerSourceLayerPublicationTests.ReplaceFileByHandle(files.SourcePath, original);
        await using ViewerSourceLayerSaveCapture retry = await editor.PrepareSourceLayerSaveAsync();
        ViewerSourceLayerSaveResult result = await editor.PublishSourceLayerSaveAsync(retry);

        await Assert.That(result.Message).Contains("Source layer saved");
        await Assert.That(result.ReviewBindingRefreshed).IsTrue();
        await Assert.That((await File.ReadAllBytesAsync(files.SourcePath)).SequenceEqual(original)).IsFalse();
        string publishedComment = await scheduler.InvokeAsync(stage =>
        {
            using UsdLayer root = stage.GetRootLayer();
            return root.GetMetadataString("comment");
        });
        await Assert.That(publishedComment).IsEqualTo("retry edit");
        await retry.DisposeAsync();
        await Assert.That(Directory.GetFiles(files.Root, ".openusd-source-*")).IsEmpty();
    }

    [Test]
    public async Task PublishSourceLayerSave_CapturedExportRemainsImmutableUntilPublication()
    {
        using ViewerPortableReviewFixture files = await ViewerPortableReviewFixture.CreateAsync();
        await using UsdStageScheduler scheduler = UsdStageScheduler.OpenForReview(files.SourcePath);
        UsdReviewSourceBinding binding = await scheduler.InvokeAsync(
            static stage => stage.CaptureReviewSourceBinding());
        await using var editor = new ViewerAuthoredEditController(scheduler, binding);
        await editor.SetSourceEditOptInAsync(true);
        await MarkSourceDirtyAsync(scheduler, "rollback edit");
        await using ViewerSourceLayerSaveCapture capture = await editor.PrepareSourceLayerSaveAsync();
        await Assert.That(async () => await File.WriteAllBytesAsync(
            capture.Publication.StagingPath, "not a USD layer"u8.ToArray())).Throws<IOException>();

        ViewerSourceLayerSaveResult result = await editor.PublishSourceLayerSaveAsync(capture);

        await Assert.That(result.Message).Contains("Source layer saved");
        await Assert.That(await File.ReadAllTextAsync(files.SourcePath)).Contains("rollback edit");
        await capture.DisposeAsync();
        await Assert.That(Directory.GetFiles(files.Root, ".openusd-source-*")).IsEmpty();
        await Assert.That(await File.ReadAllTextAsync(Path.Combine(files.Root, "sublayer.usda")))
            .IsEqualTo(ViewerPortableReviewFixture.SublayerText);
    }

    [Test]
    public async Task PublishSourceLayerSave_CanceledBeforeCommitPreservesSourceAndCleansStagingOnDispose()
    {
        using ViewerPortableReviewFixture files = await ViewerPortableReviewFixture.CreateAsync();
        await using UsdStageScheduler scheduler = UsdStageScheduler.OpenForReview(files.SourcePath);
        UsdReviewSourceBinding binding = await scheduler.InvokeAsync(
            static stage => stage.CaptureReviewSourceBinding());
        await using var editor = new ViewerAuthoredEditController(scheduler, binding);
        await editor.SetSourceEditOptInAsync(true);
        await MarkSourceDirtyAsync(scheduler, "cancel before commit");
        byte[] original = await File.ReadAllBytesAsync(files.SourcePath);
        string stagingPath;

        await using (ViewerSourceLayerSaveCapture capture = await editor.PrepareSourceLayerSaveAsync())
        {
            stagingPath = capture.Publication.StagingPath;
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            await Assert.That(async () =>
                    await editor.PublishSourceLayerSaveAsync(capture, cancellation.Token))
                .Throws<OperationCanceledException>();
            await Assert.That((await File.ReadAllBytesAsync(files.SourcePath)).SequenceEqual(original)).IsTrue();
        }

        await Assert.That(File.Exists(stagingPath)).IsFalse();
        await Assert.That(Directory.GetFiles(files.Root, ".openusd-source-*")).IsEmpty();
    }

    [Test]
    public async Task PublishSourceLayerSave_PublishesRootEditThatSurvivesOpenUsdReopen()
    {
        using ViewerPortableReviewFixture files = await ViewerPortableReviewFixture.CreateAsync();
        byte[] originalRoot = await File.ReadAllBytesAsync(files.SourcePath);
        byte[] originalSublayer = await File.ReadAllBytesAsync(Path.Combine(files.Root, "sublayer.usda"));
        byte[] originalAsset = await File.ReadAllBytesAsync(Path.Combine(files.Root, "texture.png"));
        await using (UsdStageScheduler scheduler = UsdStageScheduler.OpenForReview(files.SourcePath))
        {
            UsdReviewSourceBinding binding = await scheduler.InvokeAsync(
                static stage => stage.CaptureReviewSourceBinding());
            await using var editor = new ViewerAuthoredEditController(scheduler, binding);
            await editor.SetSourceEditOptInAsync(true);
            await MarkSourceDirtyAsync(scheduler, "authored source value");
            await using ViewerSourceLayerSaveCapture capture = await editor.PrepareSourceLayerSaveAsync();
            await Assert.That(capture.LayerIdentifier).IsEqualTo(
                await scheduler.InvokeAsync(static stage => stage.RootLayerIdentifier));

            ViewerSourceLayerSaveResult saved = await editor.PublishSourceLayerSaveAsync(capture);

            await Assert.That(saved.Message).Contains("Source layer saved");
            await Assert.That(saved.ReviewBindingRefreshed).IsTrue();
            await Assert.That(editor.CanSaveReview).IsTrue();
            await Assert.That((await File.ReadAllBytesAsync(files.SourcePath)).SequenceEqual(originalRoot)).IsFalse();
        }

        await using UsdStageScheduler reopened = ViewerNativeTestStages.OpenSchedulerOrSkip(files.SourcePath);
        string marker = await reopened.InvokeAsync(stage =>
        {
            using UsdLayer root = stage.GetRootLayer();
            return root.GetMetadataString("comment");
        });
        await Assert.That(marker).IsEqualTo("authored source value");
        await Assert.That((await File.ReadAllBytesAsync(Path.Combine(files.Root, "sublayer.usda")))
            .SequenceEqual(originalSublayer)).IsTrue();
        await Assert.That((await File.ReadAllBytesAsync(Path.Combine(files.Root, "texture.png")))
            .SequenceEqual(originalAsset)).IsTrue();
    }

    [Test]
    public async Task PublishSourceLayerSave_RebindsActiveSavedReviewAndRequiresResave()
    {
        using ViewerPortableReviewFixture files = await ViewerPortableReviewFixture.CreateAsync();
        await using UsdStageScheduler scheduler = UsdStageScheduler.OpenForReview(files.SourcePath);
        UsdReviewSourceBinding binding = await scheduler.InvokeAsync(
            static stage => stage.CaptureReviewSourceBinding());
        await using var editor = new ViewerAuthoredEditController(scheduler, binding);
        var address = new UsdLayerEditAddress("/Body.review:value", UsdLayerEditField.Default);
        await editor.ApplyAsync(await editor.CaptureAsync([address]),
            [UsdLayerEdit.Set(address, UsdLayerEditValue.FromDouble(47), "double")], "Saved review value");
        ViewerReviewSaveResult initialReview =
            await editor.PublishReviewSaveAsync(await editor.PrepareReviewSaveAsync(files.DestinationPath));
        await Assert.That(initialReview.Acknowledged).IsTrue();
        await Assert.That((await editor.ReadDocumentAsync()).HasChanges).IsFalse();

        await editor.SetSourceEditOptInAsync(true);
        await MarkSourceDirtyAsync(scheduler, "source changed under review");
        await using ViewerSourceLayerSaveCapture sourceCapture = await editor.PrepareSourceLayerSaveAsync();
        ViewerSourceLayerSaveResult sourceSaved = await editor.PublishSourceLayerSaveAsync(sourceCapture);

        await Assert.That(sourceSaved.ReviewBindingRefreshed).IsTrue();
        await Assert.That(editor.CanSaveReview).IsTrue();
        await Assert.That(editor.SavedReviewPath).IsNull();
        await Assert.That((await editor.ReadDocumentAsync()).State.Review!.HasChanges).IsTrue();
        UsdLayerAuthoredOpinion retained = (await editor.CaptureAsync([address])).Snapshot.Opinions[0];
        await Assert.That(retained.Value.AsDouble()).IsEqualTo(47d);
        await sourceCapture.DisposeAsync();

        ViewerReviewSaveCapture refreshedReview = await editor.PrepareReviewSaveAsync(files.DestinationPath);
        UsdReviewSourceBinding refreshedBinding = await scheduler.InvokeAsync(
            static stage => stage.CaptureReviewSourceBinding());
        await Assert.That(Path.GetFullPath(refreshedReview.Document.SourceRootPath))
            .IsEqualTo(Path.GetFullPath(files.SourcePath));
        await Assert.That(refreshedReview.Document.SourceFingerprint)
            .IsEqualTo(refreshedBinding.SourceFingerprint);
        await Assert.That(refreshedReview.Document.SourceFingerprint)
            .IsNotEqualTo(initialReview.Document.SourceFingerprint);
        ViewerReviewSaveResult resaved = await editor.PublishReviewSaveAsync(refreshedReview);
        await Assert.That(resaved.Acknowledged).IsTrue();
        await Assert.That(editor.SavedReviewPath).IsEqualTo(files.DestinationPath);
        await Assert.That((await editor.ReadDocumentAsync()).State.Review!.HasChanges).IsFalse();
        await Assert.That(await File.ReadAllBytesAsync(files.SourcePath)).IsNotEmpty();
    }

    [Test]
    public async Task PublishSourceLayerSave_ReportsCommittedSourceAndDisablesReviewWhenBindingRefreshFails()
    {
        using ViewerPortableReviewFixture files = await ViewerPortableReviewFixture.CreateAsync();
        await using UsdStageScheduler scheduler = UsdStageScheduler.OpenForReview(files.SourcePath);
        UsdReviewSourceBinding binding = await scheduler.InvokeAsync(
            static stage => stage.CaptureReviewSourceBinding());
        await using var editor = new ViewerAuthoredEditController(scheduler, binding);
        var address = new UsdLayerEditAddress("/Body.review:value", UsdLayerEditField.Default);
        await editor.ApplyAsync(await editor.CaptureAsync([address]),
            [UsdLayerEdit.Set(address, UsdLayerEditValue.FromDouble(47), "double")], "Review value");
        await editor.PublishReviewSaveAsync(await editor.PrepareReviewSaveAsync(files.DestinationPath));
        await editor.SetSourceEditOptInAsync(true);
        await MarkSourceDirtyAsync(scheduler, "committed despite stale dependency");
        await using ViewerSourceLayerSaveCapture capture = await editor.PrepareSourceLayerSaveAsync();
        await File.WriteAllTextAsync(
            Path.Combine(files.Root, "sublayer.usda"), "#usda 1.0\ndef Xform \"ChangedDependency\" {}\n");

        ViewerSourceLayerSaveResult result = await editor.PublishSourceLayerSaveAsync(capture);

        await Assert.That(result.ReviewBindingRefreshed).IsFalse();
        await Assert.That(result.Message).Contains("Source layer saved");
        await Assert.That(result.Message).Contains("reopened");
        await Assert.That(editor.CanSaveReview).IsFalse();
        await Assert.That(async () => await editor.PrepareReviewSaveAsync(files.DestinationPath))
            .Throws<NotSupportedException>();
        await Assert.That((await File.ReadAllTextAsync(files.SourcePath))
            .Contains("committed despite stale dependency", StringComparison.Ordinal)).IsTrue();
        await Assert.That(await File.ReadAllTextAsync(Path.Combine(files.Root, "sublayer.usda")))
            .Contains("ChangedDependency");
    }

    private static async Task MarkSourceDirtyAsync(UsdStageScheduler scheduler, string marker)
    {
        await scheduler.EditAsync(stage =>
        {
            using UsdLayer root = stage.GetRootLayer();
            root.SetMetadata("comment", marker);
        }, UsdStageInvalidationKind.Property);
    }
}
