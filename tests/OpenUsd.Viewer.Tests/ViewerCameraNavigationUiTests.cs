// Copyright (c) marcschier. Licensed under the MIT License.

using System.Numerics;
using System.Runtime.CompilerServices;
using Avalonia.Input;
using OpenUsd.Geom;
using OpenUsd.Rendering;
using OpenUsd.Rendering.Storm;

namespace OpenUsd.Viewer.Tests;

public sealed class ViewerCameraNavigationUiTests
{
    private const int AllocationIterations = 1000;

    [Test]
    public async Task PointerGestureClassifierReservesPlainLeftAndMapsAltButtons()
    {
        await Assert.That(ViewerCameraGestureClassifier.Classify(
            KeyModifiers.None,
            ViewerPointerButtons.Left)).IsEqualTo(ViewerCameraPointerGesture.None);
        await Assert.That(ViewerCameraGestureClassifier.Classify(
            KeyModifiers.Alt,
            ViewerPointerButtons.Left)).IsEqualTo(ViewerCameraPointerGesture.Orbit);
        await Assert.That(ViewerCameraGestureClassifier.Classify(
            KeyModifiers.Alt,
            ViewerPointerButtons.Middle)).IsEqualTo(ViewerCameraPointerGesture.Pan);
        await Assert.That(ViewerCameraGestureClassifier.Classify(
            KeyModifiers.Alt,
            ViewerPointerButtons.Right)).IsEqualTo(ViewerCameraPointerGesture.Dolly);
        await Assert.That(ViewerCameraGestureClassifier.Classify(
            KeyModifiers.Alt | KeyModifiers.Shift,
            ViewerPointerButtons.Left)).IsEqualTo(ViewerCameraPointerGesture.None);
        await Assert.That(ViewerCameraGestureClassifier.Classify(
            KeyModifiers.Alt,
            ViewerPointerButtons.Left | ViewerPointerButtons.Right))
            .IsEqualTo(ViewerCameraPointerGesture.None);
    }

    [Test]
    public async Task StormNavigationTrackerMapsGesturesWheelAndCommands()
    {
        var tracker = new ViewerStormNavigationInputTracker();
        ViewerStormNavigationDelta baseline = tracker.Update(
            Navigation(
                sequence: 10,
                x: 10,
                y: 20,
                buttons: OpenUsdStormPointerButtons.Left,
                modifiers: OpenUsdStormInputModifiers.Alt),
            routedInputGeneration: 0);
        ViewerStormNavigationDelta moved = tracker.Update(
            Navigation(
                sequence: 11,
                x: 24,
                y: 29,
                buttons: OpenUsdStormPointerButtons.Left,
                modifiers: OpenUsdStormInputModifiers.Alt,
                wheel: 1.5,
                frame: 2,
                home: 3,
                projection: 4,
                left: 5,
                right: 6,
                up: 7,
                down: 8),
            routedInputGeneration: 0);

        await Assert.That(baseline.ResetPointerGesture).IsTrue();
        await Assert.That(moved.SequenceDelta).IsEqualTo(1UL);
        await Assert.That(moved.Gesture)
            .IsEqualTo(ViewerCameraPointerGesture.Orbit);
        await Assert.That(moved.PointerDelta).IsEqualTo(new Vector2(14, 9));
        await Assert.That(moved.WheelDelta).IsEqualTo(1.5f);
        await Assert.That(moved.FrameSelectedPresses).IsEqualTo(2UL);
        await Assert.That(moved.ResetAutomaticPresses).IsEqualTo(3UL);
        await Assert.That(moved.ToggleProjectionPresses).IsEqualTo(4UL);
        await Assert.That(moved.OrbitLeftPresses).IsEqualTo(5UL);
        await Assert.That(moved.OrbitRightPresses).IsEqualTo(6UL);
        await Assert.That(moved.OrbitUpPresses).IsEqualTo(7UL);
        await Assert.That(moved.OrbitDownPresses).IsEqualTo(8UL);
        await Assert.That(moved.HasCameraMutation).IsTrue();

        tracker.Reset();
        _ = tracker.Update(
            Navigation(
                20,
                1,
                2,
                OpenUsdStormPointerButtons.Middle,
                OpenUsdStormInputModifiers.Alt),
            0);
        ViewerStormNavigationDelta panned = tracker.Update(
            Navigation(
                21,
                4,
                8,
                OpenUsdStormPointerButtons.Middle,
                OpenUsdStormInputModifiers.Alt,
                inside: false),
            0);
        await Assert.That(panned.Gesture)
            .IsEqualTo(ViewerCameraPointerGesture.Pan);

        tracker.Reset();
        _ = tracker.Update(
            Navigation(
                30,
                1,
                2,
                OpenUsdStormPointerButtons.Right,
                OpenUsdStormInputModifiers.Alt),
            0);
        ViewerStormNavigationDelta dollied = tracker.Update(
            Navigation(
                31,
                4,
                8,
                OpenUsdStormPointerButtons.Right,
                OpenUsdStormInputModifiers.Alt),
            0);
        await Assert.That(dollied.Gesture)
            .IsEqualTo(ViewerCameraPointerGesture.Dolly);
    }

