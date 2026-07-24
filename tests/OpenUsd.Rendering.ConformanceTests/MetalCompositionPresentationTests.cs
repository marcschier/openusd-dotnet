// Copyright (c) marcschier. Licensed under the MIT License.

using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using OpenUsd.Rendering.Silk;
using OpenUsd.Rendering.Silk.Metal;
using SharpMetal.Metal;

namespace OpenUsd.Rendering.ConformanceTests;

[NotInParallel]
public sealed partial class MetalCompositionPresentationTests
{
    [Test]
    [SupportedOSPlatform("macos")]
    public async Task PresentsIOSurfaceFramesWithTimelineSynchronizationOnMacOS()
    {
        if (!OperatingSystem.IsMacOS())
        {
            return;
        }

        byte[] deviceLuid;
        using (MetalSilkGraphicsDevice identityDevice =
               MetalSilkGraphicsDevice.Create())
        {
            deviceLuid = identityDevice.GetPresentationDeviceLuid();
        }

        await using (var incompatible = new MetalCompositionViewportPresenter())
        {
            byte[] otherDevice = deviceLuid.ToArray();
            otherDevice[0] ^= byte.MaxValue;
            CompositionPresenterProbeResult mismatch = await incompatible.ProbeAsync(
                CreateTarget(otherDevice));
            await Assert.That(mismatch.IsAvailable).IsFalse();
            await Assert.That(mismatch.Status).Contains("different devices");
        }

        await using var presenter =
            new MetalCompositionViewportPresenter(required: true);
        CompositionPresenterProbeResult probe = await presenter.ProbeAsync(
            CreateTarget(deviceLuid));
        await Assert.That(probe.IsAvailable).IsTrue();
        await Assert.That(presenter.HasDiagnosticPipeline).IsTrue();
        await Assert.That(presenter.HasRetainedMeshRenderer).IsFalse();

        ICompositionPresentationGeneration generation =
            await presenter.CreateGenerationAsync(
                new ViewportDimensions(64, 64),
                frameCount: 2);
        CompositionPresenterProbeResult repeatedProbe = await presenter.ProbeAsync(
            CreateTarget(deviceLuid));
        await Assert.That(repeatedProbe.IsAvailable).IsTrue();
        byte[] reprobeMismatch = deviceLuid.ToArray();
        reprobeMismatch[0] ^= byte.MaxValue;
        Exception? reprobeFailure = null;
        try
        {
            _ = await presenter.ProbeAsync(CreateTarget(reprobeMismatch));
        }
        catch (Exception exception)
        {
            reprobeFailure = exception;
        }
        await Assert.That(reprobeFailure).IsTypeOf<InvalidOperationException>();
        await Assert.That(reprobeFailure!.Message).Contains("different devices");

        await Assert.That(generation.Frames.Count).IsEqualTo(2);
        ICompositionPresentationFrame frame = generation.Frames[0];
        await Assert.That(frame.Image.HandleType)
            .IsEqualTo(MetalCompositionViewportPresenter.IOSurfaceHandleType);
        await Assert.That(frame.Image.Format)
            .IsEqualTo(CompositionExternalImageFormat.R8G8B8A8UNorm);
        await Assert.That(frame.Semaphores.Count).IsEqualTo(1);

        await using ICompositionExternalHandleLease imageLease =
            await frame.LeaseImageHandleAsync();
        await using ICompositionExternalHandleLease eventLease =
            await frame.LeaseSemaphoreHandleAsync(frame.Semaphores[0].ResourceId);
        await Assert.That(imageLease.Handle).IsNotEqualTo(0);
        await Assert.That(eventLease.Handle).IsNotEqualTo(0);
        await Assert.That(imageLease.Ownership).IsEqualTo(
            CompositionExternalHandleOwnership.BorrowedUntilImportCompleted);
        await Assert.That(eventLease.Ownership).IsEqualTo(
            CompositionExternalHandleOwnership.BorrowedUntilImportCompleted);

        var sharedEvent = new MTLSharedEvent(eventLease.Handle);
        CompositionFrameRenderResult first = await presenter.RenderAsync(frame);
        await Assert.That(first.Status)
            .IsEqualTo(CompositionFrameRenderStatus.Presented);
        await Assert.That(first.Synchronization.Kind).IsEqualTo(
            CompositionFrameSynchronizationKind.TimelineSemaphores);
        await Assert.That(sharedEvent.WaitUntilSignaledValue(
            first.Synchronization.WaitValue,
            5000)).IsTrue();
        byte[] background = IOSurfaceTestInterop.ReadPixel(imageLease.Handle, 2, 2);
        byte[] interior = IOSurfaceTestInterop.ReadPixel(imageLease.Handle, 32, 32);
        await Assert.That(background.SequenceEqual(new byte[] { 0, 0, 0, 255 }))
            .IsTrue();
        await Assert.That(interior[0]).IsGreaterThanOrEqualTo((byte)240);
        await Assert.That(interior[1]).IsLessThanOrEqualTo((byte)10);
        await Assert.That(interior[2]).IsLessThanOrEqualTo((byte)10);
        await Assert.That(interior[3]).IsGreaterThanOrEqualTo((byte)240);
        sharedEvent.SignaledValue = first.Synchronization.SignalValue;

        CompositionFrameRenderResult second = await presenter.RenderAsync(frame);
        await Assert.That(second.Synchronization.WaitValue)
            .IsGreaterThan(first.Synchronization.SignalValue);
        await Assert.That(second.Synchronization.SignalValue)
            .IsGreaterThan(second.Synchronization.WaitValue);
        await Assert.That(sharedEvent.WaitUntilSignaledValue(
            second.Synchronization.WaitValue,
            5000)).IsTrue();
        sharedEvent.SignaledValue = second.Synchronization.SignalValue;

        ICompositionExternalHandleLease retainedImage =
            await frame.LeaseImageHandleAsync();
        ICompositionExternalHandleLease retainedEvent =
            await frame.LeaseSemaphoreHandleAsync(frame.Semaphores[0].ResourceId);
        await generation.DisposeAsync();
        await Assert.That(IOSurfaceTestInterop.GetWidth(retainedImage.Handle))
            .IsEqualTo((nuint)64);
        var retainedSharedEvent = new MTLSharedEvent(retainedEvent.Handle);
        await Assert.That(retainedSharedEvent.SignaledValue)
            .IsGreaterThanOrEqualTo(second.Synchronization.SignalValue);
        await retainedEvent.DisposeAsync();
        await retainedImage.DisposeAsync();

        ICompositionPresentationGeneration lostGeneration =
            await presenter.CreateGenerationAsync(
                new ViewportDimensions(16, 16),
                frameCount: 2);
        ICompositionPresentationFrame poisonedFrame = lostGeneration.Frames[0];
        await using ICompositionExternalHandleLease poisonedEventLease =
            await poisonedFrame.LeaseSemaphoreHandleAsync(
                poisonedFrame.Semaphores[0].ResourceId);
        var poisonedEvent = new MTLSharedEvent(poisonedEventLease.Handle);
        CompositionFrameRenderResult poisoned =
            await presenter.RenderAsync(poisonedFrame);
        await Assert.That(poisonedEvent.WaitUntilSignaledValue(
            poisoned.Synchronization.WaitValue,
            5000)).IsTrue();
        CompositionFrameRenderResult lost = await presenter.RenderAsync(poisonedFrame)
            .AsTask()
            .WaitAsync(TimeSpan.FromSeconds(5));
        await Assert.That(lost.Status)
            .IsEqualTo(CompositionFrameRenderStatus.DeviceLost);
        await lostGeneration.DisposeAsync()
            .AsTask()
            .WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Test]
    [SupportedOSPlatform("macos")]
    public async Task RequiredModeRejectsMissingMetalInteropOnMacOS()
    {
        if (!OperatingSystem.IsMacOS())
        {
            return;
        }

        await using var presenter =
            new MetalCompositionViewportPresenter(required: true);
        Exception? failure = null;
        try
        {
            _ = await presenter.ProbeAsync(
                new CompositionPresentationTarget([], [], null, null));
        }
        catch (Exception exception)
        {
            failure = exception;
        }

        await Assert.That(failure).IsTypeOf<InvalidOperationException>();
        await Assert.That(failure!.Message).Contains("IOSurfaceRef");
    }

