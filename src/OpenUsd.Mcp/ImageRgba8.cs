// Copyright (c) marcschier. Licensed under the MIT License.

using OpenUsd.Rendering;

namespace OpenUsd.Mcp;

public sealed class ImageRgba8
{
    public const int BytesPerPixel = PngRgba8Writer.BytesPerPixel;
    public const int MaximumByteCount = PngRgba8Writer.MaximumPixelByteCount;

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

    internal static int GetByteCount(int width, int height) => PngRgba8Writer.GetPixelByteCount(width, height);
}
