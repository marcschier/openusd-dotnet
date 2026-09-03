// Copyright (c) marcschier. Licensed under the MIT License.

using System.Buffers.Binary;
using System.Text;
using OpenUsd.Rendering.Silk;

namespace OpenUsd.Rendering.Tests;

/// <summary>
/// Covers the ABI v22 subprim identity contract: which authored components a
/// retained mesh answers, which it refuses and why, and the exact primitives an
/// edge or point pick pass rasterizes for it.
/// </summary>
public sealed class SilkSubprimIdentityTests
{
    private static readonly int[] QuadAuthoredEdges = [0, 1, 2, 3];
    private static readonly uint[] QuadEdgeLineIndices = [0, 1, 1, 2, 2, 3, 3, 0];
    private static readonly int[] QuadAuthoredPoints = [0, 1, 2, 3];
    private static readonly int[] ExpandedQuadAuthoredPoints = [0, 1, 2, 0, 2, 3];
    private static readonly uint[] ExpandedQuadPointIndices = [0, 1, 2, 3, 4, 5];
    private static readonly int[] PointDrawModeSubprims = [0, 0, 0, 0];
    private static readonly int[] DuplicatedPointOrigins = [0, 0, 1, 1, 2, 2];
    private static readonly uint[] DuplicatedPointIndices = [0, 1, 2, 3, 4, 5];
    /// <summary>
    /// A quad triangulated into two triangles shares one diagonal, and that
    /// diagonal is the one corner edge no authored edge maps onto.
    /// </summary>
    /// <remarks>
    /// This is the n-gon case the whole edge table exists for. Corner 2 of the
    /// first triangle and corner 0 of the second both span authored points 0
    /// and 2, which the quad never authored an edge between.
    /// </remarks>
    [Test]
    public async Task NgonTriangulationDiagonalsAreNotAuthoredEdges()
    {
        SilkMeshData mesh = CreateQuad();

        await Assert.That(mesh.CornerEdges.Length).IsEqualTo(6);
        await Assert.That(mesh.CornerEdges.Span[0]).IsEqualTo(0);
        await Assert.That(mesh.CornerEdges.Span[1]).IsEqualTo(1);
        await Assert.That(mesh.CornerEdges.Span[2]).IsEqualTo(-1);
        await Assert.That(mesh.CornerEdges.Span[3]).IsEqualTo(-1);
        await Assert.That(mesh.CornerEdges.Span[4]).IsEqualTo(2);
        await Assert.That(mesh.CornerEdges.Span[5]).IsEqualTo(3);
    }

    /// <summary>
    /// The edge pass draws one line per authored edge, ascending, and draws no
    /// line at all for a triangulation diagonal.
    /// </summary>
    [Test]
    public async Task EdgePassDrawsOneLinePerAuthoredEdgeAndNoDiagonal()
    {
        SilkMeshData mesh = CreateQuad();

        bool resolved = SilkSubprimPickGeometry.TryResolveEdges(
            mesh,
            out int[] authoredEdges,
            out uint[] lineIndices);

        await Assert.That(resolved).IsTrue();
        await Assert.That(authoredEdges).IsEquivalentTo(QuadAuthoredEdges);
        await Assert.That(lineIndices.Length).IsEqualTo(8);
        await Assert.That(lineIndices).IsEquivalentTo(QuadEdgeLineIndices);
    }

    /// <summary>
    /// A face-varying mesh emits one vertex per corner, so the same authored
    /// point arrives several times. Every emitted copy is drawn, and every copy
    /// resolves back to the one authored index.
    /// </summary>
    /// <remarks>
    /// Drawing only the first copy would lose the rest: divergent face-varying
    /// normals or displacement can place the copies at visibly different pixels,
    /// and a pick that lands on an undrawn copy would miss a point the user can
    /// plainly see.
    /// </remarks>
    [Test]
    public async Task DuplicatedFaceVaryingVerticesResolveToOneAuthoredPoint()
    {
        SilkMeshData mesh = CreateExpandedQuad();

        bool resolved = SilkSubprimPickGeometry.TryResolvePoints(
            mesh,
            out int[] authoredPoints,
            out uint[] pointIndices);

        await Assert.That(resolved).IsTrue();
        await Assert.That(mesh.Points.Length / 3).IsEqualTo(6);

        // Six emitted vertices produce six drawn points, and the four distinct
        // authored indices behind them are exactly the quad's authored points.
        await Assert.That(authoredPoints).IsEquivalentTo(ExpandedQuadAuthoredPoints);
        await Assert.That(pointIndices).IsEquivalentTo(ExpandedQuadPointIndices);
        await Assert.That(new HashSet<int>(authoredPoints).OrderBy(value => value))
            .IsEquivalentTo(QuadAuthoredPoints);
    }

