// Copyright (c) marcschier. Licensed under the MIT License.

using System.Buffers.Binary;
using System.Text;
using OpenUsd.Rendering.Silk;

namespace OpenUsd.Rendering.Tests;

/// <summary>
/// Proves that renderer-neutral deformable geometry reaches a retained hdSilk mesh.
/// </summary>
/// <remarks>
/// A deforming body publishes one simulated position per rendered vertex rather than a transform,
/// so the backend half of that path replaces the retained mesh's points. Everything else about the
/// mesh - its topology, its transform, its material - is the authored one, because a body that
/// deforms is still the same object.
/// </remarks>
public sealed class SilkPhysicsDeformationTests
{
    private const string MeshPath = "/Cloth";

    private static readonly float[] AuthoredPoints = [-0.5f, -0.5f, 0, 0, 0.5f, 0, 0.5f, -0.5f, 0];

    [Test]
    public async Task ARetainedMeshTakesTheSimulatedPoints()
    {
        var scene = new SilkSceneState();
        _ = scene.Apply(CreateMeshCommand(), 1, 1);
        var bindings = new PhysicsRenderBindingTable(4);
        var id = new PhysicsRenderObjectId(404, PhysicsRenderObjectKind.Deformable);
        _ = bindings.TryBind(id, MeshPath);
        var deformations = new SilkPhysicsDeformations();

        float[] simulated = [-0.5f, 0.25f, 0, 0, 1.25f, 0, 0.5f, 0.25f, 0];
        int applied = deformations.Refresh(scene, bindings, View(id, PhysicsRenderDomain.Cloth, simulated));

        await Assert.That(applied).IsEqualTo(1);
        await Assert.That(deformations.Count).IsEqualTo(1);
        await Assert.That(deformations.Revision).IsGreaterThan(0UL);

        SilkMeshData mesh = scene.MeshesByPath[(MeshPath, 0)];
        await Assert.That(mesh.Points.ToArray()).IsEquivalentTo(simulated);
        await Assert.That(deformations.Contains(mesh.Id)).IsTrue();

        // The authored topology and transform survive a deformation, which is
        // what keeps a deformed prim the prim its author wrote.
        await Assert.That(mesh.Indices.Length).IsEqualTo(3);
        await Assert.That(mesh.Path).IsEqualTo(MeshPath);
    }

    [Test]
    public async Task ARegionThatResolvesToNoMeshIsCountedRatherThanApplied()
    {
        var scene = new SilkSceneState();
        _ = scene.Apply(CreateMeshCommand(), 1, 1);
        var bindings = new PhysicsRenderBindingTable(4);
        var id = new PhysicsRenderObjectId(404, PhysicsRenderObjectKind.Deformable);
        _ = bindings.TryBind(id, "/Absent");
        var deformations = new SilkPhysicsDeformations();

        int applied = deformations.Refresh(
            scene,
            bindings,
            View(id, PhysicsRenderDomain.Cloth, [0, 1, 0, 1, 1, 0, 1, 1, 1]));

        await Assert.That(applied).IsEqualTo(0);

        // A prim that is bound but absent from the rendered scene is its own
        // reason. Counting it as a topology mismatch would send a reader looking
        // for a vertex count problem that does not exist.
        await Assert.That(deformations.MissingMeshRegions).IsEqualTo(1);
        await Assert.That(deformations.MismatchedRegions).IsEqualTo(0);
        await Assert.That(scene.MeshesByPath[(MeshPath, 0)].Points.ToArray())
            .IsEquivalentTo(AuthoredPoints);
    }

    [Test]
    public async Task ARegionWhoseVertexCountDisagreesIsRefusedRatherThanResized()
    {
        var scene = new SilkSceneState();
        _ = scene.Apply(CreateMeshCommand(), 1, 1);
        var bindings = new PhysicsRenderBindingTable(4);
        var id = new PhysicsRenderObjectId(404, PhysicsRenderObjectKind.Deformable);
        _ = bindings.TryBind(id, MeshPath);
        var deformations = new SilkPhysicsDeformations();

        // Two simulated vertices cannot drive a three vertex mesh: the indices
        // the mesh already has would address a vertex that was never simulated.
        int applied = deformations.Refresh(
            scene,
            bindings,
            View(id, PhysicsRenderDomain.Cloth, [0, 1, 0, 1, 1, 0]));

        await Assert.That(applied).IsEqualTo(0);
        await Assert.That(deformations.MismatchedRegions).IsEqualTo(1);
        await Assert.That(scene.MeshesByPath[(MeshPath, 0)].Points.ToArray())
            .IsEquivalentTo(AuthoredPoints);
    }

    [Test]
    public async Task ANonFiniteSimulatedPointNeverReachesAMesh()
    {
        var scene = new SilkSceneState();
        _ = scene.Apply(CreateMeshCommand(), 1, 1);
        var bindings = new PhysicsRenderBindingTable(4);
        var id = new PhysicsRenderObjectId(404, PhysicsRenderObjectKind.Deformable);
        _ = bindings.TryBind(id, MeshPath);
        var deformations = new SilkPhysicsDeformations();

        int applied = deformations.Refresh(
            scene,
            bindings,
            View(id, PhysicsRenderDomain.Cloth, [0, float.NaN, 0, 1, 1, 0, 1, 1, 1]));

        await Assert.That(applied).IsEqualTo(0);
        await Assert.That(scene.MeshesByPath[(MeshPath, 0)].Points.ToArray())
            .IsEquivalentTo(AuthoredPoints);
    }

