// Copyright (c) marcschier. Licensed under the MIT License.

namespace OpenUsd.Mcp.Tests;

public sealed partial class ArtifactResourceStoreTests
{
    [Test]
    [Arguments("bytes")]
    [Arguments("count")]
    [Arguments("duplicate")]
    [Arguments("changed")]
    [Arguments("missing")]
    public async Task VerifiedFileBatchFailuresLeaveNoNewDescriptorOrContent(string failure)
    {
        using var files = new ResourceStoreTestFiles();
        string first = files.CreateFile("color.bin", [1, 2, 3, 4]);
        string second = files.CreateFile("depth.bin", [5, 6, 7, 8]);
        using var store = new ArtifactResourceStore(new ArtifactResourceStoreOptions(
            MaximumResourceCount: failure == "count" ? 2 : 3,
            MaximumTotalBytes: failure == "bytes" ? 8 : 9,
            FileStorageRoot: files.StoreRoot));
        ArtifactResourceDescriptor existing = store.Add("existing", "application/octet-stream", new byte[] { 42 });
        ArtifactResourceFileWrite[] batch =
        [
            new("color", "image/png", first, 4, Hash([1, 2, 3, 4])),
            new(failure == "duplicate" ? "color" : "depth", "application/octet-stream",
                second, 4, Hash([5, 6, 7, 8]))
        ];
        if (failure == "changed")
        {
            await File.WriteAllBytesAsync(second, [8, 7, 6, 5]);
        }
        else if (failure == "missing")
        {
            File.Delete(second);
        }
        if (failure is "changed" or "missing")
        {
            await Assert.That(async () => await store.AddVerifiedFilesAsync(batch)).Throws<IOException>();
        }
        else
        {
            await Assert.That(async () => await store.AddVerifiedFilesAsync(batch)).Throws<InvalidOperationException>();
        }
        await Assert.That(store.Count).IsEqualTo(1);
        await Assert.That(store.TotalBytes).IsEqualTo(1L);
        await Assert.That((await store.ReadAsync(existing.ResourceUri))!.Content.Span[0]).IsEqualTo((byte)42);
        await Assert.That(Directory.Exists(files.StoreRoot)
            ? Directory.GetFiles(files.StoreRoot, "*", SearchOption.AllDirectories) : []).IsEmpty();
    }

    [Test]
    public async Task VerifiedFileBatchUsesExactBudgetAndDeduplicatesWithoutLosingOwnership()
    {
        using var files = new ResourceStoreTestFiles();
        string path = files.CreateFile("shared.bin", [1, 2, 3, 4]);
        using var store = files.CreateStore(maximumTotalBytes: 8);
        ArtifactResourceFileWrite[] batch =
        [
            new("first", "application/octet-stream", path, 4, Hash([1, 2, 3, 4])),
            new("second", "application/octet-stream", path, 4, Hash([1, 2, 3, 4]))
        ];
        IReadOnlyList<ArtifactResourceDescriptor> added = await store.AddVerifiedFilesAsync(batch);
        await Assert.That(added.Count).IsEqualTo(2);
        await Assert.That(added is System.Collections.ICollection).IsFalse();
        await Assert.That(store.Count).IsEqualTo(2);
        await Assert.That(store.TotalBytes).IsEqualTo(8L);
        await Assert.That(Directory.GetFiles(files.StoreRoot, "*", SearchOption.AllDirectories).Length).IsEqualTo(1);
        await File.WriteAllBytesAsync(path, [8, 8, 8, 8]);
        await Assert.That(Convert.ToHexString((await store.ReadAsync(added[0].ResourceUri))!.Content.Span))
            .IsEqualTo("01020304");
        await Assert.That(Convert.ToHexString((await store.ReadAsync(added[1].ResourceUri))!.Content.Span))
            .IsEqualTo("01020304");
    }

    [Test]
    public async Task ConcurrentVerifiedBatchesHaveOneCompleteWinner()
    {
        using var files = new ResourceStoreTestFiles();
        string path = files.CreateFile("shared.bin", [1, 2, 3, 4]);
        using var store = files.CreateStore(maximumTotalBytes: 8);
        ArtifactResourceFileWrite[] batch =
        [
            new("first", "application/octet-stream", path, 4, Hash([1, 2, 3, 4])),
            new("second", "application/octet-stream", path, 4, Hash([1, 2, 3, 4]))
        ];
        Task<bool>[] attempts = Enumerable.Range(0, 8).Select(async _ =>
        {
            try
            {
                await store.AddVerifiedFilesAsync(batch);
                return true;
            }
            catch (InvalidOperationException)
            {
                return false;
            }
        }).ToArray();
        bool[] results = await Task.WhenAll(attempts);
        await Assert.That(results.Count(static won => won)).IsEqualTo(1);
        await Assert.That(store.Count).IsEqualTo(2);
        await Assert.That(store.TotalBytes).IsEqualTo(8L);
        await Assert.That(Directory.GetFiles(files.StoreRoot, "*", SearchOption.AllDirectories).Length).IsEqualTo(1);
        await Assert.That(Convert.ToHexString(
            (await store.ReadAsync(ArtifactResourceUri.Create("first")))!.Content.Span)).IsEqualTo("01020304");
        await Assert.That(Convert.ToHexString(
            (await store.ReadAsync(ArtifactResourceUri.Create("second")))!.Content.Span)).IsEqualTo("01020304");
    }
}
