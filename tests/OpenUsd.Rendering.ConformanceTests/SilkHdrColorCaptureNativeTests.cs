// Copyright (c) marcschier. Licensed under the MIT License.

using OpenUsd.Rendering.Silk;

namespace OpenUsd.Rendering.ConformanceTests;

[NotInParallel]
public sealed partial class SilkHdrColorCaptureNativeTests
{
    [Test]
    [Arguments(SilkGraphicsBackend.D3D12)]
    [Arguments(SilkGraphicsBackend.Vulkan)]
    [Arguments(SilkGraphicsBackend.Metal)]
    public async Task NativeEmissionProducesLiteralHalfPixelsBeforeDisplayAndSurvivesDisposal(
        SilkGraphicsBackend backend)
    {
        using ISilkGraphicsDevice device = SilkDepthCaptureConformance.CreateDevice(backend);
        using var fixture = new SilkHdrColorCaptureFixture();
        SilkFrameCaptureResult capture;
        SilkFrameCaptureResult legacy;
        using (OpenUsdSilkSession session = fixture.CreateSession())
        using (var capturer = new SilkFrameCapturer(device))
        {
            capture = capturer.CaptureWithHdrColor(
                session, 40, 32, RenderSettings.Default, camera: SilkHdrColorCaptureFixture.Camera);
            legacy = capturer.Capture(
                session, 40, 32, RenderSettings.Default, camera: SilkHdrColorCaptureFixture.Camera);
        }
        device.Dispose();

        await Assert.That(capture.HdrColor!.Width).IsEqualTo(40);
        await Assert.That(capture.HdrColor.Height).IsEqualTo(32);
        await Assert.That(capture.HdrColor.Rgba16Float.Length).IsEqualTo(10_240);
        await Assert.That(capture.HdrColor.Rgba16Float.Span.Slice(((8 * 40) + 10) * 8, 8)
            .SequenceEqual<byte>([0x00, 0x30, 0x00, 0x38, 0x00, 0x40, 0x00, 0x3C])).IsTrue();
        await Assert.That(capture.HdrColor.Rgba16Float.Span.Slice(((24 * 40) + 30) * 8, 8)
            .SequenceEqual<byte>([0x00, 0x44, 0x00, 0x34, 0x00, 0x2C, 0x00, 0x3C])).IsTrue();
        await Assert.That(capture.Rgba.Span.Slice(((8 * 40) + 10) * 4, 4)
            .SequenceEqual<byte>([32, 128, 255, 255])).IsTrue();
        await Assert.That(capture.RenderResult.DrawCount).IsEqualTo(2);
        await Assert.That(capture.Depth).IsNull();
        await Assert.That(legacy.HdrColor).IsNull();
        await Assert.That(legacy.Rgba.Span.SequenceEqual(capture.Rgba.Span)).IsTrue();
    }

    [Test]
    [Arguments(SilkGraphicsBackend.D3D12)]
    [Arguments(SilkGraphicsBackend.Vulkan)]
    [Arguments(SilkGraphicsBackend.Metal)]
    public async Task ExposureBuiltinTransformAndCpuOcioChangeDisplayButNeverHdrOrDepth(
        SilkGraphicsBackend backend)
    {
        using ISilkGraphicsDevice device = SilkDepthCaptureConformance.CreateDevice(backend);
        using var fixture = new SilkHdrColorCaptureFixture();
        using OpenUsdSilkSession session = fixture.CreateSession();
        using var capturer = new SilkFrameCapturer(device);
        using SilkOpenColorIoProcessor identity = SilkHdrColorCaptureFixture.CreateProcessor("IdentityView");
        using SilkOpenColorIoProcessor display = SilkHdrColorCaptureFixture.CreateProcessor("TestView");
        var options = new SilkHdrColorCaptureOptions(includeDeviceDepth: true);
        CameraState camera = SilkHdrColorCaptureFixture.Camera;
        SilkFrameCaptureResult baseline = capturer.CaptureWithHdrColor(
            session, 40, 32, RenderSettings.Default, options, camera: camera);
        SilkFrameCaptureResult exposure = capturer.CaptureWithHdrColor(
            session, 40, 32, SilkHdrColorCaptureFixture.Settings(exposure: -2), options, camera: camera);
        SilkFrameCaptureResult reinhard = capturer.CaptureWithHdrColor(
            session, 40, 32, SilkHdrColorCaptureFixture.Settings(outputTransform: RenderOutputTransform.Reinhard),
            options, camera: camera);
        SilkFrameCaptureResult ocioIdentity = capturer.CaptureWithHdrColor(
            session, 40, 32, RenderSettings.Default, identity, options, camera: camera);
        SilkFrameCaptureResult ocioDisplay = capturer.CaptureWithHdrColor(
            session, 40, 32, RenderSettings.Default, display, options, camera: camera);
        SilkFrameCaptureResult ocioExposure = capturer.CaptureWithHdrColor(
            session, 40, 32, SilkHdrColorCaptureFixture.Settings(exposure: -2), identity, options, camera: camera);
        SilkFrameCaptureResult legacyOcio = capturer.Capture(
            session, 40, 32, RenderSettings.Default, display, camera: camera);

        await Assert.That(exposure.Rgba.Span.Slice(((8 * 40) + 10) * 4, 4)
            .SequenceEqual<byte>([8, 32, 128, 255])).IsTrue();
        await Assert.That(reinhard.Rgba.Span.Slice(((8 * 40) + 10) * 4, 4)
            .SequenceEqual<byte>([28, 85, 170, 255])).IsTrue();
        await Assert.That(ocioIdentity.Rgba.Span.Slice(((8 * 40) + 10) * 4, 4)
            .SequenceEqual<byte>([32, 128, 255, 255])).IsTrue();
        await Assert.That(ocioExposure.Rgba.Span.SequenceEqual(exposure.Rgba.Span)).IsTrue();
        await Assert.That(ocioDisplay.Rgba.Span.SequenceEqual(baseline.Rgba.Span)).IsFalse();
        await Assert.That(legacyOcio.Rgba.Span.SequenceEqual(ocioDisplay.Rgba.Span)).IsTrue();
        await Assert.That(legacyOcio.HdrColor).IsNull();
        foreach (SilkFrameCaptureResult changed in new[]
        {
            exposure, reinhard, ocioIdentity, ocioDisplay, ocioExposure
        })
        {
            await Assert.That(changed.HdrColor!.Rgba16Float.Span.SequenceEqual(
                baseline.HdrColor!.Rgba16Float.Span)).IsTrue();
            await Assert.That(changed.Depth!.Values.Span.SequenceEqual(baseline.Depth!.Values.Span)).IsTrue();
        }
        await Assert.That(baseline.Depth!.Values.Span[(8 * 40) + 10])
            .IsEqualTo(0.2f).Within(0.00001f);
        await Assert.That(baseline.Depth.Values.Span[(24 * 40) + 30])
            .IsEqualTo(0.6f).Within(0.00001f);
    }

