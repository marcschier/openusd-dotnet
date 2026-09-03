// Copyright (c) marcschier. Licensed under the MIT License.

using System.Buffers.Binary;
using System.Text;
using OpenUsd.Rendering.Silk;

namespace OpenUsd.Rendering.Tests;

/// <summary>
/// Gates the retained-scene half of hdSilk point instancing for the subset the
/// native probe publishes: several prototypes under one instancer, proto
/// indices that vary over time, and instances hidden through invisibleIds.
/// </summary>
/// <remarks>
/// hdSilk publishes the instance's own index inside the instancer, so a
/// prototype that owns only part of an instancer publishes a sparse set of
/// indices. With authored proto indices [0, 1, 0, 1] the second prototype owns
/// instancer instances 1 and 3 and never publishes an index-zero record, which
/// is why the retained scene resolves the ABI v8 prototype payload by the
/// lowest index a path published rather than by index zero.
/// </remarks>
public sealed class SilkPointInstancerIdentityTests
{
    private const string AlphaPath = "/World/Instancer/Protos/Alpha";
    private const string BetaPath = "/World/Instancer/Protos/Beta";
    private const int InstancerId = 735836358;
    private const string InstancerPath = "/World/Instancer";

    [Test]
    public async Task ASecondPrototypeResolvesItsPayloadWithoutAnIndexZeroRecord()
    {
        var scene = new SilkSceneState();

        // Proto indices [0, 1, 0, 1]: Alpha owns instancer instances 0 and 2,
        // Beta owns 1 and 3. Both payload records are the lowest index each
        // prototype owns, which for Beta is one.
        _ = scene.Apply(
            Page(
                Prototype(AlphaPath, primId: 11, instanceIndex: 0, translateX: 10),
                Reference(AlphaPath, primId: 11, instanceIndex: 2, translateX: 12),
                Prototype(BetaPath, primId: 12, instanceIndex: 1, translateX: 11),
                Reference(BetaPath, primId: 12, instanceIndex: 3, translateX: 13)),
            4,
            1);

        await Assert.That(scene.MeshesByPath.ContainsKey((AlphaPath, 1))).IsFalse();
        await Assert.That(scene.MeshesByPath.ContainsKey((BetaPath, 0))).IsFalse();

        SilkMeshData betaReference = scene.MeshesByPath[(BetaPath, 3)];
        await Assert.That(betaReference.Points.Length).IsEqualTo(9);
        await Assert.That(betaReference.Indices.Length).IsEqualTo(3);
        await Assert.That(betaReference.Transform.Span[12]).IsEqualTo(13d);

        // The two prototypes share one instancer, so instance identity is only
        // unique because the path participates in the key.
        await Assert.That(scene.MeshesByPath[(AlphaPath, 2)].InstanceId)
            .IsEqualTo(InstancerId);
        await Assert.That(betaReference.InstanceId).IsEqualTo(InstancerId);
        await Assert.That(scene.MeshesByPath[(AlphaPath, 2)].Id)
            .IsNotEqualTo(betaReference.Id);
    }

