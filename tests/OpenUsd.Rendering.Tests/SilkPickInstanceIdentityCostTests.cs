// Copyright (c) marcschier. Licensed under the MIT License.

using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Text;
using OpenUsd.Rendering.Silk;

namespace OpenUsd.Rendering.Tests;

/// <summary>
/// Gates what a scene with many instances of one prototype costs to retain, and
/// what happens to a retained pick token when an instance's place in the
/// instancing hierarchy changes without its topology changing.
/// </summary>
public sealed class SilkPickInstanceIdentityCostTests
{
    private const string ProtoPath = "/World/Instancer/Protos/Alpha";
    private const string InstancerPath = "/World/Instancer";
    private const string OtherInstancerPath = "/World/OtherInstancer";
    private const int InstanceCount = 256;
    private const int PointCount = 4_096;

    /// <summary>
    /// A lightweight instance record shares every prototype payload array
    /// instead of copying it.
    /// </summary>
    /// <remarks>
    /// Copying them made a prototype with a million points cost a million floats
    /// per instance -- O(points x instances) -- for arrays every instance
    /// describes identically. A retained record is immutable and only ever hands
    /// out read-only views, so sharing is safe; reference identity is the only
    /// way to prove no copy happened.
    /// </remarks>
    [Test]
    public async Task AnInstanceRecordSharesEveryPrototypePayloadArray()
    {
        var scene = new SilkSceneState();
        _ = scene.Apply(
            Page(
                Prototype(instanceIndex: 0, pointCount: 64),
                Reference(instanceIndex: 1),
                Reference(instanceIndex: 2)),
            3,
            1);

        SilkMeshData prototype = scene.MeshesByPath[(ProtoPath, 0)];
        SilkMeshData instance = scene.MeshesByPath[(ProtoPath, 2)];

        await Assert.That(SameArray(prototype.Points, instance.Points)).IsTrue();
        await Assert.That(SameArray(prototype.Indices, instance.Indices)).IsTrue();
        await Assert.That(
                SameArray(prototype.TriangleSubprims, instance.TriangleSubprims))
            .IsTrue();
        await Assert.That(
                ReferenceEquals(prototype.SubprimTables, instance.SubprimTables))
            .IsTrue();

        // Instance identity is the instance's own, never the prototype's.
        await Assert.That(instance.InstanceIndex).IsEqualTo(2);
        await Assert.That(instance.InstancerPath).IsEqualTo(InstancerPath);
        await Assert.That(instance.InstancerContext.Count).IsEqualTo(1);
        await Assert.That(instance.InstancerContext[0].InstanceIndex).IsEqualTo(2);
        await Assert.That(prototype.InstancerContext[0].InstanceIndex).IsEqualTo(0);
    }

