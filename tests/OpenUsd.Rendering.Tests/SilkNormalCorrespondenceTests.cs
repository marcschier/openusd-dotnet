// Copyright (c) marcschier. Licensed under the MIT License.

using System.Buffers.Binary;
using System.Text;
using OpenUsd.Rendering.Silk;

namespace OpenUsd.Rendering.Tests;

public sealed class SilkNormalCorrespondenceTests
{
    private static readonly float[] NgonPoints =
        [0, 0, 0, 1, 0, 0, 1, 1, 0, 0, 1, 0, 0, 0, 1];
    private static readonly uint[] NgonIndices = [0, 1, 2, 0, 2, 3, 0, 4, 1];
    private static readonly int[] NgonCorners =
        [3, 0, 1, 0, 1, 2, 1, 2, 3, 2, 3, 0, 1, 0, 4, 0, 4, 1, 4, 1, 0];

    [Test]
    public async Task NgonUsesPolygonCornersRatherThanFanTriangleWeights()
    {
        SilkMeshData mesh = Mesh(NgonPoints, NgonIndices, [0, 1, 2, 3, 4], NgonCorners);
        SilkMeshGeometry geometry = SilkMeshGeometryBuilder.Build(mesh);
        await Normal(geometry, 0, 0, Math.Sqrt(0.5), Math.Sqrt(0.5));
        await Normal(geometry, 1, 0, Math.Sqrt(0.5), Math.Sqrt(0.5));
        await Normal(geometry, 2, 0, 0, 1);
        await Assert.That(mesh.NormalCorrespondenceBytes).IsEqualTo(104ul);
    }

    [Test]
    public async Task NonplanarNgonUsesEachCornersNeighborsNotWholeFaceArea()
    {
        SilkMeshData mesh = Mesh(
            [0, 0, 0, 2, 0, 0, 1, 1, 1, 0, 1, 0], [0, 1, 2, 0, 2, 3],
            [0, 1, 2, 3], [3, 0, 1, 0, 1, 2, 1, 2, 3, 2, 3, 0]);
        SilkMeshGeometry geometry = SilkMeshGeometryBuilder.Build(mesh);
        await Normal(geometry, 0, 0, 0, 1);
        await Normal(geometry, 1, 0, -Math.Sqrt(0.5), Math.Sqrt(0.5));
        await Normal(geometry, 2, -1 / Math.Sqrt(6), -2 / Math.Sqrt(6), 1 / Math.Sqrt(6));
        await Normal(geometry, 3, -Math.Sqrt(0.5), 0, Math.Sqrt(0.5));
    }

    [Test]
    public async Task SeamCopiesSmoothWithoutPickingAndSplitAndRejoinAtSimulatedPositions()
    {
        SilkMeshData authored = ExpandedNgon();
        await Assert.That(authored.PointOrigins.IsEmpty).IsTrue();
        SilkMeshGeometry original = SilkMeshGeometryBuilder.Build(authored);
        foreach (int point in new[] { 0, 3, 6 })
        {
            await Normal(original, point, 0, Math.Sqrt(0.5), Math.Sqrt(0.5));
        }

        float[] changed = authored.Points.ToArray();
        changed[6 * 3 + 2] = -1;
        SilkMeshData simulated = authored.WithSimulatedPoints(changed);
        SilkMeshGeometry divergent = SilkMeshGeometryBuilder.Build(simulated);
        await Normal(divergent, 0, 0, 0, 1);
        await Normal(divergent, 3, 0, 0, 1);
        await Normal(divergent, 6, 0, 1, 0);
        await Assert.That(ReferenceEquals(authored.NormalIdentityArray, simulated.NormalIdentityArray)).IsTrue();
        await Assert.That(ReferenceEquals(authored.NormalCornerArray, simulated.NormalCornerArray)).IsTrue();

        SilkMeshGeometry restored = SilkMeshGeometryBuilder.Build(
            simulated.WithSimulatedPoints(authored.Points.Span));
        await Assert.That(restored.Vertices).IsEquivalentTo(original.Vertices);
    }

