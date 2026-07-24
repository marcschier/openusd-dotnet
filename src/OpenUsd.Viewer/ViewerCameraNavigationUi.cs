// Copyright (c) marcschier. Licensed under the MIT License.

using System.Numerics;
using Avalonia.Input;
using Avalonia.Threading;
using OpenUsd.Geom;
using OpenUsd.Rendering;
using OpenUsd.Rendering.Storm;

namespace OpenUsd.Viewer;

[Flags]
internal enum ViewerPointerButtons
{
    None = 0,
    Left = 1,
    Middle = 2,
    Right = 4,
}

internal enum ViewerCameraPointerGesture
{
    None,
    Orbit,
    Pan,
    Dolly,
}

internal static class ViewerCameraGestureClassifier
{
    internal static ViewerCameraPointerGesture Classify(
        KeyModifiers modifiers,
        ViewerPointerButtons buttons)
    {
        if (modifiers != KeyModifiers.Alt)
        {
            return ViewerCameraPointerGesture.None;
        }

        return buttons switch
        {
            ViewerPointerButtons.Left => ViewerCameraPointerGesture.Orbit,
            ViewerPointerButtons.Middle => ViewerCameraPointerGesture.Pan,
            ViewerPointerButtons.Right => ViewerCameraPointerGesture.Dolly,
            _ => ViewerCameraPointerGesture.None,
        };
    }
}

internal readonly record struct ViewerStormNavigationDelta(
    ulong SequenceDelta,
    bool ResetPointerGesture,
    ViewerCameraPointerGesture Gesture,
    Vector2 PointerDelta,
    float WheelDelta,
    ulong FrameSelectedPresses,
    ulong ResetAutomaticPresses,
    ulong ToggleProjectionPresses)
{
    internal bool HasCameraMutation =>
        Gesture != ViewerCameraPointerGesture.None ||
        WheelDelta != 0f ||
        ResetAutomaticPresses != 0 ||
        ToggleProjectionPresses != 0;
}

internal sealed class ViewerStormNavigationInputTracker
{
    private OpenUsdStormNavigationInput _previous;
    private ulong _routedInputGeneration;
    private bool _hasBaseline;

    internal void Reset()
    {
        _previous = default;
        _routedInputGeneration = 0;
        _hasBaseline = false;
    }

    internal ViewerStormNavigationDelta Update(
        in OpenUsdStormNavigationInput current,
        ulong routedInputGeneration)
    {
        if (!_hasBaseline)
        {
            SetBaseline(current, routedInputGeneration);
            return new ViewerStormNavigationDelta(
                0,
                ResetPointerGesture: true,
                ViewerCameraPointerGesture.None,
                Vector2.Zero,
                0,
                0,
                0,
                0);
        }

        OpenUsdStormNavigationInput previous = _previous;
        ulong sequenceDelta = CounterDelta(current.Sequence, previous.Sequence);
        bool routedInputObserved =
            routedInputGeneration != _routedInputGeneration;
        bool focusChanged = current.Focused != previous.Focused;
        if (routedInputObserved || focusChanged || !current.Focused)
        {
            SetBaseline(current, routedInputGeneration);
            return new ViewerStormNavigationDelta(
                sequenceDelta,
                ResetPointerGesture: true,
                ViewerCameraPointerGesture.None,
                Vector2.Zero,
                0,
                0,
                0,
                0);
        }

        ViewerCameraPointerGesture previousGesture = Classify(previous);
        ViewerCameraPointerGesture currentGesture = Classify(current);
        Vector2 pointerDelta =
            sequenceDelta != 0 &&
            currentGesture != ViewerCameraPointerGesture.None &&
            currentGesture == previousGesture
                ? new Vector2(
                    SubtractCoordinates(current.PointerX, previous.PointerX),
                    SubtractCoordinates(current.PointerY, previous.PointerY))
                : Vector2.Zero;
        if (pointerDelta == Vector2.Zero)
        {
            currentGesture = ViewerCameraPointerGesture.None;
        }

        float wheelDelta = current.Inside
            ? ClampToFiniteFloat(
                current.CumulativeWheelDelta -
                previous.CumulativeWheelDelta)
            : 0f;
        var result = new ViewerStormNavigationDelta(
            sequenceDelta,
            ResetPointerGesture: false,
            currentGesture,
            pointerDelta,
            wheelDelta,
            CounterDelta(
                current.FrameSelectedPressCount,
                previous.FrameSelectedPressCount),
            CounterDelta(
                current.ResetAutomaticPressCount,
                previous.ResetAutomaticPressCount),
            CounterDelta(
                current.ToggleProjectionPressCount,
                previous.ToggleProjectionPressCount));
        SetBaseline(current, routedInputGeneration);
        return result;
    }

