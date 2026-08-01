// Copyright (c) marcschier. Licensed under the MIT License.

using System.Buffers.Binary;
using System.Text;
using OpenUsd.Rendering.Silk;

namespace OpenUsd.Performance.Tests;

internal static class PerformanceTestData
{
    internal static byte[] CreateFrameCommand(int width = 1920, int height = 1080)
    {
        var bytes = new byte[272];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, (uint)SilkCommandType.Frame);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), (uint)bytes.Length);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(8), width);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(12), height);
        for (int index = 0; index < 16; index++)
        {
            double value = index % 5 == 0 ? 1 : 0;
            BinaryPrimitives.WriteDoubleLittleEndian(
                bytes.AsSpan(16 + (index * sizeof(double))),
                value);
            BinaryPrimitives.WriteDoubleLittleEndian(
                bytes.AsSpan(144 + (index * sizeof(double))),
                value);
        }
        return bytes;
    }

    internal static byte[] CreateFramePage(int commandCount)
    {
        byte[] frame = CreateFrameCommand();
        var commands = new byte[commandCount][];
        Array.Fill(commands, frame);
        return Concat(commands);
    }

    internal static byte[] CreateMeshCommand(
        string pathValue = "/World/PerformanceMesh",
        int primId = 42,
        int triangleCount = 1,
        float color = 0.7f,
        string materialPath = "")
    {
        ArgumentOutOfRangeException.ThrowIfNegative(primId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(triangleCount);
        byte[] path = Encoding.UTF8.GetBytes(pathValue);
        byte[] material = Encoding.UTF8.GetBytes(materialPath);
        const int pointCount = 3;
        int indexCount = checked(triangleCount * 3);
        int size = checked(
            224 +
            path.Length +
            (pointCount * 3 * sizeof(float)) +
            (indexCount * sizeof(uint)) +
            (triangleCount * sizeof(uint)) +
            material.Length);
        var bytes = new byte[size];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, (uint)SilkCommandType.MeshUpsert);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), (uint)size);
        BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(8), ComputeStableHash(path));
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(16), primId);
        BinaryPrimitives.WriteUInt32LittleEndian(
            bytes.AsSpan(28),
            (uint)SilkTopologyKind.TriangleList);
        BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(32), 1);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(40), 1);
        BinaryPrimitives.WriteUInt32LittleEndian(
            bytes.AsSpan(44),
            (uint)SilkMeshCullStyle.BackUnlessDoubleSided);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(48), (uint)path.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(52), pointCount);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(56), (uint)indexCount);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(60), (uint)triangleCount);
        BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(208), ComputeStableHash(material));
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(216), (uint)material.Length);
        for (int component = 0; component < 4; component++)
        {
            BinaryPrimitives.WriteSingleLittleEndian(
                bytes.AsSpan(64 + (component * sizeof(float))),
                component == 3 ? 1 : color);
        }
        WriteIdentityMatrix(bytes.AsSpan(80, 16 * sizeof(double)));
        path.CopyTo(bytes, 224);

        int pointsOffset = 224 + path.Length;
        ReadOnlySpan<float> points = [0, 0, 0, 1, 0, 0, 0, 1, 0];
        for (int index = 0; index < points.Length; index++)
        {
            BinaryPrimitives.WriteSingleLittleEndian(
                bytes.AsSpan(pointsOffset + (index * sizeof(float))),
                points[index]);
        }

        int indicesOffset = pointsOffset + (pointCount * 3 * sizeof(float));
        for (int triangle = 0; triangle < triangleCount; triangle++)
        {
            int baseIndex = triangle * 3;
            BinaryPrimitives.WriteUInt32LittleEndian(
                bytes.AsSpan(indicesOffset + (baseIndex * sizeof(uint))),
                0);
            BinaryPrimitives.WriteUInt32LittleEndian(
                bytes.AsSpan(indicesOffset + ((baseIndex + 1) * sizeof(uint))),
                1);
            BinaryPrimitives.WriteUInt32LittleEndian(
                bytes.AsSpan(indicesOffset + ((baseIndex + 2) * sizeof(uint))),
                2);
        }

        int subprimsOffset = indicesOffset + (indexCount * sizeof(uint));
        for (int triangle = 0; triangle < triangleCount; triangle++)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(
                bytes.AsSpan(subprimsOffset + (triangle * sizeof(uint))),
                checked((uint)triangle));
        }
        material.CopyTo(bytes, subprimsOffset + (triangleCount * sizeof(uint)));
        return bytes;
    }

    internal static byte[] CreateMeshInstanceReferenceCommand(
        string pathValue = "/World/PerformanceMesh",
        int primId = 42,
        int instanceId = 7,
        int instanceIndex = 1,
        double x = 1)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(primId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(instanceId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(instanceIndex);
        byte[] path = Encoding.UTF8.GetBytes(pathValue);
        int size = checked(224 + path.Length);
        var bytes = new byte[size];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, (uint)SilkCommandType.MeshUpsert);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), (uint)size);
        BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(8), ComputeStableHash(path));
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(16), primId);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(20), instanceId);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(24), instanceIndex);
        BinaryPrimitives.WriteUInt32LittleEndian(
            bytes.AsSpan(28),
            (uint)SilkTopologyKind.TriangleList);
        BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(32), 1);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(40), 1);
        BinaryPrimitives.WriteUInt32LittleEndian(
            bytes.AsSpan(44),
            (uint)SilkMeshCullStyle.BackUnlessDoubleSided);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(48), (uint)path.Length);
        for (int component = 0; component < 4; component++)
        {
            BinaryPrimitives.WriteSingleLittleEndian(
                bytes.AsSpan(64 + (component * sizeof(float))),
                1);
        }
        WriteIdentityMatrix(bytes.AsSpan(80, 16 * sizeof(double)));
        BinaryPrimitives.WriteDoubleLittleEndian(bytes.AsSpan(80 + (12 * sizeof(double))), x);
        path.CopyTo(bytes, 224);
        return bytes;
    }

    internal static byte[] Concat(params byte[][] commands)
    {
        int length = 0;
        foreach (byte[] command in commands)
        {
            length = checked(length + command.Length);
        }
        var page = new byte[length];
        int offset = 0;
        foreach (byte[] command in commands)
        {
            command.CopyTo(page, offset);
            offset += command.Length;
        }
        return page;
    }

    private static ulong ComputeStableHash(ReadOnlySpan<byte> path)
    {
        ulong hash = 14695981039346656037;
        foreach (byte value in path)
        {
            hash ^= value;
            hash *= 1099511628211;
        }
        return hash;
    }

    private static void WriteIdentityMatrix(Span<byte> destination)
    {
        for (int index = 0; index < 16; index++)
        {
            BinaryPrimitives.WriteDoubleLittleEndian(
                destination.Slice(index * sizeof(double), sizeof(double)),
                index % 5 == 0 ? 1 : 0);
        }
    }
}