    [Test]
    public async Task DivergedFanTriangleCopyUsesItsLocalNormalAndRejoins()
    {
        SilkMeshData authored = ExpandedNgon();
        float[] changed = authored.Points.ToArray();
        changed[3 * 3 + 2] = -1;
        SilkMeshGeometry geometry = SilkMeshGeometryBuilder.Build(authored.WithSimulatedPoints(changed));
        await Normal(geometry, 0, 0, Math.Sqrt(0.5), Math.Sqrt(0.5));
        await Normal(geometry, 3, 0, -Math.Sqrt(0.5), Math.Sqrt(0.5));
        await Normal(geometry, 6, 0, Math.Sqrt(0.5), Math.Sqrt(0.5));
        SilkMeshGeometry restored = SilkMeshGeometryBuilder.Build(
            authored.WithSimulatedPoints(authored.Points.Span));
        await Normal(restored, 3, 0, Math.Sqrt(0.5), Math.Sqrt(0.5));
    }

    [Test]
    public async Task UnrelatedCoincidentAuthoredVerticesNeverWeld()
    {
        SilkMeshData mesh = Mesh(
            [0, 0, 0, 1, 0, 0, 0, 1, 0, 0, 0, 0, 0, 0, 1, 1, 0, 0],
            [0, 1, 2, 3, 4, 5], [0, 1, 2, 3, 4, 5],
            [2, 0, 1, 0, 1, 2, 1, 2, 0, 5, 3, 4, 3, 4, 5, 4, 5, 3]);
        SilkMeshGeometry geometry = SilkMeshGeometryBuilder.Build(mesh);
        await Normal(geometry, 0, 0, 0, 1);
        await Normal(geometry, 3, 0, 1, 0);
    }

    [Test]
    public async Task SmoothHoleAdjacencyRetainsNormalOnlyNeighborsWithoutRasterizingHole()
    {
        SilkMeshData source = Mesh(
            [0, 0, 0, 1, 0, 0, 0, 1, 0], [0, 1, 2], [0, 1, 2],
            [2, 0, 1, 0, 1, 2, 1, 2, 0, 1, 0, 3, 0, 3, 1, 3, 1, 0],
            normalExtraPoints: [0, 0, 1]);
        using var scene = new SilkSceneState();
        _ = scene.Apply(Encode(source), 1, 1);
        SilkMeshData mesh = scene.MeshesByPath[("/Mesh", 0)];
        await Assert.That(mesh.Indices.Length).IsEqualTo(3);
        await Assert.That(mesh.Points.Length).IsEqualTo(9);
        await Assert.That(mesh.NormalExtraPointArray).IsEquivalentTo(new float[] { 0, 0, 1 });
        await Normal(SilkMeshGeometryBuilder.Build(mesh), 0, 0, Math.Sqrt(0.5), Math.Sqrt(0.5));
        SilkMeshData simulated = mesh.WithSimulatedPoints(mesh.Points.Span);
        await Assert.That(ReferenceEquals(mesh.NormalExtraPointArray, simulated.NormalExtraPointArray)).IsTrue();
        await Normal(SilkMeshGeometryBuilder.Build(simulated), 0, 0, Math.Sqrt(0.5), Math.Sqrt(0.5));
    }

    [Test]
    public async Task NonfiniteNormalOnlyPointIsRejectedBeforePublication()
    {
        SilkMeshData source = Mesh([0, 0, 0, 1, 0, 0, 0, 1, 0], [0, 1, 2],
            [0, 1, 2], [2, 0, 1], normalExtraPoints: [float.NaN, 0, 1]);
        using var scene = new SilkSceneState();
        await Assert.That(() => scene.Apply(Encode(source), 1, 1)).Throws<InvalidDataException>();
        await Assert.That(scene.MeshesByPath.Count).IsEqualTo(0);
    }

    [Test]
    public async Task AuthoredNormalsWinOverPolygonCorrespondence()
    {
        float[] normals = [1, 0, 0, 1, 0, 0, 1, 0, 0];
        SilkMeshData mesh = Mesh([0, 0, 0, 1, 0, 0, 0, 1, 0], [0, 1, 2],
            [0, 1, 2], [2, 0, 1, 0, 1, 2, 1, 2, 0], normals);
        SilkMeshGeometry geometry = SilkMeshGeometryBuilder.Build(mesh);
        await Normal(geometry, 0, 1, 0, 0);
        await Normal(geometry, 1, 1, 0, 0);
        await Normal(geometry, 2, 1, 0, 0);
    }