    internal static ulong CounterDelta(ulong current, ulong previous) =>
        unchecked(current - previous);

    private void SetBaseline(
        in OpenUsdStormNavigationInput input,
        ulong routedInputGeneration)
    {
        _previous = input;
        _routedInputGeneration = routedInputGeneration;
        _hasBaseline = true;
    }

    private static ViewerCameraPointerGesture Classify(
        in OpenUsdStormNavigationInput input) =>
        ViewerCameraGestureClassifier.Classify(
            ToKeyModifiers(input.Modifiers),
            ToPointerButtons(input.Buttons));

    private static KeyModifiers ToKeyModifiers(
        OpenUsdStormInputModifiers modifiers)
    {
        KeyModifiers result = KeyModifiers.None;
        if ((modifiers & OpenUsdStormInputModifiers.Alt) != 0)
        {
            result |= KeyModifiers.Alt;
        }
        if ((modifiers & OpenUsdStormInputModifiers.Shift) != 0)
        {
            result |= KeyModifiers.Shift;
        }
        if ((modifiers & OpenUsdStormInputModifiers.Control) != 0)
        {
            result |= KeyModifiers.Control;
        }
        if ((modifiers & OpenUsdStormInputModifiers.Meta) != 0)
        {
            result |= KeyModifiers.Meta;
        }
        return result;
    }

    private static ViewerPointerButtons ToPointerButtons(
        OpenUsdStormPointerButtons buttons)
    {
        ViewerPointerButtons result = ViewerPointerButtons.None;
        if ((buttons & OpenUsdStormPointerButtons.Left) != 0)
        {
            result |= ViewerPointerButtons.Left;
        }
        if ((buttons & OpenUsdStormPointerButtons.Middle) != 0)
        {
            result |= ViewerPointerButtons.Middle;
        }
        if ((buttons & OpenUsdStormPointerButtons.Right) != 0)
        {
            result |= ViewerPointerButtons.Right;
        }
        return result;
    }

    private static float SubtractCoordinates(int current, int previous) =>
        ClampToFiniteFloat((double)current - previous);

    private static float ClampToFiniteFloat(double value)
    {
        if (double.IsNaN(value))
        {
            return 0f;
        }
        if (value <= -float.MaxValue)
        {
            return -float.MaxValue;
        }
        if (value >= float.MaxValue)
        {
            return float.MaxValue;
        }
        return (float)value;
    }
}

internal enum ViewerCameraShortcut
{
    None,
    FrameSelected,
    ResetAutomatic,
    ToggleProjection,
}

internal sealed class ViewerCameraShortcutRepeatGuard
{
    private uint _pressed;

    internal bool TryPress(Key key)
    {
        uint flag = GetFlag(key);
        if (flag == 0)
        {
            return true;
        }
        if ((_pressed & flag) != 0)
        {
            return false;
        }
        _pressed |= flag;
        return true;
    }

    internal void Release(Key key) => _pressed &= ~GetFlag(key);

    internal void ResetForFocusTransfer() => _pressed = 0;

    internal void Reset() => ResetForFocusTransfer();

    private static uint GetFlag(Key key) =>
        key switch
        {
            Key.F => 1,
            Key.Home => 2,
            Key.P => 4,
            _ => 0,
        };
}

internal enum ViewerCameraDisplayMode
{
    Automatic,
    Perspective,
    Orthographic,
    StagePerspective,
    StageOrthographic,
}

internal static class ViewerCameraDisplay
{
    internal static ViewerCameraDisplayMode GetMode(
        in ViewerCameraNavigationState state) =>
        state.IsAutomatic
            ? ViewerCameraDisplayMode.Automatic
            : state.ProjectionMode == ViewerCameraProjectionMode.Perspective
                ? ViewerCameraDisplayMode.Perspective
                : ViewerCameraDisplayMode.Orthographic;
}

