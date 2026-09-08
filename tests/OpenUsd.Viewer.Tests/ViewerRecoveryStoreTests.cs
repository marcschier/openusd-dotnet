// Copyright (c) marcschier. Licensed under the MIT License.

namespace OpenUsd.Viewer.Tests;

public sealed class ViewerRecoveryStoreTests
{
    [Test]
    public async Task RecoveryCacheRoundTripsOwnedBytesWithoutWritingTheSource()
    {
        string root = Path.Combine(AppContext.BaseDirectory, "recovery-store", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string source = Path.Combine(root, "source.usda");
        await File.WriteAllTextAsync(source, "#usda 1.0\n");
        var binding = new ViewerRecoveryBinding(source, new string('A', 64), "review-one", root);
        var checkpoint = new ViewerDocumentRecovery(
            1, binding, "native-layer-v1", ViewerDocumentLayerRole.Review, [1, 2, 3]);
        try
        {
            using var store = new ViewerRecoveryStore(Path.Combine(root, "cache"));
            await store.SaveAsync(checkpoint, CancellationToken.None);
            ViewerDocumentRecovery? loaded = await store.LoadAsync("review-one", CancellationToken.None);

            await Assert.That(loaded).IsNotNull();
            await Assert.That(loaded!.Validate(binding, "native-layer-v1").CanOffer).IsTrue();
            await Assert.That(Convert.ToHexString(loaded.CopyPayload())).IsEqualTo("010203");
            await Assert.That(await File.ReadAllTextAsync(source)).IsEqualTo("#usda 1.0\n");
            await Assert.That(Directory.GetFiles(store.RootPath, "*.tmp")).IsEmpty();
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    public async Task CancelledReplacementKeepsThePreviousCheckpointAndCorruptionIsAnError()
    {
        string root = Path.Combine(AppContext.BaseDirectory, "recovery-store", Guid.NewGuid().ToString("N"));
        var binding = new ViewerRecoveryBinding("source", new string('A', 64), "review", "anchor");
        try
        {
            using var store = new ViewerRecoveryStore(root);
            var original = new ViewerDocumentRecovery(
                1, binding, "native-1", ViewerDocumentLayerRole.Review, [1, 2, 3]);
            await store.SaveAsync(original, CancellationToken.None);
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            var replacement = new ViewerDocumentRecovery(1, binding, "native-1", ViewerDocumentLayerRole.Review, [9]);

            await Assert.That(async () => await store.SaveAsync(replacement, cancellation.Token))
                .Throws<OperationCanceledException>();
            ViewerDocumentRecovery? retained = await store.LoadAsync("review", CancellationToken.None);
            await Assert.That(Convert.ToHexString(retained!.CopyPayload())).IsEqualTo("010203");
            await Assert.That(Directory.GetFiles(root, "*.tmp")).IsEmpty();

            string path = Directory.GetFiles(root, "*.review-recovery").Single();
            byte[] bytes = await File.ReadAllBytesAsync(path);
            bytes[^1] ^= 1;
            await File.WriteAllBytesAsync(path, bytes);
            await Assert.That(async () => await store.LoadAsync("review", CancellationToken.None))
                .Throws<InvalidDataException>();
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Test]
    public async Task AFileSystemFailureDoesNotTurnIntoASavedCheckpoint()
    {
        string path = Path.Combine(AppContext.BaseDirectory, $"recovery-blocker-{Guid.NewGuid():N}");
        await File.WriteAllTextAsync(path, "untouched");
        try
        {
            using var store = new ViewerRecoveryStore(path);
            var binding = new ViewerRecoveryBinding("source", new string('A', 64), "review", "anchor");
            var checkpoint = new ViewerDocumentRecovery(
                1, binding, "native-1", ViewerDocumentLayerRole.Review, [1]);

            await Assert.That(async () => await store.SaveAsync(checkpoint, CancellationToken.None))
                .Throws<IOException>();
            await Assert.That(await File.ReadAllTextAsync(path)).IsEqualTo("untouched");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Test]
    public async Task AnExistingButUnreadableRecoveryEntryIsNotReportedAsMissing()
    {
        string root = Directory.CreateTempSubdirectory("openusd-recovery-read-").FullName;
        try
        {
            using var store = new ViewerRecoveryStore(Path.Combine(root, "cache"));
            var binding = new ViewerRecoveryBinding("source", new string('A', 64), "review", "anchor");
            var checkpoint = new ViewerDocumentRecovery(
                1, binding, "native-1", ViewerDocumentLayerRole.Review, [1]);
            await Assert.That(await store.LoadAsync("review", CancellationToken.None)).IsNull();
            await store.SaveAsync(checkpoint, CancellationToken.None);
            string entry = Directory.GetFiles(store.RootPath, "*.review-recovery").Single();
            File.Delete(entry);
            Directory.CreateDirectory(entry);

            await Assert.That(async () => await store.LoadAsync("review", CancellationToken.None))
                .Throws<UnauthorizedAccessException>();
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
