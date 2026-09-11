// Copyright (c) marcschier. Licensed under the MIT License.

using OpenUsd.Rendering.Silk;

namespace OpenUsd.Rendering.ConformanceTests;

public sealed partial class SilkGpuHdrColorCaptureTests
{
    [Test]
    [Arguments(SilkGraphicsBackend.D3D12)]
    [Arguments(SilkGraphicsBackend.Vulkan)]
    [Arguments(SilkGraphicsBackend.Metal)]
    public async Task InvalidGpuClearDoesNotAllocateOrConsumeTheInitialScene(SilkGraphicsBackend backend)
    {
        using var device = new SilkGpuHdrObservedDevice(SilkDepthCaptureConformance.CreateDevice(backend));
        using var fixture = new SilkHdrColorCaptureFixture();
        using OpenUsdSilkSession session = fixture.CreateSession();
        using var capturer = new SilkFrameCapturer(device);
        using var renderer = new SilkMeshRenderer(device);
        RenderSettings invalid = SilkHdrColorCaptureFixture.Settings(
            clearColor: new System.Numerics.Vector4(float.NaN, 0, 0, 1)) with
        {
            DisplayTransform = GpuSettings().DisplayTransform
        };
        await Assert.That(() => SilkFrameCapture.CaptureRetainedWithHdrColor(
            renderer, device, 40, 32, invalid)).Throws<ArgumentOutOfRangeException>();
        await Assert.That(() => capturer.CaptureWithHdrColor(
            session, 40, 32, invalid, camera: SilkHdrColorCaptureFixture.Camera)).Throws<ArgumentOutOfRangeException>();
        await Assert.That(device.TextureRequests).IsEqualTo(0);
        using var fresh = new SilkFrameCapturer(device);
        SilkFrameCaptureResult untouched = fresh.CaptureWithHdrColor(
            session, 40, 32, GpuSettings(), camera: SilkHdrColorCaptureFixture.Camera);
        await Assert.That(untouched.RenderResult.DrawCount).IsEqualTo(2);
    }

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
    public async Task CancelledGpuCaptureDrainsUploadsAndRenderingRetainsEditsAndOwnsDetachedPlanes(
        SilkGraphicsBackend backend, bool cancelDuringLatticeUpload, bool retained)
    {
        using var device = new SilkGpuHdrObservedDevice(SilkDepthCaptureConformance.CreateDevice(backend));
        using var fixture = new SilkHdrColorCaptureFixture();
        SilkFrameCaptureResult first;
        SilkFrameCaptureResult retry;
        byte[] firstHdr;
        byte[] firstRgba;
        float[] firstDepth;
        bool cancelled = false;
        int submitted;
        int completed;
        using (OpenUsdSilkSession session = fixture.CreateSession())
        using (var capturer = new SilkFrameCapturer(device))
        using (var renderer = new SilkMeshRenderer(device))
        using (var cancellation = new CancellationTokenSource())
        {
            var options = new SilkHdrColorCaptureOptions(includeDeviceDepth: true);
            if (retained)
            {
                _ = SilkHdrColorCaptureFixture.RetainScene(device, renderer, session);
            }
            first = capture(40, 32, GpuSettings(), 0, default);
            firstHdr = first.HdrColor!.Rgba16Float.ToArray();
            firstRgba = first.Rgba.ToArray();
            firstDepth = first.Depth!.Values.ToArray();
            if (retained)
            {
                _ = SilkHdrColorCaptureFixture.RetainScene(device, renderer, session, timeCode: 1);
            }
            device.AfterCompletedWait = cancellation.Cancel;
            try
            {
                _ = capture(
                    40, 32, cancelDuringLatticeUpload ? GpuSettings(view: "IdentityView") : GpuSettings(),
                    1, cancellation.Token);
            }
            catch (OperationCanceledException)
            {
                cancelled = true;
            }
            submitted = device.Submissions;
            completed = device.CompletedSubmissions;
            retry = capture(44, 36, GpuSettings(), 1, default);

            SilkFrameCaptureResult capture(
                int width, int height, RenderSettings settings, double time, CancellationToken token) => retained
                ? SilkFrameCapture.CaptureRetainedWithHdrColor(
                    renderer, device, width, height, settings, options, cancellationToken: token)
                : capturer.CaptureWithHdrColor(
                    session, width, height, settings, options, timeCode: time,
                    camera: SilkHdrColorCaptureFixture.Camera, cancellationToken: token);
        }
        device.Dispose();

        await Assert.That(cancelled).IsTrue();
        await Assert.That(submitted).IsEqualTo(completed);
        await Assert.That(device.StayedOnCallingThread).IsTrue();
        await Assert.That(first.HdrColor!.Rgba16Float.Span.SequenceEqual(firstHdr)).IsTrue();
        await Assert.That(first.Rgba.Span.SequenceEqual(firstRgba)).IsTrue();
        await Assert.That(first.Depth!.Values.Span.SequenceEqual(firstDepth)).IsTrue();
        await Assert.That(retry.HdrColor!.Rgba16Float.Span.Slice(((8 * 44) + 10) * 8, 8)
            .SequenceEqual<byte>([0x00, 0x38, 0x00, 0x40, 0x00, 0x30, 0x00, 0x3C])).IsTrue();
        await Assert.That(retry.Depth!.Values.Span[(8 * 44) + 10]).IsEqualTo(0.3f).Within(0.00001f);
        await Assert.That(retry.RenderResult.DrawCount).IsEqualTo(2);
    }

