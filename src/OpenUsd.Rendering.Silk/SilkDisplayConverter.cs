// Copyright (c) marcschier. Licensed under the MIT License.

using System.Runtime.InteropServices;

namespace OpenUsd.Rendering.Silk;

internal static class SilkDisplayConverter
{
    internal static SilkColor TransformColor(
        SilkColor color,
        RenderOutputTransform outputTransform,
        float exposure)
    {
        ValidateTransform(outputTransform, exposure, out float exposureScale);
        return new SilkColor(
            Transform(color.Red, exposureScale, outputTransform),
            Transform(color.Green, exposureScale, outputTransform),
            Transform(color.Blue, exposureScale, outputTransform),
            color.Alpha);
    }

    internal static void ConvertRgba16FloatToRgba8(
        ReadOnlySpan<byte> source,
        Span<byte> destination,
        RenderOutputTransform outputTransform,
        float exposure)
    {
        if (source.Length % (sizeof(ushort) * 4) != 0)
        {
            throw new ArgumentException(
                "RGBA16Float source data must contain complete pixels.",
                nameof(source));
        }
        if (destination.Length != source.Length / 2)
        {
            throw new ArgumentException(
                "RGBA8 destination data must contain one byte per source channel.",
                nameof(destination));
        }
        ValidateTransform(outputTransform, exposure, out float exposureScale);

        ReadOnlySpan<Half> sourceChannels = MemoryMarshal.Cast<byte, Half>(source);
        for (int channel = 0; channel < sourceChannels.Length; channel += 4)
        {
            destination[channel] = Quantize(Transform(sourceChannels[channel], exposureScale, outputTransform));
            destination[channel + 1] = Quantize(Transform(sourceChannels[channel + 1], exposureScale, outputTransform));
            destination[channel + 2] = Quantize(Transform(sourceChannels[channel + 2], exposureScale, outputTransform));
            destination[channel + 3] = Quantize(sourceChannels[channel + 3]);
        }
    }

    private static void ValidateTransform(
        RenderOutputTransform outputTransform,
        float exposure,
        out float exposureScale)
    {
        if (outputTransform is < RenderOutputTransform.Identity or > RenderOutputTransform.Reinhard)
        {
            throw new ArgumentOutOfRangeException(nameof(outputTransform));
        }
        if (!float.IsFinite(exposure))
        {
            throw new ArgumentOutOfRangeException(nameof(exposure));
        }

        exposureScale = MathF.Pow(2, exposure);
        if (!float.IsFinite(exposureScale))
        {
            throw new ArgumentOutOfRangeException(nameof(exposure));
        }
    }

    private static float Transform(
        Half value,
        float exposureScale,
        RenderOutputTransform outputTransform) =>
        Transform((float)value, exposureScale, outputTransform);

    private static float Transform(
        float value,
        float exposureScale,
        RenderOutputTransform outputTransform)
    {
        float transformed = value * exposureScale;
        return outputTransform == RenderOutputTransform.Reinhard
            ? transformed / (1 + MathF.Max(transformed, 0))
            : transformed;
    }

    private static byte Quantize(Half value) => Quantize((float)value);

    private static byte Quantize(float value)
    {
        if (!float.IsFinite(value))
        {
            throw new InvalidDataException("The HDR render target contains a non-finite channel.");
        }

        float normalized = Math.Clamp(value, 0, 1);
        return checked((byte)MathF.Floor((normalized * byte.MaxValue) + 0.5f));
    }
}