    [Test]
    public async Task StormNavigationTrackingAllocatesNothingAfterWarmup()
    {
        var tracker = new ViewerStormNavigationInputTracker();
        _ = tracker.Update(
            Navigation(
                1,
                0,
                0,
                OpenUsdStormPointerButtons.Left,
                OpenUsdStormInputModifiers.Alt),
            0);
        int warmed = AllocationWarmup.UntilQuiet(step =>
        {
            _ = tracker.Update(
                Navigation(
                    checked((ulong)step + 2),
                    step + 1,
                    step + 2,
                    OpenUsdStormPointerButtons.Left,
                    OpenUsdStormInputModifiers.Alt),
                0);
        });

        float deltaSum = 0;
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int index = 0; index < AllocationIterations; index++)
        {
            int step = warmed + index;
            ViewerStormNavigationDelta delta = tracker.Update(
                Navigation(
                    checked((ulong)step + 2),
                    step + 1,
                    step + 2,
                    OpenUsdStormPointerButtons.Left,
                    OpenUsdStormInputModifiers.Alt),
                0);
            deltaSum += delta.PointerDelta.X;
        }
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        await Assert.That(allocated).IsEqualTo(0);
        await Assert.That(deltaSum).IsGreaterThan(0);
    }

    [Test]
    public async Task StormNavigationTrackerHandlesWrapFocusAndRoutedDuplicates()
    {
        var tracker = new ViewerStormNavigationInputTracker();
        _ = tracker.Update(
            Navigation(
                ulong.MaxValue - 1,
                10,
                10,
                OpenUsdStormPointerButtons.Left,
                OpenUsdStormInputModifiers.Alt,
                frame: ulong.MaxValue - 1,
                home: ulong.MaxValue - 1,
                projection: ulong.MaxValue - 1),
            0);
        ViewerStormNavigationDelta wrapped = tracker.Update(
            Navigation(
                1,
                12,
                14,
                OpenUsdStormPointerButtons.Left,
                OpenUsdStormInputModifiers.Alt,
                frame: 1,
                home: 1,
                projection: 1),
            0);
        await Assert.That(wrapped.SequenceDelta).IsEqualTo(3UL);
        await Assert.That(wrapped.FrameSelectedPresses).IsEqualTo(3UL);
        await Assert.That(wrapped.ResetAutomaticPresses).IsEqualTo(3UL);
        await Assert.That(wrapped.ToggleProjectionPresses).IsEqualTo(3UL);

        ViewerStormNavigationDelta duplicate = tracker.Update(
            Navigation(
                2,
                20,
                30,
                OpenUsdStormPointerButtons.Left,
                OpenUsdStormInputModifiers.Alt),
            routedInputGeneration: 1);
        await Assert.That(duplicate.ResetPointerGesture).IsTrue();
        await Assert.That(duplicate.Gesture)
            .IsEqualTo(ViewerCameraPointerGesture.None);

        ViewerStormNavigationDelta lostFocus = tracker.Update(
            Navigation(
                3,
                100,
                100,
                OpenUsdStormPointerButtons.None,
                OpenUsdStormInputModifiers.None,
                focused: false),
            routedInputGeneration: 1);
        await Assert.That(lostFocus.ResetPointerGesture).IsTrue();
        await Assert.That(lostFocus.WheelDelta).IsEqualTo(0);

        ViewerStormNavigationDelta regainedFocus = tracker.Update(
            Navigation(
                4,
                200,
                200,
                OpenUsdStormPointerButtons.Left,
                OpenUsdStormInputModifiers.Alt),
            routedInputGeneration: 1);
        await Assert.That(regainedFocus.ResetPointerGesture).IsTrue();
        await Assert.That(regainedFocus.PointerDelta).IsEqualTo(Vector2.Zero);
    }

    [Test]
    public async Task ShortcutPolicySkipsEditingAndExistingModifiedShortcuts()
    {
        await Assert.That(ViewerCameraShortcutPolicy.Classify(
            Key.F,
            KeyModifiers.None,
            isEditing: false)).IsEqualTo(ViewerCameraShortcut.FrameSelected);
        await Assert.That(ViewerCameraShortcutPolicy.Classify(
            Key.Home,
            KeyModifiers.None,
            isEditing: false)).IsEqualTo(ViewerCameraShortcut.ResetAutomatic);
        await Assert.That(ViewerCameraShortcutPolicy.Classify(
            Key.P,
            KeyModifiers.None,
            isEditing: false)).IsEqualTo(ViewerCameraShortcut.ToggleProjection);
        await Assert.That(ViewerCameraShortcutPolicy.Classify(
            Key.F,
            KeyModifiers.None,
            isEditing: true)).IsEqualTo(ViewerCameraShortcut.None);
        await Assert.That(ViewerCameraShortcutPolicy.Classify(
            Key.P,
            KeyModifiers.Control,
            isEditing: false)).IsEqualTo(ViewerCameraShortcut.None);
        await Assert.That(ViewerCameraShortcutPolicy.Classify(
            Key.Space,
            KeyModifiers.None,
            isEditing: false)).IsEqualTo(ViewerCameraShortcut.None);
        await Assert.That(ViewerCameraShortcutPolicy.Classify(
            Key.D1,
            KeyModifiers.Control,
            isEditing: false)).IsEqualTo(ViewerCameraShortcut.None);
        await Assert.That(ViewerCameraShortcutPolicy.Classify(
            Key.Left,
            KeyModifiers.None,
            isEditing: false,
            isViewportFocused: true)).IsEqualTo(ViewerCameraShortcut.OrbitLeft);
        await Assert.That(ViewerCameraShortcutPolicy.Classify(
            Key.Right,
            KeyModifiers.None,
            isEditing: false,
            isViewportFocused: true)).IsEqualTo(ViewerCameraShortcut.OrbitRight);
        await Assert.That(ViewerCameraShortcutPolicy.Classify(
            Key.Up,
            KeyModifiers.None,
            isEditing: false,
            isViewportFocused: true)).IsEqualTo(ViewerCameraShortcut.OrbitUp);
        await Assert.That(ViewerCameraShortcutPolicy.Classify(
            Key.Down,
            KeyModifiers.None,
            isEditing: false,
            isViewportFocused: true)).IsEqualTo(ViewerCameraShortcut.OrbitDown);
        await Assert.That(ViewerCameraShortcutPolicy.Classify(
            Key.Left,
            KeyModifiers.None,
            isEditing: false,
            isViewportFocused: false)).IsEqualTo(ViewerCameraShortcut.None);
        await Assert.That(ViewerCameraShortcutPolicy.Classify(
            Key.Right,
            KeyModifiers.Shift,
            isEditing: false,
            isViewportFocused: true)).IsEqualTo(ViewerCameraShortcut.None);
    }

    [Test]
    public async Task CameraShortcutRepeatGuardRequiresReleaseBeforeRepress()
    {
        var guard = new ViewerCameraShortcutRepeatGuard();
        foreach (Key key in new[] { Key.F, Key.Home, Key.P })
        {
            await Assert.That(guard.TryPress(key)).IsTrue();
            await Assert.That(guard.TryPress(key)).IsFalse();
            await Assert.That(guard.TryPress(key)).IsFalse();
            guard.Release(key);
            await Assert.That(guard.TryPress(key)).IsTrue();
            guard.Release(key);
        }

        await Assert.That(guard.TryPress(Key.Space)).IsTrue();
        await Assert.That(guard.TryPress(Key.F)).IsTrue();
        await Assert.That(guard.TryPress(Key.Left)).IsTrue();
        await Assert.That(guard.TryPress(Key.Left)).IsTrue();
        guard.Reset();
        await Assert.That(guard.TryPress(Key.F)).IsTrue();
    }

    [Test]
    public async Task CameraShortcutLatchRecoversWhenKeyUpMovesToNativeChild()
    {
        var guard = new ViewerCameraShortcutRepeatGuard();
        foreach (Key key in new[] { Key.F, Key.Home, Key.P })
        {
            await Assert.That(guard.TryPress(key)).IsTrue();
            await Assert.That(guard.TryPress(key)).IsFalse();

            // Focus moved to the native child, so managed code never receives KeyUp.
            guard.ResetForFocusTransfer();

            await Assert.That(guard.TryPress(key)).IsTrue();
            await Assert.That(guard.TryPress(key)).IsFalse();
            guard.Release(key);
            await Assert.That(guard.TryPress(key)).IsTrue();
            guard.Release(key);
        }
    }

    [Test]
    public async Task CameraInputStatusKeepsStormAndCompositionNavigationAvailable()
    {
        const string cameraStatus = "Camera: Perspective";

        string stormStatus = ViewerCameraInputAvailability.FormatStatus(
            cameraStatus,
            RenderBackendKind.Storm);
        string compositionStatus = ViewerCameraInputAvailability.FormatStatus(
            cameraStatus,
            RenderBackendKind.D3D12);

        await Assert.That(stormStatus).IsEqualTo(cameraStatus);
        await Assert.That(compositionStatus).IsEqualTo(cameraStatus);
    }

    [Test]
    public async Task PurposeMappingPreservesEveryRenderPurposeFlag()
    {
        await Assert.That(ViewerCameraPurposeMapping.ToUsdGeomPurposeMask(
            RenderPurpose.None)).IsEqualTo(UsdGeomPurposeMask.None);
        await Assert.That(ViewerCameraPurposeMapping.ToUsdGeomPurposeMask(
            RenderPurpose.Default)).IsEqualTo(UsdGeomPurposeMask.Default);
        await Assert.That(ViewerCameraPurposeMapping.ToUsdGeomPurposeMask(
            RenderPurpose.Proxy)).IsEqualTo(UsdGeomPurposeMask.Proxy);
        await Assert.That(ViewerCameraPurposeMapping.ToUsdGeomPurposeMask(
            RenderPurpose.Render)).IsEqualTo(UsdGeomPurposeMask.Render);
        await Assert.That(ViewerCameraPurposeMapping.ToUsdGeomPurposeMask(
            RenderPurpose.Guide)).IsEqualTo(UsdGeomPurposeMask.Guide);
        await Assert.That(ViewerCameraPurposeMapping.ToUsdGeomPurposeMask(
            RenderPurpose.Default |
            RenderPurpose.Proxy |
            RenderPurpose.Render |
            RenderPurpose.Guide)).IsEqualTo(UsdGeomPurposeMask.All);
        await Assert.That(() => ViewerCameraPurposeMapping.ToUsdGeomPurposeMask(
            (RenderPurpose)(1 << 20))).Throws<ArgumentOutOfRangeException>();
    }

    [Test]
    public async Task ResizeMutationPublishesViewportAndChangedExplicitProjectionTogether()
    {
        var verifier = new TestUiThreadVerifier(isUiThread: true);
        var controller = new ViewerCameraNavigationController(
            new ViewportDimensions(800, 600));
        var adapter = new ViewerCameraNavigationUiAdapter(controller, verifier);
        adapter.ResetToExplicitPose();
        CameraState beforeCamera = adapter.Camera;
        StageRenderState before = StageRenderState
            .Create(new StageIdentity("stage"))
            .WithViewport(new ViewportDimensions(800, 600))
            .WithCamera(beforeCamera);

        ViewerCameraResizeUpdate update = adapter.Resize(
            new ViewportDimensions(1920, 1080));
        StageRenderState after = ViewerCameraStateMutation.ApplyResize(before, update);

        await Assert.That(update.ViewportChanged).IsTrue();
        await Assert.That(update.CameraChanged).IsTrue();
        await Assert.That(after.Viewport).IsEqualTo(new ViewportDimensions(1920, 1080));
        await Assert.That(after.Camera).IsEqualTo(update.Camera);
        await Assert.That(after.Camera).IsNotEqualTo(beforeCamera);

        ViewerCameraResizeUpdate sameAspect = adapter.Resize(
            new ViewportDimensions(3840, 2160));
        await Assert.That(sameAspect.ViewportChanged).IsTrue();
        await Assert.That(sameAspect.CameraChanged).IsFalse();

        var automaticController = new ViewerCameraNavigationController(
            new ViewportDimensions(800, 600));
        var automaticAdapter = new ViewerCameraNavigationUiAdapter(
            automaticController,
            verifier);
        ViewerCameraResizeUpdate automatic = automaticAdapter.Resize(
            new ViewportDimensions(1920, 1080));
        StageRenderState automaticState = ViewerCameraStateMutation.ApplyResize(
            StageRenderState.Create(new StageIdentity("automatic")),
            automatic);
        await Assert.That(automatic.CameraChanged).IsFalse();
        await Assert.That(automaticState.Camera).IsEqualTo(CameraState.Default);
        await Assert.That(automaticState.Viewport)
            .IsEqualTo(new ViewportDimensions(1920, 1080));
    }

    [Test]
    public async Task UiAdapterRejectsControllerReadsAndMutationsOffUiThread()
    {
        var verifier = new TestUiThreadVerifier(isUiThread: false);
        var controller = new ViewerCameraNavigationController(
            new ViewportDimensions(800, 600));
        var adapter = new ViewerCameraNavigationUiAdapter(controller, verifier);

        await Assert.That(() => _ = adapter.State)
            .Throws<InvalidOperationException>();
        await Assert.That(adapter.ResetToExplicitPose)
            .Throws<InvalidOperationException>();
        await Assert.That(() => adapter.OrbitStep(ViewerCameraOrbitCommand.Left))
            .Throws<InvalidOperationException>();
        await Assert.That(controller.Camera).IsEqualTo(CameraState.Default);

        verifier.IsUiThread = true;
        await Assert.That(adapter.ResetToExplicitPose()).IsTrue();
        await Assert.That(adapter.Camera.Mode).IsEqualTo(CameraMode.Matrices);
    }

    [Test]
    public async Task OrbitStepsChangeOneAxisAndProduceExplicitCameraMatrices()
    {
        var verifier = new TestUiThreadVerifier(isUiThread: true);
        var controller = new ViewerCameraNavigationController(
            new ViewportDimensions(800, 600));
        var adapter = new ViewerCameraNavigationUiAdapter(controller, verifier);
        ViewerCameraNavigationState automatic = adapter.State;

        await Assert.That(adapter.OrbitStep(ViewerCameraOrbitCommand.Right)).IsTrue();
        ViewerCameraNavigationState right = adapter.State;
        await Assert.That(right.IsAutomatic).IsFalse();
        await Assert.That(right.Yaw).IsGreaterThan(automatic.Yaw);
        await Assert.That(right.Pitch).IsEqualTo(automatic.Pitch);
        await Assert.That(right.Target).IsEqualTo(automatic.Target);
        await Assert.That(right.Distance).IsEqualTo(automatic.Distance);
        await Assert.That(adapter.Camera.Mode).IsEqualTo(CameraMode.Matrices);
        await Assert.That(adapter.Camera.View)
            .IsNotEqualTo(Matrix4x4.CreateLookAt(
                new Vector3(4f, 3f, 4f),
                Vector3.Zero,
                Vector3.UnitY));

        await Assert.That(adapter.OrbitStep(ViewerCameraOrbitCommand.Up)).IsTrue();
        ViewerCameraNavigationState up = adapter.State;
        await Assert.That(up.Yaw).IsEqualTo(right.Yaw);
        await Assert.That(up.Pitch).IsGreaterThan(right.Pitch);

        await Assert.That(adapter.OrbitStep(ViewerCameraOrbitCommand.Down)).IsTrue();
        await Assert.That(adapter.State.Pitch).IsLessThan(up.Pitch);
        await Assert.That(() => adapter.OrbitStep((ViewerCameraOrbitCommand)int.MaxValue))
            .Throws<ArgumentOutOfRangeException>();

        await Assert.That(adapter.OrbitSteps(2, 5, 7, 3)).IsTrue();
        await Assert.That(adapter.State.Yaw).IsGreaterThan(right.Yaw);
    }

    [Test]
    public async Task FrameSelectedQueryHandlesNoSelectionMissingEmptyAndReadyOnce()
    {
        var unused = new TestSelectedBoundsSource(
            new ViewerSelectedBoundsSourceResult(true, CreateBounds()));
        ViewerFrameSelectedResult noSelection = await ViewerFrameSelectedQuery.QueryAsync(
            unused,
            selectedPrimPath: null,
            new StageTime(12.5),
            RenderPurpose.Default,
            CancellationToken.None);
        await Assert.That(noSelection.Outcome)
            .IsEqualTo(ViewerFrameSelectedOutcome.NoSelection);
        await Assert.That(unused.QueryCount).IsEqualTo(0);

        var missing = new TestSelectedBoundsSource(
            new ViewerSelectedBoundsSourceResult(false, UsdBounds3d.Empty));
        ViewerFrameSelectedResult missingResult =
            await ViewerFrameSelectedQuery.QueryAsync(
                missing,
                "/World/Missing",
                new StageTime(12.5),
                RenderPurpose.Default,
                CancellationToken.None);
        await Assert.That(missingResult.Outcome)
            .IsEqualTo(ViewerFrameSelectedOutcome.MissingPrim);
        await Assert.That(missing.QueryCount).IsEqualTo(1);

        var empty = new TestSelectedBoundsSource(
            new ViewerSelectedBoundsSourceResult(true, UsdBounds3d.Empty));
        ViewerFrameSelectedResult emptyResult =
            await ViewerFrameSelectedQuery.QueryAsync(
                empty,
                "/World/Empty",
                new StageTime(24.5),
                RenderPurpose.Proxy,
                CancellationToken.None);
        await Assert.That(emptyResult.Outcome)
            .IsEqualTo(ViewerFrameSelectedOutcome.EmptyBounds);
        await Assert.That(empty.QueryCount).IsEqualTo(1);

        UsdBounds3d bounds = CreateBounds();
        var ready = new TestSelectedBoundsSource(
            new ViewerSelectedBoundsSourceResult(true, bounds));
        ViewerFrameSelectedResult readyResult =
            await ViewerFrameSelectedQuery.QueryAsync(
                ready,
                "/World/Mesh",
                new StageTime(48.25),
                RenderPurpose.Default | RenderPurpose.Render,
                CancellationToken.None);
        await Assert.That(readyResult.Outcome)
            .IsEqualTo(ViewerFrameSelectedOutcome.Ready);
        await Assert.That(readyResult.Bounds).IsEqualTo(bounds);
        await Assert.That(ready.QueryCount).IsEqualTo(1);
        await Assert.That(ready.LastRequest).IsEqualTo(
            new ViewerSelectedBoundsRequest(
                "/World/Mesh",
                48.25,
                UsdGeomPurposeMask.Default | UsdGeomPurposeMask.Render));
    }

    [Test]
    public async Task CameraUpdatePumpCoalescesPendingSnapshotsLatestWins()
    {
        var entered = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var completed = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var applied = new List<float>();
        var failures = new List<Exception>();
        var pump = new ViewerCameraUpdatePump(
            async (camera, cancellationToken) =>
            {
                float marker = camera.View.M41;
                lock (applied)
                {
                    applied.Add(marker);
                }
                if (marker == 1f)
                {
                    entered.TrySetResult();
                    await release.Task.WaitAsync(cancellationToken);
                }
                if (marker == 3f)
                {
                    completed.TrySetResult();
                }
            },
            failures.Add,
            CancellationToken.None);

        await Assert.That(pump.TryPost(CreateCamera(1f))).IsTrue();
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await Assert.That(pump.TryPost(CreateCamera(2f))).IsTrue();
        await Assert.That(pump.TryPost(CreateCamera(3f))).IsTrue();
        release.TrySetResult();
        await completed.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await pump.DisposeAsync();

        await Assert.That(applied).IsEquivalentTo([1f, 3f]);
        await Assert.That(failures).IsEmpty();
        await Assert.That(pump.TryPost(CreateCamera(4f))).IsFalse();
    }

    [Test]
    public async Task CameraUpdatePumpCancelsInFlightApplyAndStopsAccepting()
    {
        using var documentLifetime = new CancellationTokenSource();
        var entered = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var cancellationObserved = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var failures = new List<Exception>();
        var pump = new ViewerCameraUpdatePump(
            async (_, cancellationToken) =>
            {
                entered.TrySetResult();
                try
                {
                    await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                }
                finally
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        cancellationObserved.TrySetResult();
                    }
                }
            },
            failures.Add,
            documentLifetime.Token);

        await Assert.That(pump.TryPost(CreateCamera(1f))).IsTrue();
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        documentLifetime.Cancel();
        await cancellationObserved.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await Assert.That(pump.TryPost(CreateCamera(2f))).IsFalse();
        await pump.DisposeAsync();
        await Assert.That(failures).IsEmpty();
    }

    [Test]
    public async Task CameraUpdatePumpPostingAllocatesNothingAfterWarmup()
    {
        var entered = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var pump = new ViewerCameraUpdatePump(
            async (_, cancellationToken) =>
            {
                entered.TrySetResult();
                await release.Task.WaitAsync(cancellationToken);
            },
            static _ => { },
            CancellationToken.None);
        CameraState camera = CreateCamera(1f);
        await Assert.That(pump.TryPost(camera)).IsTrue();
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        AllocationWarmup.UntilQuiet(_ => PostCamera(pump, camera));

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int index = 0; index < AllocationIterations; index++)
        {
            PostCamera(pump, camera);
        }
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        release.TrySetResult();
        await pump.DisposeAsync();
        await Assert.That(allocated).IsEqualTo(0);
    }

    [Test]
    public async Task ViewerSourceWiresAccessibleControlsBoundedRoutingAndEvidenceGuard()
    {
        string root = FindRepositoryRoot();
        string markup = await File.ReadAllTextAsync(Path.Combine(
            root,
            "src",
            "OpenUsd.Viewer",
            "MainWindow.axaml"));
        string window = await File.ReadAllTextAsync(Path.Combine(
            root,
            "src",
            "OpenUsd.Viewer",
            "MainWindow.axaml.cs"));
        string integration = await File.ReadAllTextAsync(Path.Combine(
            root,
            "src",
            "OpenUsd.Viewer",
            "ViewerCameraNavigationUi.cs"));

        await Assert.That(markup).Contains("x:Name=\"ResetCameraAutomaticButton\"");
        await Assert.That(markup).Contains("x:Name=\"ResetCameraLegacyButton\"");
        await Assert.That(markup).Contains("x:Name=\"ToggleCameraProjectionButton\"");
        await Assert.That(markup).Contains("x:Name=\"UseSelectedCameraButton\"");
        await Assert.That(markup).Contains("x:Name=\"FrameSelectedButton\"");
        await Assert.That(markup).Contains("x:Name=\"CameraOrbitLeftButton\"");
        await Assert.That(markup).Contains("x:Name=\"CameraOrbitRightButton\"");
        await Assert.That(markup).Contains("x:Name=\"CameraOrbitUpButton\"");
        await Assert.That(markup).Contains("x:Name=\"CameraOrbitDownButton\"");
        await Assert.That(markup).Contains("x:Name=\"CameraStatus\"");
        await Assert.That(markup).Contains("AutomationProperties.Name=\"Frame selected prim\"");
        await Assert.That(markup).Contains("x:Name=\"ViewerToolbarGrid\"");
        await Assert.That(markup).Contains("RowDefinitions=\"Auto,Auto\"");
        await Assert.That(markup).Contains("Grid.ColumnSpan=\"5\"");
        await Assert.That(markup).Contains("TextTrimming=\"CharacterEllipsis\"");
        await Assert.That(markup).Contains("RowDefinitions=\"32,32\"");
        await Assert.That(markup).Contains("ColumnDefinitions=\"32,32,32\"");
        await Assert.That(markup).Contains("<Setter Property=\"Padding\" Value=\"0\" />");
        await Assert.That(markup).Contains("<Setter Property=\"FontSize\" Value=\"18\" />");
        await Assert.That(window).Contains("RegisterCameraInputHandlers(this);");
        await Assert.That(window).Contains("RegisterCameraInputHandlers(ViewportHost);");
        await Assert.That(window).Contains("KeyUp += OnWindowKeyUp;");
        await Assert.That(window).Contains("_cameraShortcutRepeat.TryPress(e.Key)");
        await Assert.That(window).Contains("ViewportHost.IsKeyboardFocusWithin");
        await Assert.That(window).Contains("OrbitCamera(command);");
        await Assert.That(integration).Contains("ViewerCameraShortcutRepeatGuard");
        await Assert.That(integration).Contains("ResetForFocusTransfer");
        string pointerPress = SliceMethod(
            window,
            "private void OnCameraPointerPressed",
            "private void OnCameraPointerMoved");
        int resetBeforeFocus = pointerPress.IndexOf(
            "_cameraShortcutRepeat.ResetForFocusTransfer();",
            StringComparison.Ordinal);
        int viewportFocus = pointerPress.IndexOf(
            "ViewportHost.Focus()",
            StringComparison.Ordinal);
        await Assert.That(resetBeforeFocus).IsGreaterThanOrEqualTo(0);
        await Assert.That(viewportFocus).IsGreaterThan(resetBeforeFocus);
        string nativePoll = SliceMethod(
            window,
            "private void OnStormNavigationTick",
            "private void InitializeCameraUpdates");
        await Assert.That(nativePoll).Contains("if (input.Focused)");
        await Assert.That(nativePoll).Contains(
            "_cameraShortcutRepeat.ResetForFocusTransfer();");
        await Assert.That(window).Contains(
            "RoutingStrategies.Tunnel | RoutingStrategies.Bubble");
        await Assert.That(window).Contains("handledEventsToo: true");
        await Assert.That(window).Contains(
            "ViewerCameraStateMutation.ApplyResize(state, cameraResize)");
        await Assert.That(window).Contains("if (IsAutomatedViewerRun())");
        await Assert.That(window).Contains("ViewerFrameSelectedQuery.QueryAsync(");
        await Assert.That(integration).Contains(
            "private CameraState _pending;");
        await Assert.That(integration).DoesNotContain("GetWorldTransform");
        await Assert.That(integration).DoesNotContain(
            "ViewerSchedulerStageCameraSource");
    }

    [Test]
    public async Task ResizeSourceDoesNotRepublishAlreadyAppliedCamera()
    {
        string root = FindRepositoryRoot();
        string window = await File.ReadAllTextAsync(Path.Combine(
            root,
            "src",
            "OpenUsd.Viewer",
            "MainWindow.axaml.cs"));
        string resize = SliceMethod(
            window,
            "private async Task UpdateViewportStateAsync",
            "private async Task RunRenderLoopAsync");

        await Assert.That(resize).Contains(
            "ViewerCameraStateMutation.ApplyResize(state, cameraResize)");
        await Assert.That(resize).DoesNotContain("_cameraUpdates");
        await Assert.That(resize).DoesNotContain(
            "TryPost(cameraResize.Camera)");
    }

    [Test]
    public async Task MainWindowKeepsNativeStormBridgeAndToolbarCommandsVisible()
    {
        string root = FindRepositoryRoot();
        string markup = await File.ReadAllTextAsync(Path.Combine(
            root,
            "src",
            "OpenUsd.Viewer",
            "MainWindow.axaml"));
        string window = await File.ReadAllTextAsync(Path.Combine(
            root,
            "src",
            "OpenUsd.Viewer",
            "MainWindow.axaml.cs"));
        string documentation = await File.ReadAllTextAsync(Path.Combine(
            root,
            "docs",
            "viewer.md"));
        string availability = SliceMethod(
            window,
            "private void UpdateCameraAvailability",
            "private void UpdateCameraStatus");

        await Assert.That(window).Contains(
            "ViewerCameraInputAvailability.FormatStatus(");
        await Assert.That(window).Contains("OnStormNavigationTick");
        await Assert.That(window).Contains("TryGetNavigationInput");
        await Assert.That(window).Contains(
            "private void SetActiveBackendStatus()");
        await Assert.That(window).Contains("UpdateCameraStatus();");
        await Assert.That(markup).Contains(
            "x:Name=\"ResetCameraAutomaticButton\"");
        await Assert.That(markup).Contains(
            "x:Name=\"UseSelectedCameraButton\"");
        await Assert.That(markup).Contains(
            "x:Name=\"FrameSelectedButton\"");
        await Assert.That(availability).Contains(
            "ResetCameraAutomaticButton.IsEnabled = enabled;");
        await Assert.That(availability).Contains(
            "ToggleCameraProjectionButton.IsEnabled = enabled;");
        await Assert.That(availability).Contains(
            "FrameSelectedButton.IsEnabled = enabled;");
        await Assert.That(availability).Contains(
            "CameraOrbitLeftButton.IsEnabled = enabled;");
        await Assert.That(availability).Contains(
            "CameraOrbitRightButton.IsEnabled = enabled;");
        await Assert.That(availability).DoesNotContain(
            "RenderBackendKind.Storm");
        await Assert.That(documentation).Contains(
            "Native Storm child-window camera input is polled");
        await Assert.That(documentation).Contains(
            "toolbar and menu camera commands remain available");
        await Assert.That(documentation).Contains("40 points per logical step");
        await Assert.That(documentation).Contains(
            "execute once per physical press");
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void PostCamera(
        ViewerCameraUpdatePump pump,
        CameraState camera)
    {
        if (!pump.TryPost(camera))
        {
            throw new InvalidOperationException("The camera pump stopped unexpectedly.");
        }
    }

    private static CameraState CreateCamera(float marker)
    {
        Matrix4x4 view = Matrix4x4.Identity;
        view.M41 = marker;
        return new CameraState(view, Matrix4x4.Identity);
    }

    private static OpenUsdStormNavigationInput Navigation(
        ulong sequence,
        int x,
        int y,
        OpenUsdStormPointerButtons buttons,
        OpenUsdStormInputModifiers modifiers,
        double wheel = 0,
        ulong frame = 0,
        ulong home = 0,
        ulong projection = 0,
        ulong left = 0,
        ulong right = 0,
        ulong up = 0,
        ulong down = 0,
        bool focused = true,
        bool inside = true) =>
        new(
                sequence,
                x,
                y,
                buttons,
                modifiers,
                wheel,
                frame,
                home,
                projection,
                (focused ? OpenUsdStormNavigationState.Focused : 0) |
                (inside ? OpenUsdStormNavigationState.Inside : 0))
        {
            OrbitLeftPressCount = left,
            OrbitRightPressCount = right,
            OrbitUpPressCount = up,
            OrbitDownPressCount = down,
        };

    private static UsdBounds3d CreateBounds() =>
        new(
            new UsdVec3d(-1d, -2d, -3d),
            new UsdVec3d(4d, 5d, 6d));

    private static string FindRepositoryRoot()
    {
        string? directory = AppContext.BaseDirectory;
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory, "OpenUsd.slnx")))
            {
                return directory;
            }
            directory = Directory.GetParent(directory)?.FullName;
        }
        throw new InvalidOperationException("Could not locate the repository root.");
    }

    private static string SliceMethod(
        string source,
        string startMarker,
        string endMarker)
    {
        int start = source.IndexOf(startMarker, StringComparison.Ordinal);
        int end = source.IndexOf(endMarker, start, StringComparison.Ordinal);
        if (start < 0 || end < 0)
        {
            throw new InvalidOperationException(
                $"Could not locate source range '{startMarker}' to '{endMarker}'.");
        }
        return source[start..end];
    }

    private sealed class TestUiThreadVerifier(bool isUiThread) : IViewerUiThreadVerifier
    {
        internal bool IsUiThread { get; set; } = isUiThread;

        public void VerifyAccess()
        {
            if (!IsUiThread)
            {
                throw new InvalidOperationException("Not on the test UI thread.");
            }
        }
    }

    private sealed class TestSelectedBoundsSource(
        ViewerSelectedBoundsSourceResult result) : IViewerSelectedBoundsSource
    {
        internal int QueryCount { get; private set; }

        internal ViewerSelectedBoundsRequest LastRequest { get; private set; }

        public ValueTask<ViewerSelectedBoundsSourceResult> QueryAsync(
            ViewerSelectedBoundsRequest request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            QueryCount++;
            LastRequest = request;
            return ValueTask.FromResult(result);
        }
    }
}
