// Copyright (c) marcschier. Licensed under the MIT License.

using System.Security.Cryptography;
using System.Text.Json;

namespace OpenUsd.Rendering.Silk;

public static partial class SilkCheckedShaderAssets
{
    private const string MetalLibraryFileName = "mesh.metallib";
    private const string MetalManifestFileName = "mesh.metallib.manifest.json";
    private const int MetalManifestSchemaVersion = 4;
    private static readonly object MetalValidationSync = new();
    private static readonly Dictionary<string, string?> MetalValidationCache =
        new(StringComparer.Ordinal);
    private static readonly Lazy<MetalShaderIdentity[]> ExpectedMetalIdentities =
        new(LoadExpectedMetalIdentities);

    /// <summary>
    /// Validates the pinned Metal library and sidecar deployed beside the application.
    /// </summary>
    /// <exception cref="IOException">The pair is missing or unreadable.</exception>
    /// <exception cref="InvalidDataException">The pair is corrupt or stale.</exception>
    public static void ValidatePinnedMetalLibrary() => _ = LoadPinnedMetalLibrary();

    private static bool TryLoadPinnedMetalLibrary(out byte[] library)
    {
        string libraryPath = Path.Combine(AppContext.BaseDirectory, MetalLibraryFileName);
        string manifestPath = Path.Combine(AppContext.BaseDirectory, MetalManifestFileName);
        return TryLoadMetalLibraryPair(libraryPath, manifestPath, out library);
    }

    private static bool HasValidMetalLibraryPairForTesting(
        string libraryPath,
        string manifestPath) =>
        TryLoadMetalLibraryPair(libraryPath, manifestPath, out _);

    private static bool TryLoadMetalLibraryPair(
        string libraryPath,
        string manifestPath,
        out byte[] library)
    {
        try
        {
            library = LoadAndValidateMetalLibraryPair(libraryPath, manifestPath);
            return true;
        }
        catch (InvalidDataException)
        {
            library = [];
            return false;
        }
        catch (IOException)
        {
            library = [];
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            library = [];
            return false;
        }
    }

    private static byte[] LoadPinnedMetalLibrary()
    {
        string libraryPath = Path.Combine(AppContext.BaseDirectory, MetalLibraryFileName);
        string manifestPath = Path.Combine(AppContext.BaseDirectory, MetalManifestFileName);
        return LoadAndValidateMetalLibraryPair(libraryPath, manifestPath);
    }

    private static byte[] ValidateMetalLibraryPairForTesting(
        string libraryPath,
        string manifestPath) =>
        LoadAndValidateMetalLibraryPair(libraryPath, manifestPath);

