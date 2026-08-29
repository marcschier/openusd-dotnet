// Copyright (c) marcschier. Licensed under the MIT License.

using System.Runtime.InteropServices;

namespace OpenUsd.Rendering.Silk;

/// <summary>
/// Generates a CPU mip chain for one decoded, flipped, and scale/bias-applied ordinary
/// (non-UDIM) material image. Uses a deterministic 2x2 box filter with clamp-to-edge sampling
/// so odd dimensions still halve correctly. Normal-map slots decode encoded [0,1] RGB into a
/// tangent-space direction, average, and renormalize before re-encoding so downsampled normals
/// stay unit length; every other slot (including alpha) uses ordinary component averaging.
/// </summary>
internal static class SilkMipGenerator
{
    /// <summary>
    /// Builds the full packed mip chain for <paramref name="baseLevel"/>, returning the packed
    /// chain bytes (mip 0 first, ascending) and the level count actually generated.
    /// </summary>
    internal static byte[] GenerateChain(
        byte[] baseLevel,
        uint width,
        uint height,
        SilkTextureFormat format,
        bool isNormalMap,
        out uint mipLevelCount)
    {
        if (format is not (SilkTextureFormat.Rgba8Unorm or SilkTextureFormat.Rgba32Float))
        {
            throw new ArgumentOutOfRangeException(
                nameof(format),
                format,
                "Mip generation supports only Rgba8Unorm and Rgba32Float material images.");
        }
        mipLevelCount = SilkMipChainLayout.GetMaxMipLevelCount(width, height);
        SilkMipLevelLayout[] levels =
            SilkMipChainLayout.Create(width, height, format, mipLevelCount);
        SilkMipLevelLayout lastLevel = levels[^1];
        int totalSize = checked(lastLevel.Offset + lastLevel.Size);
        if (baseLevel.Length != levels[0].Size)
        {
            throw new ArgumentException(
                $"The base level must contain exactly {levels[0].Size} bytes.",
                nameof(baseLevel));
        }
        byte[] chain = new byte[totalSize];
        baseLevel.CopyTo(chain.AsSpan(levels[0].Offset, levels[0].Size));
        if (format == SilkTextureFormat.Rgba32Float)
        {
            ValidateFinite(MemoryMarshal.Cast<byte, float>(baseLevel.AsSpan()));
        }
        for (int level = 1; level < levels.Length; level++)
        {
            GenerateLevel(chain, levels[level - 1], levels[level], format, isNormalMap);
        }
        return chain;
    }

    private static void GenerateLevel(
        byte[] chain,
        SilkMipLevelLayout source,
        SilkMipLevelLayout destination,
        SilkTextureFormat format,
        bool isNormalMap)
    {
        if (format == SilkTextureFormat.Rgba32Float)
        {
            Span<float> sourcePixels = MemoryMarshal.Cast<byte, float>(
                chain.AsSpan(source.Offset, source.Size));
            Span<float> destinationPixels = MemoryMarshal.Cast<byte, float>(
                chain.AsSpan(destination.Offset, destination.Size));
            GenerateLevelFloat(
                sourcePixels,
                source.Width,
                source.Height,
                destinationPixels,
                destination.Width,
                destination.Height,
                isNormalMap);
            return;
        }
        GenerateLevelRgba8(
            chain.AsSpan(source.Offset, source.Size),
            source.Width,
            source.Height,
            chain.AsSpan(destination.Offset, destination.Size),
            destination.Width,
            destination.Height,
            isNormalMap);
    }

    private static void GenerateLevelFloat(
        ReadOnlySpan<float> source,
        uint sourceWidth,
        uint sourceHeight,
        Span<float> destination,
        uint destinationWidth,
        uint destinationHeight,
        bool isNormalMap)
    {
        for (uint y = 0; y < destinationHeight; y++)
        {
            for (uint x = 0; x < destinationWidth; x++)
            {
                Span<float> texel = destination.Slice(
                    checked((int)(((y * destinationWidth) + x) * 4)),
                    4);
                Sample4(source, sourceWidth, sourceHeight, x, y, out ReadOnlySpan<float> s00,
                    out ReadOnlySpan<float> s01, out ReadOnlySpan<float> s10, out ReadOnlySpan<float> s11);
                if (isNormalMap)
                {
                    AverageNormal(s00, s01, s10, s11, texel);
                }
                else
                {
                    AverageComponents(s00, s01, s10, s11, texel);
                }
                ValidateFinite(texel);
            }
        }
    }

