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
        SafeFileHandle? heldLease = null;
        Task release = Task.CompletedTask;
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
            if (outcome == "held")
            {
                heldLease = handle;
                return;
            }
            release = Task.Run(async () =>
            {
                if (outcome == "canceled")
                {
                    await Task.Delay(50);
                    cancellation.Cancel();
                }
                await Task.Delay(100);
                if (outcome == "collision")
                {
                    string destination = Directory.CreateDirectory(Path.Combine(root, "complete")).FullName;
                    await File.WriteAllTextAsync(Path.Combine(destination, "original.txt"), "preserved");
                }
                handle.Dispose();
            });
        });
        StageRenderState state = StageRenderState.Create(new StageIdentity("source.usda"))
            .WithViewport(new ViewportDimensions(1, 1));
        try
        {
            var request = new RenderDiskJobRequest(Path.Combine(root, "complete"), [state]);
            if (outcome == "canceled")
            {
                await Assert.That(() => RenderDiskJob.Execute(request, source, cancellation.Token))
                    .Throws<OperationCanceledException>();
                await release;
                await Assert.That(Directory.GetFileSystemEntries(root)).IsEmpty();
            }
            else if (outcome == "held")
            {
                await Assert.That(() => RenderDiskJob.Execute(request, source)).Throws<IOException>();
                await Assert.That(Directory.Exists(request.OutputDirectory)).IsFalse();
            }
            else if (outcome == "collision")
            {
                await Assert.That(() => RenderDiskJob.Execute(request, source)).Throws<IOException>();
                await Assert.That(await File.ReadAllTextAsync(
                    Path.Combine(request.OutputDirectory, "original.txt"))).IsEqualTo("preserved");
                await Assert.That(Directory.GetDirectories(root)).IsEquivalentTo([request.OutputDirectory]);
            }
            else
            {
                RenderDiskJobResult result = RenderDiskJob.Execute(request, source);
                await Assert.That(result.Frames.Count).IsEqualTo(1);
                await Assert.That(File.Exists(Path.Combine(result.OutputDirectory, "frame-000000.png"))).IsTrue();
                await Assert.That(File.Exists(Path.Combine(result.OutputDirectory, "manifest.json"))).IsTrue();
                await Assert.That(Directory.GetDirectories(root)).IsEquivalentTo([result.OutputDirectory]);
            }
            await release;
        }
        finally
        {
            await release;
            heldLease?.Dispose();
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
