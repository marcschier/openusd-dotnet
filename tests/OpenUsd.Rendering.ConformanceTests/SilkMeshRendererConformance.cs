// Copyright (c) marcschier. Licensed under the MIT License.

using System.Buffers.Binary;
using System.Text;
using OpenUsd.Rendering.Silk;

namespace OpenUsd.Rendering.ConformanceTests;

internal static class SilkMeshRendererConformance
{
    internal static async Task RendersRetainedMeshes(ISilkGraphicsDevice device)
    {
        const uint size = 64;
        using ISilkGraphicsTexture color = device.CreateTexture2D(new SilkTextureDescriptor(
            size,
            size,
            SilkTextureFormat.Rgba8Unorm,
            SilkTextureUsage.ColorRenderTarget | SilkTextureUsage.CopySource));
        using ISilkGraphicsTexture depth = device.CreateTexture2D(
            SilkTextureDescriptor.DepthTarget(size, size));
        using var renderer = new SilkMeshRenderer(device);

        Apply(
            renderer,
            revision: 1,
            CreateFrameCommand(size, size, Identity()),
            CreateCubeCommand(1, "/Cube", -0.45, 0, [0, 0, 1, 1]),
            CreateTriangleCommand(2, "/Triangle", 0.5, 0, [0, 1, 0, 1]));
        SilkMeshRenderResult first = renderer.Render(color, depth);
        byte[] firstPixels = ReadPixels(color);
        await AssertPixel(firstPixels, size, 17, 32, red: false, green: false, blue: true);
        await AssertPixel(firstPixels, size, 48, 32, red: false, green: true, blue: false);
        await AssertPixel(firstPixels, size, 2, 2, red: false, green: false, blue: false);
        await Assert.That(first.DrawCount).IsEqualTo(2);
        await Assert.That(first.UniformUploads).IsEqualTo(2);
        await Assert.That(first.Statistics.GeometryBuilds).IsEqualTo(2ul);

        Apply(
            renderer,
            revision: 2,
            CreateFrameCommand(size, size, Identity()),
            CreateCubeCommand(1, "/Cube", -0.45, 0, [1, 1, 1, 0.5f]));
        SilkMeshRenderResult colorEdit = renderer.Render(color, depth);
        byte[] colorPixels = ReadPixels(color);
        await Assert.That(PixelChannel(colorPixels, size, 17, 32, 2)).IsGreaterThan((byte)100);
        await Assert.That(PixelChannel(colorPixels, size, 17, 32, 3))
            .IsBetween((byte)120, (byte)136);
        await Assert.That(colorEdit.UniformUploads).IsEqualTo(1);
        await Assert.That(colorEdit.Statistics.GeometryBuilds).IsEqualTo(2ul);

        double[] movedView = Identity();
        movedView[12] = 0.1;
        Apply(
            renderer,
            revision: 3,
            CreateFrameCommand(size, size, movedView),
            CreateCubeCommand(1, "/Cube", 0, 0, [1, 1, 1, 0.5f]));
        SilkMeshRenderResult moved = renderer.Render(color, depth);
        byte[] movedPixels = ReadPixels(color);
        await AssertPixel(movedPixels, size, 17, 32, red: false, green: false, blue: false);
        await Assert.That(PixelChannel(movedPixels, size, 35, 32, 2)).IsGreaterThan((byte)100);
        await Assert.That(moved.UniformUploads).IsEqualTo(2);
        await Assert.That(moved.Statistics.GeometryBuilds).IsEqualTo(2ul);

        SilkMeshRenderResult steady = renderer.Render(color, depth);
        await Assert.That(steady.UniformUploads).IsEqualTo(0);
        await Assert.That(steady.Statistics.GeometryBuilds).IsEqualTo(2ul);

        const uint resizedWidth = 80;
        const uint resizedHeight = 48;
        using (ISilkGraphicsTexture resizedColor =
            device.CreateTexture2D(new SilkTextureDescriptor(
                resizedWidth,
                resizedHeight,
                SilkTextureFormat.Rgba8Unorm,
                SilkTextureUsage.ColorRenderTarget | SilkTextureUsage.CopySource)))
        using (ISilkGraphicsTexture resizedDepth =
            device.CreateTexture2D(SilkTextureDescriptor.DepthTarget(
                resizedWidth,
                resizedHeight)))
        {
            Apply(
                renderer,
                revision: 4,
                CreateFrameCommand(resizedWidth, resizedHeight, movedView));
            SilkMeshRenderResult resized = renderer.Render(resizedColor, resizedDepth);
            await Assert.That(resized.DrawCount).IsEqualTo(2);
            await Assert.That(resized.Statistics.GeometryBuilds).IsEqualTo(2ul);
            await Assert.That(CountColoredPixels(ReadPixels(resizedColor))).IsGreaterThan(0);
        }

        Apply(
            renderer,
            revision: 5,
            CreateFrameCommand(size, size, movedView),
            CreateRemoveCommand(2, "/Triangle"));
        SilkMeshRenderResult removed = renderer.Render(color, depth);
        byte[] removedPixels = ReadPixels(color);
        await AssertPixel(removedPixels, size, 51, 32, red: false, green: false, blue: false);
        await Assert.That(removed.DrawCount).IsEqualTo(1);
        await Assert.That(removed.Statistics.MeshCount).IsEqualTo(1);

        Apply(renderer, revision: 6, CreateRemoveCommand(1, "/Cube"));
        SilkMeshRenderResult empty = renderer.Render(color, depth);
        byte[] emptyPixels = ReadPixels(color);
        await Assert.That(empty.DrawCount).IsEqualTo(0);
        await Assert.That(empty.Statistics.MeshCount).IsEqualTo(0);
        await Assert.That(emptyPixels.Any(channel => channel != 0 && channel != byte.MaxValue)).IsFalse();

        Apply(
            renderer,
            revision: 7,
            CreateFrameCommand(size, size, Identity()),
            CreateBoundaryCubeCommand());
        _ = renderer.Render(color, depth);
        await Assert.That(CountColoredPixels(ReadPixels(color))).IsGreaterThan(100);

        renderer.Dispose();
        await Assert.That(() => renderer.Render(color, depth)).Throws<ObjectDisposedException>();
    }