    [Test]
    public async Task SwappedProtoIndicesRetireIdentitiesInsteadOfRenumberingThem()
    {
        var scene = new SilkSceneState();
        _ = scene.Apply(
            Page(
                Prototype(AlphaPath, primId: 11, instanceIndex: 0, translateX: 10),
                Reference(AlphaPath, primId: 11, instanceIndex: 2, translateX: 12),
                Prototype(BetaPath, primId: 12, instanceIndex: 1, translateX: 11),
                Reference(BetaPath, primId: 12, instanceIndex: 3, translateX: 13)),
            4,
            1);

        // Proto indices become [1, 0, 1, 0]: each prototype now owns the other
        // two instancer instances. Upserts precede removals in a page, so the
        // new payload record is retained before the old one retires.
        _ = scene.Apply(
            Page(
                Prototype(AlphaPath, primId: 11, instanceIndex: 1, translateX: 21),
                Reference(AlphaPath, primId: 11, instanceIndex: 3, translateX: 23),
                Prototype(BetaPath, primId: 12, instanceIndex: 0, translateX: 20),
                Reference(BetaPath, primId: 12, instanceIndex: 2, translateX: 22),
                Removal(AlphaPath, 0),
                Removal(AlphaPath, 2),
                Removal(BetaPath, 1),
                Removal(BetaPath, 3)),
            8,
            2);

        await Assert.That(scene.MeshesByPath.ContainsKey((AlphaPath, 0))).IsFalse();
        await Assert.That(scene.MeshesByPath.ContainsKey((AlphaPath, 2))).IsFalse();
        await Assert.That(scene.MeshesByPath[(AlphaPath, 1)].Transform.Span[12])
            .IsEqualTo(21d);
        await Assert.That(scene.MeshesByPath[(AlphaPath, 3)].Transform.Span[12])
            .IsEqualTo(23d);
        await Assert.That(scene.MeshesByPath[(BetaPath, 0)].Transform.Span[12])
            .IsEqualTo(20d);
        await Assert.That(scene.MeshesByPath[(BetaPath, 2)].Points.Length)
            .IsEqualTo(9);
    }

    [Test]
    public async Task HidingInstanceZeroKeepsTheSurvivingInstanceIndex()
    {
        var scene = new SilkSceneState();
        _ = scene.Apply(
            Page(
                Prototype(BetaPath, primId: 12, instanceIndex: 0, translateX: 20),
                Reference(BetaPath, primId: 12, instanceIndex: 2, translateX: 22)),
            2,
            1);

        SilkPickTokenRange survivor = ResolveRange(scene, BetaPath, 2);

        // invisibleIds hides instancer instance 0. The surviving instance keeps
        // index 2 and inherits the payload because it is now the lowest index
        // the prototype owns.
        _ = scene.Apply(
            Page(
                Prototype(BetaPath, primId: 12, instanceIndex: 2, translateX: 32),
                Removal(BetaPath, 0)),
            2,
            2);

        await Assert.That(scene.MeshesByPath.ContainsKey((BetaPath, 0))).IsFalse();
        await Assert.That(scene.MeshesByPath[(BetaPath, 2)].Transform.Span[12])
            .IsEqualTo(32d);

        // Identity survived the hide: the same token range still resolves to
        // instancer instance 2 rather than being reallocated or renumbered.
        SilkPickTokenRange afterHide = ResolveRange(scene, BetaPath, 2);
        await Assert.That(afterHide.FirstToken).IsEqualTo(survivor.FirstToken);
        await Assert.That(
            scene.PickIdentities.TryResolve(afterHide.FirstToken, out SilkPickIdentity hit))
            .IsTrue();
        await Assert.That(hit.InstanceIndex).IsEqualTo(2);
        await Assert.That(hit.Path).IsEqualTo(BetaPath);
    }

    [Test]
    public async Task AnInstanceReferenceWithoutAPayloadRecordIsRejected()
    {
        var scene = new SilkSceneState();

        await Assert.That(
                () => scene.Apply(Page(Reference(BetaPath, 12, 3, 13)), 1, 1))
            .Throws<InvalidDataException>();
    }

    /// <summary>
    /// A payload record with no geometry is byte-identical on the wire to an
    /// ABI v8 instance reference, so the retained scene cannot tell them apart
    /// and must reject the page rather than resolve the path against itself.
    /// </summary>
    /// <remarks>
    /// hdSilk never emits one: an empty mesh is retired rather than published,
    /// which the native probe gates through <c>/World/HollowInstancer</c>. This
    /// is the consumer half of that invariant, and it is why the guard belongs
    /// in the delegate rather than here -- the wire has no field that could
    /// distinguish the two, so a consumer can only refuse.
    /// </remarks>
    [Test]
    public async Task AnEmptyPayloadRecordIsIndistinguishableFromAReferenceAndIsRejected()
    {
        var scene = new SilkSceneState();

        await Assert.That(
                () => scene.Apply(
                    Page(
                        Reference(BetaPath, primId: 12, instanceIndex: 1, translateX: 11),
                        Reference(BetaPath, primId: 12, instanceIndex: 3, translateX: 13)),
                    2,
                    1))
            .Throws<InvalidDataException>();
    }