    /// <summary>
    /// A record that refuses a target names why, and resolves to no primitives
    /// at all rather than to emitted indices standing in for authored ones.
    /// </summary>
    [Test]
    [Arguments(SilkSubprimUnsupportedReason.RefinedSubdivision)]
    [Arguments(SilkSubprimUnsupportedReason.TopologyMode)]
    [Arguments(SilkSubprimUnsupportedReason.Geometry)]
    [Arguments(SilkSubprimUnsupportedReason.Budget)]
    public async Task ARefusedTargetResolvesNothingAndKeepsItsReason(
        SilkSubprimUnsupportedReason reason)
    {
        SilkMeshData mesh = CreateQuad(
            identity: SilkSubprimIdentity.Face,
            unsupported: reason,
            withTables: false);

        await Assert.That(mesh.SubprimUnsupported).IsEqualTo(reason);
        await Assert.That(SilkSubprimPickGeometry.TryResolveEdges(mesh, out _, out _))
            .IsFalse();
        await Assert.That(SilkSubprimPickGeometry.TryResolvePoints(mesh, out _, out _))
            .IsFalse();
    }

    /// <summary>
    /// The identity table allocates a disjoint token range per target, and a
    /// token resolves to the authored component of the target it was drawn for.
    /// </summary>
    [Test]
    public async Task EachTargetOwnsADisjointTokenRangeThatResolvesAuthoredIdentity()
    {
        var table = new SilkPickIdentityTable();
        SilkMeshData mesh = CreateQuad();
        _ = table.Upsert(mesh);

        await Assert.That(table.TryGetRange(
            mesh.Path,
            0,
            SilkPickSubprimKind.Face,
            out SilkPickTokenRange faceRange)).IsTrue();
        await Assert.That(table.TryGetRange(
            mesh.Path,
            0,
            SilkPickSubprimKind.Edge,
            out SilkPickTokenRange edgeRange)).IsTrue();
        await Assert.That(table.TryGetRange(
            mesh.Path,
            0,
            SilkPickSubprimKind.Point,
            out SilkPickTokenRange pointRange)).IsTrue();

        await Assert.That(faceRange.TokenCount).IsEqualTo(2u);
        await Assert.That(edgeRange.TokenCount).IsEqualTo(4u);
        await Assert.That(pointRange.TokenCount).IsEqualTo(4u);
        await Assert.That(edgeRange.FirstToken).IsGreaterThan(faceRange.LastToken);
        await Assert.That(pointRange.FirstToken).IsGreaterThan(edgeRange.LastToken);

        await Assert.That(table.TryResolve(edgeRange.FirstToken + 2, out SilkPickIdentity edge))
            .IsTrue();
        await Assert.That(edge.SubprimKind).IsEqualTo(SilkPickSubprimKind.Edge);
        await Assert.That(edge.SubprimIndex).IsEqualTo(2);

        await Assert.That(table.TryResolve(pointRange.FirstToken + 3, out SilkPickIdentity point))
            .IsTrue();
        await Assert.That(point.SubprimKind).IsEqualTo(SilkPickSubprimKind.Point);
        await Assert.That(point.SubprimIndex).IsEqualTo(3);

        await Assert.That(table.TryResolve(faceRange.FirstToken + 1, out SilkPickIdentity face))
            .IsTrue();
        await Assert.That(face.SubprimKind).IsEqualTo(SilkPickSubprimKind.Face);
        await Assert.That(face.SubprimIndex).IsEqualTo(0);
    }

