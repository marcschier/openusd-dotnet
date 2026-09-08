// Copyright (c) marcschier. Licensed under the MIT License.

using System.Buffers.Binary;
using OpenUsd.Rendering.Silk;
using OpenUsd.Rendering.Silk.D3D12;
using OpenUsd.Rendering.Silk.Metal;
using OpenUsd.Rendering.Silk.Vulkan;

namespace OpenUsd.Rendering.ConformanceTests;

[NotInParallel]
public sealed class SilkDepthCaptureConformanceTests
{
    [Test]
    [Arguments(SilkGraphicsBackend.D3D12, false)]
    [Arguments(SilkGraphicsBackend.D3D12, true)]
    [Arguments(SilkGraphicsBackend.Vulkan, false)]
    [Arguments(SilkGraphicsBackend.Vulkan, true)]
    [Arguments(SilkGraphicsBackend.Metal, false)]
    [Arguments(SilkGraphicsBackend.Metal, true)]
    public async Task RetainedCaptureReadsRasterDepthInsteadOfBeauty(
        SilkGraphicsBackend backend, bool perspective)
    {
        using ISilkGraphicsDevice device = SilkDepthCaptureConformance.CreateDevice(backend);
        using var renderer = new SilkMeshRenderer(device);
        SilkMeshRendererConformance.Apply(
            renderer, 1, SilkDepthCaptureConformance.CreateFrame(perspective),
            SilkMeshRendererConformance.CreateMeshCommand(
                1, "/Near",
                perspective
                    ? [-2.7f, 0.3f, -1, -0.3f, 0.3f, -1, -0.3f, 2.7f, -1, -2.7f, 2.7f, -1]
                    : [-0.9f, 0.1f, -1, -0.1f, 0.1f, -1, -0.1f, 0.9f, -1, -0.9f, 0.9f, -1],
                [0, 1, 2, 0, 2, 3],
                color: [1, 0, 0, 1]),
            SilkMeshRendererConformance.CreateMeshCommand(
                2, "/Far",
                perspective
                    ? [0.7f, -6.3f, -5, 6.3f, -6.3f, -5, 6.3f, -0.7f, -5, 0.7f, -0.7f, -5]
                    : [0.1f, -0.9f, -5, 0.9f, -0.9f, -5, 0.9f, -0.1f, -5, 0.1f, -0.1f, -5],
                [0, 1, 2, 0, 2, 3],
                color: [0, 1, 0, 1]));

        SilkFrameCaptureResult capture = SilkFrameCapture.CaptureRetainedWithDepth(
            renderer, device, 40, 32, RenderSettings.Default, pageRevision: 1);
        SilkDepthCaptureResult depth = capture.Depth
            ?? throw new InvalidOperationException("An explicitly requested depth capture is missing.");
        float[] original = depth.Values.ToArray();
        SilkFrameCaptureResult beauty = SilkFrameCapture.CaptureRetained(
            renderer, device, 40, 32, RenderSettings.Default);
        using ISilkGraphicsTexture rawColor = device.CreateTexture2D(
            SilkTextureDescriptor.HdrColorTarget(40, 32));
        using ISilkGraphicsTexture rawDepth = device.CreateTexture2D(
            SilkTextureDescriptor.SampledDepthTarget(40, 32));
        _ = renderer.Render(rawColor, rawDepth);
        var rawValues = new float[40 * 32];
        rawDepth.ReadbackForTesting(rawValues);

        SilkMeshRendererConformance.Apply(
            renderer, 2, SilkMeshRendererConformance.CreateRemoveCommand(1, "/Near"));
        SilkFrameCaptureResult removed = SilkFrameCapture.CaptureRetainedWithDepth(
            renderer, device, 40, 32, RenderSettings.Default, pageRevision: 2);
        rawColor.Dispose();
        rawDepth.Dispose();
        renderer.Dispose();
        device.Dispose();

        await Assert.That(depth.Convention)
            .IsEqualTo(SilkDepthConvention.NormalizedDeviceDepthZeroToOne);
        await Assert.That(depth.Width).IsEqualTo(40);
        await Assert.That(depth.Height).IsEqualTo(32);
        await Assert.That(depth.Values.Length).IsEqualTo(40 * 32);
        // Camera z=2, world z=-1/-5: view distances 3/7. With near=1, far=11,
        // orthographic depths are 0.2/0.6; perspective depths are 11/15 and 33/35.
        await Assert.That(depth.Values.Span[(8 * 40) + 10])
            .IsEqualTo(perspective ? 0.733333333f : 0.2f).Within(0.00001f);
        await Assert.That(depth.Values.Span[(24 * 40) + 30])
            .IsEqualTo(perspective ? 0.942857143f : 0.6f).Within(0.00001f);
        await Assert.That(depth.Values.Span[(24 * 40) + 10]).IsEqualTo(1f);
        await Assert.That(depth.ClearValue).IsEqualTo(1f);
        await Assert.That(capture.RenderResult.DrawCount).IsEqualTo(2);
        await Assert.That(capture.PageRevision).IsEqualTo(1ul);
        await Assert.That(capture.CommandCount).IsEqualTo(0u);
        await Assert.That(capture.Rgba.Span[((8 * 40) + 10) * 4]).IsGreaterThan((byte)150);
        await Assert.That(capture.Rgba.Span[(((24 * 40) + 30) * 4) + 1]).IsGreaterThan((byte)150);
        await Assert.That(rawValues.AsSpan().SequenceEqual(depth.Values.Span)).IsTrue();
        await Assert.That(beauty.Rgba.Span.SequenceEqual(capture.Rgba.Span)).IsTrue();
        await Assert.That(beauty.Depth).IsNull();
        await Assert.That(removed.Depth!.Values.Span[(8 * 40) + 10]).IsEqualTo(1f);
        await Assert.That(removed.Depth.Values.Span[(24 * 40) + 30])
            .IsEqualTo(perspective ? 0.942857143f : 0.6f).Within(0.00001f);
        await Assert.That(depth.Values.Span.SequenceEqual(original)).IsTrue()
            .Because("later captures and renderer/device disposal must not overwrite owned samples");
    }

