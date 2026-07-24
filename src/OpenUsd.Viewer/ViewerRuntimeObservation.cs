// Copyright (c) marcschier. Licensed under the MIT License.

using System.Runtime.InteropServices;
using Avalonia.Controls;
using Avalonia.Platform;
using OpenUsd.Rendering;

namespace OpenUsd.Viewer;

internal sealed record ViewerCompositionEvidence(
    string Backend,
    string[] SupportedImageHandleTypes,
    string[] SupportedSemaphoreHandleTypes,
    string DeviceLuid,
    string DeviceUuid,
    string UsedImageHandleType,
    string[] UsedSemaphoreHandleTypes,
    string SynchronizationKind,
    long SuccessfulImports,
    long SuccessfulPresents,
    bool CompositionHostVisible);

internal sealed class ViewerCompositionObservation
{
    private readonly Lock _gate = new();
    private RenderBackendKind _backend;
    private string[] _supportedImages = [];
    private string[] _supportedSemaphores = [];
    private string _deviceLuid = string.Empty;
    private string _deviceUuid = string.Empty;
    private string _usedImage = string.Empty;
    private string[] _usedSemaphores = [];
    private string _synchronization = string.Empty;
    private long _imports;
    private long _presents;

    internal void ObserveTarget(
        RenderBackendKind backend,
        CompositionPresentationTarget target)
    {
        ArgumentNullException.ThrowIfNull(target);
        lock (_gate)
        {
            _backend = backend;
            _supportedImages = [.. target.ImageHandleTypes];
            _supportedSemaphores = [.. target.SemaphoreHandleTypes];
            _deviceLuid = Convert.ToHexString([.. target.DeviceLuid]);
            _deviceUuid = Convert.ToHexString([.. target.DeviceUuid]);
        }
    }

    internal void ObserveImport(ICompositionPresentationFrame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        lock (_gate)
        {
            _usedImage = frame.Image.HandleType;
            _usedSemaphores =
            [
                .. frame.Semaphores
                    .Select(semaphore => semaphore.HandleType)
                    .Distinct(StringComparer.Ordinal)
                    .Order(StringComparer.Ordinal)
            ];
            _imports++;
        }
    }

    internal void ObservePresent(CompositionFrameSynchronization synchronization)
    {
        lock (_gate)
        {
            _synchronization = synchronization.Kind.ToString();
            _presents++;
        }
    }

    internal ViewerCompositionEvidence Snapshot(bool hostVisible)
    {
        lock (_gate)
        {
            return new ViewerCompositionEvidence(
                _backend.ToString(),
                [.. _supportedImages],
                [.. _supportedSemaphores],
                _deviceLuid,
                _deviceUuid,
                _usedImage,
                [.. _usedSemaphores],
                _synchronization,
                _imports,
                _presents,
                hostVisible);
        }
    }
}

internal sealed record ViewerHwndEvidence(
    string Backend,
    string Phase,
    string TopLevelHwnd,
    uint TopLevelProcessId,
    uint TopLevelThreadId,
    string ExpectedStormHwnd,
    string ObservedStormHwnd,
    string StormClassName,
    bool StormIsWindow,
    bool StormIsVisible,
    string StormParentHwnd,
    bool StormParentWithinViewer,
    uint StormProcessId,
    uint StormThreadId,
    int EnumeratedStormCount,
    int VisibleStormCount,
    int LiveKnownStormCount,
    int StaleLiveStormCount,
    bool CompositionHostVisible);

internal sealed class ViewerWindowsHwndObserver
{
    private const string StormWindowClass = "OpenUsdStormNativeChild";
    private readonly HashSet<nint> _knownStormWindows = [];

    internal ViewerHwndEvidence Observe(
        Window window,
        RendererSwitchingViewport viewport,
        RenderBackendKind backend,
        string phase)
    {
        ArgumentNullException.ThrowIfNull(window);
        ArgumentNullException.ThrowIfNull(viewport);
        ArgumentException.ThrowIfNullOrWhiteSpace(phase);
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("HWND evidence requires Windows.");
        }

