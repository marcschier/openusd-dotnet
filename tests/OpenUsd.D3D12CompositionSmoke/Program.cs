// Copyright (c) marcschier. Licensed under the MIT License.

using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;

namespace OpenUsd.D3D12CompositionSmoke;

internal static class Program
{
    internal static SmokeContext? Context { get; private set; }

    [STAThread]
    public static int Main(string[] args)
    {
        SmokeStatus.Initialize();
        if (!OperatingSystem.IsWindows())
        {
            SmokeStatus.Write("D3D12_SMOKE_FAIL reason=Windows_required");
            return 1;
        }

        try
        {
            string pluginPath = GetRequiredPath("OPENUSD_PLUGIN_PATH");
            string stagePath = GetRequiredPath("OPENUSD_STAGE_PATH");
            Context = SmokeContext.CreateAsync(pluginPath, stagePath)
                .GetAwaiter()
                .GetResult();
            SmokeStatus.Write("D3D12_SMOKE_STATUS startup=resources_ready");
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(
                args,
                ShutdownMode.OnExplicitShutdown);
            return SmokeApp.ExitCode;
        }
        catch (Exception exception)
        {
            SmokeStatus.Write($"D3D12_SMOKE_FAIL reason={SmokeStatus.Value(exception.Message)}");
            Trace.TraceError(exception.ToString());
            Context?.DisposeAsync().AsTask().GetAwaiter().GetResult();
            Context = null;
            return 1;
        }
    }

    private static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<SmokeApp>()
            .UsePlatformDetect()
            .With(new Win32PlatformOptions
            {
                RenderingMode = [Win32RenderingMode.AngleEgl],
                CompositionMode = [Win32CompositionMode.WinUIComposition]
            })
            .LogToTrace();

    private static string GetRequiredPath(string name)
    {
        string? value = Environment.GetEnvironmentVariable(name);
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException($"{name} is required.");
        }
        string path = Path.GetFullPath(value);
        if (!Directory.Exists(path) && !File.Exists(path))
        {
            throw new FileNotFoundException($"{name} does not exist.", path);
        }
        return path;
    }
}
