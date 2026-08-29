// Copyright (c) marcschier. Licensed under the MIT License.

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using OpenUsd.Interop;

namespace OpenUsd.Rendering.Silk;

internal sealed record SilkDecodedImage(
    uint Width,
    uint Height,
    byte[] Pixels,
    SilkTextureFormat Format = SilkTextureFormat.Rgba8Unorm);

internal static unsafe partial class SilkNativeImageDecoder
{
    private const uint ImageInfoVersion = 1;

    internal static SilkDecodedImage Decode(string asset, bool convertSrgbToLinear)
    {
        ArgumentException.ThrowIfNullOrEmpty(asset);
        NativeImageInfo info = new()
        {
            StructSize = (uint)sizeof(NativeImageInfo),
            Version = ImageInfoVersion
        };
        Span<byte> errorBytes = stackalloc byte[1024];
        fixed (byte* error = errorBytes)
        {
            NativeErrorBuffer errorBuffer = new(error, (nuint)errorBytes.Length);
            OpenUsdNativeStatus status = DecodeImageRgba8(
                asset,
                convertSrgbToLinear ? 1u : 0u,
                ref info,
                null,
                0,
                ref errorBuffer);
            if (status == OpenUsdNativeStatus.BufferTooSmall)
            {
                return DecodeRgba8(
                    asset,
                    convertSrgbToLinear,
                    info,
                    errorBytes,
                    error,
                    errorBuffer);
            }
            if (status is OpenUsdNativeStatus.NotFound or OpenUsdNativeStatus.InvalidArgument)
            {
                ThrowIfFailed(asset, status, errorBytes, errorBuffer);
            }
            return DecodeRgba32Float(
                asset,
                convertSrgbToLinear,
                ref info,
                errorBytes,
                error);
        }
    }

    private static SilkDecodedImage DecodeRgba8(
        string asset,
        bool convertSrgbToLinear,
        NativeImageInfo info,
        Span<byte> errorBytes,
        byte* error,
        NativeErrorBuffer errorBuffer)
    {
        byte[] pixels = new byte[checked((int)(info.Width * info.Height * 4))];
        fixed (byte* pixelBytes = pixels)
        {
            errorBuffer = new NativeErrorBuffer(error, (nuint)errorBytes.Length);
            OpenUsdNativeStatus status = DecodeImageRgba8(
                asset,
                convertSrgbToLinear ? 1u : 0u,
                ref info,
                pixelBytes,
                (nuint)pixels.Length,
                ref errorBuffer);
            ThrowIfFailed(asset, status, errorBytes, errorBuffer);
        }
        return new SilkDecodedImage(info.Width, info.Height, pixels);
    }

    private static SilkDecodedImage DecodeRgba32Float(
        string asset,
        bool convertSrgbToLinear,
        ref NativeImageInfo info,
        Span<byte> errorBytes,
        byte* error)
    {
        NativeErrorBuffer errorBuffer = new(error, (nuint)errorBytes.Length);
        OpenUsdNativeStatus status = DecodeImageRgba32Float(
            asset,
            convertSrgbToLinear ? 1u : 0u,
            ref info,
            null,
            0,
            ref errorBuffer);
        if (status != OpenUsdNativeStatus.BufferTooSmall)
        {
            ThrowIfFailed(asset, status, errorBytes, errorBuffer);
        }
        float[] values = new float[checked((int)(info.Width * info.Height * 4))];
        fixed (float* pixels = values)
        {
            errorBuffer = new NativeErrorBuffer(error, (nuint)errorBytes.Length);
            status = DecodeImageRgba32Float(
                asset,
                convertSrgbToLinear ? 1u : 0u,
                ref info,
                pixels,
                checked((nuint)values.Length * (nuint)sizeof(float)),
                ref errorBuffer);
            ThrowIfFailed(asset, status, errorBytes, errorBuffer);
        }
        byte[] bytes = MemoryMarshal.AsBytes(values.AsSpan()).ToArray();
        return new SilkDecodedImage(
            info.Width,
            info.Height,
            bytes,
            SilkTextureFormat.Rgba32Float);
    }

    private static void ThrowIfFailed(
        string asset,
        OpenUsdNativeStatus status,
        ReadOnlySpan<byte> errorBytes,
        NativeErrorBuffer error)
    {
        if (status == OpenUsdNativeStatus.Ok)
        {
            return;
        }
        int length = (int)Math.Min(error.Required, (nuint)errorBytes.Length);
        string message = length == 0
            ? $"Native image decode failed with status {status}."
            : System.Text.Encoding.UTF8.GetString(errorBytes[..length]);
        if (status == OpenUsdNativeStatus.NotFound)
        {
            throw new FileNotFoundException(message, asset);
        }
        throw new InvalidDataException(message);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeImageInfo
    {
        internal uint StructSize;
        internal uint Version;
        internal uint Width;
        internal uint Height;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeErrorBuffer
    {
        internal NativeErrorBuffer(byte* data, nuint capacity)
        {
            Data = data;
            Capacity = capacity;
            Required = 0;
        }

        internal byte* Data;
        internal nuint Capacity;
        internal nuint Required;
    }

    [LibraryImport(
        OpenUsdNativeContract.LibraryName,
        EntryPoint = "openusd_decode_image_rgba8",
        StringMarshalling = StringMarshalling.Utf8)]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    private static partial OpenUsdNativeStatus DecodeImageRgba8(
        string assetPath,
        uint convertSrgbToLinear,
        ref NativeImageInfo info,
        byte* rgba,
        nuint rgbaSize,
        ref NativeErrorBuffer error);

    [LibraryImport(
        OpenUsdNativeContract.LibraryName,
        EntryPoint = "openusd_decode_image_rgba32f",
        StringMarshalling = StringMarshalling.Utf8)]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    private static partial OpenUsdNativeStatus DecodeImageRgba32Float(
        string assetPath,
        uint convertSrgbToLinear,
        ref NativeImageInfo info,
        float* rgba,
        nuint rgbaSize,
        ref NativeErrorBuffer error);
}
