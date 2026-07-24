// Copyright (c) marcschier. Licensed under the MIT License.

using System.Buffers.Binary;
using System.Runtime.Versioning;
using System.Text;
using OpenUsd.Rendering.Silk;
using OpenUsd.Rendering.Silk.Metal;

namespace OpenUsd.Rendering.ConformanceTests;

[NotInParallel]
public sealed class MetalPickingConformanceTests
{
    [Test]
    [SupportedOSPlatform("macos")]
    public async Task PersistentReadbackRingSaturatesAndReusesOnMacOS()
    {
        if (!OperatingSystem.IsMacOS())
        {
            return;
        }
        RequirePinnedMetalLibrary();

        using MetalSilkGraphicsDevice device = MetalSilkGraphicsDevice.Create();
        using ISilkPickGraphicsPipeline pipeline =
            device.CreatePickGraphicsPipeline(
                SilkPickPipelineDescriptor.CreateChecked(
                    SilkShaderBinaryFormat.MetalLibrary));
        using var ring = new SilkPickReadbackRing(device);
        var reservations = new SilkPickReadbackReservation[ring.Capacity];
        var buffers = new HashSet<ISilkPickReadbackBuffer>(
            ReferenceEqualityComparer.Instance);

        for (int index = 0; index < reservations.Length; index++)
        {
            if (!ring.TryAcquire(out reservations[index]))
            {
                throw new InvalidOperationException(
                    "The persistent Metal readback ring saturated too early.");
            }
            if (reservations[index].Buffer.ByteSize !=
                SilkPickTokenEncoding.ByteSize)
            {
                throw new InvalidOperationException(
                    "A Metal pick readback did not expose exactly four RGBA bytes.");
            }
            _ = buffers.Add(reservations[index].Buffer);
        }

        await Assert.That(ring.TryAcquire(out _)).IsFalse();
        await Assert.That(buffers.Count).IsEqualTo(3);
        await Assert.That(device.PickPipelineCreationCount).IsEqualTo(1L);
        await Assert.That(device.PickReadbackBufferCreationCount).IsEqualTo(3L);

        foreach (SilkPickReadbackReservation reservation in reservations)
        {
            ring.Cancel(reservation);
        }
        for (int iteration = 0; iteration < 100; iteration++)
        {
            if (!ring.TryAcquire(out SilkPickReadbackReservation reservation))
            {
                throw new InvalidOperationException(
                    "The Metal readback ring did not reuse a released slot.");
            }
            if (!buffers.Contains(reservation.Buffer))
            {
                throw new InvalidOperationException(
                    "The Metal readback ring allocated a warm replacement buffer.");
            }
            ring.Cancel(reservation);
        }

        await Assert.That(device.PickReadbackBufferCreationCount).IsEqualTo(3L);
    }

