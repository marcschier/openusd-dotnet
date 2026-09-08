// Copyright (c) marcschier. Licensed under the MIT License.

using Avalonia.Controls;
using Avalonia.Interactivity;
using OpenUsd.Editing;
using OpenUsd.Geom;
using OpenUsd.Interop;
using OpenUsd.Rendering;

namespace OpenUsd.Viewer;

public sealed partial class MainWindow
{
    private const string SavedViewCommandPrefix = "camera.savedView:";
    private CameraBookmarksWindow? _cameraBookmarksWindow;
    private IReadOnlyList<ViewerCameraBookmark> _savedViews = Array.Empty<ViewerCameraBookmark>();
    private bool _savedViewOperationBusy;
    private bool _savedViewsRefreshRequested;
    private Task _savedViewsRefreshTask = Task.CompletedTask;
    private CancellationTokenSource? _savedViewRecallCancellation;
    private Task _savedViewRecallDrained = Task.CompletedTask;

    private void WireSavedViewCommands()
    {
        ManageSavedViewsMenuItem.Click += (_, _) => ShowSavedViews();
        UpdateSavedViewsContext();
    }

    private ViewerCameraBookmarkAvailability GetSavedViewAvailability()
    {
        ViewerAuthoredEditController? editor = _documentEditor;
        bool ready = editor is not null && _coordinator is not null && _documentObservation is not null &&
            !_documentBusy && !_documentEditBusy && !_savedViewOperationBusy && !_sequenceJobRunning &&
            !_workspaceCaptureBusy && !_physicsAuthoringBusy && !_closeDecisionPending && !_shutdownStarted &&
            !_playbackStopping && !editor.IsSuspended && _savedViewRecallCancellation is null && !IsAutomatedViewerRun();
        bool editable = ready && editor is { CanSaveReview: true };
        string source = editor is { CanSaveReview: true }
            ? $"Source: {editor.CameraBookmarkSourceBinding.SourceRootPath}\n" +
                "Saved views are review data. Save Review Document preserves them; source files stay untouched."
            : _reviewSessionOnlyReason ?? "Saved views require an editable review with a verified source.";
        string reason = !editable ? "Saved-view editing is unavailable while the document is busy or unverified." :
            _physics is { IsEnabled: true } ? "Disable physics preview before capturing or recalling an exact view." :
            _playbackLifetime is not null ? "Pause playback before adding an exact saved view." :
            _cameraNavigation.Camera.Mode != CameraMode.Matrices
                ? "Automatic is not an exact view. Use Frame Selected or Explicit Legacy Pose first." :
            _coordinator!.CurrentState.Camera != _cameraNavigation.Camera
                ? "Wait for the current camera update to finish before adding a saved view." : string.Empty;
        return new ViewerCameraBookmarkAvailability(
            editable, editable && reason.Length == 0,
            editable && _physics is not { IsEnabled: true }, source, reason);
    }

    private void UpdateSavedViewsContext()
    {
        ViewerCameraBookmarkAvailability available = GetSavedViewAvailability();
        ManageSavedViewsMenuItem.IsEnabled = _coordinator is not null && !_shutdownStarted &&
            !_documentBusy && !_sequenceJobRunning && _savedViewRecallCancellation is null;
        SavedViewsMenu.IsEnabled = available.CanRecall && _savedViews.Count != 0;
        _cameraBookmarksWindow?.UpdateContext();
    }

    private void ShowSavedViews()
    {
        if (_cameraBookmarksWindow is { } existing)
        {
            existing.Activate();
            return;
        }
        if (_documentEditor is not { } editor || _coordinator is null || _shutdownStarted)
        {
            ShowError("Open a document before managing saved views.");
            return;
        }
        WorkspaceFocus focus = CaptureWorkspaceFocus();
        var window = new CameraBookmarksWindow(
            token => ReadSavedViewsAsync(editor, token),
            (name, token) => CaptureSavedViewAsync(editor, name, token),
            (capture, items, description, token) => ApplySavedViewsAsync(editor, capture, items, description, token),
            (bookmark, token) => RecallSavedViewAsync(editor, bookmark, token),
            GetSavedViewAvailability);
        _cameraBookmarksWindow = window;
        window.Closed += (_, _) =>
        {
            _cameraBookmarksWindow = null;
            RestoreWorkspaceFocus(focus);
        };
        window.Show(this);
    }

