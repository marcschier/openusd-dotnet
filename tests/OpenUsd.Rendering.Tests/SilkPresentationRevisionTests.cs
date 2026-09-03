// Copyright (c) marcschier. Licensed under the MIT License.

using System.Buffers.Binary;
using System.Text;
using OpenUsd.Rendering.Silk;

namespace OpenUsd.Rendering.Tests;

/// <summary>
/// Covers the retained-scene half of the presentation topology revision: what a
/// consumer does when one authored mesh arrives drawn several different ways.
/// </summary>
/// <remarks>
/// <c>topology_revision</c> is what a retained scene keys its topology by. A
/// record that arrives carrying the revision the scene already holds is taken
/// to have the topology the scene already holds, and a record that contradicts
/// that is refused outright rather than silently replacing it. Draw mode and
/// complexity rebuild the emitted arrays -- the topology kind, the indices and
/// the point-origin table all change -- while the authored topology behind them
/// does not, so a producer that published the authored revision for every
/// presentation handed this side two topologies under one revision. These tests
/// pin both directions: distinct presentation revisions apply cleanly in
/// sequence, and one revision covering two topologies is still the corruption
/// the refusal exists for.
/// </remarks>
public sealed class SilkPresentationRevisionTests
{
    private const string MeshPath = "/World/PresentedQuad";

    // The quad as the delegate publishes it shaded: four authored points, two
    // triangles, and one point origin per emitted vertex.
    private static readonly float[] QuadPoints = [0, 0, 0, 1, 0, 0, 1, 1, 0, 0, 1, 0];
    private static readonly uint[] QuadTriangleIndices = [0, 1, 2, 0, 2, 3];
    private static readonly uint[] QuadTriangleSubprims = [2, 2];
    private static readonly uint[] QuadPointOrigins = [0, 1, 2, 3];

    // The same quad drawn as points: every emitted vertex is indexed, the
    // per-primitive table carries no authored face, and the point origins are
    // unchanged because the emitted vertex array is.
    private static readonly uint[] PointIndices = [0, 1, 2, 3];
    private static readonly uint[] PointSubprims = [0, 0, 0, 0];

    // And at Medium complexity, where every drawn point is emitted twice.
    private static readonly float[] DensePoints =
        [0, 0, 0, 0, 0, 0, 1, 0, 0, 1, 0, 0, 1, 1, 0, 1, 1, 0, 0, 1, 0, 0, 1, 0];
    private static readonly uint[] DenseIndices = [0, 1, 2, 3, 4, 5, 6, 7];
    private static readonly uint[] DenseSubprims = [0, 0, 0, 0, 0, 0, 0, 0];
    private static readonly uint[] DensePointOrigins = [0, 0, 1, 1, 2, 2, 3, 3];

    private static readonly int[] ExpectedQuadPointOrigins = [0, 1, 2, 3];
    private static readonly int[] ExpectedDensePointOrigins = [0, 0, 1, 1, 2, 2, 3, 3];

