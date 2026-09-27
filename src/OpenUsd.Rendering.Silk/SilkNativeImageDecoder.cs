// Copyright (c) marcschier. Licensed under the MIT License.

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using OpenUsd.Interop;

namespace OpenUsd.Rendering.Silk;

internal sealed class SilkDecodedImage(
    uint width,
    uint height,
    byte[] pixels,
    SilkTextureFormat format = SilkTextureFormat.Rgba8Unorm) : IDisposable
{
    private IDisposable? _reservation;

    internal uint Width { get; } = width;
    internal uint Height { get; } = height;
    internal byte[] Pixels { get; } = pixels;
    internal SilkTextureFormat Format { get; } = format;

    internal void OwnReservation(IDisposable? reservation)
    {
        if (reservation is not null && Interlocked.CompareExchange(ref _reservation, reservation, null) is not null)
        {
            throw new InvalidOperationException("Decoded pixels already own a CPU reservation.");
        }
    }
    internal IDisposable? TransferReservation() => Interlocked.Exchange(ref _reservation, null);
    public void Dispose() => TransferReservation()?.Dispose();
}

/// <summary>
/// What an image library could observe about one image, beyond its shape.
/// </summary>
/// <remarks>
/// Each bit says that the library *answered* for that field. A describer that
/// never consults the library reports none of them, which is what lets a consumer
/// refuse exactly the case it could not observe instead of substituting a default
/// and calling it resolved.
/// </remarks>
[Flags]
internal enum SilkImageObservation
{
    /// <summary>Nothing beyond the shape was observed.</summary>
    None = 0,

    /// <summary>The source channel count was observed.</summary>
    ChannelCount = 1,

    /// <summary>The image library's own effective colour space was observed.</summary>
    ColorSpace = 2,

    /// <summary>The image carries horizontal sampler wrap metadata.</summary>
    AddressU = 4,

    /// <summary>The image carries vertical sampler wrap metadata.</summary>
    AddressV = 8,

    /// <summary>
    /// The image library was consulted at all. An axis that is queried but
    /// carries no metadata is USD's documented "no metadata" case; an axis that
    /// was never queried is a case no consumer may resolve.
    /// </summary>
    Queried = 16
}

/// <summary>The effective colour space an image library resolved for a file.</summary>
internal enum SilkImageColorSpaceObservation
{
    /// <summary>Linear, and never sRGB-decoded.</summary>
    Raw = 0,

    /// <summary>sRGB-encoded, and linearized on decode.</summary>
    Srgb = 1
}

/// <summary>One axis of sampler wrap metadata carried by an image file.</summary>
internal enum SilkImageAddressObservation
{
    /// <summary>Clamp to the edge texel.</summary>
    ClampToEdge = 0,

    /// <summary>Mirror once and then clamp; not representable on the wire.</summary>
    MirrorClampToEdge = 1,

    /// <summary>Repeat.</summary>
    Repeat = 2,

    /// <summary>Mirror and repeat.</summary>
    MirrorRepeat = 3,

    /// <summary>Clamp to a border colour.</summary>
    ClampToBorder = 4
}

/// <summary>
/// One image's declared shape and whatever the image library could observe about
/// how it should be read.
/// </summary>
/// <remarks>
/// This is what lets a budget be enforced before an allocation rather than after
/// one: an image whose header alone claims more texels than a consumer will
/// retain is refused without the decoder ever materializing it. It is also what
/// lets `sourceColorSpace = auto` and `wrap = useMetadata` be resolved from the
/// file rather than guessed from the decoded format.
/// </remarks>
internal readonly record struct SilkImageDescription(
    uint Width,
    uint Height,
    SilkTextureFormat Format,
    uint ChannelCount = 0,
    SilkImageObservation Observed = SilkImageObservation.None,
    SilkImageColorSpaceObservation ColorSpace = SilkImageColorSpaceObservation.Raw,
    SilkImageAddressObservation AddressU = SilkImageAddressObservation.ClampToEdge,
    SilkImageAddressObservation AddressV = SilkImageAddressObservation.ClampToEdge);

