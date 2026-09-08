// Copyright (c) marcschier. Licensed under the MIT License.

using System.Security.Cryptography;

namespace OpenUsd.Mcp.Tests;

public sealed partial class ArtifactResourceStoreTests
{
    [Test]
    [Arguments(0)]
    [Arguments(128 * 1024)]
    public async Task ChangedRecordedExtentIsRejectedBeforeCreatingCopyStorage(int changedLength)
    {
        using var files = new ResourceStoreTestFiles();
        byte[] admitted = new byte[4096];
        string source = files.CreateFile("frame.png", admitted);
        string recordedHash = Hash(admitted);
        await File.WriteAllBytesAsync(source, new byte[changedLength]);
        var store = files.CreateStore(maximumTotalBytes: admitted.Length);

        await Assert.That(async () => await store.AddVerifiedFileAsync(
            "frame-0", "image/png", source, admitted.Length, recordedHash))
            .Throws<ArtifactResourceIntegrityException>();

        await Assert.That(store.Count).IsEqualTo(0);
        await Assert.That(store.TotalBytes).IsEqualTo(0L);
        await Assert.That(Directory.Exists(files.StoreRoot)).IsFalse();
    }

    [Test]
    public async Task VerifiedFilePublicationRejectsChangedBytesBeforeRegisteringAResource()
    {
        using var files = new ResourceStoreTestFiles();
        string source = files.CreateFile("frame.png", [9, 2, 3, 4]);
        var store = files.CreateStore();
        const string originalHash = "9f64a747e1b97f131fabb6b447296c9b6f0201e79fb3c5356e6c77e89b6a806a";

        await Assert.That(async () => await store.AddVerifiedFileAsync(
            "frame-0", "image/png", source, 4, originalHash)).Throws<ArtifactResourceIntegrityException>();
        await Assert.That(store.Count).IsEqualTo(0);
        await Assert.That(store.TotalBytes).IsEqualTo(0L);
        await File.WriteAllBytesAsync(source, [1, 2, 3, 4]);
        ArtifactResourceDescriptor descriptor = await store.AddVerifiedFileAsync(
            "frame-0", "image/png", source, 4, originalHash);
        await File.WriteAllBytesAsync(source, [8, 8, 8, 8]);
        ArtifactResourceContent? read = await store.ReadAsync(descriptor.ResourceUri);
        await Assert.That(Convert.ToHexString(read!.Content.Span)).IsEqualTo("01020304");
    }

    [Test]
    public async Task AddsImmutableContentWithOpenUsdResourceUri()
    {
        var store = new ArtifactResourceStore();
        byte[] content = [1, 2, 3, 4];

        ArtifactResourceDescriptor descriptor =
            store.Add("request 1.png", "image/png", content);
        content[0] = 99;
        ArtifactResourceContent? resolved = await store.ReadAsync(
            descriptor.ResourceUri);

        await Assert.That(descriptor.ResourceUri.AbsoluteUri)
            .IsEqualTo("openusd://artifact/request%201.png");
        await Assert.That(descriptor.Sha256)
            .IsEqualTo("9f64a747e1b97f131fabb6b447296c9b6f0201e79fb3c5356e6c77e89b6a806a");
        await Assert.That(resolved).IsNotNull();
        await Assert.That(resolved!.Descriptor).IsEqualTo(descriptor);
        await Assert.That(resolved.Content.ToArray())
            .IsEquivalentTo(new byte[] { 1, 2, 3, 4 });
        await Assert.That(descriptor.IsInline).IsTrue();
        await Assert.That(descriptor.InlineBase64).IsEqualTo("AQIDBA==");

        byte[] retrieved = resolved.Content.ToArray();
        retrieved[0] = 42;
        ArtifactResourceContent? retrievedAgain = await store.ReadAsync(
            descriptor.ResourceUri);
        await Assert.That(retrievedAgain!.Content.Span[0]).IsEqualTo((byte)1);
    }

