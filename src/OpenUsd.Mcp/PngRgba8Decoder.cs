// Copyright (c) marcschier. Licensed under the MIT License.

using OpenUsd.Rendering;

namespace OpenUsd.Mcp;

public static class PngRgba8Decoder
{
    public static ImageRgba8 Decode(ReadOnlySpan<byte> png)
    {
        _ = PngRgba8Reader.ReadHeader(png);
        if (png.Length > PngRgba8Reader.MaximumEncodedByteCount)
        {
            throw new InvalidDataException("The encoded PNG exceeds its byte budget.");
        }
        PngRgba8Image image = PngRgba8Reader.Decode(png.ToArray());
        return new ImageRgba8(image.Width, image.Height, image.Pixels);
    }
}
