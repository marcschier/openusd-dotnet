// Copyright (c) marcschier. Licensed under the MIT License.

using OpenUsd.Rendering;

namespace OpenUsd.Mcp;

public static class PngRgba8Encoder
{
    public static byte[] Encode(ImageRgba8 image)
    {
        ArgumentNullException.ThrowIfNull(image);
        return PngRgba8Writer.Encode(image.Width, image.Height, image.Pixels.Span);
    }
}
