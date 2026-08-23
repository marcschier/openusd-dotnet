// Copyright (c) marcschier. Licensed under the MIT License.

using System.Diagnostics;
using System.Globalization;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;

namespace OpenUsd.Viewer;

/// <summary>
/// Wires the interactive physics transport into the viewer window.
/// </summary>
/// <remarks>
/// <para>
/// The controller is created only when the user asks for physics. Most stages have no simulation
/// content, and building a world, starting a worker, and allocating render buffers for every stage
/// that is merely opened would make opening a stage slower for everyone to benefit nobody.
/// </para>
/// <para>
/// Everything below runs on the UI thread and does no simulation. It issues requests, reads a
/// lock-free status, and renders that status; the physics worker does the work.
/// </para>
/// </remarks>
public sealed partial class MainWindow
{
    private ViewerPhysicsController? _physics;
    private Action<ViewerPhysicsStatusSnapshot>? _physicsStatusHandler;
    private DispatcherTimer? _physicsPump;
    private MenuFlyout? _physicsOverflowFlyout;
    private ViewerToolbarItem[] _physicsToolbarItems = [];
    private Control[] _physicsToolbarControls = [];
    private double _physicsPlannedWidth = -1;
    private long _physicsBackendGeneration = -1;
    private ulong _physicsInspectorObjectRevision = ulong.MaxValue;
    private int _physicsSessionVersion;
    private bool _updatingPhysicsUi;
    private bool _physicsBakeBusy;
    private bool _physicsScrubbing;
    private bool _physicsRenderFaulted;
    private bool _physicsOverflowOpen;
    private bool _physicsOverflowStale;
    private bool _physicsInspectorStale;
    private readonly ViewerPhysicsBakeCancellation _physicsBakeLifetime = new();

    private void InitializePhysicsUi()
    {
        _physicsToolbarControls =
        [
            PhysicsEnableButton,
            PhysicsPlayPauseButton,
            PhysicsStopButton,
            PhysicsStepButton,
            PhysicsLoopCheckBox,
            PhysicsSpeedSelector,
            PhysicsPreviewCheckBox,
            PhysicsBakeButton,
            PhysicsGizmoSelector,
            PhysicsSnapCheckBox,
            PhysicsUndoButton,
            PhysicsRedoButton,
        ];
        _physicsToolbarItems =
        [
            new ViewerToolbarItem("PhysicsEnableButton", "Physics", 88),
            new ViewerToolbarItem("PhysicsPlayPauseButton", "Play", 72),
            new ViewerToolbarItem("PhysicsStopButton", "Stop", 72),
            new ViewerToolbarItem("PhysicsStepButton", "Step", 72),
            new ViewerToolbarItem("PhysicsLoopCheckBox", "Loop", 72),
            new ViewerToolbarItem("PhysicsSpeedSelector", "Speed", 96),
            new ViewerToolbarItem("PhysicsPreviewCheckBox", "Apply Preview", 132),
            new ViewerToolbarItem("PhysicsBakeButton", "Bake...", 84),
            new ViewerToolbarItem("PhysicsGizmoSelector", "Gizmo", 104),
            new ViewerToolbarItem("PhysicsSnapCheckBox", "Snap", 72),
            new ViewerToolbarItem("PhysicsUndoButton", "Undo", 72),
            new ViewerToolbarItem("PhysicsRedoButton", "Redo", 72),
        ];
        PhysicsEnableButton.Click += OnPhysicsEnableClick;
        PhysicsPlayPauseButton.Click += OnPhysicsPlayPauseClick;
        PhysicsStopButton.Click += OnPhysicsStopClick;
        PhysicsStepButton.Click += OnPhysicsStepClick;
        PhysicsLoopCheckBox.IsCheckedChanged += OnPhysicsLoopChanged;
        PhysicsSpeedSelector.SelectionChanged += OnPhysicsSpeedChanged;
        PhysicsPreviewCheckBox.IsCheckedChanged += OnPhysicsPreviewChanged;
        PhysicsBakeButton.Click += OnPhysicsBakeClick;
        PhysicsBakeCancelButton.Click += OnPhysicsBakeCancelClick;
        PhysicsBakeCancelButton.IsEnabled = false;
        PhysicsScrubber.AddHandler(
            PointerPressedEvent,
            OnPhysicsScrubPressed,
            RoutingStrategies.Tunnel | RoutingStrategies.Bubble,
            handledEventsToo: true);
        PhysicsScrubber.AddHandler(
            PointerReleasedEvent,
            OnPhysicsScrubReleased,
            RoutingStrategies.Tunnel | RoutingStrategies.Bubble,
            handledEventsToo: true);
        PhysicsToolbarGrid.SizeChanged += OnPhysicsToolbarSizeChanged;
        InspectorTabs.SelectionChanged += OnInspectorTabsSelectionChangedForPhysics;
        _physicsOverflowFlyout = new MenuFlyout();
        _physicsOverflowFlyout.Opened += OnPhysicsOverflowOpened;
        _physicsOverflowFlyout.Closed += OnPhysicsOverflowClosed;
        PhysicsOverflowButton.Flyout = _physicsOverflowFlyout;
        InitializePhysicsAuthoringUi();
        RenderPhysicsState(ViewerPhysicsStatusSnapshot.Disabled);
    }

