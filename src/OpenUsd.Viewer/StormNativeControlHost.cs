// Copyright (c) marcschier. Licensed under the MIT License.

using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform;
using Avalonia.Threading;
using OpenUsd.Rendering;
using OpenUsd.Rendering.Storm;

namespace OpenUsd.Viewer;

internal sealed class StormNativeControlHost : NativeControlHost
{
    private readonly TaskCompletionSource<string> _initialized =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly string _pluginPath;
    private readonly UsdStageRenderSource _source;
    private readonly object _physicsGate = new();
    private readonly object _frameGate = new();
    private readonly ViewerPhysicsOverrideStage _physicsStage = new();
    private readonly StormPhysicsTransformOverrides _physicsOverrides = new(
        ViewerPhysicsRenderCapacities.BodyCapacity,
        ViewerPhysicsRenderCapacities.StormPathBytes);
    private readonly StormPhysicsDeformationOverrides _physicsDeformations = new(
        ViewerPhysicsRenderCapacities.DeformableCapacity,
        ViewerPhysicsRenderCapacities.DeformableVertexCapacity,
        ViewerPhysicsRenderCapacities.StormPathBytes);
    private StormPhysicsDeformationDiagnostics _physicsDeformationDiagnostics;
    private IViewerStormFrameAdapter? _frameAdapter;
    private OpenUsdStormChildSession? _session;
    private TopLevel? _topLevel;
    private int _destroyed;

