// Copyright (c) marcschier. Licensed under the MIT License.

using System.ComponentModel;
using System.Runtime.InteropServices;
using OpenUsd.Interop;

namespace OpenUsd.Rendering;

/// <summary>Writes bounded raw-working RGBA16Float EXR output through the project-owned native C ABI.</summary>
/// <remarks>
/// The initial file-handle implementation supports Windows x64 only. Input is exact, tightly packed
/// RGBA binary16 little-endian with finite samples. Negative/HDR values, signed zero and alpha are
/// preserved without widening, color conversion, exposure, premultiplication or an image-sized copy.
/// Output has explicit origin-zero equal data/display windows and square pixels. Primaries and
/// stored alpha association are unspecified by default; no Rec.709/ACES or straight-alpha claim is inferred.
/// </remarks>
public static class ExrRgba16FloatWriter
{
    /// <summary>Gets whether this host matches the implemented file-handle profile.</summary>
    /// <remarks>The matching Data ABI24/capability/export and Core native assets are additionally required.</remarks>
    public static bool IsSupported =>
        OperatingSystem.IsWindows() && RuntimeInformation.ProcessArchitecture == Architecture.X64 &&
        BitConverter.IsLittleEndian;

    /// <summary>Writes top-down half rows with raw-working primaries and stored alpha both unspecified.</summary>
    /// <returns>The encoded file extent in bytes on success.</returns>
    public static long Write(
        FileStream destination,
        int width,
        int height,
        ReadOnlySpan<byte> rgba16Float,
        long maximumBytes,
        CancellationToken cancellationToken = default) =>
        Write(destination, width, height, rgba16Float, Rgba16FloatRowOrder.TopDown,
            ExrStoredAlphaPolicy.Unspecified, maximumBytes, cancellationToken);

    /// <summary>Writes either physical half-row order without an image-sized flip copy.</summary>
    /// <returns>The encoded file extent in bytes on success.</returns>
    public static long Write(
        FileStream destination,
        int width,
        int height,
        ReadOnlySpan<byte> rgba16Float,
        Rgba16FloatRowOrder rowOrder,
        long maximumBytes,
        CancellationToken cancellationToken = default) =>
        Write(destination, width, height, rgba16Float, rowOrder,
            ExrStoredAlphaPolicy.Unspecified, maximumBytes, cancellationToken);

