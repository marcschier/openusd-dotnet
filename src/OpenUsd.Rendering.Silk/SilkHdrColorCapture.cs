// Copyright (c) marcschier. Licensed under the MIT License.

namespace OpenUsd.Rendering.Silk;

/// <summary>Identifies the numerical meaning of captured HDR framebuffer color.</summary>
public enum SilkHdrColorConvention
{
    /// <summary>Renderer-working composited framebuffer color before exposure and display conversion.</summary>
    RendererWorkingCompositedBeforeExposureAndDisplay
}

/// <summary>Managed-owned RGBA binary16 samples from the rendered HDR attachment.</summary>
/// <remarks>
/// <para>
/// These are renderer-working composited framebuffer samples before exposure,
/// built-in output transforms, CPU OCIO conversion, GPU display transforms, and display selection outlines.
/// They are not albedo, a physical radiance guarantee, or a guaranteed named
/// primaries/transfer-function encoding. An OCIO source color space is a consumer
/// assumption, not measured source primaries.
/// </para>
/// <para>
/// Alpha is the stored framebuffer alpha after blending. The image is not
/// universally straight/unassociated alpha, and no unpremultiply is performed.
/// </para>
/// <para>
/// All four stored channels are checked for finiteness without clamping. Finite
/// negative values and signed zero are preserved. Binary16 has a finite maximum
/// magnitude of 65504; this is not float32 precision or a guarantee of GPU subnormal
/// preservation. The check cannot recover overflow already saturated by the renderer
/// or graphics backend before storage.
/// </para>
/// <para>
/// The existing half readback array is transferred without another pixel copy.
/// Storage is detached, not pooled, retains no GPU handle, and remains valid after
/// subsequent captures and renderer, session, or graphics-device disposal.
/// </para>
/// </remarks>
public sealed class SilkHdrColorCaptureResult
{
    internal SilkHdrColorCaptureResult(int width, int height, byte[] rgba16Float)
    {
        Width = width;
        Height = height;
        Rgba16Float = rgba16Float;
    }

    /// <summary>Gets the width in pixels, equal to the accompanying RGBA8 capture.</summary>
    public int Width { get; }

    /// <summary>Gets the height in pixels, equal to the accompanying RGBA8 capture.</summary>
    public int Height { get; }

    /// <summary>
    /// Gets exactly Width * Height * 8 tightly packed, top-down, left-to-right RGBA
    /// binary16 bytes in little-endian order, without padding. Pixel (x, y) begins
    /// at byte offset (y * Width + x) * 8. No expanded float or copied half array is retained.
    /// </summary>
    public ReadOnlyMemory<byte> Rgba16Float { get; }

    /// <summary>Gets the pre-exposure, pre-display composited framebuffer convention.</summary>
    public SilkHdrColorConvention Convention { get; } =
        SilkHdrColorConvention.RendererWorkingCompositedBeforeExposureAndDisplay;
}

/// <summary>Bounds an opt-in HDR and RGBA8 capture, with optional device depth.</summary>
/// <remarks>
/// Limits are checked before target allocation or session synchronization. The hard
/// raster bounds are 16,384 pixels per dimension and 16,777,216 pixels in total.
/// The default 256 MiB managed pixel-array budget rejects a 4096-square capture,
/// which requires 320 MiB. GPU/native staging, native OCIO scratch, allocation
/// alignment, display-transform lattice baking/caches, and existing scene, texture, shadow,
/// and selection infrastructure are not charged to this budget.
/// </remarks>
public sealed class SilkHdrColorCaptureOptions
{
    /// <summary>Creates bounded HDR capture options.</summary>
    /// <param name="includeDeviceDepth">Whether to return the actual normalized D32 depth plane.</param>
    /// <param name="maximumPixelCount">The largest raster, at most 16,777,216 pixels.</param>
    /// <param name="maximumReadbackBytes">
    /// The managed pixel-array budget, charged uniformly at 20 bytes per pixel:
    /// 8 for HDR, 4 for RGBA8, 4 for optional depth, and 4 for the managed RGBA8
    /// selection-upload copy. The charge is unchanged when depth or selection is disabled.
    /// </param>
    public SilkHdrColorCaptureOptions(
        bool includeDeviceDepth = false,
        int maximumPixelCount = 16_777_216,
        long maximumReadbackBytes = 268_435_456)
    {
        Limits = new SilkDepthCaptureOptions(maximumPixelCount, maximumReadbackBytes);
        IncludeDeviceDepth = includeDeviceDepth;
    }

    /// <summary>Gets the default bounded options, without device-depth output.</summary>
    public static SilkHdrColorCaptureOptions Default { get; } = new();

    /// <summary>Gets whether the actual normalized D32 depth plane is returned.</summary>
    public bool IncludeDeviceDepth { get; }

    /// <summary>Gets the maximum raster pixel count.</summary>
    public int MaximumPixelCount => Limits.MaximumPixelCount;

    /// <summary>Gets the maximum managed pixel-array charge in bytes.</summary>
    public long MaximumReadbackBytes => Limits.MaximumReadbackBytes;

    internal SilkDepthCaptureOptions Limits { get; }
}
