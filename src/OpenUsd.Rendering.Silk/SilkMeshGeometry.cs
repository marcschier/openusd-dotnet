// Copyright (c) marcschier. Licensed under the MIT License.

using System.Buffers.Binary;

namespace OpenUsd.Rendering.Silk;

internal sealed record SilkMeshGeometry(
    float[] Vertices,
    uint[] Indices,
    SilkVertexLayoutDescriptor VertexLayout,
    string UvPrimvar,
    bool HasTangents,
    bool Displaced = false,
    float MaximumDisplacement = 0,
    string SecondaryUvPrimvar = "",
    bool DerivedCornerVertices = false,
    bool PtexUvSecondary = false)
{
    internal uint IndexCount => checked((uint)Indices.Length);
    internal uint VertexCount => checked((uint)(Vertices.Length /
        (VertexLayout.Stride / sizeof(float))));
}

internal static class SilkMeshGeometryBuilder
{
    private const double NormalEpsilonSquared = 1e-30;

    internal static SilkMeshGeometry Build(
        SilkMeshData mesh,
        string uvPrimvar = "",
        bool requireTangents = false,
        ReadOnlySpan<float> displacementAmounts = default,
        bool requireTextureCoordinates = false,
        string secondaryUvPrimvar = "",
        bool tangentsFromSecondary = false,
        SilkPtexAtlasLayout? ptexAtlas = null,
        bool ptexAsSecondary = false)
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
        if (!displacementAmounts.IsEmpty && displacementAmounts.Length != pointCount)
        {
            throw InvalidMesh(
                mesh,
                "displacement amounts must carry exactly one value per point");
        }
        for (int point = 0; point < displacementAmounts.Length; point++)
        {
            if (!float.IsFinite(displacementAmounts[point]))
            {
                throw InvalidMesh(mesh, $"displacement amount {point} is not finite");
            }
        }

        for (int i = 0; i < points.Length; i++)
        {
            if (!float.IsFinite(points[i]))
            {
                throw InvalidMesh(mesh, $"point component {i} is not finite");
            }
        }

        ReadOnlySpan<float> authored = mesh.AuthoredNormals.Span;
        bool useAuthored = authored.Length == points.Length;
        bool triangleLocal = mesh.TriangleLocalNormalFallback && !useAuthored &&
            mesh.TopologyKind == SilkTopologyKind.TriangleList;
        int[] unreferencedPoints = triangleLocal || ptexAtlas is { IsFallback: false }
            ? SilkSubprimPickGeometry.FindUnreferencedAuthoredPoints(mesh)
            : [];
        int vertexCount = triangleLocal
            ? checked(meshIndices.Length + unreferencedPoints.Length)
            : pointCount;
        uint[] indices = new uint[meshIndices.Length];
        double[] normals = useAuthored ? [] : new double[checked(vertexCount * 3)];
        for (int i = 0; i < meshIndices.Length; i++)
        {
            indices[i] = checked((uint)ValidateIndex(mesh, meshIndices, pointCount, i));
        }

        if (!useAuthored && !triangleLocal && mesh.TopologyKind == SilkTopologyKind.TriangleList &&
            mesh.NormalIdentityArray.Length != 0)
        {
            BuildPolygonNormals(mesh, points, indices, normals);
        }
        else if (!useAuthored && mesh.TopologyKind == SilkTopologyKind.TriangleList)
        {
            for (int triangle = 0; triangle < meshIndices.Length; triangle += 3)
            {
                int a = checked((int)indices[triangle]);
                int b = checked((int)indices[triangle + 1]);
                int c = checked((int)indices[triangle + 2]);

                int pa = a * 3;
                int pb = b * 3;
                int pc = c * 3;
                double abx = (double)points[pb] - points[pa];
                double aby = (double)points[pb + 1] - points[pa + 1];
                double abz = (double)points[pb + 2] - points[pa + 2];
                double acx = (double)points[pc] - points[pa];
                double acy = (double)points[pc + 1] - points[pa + 1];
                double acz = (double)points[pc + 2] - points[pa + 2];
                double nx = (aby * acz) - (abz * acy);
                double ny = (abz * acx) - (abx * acz);
                double nz = (abx * acy) - (aby * acx);
                double lengthSquared = (nx * nx) + (ny * ny) + (nz * nz);
                if (!double.IsFinite(lengthSquared))
                {
                    continue;
                }

                // A tiny face can still change the sum at a shared vertex.
                Accumulate(normals, triangleLocal ? triangle * 3 : pa, nx, ny, nz);
                Accumulate(normals, triangleLocal ? (triangle + 1) * 3 : pb, nx, ny, nz);
                Accumulate(normals, triangleLocal ? (triangle + 2) * 3 : pc, nx, ny, nz);
            }
        }