    [Test]
    [Arguments(SilkGraphicsBackend.Vulkan, false)]
    [Arguments(SilkGraphicsBackend.Vulkan, true)]
    [Arguments(SilkGraphicsBackend.Metal, false)]
    [Arguments(SilkGraphicsBackend.Metal, true)]
    public async Task HalfOverflowIsRefusedBeforeCpuDisplayCanHideIt(
        SilkGraphicsBackend backend, bool cpuOcio)
    {
        using ISilkGraphicsDevice device = SilkDepthCaptureConformance.CreateDevice(backend);
        using var fixture = new SilkHdrColorCaptureFixture(emissionA: "(70000, 0.5, 2)");
        using OpenUsdSilkSession session = fixture.CreateSession();
        using var capturer = new SilkFrameCapturer(device);
        using SilkOpenColorIoProcessor processor = SilkHdrColorCaptureFixture.CreateProcessor("TestView");
        InvalidDataException? refusal = null;
        try
        {
            _ = cpuOcio
                ? capturer.CaptureWithHdrColor(
                    session, 40, 32, RenderSettings.Default, processor,
                    camera: SilkHdrColorCaptureFixture.Camera)
                : capturer.CaptureWithHdrColor(
                    session, 40, 32, RenderSettings.Default, camera: SilkHdrColorCaptureFixture.Camera);
        }
        catch (InvalidDataException exception)
        {
            refusal = exception;
        }
        SilkFrameCaptureResult repaired = capturer.CaptureWithHdrColor(
            session, 40, 32, RenderSettings.Default, timeCode: 1, camera: SilkHdrColorCaptureFixture.Camera);

        await Assert.That(refusal).IsNotNull()
            .Because("finite shader floats can overflow binary16; clamping RGBA8 must not conceal invalid HDR");
        await Assert.That(repaired.RenderResult.DrawCount).IsEqualTo(2);
        await Assert.That(repaired.HdrColor!.Rgba16Float.Span.Slice(((8 * 40) + 10) * 8, 8)
            .SequenceEqual<byte>([0x00, 0x38, 0x00, 0x40, 0x00, 0x30, 0x00, 0x3C])).IsTrue();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task WarpAttachmentSaturationIsReportedAsStoredNotMisidentifiedAsRecoverableOverflow(
        bool cpuOcio)
    {
        using ISilkGraphicsDevice device = SilkDepthCaptureConformance.CreateDevice(SilkGraphicsBackend.D3D12);
        using var fixture = new SilkHdrColorCaptureFixture(emissionA: "(70000, 0.5, 2)");
        using OpenUsdSilkSession session = fixture.CreateSession();
        using var renderer = new SilkMeshRenderer(device);
        using SilkOpenColorIoProcessor processor = SilkHdrColorCaptureFixture.CreateProcessor("TestView");
        using ISilkGraphicsTexture color = device.CreateTexture2D(SilkTextureDescriptor.HdrColorTarget(40, 32));
        using ISilkGraphicsTexture depth = device.CreateTexture2D(SilkTextureDescriptor.SampledDepthTarget(40, 32));
        using OpenUsdSilkPage page = session.Sync(40, 32, camera: SilkHdrColorCaptureFixture.Camera);
        _ = renderer.ApplyAndRender(page, color, depth);
        var attachment = new byte[10_240];
        color.ReadbackForTesting(attachment);
        SilkFrameCaptureResult capture = cpuOcio
            ? SilkFrameCapture.CaptureRetainedWithHdrColor(
                renderer, device, 40, 32, RenderSettings.Default, processor)
            : SilkFrameCapture.CaptureRetainedWithHdrColor(renderer, device, 40, 32, RenderSettings.Default);

        await Assert.That(attachment.AsSpan(((8 * 40) + 10) * 8, 8)
            .SequenceEqual<byte>([0xFF, 0x7B, 0x00, 0x38, 0x00, 0x40, 0x00, 0x3C])).IsTrue()
            .Because("WARP stores 65504 before capture; the capture must not pretend to recover shader intermediates");
        await Assert.That(capture.HdrColor!.Rgba16Float.Span.SequenceEqual(attachment)).IsTrue();
    }
}
