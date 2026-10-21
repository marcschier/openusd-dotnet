// Copyright (c) marcschier. Licensed under the MIT License.

using OpenUsd.Rendering;
using OpenUsd.Rendering.Physics;
using OpenUsd.Rendering.Silk;

namespace OpenUsd.Mcp;

internal sealed partial class PreviewSilkFrameSource : IPhysicsRenderRecordingSource
{
    private readonly UsdStageRenderSource? _recordingSource;
    private readonly SilkFrameCapturer? _recordingCapturer;
    private readonly OpenUsdSilkSession? _recordingSession;
    private SilkPhysicsTransformOverrides? _recordingTransforms;
    private SilkPhysicsDeformations? _recordingDeformations;
    private RenderDiskJobRequest? _recordingRequest;
    private PhysicsRenderBindingTable? _recordingBindings;
    private StageRenderState? _recordingState;
    private bool _recordingActive;

    public void Validate(
        RenderDiskJobRequest request, PhysicsRenderRecordingContext context, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_teardownStarted, this);
        cancellationToken.ThrowIfCancellationRequested();
        if (_recordingSource is not { } source || _recordingCapturer is null || _recordingSession is null)
        {
            throw new NotSupportedException("The configured source does not expose native physics capture.");
        }
        if (request.ProductPlan is not null || context.ReviewIdentity is not null)
        {
            throw new NotSupportedException("This MCP physics source admits viewport captures without a review-origin claim.");
        }
        if (!ReferenceEquals(request, _recordingRequest))
        {
            foreach (StageRenderState state in request.Frames)
            {
                if (state.RenderSettings != RenderSettings.PresentationDefault ||
                    state.Display != SceneDisplayState.Default || state.Selection.Items.Count != 0)
                {
                    throw new NotSupportedException("MCP physics capture requires its explicit presentation settings.");
                }
            }
        }
        using UsdStageRenderLease lease = source.AcquireLease();
        if (lease.ChangeSerial != context.SourceRevision)
        {
            throw new InvalidOperationException("The authored source changed during physics capture.");
        }
        if (_recordingActive && !ReferenceEquals(request, _recordingRequest))
        {
            throw new InvalidOperationException("A different recording still owns the capture source.");
        }
        _recordingRequest = request;
    }

    public PhysicsRenderCaptureResult Capture(
        StageRenderState state, PhysicsRenderRecordingFrame frame, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_teardownStarted, this);
        SilkFrameCapturer capturer = _recordingCapturer ??
            throw new NotSupportedException("The configured source does not expose native physics capture.");
        RenderDiskJobRequest request = _recordingRequest ??
            throw new InvalidOperationException("Physics capture must be validated first.");
        cancellationToken.ThrowIfCancellationRequested();
        foreach (PhysicsRenderDeformableRegion region in frame.Deformations.Regions)
        {
            if (!SilkPhysicsDeformations.IsDomainSupported(region.Domain))
            {
                throw new NotSupportedException("The capture backend cannot render a requested deformation domain.");
            }
        }
        if (_recordingTransforms is null || _recordingTransforms.Capacity < frame.Transforms.Count)
        {
            _recordingTransforms = new SilkPhysicsTransformOverrides(4096);
        }
        _recordingDeformations ??= new SilkPhysicsDeformations();
        _recordingTransforms.Stage(frame.Bindings, frame.Transforms);
        if (_recordingDeformations.Stage(frame.Bindings, frame.Deformations) != frame.Deformations.Count)
        {
            throw new InvalidDataException("The complete deformation set could not be staged.");
        }
        _recordingBindings = frame.Bindings;
        _recordingState = state;
        _recordingActive = true;
        capturer.PhysicsOverrides = _recordingTransforms;
        capturer.PhysicsDeformations = _recordingDeformations;
        var view = new CaptureView("physics-sequence", state.Camera, state.Time.TimeCode);
        RenderJobImage image;
        if (request.IncludeHdrColor)
        {
            image = CaptureWithHdrColor(view, state.Viewport.Width, state.Viewport.Height,
                request.Limits.MaximumFrameBytes, request.IncludeDeviceDepth, cancellationToken);
        }
        else if (request.IncludeDeviceDepth)
        {
            image = CaptureWithDepth(view, state.Viewport.Width, state.Viewport.Height,
                request.Limits.MaximumFrameBytes, cancellationToken);
        }
        else
        {
            ImageRgba8 rgba = Capture(view, state.Viewport.Width, state.Viewport.Height);
            image = new RenderJobImage(rgba.Width, rgba.Height, rgba.Pixels, Rgba8RowOrder.TopDown)
            {
                Diagnostics = Diagnostics
            };
        }
        return new PhysicsRenderCaptureResult(image, _recordingTransforms.SourceRevision,
            _recordingTransforms.ResolvedOverrideCount, _recordingDeformations.Count);
    }

    public void ClearOverrides()
    {
        if (!_recordingActive)
        {
            return;
        }
        ObjectDisposedException.ThrowIf(_teardownStarted, this);
        SilkFrameCapturer capturer = _recordingCapturer ??
            throw new InvalidOperationException("The recording capturer was lost during cleanup.");
        StageRenderState state = _recordingState ??
            throw new InvalidOperationException("The recording state was lost during cleanup.");
        _recordingTransforms!.Clear();
        _recordingDeformations!.Stage(_recordingBindings!, PhysicsRenderDeformationView.Empty);
        // The final image is already encoded. This drained capture restores authored GPU geometry
        // as well as transforms before the shared engine publishes the completed directory.
        _ = capturer.Capture(_recordingSession!, state.Viewport.Width, state.Viewport.Height,
            state.RenderSettings, state.Time.TimeCode, state.Camera);
        capturer.PhysicsOverrides = null;
        capturer.PhysicsDeformations = null;
        _recordingActive = false;
        _recordingBindings = null;
        _recordingState = null;
    }
}