    /// <summary>
    /// Retaining many instances of one prototype costs identity, not geometry.
    /// </summary>
    /// <remarks>
    /// The assertion is on managed heap growth, because the defect this pins is
    /// an allocation defect: the old code copied the prototype's points, indices
    /// and subprim table once per instance, and derived the prototype's subprim
    /// draw tables once per instance on top of that. At this fixture's size one
    /// copy of the points alone is 48 KiB, so a per-instance budget of 4 KiB is
    /// far below the old cost and far above what the per-instance colour,
    /// transform and chain legitimately need.
    /// <para>
    /// <c>GC.GetTotalMemory</c> measures the whole process, so a test running
    /// beside this one contributes its own allocations to the delta and can push
    /// a correct implementation over the budget. The measurement is therefore
    /// serialized rather than loosened: a bound wide enough to absorb arbitrary
    /// concurrent allocation would no longer separate 4 KiB per instance from
    /// 48 KiB per instance, which is the entire point of the bound.
    /// </para>
    /// </remarks>
    [Test]
    [NotInParallel]
    public async Task ManyInstancesDoNotRetainPerInstanceGeometry()
    {
        var scene = new SilkSceneState();
        _ = scene.Apply(Page(Prototype(instanceIndex: 0, PointCount)), 1, 1);
        SilkMeshData prototype = scene.MeshesByPath[(ProtoPath, 0)];

        var commands = new byte[InstanceCount][];
        for (int instance = 0; instance < InstanceCount; instance++)
        {
            commands[instance] = Reference(instance + 1);
        }
        byte[] page = Page(commands);

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        long before = GC.GetTotalMemory(forceFullCollection: true);
        _ = scene.Apply(page, (uint)InstanceCount, 2);
        long after = GC.GetTotalMemory(forceFullCollection: true);
        long perInstance = (after - before) / InstanceCount;

        await Assert.That(scene.MeshesByPath.Count).IsEqualTo(InstanceCount + 1);
        await Assert.That(perInstance).IsLessThan(4_096);

        // Every retained instance still shares exactly one payload, and the
        // derived subprim tables were derived at most once for the family.
        SilkMeshData last = scene.MeshesByPath[(ProtoPath, InstanceCount)];
        await Assert.That(SameArray(prototype.Points, last.Points)).IsTrue();
        await Assert.That(SameArray(prototype.Indices, last.Indices)).IsTrue();
        await Assert.That(
                ReferenceEquals(prototype.SubprimTables, last.SubprimTables))
            .IsTrue();
        GC.KeepAlive(scene);
    }

    /// <summary>
    /// Two identities resolved from two separate readbacks of one token are
    /// equal and hash equally, including their instancing chains.
    /// </summary>
    /// <remarks>
    /// The compiler-generated comparison of a record struct compares an array
    /// field by reference, so two resolutions of one token were unequal and a
    /// dictionary keyed on the identity grew a new entry per pick -- which is
    /// exactly how a caller de-duplicates repeated picks of one instance.
    /// </remarks>
    [Test]
    public async Task RepeatedResolutionsOfOneTokenAreValueEqual()
    {
        var scene = new SilkSceneState();
        _ = scene.Apply(
            Page(Prototype(instanceIndex: 0, pointCount: 3), Reference(1)),
            2,
            1);
        SilkPickTokenRange range = ResolveRange(scene, 1);

        await Assert.That(
                scene.PickIdentities.TryResolve(
                    range.FirstToken,
                    out SilkPickIdentity first))
            .IsTrue();
        await Assert.That(
                scene.PickIdentities.TryResolve(
                    range.FirstToken,
                    out SilkPickIdentity second))
            .IsTrue();

        await Assert.That(first.InstancerContext.Length).IsEqualTo(1);
        await Assert.That(first).IsEqualTo(second);
        await Assert.That(first.GetHashCode()).IsEqualTo(second.GetHashCode());

        var seen = new Dictionary<SilkPickIdentity, int>();
        for (int pick = 0; pick < 8; pick++)
        {
            _ = scene.PickIdentities.TryResolve(
                range.FirstToken,
                out SilkPickIdentity identity);
            seen[identity] = seen.TryGetValue(identity, out int count) ? count + 1 : 1;
        }

        await Assert.That(seen.Count).IsEqualTo(1);
        await Assert.That(seen[first]).IsEqualTo(8);
    }

    /// <summary>
    /// Two identities that differ only in one instancing level are not equal, so
    /// value equality cannot collapse two scene instances into one.
    /// </summary>
    [Test]
    public async Task IdentitiesWithDifferentChainsAreNotEqual()
    {
        var scene = new SilkSceneState();
        _ = scene.Apply(
            Page(
                Prototype(instanceIndex: 0, pointCount: 3),
                Reference(1),
                Reference(2)),
            3,
            1);

        _ = scene.PickIdentities.TryResolve(
            ResolveRange(scene, 1).FirstToken,
            out SilkPickIdentity first);
        _ = scene.PickIdentities.TryResolve(
            ResolveRange(scene, 2).FirstToken,
            out SilkPickIdentity second);

        await Assert.That(first).IsNotEqualTo(second);
        await Assert.That(first.InstancerContext[0].InstanceIndex).IsEqualTo(1);
        await Assert.That(second.InstancerContext[0].InstanceIndex).IsEqualTo(2);
    }