    /// <summary>
    /// A shaded page and then a points page of one mesh apply to one retained
    /// scene, and the scene ends up holding the points topology.
    /// </summary>
    /// <remarks>
    /// This is the sequence a user produces by switching the draw mode of a live
    /// viewport. Both pages describe the same authored mesh, so they arrive
    /// under the same path, prim ID and stable hash; only the presentation
    /// revision separates them, and without it the second page was refused as a
    /// topology that changed without a new revision.
    /// </remarks>
    [Test]
    public async Task SequentialShadedAndPointsPagesApplyToOneScene()
    {
        var scene = new SilkSceneState();

        SilkSceneDelta shaded = scene.Apply(ShadedQuad(topologyRevision: 1), 1, 1);
        await Assert.That(shaded.MeshUpserts).IsEqualTo(1);
        await Assert.That(scene.MeshesByPath[(MeshPath, 0)].TopologyKind)
            .IsEqualTo(SilkTopologyKind.TriangleList);

        // The presentation revision of the points page is deliberately not the
        // authored one, and deliberately not derived from it by counting: the
        // producer composes it, and the only thing this side may assume is that
        // it differs.
        SilkSceneDelta points = scene.Apply(PointsQuad(topologyRevision: 0x9E37), 1, 2);

        await Assert.That(points.MeshUpserts).IsEqualTo(1);
        SilkMeshData mesh = scene.MeshesByPath[(MeshPath, 0)];
        await Assert.That(mesh.TopologyKind).IsEqualTo(SilkTopologyKind.PointList);
        await Assert.That(mesh.Points.Length / 3).IsEqualTo(4);
        await Assert.That(mesh.Indices.Length).IsEqualTo(4);
        await Assert.That(mesh.SubprimIdentity).IsEqualTo(SilkSubprimIdentity.Point);
        await Assert.That(mesh.PointOrigins.ToArray())
            .IsEquivalentTo(ExpectedQuadPointOrigins);

        // Every drawn point answers a pick, which is the whole reason the
        // identity had to survive the presentation change.
        await Assert.That(scene.PickIdentities.TryGetRange(
            MeshPath,
            0,
            SilkPickSubprimKind.Point,
            out SilkPickTokenRange pointRange)).IsTrue();
        await Assert.That(pointRange.TokenCount).IsEqualTo(4u);
    }

    /// <summary>
    /// A Low page and then a Medium page of one point list apply to one retained
    /// scene, and the scene ends up holding the duplicated vertices.
    /// </summary>
    /// <remarks>
    /// Complexity needs no draw mode to rebuild a topology: it duplicates a
    /// point list on its own, so a point cloud viewed at two densities produces
    /// exactly the same contradiction a draw-mode switch does.
    /// </remarks>
    [Test]
    public async Task SequentialLowAndMediumComplexityPagesApplyToOneScene()
    {
        var scene = new SilkSceneState();

        _ = scene.Apply(PointsQuad(topologyRevision: 1), 1, 1);
        SilkSceneDelta dense = scene.Apply(DenseQuad(topologyRevision: 0x4F1B), 1, 2);

        await Assert.That(dense.MeshUpserts).IsEqualTo(1);
        SilkMeshData mesh = scene.MeshesByPath[(MeshPath, 0)];
        await Assert.That(mesh.Points.Length / 3).IsEqualTo(8);
        await Assert.That(mesh.SubprimIdentity).IsEqualTo(SilkSubprimIdentity.Point);
        await Assert.That(mesh.AuthoredPointCount).IsEqualTo(4);

        // Every duplicate still names the authored point it was copied from, so
        // a pick on any copy resolves to the one authored index.
        await Assert.That(mesh.PointOrigins.ToArray())
            .IsEquivalentTo(ExpectedDensePointOrigins);
        await Assert.That(scene.PickIdentities.TryGetRange(
            MeshPath,
            0,
            SilkPickSubprimKind.Point,
            out SilkPickTokenRange pointRange)).IsTrue();
        await Assert.That(pointRange.TokenCount).IsEqualTo(8u);
        await Assert.That(scene.PickIdentities.TryResolve(
            pointRange.FirstToken + 5,
            out SilkPickIdentity identity)).IsTrue();
        await Assert.That(identity.SubprimIndex).IsEqualTo(2);
    }

    /// <summary>
    /// Republishing one presentation keeps the retained identity rather than
    /// rotating it.
    /// </summary>
    /// <remarks>
    /// The presentation revision is a pure function of the authored revision and
    /// the presentation, so a page that changed nothing republishes one value.
    /// If it were a counter instead, every redundant page would rotate pick
    /// tokens and invalidate readbacks in flight for a scene nothing moved in.
    /// </remarks>
    [Test]
    public async Task RepublishingOnePresentationKeepsItsRetainedIdentity()
    {
        var scene = new SilkSceneState();
        _ = scene.Apply(PointsQuad(topologyRevision: 0x9E37), 1, 1);
        await Assert.That(scene.PickIdentities.TryGetRange(
            MeshPath,
            0,
            SilkPickSubprimKind.Point,
            out SilkPickTokenRange first)).IsTrue();

        _ = scene.Apply(PointsQuad(topologyRevision: 0x9E37), 1, 2);

        await Assert.That(scene.PickIdentities.TryGetRange(
            MeshPath,
            0,
            SilkPickSubprimKind.Point,
            out SilkPickTokenRange second)).IsTrue();
        await Assert.That(second.FirstToken).IsEqualTo(first.FirstToken);
        await Assert.That(second.TokenCount).IsEqualTo(first.TokenCount);
    }