    private static byte[] LoadAndValidateMetalLibraryPair(
        string libraryPath,
        string manifestPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(libraryPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(manifestPath);

        byte[] library = File.ReadAllBytes(libraryPath);
        byte[] manifest = File.ReadAllBytes(manifestPath);
        string libraryHash = GetSha256(library);
        string manifestHash = GetSha256(manifest);
        string cacheKey = string.Concat(
            Path.GetFullPath(libraryPath),
            "\n",
            Path.GetFullPath(manifestPath),
            "\n",
            libraryHash,
            "\n",
            manifestHash);

        lock (MetalValidationSync)
        {
            if (MetalValidationCache.TryGetValue(cacheKey, out string? cachedError))
            {
                if (cachedError is not null)
                {
                    throw new InvalidDataException(cachedError);
                }
                return library;
            }
        }

        string? error = null;
        try
        {
            ValidateMetalManifest(manifest, libraryHash, library.LongLength);
        }
        catch (JsonException exception)
        {
            error = $"The pinned {MetalManifestFileName} is not valid JSON: " +
                exception.Message;
        }
        catch (InvalidDataException exception)
        {
            error = exception.Message;
        }

        lock (MetalValidationSync)
        {
            if (MetalValidationCache.Count >= 64)
            {
                MetalValidationCache.Clear();
            }
            MetalValidationCache[cacheKey] = error;
        }
        if (error is not null)
        {
            throw new InvalidDataException(error);
        }
        return library;
    }

    private static void ValidateMetalManifest(
        ReadOnlyMemory<byte> manifest,
        string libraryHash,
        long librarySize)
    {
        if (librarySize == 0)
        {
            throw new InvalidDataException("The pinned Metal library is empty.");
        }
        using JsonDocument document = JsonDocument.Parse(manifest);
        JsonElement root = document.RootElement;
        RequireObject(root, "Metal sidecar");
        RequireExactProperties(
            root,
            "Metal sidecar",
            "schemaVersion",
            "rid",
            "checkedRoot",
            "payloadRoot",
            "stagedManifestPath",
            "toolchain",
            "provenance",
            "library");
        if (RequireInt32(root, "schemaVersion") != MetalManifestSchemaVersion)
        {
            throw new InvalidDataException(
                $"The pinned Metal sidecar must use schemaVersion {MetalManifestSchemaVersion}.");
        }
        RequireString(root, "rid", "osx-arm64");
        RequireString(root, "checkedRoot", "eng/shaders/checked");
        string payloadRoot = RequireRelativePath(root, "payloadRoot");
        RequireString(
            root,
            "stagedManifestPath",
            "eng/shaders/checked/mesh.metallib.manifest.json");
        RequireObject(RequireProperty(root, "toolchain"), "Metal toolchain");
        RequireArray(RequireProperty(root, "provenance"), "Metal provenance");

        JsonElement library = RequireProperty(root, "library");
        RequireObject(library, "Metal sidecar library");
        RequireExactProperties(
            library,
            "Metal sidecar library",
            "name",
            "path",
            "stagedPath",
            "sha256",
            "size",
            "sources",
            "air",
            "entryPoints",
            "symbolDump",
            "symbolDumpSha256",
            "symbolDumpSize",
            "commands");
        RequireString(library, "name", "mesh");
        RequireString(library, "path", MetalLibraryFileName);
        RequireString(
            library,
            "stagedPath",
            "eng/shaders/checked/mesh.metallib");
        RequireString(library, "sha256", libraryHash);
        if (RequireInt64(library, "size") != librarySize)
        {
            throw new InvalidDataException(
                "The pinned Metal library size does not match its sidecar.");
        }

        MetalShaderIdentity[] expected = ExpectedMetalIdentities.Value;
        ValidateEntryPoints(RequireProperty(library, "entryPoints"), expected);
        ValidateSources(RequireProperty(library, "sources"), expected);
        ValidateAir(RequireProperty(library, "air"), payloadRoot, expected);
    }

    private static void ValidateEntryPoints(
        JsonElement entryPoints,
        MetalShaderIdentity[] expected)
    {
        RequireArray(entryPoints, "Metal entryPoints");
        if (entryPoints.GetArrayLength() != expected.Length)
        {
            throw new InvalidDataException(
                $"The pinned Metal sidecar must contain exactly {expected.Length} entry points.");
        }

        int index = 0;
        foreach (JsonElement element in entryPoints.EnumerateArray())
        {
            RequireObject(element, "Metal entry point");
            RequireExactProperties(
                element,
                "Metal entry point",
                "programName",
                "name",
                "stage");
            string programName = RequireString(element, "programName");
            string name = RequireString(element, "name");
            string stage = RequireString(element, "stage");
            MetalShaderIdentity expectedIdentity = expected[index++];
            if (expectedIdentity.ProgramName != programName ||
                expectedIdentity.EntryPoint != name ||
                expectedIdentity.Stage != stage)
            {
                throw new InvalidDataException(
                    "The pinned Metal sidecar entry points are not in the exact " +
                    "checked shader contract order.");
            }
        }
    }

    private static void ValidateSources(
        JsonElement sources,
        MetalShaderIdentity[] expected)
    {
        RequireArray(sources, "Metal sources");
        if (sources.GetArrayLength() != expected.Length)
        {
            throw new InvalidDataException(
                $"The pinned Metal sidecar must contain exactly {expected.Length} sources.");
        }

        int index = 0;
        foreach (JsonElement element in sources.EnumerateArray())
        {
            RequireObject(element, "Metal source");
            RequireExactProperties(
                element,
                "Metal source",
                "programName",
                "path",
                "sha256",
                "size",
                "entryPoint",
                "stage");
            var actual = new MetalShaderIdentity(
                RequireString(element, "programName"),
                RequireString(element, "path"),
                RequireString(element, "sha256"),
                RequireInt64(element, "size"),
                RequireString(element, "entryPoint"),
                RequireString(element, "stage"));
            if (expected[index++] != actual)
            {
                throw new InvalidDataException(
                    "The pinned Metal sidecar sources are stale or not in the exact " +
                    "checked shader contract order.");
            }
        }
    }

    private static void ValidateAir(
        JsonElement air,
        string payloadRoot,
        MetalShaderIdentity[] expected)
    {
        RequireArray(air, "Metal AIR records");
        if (air.GetArrayLength() != expected.Length)
        {
            throw new InvalidDataException(
                $"The pinned Metal sidecar must contain exactly {expected.Length} AIR records.");
        }

        int index = 0;
        foreach (JsonElement element in air.EnumerateArray())
        {
            RequireObject(element, "Metal AIR record");
            RequireExactProperties(
                element,
                "Metal AIR record",
                "programName",
                "path",
                "sha256",
                "size",
                "entryPoint",
                "stage");
            MetalShaderIdentity identity = expected[index++];
            if (RequireString(element, "programName") != identity.ProgramName ||
                RequireString(element, "path") !=
                    $"{payloadRoot}/{identity.ProgramName}.air" ||
                RequireString(element, "entryPoint") != identity.EntryPoint ||
                RequireString(element, "stage") != identity.Stage ||
                !IsSha256(RequireString(element, "sha256")) ||
                RequireInt64(element, "size") <= 0)
            {
                throw new InvalidDataException(
                    "The pinned Metal AIR records do not match the exact checked " +
                    "shader contract order.");
            }
        }
    }

    private static MetalShaderIdentity[] LoadExpectedMetalIdentities()
    {
        using JsonDocument document = JsonDocument.Parse(LoadEmbedded("manifest.json"));
        JsonElement programs = RequireProperty(document.RootElement, "programs");
        RequireArray(programs, "checked shader programs");
        var identities = new List<MetalShaderIdentity>(10);
        foreach (JsonElement program in programs.EnumerateArray())
        {
            string programName = RequireString(program, "name");
            if (programName is not ("mesh.vertex" or "mesh.fragment" or
                "pick.vertex" or "pick.fragment" or
                "selection.mask.vertex" or "selection.mask.fragment" or
                "selection.outline.vertex" or "selection.outline.fragment" or
                "compute.fill" or "compute.scale"))
            {
                continue;
            }
            JsonElement metal = RequireProperty(
                RequireProperty(program, "artifacts"),
                "metal");
            identities.Add(new MetalShaderIdentity(
                programName,
                RequireString(metal, "path"),
                RequireString(metal, "sha256"),
                RequireInt64(metal, "size"),
                RequireString(program, "entryPoint"),
                RequireString(program, "stage")));
        }
        if (identities.Count != 10 || identities.Select(item => item.ProgramName)
            .Distinct(StringComparer.Ordinal).Count() != 10)
        {
            throw new InvalidDataException(
                "The embedded checked shader manifest does not contain the ten Metal programs.");
        }
        return [.. identities];
    }

    private static JsonElement RequireProperty(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out JsonElement value))
        {
            throw new InvalidDataException(
                $"The pinned Metal sidecar is missing required property '{name}'.");
        }
        return value;
    }

    private static void RequireObject(JsonElement element, string description)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidDataException($"{description} must be an object.");
        }
    }

    private static void RequireArray(JsonElement element, string description)
    {
        if (element.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidDataException($"{description} must be an array.");
        }
    }

    private static void RequireExactProperties(
        JsonElement element,
        string description,
        params string[] expected)
    {
        var actual = new HashSet<string>(StringComparer.Ordinal);
        foreach (JsonProperty property in element.EnumerateObject())
        {
            if (!actual.Add(property.Name))
            {
                throw new InvalidDataException(
                    $"{description} contains duplicate property '{property.Name}'.");
            }
        }
        if (actual.Count != expected.Length ||
            expected.Any(name => !actual.Contains(name)))
        {
            throw new InvalidDataException(
                $"{description} properties do not match schemaVersion 4.");
        }
    }

    private static string RequireString(JsonElement element, string name)
    {
        JsonElement value = RequireProperty(element, name);
        if (value.ValueKind != JsonValueKind.String)
        {
            throw new InvalidDataException(
                $"The pinned Metal sidecar property '{name}' must be a string.");
        }
        return value.GetString()!;
    }

    private static void RequireString(
        JsonElement element,
        string name,
        string expected)
    {
        if (!string.Equals(
                RequireString(element, name),
                expected,
                StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                $"The pinned Metal sidecar property '{name}' does not match " +
                "the checked shader contract.");
        }
    }

    private static int RequireInt32(JsonElement element, string name)
    {
        JsonElement value = RequireProperty(element, name);
        if (!value.TryGetInt32(out int result))
        {
            throw new InvalidDataException(
                $"The pinned Metal sidecar property '{name}' must be an integer.");
        }
        return result;
    }

    private static long RequireInt64(JsonElement element, string name)
    {
        JsonElement value = RequireProperty(element, name);
        if (!value.TryGetInt64(out long result))
        {
            throw new InvalidDataException(
                $"The pinned Metal sidecar property '{name}' must be an integer.");
        }
        return result;
    }

    private static string RequireRelativePath(JsonElement element, string name)
    {
        string path = RequireString(element, name);
        if (path.Length == 0 ||
            path[0] is '/' or '\\' ||
            path.Contains('\\', StringComparison.Ordinal) ||
            path.Contains(':', StringComparison.Ordinal) ||
            path.Split('/').Contains("..", StringComparer.Ordinal))
        {
            throw new InvalidDataException(
                $"The pinned Metal sidecar property '{name}' must be a POSIX relative path.");
        }
        return path;
    }

    private static string GetSha256(ReadOnlySpan<byte> bytes) =>
        Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private static bool IsSha256(string value) =>
        value.Length == 64 &&
        value.All(character =>
            character is >= '0' and <= '9' or >= 'a' and <= 'f');

    private sealed record MetalShaderIdentity(
        string ProgramName,
        string Path,
        string Sha256,
        long Size,
        string EntryPoint,
        string Stage);
}