internal static class ViewerCameraInputAvailability
{
    internal static string FormatStatus(
        string cameraStatus,
        RenderBackendKind? backendKind)
    {
        ArgumentNullException.ThrowIfNull(cameraStatus);
        _ = backendKind;
        return cameraStatus;
    }
}

internal static class ViewerCameraShortcutPolicy
{
    internal static ViewerCameraShortcut Classify(
        Key key,
        KeyModifiers modifiers,
        bool isEditing)
    {
        if (isEditing || modifiers != KeyModifiers.None)
        {
            return ViewerCameraShortcut.None;
        }

        return key switch
        {
            Key.F => ViewerCameraShortcut.FrameSelected,
            Key.Home => ViewerCameraShortcut.ResetAutomatic,
            Key.P => ViewerCameraShortcut.ToggleProjection,
            _ => ViewerCameraShortcut.None,
        };
    }
}

internal static class ViewerCameraPointerDeltas
{
    internal const float DollyExponentPerPixel = 0.01f;

    internal static Vector2 ToPhysicalPixels(Vector2 logicalDelta, double renderScaling)
    {
        if (!float.IsFinite(logicalDelta.X) ||
            !float.IsFinite(logicalDelta.Y))
        {
            throw new ArgumentOutOfRangeException(
                nameof(logicalDelta),
                "Pointer deltas must contain only finite values.");
        }
        if (!double.IsFinite(renderScaling) || renderScaling <= 0d)
        {
            throw new ArgumentOutOfRangeException(
                nameof(renderScaling),
                "Render scaling must be finite and positive.");
        }

        return new Vector2(
            ClampToFiniteFloat((double)logicalDelta.X * renderScaling),
            ClampToFiniteFloat((double)logicalDelta.Y * renderScaling));
    }

    internal static float CreateDollyExponent(float verticalPixelDelta)
    {
        if (!float.IsFinite(verticalPixelDelta))
        {
            throw new ArgumentOutOfRangeException(
                nameof(verticalPixelDelta),
                "Pointer deltas must contain only finite values.");
        }

        return ClampToFiniteFloat((double)verticalPixelDelta * DollyExponentPerPixel);
    }

    private static float ClampToFiniteFloat(double value)
    {
        if (value <= -float.MaxValue)
        {
            return -float.MaxValue;
        }
        if (value >= float.MaxValue)
        {
            return float.MaxValue;
        }
        return (float)value;
    }
}

internal interface IViewerUiThreadVerifier
{
    void VerifyAccess();
}

internal sealed class AvaloniaViewerUiThreadVerifier : IViewerUiThreadVerifier
{
    private AvaloniaViewerUiThreadVerifier()
    {
    }

    internal static AvaloniaViewerUiThreadVerifier Instance { get; } = new();

    public void VerifyAccess()
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            throw new InvalidOperationException(
                "Viewer camera navigation must run on the Avalonia UI thread.");
        }
    }
}

internal readonly record struct ViewerCameraResizeUpdate(
    ViewportDimensions Viewport,
    bool ViewportChanged,
    CameraState Camera,
    bool CameraChanged);

internal sealed class ViewerCameraNavigationUiAdapter
{
    private readonly ViewerCameraNavigationController _controller;
    private readonly ViewerStageCameraModeState _stageCamera;
    private readonly IViewerUiThreadVerifier _uiThread;

    internal ViewerCameraNavigationUiAdapter(
        ViewerCameraNavigationController controller,
        IViewerUiThreadVerifier uiThread)
        : this(
            controller,
            new ViewerStageCameraModeState(controller.Viewport),
            uiThread)
    {
    }

    internal ViewerCameraNavigationUiAdapter(
        ViewerCameraNavigationController controller,
        ViewerStageCameraModeState stageCamera,
        IViewerUiThreadVerifier uiThread)
    {
        ArgumentNullException.ThrowIfNull(controller);
        ArgumentNullException.ThrowIfNull(stageCamera);
        ArgumentNullException.ThrowIfNull(uiThread);
        _controller = controller;
        _stageCamera = stageCamera;
        _uiThread = uiThread;
    }

    internal ViewerCameraNavigationState State
    {
        get
        {
            _uiThread.VerifyAccess();
            return _controller.State;
        }
    }

    internal CameraState Camera
    {
        get
        {
            _uiThread.VerifyAccess();
            return _stageCamera.TryGetCamera(out CameraState camera)
                ? camera
                : _controller.Camera;
        }
    }

