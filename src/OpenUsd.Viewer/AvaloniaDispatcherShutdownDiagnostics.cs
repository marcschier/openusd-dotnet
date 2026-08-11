// Copyright (c) marcschier. Licensed under the MIT License.

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;

namespace OpenUsd.Viewer;

internal static class AvaloniaDispatcherShutdownDiagnostics
{
    private static int s_subscribed;
    private static int s_interpretationLogged;
    private static int s_shutdownStarted;

    internal static void EnsureSubscribed(string reason)
    {
        if (Interlocked.Exchange(ref s_subscribed, 1) != 0)
        {
            return;
        }

        Dispatcher.UIThread.ShutdownStarted += (_, _) =>
        {
            Volatile.Write(ref s_shutdownStarted, 1);
            ViewerStartupOptions.WriteStatus(
                "Viewer dispatcher shutdown: ShutdownStarted " + FormatThreadStatus());
        };
        Dispatcher.UIThread.ShutdownFinished += (_, _) =>
            ViewerStartupOptions.WriteStatus(
                "Viewer dispatcher shutdown: ShutdownFinished " + FormatThreadStatus());

        ViewerStartupOptions.WriteStatus(
            "Viewer dispatcher shutdown: probes subscribed " +
            $"reason={reason}; avalonia-version={GetAvaloniaVersion()}; " +
            "api-surface=ShutdownStarted,ShutdownFinished; " +
            "HasShutdownStarted=not-present-in-compiled-Avalonia; " +
            FormatThreadStatus());
    }

    internal static void LogInterpretationOnce()
    {
        if (Interlocked.Exchange(ref s_interpretationLogged, 1) != 0)
        {
            return;
        }

        ViewerStartupOptions.WriteStatus(
            "Viewer dispatcher shutdown-vs-block interpretation: " +
            "ShutdownStarted or ShutdownFinished means dispatcher/application shutdown; " +
            "the last processed bisect probe identifies the trigger boundary; " +
            "no shutdown event and no processed probes means a hard UI-thread block; " +
            "before viewport attach without after viewport attach means Attach did not return.");
    }

    internal static string FormatThreadStatus() =>
        $"thread={Environment.CurrentManagedThreadId} " +
        $"dispatcher-access={Dispatcher.UIThread.CheckAccess()} " +
        $"dispatcher-shutdown-started-observed={Volatile.Read(ref s_shutdownStarted) != 0} " +
        "dispatcher-has-shutdown-started-api=missing " +
        FormatLifetimeState();

    private static string FormatLifetimeState()
    {
        if (Application.Current?.ApplicationLifetime is not
            IClassicDesktopStyleApplicationLifetime desktop)
        {
            return "desktop-lifetime=unavailable";
        }

        try
        {
            Window? mainWindow = desktop.MainWindow;
            bool mainWindowListed = false;
            int windowCount = 0;
            foreach (Window window in desktop.Windows)
            {
                windowCount++;
                mainWindowListed |= ReferenceEquals(window, mainWindow);
            }

            string mainWindowState = mainWindow is null
                ? "null"
                : mainWindowListed
                    ? "open"
                    : "not-listed";
            return
                $"desktop-shutdown-mode={desktop.ShutdownMode} " +
                $"desktop-window-count={windowCount} " +
                $"desktop-main-window={mainWindowState}";
        }
        catch (InvalidOperationException exception)
        {
            return $"desktop-lifetime=unavailable:{exception.GetType().Name}";
        }
    }

    private static string GetAvaloniaVersion()
    {
        Version? version = typeof(Application).Assembly.GetName().Version;
        return version?.ToString() ?? "unknown";
    }
}
