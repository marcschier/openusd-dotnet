// Copyright (c) marcschier. Licensed under the MIT License.

namespace OpenUsd.Viewer.Tests;

public sealed class RecentStageStoreTests
{
    [Test]
    public async Task NormalizeDeduplicatesUsesMruOrderAndCapsAtTen()
    {
        string root = CreateTestRoot();
        try
        {
            var store = new RecentStageStore(root);
            string[] inputs = Enumerable.Range(0, 12)
                .Select(index => Path.Combine(root, $"stage-{index}.usd"))
                .Prepend(Path.Combine(root, "stage-3.usd"))
                .ToArray();

            IReadOnlyList<string> normalized = store.Normalize(inputs, onlyExisting: false);

            await Assert.That(normalized.Count).IsEqualTo(RecentStageStore.Capacity);
            await Assert.That(normalized[0]).IsEqualTo(Path.Combine(root, "stage-3.usd"));
            await Assert.That(normalized.Distinct(GetPathComparer()).Count())
                .IsEqualTo(normalized.Count);
        }
        finally
        {
            DeleteTestRoot(root);
        }
    }

    [Test]
    public async Task AddMovesExistingStageToFrontAndPersistsAtomically()
    {
        string root = CreateTestRoot();
        try
        {
            var store = new RecentStageStore(root);
            string first = CreateStage(root, "first.usda");
            string second = CreateStage(root, "second.usdc");

            _ = await store.AddAsync(first);
            _ = await store.AddAsync(second);
            IReadOnlyList<string> revised = await store.AddAsync(first);

            await Assert.That(revised).IsEquivalentTo([first, second]);
            await Assert.That(revised[0]).IsEqualTo(first);
            await Assert.That(Directory.GetFiles(root, "*.tmp")).IsEmpty();
            await Assert.That(await File.ReadAllLinesAsync(store.StorePath))
                .IsEquivalentTo([first, second]);
        }
        finally
        {
            DeleteTestRoot(root);
        }
    }

    [Test]
    public async Task LoadRemovesMissingFilesAndRewritesTheStore()
    {
        string root = CreateTestRoot();
        try
        {
            var store = new RecentStageStore(root);
            string existing = CreateStage(root, "existing.usd");
            string missing = Path.Combine(root, "missing.usd");
            await store.PersistAsync([missing, existing]);

            IReadOnlyList<string> loaded = await store.LoadAsync();

            await Assert.That(loaded).IsEquivalentTo([existing]);
            await Assert.That(await File.ReadAllLinesAsync(store.StorePath))
                .IsEquivalentTo([existing]);
        }
        finally
        {
            DeleteTestRoot(root);
        }
    }

    [Test]
    public async Task PersistenceFailureIsReportedToTheCaller()
    {
        string root = CreateTestRoot();
        try
        {
            string rootFile = Path.Combine(root, "not-a-directory");
            await File.WriteAllTextAsync(rootFile, "occupied");
            var store = new RecentStageStore(rootFile);

            await Assert.That(() => store.PersistAsync(["C:\\stage.usd"]))
                .Throws<IOException>();
        }
        finally
        {
            DeleteTestRoot(root);
        }
    }

    private static string CreateTestRoot()
    {
        string root = Path.Combine(
            AppContext.BaseDirectory,
            "recent-stage-store-tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    private static string CreateStage(string root, string name)
    {
        string path = Path.Combine(root, name);
        File.WriteAllText(path, "#usda 1.0");
        return Path.GetFullPath(path);
    }

    private static StringComparer GetPathComparer() =>
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    private static void DeleteTestRoot(string root)
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
