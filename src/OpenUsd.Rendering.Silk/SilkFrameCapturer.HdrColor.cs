// Copyright (c) marcschier. Licensed under the MIT License.

namespace OpenUsd.Rendering.Silk;

public sealed partial class SilkFrameCapturer
{
    /// <summary>
    /// Synchronizes and captures pre-exposure, pre-display HDR framebuffer color
    /// together with the existing RGBA8 output, retaining the scene between calls.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This capturer requires exclusive session synchronization ownership. The
    /// continuity guard rejects initially foreign-synchronized or mixed sessions;
    /// it cannot detect another consumer synchronizing the same bound session.
    /// Use retained capture on that consumer's renderer instead. This adds no new
    /// cross-thread or concurrent synchronization guarantee.
    /// </para>
    /// <para>
    /// Limits and pre-cancellation are checked before target allocation or native
    /// Sync. Once Sync consumes a delta, it is applied and GPU work completes before
    /// cancellation is reported. The capture lease includes readbacks, conversion,
    /// selection, and detached result construction on the calling thread.
    /// </para>
    /// <para>
    /// GPU display transforms return HDR only from this completed frame's actual
    /// scene target. Failed preparation refuses HDR after draining submitted work;
    /// no cached previous frame or RGBA8 fallback is exported. Its consumed scene
    /// delta remains retained for a later capture with a valid transform.
    /// </para>
    /// </remarks>
    /// <exception cref="NotSupportedException">The device cannot apply GPU display transforms.</exception>
    /// <exception cref="InvalidOperationException">
    /// The complete session scene is not retained, the requested GPU transform failed
    /// to provide this frame's HDR target, or a non-Identity built-in transform is also requested.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">The raster or capture quotas are exceeded.</exception>
    /// <exception cref="PlatformNotSupportedException">The host is not little-endian.</exception>
    /// <exception cref="InvalidDataException">Any stored HDR channel is non-finite.</exception>
    public SilkFrameCaptureResult CaptureWithHdrColor(
        OpenUsdSilkSession session,
        int width,
        int height,
        RenderSettings renderSettings,
        SilkHdrColorCaptureOptions? hdrColorOptions = null,
        double timeCode = 0,
        CameraState camera = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        SilkDepthCaptureRequest request = SilkDepthCaptureRequest.ForHdrColor(
            width, height, hdrColorOptions, cancellationToken);
        SilkFrameCapture.ValidateHdrColorSettings(renderSettings, _device);
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            ValidateDepthCaptureSession(session);
            ObserveCaptureSession(session);
            return SilkFrameCapture.CaptureCore(
                session, _device, _renderer, width, height, renderSettings, timeCode, camera, request);
        }
    }

    /// <summary>
    /// Synchronizes and captures unchanged pre-exposure HDR with CPU OCIO RGBA8 display output.
    /// </summary>
    /// <remarks>
    /// The processor follows exposure and transforms display output only. The
    /// complete-scene, exclusive synchronization ownership, limits, lease, cancellation,
    /// and detached storage rules are the same as built-in HDR capture.
    /// </remarks>
    /// <exception cref="InvalidOperationException">
    /// A non-Identity built-in output transform is requested or the complete scene is not retained.
    /// </exception>
    /// <exception cref="NotSupportedException">A GPU display transform is also requested.</exception>
    /// <exception cref="InvalidDataException">Any stored HDR channel is non-finite.</exception>
    public SilkFrameCaptureResult CaptureWithHdrColor(
        OpenUsdSilkSession session,
        int width,
        int height,
        RenderSettings renderSettings,
        SilkOpenColorIoProcessor ocioProcessor,
        SilkHdrColorCaptureOptions? hdrColorOptions = null,
        double timeCode = 0,
        CameraState camera = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(ocioProcessor);
        SilkDepthCaptureRequest request = SilkDepthCaptureRequest.ForHdrColor(
            width, height, hdrColorOptions, cancellationToken);
        SilkFrameCapture.ValidateCpuOcioHdrColorSettings(renderSettings);
        SilkFrameCapture.ValidateOcioSettings(renderSettings);
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            ValidateDepthCaptureSession(session);
            ObserveCaptureSession(session);
            return SilkFrameCapture.CaptureCoreOcio(
                session, _device, _renderer, width, height, renderSettings,
                ocioProcessor, timeCode, camera, request);
        }
    }
}
