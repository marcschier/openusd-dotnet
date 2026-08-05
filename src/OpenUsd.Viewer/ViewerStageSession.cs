// Copyright (c) marcschier. Licensed under the MIT License.

using OpenUsd.Rendering;

namespace OpenUsd.Viewer;

/// <summary>
/// The viewer-owned stage a host may author into while the shell renders it. Obtained
/// from <see cref="ViewerHostOptions.StageReadyAsync"/>.
/// </summary>
/// <remarks>
/// Authoring through <see cref="Scheduler"/> flows into the scheduler's ordered change
/// feed, which the viewer already pumps, so edits invalidate and redraw without any
/// further call from the host.
/// </remarks>
public sealed class ViewerStageSession
{
    private readonly Func<IRenderPickingBackend?> _getPickingBackend;
    private readonly Func<StageRenderState> _getCurrentRenderState;
    private readonly Func<string, CancellationToken, ValueTask> _frameAsync;

    internal ViewerStageSession(
        UsdStageScheduler scheduler,
        string stagePath,
        Func<IRenderPickingBackend?> getPickingBackend,
        Func<StageRenderState> getCurrentRenderState,
        Func<string, CancellationToken, ValueTask> frameAsync)
    {
        ArgumentNullException.ThrowIfNull(scheduler);
        ArgumentException.ThrowIfNullOrWhiteSpace(stagePath);
        ArgumentNullException.ThrowIfNull(getPickingBackend);
        ArgumentNullException.ThrowIfNull(getCurrentRenderState);
        ArgumentNullException.ThrowIfNull(frameAsync);
        Scheduler = scheduler;
        StagePath = stagePath;
        _getPickingBackend = getPickingBackend;
        _getCurrentRenderState = getCurrentRenderState;
        _frameAsync = frameAsync;
    }

    /// <summary>
    /// Raised when the viewport camera or viewport dimensions change.
    /// </summary>
    public event EventHandler<ViewerCameraState>? CameraChanged;

    /// <summary>
    /// The scheduler that owns the open stage. All host authoring must go through it.
    /// </summary>
    public UsdStageScheduler Scheduler { get; }

    /// <summary>
    /// Absolute path of the stage the viewer opened.
    /// </summary>
    public string StagePath { get; }

    /// <summary>
    /// The picking backend for the renderer currently presenting this stage, or
    /// <see langword="null"/> when no pick-capable backend is active.
    /// </summary>
    public IRenderPickingBackend? PickingBackend => _getPickingBackend();

    /// <summary>
    /// Current render state, including the revisions a <see cref="RenderPickRequest"/>
    /// must quote.
    /// </summary>
    public StageRenderState CurrentRenderState => _getCurrentRenderState();

    /// <summary>
    /// The camera the viewport is currently rendering from and the viewport dimensions
    /// its projection assumes.
    /// </summary>
    public ViewerCameraState Camera
    {
        get
        {
            StageRenderState state = CurrentRenderState;
            return new ViewerCameraState(state.Camera, state.Viewport);
        }
    }

    /// <summary>
    /// Frames the viewport around the specified prim path.
    /// </summary>
    public ValueTask FrameAsync(string primPath, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(primPath);
        return _frameAsync(primPath, cancellationToken);
    }

    internal void NotifyCameraChanged(ViewerCameraState camera) =>
        CameraChanged?.Invoke(this, camera);
}
