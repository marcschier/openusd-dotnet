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
    public async Task CancelledSubmissionRetainsNativeDeltaAndDetachedPlanesSurviveLaterCaptures(
        SilkGraphicsBackend backend, bool cpuOcio)
    {
        using var device = new SilkDepthCaptureObservedDevice(
            SilkDepthCaptureConformance.CreateDevice(backend));
        using var fixture = new SilkHdrColorCaptureFixture();
        SilkFrameCaptureResult first;
        SilkFrameCaptureResult recovered;
        SilkFrameCaptureResult legacyDepth;
        byte[] originalHdr;
        byte[] originalRgba;
        float[] originalDepth;
        bool quotaRefused = false;
        bool preCancelled = false;
        bool submissionCancelled = false;
        int before;
        int after;
        using (OpenUsdSilkSession session = fixture.CreateSession())
        using (var capturer = new SilkFrameCapturer(device))
        using (SilkOpenColorIoProcessor processor = SilkHdrColorCaptureFixture.CreateProcessor("TestView"))
        using (var cancellation = new CancellationTokenSource())
        {
            CameraState camera = SilkHdrColorCaptureFixture.Camera;
            var options = new SilkHdrColorCaptureOptions(includeDeviceDepth: true);
            _ = capturer.Capture(session, 40, 32, RenderSettings.Default, camera: camera);
            first = cpuOcio
                ? capturer.CaptureWithHdrColor(session, 40, 32, RenderSettings.Default, processor, options, camera: camera)
                : capturer.CaptureWithHdrColor(session, 40, 32, RenderSettings.Default, options, camera: camera);
            originalHdr = first.HdrColor!.Rgba16Float.ToArray();
            originalRgba = first.Rgba.ToArray();
            originalDepth = first.Depth!.Values.ToArray();
            before = device.TextureRequests;
            try
            {
                var insufficient = new SilkHdrColorCaptureOptions(maximumReadbackBytes: 25_599);
                _ = cpuOcio
                    ? capturer.CaptureWithHdrColor(
                        session, 40, 32, RenderSettings.Default, processor, insufficient, timeCode: 1, camera: camera)
                    : capturer.CaptureWithHdrColor(
                        session, 40, 32, RenderSettings.Default, insufficient, timeCode: 1, camera: camera);
            }
            catch (ArgumentOutOfRangeException)
            {
                quotaRefused = true;
            }
            try
            {
                _ = cpuOcio
                    ? capturer.CaptureWithHdrColor(
                        session, 40, 32, RenderSettings.Default, processor, options, timeCode: 1, camera: camera,
                        cancellationToken: new CancellationToken(canceled: true))
                    : capturer.CaptureWithHdrColor(
                        session, 40, 32, RenderSettings.Default, options, timeCode: 1, camera: camera,
                        cancellationToken: new CancellationToken(canceled: true));
            }
            catch (OperationCanceledException)
            {
                preCancelled = true;
            }
            after = device.TextureRequests;
            device.AfterCompletedWait = cancellation.Cancel;
            try
            {
                _ = cpuOcio
                    ? capturer.CaptureWithHdrColor(
                        session, 40, 32, RenderSettings.Default, processor, options, timeCode: 1, camera: camera,
                        cancellationToken: cancellation.Token)
                    : capturer.CaptureWithHdrColor(
                        session, 40, 32, RenderSettings.Default, options, timeCode: 1, camera: camera,
                        cancellationToken: cancellation.Token);
            }
            catch (OperationCanceledException)
            {
                submissionCancelled = true;
            }
            recovered = cpuOcio
                ? capturer.CaptureWithHdrColor(
                    session, 40, 32, RenderSettings.Default, processor, options, timeCode: 1, camera: camera)
                : capturer.CaptureWithHdrColor(
                    session, 40, 32, RenderSettings.Default, options, timeCode: 1, camera: camera);
            legacyDepth = cpuOcio
                ? capturer.CaptureWithDepth(
                    session, 40, 32, RenderSettings.Default, processor, timeCode: 1, camera: camera)
                : capturer.CaptureWithDepth(session, 40, 32, RenderSettings.Default, timeCode: 1, camera: camera);
        }
        device.Dispose();

        await Assert.That(quotaRefused).IsTrue();
        await Assert.That(preCancelled).IsTrue();
        await Assert.That(submissionCancelled).IsTrue();
        await Assert.That(after).IsEqualTo(before);
        await Assert.That(device.Submissions).IsGreaterThan(0);
        await Assert.That(device.CompletedSubmissions).IsEqualTo(device.Submissions);
        await Assert.That(device.StayedOnCallingThread).IsTrue();
        await Assert.That(first.HdrColor!.Rgba16Float.Span.SequenceEqual(originalHdr)).IsTrue();
        await Assert.That(first.Rgba.Span.SequenceEqual(originalRgba)).IsTrue();
        await Assert.That(first.Depth!.Values.Span.SequenceEqual(originalDepth)).IsTrue();
        await Assert.That(recovered.HdrColor!.Rgba16Float.Span.Slice(((8 * 40) + 10) * 8, 8)
            .SequenceEqual<byte>([0x00, 0x38, 0x00, 0x40, 0x00, 0x30, 0x00, 0x3C])).IsTrue();
        await Assert.That(recovered.Depth!.Values.Span[(8 * 40) + 10]).IsEqualTo(0.3f).Within(0.00001f);
        await Assert.That(recovered.RenderResult.DrawCount).IsEqualTo(2);
        await Assert.That(legacyDepth.HdrColor).IsNull();
        await Assert.That(legacyDepth.Rgba.Span.SequenceEqual(recovered.Rgba.Span)).IsTrue();
        await Assert.That(legacyDepth.Depth!.Values.Span.SequenceEqual(recovered.Depth.Values.Span)).IsTrue();
    }

    [Test]
    [Arguments(SilkGraphicsBackend.D3D12)]
    [Arguments(SilkGraphicsBackend.Vulkan)]
    [Arguments(SilkGraphicsBackend.Metal)]
    public async Task ForeignAndMixedSessionContinuityIsRejectedBeforeTargets(SilkGraphicsBackend backend)
    {
        using var device = new SilkDepthCaptureObservedDevice(
            SilkDepthCaptureConformance.CreateDevice(backend));
        using var fixture = new SilkHdrColorCaptureFixture();
        using OpenUsdSilkSession first = fixture.CreateSession();
        using OpenUsdSilkSession second = fixture.CreateSession();
        using var owner = new SilkFrameCapturer(device);
        using var foreign = new SilkFrameCapturer(device);
        CameraState camera = SilkHdrColorCaptureFixture.Camera;
        _ = owner.Capture(first, 40, 32, RenderSettings.Default, camera: camera);
        int before = device.TextureRequests;
        await Assert.That(() => foreign.CaptureWithHdrColor(
            first, 40, 32, RenderSettings.Default, camera: camera)).Throws<InvalidOperationException>();
        await Assert.That(() => owner.CaptureWithHdrColor(
            second, 40, 32, RenderSettings.Default, camera: camera)).Throws<InvalidOperationException>();
        await Assert.That(device.TextureRequests).IsEqualTo(before);
        SilkFrameCaptureResult valid = owner.CaptureWithHdrColor(
            first, 40, 32, RenderSettings.Default, camera: camera);
        _ = owner.Capture(second, 40, 32, RenderSettings.Default, camera: camera);
        before = device.TextureRequests;
        await Assert.That(() => owner.CaptureWithHdrColor(
            first, 40, 32, RenderSettings.Default, camera: camera)).Throws<InvalidOperationException>();
        await Assert.That(device.TextureRequests).IsEqualTo(before);
        await Assert.That(valid.RenderResult.DrawCount).IsEqualTo(2);
    }
}
