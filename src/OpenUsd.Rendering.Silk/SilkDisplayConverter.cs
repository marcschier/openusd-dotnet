// Copyright (c) marcschier. Licensed under the MIT License.

namespace OpenUsd.Rendering.Silk;

internal static class SilkDisplayConverter
{
    internal static SilkColor TransformColor(
        SilkColor color,
        RenderOutputTransform outputTransform,
        float exposure)
    {
        float exposureScale = Rgba16FloatDisplayConverter.GetExposureScale(outputTransform, exposure);
        return new SilkColor(
            Rgba16FloatDisplayConverter.Transform(color.Red, exposureScale, outputTransform),
            Rgba16FloatDisplayConverter.Transform(color.Green, exposureScale, outputTransform),
            Rgba16FloatDisplayConverter.Transform(color.Blue, exposureScale, outputTransform),
            color.Alpha);
    }

    internal static void ConvertRgba16FloatToRgba8(
        ReadOnlySpan<byte> source,
        Span<byte> destination,
        RenderOutputTransform outputTransform,
        float exposure) =>
        Rgba16FloatDisplayConverter.Convert(source, destination, outputTransform, exposure);
}
