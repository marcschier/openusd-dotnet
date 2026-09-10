// Copyright (c) marcschier. Licensed under the MIT License.

using Avalonia.Controls;
using OpenUsd.Rendering;

namespace OpenUsd.Viewer;

public sealed partial class MainWindow
{
    private RenderImageSequenceWindow? _renderSequenceWindow;
    private AuthoredRenderProductWindow? _authoredRenderProductWindow;
    private bool _sequenceJobRunning;

    private void ShowRenderImageSequence()
    {
        if (_renderSequenceWindow is { } existing)
        {
            existing.Activate();
            return;
        }
        if (_coordinator is not { } coordinator || _shutdownStarted)
        {
            ShowError("Open a stage before rendering an image sequence.");
            return;
        }
        WorkspaceFocus focus = CaptureWorkspaceFocus();
        StageRenderState state = coordinator.CurrentState;
        double start = _timing.HasFiniteRange ? _timing.PresentationStart : state.Time.TimeCode;
        double end = _timing.HasFiniteRange ? _timing.PresentationEnd : start;
        var window = new RenderImageSequenceWindow(
            start, end,
            DescribeRenderSequence(coordinator, state),
            coordinator.GetRenderSequenceUnsupportedReason,
            RenderImageSequenceAsync);
        _renderSequenceWindow = window;
        window.Closed += (_, _) =>
        {
            _renderSequenceWindow = null;
            RestoreWorkspaceFocus(focus);
        };
        window.Show(this);
    }