    /// <summary>
    /// A record that keeps its composite index and its topology revision but
    /// changes its instancer path gets a new compact identity, a new token
    /// range, and a new identity revision.
    /// </summary>
    /// <remarks>
    /// Nothing the topology check looks at moves when an instancer is
    /// retargeted, so the table used to return the existing range and the old
    /// token kept resolving through the old chain -- reporting an instance the
    /// scene no longer contains, and letting a readback already in flight be
    /// re-resolved instead of recognised as stale.
    /// <para>
    /// hdSilk derives the record's instance ID from its instancer path, so the
    /// retarget changes the ID as well and the two changes arrive together. The
    /// fixture derives the ID the same way the renderer does, because pinning
    /// one constant across both paths published a combination the renderer can
    /// never emit and hid the fact that the changed ID alone used to be refused
    /// as corruption.
    /// </para>
    /// </remarks>
    [Test]
    public async Task ChangingTheInstancerPathReplacesTheTokenAndAdvancesTheRevision()
    {
        var scene = new SilkSceneState();
        _ = scene.Apply(Page(Prototype(instanceIndex: 0, pointCount: 3)), 1, 1);
        SilkPickTokenRange before = ResolveRange(scene, 0);
        ulong revisionBefore = scene.PickIdentities.Revision;

        // The token a readback already in flight is holding.
        _ = scene.PickIdentities.TryResolve(
            before.FirstToken,
            out SilkPickIdentity staleCandidate);
        await Assert.That(staleCandidate.InstanceId)
            .IsEqualTo(StableInstanceId(InstancerPath));

        _ = scene.Apply(
            Page(Prototype(
                instanceIndex: 0,
                pointCount: 3,
                instancerPath: OtherInstancerPath)),
            1,
            2);
        SilkPickTokenRange after = ResolveRange(scene, 0);

        await Assert.That(after.FirstToken).IsNotEqualTo(before.FirstToken);
        await Assert.That(scene.PickIdentities.Revision).IsGreaterThan(revisionBefore);
        await Assert.That(scene.PickIdentities.TryResolve(before.FirstToken, out _))
            .IsFalse();
        await Assert.That(
                scene.PickIdentities.TryResolve(
                    after.FirstToken,
                    out SilkPickIdentity identity))
            .IsTrue();
        await Assert.That(identity.InstancerPath).IsEqualTo(OtherInstancerPath);
        await Assert.That(identity.InstancerContext[^1].InstancerPath)
            .IsEqualTo(OtherInstancerPath);
        await Assert.That(identity.InstanceId)
            .IsEqualTo(StableInstanceId(OtherInstancerPath));
        await Assert.That(identity.InstanceId).IsNotEqualTo(staleCandidate.InstanceId);
    }

    /// <summary>
    /// An instance ID that changes while the instancer path and the whole
    /// ordered chain stay identical is still refused.
    /// </summary>
    /// <remarks>
    /// The ID is derived from the path, so with the path unchanged it cannot
    /// legitimately move. Accepting it would let two different instances share
    /// one retained identity, which is the corruption the stable-identity check
    /// exists for; only a changed instancing position is evidence of a
    /// replacement.
    /// </remarks>
    [Test]
    public async Task AnInstanceIdChangeWithAnUnchangedInstancerIsRefused()
    {
        var table = new SilkPickIdentityTable();
        _ = table.Upsert(InstancedMesh(StableInstanceId(InstancerPath)));
        ulong revision = table.Revision;

        await Assert.That(() => table.Upsert(
                InstancedMesh(StableInstanceId(OtherInstancerPath))))
            .Throws<InvalidDataException>();
        await Assert.That(table.Revision).IsEqualTo(revision);
    }