    /// <summary>
    /// Switching back to a presentation the scene already showed rotates the
    /// retained identity instead of failing, even though the revision goes down.
    /// </summary>
    /// <remarks>
    /// The presentation revision is not a counter: returning to smooth-shaded
    /// returns to the authored revision, which is lower than the composed one
    /// the points page carried. A lower revision is an identity replacement, and
    /// the tokens rotate so a readback already in flight is recognised as stale
    /// rather than resolved against a topology the scene no longer draws.
    /// </remarks>
    [Test]
    public async Task SwitchingBackToAPresentationRotatesTheRetainedIdentity()
    {
        var scene = new SilkSceneState();
        _ = scene.Apply(ShadedQuad(topologyRevision: 1), 1, 1);
        await Assert.That(scene.PickIdentities.TryGetRange(
            MeshPath,
            0,
            SilkPickSubprimKind.Face,
            out SilkPickTokenRange firstShaded)).IsTrue();

        _ = scene.Apply(PointsQuad(topologyRevision: 0x9E37), 1, 2);
        SilkSceneDelta restored = scene.Apply(ShadedQuad(topologyRevision: 1), 1, 3);

        await Assert.That(restored.MeshUpserts).IsEqualTo(1);
        SilkMeshData mesh = scene.MeshesByPath[(MeshPath, 0)];
        await Assert.That(mesh.TopologyKind).IsEqualTo(SilkTopologyKind.TriangleList);
        await Assert.That(scene.PickIdentities.AnswersFacePicks(MeshPath, 0)).IsTrue();
        await Assert.That(scene.PickIdentities.TryGetRange(
            MeshPath,
            0,
            SilkPickSubprimKind.Face,
            out SilkPickTokenRange secondShaded)).IsTrue();
        await Assert.That(secondShaded.TokenCount).IsEqualTo(firstShaded.TokenCount);
        await Assert.That(secondShaded.FirstToken).IsNotEqualTo(firstShaded.FirstToken);
    }

    /// <summary>
    /// Two presentations under one revision are still refused, which is the
    /// corruption the producer's presentation revision exists to avoid.
    /// </summary>
    /// <remarks>
    /// This is the page the delegate used to publish for a draw-mode switch: the
    /// authored revision, unchanged, over a topology it no longer describes.
    /// Nothing on this side can tell that apart from a producer that lost track
    /// of its own topology, so it is refused -- which is exactly why the revision
    /// had to move at the producer instead.
    /// </remarks>
    [Test]
    public async Task APresentationChangeUnderOneRevisionIsStillRefused()
    {
        var scene = new SilkSceneState();
        _ = scene.Apply(ShadedQuad(topologyRevision: 1), 1, 1);

        await Assert.That(() => scene.Apply(PointsQuad(topologyRevision: 1), 1, 2))
            .Throws<InvalidDataException>();
    }

    private static byte[] ShadedQuad(ulong topologyRevision) => MeshUpsert(
        topologyRevision,
        SilkTopologyKind.TriangleList,
        QuadPoints,
        QuadTriangleIndices,
        QuadTriangleSubprims,
        QuadPointOrigins,
        authoredPointCount: 4,
        identity: SilkSubprimIdentity.Face | SilkSubprimIdentity.Point,
        unsupported: SilkSubprimUnsupportedReason.None);