        SilkVertexAttributeData? uvAttribute = string.IsNullOrEmpty(uvPrimvar)
            ? null
            : mesh.FindTexCoord(uvPrimvar);
        bool hasUv = uvAttribute is not null || requireTextureCoordinates || ptexAtlas is not null;
        bool hasTangents = hasUv && requireTangents;
        // A second surface coordinate set is emitted only beside a first one. A mesh
        // that lacks it reads zeros, exactly as a missing first set does.
        bool hasSecondary = hasUv &&
            (ptexAsSecondary ||
                (!string.IsNullOrEmpty(secondaryUvPrimvar) &&
                    !string.Equals(secondaryUvPrimvar, uvPrimvar, StringComparison.Ordinal)));
        SilkVertexAttributeData? secondaryAttribute = hasSecondary
            ? mesh.FindTexCoord(secondaryUvPrimvar)
            : null;
        // Tangents follow the coordinate set the normal map samples, or the
        // tangent frame would rotate the mapped normal by the angle between sets.
        SilkVertexAttributeData? tangentAttribute = hasSecondary && tangentsFromSecondary
            ? secondaryAttribute
            : uvAttribute;
        double[] tangents = hasTangents ? new double[checked(vertexCount * 3)] : [];
        double[] bitangents = hasTangents ? new double[checked(vertexCount * 3)] : [];

        if (hasTangents && tangentAttribute is not null && mesh.TopologyKind == SilkTopologyKind.TriangleList)
        {
            for (int triangle = 0; triangle < meshIndices.Length; triangle += 3)
            {
                int a = checked((int)indices[triangle]);
                int b = checked((int)indices[triangle + 1]);
                int c = checked((int)indices[triangle + 2]);
                AccumulateTangent(mesh, points, tangentAttribute, tangents, bitangents, a, b, c,
                    triangleLocal ? triangle : a,
                    triangleLocal ? triangle + 1 : b,
                    triangleLocal ? triangle + 2 : c);
            }
        }

