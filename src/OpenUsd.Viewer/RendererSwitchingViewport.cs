// Copyright (c) marcschier. Licensed under the MIT License.

using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform;
using Avalonia.Threading;
using OpenUsd.Rendering;
using OpenUsd.Rendering.Storm;

namespace OpenUsd.Viewer;

internal sealed record ViewerStormNavigationDelivery(
    OpenUsdStormNavigationInput Before,
    OpenUsdStormNavigationInput Pressed,
    OpenUsdStormNavigationInput Moved,
    OpenUsdStormNavigationInput After,
    int AvaloniaRoutedEvents,
    ViewerWin32MessageEvidence[] Messages);

internal sealed class RendererSwitchingViewport : Grid
{
    private int _resizeEvents;
    private int _focusEvents;
    private int _pointerMoves;
    private int _pointerButtons;
    private int _wheelEvents;
    private int _keyEvents;

    public RendererSwitchingViewport()
    {
        Focusable = true;
        SizeChanged += (_, _) => Interlocked.Increment(ref _resizeEvents);
        GotFocus += (_, _) => Interlocked.Increment(ref _focusEvents);
        LostFocus += (_, _) => Interlocked.Increment(ref _focusEvents);
        AddHandler(
            PointerMovedEvent,
            (_, _) => Interlocked.Increment(ref _pointerMoves),
            RoutingStrategies.Tunnel | RoutingStrategies.Bubble,
            handledEventsToo: true);
        AddHandler(
            PointerPressedEvent,
            (_, _) => Interlocked.Increment(ref _pointerButtons),
            RoutingStrategies.Tunnel | RoutingStrategies.Bubble,
            handledEventsToo: true);
        AddHandler(
            PointerReleasedEvent,
            (_, _) => Interlocked.Increment(ref _pointerButtons),
            RoutingStrategies.Tunnel | RoutingStrategies.Bubble,
            handledEventsToo: true);
        AddHandler(
            PointerWheelChangedEvent,
            (_, _) => Interlocked.Increment(ref _wheelEvents),
            RoutingStrategies.Tunnel | RoutingStrategies.Bubble,
            handledEventsToo: true);
        KeyDown += (_, _) => Interlocked.Increment(ref _keyEvents);
        KeyUp += (_, _) => Interlocked.Increment(ref _keyEvents);
    }

    internal int AttachedControlCount => Children.Count;

    internal int VisibleControlCount => Children.Count(control => control.IsVisible);

    internal bool HasVisibleCompositionHost =>
        Children.OfType<CompositionViewportControl>().Any(control => control.IsVisible);

    internal void Attach(Control control, bool isActive = true)
    {
        ArgumentNullException.ThrowIfNull(control);
        control.IsVisible = isActive;
        Children.Add(control);
    }

    internal void SetActive(Control control, bool isActive)
    {
        ArgumentNullException.ThrowIfNull(control);
        if (!Children.Contains(control))
        {
            throw new InvalidOperationException(
                "The renderer control is not attached to this viewport.");
        }
        control.IsVisible = isActive;
    }

    internal void Detach(Control control)
    {
        ArgumentNullException.ThrowIfNull(control);
        control.IsVisible = false;
        Children.Remove(control);
    }

    internal nint GetEvidenceNativeWindow() =>
        Children.OfType<StormNativeControlHost>().FirstOrDefault()?.GetEvidenceWindow() ?? 0;

    internal OpenUsdStormChildDiagnostics GetActiveStormDiagnostics() =>
        Children
            .OfType<StormNativeControlHost>()
            .Single(control => control.IsVisible)
            .GetDiagnostics();

    internal StormNativeControlHost? GetActiveStormNavigationSource() =>
        Children
            .OfType<StormNativeControlHost>()
            .SingleOrDefault(control => control.IsVisible);

    internal ViewerCompositionEvidence GetCompositionRuntimeEvidence(
        RenderBackendKind backend)
    {
        CompositionViewportControl control = Children
            .OfType<CompositionViewportControl>()
            .Single(candidate => candidate.IsVisible && candidate.BackendKind == backend);
        return control.GetRuntimeEvidence();
    }

    internal Task<OpenUsdStormFramebufferCapture> CaptureStormFramebufferAsync(
        CancellationToken cancellationToken)
    {
        StormNativeControlHost storm =
            Children.OfType<StormNativeControlHost>().SingleOrDefault()
            ?? throw new InvalidOperationException(
                "The active Storm native child is unavailable for framebuffer capture.");
        return storm.CaptureFramebufferAsync(cancellationToken);
    }

    internal async Task<ViewerStormNavigationDelivery>
        ExerciseStormNavigationGestureAsync(
            Window window,
            CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(window);
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException(
                "Real Storm navigation evidence currently requires Win32.");
        }
        StormNativeControlHost storm = GetActiveStormNavigationSource()
            ?? throw new InvalidOperationException(
                "The active Storm native child is unavailable for navigation evidence.");
        nint target = storm.GetEvidenceWindow();
        var messages = new List<ViewerWin32MessageEvidence>();
        var wndProcCounts = new Dictionary<uint, int>();
        int routedBefore = CaptureAvaloniaRoutedInputCount();

        async Task sendAsync(
            string name,
            uint message,
            nint wParam,
            nint lParam,
            Func<ViewerInputCounterEvidence, ViewerInputCounterEvidence, bool> observed)
        {
            ViewerWin32MessageEvidence evidence = await SendWindowsMessageAsync(
                window,
                target,
                "StormChild",
                name,
                message,
                wParam,
                lParam,
                storm,
                static () => 0,
                wndProcCounts,
                requireWndProcHook: false,
                observed,
                cancellationToken);
            RequireSuccessfulOsRouting(evidence);
            messages.Add(evidence);
        }