    internal ViewerCameraDisplayMode DisplayMode
    {
        get
        {
            _uiThread.VerifyAccess();
            ViewerStageCameraModeView stageCamera = _stageCamera.GetView();
            if (stageCamera.IsActive)
            {
                return stageCamera.Projection == UsdGeomCameraProjection.Perspective
                    ? ViewerCameraDisplayMode.StagePerspective
                    : ViewerCameraDisplayMode.StageOrthographic;
            }
            if (stageCamera.ForcesAutomatic)
            {
                return ViewerCameraDisplayMode.Automatic;
            }
            return ViewerCameraDisplay.GetMode(_controller.State);
        }
    }

    internal string? StageCameraPath
    {
        get
        {
            _uiThread.VerifyAccess();
            return _stageCamera.GetView().PrimPath;
        }
    }

    internal ViewerStageCameraModeState StageCameraMode => _stageCamera;

    internal bool ApplyGesture(
        ViewerCameraPointerGesture gesture,
        Vector2 physicalPixelDelta)
    {
        _uiThread.VerifyAccess();
        if (gesture == ViewerCameraPointerGesture.None)
        {
            return false;
        }
        bool exitedStageCamera = PrepareOrbitNavigation();
        return gesture switch
        {
            ViewerCameraPointerGesture.Orbit =>
                ApplyOrbit(physicalPixelDelta) || exitedStageCamera,
            ViewerCameraPointerGesture.Pan =>
                ApplyPan(physicalPixelDelta) || exitedStageCamera,
            ViewerCameraPointerGesture.Dolly =>
                _controller.Zoom(
                    ViewerCameraPointerDeltas.CreateDollyExponent(
                        physicalPixelDelta.Y)) ||
                exitedStageCamera,
            _ => throw new ArgumentOutOfRangeException(nameof(gesture)),
        };
    }

    internal bool ZoomWheel(float wheelDelta)
    {
        _uiThread.VerifyAccess();
        bool exitedStageCamera = PrepareOrbitNavigation();
        return _controller.Zoom(
            ViewerCameraInputDeltas.CreateZoomExponent(wheelDelta)) ||
            exitedStageCamera;
    }

    internal bool ToggleProjection()
    {
        _uiThread.VerifyAccess();
        bool exitedStageCamera = PrepareOrbitNavigation();
        return _controller.ToggleProjection() || exitedStageCamera;
    }

    internal bool ResetToAutomatic()
    {
        _uiThread.VerifyAccess();
        bool stageCameraChanged = _stageCamera.ResetToAutomatic();
        return _controller.ResetToAutomatic() || stageCameraChanged;
    }

    internal bool ResetToExplicitPose()
    {
        _uiThread.VerifyAccess();
        bool exitedStageCamera = PrepareOrbitNavigation();
        return _controller.ResetToExplicitPose() || exitedStageCamera;
    }

    internal bool FrameBounds(UsdBounds3d bounds)
    {
        _uiThread.VerifyAccess();
        bool exitedStageCamera = PrepareOrbitNavigation();
        return _controller.FrameBounds(bounds) || exitedStageCamera;
    }

    internal bool ExitStageCameraForNavigation()
    {
        _uiThread.VerifyAccess();
        return PrepareOrbitNavigation();
    }

    internal ViewerStageCameraActivation CaptureStageCameraActivation(
        string primPath,
        double timeCode)
    {
        _uiThread.VerifyAccess();
        return _stageCamera.CaptureActivation(primPath, timeCode);
    }

    internal bool TryActivateStageCamera(
        in ViewerStageCameraActivation activation,
        in ViewerStageCameraSnapshot snapshot,
        out CameraState camera)
    {
        _uiThread.VerifyAccess();
        return _stageCamera.TryActivate(activation, snapshot, out camera);
    }

    internal bool TryFallbackStageCameraActivation(
        in ViewerStageCameraActivation activation,
        out long fallbackGeneration)
    {
        _uiThread.VerifyAccess();
        return _stageCamera.TryFallbackFromActivation(
            activation,
            out fallbackGeneration);
    }