    [Test]
    public async Task DuplicateResourceUrisAreRejected()
    {
        var store = new ArtifactResourceStore();
        _ = store.Add("same", "image/png", new byte[1]);

        await Assert.That(() => store.Add("same", "image/png", new byte[1]))
            .Throws<InvalidOperationException>();
    }

    [Test]
    public async Task EnforcesResourceAndByteBoundsWithoutMutatingOnFailure()
    {
        var store = new ArtifactResourceStore(
            new ArtifactResourceStoreOptions(
                MaximumResourceCount: 2,
                MaximumTotalBytes: 3,
                InlineThresholdBytes: 1));
        ArtifactResourceDescriptor first =
            store.Add("first", "image/png", new byte[2]);

        await Assert.That(first.IsInline).IsFalse();
        await Assert.That(() => store.Add("too-large", "image/png", new byte[2]))
            .Throws<ArtifactResourceStoreCapacityException>();
        _ = store.Add("second", "image/png", new byte[1]);
        await Assert.That(
                () => store.Add("too-many", "image/png", ReadOnlyMemory<byte>.Empty))
            .Throws<ArtifactResourceStoreCapacityException>();
        await Assert.That(store.Count).IsEqualTo(2);
        await Assert.That(store.TotalBytes).IsEqualTo(3);
    }

    [Test]
    public async Task BatchPublicationIsAtomicAtCountAndByteBoundaries()
    {
        var exact = new ArtifactResourceStore(
            new ArtifactResourceStoreOptions(
                MaximumResourceCount: 2,
                MaximumTotalBytes: 3));
        IReadOnlyList<ArtifactResourceDescriptor> added = exact.AddRange(
        [
            new ArtifactResourceWrite("one", "image/png", new byte[1]),
            new ArtifactResourceWrite("two", "image/png", new byte[2]),
        ]);

        await Assert.That(added.Count).IsEqualTo(2);
        await Assert.That(exact.Count).IsEqualTo(2);
        await Assert.That(exact.TotalBytes).IsEqualTo(3);

        var countLimited = new ArtifactResourceStore(
            new ArtifactResourceStoreOptions(
                MaximumResourceCount: 1,
                MaximumTotalBytes: 10));
        await Assert.That(() => countLimited.AddRange(
            [
                new ArtifactResourceWrite("one", "image/png", new byte[1]),
                new ArtifactResourceWrite("two", "image/png", new byte[1]),
            ]))
            .Throws<ArtifactResourceStoreCapacityException>();
        await Assert.That(countLimited.Count).IsEqualTo(0);
        await Assert.That(countLimited.TotalBytes).IsEqualTo(0);

        var byteLimited = new ArtifactResourceStore(
            new ArtifactResourceStoreOptions(
                MaximumResourceCount: 2,
                MaximumTotalBytes: 1));
        await Assert.That(() => byteLimited.AddRange(
            [
                new ArtifactResourceWrite("one", "image/png", new byte[1]),
                new ArtifactResourceWrite("two", "image/png", new byte[1]),
            ]))
            .Throws<ArtifactResourceStoreCapacityException>();
        await Assert.That(byteLimited.Count).IsEqualTo(0);
        await Assert.That(byteLimited.TotalBytes).IsEqualTo(0);
    }

    [Test]
    public async Task ConcurrentIdCollisionHasExactlyOneWinner()
    {
        var store = new ArtifactResourceStore();
        Task<bool>[] attempts = Enumerable.Range(0, 8)
            .Select(attempt => Task.Run(() =>
            {
                try
                {
                    _ = store.Add("collision", "image/png", new byte[] { 1 });
                    return true;
                }
                catch (InvalidOperationException)
                {
                    return false;
                }
            }))
            .ToArray();

        bool[] results = await Task.WhenAll(attempts);

        await Assert.That(results.Count(static result => result)).IsEqualTo(1);
        await Assert.That(store.Count).IsEqualTo(1);
    }

