// Copyright (c) marcschier. Licensed under the MIT License.

using Avalonia;

namespace OpenUsd.Viewer;

/// <summary>
/// Public launch surface for the viewer desktop shell. Application hosts call
/// <see cref="Run(string[])"/> from their own <c>Main</c>; the Avalonia designer and
/// tooling use <see cref="BuildAvaloniaApp"/>.
/// </summary>
public static class ViewerEntryPoint
{
    /// <summary>
    /// Runs the viewer shell on the calling thread until the main window closes. The
    /// caller must own the process main thread, and on Windows that thread must be
    /// marked <c>[STAThread]</c>.
    /// </summary>
    /// <param name="args">
    /// Command-line arguments in the shell's own syntax (<c>--stage</c>,
    /// <c>--plugins</c>, <c>--renderer</c>, ...).
    /// </param>
    /// <exception cref="System.ArgumentNullException"><paramref name="args"/> is <c>null</c>.</exception>
    public static void Run(string[] args)
    {
        System.ArgumentNullException.ThrowIfNull(args);
        Program.Main(args);
    }

    /// <summary>
    /// Runs the viewer shell on the calling thread against a programmatic configuration,
    /// so a host can author into <see cref="ViewerStageSession.Scheduler"/> while the
    /// stage renders. The caller must own the process main thread, and on Windows that
    /// thread must be marked <c>[STAThread]</c>.
    /// </summary>
    /// <param name="options">The stage, plugin tree, renderer, and stage-ready callback.</param>
    /// <exception cref="System.ArgumentNullException"><paramref name="options"/> is <c>null</c>.</exception>
    public static void Run(ViewerHostOptions options)
    {
        System.ArgumentNullException.ThrowIfNull(options);
        Program.RunHosted(options);
    }

    /// <summary>
    /// Builds the configured Avalonia application without starting it. Exposed for the
    /// Avalonia designer and for hosts that drive their own application lifetime.
    /// </summary>
    public static AppBuilder BuildAvaloniaApp() => Program.BuildAvaloniaApp();
}