    /// <summary>
    /// A page that drops a path entirely leaves the previously retained records
    /// of that path alone, so the retained scene is never torn.
    /// </summary>
    /// <remarks>
    /// hdSilk serializes the records of one path atomically: if any record of a
    /// path fails validation, every record of that path is rolled back out of
    /// the page. The consumer therefore sees the path simply absent from the
    /// page, which must be a no-op for that path rather than a removal.
    /// </remarks>
    [Test]
    public async Task APageThatOmitsAPathEntirelyLeavesItsRetainedRecordsIntact()
    {
        var scene = new SilkSceneState();
        _ = scene.Apply(
            Page(
                Prototype(BetaPath, primId: 12, instanceIndex: 1, translateX: 11),
                Reference(BetaPath, primId: 12, instanceIndex: 3, translateX: 13)),
            2,
            1);

        // The next page carries only the other prototype, exactly as a page
        // that rolled the Beta records back would.
        _ = scene.Apply(
            Page(Prototype(AlphaPath, primId: 11, instanceIndex: 0, translateX: 10)),
            1,
            2);

        await Assert.That(scene.MeshesByPath[(BetaPath, 1)].Transform.Span[12])
            .IsEqualTo(11d);
        await Assert.That(scene.MeshesByPath[(BetaPath, 3)].Points.Length).IsEqualTo(9);
        await Assert.That(
            scene.PickIdentities.TryGetRange(BetaPath, 3, out SilkPickTokenRange _)).IsTrue();

        // And the prototype pointer still resolves, so a later reference for
        // that path is not orphaned by the page that skipped it.
        _ = scene.Apply(
            Page(Reference(BetaPath, primId: 12, instanceIndex: 5, translateX: 15)),
            1,
            3);
        await Assert.That(scene.MeshesByPath[(BetaPath, 5)].Points.Length).IsEqualTo(9);
    }

