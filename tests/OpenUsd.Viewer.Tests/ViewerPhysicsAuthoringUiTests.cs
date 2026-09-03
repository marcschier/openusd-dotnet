// Copyright (c) marcschier. Licensed under the MIT License.

using System.Xml.Linq;
using Avalonia.Input;

namespace OpenUsd.Viewer.Tests;

/// <summary>
/// Asserts the physics authoring user interface contract: every authoring control is named and
/// explained in the markup, the widened toolbar still never clips a control at any width, and the
/// authoring shortcuts never fire while the user is typing or collide with an existing binding.
/// </summary>
public sealed class ViewerPhysicsAuthoringUiTests
{
    private static readonly ViewerToolbarItem[] PhysicsToolbar =
    [
        new("PhysicsEnableButton", "Physics", 88),
        new("PhysicsPlayPauseButton", "Play", 72),
        new("PhysicsStopButton", "Stop", 72),
        new("PhysicsStepButton", "Step", 72),
        new("PhysicsLoopCheckBox", "Loop", 72),
        new("PhysicsSpeedSelector", "Speed", 96),
        new("PhysicsPreviewCheckBox", "Apply Preview", 132),
        new("PhysicsBakeButton", "Bake...", 84),
        new("PhysicsGizmoSelector", "Gizmo", 104),
        new("PhysicsSnapCheckBox", "Snap", 72),
        new("PhysicsUndoButton", "Undo", 72),
        new("PhysicsRedoButton", "Redo", 72),
    ];

    [Test]
    public async Task TheToolbarThatCarriesTheAuthoringControlsNeverClipsOneAtAnyWidth()
    {
        // The widths are swept and the violations collected, then asserted once. Awaiting inside
        // the sweep would put a thousand continuations on the shared scheduler for one assertion.
        var violations = new List<string>();
        for (double width = 0d; width <= 1600d; width += 3d)
        {
            ViewerToolbarOverflowPlan plan = ViewerToolbarOverflowPlanner.Plan(
                PhysicsToolbar,
                width);
            if (plan.UsedWidth > width)
            {
                violations.Add($"{width} clipped at {plan.UsedWidth}");
            }

            if (plan.Visible.Count + plan.Overflow.Count != PhysicsToolbar.Length)
            {
                violations.Add($"{width} lost a control");
            }
        }

        await Assert.That(violations).IsEmpty();
    }

    [Test]
    public async Task ANarrowToolbarDefersTheAuthoringControlsIntoTheOverflowMenu()
    {
        ViewerToolbarOverflowPlan plan = ViewerToolbarOverflowPlanner.Plan(PhysicsToolbar, 300d);

        await Assert.That(plan.HasOverflow).IsTrue();
        var deferred = new List<string>();
        foreach (ViewerToolbarItem item in plan.Overflow)
        {
            deferred.Add(item.Name);
        }

        await Assert.That(deferred).Contains("PhysicsUndoButton");
        await Assert.That(deferred).Contains("PhysicsRedoButton");
        await Assert.That(deferred).Contains("PhysicsGizmoSelector");
    }

    [Test]
    public async Task TheAuthoringToolbarItemsMatchTheWindowsOwnList()
    {
        // The transport strip only ever shows Play/Pause, Stop, and Step: every other authoring
        // control (Loop, Speed, Preview, Bake, Gizmo, Snap, Undo, Redo, Enable) moved into the
        // Physics menu, so this checks the reduced list the window actually keeps in its toolbar
        // overflow plan rather than the wider set exercised generically above.
        ViewerToolbarItem[] realPhysicsToolbar =
        [
            new("PhysicsPlayPauseButton", "Play", 72),
            new("PhysicsStopButton", "Stop", 72),
            new("PhysicsStepButton", "Step", 72),
        ];
        string source = await File.ReadAllTextAsync(Path.Combine(
            FindRepositoryRoot(),
            "src",
            "OpenUsd.Viewer",
            "MainWindow.Physics.cs"));

        foreach (ViewerToolbarItem item in realPhysicsToolbar)
        {
            await Assert.That(source).Contains($"new ViewerToolbarItem(\"{item.Name}\"");
        }
    }

