// Copyright (c) marcschier. Licensed under the MIT License.

using OpenUsd.Rendering.Silk;

namespace OpenUsd.Rendering.ConformanceTests;

[NotInParallel]
public sealed class SilkHdrColorCaptureConformanceTests
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
    public async Task SelectedHdrUsesExactQuotaBeforeTargetsOrNativeSyncAndPreservesHdrAndDepth(
        SilkGraphicsBackend backend, bool cpuOcio, bool includeDeviceDepth)
    {
        using var device = new SilkDepthCaptureObservedDevice(
            SilkDepthCaptureConformance.CreateDevice(backend));
        using var fixture = new SilkHdrColorCaptureFixture();
        using OpenUsdSilkSession retainedSession = fixture.CreateSession();
        using var renderer = new SilkMeshRenderer(device);
        using SilkOpenColorIoProcessor processor = SilkHdrColorCaptureFixture.CreateProcessor("TestView");
        ulong revision = SilkHdrColorCaptureFixture.RetainScene(device, renderer, retainedSession);
        var exact = new SilkHdrColorCaptureOptions(includeDeviceDepth, maximumReadbackBytes: 25_600);
        var insufficient = new SilkHdrColorCaptureOptions(includeDeviceDepth, maximumReadbackBytes: 25_599);
        SilkFrameCaptureResult plain = cpuOcio
            ? SilkFrameCapture.CaptureRetainedWithHdrColor(
                renderer, device, 40, 32, RenderSettings.Default, processor, exact)
            : SilkFrameCapture.CaptureRetainedWithHdrColor(
                renderer, device, 40, 32, RenderSettings.Default, exact);
        renderer.UpdateSelection(new SelectionState(["/World/A"]));

        int before = device.TextureRequests;
        bool retainedRefused = false;
        try
        {
            _ = cpuOcio
                ? SilkFrameCapture.CaptureRetainedWithHdrColor(
                    renderer, device, 40, 32, RenderSettings.Default, processor, insufficient)
                : SilkFrameCapture.CaptureRetainedWithHdrColor(
                    renderer, device, 40, 32, RenderSettings.Default, insufficient);
        }
        catch (ArgumentOutOfRangeException)
        {
            retainedRefused = true;
        }
        int after = device.TextureRequests;
        SilkFrameCaptureResult selected = cpuOcio
            ? SilkFrameCapture.CaptureRetainedWithHdrColor(
                renderer, device, 40, 32, RenderSettings.Default, processor, exact, revision)
            : SilkFrameCapture.CaptureRetainedWithHdrColor(
                renderer, device, 40, 32, RenderSettings.Default, exact, revision);
        SilkFrameCaptureResult legacy = cpuOcio
            ? SilkFrameCapture.CaptureRetained(renderer, device, 40, 32, RenderSettings.Default, processor)
            : SilkFrameCapture.CaptureRetained(renderer, device, 40, 32, RenderSettings.Default);
        SilkSelectionOutlineStatus selectionStatus = renderer.SelectionOutlineDiagnostics.Status;

        using OpenUsdSilkSession session = fixture.CreateSession();
        using var capturer = new SilkFrameCapturer(device);
        int beforeSync = device.TextureRequests;
        bool syncRefused = false;
        try
        {
            _ = cpuOcio
                ? capturer.CaptureWithHdrColor(
                    session, 40, 32, RenderSettings.Default, processor, insufficient,
                    camera: SilkHdrColorCaptureFixture.Camera)
                : capturer.CaptureWithHdrColor(
                    session, 40, 32, RenderSettings.Default, insufficient,
                    camera: SilkHdrColorCaptureFixture.Camera);
        }
        catch (ArgumentOutOfRangeException)
        {
            syncRefused = true;
        }
        int afterSync = device.TextureRequests;
        using var freshCapturer = new SilkFrameCapturer(device);
        SilkFrameCaptureResult initialDelta = cpuOcio
            ? freshCapturer.CaptureWithHdrColor(
                session, 40, 32, RenderSettings.Default, processor, exact,
                camera: SilkHdrColorCaptureFixture.Camera)
            : freshCapturer.CaptureWithHdrColor(
                session, 40, 32, RenderSettings.Default, exact, camera: SilkHdrColorCaptureFixture.Camera);

        await Assert.That(retainedRefused).IsTrue();
        await Assert.That(after).IsEqualTo(before);
        await Assert.That(syncRefused).IsTrue();
        await Assert.That(afterSync).IsEqualTo(beforeSync);
        await Assert.That(initialDelta.RenderResult.DrawCount).IsEqualTo(2)
            .Because("a fresh capturer must still receive the untouched initial native delta");
        await Assert.That(selectionStatus).IsEqualTo(SilkSelectionOutlineStatus.Rendered);
        await Assert.That(selected.Rgba.Span.SequenceEqual(plain.Rgba.Span)).IsFalse();
        await Assert.That(selected.Rgba.Span.SequenceEqual(legacy.Rgba.Span)).IsTrue();
        await Assert.That(legacy.HdrColor).IsNull();
        await Assert.That(selected.HdrColor!.Rgba16Float.Span.SequenceEqual(
            plain.HdrColor!.Rgba16Float.Span)).IsTrue();
        await Assert.That(selected.HdrColor.Rgba16Float.Span.Slice(((8 * 40) + 10) * 8, 8)
            .SequenceEqual<byte>([0x00, 0x30, 0x00, 0x38, 0x00, 0x40, 0x00, 0x3C])).IsTrue();
        await Assert.That(selected.PageRevision).IsEqualTo(revision);
        await Assert.That(selected.CommandCount).IsEqualTo(0u);
        if (includeDeviceDepth)
        {
            await Assert.That(selected.Depth!.Values.Span.SequenceEqual(plain.Depth!.Values.Span)).IsTrue();
            await Assert.That(selected.Depth.Values.Span[(8 * 40) + 10])
                .IsEqualTo(0.2f).Within(0.00001f);
        }
        else
        {
            await Assert.That(selected.Depth).IsNull();
        }
    }
}