    /// <summary>
    /// ABI v8 payload elision is topology neutral: an instanced BasisCurves
    /// line list and an instanced Points point list carry their geometry once
    /// on the lowest published index, exactly as a triangle-list prototype
    /// does, and their references resolve against it.
    /// </summary>
    /// <remarks>
    /// Both natively publish a sparse index set as well -- with proto indices
    /// <c>[0, 1, 0]</c> the second prototype of a pair owns instance 1 alone --
    /// so this also covers a non-triangle payload that never publishes an
    /// index-zero record. The native half is gated by <c>/World/CurveInstancer</c>
    /// and <c>/World/PointsInstancer</c> in the hdSilk probe stage.
    /// </remarks>
    [Test]
    [Arguments(SilkTopologyKind.LineList, 6, 2, 1)]
    [Arguments(SilkTopologyKind.PointList, 9, 3, 3)]
    public async Task ANonTriangleInstancedPrototypeElidesItsPayloadAfterTheLowestIndex(
        SilkTopologyKind topologyKind,
        int expectedPointComponents,
        int expectedIndices,
        int expectedSubprims)
    {
        var scene = new SilkSceneState();

        // Proto indices [0, 1, 0]: the first prototype owns instances 0 and 2,
        // the second owns instance 1 alone and puts its payload there.
        _ = scene.Apply(
            Page(
                Prototype(AlphaPath, primId: 21, instanceIndex: 0, translateX: 0, topologyKind),
                Reference(AlphaPath, primId: 21, instanceIndex: 2, translateX: 2, topologyKind),
                Prototype(BetaPath, primId: 22, instanceIndex: 1, translateX: 1, topologyKind)),
            3,
            1);

        SilkMeshData payload = scene.MeshesByPath[(AlphaPath, 0)];
        SilkMeshData reference = scene.MeshesByPath[(AlphaPath, 2)];
        SilkMeshData sparse = scene.MeshesByPath[(BetaPath, 1)];

        await Assert.That(payload.TopologyKind).IsEqualTo(topologyKind);
        await Assert.That(reference.TopologyKind).IsEqualTo(topologyKind);
        await Assert.That(sparse.TopologyKind).IsEqualTo(topologyKind);

        // The reference carried no geometry on the wire and reconstructs the
        // prototype's, so the counts and the fingerprint have to match exactly.
        await Assert.That(reference.Points.Length).IsEqualTo(expectedPointComponents);
        await Assert.That(reference.Indices.Length).IsEqualTo(expectedIndices);
        await Assert.That(reference.TriangleSubprims.Length).IsEqualTo(expectedSubprims);
        await Assert.That(reference.TopologyFingerprint)
            .IsEqualTo(payload.TopologyFingerprint);
        await Assert.That(reference.Points.ToArray()).IsEquivalentTo(payload.Points.ToArray());
        await Assert.That(reference.Indices.ToArray()).IsEquivalentTo(payload.Indices.ToArray());

        // Identity is still per instance, and the transform is the record's own.
        await Assert.That(reference.Transform.Span[12]).IsEqualTo(2d);
        await Assert.That(payload.Transform.Span[12]).IsEqualTo(0d);
        await Assert.That(reference.Id).IsNotEqualTo(payload.Id);

        // The second prototype never published an index-zero record.
        await Assert.That(scene.MeshesByPath.ContainsKey((BetaPath, 0))).IsFalse();
        await Assert.That(sparse.Points.Length).IsEqualTo(expectedPointComponents);

        // And its references resolve against index 1 rather than index zero.
        _ = scene.Apply(
            Page(Reference(BetaPath, primId: 22, instanceIndex: 3, translateX: 3, topologyKind)),
            1,
            2);
        await Assert.That(scene.MeshesByPath[(BetaPath, 3)].Points.Length)
            .IsEqualTo(expectedPointComponents);
        await Assert.That(scene.MeshesByPath[(BetaPath, 3)].TopologyKind)
            .IsEqualTo(topologyKind);
    }

    private static SilkPickTokenRange ResolveRange(
        SilkSceneState scene,
        string path,
        int instanceIndex)
    {
        if (!scene.PickIdentities.TryGetRange(path, instanceIndex, out SilkPickTokenRange range))
        {
            throw new InvalidOperationException(
                $"No retained pick range for '{path}' instance {instanceIndex}.");
        }
        return range;
    }

    private static byte[] Page(params byte[][] commands)
    {
        int size = 0;
        foreach (byte[] command in commands)
        {
            size += command.Length;
        }
        var page = new byte[size];
        int cursor = 0;
        foreach (byte[] command in commands)
        {
            command.CopyTo(page, cursor);
            cursor += command.Length;
        }
        return page;
    }

    private static byte[] Prototype(
        string path,
        int primId,
        int instanceIndex,
        double translateX,
        SilkTopologyKind topologyKind = SilkTopologyKind.TriangleList) =>
        MeshUpsert(path, primId, instanceIndex, translateX, true, topologyKind);

    private static byte[] Reference(
        string path,
        int primId,
        int instanceIndex,
        double translateX,
        SilkTopologyKind topologyKind = SilkTopologyKind.TriangleList) =>
        MeshUpsert(path, primId, instanceIndex, translateX, false, topologyKind);