    internal StormNativeControlHost(
        string pluginPath,
        UsdStageRenderSource source)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pluginPath);
        ArgumentNullException.ThrowIfNull(source);
        _pluginPath = pluginPath;
        _source = source;
        SizeChanged += OnSizeChanged;
        AddHandler(
            PointerPressedEvent,
            OnPointerPressed,
            RoutingStrategies.Bubble,
            handledEventsToo: true);
        PropertyChanged += OnHostPropertyChanged;
    }

    internal Task<string> WaitForInitializationAsync(CancellationToken cancellationToken) =>
        _initialized.Task.WaitAsync(cancellationToken);

    internal Task<OpenUsdStormChildDiagnostics> RenderFrameAsync(
        StageRenderState state,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(state);
        cancellationToken.ThrowIfCancellationRequested();
        IViewerStormFrameAdapter adapter = GetFrameAdapter();
        ViewerFrameRequest request = ViewerFrameRequest.Capture(state);
        return Task.Run(
            () =>
            {
                lock (_frameGate)
                {
                    OpenUsdStormChildSession session = GetSession();
                    ViewerPhysicsOverrideReport? report = ApplyPendingPhysicsOverrides(session);
                    OpenUsdStormChildDiagnostics diagnostics = ViewerFrameAdapter.RenderStorm(adapter, request);
                    if (report is { } applied)
                    {
                        _physicsStage.PublishReport(applied.Revision, applied.Applied, applied.Unresolved);
                    }
                    return diagnostics;
                }
            },
            cancellationToken);
    }

    internal void RequestFrame(StageRenderState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        IViewerStormFrameAdapter? adapter = Volatile.Read(ref _frameAdapter);
        if (adapter is not null)
        {
            ViewerFrameRequest request = ViewerFrameRequest.Capture(state);
            ViewerFrameAdapter.RequestStorm(adapter, request);
        }
    }

    internal Task<RenderPickResult> PickAsync(
        RenderPickRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        OpenUsdStormChildSession session = GetSession();
        return Task.Run(() => session.Pick(request), cancellationToken);
    }

    internal void SetSelection(SelectionState selection)
    {
        ArgumentNullException.ThrowIfNull(selection);
        GetSession().SetSelection(
            ViewerPickingPolicy.ProjectForStorm(selection),
            ViewerPickingPolicy.StormSelectionColor);
    }

    internal async ValueTask ResizeAsync(
        ViewportDimensions viewport,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await Dispatcher.UIThread.InvokeAsync(
            () => ResizeCore(viewport),
            DispatcherPriority.Send,
            cancellationToken);
    }

    internal void ResizeCore(ViewportDimensions viewport)
    {
        VerifyUiThread();
        if (viewport.Width <= 0 || viewport.Height <= 0)
        {
            return;
        }
        OpenUsdStormChildSession? session = Volatile.Read(ref _session);
        if (session is null)
        {
            return;
        }
        session.Resize(viewport.Width, viewport.Height, GetDpi());
    }

    internal void SimulateContextLoss() => GetSession().SimulateContextLoss();

    /// <summary>Stages one physics override batch without waiting for the native child's render thread.</summary>
    /// <param name="overrides">The overrides one render update produced.</param>
    /// <param name="bindings">The table naming the prim each identity drives.</param>
    /// <returns>The number of overrides staged for the next frame.</returns>
    internal int SetPhysicsOverrides(
        in PhysicsRenderOverrideView overrides,
        PhysicsRenderBindingTable bindings)
    {
        OpenUsdStormChildSession? session = Volatile.Read(ref _session);
        if (session is null || Volatile.Read(ref _destroyed) != 0)
        {
            return 0;
        }

        return _physicsStage.Stage(in overrides, bindings);
    }

    /// <summary>Stages one deformable geometry batch without waiting for the native child's render thread.</summary>
    /// <param name="deformations">The geometry one render update produced.</param>
    /// <param name="bindings">The table naming the prim each identity drives.</param>
    /// <returns>The number of regions staged for the next frame.</returns>
    /// <remarks>
    /// The regions and their shared point page are packed once and handed over in one call, so a
    /// deforming body costs one boundary crossing per frame no matter how many vertices it has.
    /// </remarks>
    internal int SetPhysicsDeformations(
        in PhysicsRenderDeformationView deformations,
        PhysicsRenderBindingTable bindings)
    {
        OpenUsdStormChildSession? session = Volatile.Read(ref _session);
        if (session is null || Volatile.Read(ref _destroyed) != 0)
        {
            return 0;
        }

        return _physicsStage.StageDeformations(in deformations);
    }

    /// <summary>Gets what the child reported about the last deformation batch it accepted.</summary>
    internal StormPhysicsDeformationDiagnostics PhysicsDeformationDiagnostics
    {
        get
        {
            lock (_physicsGate)
            {
                return _physicsDeformationDiagnostics;
            }
        }
    }

    /// <summary>Takes the newest report of what the child resolved before a successful frame.</summary>
    /// <param name="report">Receives the newest unread report.</param>
    /// <returns><see langword="true"/> when a report was taken.</returns>
    /// <remarks>
    /// Delivery is asynchronous. Staging a batch is not evidence that the child rendered it.
    /// </remarks>
    internal bool TryTakePhysicsOverrideReport(out ViewerPhysicsOverrideReport report) =>
        _physicsStage.TryTakeReport(out report);

    /// <summary>Stages empty batches so the next frame restores the authored transforms and points.</summary>
    internal void ClearPhysicsOverrides()
    {
        OpenUsdStormChildSession? session = Volatile.Read(ref _session);
        if (session is null || Volatile.Read(ref _destroyed) != 0)
        {
            return;
        }

        _physicsStage.Clear();
        _physicsStage.ClearDeformations();
    }

    private ViewerPhysicsOverrideReport? ApplyPendingPhysicsOverrides(OpenUsdStormChildSession session)
    {
        ViewerPhysicsOverrideReport? report = null;
        if (_physicsStage.TryTake(out ViewerPhysicsOverrideBatch batch))
        {
            using (batch)
            {
                int resolved = _physicsOverrides.Refresh(batch.Overrides, batch.Bindings);
                session.SetPhysicsTransformOverrides(_physicsOverrides);
                report = new ViewerPhysicsOverrideReport(
                    batch.Overrides.Revision, resolved, Math.Max(0, batch.Overrides.Count - resolved));
            }
        }
        if (_physicsStage.TryTakeDeformations(out PhysicsRenderDeformationView deformations))
        {
            _physicsDeformations.Refresh(in deformations, _physicsStage.Bindings);
            StormPhysicsDeformationDiagnostics diagnostics =
                session.SetPhysicsDeformationOverrides(_physicsDeformations);
            lock (_physicsGate)
            {
                _physicsDeformationDiagnostics = diagnostics;
            }
        }
        return report;
    }

    internal OpenUsdStormChildDiagnostics GetDiagnostics() => GetSession().GetDiagnostics();

    internal bool TryGetNavigationInput(out OpenUsdStormNavigationInput input)
    {
        OpenUsdStormChildSession? session = Volatile.Read(ref _session);
        if (session is null || Volatile.Read(ref _destroyed) != 0)
        {
            input = default;
            return false;
        }
        input = session.GetNavigationInput();
        return true;
    }

    internal Task<OpenUsdStormFramebufferCapture> CaptureFramebufferAsync(
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        OpenUsdStormChildSession session = GetSession();
        return Task.Run(
            () => session.CaptureFramebuffer(copyPixels: true),
            cancellationToken);
    }

    internal Task<OpenUsdStormChildAovCapture> RenderAovsAsync(
        StormAovRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        OpenUsdStormChildSession session = GetSession();
        return Task.Run(() => session.RenderAovs(request, cancellationToken), cancellationToken);
    }

    internal nint GetEvidenceWindow() => GetSession().Window;

    internal void FocusEvidenceWindow() => GetSession().Focus();

    internal void FocusNativeViewport()
    {
        Focus();
        GetSession().Focus();
    }

    internal void PumpEvidenceEvents() =>
        GetSession().RequestFrame(0, 0, CameraState.Default);

    internal void DisposeSession()
    {
        VerifyUiThread();
        if (_destroyed != 0)
        {
            return;
        }
        OpenUsdStormChildSession? session = _session;
        if (session is not null)
        {
            if (ViewerStartupOptions.TryConsumeNativeStormDestroyFailure())
            {
                throw new InvalidOperationException(
                    "The test-only Storm DestroyWindow cleanup failpoint was triggered.");
            }
            session.Dispose();
            _session = null;
            _frameAdapter = null;
        }
        _destroyed = 1;
    }

    protected override IPlatformHandle CreateNativeControlCore(IPlatformHandle parent)
    {
        VerifyUiThread();
        bool windows = OperatingSystem.IsWindows() &&
            string.Equals(parent.HandleDescriptor, "HWND", StringComparison.OrdinalIgnoreCase);
        bool linux = OperatingSystem.IsLinux() &&
            string.Equals(parent.HandleDescriptor, "XID", StringComparison.OrdinalIgnoreCase);
        bool macOS = StormNativeControlHostMacOS.IsParent(parent);
        if (!windows && !linux && !macOS)
        {
            throw new PlatformNotSupportedException(
                "StormNativeControlHost requires a Win32 HWND, Linux X11 XID, " +
                "or macOS NSView parent.");
        }
        try
        {
            ViewportDimensions viewport = GetPixelSize();
            int width = Math.Max(1, viewport.Width);
            int height = Math.Max(1, viewport.Height);
            LinuxX11Threading.RebindAfterPlatformSetup();
            OpenUsdStormChildSession session = OpenUsdStormChildRuntime.CreateForNativeHost(
                parent.Handle,
                _pluginPath,
                _source,
                width,
                height,
                GetDpi(),
                deferNativeInitialization: linux);
            Volatile.Write(
                ref _frameAdapter,
                new OpenUsdStormFrameAdapter(session));
            Volatile.Write(ref _session, session);
            session.SetVisible(IsVisible);
            if (linux)
            {
                CompleteLinuxInitializationAsync(session);
            }
            else
            {
                _initialized.TrySetResult(session.RendererName);
            }
            string descriptor = windows
                ? "HWND"
                : macOS
                    ? StormNativeControlHostMacOS.HandleDescriptor
                    : "XID";
            return new PlatformHandle(session.Window, descriptor);
        }
        catch (Exception exception)
        {
            _initialized.TrySetException(exception);
            throw;
        }
    }

    private void CompleteLinuxInitializationAsync(OpenUsdStormChildSession session) =>
        _ = Task.Run(() =>
        {
            try
            {
                // Linux CI job 93119646386 showed this path on the Avalonia UI-thread stack.
                _initialized.TrySetResult(session.CompleteDeferredInitialization());
            }
            catch (Exception exception)
            {
                _initialized.TrySetException(exception);
            }
        });

    protected override void DestroyNativeControlCore(IPlatformHandle control)
    {
        VerifyUiThread();
        DisposeSession();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _topLevel = TopLevel.GetTopLevel(this);
        _topLevel?.ScalingChanged += OnScalingChanged;
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _topLevel?.ScalingChanged -= OnScalingChanged;
        _topLevel = null;
        base.OnDetachedFromVisualTree(e);
    }

    private void OnSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        ResizeCore(ViewportPixelMath.ToPixels(
            e.NewSize.Width,
            e.NewSize.Height,
            GetScaling()));
    }

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        Volatile.Read(ref _session)?.Focus();
    }

    private void OnHostPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == IsVisibleProperty)
        {
            Volatile.Read(ref _session)?.SetVisible(IsVisible);
        }
    }

    private uint GetDpi()
    {
        double scaling = GetScaling();
        return checked((uint)Math.Max(1, Math.Round(96 * scaling)));
    }

    private void OnScalingChanged(object? sender, EventArgs e) =>
        ResizeCore(GetPixelSize());

    private ViewportDimensions GetPixelSize() =>
        ViewportPixelMath.ToPixels(Bounds.Width, Bounds.Height, GetScaling());

    private double GetScaling() =>
        _topLevel?.RenderScaling ??
        TopLevel.GetTopLevel(this)?.RenderScaling ??
        1;

    private static void VerifyUiThread()
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            throw new InvalidOperationException(
                "Storm native child window operations require the Avalonia UI thread.");
        }
    }

    private OpenUsdStormChildSession GetSession() =>
        Volatile.Read(ref _session)
        ?? throw new InvalidOperationException(
            "The Storm native child has not been initialized.");

    private IViewerStormFrameAdapter GetFrameAdapter() =>
        Volatile.Read(ref _frameAdapter)
        ?? throw new InvalidOperationException(
            "The Storm native child has not been initialized.");
}