internal sealed record SilkUdimTile(uint Number, string Asset);

internal static unsafe partial class SilkNativeImageDecoder
{
    internal static SilkDecodedImage Decode(string asset, bool convertSrgbToLinear)
        => Decode(asset, convertSrgbToLinear, null);

    internal static SilkDecodedImage Decode(
        string asset, bool convertSrgbToLinear, SilkCpuTextureBudget? budget)
    {
        ArgumentException.ThrowIfNullOrEmpty(asset);
        OpenUsdNativeImageInfo info = OpenUsdNativeImageInfo.Create();
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
                    errorBuffer,
                    budget);
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
                error,
                budget);
        }
    }

    /// <summary>
    /// Reads one image's declared width, height and decoded format from its
    /// header, without decoding it.
    /// </summary>
    /// <remarks>
    /// The native decoder's own two-phase contract is what makes this exact: a
    /// call with no destination buffer reports the shape and answers
    /// <c>BufferTooSmall</c>. The eight-bit entry point is probed first for the
    /// same reason <see cref="Decode(string, bool)"/> probes it first -- an image it can
    /// represent is decoded as eight-bit -- so the format reported here is the
    /// format a later decode produces.
    /// </remarks>
    internal static SilkImageDescription Describe(string asset)
    {
        ArgumentException.ThrowIfNullOrEmpty(asset);
        OpenUsdNativeImageInfo info = OpenUsdNativeImageInfo.Create();
        Span<byte> errorBytes = stackalloc byte[1024];
        fixed (byte* error = errorBytes)
        {
            NativeErrorBuffer errorBuffer = new(error, (nuint)errorBytes.Length);
            OpenUsdNativeStatus status = DecodeImageRgba8(
                asset,
                0,
                ref info,
                null,
                0,
                ref errorBuffer);
            if (status == OpenUsdNativeStatus.BufferTooSmall)
            {
                return Describe(info, SilkTextureFormat.Rgba8Unorm);
            }
            if (status is OpenUsdNativeStatus.NotFound or OpenUsdNativeStatus.InvalidArgument)
            {
                ThrowIfFailed(asset, status, errorBytes, errorBuffer);
            }
            errorBuffer = new NativeErrorBuffer(error, (nuint)errorBytes.Length);
            status = DecodeImageRgba32Float(
                asset,
                0,
                ref info,
                null,
                0,
                ref errorBuffer);
            if (status != OpenUsdNativeStatus.BufferTooSmall)
            {
                ThrowIfFailed(asset, status, errorBytes, errorBuffer);
            }
            return Describe(info, SilkTextureFormat.Rgba32Float);
        }
    }

    internal static IReadOnlyList<SilkUdimTile> ResolveUdimTiles(string asset)
    {
        string[] values;
        try
        {
            values = OpenUsdNativeRuntime.ResolveUdimTiles(asset);
        }
        catch (OpenUsdNativeException exception)
            when (exception.Status == OpenUsdNativeStatus.NotFound)
        {
            throw new FileNotFoundException(exception.Message, asset, exception);
        }
        if ((values.Length % 2) != 0)
        {
            throw new InvalidDataException(
                "Native UDIM resolution returned an incomplete tile-path pair.");
        }
        var result = new SilkUdimTile[values.Length / 2];
        for (int index = 0; index < result.Length; index++)
        {
            string tile = values[index * 2];
            string path = values[(index * 2) + 1];
            if (!uint.TryParse(
                    tile,
                    System.Globalization.NumberStyles.None,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out uint number) ||
                number < 1001 ||
                number > 1999 ||
                string.IsNullOrWhiteSpace(path))
            {
                throw new InvalidDataException(
                    "Native UDIM resolution returned an invalid tile-path pair.");
            }
            result[index] = new SilkUdimTile(number, path);
        }
        return result;
    }

    private static SilkDecodedImage DecodeRgba8(
        string asset,
        bool convertSrgbToLinear,
        OpenUsdNativeImageInfo info,
        Span<byte> errorBytes,
        byte* error,
        NativeErrorBuffer errorBuffer,
        SilkCpuTextureBudget? budget)
    {
        int byteCount = checked((int)((ulong)info.Width * info.Height * 4));
        IDisposable? reservation = budget?.Reserve((ulong)byteCount);
        try
        {
            byte[] pixels = new byte[byteCount];
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
            if (checked((ulong)info.Width * info.Height * 4) != (ulong)byteCount)
            {
                throw new InvalidDataException("Image dimensions changed during decoding.");
            }
            var image = new SilkDecodedImage(info.Width, info.Height, pixels);
            image.OwnReservation(reservation);
            return image;
        }
        catch
        {
            reservation?.Dispose();
            throw;
        }
    }

    private static SilkDecodedImage DecodeRgba32Float(
        string asset,
        bool convertSrgbToLinear,
        ref OpenUsdNativeImageInfo info,
        Span<byte> errorBytes,
        byte* error,
        SilkCpuTextureBudget? budget)
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
        int byteCount = checked((int)((ulong)info.Width * info.Height * 4 * sizeof(float)));
        IDisposable? reservation = budget?.Reserve((ulong)byteCount);
        try
        {
            byte[] bytes = new byte[byteCount];
            fixed (byte* pixels = bytes)
            {
                errorBuffer = new NativeErrorBuffer(error, (nuint)errorBytes.Length);
                status = DecodeImageRgba32Float(
                    asset,
                    convertSrgbToLinear ? 1u : 0u,
                    ref info,
                    (float*)pixels,
                    (nuint)bytes.Length,
                    ref errorBuffer);
                ThrowIfFailed(asset, status, errorBytes, errorBuffer);
            }
            if (checked((ulong)info.Width * info.Height * 4 * sizeof(float)) != (ulong)byteCount)
            {
                throw new InvalidDataException("Image dimensions changed during decoding.");
            }
            var image = new SilkDecodedImage(info.Width, info.Height, bytes, SilkTextureFormat.Rgba32Float);
            image.OwnReservation(reservation);
            return image;
        }
        catch
        {
            reservation?.Dispose();
            throw;
        }
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

    /// <summary>
    /// Projects one native image-info answer onto the renderer's own description.
    /// </summary>
    /// <remarks>
    /// The layout itself lives in <see cref="OpenUsdNativeImageInfo"/>, which is
    /// the single managed statement of this seam. This decoder used to carry a
    /// private copy, and a private copy is exactly how one path reaches version
    /// two while another keeps writing version one.
    /// </remarks>
    private static SilkImageDescription Describe(
        OpenUsdNativeImageInfo info,
        SilkTextureFormat format)
    {
        SilkImageObservation observed = SilkImageObservation.Queried;
        if (info.Observed.HasFlag(OpenUsdNativeImageObservation.ChannelCount))
        {
            observed |= SilkImageObservation.ChannelCount;
        }
        if (info.Observed.HasFlag(OpenUsdNativeImageObservation.ColorSpace))
        {
            observed |= SilkImageObservation.ColorSpace;
        }
        if (info.Observed.HasFlag(OpenUsdNativeImageObservation.AddressU))
        {
            observed |= SilkImageObservation.AddressU;
        }
        if (info.Observed.HasFlag(OpenUsdNativeImageObservation.AddressV))
        {
            observed |= SilkImageObservation.AddressV;
        }
        return new SilkImageDescription(
            info.Width,
            info.Height,
            format,
            info.ChannelCount,
            observed,
            info.ColorSpace == OpenUsdNativeImageColorSpace.Srgb
                ? SilkImageColorSpaceObservation.Srgb
                : SilkImageColorSpaceObservation.Raw,
            (SilkImageAddressObservation)Math.Min((uint)info.AddressU, 4u),
            (SilkImageAddressObservation)Math.Min((uint)info.AddressV, 4u));
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
        ref OpenUsdNativeImageInfo info,
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
        ref OpenUsdNativeImageInfo info,
        float* rgba,
        nuint rgbaSize,
        ref NativeErrorBuffer error);
}
