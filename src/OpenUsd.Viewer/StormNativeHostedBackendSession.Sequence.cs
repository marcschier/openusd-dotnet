// Copyright (c) marcschier. Licensed under the MIT License.

using OpenUsd.Rendering;
using OpenUsd.Rendering.Storm;

namespace OpenUsd.Viewer;

internal sealed partial class StormNativeHostedBackendSession
{
    private const string SequenceProfile =
        "native Storm viewport appearance; no hdSilk exposure, tone-map or OCIO conversion";

    private static readonly RenderDiagnosticsState SequenceDiagnostics = new(
    [
        new RenderDiagnostic(RenderDiagnosticSeverity.Information, "VIEWER_STORM_NATIVE_VIEWPORT_SEQUENCE",
            "PNG captures the completed native Storm viewport with its native lighting, material and " +
            "background profile. The shared state retains the Viewer's requested presentation settings; " +
            "hdSilk exposure/tone mapping and OCIO are not applied to this native framebuffer.")
    ]);

    public string? RenderSequenceProfileDescription => SequenceProfile +
        (SupportsAovCapture
            ? "; optional raw HDR/depth: at most 4096 per side and 1,048,576 pixels"
            : "; PNG only; Metal Storm AOV capture is not implemented");

    private static bool SupportsAovCapture => OperatingSystem.IsWindows() || OperatingSystem.IsLinux();

    public string? GetRenderSequenceUnsupportedReason(
        StageRenderState state, ViewerRenderSequenceOutputOptions outputs) =>
        !SupportsFrameCapture
            ? "The native Storm viewport is no longer available."
            : GetSequenceProfileUnsupportedReason(state, outputs, SupportsAovCapture);

