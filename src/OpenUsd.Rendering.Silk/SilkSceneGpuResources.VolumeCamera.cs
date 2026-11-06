// Copyright (c) marcschier. Licensed under the MIT License.

using System.Runtime.CompilerServices;

namespace OpenUsd.Rendering.Silk;

public sealed partial class SilkSceneGpuResources
{
    private const ulong MaximumVolumeCameraCpuBytes = 512UL * 1024 * 1024;
    private readonly SilkCpuTextureBudget _volumeCameraCpuBudget =
        new(MaximumVolumeCameraCpuBytes);
    private SilkGpuTextureBudget? _volumeCameraGpuBudget;

    internal SilkGpuTextureUsage? VolumeCameraAtlasGpuUsage => _volumeCameraGpuBudget?.Usage;

    internal IDisposable ReserveVolumeCameraCpu(ulong bytes)
    {
        IDisposable volumeReservation = _volumeCameraCpuBudget.Reserve(bytes);
        try
        {
            IDisposable? sharedReservation = _cpuTextureBudget?.Reserve(bytes);
            return sharedReservation is null
                ? volumeReservation
                : new CombinedReservation(volumeReservation, sharedReservation);
        }
        catch
        {
            volumeReservation.Dispose();
            throw;
        }
    }

    internal IDisposable ReserveVolumeCameraAtlas(uint width, uint height, uint depth, int fieldCount)
    {
        SilkGpuTextureBudget budget = _volumeCameraGpuBudget ??=
            new SilkGpuTextureBudget(_residencyOptions.MaxGpuBytes);
        IDisposable gpuReservation = budget.Reserve(
            new SilkTextureDescriptor(
                width,
                height,
                SilkTextureFormat.R32Float,
                SilkTextureUsage.Sampled | SilkTextureUsage.CopyDestination),
            depth);
        IDisposable? metadataReservation = null;
        try
        {
            metadataReservation = ReserveVolumeCameraCpu(
                checked((ulong)fieldCount * (uint)Unsafe.SizeOf<SilkVolumeDensitySamples>()));
            var combined = new CombinedReservation(gpuReservation, metadataReservation);
            metadataReservation = null;
            return combined;
        }
        catch
        {
            metadataReservation?.Dispose();
            gpuReservation.Dispose();
            throw;
        }
    }

    internal static byte[] CopyVolumeCameraField(
        SilkMaterialTexture texture,
        SilkVolumeTextureExtent extent) =>
        CopyVolumeCameraField(texture, extent, out _);

    internal static byte[] CopyVolumeCameraField(
        SilkMaterialTexture texture,
        SilkVolumeTextureExtent extent,
        out SilkVolumeDensitySamples samples)
    {
        ArgumentNullException.ThrowIfNull(texture);
        ulong byteCount = checked(
            (ulong)extent.Width * extent.Height * extent.Depth * sizeof(float));
        if (byteCount > int.MaxValue)
        {
            throw new InvalidDataException(
                $"Density field '{texture.Asset}' exceeds the managed array limit.");
        }

        var pixels = new byte[checked((int)byteCount)];
        if (SilkVolumeTextureEncoding.IsNativeAsset(texture.Asset))
        {
            if (!texture.HasNativeVolumeAssets || !texture.TryCopyVolumeAssets(pixels))
            {
                throw new InvalidDataException(
                    $"Native density field '{texture.Asset}' has no retained decoded-field lease.");
            }
            samples = SilkVolumeCameraValidation.InspectDensitySamples(pixels, texture.Asset);
            return pixels;
        }

        try
        {
            using var source = new FileStream(texture.Asset, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (source.Length != pixels.Length)
            {
                throw new InvalidDataException(
                    $"Volume texture '{texture.Asset}' contains {source.Length} bytes, expected {pixels.Length}.");
            }
            source.ReadExactly(pixels);
            if (source.ReadByte() != -1)
            {
                throw new InvalidDataException($"Volume texture '{texture.Asset}' changed during reading.");
            }
        }
        catch (IOException exception) when (exception is FileNotFoundException or DirectoryNotFoundException)
        {
            throw new InvalidDataException(
                $"Density field '{texture.Asset}' is missing; the volume update was refused.", exception);
        }
        samples = SilkVolumeCameraValidation.InspectDensitySamples(pixels, texture.Asset);
        return pixels;
    }

    private sealed class CombinedReservation(
        IDisposable first,
        IDisposable second) : IDisposable
    {
        private IDisposable? _first = first;
        private IDisposable? _second = second;

        public void Dispose()
        {
            Interlocked.Exchange(ref _second, null)?.Dispose();
            Interlocked.Exchange(ref _first, null)?.Dispose();
        }
    }
}
