// Copyright (c) marcschier. Licensed under the MIT License.

using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using OpenUsd.Rendering.Silk;

namespace OpenUsd.Rendering.Tests;

public sealed class MetalLibrarySidecarTests
{
    private static readonly MethodInfo Validator = typeof(SilkCheckedShaderAssets).GetMethod(
        "ValidateMetalLibraryPairForTesting",
        BindingFlags.NonPublic | BindingFlags.Static)!;
    private static readonly MethodInfo Availability = typeof(SilkCheckedShaderAssets).GetMethod(
        "HasValidMetalLibraryPairForTesting",
        BindingFlags.NonPublic | BindingFlags.Static)!;

    [Test]
    public async Task ValidSyntheticPairMatchesCheckedTenEntryContract()
    {
        using var pair = MetalLibraryPair.Create();

        byte[] validated = InvokeValid(pair);
        byte[] validatedAgain = InvokeValid(pair);

        await Assert.That(validated).IsEquivalentTo(pair.LibraryBytes);
        await Assert.That(validatedAgain).IsEquivalentTo(pair.LibraryBytes);
        await Assert.That(InvokeAvailability(pair)).IsTrue();
    }

    [Test]
    public async Task MissingOrCorruptSidecarIsRejected()
    {
        using (var missing = MetalLibraryPair.Create())
        {
            File.Delete(missing.ManifestPath);
            await Assert.That(InvokeAvailability(missing)).IsFalse();
            Exception exception = InvokeInvalid(missing);
            await Assert.That(exception).IsTypeOf<FileNotFoundException>();
        }

        using (var corrupt = MetalLibraryPair.Create())
        {
            File.WriteAllText(corrupt.ManifestPath, "{not-json");
            await Assert.That(InvokeAvailability(corrupt)).IsFalse();
            Exception exception = InvokeInvalid(corrupt);
            await Assert.That(exception).IsTypeOf<InvalidDataException>();
            await Assert.That(exception.Message).Contains("not valid JSON");
        }
    }

    [Test]
    public async Task WrongLibraryHashOrSizeIsRejectedAfterCachedSuccess()
    {
        using (var wrongHash = MetalLibraryPair.Create())
        {
            _ = InvokeValid(wrongHash);
            wrongHash.LibraryBytes[0] ^= byte.MaxValue;
            File.WriteAllBytes(wrongHash.LibraryPath, wrongHash.LibraryBytes);
            Exception exception = InvokeInvalid(wrongHash);
            await Assert.That(exception).IsTypeOf<InvalidDataException>();
            await Assert.That(exception.Message).Contains("sha256");
        }

        using (var wrongSize = MetalLibraryPair.Create())
        {
            wrongSize.Library["size"] = wrongSize.LibraryBytes.LongLength + 1;
            wrongSize.WriteManifest();
            Exception exception = InvokeInvalid(wrongSize);
            await Assert.That(exception).IsTypeOf<InvalidDataException>();
            await Assert.That(exception.Message).Contains("size");
        }
    }

    [Test]
    public async Task WrongSchemaVersionIsRejected()
    {
        using var pair = MetalLibraryPair.Create();
        pair.Manifest["schemaVersion"] = 3;
        pair.WriteManifest();

        await AssertInvalidContract(pair, "schemaVersion 4");
    }

    [Test]
    public async Task MissingOrDuplicateComputeEntryIsRejected()
    {
        using (var missing = MetalLibraryPair.Create())
        {
            RemoveByProgramName(missing.EntryPoints, "compute.fill");
            missing.WriteManifest();
            await AssertInvalidContract(missing, "exactly 10 entry points");
        }

        using (var duplicate = MetalLibraryPair.Create())
        {
            ReplaceWithDuplicate(
                duplicate.EntryPoints,
                "compute.scale",
                "compute.fill");
            duplicate.WriteManifest();
            await AssertInvalidContract(duplicate, "exact checked shader contract order");
        }
    }

    [Test]
    public async Task WrongStageComputeEntryIsRejected()
    {
        using var pair = MetalLibraryPair.Create();
        FindByProgramName(pair.EntryPoints, "compute.fill")["stage"] = "fragment";
        pair.WriteManifest();

        await AssertInvalidContract(pair, "exact checked shader contract order");
    }

    [Test]
    public async Task MissingOrDuplicateComputeSourceIsRejected()
    {
        using (var missing = MetalLibraryPair.Create())
        {
            RemoveByProgramName(missing.Sources, "compute.fill");
            missing.WriteManifest();
            await AssertInvalidContract(missing, "exactly 10 sources");
        }

        using (var duplicate = MetalLibraryPair.Create())
        {
            ReplaceWithDuplicate(
                duplicate.Sources,
                "compute.scale",
                "compute.fill");
            duplicate.WriteManifest();
            await AssertInvalidContract(duplicate, "exact checked shader contract order");
        }
    }

