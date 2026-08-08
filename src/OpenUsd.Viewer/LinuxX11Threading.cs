// Copyright (c) marcschier. Licensed under the MIT License.

using System.Runtime.InteropServices;
using OpenUsd.Rendering.Storm;

namespace OpenUsd.Viewer;

internal static class LinuxX11Threading
{
    private static readonly InitializationState State = new();

    internal static bool IsInitialized => State.IsInitialized;

    internal static void Initialize()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }
        InitializeCore(
            State,
            XInitThreads,
            OpenUsdStormChildRuntime.InitializeLinuxX11Dispatcher);
    }

    internal static void RebindAfterPlatformSetup()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }
        if (!IsInitialized)
        {
            throw new InvalidOperationException(
                "Linux X11 threading must be initialized before platform dispatcher rebinding.");
        }
        // Run 31215759239 hung on Linux with the X11 window never opened, and
        // this was the only Linux-specific call between platform setup and the
        // window being shown. Moving it here is not a proven cause, but the
        // Storm child is the only thing that needs the error handler, and it
        // needs it only immediately before it creates its native child.
        OpenUsdStormChildRuntime.InitializeLinuxX11Dispatcher();
    }

    internal static void InitializeCore(
        InitializationState state,
        Func<int> xInitThreads,
        Action initializeLinux,
        Func<uint> getAbiVersion) =>
        InitializeCore(
            state,
            xInitThreads,
            () => OpenUsdStormChildRuntime.InitializeLinuxX11DispatcherCore(
                initializeLinux,
                getAbiVersion));

    internal static void InitializeCore(
        InitializationState state,
        Func<int> xInitThreads,
        Action initializeStorm)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(xInitThreads);
        ArgumentNullException.ThrowIfNull(initializeStorm);

        if (state.IsInitialized)
        {
            return;
        }
        lock (state.Gate)
        {
            if (state.IsInitialized)
            {
                return;
            }
            if (xInitThreads() == 0)
            {
                throw new InvalidOperationException(
                    "XInitThreads failed. It must succeed before Avalonia or any other Xlib call.");
            }
            initializeStorm();
            state.MarkInitialized();
        }
    }

    [DllImport("libX11.so.6", ExactSpelling = true)]
    private static extern int XInitThreads();

    internal sealed class InitializationState
    {
        private int _initialized;

        internal object Gate { get; } = new();

        internal bool IsInitialized => Volatile.Read(ref _initialized) == 2;

        internal void MarkInitialized() => Volatile.Write(ref _initialized, 2);
    }
}
