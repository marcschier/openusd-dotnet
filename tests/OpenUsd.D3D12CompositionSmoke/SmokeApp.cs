// Copyright (c) marcschier. Licensed under the MIT License.

using System.Runtime.Versioning;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;

namespace OpenUsd.D3D12CompositionSmoke;

internal sealed class SmokeApp : Application
{
    internal static int ExitCode { get; private set; } = 1;

    internal static void Complete(int exitCode)
    {
        ExitCode = exitCode;
    }

    [SupportedOSPlatform("windows")]
    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop ||
            Program.Context is not SmokeContext context)
        {
            throw new InvalidOperationException("The D3D12 smoke requires a desktop lifetime.");
        }

        desktop.MainWindow = new SmokeWindow(context, desktop);
        base.OnFrameworkInitializationCompleted();
    }
}
