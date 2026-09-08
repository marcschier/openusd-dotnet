// Copyright (c) marcschier. Licensed under the MIT License.

using OpenUsd.Rendering.Silk;

namespace OpenUsd.Rendering.ConformanceTests;

public sealed partial class SilkHdrColorCaptureNativeTests
{
    [Test]
    [Arguments(SilkGraphicsBackend.D3D12, false)]
    [Arguments(SilkGraphicsBackend.D3D12, true)]
    [Arguments(SilkGraphicsBackend.Vulkan, false)]
    [Arguments(SilkGraphicsBackend.Vulkan, true)]
    [Arguments(SilkGraphicsBackend.Metal, false)]
    [Arguments(SilkGraphicsBackend.Metal, true)]
    public async Task GpuDisplayRefusalPrecedesTargetsAndInitialNativeSync(SilkGraphicsBackend backend, bool cpuOcio)
    {
        using var device = new SilkDepthCaptureObservedDevice(
            SilkDepthCaptureConformance.CreateDevice(backend));
        using var fixture = new SilkHdrColorCaptureFixture();
        using OpenUsdSilkSession retainedSession = fixture.CreateSession();
        using var renderer = new SilkMeshRenderer(device);
        using SilkOpenColorIoProcessor processor = SilkHdrColorCaptureFixture.CreateProcessor("TestView");
        _ = SilkHdrColorCaptureFixture.RetainScene(device, renderer, retainedSession);
        RenderSettings gpu = RenderSettings.Default with
        {
            DisplayTransform = new RenderDisplayTransform(
                SilkHdrColorCaptureFixture.OcioConfigPath, "linear", "TestDisplay", "TestView")
        };
        int before = device.TextureRequests;
        bool retainedRefused = false;
        bool syncRefused = false;
        try
        {
            _ = cpuOcio
                ? SilkFrameCapture.CaptureRetainedWithHdrColor(renderer, device, 40, 32, gpu, processor)
                : SilkFrameCapture.CaptureRetainedWithHdrColor(renderer, device, 40, 32, gpu);
        }
        catch (NotSupportedException)
        {
            retainedRefused = true;
        }
        using OpenUsdSilkSession session = fixture.CreateSession();
        using var refused = new SilkFrameCapturer(device);
        try
        {
            _ = cpuOcio
                ? refused.CaptureWithHdrColor(
                    session, 40, 32, gpu, processor, camera: SilkHdrColorCaptureFixture.Camera)
                : refused.CaptureWithHdrColor(session, 40, 32, gpu, camera: SilkHdrColorCaptureFixture.Camera);
        }
        catch (NotSupportedException)
        {
            syncRefused = true;
        }
        int after = device.TextureRequests;
        using var fresh = new SilkFrameCapturer(device);
        SilkFrameCaptureResult untouched = fresh.CaptureWithHdrColor(
            session, 40, 32, RenderSettings.Default, camera: SilkHdrColorCaptureFixture.Camera);

        await Assert.That(retainedRefused).IsTrue();
        await Assert.That(syncRefused).IsTrue();
        await Assert.That(after).IsEqualTo(before);
        await Assert.That(untouched.RenderResult.DrawCount).IsEqualTo(2);
        await Assert.That(untouched.HdrColor!.Rgba16Float.Span.Slice(((8 * 40) + 10) * 8, 8)
            .SequenceEqual<byte>([0x00, 0x30, 0x00, 0x38, 0x00, 0x40, 0x00, 0x3C])).IsTrue();
    }

    [Test]
    [Arguments(SilkGraphicsBackend.D3D12)]
    [Arguments(SilkGraphicsBackend.Vulkan)]
    [Arguments(SilkGraphicsBackend.Metal)]
    public async Task FailedGpuSetupNeverExportsCachedPreviousFrameHdr(SilkGraphicsBackend backend)
    {
        using ISilkGraphicsDevice device = SilkDepthCaptureConformance.CreateDevice(backend);
        using var fixture = new SilkHdrColorCaptureFixture();
        using OpenUsdSilkSession session = fixture.CreateSession();
        using var renderer = new SilkMeshRenderer(device);
        _ = SilkHdrColorCaptureFixture.RetainScene(device, renderer, session);
        RenderSettings gpu = RenderSettings.Default with
        {
            DisplayTransform = new RenderDisplayTransform(
                SilkHdrColorCaptureFixture.OcioConfigPath, "linear", "TestDisplay", "TestView")
        };
        SilkFrameCaptureResult gpuFrame = SilkFrameCapture.CaptureRetained(renderer, device, 40, 32, gpu);
        SilkDisplayTransformStatus firstStatus = renderer.DisplayTransformDiagnostics.Status;
        _ = SilkHdrColorCaptureFixture.RetainScene(device, renderer, session, timeCode: 1);
        RenderSettings failedGpu = RenderSettings.Default with
        {
            DisplayTransform = new RenderDisplayTransform(
                Path.Combine(fixture.Root, "missing.ocio"), "linear", "TestDisplay", "TestView")
        };
        SilkFrameCaptureResult fallback = SilkFrameCapture.CaptureRetained(renderer, device, 40, 32, failedGpu);
        SilkDisplayTransformStatus failedStatus = renderer.DisplayTransformDiagnostics.Status;
        bool hdrRefused = false;
        try
        {
            _ = SilkFrameCapture.CaptureRetainedWithHdrColor(renderer, device, 40, 32, failedGpu);
        }
        catch (InvalidOperationException)
        {
            hdrRefused = true;
        }
        SilkFrameCaptureResult current = SilkFrameCapture.CaptureRetainedWithHdrColor(
            renderer, device, 40, 32, RenderSettings.Default);

        await Assert.That(firstStatus).IsEqualTo(SilkDisplayTransformStatus.Applied);
        await Assert.That(failedStatus).IsNotEqualTo(SilkDisplayTransformStatus.Applied);
        await Assert.That(hdrRefused).IsTrue();
        await Assert.That(gpuFrame.HdrColor).IsNull();
        await Assert.That(fallback.HdrColor).IsNull();
        await Assert.That(current.HdrColor!.Rgba16Float.Span.Slice(((8 * 40) + 10) * 8, 8)
            .SequenceEqual<byte>([0x00, 0x38, 0x00, 0x40, 0x00, 0x30, 0x00, 0x3C])).IsTrue();
    }
}