    private static byte[] MeshUpsert(
        string path,
        int primId,
        int instanceIndex,
        double translateX,
        bool carriesGeometry,
        SilkTopologyKind topologyKind = SilkTopologyKind.TriangleList)
    {
        byte[] pathBytes = Encoding.UTF8.GetBytes(path);
        byte[] instancerPathBytes = Encoding.UTF8.GetBytes(InstancerPath);
        // One primitive per topology kind, with the subprim count the wire
        // requires: three indices per triangle, two per line, one per point.
        (float[] Points, uint[] Indices, uint[] Subprims) geometry = topologyKind switch
        {
            SilkTopologyKind.LineList => ([0f, 0f, 0f, 1f, 0f, 0f], [0u, 1u], [0u]),
            SilkTopologyKind.PointList =>
                ([0f, 0f, 0f, 1f, 0f, 0f, 0f, 1f, 0f], [0u, 1u, 2u], [0u, 1u, 2u]),
            _ => ([0f, 0f, 0f, 1f, 0f, 0f, 0f, 1f, 0f], [0u, 1u, 2u], [0u]),
        };
        float[] points = carriesGeometry ? geometry.Points : [];
        uint[] indices = carriesGeometry ? geometry.Indices : [];
        uint[] subprims = carriesGeometry ? geometry.Subprims : [];
        int size = 268 +
            pathBytes.Length +
            (points.Length * sizeof(float)) +
            (indices.Length * sizeof(uint)) +
            (subprims.Length * sizeof(uint)) +
            instancerPathBytes.Length +
            8 + instancerPathBytes.Length;
        var bytes = new byte[size];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, (uint)SilkCommandType.MeshUpsert);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), (uint)size);
        BinaryPrimitives.WriteUInt64LittleEndian(
            bytes.AsSpan(8),
            SilkWireFormat.ComputeStableHash(path));
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(16), primId);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(20), InstancerId);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(24), instanceIndex);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(28), (uint)topologyKind);
        BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(32), 1);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(40), 1);
        BinaryPrimitives.WriteUInt32LittleEndian(
            bytes.AsSpan(44),
            (uint)SilkMeshCullStyle.BackUnlessDoubleSided);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(48), (uint)pathBytes.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(52), (uint)(points.Length / 3));
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(56), (uint)indices.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(60), (uint)subprims.Length);
        for (int component = 0; component < 4; component++)
        {
            BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(64 + (component * 4)), 1);
        }
        for (int element = 0; element < 16; element++)
        {
            BinaryPrimitives.WriteDoubleLittleEndian(
                bytes.AsSpan(80 + (element * 8)),
                element % 5 == 0 ? 1 : 0);
        }
        BinaryPrimitives.WriteDoubleLittleEndian(bytes.AsSpan(80 + (12 * 8)), translateX);
        BinaryPrimitives.WriteUInt32LittleEndian(
            bytes.AsSpan(260),
            (uint)instancerPathBytes.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(264), 1);

        int cursor = 268;
        pathBytes.CopyTo(bytes.AsSpan(cursor));
        cursor += pathBytes.Length;
        foreach (float value in points)
        {
            BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(cursor), value);
            cursor += sizeof(float);
        }
        foreach (uint value in indices)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(cursor), value);
            cursor += sizeof(uint);
        }
        foreach (uint value in subprims)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(cursor), value);
            cursor += sizeof(uint);
        }
        instancerPathBytes.CopyTo(bytes.AsSpan(cursor));
        cursor += instancerPathBytes.Length;
        BinaryPrimitives.WriteUInt32LittleEndian(
            bytes.AsSpan(cursor),
            (uint)instancerPathBytes.Length);
        BinaryPrimitives.WriteInt32LittleEndian(
            bytes.AsSpan(cursor + 4),
            instanceIndex);
        instancerPathBytes.CopyTo(bytes.AsSpan(cursor + 8));
        return bytes;
    }

    private static byte[] Removal(string path, int instanceIndex)
    {
        byte[] pathBytes = Encoding.UTF8.GetBytes(path);
        int size = 24 + pathBytes.Length;
        var bytes = new byte[size];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, (uint)SilkCommandType.MeshRemove);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), (uint)size);
        BinaryPrimitives.WriteUInt64LittleEndian(
            bytes.AsSpan(8),
            SilkWireFormat.ComputeStableHash(path));
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(16), instanceIndex);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(20), (uint)pathBytes.Length);
        pathBytes.CopyTo(bytes.AsSpan(24));
        return bytes;
    }
}
