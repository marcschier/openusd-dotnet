// Copyright (c) marcschier. Licensed under the MIT License.

using OpenUsd.Rendering.Silk;

namespace OpenUsd.Rendering.ConformanceTests;

/// <summary>
/// Backend-neutral helpers for the display-transform conformance gates.
/// </summary>
internal static class DisplayTransformConformance
{
    internal const uint Width = 32;
    internal const uint Height = 24;

    /// <summary>
    /// Builds a vertically asymmetric scene: a bright quad covering the upper half of
    /// clip space and a dark quad covering the lower half.
    /// </summary>
    /// <remarks>
    /// A clear colour cannot catch a vertical flip and neither can a centred quad, which
    /// is exactly why the fullscreen composite's orientation went unnoticed. This scene
    /// makes the top and bottom of the image different, so writing the source's top row
    /// into the target's bottom row is a failing assertion rather than an invisible one.
    /// </remarks>
    internal static void ApplyVerticallyAsymmetricScene(SilkMeshRenderer renderer)
    {
        ArgumentNullException.ThrowIfNull(renderer);
        byte[] frame = SilkMeshRendererConformance.CreateFrameCommand(
            Width,
            Height,
            SilkMeshRendererConformance.Identity());
        // Geometry, not colour, carries the asymmetry: one quad occupies the upper half
        // of clip space and the lower half stays at the clear colour. That keeps the
        // difference independent of shading, tinting, and lighting, so the assertion is
        // about orientation and nothing else.
        byte[] upper = CreateQuad(1, "/Upper", -0.9f, 0.9f, 0.9f, 0.1f, 0.5f);
        SilkMeshRendererConformance.Apply(renderer, 1, [frame, upper]);
    }

    /// <summary>
    /// Gets the mean luminance of the top and bottom quarter of an RGBA8 image.
    /// </summary>
    internal static (double Top, double Bottom) MeasureVerticalBias(ReadOnlySpan<byte> rgba)
    {
        int quarter = (int)(Height / 4);
        double top = 0;
        double bottom = 0;
        int count = 0;
        for (int row = 0; row < quarter; row++)
        {
            for (int column = 0; column < Width; column++)
            {
                int topOffset = (int)(((row * Width) + column) * 4);
                int bottomOffset =
                    (int)(((((int)Height - 1 - row) * Width) + column) * 4);
                top += rgba[topOffset] + rgba[topOffset + 1] + rgba[topOffset + 2];
                bottom +=
                    rgba[bottomOffset] + rgba[bottomOffset + 1] + rgba[bottomOffset + 2];
                count++;
            }
        }
        return (top / (count * 3.0), bottom / (count * 3.0));
    }

    private static byte[] CreateQuad(
        ulong id,
        string pathValue,
        float left,
        float top,
        float right,
        float bottom,
        float depth) =>
        SilkMeshRendererConformance.CreateMeshCommand(
            id,
            pathValue,
            [
                left, top, depth,
                right, top, depth,
                right, bottom, depth,
                left, bottom, depth
            ],
            [0, 1, 2, 0, 2, 3]);
}
