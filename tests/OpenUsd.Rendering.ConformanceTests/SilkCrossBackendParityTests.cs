// Copyright (c) marcschier. Licensed under the MIT License.

using OpenUsd.Rendering.Silk;
using OpenUsd.Rendering.Silk.D3D12;
using OpenUsd.Rendering.Silk.Vulkan;

namespace OpenUsd.Rendering.ConformanceTests;

/// <summary>
/// Cross-backend parity: the hdSilk backends must agree geometrically on the same retained
/// scene. Storm remains the eventual reference, but the backends must first agree with each
/// other, and this is the gate that catches a backend-only regression such as an index format
/// or vertex layout that only one RHI got right.
/// </summary>
[NotInParallel]
public sealed class SilkCrossBackendParityTests
{
    private const uint Size = 128;
    private const uint Background = 0x000000FFU;

    [Test]
    public async Task WarpAndSwiftShaderAgreeOnRetainedGeometry()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        ParityImage direct3D = RenderScene(D3D12SilkGraphicsDevice.Create(useWarp: true));
        ParityImage vulkan = RenderScene(VulkanSilkGraphicsDevice.Create());

        ParityComparisonResult result = ParityImageComparer.Compare(
            direct3D,
            vulkan,
            Background,
            ParityTolerance.Geometry);

        await Assert.That(result.ReferenceCoveragePixels).IsGreaterThan(0);
        await Assert.That(result.CandidateCoveragePixels).IsGreaterThan(0);
        await Assert.That(result.Passed)
            .IsTrue()
            .Because($"D3D12 and Vulkan disagree: {result.Diagnostics}");
    }

    [Test]
    public async Task ComparisonDetectsAMovedMesh()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        ParityImage baseline = RenderScene(
            D3D12SilkGraphicsDevice.Create(useWarp: true));
        ParityImage moved = RenderScene(
            D3D12SilkGraphicsDevice.Create(useWarp: true),
            cubeX: 0.35);

        ParityComparisonResult result = ParityImageComparer.Compare(
            baseline,
            moved,
            Background,
            ParityTolerance.Geometry);

        await Assert.That(result.Passed)
            .IsFalse()
            .Because("A displaced mesh must not compare equal to the baseline.");
    }

    /// <summary>
    /// A grid with more than 65,535 vertices cannot be drawn with 16-bit indices. Page ABI 3
    /// widened indices to 32 bits end to end, and this locks that in: a backend that silently
    /// truncated them would disagree with the one that did not.
    /// </summary>
    [Test]
    public async Task BackendsAgreeOnAMeshBeyondTheSixteenBitIndexCeiling()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        (float[] points, uint[] indices) = CreateGrid(258);
        await Assert.That(points.Length / 3).IsGreaterThan(ushort.MaxValue);

        ParityImage direct3D = RenderGrid(
            D3D12SilkGraphicsDevice.Create(useWarp: true),
            points,
            indices);
        ParityImage vulkan = RenderGrid(
            VulkanSilkGraphicsDevice.Create(),
            points,
            indices);

        ParityComparisonResult result = ParityImageComparer.Compare(
            direct3D,
            vulkan,
            Background,
            ParityTolerance.Geometry);

        await Assert.That(result.ReferenceCoveragePixels).IsGreaterThan(0);
        await Assert.That(result.Passed)
            .IsTrue()
            .Because($"Wide-index geometry differs between backends: {result.Diagnostics}");
    }

    private static (float[] Points, uint[] Indices) CreateGrid(int resolution)
    {
        float[] points = new float[resolution * resolution * 3];
        for (int row = 0; row < resolution; row++)
        {
            for (int column = 0; column < resolution; column++)
            {
                int offset = ((row * resolution) + column) * 3;
                points[offset] = ((float)column / (resolution - 1) - 0.5f) * 0.8f;
                points[offset + 1] = ((float)row / (resolution - 1) - 0.5f) * 0.8f;
                points[offset + 2] = 0.4f;
            }
        }

        int quads = (resolution - 1) * (resolution - 1);
        uint[] indices = new uint[quads * 6];
        int index = 0;
        for (int row = 0; row < resolution - 1; row++)
        {
            for (int column = 0; column < resolution - 1; column++)
            {
                uint topLeft = (uint)((row * resolution) + column);
                uint topRight = topLeft + 1;
                uint bottomLeft = topLeft + (uint)resolution;
                uint bottomRight = bottomLeft + 1;
                indices[index++] = topLeft;
                indices[index++] = bottomLeft;
                indices[index++] = topRight;
                indices[index++] = topRight;
                indices[index++] = bottomLeft;
                indices[index++] = bottomRight;
            }
        }

        return (points, indices);
    }

    private static ParityImage RenderGrid(
        ISilkGraphicsDevice device,
        float[] points,
        uint[] indices)
    {
        using (device)
        {
            using ISilkGraphicsTexture color = device.CreateTexture2D(
                new SilkTextureDescriptor(
                    Size,
                    Size,
                    SilkTextureFormat.Rgba8Unorm,
                    SilkTextureUsage.ColorRenderTarget | SilkTextureUsage.CopySource));
            using ISilkGraphicsTexture depth = device.CreateTexture2D(
                SilkTextureDescriptor.DepthTarget(Size, Size));
            using var renderer = new SilkMeshRenderer(device);

            SilkMeshRendererConformance.Apply(
                renderer,
                revision: 1,
                SilkMeshRendererConformance.CreateFrameCommand(
                    Size,
                    Size,
                    SilkMeshRendererConformance.Identity()),
                SilkMeshRendererConformance.CreateMeshCommand(
                    1,
                    "/Grid",
                    points,
                    indices,
                    0,
                    0,
                    [1, 1, 1, 1]));
            renderer.Render(color, depth);

            byte[] pixels = new byte[Size * Size * ParityImage.BytesPerPixel];
            color.ReadbackForTesting(pixels);
            return new ParityImage(checked((int)Size), checked((int)Size), pixels);
        }
    }

    private static ParityImage RenderScene(
        ISilkGraphicsDevice device,
        double cubeX = -0.45)
    {
        using (device)
        {
            using ISilkGraphicsTexture color = device.CreateTexture2D(
                new SilkTextureDescriptor(
                    Size,
                    Size,
                    SilkTextureFormat.Rgba8Unorm,
                    SilkTextureUsage.ColorRenderTarget | SilkTextureUsage.CopySource));
            using ISilkGraphicsTexture depth = device.CreateTexture2D(
                SilkTextureDescriptor.DepthTarget(Size, Size));
            using var renderer = new SilkMeshRenderer(device);

            SilkMeshRendererConformance.Apply(
                renderer,
                revision: 1,
                SilkMeshRendererConformance.CreateFrameCommand(
                    Size,
                    Size,
                    SilkMeshRendererConformance.Identity()),
                SilkMeshRendererConformance.CreateCubeCommand(
                    1,
                    "/Cube",
                    cubeX,
                    0,
                    [0, 0, 1, 1]));
            renderer.Render(color, depth);

            byte[] pixels = new byte[Size * Size * ParityImage.BytesPerPixel];
            color.ReadbackForTesting(pixels);
            return new ParityImage(checked((int)Size), checked((int)Size), pixels);
        }
    }
}
