// Copyright (c) marcschier. Licensed under the MIT License.

using System.Diagnostics;
using Avalonia;
using Avalonia.OpenGL;

namespace OpenUsd.Viewer;

internal static class Program
{
    private static readonly string? PlatformOverride =
        Environment.GetEnvironmentVariable("OPENUSD_VIEWER_PLATFORM");

    [STAThread]
    public static void Main(string[] args)
    {
        LinuxX11Threading.Initialize();
        ViewerStartupOptions.Initialize(args);
        RunCore(args);
    }

    /// <summary>
    /// Runs the shell for an embedding host that configured the viewer programmatically
    /// instead of through the command line.
    /// </summary>
    internal static void RunHosted(ViewerHostOptions options)
    {
        LinuxX11Threading.Initialize();
        ViewerStartupOptions.Initialize(options);
        RunCore([]);
    }

    private static void RunCore(string[] args)
    {
        ValidatePlatformOverride();
        ViewerPlatformDecision decision = GetPlatformDecision();
        if (decision.FailureReason is not null)
        {
            throw new PlatformNotSupportedException(decision.FailureReason);
        }
        ViewerStartupOptions.ConfigurePlatform(decision);
        if (!string.IsNullOrEmpty(PlatformOverride))
        {
            ViewerStartupOptions.WriteStatus($"Platform: {PlatformOverride}");
        }
        ViewerStartupOptions.WriteStatus(
            $"Platform backend: {decision.BackendName}; selected");
        if (OperatingSystem.IsWindows())
        {
            ViewerStartupOptions.WriteStatus(
                $"Windows shell mode configured: {GetConfiguredWindowsShellMode()}");
        }
        if (decision.UsesXWaylandFallback)
        {
            ViewerStartupOptions.WriteStatus(
                "Storm Wayland fallback: OpenUSD v26.05 Linux Glf/Garch is " +
                $"GLX-only; DISPLAY={decision.Display}; " +
                $"WAYLAND_DISPLAY={decision.WaylandDisplay}");
        }
        if (!string.IsNullOrWhiteSpace(ViewerStartupOptions.LogFile))
        {
            Trace.Listeners.Add(new TextWriterTraceListener(ViewerStartupOptions.LogFile));
            Trace.AutoFlush = true;
        }
        BuildAvaloniaApp(decision).StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp()
    {
        LinuxX11Threading.Initialize();
        ViewerPlatformDecision decision = GetPlatformDecision();
        if (decision.FailureReason is not null)
        {
            throw new PlatformNotSupportedException(decision.FailureReason);
        }
        ViewerStartupOptions.ConfigurePlatform(decision);
        return BuildAvaloniaApp(decision);
    }

    private static AppBuilder BuildAvaloniaApp(ViewerPlatformDecision decision)
    {
        var builder = AppBuilder.Configure<App>().UsePlatformDetect();
        if (decision.UseWayland)
        {
            builder = builder.UseWayland();
        }
        else if (OperatingSystem.IsLinux())
        {
            builder = builder.UseX11();
        }
        return builder
            .With(new Win32PlatformOptions
            {
                RenderingMode =
                    GetWindowsRenderingModes(
                        ViewerStartupOptions.SharedStageSoak,
                        ViewerStartupOptions.WindowsRenderingOverride ?? PlatformOverride),
                CompositionMode =
                [
                    Win32CompositionMode.WinUIComposition,
                    Win32CompositionMode.RedirectionSurface
                ],
                WglProfiles =
                [
                    new GlVersion(GlProfileType.OpenGL, 4, 6, isCompatibilityProfile: true),
                    new GlVersion(GlProfileType.OpenGL, 4, 5, isCompatibilityProfile: true)
                ]
            })
            .With(CreateX11PlatformOptions(decision.UsesXWaylandFallback))
            .With(new WaylandPlatformOptions
            {
                UseDmabufSwapchain = PlatformOverride == "linux-wayland" ? false : null,
                GlProfiles =
                [
                    new GlVersion(GlProfileType.OpenGL, 4, 5)
                ]
            })
            .With(new AvaloniaNativePlatformOptions
            {
                RenderingMode = GetMacOSRenderingModes()
            })
            .WithInterFont()
            .LogToTrace();
    }

    internal static Win32RenderingMode[] GetWindowsRenderingModes(
        bool sharedStageSoak,
        string? platformOverride) =>
        sharedStageSoak ||
        string.Equals(platformOverride, "windows-wgl", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(platformOverride, "wgl", StringComparison.OrdinalIgnoreCase)
            ? [Win32RenderingMode.Wgl]
            : [Win32RenderingMode.AngleEgl];

    internal static string GetConfiguredWindowsShellMode() =>
        GetWindowsRenderingModes(
            ViewerStartupOptions.SharedStageSoak,
            ViewerStartupOptions.WindowsRenderingOverride ?? PlatformOverride)[0].ToString();

    internal static string GetConfiguredShellMode()
    {
        if (OperatingSystem.IsLinux())
        {
            return string.Equals(
                Environment.GetEnvironmentVariable("OPENUSD_VIEWER_PLATFORM"),
                "linux-wayland",
                StringComparison.OrdinalIgnoreCase)
                ? "X11 / compositor-managed XWayland"
                : "X11";
        }
        return OperatingSystem.IsMacOS()
            ? "Avalonia Native / Metal"
            : GetConfiguredWindowsShellMode();
    }

    internal static AvaloniaNativeRenderingMode[] GetMacOSRenderingModes() =>
        [AvaloniaNativeRenderingMode.Metal];

    private static ViewerPlatformDecision GetPlatformDecision()
    {
        ViewerPlatformDecision decision = ViewerPlatformSelection.Decide(
            PlatformOverride,
            OperatingSystem.IsLinux(),
            ViewerStartupOptions.IsStormRenderer,
            ViewerPlatformSelection.HasNativeWaylandStormSupport(
                Environment.GetEnvironmentVariable),
            Environment.GetEnvironmentVariable);
        if (OperatingSystem.IsWindows())
        {
            return decision with { BackendName = "Win32" };
        }
        if (OperatingSystem.IsMacOS())
        {
            return decision with { BackendName = "macOS" };
        }
        return decision;
    }

    private static X11PlatformOptions CreateX11PlatformOptions(bool xWaylandFallback)
    {
        bool forced = OperatingSystem.IsLinux();
        var options = new X11PlatformOptions
        {
            RenderingMode =
                forced
                    ? [X11RenderingMode.Glx]
                    :
                    [
                        X11RenderingMode.Glx,
                        X11RenderingMode.Egl,
                        X11RenderingMode.Software
                    ],
            GlProfiles =
                [
                    new GlVersion(
                        GlProfileType.OpenGL,
                        4,
                        6,
                        isCompatibilityProfile: true),
                    new GlVersion(
                        GlProfileType.OpenGL,
                        4,
                        5,
                        isCompatibilityProfile: true)
                ]
        };
        if (forced)
        {
            options.GlxRendererBlacklist = [];
        }
        return options;
    }

    private static void ValidatePlatformOverride()
    {
        bool valid = PlatformOverride switch
        {
            null or "" => true,
            "windows-wgl" => OperatingSystem.IsWindows(),
            "linux-x11" or "linux-wayland" => OperatingSystem.IsLinux(),
            "macos-arm64" => OperatingSystem.IsMacOS() &&
                System.Runtime.InteropServices.RuntimeInformation.OSArchitecture ==
                    System.Runtime.InteropServices.Architecture.Arm64,
            _ => false
        };
        if (!valid)
        {
            throw new PlatformNotSupportedException(
                $"OPENUSD_VIEWER_PLATFORM '{PlatformOverride}' is invalid on this host.");
        }
    }
}
