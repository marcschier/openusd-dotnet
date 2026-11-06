// Copyright (c) marcschier. Licensed under the MIT License.

namespace OpenUsd.Rendering.Silk;

internal sealed class SilkShadowTransmissionException(Exception innerException)
    : Exception(innerException.Message, innerException)
{
}

internal enum SilkShadowTransmissionPass
{
    FirstPeel,
    Peel,
    EqualDepthProbe,
}

internal static class SilkShadowTransmissionValidation
{
    internal static void ValidateProbe(
        ReadOnlySpan<byte> pixels,
        uint edge,
        SilkShadowTilePlacement tile,
        uint lightIndex,
        int layer,
        bool overflow)
    {
        if (pixels.Length != checked((int)(edge * edge * 4)) ||
            tile.PixelX > edge || tile.PixelY > edge ||
            tile.Resolution > edge - tile.PixelX || tile.Resolution > edge - tile.PixelY)
        {
            throw new ArgumentException("The shadow probe must contain the complete atlas and tile.", nameof(pixels));
        }
        for (uint y = tile.PixelY; y < tile.PixelY + tile.Resolution; y++)
        {
            for (uint x = tile.PixelX; x < tile.PixelX + tile.Resolution; x++)
            {
                byte count = pixels[checked((int)(((y * edge) + x) * 4 + 3))];
                if (overflow ? count != 0 : count > 128)
                {
                    string reason = overflow
                        ? "ShadowTransmissionLayerOverflow: more than four positive-opacity intersections"
                        : "ShadowTransmissionEqualDepth: multiple positive-opacity intersections " +
                            "at the same raster depth";
                    throw new InvalidDataException(
                        $"{reason} at light {lightIndex}, layer {layer}, atlas pixel ({x}, {y}). " +
                        "The candidate shadow atlas was refused; the previous atlas remains published.");
                }
            }
        }
    }
}
