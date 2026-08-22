// Copyright (c) marcschier. Licensed under the MIT License.

using System.Diagnostics;

namespace OpenUsd.Mcp.Tests;

public sealed class ViewerChildLauncherTests
{
    [Test]
    public async Task UsesArgumentListAndRedirectedMcpStreams()
    {
        using var files = new ViewerLaunchTestFiles();
        var request = new ViewerLaunchRequest(
            files.StagePath,
            files.PluginPath,
            "Storm; --stage injected.usda",
            "/World/Camera; --renderer injected");

        ProcessStartInfo startInfo = ViewerChildLauncher.CreateStartInfo(
            files.ExecutablePath,
            request);

        await Assert.That(startInfo.UseShellExecute).IsFalse();
        await Assert.That(startInfo.RedirectStandardInput).IsTrue();
        await Assert.That(startInfo.RedirectStandardOutput).IsTrue();
        await Assert.That(startInfo.RedirectStandardError).IsTrue();
        await Assert.That(startInfo.Arguments).IsEmpty();
        await Assert.That(startInfo.ArgumentList).IsEquivalentTo(
        [
            "--stage",
            Path.GetFullPath(files.StagePath),
            "--plugins",
            Path.GetFullPath(files.PluginPath),
            "--renderer",
            request.Renderer,
            "--camera",
            request.CameraPath!,
        ]);
        await Assert.That(startInfo.ArgumentList.Count).IsEqualTo(8);
    }

    [Test]
    public async Task LaunchReturnsStarterMetadataWithoutWaiting()
    {
        using var files = new ViewerLaunchTestFiles();
        var starter = new RecordingViewerProcessStarter();
        var launcher = new ViewerChildLauncher(
            new ViewerChildLauncherOptions(files.Root, files.ExecutablePath),
            starter);

        ViewerProcessMetadata metadata = launcher.Launch(
            new ViewerLaunchRequest(
                files.StagePath,
                files.PluginPath,
                "Storm"));

        await Assert.That(starter.StartCount).IsEqualTo(1);
        await Assert.That(metadata.ProcessId).IsEqualTo(1234);
        await Assert.That(metadata.ExecutablePath).IsEqualTo(files.ExecutablePath);
        await Assert.That(metadata.Arguments).IsEquivalentTo(
            starter.StartInfo!.ArgumentList);
    }

    [Test]
    public async Task ExecutableMustBeContainedNamedAndPresent()
    {
        using var files = new ViewerLaunchTestFiles();
        string outside = Path.Combine(
            Path.GetDirectoryName(files.Root)!,
            ExecutableName());
        File.WriteAllText(outside, "outside");
        try
        {
            await Assert.That(
                    () => new ViewerChildLauncher(
                        new ViewerChildLauncherOptions(files.Root, outside),
                        new RecordingViewerProcessStarter()))
                .Throws<ArgumentException>();
            string wrongName = Path.Combine(files.Root, "viewer.exe");
            File.WriteAllText(wrongName, "wrong");
            await Assert.That(
                    () => new ViewerChildLauncher(
                        new ViewerChildLauncherOptions(files.Root, wrongName),
                        new RecordingViewerProcessStarter()))
                .Throws<ArgumentException>();
            File.Delete(files.ExecutablePath);
            await Assert.That(
                    () => new ViewerChildLauncher(
                        new ViewerChildLauncherOptions(files.Root, files.ExecutablePath),
                        new RecordingViewerProcessStarter()))
                .Throws<FileNotFoundException>();
        }
        finally
        {
            File.Delete(outside);
        }
    }

    [Test]
    public async Task ControlCharactersAreRejectedBeforeStartingViewer()
    {
        using var files = new ViewerLaunchTestFiles();
        var starter = new RecordingViewerProcessStarter();
        var launcher = new ViewerChildLauncher(
            new ViewerChildLauncherOptions(files.Root, files.ExecutablePath),
            starter);

        await Assert.That(
                () => launcher.Launch(
                    new ViewerLaunchRequest(
                        files.StagePath,
                        files.PluginPath,
                        "Storm\n--stage")))
            .Throws<ArgumentException>();

        await Assert.That(starter.StartCount).IsEqualTo(0);
    }

    private static string ExecutableName() =>
        OperatingSystem.IsWindows()
            ? "OpenUsd.Viewer.App.exe"
            : "OpenUsd.Viewer.App";

    private sealed class ViewerLaunchTestFiles : IDisposable
    {
        internal ViewerLaunchTestFiles()
        {
            Root = Path.Combine(
                AppContext.BaseDirectory,
                "viewer-launch-tests",
                Guid.NewGuid().ToString("N"));
            PluginPath = Path.Combine(Root, "plugins");
            Directory.CreateDirectory(PluginPath);
            ExecutablePath = Path.Combine(Root, ExecutableName());
            StagePath = Path.Combine(Root, "final stage.usda");
            File.WriteAllText(ExecutablePath, "viewer");
            File.WriteAllText(StagePath, "#usda 1.0");
        }

        internal string ExecutablePath { get; }

        internal string PluginPath { get; }

        internal string Root { get; }

        internal string StagePath { get; }

        public void Dispose()
        {
            if (Directory.Exists(Root))
            {
                Directory.Delete(Root, recursive: true);
            }
        }
    }
}

internal sealed class RecordingViewerProcessStarter : IViewerProcessStarter
{
    internal int StartCount { get; private set; }

    internal ProcessStartInfo? StartInfo { get; private set; }

    public ViewerProcessMetadata Start(ProcessStartInfo startInfo)
    {
        StartCount++;
        StartInfo = startInfo;
        return new ViewerProcessMetadata(
            1234,
            DateTimeOffset.UnixEpoch,
            startInfo.FileName,
            Array.AsReadOnly(startInfo.ArgumentList.ToArray()));
    }
}