    [Test]
    public async Task MissingPickEntryOrSourceIsRejected()
    {
        using (var missingEntry = MetalLibraryPair.Create())
        {
            RemoveByProgramName(missingEntry.EntryPoints, "pick.fragment");
            missingEntry.WriteManifest();
            await AssertInvalidContract(
                missingEntry,
                "exactly 10 entry points");
        }

        using (var missingSource = MetalLibraryPair.Create())
        {
            RemoveByProgramName(missingSource.Sources, "pick.vertex");
            missingSource.WriteManifest();
            await AssertInvalidContract(missingSource, "exactly 10 sources");
        }
    }

    [Test]
    public async Task MissingSelectionOutlineEntryOrSourceIsRejected()
    {
        using (var missingEntry = MetalLibraryPair.Create())
        {
            RemoveByProgramName(
                missingEntry.EntryPoints,
                "selection.outline.fragment");
            missingEntry.WriteManifest();
            await AssertInvalidContract(
                missingEntry,
                "exactly 10 entry points");
        }

        using (var missingSource = MetalLibraryPair.Create())
        {
            RemoveByProgramName(
                missingSource.Sources,
                "selection.mask.vertex");
            missingSource.WriteManifest();
            await AssertInvalidContract(missingSource, "exactly 10 sources");
        }
    }

    [Test]
    public async Task WrongStageOrIdentityComputeSourceIsRejected()
    {
        using (var wrongStage = MetalLibraryPair.Create())
        {
            FindByProgramName(wrongStage.Sources, "compute.scale")["stage"] = "vertex";
            wrongStage.WriteManifest();
            await AssertInvalidContract(wrongStage, "sources are stale");
        }

        using (var wrongIdentity = MetalLibraryPair.Create())
        {
            FindByProgramName(wrongIdentity.Sources, "compute.fill")["sha256"] =
                new string('0', 64);
            wrongIdentity.WriteManifest();
            await AssertInvalidContract(wrongIdentity, "sources are stale");
        }
    }

    [Test]
    public async Task ExtraSchemaPropertiesAndWrongContractOrderAreRejected()
    {
        using (var extra = MetalLibraryPair.Create())
        {
            extra.Library["unexpected"] = true;
            extra.WriteManifest();
            await AssertInvalidContract(extra, "properties do not match schemaVersion 4");
        }

        using (var reordered = MetalLibraryPair.Create())
        {
            JsonNode first = reordered.EntryPoints[0]!.DeepClone();
            reordered.EntryPoints[0] = reordered.EntryPoints[1]!.DeepClone();
            reordered.EntryPoints[1] = first;
            reordered.WriteManifest();
            await AssertInvalidContract(reordered, "exact checked shader contract order");
        }
    }

    private static byte[] InvokeValid(MetalLibraryPair pair) =>
        (byte[])Validator.Invoke(null, [pair.LibraryPath, pair.ManifestPath])!;

    private static bool InvokeAvailability(MetalLibraryPair pair) =>
        (bool)Availability.Invoke(null, [pair.LibraryPath, pair.ManifestPath])!;

    private static Exception InvokeInvalid(MetalLibraryPair pair)
    {
        TargetInvocationException exception = Assert.Throws<TargetInvocationException>(
            () => Validator.Invoke(null, [pair.LibraryPath, pair.ManifestPath]));
        return exception.InnerException!;
    }

    private static async Task AssertInvalidContract(
        MetalLibraryPair pair,
        string message)
    {
        Exception exception = InvokeInvalid(pair);
        await Assert.That(exception).IsTypeOf<InvalidDataException>();
        await Assert.That(exception.Message).Contains(message);
    }

    private static JsonObject FindByProgramName(JsonArray array, string programName) =>
        array.Select(node => (JsonObject)node!)
            .Single(node => (string?)node["programName"] == programName);

    private static void RemoveByProgramName(JsonArray array, string programName)
    {
        JsonObject item = FindByProgramName(array, programName);
        _ = array.Remove(item);
    }

    private static void ReplaceWithDuplicate(
        JsonArray array,
        string programName,
        string duplicateProgramName)
    {
        JsonObject item = FindByProgramName(array, programName);
        int index = array.IndexOf(item);
        array[index] = FindByProgramName(array, duplicateProgramName).DeepClone();
    }

    private sealed class MetalLibraryPair : IDisposable
    {
        private MetalLibraryPair(
            string directory,
            byte[] libraryBytes,
            JsonObject manifest)
        {
            Directory = directory;
            LibraryBytes = libraryBytes;
            Manifest = manifest;
            Library = (JsonObject)manifest["library"]!;
            Sources = (JsonArray)Library["sources"]!;
            EntryPoints = (JsonArray)Library["entryPoints"]!;
            LibraryPath = Path.Combine(directory, "mesh.metallib");
            ManifestPath = Path.Combine(directory, "mesh.metallib.manifest.json");
        }