    [Test]
    [Arguments(SilkGraphicsBackend.D3D12)]
    [Arguments(SilkGraphicsBackend.Vulkan)]
    [Arguments(SilkGraphicsBackend.Metal)]
    public async Task NearIsZeroAndFarPlaneWritesAreIndistinguishableFromClear(
        SilkGraphicsBackend backend)
    {
        using ISilkGraphicsDevice device = SilkDepthCaptureConformance.CreateDevice(backend);
        using var renderer = new SilkMeshRenderer(device);
        SilkMeshRendererConformance.Apply(
            renderer, 1, SilkDepthCaptureConformance.CreateFrame(perspective: false),
            SilkMeshRendererConformance.CreateMeshCommand(
                1, "/NearClip",
                [-0.9f, 0.1f, 1, -0.1f, 0.1f, 1, -0.1f, 0.9f, 1, -0.9f, 0.9f, 1],
                [0, 1, 2, 0, 2, 3], color: [1, 0, 0, 1]),
            SilkMeshRendererConformance.CreateMeshCommand(
                2, "/FarClip",
                [0.1f, -0.9f, -9, 0.9f, -0.9f, -9, 0.9f, -0.1f, -9, 0.1f, -0.1f, -9],
                [0, 1, 2, 0, 2, 3], color: [0, 1, 0, 1]));
        SilkFrameCaptureResult boundaries = SilkFrameCapture.CaptureRetainedWithDepth(
            renderer, device, 40, 32, RenderSettings.Default);
        SilkMeshRendererConformance.Apply(
            renderer, 2,
            SilkMeshRendererConformance.CreateRemoveCommand(1, "/NearClip"),
            SilkMeshRendererConformance.CreateRemoveCommand(2, "/FarClip"));
        SilkFrameCaptureResult empty = SilkFrameCapture.CaptureRetainedWithDepth(
            renderer, device, 40, 32, RenderSettings.Default);

        await Assert.That(boundaries.Depth!.Values.Span[(8 * 40) + 10]).IsEqualTo(0f);
        await Assert.That(boundaries.Rgba.Span[((8 * 40) + 10) * 4]).IsGreaterThan((byte)150);
        await Assert.That(boundaries.Depth.Values.Span[(24 * 40) + 30]).IsEqualTo(1f);
        await Assert.That(boundaries.Rgba.Span[(((24 * 40) + 30) * 4) + 1]).IsGreaterThan((byte)150);
        await Assert.That(boundaries.Depth.Values.Span[(24 * 40) + 10]).IsEqualTo(1f);
        await Assert.That(boundaries.Rgba.Span[(((24 * 40) + 10) * 4) + 1]).IsEqualTo((byte)0);
        await Assert.That(empty.Depth!.Values.ToArray().All(value => value == 1f)).IsTrue();
        await Assert.That(empty.RenderResult.DrawCount).IsEqualTo(0);
    }
}

internal static class SilkDepthCaptureConformance
{
    internal static ISilkGraphicsDevice CreateDevice(SilkGraphicsBackend backend)
    {
        if (backend == SilkGraphicsBackend.D3D12 && OperatingSystem.IsWindows())
        {
            return D3D12SilkGraphicsDevice.Create(useWarp: true);
        }
        if (backend == SilkGraphicsBackend.Vulkan &&
            (OperatingSystem.IsWindows() || OperatingSystem.IsLinux()))
        {
            VulkanSilkGraphicsDevice device = VulkanSilkGraphicsDevice.Create();
            if (Environment.GetEnvironmentVariable("OPENUSD_REQUIRE_SWIFTSHADER") == "1" &&
                (!device.Capabilities.IsSoftware ||
                    !device.Capabilities.DeviceName.Contains("SwiftShader", StringComparison.OrdinalIgnoreCase)))
            {
                string deviceName = device.Capabilities.DeviceName;
                device.Dispose();
                throw new InvalidOperationException($"Expected SwiftShader, but Vulkan selected '{deviceName}'.");
            }
            return device;
        }
        if (backend == SilkGraphicsBackend.Metal && OperatingSystem.IsMacOS())
        {
            return MetalSilkGraphicsDevice.Create();
        }
        Skip.Test($"{backend} execution is unavailable on this operating system.");
        throw new InvalidOperationException("Skip.Test returned unexpectedly.");
    }

    internal static byte[] CreateFrame(bool perspective)
    {
        byte[] frame = SilkMeshRendererConformance.CreateFrameCommand(
            40, 32,
            [1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, -2, 1]);
        double[] projection = perspective
            ? [1, 0, 0, 0, 0, 1, 0, 0, 0, 0, -1.2, -1, 0, 0, -2.2, 0]
            : [1, 0, 0, 0, 0, 1, 0, 0, 0, 0, -0.2, 0, 0, 0, -1.2, 1];
        for (int index = 0; index < projection.Length; index++)
        {
            BinaryPrimitives.WriteDoubleLittleEndian(
                frame.AsSpan(144 + (index * sizeof(double))), projection[index]);
        }
        return frame;
    }
}
