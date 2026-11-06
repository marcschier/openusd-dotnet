// Copyright (c) marcschier. Licensed under the MIT License.

using System.Numerics;

namespace OpenUsd.Rendering.Silk;

internal readonly record struct SilkPtexMaterialValue(
    SilkMaterialParameter Parameter,
    SilkColorSpace ColorSpace,
    SilkTextureChannel Channel,
    Vector4 Scale,
    Vector4 Bias);

public sealed partial class SilkSceneGpuResources
{
    internal static SilkPtexMaterialValue CapturePtexMaterialValue(SilkMaterialTexture texture)
    {
        ArgumentNullException.ThrowIfNull(texture);
        if (texture.SourceKind != SilkTextureSourceKind.Ptex ||
            texture.CompositeOperator != SilkCompositeOperator.None || texture.LuminanceMix.W != 0)
        {
            throw new NotSupportedException(
                "Ptex material preprocessing requires a direct source without luminance mixing.");
        }
        if (texture.Scale.Count != 4 || texture.Bias.Count != 4)
        {
            throw new InvalidDataException("Ptex scale and bias require four components.");
        }
        var scale = new Vector4(texture.Scale[0], texture.Scale[1], texture.Scale[2], texture.Scale[3]);
        var bias = new Vector4(texture.Bias[0], texture.Bias[1], texture.Bias[2], texture.Bias[3]);
        for (int component = 0; component < 4; component++)
        {
            if (!float.IsFinite(scale[component]) || !float.IsFinite(bias[component]))
            {
                throw new InvalidDataException("Ptex scale and bias must be finite.");
            }
        }
        if (texture.Channel != SilkTextureChannel.Rgb && (uint)texture.Channel > 3)
        {
            throw new NotSupportedException("RGBA8 Ptex preprocessing requires RGB or a single RGBA output channel.");
        }
        return new(texture.Parameter, GetEffectiveColorSpace(texture), texture.Channel, scale, bias);
    }

    internal static void PreparePtexMaterialPixels(byte[] pixels, SilkMaterialTexture texture) =>
        PreparePtexMaterialPixels(pixels, CapturePtexMaterialValue(texture));

    internal static void PreparePtexMaterialPixels(byte[] pixels, SilkPtexMaterialValue value)
    {
        ArgumentNullException.ThrowIfNull(pixels);
        if (pixels.Length == 0 || pixels.Length % 4 != 0)
        {
            throw new InvalidDataException("Ptex atlas pixels require complete RGBA8 texels.");
        }
        if (value.ColorSpace == SilkColorSpace.Srgb)
        {
            LinearizeSrgbPtexAtlas(pixels);
        }
        ApplyScaleBiasValues(pixels, SilkTextureFormat.Rgba8Unorm, value.Scale, value.Bias);
        ApplyOutputChannelValues(pixels, SilkTextureFormat.Rgba8Unorm, value.Parameter, value.Channel);
    }

    internal static SilkSamplerDescriptor PtexMaterialSampler => new(
        SilkSamplerFilter.Linear, SilkSamplerFilter.Linear,
        SilkSamplerAddressMode.ClampToEdge, SilkSamplerAddressMode.ClampToEdge,
        SilkSamplerAddressMode.ClampToEdge, 1);
}
