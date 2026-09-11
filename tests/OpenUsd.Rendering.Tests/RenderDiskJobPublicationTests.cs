// Copyright (c) marcschier. Licensed under the MIT License.

using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace OpenUsd.Rendering.Tests;

public sealed partial class RenderDiskJobPublicationTests
{
    [Test]
    [Arguments("released")]
    [Arguments("canceled")]
    [Arguments("held")]
    [Arguments("collision")]
    public async Task WindowsSharingConflictsPreservePublicationAndCancellationRules(string outcome)
    {
        if (!OperatingSystem.IsWindows())
        {
            Skip.Test("This case exercises Windows directory sharing semantics.");
            return;
        }
        string root = Directory.CreateTempSubdirectory("openusd-publication-sharing-").FullName;
        using var cancellation = new CancellationTokenSource();
        var clock = new ManualRetryTimeProvider();
        SafeFileHandle? heldLease = null;
        var source = new FrameSource(() =>
        {
            string staging = Directory.GetDirectories(root).Single();
            SafeFileHandle handle = OpenDirectory(staging, 0x80000000, 3, 0, 3, 0x02000000, 0);
            if (handle.IsInvalid)
            {
                int error = Marshal.GetLastPInvokeError();
                handle.Dispose();
                throw new Win32Exception(error, "Could not acquire the controlled directory-sharing lease.");
            }
            heldLease = handle;
        });
        StageRenderState state = StageRenderState.Create(new StageIdentity("source.usda"))
            .WithViewport(new ViewportDimensions(1, 1));
        var request = new RenderDiskJobRequest(Path.Combine(root, "complete"), [state]);
        Task<RenderDiskJobResult> operation = Task.Factory.StartNew(
            () => RenderDiskJob.Execute(request, source, clock, cancellation.Token),
            CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
        try
        {
            TimeSpan firstDelay = await clock.NextDelayAsync();
            await Assert.That(firstDelay).IsEqualTo(TimeSpan.FromMilliseconds(20));
            clock.Advance(firstDelay - TimeSpan.FromMilliseconds(1));
            await Assert.That(operation.IsCompleted).IsFalse();
            await Assert.That(Directory.Exists(request.OutputDirectory)).IsFalse();
            if (outcome == "canceled")
            {
                cancellation.Cancel();
                TimeSpan cleanupDelay = await clock.NextDelayAsync();
                await Assert.That(cleanupDelay).IsEqualTo(TimeSpan.FromMilliseconds(20));
                await Assert.That(operation.IsCompleted).IsFalse();
                heldLease!.Dispose();
                clock.Advance(cleanupDelay);
                OperationCanceledException? exception =
                    await Assert.ThrowsAsync<OperationCanceledException>(() => operation);
                await Assert.That(exception).IsNotNull();
                await Assert.That(exception!.CancellationToken).IsEqualTo(cancellation.Token);
                await Assert.That(clock.CreatedTimers).IsEqualTo(2);
                await Assert.That(Directory.GetFileSystemEntries(root)).IsEmpty();
            }
            else if (outcome == "held")
            {
                clock.Advance(TimeSpan.FromMilliseconds(1));
                foreach (int milliseconds in new[] { 40, 80, 160, 20, 40, 80, 160 })
                {
                    TimeSpan delay = await clock.NextDelayAsync();
                    await Assert.That(delay).IsEqualTo(TimeSpan.FromMilliseconds(milliseconds));
                    clock.Advance(delay - TimeSpan.FromMilliseconds(1));
                    await Assert.That(operation.IsCompleted).IsFalse();
                    clock.Advance(TimeSpan.FromMilliseconds(1));
                }
                _ = await Assert.ThrowsAsync<IOException>(() => operation);
                await Assert.That(Directory.Exists(request.OutputDirectory)).IsFalse();
                await Assert.That(clock.CreatedTimers).IsEqualTo(8);
            }
            else if (outcome == "collision")
            {
                Directory.CreateDirectory(request.OutputDirectory);
                await File.WriteAllTextAsync(Path.Combine(request.OutputDirectory, "original.txt"), "preserved");
                heldLease!.Dispose();
                clock.Advance(TimeSpan.FromMilliseconds(1));
                _ = await Assert.ThrowsAsync<IOException>(() => operation);
                await Assert.That(await File.ReadAllTextAsync(
                    Path.Combine(request.OutputDirectory, "original.txt"))).IsEqualTo("preserved");
                await Assert.That(Directory.GetDirectories(root)).IsEquivalentTo([request.OutputDirectory]);
                await Assert.That(clock.CreatedTimers).IsEqualTo(1);
            }
            else
            {
                heldLease!.Dispose();
                clock.Advance(TimeSpan.FromMilliseconds(1));
                RenderDiskJobResult result = await operation;
                await Assert.That(result.Frames.Count).IsEqualTo(1);
                await Assert.That(File.Exists(Path.Combine(result.OutputDirectory, "frame-000000.png"))).IsTrue();
                await Assert.That(File.Exists(Path.Combine(result.OutputDirectory, "manifest.json"))).IsTrue();
                await Assert.That(Directory.GetDirectories(root)).IsEquivalentTo([result.OutputDirectory]);
                await Assert.That(clock.CreatedTimers).IsEqualTo(1);
            }
            await Assert.That(clock.ActiveTimers).IsEqualTo(0);
        }
        finally
        {
            heldLease?.Dispose();
            cancellation.Cancel();
            clock.Advance(TimeSpan.FromDays(1));
            try
            {
                await operation;
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
            {
            }
            catch (IOException) when (outcome is "held" or "collision")
            {
            }
            Directory.Delete(root, recursive: true);
        }
    }

    private sealed class FrameSource(Action onCapture) : IRenderJobFrameSource
    {
        public RenderJobImage Render(StageRenderState state, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            onCapture();
            return new RenderJobImage(1, 1, new byte[] { 20, 40, 60, 255 }, Rgba8RowOrder.TopDown);
        }
    }

    [LibraryImport("kernel32", EntryPoint = "CreateFileW", SetLastError = true,
        StringMarshalling = StringMarshalling.Utf16)]
    private static partial SafeFileHandle OpenDirectory(
        string path, uint access, uint share, nint security, uint disposition, uint flags, nint template);
}