        internal string Directory { get; }

        internal string LibraryPath { get; }

        internal string ManifestPath { get; }

        internal byte[] LibraryBytes { get; }

        internal JsonObject Manifest { get; }

        internal JsonObject Library { get; }

        internal JsonArray Sources { get; }

        internal JsonArray EntryPoints { get; }

        internal static MetalLibraryPair Create()
        {
            string directory = Path.Combine(
                AppContext.BaseDirectory,
                "metal-sidecar-validation",
                Guid.NewGuid().ToString("N"));
            System.IO.Directory.CreateDirectory(directory);
            byte[] libraryBytes =
            [
                0x4d, 0x54, 0x4c, 0x42, 0x01, 0x02, 0x03, 0x04,
                0x46, 0x41, 0x4b, 0x45, 0x05, 0x06, 0x07, 0x08
            ];
            string libraryHash = Convert.ToHexString(
                SHA256.HashData(libraryBytes)).ToLowerInvariant();
            JsonArray sources = LoadCheckedSources();
            var entryPoints = new JsonArray(
                sources.Select(source =>
                {
                    var item = (JsonObject)source!;
                    return (JsonNode)new JsonObject
                    {
                        ["programName"] = item["programName"]!.DeepClone(),
                        ["name"] = item["entryPoint"]!.DeepClone(),
                        ["stage"] = item["stage"]!.DeepClone()
                    };
                }).ToArray());
            var air = new JsonArray(
                sources.Select(source =>
                {
                    var item = (JsonObject)source!;
                    string programName = (string)item["programName"]!;
                    return (JsonNode)new JsonObject
                    {
                        ["programName"] = programName,
                        ["path"] = $"artifacts/metal-sidecar/{programName}.air",
                        ["sha256"] = new string('1', 64),
                        ["size"] = 1,
                        ["entryPoint"] = item["entryPoint"]!.DeepClone(),
                        ["stage"] = item["stage"]!.DeepClone()
                    };
                }).ToArray());
            var library = new JsonObject
            {
                ["name"] = "mesh",
                ["path"] = "mesh.metallib",
                ["stagedPath"] = "eng/shaders/checked/mesh.metallib",
                ["sha256"] = libraryHash,
                ["size"] = libraryBytes.LongLength,
                ["sources"] = sources,
                ["air"] = air,
                ["entryPoints"] = entryPoints,
                ["symbolDump"] = "mesh.symbols.txt",
                ["symbolDumpSha256"] = new string('2', 64),
                ["symbolDumpSize"] = 1,
                ["commands"] = new JsonObject()
            };
            var manifest = new JsonObject
            {
                ["schemaVersion"] = 4,
                ["rid"] = "osx-arm64",
                ["checkedRoot"] = "eng/shaders/checked",
                ["payloadRoot"] = "artifacts/metal-sidecar",
                ["stagedManifestPath"] =
                    "eng/shaders/checked/mesh.metallib.manifest.json",
                ["toolchain"] = new JsonObject(),
                ["provenance"] = new JsonArray(),
                ["library"] = library
            };
            var pair = new MetalLibraryPair(directory, libraryBytes, manifest);
            File.WriteAllBytes(pair.LibraryPath, libraryBytes);
            pair.WriteManifest();
            return pair;
        }

        internal void WriteManifest() =>
            File.WriteAllText(ManifestPath, Manifest.ToJsonString());

        public void Dispose() => System.IO.Directory.Delete(Directory, recursive: true);

        private static JsonArray LoadCheckedSources()
        {
            using Stream stream = typeof(SilkCheckedShaderAssets).Assembly
                .GetManifestResourceStream(
                    "OpenUsd.Rendering.Silk.Shaders.manifest.json")!;
            using JsonDocument document = JsonDocument.Parse(stream);
            var sources = new JsonArray();
            foreach (JsonElement program in document.RootElement
                .GetProperty("programs").EnumerateArray())
            {
                string programName = program.GetProperty("name").GetString()!;
                if (programName is not ("mesh.vertex" or "mesh.fragment" or
                    "pick.vertex" or "pick.fragment" or
                    "selection.mask.vertex" or "selection.mask.fragment" or
                    "selection.outline.vertex" or "selection.outline.fragment" or
                    "compute.fill" or "compute.scale"))
                {
                    continue;
                }
                JsonElement metal = program.GetProperty("artifacts").GetProperty("metal");
                sources.Add(new JsonObject
                {
                    ["programName"] = programName,
                    ["path"] = metal.GetProperty("path").GetString(),
                    ["sha256"] = metal.GetProperty("sha256").GetString(),
                    ["size"] = metal.GetProperty("size").GetInt64(),
                    ["entryPoint"] = program.GetProperty("entryPoint").GetString(),
                    ["stage"] = program.GetProperty("stage").GetString()
                });
            }
            return sources;
        }
    }
}
