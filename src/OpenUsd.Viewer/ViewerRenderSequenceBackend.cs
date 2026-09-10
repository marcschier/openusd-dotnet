// Copyright (c) marcschier. Licensed under the MIT License.

using Avalonia.Threading;
using OpenUsd.Rendering;
using OpenUsd.Rendering.Silk;

namespace OpenUsd.Viewer;

internal sealed partial class CompositionHostedBackendSession
{
    public string? GetRenderSequenceUnsupportedReason(
        StageRenderState state, ViewerRenderSequenceOutputOptions outputs)
    {
        if (!SupportsFrameCapture)
        {
            return "The active composition device does not expose RGBA readback. " +
                "Wait for a completed frame from a supported hdSilk renderer.";
        }
        if (state.Display.Purposes != SceneDisplayState.Default.Purposes ||
            state.Display.Visibility != RenderVisibility.RespectAuthored)
        {
            return "This Viewer sync binding cannot apply custom purpose masks or ignore authored visibility. " +
                "Use Default, Proxy and Render purposes with Guide off before rendering a sequence.";
        }
        if (state.RenderSettings.SamplesPerPixel != 1)
        {
            return "This sequence capture device supports exactly one sample per pixel.";
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
        await RenderSequenceStateAsync(state, cancellationToken);
        using IDisposable ownership = await control.AcquireCaptureAsync(cancellationToken);
        ViewerFrameCaptureResult capture = await Dispatcher.UIThread.InvokeAsync(
            () => outputs.HasAdditionalPlanes
                ? CaptureRenderSequencePlanes(state, outputs, cancellationToken)
                : CaptureFrameCore(state.Viewport.Width, state.Viewport.Height, cancellationToken),
            DispatcherPriority.Normal, cancellationToken);
        if (state.RenderSettings.DisplayTransform is { } transform &&
            (DisplayTransformDiagnostics is not { Status: SilkDisplayTransformStatus.Applied } applied ||
                applied.RequestKey != transform.CacheKey))
        {
            throw new NotSupportedException(
                "The requested display transform did not run: " +
                (DisplayTransformDiagnostic?.Message ?? "no matching pass"));
        }
        return capture;
    }

    private ViewerFrameCaptureResult CaptureRenderSequencePlanes(
        StageRenderState state, ViewerRenderSequenceOutputOptions outputs, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (resources.CaptureDevice is not { } device ||
            resources.Renderer.RetainedRenderer is not { } retained || resources.Renderer.LastSceneRevision == 0)
        {
            throw new InvalidOperationException("The renderer has no retained sequence frame to capture.");
        }
        long maximumBytes = RenderDiskJobLimits.Default.MaximumFrameBytes;
        SilkFrameCaptureResult capture = outputs.IncludeHdrColor
            ? SilkFrameCapture.CaptureRetainedWithHdrColor(
                retained, device, state.Viewport.Width, state.Viewport.Height, state.RenderSettings,
                new SilkHdrColorCaptureOptions(outputs.IncludeDeviceDepth, maximumReadbackBytes: maximumBytes),
                resources.Renderer.LastSceneRevision, cancellationToken)
            : SilkFrameCapture.CaptureRetainedWithDepth(
                retained, device, state.Viewport.Width, state.Viewport.Height, state.RenderSettings,
                new SilkDepthCaptureOptions(maximumReadbackBytes: maximumBytes),
                resources.Renderer.LastSceneRevision, cancellationToken);
        return new ViewerFrameCaptureResult(
            capture.Width, capture.Height, capture.Rgba, ViewerFrameRowOrder.TopDown, capture.Diagnostics)
        {
            DeviceDepth = capture.Depth is { } depth
                ? new RenderJobDeviceDepth(depth.Width, depth.Height, depth.Values) : null,
            HdrColor = capture.HdrColor is { } hdr
                ? new RenderJobHdrColor(hdr.Width, hdr.Height, hdr.Rgba16Float) : null
        };
    }

    public ValueTask RestoreRenderSequenceFrameAsync(StageRenderState state, CancellationToken cancellationToken) =>
        RenderSequenceStateAsync(state, cancellationToken);

    private async ValueTask RenderSequenceStateAsync(StageRenderState state, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        try
        {
            await control.WaitForPresentationIdleAsync();
            while (true)
            {
                timeout.Token.ThrowIfCancellationRequested();
                RenderFrameResult frame = await RenderAsync(timeout.Token);
                if (frame.Status == RenderFrameStatus.Rendered)
                {
                    StageRenderState? rendered = resources.Renderer.LastRenderedPickState?.State;
                    if (frame.StateRevision != state.Revision || rendered != state)
                    {
                        throw new InvalidOperationException(
                            $"The renderer did not produce the exact requested sequence state " +
                            $"(requested revision {state.Revision}, time {state.Time.TimeCode}; " +
                            $"frame revision {frame.StateRevision}, retained revision {rendered?.Revision}, " +
                            $"time {rendered?.Time.TimeCode}).");
                    }
                    return;
                }
                if (frame.Status != RenderFrameStatus.Skipped)
                {
                    throw new InvalidOperationException($"The sequence renderer failed: {frame.Status}.");
                }
                await Task.Delay(16, timeout.Token);
            }
        }
        catch (OperationCanceledException) when (
            !cancellationToken.IsCancellationRequested && timeout.IsCancellationRequested)
        {
            throw new TimeoutException("The sequence renderer did not produce a frame within 30 seconds.");
        }
        finally
        {
            // Cancelling the caller's presentation wait does not cancel an in-flight graphics
            // command. Do not let restoration or teardown race that command.
            await control.WaitForPresentationIdleAsync();
        }
    }
}
