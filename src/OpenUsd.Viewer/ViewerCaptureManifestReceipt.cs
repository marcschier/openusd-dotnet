// Copyright (c) marcschier. Licensed under the MIT License.

using System.Numerics;
using System.Security.Cryptography;
using System.Text.Json;
using OpenUsd.Rendering;

namespace OpenUsd.Viewer;

/// <summary>
/// Derives a capture state receipt from the render-job manifest beside a sequence or product frame.
/// </summary>
/// <remarks>
/// The manifest entry is accepted only when its SHA-256 matches the frame file bytes and those bytes decode
/// to the displayed pixels. The manifest does not name the backend, so the receipt records it as unrecorded.
/// </remarks>
internal static class ViewerCaptureManifestReceipt
{
    internal const string ManifestName = "manifest.json";
    internal const int MaximumManifestBytes = 32 * 1024 * 1024;

    internal static async Task<ViewerCaptureStateReceiptResult> ReadAsync(
        string capturePath, ViewerCaptureImage image, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(image);
        cancellationToken.ThrowIfCancellationRequested();
        string fullPath = Path.GetFullPath(capturePath);
        string manifestPath = Path.Combine(Path.GetDirectoryName(fullPath)!, ManifestName);
        byte[]? manifest = await ReadBoundedAsync(manifestPath, MaximumManifestBytes, cancellationToken)
            .ConfigureAwait(false);
        if (manifest is null)
        {
            return ViewerCaptureStateReceiptResult.Missing;
        }
        if (manifest.Length == 0)
        {
            return Invalid;
        }

        ManifestFrame? frame;
        try
        {
            frame = Parse(manifest, Path.GetFileName(fullPath));
        }
        catch (InvalidDataException)
        {
            return Invalid;
        }
        if (frame is null)
        {
            return ViewerCaptureStateReceiptResult.Missing;
        }

        byte[]? encoded = await ReadBoundedAsync(fullPath, ViewerCaptureComparison.MaximumFileBytes,
            cancellationToken).ConfigureAwait(false);
        if (encoded is not { Length: > 0 } ||
            !string.Equals(Convert.ToHexString(SHA256.HashData(encoded)), frame.FileSha256,
                StringComparison.OrdinalIgnoreCase) ||
            frame.Receipt.Width != image.Width || frame.Receipt.Height != image.Height)
        {
            return Stale;
        }
        try
        {
            PngRgba8Image decoded = PngRgba8Reader.Decode(encoded, cancellationToken);
            if (!decoded.Pixels.AsSpan().SequenceEqual(image.Rgba))
            {
                return Stale;
            }
        }
        catch (InvalidDataException)
        {
            return Stale;
        }
        return new ViewerCaptureStateReceiptResult(ViewerCaptureStateReceiptStatus.Verified,
            frame.Receipt with { PixelSha256 = ViewerCaptureStateReceiptFile.HashPixels(image) });
    }

    private static ViewerCaptureStateReceiptResult Invalid { get; } =
        new(ViewerCaptureStateReceiptStatus.Invalid, null);

    private static ViewerCaptureStateReceiptResult Stale { get; } =
        new(ViewerCaptureStateReceiptStatus.Stale, null);

