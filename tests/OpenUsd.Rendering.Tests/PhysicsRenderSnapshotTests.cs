// Copyright (c) marcschier. Licensed under the MIT License.

namespace OpenUsd.Rendering.Tests;

public sealed class PhysicsRenderSnapshotTests
{
    [Test]
    public async Task StoresBodiesAndReportsSupportedDomains()
    {
        var snapshot = new PhysicsRenderSnapshot(new PhysicsRenderCapacities(4));

        snapshot.BeginWrite(3, 1, 0.05, 24, 1.0 / 60);
        await Assert.That(snapshot.TryAddBody(Body(1, 1, 2, 3))).IsTrue();
        await Assert.That(snapshot.TryAddBody(Body(2, 4, 5, 6))).IsTrue();
        snapshot.EndWrite();

        await Assert.That(snapshot.IsComplete).IsTrue();
        await Assert.That(snapshot.BodyCount).IsEqualTo(2);
        await Assert.That(snapshot.StepIndex).IsEqualTo(3ul);
        await Assert.That(snapshot.IdentityRevision).IsEqualTo(1ul);
        await Assert.That(snapshot.Bodies[1].Position.X).IsEqualTo(4d);
        PhysicsRenderDomainReport rigid = snapshot.GetDomain(PhysicsRenderDomain.RigidBody);
        await Assert.That(rigid.Status).IsEqualTo(PhysicsRenderDomainStatus.Supported);
        await Assert.That(rigid.Count).IsEqualTo(2);
        await Assert.That(rigid.Capacity).IsEqualTo(4);
        await Assert.That(rigid.IsRenderable).IsTrue();
        await Assert.That(rigid.ToDiagnostic()).IsNull();
        await Assert.That(snapshot.HasOverflow).IsFalse();
    }

    [Test]
    public async Task OverflowIsCountedAndDiagnosedInsteadOfGrowing()
    {
        var snapshot = new PhysicsRenderSnapshot(new PhysicsRenderCapacities(2));

        snapshot.BeginWrite(1, 1, 0, 0, 1.0 / 60);
        _ = snapshot.TryAddBody(Body(1, 0, 0, 0));
        _ = snapshot.TryAddBody(Body(2, 0, 0, 0));
        await Assert.That(snapshot.TryAddBody(Body(3, 0, 0, 0))).IsFalse();
        snapshot.EndWrite();

        await Assert.That(snapshot.BodyCount).IsEqualTo(2);
        await Assert.That(snapshot.HasOverflow).IsTrue();
        PhysicsRenderDomainReport rigid = snapshot.GetDomain(PhysicsRenderDomain.RigidBody);
        await Assert.That(rigid.Status).IsEqualTo(PhysicsRenderDomainStatus.Truncated);
        await Assert.That(rigid.DroppedCount).IsEqualTo(1);
        await Assert.That(rigid.IsRenderable).IsTrue();
        RenderDiagnostic? diagnostic = rigid.ToDiagnostic();
        await Assert.That(diagnostic).IsNotNull();
        await Assert.That(diagnostic!.Code)
            .IsEqualTo(PhysicsRenderDiagnosticCodes.DomainTruncated);
        await Assert.That(diagnostic.Severity).IsEqualTo(RenderDiagnosticSeverity.Warning);
    }

    [Test]
    public async Task CopyIntoSmallerDestinationTruncatesAndReports()
    {
        var source = new PhysicsRenderSnapshot(new PhysicsRenderCapacities(4));
        var destination = new PhysicsRenderSnapshot(new PhysicsRenderCapacities(2));

        source.BeginWrite(7, 2, 0.25, 12, 1.0 / 120);
        for (ulong id = 1; id <= 4; id++)
        {
            _ = source.TryAddBody(Body(id, id, 0, 0));
        }
        source.EndWrite();
        source.CopyTo(destination);

        await Assert.That(destination.BodyCount).IsEqualTo(2);
        await Assert.That(destination.StepIndex).IsEqualTo(7ul);
        await Assert.That(destination.IdentityRevision).IsEqualTo(2ul);
        await Assert.That(destination.SimulationSeconds).IsEqualTo(0.25);
        await Assert.That(destination.IsComplete).IsTrue();
        PhysicsRenderDomainReport rigid = destination.GetDomain(PhysicsRenderDomain.RigidBody);
        await Assert.That(rigid.Status).IsEqualTo(PhysicsRenderDomainStatus.Truncated);
        await Assert.That(rigid.Count).IsEqualTo(2);
        await Assert.That(rigid.DroppedCount).IsEqualTo(2);
    }

