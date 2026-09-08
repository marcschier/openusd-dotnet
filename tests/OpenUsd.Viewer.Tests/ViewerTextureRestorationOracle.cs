// Copyright (c) marcschier. Licensed under the MIT License.

using OpenUsd.Rendering.Storm;

namespace OpenUsd.Viewer.Tests;

internal sealed class ViewerTextureRestorationOracle
{
    private readonly int _width;
    private readonly int _height;
    private readonly byte[] _baseline;
    private readonly int[] _whiteOffsets;

    internal ViewerTextureRestorationOracle(OpenUsdStormFramebufferCapture baseline)
    {
        if (baseline.Width <= 0 || baseline.Height <= 0 ||
            baseline.RgbaPixels.Length != (long)baseline.Width * baseline.Height * 4)
        {
            throw new InvalidDataException("The baseline capture has no complete drawable image.");
        }
        _width = baseline.Width;
        _height = baseline.Height;
        _baseline = baseline.RgbaPixels.ToArray();
        var offsets = new List<int>();
        for (int offset = 0; offset < _baseline.Length; offset += 4)
        {
            if (IsOpaqueWhite(_baseline.AsSpan(offset, 4)))
            {
                offsets.Add(offset);
            }
        }
        if (offsets.Count < 64)
        {
            throw new InvalidDataException("The original white quad is not visibly established in the baseline.");
        }
        _whiteOffsets = offsets.ToArray();
    }

    internal bool IsRestored(OpenUsdStormFramebufferCapture current)
    {
        if (current.Width != _width || current.Height != _height || current.RgbaPixels.Length != _baseline.Length)
        {
            return false;
        }
        ReadOnlySpan<byte> pixels = current.RgbaPixels.Span;
        int matched = 0;
        foreach (int offset in _whiteOffsets)
        {
            if (IsOpaqueWhite(pixels.Slice(offset, 4)) &&
                Math.Abs(pixels[offset] - _baseline[offset]) <= 16 &&
                Math.Abs(pixels[offset + 1] - _baseline[offset + 1]) <= 16 &&
                Math.Abs(pixels[offset + 2] - _baseline[offset + 2]) <= 16)
            {
                matched++;
            }
        }
        return matched * 100L >= _whiteOffsets.Length * 98L;
    }

    private static bool IsOpaqueWhite(ReadOnlySpan<byte> pixel) =>
        pixel[0] >= 128 && pixel[1] >= 128 && pixel[2] >= 128 && pixel[3] >= 240 &&
        Math.Abs(pixel[0] - pixel[1]) <= 12 && Math.Abs(pixel[1] - pixel[2]) <= 12 &&
        Math.Abs(pixel[0] - pixel[2]) <= 12;
}