    internal ViewerCameraResizeUpdate Resize(ViewportDimensions viewport)
    {
        _uiThread.VerifyAccess();
        ViewportDimensions previousViewport = _controller.Viewport;
        CameraState previousCamera = _stageCamera.TryGetCamera(out CameraState staged)
            ? staged
            : _controller.Camera;
        _controller.Resize(viewport);
        _stageCamera.Resize(viewport);
        CameraState camera = _stageCamera.TryGetCamera(out staged)
            ? staged
            : _controller.Camera;
        return new ViewerCameraResizeUpdate(
            viewport,
            viewport != previousViewport,
            camera,
            camera != previousCamera);
    }

    private bool ApplyOrbit(Vector2 physicalPixelDelta)
    {
        Vector2 orbit = ViewerCameraInputDeltas.CreateOrbitDelta(physicalPixelDelta);
        return _controller.Orbit(orbit.X, orbit.Y);
    }

    private bool ApplyPan(Vector2 physicalPixelDelta)
    {
        Vector2 pan = ViewerCameraInputDeltas.CreatePanDelta(
            physicalPixelDelta,
            _controller.Viewport,
            _controller.State);
        return _controller.Pan(pan.X, pan.Y);
    }

    private bool PrepareOrbitNavigation()
    {
        bool exited = _stageCamera.ExitForNavigation(
            out bool resetOrbitToAutomatic);
        if (resetOrbitToAutomatic)
        {
            _controller.ResetToAutomatic();
        }
        return exited;
    }
}

internal static class ViewerCameraStateMutation
{
    internal static StageRenderState ApplyResize(
        StageRenderState state,
        in ViewerCameraResizeUpdate update)
    {
        ArgumentNullException.ThrowIfNull(state);
        StageRenderState revised = update.ViewportChanged
            ? state.WithViewport(update.Viewport)
            : state;
        return update.CameraChanged
            ? revised.WithCamera(update.Camera)
            : revised;
    }
}

internal static class ViewerCameraPurposeMapping
{
    private const RenderPurpose AllPurposes =
        RenderPurpose.Default |
        RenderPurpose.Proxy |
        RenderPurpose.Render |
        RenderPurpose.Guide;

    internal static UsdGeomPurposeMask ToUsdGeomPurposeMask(RenderPurpose purposes)
    {
        if ((purposes & ~AllPurposes) != 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(purposes),
                "The render purpose mask contains unsupported values.");
        }

        UsdGeomPurposeMask result = UsdGeomPurposeMask.None;
        if ((purposes & RenderPurpose.Default) != 0)
        {
            result |= UsdGeomPurposeMask.Default;
        }
        if ((purposes & RenderPurpose.Proxy) != 0)
        {
            result |= UsdGeomPurposeMask.Proxy;
        }
        if ((purposes & RenderPurpose.Render) != 0)
        {
            result |= UsdGeomPurposeMask.Render;
        }
        if ((purposes & RenderPurpose.Guide) != 0)
        {
            result |= UsdGeomPurposeMask.Guide;
        }
        return result;
    }
}

internal readonly record struct ViewerSelectedBoundsRequest(
    string PrimPath,
    double TimeCode,
    UsdGeomPurposeMask PurposeMask);

internal readonly record struct ViewerSelectedBoundsSourceResult(
    bool PrimExists,
    UsdBounds3d Bounds) : IUsdDetachedResult;

internal interface IViewerSelectedBoundsSource
{
    ValueTask<ViewerSelectedBoundsSourceResult> QueryAsync(
        ViewerSelectedBoundsRequest request,
        CancellationToken cancellationToken);
}

internal sealed class ViewerSchedulerSelectedBoundsSource : IViewerSelectedBoundsSource
{
    private readonly UsdStageScheduler _scheduler;

    internal ViewerSchedulerSelectedBoundsSource(UsdStageScheduler scheduler)
    {
        ArgumentNullException.ThrowIfNull(scheduler);
        _scheduler = scheduler;
    }

    public ValueTask<ViewerSelectedBoundsSourceResult> QueryAsync(
        ViewerSelectedBoundsRequest request,
        CancellationToken cancellationToken) =>
        _scheduler.InvokeAsync(
            stage => QueryStage(stage, request),
            cancellationToken);

    private static ViewerSelectedBoundsSourceResult QueryStage(
        UsdStage stage,
        in ViewerSelectedBoundsRequest request)
    {
        if (!stage.HasPrim(request.PrimPath))
        {
            return new ViewerSelectedBoundsSourceResult(
                PrimExists: false,
                UsdBounds3d.Empty);
        }

        UsdBounds3d bounds = stage
            .GetPrim(request.PrimPath)
            .GetWorldBounds(request.TimeCode, request.PurposeMask);
        return new ViewerSelectedBoundsSourceResult(
            PrimExists: true,
            bounds);
    }
}

