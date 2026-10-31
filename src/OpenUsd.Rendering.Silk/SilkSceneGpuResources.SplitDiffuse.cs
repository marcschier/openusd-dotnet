// Copyright (c) marcschier. Licensed under the MIT License.

namespace OpenUsd.Rendering.Silk;

public sealed partial class SilkSceneGpuResources
{
    private bool? _materialRowUploadsSupported;

    private bool UsesCompactRgb(SilkMaterialData? material, SilkMaterialParameter parameter) =>
        StreamsMaterialTextures && material is { UsesRuntimeMaterialShader: false } &&
        parameter is SilkMaterialParameter.DiffuseColor or SilkMaterialParameter.EmissiveColor &&
        material.SurfaceKind is SilkSurfaceKind.PreviewSurface or SilkSurfaceKind.MdlDistilled &&
        material.GetCompositeTexture() is null &&
        material.GetTexture(parameter) is { ComponentCount: 3, Channel: SilkTextureChannel.Rgb } texture &&
        !texture.Asset.Contains("<UDIM>", StringComparison.Ordinal) && RequiresFloatTextureAffine(texture) &&
        SupportsMaterialRowUploads();

    private bool SupportsMaterialRowUploads()
    {
        if (_materialRowUploadsSupported is { } supported)
        {
            return supported;
        }
        using ISilkGraphicsCommandList probe = _device.CreateCommandList();
        _materialRowUploadsSupported = probe is ISilkTextureRowUploadCommandList;
        return _materialRowUploadsSupported.Value;
    }

    private TextureCacheEntry CreateStreamedSplitDiffuse(
        TextureCacheKey key, SilkDecodedImage image,
        Dictionary<TextureCacheKey, TextureCacheEntry> cache, IReadOnlyList<string>? dependencies)
    {
        if (image.Format != SilkTextureFormat.Rgba32Float)
        {
            throw new InvalidDataException("Split diffuse storage requires an exact RGBA32F source.");
        }
        SilkMipGenerator.ValidateBaseLevel(image);
        uint levels = SilkMipChainLayout.GetMaxMipLevelCount(image.Width, image.Height);
        ulong bytes = checked(
            SilkMipChainLayout.GetLogicalByteSize(image.Width, image.Height, SilkTextureFormat.Rg32Float, levels) +
            SilkMipChainLayout.GetLogicalByteSize(image.Width, image.Height, SilkTextureFormat.R32Float, levels));
        TrimStreamedTextureReuse(bytes, key.MaterialPath);
        ISilkGraphicsTexture? rg = null;
        ISilkGraphicsTexture? blue = null;
        SilkDecodedImage current = image;
        try
        {
            const SilkTextureUsage usage = SilkTextureUsage.Sampled |
                SilkTextureUsage.CopySource | SilkTextureUsage.CopyDestination;
            rg = _device.CreateTexture2D(new(image.Width, image.Height, SilkTextureFormat.Rg32Float, usage, levels));
            blue = _device.CreateTexture2D(new(image.Width, image.Height, SilkTextureFormat.R32Float, usage, levels));
            for (uint level = 0; level < levels; level++)
            {
                UploadSplitDiffuseRows(rg, blue, current, level);
                if (level + 1 < levels)
                {
                    SilkDecodedImage next = SilkMipGenerator.GenerateNextLevel(current, false, _cpuTextureBudget);
                    current.Dispose();
                    current = next;
                }
            }
            var entry = new TextureCacheEntry(rg, [], bytes, false, CaptureDependencies(dependencies),
                auxiliaryTexture: blue)
            {
                Uploaded = true
            };
            RegisterEntry(entry);
            cache.Add(key, entry);
            rg = null;
            blue = null;
            return entry;
        }
        finally
        {
            current.Dispose();
            try
            {
                rg?.Dispose();
            }
            finally
            {
                blue?.Dispose();
            }
        }
    }

    private void UploadSplitDiffuseRows(
        ISilkGraphicsTexture rg, ISilkGraphicsTexture blue, SilkDecodedImage image, uint level)
    {
        const ulong maximumBandBytes = 8 * 1024 * 1024;
        uint rowBytes = checked(image.Width * 8);
        ulong available = _cpuTextureBudget is null ? maximumBandBytes :
            (_cpuTextureBudget.MaximumBytes - _cpuTextureBudget.Usage.ReservedBytes) / 2;
        uint bandRows = checked((uint)Math.Min(image.Height, Math.Min(maximumBandBytes, available) / rowBytes));
        if (bandRows == 0)
        {
            throw new SilkCpuTextureBudgetExceededException(2ul * rowBytes,
                _cpuTextureBudget?.Usage.ReservedBytes ?? 0, _cpuTextureBudget?.MaximumBytes ?? maximumBandBytes);
        }
        int bandBytes = checked((int)(bandRows * rowBytes));
        using IDisposable? reservation = _cpuTextureBudget?.Reserve((ulong)bandBytes);
        byte[] packed = new byte[bandBytes];
        for (uint first = 0; first < image.Height;)
        {
            _materialCaptureRequest?.ThrowIfCancellationRequested();
            uint rows = Math.Min(bandRows, image.Height - first);
            int pixels = checked((int)(rows * image.Width));
            int source = checked((int)(first * image.Width * 16));
            for (int pixel = 0; pixel < pixels; pixel++)
            {
                image.Pixels.AsSpan(source + pixel * 16, 8).CopyTo(packed.AsSpan(pixel * 8));
            }
            UploadTextureRowBand(rg, new(level, first, rows), packed.AsSpan(0, pixels * 8));
            _materialCaptureRequest?.ThrowIfCancellationRequested();
            for (int pixel = 0; pixel < pixels; pixel++)
            {
                image.Pixels.AsSpan(source + pixel * 16 + 8, 4).CopyTo(packed.AsSpan(pixel * 4));
            }
            UploadTextureRowBand(blue, new(level, first, rows), packed.AsSpan(0, pixels * 4));
            _materialCaptureRequest?.ThrowIfCancellationRequested();
            first += rows;
        }
        _materialCaptureRequest?.ThrowIfCancellationRequested();
    }
}
