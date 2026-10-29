// Copyright (c) marcschier. Licensed under the MIT License.

using System.Globalization;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using OpenUsd.Rendering;

namespace OpenUsd.Viewer;

/// <summary>
/// Records the effective Viewer state that produced one saved capture, bound to its decoded pixels.
/// </summary>
internal sealed record ViewerCaptureStateReceipt(
    string Stage,
    double TimeCode,
    string Camera,
    string Display,
    string RenderSettings,
    string Backend,
    int Width,
    int Height,
    string PixelSha256)
{
    /// <summary>Marks a receipt derived from a render-job manifest, which does not name the backend.</summary>
    internal const string UnrecordedBackend = "unrecorded";

    internal bool RecordsBackend => !string.Equals(Backend, UnrecordedBackend, StringComparison.Ordinal);
    internal static ViewerCaptureStateReceipt From(
        StageRenderState state, RenderBackendIdentity? backend, ViewerCaptureImage image)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(image);
        return new ViewerCaptureStateReceipt(
            state.Stage.Identifier,
            state.Time.TimeCode,
            DescribeCamera(state.Camera),
            DescribeDisplay(state.Display),
            DescribeRenderSettings(state.RenderSettings),
            backend is null ? "none" : $"{backend.Kind}:{backend.Name}",
            image.Width,
            image.Height,
            ViewerCaptureStateReceiptFile.HashPixels(image));
    }

    /// <summary>Names the recorded state fields that differ between two receipts.</summary>
    internal static IReadOnlyList<string> Differences(ViewerCaptureStateReceipt before, ViewerCaptureStateReceipt after)
    {
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(after);
        var differences = new List<string>();
        Add(differences, "stage", before.Stage, after.Stage);
        if (before.TimeCode.CompareTo(after.TimeCode) != 0)
        {
            differences.Add("time");
        }
        Add(differences, "camera", before.Camera, after.Camera);
        Add(differences, "display", before.Display, after.Display);
        Add(differences, "render settings", before.RenderSettings, after.RenderSettings);
        if (before.RecordsBackend && after.RecordsBackend)
        {
            Add(differences, "backend", before.Backend, after.Backend);
        }
        if (before.Width != after.Width || before.Height != after.Height)
        {
            differences.Add("size");
        }
        return differences;
    }

    private static void Add(List<string> differences, string name, string before, string after)
    {
        if (!string.Equals(before, after, StringComparison.Ordinal))
        {
            differences.Add(name);
        }
    }

    internal static string DescribeCamera(CameraState camera) =>
        DescribeCamera(camera.Mode, camera.View, camera.Projection, camera.ClipPlanes);

    internal static string DescribeCamera(
        CameraMode mode, Matrix4x4 view, Matrix4x4 projection, IReadOnlyList<Vector4> clipPlanes)
    {
        var text = new StringBuilder();
        text.Append(mode.ToString());
        text.Append(";view=");
        AppendMatrix(text, view);
        text.Append(";projection=");
        AppendMatrix(text, projection);
        text.Append(";clip=");
        for (int index = 0; index < clipPlanes.Count; index++)
        {
            Vector4 plane = clipPlanes[index];
            if (index > 0)
            {
                text.Append('|');
            }
            AppendFloats(text, [plane.X, plane.Y, plane.Z, plane.W]);
        }
        return text.ToString();
    }

    internal static string DescribeDisplay(SceneDisplayState display) =>
        $"purposes={display.Purposes};visibility={display.Visibility};drawMode={display.DrawMode}";

    internal static string DescribeRenderSettings(RenderSettings settings) =>
        DescribeRenderSettings(settings, settings.DisplayTransform is { } transform
            ? DescribeDisplayTransform(transform.ConfigPath, transform.SourceColorSpace, transform.Display,
                transform.View, transform.Look, transform.LatticeSize, transform.ShaperMinimumLog2,
                transform.ShaperMaximumLog2)
            : "none");

    internal static string DescribeDisplayTransform(
        string configPath, string sourceColorSpace, string? display, string? view, string? look, int latticeSize,
        float shaperMinimumLog2, float shaperMaximumLog2)
    {
        var text = new StringBuilder();
        text.Append(CultureInfo.InvariantCulture, $"{configPath}|{sourceColorSpace}|{display}|");
        text.Append(CultureInfo.InvariantCulture, $"{view}|{look}|{latticeSize}|");
        AppendFloats(text, [shaperMinimumLog2, shaperMaximumLog2]);
        return text.ToString();
    }

    /// <summary>Describes render settings whose display transform was already described.</summary>
    /// <remarks>The display transform of <paramref name="settings"/> itself is ignored.</remarks>
    internal static string DescribeRenderSettings(RenderSettings settings, string displayTransform)
    {
        var text = new StringBuilder();
        text.Append(CultureInfo.InvariantCulture,
            $"spp={settings.SamplesPerPixel};lighting={settings.EnableLighting};shadows={settings.EnableShadows}");
        text.Append(";clear=");
        Vector4 clear = settings.ClearColor;
        AppendFloats(text, [clear.X, clear.Y, clear.Z, clear.W]);
        text.Append(CultureInfo.InvariantCulture,
            $";backface={settings.BackfaceCulling};materials={settings.UseSceneMaterials}");
        text.Append(CultureInfo.InvariantCulture,
            $";complexity={settings.Complexity};output={settings.OutputTransform};exposure=");
        AppendFloats(text, [settings.Exposure]);
        text.Append(";ocio=");
        text.Append(displayTransform);
        return text.ToString();
    }

    private static void AppendMatrix(StringBuilder text, Matrix4x4 matrix) =>
        AppendFloats(text,
        [
            matrix.M11, matrix.M12, matrix.M13, matrix.M14,
            matrix.M21, matrix.M22, matrix.M23, matrix.M24,
            matrix.M31, matrix.M32, matrix.M33, matrix.M34,
            matrix.M41, matrix.M42, matrix.M43, matrix.M44
        ]);

    private static void AppendFloats(StringBuilder text, ReadOnlySpan<float> values)
    {
        for (int index = 0; index < values.Length; index++)
        {
            if (index > 0)
            {
                text.Append(',');
            }
            text.Append(values[index].ToString("R", CultureInfo.InvariantCulture));
        }
    }
}

