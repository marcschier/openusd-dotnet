// Copyright (c) marcschier. Licensed under the MIT License.

using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using OpenUsd.Interop;

namespace OpenUsd.Rendering.Storm;

/// <summary>Immutable diagnostics from the native Storm child.</summary>
public readonly record struct OpenUsdStormChildDiagnostics(
    ulong FrameCount,
    ulong PixelSignature,
    ulong PixelSampleCount,
    ulong FocusCount,
    ulong PointerCount,
    ulong WheelCount,
    ulong KeyCount,
    ulong ContextGeneration,
    ulong CoalescedRequestCount,
    ulong CancelledCommandCount,
    ulong TeardownFallbackCount,
    ulong LatestRequestedRevision,
    ulong LatestRequestedCameraSignature,
    ulong LatestRenderedCameraSignature,
    uint RenderThreadId,
    uint CreatorThreadId,
    uint PendingCommandCount,
    uint PeakPendingCommandCount,
    int GlMajor,
    int GlMinor,
    bool CompatibilityProfile,
    int Width,
    int Height,
    uint Dpi,
    bool Visible,
    bool Focused,
    bool Converged);

/// <summary>Diagnostic evidence read from a completed Storm child framebuffer.</summary>
/// <remarks>
/// <c>RgbaPixels</c> contains optional bottom-up, tightly packed RGBA8 pixels. It is empty unless
/// pixel copying was requested.
/// </remarks>
public readonly record struct OpenUsdStormFramebufferCapture(
    ulong FrameCount,
    ulong PixelHash,
    ulong PixelCount,
    ulong NonBackgroundPixelCount,
    int Width,
    int Height,
    uint Dpi,
    uint BackgroundRgba,
    uint AverageRgba,
    uint MinimumRgba,
    uint MaximumRgba,
    uint ReadBuffer,
    ReadOnlyMemory<byte> RgbaPixels);

/// <summary>Stable native Storm pointer-button flags.</summary>
[Flags]
public enum OpenUsdStormPointerButtons : uint
{
    /// <summary>No pointer button is pressed.</summary>
    None = 0,
    /// <summary>The primary (left) pointer button is pressed.</summary>
    Left = 1,
    /// <summary>The middle pointer button is pressed.</summary>
    Middle = 2,
    /// <summary>The secondary (right) pointer button is pressed.</summary>
    Right = 4,
}

/// <summary>Stable native Storm input-modifier flags.</summary>
[Flags]
public enum OpenUsdStormInputModifiers : uint
{
    /// <summary>No keyboard modifier is pressed.</summary>
    None = 0,
    /// <summary>Alt or Option is pressed.</summary>
    Alt = 1,
    /// <summary>Shift is pressed.</summary>
    Shift = 2,
    /// <summary>Control is pressed.</summary>
    Control = 4,
    /// <summary>The platform meta key is pressed.</summary>
    Meta = 8,
}

/// <summary>Stable native Storm navigation state flags.</summary>
[Flags]
public enum OpenUsdStormNavigationState : uint
{
    /// <summary>The child is neither focused nor pointer-contained.</summary>
    None = 0,
    /// <summary>The native child owns keyboard focus.</summary>
    Focused = 1,
    /// <summary>The latest pointer position is inside the native child.</summary>
    Inside = 2,
}

/// <summary>A detached latest-state snapshot of native Storm navigation input.</summary>
public readonly record struct OpenUsdStormNavigationInput(
    ulong Sequence,
    int PointerX,
    int PointerY,
    OpenUsdStormPointerButtons Buttons,
    OpenUsdStormInputModifiers Modifiers,
    double CumulativeWheelDelta,
    ulong FrameSelectedPressCount,
    ulong ResetAutomaticPressCount,
    ulong ToggleProjectionPressCount,
    OpenUsdStormNavigationState State)
{
    /// <summary>Gets the cumulative unmodified Left Arrow press and repeat count.</summary>
    public ulong OrbitLeftPressCount { get; init; }

    /// <summary>Gets the cumulative unmodified Right Arrow press and repeat count.</summary>
    public ulong OrbitRightPressCount { get; init; }

    /// <summary>Gets the cumulative unmodified Up Arrow press and repeat count.</summary>
    public ulong OrbitUpPressCount { get; init; }

    /// <summary>Gets the cumulative unmodified Down Arrow press and repeat count.</summary>
    public ulong OrbitDownPressCount { get; init; }

    /// <summary>Gets whether the native child owns keyboard focus.</summary>
    public bool Focused => (State & OpenUsdStormNavigationState.Focused) != 0;

    /// <summary>Gets whether the latest pointer position is inside the native child.</summary>
    public bool Inside => (State & OpenUsdStormNavigationState.Inside) != 0;
}

/// <summary>
/// Explicitly owns a child native window, GL context thread, and exact-stage Storm session.
/// </summary>
/// <remarks>
/// This owner is deliberately non-finalizable. Native teardown synchronously joins the render thread.
/// </remarks>
public sealed class OpenUsdStormChildSession : IDisposable
{
    private readonly object _gate = new();
    private readonly int _creatorThreadId = Environment.CurrentManagedThreadId;
    private UsdStageRenderLease? _lease;
    private nint _handle;
    private StormFrameBinding _latestFrame;
    private bool _hasRequestedFrame;
    private ulong _contextGeneration;
    private int _width;
    private int _height;

    internal OpenUsdStormChildSession(
        nint handle,
        nint window,
        UsdStageRenderLease lease,
        string rendererName,
        ulong contextGeneration = 0,
        int width = 1,
        int height = 1)
    {
        _handle = handle;
        _lease = lease;
        Window = window;
        RendererName = rendererName;
        _contextGeneration = contextGeneration;
        _width = width;
        _height = height;
    }

    /// <summary>Gets the application-owned child HWND, XID, or NSView.</summary>
    public nint Window { get; }