    private static SilkMeshData InstancedMesh(int instanceId) =>
        new(
            11,
            ProtoPath,
            SilkWireFormat.ComputeStableHash(ProtoPath),
            instanceId,
            0,
            SilkTopologyKind.TriangleList,
            1,
            [0, 0, 0, 1, 0, 0, 0, 1, 0],
            [0, 1, 2],
            [0],
            [1, 1, 1, 1],
            [1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1])
        {
            InstancerPath = InstancerPath,
            InstancerContext = [new SilkInstancerContextEntry(InstancerPath, 0)]
        };

    /// <summary>
    /// A record that gains an outer instancing level, with the same composite
    /// index and the same topology revision, is treated the same way.
    /// </summary>
    [Test]
    public async Task ChangingTheOrderedChainReplacesTheTokenAndAdvancesTheRevision()
    {
        var scene = new SilkSceneState();
        _ = scene.Apply(Page(Prototype(instanceIndex: 0, pointCount: 3)), 1, 1);
        SilkPickTokenRange before = ResolveRange(scene, 0);
        ulong revisionBefore = scene.PickIdentities.Revision;

        _ = scene.Apply(
            Page(Prototype(
                instanceIndex: 0,
                pointCount: 3,
                nested: true)),
            1,
            2);
        SilkPickTokenRange after = ResolveRange(scene, 0);

        await Assert.That(after.FirstToken).IsNotEqualTo(before.FirstToken);
        await Assert.That(scene.PickIdentities.Revision).IsGreaterThan(revisionBefore);
        await Assert.That(scene.PickIdentities.TryResolve(before.FirstToken, out _))
            .IsFalse();
        await Assert.That(
                scene.PickIdentities.TryResolve(
                    after.FirstToken,
                    out SilkPickIdentity identity))
            .IsTrue();
        await Assert.That(identity.InstancerContext.Length).IsEqualTo(2);
        await Assert.That(identity.InstancerContext[0].InstancerPath)
            .IsEqualTo("/World/Root");
        await Assert.That(identity.InstancerContext[1].InstancerPath)
            .IsEqualTo(InstancerPath);
    }

    /// <summary>
    /// An unchanged record keeps its token and its revision, so the instancer
    /// check does not churn identity on every republished frame.
    /// </summary>
    [Test]
    public async Task AnUnchangedInstancerChainKeepsTheTokenAndTheRevision()
    {
        var scene = new SilkSceneState();
        _ = scene.Apply(Page(Prototype(instanceIndex: 0, pointCount: 3)), 1, 1);
        SilkPickTokenRange before = ResolveRange(scene, 0);
        ulong revisionBefore = scene.PickIdentities.Revision;

        _ = scene.Apply(Page(Prototype(instanceIndex: 0, pointCount: 3)), 1, 2);
        SilkPickTokenRange after = ResolveRange(scene, 0);

        await Assert.That(after.FirstToken).IsEqualTo(before.FirstToken);
        await Assert.That(scene.PickIdentities.Revision).IsEqualTo(revisionBefore);
    }

