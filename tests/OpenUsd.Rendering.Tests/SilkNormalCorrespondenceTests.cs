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
    public async Task TriangleLocalFallbackSurvivesPageAndSimulationCopies()
    {
        SilkMeshData source = Mesh(
            NgonPoints, NgonIndices, [0, 1, 2, 3, 4], NgonCorners,
            triangleLocalFallback: true);
        using var scene = new SilkSceneState();
        _ = scene.Apply(Encode(source), 1, 1);
        SilkMeshData retained = scene.MeshesByPath[("/Mesh", 0)];
        await Assert.That(retained.TriangleLocalNormalFallback).IsTrue();
        await Assert.That(retained.WithSimulatedPoints(retained.Points.Span).TriangleLocalNormalFallback)
            .IsTrue();
    }

    [Test]
    public async Task TriangleLocalFallbackSeparatesRetainedGeometryIdentity()
    {
        SilkMeshData smooth = Mesh(NgonPoints, NgonIndices, [0, 1, 2, 3, 4], NgonCorners);
        SilkMeshData flat = Mesh(
            NgonPoints, NgonIndices, [0, 1, 2, 3, 4], NgonCorners,
            triangleLocalFallback: true);
        await Assert.That(
            SilkMeshGpuGeometryKey.Create(smooth, "", false) ==
            SilkMeshGpuGeometryKey.Create(flat, "", false)).IsFalse();
    }

    [Test]
    public async Task TriangleLocalFallbackExpandsSharedCornersWithoutChangingRetainedTopology()
    {
        SilkMeshData mesh = FlatSharedMesh();
        SilkMeshGeometry geometry = SilkMeshGeometryBuilder.Build(mesh);

        await Assert.That(geometry.DerivedCornerVertices).IsTrue();
        for (int corner = 0; corner < 6; corner++)
        {
            await Assert.That(geometry.Indices[corner]).IsEqualTo((uint)corner);
        }
        await Assert.That(mesh.Indices.Span[3]).IsEqualTo(0u);
        await Assert.That(mesh.Indices.Span[4]).IsEqualTo(3u);
        await Assert.That(mesh.Indices.Span[5]).IsEqualTo(1u);
        await Assert.That(mesh.Points.Length).IsEqualTo(15);
        for (int corner = 0; corner < 3; corner++)
        {
            await Normal(geometry, corner, 0, 0, 1);
            await Normal(geometry, corner + 3, 0, 1, 0);
        }

        float[] moved = mesh.Points.ToArray();
        moved[10] = 1;
        SilkMeshData simulated = mesh.WithSimulatedPoints(moved);
        SilkMeshGeometry changed = SilkMeshGeometryBuilder.Build(simulated);
        await Normal(changed, 0, 0, 0, 1);
        await Normal(changed, 3, 0, Math.Sqrt(0.5), -Math.Sqrt(0.5));
        SilkMeshGeometry restored = SilkMeshGeometryBuilder.Build(
            simulated.WithSimulatedPoints(mesh.Points.Span));
        await Assert.That(restored.Vertices).IsEquivalentTo(geometry.Vertices);
    }

    [Test]
    public async Task TriangleLocalDisplacementUsesEachCornerNormalAndValidatesUnreferencedPoints()
    {
        SilkMeshData mesh = FlatSharedMesh();
        SilkMeshGeometry geometry = SilkMeshGeometryBuilder.Build(
            mesh, displacementAmounts: [1, 0, 0, 0, 0]);

        await Assert.That(geometry.Vertices[0]).IsEqualTo(0f);
        await Assert.That(geometry.Vertices[1]).IsEqualTo(0f);
        await Assert.That(geometry.Vertices[2]).IsEqualTo(1f);
        await Assert.That(geometry.Vertices[18]).IsEqualTo(0f);
        await Assert.That(geometry.Vertices[19]).IsEqualTo(1f);
        await Assert.That(geometry.Vertices[20]).IsEqualTo(0f);
        await Assert.That(geometry.MaximumDisplacement).IsEqualTo(1f);
        await Assert.That(() => SilkMeshGeometryBuilder.Build(
            mesh, displacementAmounts: [0, 0, 0, 0, float.NaN]))
            .Throws<InvalidDataException>();
    }

    [Test]
    public async Task TriangleLocalFallbackCopiesUvsAndBuildsTriangleLocalTangents()
    {
        SilkVertexAttributeData[] attributes =
        [
            new("st", SilkAttributeSemantic.TexCoord, SilkAttributeInterpolation.Vertex, 2,
                [0, 0, 1, 1, 0, 1, 0, 1, 0.5f, 0.5f])
        ];
        SilkMeshGeometry geometry = SilkMeshGeometryBuilder.Build(
            FlatSharedMesh(attributes: attributes), "st", requireTangents: true);

        await Assert.That(geometry.VertexLayout.Stride).IsEqualTo(48u);
        await Assert.That(geometry.Vertices.AsSpan(6, 2).ToArray())
            .IsEquivalentTo(new float[] { 0, 0 });
        await Assert.That(geometry.Vertices.AsSpan((3 * 12) + 6, 2).ToArray())
            .IsEquivalentTo(new float[] { 0, 0 });
        await Assert.That(geometry.Vertices[8])
            .IsEqualTo(1 / MathF.Sqrt(2)).Within(1e-6f);
        await Assert.That(geometry.Vertices[9])
            .IsEqualTo(-1 / MathF.Sqrt(2)).Within(1e-6f);
        await Assert.That(geometry.Vertices[10]).IsEqualTo(0f).Within(1e-6f);
        await Assert.That(geometry.Vertices[(3 * 12) + 8])
            .IsEqualTo(1 / MathF.Sqrt(2)).Within(1e-6f);
        await Assert.That(geometry.Vertices[(3 * 12) + 10])
            .IsEqualTo(-1 / MathF.Sqrt(2)).Within(1e-6f);
        await Assert.That(geometry.Vertices[11]).IsEqualTo(1f);
        await Assert.That(geometry.Vertices[(3 * 12) + 11]).IsEqualTo(-1f);
    }

    [Test]
    public async Task AuthoredNormalsOverrideTriangleLocalFallbackUntilSimulation()
    {
        SilkMeshData mesh = FlatSharedMesh(
            authoredNormals: [1, 0, 0, 1, 0, 0, 1, 0, 0, 1, 0, 0, 1, 0, 0]);
        SilkMeshGeometry authored = SilkMeshGeometryBuilder.Build(mesh);
        await Assert.That(authored.DerivedCornerVertices).IsFalse();
        await Assert.That(authored.Vertices.Length).IsEqualTo(30);
        await Normal(authored, 0, 1, 0, 0);

        SilkMeshGeometry simulated = SilkMeshGeometryBuilder.Build(
            mesh.WithSimulatedPoints(mesh.Points.Span));
        await Assert.That(simulated.DerivedCornerVertices).IsTrue();
        await Normal(simulated, 0, 0, 0, 1);
        await Normal(simulated, 3, 0, 1, 0);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task FlatNormalsAndPtexShareCornerStreamAcrossSimulationAndRestoration(bool alreadyExpanded)
    {
        SilkMeshData mesh = FlatPtexMesh(alreadyExpanded);
        SilkPtexAtlasLayout atlas = PtexAtlas();
        float[] amounts = alreadyExpanded ? [0.25f, 0, 0, 0.25f, 0, 0] : [0.25f, 0, 0, 0, 0, 0];
        SilkMeshGeometry build(SilkMeshData source) => SilkMeshGeometryBuilder.Build(
            source, "st", requireTangents: true, displacementAmounts: amounts,
            ptexAtlas: atlas, ptexAsSecondary: true);

        SilkMeshGeometry geometry = build(mesh);
        await Assert.That(geometry.DerivedCornerVertices).IsTrue();
        await Assert.That(geometry.PtexUvSecondary).IsTrue();
        await Assert.That(geometry.VertexCount).IsEqualTo(6u);
        await Assert.That(mesh.Points.Length / 3).IsEqualTo(6);
        await Assert.That(geometry.VertexLayout.Stride).IsEqualTo(56u);
        await Assert.That(geometry.Indices.SequenceEqual(new uint[] { 0, 1, 2, 3, 4, 5 })).IsTrue();
        await Assert.That(mesh.Indices.Span.SequenceEqual(alreadyExpanded
            ? new uint[] { 0, 1, 2, 3, 4, 5 }
            : new uint[] { 0, 1, 2, 0, 3, 1 })).IsTrue();

        float[] expectedPtex =
            [0.125f, 0.25f, 0.375f, 0.25f, 0.125f, 0.5f, 0.875f, 0.5f, 0.625f, 0.5f, 0.875f, 0.25f];
        float[] expectedUv = [0, 0, 1, 1, 0, 1, 0, 0, 0, 1, 1, 1];
        for (int corner = 0; corner < 6; corner++)
        {
            int offset = corner * 14;
            await Assert.That(geometry.Vertices[offset + 6]).IsEqualTo(expectedUv[corner * 2]);
            await Assert.That(geometry.Vertices[offset + 7]).IsEqualTo(expectedUv[(corner * 2) + 1]);
            await Assert.That(geometry.Vertices[offset + 12]).IsEqualTo(expectedPtex[corner * 2]);
            await Assert.That(geometry.Vertices[offset + 13]).IsEqualTo(expectedPtex[(corner * 2) + 1]);
            await Normal(geometry, corner, 0, corner < 3 ? 0 : 1, corner < 3 ? 1 : 0);
        }
        await Assert.That(geometry.Vertices[2]).IsEqualTo(0.25f);
        await Assert.That(geometry.Vertices[(3 * 14) + 1]).IsEqualTo(0.25f);
        await Assert.That(geometry.Vertices[8]).IsEqualTo(1 / MathF.Sqrt(2)).Within(1e-6f);
        await Assert.That(geometry.Vertices[9]).IsEqualTo(-1 / MathF.Sqrt(2)).Within(1e-6f);
        await Assert.That(geometry.Vertices[(3 * 14) + 10])
            .IsEqualTo(-1 / MathF.Sqrt(2)).Within(1e-6f);

        float[] moved = mesh.Points.ToArray();
        moved[((alreadyExpanded ? 4 : 3) * 3) + 1] = 1;
        SilkMeshData simulated = mesh.WithSimulatedPoints(moved);
        SilkMeshGeometry changed = build(simulated);
        await Normal(changed, 3, 0, Math.Sqrt(0.5), -Math.Sqrt(0.5));
        await Assert.That(changed.Vertices[(3 * 14) + 12]).IsEqualTo(expectedPtex[6]);
        await Assert.That(changed.Vertices[(3 * 14) + 13]).IsEqualTo(expectedPtex[7]);
        SilkMeshGeometry restored = build(simulated.WithSimulatedPoints(mesh.Points.Span));
        await Assert.That(restored.Vertices.SequenceEqual(geometry.Vertices)).IsTrue();
        await Assert.That(restored.Indices.SequenceEqual(geometry.Indices)).IsTrue();
    }

    [Test]
    public async Task AuthoredNormalsAndPtexRestoreAfterSimulatedFlatNormals()
    {
        SilkMeshData mesh = FlatPtexMesh(alreadyExpanded: false, authoredNormals: true);
        SilkPtexAtlasLayout atlas = PtexAtlas();
        SilkMeshGeometry build(SilkMeshData source) =>
            SilkMeshGeometryBuilder.Build(source, ptexAtlas: atlas);

        SilkMeshGeometry authored = build(mesh);
        await Assert.That(authored.DerivedCornerVertices).IsTrue();
        await Normal(authored, 0, 1, 0, 0);
        await Normal(authored, 3, 1, 0, 0);
        await Assert.That(authored.Vertices[(3 * 8) + 6]).IsEqualTo(0.875f);
        await Assert.That(authored.Vertices[(3 * 8) + 7]).IsEqualTo(0.5f);

        SilkMeshGeometry simulated = build(mesh.WithSimulatedPoints(mesh.Points.Span));
        await Normal(simulated, 0, 0, 0, 1);
        await Normal(simulated, 3, 0, 1, 0);
        await Assert.That(simulated.Vertices[(3 * 8) + 6]).IsEqualTo(0.875f);
        await Assert.That(simulated.Vertices[(3 * 8) + 7]).IsEqualTo(0.5f);
        SilkMeshGeometry restored = build(mesh);
        await Assert.That(restored.Vertices.SequenceEqual(authored.Vertices)).IsTrue();
        await Assert.That(mesh.AuthoredNormals.Length).IsEqualTo(18);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task PtexFallbackCoordinatesCoverEveryFlatCorner(bool ptexSecondary)
    {
        SilkMeshData mesh = FlatPtexMesh(alreadyExpanded: false);
        SilkMeshGeometry geometry = SilkMeshGeometryBuilder.Build(
            mesh,
            ptexSecondary ? "st" : "",
            secondaryUvPrimvar: ptexSecondary ? "" : "st",
            ptexAtlas: SilkPtexAtlasLayout.Fallback,
            ptexAsSecondary: ptexSecondary);

        await Assert.That(geometry.DerivedCornerVertices).IsTrue();
        await Assert.That(geometry.VertexCount).IsEqualTo(6u);
        await Assert.That(geometry.VertexLayout.Stride).IsEqualTo(40u);
        int ptexOffset = ptexSecondary ? 8 : 6;
        for (int corner = 0; corner < 6; corner++)
        {
            await Assert.That(geometry.Vertices[(corner * 10) + ptexOffset]).IsEqualTo(0.5f);
            await Assert.That(geometry.Vertices[(corner * 10) + ptexOffset + 1]).IsEqualTo(0.5f);
        }
    }

    [Test]
    [Arguments(0x80000000u)]
    [Arguments(0x80000004u)]
    public async Task TriangleLocalFallbackRequiresMatchingNonemptyIdentityTable(uint countAndMode)
    {
        SilkMeshData source = Mesh(
            NgonPoints, NgonIndices, [0, 1, 2, 3, 4], NgonCorners,
            triangleLocalFallback: true);
        byte[] bytes = Encode(source);
        int tail = bytes.Length - 12 - checked((int)source.NormalCorrespondenceBytes);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(tail), countAndMode);
        using var scene = new SilkSceneState();
        await Assert.That(() => scene.Apply(bytes, 1, 1)).Throws<InvalidDataException>();
        await Assert.That(scene.MeshesByPath.Count).IsEqualTo(0);
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
    [Arguments(false, false)]
    [Arguments(true, false)]
    [Arguments(false, true)]
    public async Task LightweightInstancesShareNormalTablesWithoutCopyingOrChangingGeometry(bool hole, bool flat)
    {
        SilkMeshData source = hole
            ? Mesh([0, 0, 0, 1, 0, 0, 0, 1, 0], [0, 1, 2], [0, 1, 2],
                [2, 0, 1, 1, 0, 3], normalExtraPoints: [0, 0, 1])
            : ExpandedNgon(flat);
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
        await Assert.That(instance.TriangleLocalNormalFallback).IsEqualTo(flat);
        await Assert.That(instance.Transform.Span[12]).IsEqualTo(7d);
        await Assert.That(SilkMeshGpuGeometryKey.Create(instance, "", false))
            .IsEqualTo(SilkMeshGpuGeometryKey.Create(prototype, "", false));
        await Normal(SilkMeshGeometryBuilder.Build(instance), 0, 0,
            flat ? 0 : Math.Sqrt(0.5), flat ? 1 : Math.Sqrt(0.5));
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

    private static SilkMeshData ExpandedNgon(bool triangleLocalFallback = false)
    {
        var points = new float[NgonIndices.Length * 3];
        for (int corner = 0; corner < NgonIndices.Length; corner++)
        {
            NgonPoints.AsSpan((int)NgonIndices[corner] * 3, 3).CopyTo(points.AsSpan(corner * 3));
        }
        return Mesh(points, Enumerable.Range(0, 9).Select(static index => (uint)index).ToArray(),
            NgonIndices.Select(static index => (int)index).ToArray(),
            [5, 0, 1, 0, 1, 2, 1, 2, 5, 2, 5, 0, 8, 6, 7, 6, 7, 8, 7, 8, 6],
            triangleLocalFallback: triangleLocalFallback);
    }

    private static SilkMeshData FlatSharedMesh(
        float[]? authoredNormals = null, SilkVertexAttributeData[]? attributes = null) =>
        Mesh(
            [0, 0, 0, 1, 0, 0, 0, 1, 0, 0, 0, 1, 2, 2, 2],
            [0, 1, 2, 0, 3, 1],
            [0, 1, 2, 3, 4],
            [2, 0, 1, 0, 1, 2, 1, 2, 0, 1, 0, 3, 0, 3, 1, 3, 1, 0],
            authoredNormals, triangleLocalFallback: true, attributes: attributes);

    private static SilkMeshData FlatPtexMesh(bool alreadyExpanded, bool authoredNormals = false)
    {
        float[] points = [0, 0, 0, 1, 0, 0, 0, 1, 0, 0, 0, 1, 2, 2, 2, 3, 3, 3];
        float[] uv = [0, 0, 1, 1, 0, 1, 0, 1, 0.5f, 0.5f, 0.5f, 0.5f];
        uint[] indices = [0, 1, 2, 0, 3, 1];
        if (alreadyExpanded)
        {
            points = [.. indices.SelectMany(index => points.AsSpan(checked((int)index * 3), 3).ToArray())];
            uv = [.. indices.SelectMany(index => uv.AsSpan(checked((int)index * 2), 2).ToArray())];
            indices = [0, 1, 2, 3, 4, 5];
        }
        float[] normals = authoredNormals
            ? [1, 0, 0, 1, 0, 0, 1, 0, 0, 1, 0, 0, 1, 0, 0, 1, 0, 0]
            : [];
        SilkVertexAttributeData[] attributes =
        [
            new("st", SilkAttributeSemantic.TexCoord, SilkAttributeInterpolation.Vertex, 2, uv)
        ];
        SilkMeshData mesh = Mesh(points, indices, [0, 1, 2, 3, 4, 5], [], normals,
            triangleLocalFallback: true, attributes: attributes);
        return AddPtexMapping(mesh,
            [4, 9], [0, 0, 1, 0, 0, 1, 1, 1, 0, 1, 1, 0]);
    }

    private static SilkPtexAtlasLayout PtexAtlas()
    {
        var first = new SilkPtexAtlasTile(
            4, 1, 2, 2, 2, 0.125f, 0.25f, 0.375f, 0.5f, -1, -1, -1, -1, 0, 0, 0, 0);
        var second = new SilkPtexAtlasTile(
            9, 5, 2, 2, 2, 0.625f, 0.25f, 0.875f, 0.5f, -1, -1, -1, -1, 0, 0, 0, 0);
        return new SilkPtexAtlasLayout(8, 8, new Dictionary<uint, SilkPtexAtlasTile>
        {
            [4] = first,
            [9] = second
        });
    }

    private static SilkMeshData AddPtexMapping(SilkMeshData mesh, int[] faces, float[] coordinates)
    {
        byte[] source = Encode(mesh);
        int normalOffset = source.Length - 12 - checked((int)mesh.NormalCorrespondenceBytes);
        using var stream = new MemoryStream();
        stream.Write(source.AsSpan(0, normalOffset));
        using (var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true))
        {
            if (!mesh.AuthoredNormals.IsEmpty)
            {
                writeAttribute(new SilkVertexAttributeData(
                    "normals", SilkAttributeSemantic.Normal, SilkAttributeInterpolation.Vertex, 3,
                    mesh.AuthoredNormals.ToArray()));
            }
            foreach (SilkVertexAttributeData attribute in mesh.Attributes)
            {
                writeAttribute(attribute);
            }
            for (int face = 0; face < faces.Length; face++)
            {
                writer.Write(faces[face]);
                for (int component = 0; component < 6; component++)
                {
                    writer.Write(coordinates[(face * 6) + component]);
                }
            }

            void writeAttribute(SilkVertexAttributeData attribute)
            {
                byte[] name = Encoding.UTF8.GetBytes(attribute.Name);
                writer.Write((uint)attribute.Semantic);
                writer.Write((uint)attribute.ComponentCount);
                writer.Write((uint)attribute.Interpolation);
                writer.Write((uint)name.Length);
                writer.Write((uint)attribute.ElementCount);
                writer.Write(name);
                foreach (float value in attribute.Data.Span)
                {
                    writer.Write(value);
                }
            }
        }
        stream.Write(source.AsSpan(normalOffset));
        byte[] command = stream.ToArray();
        BinaryPrimitives.WriteUInt32LittleEndian(command.AsSpan(4), checked((uint)command.Length));
        BinaryPrimitives.WriteUInt32LittleEndian(command.AsSpan(220),
            checked((uint)(mesh.Attributes.Count + (mesh.AuthoredNormals.IsEmpty ? 0 : 1))));
        BinaryPrimitives.WriteUInt32LittleEndian(command.AsSpan(268), checked((uint)faces.Length));
        BinaryPrimitives.WriteUInt32LittleEndian(command.AsSpan(272), (uint)SilkPtexMappingStatus.Mapped);
        using var scene = new SilkSceneState();
        _ = scene.Apply(command, 1, 1);
        return scene.MeshesByPath[(mesh.Path, 0)];
    }

    private static SilkMeshData Mesh(
        float[] points, uint[] indices, int[] identities, int[] corners, float[]? authoredNormals = null,
        float[]? normalExtraPoints = null, bool triangleLocalFallback = false,
        SilkVertexAttributeData[]? attributes = null) =>
        new(1, "/Mesh", SilkWireFormat.ComputeStableHash("/Mesh"), 0, 0,
            SilkTopologyKind.TriangleList, 1, points, indices,
            new int[indices.Length / 3], [1, 1, 1, 1],
            [1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1],
            1, true, SilkMeshCullStyle.BackUnlessDoubleSided, authoredNormals ?? [], "", attributes ?? [])
        {
            NormalIdentityArray = identities,
            NormalCornerArray = corners,
            NormalExtraPointArray = normalExtraPoints ?? [],
            TriangleLocalNormalFallback = triangleLocalFallback
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
        uint countAndMode = (uint)mesh.NormalIdentityArray.Length |
            (mesh.TriangleLocalNormalFallback ? 0x80000000u : 0u);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(cursor), countAndMode);
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