    [Test]
    public async Task EveryAuthoringControlIsNamedForAssistiveTechnology()
    {
        XDocument markup = await LoadMainWindowMarkupAsync();

        string[] names =
        [
            "PhysicsGizmoSelector",
            "PhysicsSnapCheckBox",
            "PhysicsUndoButton",
            "PhysicsRedoButton",
            "PhysicsRefreshPropertiesButton",
            "PhysicsObjectSelector",
            "PhysicsPropertyList",
            "PhysicsPropertyValueBox",
            "PhysicsPropertyTokenSelector",
            "PhysicsApplyPropertyButton",
            "PhysicsClearPropertyButton",
            "PhysicsForceDirectionBox",
            "PhysicsForceMagnitudeBox",
            "PhysicsForceModeSelector",
            "PhysicsApplyForceButton",
            "PhysicsApplyImpulseButton",
            "PhysicsApplyTorqueButton",
            "PhysicsWakeButton",
            "PhysicsSleepButton",
            "PhysicsControllerDriveCheckBox",
            "PhysicsControllerSpeedBox",
            "PhysicsVehicleThrottleSlider",
            "PhysicsVehicleBrakeSlider",
            "PhysicsVehicleSteerSlider",
            "PhysicsVehicleHandBrakeSlider",
            "PhysicsVehicleClutchSlider",
            "PhysicsVehicleGearSelector",
            "PhysicsVehicleDriveCheckBox",
        ];

        var unnamed = new List<string>();
        foreach (string name in names)
        {
            XElement element = FindByName(markup, name);
            if (!element.Attributes()
                .Any(attribute => attribute.Name.LocalName == "AutomationProperties.Name"))
            {
                unnamed.Add(name);
            }
        }

        await Assert.That(unnamed).IsEmpty();
    }

    [Test]
    public async Task EveryAuthoringControlStartsDisabledSoNothingLooksUsableBeforeAWorldExists()
    {
        XDocument markup = await LoadMainWindowMarkupAsync();

        string[] names =
        [
            "PhysicsGizmoSelector",
            "PhysicsSnapCheckBox",
            "PhysicsUndoButton",
            "PhysicsRedoButton",
            "PhysicsApplyPropertyButton",
            "PhysicsApplyForceButton",
            "PhysicsVehicleThrottleSlider",
            "PhysicsVehicleDriveCheckBox",
            "PhysicsControllerDriveCheckBox",
        ];

        var enabled = new List<string>();
        foreach (string name in names)
        {
            if (FindByName(markup, name).Attribute("IsEnabled")?.Value != "False")
            {
                enabled.Add(name);
            }
        }

        await Assert.That(enabled).IsEmpty();
    }

    [Test]
    public async Task TheVehicleSlidersDeclareExactlyTheRangesTheCommandAbiAccepts()
    {
        XDocument markup = await LoadMainWindowMarkupAsync();

        foreach (string name in new[]
        {
            "PhysicsVehicleThrottleSlider",
            "PhysicsVehicleBrakeSlider",
            "PhysicsVehicleHandBrakeSlider",
            "PhysicsVehicleClutchSlider",
        })
        {
            XElement slider = FindByName(markup, name);
            await Assert.That(slider.Attribute("Minimum")?.Value).IsEqualTo("0");
            await Assert.That(slider.Attribute("Maximum")?.Value).IsEqualTo("1");
        }

        XElement steer = FindByName(markup, "PhysicsVehicleSteerSlider");
        await Assert.That(steer.Attribute("Minimum")?.Value).IsEqualTo("-1");
        await Assert.That(steer.Attribute("Maximum")?.Value).IsEqualTo("1");
    }

    [Test]
    public async Task TheGearSelectorNumbersGearsTheWayTheRuntimeDoes()
    {
        XDocument markup = await LoadMainWindowMarkupAsync();
        XElement selector = FindByName(markup, "PhysicsVehicleGearSelector");

        var tags = new List<string>();
        foreach (XElement item in selector.Elements())
        {
            if (item.Attribute("Tag")?.Value is { } tag)
            {
                tags.Add(tag);
            }
        }

        await Assert.That(tags[0]).IsEqualTo("0");
        await Assert.That(tags[1]).IsEqualTo("1");
        await Assert.That(tags[2]).IsEqualTo("2");
        await Assert.That(tags[3]).IsEqualTo("3");
    }