    /// <summary>
    /// A record that refuses a target allocates no token range for it, so no
    /// GPU token can ever resolve to an identity the record does not have.
    /// </summary>
    [Test]
    public async Task ARefusedTargetAllocatesNoTokenRange()
    {
        var table = new SilkPickIdentityTable();
        SilkMeshData mesh = CreateQuad(
            identity: SilkSubprimIdentity.Face,
            unsupported: SilkSubprimUnsupportedReason.RefinedSubdivision,
            withTables: false);
        _ = table.Upsert(mesh);

        await Assert.That(table.TryGetRange(
            mesh.Path,
            0,
            SilkPickSubprimKind.Edge,
            out SilkPickTokenRange edgeRange)).IsTrue();
        await Assert.That(edgeRange.TokenCount).IsEqualTo(0u);
        await Assert.That(table.TryGetSubprimSupport(
            mesh.Path,
            0,
            out SilkPickSubprimSupport support)).IsTrue();
        await Assert.That(support.Supports(SilkPickSubprimKind.Edge)).IsFalse();
        await Assert.That(support.Supports(SilkPickSubprimKind.Face)).IsTrue();
        await Assert.That(support.Unsupported)
            .IsEqualTo(SilkSubprimUnsupportedReason.RefinedSubdivision);
    }

    /// <summary>
    /// A retained mesh that claims a target without the table behind it is
    /// refused rather than allocating a range no draw could fill.
    /// </summary>
    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public async Task AClaimWithoutATableIsMalformed(bool claimEdges)
    {
        var table = new SilkPickIdentityTable();
        SilkMeshData mesh = CreateQuad(
            identity: claimEdges
                ? SilkSubprimIdentity.Face | SilkSubprimIdentity.Edge
                : SilkSubprimIdentity.Face | SilkSubprimIdentity.Point,
            unsupported: SilkSubprimUnsupportedReason.None,
            withTables: false);

        await Assert.That(() => table.Upsert(mesh))
            .Throws<InvalidDataException>();
    }

    /// <summary>
    /// A point origin outside the authored point count the record declares is
    /// malformed, because a resolved pick would name a component the stage does
    /// not have.
    /// </summary>
    [Test]
    public async Task AnOutOfRangePointOriginIsMalformed()
    {
        var table = new SilkPickIdentityTable();
        SilkMeshData mesh = CreateQuad(pointOriginOverride: [0, 1, 2, 9]);

        await Assert.That(() => table.Upsert(mesh))
            .Throws<InvalidDataException>();
    }

    /// <summary>
    /// A corner edge outside the authored edge count the record declares is
    /// malformed for the same reason.
    /// </summary>
    [Test]
    public async Task AnOutOfRangeCornerEdgeIsMalformed()
    {
        var table = new SilkPickIdentityTable();
        SilkMeshData mesh = CreateQuad(
            cornerEdgeOverride: [0, 1, -1, -1, 2, 12]);

        await Assert.That(() => table.Upsert(mesh))
            .Throws<InvalidDataException>();
    }

    /// <summary>
    /// Two instances of one prototype compose the prototype's authored subprim
    /// identity with their own instance index rather than publishing separate
    /// tables.
    /// </summary>
    [Test]
    public async Task InstancesComposeThePrototypeSubprimIdentity()
    {
        var table = new SilkPickIdentityTable();
        SilkMeshData first = CreateQuad(instanceId: 7, instanceIndex: 0);
        SilkMeshData second = CreateQuad(instanceId: 7, instanceIndex: 3);
        _ = table.Upsert(first);
        _ = table.Upsert(second);

        await Assert.That(table.TryGetRange(
            first.Path,
            3,
            SilkPickSubprimKind.Edge,
            out SilkPickTokenRange secondEdges)).IsTrue();
        await Assert.That(table.TryResolve(
            secondEdges.FirstToken + 1,
            out SilkPickIdentity identity)).IsTrue();
        await Assert.That(identity.InstanceIndex).IsEqualTo(3);
        await Assert.That(identity.SubprimIndex).IsEqualTo(1);
        await Assert.That(identity.SubprimKind).IsEqualTo(SilkPickSubprimKind.Edge);
    }

