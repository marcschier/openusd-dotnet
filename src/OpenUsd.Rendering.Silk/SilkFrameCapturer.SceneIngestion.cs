// Copyright (c) marcschier. Licensed under the MIT License.

namespace OpenUsd.Rendering.Silk;

public sealed partial class SilkFrameCapturer
{
    /// <summary>Captures HDR and optional depth while synchronizing the exact scene-ingestion choices.</summary>
    /// <remarks>
    /// Uses the same retained renderer as legacy captures. Explicit and legacy calls can alternate;
    /// no pre-sync call or second renderer consumes the session delta. Existing ownership, admission,
    /// completed-submission drainage and current-frame HDR rules apply unchanged.
    /// </remarks>
    public SilkFrameCaptureResult CaptureWithHdrColor(
        OpenUsdSilkSession session, int width, int height, RenderSettings renderSettings,
        SilkSceneIngestionOptions ingestionOptions, SilkHdrColorCaptureOptions? hdrColorOptions = null,
        double timeCode = 0, CameraState camera = default, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(ingestionOptions);
        SilkDepthCaptureRequest request = SilkDepthCaptureRequest.ForHdrColor(
            width, height, hdrColorOptions, cancellationToken);
        SilkFrameCapture.ValidateHdrColorSettings(renderSettings, _device);
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            ValidateDepthCaptureSession(session);
            ObserveCaptureSession(session);
            return SilkFrameCapture.CaptureCore(session, _device, _renderer, width, height,
                renderSettings, timeCode, camera, request, ingestionOptions);
        }
    }

    /// <summary>Captures depth and display color while synchronizing exact scene-ingestion choices.</summary>
    /// <remarks>Legacy captures restore legacy ingestion on the same retained renderer after this operation.</remarks>
    public SilkFrameCaptureResult CaptureWithDepth(
        OpenUsdSilkSession session, int width, int height, RenderSettings renderSettings,
        SilkSceneIngestionOptions ingestionOptions, SilkDepthCaptureOptions? depthOptions = null,
        double timeCode = 0, CameraState camera = default, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(ingestionOptions);
        var request = new SilkDepthCaptureRequest(width, height, depthOptions, cancellationToken);
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            ValidateDepthCaptureSession(session);
            ObserveCaptureSession(session);
            return SilkFrameCapture.CaptureCore(session, _device, _renderer, width, height,
                renderSettings, timeCode, camera, request, ingestionOptions);
        }
    }
}