    private async Task<RenderDiskJobResult> RenderImageSequenceAsync(
        ViewerRenderSequenceRange range, string outputParent, ViewerRenderSequenceOutputOptions outputs,
        Action<ViewerRenderSequenceProgress> progress, CancellationToken cancellationToken)
    {
        if (_sequenceJobRunning || _shutdownStarted || _closeDecisionPending || _documentBusy)
        {
            throw new InvalidOperationException("The Viewer is already rendering or changing documents.");
        }
        _sequenceJobRunning = true;
        WindowClosingBehavior closingBehavior = ClosingBehavior;
        // Let MainWindow cancel and drain first. Otherwise the owned job window's
        // asynchronous Closing cancellation prevents the owner's Closing handler.
        ClosingBehavior = WindowClosingBehavior.OwnerWindowOnly;
        bool entered = false;
        bool paused = false;
        SourceChangeRegistration? sourceChanges = null;
        bool contentEnabled = MainContentGrid.IsEnabled;
        bool rendererEnabled = RendererSelector.IsEnabled;
        ViewerRenderCoordinator? coordinator = null;
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken, _documentLifetime?.Token ?? _viewerLifetime.Token, _viewerLifetime.Token);
        try
        {
            await _documentGate.WaitAsync(cancellation.Token);
            entered = true;
            coordinator = _coordinator ?? throw new InvalidOperationException("No document is open.");
            if (_workspaceCaptureBusy || _documentEditBusy || _physicsAuthoringBusy ||
                _physics is { IsEnabled: true } || IsAutomatedViewerRun())
            {
                throw new InvalidOperationException(
                    "Finish other capture/edit operations and disable physics preview " +
                    "before rendering a USD time sequence.");
            }
            if (_propertyEditor is { } editor)
            {
                await editor.CloseAsync();
            }
            await UpdateViewportStateAsync(coordinator, cancellation.Token);
            if (coordinator.GetRenderSequenceUnsupportedReason(outputs) is { } unsupported)
            {
                throw new NotSupportedException(unsupported);
            }
            paused = true;
            sourceChanges = _sourceChanges;
            sourceChanges?.Changes.Pause();
            SetBusy("Rendering image sequence...");
            SyncColorManagementMenu();
            ReloadStageMenuItem.IsEnabled = true;
            ReloadStageButton.IsEnabled = true;
            MainContentGrid.IsEnabled = false;
            RendererSelector.IsEnabled = false;
            StopStormNavigationPolling();
            EndCameraPointerGesture();
            await StopTimelineAsync();
            ViewerStageCameraRefreshPump? stageCameras = _stageCameraRefreshes;
            _stageCameraRefreshes = null;
            if (stageCameras is not null)
            {
                await stageCameras.DisposeAsync();
            }
            ViewerCameraUpdatePump? cameras = _cameraUpdates;
            _cameraUpdates = null;
            if (cameras is not null)
            {
                await cameras.DisposeAsync();
            }
            ViewerStageCameraModeView camera = _stageCameraMode.GetView();
            return await coordinator.RenderSequenceAsync(
                range, outputParent, outputs, camera.IsActive ? camera.PrimPath : null,
                update =>
                {
                    progress(update);
                    ViewerStatus.Text = $"Image sequence: {update.Phase.ToLowerInvariant()} " +
                        $"{update.CompletedFrames}/{update.TotalFrames} frames.";
                },
                cancellation.Token);
        }
        finally
        {
            try
            {
                if (paused && ReferenceEquals(coordinator, _coordinator) &&
                    _documentLifetime is { IsCancellationRequested: false } lifetime)
                {
                    await InitializeTimelineAsync(coordinator!, _timing, lifetime.Token, preserveTime: true);
                    InitializeCameraUpdates(coordinator!, lifetime.Token);
                    MainContentGrid.IsEnabled = contentEnabled;
                    RendererSelector.IsEnabled = rendererEnabled;
                    SetReady("Image sequence stopped. See its result window; playback remains paused.");
                    RenderHierarchy();
                }
            }
            finally
            {
                ClosingBehavior = closingBehavior;
                _sequenceJobRunning = false;
                try
                {
                    SyncColorManagementMenu();
                    if (sourceChanges is not null && IsCurrentSource(sourceChanges) &&
                        sourceChanges.Changes.Resume())
                    {
                        ScheduleSourceRefresh(sourceChanges);
                    }
                }
                finally
                {
                    if (entered)
                    {
                        _documentGate.Release();
                    }
                }
            }
        }
    }

    private Task CloseRenderSequenceAsync() =>
        _renderSequenceWindow is { } window ? window.CloseAsync() : Task.CompletedTask;

    private void ShowAuthoredRenderProduct()
    {
        if (_authoredRenderProductWindow is { } existing)
        {
            existing.Activate();
            return;
        }
        if (_coordinator is not { } coordinator || _shutdownStarted)
        {
            ShowError("Open a stage before rendering an authored RenderProduct.");
            return;
        }
        WorkspaceFocus focus = CaptureWorkspaceFocus();
        StageRenderState state = coordinator.CurrentState;
        double start = _timing.HasFiniteRange ? _timing.PresentationStart : state.Time.TimeCode;
        double end = _timing.HasFiniteRange ? _timing.PresentationEnd : start;
        var window = new AuthoredRenderProductWindow(
            start,
            end,
            $"{coordinator.ActiveBackend?.Name ?? "Unavailable"}; authored product camera and raster",
            coordinator.QueryAuthoredRenderProductsAsync,
            RenderAuthoredProductAsync,
            () => coordinator.SupportsRenderProductCapture
                ? null
                : "Wait for a completed frame from an hdSilk renderer with authored-product capture.");
        _authoredRenderProductWindow = window;
        window.Closed += (_, _) =>
        {
            _authoredRenderProductWindow = null;
            RestoreWorkspaceFocus(focus);
        };
        window.Show(this);
    }

    private async Task<RenderDiskJobResult> RenderAuthoredProductAsync(
        ViewerAuthoredRenderProductRequest request,
        Action<ViewerAuthoredRenderProductProgress> progress,
        CancellationToken cancellationToken)
    {
        if (_sequenceJobRunning || _shutdownStarted || _closeDecisionPending || _documentBusy)
        {
            throw new InvalidOperationException("The Viewer is already rendering or changing documents.");
        }
        _sequenceJobRunning = true;
        WindowClosingBehavior closingBehavior = ClosingBehavior;
        ClosingBehavior = WindowClosingBehavior.OwnerWindowOnly;
        bool entered = false;
        bool paused = false;
        SourceChangeRegistration? sourceChanges = null;
        bool contentEnabled = MainContentGrid.IsEnabled;
        bool rendererEnabled = RendererSelector.IsEnabled;
        ViewerRenderCoordinator? coordinator = null;
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken, _documentLifetime?.Token ?? _viewerLifetime.Token, _viewerLifetime.Token);
        try
        {
            await _documentGate.WaitAsync(cancellation.Token);
            entered = true;
            coordinator = _coordinator ?? throw new InvalidOperationException("No document is open.");
            if (_workspaceCaptureBusy || _documentEditBusy || _physicsAuthoringBusy ||
                _physics is { IsEnabled: true } || IsAutomatedViewerRun())
            {
                throw new InvalidOperationException(
                    "Finish other capture/edit operations and disable physics preview " +
                    "before rendering an authored RenderProduct.");
            }
            if (_propertyEditor is { } editor)
            {
                await editor.CloseAsync();
            }
            await UpdateViewportStateAsync(coordinator, cancellation.Token);
            paused = true;
            sourceChanges = _sourceChanges;
            sourceChanges?.Changes.Pause();
            SetBusy("Rendering authored RenderProduct...");
            SyncColorManagementMenu();
            ReloadStageMenuItem.IsEnabled = true;
            ReloadStageButton.IsEnabled = true;
            MainContentGrid.IsEnabled = false;
            RendererSelector.IsEnabled = false;
            StopStormNavigationPolling();
            EndCameraPointerGesture();
            await StopTimelineAsync();
            ViewerStageCameraRefreshPump? stageCameras = _stageCameraRefreshes;
            _stageCameraRefreshes = null;
            if (stageCameras is not null)
            {
                await stageCameras.DisposeAsync();
            }
            ViewerCameraUpdatePump? cameras = _cameraUpdates;
            _cameraUpdates = null;
            if (cameras is not null)
            {
                await cameras.DisposeAsync();
            }
            return await coordinator.RenderAuthoredProductAsync(
                request,
                update =>
                {
                    progress(update);
                    ViewerStatus.Text = $"Authored RenderProduct: {update.Phase.ToLowerInvariant()} " +
                        $"{update.CompletedFrames}/{update.TotalFrames} frames.";
                },
                cancellation.Token);
        }
        finally
        {
            try
            {
                if (paused && ReferenceEquals(coordinator, _coordinator) &&
                    _documentLifetime is { IsCancellationRequested: false } lifetime)
                {
                    await InitializeTimelineAsync(coordinator!, _timing, lifetime.Token, preserveTime: true);
                    InitializeCameraUpdates(coordinator!, lifetime.Token);
                    MainContentGrid.IsEnabled = contentEnabled;
                    RendererSelector.IsEnabled = rendererEnabled;
                    SetReady("Authored RenderProduct stopped. See its result window; playback remains paused.");
                    RenderHierarchy();
                }
            }
            finally
            {
                ClosingBehavior = closingBehavior;
                _sequenceJobRunning = false;
                try
                {
                    SyncColorManagementMenu();
                    if (sourceChanges is not null && IsCurrentSource(sourceChanges) &&
                        sourceChanges.Changes.Resume())
                    {
                        ScheduleSourceRefresh(sourceChanges);
                    }
                }
                finally
                {
                    if (entered)
                    {
                        _documentGate.Release();
                    }
                }
            }
        }
    }

    private Task CloseAuthoredRenderProductAsync() =>
        _authoredRenderProductWindow is { } window ? window.CloseAsync() : Task.CompletedTask;

    private void UpdateRenderSequenceContext()
    {
        if (_renderSequenceWindow is { } window && _coordinator is { } coordinator)
        {
            StageRenderState state = coordinator.CurrentState;
            window.UpdateContext(DescribeRenderSequence(coordinator, state));
        }
        if (_authoredRenderProductWindow is { } productWindow && _coordinator is { } productCoordinator)
        {
            productWindow.UpdateContext(
                $"{productCoordinator.ActiveBackend?.Name ?? "Unavailable"}; authored product camera and raster");
        }
    }

    private static string DescribeRenderSequence(ViewerRenderCoordinator coordinator, StageRenderState state) =>
        $"{coordinator.ActiveBackend?.Name ?? "Unavailable"}; " +
        $"{state.Viewport.Width} x {state.Viewport.Height} viewport pixels" +
        (coordinator.RenderSequenceProfileDescription is { } profile ? $"; {profile}" : string.Empty);

    private void UpdateCaptureAvailability()
    {
        bool enabled = _coordinator is not null && !_documentBusy && !_shutdownStarted &&
            !_workspaceCaptureBusy && !IsAutomatedViewerRun();
        CaptureFrameMenuItem.IsEnabled = enabled && _coordinator is { CanCaptureFrame: true };
        RenderImageSequenceMenuItem.IsEnabled = enabled &&
            _coordinator is { CurrentState.Viewport: { Width: > 0, Height: > 0 } };
        RenderAuthoredProductMenuItem.IsEnabled = enabled;
        UpdateRenderSequenceContext();
    }
}