    [Test]
    public async Task AuthoringShortcutsRefuseToFireWhileTheUserIsTyping()
    {
        (Key Key, ViewerPhysicsShortcut Expected)[] bindings =
        [
            (Key.Q, ViewerPhysicsShortcut.GizmoNone),
            (Key.G, ViewerPhysicsShortcut.GizmoTranslate),
            (Key.E, ViewerPhysicsShortcut.GizmoRotate),
            (Key.R, ViewerPhysicsShortcut.GizmoScale),
            (Key.H, ViewerPhysicsShortcut.GizmoDrag),
            (Key.X, ViewerPhysicsShortcut.ToggleSnap),
            (Key.Z, ViewerPhysicsShortcut.Undo),
            (Key.Y, ViewerPhysicsShortcut.Redo),
        ];

        var violations = new List<string>();
        foreach ((Key key, ViewerPhysicsShortcut expected) in bindings)
        {
            if (ViewerPhysicsShortcutPolicy.Classify(key, KeyModifiers.None, false) != expected ||
                ViewerPhysicsShortcutPolicy.Classify(key, KeyModifiers.None, true) !=
                    ViewerPhysicsShortcut.None ||
                ViewerPhysicsShortcutPolicy.Classify(key, KeyModifiers.Control, false) !=
                    ViewerPhysicsShortcut.None)
            {
                violations.Add(key.ToString());
            }
        }

        await Assert.That(violations).IsEmpty();
    }

    [Test]
    public async Task ThePhysicsShortcutsAndTheMovementKeysNeverOverlap()
    {
        Key[] movement = [Key.W, Key.A, Key.S, Key.D, Key.Space, Key.C];

        var violations = new List<string>();
        foreach (Key key in movement)
        {
            if (ViewerPhysicsShortcutPolicy.Classify(key, KeyModifiers.None, false) !=
                    ViewerPhysicsShortcut.None ||
                ViewerPhysicsControllerKeyPolicy.Classify(key, KeyModifiers.None, false) ==
                    ViewerPhysicsControllerDirection.None)
            {
                violations.Add(key.ToString());
            }
        }

        await Assert.That(violations).IsEmpty();
    }

    [Test]
    public async Task MovementKeysRefuseToWalkAControllerWhileTheUserIsTyping()
    {
        await Assert.That(ViewerPhysicsControllerKeyPolicy.Classify(
            Key.W, KeyModifiers.None, true))
            .IsEqualTo(ViewerPhysicsControllerDirection.None);
        await Assert.That(ViewerPhysicsControllerKeyPolicy.Classify(
            Key.W, KeyModifiers.Control, false))
            .IsEqualTo(ViewerPhysicsControllerDirection.None);
        await Assert.That(ViewerPhysicsControllerKeyPolicy.Classify(
            Key.F1, KeyModifiers.None, false))
            .IsEqualTo(ViewerPhysicsControllerDirection.None);
    }

    [Test]
    public async Task EveryAuthoringShortcutIsListedInTheShortcutsDialogCatalog()
    {
        var gestures = new List<string>();
        var unresolved = new List<string>();
        foreach (ViewerShortcut shortcut in ViewerShortcutCatalog.Physics)
        {
            gestures.Add(shortcut.Gesture);
            Key? key = ViewerShortcutCatalog.TryResolveKey(shortcut);
            if (key is null ||
                ViewerPhysicsShortcutPolicy.Classify(key.Value, KeyModifiers.None, false) ==
                    ViewerPhysicsShortcut.None)
            {
                unresolved.Add(shortcut.Gesture);
            }
        }

        await Assert.That(unresolved).IsEmpty();
        foreach (string gesture in new[] { "Q", "G", "E", "R", "H", "X", "Z", "Y" })
        {
            await Assert.That(gestures).Contains(gesture);
        }
    }

    [Test]
    public async Task TheDriveInputPumpBatchesTheVehicleAndControllerIntoOneSubmission()
    {
        string source = await File.ReadAllTextAsync(Path.Combine(
            FindRepositoryRoot(),
            "src",
            "OpenUsd.Viewer",
            "MainWindow.PhysicsAuthoring.cs"));

        int start = source.IndexOf(
            "private async Task SubmitPhysicsDriveInputsAsync",
            StringComparison.Ordinal);
        await Assert.That(start).IsGreaterThan(0);
        string body = source[start..];
        int end = body.IndexOf(
            "private bool TryBuildPhysicsControllerMove",
            StringComparison.Ordinal);
        body = body[..end];

        // One submission per pump, never one per control.
        await Assert.That(CountOccurrences(body, "SubmitCommandsAsync")).IsEqualTo(1);

        // A vehicle and a controller are two composed objects with two addresses, so the pump has
        // to resolve them separately rather than sending one identity to both maps.
        await Assert.That(body).Contains("ViewerPhysicsCommandability.Vehicle");
        await Assert.That(body).Contains("ViewerPhysicsCommandability.Controller");
        await Assert.That(body).DoesNotContain("ResolveIdentity");
    }