        await sendAsync(
                "WM_SETFOCUS",
                WmSetFocus,
                0,
                0,
                static (before, after) =>
                    after.NativeFocusEvents > before.NativeFocusEvents);
        if (!storm.TryGetNavigationInput(out OpenUsdStormNavigationInput before))
        {
            throw new InvalidOperationException(
                "The Storm navigation baseline is unavailable.");
        }
        await sendAsync(
                "WM_SYSKEYDOWN(VK_MENU)",
                WmSysKeyDown,
                VkMenu,
                0x20000001,
                static (before, after) =>
                    after.NativeKeyEvents > before.NativeKeyEvents);
        await sendAsync(
                "WM_MOUSEMOVE(start)",
                WmMouseMove,
                0,
                MakePoint(24, 24),
                static (before, after) =>
                    after.NativePointerEvents > before.NativePointerEvents);
        await sendAsync(
                "WM_LBUTTONDOWN",
                WmLeftButtonDown,
                MkLeftButton,
                MakePoint(24, 24),
                static (before, after) =>
                    after.NativePointerEvents > before.NativePointerEvents);
        if (!storm.TryGetNavigationInput(out OpenUsdStormNavigationInput pressed))
        {
            throw new InvalidOperationException(
                "The Storm pressed navigation snapshot is unavailable.");
        }
        await sendAsync(
                "WM_MOUSEMOVE(drag)",
                WmMouseMove,
                MkLeftButton,
                MakePoint(104, 72),
                static (before, after) =>
                    after.NativePointerEvents > before.NativePointerEvents);
        if (!storm.TryGetNavigationInput(out OpenUsdStormNavigationInput moved))
        {
            throw new InvalidOperationException(
                "The Storm moved navigation snapshot is unavailable.");
        }
        await sendAsync(
                "WM_LBUTTONUP",
                WmLeftButtonUp,
                0,
                MakePoint(104, 72),
                static (before, after) =>
                    after.NativePointerEvents > before.NativePointerEvents);
        await sendAsync(
                "WM_SYSKEYUP(VK_MENU)",
                WmSysKeyUp,
                VkMenu,
                unchecked((nint)0xE0000001u),
                static (before, after) =>
                    after.NativeKeyEvents > before.NativeKeyEvents);
        if (!storm.TryGetNavigationInput(out OpenUsdStormNavigationInput after))
        {
            throw new InvalidOperationException(
                "The Storm released navigation snapshot is unavailable.");
        }
        int routedAfter = CaptureAvaloniaRoutedInputCount();
        return new ViewerStormNavigationDelivery(
            before,
            pressed,
            moved,
            after,
            routedAfter - routedBefore,
            messages.ToArray());
    }

    internal async Task ExerciseStormClickAsync(
        Window window,
        ViewerPhysicalPixel pixel,
        Action pollInput,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(window);
        ArgumentNullException.ThrowIfNull(pollInput);
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException(
                "Real Storm click evidence currently requires Win32.");
        }
        StormNativeControlHost storm = GetActiveStormNavigationSource()
            ?? throw new InvalidOperationException(
                "The active Storm native child is unavailable for click evidence.");
        nint target = storm.GetEvidenceWindow();
        var wndProcCounts = new Dictionary<uint, int>();

        async Task sendAsync(
            string name,
            uint message,
            nint wParam,
            nint lParam,
            Func<ViewerInputCounterEvidence, ViewerInputCounterEvidence, bool> observed)
        {
            ViewerWin32MessageEvidence evidence = await SendWindowsMessageAsync(
                window,
                target,
                "StormChild",
                name,
                message,
                wParam,
                lParam,
                storm,
                static () => 0,
                wndProcCounts,
                requireWndProcHook: false,
                observed,
                cancellationToken);
            RequireSuccessfulOsRouting(evidence);
        }

        nint point = MakePoint(pixel.X, pixel.Y);
        await sendAsync(
            "WM_SETFOCUS",
            WmSetFocus,
            0,
            0,
            static (before, after) =>
                after.NativeFocusEvents > before.NativeFocusEvents);
        await sendAsync(
            "WM_MOUSEMOVE(click)",
            WmMouseMove,
            0,
            point,
            static (before, after) =>
                after.NativePointerEvents > before.NativePointerEvents);
        await sendAsync(
            "WM_LBUTTONDOWN(click)",
            WmLeftButtonDown,
            MkLeftButton,
            point,
            static (before, after) =>
                after.NativePointerEvents > before.NativePointerEvents);
        await Task.Delay(TimeSpan.FromMilliseconds(64), cancellationToken);
        await Dispatcher.UIThread.InvokeAsync(pollInput);
        await DrainUiDispatcherAsync(cancellationToken);
        await sendAsync(
            "WM_LBUTTONUP(click)",
            WmLeftButtonUp,
            0,
            point,
            static (before, after) =>
                after.NativePointerEvents > before.NativePointerEvents);
        await Task.Delay(TimeSpan.FromMilliseconds(64), cancellationToken);
        await Dispatcher.UIThread.InvokeAsync(pollInput);
        await DrainUiDispatcherAsync(cancellationToken);
    }

    internal async Task ExerciseCompositionClickAsync(
        Window window,
        ViewerPhysicalPixel pixel,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(window);
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException(
                "Real composition click evidence currently requires Win32.");
        }
        IPlatformHandle parent = window.TryGetPlatformHandle()
            ?? throw new InvalidOperationException("Avalonia did not expose a window handle.");
        double scaling = window.RenderScaling;
        Point origin = this.TranslatePoint(default, window) ??
            throw new InvalidOperationException("Could not locate the viewer viewport.");
        int clientX = checked((int)Math.Floor(
            (origin.X * scaling) + pixel.X + 0.5));
        int clientY = checked((int)Math.Floor(
            (origin.Y * scaling) + pixel.Y + 0.5));
        nint point = MakePoint(clientX, clientY);
        var wndProcCounts = new Dictionary<uint, int>();
        IPlatformHandle parentHandle = parent;
        nint observeWndProc(
            nint hwnd,
            uint message,
            nint wParam,
            nint lParam,
            ref bool handled)
        {
            _ = wParam;
            _ = lParam;
            if (hwnd == parentHandle.Handle)
            {
                wndProcCounts[message] =
                    wndProcCounts.GetValueOrDefault(message) + 1;
            }
            handled = false;
            return 0;
        }
        Win32Properties.CustomWndProcHookCallback wndProcHook = observeWndProc;
        EventHandler<PointerEventArgs> moved =
            (_, _) => Interlocked.Increment(ref _pointerMoves);
        EventHandler<PointerPressedEventArgs> pressed =
            (_, _) => Interlocked.Increment(ref _pointerButtons);
        EventHandler<PointerReleasedEventArgs> released =
            (_, _) => Interlocked.Increment(ref _pointerButtons);
        if (!DisableMouseInPointerForEvidence(out int mouseInPointerError))
        {
            throw new InvalidOperationException(
                "Could not enable Win32 click evidence; " +
                $"Win32 error {mouseInPointerError}.");
        }

        async Task sendAsync(
            string name,
            uint message,
            nint wParam,
            Func<ViewerInputCounterEvidence, ViewerInputCounterEvidence, bool> observed)
        {
            ViewerWin32MessageEvidence evidence = await SendWindowsMessageAsync(
                window,
                parent.Handle,
                "Viewer",
                name,
                message,
                wParam,
                point,
                storm: null,
                static () => 0,
                wndProcCounts,
                requireWndProcHook: true,
                observed,
                cancellationToken);
            RequireSuccessfulOsRouting(evidence);
        }

        window.AddHandler(
            PointerMovedEvent,
            moved,
            RoutingStrategies.Tunnel | RoutingStrategies.Bubble,
            handledEventsToo: true);
        window.AddHandler(
            PointerPressedEvent,
            pressed,
            RoutingStrategies.Tunnel | RoutingStrategies.Bubble,
            handledEventsToo: true);
        window.AddHandler(
            PointerReleasedEvent,
            released,
            RoutingStrategies.Tunnel | RoutingStrategies.Bubble,
            handledEventsToo: true);
        Win32Properties.AddWndProcHookCallback(window, wndProcHook);
        try
        {
            _ = Focus();
            await DrainUiDispatcherAsync(cancellationToken);
            await sendAsync(
                "WM_MOUSEMOVE(click)",
                WmMouseMove,
                0,
                static (before, after) => after.PointerMoves > before.PointerMoves);
            await sendAsync(
                "WM_LBUTTONDOWN(click)",
                WmLeftButtonDown,
                MkLeftButton,
                static (before, after) => after.PointerButtons > before.PointerButtons);
            await Task.Delay(TimeSpan.FromMilliseconds(32), cancellationToken);
            await sendAsync(
                "WM_LBUTTONUP(click)",
                WmLeftButtonUp,
                0,
                static (before, after) => after.PointerButtons > before.PointerButtons);
            await Task.Delay(TimeSpan.FromMilliseconds(32), cancellationToken);
            await DrainUiDispatcherAsync(cancellationToken);
        }
        finally
        {
            Win32Properties.RemoveWndProcHookCallback(window, wndProcHook);
            window.RemoveHandler(PointerMovedEvent, moved);
            window.RemoveHandler(PointerPressedEvent, pressed);
            window.RemoveHandler(PointerReleasedEvent, released);
        }
    }

    internal async Task<ViewerInputEvidence> ExerciseEvidenceInputAsync(
        Window window,
        RenderBackendKind backend,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(window);
        cancellationToken.ThrowIfCancellationRequested();
        if (OperatingSystem.IsWindows())
        {
            _ = Focus();
            await DrainUiDispatcherAsync(cancellationToken);
        }
        EventHandler<PointerEventArgs>? windowPointerMoved = null;
        EventHandler<PointerPressedEventArgs>? windowPointerPressed = null;
        EventHandler<PointerReleasedEventArgs>? windowPointerReleased = null;
        EventHandler<PointerWheelEventArgs>? windowPointerWheel = null;
        EventHandler<KeyEventArgs>? windowKeyDown = null;
        EventHandler<KeyEventArgs>? windowKeyUp = null;
        if (OperatingSystem.IsWindows())
        {
            windowPointerMoved = (_, _) => Interlocked.Increment(ref _pointerMoves);
            windowPointerPressed = (_, _) => Interlocked.Increment(ref _pointerButtons);
            windowPointerReleased = (_, _) => Interlocked.Increment(ref _pointerButtons);
            windowPointerWheel = (_, _) => Interlocked.Increment(ref _wheelEvents);
            windowKeyDown = (_, _) => Interlocked.Increment(ref _keyEvents);
            windowKeyUp = (_, _) => Interlocked.Increment(ref _keyEvents);
            window.AddHandler(
                PointerMovedEvent,
                windowPointerMoved,
                RoutingStrategies.Tunnel | RoutingStrategies.Bubble,
                handledEventsToo: true);
            window.AddHandler(
                PointerPressedEvent,
                windowPointerPressed,
                RoutingStrategies.Tunnel | RoutingStrategies.Bubble,
                handledEventsToo: true);
            window.AddHandler(
                PointerReleasedEvent,
                windowPointerReleased,
                RoutingStrategies.Tunnel | RoutingStrategies.Bubble,
                handledEventsToo: true);
            window.AddHandler(
                PointerWheelChangedEvent,
                windowPointerWheel,
                RoutingStrategies.Tunnel | RoutingStrategies.Bubble,
                handledEventsToo: true);
            window.AddHandler(
                KeyDownEvent,
                windowKeyDown,
                RoutingStrategies.Tunnel | RoutingStrategies.Bubble,
                handledEventsToo: true);
            window.AddHandler(
                KeyUpEvent,
                windowKeyUp,
                RoutingStrategies.Tunnel | RoutingStrategies.Bubble,
                handledEventsToo: true);
        }
        int resizeBefore = Volatile.Read(ref _resizeEvents);
        int focusBefore = Volatile.Read(ref _focusEvents);
        int moveBefore = Volatile.Read(ref _pointerMoves);
        int buttonBefore = Volatile.Read(ref _pointerButtons);
        int wheelBefore = Volatile.Read(ref _wheelEvents);
        int keyBefore = Volatile.Read(ref _keyEvents);
        int scalingEvents = 0;
        EventHandler scalingChanged = (_, _) => Interlocked.Increment(ref scalingEvents);
        window.ScalingChanged += scalingChanged;
        var win32Messages = new List<ViewerWin32MessageEvidence>();
        var xTestInjections = new List<ViewerXTestInjectionEvidence>();
        OpenUsdStormChildDiagnostics? nativeBefore =
            Children.OfType<StormNativeControlHost>().FirstOrDefault()?.GetDiagnostics();
        StormNativeControlHost? storm = Children.OfType<StormNativeControlHost>().FirstOrDefault();
        IPlatformHandle parent = window.TryGetPlatformHandle()
            ?? throw new InvalidOperationException("Avalonia did not expose a window handle.");
        double scalingBefore = window.RenderScaling;
        uint dpiBefore = nativeBefore?.Dpi ??
            checked((uint)Math.Round(scalingBefore * 96));
        (int widthBefore, int heightBefore) = GetPhysicalSize(window, nativeBefore);
        OpenUsdStormChildDiagnostics? nativeObserved = nativeBefore;
        double scalingObserved = scalingBefore;
        int widthObserved = widthBefore;
        int heightObserved = heightBefore;
        string deliveryApi;
        var wndProcCounts = new Dictionary<uint, int>();
        nint observeWndProc(
            nint hwnd,
            uint message,
            nint wParam,
            nint lParam,
            ref bool handled)
        {
            _ = wParam;
            _ = lParam;
            if (hwnd == parent.Handle)
            {
                wndProcCounts[message] =
                    wndProcCounts.GetValueOrDefault(message) + 1;
            }
            handled = false;
            return 0;
        }
        Win32Properties.CustomWndProcHookCallback? wndProcHook = OperatingSystem.IsWindows()
            ? observeWndProc
            : null;
        try
        {
            if (OperatingSystem.IsWindows())
            {
                bool mouseInPointerDisabled =
                    DisableMouseInPointerForEvidence(out int mouseInPointerError);
                if (!mouseInPointerDisabled)
                {
                    throw new InvalidOperationException(
                        "Could not enable Win32 mouse-message evidence; " +
                        $"Win32 error {mouseInPointerError}.");
                }
                Win32Properties.AddWndProcHookCallback(window, wndProcHook);
                NativeRect originalRectangle = GetWindowRectangle(parent.Handle);
                ViewerWin32MessageEvidence changedDpi =
                    await SendWindowsDpiChangedAsync(
                        window,
                        parent.Handle,
                        storm,
                        () => Volatile.Read(ref scalingEvents),
                        wndProcCounts,
                        restore: false,
                        dpiBefore,
                        originalRectangle,
                        cancellationToken);
                RequireSuccessfulOsRouting(changedDpi);
                win32Messages.Add(changedDpi);
                nativeObserved = storm?.GetDiagnostics();
                scalingObserved = window.RenderScaling;
                (widthObserved, heightObserved) = GetPhysicalSize(window, nativeObserved);

                ViewerWin32MessageEvidence restoredDpi =
                    await SendWindowsDpiChangedAsync(
                        window,
                        parent.Handle,
                        storm,
                        () => Volatile.Read(ref scalingEvents),
                        wndProcCounts,
                        restore: true,
                        dpiBefore,
                        originalRectangle,
                        cancellationToken);
                RequireSuccessfulOsRouting(restoredDpi);
                win32Messages.Add(restoredDpi);

                Point origin = this.TranslatePoint(default, window) ??
                    throw new InvalidOperationException("Could not locate the viewer viewport.");
                int x = Math.Max(
                    1,
                    checked((int)Math.Round(
                        (origin.X + Bounds.Width / 2) * window.RenderScaling)));
                int y = Math.Max(
                    1,
                    checked((int)Math.Round(
                        (origin.Y + Bounds.Height / 2) * window.RenderScaling)));
                await SendWindowsEvidenceMessagesAsync(
                    window,
                    parent.Handle,
                    "ViewerTopLevel",
                    x,
                    y,
                    storm,
                    () => Volatile.Read(ref scalingEvents),
                    wndProcCounts,
                    requireAvaloniaHandlers: true,
                    win32Messages,
                    cancellationToken);
                if (storm is not null)
                {
                    await SendWindowsEvidenceMessagesAsync(
                        window,
                        storm.GetEvidenceWindow(),
                        "StormChild",
                        8,
                        8,
                        storm,
                        () => Volatile.Read(ref scalingEvents),
                        wndProcCounts,
                        requireAvaloniaHandlers: false,
                        win32Messages,
                        cancellationToken);
                    storm.PumpEvidenceEvents();
                }
                await DrainUiDispatcherAsync(cancellationToken);
                deliveryApi =
                    "EnableMouseInPointer(false,success=True,error=0)+" +
                    "SendMessageTimeoutW+Win32WndProc+DiagnosticWM_DPICHANGED+" +
                    "AvaloniaRoutedHandlers+NativeDiagnostics";
            }
            else if (OperatingSystem.IsLinux())
            {
                Size original = window.ClientSize;
                window.Width = original.Width + 16;
                window.Height = original.Height + 12;
                await Task.Delay(TimeSpan.FromMilliseconds(150), cancellationToken);
                nativeObserved = storm?.GetDiagnostics();
                scalingObserved = window.RenderScaling;
                (widthObserved, heightObserved) = GetPhysicalSize(window, nativeObserved);
                window.Width = original.Width;
                window.Height = original.Height;
                await Task.Delay(TimeSpan.FromMilliseconds(150), cancellationToken);
                xTestInjections.Add(InjectXTestEvidence(
                    NativeXTestApi.Instance,
                    parent.Handle,
                    8,
                    8,
                    "ViewerTopLevel",
                    Environment.GetEnvironmentVariable("DISPLAY")));
                if (storm is not null)
                {
                    xTestInjections.Add(InjectXTestEvidence(
                        NativeXTestApi.Instance,
                        storm.GetEvidenceWindow(),
                        8,
                        8,
                        "StormChild",
                        Environment.GetEnvironmentVariable("DISPLAY")));
                    storm.PumpEvidenceEvents();
                }
                deliveryApi = "XTest+AvaloniaRoutedHandlers+NativeDiagnostics";
            }
            else if (OperatingSystem.IsMacOS())
            {
                Size original = window.ClientSize;
                window.Width = original.Width + 16;
                window.Height = original.Height + 12;
                await Task.Delay(TimeSpan.FromMilliseconds(150), cancellationToken);
                nativeObserved = storm?.GetDiagnostics();
                scalingObserved = window.RenderScaling;
                (widthObserved, heightObserved) = GetPhysicalSize(window, nativeObserved);
                window.Width = original.Width;
                window.Height = original.Height;
                await Task.Delay(TimeSpan.FromMilliseconds(150), cancellationToken);
                await FocusForEvidenceAsync(window, cancellationToken);
                StormNativeControlHostMacOS.InjectDiagnosticInput(
                    storm?.GetEvidenceWindow() ?? parent.Handle);
                deliveryApi = "AppKitEvents+AvaloniaRoutedHandlers+NativeDiagnostics";
            }
            else
            {
                throw new PlatformNotSupportedException(
                    "Viewer input diagnostics support Windows, Linux, and macOS.");
            }

            await WaitForInputHandlersAsync(
                focusBefore,
                moveBefore,
                buttonBefore,
                wheelBefore,
                keyBefore,
                storm,
                nativeBefore,
                cancellationToken);
        }
        finally
        {
            if (wndProcHook is not null)
            {
                Win32Properties.RemoveWndProcHookCallback(window, wndProcHook);
            }
            if (windowPointerMoved is not null)
            {
                window.RemoveHandler(PointerMovedEvent, windowPointerMoved);
                window.RemoveHandler(PointerPressedEvent, windowPointerPressed!);
                window.RemoveHandler(PointerReleasedEvent, windowPointerReleased!);
                window.RemoveHandler(PointerWheelChangedEvent, windowPointerWheel!);
                window.RemoveHandler(KeyDownEvent, windowKeyDown!);
                window.RemoveHandler(KeyUpEvent, windowKeyUp!);
            }
            window.ScalingChanged -= scalingChanged;
        }

        OpenUsdStormChildDiagnostics? nativeAfter = storm?.GetDiagnostics();
        long nativeFocus = Delta(nativeBefore?.FocusCount, nativeAfter?.FocusCount);
        long nativePointer = Delta(nativeBefore?.PointerCount, nativeAfter?.PointerCount);
        long nativeWheel = Delta(nativeBefore?.WheelCount, nativeAfter?.WheelCount);
        long nativeKey = Delta(nativeBefore?.KeyCount, nativeAfter?.KeyCount);
        if (OperatingSystem.IsLinux() && storm is not null)
        {
            bool nativeServerEventsObserved =
                nativeFocus > 0 &&
                nativePointer >= 3 &&
                nativeWheel > 0 &&
                nativeKey >= 2;
            for (int index = 0; index < xTestInjections.Count; index++)
            {
                if (string.Equals(
                        xTestInjections[index].Target,
                        "StormChild",
                        StringComparison.Ordinal))
                {
                    xTestInjections[index] = xTestInjections[index] with
                    {
                        NativeSendEventFalseObserved = nativeServerEventsObserved
                    };
                }
            }
        }
        int resize = Volatile.Read(ref _resizeEvents) - resizeBefore;
        int focus = Volatile.Read(ref _focusEvents) - focusBefore;
        int moves = Volatile.Read(ref _pointerMoves) - moveBefore;
        int buttons = Volatile.Read(ref _pointerButtons) - buttonBefore;
        int wheel = Volatile.Read(ref _wheelEvents) - wheelBefore;
        int keys = Volatile.Read(ref _keyEvents) - keyBefore;
        double scalingAfter = window.RenderScaling;
        uint dpiObserved = nativeObserved?.Dpi ??
            checked((uint)Math.Round(scalingObserved * 96));
        uint dpiAfter = nativeAfter?.Dpi ??
            checked((uint)Math.Round(scalingAfter * 96));
        (int widthAfter, int heightAfter) = GetPhysicalSize(window, nativeAfter);
        bool synthesized = OperatingSystem.IsLinux() &&
            (xTestInjections.Count == 0 ||
             xTestInjections.Any(injection => !injection.ServerGenerated));

        return new ViewerInputEvidence(
            backend.ToString(),
            deliveryApi,
            Synthesized: synthesized,
            resize,
            scalingEvents,
            focus,
            moves,
            buttons,
            wheel,
            keys,
            nativeFocus,
            nativePointer,
            nativeWheel,
            nativeKey,
            scalingBefore,
            scalingObserved,
            scalingAfter,
            dpiBefore,
            dpiObserved,
            dpiAfter,
            widthBefore,
            heightBefore,
            widthObserved,
            heightObserved,
            widthAfter,
            heightAfter,
            win32Messages.ToArray(),
            xTestInjections.ToArray());
    }

    private async Task FocusForEvidenceAsync(
        Window window,
        CancellationToken cancellationToken)
    {
        var focusSink = new Button
        {
            Width = 1,
            Height = 1,
            Opacity = 0,
            Focusable = true,
            IsHitTestVisible = false
        };
        Children.Add(focusSink);
        _ = focusSink.Focus();
        await Task.Delay(TimeSpan.FromMilliseconds(25), cancellationToken);
        _ = Focus();
        Children.Remove(focusSink);
        await Task.Delay(TimeSpan.FromMilliseconds(25), cancellationToken);
    }

    private async Task WaitForInputHandlersAsync(
        int focusBefore,
        int moveBefore,
        int buttonBefore,
        int wheelBefore,
        int keyBefore,
        StormNativeControlHost? storm,
        OpenUsdStormChildDiagnostics? nativeBefore,
        CancellationToken cancellationToken)
    {
        for (int attempt = 0; attempt < 40; attempt++)
        {
            OpenUsdStormChildDiagnostics? nativeAfter = storm?.GetDiagnostics();
            bool routed =
                Volatile.Read(ref _focusEvents) - focusBefore >= 1 &&
                Volatile.Read(ref _pointerMoves) - moveBefore >= 1 &&
                Volatile.Read(ref _pointerButtons) - buttonBefore >= 2 &&
                Volatile.Read(ref _wheelEvents) - wheelBefore >= 1 &&
                Volatile.Read(ref _keyEvents) - keyBefore >= 2;
            bool native = storm is null ||
                (Delta(nativeBefore?.FocusCount, nativeAfter?.FocusCount) >= 1 &&
                 Delta(nativeBefore?.PointerCount, nativeAfter?.PointerCount) >= 3 &&
                 Delta(nativeBefore?.WheelCount, nativeAfter?.WheelCount) >= 1 &&
                 Delta(nativeBefore?.KeyCount, nativeAfter?.KeyCount) >= 2);
            if (routed && native)
            {
                return;
            }
            await Task.Delay(TimeSpan.FromMilliseconds(50), cancellationToken);
        }
        throw new InvalidOperationException(
            "OS-routed input did not advance the required Avalonia/native counters.");
    }

    private static (int Width, int Height) GetPhysicalSize(
        Window window,
        OpenUsdStormChildDiagnostics? native)
    {
        if (native is { } diagnostics)
        {
            return (diagnostics.Width, diagnostics.Height);
        }
        return (
            Math.Max(1, checked((int)Math.Round(window.ClientSize.Width * window.RenderScaling))),
            Math.Max(1, checked((int)Math.Round(window.ClientSize.Height * window.RenderScaling))));
    }

    private static long Delta(ulong? before, ulong? after)
    {
        ulong beforeValue = before.GetValueOrDefault();
        ulong afterValue = after.GetValueOrDefault();
        return afterValue >= beforeValue
            ? checked((long)(afterValue - beforeValue))
            : 0;
    }

    private int CaptureAvaloniaRoutedInputCount() =>
        Volatile.Read(ref _pointerMoves) +
        Volatile.Read(ref _pointerButtons) +
        Volatile.Read(ref _wheelEvents) +
        Volatile.Read(ref _keyEvents);

    private static nint MakePoint(int x, int y) =>
        (nint)((y & 0xFFFF) << 16 | (x & 0xFFFF));

    private async Task<ViewerWin32MessageEvidence> SendWindowsDpiChangedAsync(
        Window window,
        nint target,
        StormNativeControlHost? storm,
        Func<int> getScalingEvents,
        IReadOnlyDictionary<uint, int> wndProcCounts,
        bool restore,
        uint originalDpi,
        NativeRect originalRectangle,
        CancellationToken cancellationToken)
    {
        uint changedDpi = originalDpi >= 240
            ? originalDpi - 24
            : originalDpi + 24;
        uint targetDpi = restore ? originalDpi : changedDpi;
        NativeRect suggested = restore
            ? originalRectangle
            : new NativeRect
            {
                Left = originalRectangle.Left,
                Top = originalRectangle.Top,
                Right = originalRectangle.Left + checked((int)Math.Round(
                    originalRectangle.Width * (double)changedDpi / originalDpi)),
                Bottom = originalRectangle.Top + checked((int)Math.Round(
                    originalRectangle.Height * (double)changedDpi / originalDpi))
            };
        nint rectangle = Marshal.AllocHGlobal(Marshal.SizeOf<NativeRect>());
        try
        {
            Marshal.StructureToPtr(suggested, rectangle, fDeleteOld: false);
            return await SendWindowsMessageAsync(
                window,
                target,
                "ViewerTopLevel",
                restore ? "WM_DPICHANGED(restore)" : "WM_DPICHANGED(change)",
                WmDpiChanged,
                MakeDpiWParam(targetDpi),
                rectangle,
                storm,
                getScalingEvents,
                wndProcCounts,
                requireWndProcHook: true,
                (before, after) =>
                    after.ScalingEvents > before.ScalingEvents &&
                    after.ResizeEvents > before.ResizeEvents &&
                    after.Dpi == targetDpi &&
                    (restore
                        ? after.Dpi != before.Dpi
                        : after.Dpi != originalDpi &&
                          (after.PhysicalWidth != before.PhysicalWidth ||
                           after.PhysicalHeight != before.PhysicalHeight)),
                cancellationToken);
        }
        finally
        {
            Marshal.FreeHGlobal(rectangle);
        }
    }

    private async Task SendWindowsEvidenceMessagesAsync(
        Window window,
        nint target,
        string targetName,
        int x,
        int y,
        StormNativeControlHost? storm,
        Func<int> getScalingEvents,
        IReadOnlyDictionary<uint, int> wndProcCounts,
        bool requireAvaloniaHandlers,
        List<ViewerWin32MessageEvidence> messages,
        CancellationToken cancellationToken)
    {
        var screenPoint = new NativePoint { X = x, Y = y };
        if (!ClientToScreen(target, ref screenPoint))
        {
            throw new InvalidOperationException(
                $"Could not translate evidence coordinates for {targetName}; " +
                $"Win32 error {Marshal.GetLastPInvokeError()}.");
        }

        var specifications = new List<(
            string Name,
            uint Id,
            nint WParam,
            nint LParam,
            Func<ViewerInputCounterEvidence, ViewerInputCounterEvidence, bool> Observed)>();
        if (requireAvaloniaHandlers)
        {
            specifications.Add((
                "WM_KILLFOCUS",
                WmKillFocus,
                0,
                0,
                (before, after) => after.FocusEvents > before.FocusEvents));
        }
        else
        {
            specifications.Add((
                "WM_SETFOCUS",
                WmSetFocus,
                0,
                0,
                (before, after) =>
                    after.NativeFocusEvents > before.NativeFocusEvents));
        }
        specifications.AddRange(
        [
            (
                "WM_MOUSEMOVE",
                WmMouseMove,
                0,
                MakePoint(x, y),
                requireAvaloniaHandlers
                    ? (before, after) => after.PointerMoves > before.PointerMoves
                    : (before, after) =>
                        after.NativePointerEvents > before.NativePointerEvents),
            (
                "WM_LBUTTONDOWN",
                WmLeftButtonDown,
                MkLeftButton,
                MakePoint(x, y),
                requireAvaloniaHandlers
                    ? (before, after) => after.PointerButtons > before.PointerButtons
                    : (before, after) =>
                        after.NativePointerEvents > before.NativePointerEvents),
            (
                "WM_LBUTTONUP",
                WmLeftButtonUp,
                0,
                MakePoint(x, y),
                requireAvaloniaHandlers
                    ? (before, after) => after.PointerButtons > before.PointerButtons
                    : (before, after) =>
                        after.NativePointerEvents > before.NativePointerEvents),
            (
                "WM_MOUSEWHEEL",
                WmMouseWheel,
                (nint)(WheelDelta << 16),
                MakePoint(screenPoint.X, screenPoint.Y),
                requireAvaloniaHandlers
                    ? (before, after) => after.WheelEvents > before.WheelEvents
                    : (before, after) =>
                        after.NativeWheelEvents > before.NativeWheelEvents),
            (
                "WM_KEYDOWN",
                WmKeyDown,
                VkSpace,
                1,
                requireAvaloniaHandlers
                    ? (before, after) => after.KeyEvents > before.KeyEvents
                    : (before, after) =>
                        after.NativeKeyEvents > before.NativeKeyEvents),
            (
                "WM_KEYUP",
                WmKeyUp,
                VkSpace,
                unchecked((nint)0xC0000001u),
                requireAvaloniaHandlers
                    ? (before, after) => after.KeyEvents > before.KeyEvents
                    : (before, after) =>
                        after.NativeKeyEvents > before.NativeKeyEvents)
        ]);

        foreach (var specification in specifications)
        {
            ViewerWin32MessageEvidence message = await SendWindowsMessageAsync(
                window,
                target,
                targetName,
                specification.Name,
                specification.Id,
                specification.WParam,
                specification.LParam,
                storm,
                getScalingEvents,
                wndProcCounts,
                requireWndProcHook: requireAvaloniaHandlers,
                specification.Observed,
                cancellationToken);
            RequireSuccessfulOsRouting(message);
            messages.Add(message);
        }
    }

    private async Task<ViewerWin32MessageEvidence> SendWindowsMessageAsync(
        Window window,
        nint target,
        string targetName,
        string messageName,
        uint message,
        nint wParam,
        nint lParam,
        StormNativeControlHost? storm,
        Func<int> getScalingEvents,
        IReadOnlyDictionary<uint, int> wndProcCounts,
        bool requireWndProcHook,
        Func<ViewerInputCounterEvidence, ViewerInputCounterEvidence, bool> handlerObserved,
        CancellationToken cancellationToken)
    {
        int wndProcBefore = wndProcCounts.GetValueOrDefault(message);
        ViewerInputCounterEvidence before =
            CaptureInputCounters(window, storm, getScalingEvents());
        _ = SetMessageExtraInfo(0);
        Marshal.SetLastPInvokeError(0);
        bool apiSucceeded = SendMessageTimeout(
            target,
            message,
            wParam,
            lParam,
            SmtoAbortIfHung | SmtoBlock,
            EvidenceMessageTimeoutMilliseconds,
            out nint apiReturn);
        int lastError = apiSucceeded ? 0 : Marshal.GetLastPInvokeError();
        ViewerInputCounterEvidence after = before;
        bool observed = false;
        bool wndProcObserved = false;
        for (int attempt = 0; attempt < 40; attempt++)
        {
            await DrainUiDispatcherAsync(cancellationToken);
            after = CaptureInputCounters(window, storm, getScalingEvents());
            observed = handlerObserved(before, after);
            wndProcObserved = requireWndProcHook
                ? wndProcCounts.GetValueOrDefault(message) > wndProcBefore
                : observed;
            if (observed && wndProcObserved)
            {
                break;
            }
            await Task.Delay(TimeSpan.FromMilliseconds(25), cancellationToken);
        }

        bool routed = apiSucceeded && wndProcObserved && observed;
        return new ViewerWin32MessageEvidence(
            targetName,
            "SendMessageTimeoutW",
            FormatPointer(target),
            messageName,
            message,
            FormatPointer(wParam),
            FormatPointer(lParam),
            apiSucceeded,
            FormatPointer(apiReturn),
            lastError,
            wndProcObserved,
            observed,
            Synthesized: !routed,
            before,
            after);
    }

    private ViewerInputCounterEvidence CaptureInputCounters(
        Window window,
        StormNativeControlHost? storm,
        int scalingEvents)
    {
        OpenUsdStormChildDiagnostics? native = storm?.GetDiagnostics();
        (int width, int height) = GetPhysicalSize(window, native);
        return new ViewerInputCounterEvidence(
            Volatile.Read(ref _resizeEvents),
            scalingEvents,
            Volatile.Read(ref _focusEvents),
            Volatile.Read(ref _pointerMoves),
            Volatile.Read(ref _pointerButtons),
            Volatile.Read(ref _wheelEvents),
            Volatile.Read(ref _keyEvents),
            native?.FocusCount ?? 0,
            native?.PointerCount ?? 0,
            native?.WheelCount ?? 0,
            native?.KeyCount ?? 0,
            window.RenderScaling,
            native?.Dpi ?? checked((uint)Math.Round(window.RenderScaling * 96)),
            width,
            height);
    }

    private static async Task DrainUiDispatcherAsync(CancellationToken cancellationToken)
    {
        await Dispatcher.UIThread.InvokeAsync(
            static () => { },
            DispatcherPriority.Background,
            cancellationToken);
    }

    private static void RequireSuccessfulOsRouting(ViewerWin32MessageEvidence message)
    {
        if (!message.ApiSucceeded ||
            !message.WndProcObserved ||
            !message.HandlerObserved ||
            message.Synthesized)
        {
            throw new InvalidOperationException(
                $"Viewer OS routing failed for {message.Target} {message.Message}: " +
                $"api={message.ApiSucceeded}; wndProc={message.WndProcObserved}; " +
                $"handler={message.HandlerObserved}; error={message.LastError}.");
        }
    }

    private static NativeRect GetWindowRectangle(nint window)
    {
        if (!GetWindowRect(window, out NativeRect rectangle))
        {
            throw new InvalidOperationException(
                $"Could not inspect Viewer window bounds; Win32 error " +
                $"{Marshal.GetLastPInvokeError()}.");
        }
        return rectangle;
    }

    private static nint MakeDpiWParam(uint dpi) =>
        checked((nint)((dpi << 16) | dpi));

    private static bool DisableMouseInPointerForEvidence(out int error)
    {
        Marshal.SetLastPInvokeError(0);
        bool accepted = EnableMouseInPointer(enable: false);
        bool disabled = accepted && !IsMouseInPointerEnabled();
        error = disabled ? 0 : Marshal.GetLastPInvokeError();
        return disabled;
    }

    private static string FormatPointer(nint value) =>
        $"0x{unchecked((ulong)(nuint)value):X}";

    internal static ViewerXTestInjectionEvidence InjectXTestEvidence(
        IXTestApi api,
        nint window,
        int x,
        int y,
        string target,
        string? displayName)
    {
        ArgumentNullException.ThrowIfNull(api);
        ArgumentException.ThrowIfNullOrWhiteSpace(target);
        var calls = new List<ViewerXTestCallEvidence>();
        nint display = api.OpenDisplay();
        if (display == 0)
        {
            throw new XTestUnavailableException(
                "Could not open DISPLAY for XTest input evidence.");
        }
        try
        {
            int query = api.QueryExtension(
                display,
                out int eventBase,
                out int errorBase,
                out int extensionMajor,
                out int extensionMinor);
            calls.Add(new(
                "XTestQueryExtension",
                string.Empty,
                query));
            if (query == 0)
            {
                throw new XTestUnavailableException(
                    "The XTest extension is unavailable on the active DISPLAY.");
            }

            int screen = api.DefaultScreen(display);
            nint root = api.DefaultRootWindow(display);
            int translated = api.TranslateCoordinates(
                display,
                window,
                root,
                x,
                y,
                out int rootX,
                out int rootY);
            RequireXTestCall(
                calls,
                "XTranslateCoordinates",
                $"x={x},y={y}",
                translated);
            RequireXTestCall(
                calls,
                "XSetInputFocus",
                $"xid={FormatPointer(window)}",
                api.SetInputFocus(display, window, RevertToParent, CurrentTime));
            RequireXTestCall(
                calls,
                "XTestFakeMotionEvent",
                $"screen={screen},x={rootX},y={rootY}",
                api.FakeMotionEvent(display, screen, rootX, rootY, CurrentTime));
            AddXTestButton(calls, api, display, Button1, pressed: true);
            AddXTestButton(calls, api, display, Button1, pressed: false);
            AddXTestButton(calls, api, display, Button4, pressed: true);
            AddXTestButton(calls, api, display, Button4, pressed: false);
            AddXTestButton(calls, api, display, Button5, pressed: true);
            AddXTestButton(calls, api, display, Button5, pressed: false);

            uint keyCode = api.KeySymToKeyCode(display, XKeySpace);
            if (keyCode == 0)
            {
                throw new XTestUnavailableException(
                    "XTest could not resolve the Space keycode.");
            }
            RequireXTestCall(
                calls,
                "XTestFakeKeyEvent(press)",
                $"keycode={keyCode}",
                api.FakeKeyEvent(display, keyCode, pressed: true, CurrentTime));
            RequireXTestCall(
                calls,
                "XTestFakeKeyEvent(release)",
                $"keycode={keyCode}",
                api.FakeKeyEvent(display, keyCode, pressed: false, CurrentTime));
            RequireXTestCall(calls, "XFlush", string.Empty, api.Flush(display));
            RequireXTestCall(calls, "XSync", "discard=false", api.Sync(display, discard: false));

            return new ViewerXTestInjectionEvidence(
                target,
                "XTest",
                ExtensionAvailable: true,
                extensionMajor,
                extensionMinor,
                eventBase,
                errorBase,
                string.IsNullOrWhiteSpace(displayName)
                    ? FormatPointer(display)
                    : displayName,
                FormatPointer(window),
                ServerGenerated: true,
                NativeSendEventFalseObserved: false,
                calls.ToArray());
        }
        finally
        {
            _ = api.CloseDisplay(display);
        }
    }

    private static void AddXTestButton(
        List<ViewerXTestCallEvidence> calls,
        IXTestApi api,
        nint display,
        uint button,
        bool pressed)
    {
        string state = pressed ? "press" : "release";
        RequireXTestCall(
            calls,
            $"XTestFakeButtonEvent(button={button},{state})",
            string.Empty,
            api.FakeButtonEvent(display, button, pressed, CurrentTime));
    }

    private static void RequireXTestCall(
        List<ViewerXTestCallEvidence> calls,
        string api,
        string arguments,
        int result)
    {
        calls.Add(new(api, arguments, result));
        if (result == 0)
        {
            throw new InvalidOperationException(
                $"{api} failed while injecting XTest viewer evidence.");
        }
    }

    private const uint WmSetFocus = 0x0007;
    private const uint WmKillFocus = 0x0008;
    private const uint WmKeyDown = 0x0100;
    private const uint WmKeyUp = 0x0101;
    private const uint WmSysKeyDown = 0x0104;
    private const uint WmSysKeyUp = 0x0105;
    private const uint WmMouseMove = 0x0200;
    private const uint WmLeftButtonDown = 0x0201;
    private const uint WmLeftButtonUp = 0x0202;
    private const uint WmMouseWheel = 0x020A;
    private const uint WmDpiChanged = 0x02E0;
    private const ushort VkSpace = 0x20;
    private const ushort VkMenu = 0x12;
    private const nint MkLeftButton = 0x0001;
    private const int WheelDelta = 120;
    private const uint SmtoBlock = 0x0001;
    private const uint SmtoAbortIfHung = 0x0002;
    private const uint EvidenceMessageTimeoutMilliseconds = 5000;
    private const uint Button1 = 1;
    private const uint Button4 = 4;
    private const uint Button5 = 5;
    private const nuint XKeySpace = 0x20;
    private const int RevertToParent = 2;
    private const nuint CurrentTime = 0;

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        internal int X;
        internal int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        internal int Left;
        internal int Top;
        internal int Right;
        internal int Bottom;

        internal readonly int Width => Right - Left;

        internal readonly int Height => Bottom - Top;
    }

    [DllImport(
        "user32.dll",
        EntryPoint = "SendMessageTimeoutW",
        SetLastError = true,
        ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SendMessageTimeout(
        nint window,
        uint message,
        nint wParam,
        nint lParam,
        uint flags,
        uint timeoutMilliseconds,
        out nint result);

    [DllImport("user32.dll", EntryPoint = "EnableMouseInPointer", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnableMouseInPointer(
        [MarshalAs(UnmanagedType.Bool)] bool enable);

    [DllImport("user32.dll", EntryPoint = "IsMouseInPointerEnabled")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsMouseInPointerEnabled();

    [DllImport("user32.dll", EntryPoint = "SetMessageExtraInfo")]
    private static extern nint SetMessageExtraInfo(nint lParam);

    [DllImport(
        "user32.dll",
        EntryPoint = "ClientToScreen",
        SetLastError = true,
        ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ClientToScreen(nint window, ref NativePoint point);

    [DllImport("user32.dll", EntryPoint = "GetWindowRect", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(nint window, out NativeRect rectangle);

    [DllImport("libX11.so.6")]
    private static extern nint XOpenDisplay(nint displayName);

    [DllImport("libX11.so.6")]
    private static extern int XCloseDisplay(nint display);

    [DllImport("libX11.so.6")]
    private static extern int XSync(nint display, [MarshalAs(UnmanagedType.Bool)] bool discard);

    [DllImport("libX11.so.6")]
    private static extern int XFlush(nint display);

    [DllImport("libX11.so.6")]
    private static extern int XSetInputFocus(
        nint display,
        nint focus,
        int revertTo,
        nuint time);

    [DllImport("libX11.so.6")]
    private static extern int XDefaultScreen(nint display);

    [DllImport("libX11.so.6")]
    private static extern nint XDefaultRootWindow(nint display);

    [DllImport("libX11.so.6")]
    private static extern int XTranslateCoordinates(
        nint display,
        nint destination,
        nint root,
        int sourceX,
        int sourceY,
        out int destinationX,
        out int destinationY,
        out nint child);

    [DllImport("libX11.so.6")]
    private static extern byte XKeysymToKeycode(nint display, nuint keySym);

    [DllImport("libXtst.so.6")]
    private static extern int XTestQueryExtension(
        nint display,
        out int eventBase,
        out int errorBase,
        out int majorVersion,
        out int minorVersion);

    [DllImport("libXtst.so.6")]
    private static extern int XTestFakeMotionEvent(
        nint display,
        int screen,
        int x,
        int y,
        nuint delay);

    [DllImport("libXtst.so.6")]
    private static extern int XTestFakeButtonEvent(
        nint display,
        uint button,
        [MarshalAs(UnmanagedType.Bool)] bool pressed,
        nuint delay);

    [DllImport("libXtst.so.6")]
    private static extern int XTestFakeKeyEvent(
        nint display,
        uint keyCode,
        [MarshalAs(UnmanagedType.Bool)] bool pressed,
        nuint delay);

    private sealed class NativeXTestApi : IXTestApi
    {
        internal static NativeXTestApi Instance { get; } = new();

        private NativeXTestApi()
        {
        }

        public nint OpenDisplay() => XOpenDisplay(0);

        public int CloseDisplay(nint display) => XCloseDisplay(display);

        public int QueryExtension(
            nint display,
            out int eventBase,
            out int errorBase,
            out int majorVersion,
            out int minorVersion) =>
            XTestQueryExtension(
                display,
                out eventBase,
                out errorBase,
                out majorVersion,
                out minorVersion);

        public int SetInputFocus(
            nint display,
            nint window,
            int revertTo,
            nuint time) =>
            XSetInputFocus(display, window, revertTo, time);

        public int DefaultScreen(nint display) => XDefaultScreen(display);

        public nint DefaultRootWindow(nint display) => XDefaultRootWindow(display);

        public int TranslateCoordinates(
            nint display,
            nint window,
            nint root,
            int x,
            int y,
            out int rootX,
            out int rootY)
        {
            return XTranslateCoordinates(
                display,
                window,
                root,
                x,
                y,
                out rootX,
                out rootY,
                out _);
        }

        public uint KeySymToKeyCode(nint display, nuint keySym) =>
            XKeysymToKeycode(display, keySym);

        public int FakeMotionEvent(
            nint display,
            int screen,
            int x,
            int y,
            nuint delay) =>
            XTestFakeMotionEvent(display, screen, x, y, delay);

        public int FakeButtonEvent(
            nint display,
            uint button,
            bool pressed,
            nuint delay) =>
            XTestFakeButtonEvent(display, button, pressed, delay);

        public int FakeKeyEvent(
            nint display,
            uint keyCode,
            bool pressed,
            nuint delay) =>
            XTestFakeKeyEvent(display, keyCode, pressed, delay);

        public int Flush(nint display) => XFlush(display);

        public int Sync(nint display, bool discard) => XSync(display, discard);
    }
}

internal interface IXTestApi
{
    nint OpenDisplay();

    int CloseDisplay(nint display);

    int QueryExtension(
        nint display,
        out int eventBase,
        out int errorBase,
        out int majorVersion,
        out int minorVersion);

    int SetInputFocus(nint display, nint window, int revertTo, nuint time);

    int DefaultScreen(nint display);

    nint DefaultRootWindow(nint display);

    int TranslateCoordinates(
        nint display,
        nint window,
        nint root,
        int x,
        int y,
        out int rootX,
        out int rootY);

    uint KeySymToKeyCode(nint display, nuint keySym);

    int FakeMotionEvent(nint display, int screen, int x, int y, nuint delay);

    int FakeButtonEvent(nint display, uint button, bool pressed, nuint delay);

    int FakeKeyEvent(nint display, uint keyCode, bool pressed, nuint delay);

    int Flush(nint display);

    int Sync(nint display, bool discard);
}

internal sealed class XTestUnavailableException : InvalidOperationException
{
    internal XTestUnavailableException(string message)
        : base(message)
    {
    }
}