    [Test]
    [SupportedOSPlatform("macos")]
    public async Task PicksDepthCoordinatesMissAndPreservesVisibleFrameOnMacOS()
    {
        if (!OperatingSystem.IsMacOS())
        {
            return;
        }
        RequirePinnedMetalLibrary();

        const uint size = 64;
        using MetalSilkGraphicsDevice device = MetalSilkGraphicsDevice.Create();
        using var renderer = new SilkMeshRenderer(device);
        using ISilkGraphicsTexture color = CreateColorTarget(device, size, size);
        using ISilkGraphicsTexture depth = device.CreateTexture2D(
            SilkTextureDescriptor.DepthTarget(size, size));
        SilkMeshRendererConformance.Apply(
            renderer,
            revision: 1,
            SilkMeshRendererConformance.CreateFrameCommand(
                size,
                size,
                Identity()),
            CreateTriangle(
                1,
                "/Far",
                z: 0.8f,
                [-0.55f, -0.55f, 0, 0.55f, 0.55f, -0.55f],
                [1, 0, 0, 1]),
            CreateTriangle(
                2,
                "/Near",
                z: 0.2f,
                [-0.55f, -0.55f, 0, 0.55f, 0.55f, -0.55f],
                [0, 1, 0, 1]),
            CreateQuad(
                3,
                "/TopLeft",
                z: 0.3f,
                left: -0.95f,
                top: 0.95f,
                right: -0.55f,
                bottom: 0.55f,
                [0, 0, 1, 1]),
            CreateQuad(
                4,
                "/BottomRight",
                z: 0.3f,
                left: 0.55f,
                top: -0.55f,
                right: 0.95f,
                bottom: -0.95f,
                [1, 1, 0, 1]));
        var binding = new SilkPickFrameBinding(5, 9);

        _ = renderer.Render(color, depth, binding);
        byte[] visibleBefore = ReadPixels(color);

        RenderPickResult nearest = await Pick(
            renderer,
            color,
            depth,
            binding,
            x: 32,
            y: 32,
            size,
            size);
        long pipelineCount = device.PickPipelineCreationCount;
        long readbackCount = device.PickReadbackBufferCreationCount;
        ulong targetCount = renderer.PickingStatistics.TargetCreations;
        ulong geometryBuilds = renderer.GpuResources.Statistics.GeometryBuilds;
        RenderPickResult topLeft = await Pick(
            renderer,
            color,
            depth,
            binding,
            x: 4,
            y: 4,
            size,
            size);
        RenderPickResult bottomRight = await Pick(
            renderer,
            color,
            depth,
            binding,
            x: 59,
            y: 59,
            size,
            size);
        RenderPickResult miss = await Pick(
            renderer,
            color,
            depth,
            binding,
            x: 32,
            y: 4,
            size,
            size);
        byte[] visibleAfter = ReadPixels(color);

        await AssertHit(nearest, "/Near");
        await AssertHit(topLeft, "/TopLeft");
        await AssertHit(bottomRight, "/BottomRight");
        await Assert.That(miss.Status).IsEqualTo(RenderPickStatus.Miss);
        await AssertNullableGeometry(miss);
        await Assert.That(visibleAfter.AsSpan().SequenceEqual(visibleBefore))
            .IsTrue();
        await Assert.That(device.PickPipelineCreationCount)
            .IsEqualTo(pipelineCount);
        await Assert.That(device.PickReadbackBufferCreationCount)
            .IsEqualTo(readbackCount);
        await Assert.That(renderer.PickingStatistics.TargetCreations)
            .IsEqualTo(targetCount);
        await Assert.That(renderer.GpuResources.Statistics.GeometryBuilds)
            .IsEqualTo(geometryBuilds);
    }

    [Test]
    [SupportedOSPlatform("macos")]
    public async Task ResizeAndCommandFailureGenerationReturnStaleOnMacOS()
    {
        if (!OperatingSystem.IsMacOS())
        {
            return;
        }
        RequirePinnedMetalLibrary();

        const uint width = 64;
        const uint height = 64;
        using MetalSilkGraphicsDevice device = MetalSilkGraphicsDevice.Create();
        using var renderer = new SilkMeshRenderer(device);
        using ISilkGraphicsTexture color = CreateColorTarget(device, width, height);
        using ISilkGraphicsTexture depth = device.CreateTexture2D(
            SilkTextureDescriptor.DepthTarget(width, height));
        SilkMeshRendererConformance.Apply(
            renderer,
            revision: 1,
            SilkMeshRendererConformance.CreateFrameCommand(
                width,
                height,
                Identity()),
            CreateTriangle(
                1,
                "/Triangle",
                z: 0.2f,
                [-0.7f, -0.7f, 0, 0.7f, 0.7f, -0.7f],
                [0, 1, 0, 1]));
        var binding = new SilkPickFrameBinding(5, 9);

        Task<RenderPickResult> resizePending = renderer.PickAsync(
            CreateRequest(32, 32, width, height, binding)).AsTask();
        _ = renderer.Render(color, depth, binding);

        const uint resizedWidth = 80;
        const uint resizedHeight = 48;
        using ISilkGraphicsTexture resizedColor = CreateColorTarget(
            device,
            resizedWidth,
            resizedHeight);
        using ISilkGraphicsTexture resizedDepth = device.CreateTexture2D(
            SilkTextureDescriptor.DepthTarget(resizedWidth, resizedHeight));
        _ = renderer.Render(resizedColor, resizedDepth, binding);
        RenderPickResult resized = await resizePending;

        Task<RenderPickResult> failurePending = renderer.PickAsync(
            CreateRequest(
                40,
                24,
                resizedWidth,
                resizedHeight,
                binding)).AsTask();
        _ = renderer.Render(resizedColor, resizedDepth, binding);
        ulong previousGeneration = device.PickDeviceGeneration;
        device.NotifyCommandBufferFailure();
        _ = renderer.Render(resizedColor, resizedDepth, binding);
        RenderPickResult failedGeneration = await failurePending;

        await Assert.That(resized.Status).IsEqualTo(RenderPickStatus.Stale);
        await Assert.That(failedGeneration.Status)
            .IsEqualTo(RenderPickStatus.Stale);
        await Assert.That(resized.StaleReasons)
            .IsEqualTo(RenderPickStaleReason.Viewport);
        await Assert.That(failedGeneration.StaleReasons)
            .IsEqualTo(RenderPickStaleReason.ContextGeneration);
        await AssertNullableGeometry(resized);
        await AssertNullableGeometry(failedGeneration);
        await Assert.That(device.PickDeviceGeneration)
            .IsEqualTo(previousGeneration + 1);
        await Assert.That(device.PickPipelineCreationCount).IsEqualTo(2L);
        await Assert.That(device.PickReadbackBufferCreationCount).IsEqualTo(6L);
        await Assert.That(renderer.PickingStatistics.TargetCreations)
            .IsEqualTo(2UL);
    }

