// Copyright (c) marcschier. Licensed under the MIT License.

using OpenUsd.Rendering.Silk;

namespace OpenUsd.Rendering.ConformanceTests;

[NotInParallel]
public sealed class SilkInstanceIdCaptureConformanceTests
{
    private const int Size = 32;

    [Test]
    [Arguments(SilkGraphicsBackend.D3D12, false)]
    [Arguments(SilkGraphicsBackend.D3D12, true)]
    [Arguments(SilkGraphicsBackend.Vulkan, false)]
    [Arguments(SilkGraphicsBackend.Vulkan, true)]
    [Arguments(SilkGraphicsBackend.Metal, false)]
    [Arguments(SilkGraphicsBackend.Metal, true)]
    public async Task RetainedCaptureResolvesVisibleInstanceIndicesFromThePickRaster(
        SilkGraphicsBackend backend, bool withPrimId)
    {
        using ISilkGraphicsDevice device = SilkDepthCaptureConformance.CreateDevice(backend);
        using var renderer = new SilkMeshRenderer(device);
        SilkMeshRendererConformance.Apply(
            renderer, 1,
            SilkMeshRendererConformance.CreateFrameCommand(Size, Size, SilkMeshRendererConformance.Identity()),
            SilkCrossBackendParityTests.CreateInstanceCommand(1, "/Proto", 0, -0.5),
            SilkCrossBackendParityTests.CreateInstanceCommand(1, "/Proto", 1, 0.0),
            SilkCrossBackendParityTests.CreateInstanceCommand(1, "/Proto", 2, 0.5),
            SilkMeshRendererConformance.CreateCubeCommand(2, "/Plain", 0, -0.6, [1, 0, 0, 1]));

        SilkFrameCaptureResult capture = SilkFrameCapture.CaptureRetainedWithDepth(
            renderer, device, Size, Size, RenderSettings.Default,
            new SilkDepthCaptureOptions { IncludePrimId = withPrimId, IncludeInstanceId = true }, pageRevision: 1);
        SilkInstanceIdCaptureResult instanceId = capture.InstanceId
            ?? throw new InvalidOperationException("An explicitly requested instance identity capture is missing.");
        int[] original = instanceId.Values.ToArray();
        SilkFrameCaptureResult primOnly = SilkFrameCapture.CaptureRetainedWithDepth(
            renderer, device, Size, Size, RenderSettings.Default,
            new SilkDepthCaptureOptions { IncludePrimId = true }, pageRevision: 2);
        renderer.Dispose();
        device.Dispose();

        await Assert.That(instanceId.Width).IsEqualTo(Size);
        await Assert.That(instanceId.Height).IsEqualTo(Size);
        await Assert.That(instanceId.Values.Length).IsEqualTo(Size * Size);
        await Assert.That(instanceId.Values.Span[(12 * Size) + 8]).IsEqualTo(0);
        await Assert.That(instanceId.Values.Span[(12 * Size) + 16]).IsEqualTo(1);
        await Assert.That(instanceId.Values.Span[(12 * Size) + 24]).IsEqualTo(2);
        await Assert.That(instanceId.Values.Span[(25 * Size) + 16]).IsEqualTo(-1)
            .Because("a non-instanced prim reports the same minus-one instance index as its identity");
        await Assert.That(instanceId.Values.Span[(28 * Size) + 2]).IsEqualTo(-1);
        await Assert.That(instanceId.Values.ToArray().All(static value => value is >= -1 and <= 2)).IsTrue();
        await Assert.That(capture.PrimId is not null).IsEqualTo(withPrimId);
        await Assert.That(primOnly.InstanceId).IsNull();
        SilkPrimIdCaptureResult reference = withPrimId ? capture.PrimId! : primOnly.PrimId!;
        await Assert.That(string.Join('|', reference.Identities.Select(
                static identity => $"{identity.Path}#{identity.InstanceIndex}")))
            .IsEqualTo("/Plain#-1|/Proto#0|/Proto#1|/Proto#2");
        for (int pixel = 0; pixel < original.Length; pixel++)
        {
            int index = reference.Values.Span[pixel];
            int expected = index < 0 ? -1 : reference.Identities[index].InstanceIndex;
            if (original[pixel] != expected)
            {
                Assert.Fail($"Pixel {pixel} reports instance {original[pixel]} but its identity says {expected}.");
            }
        }
        await Assert.That(instanceId.Values.Span.SequenceEqual(original)).IsTrue()
            .Because("later captures and renderer/device disposal must not overwrite owned instance indices");
    }

    [Test]
    [Arguments(SilkGraphicsBackend.D3D12)]
    [Arguments(SilkGraphicsBackend.Vulkan)]
    [Arguments(SilkGraphicsBackend.Metal)]
    public async Task DeviceWithoutPickingRefusesInstanceIdInsteadOfSynthesizingIt(SilkGraphicsBackend backend)
    {
        using var device = new SilkDepthCaptureObservedDevice(SilkDepthCaptureConformance.CreateDevice(backend));
        using var renderer = new SilkMeshRenderer(device);
        SilkMeshRendererConformance.Apply(
            renderer, 1,
            SilkMeshRendererConformance.CreateFrameCommand(Size, Size, SilkMeshRendererConformance.Identity()),
            SilkCrossBackendParityTests.CreateInstanceCommand(1, "/Proto", 0, -0.5));

        NotSupportedException? failure = Assert.Throws<NotSupportedException>(
            () => SilkFrameCapture.CaptureRetainedWithDepth(
                renderer, device, Size, Size, RenderSettings.Default,
                new SilkDepthCaptureOptions { IncludeInstanceId = true }, pageRevision: 1));
        SilkFrameCaptureResult depthOnly = SilkFrameCapture.CaptureRetainedWithDepth(
            renderer, device, Size, Size, RenderSettings.Default, pageRevision: 1);

        await Assert.That(failure!.Message).Contains("Instance identity capture requires a pick-capable");
        await Assert.That(depthOnly.Depth).IsNotNull();
        await Assert.That(depthOnly.InstanceId).IsNull();
    }
}
