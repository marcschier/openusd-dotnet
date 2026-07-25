// Copyright (c) marcschier. Licensed under the MIT License.

using System.Buffers.Binary;

namespace OpenUsd.Rendering.Silk;

internal sealed record SilkMeshGeometry(float[] Vertices, uint[] Indices)
{
    internal uint IndexCount => checked((uint)Indices.Length);
}

internal static class SilkMeshGeometryBuilder
{
    private const double NormalEpsilonSquared = 1e-30;

    internal static SilkMeshGeometry Build(SilkMeshData mesh)
    {
        ArgumentNullException.ThrowIfNull(mesh);
        ReadOnlySpan<float> points = mesh.Points.Span;
        ReadOnlySpan<uint> meshIndices = mesh.Indices.Span;
        if (points.Length % 3 != 0)
        {
            throw InvalidMesh(mesh, "point component count must be divisible by three");
        }
        if (meshIndices.Length % 3 != 0)
        {
            throw InvalidMesh(mesh, "index count must be divisible by three");
        }

        int pointCount = points.Length / 3;

        for (int i = 0; i < points.Length; i++)
        {
            if (!float.IsFinite(points[i]))
            {
                throw InvalidMesh(mesh, $"point component {i} is not finite");
            }
        }

        uint[] indices = new uint[meshIndices.Length];
        double[] normals = new double[points.Length];
        for (int triangle = 0; triangle < meshIndices.Length; triangle += 3)
        {
            int a = ValidateIndex(mesh, meshIndices, pointCount, triangle);
            int b = ValidateIndex(mesh, meshIndices, pointCount, triangle + 1);
            int c = ValidateIndex(mesh, meshIndices, pointCount, triangle + 2);
            indices[triangle] = checked((uint)a);
            indices[triangle + 1] = checked((uint)b);
            indices[triangle + 2] = checked((uint)c);

            int pa = a * 3;
            int pb = b * 3;
            int pc = c * 3;
            double abx = points[pb] - points[pa];
            double aby = points[pb + 1] - points[pa + 1];
            double abz = points[pb + 2] - points[pa + 2];
            double acx = points[pc] - points[pa];
            double acy = points[pc + 1] - points[pa + 1];
            double acz = points[pc + 2] - points[pa + 2];
            double nx = (aby * acz) - (abz * acy);
            double ny = (abz * acx) - (abx * acz);
            double nz = (abx * acy) - (aby * acx);
            double lengthSquared = (nx * nx) + (ny * ny) + (nz * nz);
            if (!double.IsFinite(lengthSquared) || lengthSquared <= NormalEpsilonSquared)
            {
                continue;
            }

            double inverseLength = 1.0 / Math.Sqrt(lengthSquared);
            nx *= inverseLength;
            ny *= inverseLength;
            nz *= inverseLength;
            Accumulate(normals, pa, nx, ny, nz);
            Accumulate(normals, pb, nx, ny, nz);
            Accumulate(normals, pc, nx, ny, nz);
        }

        float[] vertices = new float[checked(pointCount * 6)];
        for (int point = 0; point < pointCount; point++)
        {
            int source = point * 3;
            int destination = point * 6;
            vertices[destination] = points[source];
            vertices[destination + 1] = points[source + 1];
            vertices[destination + 2] = points[source + 2];

            double nx = normals[source];
            double ny = normals[source + 1];
            double nz = normals[source + 2];
            double lengthSquared = (nx * nx) + (ny * ny) + (nz * nz);
            if (!double.IsFinite(lengthSquared) || lengthSquared <= NormalEpsilonSquared)
            {
                vertices[destination + 5] = 1f;
                continue;
            }

            double inverseLength = 1.0 / Math.Sqrt(lengthSquared);
            vertices[destination + 3] = checked((float)(nx * inverseLength));
            vertices[destination + 4] = checked((float)(ny * inverseLength));
            vertices[destination + 5] = checked((float)(nz * inverseLength));
        }

        return new SilkMeshGeometry(vertices, indices);
    }

    private static int ValidateIndex(
        SilkMeshData mesh,
        ReadOnlySpan<uint> indices,
        int pointCount,
        int offset)
    {
        uint index = indices[offset];
        if (index >= pointCount)
        {
            throw InvalidMesh(
                mesh,
                $"index {offset} references vertex {index}, but the mesh has {pointCount} vertices");
        }
        return checked((int)index);
    }