    [Test]
    public async Task NormalCorrespondenceSeparatesRetainedGeometryIdentity()
    {
        SilkMeshData smooth = ExpandedNgon();
        SilkMeshData separate = Mesh(smooth.Points.ToArray(), smooth.Indices.ToArray(),
            Enumerable.Range(0, 9).ToArray(), smooth.NormalCornerArray);
        var smoothKey = SilkMeshGpuGeometryKey.Create(smooth, "", false);
        var separateKey = SilkMeshGpuGeometryKey.Create(separate, "", false);
        await Assert.That(smoothKey == separateKey).IsFalse();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task NormalAdjacencyAndExtraPositionsSeparateRetainedGeometryIdentity(bool extraPositions)
    {
        SilkMeshData original = Mesh(
            [0, 0, 0, 1, 0, 0, 0, 1, 0], [0, 1, 2], [0, 1, 2],
            [2, 0, 1, 1, 0, 3], normalExtraPoints: [0, 0, 1]);
        SilkMeshData changed = Mesh(original.Points.ToArray(), original.Indices.ToArray(),
            original.NormalIdentityArray,
            extraPositions ? original.NormalCornerArray : [1, 0, 2, 1, 0, 3],
            normalExtraPoints: extraPositions ? [0, 0, -1] : original.NormalExtraPointArray);
        await Assert.That(
            SilkMeshGpuGeometryKey.Create(original, "", false) ==
            SilkMeshGpuGeometryKey.Create(changed, "", false)).IsFalse();
    }

    [Test]
    public async Task MalformedReplacementKeepsPublishedMeshAndAllowsValidRetry()
    {
        SilkMeshData source = ExpandedNgon();
        byte[] valid = Encode(source);
        using var scene = new SilkSceneState();
        _ = scene.Apply(valid, 1, 1);
        SilkMeshData retained = scene.MeshesByPath[("/Mesh", 0)];
        byte[] invalid = valid.ToArray();
        int tail = invalid.Length - 12 - checked((int)source.NormalCorrespondenceBytes);
        BinaryPrimitives.WriteUInt32LittleEndian(invalid.AsSpan(tail + 8), uint.MaxValue);
        await Assert.That(() => scene.Apply(invalid, 1, 2)).Throws<InvalidDataException>();
        await Assert.That(ReferenceEquals(retained, scene.MeshesByPath[("/Mesh", 0)])).IsTrue();
        await Assert.That(scene.Revision).IsEqualTo(1ul);
        _ = scene.Apply(valid, 1, 3);
        await Assert.That(scene.Revision).IsEqualTo(3ul);
        await Normal(SilkMeshGeometryBuilder.Build(scene.MeshesByPath[("/Mesh", 0)]),
            0, 0, Math.Sqrt(0.5), Math.Sqrt(0.5));
    }

    [Test]
    public async Task BulkPageRetainsPolygonCorrespondenceAfterSourcePageDisposal()
    {
        SilkMeshData source = ExpandedNgon();
        byte[] bytes = Encode(source);
        using var scene = new SilkSceneState();
        using (var page = new OpenUsdSilkPage(SilkCommandParser.PageAbiVersion, 1, bytes, 1))
        {
            _ = scene.Apply(page);
        }
        Array.Clear(bytes);
        SilkMeshData mesh = scene.MeshesByPath[("/Mesh", 0)];
        await Assert.That(mesh.NormalIdentityArray).IsEquivalentTo(source.NormalIdentityArray);
        await Assert.That(mesh.NormalCornerArray).IsEquivalentTo(source.NormalCornerArray);
        await Normal(SilkMeshGeometryBuilder.Build(mesh), 0, 0, Math.Sqrt(0.5), Math.Sqrt(0.5));
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task LightweightInstancesShareNormalTablesWithoutCopyingOrChangingGeometry(bool hole)
    {
        SilkMeshData source = hole
            ? Mesh([0, 0, 0, 1, 0, 0, 0, 1, 0], [0, 1, 2], [0, 1, 2],
                [2, 0, 1, 1, 0, 3], normalExtraPoints: [0, 0, 1])
            : ExpandedNgon();
        byte[] prototypeBytes = Encode(source);
        byte[] instancer = Encoding.UTF8.GetBytes("/Instancer");
        int pathLength = Encoding.UTF8.GetByteCount(source.Path);
        var referenceBytes = new byte[276 + pathLength + 8 + instancer.Length * 2];
        prototypeBytes.AsSpan(0, 276 + pathLength).CopyTo(referenceBytes);
        BinaryPrimitives.WriteUInt32LittleEndian(referenceBytes.AsSpan(4), (uint)referenceBytes.Length);
        BinaryPrimitives.WriteInt32LittleEndian(referenceBytes.AsSpan(20), 1);
        BinaryPrimitives.WriteInt32LittleEndian(referenceBytes.AsSpan(24), 7);
        Array.Clear(referenceBytes, 52, 12);
        BinaryPrimitives.WriteDoubleLittleEndian(referenceBytes.AsSpan(80 + 12 * 8), 7);
        BinaryPrimitives.WriteUInt32LittleEndian(referenceBytes.AsSpan(260), (uint)instancer.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(referenceBytes.AsSpan(264), 1);
        int cursor = 276 + pathLength;
        instancer.CopyTo(referenceBytes.AsSpan(cursor));
        cursor += instancer.Length;
        BinaryPrimitives.WriteUInt32LittleEndian(referenceBytes.AsSpan(cursor), (uint)instancer.Length);
        BinaryPrimitives.WriteInt32LittleEndian(referenceBytes.AsSpan(cursor + 4), 7);
        instancer.CopyTo(referenceBytes.AsSpan(cursor + 8));

        using var scene = new SilkSceneState();
        _ = scene.Apply([.. prototypeBytes, .. referenceBytes], 2, 1);
        SilkMeshData prototype = scene.MeshesByPath[("/Mesh", 0)];
        SilkMeshData instance = scene.MeshesByPath[("/Mesh", 7)];
        await Assert.That(ReferenceEquals(prototype.NormalIdentityArray, instance.NormalIdentityArray)).IsTrue();
        await Assert.That(ReferenceEquals(prototype.NormalCornerArray, instance.NormalCornerArray)).IsTrue();
        await Assert.That(ReferenceEquals(prototype.NormalExtraPointArray, instance.NormalExtraPointArray)).IsTrue();
        await Assert.That(instance.Transform.Span[12]).IsEqualTo(7d);
        await Assert.That(SilkMeshGpuGeometryKey.Create(instance, "", false))
            .IsEqualTo(SilkMeshGpuGeometryKey.Create(prototype, "", false));
        await Normal(SilkMeshGeometryBuilder.Build(instance), 0, 0, Math.Sqrt(0.5), Math.Sqrt(0.5));
    }

    [Test]
    [Arguments(0)]
    [Arguments(1)]
    [Arguments(2)]
    [Arguments(3)]
    [Arguments(4)]
    public async Task MalformedBulkCorrespondenceIsRejectedBeforeScenePublication(int mutation)
    {
        SilkMeshData mesh = ExpandedNgon();
        byte[] bytes = Encode(mesh);
        int tail = bytes.Length - 12 - checked((int)mesh.NormalCorrespondenceBytes);
        switch (mutation)
        {
            case 0:
                BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(tail), 0);
                break;
            case 1:
                BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(tail), 8);
                break;
            case 2:
                BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(tail + 4), uint.MaxValue);
                break;
            case 3:
                BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(tail + 12), uint.MaxValue);
                break;
            case 4:
                BinaryPrimitives.WriteUInt32LittleEndian(
                    bytes.AsSpan(tail + 12 + mesh.NormalIdentityArray.Length * 4), 9);
                break;
        }
        using var scene = new SilkSceneState();
        await Assert.That(() => scene.Apply(bytes, 1, 1)).Throws<InvalidDataException>();
        await Assert.That(scene.MeshesByPath.Count).IsEqualTo(0);
        await Assert.That(scene.Revision).IsEqualTo(0ul);
    }

    private static SilkMeshData ExpandedNgon()
    {
        var points = new float[NgonIndices.Length * 3];
        for (int corner = 0; corner < NgonIndices.Length; corner++)
        {
            NgonPoints.AsSpan((int)NgonIndices[corner] * 3, 3).CopyTo(points.AsSpan(corner * 3));
        }
        return Mesh(points, Enumerable.Range(0, 9).Select(static index => (uint)index).ToArray(),
            NgonIndices.Select(static index => (int)index).ToArray(),
            [5, 0, 1, 0, 1, 2, 1, 2, 5, 2, 5, 0, 8, 6, 7, 6, 7, 8, 7, 8, 6]);
    }

    private static SilkMeshData Mesh(
        float[] points, uint[] indices, int[] identities, int[] corners, float[]? authoredNormals = null,
        float[]? normalExtraPoints = null) =>
        new(1, "/Mesh", SilkWireFormat.ComputeStableHash("/Mesh"), 0, 0,
            SilkTopologyKind.TriangleList, 1, points, indices,
            new int[indices.Length / 3], [1, 1, 1, 1],
            [1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1],
            1, true, SilkMeshCullStyle.BackUnlessDoubleSided, authoredNormals ?? [], "")
        {
            NormalIdentityArray = identities,
            NormalCornerArray = corners,
            NormalExtraPointArray = normalExtraPoints ?? []
        };

    private static async Task Normal(SilkMeshGeometry geometry, int point, double x, double y, double z)
    {
        int offset = point * checked((int)geometry.VertexLayout.Stride / 4) + 3;
        await Assert.That(Math.Abs(geometry.Vertices[offset] - x)).IsLessThan(1e-6);
        await Assert.That(Math.Abs(geometry.Vertices[offset + 1] - y)).IsLessThan(1e-6);
        await Assert.That(Math.Abs(geometry.Vertices[offset + 2] - z)).IsLessThan(1e-6);
    }

    private static byte[] Encode(SilkMeshData mesh)
    {
        byte[] path = Encoding.UTF8.GetBytes(mesh.Path);
        int size = 276 + path.Length + mesh.Points.Length * 4 + mesh.Indices.Length * 4 +
            mesh.TriangleSubprims.Length * 4 + 12 + checked((int)mesh.NormalCorrespondenceBytes);
        var bytes = new byte[size];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, (uint)SilkCommandType.MeshUpsert);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), (uint)size);
        BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(8), mesh.StableHash);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(16), mesh.PrimId);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(28), (uint)mesh.TopologyKind);
        BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(32), 1);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(40), 1);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(48), (uint)path.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(52), (uint)(mesh.Points.Length / 3));
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(56), (uint)mesh.Indices.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(60), (uint)mesh.TriangleSubprims.Length);
        for (int component = 0; component < 4; component++)
        {
            BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(64 + component * 4), 1);
        }
        for (int component = 0; component < 16; component++)
        {
            BinaryPrimitives.WriteDoubleLittleEndian(bytes.AsSpan(80 + component * 8), component % 5 == 0 ? 1 : 0);
        }
        path.CopyTo(bytes, 276);
        int cursor = 276 + path.Length;
        foreach (float point in mesh.Points.Span)
        {
            BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(cursor), point);
            cursor += 4;
        }
        foreach (uint index in mesh.Indices.Span)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(cursor), index);
            cursor += 4;
        }
        cursor += mesh.TriangleSubprims.Length * 4;
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(cursor), (uint)mesh.NormalIdentityArray.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(cursor + 4), (uint)(mesh.NormalCornerArray.Length / 3));
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(cursor + 8), (uint)(mesh.NormalExtraPointArray.Length / 3));
        cursor += 12;
        foreach (int value in mesh.NormalIdentityArray.Concat(mesh.NormalCornerArray))
        {
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(cursor), (uint)value);
            cursor += 4;
        }
        foreach (float value in mesh.NormalExtraPointArray)
        {
            BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(cursor), value);
            cursor += 4;
        }
        return bytes;
    }
}