    /// <summary>Gets the immutable Storm renderer name captured on its render thread.</summary>
    public string RendererName { get; private set; }

    /// <summary>Gets the renderer-neutral backend identity.</summary>
    [SuppressMessage(
        "Performance",
        "CA1822:Mark members as static",
        Justification = "Backend identity is part of the session instance contract.")]
    public RenderBackendKind BackendKind => RenderBackendKind.Storm;

    /// <summary>Gets the latest observed native OpenGL context generation.</summary>
    public ulong ContextGeneration
    {
        get
        {
            lock (_gate)
            {
                return _contextGeneration;
            }
        }
    }

    /// <summary>Renders and presents one frame on the native render thread.</summary>
    public OpenUsdStormChildDiagnostics Render(
        double timeCode,
        CameraState camera = default,
        ulong revision = 0,
        ulong? sceneRevision = null)
    {
        lock (_gate)
        {
            nint handle = GetHandleLocked();
            OpenUsdStormChildRuntime.Render(
                handle,
                timeCode,
                camera,
                revision,
                sceneRevision);
            OpenUsdStormChildDiagnostics diagnostics =
                OpenUsdStormChildRuntime.GetDiagnostics(handle);
            _contextGeneration = diagnostics.ContextGeneration;
            _width = diagnostics.Width;
            _height = diagnostics.Height;
            _latestFrame = new StormFrameBinding(
                diagnostics.Width,
                diagnostics.Height,
                timeCode,
                camera,
                revision,
                sceneRevision,
                diagnostics.ContextGeneration);
            _hasRequestedFrame = true;
            return diagnostics;
        }
    }

    /// <summary>Gets the latest detached native navigation input snapshot.</summary>
    public OpenUsdStormNavigationInput GetNavigationInput()
    {
        nint handle;
        lock (_gate)
        {
            handle = GetHandleLocked();
        }
        return OpenUsdStormChildRuntime.GetNavigationInput(handle);
    }

    /// <summary>Queues a frame without blocking for completion.</summary>
    public void RequestFrame(
        double timeCode,
        ulong revision = 0,
        CameraState camera = default,
        ulong? sceneRevision = null)
    {
        lock (_gate)
        {
            OpenUsdStormChildRuntime.RequestFrame(
                GetHandleLocked(),
                timeCode,
                revision,
                camera,
                sceneRevision);
            _latestFrame = new StormFrameBinding(
                _width,
                _height,
                timeCode,
                camera,
                revision,
                sceneRevision,
                _contextGeneration);
            _hasRequestedFrame = true;
        }
    }

    /// <summary>Synchronously resolves one nearest hit on the render thread.</summary>
    public RenderPickResult Pick(RenderPickRequest request)
    {
        lock (_gate)
        {
            nint handle = GetHandleLocked();
            if (!_hasRequestedFrame)
            {
                throw new InvalidOperationException(
                    "The Storm child must render or request a frame before picking.");
            }
            return OpenUsdStormChildRuntime.Pick(
                handle,
                request,
                _latestFrame,
                _contextGeneration);
        }
    }

    /// <summary>Queues one packed selection-highlight update on the render thread.</summary>
    /// <remarks>
    /// OpenUSD scene-index mode currently reduces instance-index highlights to
    /// whole-path selection; legacy scene-delegate mode honors supported indices.
    /// </remarks>
    public void SetSelection(SelectionState selection, Vector4 color)
    {
        lock (_gate)
        {
            OpenUsdStormChildRuntime.SetSelection(
                GetHandleLocked(),
                selection,
                color);
        }
    }

    /// <summary>Resizes the child and updates its effective DPI.</summary>
    public void Resize(int width, int height, uint dpi)
    {
        lock (_gate)
        {
            OpenUsdStormChildRuntime.Resize(GetHandleLocked(), width, height, dpi);
            _width = width;
            _height = height;
        }
    }

    /// <summary>Shows or hides the child native window.</summary>
    public void SetVisible(bool visible)
    {
        lock (_gate)
        {
            OpenUsdStormChildRuntime.SetVisible(GetHandleLocked(), visible);
        }
    }

    /// <summary>Moves keyboard focus to the child native window.</summary>
    public void Focus()
    {
        lock (_gate)
        {
            OpenUsdStormChildRuntime.Focus(GetHandleLocked());
        }
    }

    /// <summary>Abandons the lost GL engine and recreates the context/session on the render thread.</summary>
    public void SimulateContextLoss()
    {
        lock (_gate)
        {
            OpenUsdStormChildRuntime.SimulateContextLoss(GetHandleLocked());
            _contextGeneration =
                OpenUsdStormChildRuntime.GetDiagnostics(_handle).ContextGeneration;
        }
    }

    /// <summary>Gets the latest native child diagnostics.</summary>
    public OpenUsdStormChildDiagnostics GetDiagnostics()
    {
        lock (_gate)
        {
            OpenUsdStormChildDiagnostics diagnostics =
                OpenUsdStormChildRuntime.GetDiagnostics(GetHandleLocked());
            _contextGeneration = diagnostics.ContextGeneration;
            return diagnostics;
        }
    }

    /// <summary>
    /// Synchronously captures diagnostic evidence from the exact preserved completed frame.
    /// </summary>
    /// <remarks>
    /// The render thread preserves pixels before presentation; capture never reads a post-swap
    /// backbuffer. The hash is FNV-1a 64-bit over dimensions and tightly packed RGBA8 pixels.
    /// </remarks>
    public OpenUsdStormFramebufferCapture CaptureFramebuffer(
        uint backgroundRgba = 0xff0e0e0e,
        byte tolerance = 2,
        bool copyPixels = false)
    {
        lock (_gate)
        {
            return OpenUsdStormChildRuntime.CaptureFramebuffer(
                GetHandleLocked(),
                backgroundRgba,
                tolerance,
                copyPixels);
        }
    }