        // Authored normals win when the delegate resolved them onto emitted
        // vertices; otherwise they are area-weighted from topology.
        // Both paths still normalise and reject a degenerate normal, so an
        // authored zero or non-finite value cannot reach the GPU.
        int strideFloats = (hasTangents ? 12 : hasUv ? 8 : 6) + (hasSecondary ? 2 : 0);
        float[] vertices = new float[checked(vertexCount * strideFloats)];
        float maximumDisplacement = 0;
        for (int vertex = 0; vertex < vertexCount; vertex++)
        {
            int point = triangleLocal
                ? vertex < indices.Length
                    ? checked((int)indices[vertex])
                    : unreferencedPoints[vertex - indices.Length]
                : vertex;
            int source = point * 3;
            int normalSource = triangleLocal ? vertex * 3 : source;
            int destination = vertex * strideFloats;

            double nx = useAuthored ? authored[source] : normals[normalSource];
            double ny = useAuthored ? authored[source + 1] : normals[normalSource + 1];
            double nz = useAuthored ? authored[source + 2] : normals[normalSource + 2];
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

            // UsdPreviewSurface displaces along the surface normal, so the amount
            // is applied to the position and the shading frame is left exactly as
            // the deformation or the topology produced it. Nothing here re-derives
            // a normal or a tangent from the moved surface: the ordering this
            // renderer claims is deform, then displace, and a re-derived frame
            // would make the claim untestable against the CPU deformation oracle.
            float amount = displacementAmounts.IsEmpty ? 0 : displacementAmounts[point];
            float positionX = points[source] + (amount * normalX);
            float positionY = points[source + 1] + (amount * normalY);
            float positionZ = points[source + 2] + (amount * normalZ);
            if (!float.IsFinite(positionX) ||
                !float.IsFinite(positionY) ||
                !float.IsFinite(positionZ))
            {
                throw InvalidMesh(mesh, $"displaced point {point} is not finite");
            }
            maximumDisplacement = Math.Max(maximumDisplacement, Math.Abs(amount));
            vertices[destination] = positionX;
            vertices[destination + 1] = positionY;
            vertices[destination + 2] = positionZ;
            vertices[destination + 3] = normalX;
            vertices[destination + 4] = normalY;
            vertices[destination + 5] = normalZ;
            if (hasUv)
            {
                vertices[destination + 6] = uvAttribute?.GetComponent(point, 0) ?? 0;
                vertices[destination + 7] = uvAttribute is { ComponentCount: > 1 }
                    ? uvAttribute.GetComponent(point, 1)
                    : 0;
            }
            if (hasTangents)
            {
                int tangentSource = vertex * 3;
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
                double bx = bitangents[tangentSource];
                double by = bitangents[tangentSource + 1];
                double bz = bitangents[tangentSource + 2];
                double handedness = ((normalY * tz - normalZ * ty) * bx) +
                    ((normalZ * tx - normalX * tz) * by) + ((normalX * ty - normalY * tx) * bz);
                vertices[destination + 11] = handedness < 0 ? -1 : 1;
            }
            if (hasSecondary)
            {
                int secondary = destination + strideFloats - 2;
                vertices[secondary] = secondaryAttribute?.GetComponent(point, 0) ?? 0;
                vertices[secondary + 1] = secondaryAttribute is { ComponentCount: > 1 }
                    ? secondaryAttribute.GetComponent(point, 1)
                    : 0;
            }
            if (triangleLocal && vertex < indices.Length)
            {
                indices[vertex] = checked((uint)vertex);
            }
        }

        SilkVertexLayoutDescriptor layout = (hasTangents, hasSecondary) switch
        {
            (true, true) => SilkVertexLayoutDescriptor.PositionNormalTexCoordTangentSecondary,
            (true, false) => SilkVertexLayoutDescriptor.PositionNormalTexCoordTangent,
            (false, true) => SilkVertexLayoutDescriptor.PositionNormalTexCoordSecondary,
            _ => hasUv
                ? SilkVertexLayoutDescriptor.PositionNormalTexCoord
                : SilkVertexLayoutDescriptor.PositionNormal
        };
        uint[] emittedIndices = indices;
        if (ptexAtlas is { IsFallback: true })
        {
            int textureCoordinate = ptexAsSecondary ? strideFloats - 2 : 6;
            for (int vertex = 0; vertex < vertexCount; vertex++)
            {
                int destination = checked((vertex * strideFloats) + textureCoordinate);
                vertices[destination] = 0.5f;
                vertices[destination + 1] = 0.5f;
            }
        }
        else if (ptexAtlas is not null)
        {
            if (mesh.TopologyKind != SilkTopologyKind.TriangleList)
            {
                throw InvalidMesh(mesh, "Ptex sampling requires triangle-list topology");
            }
            SilkPtexAtlasLayout.ValidateMapping(mesh);
            if (!triangleLocal)
            {
                (vertices, emittedIndices) = ExpandTriangleCorners(
                    vertices, indices, strideFloats, unreferencedPoints);
            }
            for (int corner = 0; corner < meshIndices.Length; corner++)
            {
                uint faceId = checked((uint)mesh.PtexFaceIndices[corner / 3]);
                (float u, float v) = ptexAtlas.Map(
                    faceId,
                    mesh.PtexCoordinates[corner * 2],
                    mesh.PtexCoordinates[(corner * 2) + 1]);
                int textureCoordinate = ptexAsSecondary ? strideFloats - 2 : 6;
                int destination = checked((corner * strideFloats) + textureCoordinate);
                vertices[destination] = u;
                vertices[destination + 1] = v;
            }
        }