    private void RequireSavedViewDocument(ViewerAuthoredEditController editor)
    {
        if (!ReferenceEquals(editor, _documentEditor) || _coordinator is null ||
            _shutdownStarted || _documentLifetime is not { IsCancellationRequested: false })
        {
            throw new InvalidOperationException("This saved-view operation belongs to a retired document.");
        }
    }

    private async Task<ViewerCameraBookmarkCapture> ReadSavedViewsAsync(
        ViewerAuthoredEditController editor, CancellationToken token)
    {
        RequireSavedViewDocument(editor);
        ViewerCameraBookmarkCapture capture = await new ViewerCameraBookmarkCatalog(editor).ReadAsync(token);
        RequireSavedViewDocument(editor);
        _savedViews = capture.Items;
        RenderSavedViewMenu();
        return capture;
    }

    private void QueueSavedViewsRefresh()
    {
        _savedViewsRefreshRequested = true;
        if (_savedViewsRefreshTask.IsCompleted)
        {
            _savedViewsRefreshTask = RefreshSavedViewsAsync();
        }
    }

    private async Task RefreshSavedViewsAsync()
    {
        do
        {
            _savedViewsRefreshRequested = false;
            ViewerAuthoredEditController? editor = _documentEditor;
            CancellationToken token = _documentLifetime?.Token ?? _viewerLifetime.Token;
            if (editor is not { CanSaveReview: true } || _shutdownStarted)
            {
                _savedViews = Array.Empty<ViewerCameraBookmark>();
                RenderSavedViewMenu();
                return;
            }
            try
            {
                await ReadSavedViewsAsync(editor, token);
                if (_cameraBookmarksWindow is { } window)
                {
                    await window.RefreshAsync();
                }
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception) when (exception is OpenUsdNativeException or IOException or
                InvalidOperationException or ArgumentException or NotSupportedException or
                OpenUsd.Rendering.Silk.OpenUsdSilkException or OpenUsd.Rendering.Storm.OpenUsdStormException or
                TimeoutException)
            {
                if (ReferenceEquals(editor, _documentEditor))
                {
                    _savedViews = Array.Empty<ViewerCameraBookmark>();
                    RenderSavedViewMenu();
                    ViewerStartupOptions.WriteStatus($"Saved views are unavailable: {exception.Message}");
                }
            }
        }
        while (_savedViewsRefreshRequested);
    }

    private void RenderSavedViewMenu()
    {
        _commands.RemoveGroup(SavedViewCommandPrefix);
        ViewerAuthoredEditController? editor = _documentEditor;
        MenuItem[] items = [.. _savedViews.Select(bookmark =>
        {
            var item = new MenuItem
            {
                Header = bookmark.Name.Replace("_", "__", StringComparison.Ordinal),
                Tag = bookmark
            };
            ToolTip.SetTip(item, $"Recall exact camera and time {ViewerTimelineMath.Format(bookmark.TimeCode)}");
            item.Click += async (_, _) =>
            {
                if (editor is null)
                {
                    return;
                }
                try
                {
                    await RecallSavedViewAsync(editor, bookmark, _viewerLifetime.Token);
                }
                catch (OperationCanceledException)
                {
                }
                catch (Exception exception) when (exception is OpenUsdNativeException or IOException or
                    InvalidOperationException or ArgumentException or NotSupportedException)
                {
                    ShowError($"Saved view was not recalled: {exception.Message}");
                }
            };
            _commands.Register(new ViewerCommandDescriptor(
                SavedViewCommandPrefix + bookmark.Id.ToString("N"), ViewerCommandGroup.Camera,
                item.Header.ToString()!, $"Recall saved view: {bookmark.Name}"), item);
            return item;
        })];
        SavedViewsMenu.ItemsSource = items;
        UpdateSavedViewsContext();
    }

