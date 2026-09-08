// Copyright (c) marcschier. Licensed under the MIT License.

using OpenUsd.Rendering.Silk;

namespace OpenUsd.Rendering.ConformanceTests;

[NotInParallel]
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
    public async Task OneNativeCubeReturnsHdrAfterCpuCaptureWithOptionalDepthAndOldGpuDisplayEquality(
        SilkGraphicsBackend backend, bool retained, bool includeDepth)
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
        var options = new SilkHdrColorCaptureOptions(includeDepth);
        SilkFrameCaptureResult cpu = Capture(RenderSettings.Default);
        int before = device.ScenePasses;
        SilkFrameCaptureResult gpu = Capture(SilkSingleMeshHdrFixture.GpuSettings());
        int renderedPasses = device.ScenePasses - before;
        SilkFrameCaptureResult oldGpu = retained
            ? SilkFrameCapture.CaptureRetainedWithDepth(
                renderer, device, 16, 16, SilkSingleMeshHdrFixture.GpuSettings())
            : capturer.CaptureWithDepth(
                session, 16, 16, SilkSingleMeshHdrFixture.GpuSettings(), camera: SilkHdrColorCaptureFixture.Camera);

        await Assert.That(cpu.RenderResult.DrawCount).IsEqualTo(1);
        await Assert.That(gpu.RenderResult.DrawCount).IsEqualTo(1);
        await Assert.That(renderedPasses).IsEqualTo(1);
        await Assert.That(gpu.HdrColor).IsNotNull()
            .Because("the one-mesh render path must complete the same HDR handoff as the multi-mesh path");
        await Assert.That(gpu.HdrColor!.Rgba16Float.Span.Slice(((8 * 16) + 8) * 8, 8)
            .SequenceEqual<byte>([0x00, 0x54, 0x00, 0x00, 0x00, 0x00, 0x00, 0x3C])).IsTrue();
        await Assert.That(gpu.HdrColor.Rgba16Float.Span.SequenceEqual(cpu.HdrColor!.Rgba16Float.Span)).IsTrue();
        await Assert.That(gpu.Rgba.Span.SequenceEqual(oldGpu.Rgba.Span)).IsTrue();
        await Assert.That(gpu.Depth is not null).IsEqualTo(includeDepth);
        if (includeDepth)
        {
            await Assert.That(gpu.Depth!.Values.Span.SequenceEqual(oldGpu.Depth!.Values.Span)).IsTrue();
            await Assert.That(gpu.Depth.Values.Span[(8 * 16) + 8]).IsEqualTo(0.05f).Within(0.00001f);
        }

        SilkFrameCaptureResult Capture(RenderSettings settings) => retained
            ? SilkFrameCapture.CaptureRetainedWithHdrColor(renderer, device, 16, 16, settings, options)
            : capturer.CaptureWithHdrColor(
                session, 16, 16, settings, options, camera: SilkHdrColorCaptureFixture.Camera);
    }
}
