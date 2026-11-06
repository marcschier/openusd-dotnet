// Copyright (c) marcschier. Licensed under the MIT License.

namespace OpenUsd.Rendering.Silk;

internal interface ISilkTextureRowUploadCommandList
{
    void UploadTextureRows(
        ISilkGraphicsTexture texture, SilkTextureRowUpload region, ReadOnlySpan<byte> source);
}

internal interface ISilkMaterialMipReadback
{
    void ReadMipForTesting(uint mipLevel, Span<byte> destination);
}

internal readonly record struct SilkTextureRowUpload(uint MipLevel, uint FirstRow, uint RowCount)
{
    internal static void ValidateReadback(ISilkGraphicsTexture texture, uint mipLevel, int length)
    {
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(mipLevel, texture.MipLevelCount);
        if (texture.Format is not (SilkTextureFormat.Rgba8Unorm or SilkTextureFormat.Rgba32Float or
            SilkTextureFormat.R32Float or SilkTextureFormat.Rg32Float or SilkTextureFormat.R8Unorm))
        {
            throw new InvalidOperationException(
                "Material mip readback requires an RGBA8, RGBA32F, RG32F, R8 or R32F texture.");
        }
        (uint width, uint height) = SilkMipChainLayout.GetLevelExtent(texture.Width, texture.Height, mipLevel);
        if (!texture.Usage.HasFlag(SilkTextureUsage.CopySource) ||
            length != SilkMipChainLayout.GetTotalByteSize(width, height, texture.Format, 1))
        {
            throw new ArgumentException("The mip readback extent or texture usage is invalid.", nameof(length));
        }
    }

    internal SilkMipLevelLayout GetLayout(ISilkGraphicsTexture texture)
    {
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(MipLevel, texture.MipLevelCount);
        ArgumentOutOfRangeException.ThrowIfZero(RowCount);
        if (texture.Format is not (SilkTextureFormat.Rgba8Unorm or SilkTextureFormat.Rgba32Float or
            SilkTextureFormat.R32Float or SilkTextureFormat.Rg32Float or SilkTextureFormat.R8Unorm) ||
            !texture.Usage.HasFlag(SilkTextureUsage.CopyDestination))
        {
            throw new InvalidOperationException(
                "Row uploads require a writable RGBA8, RGBA32F, RG32F, R8 or R32F material texture.");
        }
        (uint width, uint height) = SilkMipChainLayout.GetLevelExtent(texture.Width, texture.Height, MipLevel);
        if (FirstRow >= height || RowCount > height - FirstRow)
        {
            throw new InvalidOperationException("The upload rows exceed their mip level.");
        }
        int rowPitch = checked((int)(width * SilkTextureFormats.GetBytesPerPixel(texture.Format)));
        return new(MipLevel, width, RowCount, 0, rowPitch, checked(rowPitch * (int)RowCount));
    }

    internal void Validate(ISilkGraphicsTexture texture, int sourceLength)
    {
        int required = GetLayout(texture).Size;
        if (sourceLength != required)
        {
            throw new ArgumentException($"The row upload must contain exactly {required} bytes.", nameof(sourceLength));
        }
    }
}
