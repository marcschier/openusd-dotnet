// Copyright (c) marcschier. Licensed under the MIT License.

using System.Numerics;
using OpenUsd.Rendering.Silk;

namespace OpenUsd.Rendering.ConformanceTests;

public sealed partial class SilkGpuHdrColorCaptureTests
{
    [Test]
    [Arguments(SilkGraphicsBackend.D3D12)]
    [Arguments(SilkGraphicsBackend.Vulkan)]
    [Arguments(SilkGraphicsBackend.Metal)]
    public async Task GpuHdrKeepsUnexposedClearAndStoredTransparentFramebufferAlpha(SilkGraphicsBackend backend)
    {
        using ISilkGraphicsDevice device = SilkDepthCaptureConformance.CreateDevice(backend);
        using var fixture = new SilkHdrColorCaptureFixture("(2, 0.5, 0.125)", transparent: true);
        using OpenUsdSilkSession session = fixture.CreateSession();
        using var capturer = new SilkFrameCapturer(device);
        var options = new SilkHdrColorCaptureOptions(includeDeviceDepth: true);
        RenderSettings transparent = SilkHdrColorCaptureFixture.Settings(exposure: -2, clearColor: Vector4.Zero) with
        {
            DisplayTransform = GpuSettings().DisplayTransform
        };
        SilkFrameCaptureResult image = capturer.CaptureWithHdrColor(
            session, 40, 32, transparent, options, camera: SilkHdrColorCaptureFixture.Camera);
        SilkFrameCaptureResult legacy = capturer.Capture(
            session, 40, 32, transparent, camera: SilkHdrColorCaptureFixture.Camera);
        RenderSettings clear = SilkHdrColorCaptureFixture.Settings(
            exposure: -2, clearColor: new Vector4(0.03125f, 0.0625f, 0.125f, 0.25f)) with
        {
            DisplayTransform = GpuSettings().DisplayTransform
        };
        SilkFrameCaptureResult background = capturer.CaptureWithHdrColor(
            session, 40, 32, clear, camera: SilkHdrColorCaptureFixture.Camera);

        await Assert.That(image.HdrColor!.Rgba16Float.Span.Slice(((8 * 40) + 10) * 8, 8)
            .SequenceEqual<byte>([0x00, 0x3C, 0x00, 0x34, 0x00, 0x2C, 0x00, 0x38])).IsTrue();
        await Assert.That(image.Depth!.Values.Span[(8 * 40) + 10]).IsEqualTo(1f);
        await Assert.That(image.Rgba.Span.SequenceEqual(legacy.Rgba.Span)).IsTrue();
        await Assert.That(background.HdrColor!.Rgba16Float.Span.Slice(((24 * 40) + 10) * 8, 8)
            .SequenceEqual<byte>([0x00, 0x28, 0x00, 0x2C, 0x00, 0x30, 0x00, 0x34])).IsTrue();
    }
}