    [Test]
    [SupportedOSPlatform("macos")]
    public async Task RetainedMeshCallbackChangesIOSurfacePixelsAndReportsRingEvidenceOnMacOS()
    {
        if (!OperatingSystem.IsMacOS())
        {
            return;
        }

        ulong revision = 1;
        double offset = -0.25;
        float[] color = [0.1f, 0.2f, 1f, 1f];
        await using var presenter = new MetalCompositionViewportPresenter(
            context =>
            {
                context.CancellationToken.ThrowIfCancellationRequested();
                SilkMeshRendererConformance.Apply(
                    context.Renderer,
                    revision,
                    SilkMeshRendererConformance.CreateFrameCommand(
                        context.ColorTarget.Width,
                        context.ColorTarget.Height,
                        SilkMeshRendererConformance.Identity()),
                    SilkMeshRendererConformance.CreateCubeCommand(
                        1,
                        "/World/Cube",
                        offset,
                        0,
                        color));
                if (!context.DepthTarget.Usage.HasFlag(SilkTextureUsage.Sampled) ||
                    !context.DepthTarget.Usage.HasFlag(
                        SilkTextureUsage.DepthRenderTarget))
                {
                    throw new InvalidOperationException(
                        "Metal composition callback depth must be sampled and renderable.");
                }
                context.Renderer.UpdateSelection(
                    new SelectionState(["/World/Cube"]));
                SilkMeshRenderResult result = context.Renderer.Render(
                    context.ColorTarget,
                    context.DepthTarget);
                if (context.Renderer.SelectionOutlineDiagnostics.Status !=
                    SilkSelectionOutlineStatus.Rendered)
                {
                    throw new InvalidOperationException(
                        "Metal composition callback did not render the selection outline.");
                }
                return new MetalCompositionRenderResult(revision, result);
            },
            required: true);
        CompositionPresenterProbeResult probe = await presenter.ProbeAsync(
            CreateTarget([]));
        await Assert.That(probe.IsAvailable).IsTrue();
        await Assert.That(presenter.HasDiagnosticPipeline).IsFalse();
        await Assert.That(presenter.HasRetainedMeshRenderer).IsTrue();

        ICompositionPresentationGeneration generation =
            await presenter.CreateGenerationAsync(new ViewportDimensions(64, 64), 2);
        (string initialHash, CompositionFrameRenderResult initial) =
            await RenderAndCaptureAsync(presenter, generation.Frames[0]);
        await Assert.That(initial.Status)
            .IsEqualTo(CompositionFrameRenderStatus.Presented);

        revision = 2;
        offset = 0.25;
        color = [1f, 0.1f, 0.1f, 1f];
        (string editedHash, _) =
            await RenderAndCaptureAsync(presenter, generation.Frames[0]);
        await Assert.That(editedHash).IsNotEqualTo(initialHash);

        MetalCompositionPresenterDiagnostics edited = presenter.GetDiagnostics();
        await Assert.That(edited.RenderCallbacks).IsEqualTo(2);
        await Assert.That(edited.RingReuseFrames).IsEqualTo(1);
        await Assert.That(edited.LastSceneRevision).IsEqualTo(2ul);
        await Assert.That(edited.LastDrawCount).IsEqualTo(1);
        await Assert.That(edited.LastTriangleCount).IsGreaterThan(0);
        await Assert.That(edited.LastAllocationId).IsGreaterThan(0);

        await generation.DisposeAsync();
        generation = await presenter.CreateGenerationAsync(
            new ViewportDimensions(80, 48),
            2);
        _ = await RenderAndCaptureAsync(presenter, generation.Frames[1]);
        MetalCompositionPresenterDiagnostics resized = presenter.GetDiagnostics();
        await Assert.That(resized.LastWidth).IsEqualTo(80);
        await Assert.That(resized.LastHeight).IsEqualTo(48);
        await generation.DisposeAsync();

        MetalCompositionPresenterDiagnostics released = presenter.GetDiagnostics();
        await Assert.That(released.ActiveGenerations).IsEqualTo(0);
        await Assert.That(released.ActiveFrames).IsEqualTo(0);
    }

