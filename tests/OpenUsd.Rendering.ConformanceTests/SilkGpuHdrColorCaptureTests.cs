// Copyright (c) marcschier. Licensed under the MIT License.

using OpenUsd.Rendering.Silk;

namespace OpenUsd.Rendering.ConformanceTests;

[NotInParallel]
public sealed partial class SilkGpuHdrColorCaptureTests
{
    [Test]
    [Arguments(SilkGraphicsBackend.D3D12, false)]
    [Arguments(SilkGraphicsBackend.D3D12, true)]
    [Arguments(SilkGraphicsBackend.Vulkan, false)]
    [Arguments(SilkGraphicsBackend.Vulkan, true)]
    [Arguments(SilkGraphicsBackend.Metal, false)]
    [Arguments(SilkGraphicsBackend.Metal, true)]
    public async Task CompletedGpuDisplayFrameReturnsActualLiteralHdrAndUnchangedDisplay(
        SilkGraphicsBackend backend, bool retained)
    {
        using ISilkGraphicsDevice device = SilkDepthCaptureConformance.CreateDevice(backend);
        using var fixture = new SilkHdrColorCaptureFixture();
        using OpenUsdSilkSession session = fixture.CreateSession();
        using var renderer = new SilkMeshRenderer(device);
        using var capturer = new SilkFrameCapturer(device);
        if (retained)
        {
            _ = SilkHdrColorCaptureFixture.RetainScene(device, renderer, session);
        }
        var options = new SilkHdrColorCaptureOptions(includeDeviceDepth: true);
        SilkFrameCaptureResult cpu = Capture(RenderSettings.Default);
        RenderSettings gpuSettings = GpuSettings(exposure: -2);
        SilkFrameCaptureResult gpu = Capture(gpuSettings);
        SilkFrameCaptureResult legacy = retained
            ? SilkFrameCapture.CaptureRetained(renderer, device, 40, 32, gpuSettings)
            : capturer.Capture(session, 40, 32, gpuSettings, camera: SilkHdrColorCaptureFixture.Camera);

        await Assert.That(gpu.HdrColor!.Rgba16Float.Span.Slice(((8 * 40) + 10) * 8, 8)
            .SequenceEqual<byte>([0x00, 0x30, 0x00, 0x38, 0x00, 0x40, 0x00, 0x3C])).IsTrue();
        await Assert.That(gpu.HdrColor.Rgba16Float.Span.Slice(((24 * 40) + 30) * 8, 8)
            .SequenceEqual<byte>([0x00, 0x44, 0x00, 0x34, 0x00, 0x2C, 0x00, 0x3C])).IsTrue();
        await Assert.That(gpu.HdrColor.Rgba16Float.Span.SequenceEqual(cpu.HdrColor!.Rgba16Float.Span)).IsTrue();
        await Assert.That(gpu.Depth!.Values.Span.SequenceEqual(cpu.Depth!.Values.Span)).IsTrue();
        await Assert.That(gpu.Rgba.Span.SequenceEqual(legacy.Rgba.Span)).IsTrue();
        await Assert.That(gpu.Rgba.Span.SequenceEqual(cpu.Rgba.Span)).IsFalse();
        await Assert.That(gpu.RenderResult.DrawCount).IsEqualTo(2);
        await Assert.That(legacy.HdrColor).IsNull();

        SilkFrameCaptureResult Capture(RenderSettings settings) => retained
            ? SilkFrameCapture.CaptureRetainedWithHdrColor(renderer, device, 40, 32, settings, options)
            : capturer.CaptureWithHdrColor(
                session, 40, 32, settings, options, camera: SilkHdrColorCaptureFixture.Camera);
    }

    private static RenderSettings GpuSettings(
        float exposure = 0, string view = "TestView", string? look = null, string? config = null) =>
        SilkHdrColorCaptureFixture.Settings(exposure) with
        {
            DisplayTransform = new RenderDisplayTransform(
                config ?? SilkHdrColorCaptureFixture.OcioConfigPath, "linear", "TestDisplay", view, look)
        };
}
