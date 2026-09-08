// Copyright (c) marcschier. Licensed under the MIT License.

namespace OpenUsd.Rendering.Silk;

public static partial class SilkFrameCapture
{
    /// <summary>
    /// Captures RGBA8 color and the actual normalized device-depth attachment from the
    /// built-in renderer's retained scene, without synchronizing a session.
    /// </summary>
    /// <remarks>
    /// Uses the camera and geometry already retained by the renderer, on the calling
    /// thread and under the same capture lease as color-only capture. Limits and
    /// pre-cancellation are checked before allocating targets. Submitted GPU work is
    /// allowed to complete before cancellation is reported; no in-flight target is
    /// disposed early. Existing color-only methods do not allocate depth output.
    /// </remarks>
    /// <exception cref="NotSupportedException">
    /// The renderer is not a <see cref="SilkMeshRenderer"/> or its target cannot provide
    /// D32Float readback. No depth is synthesized for a custom renderer.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// The dimensions or capture quotas are exceeded.
    /// </exception>
    public static SilkFrameCaptureResult CaptureRetainedWithDepth(
        ISilkRenderTargetRenderer renderer,
        ISilkGraphicsDevice device,
        int width,
        int height,
        RenderSettings renderSettings,
        SilkDepthCaptureOptions? depthOptions = null,
        ulong pageRevision = 0,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(renderer);
        ArgumentNullException.ThrowIfNull(device);
        var request = new SilkDepthCaptureRequest(width, height, depthOptions, cancellationToken);
        if (renderer is not SilkMeshRenderer)
        {
            throw new NotSupportedException(
                "Depth capture is only supported with the built-in SilkMeshRenderer.");
        }
        return CaptureRetainedCore(
            renderer, device, width, height, renderSettings, pageRevision, request);
    }

    /// <summary>
    /// Captures RGBA8 color using an OCIO CPU processor together with the retained
    /// renderer's unchanged normalized device-depth attachment.
    /// </summary>
    /// <remarks>
    /// Depth is read before color conversion and selection compositing. Quotas,
    /// cancellation, ownership, and row order are the same as for built-in color output.
    /// </remarks>
    /// <exception cref="InvalidOperationException">
    /// A built-in output transform or GPU display transform is also requested.
    /// </exception>
    public static SilkFrameCaptureResult CaptureRetainedWithDepth(
        ISilkRenderTargetRenderer renderer,
        ISilkGraphicsDevice device,
        int width,
        int height,
        RenderSettings renderSettings,
        SilkOpenColorIoProcessor ocioProcessor,
        SilkDepthCaptureOptions? depthOptions = null,
        ulong pageRevision = 0,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(renderer);
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(ocioProcessor);
        var request = new SilkDepthCaptureRequest(width, height, depthOptions, cancellationToken);
        return CaptureRetainedCoreOcio(
            renderer, device, width, height, renderSettings, ocioProcessor, pageRevision, request);
    }
}