    private static async Task<RenderPickResult> Pick(
        SilkMeshRenderer renderer,
        ISilkGraphicsTexture color,
        ISilkGraphicsTexture depth,
        SilkPickFrameBinding binding,
        int x,
        int y,
        uint width,
        uint height)
    {
        Task<RenderPickResult> pending = renderer.PickAsync(
            CreateRequest(x, y, width, height, binding)).AsTask();
        for (int frame = 0; frame < 4 && !pending.IsCompleted; frame++)
        {
            _ = renderer.Render(color, depth, binding);
            await Task.Yield();
        }
        if (!pending.IsCompleted)
        {
            throw new TimeoutException(
                "The completion-tagged Metal pick did not resolve without waiting.");
        }
        return await pending;
    }

    private static RenderPickRequest CreateRequest(
        int x,
        int y,
        uint width,
        uint height,
        SilkPickFrameBinding binding) =>
        new(
            x,
            y,
            new ViewportDimensions(checked((int)width), checked((int)height)),
            binding.StateRevision,
            binding.SceneRevision,
            RenderPickTarget.Face);

    [SupportedOSPlatform("macos")]
    private static ISilkGraphicsTexture CreateColorTarget(
        MetalSilkGraphicsDevice device,
        uint width,
        uint height) =>
        device.CreateTexture2D(new SilkTextureDescriptor(
            width,
            height,
            SilkTextureFormat.Rgba8Unorm,
            SilkTextureUsage.ColorRenderTarget | SilkTextureUsage.CopySource));

    private static byte[] CreateTriangle(
        ulong id,
        string path,
        float z,
        float[] xy,
        float[] color) =>
        CreateMesh(
            id,
            path,
            [
                xy[0], xy[1], z,
                xy[2], xy[3], z,
                xy[4], xy[5], z
            ],
            [0, 1, 2],
            color);

    private static byte[] CreateQuad(
        ulong id,
        string path,
        float z,
        float left,
        float top,
        float right,
        float bottom,
        float[] color) =>
        CreateMesh(
            id,
            path,
            [
                left, top, z,
                right, top, z,
                right, bottom, z,
                left, bottom, z
            ],
            [0, 1, 2, 0, 2, 3],
            color);

