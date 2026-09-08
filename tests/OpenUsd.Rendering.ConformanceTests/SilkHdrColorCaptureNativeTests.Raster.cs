// Copyright (c) marcschier. Licensed under the MIT License.

using System.Numerics;
using OpenUsd.Render;
using OpenUsd.Rendering.Silk;

namespace OpenUsd.Rendering.ConformanceTests;

public sealed partial class SilkHdrColorCaptureNativeTests
{
    [Test]
    [Arguments(SilkGraphicsBackend.D3D12)]
    [Arguments(SilkGraphicsBackend.Vulkan)]
    [Arguments(SilkGraphicsBackend.Metal)]
    public async Task TransparentEmissionReturnsStoredBlendedAlphaNotUnassociatedColor(SilkGraphicsBackend backend)
    {
        using ISilkGraphicsDevice device = SilkDepthCaptureConformance.CreateDevice(backend);
        using var fixture = new SilkHdrColorCaptureFixture("(2, 0.5, 0.125)", transparent: true);
        using OpenUsdSilkSession session = fixture.CreateSession();
        using var capturer = new SilkFrameCapturer(device);
        SilkFrameCaptureResult capture = capturer.CaptureWithHdrColor(
            session, 40, 32, SilkHdrColorCaptureFixture.Settings(clearColor: Vector4.Zero),
            new SilkHdrColorCaptureOptions(includeDeviceDepth: true), camera: SilkHdrColorCaptureFixture.Camera);

        await Assert.That(capture.HdrColor!.Rgba16Float.Span.Slice(((8 * 40) + 10) * 8, 8)
            .SequenceEqual<byte>([0x00, 0x3C, 0x00, 0x34, 0x00, 0x2C, 0x00, 0x38])).IsTrue()
            .Because("emission (2, .5, .125) at opacity .5 over transparent black stores (1, .25, .0625, .5)");
        await Assert.That(capture.Rgba.Span.Slice(((8 * 40) + 10) * 4, 4)
            .SequenceEqual<byte>([255, 64, 16, 128])).IsTrue();
        await Assert.That(capture.Depth!.Values.Span[(8 * 40) + 10]).IsEqualTo(1f)
            .Because("the transparent pipeline disables depth writes");
    }

    [Test]
    [Arguments(SilkGraphicsBackend.D3D12)]
    [Arguments(SilkGraphicsBackend.Vulkan)]
    [Arguments(SilkGraphicsBackend.Metal)]
    public async Task ClearHdrRemainsExactAndUnexposed(SilkGraphicsBackend backend)
    {
        using ISilkGraphicsDevice device = SilkDepthCaptureConformance.CreateDevice(backend);
        using var fixture = new SilkHdrColorCaptureFixture();
        using OpenUsdSilkSession session = fixture.CreateSession();
        using var capturer = new SilkFrameCapturer(device);
        var clear = new Vector4(0.03125f, 0.0625f, 0.125f, 0.25f);
        SilkFrameCaptureResult first = capturer.CaptureWithHdrColor(
            session, 40, 32, SilkHdrColorCaptureFixture.Settings(clearColor: clear),
            camera: SilkHdrColorCaptureFixture.Camera);
        SilkFrameCaptureResult exposed = capturer.CaptureWithHdrColor(
            session, 40, 32, SilkHdrColorCaptureFixture.Settings(exposure: -2, clearColor: clear),
            camera: SilkHdrColorCaptureFixture.Camera);

        await Assert.That(first.HdrColor!.Rgba16Float.Span.Slice(((24 * 40) + 10) * 8, 8)
            .SequenceEqual<byte>([0x00, 0x28, 0x00, 0x2C, 0x00, 0x30, 0x00, 0x34])).IsTrue();
        await Assert.That(first.HdrColor.Rgba16Float.Span.SequenceEqual(exposed.HdrColor!.Rgba16Float.Span)).IsTrue();
        await Assert.That(first.Rgba.Span.Slice(((24 * 40) + 10) * 4, 4)
            .SequenceEqual<byte>([8, 16, 32, 64])).IsTrue();
        await Assert.That(exposed.Rgba.Span.Slice(((24 * 40) + 10) * 4, 4)
            .SequenceEqual<byte>([2, 4, 8, 64])).IsTrue();
    }