    /// <summary>Writes half rows with an explicit stored-alpha policy and no association conversion.</summary>
    /// <remarks>
    /// The caller owns a writable, seekable, synchronous, buffered regular FileStream that is empty,
    /// positioned at zero and under exclusive caller I/O ownership. Native and managed code hold its
    /// handle through synchronous cancellation/drain and never independently close or delete it.
    /// A success synchronizes FileStream.Position to EOF. Failure may leave bounded partial output;
    /// reset/truncate and seek to zero or discard before reuse. Caller owns flushing, durable storage,
    /// staging and atomic publication, including cancellation checks around publication.
    /// The byte ceiling bounds logical encoded extent including header rewrites, NOT native codec
    /// heap, kernel cache, caller input or capture storage. Cancellation is cooperative per scanline/IO
    /// boundary and cannot interrupt a hung filesystem. Dimensions are bounded to 8192 each and
    /// 67,108,864 pixels. No arbitrary authored windows/aspect/color metadata are accepted.
    /// </remarks>
    /// <returns>The encoded file extent in bytes on success.</returns>
    /// <exception cref="PlatformNotSupportedException">The host has no implemented file-handle profile.</exception>
    /// <exception cref="ArgumentException">
    /// Input extent, samples, enum values or output profile are invalid.
    /// </exception>
    /// <exception cref="NotSupportedException">The native file-handle profile is unsupported.</exception>
    /// <exception cref="RenderOutputQuotaExceededException">The encoded-byte quota would be exceeded.</exception>
    /// <exception cref="OperationCanceledException">Encoding was cancelled.</exception>
    /// <exception cref="IOException">Native I/O, codec or result-contract validation failed.</exception>
    public static long Write(
        FileStream destination,
        int width,
        int height,
        ReadOnlySpan<byte> rgba16Float,
        Rgba16FloatRowOrder rowOrder,
        ExrStoredAlphaPolicy alphaPolicy,
        long maximumBytes,
        CancellationToken cancellationToken = default)
    {
        if (!IsSupported)
        {
            throw new PlatformNotSupportedException(
                "The EXR output file-handle profile currently requires Windows x64.");
        }
        ArgumentNullException.ThrowIfNull(destination);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(width, 8192);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(height, 8192);
        ArgumentOutOfRangeException.ThrowIfNegative(maximumBytes);
        long pixels = (long)width * height;
        long required = pixels * 8;
        if (rgba16Float.Length != required)
        {
            throw new ArgumentException(
                $"RGBA16Float data must contain exactly {required} bytes.", nameof(rgba16Float));
        }
        if (rowOrder is not (Rgba16FloatRowOrder.TopDown or Rgba16FloatRowOrder.BottomUp))
        {
            throw new ArgumentOutOfRangeException(nameof(rowOrder));
        }
        if (alphaPolicy is not (ExrStoredAlphaPolicy.Unspecified or
            ExrStoredAlphaPolicy.Associated or ExrStoredAlphaPolicy.Unassociated))
        {
            throw new ArgumentOutOfRangeException(nameof(alphaPolicy));
        }
        if (!destination.CanWrite || !destination.CanSeek || destination.IsAsync)
        {
            throw new ArgumentException(
                "EXR output requires a writable, seekable synchronous FileStream.", nameof(destination));
        }
        if (destination.Length != 0 || destination.Position != 0)
        {
            throw new ArgumentException("EXR output requires an empty file positioned at zero.", nameof(destination));
        }
        cancellationToken.ThrowIfCancellationRequested();

        var request = new OpenUsdImageExrRequest
        {
            StructSize = 112,
            Version = 1,
            PixelFormat = 1,
            Width = (uint)width,
            Height = (uint)height,
            RowOrder = rowOrder == Rgba16FloatRowOrder.TopDown ? 1u : 2u,
            ColorPolicy = 1,
            AlphaPolicy = (uint)alphaPolicy + 1,
            WindowPolicy = 1,
            DisplayWidth = (uint)width,
            DisplayHeight = (uint)height,
            PixelAspectNumerator = 1,
            PixelAspectDenominator = 1,
            PixelCeiling = (ulong)pixels,
            OutputByteLimit = (ulong)maximumBytes
        };
        OpenUsdImageExrResult result = OpenUsdNativeRuntime.EncodeExrRgba16Float(
            destination, rgba16Float, request, cancellationToken);
        return result.Status switch
        {
            OpenUsdImageEncodeStatus.Ok => checked((long)result.EncodedBytes),
            OpenUsdImageEncodeStatus.InvalidInput => throw new ArgumentException(
                "The native encoder rejected the finite, aligned half input or the empty regular-file profile.",
                nameof(rgba16Float)),
            OpenUsdImageEncodeStatus.Unsupported => throw new NotSupportedException(
                "The native encoder does not support this file-handle profile."),
            OpenUsdImageEncodeStatus.QuotaExceeded => throw new RenderOutputQuotaExceededException(
                "EXR output would exceed its admitted encoded-byte quota."),
            OpenUsdImageEncodeStatus.Cancelled => throw new OperationCanceledException(cancellationToken),
            OpenUsdImageEncodeStatus.IoError => throw new IOException(
                $"Native EXR I/O failed with Win32 error {result.Win32Error}.",
                new Win32Exception((int)result.Win32Error)),
            OpenUsdImageEncodeStatus.OutOfMemory => throw new IOException(
                "The native EXR codec could not allocate its working storage."),
            _ => throw new IOException("The native EXR codec failed.")
        };
    }
}
