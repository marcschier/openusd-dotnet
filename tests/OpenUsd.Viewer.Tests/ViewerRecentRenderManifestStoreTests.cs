// Copyright (c) marcschier. Licensed under the MIT License.

using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace OpenUsd.Viewer.Tests;

public sealed class ViewerRecentRenderManifestStoreTests
{
    [Test]
    public async Task MissingIndexDoesNotCreateDirectoriesOrReadReferencedArtifacts()
    {
        using var files = new ViewerCompletedJobTestFiles();
        string root = Path.Combine(files.Root, "profile");
        using var store = new ViewerRecentRenderManifestStore(root);
        await Assert.That(await store.LoadAsync()).IsEmpty();
        await Assert.That(Directory.Exists(root)).IsFalse();
        string missing = Path.Combine(files.Root, "absent-job", "manifest.json");
        await store.AddAsync(missing);
        using var restarted = new ViewerRecentRenderManifestStore(root);
        await Assert.That((await restarted.LoadAsync()).Single()).IsEqualTo(missing);
        await Assert.That(Directory.Exists(Path.GetDirectoryName(missing))).IsFalse();
    }

    [Test]
    public async Task RestartRetainsExactlyEightNewestDistinctPathsAndRepeatedPathsMoveToFront()
    {
        using var files = new ViewerCompletedJobTestFiles();
        string[] paths = Enumerable.Range(0, 10)
            .Select(index => Path.Combine(files.Root, $"job-{index}", "manifest.json")).ToArray();
        using (var store = new ViewerRecentRenderManifestStore(files.Root))
        {
            foreach (string path in paths)
            {
                await store.AddAsync(path);
            }
            await Assert.That((await store.LoadAsync()).SequenceEqual(paths.Reverse().Take(8))).IsTrue();
            await store.AddAsync(paths[4]);
        }
        using var restarted = new ViewerRecentRenderManifestStore(files.Root);
        string[] expected = [paths[4], .. paths.Reverse().Where(path => path != paths[4]).Take(7)];
        IReadOnlyList<string> snapshot = await restarted.LoadAsync();
        await Assert.That(snapshot.SequenceEqual(expected)).IsTrue();
        await Assert.That((await restarted.AddAsync(paths[0])).SequenceEqual([paths[0], .. expected.Take(7)])).IsTrue();
        await Assert.That(snapshot.SequenceEqual(expected)).IsTrue();
        using JsonDocument document = JsonDocument.Parse(await File.ReadAllTextAsync(restarted.StorePath));
        await Assert.That(document.RootElement.EnumerateObject().Select(property => property.Name)
            .SequenceEqual(["schemaVersion", "paths"])).IsTrue();
        await Assert.That(Directory.GetFiles(files.Root, "*.tmp")).IsEmpty();
        await Assert.That(new FileInfo(restarted.StorePath + ".lock").Length).IsEqualTo(0);
    }

    [Test]
    public async Task CanonicalDeduplicationUsesTheHostFilesystemCasePolicy()
    {
        using var files = new ViewerCompletedJobTestFiles();
        using var store = new ViewerRecentRenderManifestStore(files.Root);
        string path = Path.Combine(files.Root, "CaseJob", "manifest.json");
        await store.AddAsync(path);
        string equivalent = Path.Combine(files.Root, "CaseJob", "..", "CaseJob", "manifest.json");
        await Assert.That((await store.AddAsync(equivalent)).Count).IsEqualTo(1);
        string otherCase = Path.Combine(files.Root, "casejob", "manifest.json");
        IReadOnlyList<string> revised = await store.AddAsync(otherCase);
        await Assert.That(revised.Count).IsEqualTo(OperatingSystem.IsWindows() ? 1 : 2);
        await Assert.That(revised[0]).IsEqualTo(otherCase);
    }

    [Test]
    public async Task EightMaximumLengthEscapedUnicodePathsRoundTripWithoutOpeningArtifacts()
    {
        using var files = new ViewerCompletedJobTestFiles();
        using var store = new ViewerRecentRenderManifestStore(files.Root);
        var paths = new List<string>();
        for (int index = 0; index < 8; index++)
        {
            string prefix = Path.Combine(files.Root, $"job-{index}");
            int padding = 4096 - Path.Combine(prefix, "manifest.json").Length - 1;
            string path = Path.Combine(prefix, new string('\u00e9', padding), "manifest.json");
            await Assert.That(path.Length).IsEqualTo(4096);
            paths.Insert(0, path);
            await store.AddAsync(path);
        }
        await Assert.That(new FileInfo(store.StorePath).Length)
            .IsLessThanOrEqualTo(ViewerRecentRenderManifestStore.MaximumFileBytes);
        using var restarted = new ViewerRecentRenderManifestStore(files.Root);
        await Assert.That((await restarted.LoadAsync()).SequenceEqual(paths)).IsTrue();
        string oversized = paths[0].Replace("manifest.json", "x" + Path.DirectorySeparatorChar + "manifest.json",
            StringComparison.Ordinal);
        byte[] before = await File.ReadAllBytesAsync(store.StorePath);
        await Assert.That(async () => await store.AddAsync(oversized)).Throws<InvalidDataException>();
        await Assert.That((await File.ReadAllBytesAsync(store.StorePath)).SequenceEqual(before)).IsTrue();
    }