    /// <summary>
    /// A mesh drawn as points refuses the face target instead of answering it
    /// with the zero the rebuilt per-primitive table carries.
    /// </summary>
    /// <remarks>
    /// The points draw mode replaces a mesh's triangles with one point per
    /// emitted vertex and rebuilds <c>triangle_subprims</c> as one zero per
    /// point, because a point belongs to no triangulated face. The delegate
    /// used to keep the record's FACE claim across that rebuild, so a consumer
    /// that trusts the claim -- which is the only thing it can do -- reported
    /// authored face zero for every point of the mesh. Points stay exact: the
    /// emitted vertex array is untouched, so the point-origin table still names
    /// the authored point behind every drawn point.
    /// </remarks>
    [Test]
    public async Task AMeshDrawnAsPointsRefusesFacePicksAndKeepsExactPointIdentity()
    {
        const string path = "/World/PointDrawModeQuad";
        var scene = new SilkSceneState();
        _ = scene.Apply(
            CreatePointDrawModeMeshUpsert(path, SilkSubprimIdentity.Point),
            1,
            1);
        SilkMeshData mesh = scene.MeshesByPath[(path, 0)];

        await Assert.That(mesh.TopologyKind).IsEqualTo(SilkTopologyKind.PointList);
        await Assert.That(mesh.SubprimIdentity).IsEqualTo(SilkSubprimIdentity.Point);
        await Assert.That(mesh.SubprimUnsupported)
            .IsEqualTo(SilkSubprimUnsupportedReason.TopologyMode);

        // The per-primitive table the draw mode rebuilt carries no authored
        // face at all, which is exactly why the FACE claim had to go.
        await Assert.That(mesh.TriangleSubprims.ToArray())
            .IsEquivalentTo(PointDrawModeSubprims);

        var table = new SilkPickIdentityTable();
        _ = table.Upsert(mesh);

        await Assert.That(table.TryGetSubprimSupport(
            path,
            0,
            out SilkPickSubprimSupport support)).IsTrue();
        await Assert.That(support.Supports(SilkPickSubprimKind.Face)).IsFalse();
        await Assert.That(support.Unsupported)
            .IsEqualTo(SilkSubprimUnsupportedReason.TopologyMode);
        await Assert.That(table.AnswersFacePicks(path, 0)).IsFalse();
        await Assert.That(table.TryGetRange(
            path,
            0,
            SilkPickSubprimKind.Face,
            out SilkPickTokenRange faceRange)).IsTrue();
        await Assert.That(faceRange.TokenCount).IsEqualTo(0u);

        // Point picks still answer, and every drawn point resolves to the
        // authored point the mesh was drawn from.
        await Assert.That(support.Supports(SilkPickSubprimKind.Point)).IsTrue();
        await Assert.That(table.TryGetRange(
            path,
            0,
            SilkPickSubprimKind.Point,
            out SilkPickTokenRange pointRange)).IsTrue();
        await Assert.That(pointRange.TokenCount).IsEqualTo(4u);
        for (uint offset = 0; offset < pointRange.TokenCount; offset++)
        {
            await Assert.That(table.TryResolve(
                pointRange.FirstToken + offset,
                out SilkPickIdentity point)).IsTrue();
            await Assert.That(point.SubprimKind)
                .IsEqualTo(SilkPickSubprimKind.Point);
            await Assert.That(point.SubprimIndex).IsEqualTo((int)offset);
        }
    }

    /// <summary>
    /// The consumer can only trust the wire claim, so the producer is the only
    /// place the face target can be refused.
    /// </summary>
    /// <remarks>
    /// This is the record the delegate used to publish for a mesh drawn as
    /// points: a point list whose per-primitive table is all zeros and which
    /// still claims authored face identity. Nothing on this side can tell that
    /// the zeros are not authored faces, so the support report says the face
    /// target is answered while every entry behind it is face zero. That is the
    /// defect the delegate now prevents by clearing the claim, which
    /// <see cref="AMeshDrawnAsPointsRefusesFacePicksAndKeepsExactPointIdentity"/>
    /// pins.
    /// </remarks>
    [Test]
    public async Task AFaceClaimOverPointsWouldReportFaceZeroForEveryPoint()
    {
        const string path = "/World/StaleFaceClaimQuad";
        var scene = new SilkSceneState();
        _ = scene.Apply(
            CreatePointDrawModeMeshUpsert(
                path,
                SilkSubprimIdentity.Face | SilkSubprimIdentity.Point),
            1,
            1);
        SilkMeshData mesh = scene.MeshesByPath[(path, 0)];

        var table = new SilkPickIdentityTable();
        _ = table.Upsert(mesh);

        await Assert.That(table.TryGetSubprimSupport(
            path,
            0,
            out SilkPickSubprimSupport support)).IsTrue();
        await Assert.That(support.Supports(SilkPickSubprimKind.Face)).IsTrue();
        await Assert.That(mesh.TriangleSubprims.ToArray())
            .IsEquivalentTo(PointDrawModeSubprims);
    }

