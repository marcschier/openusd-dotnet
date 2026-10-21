// Copyright (c) marcschier. Licensed under the MIT License.

using System.Diagnostics;

namespace OpenUsd.Mcp.Tests;

public sealed class ViewerChildLauncherTests
{
    [Test]
    [NotInParallel]
    public async Task ChildGpuUploadPolicyUsesOnlyTheEffectiveChoiceAndCannotChangeBackend()
    {
        using var files = new ViewerLaunchTestFiles();
        const string variable = "OPENUSD_D3D12_GPU_UPLOAD_HEAPS";
        string? previous = Environment.GetEnvironmentVariable(variable);
        try
        {
            Environment.SetEnvironmentVariable(variable, "invalid");
            var request = new ViewerLaunchRequest(files.StagePath, files.PluginPath, "D3D12");
            ProcessStartInfo disabled = ViewerChildLauncher.CreateStartInfo(files.ExecutablePath, request);
            await Assert.That(disabled.Environment.ContainsKey(variable)).IsFalse();
            await Assert.That(() => ViewerChildLauncher.CreateStartInfo(
                files.ExecutablePath, request, gpuUploadHeaps: true)).Throws<ArgumentException>();
            var starter = new RecordingViewerProcessStarter();
            var launcher = new ViewerChildLauncher(
                new ViewerChildLauncherOptions(files.Root, files.ExecutablePath, GpuBufferBudget: new(4096))
                {
                    D3D12GpuUploadHeaps = true
                }, starter);
            _ = launcher.Launch(request);
            await Assert.That(starter.StartInfo!.Environment[variable]).IsEqualTo("true");
            await Assert.That(starter.StartInfo.Environment["OPENUSD_SILK_GPU_BUFFER_BYTES"]).IsEqualTo("4096");
            foreach (string renderer in new[] { "Storm", "Vulkan", "Metal" })
            {
                await Assert.That(() => launcher.Launch(request with { Renderer = renderer }))
                    .Throws<ArgumentException>();
            }
            await Assert.That(starter.StartCount).IsEqualTo(1);
            await Assert.That(Environment.GetEnvironmentVariable(variable)).IsEqualTo("invalid");
        }
        finally
        {
            Environment.SetEnvironmentVariable(variable, previous);
        }
    }

    [Test]
    [NotInParallel]
    public async Task ChildStreamingUsesOnlyTheEffectiveChoiceAndRequiresAdmission()
    {
        using var files = new ViewerLaunchTestFiles();
        const string variable = "OPENUSD_SILK_STREAM_MATERIAL_TEXTURES";
        string? previous = Environment.GetEnvironmentVariable(variable);
        try
        {
            Environment.SetEnvironmentVariable(variable, "invalid");
            var request = new ViewerLaunchRequest(files.StagePath, files.PluginPath, "D3D12");
            ProcessStartInfo disabled = ViewerChildLauncher.CreateStartInfo(files.ExecutablePath, request);
            await Assert.That(disabled.Environment.ContainsKey(variable)).IsFalse();
            await Assert.That(() => ViewerChildLauncher.CreateStartInfo(
                files.ExecutablePath, request, streamMaterialTextures: true)).Throws<ArgumentException>();
            var starter = new RecordingViewerProcessStarter();
            var launcher = new ViewerChildLauncher(
                new ViewerChildLauncherOptions(files.Root, files.ExecutablePath, GpuBufferBudget: new(4096))
                {
                    StreamMaterialTextures = true
                }, starter);
            _ = launcher.Launch(request);
            await Assert.That(starter.StartInfo!.Environment[variable]).IsEqualTo("true");
            await Assert.That(starter.StartInfo.Environment["OPENUSD_SILK_GPU_BUFFER_BYTES"]).IsEqualTo("4096");
            await Assert.That(() => launcher.Launch(request with { Renderer = "Storm" })).Throws<NotSupportedException>();
            await Assert.That(starter.StartCount).IsEqualTo(1);
            await Assert.That(Environment.GetEnvironmentVariable(variable)).IsEqualTo("invalid");
        }
        finally
        {
            Environment.SetEnvironmentVariable(variable, previous);
        }
    }

