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
            () => ViewerFrameAdapter.RenderStorm(adapter, request),
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
        GetSession().SetSelection(selection, ViewerPickingPolicy.StormSelectionColor);
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

    internal nint GetEvidenceWindow() => GetSession().Window;

    internal void FocusEvidenceWindow() => GetSession().Focus();

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
            OpenUsdStormChildSession session = OpenUsdStormChildRuntime.Create(
                parent.Handle,
                _pluginPath,
                _source,
                width,
                height,
                GetDpi());
            Volatile.Write(
                ref _frameAdapter,
                new OpenUsdStormFrameAdapter(session));
            Volatile.Write(ref _session, session);
            session.SetVisible(IsVisible);
            _initialized.TrySetResult(session.RendererName);
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