    [Test]
    [Arguments(SilkGraphicsBackend.D3D12)]
    [Arguments(SilkGraphicsBackend.Vulkan)]
    [Arguments(SilkGraphicsBackend.Metal)]
    public async Task AuthoredCropReturnsTopDownLiteralHdrSubrectangleNotResizeOrVerticalFlip(
        SilkGraphicsBackend backend)
    {
        using ISilkGraphicsDevice device = SilkDepthCaptureConformance.CreateDevice(backend);
        using var fixture = new SilkHdrColorCaptureFixture();
        using OpenUsdSilkSession session = fixture.CreateSession();
        byte[] originalScene = File.ReadAllBytes(fixture.ScenePath);
        RenderPreparedFrame full;
        RenderPreparedFrame crop;
        using (UsdStage stage = UsdStage.Open(fixture.ScenePath))
        {
            UsdRenderSpecification specification = stage.GetRenderSpecification()
                ?? throw new InvalidOperationException("The HDR fixture has no authored render specification.");
            full = new RenderProductRequest(specification, 0, new RenderProductOverrides(
                dataWindowNdc: new UsdVec4f(0, 0, 1, 1))).PrepareFrame(stage, 0);
            crop = new RenderProductRequest(specification, 0).PrepareFrame(stage, 0);
        }
        using var capturer = new SilkFrameCapturer(device);
        var options = new SilkHdrColorCaptureOptions(includeDeviceDepth: true);
        SilkFrameCaptureResult fullImage = capturer.CaptureWithHdrColor(
            session, 96, 96, RenderSettings.Default, options, camera: full.Camera);
        SilkFrameCaptureResult cropped = capturer.CaptureWithHdrColor(
            session, 60, 36, RenderSettings.Default, options, camera: crop.Camera);
        SilkFrameCaptureResult resized = capturer.CaptureWithHdrColor(
            session, 60, 36, RenderSettings.Default, options, camera: full.Camera);

        await Assert.That(crop.OutputDimensions).IsEqualTo(new ViewportDimensions(60, 36));
        await Assert.That(crop.DataWindowMinX).IsEqualTo(12);
        await Assert.That(crop.DataWindowMinY).IsEqualTo(24);
        for (int row = 0; row < 36; row++)
        {
            await Assert.That(cropped.HdrColor!.Rgba16Float.Span.Slice(row * 60 * 8, 60 * 8)
                .SequenceEqual(fullImage.HdrColor!.Rgba16Float.Span.Slice(
                    (((36 + row) * 96) + 12) * 8, 60 * 8))).IsTrue();
            await Assert.That(cropped.Rgba.Span.Slice(row * 60 * 4, 60 * 4)
                .SequenceEqual(fullImage.Rgba.Span.Slice((((36 + row) * 96) + 12) * 4, 60 * 4))).IsTrue();
            for (int column = 0; column < 60; column++)
            {
                await Assert.That(cropped.Depth!.Values.Span[(row * 60) + column])
                    .IsEqualTo(fullImage.Depth!.Values.Span[((36 + row) * 96) + 12 + column]).Within(0.00001f);
            }
        }
        await Assert.That(cropped.HdrColor!.Rgba16Float.Span.Slice(((4 * 60) + 12) * 8, 8)
            .SequenceEqual<byte>([0x00, 0x30, 0x00, 0x38, 0x00, 0x40, 0x00, 0x3C])).IsTrue();
        await Assert.That(cropped.HdrColor.Rgba16Float.Span.Slice(((24 * 60) + 48) * 8, 8)
            .SequenceEqual<byte>([0x00, 0x44, 0x00, 0x34, 0x00, 0x2C, 0x00, 0x3C])).IsTrue();
        await Assert.That(cropped.HdrColor.Rgba16Float.Span.Slice(((31 * 60) + 12) * 8, 8)
            .SequenceEqual<byte>([0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x3C])).IsTrue()
            .Because("a vertical flip would move A from row four to row thirty-one");
        await Assert.That(cropped.HdrColor.Rgba16Float.Span.SequenceEqual(resized.HdrColor!.Rgba16Float.Span))
            .IsFalse();
        await Assert.That(File.ReadAllBytes(fixture.ScenePath).SequenceEqual(originalScene)).IsTrue();
        await Assert.That(File.Exists(Path.Combine(fixture.Root, "not-written.exr"))).IsFalse();
    }
}
