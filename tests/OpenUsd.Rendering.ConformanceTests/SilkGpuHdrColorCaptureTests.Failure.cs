// Copyright (c) marcschier. Licensed under the MIT License.

using OpenUsd.Rendering.Silk;

namespace OpenUsd.Rendering.ConformanceTests;

public sealed partial class SilkGpuHdrColorCaptureTests
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
    public async Task FailedFrameCannotPublishCachedHdrAndRetryRetainsItsConsumedDelta(
        SilkGraphicsBackend backend, bool retained, bool seedCachedFrame)
    {
        using var device = new SilkGpuHdrObservedDevice(SilkDepthCaptureConformance.CreateDevice(backend));
        using var fixture = new SilkHdrColorCaptureFixture();
        using OpenUsdSilkSession session = fixture.CreateSession();
        using var renderer = new SilkMeshRenderer(device);
        using var capturer = new SilkFrameCapturer(device);
        if (retained)
        {
            _ = SilkHdrColorCaptureFixture.RetainScene(device, renderer, session);
        }
        SilkFrameCaptureResult? first = seedCachedFrame ? capture(GpuSettings(), 0) : null;
        if (retained)
        {
            _ = SilkHdrColorCaptureFixture.RetainScene(device, renderer, session, timeCode: 1);
        }
        int before = device.ScenePasses;
        RenderSettings invalid = GpuSettings(config: Path.Combine(fixture.Root, "missing.ocio"));
        InvalidOperationException? refusal = null;
        try
        {
            _ = capture(invalid, 1);
        }
        catch (InvalidOperationException exception)
        {
            refusal = exception;
        }
        int after = device.Submissions;
        int scenePasses = device.ScenePasses;
        int drained = device.CompletedSubmissions;
        SilkFrameCaptureResult retry = capture(GpuSettings(), 1);
        SilkFrameCaptureResult fallback = retained
            ? SilkFrameCapture.CaptureRetained(renderer, device, 40, 32, invalid)
            : capturer.Capture(session, 40, 32, invalid, timeCode: 1, camera: SilkHdrColorCaptureFixture.Camera);

        await Assert.That(refusal).IsNotNull();
        await Assert.That(refusal!.Message).Contains("completed frame");
        await Assert.That(refusal.Message).Contains("display fallback");
        await Assert.That(scenePasses - before).IsEqualTo(1)
            .Because("refusal follows one completed render, rather than discarding its consumed delta or rerendering");
        await Assert.That(drained).IsEqualTo(after);
        await Assert.That(retry.RenderResult.DrawCount).IsEqualTo(2);
        await Assert.That(retry.HdrColor!.Rgba16Float.Span.Slice(((8 * 40) + 10) * 8, 8)
            .SequenceEqual<byte>([0x00, 0x38, 0x00, 0x40, 0x00, 0x30, 0x00, 0x3C])).IsTrue();
        await Assert.That(retry.Depth!.Values.Span[(8 * 40) + 10]).IsEqualTo(0.3f).Within(0.00001f);
        await Assert.That(fallback.HdrColor).IsNull();
        await Assert.That(fallback.RenderResult.DrawCount).IsEqualTo(2);
        if (first is not null)
        {
            await Assert.That(first.HdrColor!.Rgba16Float.Span.Slice(((8 * 40) + 10) * 8, 8)
                .SequenceEqual<byte>([0x00, 0x30, 0x00, 0x38, 0x00, 0x40, 0x00, 0x3C])).IsTrue();
            await Assert.That(first.HdrColor.Rgba16Float.Span.SequenceEqual(retry.HdrColor.Rgba16Float.Span)).IsFalse();
        }

        SilkFrameCaptureResult capture(RenderSettings settings, double time) => retained
            ? SilkFrameCapture.CaptureRetainedWithHdrColor(
                renderer, device, 40, 32, settings, new SilkHdrColorCaptureOptions(includeDeviceDepth: true))
            : capturer.CaptureWithHdrColor(
                session, 40, 32, settings, new SilkHdrColorCaptureOptions(includeDeviceDepth: true),
                timeCode: time, camera: SilkHdrColorCaptureFixture.Camera);
    }

    [Test]
    [Arguments(SilkGraphicsBackend.D3D12, false)]
    [Arguments(SilkGraphicsBackend.D3D12, true)]
    [Arguments(SilkGraphicsBackend.Vulkan, false)]
    [Arguments(SilkGraphicsBackend.Vulkan, true)]
    [Arguments(SilkGraphicsBackend.Metal, false)]
    [Arguments(SilkGraphicsBackend.Metal, true)]
    public async Task FailedGpuResourcePreparationCannotHandOffAnOldFrameOrLoseEdits(
        SilkGraphicsBackend backend, bool retained)
    {
        using var device = new SilkGpuHdrObservedDevice(SilkDepthCaptureConformance.CreateDevice(backend));
        using var fixture = new SilkHdrColorCaptureFixture();
        using OpenUsdSilkSession session = fixture.CreateSession();
        using var renderer = new SilkMeshRenderer(device);
        using var capturer = new SilkFrameCapturer(device);
        if (retained)
        {
            _ = SilkHdrColorCaptureFixture.RetainScene(device, renderer, session);
        }
        SilkFrameCaptureResult first = capture(40, 32, 0);
        if (retained)
        {
            _ = SilkHdrColorCaptureFixture.RetainScene(device, renderer, session, timeCode: 1);
        }
        device.FailNextHdrTarget = true;
        InvalidOperationException? refusal = null;
        try
        {
            _ = capture(44, 36, 1);
        }
        catch (InvalidOperationException exception)
        {
            refusal = exception;
        }
        int submitted = device.Submissions;
        int completed = device.CompletedSubmissions;
        SilkFrameCaptureResult retry = capture(44, 36, 1);

        await Assert.That(refusal!.Message).Contains("Injected HDR target preparation failure");
        await Assert.That(completed).IsEqualTo(submitted);
        await Assert.That(retry.Width).IsEqualTo(44);
        await Assert.That(retry.Height).IsEqualTo(36);
        await Assert.That(retry.HdrColor!.Rgba16Float.Length).IsEqualTo(12_672);
        await Assert.That(retry.HdrColor.Rgba16Float.Span.Slice(((8 * 44) + 10) * 8, 8)
            .SequenceEqual<byte>([0x00, 0x38, 0x00, 0x40, 0x00, 0x30, 0x00, 0x3C])).IsTrue();
        await Assert.That(first.HdrColor!.Rgba16Float.Span.Slice(((8 * 40) + 10) * 8, 8)
            .SequenceEqual<byte>([0x00, 0x30, 0x00, 0x38, 0x00, 0x40, 0x00, 0x3C])).IsTrue();

        SilkFrameCaptureResult capture(int width, int height, double time) => retained
            ? SilkFrameCapture.CaptureRetainedWithHdrColor(renderer, device, width, height, GpuSettings())
            : capturer.CaptureWithHdrColor(
                session, width, height, GpuSettings(), timeCode: time, camera: SilkHdrColorCaptureFixture.Camera);
    }
}
