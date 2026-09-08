// Copyright (c) marcschier. Licensed under the MIT License.

using OpenUsd.Rendering.Silk;

namespace OpenUsd.Rendering.ConformanceTests;

public sealed partial class SilkGpuHdrColorCaptureTests
{
    [Test]
    [Arguments(SilkGraphicsBackend.D3D12)]
    [Arguments(SilkGraphicsBackend.Vulkan)]
    [Arguments(SilkGraphicsBackend.Metal)]
    public async Task ExposureViewLookAndSelectionAffectOnlyGpuDisplayNotHdrOrDepth(SilkGraphicsBackend backend)
    {
        using var device = new SilkGpuHdrObservedDevice(SilkDepthCaptureConformance.CreateDevice(backend));
        using var fixture = new SilkHdrColorCaptureFixture();
        using OpenUsdSilkSession session = fixture.CreateSession();
        using var renderer = new SilkMeshRenderer(device);
        _ = SilkHdrColorCaptureFixture.RetainScene(device, renderer, session);
        var options = new SilkHdrColorCaptureOptions(includeDeviceDepth: true);
        SilkFrameCaptureResult baseline = SilkFrameCapture.CaptureRetainedWithHdrColor(
            renderer, device, 40, 32, GpuSettings(), options);
        foreach (RenderSettings settings in new[]
        {
            GpuSettings(exposure: -2), GpuSettings(view: "IdentityView"), GpuSettings(look: "TestLook")
        })
        {
            int scenePasses = device.ScenePasses;
            SilkFrameCaptureResult changed = SilkFrameCapture.CaptureRetainedWithHdrColor(
                renderer, device, 40, 32, settings, options);
            int capturedPasses = device.ScenePasses - scenePasses;
            SilkFrameCaptureResult legacy = SilkFrameCapture.CaptureRetainedWithDepth(
                renderer, device, 40, 32, settings);
            await Assert.That(capturedPasses).IsEqualTo(1);
            await Assert.That(changed.Rgba.Span.SequenceEqual(legacy.Rgba.Span)).IsTrue();
            await Assert.That(changed.Rgba.Span.SequenceEqual(baseline.Rgba.Span)).IsFalse();
            await Assert.That(changed.HdrColor!.Rgba16Float.Span.SequenceEqual(baseline.HdrColor!.Rgba16Float.Span))
                .IsTrue();
            await Assert.That(changed.Depth!.Values.Span.SequenceEqual(baseline.Depth!.Values.Span)).IsTrue();
            await Assert.That(changed.Depth.Values.Span.SequenceEqual(legacy.Depth!.Values.Span)).IsTrue();
        }
        renderer.UpdateSelection(new SelectionState(["/World/A"]));
        SilkFrameCaptureResult selected = SilkFrameCapture.CaptureRetainedWithHdrColor(
            renderer, device, 40, 32, GpuSettings(), options);
        SilkFrameCaptureResult selectedLegacy = SilkFrameCapture.CaptureRetained(
            renderer, device, 40, 32, GpuSettings());
        await Assert.That(renderer.SelectionOutlineDiagnostics.Status).IsEqualTo(SilkSelectionOutlineStatus.Rendered);
        await Assert.That(selected.Rgba.Span.SequenceEqual(baseline.Rgba.Span)).IsFalse();
        await Assert.That(selected.Rgba.Span.SequenceEqual(selectedLegacy.Rgba.Span)).IsTrue();
        await Assert.That(selected.HdrColor!.Rgba16Float.Span.SequenceEqual(baseline.HdrColor!.Rgba16Float.Span))
            .IsTrue();
        await Assert.That(selected.Depth!.Values.Span.SequenceEqual(baseline.Depth!.Values.Span)).IsTrue();
    }