    [Test]
    [NotInParallel]
    public async Task ChildStorageUsesOnlyTheEffectiveSessionPolicy()
    {
        using var files = new ViewerLaunchTestFiles();
        string[] variables = ["OPENUSD_SILK_STORAGE_LIVE_BYTES", "OPENUSD_SILK_STORAGE_UPDATE_BYTES",
            "OPENUSD_SILK_STORAGE_COMMAND_BYTES", "OPENUSD_SILK_STORAGE_TRANSFER_BYTES"];
        string?[] before = variables.Select(Environment.GetEnvironmentVariable).ToArray();
        try
        {
            foreach (string variable in variables)
            {
                Environment.SetEnvironmentVariable(variable, "invalid");
            }
            var limits = new OpenUsd.Rendering.Silk.SilkPreparationLimits(1000000, 65536,
                new(6000000000, 5000000000, 32768, 4096));
            var request = new ViewerLaunchRequest(files.StagePath, files.PluginPath, "D3D12");
            ProcessStartInfo start = ViewerChildLauncher.CreateStartInfo(files.ExecutablePath, request, limits);
            await Assert.That(start.Environment[variables[0]]).IsEqualTo("6000000000");
            await Assert.That(start.Environment[variables[1]]).IsEqualTo("5000000000");
            await Assert.That(start.Environment[variables[2]]).IsEqualTo("32768");
            await Assert.That(start.Environment[variables[3]]).IsEqualTo("4096");
            ProcessStartInfo disabled = ViewerChildLauncher.CreateStartInfo(files.ExecutablePath, request,
                new OpenUsd.Rendering.Silk.SilkPreparationLimits(1000000, 65536));
            foreach (string variable in variables)
            {
                await Assert.That(disabled.Environment.ContainsKey(variable)).IsFalse();
                await Assert.That(Environment.GetEnvironmentVariable(variable)).IsEqualTo("invalid");
            }
        }
        finally
        {
            for (int index = 0; index < variables.Length; index++)
            {
                Environment.SetEnvironmentVariable(variables[index], before[index]);
            }
        }
    }

    [Test]
    [NotInParallel]
    public async Task ChildTextureLimitsUseEffectiveCeilingsWithoutBorrowingParentCreditOrAmbientSettings()
    {
        using var files = new ViewerLaunchTestFiles();
        string[] variables =
            ["OPENUSD_SILK_CPU_TEXTURE_BYTES", "OPENUSD_SILK_GPU_TEXTURE_BYTES", "OPENUSD_SILK_GPU_STAGING_BYTES"];
        string?[] previous = variables.Select(Environment.GetEnvironmentVariable).ToArray();
        try
        {
            foreach (string variable in variables)
            {
                Environment.SetEnvironmentVariable(variable, "invalid");
            }
            var budgets = new OpenUsd.Rendering.Silk.SilkTexturePreparationBudgets(new(123), new(456), new(789));
            var starter = new RecordingViewerProcessStarter();
            var options = new ViewerChildLauncherOptions(files.Root, files.ExecutablePath)
            {
                TexturePreparationBudgets = budgets
            };
            var launcher = new ViewerChildLauncher(options, starter);
            var request = new ViewerLaunchRequest(files.StagePath, files.PluginPath, "D3D12");
            _ = launcher.Launch(request);
            await Assert.That(starter.StartInfo!.Environment[variables[0]]).IsEqualTo("123");
            await Assert.That(starter.StartInfo.Environment[variables[1]]).IsEqualTo("456");
            await Assert.That(starter.StartInfo.Environment[variables[2]]).IsEqualTo("789");
            await Assert.That(budgets.CpuTextureBudget!.Usage.ReservedBytes).IsEqualTo(0ul);
            await Assert.That(budgets.GpuTextureBudget!.Usage.ReservedBytes).IsEqualTo(0ul);
            await Assert.That(budgets.GpuStagingBudget!.Usage.ReservedBytes).IsEqualTo(0ul);
            await Assert.That(() => launcher.Launch(request with { Renderer = "Storm" }))
                .Throws<NotSupportedException>();
            await Assert.That(starter.StartCount).IsEqualTo(1);
            ProcessStartInfo partial = ViewerChildLauncher.CreateStartInfo(files.ExecutablePath, request,
                texturePreparationBudgets: new(null, null, budgets.GpuStagingBudget));
            await Assert.That(partial.Environment.ContainsKey(variables[0])).IsFalse();
            await Assert.That(partial.Environment.ContainsKey(variables[1])).IsFalse();
            await Assert.That(partial.Environment[variables[2]]).IsEqualTo("789");
            ProcessStartInfo unconfigured = ViewerChildLauncher.CreateStartInfo(files.ExecutablePath, request);
            foreach (string variable in variables)
            {
                await Assert.That(unconfigured.Environment.ContainsKey(variable)).IsFalse();
                await Assert.That(Environment.GetEnvironmentVariable(variable)).IsEqualTo("invalid");
            }
        }
        finally
        {
            for (int index = 0; index < variables.Length; index++)
            {
                Environment.SetEnvironmentVariable(variables[index], previous[index]);
            }
        }
    }

