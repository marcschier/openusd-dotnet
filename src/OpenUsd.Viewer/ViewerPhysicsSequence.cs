// Copyright (c) marcschier. Licensed under the MIT License.

using Avalonia.Threading;
using OpenUsd.Rendering;
using OpenUsd.Rendering.Physics;

namespace OpenUsd.Viewer;

internal sealed record ViewerPhysicsSequenceRequest(
    int FrameCount,
    int StepsPerFrame,
    string ScenarioIdentity,
    string OutputParent,
    ViewerRenderSequenceOutputOptions Outputs)
{
    internal void Validate()
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(FrameCount, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(FrameCount, RenderDiskJobLimits.Default.MaximumFrames);
        ArgumentOutOfRangeException.ThrowIfLessThan(StepsPerFrame, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(StepsPerFrame, 8);
        _ = new PhysicsRenderRecordingContext(ScenarioIdentity, 0);
        ArgumentException.ThrowIfNullOrWhiteSpace(OutputParent);
    }
}

internal sealed class ViewerPhysicsSequenceFrameSource(
        ViewerRenderBackend backend,
        Func<StageRenderState, CancellationToken, ValueTask<ViewerFrameCaptureResult>> capture,
        Func<ValueTask> restore,
        Func<CancellationToken, ValueTask> validate,
        Action<ViewerRenderSequenceProgress> progress,
        int totalFrames) : IPhysicsRenderRecordingSource
{
    private int _completed;

    public void Validate(
        RenderDiskJobRequest request, PhysicsRenderRecordingContext context, CancellationToken cancellationToken)
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            throw new InvalidOperationException("Physics stepping and disk encoding must not run on the UI thread.");
        }
        if (request.ProductPlan is not null || context.ReviewIdentity is not null)
        {
            throw new NotSupportedException(
                "Viewer physics recording captures the viewport without a review-origin claim.");
        }
        validate(cancellationToken).AsTask().GetAwaiter().GetResult();
    }

    public PhysicsRenderCaptureResult Capture(
        StageRenderState state, PhysicsRenderRecordingFrame frame, CancellationToken cancellationToken)
    {
        if (frame.Deformations.Count != 0)
        {
            throw new NotSupportedException(
                "Viewer recording currently supports rigid-pose publications, not deformable geometry.");
        }
        Task<PhysicsRenderCaptureResult> result = Dispatcher.UIThread.InvokeAsync(async () =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            progress(new ViewerRenderSequenceProgress("Rendering", _completed, totalFrames, state.Time.TimeCode));
            if (backend.ApplyPhysicsOverrides(frame.Transforms, frame.Bindings) != frame.Transforms.Count)
            {
                throw new InvalidOperationException("The complete physics pose batch could not be staged.");
            }
            _ = backend.ApplyPhysicsDeformations(PhysicsRenderDeformationView.Empty, frame.Bindings);
            ViewerFrameCaptureResult image = await capture(state, cancellationToken);
            if (!backend.TryTakeOverrideReport(out ViewerPhysicsOverrideReport report) || report.Unresolved != 0)
            {
                throw new InvalidOperationException("The renderer did not acknowledge a complete physics pose batch.");
            }
            _completed++;
            progress(new ViewerRenderSequenceProgress("Encoding", _completed, totalFrames, state.Time.TimeCode));
            var output = new RenderJobImage(image.Width, image.Height, image.Rgba, image.RowOrder switch
            {
                ViewerFrameRowOrder.TopDown => Rgba8RowOrder.TopDown,
                ViewerFrameRowOrder.BottomUp => Rgba8RowOrder.BottomUp,
                _ => throw new InvalidDataException("The capture has an unknown RGBA row order.")
            })
            {
                Diagnostics = image.Diagnostics,
                DeviceDepth = image.DeviceDepth,
                HdrColor = image.HdrColor
            };
            return new PhysicsRenderCaptureResult(output, report.Revision, report.Applied, 0);
        }, DispatcherPriority.Normal, cancellationToken).GetAwaiter().GetResult();
        return result.GetAwaiter().GetResult();
    }

    public void ClearOverrides() => restore().AsTask().GetAwaiter().GetResult();
}