    [Test]
    public async Task AuthoringNeverWritesToTheStageOutsideTheScheduler()
    {
        string source = await File.ReadAllTextAsync(Path.Combine(
            FindRepositoryRoot(),
            "src",
            "OpenUsd.Viewer",
            "ViewerPhysicsAuthoringStage.cs"));

        await Assert.That(source).Contains("scheduler.EditAsync");
        await Assert.That(source).Contains("SetEditTargetToSessionLayer");
        await Assert.That(source).Contains("RestoreEditTarget");
        await Assert.That(source).Contains("UsdStageInvalidationKind.Property");
    }

    [Test]
    public async Task TheBodyDragNeverTakesThePointerUnlessTheDragGizmoIsSelected()
    {
        string source = await ReadAuthoringSourceAsync();

        int start = source.IndexOf(
            "private void OnPhysicsViewportPointerPressed",
            StringComparison.Ordinal);
        await Assert.That(start).IsGreaterThan(0);
        string body = source[start..];
        int end = body.IndexOf("private void CancelPhysicsDrag", StringComparison.Ordinal);
        body = body[..end];

        // The guard has to run before anything marks the event handled, or a camera orbit would
        // stop working the moment physics was enabled.
        int guard = body.IndexOf(
            "_physicsGizmoMode != ViewerGizmoMode.Drag",
            StringComparison.Ordinal);
        int handled = body.IndexOf("e.Handled = true", StringComparison.Ordinal);
        await Assert.That(guard).IsGreaterThan(0);
        await Assert.That(handled).IsGreaterThan(guard);
        await Assert.That(body).Contains("e.KeyModifiers != KeyModifiers.None");
        await Assert.That(body).Contains("IsLeftButtonPressed");
    }

    [Test]
    public async Task TheDragPointerHandlersNeverLetAnExceptionEscapeTheUiThread()
    {
        string source = await ReadAuthoringSourceAsync();

        // The move handler is the only async pointer handler left; the release and capture-lost
        // handlers are synchronous and hand the clear to one guarded async submit.
        foreach (string handler in new[]
        {
            "private async void OnPhysicsViewportPointerMoved",
            "private async Task SubmitPhysicsDragClearAsync",
        })
        {
            int start = source.IndexOf(handler, StringComparison.Ordinal);
            await Assert.That(start).IsGreaterThan(0);
            string body = source[start..Math.Min(source.Length, start + 2400)];
            await Assert.That(body).Contains("catch (Exception exception)");
        }

        await Assert.That(source).Contains("private void OnPhysicsViewportPointerReleased");
        await Assert.That(source).Contains("private void OnPhysicsViewportPointerCaptureLost");
    }

    [Test]
    public async Task TheDragRegistersOnTheViewportWithoutStealingHandledCameraGestures()
    {
        string source = await ReadAuthoringSourceAsync();

        int start = source.IndexOf(
            "ViewportHost.AddHandler(\n            PointerPressedEvent",
            StringComparison.Ordinal);
        if (start < 0)
        {
            start = source.IndexOf("ViewportHost.AddHandler(", StringComparison.Ordinal);
        }

        await Assert.That(start).IsGreaterThan(0);
        string body = source[start..Math.Min(source.Length, start + 900)];
        await Assert.That(body).Contains("handledEventsToo: false");
    }

    [Test]
    public async Task TheDragCapturesThePointerAndEndsThroughOneIdempotentPath()
    {
        string source = await ReadAuthoringSourceAsync();

        // Capture is what makes a release outside the viewport reach the viewer at all.
        await Assert.That(source).Contains("e.Pointer.Capture(ViewportHost)");
        await Assert.That(source).Contains("PointerCaptureLostEvent");
        await Assert.That(source).Contains("OnPhysicsViewportPointerCaptureLost");

        // Exactly one place produces the clear, so a release plus a capture loss cannot clear
        // twice and a deactivation cannot skip the clear altogether.
        await Assert.That(CountOccurrences(source, "_physicsDrag.TryEnd(")).IsEqualTo(1);
        await Assert.That(source).Contains("pointer?.Capture(null)");
        foreach (string reason in new[]
        {
            "ViewerPhysicsDragEnd.Released",
            "ViewerPhysicsDragEnd.CaptureLost",
            "ViewerPhysicsDragEnd.Abandoned",
        })
        {
            await Assert.That(source).Contains(reason);
        }
    }