    [Test]
    public async Task DeformableRegionsCarryTheirOwnVerticesAndDomainReports()
    {
        var snapshot = new PhysicsRenderSnapshot(new PhysicsRenderCapacities(1, 2, 4));

        snapshot.BeginWrite(1, 1, 0, 0, 1.0 / 60);
        await Assert.That(snapshot.TryAddDeformable(
            new PhysicsRenderObjectId(9, PhysicsRenderObjectKind.Deformable),
            PhysicsRenderDomain.Cloth,
            [0, 1, 2, 3, 4, 5],
            topologyRevision: 4)).IsTrue();
        await Assert.That(snapshot.TryAddDeformable(
            new PhysicsRenderObjectId(10, PhysicsRenderObjectKind.ParticleSystem),
            PhysicsRenderDomain.Particles,
            [6, 7, 8, 9, 10, 11, 12, 13, 14],
            topologyRevision: 1)).IsFalse();
        snapshot.EndWrite();

        await Assert.That(snapshot.DeformableCount).IsEqualTo(1);
        PhysicsRenderDeformableRegion region = snapshot.Deformables[0];
        await Assert.That(region.VertexCount).IsEqualTo(2);
        await Assert.That(region.TopologyRevision).IsEqualTo(4ul);
        float[] vertices = snapshot.GetDeformableVertices(region).ToArray();
        await Assert.That(vertices.Length).IsEqualTo(6);
        await Assert.That(vertices[5]).IsEqualTo(5f);
        await Assert.That(snapshot.GetDomain(PhysicsRenderDomain.Cloth).Status)
            .IsEqualTo(PhysicsRenderDomainStatus.Supported);
        await Assert.That(snapshot.GetDomain(PhysicsRenderDomain.Particles).Status)
            .IsEqualTo(PhysicsRenderDomainStatus.Truncated);
        await Assert.That(snapshot.GetDomain(PhysicsRenderDomain.RigidBody).Status)
            .IsEqualTo(PhysicsRenderDomainStatus.Unavailable);
    }

    [Test]
    public async Task UnavailableDomainDiagnosesWithoutBlockingRigidRendering()
    {
        var snapshot = new PhysicsRenderSnapshot(new PhysicsRenderCapacities(2, 1, 3));

        snapshot.BeginWrite(1, 1, 0, 0, 1.0 / 60);
        _ = snapshot.TryAddBody(Body(1, 1, 1, 1));
        snapshot.SetDomainStatus(PhysicsRenderDomain.Vehicle, PhysicsRenderDomainStatus.Unsupported);
        snapshot.EndWrite();

        await Assert.That(snapshot.GetDomain(PhysicsRenderDomain.RigidBody).IsRenderable).IsTrue();
        PhysicsRenderDomainReport vehicle = snapshot.GetDomain(PhysicsRenderDomain.Vehicle);
        await Assert.That(vehicle.IsRenderable).IsFalse();
        await Assert.That(vehicle.ToDiagnostic()!.Code)
            .IsEqualTo(PhysicsRenderDiagnosticCodes.DomainUnsupported);
        PhysicsRenderDomainReport deformable = snapshot.GetDomain(PhysicsRenderDomain.Deformable);
        await Assert.That(deformable.Status).IsEqualTo(PhysicsRenderDomainStatus.Unavailable);
        await Assert.That(deformable.ToDiagnostic()!.Severity)
            .IsEqualTo(RenderDiagnosticSeverity.Information);
    }

    [Test]
    public async Task StoredOrientationsAreNormalizedAndCanonical()
    {
        var snapshot = new PhysicsRenderSnapshot(new PhysicsRenderCapacities(1));

        snapshot.BeginWrite(1, 1, 0, 0, 1.0 / 60);
        _ = snapshot.TryAddBody(new PhysicsRenderBodyState(
            new PhysicsRenderObjectId(1, PhysicsRenderObjectKind.RigidBody),
            new UsdVec3d(0, 0, 0),
            new PhysicsRenderOrientation(0, 0, 2, -2),
            IsSleeping: false,
            IsKinematic: false));
        snapshot.EndWrite();

        PhysicsRenderOrientation stored = snapshot.Bodies[0].Orientation;
        await Assert.That(stored.W).IsGreaterThanOrEqualTo(0d);
        await Assert.That(Math.Abs(stored.Dot(stored) - 1) < 1e-12).IsTrue();
    }

    [Test]
    public async Task WritingWithoutBeginWriteThrows()
    {
        var snapshot = new PhysicsRenderSnapshot(new PhysicsRenderCapacities(1));

        _ = await Assert.That(() => snapshot.TryAddBody(Body(1, 0, 0, 0)))
            .Throws<InvalidOperationException>();
    }

    [Test]
    public async Task ClearDropsEveryValue()
    {
        var snapshot = new PhysicsRenderSnapshot(new PhysicsRenderCapacities(2));
        snapshot.BeginWrite(5, 5, 1, 1, 1.0 / 60);
        _ = snapshot.TryAddBody(Body(1, 0, 0, 0));
        snapshot.EndWrite();

        snapshot.Clear();

        await Assert.That(snapshot.BodyCount).IsEqualTo(0);
        await Assert.That(snapshot.IsComplete).IsFalse();
        await Assert.That(snapshot.StepIndex).IsEqualTo(0ul);
        await Assert.That(snapshot.GetDomain(PhysicsRenderDomain.RigidBody).Status)
            .IsEqualTo(PhysicsRenderDomainStatus.Unavailable);
    }

    internal static PhysicsRenderBodyState Body(ulong id, double x, double y, double z) =>
        new(
            new PhysicsRenderObjectId(id, PhysicsRenderObjectKind.RigidBody),
            new UsdVec3d(x, y, z),
            PhysicsRenderOrientation.Identity,
            IsSleeping: false,
            IsKinematic: false);
}