internal enum ViewerFrameSelectedOutcome
{
    Ready,
    NoSelection,
    MissingPrim,
    EmptyBounds,
}

internal readonly record struct ViewerFrameSelectedResult(
    ViewerFrameSelectedOutcome Outcome,
    string? PrimPath,
    UsdBounds3d Bounds);

internal static class ViewerFrameSelectedQuery
{
    internal static async ValueTask<ViewerFrameSelectedResult> QueryAsync(
        IViewerSelectedBoundsSource source,
        string? selectedPrimPath,
        StageTime time,
        RenderPurpose purposes,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (string.IsNullOrWhiteSpace(selectedPrimPath))
        {
            return new ViewerFrameSelectedResult(
                ViewerFrameSelectedOutcome.NoSelection,
                PrimPath: null,
                UsdBounds3d.Empty);
        }

        var request = new ViewerSelectedBoundsRequest(
            selectedPrimPath,
            time.TimeCode,
            ViewerCameraPurposeMapping.ToUsdGeomPurposeMask(purposes));
        ViewerSelectedBoundsSourceResult result = await source
            .QueryAsync(request, cancellationToken)
            .ConfigureAwait(false);
        if (!result.PrimExists)
        {
            return new ViewerFrameSelectedResult(
                ViewerFrameSelectedOutcome.MissingPrim,
                selectedPrimPath,
                UsdBounds3d.Empty);
        }
        if (result.Bounds.IsEmpty)
        {
            return new ViewerFrameSelectedResult(
                ViewerFrameSelectedOutcome.EmptyBounds,
                selectedPrimPath,
                UsdBounds3d.Empty);
        }
        return new ViewerFrameSelectedResult(
            ViewerFrameSelectedOutcome.Ready,
            selectedPrimPath,
            result.Bounds);
    }
}

internal sealed class ViewerCameraUpdatePump : IAsyncDisposable
{
    private readonly Func<CameraState, CancellationToken, ValueTask> _applyAsync;
    private readonly Action<Exception> _reportFailure;
    private readonly CancellationTokenSource _lifetime;
    private readonly SemaphoreSlim _signal = new(0, 1);
    private readonly object _gate = new();
    private readonly Task _worker;
    private CameraState _pending;
    private bool _hasPending;
    private bool _disposed;
    private bool _accepting = true;

    internal ViewerCameraUpdatePump(
        Func<CameraState, CancellationToken, ValueTask> applyAsync,
        Action<Exception> reportFailure,
        CancellationToken documentToken)
    {
        ArgumentNullException.ThrowIfNull(applyAsync);
        ArgumentNullException.ThrowIfNull(reportFailure);
        _applyAsync = applyAsync;
        _reportFailure = reportFailure;
        _lifetime = CancellationTokenSource.CreateLinkedTokenSource(documentToken);
        _worker = RunAsync(_lifetime.Token);
    }

    internal bool IsAccepting
    {
        get
        {
            lock (_gate)
            {
                return _accepting && !_lifetime.IsCancellationRequested;
            }
        }
    }

    internal bool TryPost(CameraState camera)
    {
        lock (_gate)
        {
            if (!_accepting || _lifetime.IsCancellationRequested)
            {
                return false;
            }
            _pending = camera;
            _hasPending = true;
            if (_signal.CurrentCount == 0)
            {
                _signal.Release();
            }
            return true;
        }
    }

    public async ValueTask DisposeAsync()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }
            _disposed = true;
            _accepting = false;
        }
        _lifetime.Cancel();
        try
        {
            await _worker.ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
        }
        _signal.Dispose();
        _lifetime.Dispose();
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (true)
            {
                await _signal.WaitAsync(cancellationToken).ConfigureAwait(false);
                CameraState camera;
                lock (_gate)
                {
                    if (!_hasPending)
                    {
                        continue;
                    }
                    camera = _pending;
                    _hasPending = false;
                }
                await _applyAsync(camera, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            _reportFailure(exception);
        }
        finally
        {
            lock (_gate)
            {
                _accepting = false;
            }
        }
    }
}
