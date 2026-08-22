// Copyright (c) marcschier. Licensed under the MIT License.

namespace OpenUsd.Mcp;

public sealed class ImageRgba8
{
    public const int BytesPerPixel = 4;
    public const int MaximumByteCount = 256 * 1024 * 1024;

    private readonly byte[] _pixels;

    public ImageRgba8(int width, int height, ReadOnlySpan<byte> pixels)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        int byteCount = GetByteCount(width, height);
        if (pixels.Length != byteCount)
        {
            throw new ArgumentException(
                $"RGBA8 data must contain exactly {byteCount} bytes.",
                nameof(pixels));
        }

        Width = width;
        Height = height;
        _pixels = pixels.ToArray();
    }

    public int Width { get; }

    public int Height { get; }

    public ReadOnlyMemory<byte> Pixels => _pixels;

    internal static int GetByteCount(int width, int height)
    {
        long byteCount = checked((long)width * height * BytesPerPixel);
        if (byteCount > MaximumByteCount)
        {
            throw new ArgumentOutOfRangeException(
                nameof(width),
                $"RGBA8 images may not exceed {MaximumByteCount} bytes.");
        }

        return checked((int)byteCount);
    }
}
