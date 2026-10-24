// Copyright (c) marcschier. Licensed under the MIT License.

using System.Buffers.Binary;
using System.Runtime.InteropServices;

namespace OpenUsd.Rendering.Silk;

public sealed partial class SilkSceneGpuResources
{
    private bool UsesIndexedNormal(SilkMaterialData? material, SilkMaterialParameter parameter)
    {
        if (!StreamsMaterialTextures || material is not { UsesRuntimeMaterialShader: false } ||
            material.SurfaceKind is not (SilkSurfaceKind.PreviewSurface or SilkSurfaceKind.MdlDistilled) ||
            parameter is not (SilkMaterialParameter.Normal or SilkMaterialParameter.DetailNormal) ||
            material.GetTexture(parameter) is not { ComponentCount: 3, Channel: SilkTextureChannel.Rgb } texture ||
            texture.Asset.Contains("<UDIM>", StringComparison.Ordinal) || !RequiresFloatTextureAffine(texture))
        {
            return false;
        }
        return SupportsMaterialRowUploads();
    }

    private TextureCacheEntry CreateStreamedIndexedRgb(
        TextureCacheKey key, SilkDecodedImage image,
        Dictionary<TextureCacheKey, TextureCacheEntry> cache, IReadOnlyList<string>? dependencies,
        bool isNormalMap)
    {
        SilkMipGenerator.ValidateBaseLevel(image);
        if (image.Format != SilkTextureFormat.Rgba32Float)
        {
            throw new InvalidDataException("Indexed RGB requires the original float32 component values.");
        }
        uint levels = SilkMipChainLayout.GetMaxMipLevelCount(image.Width, image.Height);
        ulong markerBytes = SilkMipChainLayout.GetLogicalByteSize(
            image.Width, image.Height, SilkTextureFormat.Rgba8Unorm, levels);
        ulong tailPixels = markerBytes / 4 - (ulong)image.Width * image.Height;
        uint tupleWidth = Math.Clamp(image.Width, 256u, 4096u);
        uint valueWidth = checked(tupleWidth * 3);
        uint valueHeight = checked((uint)((256 + tailPixels + tupleWidth - 1) / tupleWidth));
        ulong valueBytes = (ulong)valueWidth * valueHeight * 4;
        ulong bytes = checked(markerBytes + valueBytes);
        Dictionary<uint, byte>[] indices = [new(256), new(256), new(256)];
        Span<byte> palette = stackalloc byte[256 * 12];
        palette.Clear();
        // Diffuse uses equal primary/table widths for its exact split-RGB fallback,
        // and unequal widths for indices. Never admit an ambiguous representation.
        if ((!isNormalMap && valueWidth == image.Width) ||
            valueWidth > _device.Capabilities.MaxTextureDimension2D ||
            valueHeight > _device.Capabilities.MaxTextureDimension2D ||
            256 + tailPixels > 0x1000000 || bytes >= markerBytes * (isNormalMap ? 4ul : 3ul) ||
            !TryIndexRgbBase(image, indices, palette))
        {
            if (!isNormalMap)
            {
                return CreateStreamedSplitDiffuse(key, image, cache, dependencies);
            }
            // Alpha is not a normal input. A sentinel keeps arbitrary float sources
            // and small fallback images on the original path, without truncating RGB.
            Span<float> values = MemoryMarshal.Cast<byte, float>(image.Pixels.AsSpan());
            for (int offset = 3; offset < values.Length; offset += 4)
            {
                values[offset] = 2;
            }
            return CreateStreamedMipEntry(key, image, cache, dependencies, true);
        }

        TrimStreamedTextureReuse(bytes, key.MaterialPath);
        ISilkGraphicsTexture? marker = null;
        ISilkGraphicsTexture? valuesTexture = null;
        SilkDecodedImage current = image;
        try
        {
            const SilkTextureUsage usage = SilkTextureUsage.Sampled |
                SilkTextureUsage.CopySource | SilkTextureUsage.CopyDestination;
            marker = _device.CreateTexture2D(new(
                image.Width, image.Height, SilkTextureFormat.Rgba8Unorm, usage, levels));
            valuesTexture = _device.CreateTexture2D(new(
                valueWidth, valueHeight, SilkTextureFormat.R32Float, usage));

            const ulong maximumBandBytes = 1024 * 1024;
            ulong available = _cpuTextureBudget is null ? maximumBandBytes :
                (_cpuTextureBudget.MaximumBytes - _cpuTextureBudget.Usage.ReservedBytes) / 4;
            uint valueRowBytes = valueWidth * 4;
            uint markerRowBytes = checked(image.Width * 4);
            ulong minimum = Math.Max(valueRowBytes, markerRowBytes);
            if (available < minimum)
            {
                throw new SilkCpuTextureBudgetExceededException(minimum * 4,
                    _cpuTextureBudget?.Usage.ReservedBytes ?? 0,
                    _cpuTextureBudget?.MaximumBytes ?? maximumBandBytes);
            }
            ulong bandLimit = Math.Min(maximumBandBytes, available);
            int valueBandBytes = checked((int)(Math.Min(valueHeight, bandLimit / valueRowBytes) * valueRowBytes));
            uint markerBandRows = checked((uint)Math.Min(image.Height, bandLimit / markerRowBytes));
            int markerBandBytes = checked((int)(markerBandRows * markerRowBytes));
            using IDisposable? reservation = _cpuTextureBudget?.Reserve(checked((ulong)valueBandBytes + (
                ulong)markerBandBytes));
            byte[] valueBand = new byte[valueBandBytes];
            byte[] markerBand = new byte[markerBandBytes];
            int pending = 0;
            uint valueFirstRow = 0;
            appendValues(palette);
            uint tailIndex = 256;
            for (uint level = 0; level < levels; level++)
            {
                for (uint first = 0; first < current.Height;)
                {
                    _materialCaptureRequest?.ThrowIfCancellationRequested();
                    uint rows = Math.Min(markerBandRows, current.Height - first);
                    int count = checked((int)(rows * current.Width));
                    ReadOnlySpan<uint> source = MemoryMarshal.Cast<byte, uint>(
                        current.Pixels.AsSpan(checked((int)(first * current.Width * 16)), count * 16));
                    for (int pixel = 0; pixel < count; pixel++)
                    {
                        int offset = pixel * 4;
                        if (level == 0)
                        {
                            markerBand[offset] = indices[0][source[offset]];
                            markerBand[offset + 1] = indices[1][source[offset + 1]];
                            markerBand[offset + 2] = indices[2][source[offset + 2]];
                            markerBand[offset + 3] = 0;
                        }
                        else
                        {
                            BinaryPrimitives.WriteUInt32LittleEndian(
                                markerBand.AsSpan(offset, 4), 0xff000000 | tailIndex++);
                        }
                    }
                    UploadTextureRowBand(marker, new(level, first, rows), markerBand.AsSpan(0, count * 4));
                    _materialCaptureRequest?.ThrowIfCancellationRequested();
                    if (level != 0)
                    {
                        for (int pixel = 0; pixel < count; pixel++)
                        {
                            appendValues(MemoryMarshal.AsBytes(source.Slice(pixel * 4, 3)));
                        }
                    }
                    first += rows;
                }
                if (level + 1 < levels)
                {
                    SilkDecodedImage next = SilkMipGenerator.GenerateNextLevel(current, isNormalMap, _cpuTextureBudget);
                    current.Dispose();
                    current = next;
                }
            }
            if (pending != 0)
            {
                _materialCaptureRequest?.ThrowIfCancellationRequested();
                int padded = checked((int)(((uint)pending + valueRowBytes - 1) / valueRowBytes * valueRowBytes));
                valueBand.AsSpan(pending, padded - pending).Clear();
                UploadTextureRowBand(valuesTexture, new(0, valueFirstRow, (uint)padded / valueRowBytes),
                    valueBand.AsSpan(0, padded));
            }
            _materialCaptureRequest?.ThrowIfCancellationRequested();
            var entry = new TextureCacheEntry(marker, [], bytes, false, CaptureDependencies(dependencies),
                auxiliaryTexture: valuesTexture)
            {
                Uploaded = true
            };
            RegisterEntry(entry);
            cache.Add(key, entry);
            marker = null;
            valuesTexture = null;
            return entry;

            void appendValues(ReadOnlySpan<byte> source)
            {
                while (!source.IsEmpty)
                {
                    int count = Math.Min(source.Length, valueBand.Length - pending);
                    source[..count].CopyTo(valueBand.AsSpan(pending));
                    pending += count;
                    source = source[count..];
                    if (pending == valueBand.Length)
                    {
                        _materialCaptureRequest?.ThrowIfCancellationRequested();
                        uint rows = (uint)pending / valueRowBytes;
                        UploadTextureRowBand(valuesTexture, new(0, valueFirstRow, rows), valueBand);
                        _materialCaptureRequest?.ThrowIfCancellationRequested();
                        valueFirstRow += rows;
                        pending = 0;
                    }
                }
            }
        }
        finally
        {
            current.Dispose();
            try
            {
                marker?.Dispose();
            }
            finally
            {
                valuesTexture?.Dispose();
            }
        }
    }

    private bool TryIndexRgbBase(SilkDecodedImage image, Dictionary<uint, byte>[] indices, Span<byte> palette)
    {
        ReadOnlySpan<uint> source = MemoryMarshal.Cast<byte, uint>(image.Pixels);
        for (uint row = 0; row < image.Height; row++)
        {
            _materialCaptureRequest?.ThrowIfCancellationRequested();
            int start = checked((int)(row * image.Width * 4));
            int end = checked(start + (int)image.Width * 4);
            for (int offset = start; offset < end; offset += 4)
            {
                for (int component = 0; component < 3; component++)
                {
                    uint bits = source[offset + component];
                    Dictionary<uint, byte> channel = indices[component];
                    if (!channel.ContainsKey(bits))
                    {
                        if (channel.Count == 256)
                        {
                            return false;
                        }
                        byte index = checked((byte)channel.Count);
                        channel.Add(bits, index);
                        BinaryPrimitives.WriteUInt32LittleEndian(palette.Slice(index * 12 + component * 4, 4), bits);
                    }
                }
            }
        }
        return true;
    }
}