    /// <summary>
    /// Builds the MESH_UPSERT one mesh drawn as points publishes: a point list
    /// with one emitted point per authored point, the point-origin table that
    /// keeps the point target exact, and no corner-edge table at all.
    /// </summary>
    private static byte[] CreatePointDrawModeMeshUpsert(
        string pathValue,
        SilkSubprimIdentity identity)
    {
        byte[] path = Encoding.UTF8.GetBytes(pathValue);
        float[] points = [0, 0, 0, 1, 0, 0, 1, 1, 0, 0, 1, 0];
        uint[] indices = [0, 1, 2, 3];
        int pointCount = points.Length / 3;
        int size = 268 +
            path.Length +
            (points.Length * sizeof(float)) +
            (indices.Length * sizeof(uint)) +
            (indices.Length * sizeof(uint)) +
            (pointCount * sizeof(uint));
        var bytes = new byte[size];
        BinaryPrimitives.WriteUInt32LittleEndian(
            bytes,
            (uint)SilkCommandType.MeshUpsert);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), (uint)size);
        BinaryPrimitives.WriteUInt64LittleEndian(
            bytes.AsSpan(8),
            SilkWireFormat.ComputeStableHash(pathValue));
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(16), 1);
        BinaryPrimitives.WriteUInt32LittleEndian(
            bytes.AsSpan(28),
            (uint)SilkTopologyKind.PointList);
        BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(32), 1);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(40), 1);
        BinaryPrimitives.WriteUInt32LittleEndian(
            bytes.AsSpan(44),
            (uint)SilkMeshCullStyle.BackUnlessDoubleSided);
        BinaryPrimitives.WriteUInt32LittleEndian(
            bytes.AsSpan(48),
            (uint)path.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(
            bytes.AsSpan(52),
            (uint)pointCount);
        BinaryPrimitives.WriteUInt32LittleEndian(
            bytes.AsSpan(56),
            (uint)indices.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(
            bytes.AsSpan(60),
            (uint)indices.Length);
        for (int component = 0; component < 4; component++)
        {
            BinaryPrimitives.WriteSingleLittleEndian(
                bytes.AsSpan(64 + (component * sizeof(float))),
                1);
        }
        double[] transform = Identity();
        for (int component = 0; component < transform.Length; component++)
        {
            BinaryPrimitives.WriteDoubleLittleEndian(
                bytes.AsSpan(80 + (component * sizeof(double))),
                transform[component]);
        }

        // A point list has no corner an authored edge could map onto, so the
        // edge target is refused by topology exactly as the delegate refuses it.
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(236), (uint)identity);
        BinaryPrimitives.WriteUInt32LittleEndian(
            bytes.AsSpan(240),
            (uint)SilkSubprimUnsupportedReason.TopologyMode);
        BinaryPrimitives.WriteUInt32LittleEndian(
            bytes.AsSpan(244),
            (uint)pointCount);
        BinaryPrimitives.WriteUInt32LittleEndian(
            bytes.AsSpan(256),
            (uint)pointCount);
        path.CopyTo(bytes, 268);
        int pointsOffset = 268 + path.Length;
        for (int component = 0; component < points.Length; component++)
        {
            BinaryPrimitives.WriteSingleLittleEndian(
                bytes.AsSpan(pointsOffset + (component * sizeof(float))),
                points[component]);
        }
        int indicesOffset = pointsOffset + (points.Length * sizeof(float));
        for (int index = 0; index < indices.Length; index++)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(
                bytes.AsSpan(indicesOffset + (index * sizeof(uint))),
                indices[index]);
        }

        // One zero per emitted point: the draw mode rebuilt this table and a
        // point belongs to no authored face, so nothing here is face identity.
        int subprimsOffset = indicesOffset + (indices.Length * sizeof(uint));
        int pointOriginOffset = subprimsOffset + (indices.Length * sizeof(uint));
        for (int entry = 0; entry < pointCount; entry++)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(
                bytes.AsSpan(pointOriginOffset + (entry * sizeof(uint))),
                (uint)entry);
        }
        return bytes;
    }

    /// <summary>
    /// A quad emitted as two triangles over four authored points, with the
    /// authored edge table the delegate would publish for it.
    /// </summary>
    private static SilkMeshData CreateQuad(
        SilkSubprimIdentity identity =
            SilkSubprimIdentity.Face | SilkSubprimIdentity.Edge | SilkSubprimIdentity.Point,
        SilkSubprimUnsupportedReason unsupported = SilkSubprimUnsupportedReason.None,
        bool withTables = true,
        int[]? pointOriginOverride = null,
        int[]? cornerEdgeOverride = null,
        int instanceId = 0,
        int instanceIndex = 0)
    {
        const string path = "/World/Quad";
        return new SilkMeshData(
            primId: 1,
            path,
            SilkWireFormat.ComputeStableHash(path),
            instanceId,
            instanceIndex,
            SilkTopologyKind.TriangleList,
            topologyRevision: 1,
            points: [0, 0, 0, 1, 0, 0, 1, 1, 0, 0, 1, 0],
            indices: [0, 1, 2, 0, 2, 3],
            triangleSubprims: [0, 0],
            displayColor: [1, 1, 1, 1],
            transform: Identity())
        {
            SubprimIdentity = identity,
            SubprimUnsupported = unsupported,
            PointOrigins = pointOriginOverride ??
                (withTables && identity.HasFlag(SilkSubprimIdentity.Point)
                    ? new[] { 0, 1, 2, 3 }
                    : []),
            CornerEdges = cornerEdgeOverride ??
                (withTables && identity.HasFlag(SilkSubprimIdentity.Edge)
                    ? new[] { 0, 1, -1, -1, 2, 3 }
                    : []),
            AuthoredPointCount =
                pointOriginOverride is not null ||
                (withTables && identity.HasFlag(SilkSubprimIdentity.Point))
                    ? 4
                    : 0,
            AuthoredEdgeCount =
                cornerEdgeOverride is not null ||
                (withTables && identity.HasFlag(SilkSubprimIdentity.Edge))
                    ? 4
                    : 0
        };
    }

    /// <summary>
    /// The same quad with its topology expanded for a face-varying primvar: six
    /// emitted vertices, one per corner, over the same four authored points.
    /// </summary>
    private static SilkMeshData CreateExpandedQuad()
    {
        const string path = "/World/ExpandedQuad";
        return new SilkMeshData(
            primId: 2,
            path,
            SilkWireFormat.ComputeStableHash(path),
            instanceId: 0,
            instanceIndex: 0,
            SilkTopologyKind.TriangleList,
            topologyRevision: 1,
            points:
            [
                0, 0, 0,
                1, 0, 0,
                1, 1, 0,
                0, 0, 0,
                1, 1, 0,
                0, 1, 0
            ],
            indices: [0, 1, 2, 3, 4, 5],
            triangleSubprims: [0, 0],
            displayColor: [1, 1, 1, 1],
            transform: Identity())
        {
            SubprimIdentity =
                SilkSubprimIdentity.Face |
                SilkSubprimIdentity.Edge |
                SilkSubprimIdentity.Point,
            SubprimUnsupported = SilkSubprimUnsupportedReason.None,
            PointOrigins = new[] { 0, 1, 2, 0, 2, 3 },
            CornerEdges = new[] { 0, 1, -1, -1, 2, 3 },
            AuthoredPointCount = 4,
            AuthoredEdgeCount = 4
        };
    }

    /// <summary>
    /// A point cloud viewed above Low complexity draws several copies of each
    /// authored point, and every copy resolves back to the one authored point.
    /// </summary>
    /// <remarks>
    /// Complexity duplicates a point list rather than resubdividing it, so each
    /// emitted copy of authored point <c>p</c> is still authored point <c>p</c>.
    /// The delegate used to refuse all subprim identity for any record it
    /// re-emitted, which made a point cloud answer no point pick at all -- and
    /// report an authored point count of zero -- at every complexity above Low,
    /// even though nothing about the mapping had become inexact.
    /// </remarks>
    [Test]
    public async Task EveryComplexityCopyOfOnePointResolvesToItsAuthoredPoint()
    {
        SilkMeshData mesh = CreateDuplicatedPointCloud();

        await Assert.That(mesh.SubprimIdentity).IsEqualTo(SilkSubprimIdentity.Point);
        await Assert.That(mesh.SubprimUnsupported)
            .IsEqualTo(SilkSubprimUnsupportedReason.TopologyMode);
        await Assert.That(mesh.AuthoredPointCount).IsEqualTo(3);

        bool resolved = SilkSubprimPickGeometry.TryResolvePoints(
            mesh,
            out int[] authoredPoints,
            out uint[] pointIndices);

        await Assert.That(resolved).IsTrue();
        await Assert.That(authoredPoints).IsEquivalentTo(DuplicatedPointOrigins);
        await Assert.That(pointIndices).IsEquivalentTo(DuplicatedPointIndices);

        // Every drawn copy owns its own token, and both tokens of one authored
        // point resolve to that authored point rather than to the emitted
        // vertex the token was drawn for.
        var table = new SilkPickIdentityTable();
        _ = table.Upsert(mesh);

        await Assert.That(table.TryGetSubprimSupport(
            mesh.Path,
            0,
            out SilkPickSubprimSupport support)).IsTrue();
        await Assert.That(support.Supports(SilkPickSubprimKind.Point)).IsTrue();
        await Assert.That(support.Supports(SilkPickSubprimKind.Face)).IsFalse();
        await Assert.That(support.Supports(SilkPickSubprimKind.Edge)).IsFalse();

        await Assert.That(table.TryGetRange(
            mesh.Path,
            0,
            SilkPickSubprimKind.Point,
            out SilkPickTokenRange pointRange)).IsTrue();
        await Assert.That(pointRange.TokenCount).IsEqualTo(6u);
        for (uint offset = 0; offset < pointRange.TokenCount; offset++)
        {
            await Assert.That(table.TryResolve(
                pointRange.FirstToken + offset,
                out SilkPickIdentity point)).IsTrue();
            await Assert.That(point.SubprimKind)
                .IsEqualTo(SilkPickSubprimKind.Point);
            await Assert.That(point.SubprimIndex).IsEqualTo((int)(offset / 2));
        }
    }

    /// <summary>
    /// The point list a three-point cloud publishes at Medium complexity: two
    /// emitted copies of every authored point, each copy naming the authored
    /// point it was duplicated from, over the same authored point space.
    /// </summary>
    private static SilkMeshData CreateDuplicatedPointCloud()
    {
        const string path = "/World/DensePointCloud";
        return new SilkMeshData(
            primId: 3,
            path,
            SilkWireFormat.ComputeStableHash(path),
            instanceId: 0,
            instanceIndex: 0,
            SilkTopologyKind.PointList,
            topologyRevision: 1,
            points:
            [
                0, 0, 0,
                0, 0, 0,
                1, 0, 0,
                1, 0, 0,
                2, 0, 0,
                2, 0, 0
            ],
            indices: [0, 1, 2, 3, 4, 5],
            triangleSubprims: [0, 0, 1, 1, 2, 2],
            displayColor: [1, 1, 1, 1],
            transform: Identity())
        {
            SubprimIdentity = SilkSubprimIdentity.Point,
            SubprimUnsupported = SilkSubprimUnsupportedReason.TopologyMode,
            PointOrigins = DuplicatedPointOrigins,
            CornerEdges = Array.Empty<int>(),
            AuthoredPointCount = 3,
            AuthoredEdgeCount = 0
        };
    }

    private static double[] Identity() =>
    [
        1, 0, 0, 0,
        0, 1, 0, 0,
        0, 0, 1, 0,
        0, 0, 0, 1
    ];
}