    private async Task<ViewerCameraBookmark> CaptureSavedViewAsync(
        ViewerAuthoredEditController editor, string name, CancellationToken token)
    {
        name = ViewerCameraBookmark.ValidateName(name);
        await _documentGate.WaitAsync(token);
        try
        {
            RequireSavedViewDocument(editor);
            ViewerCameraBookmarkAvailability available = GetSavedViewAvailability();
            if (!available.CanCapture)
            {
                throw new NotSupportedException(available.CaptureReason);
            }
            ViewerRenderCoordinator coordinator = _coordinator!;
            StageRenderState state = coordinator.CurrentState;
            ViewerCameraBookmarkUiState ui = _cameraNavigation.CaptureSavedViewState();
            UsdReviewSourceBinding binding = editor.CameraBookmarkSourceBinding;
            ViewerCameraBookmark bookmark = await coordinator.Scheduler.InvokeAsync(stage =>
            {
                if (!binding.HasSamePayload(stage.CaptureReviewSourceBinding()))
                {
                    throw new InvalidOperationException("The verified source changed while capturing the saved view.");
                }
                string stamp = ViewerCameraBookmarkCatalog.CreateSourceStamp(binding);
                if (ui.StageCamera is not { } staged)
                {
                    return ViewerCameraBookmark.CreateFree(
                        Guid.NewGuid(), name, stamp, state.Time.TimeCode, state.Viewport, ui.FreeCamera);
                }
                if (!stage.HasPrim(staged.PrimPath))
                {
                    throw new InvalidOperationException("The active authored camera is missing.");
                }
                UsdPrim prim = stage.GetPrim(staged.PrimPath);
                if (!prim.IsActive() || !prim.IsDefined() || !UsdGeomCamera.TryWrap(prim, out UsdGeomCamera camera))
                {
                    throw new InvalidOperationException("The active authored camera is inactive or unavailable.");
                }
                ViewerStageCameraSnapshot sample = ViewerStageCameraSnapshotFactory.Create(
                    staged.PrimPath, state.Time.TimeCode, camera.Xformable.GetWorldTransform(state.Time.TimeCode),
                    camera.GetState(state.Time.TimeCode));
                return ViewerCameraBookmark.CreateStage(Guid.NewGuid(), name, stamp, state.Viewport, sample);
            }, token);
            RequireSavedViewDocument(editor);
            if (coordinator.CurrentState != state || bookmark.Camera != state.Camera ||
                _cameraNavigation.Camera != state.Camera)
            {
                throw new InvalidOperationException(
                    "The camera changed while saving the view. Try again when it settles.");
            }
            return bookmark;
        }
        finally
        {
            _documentGate.Release();
        }
    }

    private async Task ApplySavedViewsAsync(
        ViewerAuthoredEditController editor, ViewerCameraBookmarkCapture capture,
        IReadOnlyList<ViewerCameraBookmark> items, string description, CancellationToken token)
    {
        await _documentGate.WaitAsync(token);
        bool entered = false;
        try
        {
            RequireSavedViewDocument(editor);
            if (!GetSavedViewAvailability().CanEdit)
            {
                throw new InvalidOperationException("The document is not ready for a saved-view edit.");
            }
            _savedViewOperationBusy = true;
            _documentEditBusy = true;
            entered = true;
            UpdateDocumentCommands();
            ViewerAuthoredEditResult result =
                await new ViewerCameraBookmarkCatalog(editor).ApplyAsync(capture, items, description, token);
            if (result.Outcome != UsdLayerEditOutcome.Applied)
            {
                throw new InvalidOperationException(result.Message);
            }
            await ReadSavedViewsAsync(editor, token);
        }
        finally
        {
            if (entered)
            {
                _documentEditBusy = false;
                _savedViewOperationBusy = false;
                UpdateDocumentCommands();
            }
            _documentGate.Release();
        }
    }

    private Task RecallSavedViewAsync(
        ViewerAuthoredEditController editor, ViewerCameraBookmark bookmark, CancellationToken token)
    {
        if (!GetSavedViewAvailability().CanRecall || !_savedViewRecallDrained.IsCompleted)
        {
            throw new InvalidOperationException("The document is not ready to recall a saved view.");
        }
        var cancellation = CancellationTokenSource.CreateLinkedTokenSource(
            token, _documentLifetime?.Token ?? _viewerLifetime.Token, _viewerLifetime.Token);
        var drained = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _savedViewRecallCancellation = cancellation;
        _savedViewRecallDrained = drained.Task;
        return RecallSavedViewCoreAsync(editor, bookmark, cancellation, drained);
    }