    [Test]
    [Arguments(SilkGraphicsBackend.D3D12)]
    [Arguments(SilkGraphicsBackend.Vulkan)]
    [Arguments(SilkGraphicsBackend.Metal)]
    public async Task InvalidKnownGpuRequestPreCancellationWrongDeviceAndDisposalPrecedeTargetsAndSync(
        SilkGraphicsBackend backend)
    {
        using var device = new SilkGpuHdrObservedDevice(SilkDepthCaptureConformance.CreateDevice(backend));
        using var other = new SilkGpuHdrObservedDevice(SilkDepthCaptureConformance.CreateDevice(backend));
        using var fixture = new SilkHdrColorCaptureFixture();
        using OpenUsdSilkSession session = fixture.CreateSession();
        using var capturer = new SilkFrameCapturer(device);
        using var renderer = new SilkMeshRenderer(device);
        RenderSettings invalid = SilkHdrColorCaptureFixture.Settings(
            outputTransform: RenderOutputTransform.Reinhard) with
        {
            DisplayTransform = GpuSettings().DisplayTransform
        };
        int before = device.TextureRequests;
        await Assert.That(() => capturer.CaptureWithHdrColor(
            session, 40, 32, invalid, camera: SilkHdrColorCaptureFixture.Camera)).Throws<InvalidOperationException>();
        await Assert.That(() => capturer.CaptureWithHdrColor(
            session, 40, 32, GpuSettings(), camera: SilkHdrColorCaptureFixture.Camera,
            cancellationToken: new CancellationToken(canceled: true))).Throws<OperationCanceledException>();
        await Assert.That(() => SilkFrameCapture.CaptureRetainedWithHdrColor(
            renderer, device, 40, 32, invalid)).Throws<InvalidOperationException>();
        await Assert.That(() => SilkFrameCapture.CaptureRetainedWithHdrColor(
            renderer, other, 40, 32, GpuSettings())).Throws<ArgumentException>();
        renderer.Dispose();
        await Assert.That(() => SilkFrameCapture.CaptureRetainedWithHdrColor(
            renderer, device, 40, 32, GpuSettings())).Throws<ObjectDisposedException>();
        await Assert.That(device.TextureRequests).IsEqualTo(before);
        await Assert.That(other.TextureRequests).IsEqualTo(0);
        using var fresh = new SilkFrameCapturer(device);
        SilkFrameCaptureResult untouched = fresh.CaptureWithHdrColor(
            session, 40, 32, GpuSettings(), camera: SilkHdrColorCaptureFixture.Camera);
        await Assert.That(untouched.RenderResult.DrawCount).IsEqualTo(2);
    }

    [Test]
    public async Task SwiftShaderNonfiniteSceneHdrIsRefusedAfterGpuDisplayCompletes()
    {
        using var device = new SilkGpuHdrObservedDevice(
            SilkDepthCaptureConformance.CreateDevice(SilkGraphicsBackend.Vulkan));
        using var fixture = new SilkHdrColorCaptureFixture(emissionA: "(70000, 0.5, 2)");
        using OpenUsdSilkSession session = fixture.CreateSession();
        using var capturer = new SilkFrameCapturer(device);
        InvalidDataException? refusal = null;
        try
        {
            _ = capturer.CaptureWithHdrColor(
                session, 40, 32, GpuSettings(), camera: SilkHdrColorCaptureFixture.Camera);
        }
        catch (InvalidDataException exception)
        {
            refusal = exception;
        }
        int submitted = device.Submissions;
        int completed = device.CompletedSubmissions;
        SilkFrameCaptureResult retry = capturer.CaptureWithHdrColor(
            session, 40, 32, GpuSettings(), timeCode: 1, camera: SilkHdrColorCaptureFixture.Camera);
        await Assert.That(refusal).IsNotNull();
        await Assert.That(submitted).IsEqualTo(completed);
        await Assert.That(retry.HdrColor!.Rgba16Float.Span.Slice(((8 * 40) + 10) * 8, 8)
            .SequenceEqual<byte>([0x00, 0x38, 0x00, 0x40, 0x00, 0x30, 0x00, 0x3C])).IsTrue();
    }
}
