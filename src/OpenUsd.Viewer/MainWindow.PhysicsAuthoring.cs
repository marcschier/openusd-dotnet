// Copyright (c) marcschier. Licensed under the MIT License.

using System.Globalization;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace OpenUsd.Viewer;

/// <summary>
/// Wires the physics authoring surface - the property inspector, the undo history, the gizmo
/// selection, and the interactive drive controls - into the viewer window.
/// </summary>
/// <remarks>
/// <para>
/// Nothing here simulates or authors on its own. Every property edit becomes one
/// <see cref="ViewerPhysicsEditStep"/> the controller applies through the stage scheduler, and every
/// drive input becomes one batched runtime command the physics worker stages. The UI thread only
/// reads state and issues requests.
/// </para>
/// <para>
/// Every control is capability gated from the built world's own capability matrix rather than from
/// what the schema declares. A vehicle slider that moved while the world cannot simulate vehicles
/// would be telling the user their input reached a drivetrain that does not exist.
/// </para>
/// </remarks>
public sealed partial class MainWindow
{
    private IReadOnlyList<ViewerPhysicsObjectSection> _physicsSections = [];
    private ViewerPhysicsObjectSection? _physicsSelectedSection;
    private ViewerPhysicsPropertyRow? _physicsSelectedProperty;
    private readonly ViewerPhysicsDragSession _physicsDrag = new();
    private readonly ViewerPhysicsControllerKeyState _physicsControllerKeys = new();
    private ViewerGizmoSnapSettings _physicsSnap = ViewerGizmoSnapSettings.Default;
    private ViewerGizmoMode _physicsGizmoMode = ViewerGizmoMode.None;
    private ulong _physicsSectionRevision = ulong.MaxValue;
    private bool _updatingPhysicsAuthoringUi;
    private bool _physicsAuthoringBusy;

    /// <summary>
    /// How far along the pointer ray a body drag grabs when nothing was picked at a depth.
    /// </summary>
    /// <remarks>
    /// The pointer ray is normalized, so the grab depth is a distance in stage linear units. It is
    /// bounded rather than unbounded because a drag that grabbed at the far clip plane would move
    /// the body across the whole scene for one pixel of pointer movement.
    /// </remarks>
    private const double PhysicsDragGrabDistance = 10d;

    private void InitializePhysicsAuthoringUi()
    {
        PhysicsGizmoSelector.SelectionChanged += OnPhysicsGizmoChanged;
        PhysicsSnapCheckBox.IsCheckedChanged += OnPhysicsSnapChanged;
        PhysicsUndoButton.Click += OnPhysicsUndoClick;
        PhysicsRedoButton.Click += OnPhysicsRedoClick;
        PhysicsRefreshPropertiesButton.Click += OnPhysicsRefreshPropertiesClick;
        PhysicsObjectSelector.SelectionChanged += OnPhysicsObjectSelectionChanged;
        PhysicsPropertyList.SelectionChanged += OnPhysicsPropertySelectionChanged;
        PhysicsApplyPropertyButton.Click += OnPhysicsApplyPropertyClick;
        PhysicsClearPropertyButton.Click += OnPhysicsClearPropertyClick;
        PhysicsApplyForceButton.Click += OnPhysicsApplyForceClick;
        PhysicsApplyImpulseButton.Click += OnPhysicsApplyImpulseClick;
        PhysicsApplyTorqueButton.Click += OnPhysicsApplyTorqueClick;
        PhysicsWakeButton.Click += OnPhysicsWakeClick;
        PhysicsSleepButton.Click += OnPhysicsSleepClick;
        PhysicsVehicleDriveCheckBox.IsCheckedChanged += OnPhysicsVehicleDriveChanged;
        PhysicsControllerDriveCheckBox.IsCheckedChanged += OnPhysicsControllerDriveChanged;
        ViewportHost.AddHandler(
            PointerPressedEvent,
            OnPhysicsViewportPointerPressed,
            RoutingStrategies.Tunnel,
            handledEventsToo: false);
        ViewportHost.AddHandler(
            PointerMovedEvent,
            OnPhysicsViewportPointerMoved,
            RoutingStrategies.Tunnel,
            handledEventsToo: false);
        ViewportHost.AddHandler(
            PointerReleasedEvent,
            OnPhysicsViewportPointerReleased,
            RoutingStrategies.Tunnel | RoutingStrategies.Bubble,
            handledEventsToo: true);
        ViewportHost.AddHandler(
            PointerCaptureLostEvent,
            OnPhysicsViewportPointerCaptureLost,
            RoutingStrategies.Tunnel | RoutingStrategies.Bubble,
            handledEventsToo: true);
    }

    /// <summary>
    /// Starts an interactive body drag when the drag gizmo owns the pointer.
    /// </summary>
    /// <remarks>
    /// Camera navigation is untouched unless the drag gizmo is selected and a simulated identity is
    /// actually resolved. The handler only marks the event handled once it has taken ownership, so
    /// every existing orbit, pan, dolly, and pick gesture behaves exactly as it did before.
    /// </remarks>
    private void OnPhysicsViewportPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        _ = sender;
        if (_physicsGizmoMode != ViewerGizmoMode.Drag ||
            _physics is null ||
            e.KeyModifiers != KeyModifiers.None ||
            !e.GetCurrentPoint(ViewportHost).Properties.IsLeftButtonPressed ||
            _physicsSelectedSection is not { } section)
        {
            return;
        }

        ulong target = ViewerPhysicsController.ResolveCommandTarget(
            section,
            ViewerPhysicsCommandability.Body);
        if (target == 0UL || !TryBuildPhysicsPointerRay(e, out ViewerGizmoRay ray))
        {
            return;
        }

        // The grab depth is the distance from the eye to the gizmo pivot along the pointer ray, so
        // the body follows the pointer at the depth it was grabbed at rather than rushing at the
        // camera.
        double depth = Math.Max(ray.Direction.Length, 1e-3d) * PhysicsDragGrabDistance;
        if (!_physicsDrag.TryBegin(e.Pointer.Id, target, ViewerPhysicsVector3.Zero, depth))
        {
            return;
        }

