// Copyright (c) marcschier. Licensed under the MIT License.

using System.Security.Cryptography;
using System.Text.Json;

namespace OpenUsd.Viewer;

internal sealed record ViewerCapturePresentation(
    string BeforePath, string AfterPath, ViewerCapturePair Images, int DisplayMode);

internal static class ViewerCapturePresentationFile
{
    internal const int MaximumBytes = 32 * 1024;
    private const int MaximumPathLength = 4096;

    internal static async Task SaveAsync(
        string path, ViewerCapturePresentation presentation, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(presentation);
        cancellationToken.ThrowIfCancellationRequested();
        ValidateMode(presentation.DisplayMode);
        string before = ValidatePath(presentation.BeforePath);
        string after = ValidatePath(presentation.AfterPath);
        string destination = Path.GetFullPath(path);
        using var bytes = new MemoryStream();
        using (var writer = new Utf8JsonWriter(bytes, new JsonWriterOptions { Indented = true }))
        {
            writer.WriteStartObject();
            writer.WriteNumber("version", 1);
            writer.WriteNumber("displayMode", presentation.DisplayMode);
            WriteCapture(writer, "before", before, presentation.Images.Before);
            WriteCapture(writer, "after", after, presentation.Images.After);
            writer.WriteEndObject();
        }
        if (bytes.Length > MaximumBytes)
        {
            throw new InvalidDataException("The comparison presentation exceeds its 32 KiB limit.");
        }
        cancellationToken.ThrowIfCancellationRequested();
        string temporary = Path.Combine(Path.GetDirectoryName(destination)!,
            $".{Path.GetFileName(destination)}.{Guid.NewGuid():N}.tmp");
        bool created = false;
        try
        {
            created = true;
            await using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write,
                FileShare.None, 4096, FileOptions.Asynchronous))
            {
                bytes.Position = 0;
                await bytes.CopyToAsync(stream, cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            }
            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporary, destination, overwrite: false);
        }
        finally
        {
            if (created)
            {
                File.Delete(temporary);
            }
        }
    }

    internal static async Task<ViewerCapturePresentation> LoadAsync(
        string path, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        byte[] bytes;
        await using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
            4096, FileOptions.Asynchronous | FileOptions.SequentialScan))
        {
            if (stream.Length is <= 0 or > MaximumBytes)
            {
                throw new InvalidDataException("A comparison presentation must be between 1 byte and 32 KiB.");
            }
            bytes = new byte[checked((int)stream.Length)];
            await stream.ReadExactlyAsync(bytes, cancellationToken).ConfigureAwait(false);
            if (stream.Length != bytes.Length)
            {
                throw new IOException("The comparison presentation changed while it was read.");
            }
        }
        CaptureReceipt before;
        CaptureReceipt after;
        int mode;
        try
        {
            using JsonDocument document = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 4 });
            JsonElement root = document.RootElement;
            ValidateMembers(root, ["version", "displayMode", "before", "after"]);
            if (!root.GetProperty("version").TryGetInt32(out int version) || version != 1 ||
                !root.GetProperty("displayMode").TryGetInt32(out mode))
            {
                throw new InvalidDataException("The comparison presentation version or display mode is invalid.");
            }
            ValidateMode(mode);
            before = ReadCapture(root.GetProperty("before"));
            after = ReadCapture(root.GetProperty("after"));
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("The comparison presentation JSON is invalid.", exception);
        }
        catch (InvalidOperationException exception)
        {
            throw new InvalidDataException("The comparison presentation contains an invalid value type.", exception);
        }
        ViewerCapturePair pair = await ViewerCaptureComparison.LoadAsync(
            before.Path, after.Path, cancellationToken).ConfigureAwait(false);
        VerifyCapture(before, pair.Before);
        VerifyCapture(after, pair.After);
        cancellationToken.ThrowIfCancellationRequested();
        return new ViewerCapturePresentation(before.Path, after.Path, pair, mode);
    }

    private static void WriteCapture(Utf8JsonWriter writer, string name, string path, ViewerCaptureImage image)
    {
        ValidateImage(image);
        writer.WriteStartObject(name);
        writer.WriteString("path", path);
        writer.WriteNumber("width", image.Width);
        writer.WriteNumber("height", image.Height);
        writer.WriteString("pixelSha256", Convert.ToHexString(SHA256.HashData(image.Rgba)));
        writer.WriteEndObject();
    }

    private static CaptureReceipt ReadCapture(JsonElement element)
    {
        ValidateMembers(element, ["path", "width", "height", "pixelSha256"]);
        string path = ValidatePath(element.GetProperty("path").GetString());
        if (!element.GetProperty("width").TryGetInt32(out int width) ||
            !element.GetProperty("height").TryGetInt32(out int height) ||
            width is <= 0 or > ViewerCaptureComparison.MaximumDimension ||
            height is <= 0 or > ViewerCaptureComparison.MaximumDimension ||
            (long)width * height > ViewerCaptureComparison.MaximumPixels)
        {
            throw new InvalidDataException("The historical capture dimensions exceed the comparison bounds.");
        }
        string? hash = element.GetProperty("pixelSha256").GetString();
        if (hash is not { Length: 64 } || hash.Any(character => !char.IsAsciiHexDigit(character)))
        {
            throw new InvalidDataException("The historical capture pixel checksum is invalid.");
        }
        return new CaptureReceipt(path, width, height, hash);
    }

    private static void VerifyCapture(CaptureReceipt expected, ViewerCaptureImage image)
    {
        if (image.Width != expected.Width || image.Height != expected.Height ||
            !string.Equals(Convert.ToHexString(SHA256.HashData(image.Rgba)),
                expected.PixelSha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                "A historical capture changed. Choose the captures explicitly to create a new comparison.");
        }
    }

    private static void ValidateImage(ViewerCaptureImage image)
    {
        if (image.Width is <= 0 or > ViewerCaptureComparison.MaximumDimension ||
            image.Height is <= 0 or > ViewerCaptureComparison.MaximumDimension ||
            (long)image.Width * image.Height > ViewerCaptureComparison.MaximumPixels ||
            image.Rgba.LongLength != (long)image.Width * image.Height * 4)
        {
            throw new InvalidDataException("The displayed comparison image does not match its bounded RGBA storage.");
        }
    }

    private static string ValidatePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || path.Length > MaximumPathLength ||
            path.Contains('\0') || path.StartsWith(@"\\", StringComparison.Ordinal) ||
            path.StartsWith("//", StringComparison.Ordinal) || !Path.IsPathFullyQualified(path))
        {
            throw new InvalidDataException(
                "Historical captures require absolute local paths of at most 4096 characters.");
        }
        return Path.GetFullPath(path);
    }

    private static void ValidateMode(int mode)
    {
        if (mode is < 0 or > 2)
        {
            throw new InvalidDataException("The comparison display mode is invalid.");
        }
    }

    private static void ValidateMembers(JsonElement element, string[] names)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidDataException("The comparison presentation requires an object.");
        }
        var remaining = new HashSet<string>(names, StringComparer.Ordinal);
        foreach (JsonProperty property in element.EnumerateObject())
        {
            if (!remaining.Remove(property.Name))
            {
                throw new InvalidDataException("The comparison presentation contains an unknown or duplicate field.");
            }
        }
        if (remaining.Count != 0)
        {
            throw new InvalidDataException("The comparison presentation is missing required fields.");
        }
    }

    private sealed record CaptureReceipt(string Path, int Width, int Height, string PixelSha256);
}
