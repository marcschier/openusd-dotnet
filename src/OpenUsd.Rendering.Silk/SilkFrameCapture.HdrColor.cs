// Copyright (c) marcschier. Licensed under the MIT License.

namespace OpenUsd.Rendering.Silk;

public static partial class SilkFrameCapture
{
    /// <summary>
    /// Captures renderer-working composited HDR before exposure/display together
    /// with the existing RGBA8 display output, without synchronizing a session.
    /// </summary>
    /// <remarks>
    /// The built-in renderer supports CPU conversion and GPU display transforms.
    /// GPU HDR is read from this completed frame's actual scene target, never from
    /// a cached previous frame or the RGBA8 fallback. A failed transform refuses HDR
    /// after draining submitted work; existing non-HDR capture fallback is unchanged.
    /// Limits and pre-cancellation are checked before target allocation. The same
    /// capture lease covers rendering, completed submission, readback, conversion,
    /// selection, and detached result construction on the calling thread.
    /// </remarks>
    /// <exception cref="NotSupportedException">
    /// The renderer is custom or the device cannot apply GPU display transforms.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// The requested GPU display transform failed to provide this frame's HDR target,
    /// or a non-Identity built-in output transform is also requested.
    /// </exception>
    /// <exception cref="InvalidDataException">Any stored HDR channel is non-finite.</exception>
    /// <exception cref="PlatformNotSupportedException">The host is not little-endian.</exception>
    public static SilkFrameCaptureResult CaptureRetainedWithHdrColor(
        ISilkRenderTargetRenderer renderer,
        ISilkGraphicsDevice device,
        int width,
        int height,
        RenderSettings renderSettings,
        SilkHdrColorCaptureOptions? hdrColorOptions = null,
        ulong pageRevision = 0,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(renderer);
        ArgumentNullException.ThrowIfNull(device);
        SilkDepthCaptureRequest request = SilkDepthCaptureRequest.ForHdrColor(
            width, height, hdrColorOptions, cancellationToken);
        ValidateHdrColorSettings(renderSettings, device);
        if (renderer is not SilkMeshRenderer)
        {
            throw new NotSupportedException(
                "HDR capture requires the built-in SilkMeshRenderer.");
        }
        return CaptureRetainedCore(
            renderer, device, width, height, renderSettings, pageRevision, request);
    }

    /// <summary>
    /// Captures unchanged retained HDR framebuffer color with CPU OCIO RGBA8 display output.
    /// </summary>
    /// <remarks>
    /// The processor follows exposure and transforms display output only. Optional
    /// D32 depth and HDR are read before display selection compositing. Limits,
    /// cancellation, calling-thread lease, and detached storage rules match built-in HDR capture.
    /// </remarks>
    /// <exception cref="NotSupportedException">
    /// The renderer is custom or a GPU display transform is also requested.
    /// </exception>
    /// <exception cref="InvalidOperationException">A non-Identity built-in output transform is requested.</exception>
    /// <exception cref="InvalidDataException">Any stored HDR channel is non-finite.</exception>
    public static SilkFrameCaptureResult CaptureRetainedWithHdrColor(
        ISilkRenderTargetRenderer renderer,
        ISilkGraphicsDevice device,
        int width,
        int height,
        RenderSettings renderSettings,
        SilkOpenColorIoProcessor ocioProcessor,
        SilkHdrColorCaptureOptions? hdrColorOptions = null,
        ulong pageRevision = 0,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(renderer);
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(ocioProcessor);
        SilkDepthCaptureRequest request = SilkDepthCaptureRequest.ForHdrColor(
            width, height, hdrColorOptions, cancellationToken);
        ValidateCpuOcioHdrColorSettings(renderSettings);
        return CaptureRetainedCoreOcio(
            renderer, device, width, height, renderSettings, ocioProcessor, pageRevision, request);
    }

    internal static void ValidateHdrColorSettings(RenderSettings renderSettings, ISilkGraphicsDevice device)
    {
        renderSettings.ValidateDisplayTransform();
        if (renderSettings.DisplayTransform is not null)
        {
            if (device is not ISilkDisplayTransformGraphicsDevice)
            {
                throw new NotSupportedException(
                    "The graphics device cannot apply GPU display transforms for HDR capture.");
            }
            SilkMeshRenderer.ValidateOptions(CreateDisplayTransformRenderOptions(renderSettings));
        }
    }

    internal static void ValidateCpuOcioHdrColorSettings(RenderSettings renderSettings)
    {
        if (renderSettings.DisplayTransform is not null)
        {
            throw new NotSupportedException(
                "A GPU display transform cannot be combined with a CPU OCIO HDR capture processor.");
        }
    }
}
