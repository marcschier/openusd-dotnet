// Copyright (c) marcschier. Licensed under the MIT License.

using OpenUsd.Rendering.Silk;

namespace OpenUsd.Rendering.ConformanceTests;

[NotInParallel]
public sealed class SilkPrimIdCaptureConformanceTests
{
    [Test]
    [Arguments(SilkGraphicsBackend.D3D12)]
    [Arguments(SilkGraphicsBackend.Vulkan)]
    [Arguments(SilkGraphicsBackend.Metal)]
    public async Task RetainedCaptureResolvesVisiblePrimIdentitiesAndBackground(SilkGraphicsBackend backend)
    {
        using ISilkGraphicsDevice device = SilkDepthCaptureConformance.CreateDevice(backend);
        using var renderer = new SilkMeshRenderer(device);
        SilkMeshRendererConformance.Apply(
            renderer, 1, SilkDepthCaptureConformance.CreateFrame(perspective: false),
            SilkMeshRendererConformance.CreateMeshCommand(
                1, "/Near",
                [-0.9f, 0.1f, -1, -0.1f, 0.1f, -1, -0.1f, 0.9f, -1, -0.9f, 0.9f, -1],
                [0, 1, 2, 0, 2, 3], color: [1, 0, 0, 1]),
            SilkMeshRendererConformance.CreateMeshCommand(
                2, "/Hidden",
                [-0.9f, 0.1f, -3, -0.1f, 0.1f, -3, -0.1f, 0.9f, -3, -0.9f, 0.9f, -3],
                [0, 1, 2, 0, 2, 3], color: [0, 0, 1, 1]),
            SilkMeshRendererConformance.CreateMeshCommand(
                3, "/Far",
                [0.1f, -0.9f, -5, 0.9f, -0.9f, -5, 0.9f, -0.1f, -5, 0.1f, -0.1f, -5],
                [0, 1, 2, 0, 2, 3], color: [0, 1, 0, 1]));

        SilkFrameCaptureResult capture = SilkFrameCapture.CaptureRetainedWithDepth(
            renderer, device, 40, 32, RenderSettings.Default,
            new SilkDepthCaptureOptions { IncludePrimId = true }, pageRevision: 1);
        SilkPrimIdCaptureResult primId = capture.PrimId
            ?? throw new InvalidOperationException("An explicitly requested prim identity capture is missing.");
        int[] original = primId.Values.ToArray();
        SilkFrameCaptureResult plain = SilkFrameCapture.CaptureRetainedWithDepth(
            renderer, device, 40, 32, RenderSettings.Default, pageRevision: 2);
        SilkMeshRendererConformance.Apply(
            renderer, 2, SilkMeshRendererConformance.CreateRemoveCommand(1, "/Near"));
        SilkFrameCaptureResult removed = SilkFrameCapture.CaptureRetainedWithDepth(
            renderer, device, 40, 32, RenderSettings.Default,
            new SilkDepthCaptureOptions { IncludePrimId = true }, pageRevision: 3);
        renderer.Dispose();
        device.Dispose();

        await Assert.That(primId.Width).IsEqualTo(40);
        await Assert.That(primId.Height).IsEqualTo(32);
        await Assert.That(primId.Values.Length).IsEqualTo(40 * 32);
        await Assert.That(primId.Identities.Count).IsEqualTo(2)
            .Because("the depth-tested hidden prim is not visible in any pixel");
        await Assert.That(primId.Identities[0]).IsEqualTo(new SilkPrimIdentity("/Far", -1));
        await Assert.That(primId.Identities[1]).IsEqualTo(new SilkPrimIdentity("/Near", -1));
        await Assert.That(primId.Values.Span[(8 * 40) + 10]).IsEqualTo(1);
        await Assert.That(primId.Values.Span[(24 * 40) + 30]).IsEqualTo(0);
        await Assert.That(primId.Values.Span[(24 * 40) + 10]).IsEqualTo(-1);
        await Assert.That(primId.Values.ToArray().All(static value => value is >= -1 and <= 1)).IsTrue();
        await Assert.That(capture.Depth!.Values.Span[(8 * 40) + 10]).IsEqualTo(0.2f).Within(0.00001f);
        await Assert.That(plain.PrimId).IsNull();
        await Assert.That(plain.Rgba.Span.SequenceEqual(capture.Rgba.Span)).IsTrue();
        SilkPrimIdCaptureResult revealed = removed.PrimId!;
        await Assert.That(string.Join('|', revealed.Identities.Select(static identity => identity.Path)))
            .IsEqualTo("/Far|/Hidden");
        await Assert.That(revealed.Identities[revealed.Values.Span[(8 * 40) + 10]].Path).IsEqualTo("/Hidden");
        await Assert.That(primId.Values.Span.SequenceEqual(original)).IsTrue()
            .Because("later captures and renderer/device disposal must not overwrite owned identities");
    }

    [Test]
    [Arguments(SilkGraphicsBackend.D3D12)]
    [Arguments(SilkGraphicsBackend.Vulkan)]
    [Arguments(SilkGraphicsBackend.Metal)]
    public async Task DeviceWithoutPickingRefusesPrimIdInsteadOfSynthesizingIt(SilkGraphicsBackend backend)
    {
        using var device = new SilkDepthCaptureObservedDevice(SilkDepthCaptureConformance.CreateDevice(backend));
        using var renderer = new SilkMeshRenderer(device);
        SilkMeshRendererConformance.Apply(
            renderer, 1, SilkDepthCaptureConformance.CreateFrame(perspective: false),
            SilkMeshRendererConformance.CreateMeshCommand(
                1, "/Near",
                [-0.9f, 0.1f, -1, -0.1f, 0.1f, -1, -0.1f, 0.9f, -1, -0.9f, 0.9f, -1],
                [0, 1, 2, 0, 2, 3], color: [1, 0, 0, 1]));

        NotSupportedException? failure = Assert.Throws<NotSupportedException>(
            () => SilkFrameCapture.CaptureRetainedWithDepth(
                renderer, device, 40, 32, RenderSettings.Default,
                new SilkDepthCaptureOptions { IncludePrimId = true }, pageRevision: 1));
        SilkFrameCaptureResult depthOnly = SilkFrameCapture.CaptureRetainedWithDepth(
            renderer, device, 40, 32, RenderSettings.Default, pageRevision: 1);

        await Assert.That(failure!.Message).Contains("pick-capable");
        await Assert.That(depthOnly.Depth).IsNotNull();
        await Assert.That(depthOnly.PrimId).IsNull();
    }
}
