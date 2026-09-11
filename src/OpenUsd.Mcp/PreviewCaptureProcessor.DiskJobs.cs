// Copyright (c) marcschier. Licensed under the MIT License.

using OpenUsd.Rendering;

namespace OpenUsd.Mcp;

internal interface IRenderDiskCaptureProcessor
{
    RenderDiskJobResult ProcessDiskJob(RenderDiskJobRequest request, CancellationToken cancellationToken);
}

internal interface IPreviewDepthFrameSource
{
    RenderJobImage CaptureWithDepth(
        CaptureView view, int width, int height, long maximumReadbackBytes, CancellationToken cancellationToken);
}

internal interface IPreviewHdrFrameSource
{
    RenderJobImage CaptureWithHdrColor(
        CaptureView view, int width, int height, long maximumReadbackBytes,
        bool includeDepth, CancellationToken cancellationToken);
}

internal interface IPreviewProductFrameSource
{
    void ValidateProduct(RenderProductJobPlan plan, CancellationToken cancellationToken);
    RenderJobImage CaptureProduct(
        RenderProductJobPlan plan, StageRenderState state, long maximumReadbackBytes,
        CancellationToken cancellationToken);
}

public sealed partial class PreviewCaptureProcessor : IRenderDiskCaptureProcessor
{
    public RenderDiskJobResult ProcessDiskJob(
        RenderDiskJobRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ObjectDisposedException.ThrowIf(
            _state is PreviewCaptureProcessorState.DisposePending or PreviewCaptureProcessorState.Disposed, this);
        if (_state == PreviewCaptureProcessorState.ResetPending)
        {
            throw new InvalidOperationException("The previous preview reset did not complete.");
        }
        foreach (StageRenderState state in request.Frames)
        {
            if (state.Viewport.Width > _limits.MaximumWidth || state.Viewport.Height > _limits.MaximumHeight)
            {
                throw new ArgumentOutOfRangeException(nameof(request), "Frame dimensions exceed capture limits.");
            }
            if (request.ProductPlan is null && (state.RenderSettings != RenderSettings.PresentationDefault ||
                state.Display != SceneDisplayState.Default || state.Selection.Items.Count != 0))
            {
                throw new NotSupportedException(
                    "The MCP preview adapter supports its explicit presentation settings without selection overlays.");
            }
        }
        cancellationToken.ThrowIfCancellationRequested();
        StageRenderState first = request.Frames[0];
        var preview = new PreviewCaptureRequest("disk-job", first.Viewport.Width, first.Viewport.Height,
            first.Camera, first.Time.TimeCode);
        IPreviewFrameSource frameSource = _frameSource
            ??= _frameSourceFactory.Create(preview, cancellationToken)
                ?? throw new InvalidOperationException("The preview frame source factory returned null.");
        if (request.ProductPlan is null && request.IncludeDeviceDepth && !request.IncludeHdrColor &&
            frameSource is not IPreviewDepthFrameSource)
        {
            throw new NotSupportedException("The configured preview source cannot capture device depth.");
        }
        if (request.ProductPlan is null && request.IncludeHdrColor && frameSource is not IPreviewHdrFrameSource)
        {
            throw new NotSupportedException("The configured preview source cannot capture pre-display HDR color.");
        }
        return RenderDiskJob.Execute(request,
            new DiskFrameSource(
                frameSource, request.IncludeDeviceDepth, request.IncludeHdrColor,
                request.Limits.MaximumFrameBytes, request.ProductPlan),
            cancellationToken);
    }

    private sealed class DiskFrameSource(
        IPreviewFrameSource source, bool includeDepth, bool includeHdrColor,
        long maximumReadbackBytes, RenderProductJobPlan? productPlan) : IRenderProductFrameSource
    {
        public void ValidateProduct(RenderProductJobPlan plan, CancellationToken cancellationToken)
        {
            if (!ReferenceEquals(plan, productPlan) || source is not IPreviewProductFrameSource productSource)
            {
                throw new NotSupportedException(
                    "The configured capture source cannot honor authored-product scene filters.");
            }
            productSource.ValidateProduct(plan, cancellationToken);
        }

        public RenderJobImage Render(StageRenderState state, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (productPlan is not null)
            {
                return ((IPreviewProductFrameSource)source).CaptureProduct(
                    productPlan, state, maximumReadbackBytes, cancellationToken);
            }
            var view = new CaptureView("sequence", state.Camera, state.Time.TimeCode);
            if (includeHdrColor)
            {
                return ((IPreviewHdrFrameSource)source).CaptureWithHdrColor(view,
                    state.Viewport.Width, state.Viewport.Height, maximumReadbackBytes, includeDepth, cancellationToken);
            }
            if (includeDepth)
            {
                return ((IPreviewDepthFrameSource)source).CaptureWithDepth(
                    view, state.Viewport.Width, state.Viewport.Height, maximumReadbackBytes, cancellationToken);
            }
            ImageRgba8 image = source.Capture(view,
                state.Viewport.Width, state.Viewport.Height);
            cancellationToken.ThrowIfCancellationRequested();
            return new RenderJobImage(image.Width, image.Height, image.Pixels, Rgba8RowOrder.TopDown)
            {
                Diagnostics = source is IPreviewDiagnosticSource diagnostics
                    ? diagnostics.Diagnostics : RenderDiagnosticsState.Empty
            };
        }
    }
}