    private static void Accumulate(
        double[] normals,
        int offset,
        double x,
        double y,
        double z)
    {
        normals[offset] += x;
        normals[offset + 1] += y;
        normals[offset + 2] += z;
    }

    private static InvalidDataException InvalidMesh(SilkMeshData mesh, string detail) =>
        new($"Mesh {mesh.Id} ('{mesh.Path}') is invalid: {detail}.");
}

internal static class SilkSceneUniformWriter
{
    internal const int ByteSize = 80;

    internal static void Write(
        SilkMeshData mesh,
        SilkFrameState frame,
        Span<byte> destination)
    {
        if (destination.Length != ByteSize)
        {
            throw new ArgumentException($"Scene constants must be exactly {ByteSize} bytes.", nameof(destination));
        }

        Span<double> meshView = stackalloc double[16];
        Span<double> projected = stackalloc double[16];
        Span<double> objectToClip = stackalloc double[16];
        ReadOnlySpan<double> transform = mesh.Transform.Span;
        ReadOnlySpan<float> displayColor = mesh.DisplayColor.Span;
        if (transform.Length != 16)
        {
            throw InvalidMesh(mesh, "transform must contain exactly 16 values");
        }
        if (displayColor.Length != 4)
        {
            throw InvalidMesh(mesh, "display color must contain exactly four values");
        }

        Multiply(transform, frame.View.Span, meshView);
        Multiply(meshView, frame.Projection.Span, projected);
        ConvertOpenGlDepthToZeroToOne(projected, objectToClip);

        for (int row = 0; row < 4; row++)
        {
            for (int column = 0; column < 4; column++)
            {
                float value = ToFiniteSingle(
                    objectToClip[(column * 4) + row],
                    mesh,
                    $"objectToClip[{column},{row}]");
                WriteSingle(destination, ((row * 4) + column) * sizeof(float), value);
            }
        }

        WriteFiniteColor(destination, 64, displayColor[0], mesh, "red");
        WriteFiniteColor(destination, 68, displayColor[1], mesh, "green");
        WriteFiniteColor(destination, 72, displayColor[2], mesh, "blue");
        WriteFiniteColor(destination, 76, displayColor[3], mesh, "alpha");
    }

    private static void Multiply(
        ReadOnlySpan<double> left,
        ReadOnlySpan<double> right,
        Span<double> result)
    {
        for (int row = 0; row < 4; row++)
        {
            for (int column = 0; column < 4; column++)
            {
                double value = 0;
                for (int inner = 0; inner < 4; inner++)
                {
                    value += left[(row * 4) + inner] * right[(inner * 4) + column];
                }
                result[(row * 4) + column] = value;
            }
        }
    }

    private static void ConvertOpenGlDepthToZeroToOne(
        ReadOnlySpan<double> source,
        Span<double> destination)
    {
        // GfFrustum matrices produce OpenGL [-w,+w] depth; all RHI backends consume [0,+w].
        for (int row = 0; row < 4; row++)
        {
            int offset = row * 4;
            destination[offset] = source[offset];
            destination[offset + 1] = source[offset + 1];
            destination[offset + 2] = (source[offset + 2] + source[offset + 3]) * 0.5;
            destination[offset + 3] = source[offset + 3];
        }
    }

    private static float ToFiniteSingle(double value, SilkMeshData mesh, string name)
    {
        if (!double.IsFinite(value) || value > float.MaxValue || value < -float.MaxValue)
        {
            throw new InvalidDataException(
                $"Mesh {mesh.Id} ('{mesh.Path}') has an invalid {name} value {value}.");
        }
        return (float)value;
    }

    private static void WriteFiniteColor(
        Span<byte> destination,
        int offset,
        float value,
        SilkMeshData mesh,
        string channel)
    {
        if (!float.IsFinite(value))
        {
            throw new InvalidDataException(
                $"Mesh {mesh.Id} ('{mesh.Path}') has a non-finite {channel} display color.");
        }
        WriteSingle(destination, offset, value);
    }

    private static void WriteSingle(Span<byte> destination, int offset, float value) =>
        BinaryPrimitives.WriteInt32LittleEndian(
            destination.Slice(offset, sizeof(float)),
            BitConverter.SingleToInt32Bits(value));

    private static InvalidDataException InvalidMesh(SilkMeshData mesh, string detail) =>
        new($"Mesh {mesh.Id} ('{mesh.Path}') is invalid: {detail}.");
}
