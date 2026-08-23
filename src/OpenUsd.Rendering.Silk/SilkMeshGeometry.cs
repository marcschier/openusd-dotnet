// Copyright (c) marcschier. Licensed under the MIT License.

using System.Buffers.Binary;

namespace OpenUsd.Rendering.Silk;

internal sealed record SilkMeshGeometry(
    float[] Vertices,
    uint[] Indices,
    SilkVertexLayoutDescriptor VertexLayout,
    string UvPrimvar,
    bool HasTangents)
{
    internal uint IndexCount => checked((uint)Indices.Length);
}

internal static class SilkMeshGeometryBuilder
{
    private const double NormalEpsilonSquared = 1e-30;

    internal static SilkMeshGeometry Build(
        SilkMeshData mesh,
        string uvPrimvar = "",
        bool requireTangents = false)
    {
        ArgumentNullException.ThrowIfNull(mesh);
        ReadOnlySpan<float> points = mesh.Points.Span;
        ReadOnlySpan<uint> meshIndices = mesh.Indices.Span;
        if (points.Length % 3 != 0)
        {
            throw InvalidMesh(mesh, "point component count must be divisible by three");
        }
        int indicesPerPrimitive = mesh.TopologyKind switch
        {
            SilkTopologyKind.TriangleList => 3,
            SilkTopologyKind.LineList => 2,
            SilkTopologyKind.PointList => 1,
            _ => throw InvalidMesh(mesh, "topology kind is unsupported")
        };
        if (meshIndices.Length % indicesPerPrimitive != 0)
        {
            throw InvalidMesh(mesh, "index count does not match its topology kind");
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
        for (int i = 0; i < meshIndices.Length; i++)
        {
            indices[i] = checked((uint)ValidateIndex(mesh, meshIndices, pointCount, i));
        }

        if (mesh.TopologyKind == SilkTopologyKind.TriangleList)
        {
            for (int triangle = 0; triangle < meshIndices.Length; triangle += 3)
            {
                int a = checked((int)indices[triangle]);
                int b = checked((int)indices[triangle + 1]);
                int c = checked((int)indices[triangle + 2]);

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
        }

        SilkVertexAttributeData? uvAttribute = string.IsNullOrEmpty(uvPrimvar)
            ? null
            : mesh.FindTexCoord(uvPrimvar);
        bool hasUv = uvAttribute is not null;
        bool hasTangents = hasUv && requireTangents;
        double[] tangents = hasTangents ? new double[checked(pointCount * 3)] : [];

        if (hasTangents && mesh.TopologyKind == SilkTopologyKind.TriangleList)
        {
            for (int triangle = 0; triangle < meshIndices.Length; triangle += 3)
            {
                int a = checked((int)indices[triangle]);
                int b = checked((int)indices[triangle + 1]);
                int c = checked((int)indices[triangle + 2]);
                AccumulateTangent(mesh, points, uvAttribute!, tangents, a, b, c);
            }
        }

        // Authored normals win when the delegate resolved them onto emitted
        // vertices; otherwise they are area-weighted from topology as before.
        // Both paths still normalise and reject a degenerate normal, so an
        // authored zero or non-finite value cannot reach the GPU.
        ReadOnlySpan<float> authored = mesh.AuthoredNormals.Span;
        bool useAuthored = authored.Length == points.Length;

        int strideFloats = hasTangents ? 12 : hasUv ? 8 : 6;
        float[] vertices = new float[checked(pointCount * strideFloats)];
        for (int point = 0; point < pointCount; point++)
        {
            int source = point * 3;
            int destination = point * strideFloats;
            vertices[destination] = points[source];
            vertices[destination + 1] = points[source + 1];
            vertices[destination + 2] = points[source + 2];

            double nx = useAuthored ? authored[source] : normals[source];
            double ny = useAuthored ? authored[source + 1] : normals[source + 1];
            double nz = useAuthored ? authored[source + 2] : normals[source + 2];
            double lengthSquared = (nx * nx) + (ny * ny) + (nz * nz);
            if (!double.IsFinite(lengthSquared) || lengthSquared <= NormalEpsilonSquared)
            {
                nx = 0;
                ny = 0;
                nz = 1;
                lengthSquared = 1;
            }

            double inverseLength = 1.0 / Math.Sqrt(lengthSquared);
            float normalX = checked((float)(nx * inverseLength));
            float normalY = checked((float)(ny * inverseLength));
            float normalZ = checked((float)(nz * inverseLength));
            vertices[destination + 3] = normalX;
            vertices[destination + 4] = normalY;
            vertices[destination + 5] = normalZ;
            if (hasUv)
            {
                vertices[destination + 6] = uvAttribute!.GetComponent(point, 0);
                vertices[destination + 7] = uvAttribute.ComponentCount > 1
                    ? uvAttribute.GetComponent(point, 1)
                    : 0;
            }
            if (hasTangents)
            {
                int tangentSource = point * 3;
                double tx = tangents[tangentSource];
                double ty = tangents[tangentSource + 1];
                double tz = tangents[tangentSource + 2];
                double tangentLengthSquared = (tx * tx) + (ty * ty) + (tz * tz);
                if (!double.IsFinite(tangentLengthSquared) ||
                    tangentLengthSquared <= NormalEpsilonSquared)
                {
                    tx = 1;
                    ty = 0;
                    tz = 0;
                    tangentLengthSquared = 1;
                }
                double inverseTangentLength = 1.0 / Math.Sqrt(tangentLengthSquared);
                vertices[destination + 8] = checked((float)(tx * inverseTangentLength));
                vertices[destination + 9] = checked((float)(ty * inverseTangentLength));
                vertices[destination + 10] = checked((float)(tz * inverseTangentLength));
                vertices[destination + 11] = 1;
            }
        }

        SilkVertexLayoutDescriptor layout = hasTangents
            ? SilkVertexLayoutDescriptor.PositionNormalTexCoordTangent
            : hasUv
                ? SilkVertexLayoutDescriptor.PositionNormalTexCoord
                : SilkVertexLayoutDescriptor.PositionNormal;
        return new SilkMeshGeometry(vertices, indices, layout, hasUv ? uvPrimvar : "", hasTangents);
    }

    private static void AccumulateTangent(
        SilkMeshData mesh,
        ReadOnlySpan<float> points,
        SilkVertexAttributeData uv,
        double[] tangents,
        int a,
        int b,
        int c)
    {
        int pa = a * 3;
        int pb = b * 3;
        int pc = c * 3;
        double edge1X = points[pb] - points[pa];
        double edge1Y = points[pb + 1] - points[pa + 1];
        double edge1Z = points[pb + 2] - points[pa + 2];
        double edge2X = points[pc] - points[pa];
        double edge2Y = points[pc + 1] - points[pa + 1];
        double edge2Z = points[pc + 2] - points[pa + 2];
        double du1 = uv.GetComponent(b, 0) - uv.GetComponent(a, 0);
        double dv1 = (uv.ComponentCount > 1 ? uv.GetComponent(b, 1) : 0) -
            (uv.ComponentCount > 1 ? uv.GetComponent(a, 1) : 0);
        double du2 = uv.GetComponent(c, 0) - uv.GetComponent(a, 0);
        double dv2 = (uv.ComponentCount > 1 ? uv.GetComponent(c, 1) : 0) -
            (uv.ComponentCount > 1 ? uv.GetComponent(a, 1) : 0);
        double determinant = (du1 * dv2) - (du2 * dv1);
        if (!double.IsFinite(determinant) || Math.Abs(determinant) <= 1e-12)
        {
            return;
        }
        double r = 1.0 / determinant;
        double tx = ((edge1X * dv2) - (edge2X * dv1)) * r;
        double ty = ((edge1Y * dv2) - (edge2Y * dv1)) * r;
        double tz = ((edge1Z * dv2) - (edge2Z * dv1)) * r;
        if (!double.IsFinite(tx) || !double.IsFinite(ty) || !double.IsFinite(tz))
        {
            throw InvalidMesh(mesh, "computed tangent is not finite");
        }
        Accumulate(tangents, pa, tx, ty, tz);
        Accumulate(tangents, pb, tx, ty, tz);
        Accumulate(tangents, pc, tx, ty, tz);
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
        Span<byte> destination,
        bool flipClipSpaceY = false,
        ReadOnlySpan<double> overrideTransform = default)
    {
        if (destination.Length != ByteSize)
        {
            throw new ArgumentException($"Scene constants must be exactly {ByteSize} bytes.", nameof(destination));
        }
        if (!overrideTransform.IsEmpty && overrideTransform.Length != 16)
        {
            throw new ArgumentException(
                "An override transform must contain exactly 16 values.",
                nameof(overrideTransform));
        }

        Span<double> meshView = stackalloc double[16];
        Span<double> projected = stackalloc double[16];
        Span<double> objectToClip = stackalloc double[16];
        ReadOnlySpan<double> transform = overrideTransform.IsEmpty
            ? mesh.Transform.Span
            : overrideTransform;
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
        if (flipClipSpaceY)
        {
            MirrorClipSpaceY(objectToClip);
        }

        for (int row = 0; row < 4; row++)
        {
            for (int column = 0; column < 4; column++)
            {
                float value = ToFiniteSingle(
                    objectToClip[(column * 4) + row],
                    mesh,
                    column,
                    row);
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

    private static void MirrorClipSpaceY(Span<double> objectToClip)
    {
        // Vulkan clip space has +Y pointing down, Direct3D and Metal have +Y up. Negating
        // the projected Y makes every backend rasterize the same stage the same way up.
        // Only geometry drawn through these scene constants is affected, so fullscreen
        // composite passes that address the render target directly stay untouched.
        for (int row = 0; row < 4; row++)
        {
            objectToClip[(row * 4) + 1] = -objectToClip[(row * 4) + 1];
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

    // The element name is passed as its row and column rather than as a formatted string because
    // this runs sixteen times for every mesh whose transform changed, and a physics-driven mesh
    // changes every frame; formatting eagerly would allocate on every rendered frame.
    private static float ToFiniteSingle(double value, SilkMeshData mesh, int column, int row)
    {
        if (!double.IsFinite(value) || value > float.MaxValue || value < -float.MaxValue)
        {
            throw new InvalidDataException(
                $"Mesh {mesh.Id} ('{mesh.Path}') has an invalid objectToClip[{column},{row}] value {value}.");
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