    [Test]
    public async Task AParticleRegionIsNotDrawnAgainstAMeshItDoesNotDescribe()
    {
        var scene = new SilkSceneState();
        _ = scene.Apply(CreateMeshCommand(), 1, 1);
        var bindings = new PhysicsRenderBindingTable(4);
        var id = new PhysicsRenderObjectId(404, PhysicsRenderObjectKind.Deformable);
        _ = bindings.TryBind(id, MeshPath);
        var deformations = new SilkPhysicsDeformations();

        int applied = deformations.Refresh(
            scene,
            bindings,
            View(id, PhysicsRenderDomain.Particles, [5, 5, 5, 6, 6, 6, 7, 7, 7]));

        await Assert.That(applied).IsEqualTo(0);
        await Assert.That(SilkPhysicsDeformations.IsDomainSupported(PhysicsRenderDomain.Particles))
            .IsFalse();
        await Assert.That(SilkPhysicsDeformations.IsDomainSupported(PhysicsRenderDomain.Cloth)).IsTrue();
        await Assert.That(SilkPhysicsDeformations.IsDomainSupported(PhysicsRenderDomain.Deformable))
            .IsTrue();
        await Assert.That(scene.MeshesByPath[(MeshPath, 0)].Points.ToArray())
            .IsEquivalentTo(AuthoredPoints);
    }

    [Test]
    public async Task AnInterpolatorPublishesTheLatestGeometryWithoutBlendingIt()
    {
        var interpolator = new PhysicsRenderInterpolator(new PhysicsRenderCapacities(4, 4, 32));
        var channel = new PhysicsRenderChannel(new PhysicsRenderCapacities(4, 4, 32));
        var id = new PhysicsRenderObjectId(404, PhysicsRenderObjectKind.Deformable);

        Publish(channel, id, 1, [0, 0, 0, 1, 0, 0, 1, 0, 1]);
        _ = interpolator.TryIngest(channel);
        Publish(channel, id, 2, [0, 2, 0, 1, 2, 0, 1, 2, 1]);
        _ = interpolator.TryIngest(channel);
        _ = interpolator.Update(0.5);

        PhysicsRenderDeformationView view = interpolator.Deformations;

        await Assert.That(view.Count).IsEqualTo(1);
        await Assert.That(view.Revision).IsEqualTo(interpolator.Overrides.Revision);

        // Halfway between two snapshots a rigid pose is blended, but geometry is
        // not: a vertex buffer only corresponds between two snapshots that share
        // a topology, so the latest published one is always drawn whole.
        await Assert.That(view.GetVertices(view.Regions[0]).ToArray())
            .IsEquivalentTo(new float[] { 0, 2, 0, 1, 2, 0, 1, 2, 1 });
    }

    private static void Publish(
        PhysicsRenderChannel channel,
        PhysicsRenderObjectId id,
        ulong step,
        float[] vertices)
    {
        PhysicsRenderSnapshot snapshot = channel.TryBeginWrite()
            ?? throw new InvalidOperationException("The channel refused a write.");
        snapshot.BeginWrite(step, 1, step / 60.0, 0, 1.0 / 60);
        _ = snapshot.TryAddDeformable(id, PhysicsRenderDomain.Cloth, vertices, 7);
        snapshot.EndWrite();
        _ = channel.Publish(snapshot);
    }

    private static PhysicsRenderDeformationView View(
        PhysicsRenderObjectId id,
        PhysicsRenderDomain domain,
        float[] vertices) =>
        new(
            new PhysicsRenderDeformableRegion[]
            {
                new(id, domain, 0, vertices.Length / 3, 7)
            },
            vertices,
            revision: 5);

    private static byte[] CreateMeshCommand()
    {
        byte[] pathBytes = Encoding.UTF8.GetBytes(MeshPath);
        float[] points = AuthoredPoints;
        uint[] indices = [0, 1, 2];
        int size = 268 +
            pathBytes.Length +
            (points.Length * sizeof(float)) +
            (indices.Length * sizeof(uint)) +
            sizeof(uint);
        var bytes = new byte[size];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, (uint)SilkCommandType.MeshUpsert);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), (uint)size);
        BinaryPrimitives.WriteUInt64LittleEndian(
            bytes.AsSpan(8),
            SilkWireFormat.ComputeStableHash(MeshPath));
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(16), 7);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(20), 0);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(24), 0);
        BinaryPrimitives.WriteUInt32LittleEndian(
            bytes.AsSpan(28),
            (uint)SilkTopologyKind.TriangleList);
        BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(32), 1);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(40), 1);
        BinaryPrimitives.WriteUInt32LittleEndian(
            bytes.AsSpan(44),
            (uint)SilkMeshCullStyle.BackUnlessDoubleSided);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(48), (uint)pathBytes.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(52), 3);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(56), 3);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(60), 1);
        for (int i = 0; i < 4; i++)
        {
            BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(64 + (i * 4)), 1);
        }
        for (int i = 0; i < 16; i++)
        {
            BinaryPrimitives.WriteDoubleLittleEndian(
                bytes.AsSpan(80 + (i * 8)),
                i % 5 == 0 ? 1 : 0);
        }

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

        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(cursor), 0);
        return bytes;
    }
}