        // Capturing is what makes the release reach the viewer even when the pointer leaves the
        // viewport. Without it a drag that ended outside the window would never be told to stop,
        // and the staged force would be applied once more by the next step.
        e.Pointer.Capture(ViewportHost);
        e.Handled = true;
        SetReady(ViewerGizmoGeometry.Describe(
            ViewerGizmoMode.Drag,
            ViewerGizmoAxis.None,
            ViewerGizmoSpace.World,
            depth));
    }

    /// <summary>Ends any active body drag, for a document that is going away.</summary>
    private void CancelPhysicsDrag() =>
        EndPhysicsDrag(ViewerPhysicsDragEnd.Abandoned, pointer: null);

    /// <summary>
    /// The single path every body drag ends through, whatever ended it.
    /// </summary>
    /// <remarks>
    /// A release and a capture loss can both arrive for the same gesture, and a deactivation can
    /// arrive before either. Routing all of them through one idempotent end keeps the clear at
    /// exactly one per drag: the runtime applies whatever force is staged before the next
    /// sub-steps, so a missing clear pushes the body once more and a duplicate clear is one more
    /// command for the world to refuse.
    /// </remarks>
    /// <param name="reason">Why the drag ended.</param>
    /// <param name="pointer">The pointer whose capture must be given back, when there is one.</param>
    private void EndPhysicsDrag(ViewerPhysicsDragEnd reason, IPointer? pointer)
    {
        if (!_physicsDrag.TryEnd(reason, out ViewerPhysicsRuntimeCommand clear))
        {
            return;
        }

        // A capture that was already taken away must not be released again, so only the paths that
        // still hold it pass a pointer.
        pointer?.Capture(null);
        if (_physics is not { } physics)
        {
            return;
        }

        _ = SubmitPhysicsDragClearAsync(physics, clear);
    }

    private async Task SubmitPhysicsDragClearAsync(
        ViewerPhysicsController physics,
        ViewerPhysicsRuntimeCommand clear)
    {
        try
        {
            await SubmitPhysicsCommandsAsync(physics, [clear]);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            ShowError(
                "The interactive drag could not be cleared: " +
                ViewerPackageErrorFormatter.Format(exception));
        }
    }

    private async void OnPhysicsViewportPointerMoved(object? sender, PointerEventArgs e)
    {
        _ = sender;
        if (!_physicsDrag.Owns(e.Pointer.Id) || _physics is not { } physics)
        {
            return;
        }

        try
        {
            bool leftDown = e.GetCurrentPoint(ViewportHost).Properties.IsLeftButtonPressed;
            if (!TryBuildPhysicsPointerRay(e, out ViewerGizmoRay ray))
            {
                if (!leftDown)
                {
                    EndPhysicsDrag(ViewerPhysicsDragEnd.Released, e.Pointer);
                }

                return;
            }

            _ = physics.TryReadSimulatedPosition(
                _physicsDrag.TargetId,
                out ViewerPhysicsVector3 grabPoint);
            ViewerPhysicsDragStep step = _physicsDrag.Step(
                e.Pointer.Id,
                leftDown,
                grabPoint,
                ray,
                PhysicsControllerStepSeconds,
                out ViewerPhysicsRuntimeCommand command);
            if (step == ViewerPhysicsDragStep.MustEnd)
            {
                // The release was delivered somewhere the viewer never saw it. Ending here stops
                // the force and stops marking every later move handled, which would otherwise
                // suppress hover across the whole viewport.
                EndPhysicsDrag(ViewerPhysicsDragEnd.Released, e.Pointer);
                return;
            }

            if (step == ViewerPhysicsDragStep.Ignored)
            {
                return;
            }

            e.Handled = true;
            if (step == ViewerPhysicsDragStep.Applied)
            {
                await SubmitPhysicsCommandsAsync(physics, [command]);
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // Nothing above an async pointer handler can catch, so a failed drag step ends the
            // drag and is reported rather than ending the process.
            EndPhysicsDrag(ViewerPhysicsDragEnd.Abandoned, e.Pointer);
            ShowError(
                "The interactive drag was stopped: " +
                ViewerPackageErrorFormatter.Format(exception));
        }
    }

    private void OnPhysicsViewportPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        _ = sender;
        if (!_physicsDrag.Owns(e.Pointer.Id))
        {
            return;
        }

        e.Handled = true;
        EndPhysicsDrag(ViewerPhysicsDragEnd.Released, e.Pointer);
    }

    private void OnPhysicsViewportPointerCaptureLost(
        object? sender,
        PointerCaptureLostEventArgs e)
    {
        _ = sender;
        if (!_physicsDrag.Owns(e.Pointer.Id))
        {
            return;
        }

        // The capture is already gone, so there is nothing to give back.
        EndPhysicsDrag(ViewerPhysicsDragEnd.CaptureLost, pointer: null);
    }

    private bool TryBuildPhysicsPointerRay(PointerEventArgs e, out ViewerGizmoRay ray)
    {
        Avalonia.Point position = e.GetPosition(ViewportHost);
        return ViewerPhysicsPointerRay.TryBuild(
            _cameraNavigation.State,
            ViewportHost.Bounds.Width,
            ViewportHost.Bounds.Height,
            position.X,
            position.Y,
            out ray);
    }

    /// <summary>Gets the gizmo the viewport drag currently manipulates.</summary>
    internal ViewerGizmoMode PhysicsGizmoMode => _physicsGizmoMode;

    /// <summary>Gets the increments a gizmo drag quantizes to.</summary>
    internal ViewerGizmoSnapSettings PhysicsSnapSettings => _physicsSnap;

    private void OnPhysicsGizmoChanged(object? sender, SelectionChangedEventArgs e)
    {
        _ = sender;
        _ = e;
        if (_updatingPhysicsAuthoringUi)
        {
            return;
        }

        _physicsGizmoMode = ReadTag(PhysicsGizmoSelector, ViewerGizmoMode.None);
        SetReady(ViewerGizmoGeometry.Describe(
            _physicsGizmoMode,
            ViewerGizmoAxis.None,
            ViewerGizmoSpace.World,
            0d));
        SyncPhysicsGizmoMenu();
    }

    /// <summary>
    /// Reflects <see cref="PhysicsGizmoSelector"/>'s current selection into the Physics &gt;
    /// Gizmo menu's radio items, so the menu never shows a stale checked gizmo after the
    /// selector changes it directly (for example from the Q/G/E/R/H shortcuts).
    /// </summary>
    private void SyncPhysicsGizmoMenu()
    {
        MenuItem[] items =
        [
            PhysicsGizmoNoneMenuItem,
            PhysicsGizmoMoveMenuItem,
            PhysicsGizmoRotateMenuItem,
            PhysicsGizmoScaleMenuItem,
            PhysicsGizmoDragMenuItem,
        ];
        for (int index = 0; index < items.Length; index++)
        {
            items[index].IsEnabled = PhysicsGizmoSelector.IsEnabled;
            items[index].IsChecked = PhysicsGizmoSelector.SelectedIndex == index;
        }
    }

    private void OnPhysicsSnapChanged(object? sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;
        if (_updatingPhysicsAuthoringUi)
        {
            return;
        }

        _physicsSnap = _physicsSnap with { IsEnabled = PhysicsSnapCheckBox.IsChecked == true };
    }

    private async void OnPhysicsUndoClick(object? sender, RoutedEventArgs e)
    {
        _ = sender;
        e.Handled = true;
        await RunPhysicsAuthoringAsync(
            physics => physics.UndoAsync(_documentLifetime?.Token ?? default));
    }

    private async void OnPhysicsRedoClick(object? sender, RoutedEventArgs e)
    {
        _ = sender;
        e.Handled = true;
        await RunPhysicsAuthoringAsync(
            physics => physics.RedoAsync(_documentLifetime?.Token ?? default));
    }

    private async void OnPhysicsRefreshPropertiesClick(object? sender, RoutedEventArgs e)
    {
        _ = sender;
        e.Handled = true;
        await ReloadPhysicsPropertiesAsync();
    }

    private async Task ReloadPhysicsPropertiesAsync()
    {
        if (_physics is not { } physics)
        {
            return;
        }

        // The selection is captured by identity before the reload. Authoring a property changes
        // what the extractor produces, so the fingerprint moves and the list is rebuilt; restoring
        // by position would move the operator back to the first object and silently retarget every
        // interaction that followed the edit.
        ViewerPhysicsSelectionAnchor anchor = CapturePhysicsSelection();
        try
        {
            _physicsSections = await physics.LoadInspectorAsync(
                _documentLifetime?.Token ?? default);
            if (_physicsSectionRevision == physics.InspectorRevision &&
                _physicsSelectedSection is not null)
            {
                // Nothing the list is built from moved, so the selection the operator is working
                // in is left exactly where it is rather than being reset under them.
                RenderPhysicsAuthoringState(physics.Snapshot);
                return;
            }

            _physicsSectionRevision = physics.InspectorRevision;
            RebuildPhysicsObjectSelector(anchor);
            RenderPhysicsAuthoringState(physics.Snapshot);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            ShowError(
                "The physics properties could not be read: " +
                ViewerPackageErrorFormatter.Format(exception));
        }
    }

    /// <summary>Captures the object and property the operator is working in.</summary>
    /// <returns>The anchor, which is empty when nothing is selected.</returns>
    internal ViewerPhysicsSelectionAnchor CapturePhysicsSelection() =>
        _physicsSelectedSection is not { } section
            ? ViewerPhysicsSelectionAnchor.None
            : ViewerPhysicsSelectionAnchor.For(
                section,
                _physicsSelectedProperty?.Name ?? string.Empty);

    private void RebuildPhysicsObjectSelector(ViewerPhysicsSelectionAnchor anchor)
    {
        _updatingPhysicsAuthoringUi = true;
        try
        {
            var items = new List<ComboBoxItem>(_physicsSections.Count);
            for (int index = 0; index < _physicsSections.Count; index++)
            {
                items.Add(new ComboBoxItem
                {
                    Content = _physicsSections[index].Header,
                    Tag = index,
                });
            }

            PhysicsObjectSelector.ItemsSource = items;
            int selected = ViewerPhysicsSelectionResolver.ResolveSection(_physicsSections, anchor);
            PhysicsObjectSelector.SelectedIndex = selected;
            _physicsSelectedSection = selected < 0 ? null : _physicsSections[selected];
            BindPhysicsPropertyList();
            RestorePhysicsPropertySelection(anchor);
        }
        finally
        {
            _updatingPhysicsAuthoringUi = false;
        }
    }

    private void RestorePhysicsPropertySelection(ViewerPhysicsSelectionAnchor anchor)
    {
        int row = ViewerPhysicsSelectionResolver.ResolveRow(_physicsSections, anchor);
        if (row < 0 || _physicsSelectedSection is not { } section || row >= section.Rows.Count)
        {
            return;
        }

        PhysicsPropertyList.SelectedIndex = row;
        _physicsSelectedProperty = section.Rows[row];
        RenderPhysicsPropertyEditorCore();
    }

    private void OnPhysicsObjectSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        _ = sender;
        _ = e;
        if (_updatingPhysicsAuthoringUi)
        {
            return;
        }

        int index = PhysicsObjectSelector.SelectedIndex;
        _physicsSelectedSection =
            index >= 0 && index < _physicsSections.Count ? _physicsSections[index] : null;
        BindPhysicsPropertyList();
        RenderPhysicsAuthoringState(_physics?.Snapshot ?? ViewerPhysicsStatusSnapshot.Disabled);
    }

    private void BindPhysicsPropertyList()
    {
        PhysicsPropertyList.ItemsSource = _physicsSelectedSection?.Rows;
        PhysicsPropertyList.SelectedIndex = -1;
        _physicsSelectedProperty = null;
        SetInspectorText(PhysicsPropertyDetail, DescribeSelectedSection());
    }

    private string DescribeSelectedSection()
    {
        if (_physicsSelectedSection is not { } section)
        {
            return "Reload the properties to author this stage's physics.";
        }

        if (section.Diagnostics.Count == 0)
        {
            return "Select a property to author it.";
        }

        return "Select a property to author it. " + string.Join(" ", section.Diagnostics);
    }

    private void OnPhysicsPropertySelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        _ = sender;
        _ = e;
        if (_updatingPhysicsAuthoringUi)
        {
            return;
        }

        _physicsSelectedProperty = PhysicsPropertyList.SelectedItem as ViewerPhysicsPropertyRow;
        RenderPhysicsPropertyEditor();
    }

    private void RenderPhysicsPropertyEditor()
    {
        _updatingPhysicsAuthoringUi = true;
        try
        {
            RenderPhysicsPropertyEditorCore();
        }
        finally
        {
            _updatingPhysicsAuthoringUi = false;
        }
    }

    /// <summary>
    /// Repaints the property editor without touching the re-entrancy guard.
    /// </summary>
    /// <remarks>
    /// The rebuild that restores a selection already holds the guard, and a nested
    /// try/finally would clear it halfway through the rebuild - which would let the selector's own
    /// change notification run as if the operator had made the selection.
    /// </remarks>
    private void RenderPhysicsPropertyEditorCore()
    {
        if (_physicsSelectedProperty is not { } row)
        {
            SetInspectorText(PhysicsPropertyDetail, DescribeSelectedSection());
            PhysicsPropertyValueBox.IsEnabled = false;
            PhysicsPropertyTokenSelector.IsVisible = false;
            PhysicsPropertyTokenSelector.IsEnabled = false;
            PhysicsApplyPropertyButton.IsEnabled = false;
            PhysicsClearPropertyButton.IsEnabled = false;
            return;
        }

        SetInspectorText(
            PhysicsPropertyDetail,
            string.Create(
                CultureInfo.InvariantCulture,
                $"{row.Name} — {row.Documentation} {row.Detail}"));
        bool tokens = row.Kind == ViewerPhysicsValueKind.Token && row.Tokens.Count != 0;
        PhysicsPropertyTokenSelector.IsVisible = tokens;
        if (tokens)
        {
            var choices = new List<ComboBoxItem>(row.Tokens.Count);
            for (int index = 0; index < row.Tokens.Count; index++)
            {
                choices.Add(new ComboBoxItem
                {
                    Content = row.Tokens[index],
                    Tag = row.Tokens[index],
                });
            }

            PhysicsPropertyTokenSelector.ItemsSource = choices;
            PhysicsPropertyTokenSelector.SelectedIndex = IndexOfToken(row);
        }

        PhysicsPropertyValueBox.Text = row.ValueText;
        PhysicsPropertyValueBox.IsVisible = !tokens;
        bool editable = row.IsEditable && _physics is { CanAuthor: true };
        PhysicsPropertyValueBox.IsEnabled = editable && !tokens;
        PhysicsPropertyTokenSelector.IsEnabled = editable && tokens;
        PhysicsApplyPropertyButton.IsEnabled = editable;
        PhysicsClearPropertyButton.IsEnabled = editable;
    }

    private static int IndexOfToken(ViewerPhysicsPropertyRow row)
    {
        for (int index = 0; index < row.Tokens.Count; index++)
        {
            if (string.Equals(row.Tokens[index], row.ValueText, StringComparison.Ordinal))
            {
                return index;
            }
        }

        return -1;
    }

    private async void OnPhysicsApplyPropertyClick(object? sender, RoutedEventArgs e)
    {
        _ = sender;
        e.Handled = true;
        if (_physicsSelectedProperty is not { } row || _physics is not { } physics)
        {
            return;
        }

        string text = row.Kind == ViewerPhysicsValueKind.Token && row.Tokens.Count != 0
            ? ReadTokenSelection(row)
            : PhysicsPropertyValueBox.Text ?? string.Empty;
        if (!ViewerPhysicsValueParser.TryParse(
            row.Kind,
            row.Tokens,
            text,
            out ViewerPhysicsValue value,
            out string error))
        {
            ShowError($"{row.Label} was not authored: {error}");
            return;
        }

        await ApplyPhysicsPropertyAsync(physics, row, value);
    }

    private async void OnPhysicsClearPropertyClick(object? sender, RoutedEventArgs e)
    {
        _ = sender;
        e.Handled = true;
        if (_physicsSelectedProperty is not { } row || _physics is not { } physics)
        {
            return;
        }

        await ApplyPhysicsPropertyAsync(
            physics,
            row,
            ViewerPhysicsValue.Unauthored(row.Kind));
    }

    private Task ApplyPhysicsPropertyAsync(
        ViewerPhysicsController physics,
        ViewerPhysicsPropertyRow row,
        ViewerPhysicsValue value)
    {
        _ = physics;

        // Authoring goes through the same funnel undo and redo use, so the busy guard, the result
        // report, and the anchored reload are shared rather than duplicated three ways.
        return RunPhysicsAuthoringAsync(
            async controller =>
            {
                // The current value is read first so undo can restore exactly what was there,
                // including the absence of an authored opinion.
                ViewerPhysicsValue before = await controller.ReadPropertyAsync(
                    row.PrimPath,
                    row.Name,
                    row.Kind,
                    _documentLifetime?.Token ?? default);
                var step = new ViewerPhysicsEditStep(
                    string.Create(CultureInfo.InvariantCulture, $"{row.Label} on {row.PrimPath}"),
                    [new ViewerPhysicsEdit(row.PrimPath, row.Name, row.Label, before, value)]);
                return await controller.ApplyEditAsync(
                    step,
                    _documentLifetime?.Token ?? default);
            },
            $"{row.Label} was not authored: ");
    }

    private string ReadTokenSelection(ViewerPhysicsPropertyRow row) =>
        PhysicsPropertyTokenSelector.SelectedItem is ComboBoxItem { Tag: string token }
            ? token
            : row.ValueText;

    private async Task RunPhysicsAuthoringAsync(
        Func<ViewerPhysicsController, Task<ViewerPhysicsAuthoringResult>> operation,
        string failurePrefix = "The physics edit did not complete: ")
    {
        if (_physics is not { } physics)
        {
            return;
        }

        // Apply, clear, undo, and redo all edit the same stage and all reload the inspector when
        // they finish. Two of them in flight at once would interleave the edit with the reload and
        // leave the selection anchored to a document neither of them produced, so the second is
        // refused rather than queued: the operator can simply press again.
        if (_physicsAuthoringBusy)
        {
            return;
        }

        _physicsAuthoringBusy = true;
        try
        {
            ReportPhysicsAuthoringResult(await operation(physics));
            await ReloadPhysicsPropertiesAsync();
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            ShowError(failurePrefix + ViewerPackageErrorFormatter.Format(exception));
        }
        finally
        {
            _physicsAuthoringBusy = false;
        }
    }

    private void ReportPhysicsAuthoringResult(ViewerPhysicsAuthoringResult result)
    {
        if (result.Rejected != 0)
        {
            ShowError(result.Message);
            return;
        }

        SetReady(result.Message);
    }

    private async void OnPhysicsApplyForceClick(object? sender, RoutedEventArgs e)
    {
        _ = sender;
        e.Handled = true;
        await SubmitPhysicsImpulseAsync(ViewerPhysicsRuntimeCommandKind.Force);
    }

    private async void OnPhysicsApplyImpulseClick(object? sender, RoutedEventArgs e)
    {
        _ = sender;
        e.Handled = true;
        await SubmitPhysicsImpulseAsync(ViewerPhysicsRuntimeCommandKind.Impulse);
    }

    private async void OnPhysicsApplyTorqueClick(object? sender, RoutedEventArgs e)
    {
        _ = sender;
        e.Handled = true;
        await SubmitPhysicsImpulseAsync(ViewerPhysicsRuntimeCommandKind.Torque);
    }

    private async void OnPhysicsWakeClick(object? sender, RoutedEventArgs e)
    {
        _ = sender;
        e.Handled = true;
        await SubmitPhysicsStateCommandAsync(ViewerPhysicsRuntimeCommandKind.Wake);
    }

    private async void OnPhysicsSleepClick(object? sender, RoutedEventArgs e)
    {
        _ = sender;
        e.Handled = true;
        await SubmitPhysicsStateCommandAsync(ViewerPhysicsRuntimeCommandKind.Sleep);
    }

    private void OnPhysicsVehicleDriveChanged(object? sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;
        if (_updatingPhysicsAuthoringUi)
        {
            return;
        }

        SetReady(PhysicsVehicleDriveCheckBox.IsChecked == true
            ? "Vehicle input is submitted on every simulation step: " +
                ReadPhysicsVehicleInput().Describe()
            : "Vehicle input is no longer submitted.");
    }

    private void OnPhysicsControllerDriveChanged(object? sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;

        // Turning driving off has to drop whatever is still held, or switching it back on would
        // resume walking from keys the operator released while it was off.
        if (PhysicsControllerDriveCheckBox.IsChecked != true)
        {
            _physicsControllerKeys.Clear();
        }

        if (_updatingPhysicsAuthoringUi)
        {
            return;
        }

        SetReady(PhysicsControllerDriveCheckBox.IsChecked == true
            ? "The movement keys now drive the selected character controller."
            : "The movement keys no longer drive a character controller.");
    }

    /// <summary>Reads the vehicle driver input the sliders currently describe.</summary>
    /// <returns>The clamped input.</returns>
    internal ViewerPhysicsVehicleInput ReadPhysicsVehicleInput() => new ViewerPhysicsVehicleInput(
        PhysicsVehicleThrottleSlider.Value,
        PhysicsVehicleBrakeSlider.Value,
        PhysicsVehicleSteerSlider.Value,
        PhysicsVehicleHandBrakeSlider.Value,
        PhysicsVehicleClutchSlider.Value,
        ReadTag(PhysicsVehicleGearSelector, 0)).Clamped();

    private async Task SubmitPhysicsImpulseAsync(ViewerPhysicsRuntimeCommandKind kind)
    {
        // An impulse needs the impulse family; a force and a torque only need the body family,
        // because an articulation link accepts those but refuses an impulse.
        ViewerPhysicsCommandability required = kind == ViewerPhysicsRuntimeCommandKind.Impulse
            ? ViewerPhysicsCommandability.Impulse
            : ViewerPhysicsCommandability.Body;
        if (_physics is not { } physics || !TryResolvePhysicsTarget(required, out ulong target))
        {
            return;
        }

        if (!ViewerPhysicsValueParser.TryParse(
            ViewerPhysicsValueKind.Vector3,
            [],
            PhysicsForceDirectionBox.Text ?? string.Empty,
            out ViewerPhysicsValue direction,
            out string directionError))
        {
            ShowError("The direction was not accepted: " + directionError);
            return;
        }

        if (!double.TryParse(
            PhysicsForceMagnitudeBox.Text,
            NumberStyles.Float,
            CultureInfo.InvariantCulture,
            out double magnitude))
        {
            ShowError("Enter a finite magnitude.");
            return;
        }

        if (!ViewerPhysicsImpulseBuilder.TryBuild(
            kind,
            target,
            direction.VectorValue,
            magnitude,
            ReadTag(PhysicsForceModeSelector, ViewerPhysicsForceMode.Default),
            out ViewerPhysicsRuntimeCommand command,
            out string error))
        {
            ShowError(error);
            return;
        }

        await SubmitPhysicsCommandsAsync(physics, [command]);
    }

    private async Task SubmitPhysicsStateCommandAsync(ViewerPhysicsRuntimeCommandKind kind)
    {
        if (_physics is not { } physics || !TryResolvePhysicsTarget(ViewerPhysicsCommandability.Body, out ulong target))
        {
            return;
        }

        await SubmitPhysicsCommandsAsync(
            physics,
            [new ViewerPhysicsRuntimeCommand(kind, target, ViewerPhysicsVector3.Zero)]);
    }

    private async Task SubmitPhysicsCommandsAsync(
        ViewerPhysicsController physics,
        IReadOnlyList<ViewerPhysicsRuntimeCommand> commands)
    {
        try
        {
            ViewerPhysicsCommandOutcome outcome = await physics.SubmitCommandsAsync(
                commands,
                _documentLifetime?.Token ?? default);
            if (outcome.Rejected != 0)
            {
                ShowError(outcome.Message);
                return;
            }

            SetReady(outcome.Message);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            ShowError(
                "The interactive command was refused: " +
                ViewerPackageErrorFormatter.Format(exception));
        }
    }

    /// <summary>Resolves the simulated identity the interaction controls target.</summary>
    /// <remarks>
    /// The selected section already carries the address the composer gave the object, so the
    /// interaction targets exactly the object the operator selected rather than whichever record
    /// for that path happens to come first. A section that cannot receive the command family says
    /// so, because a control that can only ever be refused is worse than a control that explains
    /// itself.
    /// </remarks>
    /// <param name="required">The command family the interaction needs.</param>
    /// <param name="target">Receives the identity.</param>
    /// <returns><see langword="true"/> when an identity is selected.</returns>
    private bool TryResolvePhysicsTarget(ViewerPhysicsCommandability required, out ulong target)
    {
        target = 0UL;
        if (_physics is null || _physicsSelectedSection is not { } section)
        {
            ShowError("Select a simulated object before driving the simulation.");
            return false;
        }

        target = ViewerPhysicsController.ResolveCommandTarget(section, required);
        if (target == 0UL)
        {
            ShowError(string.Create(
                CultureInfo.InvariantCulture,
                $"{section.PrimPath} ({section.Kind}) does not accept {required} commands, " +
                $"so it cannot be driven."));
            return false;
        }

        return true;
    }

    private void RenderPhysicsAuthoringState(in ViewerPhysicsStatusSnapshot snapshot)
    {
        _updatingPhysicsAuthoringUi = true;
        try
        {
            ViewerPhysicsController? physics = _physics;
            bool enabled = snapshot.IsEnabled && physics is not null;
            PhysicsGizmoSelector.IsEnabled = enabled;
            SyncPhysicsGizmoMenu();
            PhysicsSnapCheckBox.IsEnabled = enabled;
            PhysicsSnapMenuItem.IsEnabled = enabled;
            PhysicsSnapMenuItem.IsChecked = PhysicsSnapCheckBox.IsChecked == true;
            PhysicsRefreshPropertiesButton.IsEnabled = enabled;
            PhysicsObjectSelector.IsEnabled = enabled && _physicsSections.Count != 0;
            PhysicsPropertyList.IsEnabled = enabled && _physicsSelectedSection is not null;
            PhysicsUndoButton.IsEnabled = enabled && physics is { History.CanUndo: true };
            PhysicsRedoButton.IsEnabled = enabled && physics is { History.CanRedo: true };
            ToolTip.SetTip(
                PhysicsUndoButton,
                physics is { History.CanUndo: true }
                    ? "Undo " + physics.History.UndoDescription
                    : "There is no physics property edit to undo.");
            ToolTip.SetTip(
                PhysicsRedoButton,
                physics is { History.CanRedo: true }
                    ? "Redo " + physics.History.RedoDescription
                    : "There is no undone physics property edit to redo.");

            bool bodies = enabled && HasPhysicsCapability(physics, "RigidBodies");
            bool commands = enabled && HasPhysicsCapability(physics, "Commands");
            bool controllers = enabled && HasPhysicsCapability(physics, "Controllers");
            bool vehicles = enabled && HasPhysicsCapability(physics, "Vehicles");

            // A capability says the world can carry the command; the selection says whether the
            // object the operator picked is one the command can reach. A collider section resolves
            // to the body that owns it, a vehicle section to the vehicle, and a joint or a material
            // to nothing at all, so the controls follow the selection rather than only the world.
            ViewerPhysicsObjectSection? selected = _physicsSelectedSection;
            bool bodyTarget = selected?.Accepts(ViewerPhysicsCommandability.Body) == true;
            bool impulseTarget = selected?.Accepts(ViewerPhysicsCommandability.Impulse) == true;
            bool controllerTarget = selected?.Accepts(ViewerPhysicsCommandability.Controller) == true;
            bool vehicleTarget = selected?.Accepts(ViewerPhysicsCommandability.Vehicle) == true;

            bool drivable = bodies && commands && bodyTarget;
            PhysicsForceDirectionBox.IsEnabled = drivable;
            PhysicsForceMagnitudeBox.IsEnabled = drivable;
            PhysicsForceModeSelector.IsEnabled = drivable;
            PhysicsApplyForceButton.IsEnabled = drivable;

            // An articulation link takes a force but not an impulse, so the impulse control follows
            // its own flag rather than the body one.
            PhysicsApplyImpulseButton.IsEnabled = bodies && commands && impulseTarget;
            PhysicsApplyTorqueButton.IsEnabled = drivable;
            PhysicsWakeButton.IsEnabled = drivable;
            PhysicsSleepButton.IsEnabled = drivable;
            PhysicsControllerDriveCheckBox.IsEnabled =
                controllers && commands && controllerTarget;
            PhysicsControllerSpeedBox.IsEnabled = controllers && commands && controllerTarget;
            if (!PhysicsControllerDriveCheckBox.IsEnabled)
            {
                // A control that can no longer be driven must not keep the keys it was holding:
                // re-enabling it later would resume walking from a gesture that ended long ago.
                PhysicsControllerDriveCheckBox.IsChecked = false;
                _physicsControllerKeys.Clear();
            }
            SetPhysicsVehicleEnabled(vehicles && commands && vehicleTarget);
            SetInspectorText(PhysicsAuthoringState, DescribeAuthoringState(physics, in snapshot));
            SetInspectorText(
                PhysicsInteractionState,
                DescribeInteractionState(
                    enabled,
                    commands,
                    bodies,
                    controllers,
                    vehicles,
                    selected));
            RenderPhysicsPropertyEditorState();
        }
        finally
        {
            _updatingPhysicsAuthoringUi = false;
        }
    }

    private void RenderPhysicsPropertyEditorState()
    {
        bool editable = _physicsSelectedProperty is { IsEditable: true } &&
            _physics is { CanAuthor: true };
        PhysicsApplyPropertyButton.IsEnabled = editable;
        PhysicsClearPropertyButton.IsEnabled = editable;
        PhysicsPropertyValueBox.IsEnabled = editable && !PhysicsPropertyTokenSelector.IsVisible;
        PhysicsPropertyTokenSelector.IsEnabled =
            editable && PhysicsPropertyTokenSelector.IsVisible;
    }

    private void SetPhysicsVehicleEnabled(bool enabled)
    {
        PhysicsVehicleThrottleSlider.IsEnabled = enabled;
        PhysicsVehicleBrakeSlider.IsEnabled = enabled;
        PhysicsVehicleSteerSlider.IsEnabled = enabled;
        PhysicsVehicleHandBrakeSlider.IsEnabled = enabled;
        PhysicsVehicleClutchSlider.IsEnabled = enabled;
        PhysicsVehicleGearSelector.IsEnabled = enabled;
        PhysicsVehicleDriveCheckBox.IsEnabled = enabled;
        if (!enabled)
        {
            PhysicsVehicleDriveCheckBox.IsChecked = false;
        }
    }

    private static bool HasPhysicsCapability(ViewerPhysicsController? physics, string name)
    {
        if (physics is null)
        {
            return false;
        }

        IReadOnlyList<ViewerPhysicsCapabilityRow> rows = physics.Capabilities;
        for (int index = 0; index < rows.Count; index++)
        {
            if (string.Equals(rows[index].Name, name, StringComparison.Ordinal))
            {
                return rows[index].IsSupported;
            }
        }

        return false;
    }

    private string DescribeAuthoringState(
        ViewerPhysicsController? physics,
        in ViewerPhysicsStatusSnapshot snapshot)
    {
        if (!snapshot.IsEnabled || physics is null)
        {
            return "Enable physics for this stage to author its simulation inputs.";
        }

        if (!physics.CanAuthor)
        {
            return "This document has no writable stage, so physics properties are read only.";
        }

        if (_physicsSections.Count == 0)
        {
            return "Reload the properties to list every extracted physics object.";
        }

        return string.Create(
            CultureInfo.InvariantCulture,
            $"{_physicsSections.Count} extracted object(s) · undo {physics.History.UndoDepth} · " +
            $"redo {physics.History.RedoDepth}");
    }

    private static string DescribeInteractionState(
        bool enabled,
        bool commands,
        bool bodies,
        bool controllers,
        bool vehicles,
        ViewerPhysicsObjectSection? selected)
    {
        if (!enabled)
        {
            return "Enable physics and select a simulated object to drive it.";
        }

        if (!commands)
        {
            return "The built world does not accept runtime commands, so nothing can be driven.";
        }

        var parts = new List<string>(3);
        parts.Add(bodies ? "forces and impulses" : "no rigid bodies");
        parts.Add(controllers ? "character controllers" : "no character controllers");
        parts.Add(vehicles ? "vehicles" : "no vehicles");
        string world = "The built world simulates: " + string.Join(", ", parts) + ".";
        if (selected is null)
        {
            return world + " Select an object to drive it.";
        }

        // Naming the object a command would actually reach is the only way an operator can tell a
        // collider section from the body it drives, which is the difference that decides where a
        // force lands.
        return world + " " + DescribeSelectedTarget(selected);
    }

    private static string DescribeSelectedTarget(ViewerPhysicsObjectSection selected)
    {
        if (selected.Commandability == ViewerPhysicsCommandability.None)
        {
            return string.Create(
                CultureInfo.InvariantCulture,
                $"{selected.PrimPath} ({selected.Kind}) receives no runtime command.");
        }

        return string.Create(
            CultureInfo.InvariantCulture,
            $"{selected.PrimPath} ({selected.Kind}) drives {selected.TargetPath} " +
            $"as {selected.Commandability}.");
    }

    /// <summary>Records or releases one held character-controller movement key.</summary>
    /// <param name="e">The key event.</param>
    /// <param name="held">Whether the key went down rather than up.</param>
    /// <returns><see langword="true"/> when the key drives a controller and was consumed.</returns>
    /// <remarks>
    /// The press is gated by the drive toggle, the modifiers, and text focus; the release is gated
    /// by none of them. A key that is physically up must stop contributing whatever changed while
    /// it was down, or the controller latches and walks forever with nothing held.
    /// </remarks>
    private bool TryHandlePhysicsControllerKey(KeyEventArgs e, bool held)
    {
        ArgumentNullException.ThrowIfNull(e);
        if (!held)
        {
            return _physicsControllerKeys.TryRelease(e.Key);
        }

        return _physicsControllerKeys.TryPress(
            e.Key,
            e.KeyModifiers,
            IsCameraShortcutEditing(),
            PhysicsControllerDriveCheckBox.IsChecked == true &&
                PhysicsControllerDriveCheckBox.IsEnabled);
    }

    /// <summary>Selects one gizmo from a keyboard shortcut.</summary>
    /// <param name="mode">The gizmo to select.</param>
    internal void SelectPhysicsGizmo(ViewerGizmoMode mode)
    {
        for (int index = 0; index < PhysicsGizmoSelector.ItemCount; index++)
        {
            if (PhysicsGizmoSelector.Items[index] is ComboBoxItem { Tag: string tag } &&
                string.Equals(tag, mode.ToString(), StringComparison.Ordinal))
            {
                PhysicsGizmoSelector.SelectedIndex = index;
                return;
            }
        }
    }

    /// <summary>Clears every authoring surface when a document closes.</summary>
    private void ResetPhysicsAuthoringUi()
    {
        _updatingPhysicsAuthoringUi = true;
        try
        {
            _physicsSections = [];
            _physicsSelectedSection = null;
            _physicsSelectedProperty = null;
            _physicsSectionRevision = ulong.MaxValue;
            _physicsControllerKeys.Clear();
            CancelPhysicsDrag();
            PhysicsObjectSelector.ItemsSource = null;
            PhysicsPropertyList.ItemsSource = null;
            PhysicsPropertyTokenSelector.ItemsSource = null;
            PhysicsPropertyTokenSelector.IsVisible = false;
            PhysicsVehicleDriveCheckBox.IsChecked = false;
            PhysicsControllerDriveCheckBox.IsChecked = false;
            SetInspectorText(PhysicsPropertyDetail, "Select a property to author it.");
        }
        finally
        {
            _updatingPhysicsAuthoringUi = false;
        }
    }

    /// <summary>
    /// Submits the continuous drive inputs once per pump, batched into a single request.
    /// </summary>
    /// <remarks>
    /// A vehicle and a controller both want an input on every simulated step, so both are staged in
    /// one batch. Submitting them separately would double the number of requests the bounded
    /// transport queue has to carry for no benefit at all.
    /// </remarks>
    private async Task SubmitPhysicsDriveInputsAsync(ViewerPhysicsController physics)
    {
        bool vehicle = PhysicsVehicleDriveCheckBox.IsChecked == true &&
            PhysicsVehicleDriveCheckBox.IsEnabled;
        bool controller = PhysicsControllerDriveCheckBox.IsChecked == true &&
            PhysicsControllerDriveCheckBox.IsEnabled &&
            _physicsControllerKeys.HasHeldKeys;
        if ((!vehicle && !controller) || _physicsSelectedSection is not { } section)
        {
            return;
        }

        // A vehicle and a character controller are two different composed objects with two
        // different addresses, so one identity cannot stand in for both. Resolving them separately
        // is what stops a driver input from being sent to a controller identity the vehicle map
        // does not contain, which the world would refuse once per pump.
        ulong vehicleTarget = ViewerPhysicsController.ResolveCommandTarget(
            section,
            ViewerPhysicsCommandability.Vehicle);
        ulong controllerTarget = ViewerPhysicsController.ResolveCommandTarget(
            section,
            ViewerPhysicsCommandability.Controller);

        var batch = new List<ViewerPhysicsRuntimeCommand>(2);
        if (vehicle && vehicleTarget != 0UL)
        {
            batch.Add(ReadPhysicsVehicleInput().ToCommand(vehicleTarget));
        }

        if (controller &&
            controllerTarget != 0UL &&
            TryBuildPhysicsControllerMove(physics, controllerTarget, out var move))
        {
            batch.Add(move);
        }

        if (batch.Count == 0)
        {
            return;
        }

        ViewerPhysicsCommandOutcome outcome = await physics.SubmitCommandsAsync(
            batch,
            _documentLifetime?.Token ?? default);
        if (outcome.Rejected != 0)
        {
            // A refused drive input must not spam the status line every pump, so the controls that
            // produced it are switched off, whatever they were still holding is dropped, and the
            // refusal is reported exactly once.
            _updatingPhysicsAuthoringUi = true;
            try
            {
                PhysicsVehicleDriveCheckBox.IsChecked = false;
                PhysicsControllerDriveCheckBox.IsChecked = false;
            }
            finally
            {
                _updatingPhysicsAuthoringUi = false;
            }

            _physicsControllerKeys.Clear();
            ShowError(outcome.Message);
        }
    }

    private bool TryBuildPhysicsControllerMove(
        ViewerPhysicsController physics,
        ulong target,
        out ViewerPhysicsRuntimeCommand command)
    {
        _ = physics;
        double speed = 2d;
        if (double.TryParse(
                PhysicsControllerSpeedBox.Text,
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out double parsed) &&
            double.IsFinite(parsed) && parsed > 0d)
        {
            speed = parsed;
        }

        ViewerPhysicsPointerRay.ReadBasis(
            _cameraNavigation.State,
            out ViewerPhysicsVector3 forward,
            out ViewerPhysicsVector3 right,
            out ViewerPhysicsVector3 up);
        return ViewerPhysicsControllerInput.TryBuild(
            target,
            _physicsControllerKeys.Held,
            forward,
            right,
            up,
            speed,
            PhysicsControllerStepSeconds,
            out command);
    }

    /// <summary>The simulated time one controller move request covers.</summary>
    /// <remarks>
    /// The pump runs about as often as the world steps, so one move per pump covering one fixed
    /// step keeps the requested speed the speed the user typed rather than a multiple of it.
    /// </remarks>
    private const double PhysicsControllerStepSeconds = 1d / 60d;

    /// <summary>Releases every held character-controller movement key.</summary>
    /// <remarks>
    /// A window that loses focus never sees the key release, so the held set has to be dropped or
    /// the controller keeps walking for as long as the viewer stays in the background.
    /// </remarks>
    internal void ClearPhysicsControllerKeys() => _physicsControllerKeys.Clear();

    private static TValue ReadTag<TValue>(SelectingItemsControl selector, TValue fallback)
        where TValue : struct, Enum
    {
        if (selector.SelectedItem is ComboBoxItem { Tag: string tag } &&
            Enum.TryParse(tag, ignoreCase: false, out TValue value))
        {
            return value;
        }

        return fallback;
    }

    private static int ReadTag(SelectingItemsControl selector, int fallback)
    {
        if (selector.SelectedItem is ComboBoxItem { Tag: string tag } &&
            int.TryParse(tag, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value))
        {
            return value;
        }

        return fallback;
    }
}