    internal static string? GetSequenceProfileUnsupportedReason(
        StageRenderState state, ViewerRenderSequenceOutputOptions outputs, bool supportsAovCapture = false)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (!supportsAovCapture &&
            (outputs.HasAdditionalPlanes || outputs.HdrColorFormat != RenderHdrColorFormat.RawRgba16Float))
        {
            return "This native Storm platform supplies PNG only; its Metal AOV capture route is not implemented.";
        }
        if (outputs.IncludeHdrColor && state.Selection.Items.Count != 0)
        {
            return "Clear display selection before exporting native Storm HDR; " +
                "highlights can be baked into its color AOV.";
        }
        if (outputs.HasAdditionalPlanes &&
            (state.Viewport.Width > StormAovLimits.MaximumDimension ||
             state.Viewport.Height > StormAovLimits.MaximumDimension ||
             (long)state.Viewport.Width * state.Viewport.Height > StormAovLimits.MaximumPixels))
        {
            return "Native Storm HDR/depth capture is limited to 4096 pixels per side and " +
                "1,048,576 total pixels. Reduce the viewport or turn off the optional planes.";
        }
        if (state.RenderSettings.DisplayTransform is not null)
        {
            return "Native Storm viewport sequences cannot apply the requested OCIO display transform.";
        }
        if (state.RenderSettings != RenderSettings.PresentationDefault)
        {
            return "Native Storm viewport sequences use the default native appearance, not custom " +
                "hdSilk exposure, tone-map or quality overrides. Reset those controls before capturing.";
        }
        if (state.Display.Purposes != SceneDisplayState.Default.Purposes ||
            state.Display.Visibility != RenderVisibility.RespectAuthored ||
            state.Display.DrawMode != RenderDrawMode.SmoothShaded)
        {
            return "Native Storm viewport sequences require Default, Proxy and Render purposes, " +
                "authored visibility and smooth shading.";
        }
        return outputs.GetAdmissionUnsupportedReason(state.Viewport);
    }

    public async ValueTask<ViewerFrameCaptureResult> CaptureRenderSequenceFrameAsync(
        StageRenderState state, ViewerRenderSequenceOutputOptions outputs, CancellationToken cancellationToken)
    {
        if (GetRenderSequenceUnsupportedReason(state, outputs) is { } unsupported)
        {
            throw new NotSupportedException(unsupported);
        }
        OpenUsdStormChildDiagnostics rendered = await RenderSequenceNativeFrameAsync(state, cancellationToken);
        if (outputs.HasAdditionalPlanes)
        {
            return await CaptureRenderSequenceAovsAsync(state, outputs, rendered, cancellationToken);
        }
        OpenUsdStormFramebufferCapture capture = await control.CaptureFramebufferAsync(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        OpenUsdStormChildDiagnostics current = control.GetDiagnostics();
        if (capture.Width != state.Viewport.Width || capture.Height != state.Viewport.Height ||
            capture.FrameCount < rendered.FrameCount || current.LatestRequestedRevision != state.Revision ||
            current.LatestRenderedCameraSignature != rendered.LatestRenderedCameraSignature)
        {
            throw new InvalidOperationException(
                "The native Storm framebuffer no longer matches the requested sequence frame.");
        }
        return new ViewerFrameCaptureResult(
            capture.Width, capture.Height, capture.RgbaPixels, ViewerFrameRowOrder.BottomUp, SequenceDiagnostics);
    }

    private async Task<ViewerFrameCaptureResult> CaptureRenderSequenceAovsAsync(
        StageRenderState state,
        ViewerRenderSequenceOutputOptions outputs,
        OpenUsdStormChildDiagnostics rendered,
        CancellationToken cancellationToken)
    {
        StormAovKind[] kinds = outputs.IncludeHdrColor
            ? outputs.IncludeDeviceDepth ? [StormAovKind.Color, StormAovKind.Depth] : [StormAovKind.Color]
            : [StormAovKind.Depth];
        var request = new StormAovRequest(
            state.Viewport.Width, state.Viewport.Height, 0, kinds, state.Camera,
            state.Time.TimeCode, state.Revision,
            limits: new StormAovLimits(managedByteLimit: (ulong)RenderDiskJobLimits.Default.MaximumFrameBytes));
        OpenUsdStormChildAovCapture capture = await control.RenderAovsAsync(request, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        if (capture.Aovs.TimeCode != state.Time.TimeCode ||
            capture.Aovs.CallerStateRevision != state.Revision ||
            capture.Framebuffer.FrameCount < rendered.FrameCount)
        {
            throw new InvalidOperationException(
                "The native Storm AOV capture no longer matches the requested sequence frame.");
        }
        RenderJobImage image = await Task.Run(() => capture.CreateJobImage(
            outputs.IncludeDeviceDepth, outputs.IncludeHdrColor, cancellationToken: cancellationToken),
            cancellationToken);
        return new ViewerFrameCaptureResult(
            image.Width, image.Height, image.Rgba, ViewerFrameRowOrder.BottomUp,
            new RenderDiagnosticsState([.. SequenceDiagnostics.Entries, .. image.Diagnostics.Entries]))
        {
            DeviceDepth = image.DeviceDepth,
            HdrColor = image.HdrColor
        };
    }

    public async ValueTask RestoreRenderSequenceFrameAsync(
        StageRenderState state, CancellationToken cancellationToken) =>
        _ = await RenderSequenceNativeFrameAsync(state, cancellationToken);

    private async Task<OpenUsdStormChildDiagnostics> RenderSequenceNativeFrameAsync(
        StageRenderState state, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        try
        {
            await control.ResizeAsync(state.Viewport, timeout.Token);
            while (true)
            {
                OpenUsdStormChildDiagnostics frame = await control.RenderFrameAsync(state, timeout.Token);
                timeout.Token.ThrowIfCancellationRequested();
                if (frame.Converged && frame.LatestRequestedRevision == state.Revision &&
                    frame.LatestRequestedCameraSignature == frame.LatestRenderedCameraSignature)
                {
                    return frame;
                }
                await Task.Delay(16, timeout.Token);
            }
        }
        catch (OperationCanceledException) when (
            !cancellationToken.IsCancellationRequested && timeout.IsCancellationRequested)
        {
            throw new TimeoutException(
                "The native Storm renderer did not converge on the requested sequence frame within 30 seconds.");
        }
    }
}