    internal static async Task RejectsCrossDeviceTargets(
        ISilkGraphicsDevice rendererDevice,
        ISilkGraphicsDevice targetDevice)
    {
        using var renderer = new SilkMeshRenderer(rendererDevice);
        using ISilkGraphicsTexture color = targetDevice.CreateTexture2D(new SilkTextureDescriptor(
            16,
            16,
            SilkTextureFormat.Rgba8Unorm,
            SilkTextureUsage.ColorRenderTarget | SilkTextureUsage.CopySource));
        using ISilkGraphicsTexture depth = targetDevice.CreateTexture2D(
            SilkTextureDescriptor.DepthTarget(16, 16));

        await Assert.That(() => renderer.Render(color, depth)).Throws<ArgumentException>();
    }

    internal static void Apply(
        SilkMeshRenderer renderer,
        ulong revision,
        params byte[][] commands)
    {
        int length = commands.Sum(command => command.Length);
        var page = new byte[length];
        int offset = 0;
        foreach (byte[] command in commands)
        {
            command.CopyTo(page, offset);
            offset += command.Length;
        }
        SilkSceneDelta delta = renderer.Scene.Apply(page, checked((uint)commands.Length), revision);
        renderer.GpuResources.Apply(renderer.Scene, delta);
    }

    internal static byte[] CreateFrameCommand(uint width, uint height, double[] view)
    {
        var bytes = new byte[272];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, (uint)SilkCommandType.Frame);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), (uint)bytes.Length);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(8), checked((int)width));
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(12), checked((int)height));
        double[] projection = Identity();
        for (int i = 0; i < 16; i++)
        {
            BinaryPrimitives.WriteDoubleLittleEndian(bytes.AsSpan(16 + (i * 8)), view[i]);
            BinaryPrimitives.WriteDoubleLittleEndian(
                bytes.AsSpan(144 + (i * 8)),
                projection[i]);
        }
        return bytes;
    }

    internal static byte[] CreateCubeCommand(
        ulong id,
        string path,
        double x,
        double y,
        float[] color)
    {
        float[] points =
        [
            -0.25f, -0.25f, 0.2f,
             0.25f, -0.25f, 0.2f,
             0.25f,  0.25f, 0.2f,
            -0.25f,  0.25f, 0.2f,
            -0.25f, -0.25f, 0.6f,
             0.25f, -0.25f, 0.6f,
             0.25f,  0.25f, 0.6f,
            -0.25f,  0.25f, 0.6f,
        ];
        uint[] indices =
        [
            0, 2, 1, 0, 3, 2,
            4, 5, 6, 4, 6, 7,
            0, 1, 5, 0, 5, 4,
            1, 2, 6, 1, 6, 5,
            2, 3, 7, 2, 7, 6,
            3, 0, 4, 3, 4, 7,
        ];
        return CreateMeshCommand(id, path, points, indices, x, y, color);
    }

    private static byte[] CreateTriangleCommand(
        ulong id,
        string path,
        double x,
        double y,
        float[] color) =>
        CreateMeshCommand(
            id,
            path,
            [-0.3f, -0.3f, 0.1f, 0, 0.3f, 0.5f, 0.3f, -0.3f, 0.1f],
            [0, 1, 2],
            x,
            y,
            color);

    private static byte[] CreateBoundaryCubeCommand() =>
        CreateMeshCommand(
            3,
            "/BoundaryCube",
            [
                 1,  1,  1,
                -1,  1,  1,
                -1, -1,  1,
                 1, -1,  1,
                -1, -1, -1,
                -1,  1, -1,
                 1,  1, -1,
                 1, -1, -1,
            ],
            [
                0, 1, 2, 0, 2, 3,
                4, 5, 6, 4, 6, 7,
                0, 6, 5, 0, 5, 1,
                4, 7, 3, 4, 3, 2,
                0, 3, 7, 0, 7, 6,
                4, 2, 1, 4, 1, 5,
            ],
            0,
            0,
            [0.7f, 0.7f, 0.7f, 1]);

    private static byte[] CreateMeshCommand(
        ulong id,
        string pathValue,
        float[] points,
        uint[] indices,
        double x,
        double y,
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
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, (uint)SilkCommandType.MeshUpsert);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), (uint)size);
        BinaryPrimitives.WriteUInt64LittleEndian(
            bytes.AsSpan(8),
            ComputeStableHash(pathValue));
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(16), checked((int)id));
        BinaryPrimitives.WriteUInt32LittleEndian(
            bytes.AsSpan(28),
            (uint)SilkTopologyKind.TriangleList);
        BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(32), 1);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(40), (uint)path.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(44), (uint)(points.Length / 3));
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(48), (uint)indices.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(
            bytes.AsSpan(52),
            checked((uint)triangleCount));
        for (int i = 0; i < 4; i++)
        {
            BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(56 + (i * 4)), color[i]);
        }
        double[] transform = Identity();
        transform[12] = x;
        transform[13] = y;
        for (int i = 0; i < 16; i++)
        {
            BinaryPrimitives.WriteDoubleLittleEndian(
                bytes.AsSpan(72 + (i * 8)),
                transform[i]);
        }
        path.CopyTo(bytes, 200);
        int pointsOffset = 200 + path.Length;
        for (int i = 0; i < points.Length; i++)
        {
            BinaryPrimitives.WriteSingleLittleEndian(
                bytes.AsSpan(pointsOffset + (i * 4)),
                points[i]);
        }
        int indicesOffset = pointsOffset + (points.Length * sizeof(float));
        for (int i = 0; i < indices.Length; i++)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(
                bytes.AsSpan(indicesOffset + (i * 4)),
                indices[i]);
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

    private static byte[] CreateRemoveCommand(ulong id, string pathValue)
    {
        byte[] path = Encoding.UTF8.GetBytes(pathValue);
        var bytes = new byte[20 + path.Length];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, (uint)SilkCommandType.MeshRemove);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), (uint)bytes.Length);
        BinaryPrimitives.WriteUInt64LittleEndian(
            bytes.AsSpan(8),
            ComputeStableHash(pathValue));
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(16), (uint)path.Length);
        path.CopyTo(bytes, 20);
        return bytes;
    }

    private static byte[] ReadPixels(ISilkGraphicsTexture color)
    {
        var pixels = new byte[checked((int)(color.Width * color.Height * 4))];
        color.ReadbackForTesting(pixels);
        return pixels;
    }

    private static int CountColoredPixels(byte[] pixels)
    {
        int count = 0;
        for (int offset = 0; offset < pixels.Length; offset += 4)
        {
            if (pixels[offset] != 0 || pixels[offset + 1] != 0 || pixels[offset + 2] != 0)
            {
                count++;
            }
        }
        return count;
    }

    private static byte PixelChannel(
        byte[] pixels,
        uint width,
        int x,
        int y,
        int channel) =>
        pixels[checked((((y * (int)width) + x) * 4) + channel)];

    private static async Task AssertPixel(
        byte[] pixels,
        uint width,
        int x,
        int y,
        bool red,
        bool green,
        bool blue)
    {
        int offset = checked(((y * (int)width) + x) * 4);
        string evidence =
            $"Pixel ({x},{y}) was rgba({pixels[offset]},{pixels[offset + 1]}," +
            $"{pixels[offset + 2]},{pixels[offset + 3]}).";
        await Assert.That(pixels[offset] > 100).IsEqualTo(red).Because(evidence);
        await Assert.That(pixels[offset + 1] > 100).IsEqualTo(green).Because(evidence);
        await Assert.That(pixels[offset + 2] > 100).IsEqualTo(blue).Because(evidence);
        await Assert.That(pixels[offset + 3]).IsGreaterThan((byte)100);
    }

    internal static double[] Identity() =>
    [
        1, 0, 0, 0,
        0, 1, 0, 0,
        0, 0, 1, 0,
        0, 0, 0, 1,
    ];

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
}