    [SupportedOSPlatform("macos")]
    private static async Task<(string Hash, CompositionFrameRenderResult Result)>
        RenderAndCaptureAsync(
            MetalCompositionViewportPresenter presenter,
            ICompositionPresentationFrame frame)
    {
        await using ICompositionExternalHandleLease image =
            await frame.LeaseImageHandleAsync();
        await using ICompositionExternalHandleLease eventLease =
            await frame.LeaseSemaphoreHandleAsync(frame.Semaphores[0].ResourceId);
        var sharedEvent = new MTLSharedEvent(eventLease.Handle);
        CompositionFrameRenderResult result = await presenter.RenderAsync(frame);
        if (!sharedEvent.WaitUntilSignaledValue(
                result.Synchronization.WaitValue,
                5000))
        {
            throw new InvalidOperationException(
                "The Metal retained-scene render did not signal its producer timeline.");
        }
        byte[] pixels = IOSurfaceTestInterop.ReadPixels(
            image.Handle,
            frame.Image.Size.Width,
            frame.Image.Size.Height);
        sharedEvent.SignaledValue = result.Synchronization.SignalValue;
        return (Convert.ToHexString(SHA256.HashData(pixels)), result);
    }

    private static CompositionPresentationTarget CreateTarget(byte[] deviceLuid) =>
        new(
            [MetalCompositionViewportPresenter.IOSurfaceHandleType],
            [MetalCompositionViewportPresenter.SharedEventHandleType],
            deviceLuid,
            deviceUuid: null);