internal enum ViewerCaptureStateReceiptStatus
{
    Missing,
    Verified,
    Stale,
    Invalid
}

internal sealed record ViewerCaptureStateReceiptResult(
    ViewerCaptureStateReceiptStatus Status, ViewerCaptureStateReceipt? Receipt)
{
    internal static ViewerCaptureStateReceiptResult Missing { get; } =
        new(ViewerCaptureStateReceiptStatus.Missing, null);

    /// <summary>Describes what the two receipts establish about a displayed comparison pair.</summary>
    internal static string DescribePair(ViewerCaptureStateReceiptResult before, ViewerCaptureStateReceiptResult after)
    {
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(after);
        if (before.Receipt is { } first && after.Receipt is { } second)
        {
            IReadOnlyList<string> differences = ViewerCaptureStateReceipt.Differences(first, second);
            if (differences.Count != 0)
            {
                return $"Recorded state differs in {string.Join(", ", differences)}.";
            }
            return first.RecordsBackend && second.RecordsBackend
                ? "Recorded state matches: stage, time, camera, display, render settings and backend."
                : "Recorded state matches: stage, time, camera, display and render settings. " +
                    "A render-job manifest does not record the backend.";
        }
        string? problem = Problem("A", before) ?? Problem("B", after);
        return $"{problem} Only pixels are compared.";
    }

    private static string? Problem(string name, ViewerCaptureStateReceiptResult result) => result.Status switch
    {
        ViewerCaptureStateReceiptStatus.Missing => $"{name} has no state receipt.",
        ViewerCaptureStateReceiptStatus.Stale => $"{name}'s state receipt belongs to different pixels.",
        ViewerCaptureStateReceiptStatus.Invalid => $"{name}'s state receipt is invalid.",
        _ => null
    };
}

/// <summary>Reads and writes the <c>.usdstate.json</c> receipt saved beside a Viewer capture.</summary>
internal static class ViewerCaptureStateReceiptFile
{
    internal const string Suffix = ".usdstate.json";
    internal const int MaximumBytes = 16 * 1024;
    internal const int MaximumFieldLength = 4096;
    private const int Version = 1;

    private static readonly string[] Members =
    [
        "version", "stage", "timeCode", "camera", "display", "renderSettings", "backend", "width", "height",
        "pixelSha256"
    ];

    internal static string PathFor(string capturePath) => Path.GetFullPath(capturePath) + Suffix;

    internal static string HashPixels(ViewerCaptureImage image) => Convert.ToHexString(SHA256.HashData(image.Rgba));