        IPlatformHandle platform = window.TryGetPlatformHandle()
            ?? throw new InvalidOperationException("Avalonia did not expose the Viewer HWND.");
        nint topLevel = platform.Handle;
        uint topThread = GetWindowThreadProcessId(topLevel, out uint topProcess);
        var enumerated = new List<nint>();
        EnumChildProc callback = (child, _) =>
        {
            if (string.Equals(GetWindowClass(child), StormWindowClass, StringComparison.Ordinal))
            {
                enumerated.Add(child);
            }
            return true;
        };
        Marshal.SetLastPInvokeError(0);
        if (!EnumChildWindows(topLevel, callback, 0) &&
            Marshal.GetLastPInvokeError() != 0)
        {
            throw new InvalidOperationException(
                $"EnumChildWindows failed with Win32 error {Marshal.GetLastPInvokeError()}.");
        }

        foreach (nint child in enumerated)
        {
            _knownStormWindows.Add(child);
        }
        nint expected = viewport.GetEvidenceNativeWindow();
        nint observed = expected != 0 && enumerated.Contains(expected)
            ? expected
            : enumerated.FirstOrDefault();
        nint parent = observed == 0 ? 0 : GetParent(observed);
        uint stormThread = observed == 0
            ? 0
            : GetWindowThreadProcessId(observed, out _);
        uint stormProcess = 0;
        if (observed != 0)
        {
            _ = GetWindowThreadProcessId(observed, out stormProcess);
        }
        bool stormIsWindow = observed != 0 && IsWindow(observed);
        bool stormVisible = stormIsWindow && IsWindowVisible(observed);
        bool parentWithinViewer = parent != 0 &&
            (parent == topLevel || IsChild(topLevel, parent));
        int liveKnown = _knownStormWindows.Count(IsWindow);
        int staleLive = _knownStormWindows.Count(handle =>
            IsWindow(handle) && handle != expected);
        return new ViewerHwndEvidence(
            backend.ToString(),
            phase,
            FormatHandle(topLevel),
            topProcess,
            topThread,
            FormatHandle(expected),
            FormatHandle(observed),
            observed == 0 ? string.Empty : GetWindowClass(observed),
            stormIsWindow,
            stormVisible,
            FormatHandle(parent),
            parentWithinViewer,
            stormProcess,
            stormThread,
            enumerated.Count,
            enumerated.Count(IsWindowVisible),
            liveKnown,
            staleLive,
            viewport.HasVisibleCompositionHost);
    }

    private static string FormatHandle(nint handle) =>
        handle == 0 ? string.Empty : $"0x{unchecked((nuint)handle):X}";

    private static string GetWindowClass(nint window)
    {
        var name = new char[256];
        int length = GetClassName(window, name, name.Length);
        return length == 0 ? string.Empty : new string(name, 0, length);
    }

    private delegate bool EnumChildProc(nint window, nint parameter);

    [DllImport(
        "user32.dll",
        EntryPoint = "EnumChildWindows",
        SetLastError = true,
        ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumChildWindows(
        nint parent,
        EnumChildProc callback,
        nint parameter);

    [DllImport(
        "user32.dll",
        EntryPoint = "GetClassNameW",
        SetLastError = true,
        ExactSpelling = true,
        CharSet = CharSet.Unicode)]
    private static extern int GetClassName(
        nint window,
        [Out] char[] className,
        int maximum);

    [DllImport("user32.dll", EntryPoint = "IsWindow", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindow(nint window);

    [DllImport("user32.dll", EntryPoint = "IsWindowVisible", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(nint window);

    [DllImport("user32.dll", EntryPoint = "IsChild", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsChild(nint parent, nint child);

    [DllImport("user32.dll", EntryPoint = "GetParent", ExactSpelling = true)]
    private static extern nint GetParent(nint window);

    [DllImport(
        "user32.dll",
        EntryPoint = "GetWindowThreadProcessId",
        ExactSpelling = true)]
    private static extern uint GetWindowThreadProcessId(nint window, out uint processId);
}