    /// <summary>
    /// Returns <see langword="null"/> when the file is absent, or empty when unreadable or oversized.
    /// </summary>
    private static async Task<byte[]?> ReadBoundedAsync(
        string path, int maximumBytes, CancellationToken cancellationToken)
    {
        try
        {
            await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
                4096, FileOptions.Asynchronous | FileOptions.SequentialScan);
            if (stream.Length is <= 0 || stream.Length > maximumBytes)
            {
                return [];
            }
            byte[] bytes = new byte[checked((int)stream.Length)];
            await stream.ReadExactlyAsync(bytes, cancellationToken).ConfigureAwait(false);
            return bytes;
        }
        catch (FileNotFoundException)
        {
            return null;
        }
        catch (DirectoryNotFoundException)
        {
            return null;
        }
        catch (IOException)
        {
            return [];
        }
        catch (UnauthorizedAccessException)
        {
            return [];
        }
    }

    /// <summary>Finds the frame entry for <paramref name="fileName"/>, or <see langword="null"/> when absent.</summary>
    internal static ManifestFrame? Parse(ReadOnlySpan<byte> manifest, string fileName)
    {
        try
        {
            var reader = new Utf8JsonReader(manifest, new JsonReaderOptions { MaxDepth = 16 });
            using JsonDocument document = JsonDocument.ParseValue(ref reader);
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty("schemaVersion", out JsonElement version) || version.GetInt32() != 1 ||
                !root.TryGetProperty("kind", out JsonElement kind) || kind.GetString() != "rgba8-sequence")
            {
                throw new InvalidDataException("The render-job manifest schema is not supported.");
            }
            foreach (JsonElement frame in root.GetProperty("frames").EnumerateArray())
            {
                if (string.Equals(frame.GetProperty("file").GetString(), fileName, StringComparison.Ordinal))
                {
                    return ReadFrame(frame);
                }
            }
            return null;
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("The render-job manifest JSON is invalid.", exception);
        }
        catch (InvalidOperationException exception)
        {
            throw new InvalidDataException("The render-job manifest contains an invalid value type.", exception);
        }
        catch (KeyNotFoundException exception)
        {
            throw new InvalidDataException("The render-job manifest frame is missing a field.", exception);
        }
        catch (FormatException exception)
        {
            throw new InvalidDataException("The render-job manifest contains an invalid number.", exception);
        }
        catch (ArgumentException exception)
        {
            throw new InvalidDataException("The render-job manifest contains invalid render state.", exception);
        }
    }

    private static ManifestFrame ReadFrame(JsonElement frame)
    {
        double timeCode = frame.GetProperty("timeCode").GetDouble();
        if (!double.IsFinite(timeCode))
        {
            throw new InvalidDataException("The render-job manifest time code is not finite.");
        }
        string hash = frame.GetProperty("sha256").GetString() ?? string.Empty;
        if (hash.Length != 64 || hash.Any(character => !char.IsAsciiHexDigit(character)))
        {
            throw new InvalidDataException("The render-job manifest frame checksum is invalid.");
        }
        var receipt = new ViewerCaptureStateReceipt(
            ViewerCaptureStateReceiptFile.ValidateField(frame.GetProperty("stage").GetString(), allowEmpty: true),
            timeCode,
            ViewerCaptureStateReceiptFile.ValidateField(ReadCamera(frame.GetProperty("camera"))),
            ViewerCaptureStateReceiptFile.ValidateField(ReadDisplay(frame.GetProperty("display"))),
            ViewerCaptureStateReceiptFile.ValidateField(ReadSettings(frame.GetProperty("settings"))),
            ViewerCaptureStateReceipt.UnrecordedBackend,
            ViewerCaptureStateReceiptFile.ValidateDimension(frame.GetProperty("width").GetInt32()),
            ViewerCaptureStateReceiptFile.ValidateDimension(frame.GetProperty("height").GetInt32()),
            new string('0', 64));
        return new ManifestFrame(receipt, hash);
    }

    private static string ReadCamera(JsonElement camera)
    {
        CameraMode mode = ReadEnum<CameraMode>(camera.GetProperty("mode"));
        Matrix4x4 view = ReadMatrix(camera.GetProperty("view"));
        Matrix4x4 projection = ReadMatrix(camera.GetProperty("projection"));
        JsonElement planes = camera.GetProperty("clipPlanes");
        if (planes.GetArrayLength() > CameraState.MaxClipPlanes)
        {
            throw new InvalidDataException("The render-job manifest has too many clip planes.");
        }
        var clipPlanes = new List<Vector4>(planes.GetArrayLength());
        foreach (JsonElement plane in planes.EnumerateArray())
        {
            clipPlanes.Add(ReadVector(plane));
        }
        return ViewerCaptureStateReceipt.DescribeCamera(mode, view, projection, clipPlanes);
    }

    private static string ReadDisplay(JsonElement display)
    {
        uint purposes = display.GetProperty("purposes").GetUInt32();
        const uint allPurposes = (uint)(RenderPurpose.Default | RenderPurpose.Proxy | RenderPurpose.Render |
            RenderPurpose.Guide);
        if ((purposes & ~allPurposes) != 0)
        {
            throw new InvalidDataException("The render-job manifest names an unknown purpose.");
        }
        return ViewerCaptureStateReceipt.DescribeDisplay(new SceneDisplayState(
            (RenderPurpose)purposes,
            ReadEnum<RenderVisibility>(display.GetProperty("visibility")),
            ReadEnum<RenderDrawMode>(display.GetProperty("drawMode"))));
    }

    private static string ReadSettings(JsonElement settings)
    {
        var parsed = new RenderSettings(
            settings.GetProperty("samplesPerPixel").GetInt32(),
            settings.GetProperty("lighting").GetBoolean(),
            settings.GetProperty("shadows").GetBoolean(),
            ReadVector(settings.GetProperty("clearColor")),
            settings.GetProperty("backfaceCulling").GetBoolean(),
            settings.GetProperty("sceneMaterials").GetBoolean(),
            ReadEnum<RenderComplexity>(settings.GetProperty("complexity")),
            ReadEnum<RenderOutputTransform>(settings.GetProperty("outputTransform")),
            ReadFloat(settings.GetProperty("exposure")));
        JsonElement transform = settings.GetProperty("displayTransform");
        string displayTransform = transform.ValueKind == JsonValueKind.Null
            ? "none"
            : ViewerCaptureStateReceipt.DescribeDisplayTransform(
                RequireString(transform.GetProperty("configPath")),
                RequireString(transform.GetProperty("sourceColorSpace")),
                transform.GetProperty("display").GetString(),
                transform.GetProperty("view").GetString(),
                transform.GetProperty("look").GetString(),
                transform.GetProperty("latticeSize").GetInt32(),
                ReadFloat(transform.GetProperty("shaperMinimumLog2")),
                ReadFloat(transform.GetProperty("shaperMaximumLog2")));
        return ViewerCaptureStateReceipt.DescribeRenderSettings(parsed, displayTransform);
    }

    private static string RequireString(JsonElement value) =>
        value.GetString() ?? throw new InvalidDataException("The render-job manifest omits a required name.");

    private static T ReadEnum<T>(JsonElement value) where T : struct, Enum
    {
        string? text = value.GetString();
        if (text is null || !Enum.TryParse(text, ignoreCase: false, out T parsed) ||
            !string.Equals(Enum.GetName(parsed), text, StringComparison.Ordinal))
        {
            throw new InvalidDataException("The render-job manifest names an unknown enumeration value.");
        }
        return parsed;
    }

    private static float ReadFloat(JsonElement value)
    {
        float parsed = value.GetSingle();
        if (!float.IsFinite(parsed))
        {
            throw new InvalidDataException("The render-job manifest contains a non-finite number.");
        }
        return parsed;
    }

    private static Vector4 ReadVector(JsonElement value)
    {
        Span<float> values = stackalloc float[4];
        ReadFloats(value, values);
        return new Vector4(values[0], values[1], values[2], values[3]);
    }

    private static Matrix4x4 ReadMatrix(JsonElement value)
    {
        Span<float> m = stackalloc float[16];
        ReadFloats(value, m);
        return new Matrix4x4(m[0], m[1], m[2], m[3], m[4], m[5], m[6], m[7],
            m[8], m[9], m[10], m[11], m[12], m[13], m[14], m[15]);
    }

    private static void ReadFloats(JsonElement value, Span<float> destination)
    {
        if (value.GetArrayLength() != destination.Length)
        {
            throw new InvalidDataException("The render-job manifest contains a vector of the wrong length.");
        }
        int index = 0;
        foreach (JsonElement element in value.EnumerateArray())
        {
            destination[index++] = ReadFloat(element);
        }
    }

    internal sealed record ManifestFrame(ViewerCaptureStateReceipt Receipt, string FileSha256);
}
