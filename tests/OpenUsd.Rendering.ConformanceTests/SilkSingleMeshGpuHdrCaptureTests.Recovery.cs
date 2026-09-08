// Copyright (c) marcschier. Licensed under the MIT License.

using OpenUsd.Rendering.Silk;

namespace OpenUsd.Rendering.ConformanceTests;

public sealed partial class SilkSingleMeshGpuHdrCaptureTests
{
    [Test]
    [Arguments(SilkGraphicsBackend.D3D12, false, false)]
    [Arguments(SilkGraphicsBackend.D3D12, false, true)]
    [Arguments(SilkGraphicsBackend.D3D12, true, false)]
    [Arguments(SilkGraphicsBackend.D3D12, true, true)]
    [Arguments(SilkGraphicsBackend.Vulkan, false, false)]
    [Arguments(SilkGraphicsBackend.Vulkan, false, true)]
    [Arguments(SilkGraphicsBackend.Vulkan, true, false)]
    [Arguments(SilkGraphicsBackend.Vulkan, true, true)]
    [Arguments(SilkGraphicsBackend.Metal, false, false)]
    [Arguments(SilkGraphicsBackend.Metal, false, true)]
    [Arguments(SilkGraphicsBackend.Metal, true, false)]
    [Arguments(SilkGraphicsBackend.Metal, true, true)]
    public async Task SingleCubeFailedGpuConfigRefusesHdrAndRetainsUpdatedSceneWithOrWithoutCachedTarget(
        SilkGraphicsBackend backend, bool retained, bool seedCachedTarget)
    {
        using var device = new SilkGpuHdrObservedDevice(SilkDepthCaptureConformance.CreateDevice(backend));
        using var fixture = new SilkSingleMeshHdrFixture();
        using OpenUsdSilkSession session = fixture.CreateSession();
        using var renderer = new SilkMeshRenderer(device);
        using var capturer = new SilkFrameCapturer(device);
        if (retained)
        {
            SilkSingleMeshHdrFixture.Retain(device, renderer, session);
        }
        SilkFrameCaptureResult? first = seedCachedTarget ? Capture(SilkSingleMeshHdrFixture.GpuSettings(), 0) : null;
        if (retained)
        {
            SilkSingleMeshHdrFixture.Retain(device, renderer, session, time: 1);
        }
        RenderSettings missing = SilkSingleMeshHdrFixture.GpuSettings(Path.Combine(fixture.Root, "missing.ocio"));
        int before = device.ScenePasses;
        InvalidOperationException? refusal = null;
        try
        {
            _ = Capture(missing, 1);
        }
        catch (InvalidOperationException exception)
        {
            refusal = exception;
        }
        int renderedPasses = device.ScenePasses - before;
        int submitted = device.Submissions;
        int completed = device.CompletedSubmissions;
        SilkFrameCaptureResult retry = Capture(SilkSingleMeshHdrFixture.GpuSettings(), 1);
        SilkFrameCaptureResult fallback = retained
            ? SilkFrameCapture.CaptureRetained(renderer, device, 16, 16, missing)
            : capturer.Capture(session, 16, 16, missing, timeCode: 1, camera: SilkHdrColorCaptureFixture.Camera);

        await Assert.That(refusal).IsNotNull()
            .Because("a one-mesh failed GPU frame must refuse HDR, not return a missing or cached plane");
        await Assert.That(refusal!.Message).Contains("completed frame");
        await Assert.That(renderedPasses).IsEqualTo(1);
        await Assert.That(completed).IsEqualTo(submitted);
        await Assert.That(retry.RenderResult.DrawCount).IsEqualTo(1);
        await Assert.That(retry.HdrColor!.Rgba16Float.Span.Slice(((8 * 16) + 8) * 8, 8)
            .SequenceEqual<byte>([0x00, 0x00, 0x00, 0x54, 0x00, 0x00, 0x00, 0x3C])).IsTrue();
        await Assert.That(retry.Depth!.Values.Span[(8 * 16) + 8]).IsEqualTo(0.15f).Within(0.00001f);
        await Assert.That(fallback.HdrColor).IsNull();
        await Assert.That(fallback.RenderResult.DrawCount).IsEqualTo(1);
        if (first is not null)
        {
            await Assert.That(first.HdrColor!.Rgba16Float.Span.Slice(((8 * 16) + 8) * 8, 8)
                .SequenceEqual<byte>([0x00, 0x54, 0x00, 0x00, 0x00, 0x00, 0x00, 0x3C])).IsTrue();
        }

        SilkFrameCaptureResult Capture(RenderSettings settings, double time) => retained
            ? SilkFrameCapture.CaptureRetainedWithHdrColor(
                renderer, device, 16, 16, settings, new SilkHdrColorCaptureOptions(includeDeviceDepth: true))
            : capturer.CaptureWithHdrColor(
                session, 16, 16, settings, new SilkHdrColorCaptureOptions(includeDeviceDepth: true),
                timeCode: time, camera: SilkHdrColorCaptureFixture.Camera);
    }

