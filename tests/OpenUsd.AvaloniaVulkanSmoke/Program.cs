// Copyright (c) marcschier. Licensed under the MIT License.

using Avalonia;
using Avalonia.Controls;

namespace OpenUsd.AvaloniaVulkanSmoke;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        SmokeOptions.Initialize(args);
        AppBuilder builder = AppBuilder.Configure<SmokeApplication>()
            .UsePlatformDetect();
        if (SmokeOptions.Platform == "windows")
        {
            builder = builder.With(new Win32PlatformOptions
            {
                RenderingMode = [Win32RenderingMode.AngleEgl],
                CompositionMode = [Win32CompositionMode.WinUIComposition]
            });
        }
        else if (SmokeOptions.Platform == "wayland")
        {
            builder = builder
                .UseWayland()
                .With(new WaylandPlatformOptions
                {
                    UseDmabufSwapchain = false
                });
        }
        else if (SmokeOptions.Platform == "x11")
        {
            builder = builder.With(new X11PlatformOptions
            {
                RenderingMode = [X11RenderingMode.Egl]
            });
        }

        builder.LogToTrace().StartWithClassicDesktopLifetime(
            args,
            ShutdownMode.OnExplicitShutdown);
        return SmokeApplication.ExitCode;
    }
}
