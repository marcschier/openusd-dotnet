// Copyright (c) marcschier. Licensed under the MIT License.

using System.Runtime.InteropServices;

namespace OpenUsd.Rendering.Silk;

/// <summary>Identifies the numerical meaning of a captured depth sample.</summary>
public enum SilkDepthConvention
{
    /// <summary>
    /// Forward device depth in [0, 1], after the perspective divide. Standard OpenUSD
    /// projections map the near plane to zero and the far plane to one.
    /// </summary>
    NormalizedDeviceDepthZeroToOne
}

/// <summary>Managed-owned samples from the visible render pass's D32Float attachment.</summary>
/// <remarks>
/// <para>
/// Samples are not camera-space distance, ray length, or color. Perspective depth is
/// nonlinear. The renderer maps OpenGL clip Z to (Z + W) / 2 before rasterization;
/// this capture reads the resulting device Z/W without another transformation.
/// </para>
/// <para>
/// Untouched pixels contain exactly <see cref="ClearValue"/>. The visible pass uses
/// LessEqual depth testing, so a far-plane write (including one rounded to one) cannot
/// be distinguished from clear. This is not a coverage mask. Transparent draws with
/// depth writes disabled, display transforms, and selection outlines add no depth.
/// </para>
/// <para>
/// Storage is detached from the renderer, session, and graphics device, is not pooled,
/// and remains valid after their disposal and subsequent captures. No GPU handle is
/// retained by this result.
/// </para>
/// </remarks>
public sealed class SilkDepthCaptureResult
{
    internal SilkDepthCaptureResult(int width, int height, float[] values)
    {
        Width = width;
        Height = height;
        Values = values;
    }

    /// <summary>Gets the width in pixels, equal to the accompanying color capture.</summary>
    public int Width { get; }

    /// <summary>Gets the height in pixels, equal to the accompanying color capture.</summary>
    public int Height { get; }

    /// <summary>
    /// Gets exactly Width * Height float32 values in top-down, left-to-right row order,
    /// without padding. The sample at (x, y) is at index y * Width + x.
    /// </summary>
    public ReadOnlyMemory<float> Values { get; }

    /// <summary>Gets the explicitly normalized, non-metric depth convention.</summary>
    public SilkDepthConvention Convention { get; } = SilkDepthConvention.NormalizedDeviceDepthZeroToOne;

    /// <summary>Gets the deterministic untouched-depth value, also used by far-plane writes.</summary>
    public float ClearValue { get; } = 1f;
}

/// <summary>Bounds the raster and managed pixel storage of an opt-in color-and-depth capture.</summary>
/// <remarks>
/// These limits are checked before target allocation or session synchronization.
/// The raster is additionally limited to 16,384 pixels per dimension and 16,777,216
/// pixels in total. GPU staging and capture targets are bounded by that raster size;
/// backend allocation alignment, native color-conversion scratch, and the renderer's
/// existing scene, texture, shadow, display-transform, and selection infrastructure
/// are not charged to the managed readback quota. The managed RGBA8 selection-upload
/// copy is included. A 4096 by 4096 capture requires 320 MiB and therefore exceeds
/// the default 256 MiB managed byte budget even though it fits the pixel ceiling.
/// </remarks>
public sealed class SilkDepthCaptureOptions
{
    /// <summary>Creates capture limits.</summary>
    /// <param name="maximumPixelCount">The largest permitted raster, at most 16,777,216 pixels.</param>
    /// <param name="maximumReadbackBytes">
    /// The managed pixel-array budget, charged conservatively at 20 bytes per pixel:
    /// 8 for RGBA16Float staging, 4 for RGBA8 output, 4 for float32 depth, and 4 for the
    /// managed RGBA8 selection-upload copy. Every capture path uses this uniform
    /// charge, including GPU display transforms or disabled selection.
    /// </param>
    public SilkDepthCaptureOptions(
        int maximumPixelCount = 16_777_216,
        long maximumReadbackBytes = 268_435_456)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumPixelCount);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(maximumPixelCount, 16_777_216);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumReadbackBytes);
        MaximumPixelCount = maximumPixelCount;
        MaximumReadbackBytes = maximumReadbackBytes;
    }

    /// <summary>Gets the default bounded capture options.</summary>
    public static SilkDepthCaptureOptions Default { get; } = new();

    /// <summary>Gets the maximum raster pixel count.</summary>
    public int MaximumPixelCount { get; }

    /// <summary>Gets the maximum managed pixel-array charge in bytes.</summary>
    public long MaximumReadbackBytes { get; }
}