    private static byte[] PointsQuad(ulong topologyRevision) => MeshUpsert(
        topologyRevision,
        SilkTopologyKind.PointList,
        QuadPoints,
        PointIndices,
        PointSubprims,
        QuadPointOrigins,
        authoredPointCount: 4,
        identity: SilkSubprimIdentity.Point,
        unsupported: SilkSubprimUnsupportedReason.TopologyMode);

    private static byte[] DenseQuad(ulong topologyRevision) => MeshUpsert(
        topologyRevision,
        SilkTopologyKind.PointList,
        DensePoints,
        DenseIndices,
        DenseSubprims,
        DensePointOrigins,
        authoredPointCount: 4,
        identity: SilkSubprimIdentity.Point,
        unsupported: SilkSubprimUnsupportedReason.TopologyMode);

    /// <summary>
    /// Builds one MESH_UPSERT command for the presented topology described by
    /// its arguments. Every record it builds names the same authored prim, so
    /// the only thing separating two of them is the presentation revision and
    /// the emitted arrays that revision covers.
    /// </summary>
    private static byte[] MeshUpsert(
        ulong topologyRevision,
        SilkTopologyKind topologyKind,
        float[] points,
        uint[] indices,
        uint[] subprims,
        uint[] pointOrigins,
        uint authoredPointCount,
        SilkSubprimIdentity identity,
        SilkSubprimUnsupportedReason unsupported)
    {
        byte[] path = Encoding.UTF8.GetBytes(MeshPath);
        int size = 268 +
            path.Length +
            (points.Length * sizeof(float)) +
            (indices.Length * sizeof(uint)) +
            (subprims.Length * sizeof(uint)) +
            (pointOrigins.Length * sizeof(uint));
        var bytes = new byte[size];
        BinaryPrimitives.WriteUInt32LittleEndian(
            bytes,
            (uint)SilkCommandType.MeshUpsert);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), (uint)size);
        BinaryPrimitives.WriteUInt64LittleEndian(
            bytes.AsSpan(8),
            SilkWireFormat.ComputeStableHash(MeshPath));
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(16), 1);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(28), (uint)topologyKind);
        BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(32), topologyRevision);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(40), 1);
        BinaryPrimitives.WriteUInt32LittleEndian(
            bytes.AsSpan(44),
            (uint)SilkMeshCullStyle.BackUnlessDoubleSided);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(48), (uint)path.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(
            bytes.AsSpan(52),
            (uint)(points.Length / 3));
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(56), (uint)indices.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(60), (uint)subprims.Length);
        for (int component = 0; component < 4; component++)
        {
            BinaryPrimitives.WriteSingleLittleEndian(
                bytes.AsSpan(64 + (component * sizeof(float))),
                1);
        }
        for (int row = 0; row < 4; row++)
        {
            BinaryPrimitives.WriteDoubleLittleEndian(
                bytes.AsSpan(80 + ((row * 4) + row) * sizeof(double)),
                1);
        }

        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(236), (uint)identity);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(240), (uint)unsupported);
        BinaryPrimitives.WriteUInt32LittleEndian(
            bytes.AsSpan(244),
            (uint)pointOrigins.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(256), authoredPointCount);

        path.CopyTo(bytes, 268);
        int offset = 268 + path.Length;
        for (int component = 0; component < points.Length; component++)
        {
            BinaryPrimitives.WriteSingleLittleEndian(
                bytes.AsSpan(offset + (component * sizeof(float))),
                points[component]);
        }
        offset += points.Length * sizeof(float);
        for (int index = 0; index < indices.Length; index++)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(
                bytes.AsSpan(offset + (index * sizeof(uint))),
                indices[index]);
        }
        offset += indices.Length * sizeof(uint);
        for (int entry = 0; entry < subprims.Length; entry++)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(
                bytes.AsSpan(offset + (entry * sizeof(uint))),
                subprims[entry]);
        }
        offset += subprims.Length * sizeof(uint);
        for (int entry = 0; entry < pointOrigins.Length; entry++)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(
                bytes.AsSpan(offset + (entry * sizeof(uint))),
                pointOrigins[entry]);
        }
        return bytes;
    }
}
