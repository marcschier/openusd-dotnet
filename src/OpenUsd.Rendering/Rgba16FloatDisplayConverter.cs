// Copyright (c) marcschier. Licensed under the MIT License.

using System.Runtime.InteropServices;

namespace OpenUsd.Rendering;

internal static class Rgba16FloatDisplayConverter
{
    internal static void Convert(
        ReadOnlySpan<byte> source, Span<byte> destination,
        RenderOutputTransform outputTransform, float exposure)
    {
        if (source.Length % (sizeof(ushort) * 4) != 0)
        {
            throw new ArgumentException("RGBA16Float source data must contain complete pixels.", nameof(source));
        }
        if (destination.Length != source.Length / 2)
        {
            throw new ArgumentException("RGBA8 destination data must contain one byte per source channel.", nameof(destination));
        }
        float scale = GetExposureScale(outputTransform, exposure);
        ReadOnlySpan<Half> channels = MemoryMarshal.Cast<byte, Half>(source);
        for (int channel = 0; channel < channels.Length; channel += 4)
        {
            destination[channel] = Quantize(Transform((float)channels[channel], scale, outputTransform));
            destination[channel + 1] = Quantize(Transform((float)channels[channel + 1], scale, outputTransform));
            destination[channel + 2] = Quantize(Transform((float)channels[channel + 2], scale, outputTransform));
            destination[channel + 3] = Quantize((float)channels[channel + 3]);
        }
    }

    internal static float GetExposureScale(RenderOutputTransform outputTransform, float exposure)
    {
        if (outputTransform is < RenderOutputTransform.Identity or > RenderOutputTransform.Reinhard)
        {
            throw new ArgumentOutOfRangeException(nameof(outputTransform));
        }
        if (!float.IsFinite(exposure))
        {
            throw new ArgumentOutOfRangeException(nameof(exposure));
        }
        float scale = MathF.Pow(2, exposure);
        if (!float.IsFinite(scale))
        {
            throw new ArgumentOutOfRangeException(nameof(exposure));
        }
        return scale;
    }

    internal static float Transform(float value, float exposureScale, RenderOutputTransform outputTransform)
    {
        float transformed = value * exposureScale;
        return outputTransform == RenderOutputTransform.Reinhard
            ? transformed / (1 + MathF.Max(transformed, 0))
            : transformed;
    }

    internal static byte Quantize(float value)
    {
        if (!float.IsFinite(value))
        {
            throw new InvalidDataException("The HDR render target contains a non-finite channel.");
        }
        float normalized = Math.Clamp(value, 0, 1);
        return checked((byte)MathF.Floor((normalized * byte.MaxValue) + 0.5f));
    }
}