internal sealed class SilkDepthCaptureRequest
{
    private readonly int _width;
    private readonly int _height;
    private readonly int _pixelCount;
    private readonly CancellationToken _cancellationToken;
    private readonly bool _includeDeviceDepth;
    private readonly bool _includeHdrColor;

    internal SilkDepthCaptureRequest(
        int width,
        int height,
        SilkDepthCaptureOptions? options,
        CancellationToken cancellationToken,
        bool includeDeviceDepth = true,
        bool includeHdrColor = false)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(width, 16_384);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(height, 16_384);
        options ??= SilkDepthCaptureOptions.Default;
        long pixelCount = (long)width * height;
        if (pixelCount > options.MaximumPixelCount)
        {
            throw new ArgumentOutOfRangeException(
                nameof(width), "The depth capture exceeds the configured raster pixel quota.");
        }
        if (pixelCount * 20 > options.MaximumReadbackBytes)
        {
            throw new ArgumentOutOfRangeException(
                nameof(width), "The color-and-depth capture exceeds the managed readback byte quota.");
        }
        _width = width;
        _height = height;
        _pixelCount = checked((int)pixelCount);
        _cancellationToken = cancellationToken;
        _includeDeviceDepth = includeDeviceDepth;
        _includeHdrColor = includeHdrColor;
    }

    internal static SilkDepthCaptureRequest ForHdrColor(
        int width,
        int height,
        SilkHdrColorCaptureOptions? options,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!BitConverter.IsLittleEndian)
        {
            throw new PlatformNotSupportedException("HDR capture requires a little-endian host.");
        }
        options ??= SilkHdrColorCaptureOptions.Default;
        return new SilkDepthCaptureRequest(
            width, height, options.Limits, cancellationToken,
            options.IncludeDeviceDepth, includeHdrColor: true);
    }

    internal void ThrowIfCancellationRequested() => _cancellationToken.ThrowIfCancellationRequested();

    internal SilkHdrColorCaptureResult? ReadbackDisplayTransformHdrColor(ISilkGraphicsTexture? completedSceneTarget)
    {
        if (!_includeHdrColor)
        {
            return null;
        }
        ThrowIfCancellationRequested();
        if (completedSceneTarget is null)
        {
            throw new InvalidOperationException(
                "The completed frame did not use an HDR scene target because GPU display-transform " +
                "preparation failed. HDR cannot be captured from the display fallback.");
        }
        if (completedSceneTarget.Format != SilkTextureFormat.Rgba16Float ||
            completedSceneTarget.Width != _width ||
            completedSceneTarget.Height != _height)
        {
            throw new NotSupportedException("HDR capture requires a matching RGBA16Float scene target.");
        }
        byte[] rgba16Float = new byte[checked(_pixelCount * 8)];
        completedSceneTarget.ReadbackForTesting(rgba16Float);
        return RetainHdrColor(rgba16Float);
    }

    internal SilkHdrColorCaptureResult? RetainHdrColor(byte[] rgba16Float)
    {
        if (!_includeHdrColor)
        {
            return null;
        }
        ThrowIfCancellationRequested();
        ReadOnlySpan<Half> channels = MemoryMarshal.Cast<byte, Half>(rgba16Float);
        foreach (Half channel in channels)
        {
            if (!Half.IsFinite(channel))
            {
                throw new InvalidDataException(
                    "The HDR framebuffer contains a non-finite channel, including possible binary16 overflow.");
            }
        }
        ThrowIfCancellationRequested();
        return new SilkHdrColorCaptureResult(_width, _height, rgba16Float);
    }

    internal SilkDepthCaptureResult? Readback(ISilkGraphicsTexture texture)
    {
        ThrowIfCancellationRequested();
        if (!_includeDeviceDepth)
        {
            return null;
        }
        if (texture.Format != SilkTextureFormat.D32Float ||
            texture.Width != _width ||
            texture.Height != _height)
        {
            throw new NotSupportedException(
                "Depth capture requires a matching D32Float visible-depth target.");
        }
        var values = new float[_pixelCount];
        texture.ReadbackForTesting(values);
        ThrowIfCancellationRequested();
        return new SilkDepthCaptureResult(_width, _height, values);
    }
}
