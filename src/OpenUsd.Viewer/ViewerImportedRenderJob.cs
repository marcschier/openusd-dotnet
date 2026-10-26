// Copyright (c) marcschier. Licensed under the MIT License.

using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using OpenUsd.Rendering;

namespace OpenUsd.Viewer;

internal sealed record ViewerImportedRenderFrame(
    int Index, double TimeCode, string FileName, ViewportDimensions Dimensions, long Bytes, string Sha256,
    string StageClaim, string CameraClaim, string RequestClaim, string SimulationClaim);

internal sealed class ViewerImportedRenderJob
{
    internal const int MaximumManifestBytes = 24 * 1024 * 1024;
    private readonly string _manifestHash;
    private readonly int _manifestBytes;

    private ViewerImportedRenderJob(
        string manifestPath, byte[] bytes, IReadOnlyList<ViewerImportedRenderFrame> frames, string metadataClaim)
    {
        ManifestPath = manifestPath;
        OutputDirectory = Path.GetDirectoryName(manifestPath)!;
        _manifestHash = Convert.ToHexString(SHA256.HashData(bytes));
        _manifestBytes = bytes.Length;
        Frames = frames;
        MetadataClaim = metadataClaim;
    }

    internal string ManifestPath { get; }
    internal string OutputDirectory { get; }
    internal IReadOnlyList<ViewerImportedRenderFrame> Frames { get; }
    internal string MetadataClaim { get; }

    internal static Task<ViewerImportedRenderJob> ReadAsync(string path, CancellationToken cancellationToken) =>
        Task.Run(() => Read(path, cancellationToken), cancellationToken);

