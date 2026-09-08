// Copyright (c) marcschier. Licensed under the MIT License.

using System.Buffers.Binary;
using OpenUsd.Rendering;

namespace OpenUsd.Viewer;

internal sealed record ViewerCaptureImage(int Width, int Height, byte[] Rgba);

internal sealed record ViewerCapturePair(ViewerCaptureImage Before, ViewerCaptureImage After);

internal static class ViewerCaptureComparison
{
    internal const int MaximumDimension = 8192;
    internal const int MaximumPixels = 16 * 1024 * 1024;
    internal const int MaximumFileBytes = 64 * 1024 * 1024;

    internal static Task<ViewerCapturePair> LoadAsync(
        string beforePath,
        string afterPath,
        CancellationToken cancellationToken) =>
        Task.Run(async () =>
        {
            await using FileStream before = OpenCapture(beforePath);
            await using FileStream after = OpenCapture(afterPath);
            CaptureInfo first = await ReadInfoAsync(before, cancellationToken).ConfigureAwait(false);
            CaptureInfo second = await ReadInfoAsync(after, cancellationToken).ConfigureAwait(false);
            ViewerCaptureImage firstImage = await DecodeAsync(before, first, cancellationToken).ConfigureAwait(false);
            ViewerCaptureImage secondImage = await DecodeAsync(after, second, cancellationToken).ConfigureAwait(false);
            return new ViewerCapturePair(firstImage, secondImage);
        }, cancellationToken);

    private static FileStream OpenCapture(string path) =>
        new(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096,
            FileOptions.Asynchronous | FileOptions.SequentialScan);

    private static async Task<CaptureInfo> ReadInfoAsync(FileStream stream, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (stream.Length < 33 || stream.Length > MaximumFileBytes)
        {
            throw new InvalidDataException("Each capture must be a PNG or BMP file no larger than 64 MiB.");
        }
        byte[] header = new byte[54];
        await stream.ReadExactlyAsync(header.AsMemory(0, 33), cancellationToken).ConfigureAwait(false);
        if (header.AsSpan(0, 8).SequenceEqual(PngRgba8Reader.Signature))
        {
            PngRgba8Header png = PngRgba8Reader.ReadHeader(header.AsSpan(0, 33));
            ValidateDimensions(png.Width, png.Height);
            return new CaptureInfo(png.Width, png.Height, 4, 0, 0, true, IsPng: true);
        }
        if (stream.Length < 54)
        {
            throw new InvalidDataException("The BMP header is truncated.");
        }
        await stream.ReadExactlyAsync(header.AsMemory(33), cancellationToken).ConfigureAwait(false);
        uint dibSize = BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(14));
        ushort bits = BinaryPrimitives.ReadUInt16LittleEndian(header.AsSpan(28));
        if (header[0] != 'B' || header[1] != 'M' || dibSize is not (40 or 52 or 56 or 108 or 124) ||
            BinaryPrimitives.ReadUInt16LittleEndian(header.AsSpan(26)) != 1 ||
            bits is not (24 or 32) || BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(30)) != 0)
        {
            throw new InvalidDataException(
                "Use RGBA PNG or uncompressed 24-bit or 32-bit BMP captures.");
        }
        int width = BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(18));
        int signedHeight = BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(22));
        long height = Math.Abs((long)signedHeight);
        ValidateDimensions(width, height);
        int rowBytes = checked(((width * (bits / 8)) + 3) & ~3);
        uint offset = BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(10));
        if (offset < 14 + dibSize || offset + (rowBytes * height) > stream.Length)
        {
            throw new InvalidDataException("The BMP pixel data is truncated or its offset is invalid.");
        }
        return new CaptureInfo(width, (int)height, bits / 8, rowBytes, offset, signedHeight < 0);
    }

    private static async Task<ViewerCaptureImage> DecodeAsync(
        FileStream stream, CaptureInfo info, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (info.IsPng)
        {
            byte[] encoded = new byte[checked((int)stream.Length)];
            stream.Position = 0;
            await stream.ReadExactlyAsync(encoded, cancellationToken).ConfigureAwait(false);
            if (PngRgba8Reader.ReadHeader(encoded) != new PngRgba8Header(info.Width, info.Height))
            {
                throw new InvalidDataException("The PNG header changed while reading the capture.");
            }
            PngRgba8Image image = PngRgba8Reader.Decode(encoded, cancellationToken);
            return new ViewerCaptureImage(image.Width, image.Height, image.Pixels);
        }
        byte[] pixels = new byte[checked(info.Width * info.Height * 4)];
        byte[] row = new byte[info.RowBytes];
        stream.Position = info.Offset;
        for (int y = 0; y < info.Height; y++)
        {
            await stream.ReadExactlyAsync(row, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            int destination = (info.TopDown ? y : info.Height - 1 - y) * info.Width * 4;
            for (int x = 0; x < info.Width; x++)
            {
                int source = x * info.BytesPerPixel;
                pixels[destination++] = row[source + 2];
                pixels[destination++] = row[source + 1];
                pixels[destination++] = row[source];
                // BI_RGB's fourth byte is reserved, not an alpha channel.
                pixels[destination++] = 255;
            }
        }
        return new ViewerCaptureImage(info.Width, info.Height, pixels);
    }

    private static void ValidateDimensions(int width, long height)
    {
        if (width <= 0 || width > MaximumDimension || height <= 0 || height > MaximumDimension ||
            width * height > MaximumPixels)
        {
            throw new InvalidDataException("Each capture is limited to 8192 pixels per side and 16 megapixels.");
        }
    }

    private readonly record struct CaptureInfo(
        int Width, int Height, int BytesPerPixel, int RowBytes, uint Offset, bool TopDown, bool IsPng = false);
}
