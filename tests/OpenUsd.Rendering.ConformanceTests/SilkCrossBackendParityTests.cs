// Copyright (c) marcschier. Licensed under the MIT License.

using System.Buffers.Binary;
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
    /// The parity scene must be vertically asymmetric, otherwise a backend rendering it upside
    /// down would still compare equal and the gate above would pass vacuously. This is exactly
    /// how the Vulkan clip-space flip survived undetected: a cube centred on Y is its own
    /// mirror. Comparing the baseline against its own vertical mirror proves the scene can
    /// distinguish the two orientations.
    /// </summary>
    [Test]
    public async Task ComparisonDetectsAVerticallyFlippedRender()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        ParityImage baseline = RenderScene(
            D3D12SilkGraphicsDevice.Create(useWarp: true));

        ParityComparisonResult result = ParityImageComparer.Compare(
            baseline,
            MirrorVertically(baseline),
            Background,
            ParityTolerance.Geometry);

        await Assert.That(result.Passed)
            .IsFalse()
            .Because("The parity scene is vertically symmetric and cannot detect a flip.");
    }

    private static ParityImage MirrorVertically(ParityImage image)
    {
        int stride = image.Width * ParityImage.BytesPerPixel;
        ReadOnlySpan<byte> source = image.Rgba.Span;
        byte[] mirrored = new byte[source.Length];
        for (int row = 0; row < image.Height; row++)
        {
            source.Slice(row * stride, stride)
                .CopyTo(mirrored.AsSpan((image.Height - 1 - row) * stride, stride));
        }

        return new ParityImage(image.Width, image.Height, mirrored);
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

    /// <summary>
    /// Shared point-instanced prototypes should preserve image coverage across backends.
    /// D3D12 and Vulkan both use one hardware-instanced draw; the Vulkan assertion
    /// catches descriptor or instance-index regressions under SwiftShader.
    /// </summary>
    [Test]
    public async Task BackendsAgreeOnPointInstancedPrototypes()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        (ParityImage direct3D, int direct3DDraws) = RenderInstances(
            D3D12SilkGraphicsDevice.Create(useWarp: true));
        (ParityImage vulkan, int vulkanDraws) = RenderInstances(
            VulkanSilkGraphicsDevice.Create());

        await Assert.That(direct3DDraws).IsEqualTo(1);
        await Assert.That(vulkanDraws).IsEqualTo(1);

        ParityComparisonResult result = ParityImageComparer.Compare(
            direct3D,
            vulkan,
            Background,
            ParityTolerance.Geometry);

        await Assert.That(result.ReferenceCoveragePixels).IsGreaterThan(0);
        await Assert.That(result.Passed)
            .IsTrue()
            .Because($"Instanced geometry differs between backends: {result.Diagnostics}");
    }

    private static (ParityImage Image, int Draws) RenderInstances(ISilkGraphicsDevice device)
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
                CreateInstanceCommand(1, "/Proto", 0, -0.5),
                CreateInstanceCommand(1, "/Proto", 1, 0.0),
                CreateInstanceCommand(1, "/Proto", 2, 0.5));
            SilkMeshRenderResult render = renderer.Render(color, depth);

            byte[] pixels = new byte[Size * Size * ParityImage.BytesPerPixel];
            color.ReadbackForTesting(pixels);
            return (
                new ParityImage(checked((int)Size), checked((int)Size), pixels),
                render.DrawCount);
        }
    }

    /// <summary>
    /// Builds one instance record of a prototype: the path stays authoritative, the instance
    /// index distinguishes the record, and a non-zero instancer id identifies the owner.
    /// </summary>
    private static byte[] CreateInstanceCommand(
        ulong id,
        string path,
        int instanceIndex,
        double x)
    {
        byte[] command = SilkMeshRendererConformance.CreateCubeCommand(
            id,
            path,
            x,
            0.25,
            [0, 0, 1, 1]);
        BinaryPrimitives.WriteInt32LittleEndian(command.AsSpan(20), 7);
        BinaryPrimitives.WriteInt32LittleEndian(command.AsSpan(24), instanceIndex);
        if (instanceIndex == 0)
        {
            return command;
        }

        int pathLength = BinaryPrimitives.ReadInt32LittleEndian(command.AsSpan(48));
        var lightweight = new byte[224 + pathLength];
        command.AsSpan(0, 224).CopyTo(lightweight);
        BinaryPrimitives.WriteUInt32LittleEndian(lightweight.AsSpan(4), (uint)lightweight.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(lightweight.AsSpan(52), 0);
        BinaryPrimitives.WriteUInt32LittleEndian(lightweight.AsSpan(56), 0);
        BinaryPrimitives.WriteUInt32LittleEndian(lightweight.AsSpan(60), 0);
        BinaryPrimitives.WriteUInt64LittleEndian(lightweight.AsSpan(208), 0);
        BinaryPrimitives.WriteUInt32LittleEndian(lightweight.AsSpan(216), 0);
        BinaryPrimitives.WriteUInt32LittleEndian(lightweight.AsSpan(220), 0);
        command.AsSpan(224, pathLength).CopyTo(lightweight.AsSpan(224));
        return lightweight;
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
                points[offset + 1] = (((float)row / (resolution - 1)) - 0.2f) * 0.8f;
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
                // Offset in Y as well as X. A cube centred on Y is vertically symmetric, so
                // a backend rendering the scene upside down would still compare equal.
                SilkMeshRendererConformance.CreateCubeCommand(
                    1,
                    "/Cube",
                    cubeX,
                    0.3,
                    [0, 0, 1, 1]));
            renderer.Render(color, depth);

            byte[] pixels = new byte[Size * Size * ParityImage.BytesPerPixel];
            color.ReadbackForTesting(pixels);
            return new ParityImage(checked((int)Size), checked((int)Size), pixels);
        }
    }
}