    private static SilkPickTokenRange ResolveRange(
        SilkSceneState scene,
        int instanceIndex)
    {
        if (!scene.PickIdentities.TryGetRange(
                ProtoPath,
                instanceIndex,
                out SilkPickTokenRange range))
        {
            throw new InvalidOperationException(
                $"No retained pick range for instance {instanceIndex}.");
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
        int instanceIndex,
        int pointCount,
        string instancerPath = InstancerPath,
        bool nested = false) =>
        MeshUpsert(instanceIndex, pointCount, instancerPath, nested);

    private static byte[] Reference(int instanceIndex) =>
        MeshUpsert(instanceIndex, pointCount: 0, InstancerPath, nested: false);

    private static byte[] MeshUpsert(
        int instanceIndex,
        int pointCount,
        string instancerPath,
        bool nested)
    {
        byte[] pathBytes = Encoding.UTF8.GetBytes(ProtoPath);
        byte[] instancerPathBytes = Encoding.UTF8.GetBytes(instancerPath);
        byte[] outerPathBytes = Encoding.UTF8.GetBytes("/World/Root");
        int triangleCount = pointCount / 3;
        var points = new float[pointCount * 3];
        var indices = new uint[triangleCount * 3];
        var subprims = new uint[triangleCount];
        for (int point = 0; point < pointCount; point++)
        {
            points[point * 3] = point;
        }
        for (int triangle = 0; triangle < triangleCount; triangle++)
        {
            indices[triangle * 3] = (uint)(triangle * 3);
            indices[(triangle * 3) + 1] = (uint)((triangle * 3) + 1);
            indices[(triangle * 3) + 2] = (uint)((triangle * 3) + 2);
            subprims[triangle] = (uint)triangle;
        }

        int contextBytes = 8 + instancerPathBytes.Length;
        if (nested)
        {
            contextBytes += 8 + outerPathBytes.Length;
        }
        int size = 268 +
            pathBytes.Length +
            (points.Length * sizeof(float)) +
            (indices.Length * sizeof(uint)) +
            (subprims.Length * sizeof(uint)) +
            instancerPathBytes.Length +
            contextBytes;
        var bytes = new byte[size];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, (uint)SilkCommandType.MeshUpsert);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), (uint)size);
        BinaryPrimitives.WriteUInt64LittleEndian(
            bytes.AsSpan(8),
            SilkWireFormat.ComputeStableHash(ProtoPath));
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(16), 11);
        BinaryPrimitives.WriteInt32LittleEndian(
            bytes.AsSpan(20),
            StableInstanceId(instancerPath));
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(24), instanceIndex);
        BinaryPrimitives.WriteUInt32LittleEndian(
            bytes.AsSpan(28),
            (uint)SilkTopologyKind.TriangleList);
        BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(32), 1);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(40), 1);
        BinaryPrimitives.WriteUInt32LittleEndian(
            bytes.AsSpan(44),
            (uint)SilkMeshCullStyle.BackUnlessDoubleSided);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(48), (uint)pathBytes.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(52), (uint)pointCount);
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
        BinaryPrimitives.WriteUInt32LittleEndian(
            bytes.AsSpan(260),
            (uint)instancerPathBytes.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(264), nested ? 2u : 1u);

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
        if (nested)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(
                bytes.AsSpan(cursor),
                (uint)outerPathBytes.Length);
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(cursor + 4), 0);
            outerPathBytes.CopyTo(bytes.AsSpan(cursor + 8));
            cursor += 8 + outerPathBytes.Length;
        }
        BinaryPrimitives.WriteUInt32LittleEndian(
            bytes.AsSpan(cursor),
            (uint)instancerPathBytes.Length);
        BinaryPrimitives.WriteInt32LittleEndian(
            bytes.AsSpan(cursor + 4),
            instanceIndex);
        instancerPathBytes.CopyTo(bytes.AsSpan(cursor + 8));
        return bytes;
    }

    /// <summary>
    /// Reproduces <c>HdSilkStableInstanceId</c>: hdSilk derives a record's
    /// instance ID from its instancer path, so a fixture that pinned one
    /// constant across two different instancer paths published a combination the
    /// renderer can never see and hid the case where the ID moves with the path.
    /// </summary>
    private static int StableInstanceId(string instancerPath)
    {
        ulong hash = 14695981039346656037ul;
        foreach (byte value in Encoding.UTF8.GetBytes(instancerPath))
        {
            hash ^= value;
            hash *= 1099511628211ul;
        }
        int folded = (int)((uint)(hash ^ (hash >> 32)) & 0x7FFFFFFFu);
        return folded == 0 ? 1 : folded;
    }

    private static bool SameArray<T>(ReadOnlyMemory<T> left, ReadOnlyMemory<T> right)
    {
        if (!MemoryMarshal.TryGetArray(left, out ArraySegment<T> leftSegment) ||
            !MemoryMarshal.TryGetArray(right, out ArraySegment<T> rightSegment))
        {
            throw new InvalidOperationException("Could not inspect a retained array.");
        }
        return ReferenceEquals(leftSegment.Array, rightSegment.Array);
    }
}
