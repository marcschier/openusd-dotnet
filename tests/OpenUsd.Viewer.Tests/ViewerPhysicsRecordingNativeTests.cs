// Copyright (c) marcschier. Licensed under the MIT License.

using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using Avalonia.Threading;

namespace OpenUsd.Viewer.Tests;

[NotInParallel]
public sealed class ViewerPhysicsRecordingNativeTests
{
    [Test]
    public async Task TheOperatorRecordsExactPhysicsFramesAndOpensTheirVerifiedGallery()
    {
        if (!OperatingSystem.IsWindows() ||
            Environment.GetEnvironmentVariable("OPENUSD_VIEWER_PHYSICS_RECORDING_SMOKE") != "1")
        {
            Skip.Test("Run this class alone with OPENUSD_VIEWER_PHYSICS_RECORDING_SMOKE=1 and matched physics/render runtimes.");
        }
        string? evidence = Environment.GetEnvironmentVariable("OPENUSD_VIEWER_PHYSICS_RECORDING_EVIDENCE_ROOT") ??
            Environment.GetEnvironmentVariable("OPENUSD_VIEWER_SEQUENCE_EVIDENCE_ROOT");
        if (evidence is not null && (!Path.IsPathFullyQualified(evidence) || !Directory.Exists(evidence)))
        {
            throw new ArgumentException("Physics recording evidence requires an existing absolute output directory.");
        }
        string root = Path.Combine(evidence ?? Path.GetTempPath(), $"viewer-physics-recording-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        using var lifetime = new CancellationTokenSource(TimeSpan.FromSeconds(100));
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                Program.BuildAvaloniaApp().SetupWithoutStarting();
                _ = Dispatcher.UIThread.InvokeAsync(async () =>
                {
                    try
                    {
                        await ViewerRenderSequenceNativeTests.ExercisePhysicsRecordingAtCoordinatorAsync(root);
                        await ViewerRenderSequenceNativeTests.ExercisePhysicsRecordingUiAsync(root);
                        await VerifyPhysicsRuntimeAsync(root);
                        completion.TrySetResult();
                    }
                    catch (Exception exception)
                    {
                        completion.TrySetException(exception);
                    }
                    finally
                    {
                        ViewerStartupOptions.Initialize([]);
                        lifetime.Cancel();
                    }
                });
                Dispatcher.UIThread.MainLoop(lifetime.Token);
            }
            catch (Exception exception)
            {
                completion.TrySetException(exception);
            }
        })
        { IsBackground = true, Name = "Viewer physics recording workflow" };
        if (OperatingSystem.IsWindows())
        {
            thread.SetApartmentState(ApartmentState.STA);
        }
        thread.Start();
        bool stopped;
        try
        {
            await completion.Task.WaitAsync(TimeSpan.FromSeconds(110));
        }
        finally
        {
            lifetime.Cancel();
            stopped = thread.Join(TimeSpan.FromSeconds(5));
            if (stopped && evidence is null)
            {
                Directory.Delete(root, recursive: true);
            }
        }
        await Assert.That(stopped).IsTrue();
    }

    private static async Task VerifyPhysicsRuntimeAsync(string root)
    {
        string expectedRoot = Environment.GetEnvironmentVariable("OPENUSD_VIEWER_PHYSICS_RUNTIME_ROOT") ??
            throw new InvalidOperationException("Set OPENUSD_VIEWER_PHYSICS_RUNTIME_ROOT to the matched physics shim prefix.");
        string expectedPath = Path.Combine(expectedRoot, "bin", "openusd_physx.dll");
        using Process process = Process.GetCurrentProcess();
        ProcessModule module = process.Modules.Cast<ProcessModule>().Single(item =>
            string.Equals(item.ModuleName, "openusd_physx.dll", StringComparison.OrdinalIgnoreCase));
        await using FileStream expected = File.OpenRead(expectedPath);
        await using FileStream actual = File.OpenRead(module.FileName);
        byte[] expectedHash = await SHA256.HashDataAsync(expected);
        byte[] actualHash = await SHA256.HashDataAsync(actual);
        await Assert.That(actualHash.SequenceEqual(expectedHash)).IsTrue()
            .Because("the loaded solver must match the runtime recorded by the workflow");
        await File.WriteAllTextAsync(Path.Combine(root, "physics-runtime.json"), JsonSerializer.Serialize(new
        {
            expectedPath,
            loadedPath = module.FileName,
            sha256 = Convert.ToHexString(actualHash)
        }));
    }
}
