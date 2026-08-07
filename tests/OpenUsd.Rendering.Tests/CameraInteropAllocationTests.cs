// Copyright (c) marcschier. Licensed under the MIT License.

using OpenUsd.Interop;
using OpenUsd.Rendering.Silk;
using OpenUsd.Rendering.Storm;

namespace OpenUsd.Rendering.Tests;

public sealed class CameraInteropAllocationTests
{
    private const int Iterations = 1000;
    private static int _stormRenderCalls;
    private static int _childRenderCalls;
    private static int _childRequestCalls;
    private static int _silkSyncCalls;

    [Test]
    public async Task StormRenderDispatchDoesNotAllocateAfterWarmup()
    {
        for (int i = 0; i < 32; i++)
        {
            _ = OpenUsdStormRuntime.Render<SuccessfulStormRenderCall>(
                (nint)1,
                640,
                480,
                0,
                0,
                CameraState.Default);
        }

        int callsBefore = Volatile.Read(ref _stormRenderCalls);
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < Iterations; i++)
        {
            _ = OpenUsdStormRuntime.Render<SuccessfulStormRenderCall>(
                (nint)1,
                640,
                480,
                0,
                i,
                CameraState.Default);
        }
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        await Assert.That(allocated).IsEqualTo(0);
        await Assert.That(Volatile.Read(ref _stormRenderCalls) - callsBefore)
            .IsEqualTo(Iterations);
    }

    [Test]
    public async Task StormChildRenderDispatchDoesNotAllocateAfterWarmup()
    {
        for (int i = 0; i < 32; i++)
        {
            OpenUsdStormChildRuntime.Render<SuccessfulChildRenderCall>(
                (nint)1,
                i,
                CameraState.Default);
        }

        int callsBefore = Volatile.Read(ref _childRenderCalls);
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < Iterations; i++)
        {
            OpenUsdStormChildRuntime.Render<SuccessfulChildRenderCall>(
                (nint)1,
                i,
                CameraState.Default);
        }
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        await Assert.That(allocated).IsEqualTo(0);
        await Assert.That(Volatile.Read(ref _childRenderCalls) - callsBefore)
            .IsEqualTo(Iterations);
    }

    [Test]
    public async Task StormChildRequestDispatchDoesNotAllocateAfterWarmup()
    {
        for (int i = 0; i < 32; i++)
        {
            OpenUsdStormChildRuntime.RequestFrame<SuccessfulChildRequestCall>(
                (nint)1,
                i,
                (ulong)i,
                CameraState.Default);
        }

        int callsBefore = Volatile.Read(ref _childRequestCalls);
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < Iterations; i++)
        {
            OpenUsdStormChildRuntime.RequestFrame<SuccessfulChildRequestCall>(
                (nint)1,
                i,
                (ulong)i,
                CameraState.Default);
        }
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        await Assert.That(allocated).IsEqualTo(0);
        await Assert.That(Volatile.Read(ref _childRequestCalls) - callsBefore)
            .IsEqualTo(Iterations);
    }

    [Test]
    public async Task SilkSyncDispatchDoesNotAllocateAfterWarmup()
    {
        long allocated;
        int callCount;
        {
            var view = new OpenUsdSilkRuntime.NativePageView();
            Span<byte> errorBytes = stackalloc byte[4096];
            for (int i = 0; i < 32; i++)
            {
                _ = OpenUsdSilkRuntime.InvokeSync<SuccessfulSilkSyncCall>(
                    (nint)1,
                    640,
                    480,
                    i,
                    CameraState.Default,
                    RenderComplexity.Low,
                    RenderDrawMode.SmoothShaded,
                    ref view,
                    out _,
                    errorBytes,
                    out _);
            }

            int callsBefore = Volatile.Read(ref _silkSyncCalls);
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < Iterations; i++)
            {
                _ = OpenUsdSilkRuntime.InvokeSync<SuccessfulSilkSyncCall>(
                    (nint)1,
                    640,
                    480,
                    i,
                    CameraState.Default,
                    RenderComplexity.Low,
                    RenderDrawMode.SmoothShaded,
                    ref view,
                    out _,
                    errorBytes,
                    out _);
            }
            allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            callCount = Volatile.Read(ref _silkSyncCalls) - callsBefore;
        }

        await Assert.That(allocated).IsEqualTo(0);
        await Assert.That(callCount).IsEqualTo(Iterations);
    }

    private readonly struct SuccessfulStormRenderCall : OpenUsdStormRuntime.IRenderCall
    {
        public static OpenUsdNativeStatus Invoke(
            nint renderer,
            int width,
            int height,
            uint framebuffer,
            double timeCode,
            in NativeRenderCamera camera,
            ulong stateRevision,
            ulong sceneRevision,
            uint revisionFlags,
            out int converged,
            Span<byte> errorBytes,
            out nuint errorRequired)
        {
            _ = renderer;
            _ = width;
            _ = height;
            _ = framebuffer;
            _ = timeCode;
            _ = camera;
            _ = stateRevision;
            _ = sceneRevision;
            _ = revisionFlags;
            _ = errorBytes;
            Interlocked.Increment(ref _stormRenderCalls);
            converged = 1;
            errorRequired = 0;
            return OpenUsdNativeStatus.Ok;
        }
    }

    private readonly struct SuccessfulChildRenderCall : OpenUsdStormChildRuntime.IRenderCall
    {
        public static OpenUsdNativeStatus Invoke(
            nint child,
            double timeCode,
            in NativeRenderCamera camera,
            ulong stateRevision,
            ulong sceneRevision,
            uint revisionFlags,
            out ulong frameCount,
            out int converged,
            Span<byte> errorBytes,
            out nuint errorRequired)
        {
            _ = child;
            _ = camera;
            _ = stateRevision;
            _ = sceneRevision;
            _ = revisionFlags;
            _ = errorBytes;
            Interlocked.Increment(ref _childRenderCalls);
            frameCount = checked((ulong)timeCode);
            converged = 1;
            errorRequired = 0;
            return OpenUsdNativeStatus.Ok;
        }
    }

    private readonly struct SuccessfulChildRequestCall : OpenUsdStormChildRuntime.IRequestFrameCall
    {
        public static OpenUsdNativeStatus Invoke(
            nint child,
            double timeCode,
            in NativeRenderCamera camera,
            ulong revision,
            ulong sceneRevision,
            uint revisionFlags,
            Span<byte> errorBytes,
            out nuint errorRequired)
        {
            _ = child;
            _ = timeCode;
            _ = camera;
            _ = revision;
            _ = sceneRevision;
            _ = revisionFlags;
            _ = errorBytes;
            Interlocked.Increment(ref _childRequestCalls);
            errorRequired = 0;
            return OpenUsdNativeStatus.Ok;
        }
    }

    private readonly struct SuccessfulSilkSyncCall : OpenUsdSilkRuntime.ISyncCall
    {
        public static OpenUsdNativeStatus Invoke(
            nint session,
            int width,
            int height,
            double timeCode,
            in NativeRenderCamera camera,
            RenderComplexity complexity,
            RenderDrawMode drawMode,
            out nint page,
            ref OpenUsdSilkRuntime.NativePageView view,
            Span<byte> errorBytes,
            out nuint errorRequired)
        {
            _ = session;
            _ = width;
            _ = height;
            _ = timeCode;
            _ = camera;
            _ = complexity;
            _ = drawMode;
            _ = view;
            _ = errorBytes;
            Interlocked.Increment(ref _silkSyncCalls);
            page = (nint)1;
            errorRequired = 0;
            return OpenUsdNativeStatus.Ok;
        }
    }
}
