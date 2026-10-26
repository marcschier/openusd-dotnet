// Copyright (c) marcschier. Licensed under the MIT License.

namespace OpenUsd.Mcp.Tests;

public sealed partial class ArtifactResourceStoreTests
{
    [Test]
    [Arguments(false, false)]
    [Arguments(false, true)]
    [Arguments(true, false)]
    [Arguments(true, true)]
    public async Task SingleFileAdmissionRejectsKnownIdentityOrCountBeforeOpeningSources(bool verified, bool duplicate)
    {
        using var files = new ResourceStoreTestFiles();
        string path = files.CreateFile("locked.bin", [1, 2, 3, 4]);
        using var store = new ArtifactResourceStore(new ArtifactResourceStoreOptions(
            MaximumResourceCount: duplicate ? 2 : 1,
            FileStorageRoot: files.StoreRoot));
        ArtifactResourceDescriptor existing = store.Add("existing", "application/octet-stream", new byte[] { 42 });
        using var locked = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        string id = duplicate ? "existing" : "new";
        async Task publish()
        {
            if (verified)
            {
                await store.AddVerifiedFileAsync(id, "application/octet-stream", path, 4, Hash([1, 2, 3, 4]));
            }
            else
            {
                await store.AddFileAsync(id, "application/octet-stream", path);
            }
        }
        if (duplicate)
        {
            await Assert.That(publish).Throws<InvalidOperationException>();
        }
        else
        {
            await Assert.That(publish).Throws<ArtifactResourceStoreCapacityException>();
        }
        await Assert.That(store.Count).IsEqualTo(1);
        await Assert.That(store.TotalBytes).IsEqualTo(1L);
        await Assert.That((await store.ReadAsync(existing.ResourceUri))!.Content.Span[0]).IsEqualTo((byte)42);
        await Assert.That(Directory.Exists(files.StoreRoot)).IsFalse();
    }

    [Test]
    public async Task VerifiedFileLengthsBeyondInt64CombinedCapacityAreRejectedWithoutOverflowOrIo()
    {
        using var files = new ResourceStoreTestFiles();
        using var store = files.CreateStore(maximumTotalBytes: long.MaxValue);
        ArtifactResourceFileWrite[] batch =
        [
            new("first", "application/octet-stream", Path.Combine(files.Root, "missing-first"), long.MaxValue,
                Hash([])),
            new("second", "application/octet-stream", Path.Combine(files.Root, "missing-second"), long.MaxValue,
                Hash([]))
        ];
        await Assert.That(async () => await store.AddVerifiedFilesAsync(batch))
            .Throws<ArtifactResourceStoreCapacityException>();
        await Assert.That(store.Count).IsEqualTo(0);
        await Assert.That(store.TotalBytes).IsEqualTo(0L);
        await Assert.That(Directory.Exists(files.StoreRoot)).IsFalse();
    }

    [Test]
    [Arguments("bytes")]
    [Arguments("count")]
    [Arguments("duplicate")]
    [Arguments("batch-duplicate")]
    public async Task VerifiedFileAdmissionRejectsKnownFailuresBeforeOpeningSources(string refusal)
    {
        using var files = new ResourceStoreTestFiles();
        string path = files.CreateFile("locked.bin", [1, 2, 3, 4]);
        using var store = new ArtifactResourceStore(new ArtifactResourceStoreOptions(
            MaximumResourceCount: refusal == "count" ? 2 : 4,
            MaximumTotalBytes: refusal == "bytes" ? 8 : 16,
            FileStorageRoot: files.StoreRoot));
        ArtifactResourceDescriptor existing = store.Add("existing", "application/octet-stream", new byte[] { 42 });
        using var locked = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        ArtifactResourceFileWrite[] batch =
        [
            new(refusal == "duplicate" ? "existing" : "first", "application/octet-stream",
                path, 4, Hash([1, 2, 3, 4])),
            new(refusal == "batch-duplicate" ? "first" : "second", "application/octet-stream",
                path, 4, Hash([1, 2, 3, 4]))
        ];
        if (refusal is "bytes" or "count")
        {
            await Assert.That(async () => await store.AddVerifiedFilesAsync(batch))
                .Throws<ArtifactResourceStoreCapacityException>();
        }
        else
        {
            await Assert.That(async () => await store.AddVerifiedFilesAsync(batch))
                .Throws<InvalidOperationException>();
        }
        await Assert.That(store.Count).IsEqualTo(1);
        await Assert.That(store.TotalBytes).IsEqualTo(1L);
        await Assert.That(store.TryGetDescriptor(ArtifactResourceUri.Create("first"), out _)).IsFalse();
        await Assert.That(store.TryGetDescriptor(ArtifactResourceUri.Create("second"), out _)).IsFalse();
        await Assert.That((await store.ReadAsync(existing.ResourceUri))!.Content.Span[0]).IsEqualTo((byte)42);
        await Assert.That(Directory.Exists(files.StoreRoot)
            ? Directory.GetFiles(files.StoreRoot, "*", SearchOption.AllDirectories) : []).IsEmpty();
        locked.Position = 0;
        byte[] original = new byte[4];
        locked.ReadExactly(original);
        await Assert.That(Convert.ToHexString(original)).IsEqualTo("01020304");
    }

    [Test]
    [Arguments("bytes")]
    [Arguments("count")]
    [Arguments("duplicate")]
    [Arguments("batch-duplicate")]
    public async Task RejectedMemoryBatchesDoNotCopyPayloadsBeforeAdmission(string refusal)
    {
        using var store = new ArtifactResourceStore(new ArtifactResourceStoreOptions(
            MaximumResourceCount: refusal == "count" ? 1 : 4,
            MaximumTotalBytes: refusal == "bytes" ? 8 : 16 * 1024 * 1024,
            InlineThresholdBytes: 0));
        ArtifactResourceDescriptor existing = store.Add("existing", "application/octet-stream",
            new byte[] { 1, 2, 3, 4 });
        byte[] payload = new byte[4 * 1024 * 1024];
        ArtifactResourceWrite[] batch =
        [
            new(refusal == "duplicate" ? "existing" : "first", "application/octet-stream", payload),
            new(refusal == "batch-duplicate" ? "first" : "second", "application/octet-stream", payload)
        ];
        InvalidOperationException? observed = null;
        long before = GC.GetAllocatedBytesForCurrentThread();
        try
        {
            store.AddRange(batch);
        }
        catch (InvalidOperationException exception)
        {
            observed = exception;
        }
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        await Assert.That(observed).IsNotNull();
        if (refusal is "bytes" or "count")
        {
            await Assert.That(observed is ArtifactResourceStoreCapacityException).IsTrue();
        }
        else
        {
            await Assert.That(observed is ArtifactResourceStoreCapacityException).IsFalse();
        }
        await Assert.That(allocated).IsLessThan(256 * 1024L);
        await Assert.That(store.Count).IsEqualTo(1);
        await Assert.That(store.TotalBytes).IsEqualTo(4L);
        await Assert.That(store.TryGetDescriptor(ArtifactResourceUri.Create("first"), out _)).IsFalse();
        await Assert.That(store.TryGetDescriptor(ArtifactResourceUri.Create("second"), out _)).IsFalse();
        await Assert.That(Convert.ToHexString((await store.ReadAsync(existing.ResourceUri))!.Content.Span))
            .IsEqualTo("01020304");
    }

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