    private static partial class IOSurfaceTestInterop
    {
        private const string IOSurface =
            "/System/Library/Frameworks/IOSurface.framework/IOSurface";

        internal static byte[] ReadPixel(nint surface, nuint x, nuint y)
        {
            int result = IOSurfaceLock(surface, 1, 0);
            if (result != 0)
            {
                throw new InvalidOperationException(
                    $"IOSurfaceLock failed with status {result}.");
            }

            try
            {
                nint address = IOSurfaceGetBaseAddress(surface);
                nuint rowBytes = IOSurfaceGetBytesPerRow(surface);
                nint offset = checked((nint)(y * rowBytes + x * 4));
                return
                [
                    Marshal.ReadByte(address, checked((int)offset)),
                    Marshal.ReadByte(address, checked((int)offset + 1)),
                    Marshal.ReadByte(address, checked((int)offset + 2)),
                    Marshal.ReadByte(address, checked((int)offset + 3))
                ];
            }
            finally
            {
                _ = IOSurfaceUnlock(surface, 1, 0);
            }
        }

        internal static byte[] ReadPixels(nint surface, int width, int height)
        {
            int result = IOSurfaceLock(surface, 1, 0);
            if (result != 0)
            {
                throw new InvalidOperationException(
                    $"IOSurfaceLock failed with status {result}.");
            }
            try
            {
                nint address = IOSurfaceGetBaseAddress(surface);
                nuint rowBytes = IOSurfaceGetBytesPerRow(surface);
                int packedRowBytes = checked(width * 4);
                var pixels = new byte[checked(packedRowBytes * height)];
                for (int y = 0; y < height; y++)
                {
                    Marshal.Copy(
                        address + checked((nint)(checked((nuint)y) * rowBytes)),
                        pixels,
                        checked(y * packedRowBytes),
                        packedRowBytes);
                }
                return pixels;
            }
            finally
            {
                _ = IOSurfaceUnlock(surface, 1, 0);
            }
        }

        [LibraryImport(IOSurface, EntryPoint = "IOSurfaceGetWidth")]
        internal static partial nuint GetWidth(nint surface);

        [LibraryImport(IOSurface, EntryPoint = "IOSurfaceGetBaseAddress")]
        private static partial nint IOSurfaceGetBaseAddress(nint surface);

        [LibraryImport(IOSurface, EntryPoint = "IOSurfaceGetBytesPerRow")]
        private static partial nuint IOSurfaceGetBytesPerRow(nint surface);

        [LibraryImport(IOSurface, EntryPoint = "IOSurfaceLock")]
        private static partial int IOSurfaceLock(
            nint surface,
            uint options,
            nint seed);

        [LibraryImport(IOSurface, EntryPoint = "IOSurfaceUnlock")]
        private static partial int IOSurfaceUnlock(
            nint surface,
            uint options,
            nint seed);
    }
}