        return new SilkMeshGeometry(
            vertices,
            emittedIndices,
            layout,
            hasUv ? uvPrimvar : "",
            hasTangents,
            !displacementAmounts.IsEmpty,
            maximumDisplacement,
            hasSecondary && !ptexAsSecondary ? secondaryUvPrimvar : "",
            triangleLocal || ptexAtlas is { IsFallback: false },
            ptexAsSecondary);
    }

    internal static (float[] Vertices, uint[] Indices) ExpandTriangleCorners(
        ReadOnlySpan<float> sourceVertices,
        ReadOnlySpan<uint> sourceIndices,
        int strideFloats,
        ReadOnlySpan<int> unreferencedPoints = default)
    {
        if (strideFloats <= 0 || sourceVertices.Length % strideFloats != 0)
        {
            throw new InvalidDataException("The source vertex stream does not match its stride.");
        }
        float[] vertices = new float[checked((sourceIndices.Length + unreferencedPoints.Length) * strideFloats)];
        uint[] indices = new uint[sourceIndices.Length];
        for (int corner = 0; corner < sourceIndices.Length; corner++)
        {
            if (sourceIndices[corner] >= sourceVertices.Length / strideFloats)
            {
                throw new InvalidDataException($"Triangle corner {corner} references an absent source vertex.");
            }
            int source = checked((int)sourceIndices[corner] * strideFloats);
            int destination = checked(corner * strideFloats);
            sourceVertices.Slice(source, strideFloats).CopyTo(vertices.AsSpan(destination, strideFloats));
            indices[corner] = checked((uint)corner);
        }
        for (int point = 0; point < unreferencedPoints.Length; point++)
        {
            int source = checked(unreferencedPoints[point] * strideFloats);
            int destination = checked((sourceIndices.Length + point) * strideFloats);
            sourceVertices.Slice(source, strideFloats).CopyTo(vertices.AsSpan(destination, strideFloats));
        }
        return (vertices, indices);
    }

    private static void AccumulateTangent(
        SilkMeshData mesh,
        ReadOnlySpan<float> points,
        SilkVertexAttributeData uv,
        double[] tangents,
        double[] bitangents,
        int a,
        int b,
        int c,
        int targetA,
        int targetB,
        int targetC)
    {
        int pa = a * 3;
        int pb = b * 3;
        int pc = c * 3;
        double edge1X = (double)points[pb] - points[pa];
        double edge1Y = (double)points[pb + 1] - points[pa + 1];
        double edge1Z = (double)points[pb + 2] - points[pa + 2];
        double edge2X = (double)points[pc] - points[pa];
        double edge2Y = (double)points[pc + 1] - points[pa + 1];
        double edge2Z = (double)points[pc + 2] - points[pa + 2];
        double du1 = (double)uv.GetComponent(b, 0) - uv.GetComponent(a, 0);
        double dv1 = uv.ComponentCount > 1
            ? (double)uv.GetComponent(b, 1) - uv.GetComponent(a, 1)
            : 0;
        double du2 = (double)uv.GetComponent(c, 0) - uv.GetComponent(a, 0);
        double dv2 = uv.ComponentCount > 1
            ? (double)uv.GetComponent(c, 1) - uv.GetComponent(a, 1)
            : 0;
        double determinant = (du1 * dv2) - (du2 * dv1);
        if (!double.IsFinite(determinant) || Math.Abs(determinant) <= 1e-12)
        {
            return;
        }
        double r = 1.0 / determinant;
        double tx = ((edge1X * dv2) - (edge2X * dv1)) * r;
        double ty = ((edge1Y * dv2) - (edge2Y * dv1)) * r;
        double tz = ((edge1Z * dv2) - (edge2Z * dv1)) * r;
        double bx = ((edge2X * du1) - (edge1X * du2)) * r;
        double by = ((edge2Y * du1) - (edge1Y * du2)) * r;
        double bz = ((edge2Z * du1) - (edge1Z * du2)) * r;
        if (!double.IsFinite(tx) || !double.IsFinite(ty) || !double.IsFinite(tz) ||
            !double.IsFinite(bx) || !double.IsFinite(by) || !double.IsFinite(bz))
        {
            throw InvalidMesh(mesh, "computed tangent is not finite");
        }
        Accumulate(tangents, targetA * 3, tx, ty, tz);
        Accumulate(tangents, targetB * 3, tx, ty, tz);
        Accumulate(tangents, targetC * 3, tx, ty, tz);
        Accumulate(bitangents, targetA * 3, bx, by, bz);
        Accumulate(bitangents, targetB * 3, bx, by, bz);
        Accumulate(bitangents, targetC * 3, bx, by, bz);
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

    private static void BuildPolygonNormals(
        SilkMeshData mesh, ReadOnlySpan<float> points, ReadOnlySpan<uint> indices, double[] normals)
    {
        int pointCount = points.Length / 3;
        ReadOnlySpan<int> identities = mesh.NormalIdentityArray;
        ReadOnlySpan<int> corners = mesh.NormalCornerArray;
        if (identities.Length != pointCount || corners.Length % 3 != 0 ||
            mesh.NormalExtraPointArray.Length % 3 != 0)
        {
            throw InvalidMesh(mesh, "normal correspondence counts do not match the emitted topology");
        }
        float[]? allNormalPoints = null;
        if (mesh.NormalExtraPointArray.Length != 0)
        {
            allNormalPoints = new float[checked(points.Length + mesh.NormalExtraPointArray.Length)];
            points.CopyTo(allNormalPoints);
            mesh.NormalExtraPointArray.CopyTo(allNormalPoints, points.Length);
        }
        ReadOnlySpan<float> normalPoints = allNormalPoints is null ? points : allNormalPoints;
        int normalPointCount = normalPoints.Length / 3;
        var groups = new Dictionary<(int Identity, float X, float Y, float Z), int>(pointCount);
        var representatives = new int[pointCount];
        var hasPolygonCorner = new bool[pointCount];
        for (int point = 0; point < pointCount; point++)
        {
            int offset = point * 3;
            var key = (identities[point], points[offset], points[offset + 1], points[offset + 2]);
            if (identities[point] < 0)
            {
                throw InvalidMesh(mesh, "normal identity is negative");
            }
            if (!groups.TryGetValue(key, out int representative))
            {
                representative = point;
                groups.Add(key, representative);
            }
            representatives[point] = representative;
        }
        for (int corner = 0; corner < corners.Length; corner += 3)
        {
            int previous = corners[corner];
            int current = corners[corner + 1];
            int next = corners[corner + 2];
            if ((uint)previous >= normalPointCount || (uint)current >= normalPointCount ||
                (uint)next >= normalPointCount)
            {
                throw InvalidMesh(mesh, "normal corner references a missing point");
            }
            if (current >= pointCount)
            {
                continue;
            }
            int representative = representatives[current];
            hasPolygonCorner[representative] = true;
            AccumulateCorner(normalPoints, normals, representative, current, next, previous);
        }
        // A simulated triangle copy can separate from the polygon corner that
        // originally represented it. Its new position has its own local normal.
        for (int triangle = 0; triangle < indices.Length; triangle += 3)
        {
            for (int corner = 0; corner < 3; corner++)
            {
                int current = checked((int)indices[triangle + corner]);
                int representative = representatives[current];
                if (!hasPolygonCorner[representative])
                {
                    AccumulateCorner(points, normals, representative, current,
                        checked((int)indices[triangle + ((corner + 1) % 3)]),
                        checked((int)indices[triangle + ((corner + 2) % 3)]));
                }
            }
        }
        for (int point = 0; point < pointCount; point++)
        {
            int representative = representatives[point] * 3;
            if (representative != point * 3)
            {
                normals.AsSpan(representative, 3).CopyTo(normals.AsSpan(point * 3, 3));
            }
        }
    }

    private static void AccumulateCorner(
        ReadOnlySpan<float> points, double[] normals, int target, int current, int next, int previous)
    {
        int a = current * 3;
        int b = next * 3;
        int c = previous * 3;
        double bx = (double)points[b] - points[a];
        double by = (double)points[b + 1] - points[a + 1];
        double bz = (double)points[b + 2] - points[a + 2];
        double cx = (double)points[c] - points[a];
        double cy = (double)points[c + 1] - points[a + 1];
        double cz = (double)points[c + 2] - points[a + 2];
        Accumulate(normals, target * 3,
            (by * cz) - (bz * cy), (bz * cx) - (bx * cz), (bx * cy) - (by * cx));
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