    [Test]
    public async Task TheDragMoveVerifiesTheLeftButtonBeforeItPushesAnything()
    {
        string source = await ReadAuthoringSourceAsync();

        int start = source.IndexOf(
            "private async void OnPhysicsViewportPointerMoved",
            StringComparison.Ordinal);
        await Assert.That(start).IsGreaterThan(0);
        string body = source[start..];
        int end = body.IndexOf(
            "private void OnPhysicsViewportPointerReleased",
            StringComparison.Ordinal);
        body = body[..end];

        await Assert.That(body).Contains("IsLeftButtonPressed");
        await Assert.That(body).Contains("ViewerPhysicsDragStep.MustEnd");

        // A move that no longer owns a drag must not be marked handled, or hover stays suppressed
        // across the whole viewport for as long as the pointer keeps moving.
        int ignored = body.IndexOf("ViewerPhysicsDragStep.Ignored", StringComparison.Ordinal);
        int handled = body.IndexOf("e.Handled = true", StringComparison.Ordinal);
        await Assert.That(ignored).IsGreaterThan(0);
        await Assert.That(handled).IsGreaterThan(ignored);
    }

    [Test]
    public async Task LosingTheWindowEndsBothTheHeldKeysAndTheActiveDrag()
    {
        string source = await File.ReadAllTextAsync(Path.Combine(
            FindRepositoryRoot(),
            "src",
            "OpenUsd.Viewer",
            "MainWindow.axaml.cs"));

        int start = source.IndexOf(
            "private void OnWindowDeactivated",
            StringComparison.Ordinal);
        await Assert.That(start).IsGreaterThan(0);
        string body = source[start..Math.Min(source.Length, start + 900)];

        await Assert.That(body).Contains("ClearPhysicsControllerKeys()");
        await Assert.That(body).Contains("CancelPhysicsDrag()");
    }

    [Test]
    public async Task AKeyReleaseNeverGoesThroughThePressPolicy()
    {
        string source = await ReadAuthoringSourceAsync();

        int start = source.IndexOf(
            "private bool TryHandlePhysicsControllerKey",
            StringComparison.Ordinal);
        await Assert.That(start).IsGreaterThan(0);
        string body = source[start..Math.Min(source.Length, start + 1400)];

        // The release branch has to come first and must not consult the drive toggle, the
        // modifiers, or the text-input guard.
        int release = body.IndexOf("_physicsControllerKeys.TryRelease", StringComparison.Ordinal);
        int press = body.IndexOf("_physicsControllerKeys.TryPress", StringComparison.Ordinal);
        await Assert.That(release).IsGreaterThan(0);
        await Assert.That(press).IsGreaterThan(release);
        await Assert.That(body[..release]).DoesNotContain("IsCameraShortcutEditing");
        await Assert.That(body[..release]).DoesNotContain("PhysicsControllerDriveCheckBox");
    }

    [Test]
    public async Task HeldKeysAreDroppedWhenDrivingStopsOrTheWorldRefusesTheBatch()
    {
        string source = await ReadAuthoringSourceAsync();

        foreach (string site in new[]
        {
            "private void OnPhysicsControllerDriveChanged",
            "private async Task SubmitPhysicsDriveInputsAsync",
            "private void ResetPhysicsAuthoringUi",
        })
        {
            int start = source.IndexOf(site, StringComparison.Ordinal);
            await Assert.That(start).IsGreaterThan(0);
            string body = source[start..Math.Min(source.Length, start + 3400)];
            await Assert.That(body).Contains("_physicsControllerKeys.Clear()");
        }

        // A control the capability matrix or the selection disabled must not keep holding keys.
        int render = source.IndexOf(
            "PhysicsControllerDriveCheckBox.IsEnabled =",
            StringComparison.Ordinal);
        await Assert.That(render).IsGreaterThan(0);
        await Assert.That(source[render..Math.Min(source.Length, render + 900)])
            .Contains("_physicsControllerKeys.Clear()");
    }

    [Test]
    public async Task AReloadRestoresTheSelectionByIdentityRatherThanByPosition()
    {
        string source = await ReadAuthoringSourceAsync();

        int start = source.IndexOf(
            "private async Task ReloadPhysicsPropertiesAsync",
            StringComparison.Ordinal);
        await Assert.That(start).IsGreaterThan(0);
        string body = source[start..];
        int end = body.IndexOf("private void RebuildPhysicsObjectSelector", StringComparison.Ordinal);
        string reload = body[..(end < 0 ? body.Length : end)];

        // The anchor is captured before the load, so the rebuild has something to restore from.
        int capture = reload.IndexOf("CapturePhysicsSelection()", StringComparison.Ordinal);
        int load = reload.IndexOf("LoadInspectorAsync", StringComparison.Ordinal);
        await Assert.That(capture).IsGreaterThan(0);
        await Assert.That(load).IsGreaterThan(capture);
        await Assert.That(reload).Contains("RebuildPhysicsObjectSelector(anchor)");

        await Assert.That(source).Contains("ViewerPhysicsSelectionResolver.ResolveSection");
        await Assert.That(source).Contains("ViewerPhysicsSelectionResolver.ResolveRow");
        await Assert.That(source).DoesNotContain("PhysicsObjectSelector.SelectedIndex = 0;");
    }

