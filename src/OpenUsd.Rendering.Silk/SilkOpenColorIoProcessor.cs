// Copyright (c) marcschier. Licensed under the MIT License.

using OpenUsd.Interop;

namespace OpenUsd.Rendering.Silk;

/// <summary>
/// An immutable, reusable OpenColorIO CPU processor that converts RGBA16Float capture
/// data to display-referred RGBA8 through a display/view pipeline.
/// </summary>
/// <remarks>
/// <para>
/// The processor is created from a <see cref="SilkOpenColorIoDisplayTransform"/> and
/// holds a native OCIO CPU processor behind a <see cref="System.Runtime.InteropServices.SafeHandle"/>.
/// It is thread-safe for concurrent <see cref="Apply"/> calls (the native
/// processor is immutable; each call allocates its own scratch buffer).
/// </para>
/// <para>
/// CPU capture/export OCIO is supported. Live GPU presentation OCIO is deferred.
/// </para>
/// </remarks>
public sealed class SilkOpenColorIoProcessor : IDisposable
{
    private readonly OpenUsdNativeOcioProcessor _native;

    /// <summary>
    /// Creates a processor from the given display transform.
    /// </summary>
    /// <param name="transform">The OCIO display/view transform descriptor.</param>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="transform"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="OpenUsdNativeException">
    /// The OCIO config could not be loaded or the display/view/color-space names are invalid.
    /// </exception>
    public SilkOpenColorIoProcessor(SilkOpenColorIoDisplayTransform transform)
    {
        ArgumentNullException.ThrowIfNull(transform);
        Transform = transform;

        // Built with OpenColorIO's process-global caches cleared first, under the shared
        // interlock. Those caches hold parsed configs, loaded file transforms, and
        // processors; without clearing them a config whose FileTransform points at a LUT
        // that changed on disk keeps producing the previous colour indefinitely, because
        // the file is re-referenced but never re-read. Clearing is process-global, so it
        // has to exclude every other processor construction, which is what the interlock
        // provides.
        _native = SilkOpenColorIoInterlock.Refresh(() =>
            OpenUsdNativeRuntime.CreateOcioProcessor(
                transform.ConfigPath,
                transform.SourceColorSpace,
                transform.Display,
                transform.View,
                transform.Looks));
    }

    /// <summary>Gets the transform this processor was created from.</summary>
    public SilkOpenColorIoDisplayTransform Transform { get; }

    /// <summary>
    /// Converts tightly packed RGBA16Float pixels to display-referred RGBA8, applying
    /// exposure (in stops) to RGB channels before the OCIO display/view transform.
    /// Alpha is preserved by the OCIO pipeline.
    /// </summary>
    /// <param name="sourceRgba16F">
    /// Tightly packed RGBA16Float pixels (8 bytes per pixel, top-down row order).
    /// </param>
    /// <param name="destinationRgba8">
    /// Output buffer for tightly packed RGBA8 pixels (4 bytes per pixel).
    /// </param>
    /// <param name="width">Image width in pixels.</param>
    /// <param name="height">Image height in pixels.</param>
    /// <param name="exposure">Exposure adjustment in stops (applied before OCIO).</param>
    /// <exception cref="ObjectDisposedException">The processor has been disposed.</exception>
    /// <exception cref="OpenUsdNativeException">
    /// A buffer size does not match the image dimensions, source data is non-finite,
    /// exposure overflows, or OCIO processing fails.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// An image dimension is not positive or <paramref name="exposure"/> is not finite.
    /// </exception>
    public void Apply(
        ReadOnlySpan<byte> sourceRgba16F,
        Span<byte> destinationRgba8,
        int width,
        int height,
        float exposure)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        if (!float.IsFinite(exposure))
        {
            throw new ArgumentOutOfRangeException(nameof(exposure), "Exposure must be finite.");
        }

        OpenUsdNativeRuntime.ApplyOcioProcessorRgba16FToRgba8(
            _native,
            sourceRgba16F,
            destinationRgba8,
            checked((uint)width),
            checked((uint)height),
            exposure);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        _native.Dispose();
    }

    /// <summary>
    /// Converts tightly packed RGBA32Float samples to display-referred RGBA8, applying
    /// exposure (in stops) to RGB before the OCIO display/view transform.
    /// </summary>
    /// <remarks>
    /// This exists for generated data such as a display-transform lattice, whose sample
    /// values are chosen rather than captured and can legitimately sit outside the
    /// half-float normal range. Capture data is genuinely half-float and keeps
    /// <see cref="Apply"/>, unchanged.
    /// </remarks>
    internal void ApplyLinearFloat(
        ReadOnlySpan<byte> sourceRgba32F,
        Span<byte> destinationRgba8,
        int width,
        int height,
        float exposure)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        if (!float.IsFinite(exposure))
        {
            throw new ArgumentOutOfRangeException(nameof(exposure), "Exposure must be finite.");
        }

        OpenUsdNativeRuntime.ApplyOcioProcessorRgba32FToRgba8(
            _native,
            sourceRgba32F,
            destinationRgba8,
            checked((uint)width),
            checked((uint)height),
            exposure);
    }
}