    [Test]
    public async Task FileBackedResourcesAreContentAddressedAndIndependentOfSource()
    {
        using var files = new ResourceStoreTestFiles();
        byte[] content = "file-backed resource"u8.ToArray();
        string sourcePath = files.CreateFile("report.json", content);
        var store = files.CreateStore();

        ArtifactResourceDescriptor first = await store.AddFileAsync(
            "first-report",
            "application/json",
            sourcePath);
        ArtifactResourceDescriptor second = await store.AddFileAsync(
            "second-report",
            "application/json",
            sourcePath);
        File.WriteAllText(sourcePath, "mutated source");
        ArtifactResourceContent? resolved = await store.ReadAsync(first.ResourceUri);

        await Assert.That(first.Sha256).IsEqualTo(Hash(content));
        await Assert.That(first.ByteLength).IsEqualTo(content.LongLength);
        await Assert.That(first.IsInline).IsFalse();
        await Assert.That(second.Sha256).IsEqualTo(first.Sha256);
        await Assert.That(resolved!.Content.ToArray()).IsEquivalentTo(content);
        await Assert.That(Directory.EnumerateFiles(
                files.StoreRoot,
                "*",
                SearchOption.AllDirectories))
            .Count().IsEqualTo(1);
    }

    [Test]
    public async Task FileBackedReadDetectsCacheMutation()
    {
        using var files = new ResourceStoreTestFiles();
        string sourcePath = files.CreateFile("report.json", "immutable"u8.ToArray());
        var store = files.CreateStore();
        ArtifactResourceDescriptor descriptor = await store.AddFileAsync(
            "report",
            "application/json",
            sourcePath);
        string storedPath = Directory.EnumerateFiles(
                files.StoreRoot,
                "*",
                SearchOption.AllDirectories)
            .Single();
        File.WriteAllText(storedPath, "corrupted");

        await Assert.That(
                async () => await store.ReadAsync(descriptor.ResourceUri))
            .Throws<ArtifactResourceIntegrityException>();
    }

    [Test]
    public async Task FileBackedReadEnforcesConfiguredResponseBoundary()
    {
        using var files = new ResourceStoreTestFiles();
        string sourcePath = files.CreateFile("four.bin", [1, 2, 3, 4]);
        var exactStore = files.CreateStore(maximumReadResponseBytes: 4);
        ArtifactResourceDescriptor exact = await exactStore.AddFileAsync(
            "exact",
            "application/octet-stream",
            sourcePath);
        ArtifactResourceContent? content = await exactStore.ReadAsync(
            exact.ResourceUri);

        await Assert.That(content!.Content.Length).IsEqualTo(4);

        var limitedStore = files.CreateStore(
            maximumReadResponseBytes: 3,
            suffix: "limited");
        ArtifactResourceDescriptor limited = await limitedStore.AddFileAsync(
            "limited",
            "application/octet-stream",
            sourcePath);
        await Assert.That(
                async () => await limitedStore.ReadAsync(limited.ResourceUri))
            .Throws<ArtifactResourceReadLimitException>();
    }

    [Test]
    public async Task FileBackedPublicationHonorsExactStoreByteBoundary()
    {
        using var files = new ResourceStoreTestFiles();
        string exactPath = files.CreateFile("exact.bin", [1, 2, 3, 4]);
        string excessPath = files.CreateFile("excess.bin", [5]);
        var store = files.CreateStore(maximumTotalBytes: 4);

        _ = await store.AddFileAsync(
            "exact",
            "application/octet-stream",
            exactPath);
        await Assert.That(
                async () => await store.AddFileAsync(
                    "excess",
                    "application/octet-stream",
                    excessPath))
            .Throws<ArtifactResourceStoreCapacityException>();
        await Assert.That(store.TotalBytes).IsEqualTo(4);
        await Assert.That(store.Count).IsEqualTo(1);
    }