    [Test]
    public async Task TheWindowSeesEveryKeyUpEvenWhenAFocusedControlConsumedIt()
    {
        string source = await File.ReadAllTextAsync(Path.Combine(
            FindRepositoryRoot(),
            "src",
            "OpenUsd.Viewer",
            "MainWindow.axaml.cs"));

        // A focused Button or CheckBox marks the Space release handled, so a plain KeyUp
        // subscription would never see it and the movement key would latch forever.
        await Assert.That(source).Contains("KeyUpEvent");
        int registration = source.IndexOf("KeyUpEvent", StringComparison.Ordinal);
        string body = source[registration..Math.Min(source.Length, registration + 400)];
        await Assert.That(body).Contains("OnWindowKeyUp");
        await Assert.That(body).Contains("RoutingStrategies.Tunnel | RoutingStrategies.Bubble");
        await Assert.That(body).Contains("handledEventsToo: true");

        // Exactly one registration: a routed handler plus the CLR event would run twice and, more
        // importantly, would leave the intent of the routed registration ambiguous.
        await Assert.That(source).DoesNotContain("KeyUp += OnWindowKeyUp");
        await Assert.That(CountOccurrences(source, "OnWindowKeyUp")).IsEqualTo(2);

        // The press path is untouched, so the press policy still gates modifiers and text focus.
        await Assert.That(source).Contains("KeyDown += OnWindowKeyDown");
    }

    [Test]
    public async Task FocusTransferToTheNativeChildClearsTheMovementKeys()
    {
        string source = await File.ReadAllTextAsync(Path.Combine(
            FindRepositoryRoot(),
            "src",
            "OpenUsd.Viewer",
            "MainWindow.axaml.cs"));

        // Every place that resets the camera repeat guard for a focus transfer also has to drop
        // the held movement keys, because the releases go to the native child instead.
        var missing = new List<string>();
        int index = source.IndexOf("ResetForFocusTransfer()", StringComparison.Ordinal);
        var checkedSites = 0;
        while (index >= 0)
        {
            string window = source[index..Math.Min(source.Length, index + 420)];
            if (window.Contains("ClearPhysicsControllerKeys()", StringComparison.Ordinal))
            {
                checkedSites++;
            }
            else
            {
                missing.Add(index.ToString(System.Globalization.CultureInfo.InvariantCulture));
            }

            index = source.IndexOf(
                "ResetForFocusTransfer()",
                index + 1,
                StringComparison.Ordinal);
        }

        // The pointer-press site keeps focus inside this window, so it is allowed not to clear.
        await Assert.That(checkedSites).IsGreaterThanOrEqualTo(2);
        await Assert.That(missing.Count).IsLessThanOrEqualTo(2);

        // Every focus transfer must also drop the discrete shortcut repeat state, or a key that
        // was down when focus left stays "held" and the next press is swallowed as a repeat.
        int camera = source.IndexOf(
            "_cameraShortcutRepeat.ResetForFocusTransfer()",
            StringComparison.Ordinal);
        while (camera >= 0)
        {
            string window = source[camera..Math.Min(source.Length, camera + 200)];
            await Assert.That(window).Contains("_physicsShortcutRepeat.ResetForFocusTransfer()");
            camera = source.IndexOf(
                "_cameraShortcutRepeat.ResetForFocusTransfer()",
                camera + 1,
                StringComparison.Ordinal);
        }
    }

    [Test]
    public async Task TheSelectionAnchorNamesAnObjectRatherThanAPath()
    {
        string source = await ReadAuthoringSourceAsync();

        // One prim yields several sections, so the anchor is built from the section itself.
        await Assert.That(source).Contains("ViewerPhysicsSelectionAnchor.For(");
        await Assert.That(source).DoesNotContain("new ViewerPhysicsSelectionAnchor(");

        // Interactions resolve their target from the address the composer gave the selected
        // section, never from the extractor's identity, which lives in a different space.
        await Assert.That(source).Contains("ViewerPhysicsController.ResolveCommandTarget(");
        await Assert.That(source).DoesNotContain("physics.ResolveIdentity(section.ObjectId");
        await Assert.That(source).DoesNotContain("physics.ResolveIdentity(section.PrimPath)");

        string state = await File.ReadAllTextAsync(Path.Combine(
            FindRepositoryRoot(),
            "src",
            "OpenUsd.Viewer",
            "ViewerPhysicsInteractionState.cs"));

        // The row lookup happens inside the resolved section only; a cross-section search would
        // happily find the same property name on a different object of the same prim.
        await Assert.That(state).DoesNotContain("ViewerPhysicsInspectorProjector.FindRow");
        await Assert.That(state).Contains("sections[section].Rows");
    }

