// Copyright (c) marcschier. Licensed under the MIT License.

using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;

namespace OpenUsd.Viewer;

public sealed class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            AvaloniaDispatcherShutdownDiagnostics.EnsureSubscribed(
                "framework initialization before main window creation");
            desktop.MainWindow = new MainWindow();
            ViewerStartupOptions.WriteStatus(
                $"Platform backend: {ViewerStartupOptions.PlatformDecision.BackendName}; " +
                "initialized");
        }

        base.OnFrameworkInitializationCompleted();
        ViewerStartupOptions.WriteStatus(
            $"Platform backend: {ViewerStartupOptions.PlatformDecision.BackendName}; " +
            "framework initialized");
    }
}