    private void OnInspectorTabsSelectionChangedForPhysics(
        object? sender,
        SelectionChangedEventArgs e)
    {
        _ = sender;
        _ = e;
        if (!_physicsInspectorStale || !PhysicsTab.IsSelected)
        {
            return;
        }

        // The tab was skipped while it was hidden, so the rows it shows are rebuilt exactly once,
        // the moment it becomes visible again.
        RenderPhysicsState(_physics?.Snapshot ?? ViewerPhysicsStatusSnapshot.Disabled);
    }

    private void OnPhysicsOverflowOpened(object? sender, EventArgs e)
    {
        _ = sender;
        _ = e;
        _physicsOverflowOpen = true;
    }

    private void OnPhysicsOverflowClosed(object? sender, EventArgs e)
    {
        _ = sender;
        _ = e;
        _physicsOverflowOpen = false;
        if (!_physicsOverflowStale)
        {
            return;
        }

        // The menu the user was pointing at is never rebuilt underneath them; a plan that became
        // stale while it was open is applied the moment it closes instead.
        _physicsOverflowStale = false;
        double width = _physicsPlannedWidth;
        _physicsPlannedWidth = -1;
        ApplyPhysicsToolbarOverflow(width);
    }

    private void OnPhysicsToolbarSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        _ = sender;
        ApplyPhysicsToolbarOverflow(e.NewSize.Width);
    }

    /// <summary>
    /// Hides the controls that do not fit and lists them in the overflow menu instead.
    /// </summary>
    /// <remarks>
    /// A toolbar that clips its own buttons is worse than one that hides them: a half-drawn label
    /// looks like a rendering fault and the button underneath is unreachable either way. So each
    /// control is either fully visible or in the overflow menu, never partly drawn.
    /// </remarks>
    /// <param name="availableWidth">The width the physics toolbar row was given.</param>
    private void ApplyPhysicsToolbarOverflow(double availableWidth)
    {
        if (_physicsToolbarControls.Length == 0 ||
            Math.Abs(availableWidth - _physicsPlannedWidth) < 0.5)
        {
            return;
        }

        if (_physicsOverflowOpen)
        {
            // Replacing the items of an open menu drops the item under the pointer, so the new
            // plan waits until the user has finished with the menu they opened.
            _physicsPlannedWidth = availableWidth;
            _physicsOverflowStale = true;
            return;
        }

        _physicsPlannedWidth = availableWidth;

        // The scrubber and the status line share the row, so the command bar only ever plans
        // against the part of the width it actually owns.
        double commandWidth = Math.Max(0d, availableWidth - 320d);
        ViewerToolbarOverflowPlan plan = ViewerToolbarOverflowPlanner.Plan(
            _physicsToolbarItems,
            commandWidth);
        var overflowItems = new List<MenuItem>();
        for (int index = 0; index < _physicsToolbarControls.Length; index++)
        {
            bool visible = plan.IsVisible(index);
            _physicsToolbarControls[index].IsVisible = visible;
            if (visible)
            {
                continue;
            }

            overflowItems.Add(ViewerToolbarOverflowMenu.CreateItem(
                _physicsToolbarControls[index],
                _physicsToolbarItems[index].Label,
                index,
                OnPhysicsOverflowItemClick,
                () => PhysicsOverflowButton.Flyout?.Hide()));
        }

        if (_physicsOverflowFlyout is not null)
        {
            _physicsOverflowFlyout.ItemsSource = overflowItems;
        }

        PhysicsOverflowButton.IsVisible = overflowItems.Count != 0;
    }

    private void OnPhysicsOverflowItemClick(object? sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (sender is not MenuItem { Tag: int index } ||
            index < 0 ||
            index >= _physicsToolbarControls.Length)
        {
            return;
        }

        switch (_physicsToolbarControls[index])
        {
            case CheckBox checkBox:
                checkBox.IsChecked = checkBox.IsChecked != true;
                break;
            case Button button:
                button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                break;
            default:
                PhysicsOverflowButton.Flyout?.Hide();
                break;
        }
    }

    private void AttachPhysics(ViewerRenderCoordinator coordinator)
    {
        ArgumentNullException.ThrowIfNull(coordinator);
        PhysicsEnableButton.IsEnabled = true;
        _physicsBackendGeneration = -1;
        RenderPhysicsState(ViewerPhysicsStatusSnapshot.Disabled);
    }

    private async Task DetachPhysicsAsync()
    {
        DispatcherTimer? pump = _physicsPump;
        _physicsPump = null;
        pump?.Stop();

        // A bake that is still running belongs to the document being closed, so it is canceled and
        // its rollback runs before the transport it authors through is disposed.
        CancelRunningPhysicsBake();
        ViewerPhysicsController? physics = _physics;
        _physics = null;

        // Every snapshot already posted for this document is now stale, and the version bump is
        // what stops one of them repainting the toolbar of whatever document is opened next.
        _physicsSessionVersion++;
        if (physics is not null)
        {
            if (_physicsStatusHandler is { } handler)
            {
                physics.StatusChanged -= handler;
            }

            // Clearing before disposal is what restores the authored transforms: the render loop
            // is gone after this point, so the backend would otherwise keep the last simulated
            // pose until something else redrew it.
            ClearPhysicsOverridesOnActiveBackend();
            await physics.DisposeAsync();
        }

        _physicsStatusHandler = null;
        _physicsBackendGeneration = -1;
        _physicsInspectorObjectRevision = ulong.MaxValue;
        _physicsScrubbing = false;
        _physicsRenderFaulted = false;
        _physicsInspectorStale = false;
        PhysicsEnableButton.IsEnabled = false;
        ResetPhysicsAuthoringUi();
        HidePhysicsBakeProgress();
        RenderPhysicsState(ViewerPhysicsStatusSnapshot.Disabled);
    }

    /// <summary>Cancels a bake that is still running.</summary>
    private void CancelRunningPhysicsBake() => _physicsBakeLifetime.Cancel();

    private void ClearPhysicsOverridesOnActiveBackend()
    {
        if (_coordinator?.CapturePhysicsOverrideTarget(out _) is { } target)
        {
            target.ClearPhysicsOverrides();
        }
    }

    private async void OnPhysicsEnableClick(object? sender, RoutedEventArgs e)
    {
        _ = sender;
        e.Handled = true;
        try
        {
            if (_physics is { } existing)
            {
                // Rebuilding is the user's way of saying "try again", so a viewport that stopped
                // applying overrides after a render fault is given another chance here.
                if (_physicsRenderFaulted)
                {
                    _physicsRenderFaulted = false;
                    existing.ResetBridgeFailure();
                }

                await existing.RebuildAsync(_documentLifetime?.Token ?? default);
                RenderPhysicsState(existing.Snapshot);
                return;
            }

            if (_coordinator is not { } coordinator)
            {
                return;
            }

            var controller = new ViewerPhysicsController(
                new ViewerPhysicsTransportFactory(coordinator.Scheduler),
                ViewerPhysicsStopwatchClock.Instance,
                ViewerPhysicsRenderCapacities.Default,
                authoring: new ViewerPhysicsSchedulerAuthoringStage(coordinator.Scheduler));
            int version = _physicsSessionVersion;
            void handler(ViewerPhysicsStatusSnapshot snapshot) =>
                OnPhysicsStatusChanged(controller, version, snapshot);
            _physicsStatusHandler = handler;
            controller.StatusChanged += handler;
            _physics = controller;
            StartPhysicsPump();
            await controller.EnableAsync(_documentLifetime?.Token ?? default);
            RenderPhysicsState(controller.Snapshot);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            ShowError($"Physics could not be enabled: {ViewerPackageErrorFormatter.Format(exception)}");
        }
    }

    private void StartPhysicsPump()
    {
        _physicsPump?.Stop();
        var pump = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(8),
        };
        pump.Tick += OnPhysicsPumpTick;
        pump.Start();
        _physicsPump = pump;
    }

    private async void OnPhysicsPumpTick(object? sender, EventArgs e)
    {
        _ = sender;
        _ = e;
        if (_physics is not { } physics)
        {
            return;
        }

        try
        {
            await physics.PumpAsync(_documentLifetime?.Token ?? default);
            await SubmitPhysicsDriveInputsAsync(physics);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            // Nothing above this handler can catch: an escaping exception from a timer callback
            // ends the process, so a failing pump stops pacing and is reported instead.
            _physicsPump?.Stop();
            ShowError(
                "Interactive physics was stopped after the pump failed: " +
                ViewerPackageErrorFormatter.Format(exception));
        }
    }

    /// <summary>
    /// Repaints the physics UI from a snapshot, ignoring snapshots that outlived their document.
    /// </summary>
    /// <param name="owner">The controller that produced the snapshot.</param>
    /// <param name="version">The document session the controller belonged to.</param>
    /// <param name="snapshot">The state to render.</param>
    private void OnPhysicsStatusChanged(
        ViewerPhysicsController owner,
        int version,
        ViewerPhysicsStatusSnapshot snapshot)
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            if (IsCurrentPhysicsSession(owner, version))
            {
                RenderPhysicsState(snapshot);
            }

            return;
        }

        Dispatcher.UIThread.Post(
            () =>
            {
                if (IsCurrentPhysicsSession(owner, version))
                {
                    RenderPhysicsState(snapshot);
                }
            },
            DispatcherPriority.Background);
    }

    private bool IsCurrentPhysicsSession(ViewerPhysicsController owner, int version) =>
        ReferenceEquals(_physics, owner) && version == _physicsSessionVersion;

    private void OnPhysicsPlayPauseClick(object? sender, RoutedEventArgs e)
    {
        _ = sender;
        e.Handled = true;
        if (_physics is not { } physics)
        {
            return;
        }

        if (physics.IsPlaying)
        {
            physics.Pause();
        }
        else
        {
            physics.Play();
        }

        RenderPhysicsState(physics.Snapshot);
    }

    private async void OnPhysicsStopClick(object? sender, RoutedEventArgs e)
    {
        _ = sender;
        e.Handled = true;
        if (_physics is not { } physics)
        {
            return;
        }

        try
        {
            await physics.StopAsync(_documentLifetime?.Token ?? default);
            RenderPhysicsState(physics.Snapshot);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            ReportPhysicsHandlerFailure("stopped", exception);
        }
    }

    private async void OnPhysicsStepClick(object? sender, RoutedEventArgs e)
    {
        _ = sender;
        e.Handled = true;
        if (_physics is not { } physics)
        {
            return;
        }

        try
        {
            await physics.StepOneFrameAsync(_documentLifetime?.Token ?? default);
            RenderPhysicsState(physics.Snapshot);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            ReportPhysicsHandlerFailure("stepped", exception);
        }
    }

    private async void OnPhysicsLoopChanged(object? sender, RoutedEventArgs e)
    {
        _ = sender;
        if (_updatingPhysicsUi || _physics is not { } physics)
        {
            return;
        }

        try
        {
            await physics.SetLoopAsync(
                PhysicsLoopCheckBox.IsChecked == true,
                _documentLifetime?.Token ?? default);
            RenderPhysicsState(physics.Snapshot);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            ReportPhysicsHandlerFailure("looped", exception);
        }
    }

    private void OnPhysicsSpeedChanged(object? sender, SelectionChangedEventArgs e)
    {
        _ = sender;
        _ = e;
        if (_updatingPhysicsUi || _physics is not { } physics)
        {
            return;
        }

        if (PhysicsSpeedSelector.SelectedItem is ComboBoxItem { Tag: string tag } &&
            double.TryParse(
                tag,
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out double speed))
        {
            physics.SetSpeed(speed);
            RenderPhysicsState(physics.Snapshot);
        }
    }

    private async void OnPhysicsPreviewChanged(object? sender, RoutedEventArgs e)
    {
        _ = sender;
        if (_updatingPhysicsUi || _physics is not { } physics)
        {
            return;
        }

        try
        {
            string message = await physics.SetPreviewAsync(
                PhysicsPreviewCheckBox.IsChecked == true,
                _documentLifetime?.Token ?? default);
            if (message.Length != 0)
            {
                SetReady(message);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            // A preview that did not complete must not look like one that did: the checkbox is
            // repainted from the controller, which reports the preview as off.
            ShowError(
                "The physics preview was not applied: " +
                ViewerPackageErrorFormatter.Format(exception));
        }

        RenderPhysicsState(physics.Snapshot);
    }

    private void OnPhysicsScrubPressed(object? sender, PointerPressedEventArgs e)
    {
        _ = sender;
        _ = e;
        _physicsScrubbing = true;
    }

    private async void OnPhysicsScrubReleased(object? sender, PointerReleasedEventArgs e)
    {
        _ = sender;
        _ = e;
        _physicsScrubbing = false;
        if (_updatingPhysicsUi || _physics is not { } physics)
        {
            return;
        }

        try
        {
            await physics.SeekAsync(PhysicsScrubber.Value, _documentLifetime?.Token ?? default);
            RenderPhysicsState(physics.Snapshot);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            ReportPhysicsHandlerFailure("scrubbed", exception);
        }
    }

    private void ReportPhysicsHandlerFailure(string action, Exception exception) =>
        ShowError(
            $"The simulation could not be {action}: " +
            ViewerPackageErrorFormatter.Format(exception));

    private async void OnPhysicsBakeClick(object? sender, RoutedEventArgs e)
    {
        _ = sender;
        e.Handled = true;
        if (_physicsBakeBusy || _physics is not { } physics)
        {
            return;
        }

        try
        {
            ViewerPhysicsStatusSnapshot snapshot = physics.Snapshot;
            var dialog = new PhysicsBakeWindow(snapshot.StartTimeCode, snapshot.EndTimeCode);
            ViewerPhysicsBakeRequest? request =
                await dialog.ShowDialog<ViewerPhysicsBakeRequest?>(this);
            if (request is null)
            {
                return;
            }

            // The bake owns its own cancellation, linked to the document so closing still stops it.
            // Cancelling must not go through the transport command gate: the gate is held by the
            // bake itself, so a cancel that waited for it could never run.
            using ViewerPhysicsBakeLease lifetime = _physicsBakeLifetime.Begin(
                _documentLifetime?.Token ?? default);
            _physicsBakeBusy = true;
            ShowPhysicsBakeProgress(0, 1);
            PhysicsBakeButton.IsEnabled = false;
            try
            {
                var progress = new Progress<ViewerPhysicsBakeProgress>(value =>
                {
                    SetBusy(value.Describe());
                    ShowPhysicsBakeProgress(value.CompletedSamples, value.TotalSamples);
                });
                ViewerPhysicsBakeOutcome outcome = await physics.BakeAsync(
                    request,
                    progress,
                    lifetime.Token);
                if (outcome.Succeeded)
                {
                    SetReady(outcome.Message);
                }
                else
                {
                    ShowError(outcome.Message);
                }
            }
            catch (OperationCanceledException) when (lifetime.IsCancellationRequested)
            {
                SetReady("The physics bake was canceled and everything it authored was rolled back.");
            }
            finally
            {
                _physicsBakeBusy = false;
                HidePhysicsBakeProgress();
                RenderPhysicsState(physics.Snapshot);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            ReportPhysicsHandlerFailure("baked", exception);
        }
    }

    private void OnPhysicsBakeCancelClick(object? sender, RoutedEventArgs e)
    {
        _ = sender;
        e.Handled = true;
        if (!_physicsBakeLifetime.Cancel())
        {
            // The bake finished on its own between the click and the cancel, which is the outcome
            // the click was asking for.
            return;
        }

        PhysicsBakeCancelButton.IsEnabled = false;
        SetBusy("Canceling the physics bake...");
    }

    private void ShowPhysicsBakeProgress(int completed, int total)
    {
        PhysicsBakeProgressBar.Maximum = Math.Max(total, 1);
        PhysicsBakeProgressBar.Value = Math.Clamp(completed, 0, Math.Max(total, 1));
        PhysicsBakeCancelButton.IsEnabled = true;
        PhysicsBakeProgressPanel.IsVisible = true;
    }

    private void HidePhysicsBakeProgress()
    {
        PhysicsBakeProgressPanel.IsVisible = false;
        PhysicsBakeCancelButton.IsEnabled = false;
        PhysicsBakeProgressBar.Value = 0;
    }

    /// <summary>
    /// Reports whether a key press would run a physics shortcut, without running it.
    /// </summary>
    /// <remarks>
    /// The repeat path needs to know whether to swallow the event, which is exactly the question
    /// the classifier answers. Running the switch again would run the command again, which is the
    /// thing the repeat guard exists to prevent.
    /// </remarks>
    private bool IsPhysicsShortcutCandidate(KeyEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        return _physics is not null &&
            ViewerPhysicsShortcutPolicy.Classify(
                e.Key,
                e.KeyModifiers,
                IsCameraShortcutEditing()) != ViewerPhysicsShortcut.None;
    }

    private bool TryHandlePhysicsShortcut(KeyEventArgs e)
    {
        ViewerPhysicsShortcut shortcut = ViewerPhysicsShortcutPolicy.Classify(
            e.Key,
            e.KeyModifiers,
            IsCameraShortcutEditing());
        if (shortcut == ViewerPhysicsShortcut.None || _physics is null)
        {
            return false;
        }

        switch (shortcut)
        {
            case ViewerPhysicsShortcut.PlayPause when PhysicsPlayPauseButton.IsEnabled:
                OnPhysicsPlayPauseClick(PhysicsPlayPauseButton, new RoutedEventArgs());
                return true;
            case ViewerPhysicsShortcut.Stop when PhysicsStopButton.IsEnabled:
                OnPhysicsStopClick(PhysicsStopButton, new RoutedEventArgs());
                return true;
            case ViewerPhysicsShortcut.StepOneFrame when PhysicsStepButton.IsEnabled:
                OnPhysicsStepClick(PhysicsStepButton, new RoutedEventArgs());
                return true;
            case ViewerPhysicsShortcut.Bake when PhysicsBakeButton.IsEnabled:
                OnPhysicsBakeClick(PhysicsBakeButton, new RoutedEventArgs());
                return true;
            case ViewerPhysicsShortcut.GizmoNone when PhysicsGizmoSelector.IsEnabled:
                SelectPhysicsGizmo(ViewerGizmoMode.None);
                return true;
            case ViewerPhysicsShortcut.GizmoTranslate when PhysicsGizmoSelector.IsEnabled:
                SelectPhysicsGizmo(ViewerGizmoMode.Translate);
                return true;
            case ViewerPhysicsShortcut.GizmoRotate when PhysicsGizmoSelector.IsEnabled:
                SelectPhysicsGizmo(ViewerGizmoMode.Rotate);
                return true;
            case ViewerPhysicsShortcut.GizmoScale when PhysicsGizmoSelector.IsEnabled:
                SelectPhysicsGizmo(ViewerGizmoMode.Scale);
                return true;
            case ViewerPhysicsShortcut.GizmoDrag when PhysicsGizmoSelector.IsEnabled:
                SelectPhysicsGizmo(ViewerGizmoMode.Drag);
                return true;
            case ViewerPhysicsShortcut.ToggleSnap when PhysicsSnapCheckBox.IsEnabled:
                PhysicsSnapCheckBox.IsChecked = PhysicsSnapCheckBox.IsChecked != true;
                return true;
            case ViewerPhysicsShortcut.Undo when PhysicsUndoButton.IsEnabled:
                OnPhysicsUndoClick(PhysicsUndoButton, new RoutedEventArgs());
                return true;
            case ViewerPhysicsShortcut.Redo when PhysicsRedoButton.IsEnabled:
                OnPhysicsRedoClick(PhysicsRedoButton, new RoutedEventArgs());
                return true;
            default:
                return false;
        }
    }

    private void NotifyPhysicsStageChanged(UsdStageChange change)
    {
        if (_physics is not { } physics)
        {
            return;
        }

        // UsdStageChange reports how much derived state a batch of edits invalidated, not which
        // fields moved, and any authored property can be a simulation input - a mass, a collider
        // extent, a joint limit. So every observed edit is treated as physics-relevant: the
        // debouncer collapses bursts, and rebuilding a world that did not need it only costs time,
        // whereas simulating a stage the file no longer describes silently shows a lie.
        // ViewerPhysicsEditClassifier does the finer field-level split for the callers that do know
        // the field names. The serial pair identifies the change exactly, which is how the
        // controller recognises the one edit its own preview authored.
        physics.NotifyStageChanged(
            ViewerPhysicsEditKind.Relevant,
            new ViewerPhysicsStageEdit(change.BeforeChangeSerial, change.AfterChangeSerial));
    }

    /// <summary>
    /// Consumes the newest simulated frame and applies one bounded override batch.
    /// </summary>
    /// <remarks>
    /// Called once per rendered frame. It never waits for the worker: if no new frame is ready the
    /// previous pose is simply redrawn, which is what dropping a simulation frame has to look like
    /// in a viewer that must keep its camera responsive.
    /// </remarks>
    private void PumpPhysicsRenderFrame()
    {
        if (_physicsRenderFaulted || _physics is not { } physics || _coordinator is not { } coordinator)
        {
            return;
        }

        try
        {
            IViewerPhysicsOverrideTarget? target =
                coordinator.CapturePhysicsOverrideTarget(out long generation);
            if (target is null)
            {
                _physicsBackendGeneration = -1;
                return;
            }

            if (generation != _physicsBackendGeneration)
            {
                // A new backend, or one that recovered from context loss, retains nothing. Replaying
                // the latest complete batch is what stops a recovered viewport from snapping back to
                // the authored pose until the next simulated frame arrives.
                _physicsBackendGeneration = generation;
                physics.RequestOverrideReplay();
            }

            _ = physics.PumpRenderFrame(
                Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency,
                target);
        }
        catch (Exception exception)
        {
            // This runs inside the render loop. An escaping exception would tear down the loop for
            // the whole document, so a viewer that fails to apply simulated poses stops applying
            // them and keeps drawing the stage.
            _physicsRenderFaulted = true;
            _physicsBackendGeneration = -1;
            TryClearFaultedPhysicsOverrides(physics, coordinator, exception);
            ShowError(
                "Simulated poses are no longer applied to this viewport: " +
                ViewerPackageErrorFormatter.Format(exception));
            RenderPhysicsState(physics.Snapshot);
        }
    }

    private static void TryClearFaultedPhysicsOverrides(
        ViewerPhysicsController physics,
        ViewerRenderCoordinator coordinator,
        Exception exception)
    {
        try
        {
            physics.DisableRenderBridge(
                "Simulated poses are no longer applied to this viewport: " + exception.Message);
            IViewerPhysicsOverrideTarget? target =
                coordinator.CapturePhysicsOverrideTarget(out _);
            target?.ClearPhysicsOverrides();
        }
        catch (Exception)
        {
            // The bridge is already being torn down; a backend that cannot even clear has nothing
            // further to contribute and must not turn one render fault into two.
        }
    }

    private void RenderPhysicsState(ViewerPhysicsStatusSnapshot snapshot)
    {
        _updatingPhysicsUi = true;
        bool layoutChanged;
        try
        {
            PhysicsStatus.Text = ViewerPhysicsStatusFormatter.FormatStatus(in snapshot);
            layoutChanged = SetPhysicsContent(
                PhysicsEnableButton,
                snapshot.IsEnabled ? "_Rebuild" : "_Physics");
            layoutChanged |= SetPhysicsEnabled(
                PhysicsEnableButton,
                _coordinator is not null && !snapshot.IsBusy);
            layoutChanged |= SetPhysicsContent(
                PhysicsPlayPauseButton,
                snapshot.IsPlaying ? "Pause" : "Play");
            AutomationProperties.SetName(
                PhysicsPlayPauseButton,
                ViewerPhysicsStatusFormatter.FormatPlayPauseName(snapshot.IsPlaying));
            layoutChanged |= SetPhysicsCommandState(
                PhysicsPlayPauseButton,
                snapshot,
                snapshot.IsPlaying ? ViewerPhysicsCommand.Pause : ViewerPhysicsCommand.Play,
                "Play or pause the interactive simulation (K)");
            layoutChanged |= SetPhysicsCommandState(
                PhysicsStopButton,
                snapshot,
                ViewerPhysicsCommand.Stop,
                "Return the simulation to the authored start (J)");
            layoutChanged |= SetPhysicsCommandState(
                PhysicsStepButton,
                snapshot,
                ViewerPhysicsCommand.StepOneFrame,
                "Advance exactly one fixed simulation step (N)");
            layoutChanged |= SetPhysicsCommandState(
                PhysicsBakeButton,
                snapshot,
                ViewerPhysicsCommand.Bake,
                "Write simulated poses into a file-backed layer (B)");
            layoutChanged |= SetPhysicsEnabled(PhysicsLoopCheckBox, snapshot.IsEnabled);
            PhysicsLoopCheckBox.IsChecked = snapshot.Loop;
            layoutChanged |= SetPhysicsEnabled(PhysicsSpeedSelector, snapshot.IsEnabled);
            layoutChanged |= SetPhysicsEnabled(
                PhysicsPreviewCheckBox,
                snapshot.IsEnabled && !snapshot.IsBusy);
            PhysicsPreviewCheckBox.IsChecked = snapshot.PreviewEnabled;
            PhysicsScrubber.IsEnabled = snapshot.IsEnabled && !snapshot.IsBusy;
            PhysicsScrubber.Minimum = snapshot.StartTimeCode;
            PhysicsScrubber.Maximum = Math.Max(snapshot.EndTimeCode, snapshot.StartTimeCode + 1e-6);
            if (!_physicsScrubbing)
            {
                // While the pointer holds the thumb the user owns the value. Writing the simulated
                // time back into it would drag the thumb out from under the pointer every frame.
                PhysicsScrubber.Value = Math.Clamp(
                    snapshot.Status.TimeCode,
                    PhysicsScrubber.Minimum,
                    PhysicsScrubber.Maximum);
            }

            RenderPhysicsInspector(snapshot);
            RenderPhysicsAuthoringState(in snapshot);
        }
        finally
        {
            _updatingPhysicsUi = false;
        }

        if (layoutChanged && _physicsPlannedWidth >= 0)
        {
            // Only a change that can move the toolbar layout is worth re-planning for. Replanning
            // on every pumped step would rebuild the overflow menu about a hundred times a second.
            double width = _physicsPlannedWidth;
            _physicsPlannedWidth = -1;
            ApplyPhysicsToolbarOverflow(width);
        }
    }

    private static bool SetPhysicsContent(ContentControl control, string content) =>
        ViewerToolbarState.SetContent(control, content);

    private static bool SetPhysicsEnabled(Control control, bool enabled) =>
        ViewerToolbarState.SetEnabled(control, enabled);

    private static bool SetPhysicsCommandState(
        Button button,
        in ViewerPhysicsStatusSnapshot snapshot,
        ViewerPhysicsCommand command,
        string readyTip)
    {
        bool available = snapshot.CanRun(command);
        bool changed = SetPhysicsEnabled(button, available);
        ToolTip.SetTip(
            button,
            available ? readyTip : snapshot.DescribeUnavailable(command));
        return changed;
    }

    private void RenderPhysicsInspector(in ViewerPhysicsStatusSnapshot snapshot)
    {
        if (!snapshot.IsEnabled)
        {
            SetInspectorText(
                PhysicsInspectorState,
                "Enable physics for this stage to inspect its simulation.");
            BindInspectorRows(PhysicsCapabilityRows, null);
            BindInspectorRows(PhysicsDiagnosticRows, null);
            BindInspectorRows(PhysicsObjectRows, null);
            _physicsInspectorObjectRevision = ulong.MaxValue;
            _physicsInspectorStale = false;
            return;
        }

        if (_physics is not { } physics)
        {
            SetInspectorText(
                PhysicsInspectorState,
                ViewerPhysicsStatusFormatter.FormatStatus(in snapshot));
            return;
        }

        string state = snapshot.Error.Length != 0
            ? snapshot.Error
            : ViewerPhysicsStatusFormatter.FormatStatus(in snapshot);
        SetInspectorText(
            PhysicsInspectorState,
            _physicsRenderFaulted
                ? state + " Simulated poses are not applied to this viewport."
                : state + " " + physics.Bindings.Describe());

        if (!PhysicsTab.IsSelected)
        {
            // A hidden tab is not worth rebinding for. The rows are recomputed the moment the tab
            // becomes visible again, so nothing is lost by skipping them while nobody can see them.
            _physicsInspectorStale = true;
            return;
        }

        _physicsInspectorStale = false;
        BindInspectorRows(PhysicsCapabilityRows, physics.Capabilities);
        BindInspectorRows(PhysicsDiagnosticRows, physics.Diagnostics);
        if (_physicsInspectorObjectRevision != physics.BindingRevision)
        {
            // The object rows only change when the bindings are rebuilt, so they are keyed by the
            // binding revision instead of being rebound on every paced step.
            _physicsInspectorObjectRevision = physics.BindingRevision;
            PhysicsObjectRows.ItemsSource = physics.Objects;
        }
    }

    private static void SetInspectorText(TextBlock block, string text)
    {
        if (!string.Equals(block.Text, text, StringComparison.Ordinal))
        {
            block.Text = text;
        }
    }

    private static void BindInspectorRows(ItemsControl control, System.Collections.IEnumerable? rows)
    {
        if (!ReferenceEquals(control.ItemsSource, rows))
        {
            control.ItemsSource = rows;
        }
    }
}
