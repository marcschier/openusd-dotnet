// Copyright (c) marcschier. Licensed under the MIT License.

using OpenUsd.Editing;

namespace OpenUsd.Viewer.Tests;

public sealed class ViewerReviewRecoveryTests
{
    [Test]
    public async Task NativeRecoveryCheckpointReopensAsUnsavedReviewWithoutSavingSourceOrHistory()
    {
        using ViewerPortableReviewFixture files = await ViewerPortableReviewFixture.CreateAsync();
        string cacheRoot = Path.Combine(files.Root, "cache");
        Directory.CreateDirectory(cacheRoot);
        using var store = new ViewerRecoveryStore(cacheRoot);
        string key = files.SourcePath.ToUpperInvariant();
        await using (UsdStageScheduler scheduler = UsdStageScheduler.OpenForReview(files.SourcePath))
        {
            UsdReviewSourceBinding binding = await scheduler.InvokeAsync(
                static stage => stage.CaptureReviewSourceBinding());
            await using var editor = new ViewerAuthoredEditController(scheduler, sourceBinding: binding);
            var address = new UsdLayerEditAddress("/Body.review:value", UsdLayerEditField.Default);
            await editor.ApplyAsync(await editor.CaptureAsync([address]),
                [UsdLayerEdit.Set(address, UsdLayerEditValue.FromDouble(47), "double")], "Value");
            UsdReviewDocument native = await editor.CaptureRecoveryDocumentAsync(
                store.GetCheckpointPath(key), retiring: false, CancellationToken.None);
            ViewerDocumentRecovery checkpoint = ViewerDocumentRecovery.FromNative(native, key);
            await store.SaveVerifiedAsync(checkpoint, CancellationToken.None);
            await Assert.That(editor.SavedReviewPath).IsNull();
            await Assert.That((await editor.ReadDocumentAsync()).HasChanges).IsTrue();
        }

        ViewerDocumentRecovery loaded = (await store.LoadAsync(key, CancellationToken.None))!;
        UsdReviewDocument document = UsdReviewDocument.Read(loaded.CopyPayload(), files.SourcePath);
        await using ViewerPreparedDocument prepared = await ViewerPreparedDocument.OpenReviewAsync(
            document, files.SourcePath, null, recovery: true, CancellationToken.None);
        await using var recovered = new ViewerAuthoredEditController(prepared.Scheduler, prepared.SourceBinding);
        recovered.InitializeImportedReview(prepared);
        await Assert.That((await recovered.ReadDocumentAsync()).HasChanges).IsTrue();
        await Assert.That(recovered.SavedReviewPath).IsNull();
        await Assert.That(recovered.UndoDepth).IsEqualTo(0);
        await Assert.That(await prepared.Scheduler.InvokeAsync(
            static stage => stage.GetPrim("/Body").GetDouble("review:value"))).IsEqualTo(47d);
        await files.AssertOriginalsUnchangedAsync();
    }

    [Test]
    public async Task AChangedDependencyRefusesRecoveryAndLeavesItsCheckpointAvailableForReconciliation()
    {
        using ViewerPortableReviewFixture files = await ViewerPortableReviewFixture.CreateAsync();
        string cacheRoot = Path.Combine(files.Root, "cache");
        Directory.CreateDirectory(cacheRoot);
        using var store = new ViewerRecoveryStore(cacheRoot);
        string key = files.SourcePath.ToUpperInvariant();
        await using (UsdStageScheduler scheduler = UsdStageScheduler.OpenForReview(files.SourcePath))
        {
            UsdReviewSourceBinding binding = await scheduler.InvokeAsync(
                static stage => stage.CaptureReviewSourceBinding());
            await using var editor = new ViewerAuthoredEditController(scheduler, sourceBinding: binding);
            var address = new UsdLayerEditAddress("/Body.review:value", UsdLayerEditField.Default);
            await editor.ApplyAsync(await editor.CaptureAsync([address]),
                [UsdLayerEdit.Set(address, UsdLayerEditValue.FromDouble(47), "double")], "Value");
            UsdReviewDocument native = await editor.CaptureRecoveryDocumentAsync(
                store.GetCheckpointPath(key), retiring: false, CancellationToken.None);
            await store.SaveVerifiedAsync(ViewerDocumentRecovery.FromNative(native, key), CancellationToken.None);
        }
        byte[] checkpointBefore = await File.ReadAllBytesAsync(store.GetCheckpointPath(key));
        await File.WriteAllTextAsync(Path.Combine(files.Root, "texture.png"), "changed dependency");
        ViewerDocumentRecovery loaded = (await store.LoadAsync(key, CancellationToken.None))!;
        UsdReviewDocument recorded = UsdReviewDocument.Read(loaded.CopyPayload(), files.SourcePath);
        await Assert.That(async () =>
        {
            await using ViewerPreparedDocument rejected = await ViewerPreparedDocument.OpenReviewAsync(
                recorded, files.SourcePath, null, recovery: true, CancellationToken.None);
        }).Throws<InvalidDataException>();
        await Assert.That((await File.ReadAllBytesAsync(store.GetCheckpointPath(key))).SequenceEqual(checkpointBefore))
            .IsTrue();
    }
}
