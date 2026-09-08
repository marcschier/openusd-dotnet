// Copyright (c) marcschier. Licensed under the MIT License.

using Avalonia.Controls;
using OpenUsd.Rendering;

namespace OpenUsd.Viewer;

public sealed partial class MainWindow
{
    private RenderImageSequenceWindow? _renderSequenceWindow;
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
            $"{coordinator.ActiveBackend?.Name ?? "Unavailable"}; " +
                $"{state.Viewport.Width} x {state.Viewport.Height} viewport pixels",
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

    private void UpdateRenderSequenceContext()
    {
        if (_renderSequenceWindow is { } window && _coordinator is { } coordinator)
        {
            StageRenderState state = coordinator.CurrentState;
            window.UpdateContext(
                $"{coordinator.ActiveBackend?.Name ?? "Unavailable"}; " +
                    $"{state.Viewport.Width} x {state.Viewport.Height} viewport pixels");
        }
    }
}