    [Test]
    [Arguments("malformed")]
    [Arguments("null")]
    [Arguments("version")]
    [Arguments("unknown-property")]
    [Arguments("duplicate-property")]
    [Arguments("missing-property")]
    [Arguments("paths-type")]
    [Arguments("path-type")]
    [Arguments("relative-path")]
    [Arguments("remote-path")]
    [Arguments("wrong-file")]
    [Arguments("duplicate-path")]
    [Arguments("too-many")]
    [Arguments("long-path")]
    public async Task InvalidIndexIsExplicitlyRefusedAndNeverOverwrittenByANewCompletion(string change)
    {
        using var files = new ViewerCompletedJobTestFiles();
        using var store = new ViewerRecentRenderManifestStore(files.Root);
        string path = Path.Combine(files.Root, "job", "manifest.json");
        var root = new JsonObject
        {
            ["schemaVersion"] = 1,
            ["paths"] = new JsonArray(JsonValue.Create(path))
        };
        switch (change)
        {
            case "version":
                root["schemaVersion"] = 2;
                break;
            case "unknown-property":
                root["other"] = true;
                break;
            case "missing-property":
                root.Remove("paths");
                break;
            case "paths-type":
                root["paths"] = "not-an-array";
                break;
            case "path-type":
                root["paths"]![0] = 123;
                break;
            case "relative-path":
                root["paths"]![0] = "manifest.json";
                break;
            case "remote-path":
                root["paths"]![0] = "//server/job/manifest.json";
                break;
            case "wrong-file":
                root["paths"]![0] = Path.Combine(files.Root, "other.json");
                break;
            case "duplicate-path":
                root["paths"]!.AsArray().Add(path);
                break;
            case "too-many":
                root["paths"] = new JsonArray(Enumerable.Range(0, 9)
                    .Select(index => (JsonNode?)JsonValue.Create(
                        Path.Combine(files.Root, $"job-{index}", "manifest.json"))).ToArray());
                break;
            case "long-path":
                root["paths"]![0] = new string('x', 4097);
                break;
        }
        string json = change switch
        {
            "malformed" => "{",
            "null" => "null",
            "duplicate-property" => root.ToJsonString().Replace("\"schemaVersion\":1",
                "\"schemaVersion\":1,\"schemaVersion\":1", StringComparison.Ordinal),
            _ => root.ToJsonString()
        };
        await File.WriteAllTextAsync(store.StorePath, json);
        byte[] original = await File.ReadAllBytesAsync(store.StorePath);
        await Assert.That(async () => await store.LoadAsync()).Throws<InvalidDataException>();
        await Assert.That(async () => await store.AddAsync(path)).Throws<InvalidDataException>();
        await Assert.That((await File.ReadAllBytesAsync(store.StorePath)).SequenceEqual(original)).IsTrue();
        await Assert.That(Directory.GetFiles(files.Root, "*.tmp")).IsEmpty();
    }

    [Test]
    [Arguments(0)]
    [Arguments(ViewerRecentRenderManifestStore.MaximumFileBytes)]
    [Arguments(ViewerRecentRenderManifestStore.MaximumFileBytes + 1)]
    public async Task IndexByteBoundIsEnforcedBeforeParsingAndIncludesItsExactLimit(int size)
    {
        using var files = new ViewerCompletedJobTestFiles();
        using var store = new ViewerRecentRenderManifestStore(files.Root);
        string json = """{"schemaVersion":1,"paths":[]}""";
        byte[] bytes = size == 0 ? [] : Encoding.UTF8.GetBytes(json.PadRight(size));
        await File.WriteAllBytesAsync(store.StorePath, bytes);
        if (size == ViewerRecentRenderManifestStore.MaximumFileBytes)
        {
            await Assert.That(await store.LoadAsync()).IsEmpty();
        }
        else
        {
            await Assert.That(async () => await store.LoadAsync()).Throws<InvalidDataException>();
        }
    }

    [Test]
    public async Task CancellationAndCompetingWriterPreserveThePreviousIndexAndLaterWritesRecover()
    {
        using var files = new ViewerCompletedJobTestFiles();
        using var store = new ViewerRecentRenderManifestStore(files.Root);
        string first = Path.Combine(files.Root, "first", "manifest.json");
        string second = Path.Combine(files.Root, "second", "manifest.json");
        await store.AddAsync(first);
        byte[] original = await File.ReadAllBytesAsync(store.StorePath);
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();
        await Assert.That(async () => await store.AddAsync(second, cancelled.Token))
            .Throws<OperationCanceledException>();
        await Assert.That(async () => await store.LoadAsync(cancelled.Token))
            .Throws<OperationCanceledException>();
        using (var otherWriter = new FileStream(
            store.StorePath + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
        {
            await Assert.That(async () => await store.AddAsync(second)).Throws<IOException>();
        }
        await Assert.That((await File.ReadAllBytesAsync(store.StorePath)).SequenceEqual(original)).IsTrue();
        await Assert.That((await store.AddAsync(second)).SequenceEqual([second, first])).IsTrue();
        await Assert.That(Directory.GetFiles(files.Root, "*.tmp")).IsEmpty();
    }
}
