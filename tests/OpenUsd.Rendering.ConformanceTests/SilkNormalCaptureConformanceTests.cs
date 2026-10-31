// Copyright (c) marcschier. Licensed under the MIT License.

using System.Buffers.Binary;
using OpenUsd.Rendering.Silk;

namespace OpenUsd.Rendering.ConformanceTests;

[NotInParallel]
public sealed class SilkNormalCaptureConformanceTests
{
    private const int Width = 40;
    private const int Height = 32;
    private const float Tolerance = 1e-4f;
    private static readonly float HalfSqrt2 = MathF.Sqrt(0.5f);

    [Test]
    [Arguments(SilkGraphicsBackend.D3D12)]
    [Arguments(SilkGraphicsBackend.Vulkan)]
    public async Task RetainedCaptureWritesWorldSpaceUnitNormalsAndZeroBackground(SilkGraphicsBackend backend)
    {
        using ISilkGraphicsDevice device = SilkDepthCaptureConformance.CreateDevice(backend);
        using var renderer = new SilkMeshRenderer(device);
        // The camera sits at world x = +2 looking down -x, so a surface facing it has the world
        // normal (1, 0, 0) while its eye-space normal is (0, 0, 1): only a world-space plane passes.
        // Each quad owns its four vertices, so the area-weighted normals are flat and exact.
        SilkMeshRendererConformance.Apply(
            renderer, 1,
            SideCameraFrame(),
            SilkMeshRendererConformance.CreateMeshCommand(
                1, "/Facing",
                [-1, -0.5f, 0.9f, -1, -0.5f, 0.4f, -1, 0.5f, 0.4f, -1, 0.5f, 0.9f],
                [0, 1, 2, 0, 2, 3]),
            SilkMeshRendererConformance.CreateMeshCommand(
                2, "/Reversed",
                [-1, -0.5f, 0.25f, -1, -0.5f, -0.25f, -1, 0.5f, -0.25f, -1, 0.5f, 0.25f],
                [0, 2, 1, 0, 3, 2]),
            SilkMeshRendererConformance.CreateMeshCommand(
                3, "/Tilted",
                [-1.25f, -0.5f, -0.4f, -0.75f, -0.5f, -0.9f, -0.75f, 0.5f, -0.9f, -1.25f, 0.5f, -0.4f],
                [0, 1, 2, 0, 2, 3]));

        SilkFrameCaptureResult capture = SilkFrameCapture.CaptureRetainedWithDepth(
            renderer, device, Width, Height, RenderSettings.Default,
            new SilkDepthCaptureOptions { IncludeNormal = true }, pageRevision: 1);
        SilkNormalCaptureResult normal = capture.Normal
            ?? throw new InvalidOperationException("An explicitly requested normal capture is missing.");
        float[] original = normal.Values.ToArray();
        SilkFrameCaptureResult withoutNormal = SilkFrameCapture.CaptureRetainedWithDepth(
            renderer, device, Width, Height, RenderSettings.Default, pageRevision: 2);
        renderer.Dispose();
        device.Dispose();

        await Assert.That(normal.Width).IsEqualTo(Width);
        await Assert.That(normal.Height).IsEqualTo(Height);
        await Assert.That(normal.Values.Length).IsEqualTo(Width * Height * 3);
        await AssertNormal(original, 16, 7, 1, 0, 0, "a quad facing the camera");
        await AssertNormal(original, 16, 20, 1, 0, 0, "a back face is flipped toward the camera");
        await AssertNormal(original, 16, 33, HalfSqrt2, 0, HalfSqrt2, "a quad tilted 45 degrees about y");
        await AssertNormal(original, 2, 7, 0, 0, 0, "the background");
        await AssertNormal(original, 16, 13, 0, 0, 0, "the gap between quads");
        for (int pixel = 0; pixel < Width * Height; pixel++)
        {
            float x = original[pixel * 3];
            float y = original[(pixel * 3) + 1];
            float z = original[(pixel * 3) + 2];
            float length = MathF.Sqrt((x * x) + (y * y) + (z * z));
            if (length != 0 && MathF.Abs(length - 1) > 1e-3f)
            {
                Assert.Fail($"Pixel {pixel} reports a normal of length {length}, which is neither zero nor unit.");
            }
        }
        await Assert.That(withoutNormal.Normal).IsNull();
        await Assert.That(normal.Values.Span.SequenceEqual(original)).IsTrue()
            .Because("later captures and renderer/device disposal must not overwrite owned normals");
    }

    [Test]
    [Arguments(SilkGraphicsBackend.D3D12)]
    [Arguments(SilkGraphicsBackend.Vulkan)]
    public async Task EmptySceneCapturesAnAllZeroNormalPlane(SilkGraphicsBackend backend)
    {
        using ISilkGraphicsDevice device = SilkDepthCaptureConformance.CreateDevice(backend);
        using var renderer = new SilkMeshRenderer(device);
        SilkMeshRendererConformance.Apply(renderer, 1, SideCameraFrame());

        SilkFrameCaptureResult capture = SilkFrameCapture.CaptureRetainedWithDepth(
            renderer, device, Width, Height, RenderSettings.Default,
            new SilkDepthCaptureOptions { IncludeNormal = true }, pageRevision: 1);

        await Assert.That(capture.Normal).IsNotNull();
        await Assert.That(capture.Normal!.Values.ToArray().All(static value => value == 0)).IsTrue();
    }

    private static async Task AssertNormal(
        float[] values, int row, int column, float x, float y, float z, string because)
    {
        int offset = ((row * Width) + column) * 3;
        await Assert.That(values[offset]).IsEqualTo(x).Within(Tolerance).Because(because);
        await Assert.That(values[offset + 1]).IsEqualTo(y).Within(Tolerance).Because(because);
        await Assert.That(values[offset + 2]).IsEqualTo(z).Within(Tolerance).Because(because);
    }

    // The depth conformance orthographic camera (near 1, far 11), turned to sit at world
    // x = +2 and look down -x: eye x is world -z, eye y is world y and eye z is world x - 2.
    private static byte[] SideCameraFrame()
    {
        byte[] frame = SilkDepthCaptureConformance.CreateFrame(perspective: false);
        double[] view = [0, 0, 1, 0, 0, 1, 0, 0, -1, 0, 0, 0, 0, 0, -2, 1];
        for (int index = 0; index < view.Length; index++)
        {
            BinaryPrimitives.WriteDoubleLittleEndian(frame.AsSpan(16 + (index * sizeof(double))), view[index]);
        }
        return frame;
    }
}