    private async Task RecallSavedViewCoreAsync(
        ViewerAuthoredEditController editor, ViewerCameraBookmark bookmark,
        CancellationTokenSource cancellation, TaskCompletionSource drained)
    {
        bool entered = false;
        bool paused = false;
        bool succeeded = false;
        bool wasPlaying = false;
        bool contentEnabled = MainContentGrid.IsEnabled;
        bool rendererEnabled = RendererSelector.IsEnabled;
        WindowClosingBehavior closing = ClosingBehavior;
        ViewerRenderCoordinator? coordinator = null;
        SourceChangeRegistration? sourceChanges = null;
        ViewerCameraBookmarkUiState previous = default;
        try
        {
            await _documentGate.WaitAsync(cancellation.Token);
            entered = true;
            RequireSavedViewDocument(editor);
            if (_documentBusy || _documentEditBusy || _sequenceJobRunning || _physics is { IsEnabled: true })
            {
                throw new InvalidOperationException("Finish other document or simulation operations before recall.");
            }
            coordinator = _coordinator!;
            _savedViewOperationBusy = true;
            ClosingBehavior = WindowClosingBehavior.OwnerWindowOnly;
            sourceChanges = _sourceChanges;
            sourceChanges?.Changes.Pause();
            wasPlaying = _playbackLifetime is not null;
            previous = _cameraNavigation.CaptureSavedViewState();
            paused = true;
            SetBusy("Preparing saved view; draining playback and camera updates...");
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
            previous = _cameraNavigation.CaptureSavedViewState();
            cancellation.Token.ThrowIfCancellationRequested();
            ViewerStatus.Text = "Recalling saved view...";
            HierarchyState.Text = ViewerStatus.Text;
            await coordinator.RecallCameraBookmarkAsync(bookmark,
                state =>
                {
                    RequireSavedViewDocument(editor);
                    _cameraNavigation.ApplySavedView(bookmark, state.Viewport);
                    SetSavedViewTimeUi(state.Time.TimeCode);
                },
                state =>
                {
                    _cameraNavigation.RestoreSavedViewState(previous);
                    SetSavedViewTimeUi(state.Time.TimeCode);
                }, cancellation.Token);
            succeeded = true;
        }
        finally
        {
            try
            {
                if (paused && ReferenceEquals(coordinator, _coordinator) &&
                    _documentLifetime is { IsCancellationRequested: false } lifetime)
                {
                    if (!succeeded)
                    {
                        _cameraNavigation.RestoreSavedViewState(previous);
                    }
                    await InitializeTimelineAsync(coordinator!, _timing, lifetime.Token, preserveTime: true);
                    InitializeCameraUpdates(coordinator!, lifetime.Token);
                    MainContentGrid.IsEnabled = contentEnabled;
                    RendererSelector.IsEnabled = rendererEnabled;
                    SetReady(succeeded ? $"Recalled saved view '{bookmark.Name}'. Playback remains paused." :
                        "Saved view was not committed; the prior view was kept.");
                }
            }
            finally
            {
                try
                {
                    ClosingBehavior = closing;
                    _savedViewOperationBusy = false;
                    _savedViewRecallCancellation = null;
                    cancellation.Dispose();
                    try
                    {
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
                    UpdateSavedViewsContext();
                    if (paused && !succeeded && wasPlaying && !_shutdownStarted && !_closeDecisionPending &&
                        ReferenceEquals(coordinator, _coordinator))
                    {
                        OnPlayPauseClick(PlayPauseButton, new RoutedEventArgs(Button.ClickEvent));
                    }
                }
                finally
                {
                    drained.TrySetResult();
                }
            }
        }
    }

    private void SetSavedViewTimeUi(double timeCode)
    {
        lock (_timelineGate)
        {
            _currentTimeCode = timeCode;
        }
        UpdateTimelineUi(timeCode);
        UpdateCameraStatus();
    }

    private async Task CloseSavedViewsAsync()
    {
        _savedViewRecallCancellation?.Cancel();
        if (_cameraBookmarksWindow is { } window)
        {
            await window.CloseAsync();
        }
        await _savedViewRecallDrained;
    }
}