    [Test]
    public async Task ABuildAttachesTheExtractedStageBeforeItComposesTheWorld()
    {
        string source = await File.ReadAllTextAsync(Path.Combine(
            FindRepositoryRoot(),
            "src",
            "OpenUsd.Viewer",
            "ViewerPhysicsTransport.cs"));

        int start = source.IndexOf(
            "public async Task BuildAsync(CancellationToken cancellationToken)",
            StringComparison.Ordinal);
        await Assert.That(start).IsGreaterThan(0);
        string body = source[start..Math.Min(source.Length, start + 900)];

        // A world with no extraction attached builds authored timeline metadata only: it steps, it
        // publishes, and every frame it publishes is empty. The attach has to precede the build.
        int extract = body.IndexOf("ExtractAsync(", StringComparison.Ordinal);
        int attach = body.IndexOf("AttachExtractionAsync(", StringComparison.Ordinal);
        int build = body.IndexOf("_transport.BuildAsync(", StringComparison.Ordinal);
        await Assert.That(extract).IsGreaterThan(0);
        await Assert.That(attach).IsGreaterThan(extract);
        await Assert.That(build).IsGreaterThan(attach);
    }

    [Test]
    public async Task DiscretePhysicsShortcutsAreGuardedAgainstOperatingSystemKeyRepeat()
    {
        string source = await File.ReadAllTextAsync(Path.Combine(
            FindRepositoryRoot(),
            "src",
            "OpenUsd.Viewer",
            "MainWindow.axaml.cs"));

        int down = source.IndexOf("private void OnWindowKeyDown", StringComparison.Ordinal);
        await Assert.That(down).IsGreaterThan(0);
        string body = source[down..];
        int end = body.IndexOf("private void OnWindowKeyUp", StringComparison.Ordinal);
        body = body[..(end < 0 ? body.Length : end)];

        // The press is recorded once for the whole handler, and the shortcut only runs on a press
        // the guard accepted. Recording it after the dispatch would let every repeat through.
        int press = body.IndexOf("_physicsShortcutRepeat.TryPress(e.Key)", StringComparison.Ordinal);
        int dispatch = body.IndexOf("TryHandlePhysicsShortcut(e)", StringComparison.Ordinal);
        await Assert.That(press).IsGreaterThan(0);
        await Assert.That(dispatch).IsGreaterThan(press);
        await Assert.That(body).Contains("firstPhysicsShortcutPress &&");

        // A refused repeat is swallowed rather than falling through to the play/pause binding.
        await Assert.That(body).Contains("!firstPhysicsShortcutPress &&");
        await Assert.That(body).Contains("IsPhysicsShortcutCandidate(e)");

        // The release path has to give the key back, or the command never runs a second time.
        int up = source.IndexOf("private void OnWindowKeyUp", StringComparison.Ordinal);
        string release = source[up..Math.Min(source.Length, up + 400)];
        await Assert.That(release).Contains("_physicsShortcutRepeat.Release(e.Key)");

        int deactivated = source.IndexOf(
            "private void OnWindowDeactivated",
            StringComparison.Ordinal);
        string lost = source[deactivated..Math.Min(source.Length, deactivated + 500)];
        await Assert.That(lost).Contains("_physicsShortcutRepeat.Reset()");
    }