    private static byte[] CreateMesh(
        ulong id,
        string pathValue,
        float[] points,
        uint[] indices,
        float[] color)
    {
        byte[] path = Encoding.UTF8.GetBytes(pathValue);
        int triangleCount = indices.Length / 3;
        int size = 200 +
            path.Length +
            (points.Length * sizeof(float)) +
            (indices.Length * sizeof(uint)) +
            (triangleCount * sizeof(uint));
        var bytes = new byte[size];
        BinaryPrimitives.WriteUInt32LittleEndian(
            bytes,
            (uint)SilkCommandType.MeshUpsert);
        BinaryPrimitives.WriteUInt32LittleEndian(
            bytes.AsSpan(4),
            checked((uint)size));
        BinaryPrimitives.WriteUInt64LittleEndian(
            bytes.AsSpan(8),
            ComputeStableHash(pathValue));
        BinaryPrimitives.WriteInt32LittleEndian(
            bytes.AsSpan(16),
            checked((int)id));
        BinaryPrimitives.WriteUInt32LittleEndian(
            bytes.AsSpan(28),
            (uint)SilkTopologyKind.TriangleList);
        BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(32), 1);
        BinaryPrimitives.WriteUInt32LittleEndian(
            bytes.AsSpan(40),
            checked((uint)path.Length));
        BinaryPrimitives.WriteUInt32LittleEndian(
            bytes.AsSpan(44),
            checked((uint)(points.Length / 3)));
        BinaryPrimitives.WriteUInt32LittleEndian(
            bytes.AsSpan(48),
            checked((uint)indices.Length));
        BinaryPrimitives.WriteUInt32LittleEndian(
            bytes.AsSpan(52),
            checked((uint)triangleCount));
        for (int index = 0; index < color.Length; index++)
        {
            BinaryPrimitives.WriteSingleLittleEndian(
                bytes.AsSpan(56 + (index * sizeof(float))),
                color[index]);
        }
        double[] transform = Identity();
        for (int index = 0; index < transform.Length; index++)
        {
            BinaryPrimitives.WriteDoubleLittleEndian(
                bytes.AsSpan(72 + (index * sizeof(double))),
                transform[index]);
        }
        path.CopyTo(bytes, 200);
        int pointsOffset = 200 + path.Length;
        for (int index = 0; index < points.Length; index++)
        {
            BinaryPrimitives.WriteSingleLittleEndian(
                bytes.AsSpan(pointsOffset + (index * sizeof(float))),
                points[index]);
        }
        int indicesOffset = pointsOffset + (points.Length * sizeof(float));
        for (int index = 0; index < indices.Length; index++)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(
                bytes.AsSpan(indicesOffset + (index * sizeof(uint))),
                indices[index]);
        }
        int subprimsOffset = indicesOffset + (indices.Length * sizeof(uint));
        for (int triangle = 0; triangle < triangleCount; triangle++)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(
                bytes.AsSpan(subprimsOffset + (triangle * sizeof(uint))),
                checked((uint)triangle));
        }
        return bytes;
    }

    private static byte[] ReadPixels(ISilkGraphicsTexture texture)
    {
        var pixels = new byte[checked((int)(texture.Width * texture.Height * 4))];
        texture.ReadbackForTesting(pixels);
        return pixels;
    }

    private static ulong ComputeStableHash(string path)
    {
        ulong hash = 14695981039346656037;
        foreach (byte value in Encoding.UTF8.GetBytes(path))
        {
            hash ^= value;
            hash *= 1099511628211;
        }
        return hash;
    }

    private static double[] Identity() =>
    [
        1, 0, 0, 0,
        0, 1, 0, 0,
        0, 0, 1, 0,
        0, 0, 0, 1
    ];

    private static async Task AssertHit(RenderPickResult result, string path)
    {
        await Assert.That(result.Status).IsEqualTo(RenderPickStatus.Hit);
        await Assert.That(result.PrimPath).IsEqualTo(path);
        await Assert.That(result.BackendKind).IsEqualTo(RenderBackendKind.Metal);
        await Assert.That(result.BackendToken).IsNotNull();
        await AssertNullableGeometry(result);
    }

    private static async Task AssertNullableGeometry(RenderPickResult result)
    {
        await Assert.That(result.WorldPosition).IsNull();
        await Assert.That(result.WorldNormal).IsNull();
        await Assert.That(result.NormalizedDepth).IsNull();
    }

    private static void RequirePinnedMetalLibrary()
    {
        if (!SilkCheckedShaderAssets.HasPinnedMetalLibrary)
        {
            throw new InvalidOperationException(
                "Metal picking conformance requires the hosted macOS arm64 " +
                "Xcode 16.4 ten-entry mesh.metallib and validation sidecar.");
        }
    }
}