    [Test]
    [NotInParallel]
    public async Task ChildInheritsTheEffectivePolicyRatherThanAmbientLimitsAndRefusesStorm()
    {
        using var files = new ViewerLaunchTestFiles();
        const string meshVariable = "OPENUSD_SILK_MESH_RESERVATION_BYTES";
        const string pageVariable = "OPENUSD_SILK_MAX_PAGE_BYTES";
        const string bufferVariable = "OPENUSD_SILK_GPU_BUFFER_BYTES";
        string? meshBefore = Environment.GetEnvironmentVariable(meshVariable);
        string? pageBefore = Environment.GetEnvironmentVariable(pageVariable);
        string? bufferBefore = Environment.GetEnvironmentVariable(bufferVariable);
        try
        {
            Environment.SetEnvironmentVariable(meshVariable, "wrong");
            Environment.SetEnvironmentVariable(pageVariable, "wrong");
            Environment.SetEnvironmentVariable(bufferVariable, "wrong");
            var starter = new RecordingViewerProcessStarter();
            var options = new ViewerChildLauncherOptions(files.Root, files.ExecutablePath,
                new OpenUsd.Rendering.Silk.SilkPreparationLimits(1234, 5678),
                new OpenUsd.Rendering.Silk.SilkGpuBufferBudget(9876));
            var launcher = new ViewerChildLauncher(options, starter);
            var request = new ViewerLaunchRequest(files.StagePath, files.PluginPath, "D3D12");
            _ = launcher.Launch(request);
            await Assert.That(starter.StartInfo!.Environment[meshVariable]).IsEqualTo("1234");
            await Assert.That(starter.StartInfo.Environment[pageVariable]).IsEqualTo("5678");
            await Assert.That(starter.StartInfo.Environment[bufferVariable]).IsEqualTo("9876");
            await Assert.That(() => launcher.Launch(request with { Renderer = "Storm" }))
                .Throws<NotSupportedException>();
            await Assert.That(starter.StartCount).IsEqualTo(1);
            ProcessStartInfo unconfigured = ViewerChildLauncher.CreateStartInfo(files.ExecutablePath, request);
            await Assert.That(unconfigured.Environment.ContainsKey(meshVariable)).IsFalse();
            await Assert.That(unconfigured.Environment.ContainsKey(pageVariable)).IsFalse();
            await Assert.That(unconfigured.Environment.ContainsKey(bufferVariable)).IsFalse();
            await Assert.That(Environment.GetEnvironmentVariable(meshVariable)).IsEqualTo("wrong");
            await Assert.That(Environment.GetEnvironmentVariable(pageVariable)).IsEqualTo("wrong");
        }
        finally
        {
            Environment.SetEnvironmentVariable(meshVariable, meshBefore);
            Environment.SetEnvironmentVariable(pageVariable, pageBefore);
            Environment.SetEnvironmentVariable(bufferVariable, bufferBefore);
        }
    }

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