    internal string CompleteDeferredInitialization()
    {
        lock (_gate)
        {
            nint handle = GetHandleLocked();
            string rendererName = OpenUsdStormChildRuntime.GetRendererName(handle);
            OpenUsdStormChildDiagnostics diagnostics =
                OpenUsdStormChildRuntime.GetDiagnostics(handle);
            _contextGeneration = diagnostics.ContextGeneration;
            _width = diagnostics.Width;
            _height = diagnostics.Height;
            RendererName = rendererName;
            return rendererName;
        }
    }

    /// <summary>Stops the render thread, destroys Storm with its context current, and releases the stage.</summary>
    public void Dispose()
    {
        lock (_gate)
        {
            if (_handle == 0)
            {
                return;
            }
            if (Environment.CurrentManagedThreadId != _creatorThreadId)
            {
                throw new InvalidOperationException(
                    "The Storm native child must be disposed on its creator UI thread.");
            }
            OpenUsdStormChildRuntime.Destroy(_handle);
            _handle = 0;
            _lease?.Dispose();
            _lease = null;
        }
    }

    private nint GetHandleLocked()
    {
        ObjectDisposedException.ThrowIf(_handle == 0, this);
        return _handle;
    }
}

/// <summary>Creates native-child Storm sessions under a compatible Avalonia shell.</summary>
public static unsafe partial class OpenUsdStormChildRuntime
{
    private const string LibraryName = "openusd_storm_child";
    private const int ErrorBufferSize = 4096;
    private const uint ExpectedAbiVersion = 8;
    internal const uint NavigationInputVersion = 2;

    /// <summary>Gets the Storm child ABI version.</summary>
    public static uint AbiVersion => NativeMethods.GetAbiVersion();