    private static ViewerImportedRenderJob Read(string path, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path) || path.Length > 4096 ||
            path.Contains('\0') || path.StartsWith(@"\\", StringComparison.Ordinal) ||
            path.StartsWith("//", StringComparison.Ordinal))
        {
            throw new InvalidDataException("Choose a bounded absolute local render manifest path.");
        }
        string canonical = OperatingSystem.IsWindows()
            ? ViewerWindowsFiles.NormalizePath(path) : Path.GetFullPath(path);
        if (!string.Equals(Path.GetFileName(canonical), "manifest.json", StringComparison.Ordinal))
        {
            throw new InvalidDataException("Choose the original manifest.json inside its completed job directory.");
        }
        ViewerCompletedJobPreview.RejectReparsePoints(canonical);
        using IDisposable? parent = OperatingSystem.IsWindows()
            ? ViewerWindowsFiles.OpenDirectory(Path.GetDirectoryName(canonical)!) : null;
        using FileStream input = OpenRead(canonical);
        if (input.Length is < 1 or > MaximumManifestBytes)
        {
            throw new InvalidDataException("The imported manifest must contain 1 byte through 24 MiB.");
        }
        byte[] bytes = new byte[checked((int)input.Length)];
        for (int offset = 0; offset < bytes.Length;)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int count = Math.Min(64 * 1024, bytes.Length - offset);
            input.ReadExactly(bytes.AsSpan(offset, count));
            offset += count;
        }
        if (input.Length != bytes.Length || input.ReadByte() != -1)
        {
            throw new InvalidDataException("The manifest changed while being imported.");
        }
        try
        {
            using JsonDocument json = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 32 });
            RejectDuplicateProperties(json.RootElement, cancellationToken);
            JsonElement root = json.RootElement;
            if (root.GetProperty("schemaVersion").GetInt32() != 1 ||
                root.GetProperty("kind").GetString() != "rgba8-sequence")
            {
                throw new NotSupportedException("Only version 1 rgba8-sequence render manifests can be imported.");
            }
            JsonElement frames = root.GetProperty("frames");
            int count = frames.GetArrayLength();
            if (count is < 1 or > 4096)
            {
                throw new InvalidDataException("An imported job requires 1-4096 recorded frames.");
            }
            string jobClaim = string.Join("\n", root.EnumerateObject()
                .Where(static property => property.Name != "frames")
                .Select(property => Claim(root, property.Name)));
            if (jobClaim.Length > 32 * 1024)
            {
                throw new InvalidDataException("Imported job metadata exceeds the 32768-character bound.");
            }
            var detached = new ViewerImportedRenderFrame[count];
            for (int index = 0; index < count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                JsonElement frame = frames[index];
                string frameClaim = frame.GetRawText();
                if (frameClaim.Length > 32 * 1024)
                {
                    throw new InvalidDataException("An imported frame record exceeds the 32768-character bound.");
                }
                string name = $"frame-{index.ToString("D6", CultureInfo.InvariantCulture)}.png";
                if (frame.GetProperty("index").GetInt32() != index || frame.GetProperty("file").GetString() != name)
                {
                    throw new InvalidDataException(
                        "Imported frame indices and generated PNG names must match job order.");
                }
                double time = frame.GetProperty("timeCode").GetDouble();
                int width = frame.GetProperty("width").GetInt32();
                int height = frame.GetProperty("height").GetInt32();
                long length = frame.GetProperty("bytes").GetInt64();
                string hash = RequiredString(frame, "sha256", 64);
                if (!double.IsFinite(time) || width is < 1 or > 8192 || height is < 1 or > 8192 ||
                    (long)width * height * 4 > RenderDiskJobLimits.Default.MaximumFrameBytes ||
                    length < 1 || length > RenderDiskJobLimits.Default.MaximumFrameBytes ||
                    hash.Length != 64 || !hash.All(Uri.IsHexDigit))
                {
                    throw new InvalidDataException(
                        "An imported frame exceeds its time, dimensions, bytes or hash bounds.");
                }
                detached[index] = new ViewerImportedRenderFrame(
                    index, time, name, new ViewportDimensions(width, height), length, hash,
                    RequiredString(frame, "stage", 4096),
                    Claim(frame, "camera"),
                    "Unverified manifest claims; not reconstructed or effective renderer state. " +
                    "Only recorded keys follow; absent fields are unknown.\nFrame record:\n" + frameClaim,
                    Claim(frame, "simulation"));
            }
            ViewerCompletedJobPreview.RejectReparsePoints(canonical);
            return new ViewerImportedRenderJob(canonical, bytes, Array.AsReadOnly(detached), jobClaim);
        }
        catch (Exception exception) when (exception is JsonException or KeyNotFoundException or
            InvalidOperationException or FormatException or OverflowException)
        {
            throw new InvalidDataException("The imported render manifest is malformed or missing required metadata.",
                exception);
        }
    }

    internal void VerifyManifest(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ViewerCompletedJobPreview.RejectReparsePoints(ManifestPath);
        using FileStream input = OpenRead(ManifestPath);
        if (input.Length != _manifestBytes)
        {
            throw new InvalidDataException("The imported manifest extent changed. Import it again explicitly.");
        }
        using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        byte[] block = new byte[64 * 1024];
        int remaining = _manifestBytes;
        while (remaining > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int count = Math.Min(remaining, block.Length);
            input.ReadExactly(block.AsSpan(0, count));
            hash.AppendData(block, 0, count);
            remaining -= count;
        }
        if (input.ReadByte() != -1 || input.Length != _manifestBytes ||
            Convert.ToHexString(hash.GetHashAndReset()) != _manifestHash)
        {
            throw new InvalidDataException("The imported manifest changed. Import it again explicitly.");
        }
        ViewerCompletedJobPreview.RejectReparsePoints(ManifestPath);
    }

    private static string RequiredString(JsonElement element, string name, int maximumLength)
    {
        string? value = element.GetProperty(name).GetString();
        if (string.IsNullOrWhiteSpace(value) || value.Length > maximumLength || value.Contains('\0'))
        {
            throw new InvalidDataException($"The imported {name} is missing or exceeds its text bound.");
        }
        return value;
    }

    private static string Claim(JsonElement frame, string name)
    {
        if (!frame.TryGetProperty(name, out JsonElement value))
        {
            return $"{name}: not recorded in this manifest.";
        }
        string text = value.GetRawText();
        if (text.Length > 32 * 1024)
        {
            throw new InvalidDataException($"The imported {name} exceeds the 32768-character bound.");
        }
        return $"{name} (unverified manifest claim):\n{text}";
    }

    private static void RejectDuplicateProperties(JsonElement value, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (value.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (JsonProperty property in value.EnumerateObject())
            {
                if (!names.Add(property.Name))
                {
                    throw new InvalidDataException("Duplicate manifest properties are not supported.");
                }
                RejectDuplicateProperties(property.Value, cancellationToken);
            }
        }
        else if (value.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement item in value.EnumerateArray())
            {
                RejectDuplicateProperties(item, cancellationToken);
            }
        }
    }

    internal static FileStream OpenRead(string path) => OperatingSystem.IsWindows()
        ? ViewerWindowsFiles.OpenRead(path)
        : new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024,
            FileOptions.SequentialScan);
}