    /// <summary>
    /// Writes a capture image, then a receipt hashed from the image as decoded back from disk.
    /// </summary>
    /// <remarks>
    /// The previous receipt is removed once the image is replaced, so a failed receipt write leaves no
    /// receipt rather than one describing state that did not produce the new pixels.
    /// </remarks>
    internal static async Task<ViewerCaptureStateReceipt> SaveCaptureAsync(
        string capturePath, ViewerFrameCaptureResult frame, StageRenderState state, RenderBackendIdentity? backend,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(state);
        await ViewerFrameFileWriter.WriteAsync(capturePath, frame, cancellationToken).ConfigureAwait(false);
        File.Delete(PathFor(capturePath));
        ViewerCaptureImage image = await ViewerCaptureComparison.LoadImageAsync(capturePath, cancellationToken)
            .ConfigureAwait(false);
        ViewerCaptureStateReceipt receipt = ViewerCaptureStateReceipt.From(state, backend, image);
        await WriteAsync(capturePath, receipt, cancellationToken).ConfigureAwait(false);
        return receipt;
    }

    internal static async Task WriteAsync(
        string capturePath, ViewerCaptureStateReceipt receipt, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(receipt);
        cancellationToken.ThrowIfCancellationRequested();
        if (!double.IsFinite(receipt.TimeCode))
        {
            throw new InvalidDataException("A capture state receipt requires a finite time code.");
        }
        string destination = PathFor(capturePath);
        using var bytes = new MemoryStream();
        using (var writer = new Utf8JsonWriter(bytes, new JsonWriterOptions { Indented = true }))
        {
            writer.WriteStartObject();
            writer.WriteNumber("version", Version);
            writer.WriteString("stage", ValidateField(receipt.Stage, allowEmpty: true));
            writer.WriteNumber("timeCode", receipt.TimeCode);
            writer.WriteString("camera", ValidateField(receipt.Camera));
            writer.WriteString("display", ValidateField(receipt.Display));
            writer.WriteString("renderSettings", ValidateField(receipt.RenderSettings));
            writer.WriteString("backend", ValidateField(receipt.Backend));
            writer.WriteNumber("width", ValidateDimension(receipt.Width));
            writer.WriteNumber("height", ValidateDimension(receipt.Height));
            writer.WriteString("pixelSha256", ValidateHash(receipt.PixelSha256));
            writer.WriteEndObject();
        }
        if (bytes.Length > MaximumBytes)
        {
            throw new InvalidDataException("The capture state receipt exceeds its 16 KiB limit.");
        }
        string temporary = Path.Combine(Path.GetDirectoryName(destination)!,
            $".openusd-state-{Guid.NewGuid():N}.tmp");
        bool created = false;
        try
        {
            await using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write,
                FileShare.None, 4096, FileOptions.Asynchronous))
            {
                created = true;
                bytes.Position = 0;
                await bytes.CopyToAsync(stream, cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            }
            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporary, destination, overwrite: true);
        }
        finally
        {
            if (created)
            {
                File.Delete(temporary);
            }
        }
    }

    /// <summary>
    /// Reads the receipt beside a capture, or the render-job manifest entry when no receipt was saved.
    /// </summary>
    internal static async Task<ViewerCaptureStateReceiptResult> ReadForComparisonAsync(
        string capturePath, ViewerCaptureImage image, CancellationToken cancellationToken)
    {
        ViewerCaptureStateReceiptResult result =
            await ReadAsync(capturePath, image, cancellationToken).ConfigureAwait(false);
        return result.Status == ViewerCaptureStateReceiptStatus.Missing
            ? await ViewerCaptureManifestReceipt.ReadAsync(capturePath, image, cancellationToken)
                .ConfigureAwait(false)
            : result;
    }

    /// <summary>
    /// Reads the receipt beside a capture and verifies it against the capture's decoded pixels.
    /// </summary>
    internal static async Task<ViewerCaptureStateReceiptResult> ReadAsync(
        string capturePath, ViewerCaptureImage image, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(image);
        cancellationToken.ThrowIfCancellationRequested();
        string path = PathFor(capturePath);
        byte[] bytes;
        try
        {
            await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
                4096, FileOptions.Asynchronous | FileOptions.SequentialScan);
            if (stream.Length is <= 0 or > MaximumBytes)
            {
                return new ViewerCaptureStateReceiptResult(ViewerCaptureStateReceiptStatus.Invalid, null);
            }
            bytes = new byte[checked((int)stream.Length)];
            await stream.ReadExactlyAsync(bytes, cancellationToken).ConfigureAwait(false);
        }
        catch (FileNotFoundException)
        {
            return ViewerCaptureStateReceiptResult.Missing;
        }
        catch (DirectoryNotFoundException)
        {
            return ViewerCaptureStateReceiptResult.Missing;
        }
        catch (EndOfStreamException)
        {
            return new ViewerCaptureStateReceiptResult(ViewerCaptureStateReceiptStatus.Invalid, null);
        }
        catch (IOException)
        {
            return new ViewerCaptureStateReceiptResult(ViewerCaptureStateReceiptStatus.Invalid, null);
        }
        catch (UnauthorizedAccessException)
        {
            return new ViewerCaptureStateReceiptResult(ViewerCaptureStateReceiptStatus.Invalid, null);
        }

        ViewerCaptureStateReceipt receipt;
        try
        {
            receipt = Parse(bytes);
        }
        catch (InvalidDataException)
        {
            return new ViewerCaptureStateReceiptResult(ViewerCaptureStateReceiptStatus.Invalid, null);
        }
        bool matches = receipt.Width == image.Width && receipt.Height == image.Height &&
            string.Equals(receipt.PixelSha256, HashPixels(image), StringComparison.OrdinalIgnoreCase);
        return matches
            ? new ViewerCaptureStateReceiptResult(ViewerCaptureStateReceiptStatus.Verified, receipt)
            : new ViewerCaptureStateReceiptResult(ViewerCaptureStateReceiptStatus.Stale, null);
    }

    internal static ViewerCaptureStateReceipt Parse(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length is 0 or > MaximumBytes)
        {
            throw new InvalidDataException("A capture state receipt must be between 1 byte and 16 KiB.");
        }
        try
        {
            var reader = new Utf8JsonReader(bytes, new JsonReaderOptions { MaxDepth = 2 });
            using JsonDocument document = JsonDocument.ParseValue(ref reader);
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                throw new InvalidDataException("A capture state receipt requires an object.");
            }
            var remaining = new HashSet<string>(Members, StringComparer.Ordinal);
            foreach (JsonProperty property in root.EnumerateObject())
            {
                if (!remaining.Remove(property.Name))
                {
                    throw new InvalidDataException("The capture state receipt contains an unknown or duplicate field.");
                }
            }
            if (remaining.Count != 0)
            {
                throw new InvalidDataException("The capture state receipt is missing required fields.");
            }
            if (!root.GetProperty("version").TryGetInt32(out int version) || version != Version)
            {
                throw new InvalidDataException("The capture state receipt version is invalid.");
            }
            if (!root.GetProperty("timeCode").TryGetDouble(out double timeCode) || !double.IsFinite(timeCode) ||
                !root.GetProperty("width").TryGetInt32(out int width) ||
                !root.GetProperty("height").TryGetInt32(out int height))
            {
                throw new InvalidDataException("The capture state receipt contains an invalid number.");
            }
            return new ViewerCaptureStateReceipt(
                ValidateField(root.GetProperty("stage").GetString(), allowEmpty: true),
                timeCode,
                ValidateField(root.GetProperty("camera").GetString()),
                ValidateField(root.GetProperty("display").GetString()),
                ValidateField(root.GetProperty("renderSettings").GetString()),
                ValidateField(root.GetProperty("backend").GetString()),
                ValidateDimension(width),
                ValidateDimension(height),
                ValidateHash(root.GetProperty("pixelSha256").GetString()));
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("The capture state receipt JSON is invalid.", exception);
        }
        catch (InvalidOperationException exception)
        {
            throw new InvalidDataException("The capture state receipt contains an invalid value type.", exception);
        }
    }

    internal static string ValidateField(string? value, bool allowEmpty = false)
    {
        if (value is null || (!allowEmpty && value.Length == 0) || value.Length > MaximumFieldLength ||
            value.Contains('\0'))
        {
            throw new InvalidDataException(
                "Capture state receipt fields must be text of at most 4096 UTF-16 code units.");
        }
        return value;
    }

    internal static int ValidateDimension(int value)
    {
        if (value is <= 0 or > ViewerCaptureComparison.MaximumDimension)
        {
            throw new InvalidDataException("The capture state receipt dimensions exceed the comparison bounds.");
        }
        return value;
    }

    private static string ValidateHash(string? hash)
    {
        if (hash is not { Length: 64 } || hash.Any(character => !char.IsAsciiHexDigit(character)))
        {
            throw new InvalidDataException("The capture state receipt pixel checksum is invalid.");
        }
        return hash;
    }
}