    /// <summary>
    /// Installs the process-lifetime Linux X11 error dispatcher.
    /// </summary>
    /// <remarks>
    /// Linux callers must invoke this immediately after XInitThreads succeeds
    /// and before any other Xlib call or platform-toolkit initialization.
    /// Repeated calls are safe and reinstall the dispatcher if a platform
    /// toolkit replaced the process XError handler. Referencing the
    /// source-generated LibraryImport type or stubs does not invoke native
    /// code; native resolution occurs when a stub is called. This method
    /// therefore calls InitializeLinux before invoking GetAbiVersion.
    /// </remarks>
    public static void InitializeLinuxX11Dispatcher()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }
        InitializeLinuxX11DispatcherCore(
            () => InvokeStatus(
                (ref NativeErrorBuffer error) =>
                    NativeMethods.InitializeLinux(ref error)),
            NativeMethods.GetAbiVersion);
    }

    internal static void InitializeLinuxX11DispatcherCore(
        Action initializeLinux,
        Func<uint> getAbiVersion)
    {
        ArgumentNullException.ThrowIfNull(initializeLinux);
        ArgumentNullException.ThrowIfNull(getAbiVersion);

        initializeLinux();
        ValidateAbiVersion(getAbiVersion());
    }

    /// <summary>Creates a native child for the exact stage retained by a render source.</summary>
    public static OpenUsdStormChildSession Create(
        nint parentWindow,
        string pluginPath,
        UsdStageRenderSource source,
        int width,
        int height,
        uint dpi) =>
        CreateCore(
            parentWindow,
            pluginPath,
            source,
            width,
            height,
            dpi,
            deferNativeInitialization: false);

    internal static OpenUsdStormChildSession CreateForNativeHost(
        nint parentWindow,
        string pluginPath,
        UsdStageRenderSource source,
        int width,
        int height,
        uint dpi,
        bool deferNativeInitialization) =>
        CreateCore(
            parentWindow,
            pluginPath,
            source,
            width,
            height,
            dpi,
            deferNativeInitialization);

    private static OpenUsdStormChildSession CreateCore(
        nint parentWindow,
        string pluginPath,
        UsdStageRenderSource source,
        int width,
        int height,
        uint dpi,
        bool deferNativeInitialization)
    {
        if (!OperatingSystem.IsWindows() &&
            !OperatingSystem.IsLinux() &&
            !OperatingSystem.IsMacOS())
        {
            throw new PlatformNotSupportedException(
                "The Storm native child host is available only on Windows, Linux, and macOS.");
        }
        ArgumentOutOfRangeException.ThrowIfEqual(parentWindow, 0);
        ArgumentException.ThrowIfNullOrWhiteSpace(pluginPath);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        ArgumentOutOfRangeException.ThrowIfZero(dpi);
        ValidateAbiVersion(AbiVersion);

        UsdStageRenderLease lease = source.AcquireLease();
        try
        {
            nint handle = Invoke(
                (ref NativeErrorBuffer error) =>
                {
                    OpenUsdNativeStatus status = NativeMethods.Create(
                        parentWindow,
                        pluginPath,
                        lease.DangerousGetHandle(),
                        width,
                        height,
                        dpi,
                        out nint created,
                        ref error);
                    return (status, created);
                });
            try
            {
                nint window = Invoke(
                    (ref NativeErrorBuffer error) =>
                    {
                        OpenUsdNativeStatus status =
                            NativeMethods.GetWindow(handle, out nint createdWindow, ref error);
                        return (status, createdWindow);
                    });
                if (deferNativeInitialization)
                {
                    return new OpenUsdStormChildSession(
                        handle,
                        window,
                        lease,
                        string.Empty,
                        width: width,
                        height: height);
                }
                string rendererName = GetRendererName(handle);
                OpenUsdStormChildDiagnostics diagnostics = GetDiagnostics(handle);
                return new OpenUsdStormChildSession(
                    handle,
                    window,
                    lease,
                    rendererName,
                    diagnostics.ContextGeneration,
                    width,
                    height);
            }
            catch
            {
                Destroy(handle);
                throw;
            }
        }
        catch
        {
            lease.Dispose();
            throw;
        }
    }

    internal static void Destroy(nint handle) =>
        InvokeStatus((ref NativeErrorBuffer error) => NativeMethods.Destroy(handle, ref error));

    internal static void Render(
        nint handle,
        double timeCode,
        CameraState camera,
        ulong revision,
        ulong? sceneRevision) =>
        Render<NativeRenderCall>(
            handle,
            timeCode,
            camera,
            revision,
            sceneRevision);

    internal static void Render<TCall>(
        nint handle,
        double timeCode,
        CameraState camera,
        ulong revision = 0,
        ulong? sceneRevision = null)
        where TCall : struct, IRenderCall
    {
        var nativeCamera = new NativeRenderCamera(camera);
        Span<byte> errorBytes = stackalloc byte[ErrorBufferSize];
        OpenUsdNativeStatus status = TCall.Invoke(
            handle,
            timeCode,
            in nativeCamera,
            revision,
            sceneRevision.GetValueOrDefault(),
            sceneRevision.HasValue ? 1u : 0u,
            out _,
            out _,
            errorBytes,
            out nuint errorRequired);
        ThrowIfFailed(
            status,
            errorBytes,
            (nuint)errorBytes.Length,
            errorRequired);
    }

    internal static void RequestFrame(
        nint handle,
        double timeCode,
        ulong revision,
        CameraState camera,
        ulong? sceneRevision) =>
        RequestFrame<NativeRequestFrameCall>(
            handle,
            timeCode,
            revision,
            camera,
            sceneRevision);

    internal static void RequestFrame<TCall>(
        nint handle,
        double timeCode,
        ulong revision,
        CameraState camera,
        ulong? sceneRevision = null)
        where TCall : struct, IRequestFrameCall
    {
        var nativeCamera = new NativeRenderCamera(camera);
        Span<byte> errorBytes = stackalloc byte[ErrorBufferSize];
        OpenUsdNativeStatus status = TCall.Invoke(
            handle,
            timeCode,
            in nativeCamera,
            revision,
            sceneRevision.GetValueOrDefault(),
            sceneRevision.HasValue ? 1u : 0u,
            errorBytes,
            out nuint errorRequired);
        ThrowIfFailed(
            status,
            errorBytes,
            (nuint)errorBytes.Length,
            errorRequired);
    }

    internal static RenderPickResult Pick(
        nint handle,
        RenderPickRequest request,
        StormFrameBinding binding,
        ulong currentContextGeneration) =>
        StormPickingInterop.Pick<NativePickCall>(
            handle,
            request,
            binding,
            currentContextGeneration);

    internal static void SetSelection(
        nint handle,
        SelectionState selection,
        Vector4 color) =>
        StormPickingInterop.SetSelection<NativeSelectionCall>(
            handle,
            selection,
            color);

    internal static void Resize(nint handle, int width, int height, uint dpi) =>
        InvokeStatus((ref NativeErrorBuffer error) =>
            NativeMethods.Resize(handle, width, height, dpi, ref error));

    internal static void SetVisible(nint handle, bool visible) =>
        InvokeStatus((ref NativeErrorBuffer error) =>
            NativeMethods.SetVisible(handle, visible ? 1 : 0, ref error));

    internal static void Focus(nint handle) =>
        InvokeStatus((ref NativeErrorBuffer error) => NativeMethods.Focus(handle, ref error));

    internal static void InjectMacOSViewDiagnosticInput(nint view) =>
        InvokeStatus((ref NativeErrorBuffer error) =>
            NativeMethods.InjectMacOSViewDiagnosticInput(view, ref error));

    internal static void SimulateContextLoss(nint handle) =>
        InvokeStatus((ref NativeErrorBuffer error) =>
            NativeMethods.SimulateContextLoss(handle, ref error));

    internal static OpenUsdStormChildDiagnostics GetDiagnostics(nint handle)
    {
        NativeDiagnostics diagnostics = default;
        InvokeStatus((ref NativeErrorBuffer error) =>
            NativeMethods.GetDiagnostics(handle, ref diagnostics, ref error));
        return diagnostics.ToManaged();
    }

    internal static OpenUsdStormNavigationInput GetNavigationInput(nint handle) =>
        GetNavigationInput<NativeNavigationInputCall>(handle);

    internal static OpenUsdStormNavigationInput GetNavigationInput<TCall>(nint handle)
        where TCall : struct, INavigationInputCall
    {
        NativeNavigationInput input = NativeNavigationInput.Create();
        Span<byte> errorBytes = stackalloc byte[ErrorBufferSize];
        OpenUsdNativeStatus status = TCall.Invoke(
            handle,
            ref input,
            errorBytes,
            out nuint errorRequired);
        ThrowIfFailed(
            status,
            errorBytes,
            (nuint)errorBytes.Length,
            errorRequired);
        input.Validate();
        return input.ToManaged();
    }

    internal static OpenUsdStormFramebufferCapture CaptureFramebuffer(
        nint handle,
        uint backgroundRgba,
        byte tolerance,
        bool copyPixels)
    {
        NativeFramebufferCapture capture = default;
        nuint required = 0;
        if (!copyPixels)
        {
            InvokeStatus((ref NativeErrorBuffer error) =>
                NativeMethods.CaptureFramebuffer(
                    handle,
                    backgroundRgba,
                    tolerance,
                    0,
                    null,
                    0,
                    out required,
                    ref capture,
                    ref error));
            return capture.ToManaged(null);
        }

        NativeDiagnostics diagnostics = default;
        InvokeStatus((ref NativeErrorBuffer error) =>
            NativeMethods.GetDiagnostics(handle, ref diagnostics, ref error));
        if (diagnostics.Width <= 0 || diagnostics.Height <= 0)
        {
            throw new InvalidOperationException(
                "The Storm child returned invalid framebuffer dimensions.");
        }
        int byteCount = GetCaptureByteCount(diagnostics.Width, diagnostics.Height);

        byte[] pixels = GC.AllocateUninitializedArray<byte>(byteCount);
        Span<byte> errorBytes = stackalloc byte[ErrorBufferSize];
        nuint written;
        OpenUsdNativeStatus status;
        NativeErrorBuffer nativeError;
        fixed (byte* errorPointer = errorBytes)
        fixed (byte* pixelPointer = pixels)
        {
            nativeError = new NativeErrorBuffer(errorPointer, (nuint)errorBytes.Length);
            status = NativeMethods.CaptureFramebuffer(
                handle,
                backgroundRgba,
                tolerance,
                0,
                pixelPointer,
                (nuint)byteCount,
                out written,
                ref capture,
                ref nativeError);
        }
        ThrowIfFailed(status, errorBytes, nativeError);
        if (written != (nuint)byteCount)
        {
            throw new InvalidOperationException(
                "The Storm child framebuffer dimensions changed during diagnostic capture.");
        }
        return capture.ToManaged(pixels);
    }

    internal static int GetCaptureByteCount(int width, int height)
    {
        if (width <= 0 || height <= 0)
        {
            throw new InvalidOperationException(
                "The Storm child returned invalid framebuffer dimensions.");
        }
        nuint byteCount = checked((nuint)width * (nuint)height * 4u);
        if (byteCount > 64u * 1024u * 1024u || byteCount > int.MaxValue)
        {
            throw new InvalidOperationException(
                "The Storm child framebuffer exceeds the 64 MiB diagnostic capture limit.");
        }
        return checked((int)byteCount);
    }

    internal static (long Live, long Peak) GetChildCounts() =>
        (
            checked((long)NativeMethods.GetLiveCount()),
            checked((long)NativeMethods.GetPeakCount())
        );

    internal static string GetRendererName(nint handle)
    {
        nuint required = 0;
        InvokeExpectedBufferTooSmall(
            (ref NativeErrorBuffer error) =>
                NativeMethods.GetRendererName(handle, null, 0, out required, ref error));
        if (required is 0 or > int.MaxValue)
        {
            throw new InvalidOperationException(
                "The Storm child returned an invalid renderer-name length.");
        }
        byte[] bytes = GC.AllocateUninitializedArray<byte>((int)required);
        nuint written = 0;
        Span<byte> errorBytes = stackalloc byte[ErrorBufferSize];
        fixed (byte* errorPointer = errorBytes)
        fixed (byte* buffer = bytes)
        {
            var error = new NativeErrorBuffer(errorPointer, (nuint)errorBytes.Length);
            OpenUsdNativeStatus status = NativeMethods.GetRendererName(
                handle,
                buffer,
                required,
                out written,
                ref error);
            ThrowIfFailed(status, errorBytes, error);
            if (written != required)
            {
                throw new InvalidOperationException(
                    "The Storm child returned a mismatched renderer-name length.");
            }
        }
        return Encoding.UTF8.GetString(bytes, 0, bytes.Length - 1);
    }

    private static nint Invoke(NativeHandleOperation operation)
    {
        Span<byte> errorBytes = stackalloc byte[ErrorBufferSize];
        fixed (byte* errorPointer = errorBytes)
        {
            var error = new NativeErrorBuffer(errorPointer, (nuint)errorBytes.Length);
            (OpenUsdNativeStatus status, nint handle) = operation(ref error);
            ThrowIfFailed(status, errorBytes, error);
            return handle;
        }
    }

    private static void ValidateAbiVersion(uint actual)
    {
        if (actual != ExpectedAbiVersion)
        {
            throw new OpenUsdStormException(
                OpenUsdNativeStatus.NativeError,
                $"Storm child ABI mismatch: managed={ExpectedAbiVersion}, native={actual}.");
        }
    }

    private static void InvokeExpectedBufferTooSmall(NativeStatusOperation operation) =>
        InvokeStatus(
            operation,
            statusValidation: status => status == OpenUsdNativeStatus.BufferTooSmall);

    private static void InvokeStatus(
        NativeStatusOperation operation,
        Func<OpenUsdNativeStatus, bool>? statusValidation = null)
    {
        Span<byte> errorBytes = stackalloc byte[ErrorBufferSize];
        fixed (byte* errorPointer = errorBytes)
        {
            var error = new NativeErrorBuffer(errorPointer, (nuint)errorBytes.Length);
            OpenUsdNativeStatus status = operation(ref error);
            if (statusValidation?.Invoke(status) == true)
            {
                return;
            }
            ThrowIfFailed(status, errorBytes, error);
        }
    }

    private static void ThrowIfFailed(
        OpenUsdNativeStatus status,
        ReadOnlySpan<byte> errorBytes,
        NativeErrorBuffer error) =>
        ThrowIfFailed(
            status,
            errorBytes,
            error.Capacity,
            error.Required);

    private static void ThrowIfFailed(
        OpenUsdNativeStatus status,
        ReadOnlySpan<byte> errorBytes,
        nuint errorCapacity,
        nuint errorRequired)
    {
        if (status == OpenUsdNativeStatus.Ok)
        {
            return;
        }
        int terminator = errorBytes.IndexOf((byte)0);
        int length = terminator >= 0 ? terminator : errorBytes.Length;
        string message = length == 0
            ? $"Storm child operation failed with status {status}."
            : Encoding.UTF8.GetString(errorBytes[..length]);
        if (errorRequired > errorCapacity)
        {
            message += $" The full native diagnostic required {errorRequired} bytes.";
        }
        throw new OpenUsdStormException(status, message);
    }

    private delegate (OpenUsdNativeStatus Status, nint Handle) NativeHandleOperation(
        ref NativeErrorBuffer error);

    private delegate OpenUsdNativeStatus NativeStatusOperation(ref NativeErrorBuffer error);

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct NativeErrorBuffer
    {
        internal NativeErrorBuffer(byte* data, nuint capacity)
        {
            Data = data;
            Capacity = capacity;
            Required = 0;
        }

        internal readonly byte* Data;
        internal readonly nuint Capacity;
        internal readonly nuint Required;
    }

    internal interface IRenderCall
    {
        static abstract OpenUsdNativeStatus Invoke(
            nint child,
            double timeCode,
            in NativeRenderCamera camera,
            ulong stateRevision,
            ulong sceneRevision,
            uint revisionFlags,
            out ulong frameCount,
            out int converged,
            Span<byte> errorBytes,
            out nuint errorRequired);
    }

    internal interface INavigationInputCall
    {
        static abstract OpenUsdNativeStatus Invoke(
            nint child,
            ref NativeNavigationInput input,
            Span<byte> errorBytes,
            out nuint errorRequired);
    }

    internal interface IRequestFrameCall
    {
        static abstract OpenUsdNativeStatus Invoke(
            nint child,
            double timeCode,
            in NativeRenderCamera camera,
            ulong revision,
            ulong sceneRevision,
            uint revisionFlags,
            Span<byte> errorBytes,
            out nuint errorRequired);
    }

    private readonly struct NativeRenderCall : IRenderCall
    {
        public static OpenUsdNativeStatus Invoke(
            nint child,
            double timeCode,
            in NativeRenderCamera camera,
            ulong stateRevision,
            ulong sceneRevision,
            uint revisionFlags,
            out ulong frameCount,
            out int converged,
            Span<byte> errorBytes,
            out nuint errorRequired)
        {
            fixed (byte* errorPointer = errorBytes)
            {
                var error = new NativeErrorBuffer(
                    errorPointer,
                    (nuint)errorBytes.Length);
                OpenUsdNativeStatus status = NativeMethods.RenderV2(
                    child,
                    timeCode,
                    in camera,
                    stateRevision,
                    sceneRevision,
                    revisionFlags,
                    out frameCount,
                    out converged,
                    ref error);
                errorRequired = error.Required;
                return status;
            }
        }
    }

    private readonly struct NativeRequestFrameCall : IRequestFrameCall
    {
        public static OpenUsdNativeStatus Invoke(
            nint child,
            double timeCode,
            in NativeRenderCamera camera,
            ulong revision,
            ulong sceneRevision,
            uint revisionFlags,
            Span<byte> errorBytes,
            out nuint errorRequired)
        {
            fixed (byte* errorPointer = errorBytes)
            {
                var error = new NativeErrorBuffer(
                    errorPointer,
                    (nuint)errorBytes.Length);
                OpenUsdNativeStatus status = NativeMethods.RequestFrameV3(
                    child,
                    timeCode,
                    in camera,
                    revision,
                    sceneRevision,
                    revisionFlags,
                    ref error);
                errorRequired = error.Required;
                return status;
            }
        }
    }

    private readonly struct NativePickCall : StormPickingInterop.IStormPickCall
    {
        public static OpenUsdNativeStatus Invoke(
            nint child,
            in StormPickingInterop.NativePickRequest request,
            ref StormPickingInterop.NativePickResult result,
            Span<byte> primPath,
            Span<byte> instancerPath,
            Span<StormPickingInterop.NativePickInstanceContext> instanceContext,
            Span<byte> instanceContextPaths,
            Span<byte> errorBytes,
            out nuint errorRequired)
        {
            fixed (byte* primPointer = primPath)
            fixed (byte* instancerPointer = instancerPath)
            fixed (StormPickingInterop.NativePickInstanceContext* contextPointer =
                instanceContext)
            fixed (byte* contextPathsPointer = instanceContextPaths)
            fixed (byte* errorPointer = errorBytes)
            {
                var error = new NativeErrorBuffer(
                    errorPointer,
                    (nuint)errorBytes.Length);
                OpenUsdNativeStatus status = NativeMethods.Pick(
                    child,
                    in request,
                    ref result,
                    primPointer,
                    checked((uint)primPath.Length),
                    instancerPointer,
                    checked((uint)instancerPath.Length),
                    contextPointer,
                    checked((uint)instanceContext.Length),
                    contextPathsPointer,
                    checked((uint)instanceContextPaths.Length),
                    ref error);
                errorRequired = error.Required;
                return status;
            }
        }
    }

    private readonly struct NativeSelectionCall :
        StormPickingInterop.IStormSelectionCall
    {
        public static OpenUsdNativeStatus Invoke(
            nint child,
            ReadOnlySpan<StormPickingInterop.NativeSelectionItem> items,
            ReadOnlySpan<byte> pathBytes,
            Vector4 color,
            Span<byte> errorBytes,
            out nuint errorRequired)
        {
            fixed (StormPickingInterop.NativeSelectionItem* itemPointer = items)
            fixed (byte* pathPointer = pathBytes)
            fixed (byte* errorPointer = errorBytes)
            {
                var update = new StormPickingInterop.NativeSelectionUpdate
                {
                    StructSize = checked((uint)Unsafe.SizeOf<
                        StormPickingInterop.NativeSelectionUpdate>()),
                    Version = StormPickingInterop.SelectionUpdateVersion,
                    ItemCount = checked((uint)items.Length),
                    Red = color.X,
                    Green = color.Y,
                    Blue = color.Z,
                    Alpha = color.W,
                    Items = itemPointer,
                    PathBytes = pathPointer,
                    PathBytesSize = checked((uint)pathBytes.Length),
                };
                var error = new NativeErrorBuffer(
                    errorPointer,
                    (nuint)errorBytes.Length);
                OpenUsdNativeStatus status = NativeMethods.SetSelection(
                    child,
                    in update,
                    ref error);
                errorRequired = error.Required;
                return status;
            }
        }
    }

    private readonly struct NativeNavigationInputCall : INavigationInputCall
    {
        public static OpenUsdNativeStatus Invoke(
            nint child,
            ref NativeNavigationInput input,
            Span<byte> errorBytes,
            out nuint errorRequired)
        {
            fixed (byte* errorPointer = errorBytes)
            {
                var error = new NativeErrorBuffer(
                    errorPointer,
                    (nuint)errorBytes.Length);
                OpenUsdNativeStatus status = NativeMethods.GetNavigationInput(
                    child,
                    ref input,
                    ref error);
                errorRequired = error.Required;
                return status;
            }
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeDiagnostics
    {
        internal ulong FrameCount;
        internal ulong PixelSignature;
        internal ulong PixelSampleCount;
        internal ulong FocusCount;
        internal ulong PointerCount;
        internal ulong WheelCount;
        internal ulong KeyCount;
        internal ulong ContextGeneration;
        internal ulong CoalescedRequestCount;
        internal ulong CancelledCommandCount;
        internal ulong TeardownFallbackCount;
        internal ulong LatestRequestedRevision;
        internal ulong LatestRequestedCameraSignature;
        internal ulong LatestRenderedCameraSignature;
        internal uint RenderThreadId;
        internal uint CreatorThreadId;
        internal uint PendingCommandCount;
        internal uint PeakPendingCommandCount;
        internal int GlMajor;
        internal int GlMinor;
        internal int CompatibilityProfile;
        internal int Width;
        internal int Height;
        internal uint Dpi;
        internal int Visible;
        internal int Focused;
        internal int Converged;

        internal readonly OpenUsdStormChildDiagnostics ToManaged() => new(
            FrameCount,
            PixelSignature,
            PixelSampleCount,
            FocusCount,
            PointerCount,
            WheelCount,
            KeyCount,
            ContextGeneration,
            CoalescedRequestCount,
            CancelledCommandCount,
            TeardownFallbackCount,
            LatestRequestedRevision,
            LatestRequestedCameraSignature,
            LatestRenderedCameraSignature,
            RenderThreadId,
            CreatorThreadId,
            PendingCommandCount,
            PeakPendingCommandCount,
            GlMajor,
            GlMinor,
            CompatibilityProfile != 0,
            Width,
            Height,
            Dpi,
            Visible != 0,
            Focused != 0,
            Converged != 0);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeFramebufferCapture
    {
        internal ulong FrameCount;
        internal ulong PixelHash;
        internal ulong PixelCount;
        internal ulong NonBackgroundPixelCount;
        internal int Width;
        internal int Height;
        internal uint Dpi;
        internal uint BackgroundRgba;
        internal uint AverageRgba;
        internal uint MinimumRgba;
        internal uint MaximumRgba;
        internal uint ReadBuffer;

        internal readonly OpenUsdStormFramebufferCapture ToManaged(byte[]? pixels) => new(
            FrameCount,
            PixelHash,
            PixelCount,
            NonBackgroundPixelCount,
            Width,
            Height,
            Dpi,
            BackgroundRgba,
            AverageRgba,
            MinimumRgba,
            MaximumRgba,
            ReadBuffer,
            pixels);
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct NativeNavigationInput
    {
        private const OpenUsdStormPointerButtons AllButtons =
            OpenUsdStormPointerButtons.Left |
            OpenUsdStormPointerButtons.Middle |
            OpenUsdStormPointerButtons.Right;
        private const OpenUsdStormInputModifiers AllModifiers =
            OpenUsdStormInputModifiers.Alt |
            OpenUsdStormInputModifiers.Shift |
            OpenUsdStormInputModifiers.Control |
            OpenUsdStormInputModifiers.Meta;
        private const OpenUsdStormNavigationState AllStates =
            OpenUsdStormNavigationState.Focused |
            OpenUsdStormNavigationState.Inside;

        internal uint StructSize;
        internal uint Version;
        internal ulong Sequence;
        internal int PointerX;
        internal int PointerY;
        internal OpenUsdStormPointerButtons Buttons;
        internal OpenUsdStormInputModifiers Modifiers;
        internal double CumulativeWheelDelta;
        internal ulong FrameSelectedPressCount;
        internal ulong ResetAutomaticPressCount;
        internal ulong ToggleProjectionPressCount;
        internal OpenUsdStormNavigationState State;
        internal uint Reserved;
        internal ulong OrbitLeftPressCount;
        internal ulong OrbitRightPressCount;
        internal ulong OrbitUpPressCount;
        internal ulong OrbitDownPressCount;

        internal static NativeNavigationInput Create() => new()
        {
            StructSize = checked((uint)Unsafe.SizeOf<NativeNavigationInput>()),
            Version = NavigationInputVersion,
        };

        internal readonly void Validate()
        {
            if (StructSize != Unsafe.SizeOf<NativeNavigationInput>() ||
                Version != NavigationInputVersion ||
                (Buttons & ~AllButtons) != 0 ||
                (Modifiers & ~AllModifiers) != 0 ||
                (State & ~AllStates) != 0 ||
                !double.IsFinite(CumulativeWheelDelta) ||
                Reserved != 0)
            {
                throw new OpenUsdStormException(
                    OpenUsdNativeStatus.NativeError,
                    "The Storm child returned an incompatible navigation input snapshot.");
            }
        }

        internal readonly OpenUsdStormNavigationInput ToManaged() =>
            new(
                Sequence,
                PointerX,
                PointerY,
                Buttons,
                Modifiers,
                CumulativeWheelDelta,
                FrameSelectedPressCount,
                ResetAutomaticPressCount,
                ToggleProjectionPressCount,
                State)
            {
                OrbitLeftPressCount = OrbitLeftPressCount,
                OrbitRightPressCount = OrbitRightPressCount,
                OrbitUpPressCount = OrbitUpPressCount,
                OrbitDownPressCount = OrbitDownPressCount,
            };
    }

    private static partial class NativeMethods
    {
        [LibraryImport(LibraryName, EntryPoint = "openusd_storm_child_get_abi_version")]
        [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
        internal static partial uint GetAbiVersion();

        [LibraryImport(
            LibraryName,
            EntryPoint = "openusd_storm_child_initialize_linux")]
        [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
        internal static partial OpenUsdNativeStatus InitializeLinux(
            ref NativeErrorBuffer error);

        [LibraryImport(
            LibraryName,
            EntryPoint = "openusd_storm_child_create",
            StringMarshalling = StringMarshalling.Utf8)]
        [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
        internal static partial OpenUsdNativeStatus Create(
            nint parentWindow,
            string pluginPath,
            nint stage,
            int width,
            int height,
            uint dpi,
            out nint child,
            ref NativeErrorBuffer error);

        [LibraryImport(LibraryName, EntryPoint = "openusd_storm_child_destroy")]
        [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
        internal static partial OpenUsdNativeStatus Destroy(
            nint child,
            ref NativeErrorBuffer error);

        [LibraryImport(LibraryName, EntryPoint = "openusd_storm_child_get_window")]
        [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
        internal static partial OpenUsdNativeStatus GetWindow(
            nint child,
            out nint window,
            ref NativeErrorBuffer error);

        [LibraryImport(LibraryName, EntryPoint = "openusd_storm_child_get_renderer_name")]
        [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
        internal static partial OpenUsdNativeStatus GetRendererName(
            nint child,
            byte* buffer,
            nuint capacity,
            out nuint required,
            ref NativeErrorBuffer error);

        [LibraryImport(LibraryName, EntryPoint = "openusd_storm_child_render_v2")]
        [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
        internal static partial OpenUsdNativeStatus RenderV2(
            nint child,
            double timeCode,
            in NativeRenderCamera camera,
            ulong stateRevision,
            ulong sceneRevision,
            uint revisionFlags,
            out ulong frameCount,
            out int converged,
            ref NativeErrorBuffer error);

        [LibraryImport(LibraryName, EntryPoint = "openusd_storm_child_request_frame_v3")]
        [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
        internal static partial OpenUsdNativeStatus RequestFrameV3(
            nint child,
            double timeCode,
            in NativeRenderCamera camera,
            ulong revision,
            ulong sceneRevision,
            uint revisionFlags,
            ref NativeErrorBuffer error);

        [LibraryImport(LibraryName, EntryPoint = "openusd_storm_child_pick")]
        [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
        internal static partial OpenUsdNativeStatus Pick(
            nint child,
            in StormPickingInterop.NativePickRequest request,
            ref StormPickingInterop.NativePickResult result,
            byte* primPath,
            uint primPathCapacity,
            byte* instancerPath,
            uint instancerPathCapacity,
            StormPickingInterop.NativePickInstanceContext* instanceContext,
            uint instanceContextCapacity,
            byte* instanceContextPaths,
            uint instanceContextPathsCapacity,
            ref NativeErrorBuffer error);

        [LibraryImport(
            LibraryName,
            EntryPoint = "openusd_storm_child_set_selection")]
        [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
        internal static partial OpenUsdNativeStatus SetSelection(
            nint child,
            in StormPickingInterop.NativeSelectionUpdate update,
            ref NativeErrorBuffer error);

        [LibraryImport(LibraryName, EntryPoint = "openusd_storm_child_resize")]
        [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
        internal static partial OpenUsdNativeStatus Resize(
            nint child,
            int width,
            int height,
            uint dpi,
            ref NativeErrorBuffer error);

        [LibraryImport(LibraryName, EntryPoint = "openusd_storm_child_set_visible")]
        [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
        internal static partial OpenUsdNativeStatus SetVisible(
            nint child,
            int visible,
            ref NativeErrorBuffer error);

        [LibraryImport(LibraryName, EntryPoint = "openusd_storm_child_focus")]
        [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
        internal static partial OpenUsdNativeStatus Focus(
            nint child,
            ref NativeErrorBuffer error);

        [LibraryImport(
            LibraryName,
            EntryPoint = "openusd_storm_child_macos_inject_view_diagnostic_input")]
        [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
        internal static partial OpenUsdNativeStatus InjectMacOSViewDiagnosticInput(
            nint view,
            ref NativeErrorBuffer error);

        [LibraryImport(
            LibraryName,
            EntryPoint = "openusd_storm_child_simulate_context_loss")]
        [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
        internal static partial OpenUsdNativeStatus SimulateContextLoss(
            nint child,
            ref NativeErrorBuffer error);

        [LibraryImport(LibraryName, EntryPoint = "openusd_storm_child_get_diagnostics")]
        [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
        internal static partial OpenUsdNativeStatus GetDiagnostics(
            nint child,
            ref NativeDiagnostics diagnostics,
            ref NativeErrorBuffer error);

        [LibraryImport(
            LibraryName,
            EntryPoint = "openusd_storm_child_get_navigation_input")]
        [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
        internal static partial OpenUsdNativeStatus GetNavigationInput(
            nint child,
            ref NativeNavigationInput input,
            ref NativeErrorBuffer error);

        [LibraryImport(
            LibraryName,
            EntryPoint = "openusd_storm_child_capture_framebuffer")]
        [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
        internal static partial OpenUsdNativeStatus CaptureFramebuffer(
            nint child,
            uint backgroundRgba,
            byte tolerance,
            uint flags,
            byte* rgbaBuffer,
            nuint rgbaCapacity,
            out nuint rgbaRequired,
            ref NativeFramebufferCapture capture,
            ref NativeErrorBuffer error);

        [LibraryImport(
            LibraryName,
            EntryPoint = "openusd_storm_child_diagnostic_get_live_count")]
        [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
        internal static partial nuint GetLiveCount();

        [LibraryImport(
            LibraryName,
            EntryPoint = "openusd_storm_child_diagnostic_get_peak_count")]
        [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
        internal static partial nuint GetPeakCount();
    }
}