    [Test]
    public async Task SparseFileLargerThanTwoGigabytesIsRejectedBeforeCopy()
    {
        using var files = new ResourceStoreTestFiles();
        string sourcePath = Path.Combine(files.Root, "large.usdc");
        await using (var stream = new FileStream(
                         sourcePath,
                         FileMode.CreateNew,
                         FileAccess.Write,
                         FileShare.None))
        {
            stream.SetLength((long)int.MaxValue + 1);
        }

        var store = new ArtifactResourceStore(
            new ArtifactResourceStoreOptions(
                MaximumTotalBytes: int.MaxValue,
                FileStorageRoot: files.StoreRoot));

        await Assert.That(
                async () => await store.AddFileAsync(
                    "large",
                    "model/vnd.usdc",
                    sourcePath))
            .Throws<ArtifactResourceStoreCapacityException>();
        await Assert.That(store.Count).IsEqualTo(0);
        await Assert.That(store.TotalBytes).IsEqualTo(0);
        await Assert.That(Directory.Exists(files.StoreRoot)).IsFalse();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task CancelledFilePublicationRemovesPartialContent(bool batch)
    {
        using var files = new ResourceStoreTestFiles();
        string sourcePath = Path.Combine(files.Root, "large.bin");
        await using (var stream = new FileStream(
                         sourcePath,
                         FileMode.CreateNew,
                         FileAccess.Write,
                         FileShare.None))
        {
            stream.SetLength(128L * 1024 * 1024);
        }

        ArtifactResourceFileWrite[] writes = [];
        if (batch)
        {
            string first = files.CreateFile("first.bin", [1, 2, 3, 4]);
            using FileStream original = File.OpenRead(sourcePath);
            string expectedHash = Convert.ToHexString(SHA256.HashData(original)).ToLowerInvariant();
            writes =
            [
                new("first", "application/octet-stream", first, 4, Hash([1, 2, 3, 4])),
                new("cancelled", "application/octet-stream", sourcePath, 128L * 1024 * 1024, expectedHash)
            ];
        }
        using var store = files.CreateStore(maximumTotalBytes: 256L * 1024 * 1024);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(1));
        if (batch)
        {
            await Assert.That(async () => await store.AddVerifiedFilesAsync(writes, cancellation.Token))
                .Throws<OperationCanceledException>();
        }
        else
        {
            await Assert.That(async () => await store.AddFileAsync(
                    "cancelled",
                    "application/octet-stream",
                    sourcePath,
                    cancellation.Token))
                .Throws<OperationCanceledException>();
        }
        await Assert.That(store.Count).IsEqualTo(0);
        await Assert.That(
                Directory.Exists(files.StoreRoot)
                    ? Directory.EnumerateFiles(
                        files.StoreRoot,
                        "*",
                        SearchOption.AllDirectories)
                    : [])
            .IsEmpty();
    }

    private static string Hash(ReadOnlySpan<byte> content) =>
        Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant();

    private sealed class ResourceStoreTestFiles : IDisposable
    {
        internal ResourceStoreTestFiles()
        {
            Root = Path.Combine(
                Environment.GetEnvironmentVariable("OPENUSD_TEST_WORK_ROOT") ?? Path.GetTempPath(),
                "artifact-store-tests",
                Guid.NewGuid().ToString("N"));
            StoreRoot = Path.Combine(Root, "store");
            Directory.CreateDirectory(Root);
        }

        internal string Root { get; }

        internal string StoreRoot { get; }

        internal ArtifactResourceStore CreateStore(
            long maximumTotalBytes = 64L * 1024 * 1024,
            long maximumReadResponseBytes = 64L * 1024 * 1024,
            string suffix = "") =>
            new(
                new ArtifactResourceStoreOptions(
                    MaximumTotalBytes: maximumTotalBytes,
                    MaximumReadResponseBytes: maximumReadResponseBytes,
                    FileStorageRoot: string.Concat(StoreRoot, suffix)));

        internal string CreateFile(string name, ReadOnlySpan<byte> content)
        {
            string path = Path.Combine(Root, name);
            File.WriteAllBytes(path, content);
            return path;
        }

        public void Dispose()
        {
            if (Directory.Exists(Root))
            {
                Directory.Delete(Root, recursive: true);
            }
        }
    }
}