    [Test]
    [Arguments(SilkGraphicsBackend.D3D12, false)]
    [Arguments(SilkGraphicsBackend.D3D12, true)]
    [Arguments(SilkGraphicsBackend.Vulkan, false)]
    [Arguments(SilkGraphicsBackend.Vulkan, true)]
    [Arguments(SilkGraphicsBackend.Metal, false)]
    [Arguments(SilkGraphicsBackend.Metal, true)]
    public async Task ExactGpuQuotaPrecedesTargetsAndSyncEvenWithSelectionAndOptionalDepth(
        SilkGraphicsBackend backend, bool includeDepth)
    {
        using var device = new SilkGpuHdrObservedDevice(SilkDepthCaptureConformance.CreateDevice(backend));
        using var fixture = new SilkHdrColorCaptureFixture();
        using OpenUsdSilkSession retainedSession = fixture.CreateSession();
        using var renderer = new SilkMeshRenderer(device);
        _ = SilkHdrColorCaptureFixture.RetainScene(device, renderer, retainedSession);
        renderer.UpdateSelection(new SelectionState(["/World/A"]));
        using OpenUsdSilkSession session = fixture.CreateSession();
        using var capturer = new SilkFrameCapturer(device);
        var tooSmall = new SilkHdrColorCaptureOptions(includeDepth, maximumReadbackBytes: 25_599);
        var exact = new SilkHdrColorCaptureOptions(includeDepth, maximumReadbackBytes: 25_600);
        int before = device.TextureRequests;
        await Assert.That(() => SilkFrameCapture.CaptureRetainedWithHdrColor(
            renderer, device, 40, 32, GpuSettings(), tooSmall)).Throws<ArgumentOutOfRangeException>();
        await Assert.That(() => capturer.CaptureWithHdrColor(
            session, 40, 32, GpuSettings(), tooSmall, camera: SilkHdrColorCaptureFixture.Camera))
            .Throws<ArgumentOutOfRangeException>();
        await Assert.That(device.TextureRequests).IsEqualTo(before);
        SilkFrameCaptureResult selected = SilkFrameCapture.CaptureRetainedWithHdrColor(
            renderer, device, 40, 32, GpuSettings(), exact);
        using var fresh = new SilkFrameCapturer(device);
        SilkFrameCaptureResult initial = fresh.CaptureWithHdrColor(
            session, 40, 32, GpuSettings(), exact, camera: SilkHdrColorCaptureFixture.Camera);

        await Assert.That(initial.RenderResult.DrawCount).IsEqualTo(2);
        await Assert.That(initial.HdrColor!.Rgba16Float.Length).IsEqualTo(10_240);
        await Assert.That(selected.HdrColor!.Rgba16Float.Span.SequenceEqual(initial.HdrColor.Rgba16Float.Span)).IsTrue();
        await Assert.That(selected.Rgba.Span.SequenceEqual(initial.Rgba.Span)).IsFalse();
        await Assert.That(selected.Depth is not null).IsEqualTo(includeDepth);
        await Assert.That(initial.Depth is not null).IsEqualTo(includeDepth);
    }

    [Test]
    [Arguments(SilkGraphicsBackend.D3D12, false)]
    [Arguments(SilkGraphicsBackend.D3D12, true)]
    [Arguments(SilkGraphicsBackend.Vulkan, false)]
    [Arguments(SilkGraphicsBackend.Vulkan, true)]
    [Arguments(SilkGraphicsBackend.Metal, false)]
    [Arguments(SilkGraphicsBackend.Metal, true)]
    public async Task WarmGpuCaptureDoesNotAllocateCopiedHalfOrExpandedFloatPixels(
        SilkGraphicsBackend backend, bool includeDepth)
    {
        using var device = new SilkGpuHdrObservedDevice(SilkDepthCaptureConformance.CreateDevice(backend));
        using var fixture = new SilkHdrColorCaptureFixture();
        using OpenUsdSilkSession session = fixture.CreateSession();
        using var renderer = new SilkMeshRenderer(device);
        _ = SilkHdrColorCaptureFixture.RetainScene(device, renderer, session);
        renderer.UpdateSelection(new SelectionState(["/World/A"]));
        var options = new SilkHdrColorCaptureOptions(includeDepth);
        RenderSettings settings = GpuSettings();
        _ = SilkFrameCapture.CaptureRetainedWithHdrColor(renderer, device, 256, 128, settings, options);
        _ = SilkFrameCapture.CaptureRetainedWithHdrColor(renderer, device, 256, 128, settings, options);
        int passes = device.ScenePasses;
        int textures = device.TextureRequests;
        long before = GC.GetAllocatedBytesForCurrentThread();
        SilkFrameCaptureResult result = SilkFrameCapture.CaptureRetainedWithHdrColor(
            renderer, device, 256, 128, settings, options);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        await Assert.That(allocated).IsLessThan(includeDepth ? 655_360L : 524_288L);
        await Assert.That(device.ScenePasses - passes).IsEqualTo(1);
        await Assert.That(device.TextureRequests - textures).IsEqualTo(2)
            .Because("a warm capture allocates only its display/depth targets, not another HDR target");
        await Assert.That(result.HdrColor!.Rgba16Float.Length).IsEqualTo(262_144);
        await Assert.That(result.Rgba.Length).IsEqualTo(131_072);
        await Assert.That(result.Depth is not null).IsEqualTo(includeDepth);
    }
}
