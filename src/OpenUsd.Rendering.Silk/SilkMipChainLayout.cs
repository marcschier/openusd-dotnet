// Copyright (c) marcschier. Licensed under the MIT License.

namespace OpenUsd.Rendering.Silk;

/// <summary>Describes one tightly packed mip level inside a packed chain buffer.</summary>
/// <param name="Level">The zero-based mip level, where 0 is the base level.</param>
/// <param name="Width">The level width in texels, <c>max(1, baseWidth >> Level)</c>.</param>
/// <param name="Height">The level height in texels, <c>max(1, baseHeight >> Level)</c>.</param>
/// <param name="Offset">The byte offset of this level's first texel within the packed chain.</param>
/// <param name="RowPitch">The tightly packed byte stride of one row at this level.</param>
/// <param name="Size">The total tightly packed byte size of this level.</param>
internal readonly record struct SilkMipLevelLayout(
    uint Level,
    uint Width,
    uint Height,
    int Offset,
    int RowPitch,
    int Size);

/// <summary>
/// Backend-neutral packed 2D mip-chain layout shared by every RHI backend and by CPU mip
/// generation. A packed chain always stores mip 0 first, followed by ascending levels, each
/// tightly packed (no backend-specific row alignment). Every backend is responsible for
/// re-packing into its own native footprint (for example D3D12's 256-byte row-pitch alignment)
/// when it stages an upload; this layout only defines the CPU-side source buffer that
/// <see cref="ISilkGraphicsCommandList.UploadTexture(ISilkGraphicsTexture, ReadOnlySpan{byte})"/>
/// accepts.
/// </summary>
internal static class SilkMipChainLayout
{
    /// <summary>
    /// Computes the number of levels in the full mathematically valid mip chain for the given
    /// base dimensions: <c>floor(log2(max(width, height))) + 1</c>, down to a trailing 1x1 level.
    /// </summary>
    internal static uint GetMaxMipLevelCount(uint width, uint height)
    {
        ArgumentOutOfRangeException.ThrowIfZero(width);
        ArgumentOutOfRangeException.ThrowIfZero(height);
        uint largest = Math.Max(width, height);
        uint count = 1;
        while (largest > 1)
        {
            largest >>= 1;
            count++;
        }
        return count;
    }

    /// <summary>Computes one level's dimensions: <c>max(1, base >> level)</c> per axis.</summary>
    internal static (uint Width, uint Height) GetLevelExtent(
        uint baseWidth,
        uint baseHeight,
        uint level) =>
        (Math.Max(1u, baseWidth >> (int)level), Math.Max(1u, baseHeight >> (int)level));

    /// <summary>
    /// Builds the tightly packed, ascending-order layout for every level in
    /// <paramref name="mipLevelCount"/>, starting at byte offset zero.
    /// </summary>
    internal static SilkMipLevelLayout[] Create(
        uint baseWidth,
        uint baseHeight,
        SilkTextureFormat format,
        uint mipLevelCount)
    {
        ArgumentOutOfRangeException.ThrowIfZero(mipLevelCount);
        uint bytesPerPixel = SilkTextureFormats.GetBytesPerPixel(format);
        var levels = new SilkMipLevelLayout[mipLevelCount];
        int offset = 0;
        for (uint level = 0; level < mipLevelCount; level++)
        {
            (uint levelWidth, uint levelHeight) = GetLevelExtent(baseWidth, baseHeight, level);
            int rowPitch = checked((int)(levelWidth * bytesPerPixel));
            int size = checked(rowPitch * (int)levelHeight);
            levels[level] = new SilkMipLevelLayout(
                level,
                levelWidth,
                levelHeight,
                offset,
                rowPitch,
                size);
            offset = checked(offset + size);
        }
        return levels;
    }

    /// <summary>Computes the exact tightly packed byte size of a full chain upload.</summary>
    internal static int GetTotalByteSize(
        uint baseWidth,
        uint baseHeight,
        SilkTextureFormat format,
        uint mipLevelCount)
    {
        SilkMipLevelLayout[] levels = Create(baseWidth, baseHeight, format, mipLevelCount);
        SilkMipLevelLayout last = levels[^1];
        return checked(last.Offset + last.Size);
    }
}