    [Test]
    public async Task TheAuthoringOperationsRefuseToOverlap()
    {
        string source = await ReadAuthoringSourceAsync();

        int start = source.IndexOf(
            "private async Task RunPhysicsAuthoringAsync",
            StringComparison.Ordinal);
        await Assert.That(start).IsGreaterThan(0);
        string body = source[start..Math.Min(source.Length, start + 1600)];

        // Apply, clear, undo, and redo all funnel through here, so one guard covers all of them.
        // The reset lives in a finally, or a failed edit would wedge every later one.
        await Assert.That(body).Contains("if (_physicsAuthoringBusy)");
        await Assert.That(body).Contains("_physicsAuthoringBusy = true;");
        int final = body.IndexOf("finally", StringComparison.Ordinal);
        await Assert.That(final).IsGreaterThan(0);
        await Assert.That(body[final..]).Contains("_physicsAuthoringBusy = false;");

        foreach (string handler in new[]
        {
            "private async void OnPhysicsUndoClick",
            "private async void OnPhysicsRedoClick",
        })
        {
            int index = source.IndexOf(handler, StringComparison.Ordinal);
            await Assert.That(index).IsGreaterThan(0);
            await Assert.That(source[index..Math.Min(source.Length, index + 500)])
                .Contains("RunPhysicsAuthoringAsync");
        }

        // Apply and clear author through the same funnel, so they share the guard rather than
        // running their own unguarded copy of read, edit, and reload.
        int apply = source.IndexOf(
            "private Task ApplyPhysicsPropertyAsync",
            StringComparison.Ordinal);
        await Assert.That(apply).IsGreaterThan(0);
        await Assert.That(source[apply..Math.Min(source.Length, apply + 1400)])
            .Contains("RunPhysicsAuthoringAsync");
    }

    [Test]
    public async Task TheInteractionControlsFollowTheSelectedObjectAndNotOnlyTheCapabilities()
    {
        string source = await ReadAuthoringSourceAsync();

        int start = source.IndexOf(
            "private void RenderPhysicsAuthoringState",
            StringComparison.Ordinal);
        await Assert.That(start).IsGreaterThan(0);
        string body = source[start..Math.Min(source.Length, start + 3600)];

        // A vehicle section cannot take a force and a body section cannot take a driver input, so
        // the enable state has to consult the selection's own commandability.
        await Assert.That(body).Contains("Accepts(ViewerPhysicsCommandability.Body)");
        await Assert.That(body).Contains("Accepts(ViewerPhysicsCommandability.Controller)");
        await Assert.That(body).Contains("Accepts(ViewerPhysicsCommandability.Vehicle)");
        await Assert.That(body).Contains("bodies && commands && bodyTarget");

        // Changing the selection has to re-run the gating, or a control stays enabled for an
        // object that cannot receive it.
        int selection = source.IndexOf(
            "private void OnPhysicsObjectSelectionChanged",
            StringComparison.Ordinal);
        await Assert.That(selection).IsGreaterThan(0);
        await Assert.That(source[selection..Math.Min(source.Length, selection + 700)])
            .Contains("RenderPhysicsAuthoringState");
    }

    private static readonly Lazy<Task<string>> AuthoringSource = new(() =>
        File.ReadAllTextAsync(Path.Combine(
            FindRepositoryRoot(),
            "src",
            "OpenUsd.Viewer",
            "MainWindow.PhysicsAuthoring.cs")));

    private static readonly Lazy<Task<XDocument>> MainWindowMarkup = new(async () =>
        XDocument.Parse(await File.ReadAllTextAsync(Path.Combine(
            FindRepositoryRoot(),
            "src",
            "OpenUsd.Viewer",
            "MainWindow.axaml"))));

    /// <summary>
    /// Reads the authoring source once for the whole class.
    /// </summary>
    /// <remarks>
    /// These source-contract tests all read the same two files. Re-reading them per test put a
    /// dozen more file operations on the shared scheduler, which is enough to perturb the
    /// allocation measurements that run alongside them.
    /// </remarks>
    private static Task<string> ReadAuthoringSourceAsync() => AuthoringSource.Value;

    private static int CountOccurrences(string source, string value)
    {
        var count = 0;
        int index = source.IndexOf(value, StringComparison.Ordinal);
        while (index >= 0)
        {
            count++;
            index = source.IndexOf(value, index + value.Length, StringComparison.Ordinal);
        }

        return count;
    }

    private static Task<XDocument> LoadMainWindowMarkupAsync() => MainWindowMarkup.Value;

    private static XElement FindByName(XDocument markup, string name)
    {
        foreach (XElement element in markup.Descendants())
        {
            foreach (XAttribute attribute in element.Attributes())
            {
                if (attribute.Name.LocalName == "Name" && attribute.Value == name)
                {
                    return element;
                }
            }
        }

        throw new InvalidOperationException($"MainWindow.axaml has no control named '{name}'.");
    }

    private static string FindRepositoryRoot()
    {
        string currentDirectory = Environment.CurrentDirectory;
        if (File.Exists(Path.Combine(currentDirectory, "OpenUsd.slnx")))
        {
            return currentDirectory;
        }

        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "OpenUsd.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate the OpenUSD repository root.");
    }
}