    private static void GenerateLevelRgba8(
        ReadOnlySpan<byte> source,
        uint sourceWidth,
        uint sourceHeight,
        Span<byte> destination,
        uint destinationWidth,
        uint destinationHeight,
        bool isNormalMap)
    {
        Span<float> s00 = stackalloc float[4];
        Span<float> s01 = stackalloc float[4];
        Span<float> s10 = stackalloc float[4];
        Span<float> s11 = stackalloc float[4];
        Span<float> texel = stackalloc float[4];
        for (uint y = 0; y < destinationHeight; y++)
        {
            for (uint x = 0; x < destinationWidth; x++)
            {
                SampleRgba8(source, sourceWidth, sourceHeight, 2 * x, 2 * y, s00);
                SampleRgba8(source, sourceWidth, sourceHeight, (2 * x) + 1, 2 * y, s01);
                SampleRgba8(source, sourceWidth, sourceHeight, 2 * x, (2 * y) + 1, s10);
                SampleRgba8(source, sourceWidth, sourceHeight, (2 * x) + 1, (2 * y) + 1, s11);
                if (isNormalMap)
                {
                    AverageNormal(s00, s01, s10, s11, texel);
                }
                else
                {
                    AverageComponents(s00, s01, s10, s11, texel);
                }
                ValidateFinite(texel);
                int destinationOffset = checked((int)(((y * destinationWidth) + x) * 4));
                for (int component = 0; component < 4; component++)
                {
                    destination[destinationOffset + component] =
                        (byte)Math.Clamp(MathF.Round(texel[component] * 255f), 0, 255);
                }
            }
        }
    }

    private static void Sample4(
        ReadOnlySpan<float> source,
        uint sourceWidth,
        uint sourceHeight,
        uint x,
        uint y,
        out ReadOnlySpan<float> s00,
        out ReadOnlySpan<float> s01,
        out ReadOnlySpan<float> s10,
        out ReadOnlySpan<float> s11)
    {
        uint x0 = Math.Min(2 * x, sourceWidth - 1);
        uint x1 = Math.Min((2 * x) + 1, sourceWidth - 1);
        uint y0 = Math.Min(2 * y, sourceHeight - 1);
        uint y1 = Math.Min((2 * y) + 1, sourceHeight - 1);
        s00 = source.Slice(checked((int)(((y0 * sourceWidth) + x0) * 4)), 4);
        s01 = source.Slice(checked((int)(((y0 * sourceWidth) + x1) * 4)), 4);
        s10 = source.Slice(checked((int)(((y1 * sourceWidth) + x0) * 4)), 4);
        s11 = source.Slice(checked((int)(((y1 * sourceWidth) + x1) * 4)), 4);
    }

    private static void SampleRgba8(
        ReadOnlySpan<byte> source,
        uint sourceWidth,
        uint sourceHeight,
        uint x,
        uint y,
        Span<float> destination)
    {
        uint clampedX = Math.Min(x, sourceWidth - 1);
        uint clampedY = Math.Min(y, sourceHeight - 1);
        int offset = checked((int)(((clampedY * sourceWidth) + clampedX) * 4));
        for (int component = 0; component < 4; component++)
        {
            destination[component] = source[offset + component] / 255f;
        }
    }

    private static void AverageComponents(
        ReadOnlySpan<float> s00,
        ReadOnlySpan<float> s01,
        ReadOnlySpan<float> s10,
        ReadOnlySpan<float> s11,
        Span<float> destination)
    {
        for (int component = 0; component < 4; component++)
        {
            destination[component] =
                (s00[component] + s01[component] + s10[component] + s11[component]) / 4f;
        }
    }

    /// <summary>
    /// Decodes tangent-space normals from encoded [0,1] RGB, averages, and renormalizes so a
    /// downsampled normal map stays unit length instead of drifting toward flat shading. A
    /// zero-length average (fully canceling neighbors) deterministically falls back to
    /// straight-up (0, 0, 1) rather than propagating a NaN from normalizing a zero vector.
    /// Alpha is averaged ordinarily; it carries no directional meaning.
    /// </summary>
    private static void AverageNormal(
        ReadOnlySpan<float> s00,
        ReadOnlySpan<float> s01,
        ReadOnlySpan<float> s10,
        ReadOnlySpan<float> s11,
        Span<float> destination)
    {
        float x = decode(s00[0]) + decode(s01[0]) + decode(s10[0]) + decode(s11[0]);
        float y = decode(s00[1]) + decode(s01[1]) + decode(s10[1]) + decode(s11[1]);
        float z = decode(s00[2]) + decode(s01[2]) + decode(s10[2]) + decode(s11[2]);
        float length = MathF.Sqrt((x * x) + (y * y) + (z * z));
        if (length < 1e-8f)
        {
            x = 0;
            y = 0;
            z = 1;
        }
        else
        {
            x /= length;
            y /= length;
            z /= length;
        }
        destination[0] = encode(x);
        destination[1] = encode(y);
        destination[2] = encode(z);
        destination[3] =
            (s00[3] + s01[3] + s10[3] + s11[3]) / 4f;

        static float decode(float encoded) => (encoded * 2f) - 1f;
        static float encode(float direction) => (direction + 1f) * 0.5f;
    }

    private static void ValidateFinite(ReadOnlySpan<float> texel)
    {
        for (int component = 0; component < texel.Length; component++)
        {
            if (!float.IsFinite(texel[component]))
            {
                throw new InvalidDataException(
                    "Mip chain generation encountered a non-finite channel.");
            }
        }
    }
}
