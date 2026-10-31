// Copyright (c) marcschier. Licensed under the MIT License.

using System.Buffers.Binary;

namespace OpenUsd.Rendering;

/// <summary>Applies one authored camera exposure multiplier to both product HDR and its PNG companion.</summary>
internal static class ProductCameraExposure
{
    private const int PixelBytes = 4 * sizeof(ushort);

    internal static RenderSettings ApplyToDisplay(RenderSettings settings, double linearScale)
    {
        if (linearScale == 1)
        {
            return settings;
        }
        if (!(linearScale > 0) || !double.IsFinite(linearScale))
        {
            throw new NotSupportedException(
                "A zero camera exposure scale has no finite display exposure for the PNG companion.");
        }
        float exposure = (float)(settings.Exposure + Math.Log2(linearScale));
        if (!float.IsFinite(exposure) || !float.IsNormal(MathF.Pow(2, exposure)))
        {
            throw new NotSupportedException("The camera exposure scale exceeds the display exposure range.");
        }
        return new RenderSettings(
            settings.SamplesPerPixel, settings.EnableLighting, settings.EnableShadows, settings.ClearColor,
            settings.BackfaceCulling, settings.UseSceneMaterials, settings.Complexity, settings.OutputTransform,
            exposure)
        {
            DisplayTransform = settings.DisplayTransform
        };
    }

    /// <summary>Scales red, green and blue by the camera multiplier; alpha and channel order stay unchanged.</summary>
    internal static void ScaleHdr(ReadOnlySpan<byte> source, Span<byte> destination, double linearScale)
    {
        if (source.Length % PixelBytes != 0 || destination.Length != source.Length)
        {
            throw new ArgumentException("HDR exposure scaling requires complete, equally sized RGBA16F pixels.");
        }
        for (int offset = 0; offset < source.Length; offset += PixelBytes)
        {
            for (int channel = 0; channel < 3; channel++)
            {
                int at = offset + (channel * sizeof(ushort));
                Half scaled = (Half)((double)BinaryPrimitives.ReadHalfLittleEndian(source[at..]) * linearScale);
                if (!Half.IsFinite(scaled))
                {
                    throw new InvalidDataException(
                        "Camera exposure moves an HDR color channel outside the binary16 range.");
                }
                BinaryPrimitives.WriteHalfLittleEndian(destination[at..], scaled);
            }
            source.Slice(offset + 6, sizeof(ushort)).CopyTo(destination.Slice(offset + 6, sizeof(ushort)));
        }
    }
}