    [Test]
    [Arguments(SilkGraphicsBackend.D3D12, false)]
    [Arguments(SilkGraphicsBackend.D3D12, true)]
    [Arguments(SilkGraphicsBackend.Vulkan, false)]
    [Arguments(SilkGraphicsBackend.Vulkan, true)]
    [Arguments(SilkGraphicsBackend.Metal, false)]
    [Arguments(SilkGraphicsBackend.Metal, true)]
    public async Task SingleCubeCancellationAfterCompletionRetainsEditsAndDetachedPlanes(
        SilkGraphicsBackend backend, bool retained)
    {
        using var device = new SilkGpuHdrObservedDevice(SilkDepthCaptureConformance.CreateDevice(backend));
        using var fixture = new SilkSingleMeshHdrFixture();
        SilkFrameCaptureResult first;
        SilkFrameCaptureResult retry;
        byte[] firstHdr;
        byte[] firstDisplay;
        float[] firstDepth;
        bool cancelled = false;
        int submitted;
        int completed;
        int renderedPasses;
        using (OpenUsdSilkSession session = fixture.CreateSession())
        using (var renderer = new SilkMeshRenderer(device))
        using (var capturer = new SilkFrameCapturer(device))
        using (var cancellation = new CancellationTokenSource())
        {
            if (retained)
            {
                SilkSingleMeshHdrFixture.Retain(device, renderer, session);
            }
            first = Capture(0, default);
            firstHdr = first.HdrColor!.Rgba16Float.ToArray();
            firstDisplay = first.Rgba.ToArray();
            firstDepth = first.Depth!.Values.ToArray();
            if (retained)
            {
                SilkSingleMeshHdrFixture.Retain(device, renderer, session, time: 1);
            }
            int before = device.ScenePasses;
            device.AfterCompletedWait = cancellation.Cancel;
            try
            {
                _ = Capture(1, cancellation.Token);
            }
            catch (OperationCanceledException)
            {
                cancelled = true;
            }
            renderedPasses = device.ScenePasses - before;
            submitted = device.Submissions;
            completed = device.CompletedSubmissions;
            retry = Capture(1, default);

            SilkFrameCaptureResult Capture(double time, CancellationToken token) => retained
                ? SilkFrameCapture.CaptureRetainedWithHdrColor(
                    renderer, device, 16, 16, SilkSingleMeshHdrFixture.GpuSettings(),
                    new SilkHdrColorCaptureOptions(includeDeviceDepth: true), cancellationToken: token)
                : capturer.CaptureWithHdrColor(
                    session, 16, 16, SilkSingleMeshHdrFixture.GpuSettings(),
                    new SilkHdrColorCaptureOptions(includeDeviceDepth: true), timeCode: time,
                    camera: SilkHdrColorCaptureFixture.Camera, cancellationToken: token);
        }
        device.Dispose();

        await Assert.That(cancelled).IsTrue();
        await Assert.That(renderedPasses).IsEqualTo(1);
        await Assert.That(completed).IsEqualTo(submitted);
        await Assert.That(device.StayedOnCallingThread).IsTrue();
        await Assert.That(retry.RenderResult.DrawCount).IsEqualTo(1);
        await Assert.That(retry.HdrColor!.Rgba16Float.Span.Slice(((8 * 16) + 8) * 8, 8)
            .SequenceEqual<byte>([0x00, 0x00, 0x00, 0x54, 0x00, 0x00, 0x00, 0x3C])).IsTrue();
        await Assert.That(retry.Depth!.Values.Span[(8 * 16) + 8]).IsEqualTo(0.15f).Within(0.00001f);
        await Assert.That(first.HdrColor!.Rgba16Float.Span.SequenceEqual(firstHdr)).IsTrue();
        await Assert.That(first.Rgba.Span.SequenceEqual(firstDisplay)).IsTrue();
        await Assert.That(first.Depth!.Values.Span.SequenceEqual(firstDepth)).IsTrue();
    }
}
