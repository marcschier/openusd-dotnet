// Copyright (c) marcschier. Licensed under the MIT License.

namespace OpenUsd.Rendering.Tests;

public sealed class PhysicsRenderInterpolatorTests
{
    [Test]
    public async Task FirstSnapshotSnapsBecauseThereIsNothingToBlendAgainst()
    {
        var channel = new PhysicsRenderChannel(new PhysicsRenderCapacities(8));
        var interpolator = new PhysicsRenderInterpolator(new PhysicsRenderCapacities(8));
        Publish(channel, step: 1, seconds: 0.1, [(1, 0, 0, 0)]);

        await Assert.That(interpolator.TryIngest(channel)).IsTrue();
        PhysicsRenderUpdateResult result = interpolator.Update(0.1);

        await Assert.That(result.Status).IsEqualTo(PhysicsRenderUpdateStatus.Snapped);
        await Assert.That(result.SnappedCount).IsEqualTo(1);
        await Assert.That(result.Count).IsEqualTo(1);
        await Assert.That(interpolator.Overrides.Items[0].Snapped).IsTrue();
    }

    [Test]
    public async Task EmptyInterpolatorProducesNoOverride()
    {
        var interpolator = new PhysicsRenderInterpolator(new PhysicsRenderCapacities(4));

        PhysicsRenderUpdateResult result = interpolator.Update(1);

        await Assert.That(result.Status).IsEqualTo(PhysicsRenderUpdateStatus.Empty);
        await Assert.That(interpolator.Overrides.IsEmpty).IsTrue();
        await Assert.That(interpolator.HasSnapshot).IsFalse();
        await Assert.That(interpolator.GetDomain(PhysicsRenderDomain.RigidBody).Status)
            .IsEqualTo(PhysicsRenderDomainStatus.Unavailable);
    }

    [Test]
    [Arguments(0.0, 0.0)]
    [Arguments(0.25, 2.5)]
    [Arguments(0.5, 5.0)]
    [Arguments(1.0, 10.0)]
    public async Task PositionsAreInterpolatedBetweenTheTwoLatestSnapshots(
        double fraction,
        double expectedX)
    {
        var channel = new PhysicsRenderChannel(new PhysicsRenderCapacities(8));
        var interpolator = new PhysicsRenderInterpolator(new PhysicsRenderCapacities(8));
        Publish(channel, step: 1, seconds: 1.0, [(1, 0, 0, 0)]);
        _ = interpolator.TryIngest(channel);
        Publish(channel, step: 2, seconds: 2.0, [(1, 10, 20, 30)]);
        _ = interpolator.TryIngest(channel);

        PhysicsRenderUpdateResult result = interpolator.Update(1.0 + fraction);

        await Assert.That(result.Status).IsEqualTo(PhysicsRenderUpdateStatus.Interpolated);
        await Assert.That(result.InterpolatedCount).IsEqualTo(1);
        PhysicsRenderTransformOverride value = interpolator.Overrides.Items[0];
        await Assert.That(value.Snapped).IsFalse();
        await Assert.That(Math.Abs(value.Position.X - expectedX) < 1e-9).IsTrue();
        await Assert.That(Math.Abs(value.Position.Y - (expectedX * 2)) < 1e-9).IsTrue();
        await Assert.That(Math.Abs(value.Position.Z - (expectedX * 3)) < 1e-9).IsTrue();
    }

    [Test]
    public async Task RenderTimeOutsideTheSnapshotSpanIsClamped()
    {
        var channel = new PhysicsRenderChannel(new PhysicsRenderCapacities(8));
        var interpolator = new PhysicsRenderInterpolator(new PhysicsRenderCapacities(8));
        Publish(channel, step: 1, seconds: 1.0, [(1, 0, 0, 0)]);
        _ = interpolator.TryIngest(channel);
        Publish(channel, step: 2, seconds: 2.0, [(1, 10, 0, 0)]);
        _ = interpolator.TryIngest(channel);

        PhysicsRenderUpdateResult behind = interpolator.Update(0.25);
        double behindX = interpolator.Overrides.Items[0].Position.X;
        PhysicsRenderUpdateResult ahead = interpolator.Update(9.5);
        double aheadX = interpolator.Overrides.Items[0].Position.X;

        await Assert.That(behind.Alpha).IsEqualTo(0d);
        await Assert.That(behindX).IsEqualTo(0d);
        await Assert.That(ahead.Alpha).IsEqualTo(1d);
        await Assert.That(aheadX).IsEqualTo(10d);
    }

    [Test]
    public async Task OrientationsAreBlendedAlongTheShortestArc()
    {
        var channel = new PhysicsRenderChannel(new PhysicsRenderCapacities(4));
        var interpolator = new PhysicsRenderInterpolator(new PhysicsRenderCapacities(4));
        // Two representations of the same pair of rotations, one written with the negated (long
        // way round) quaternion for the later snapshot.
        PhysicsRenderOrientation start = AboutZ(-Math.PI / 2);
        PhysicsRenderOrientation end = AboutZ(Math.PI / 2);
        PhysicsRenderOrientation negatedEnd = new(-end.X, -end.Y, -end.Z, -end.W);

        PhysicsRenderOrientation direct = Blend(channel, interpolator, start, end);
        var secondChannel = new PhysicsRenderChannel(new PhysicsRenderCapacities(4));
        var secondInterpolator = new PhysicsRenderInterpolator(new PhysicsRenderCapacities(4));
        PhysicsRenderOrientation negated = Blend(
            secondChannel,
            secondInterpolator,
            start,
            negatedEnd);

        // Halfway between -90 and +90 degrees along the shortest arc is the identity rotation.
        await Assert.That(Math.Abs(direct.W - 1) < 1e-9).IsTrue();
        await Assert.That(Math.Abs(direct.Z) < 1e-9).IsTrue();
        await Assert.That(Math.Abs(negated.W - direct.W) < 1e-9).IsTrue();
        await Assert.That(Math.Abs(negated.Z - direct.Z) < 1e-9).IsTrue();
        await Assert.That(direct.W).IsGreaterThanOrEqualTo(0d);
        await Assert.That(negated.W).IsGreaterThanOrEqualTo(0d);
    }

    [Test]
    public async Task ChangedIdentityRevisionSnapsInsteadOfBlendingStaleValues()
    {
        var channel = new PhysicsRenderChannel(new PhysicsRenderCapacities(8));
        var interpolator = new PhysicsRenderInterpolator(new PhysicsRenderCapacities(8));
        Publish(channel, step: 1, seconds: 1.0, [(1, 0, 0, 0)]);
        _ = interpolator.TryIngest(channel);
        Publish(channel, step: 2, seconds: 2.0, [(1, 10, 0, 0)], identityRevision: 2);
        _ = interpolator.TryIngest(channel);

        PhysicsRenderUpdateResult result = interpolator.Update(1.5);

        await Assert.That(result.Status).IsEqualTo(PhysicsRenderUpdateStatus.Snapped);
        await Assert.That(interpolator.Overrides.Items[0].Position.X).IsEqualTo(10d);
        await Assert.That(interpolator.Discontinuities).IsGreaterThan(0L);
    }

    [Test]
    public async Task RewoundStepIndexSnapsRatherThanRunningBackwards()
    {
        var channel = new PhysicsRenderChannel(new PhysicsRenderCapacities(8));
        var interpolator = new PhysicsRenderInterpolator(new PhysicsRenderCapacities(8));
        Publish(channel, step: 90, seconds: 9.0, [(1, 90, 0, 0)]);
        _ = interpolator.TryIngest(channel);
        Publish(channel, step: 1, seconds: 0.1, [(1, 1, 0, 0)]);
        _ = interpolator.TryIngest(channel);

        PhysicsRenderUpdateResult result = interpolator.Update(0.1);

        await Assert.That(result.Status).IsEqualTo(PhysicsRenderUpdateStatus.Snapped);
        await Assert.That(interpolator.Overrides.Items[0].Position.X).IsEqualTo(1d);
    }

    [Test]
    public async Task NewEntityIsSnappedWhileKnownEntitiesKeepInterpolating()
    {
        var channel = new PhysicsRenderChannel(new PhysicsRenderCapacities(8));
        var interpolator = new PhysicsRenderInterpolator(new PhysicsRenderCapacities(8));
        Publish(channel, step: 1, seconds: 1.0, [(1, 0, 0, 0)]);
        _ = interpolator.TryIngest(channel);
        Publish(channel, step: 2, seconds: 2.0, [(1, 10, 0, 0), (2, 50, 0, 0)]);
        _ = interpolator.TryIngest(channel);

        PhysicsRenderUpdateResult result = interpolator.Update(1.5);

        await Assert.That(result.Status).IsEqualTo(PhysicsRenderUpdateStatus.Interpolated);
        await Assert.That(result.InterpolatedCount).IsEqualTo(1);
        await Assert.That(result.SnappedCount).IsEqualTo(1);
        PhysicsRenderTransformOverride[] items = interpolator.Overrides.Items.ToArray();
        await Assert.That(items[0].Position.X).IsEqualTo(5d);
        await Assert.That(items[1].Position.X).IsEqualTo(50d);
        await Assert.That(items[1].Snapped).IsTrue();
    }

    [Test]
    public async Task DeletedEntityStopsProducingAnOverride()
    {
        var channel = new PhysicsRenderChannel(new PhysicsRenderCapacities(8));
        var interpolator = new PhysicsRenderInterpolator(new PhysicsRenderCapacities(8));
        Publish(channel, step: 1, seconds: 1.0, [(1, 0, 0, 0), (2, 100, 0, 0)]);
        _ = interpolator.TryIngest(channel);
        Publish(channel, step: 2, seconds: 2.0, [(1, 10, 0, 0)]);
        _ = interpolator.TryIngest(channel);

        PhysicsRenderUpdateResult result = interpolator.Update(1.5);

        await Assert.That(result.Count).IsEqualTo(1);
        await Assert.That(interpolator.Overrides.Items[0].Id.Value).IsEqualTo(1ul);
    }

    [Test]
    public async Task IdentitiesAreStableAcrossSnapshotsAndReorderings()
    {
        var channel = new PhysicsRenderChannel(new PhysicsRenderCapacities(8));
        var interpolator = new PhysicsRenderInterpolator(new PhysicsRenderCapacities(8));
        Publish(channel, step: 1, seconds: 1.0, [(1, 0, 0, 0), (2, 100, 0, 0)]);
        _ = interpolator.TryIngest(channel);
        Publish(channel, step: 2, seconds: 2.0, [(2, 200, 0, 0), (1, 10, 0, 0)]);
        _ = interpolator.TryIngest(channel);

        PhysicsRenderUpdateResult result = interpolator.Update(1.5);
        PhysicsRenderTransformOverride[] items = interpolator.Overrides.Items.ToArray();

        await Assert.That(result.InterpolatedCount).IsEqualTo(2);
        await Assert.That(items[0].Id.Value).IsEqualTo(2ul);
        await Assert.That(items[0].Position.X).IsEqualTo(150d);
        await Assert.That(items[1].Id.Value).IsEqualTo(1ul);
        await Assert.That(items[1].Position.X).IsEqualTo(5d);
    }

    [Test]
    public async Task SnapshotsBeyondBoundedStorageAreTruncatedAndCounted()
    {
        var channel = new PhysicsRenderChannel(new PhysicsRenderCapacities(8));
        var interpolator = new PhysicsRenderInterpolator(new PhysicsRenderCapacities(2));
        (ulong, double, double, double)[] bodies =
        [
            (1, 1, 0, 0),
            (2, 2, 0, 0),
            (3, 3, 0, 0),
            (4, 4, 0, 0)
        ];
        Publish(channel, step: 1, seconds: 1.0, bodies);
        _ = interpolator.TryIngest(channel);

        PhysicsRenderUpdateResult result = interpolator.Update(1.0);

        await Assert.That(result.Count).IsEqualTo(2);
        await Assert.That(result.DroppedCount).IsEqualTo(0);
        await Assert.That(channel.TruncatedReads).IsEqualTo(1L);
        PhysicsRenderDomainReport rigid = interpolator.GetDomain(PhysicsRenderDomain.RigidBody);
        await Assert.That(rigid.Status).IsEqualTo(PhysicsRenderDomainStatus.Truncated);
        await Assert.That(rigid.DroppedCount).IsEqualTo(2);
        await Assert.That(rigid.ToDiagnostic()!.Code)
            .IsEqualTo(PhysicsRenderDiagnosticCodes.DomainTruncated);
    }

    [Test]
    public async Task ResetDropsEveryOverrideSoAuthoredStateIsRestored()
    {
        var channel = new PhysicsRenderChannel(new PhysicsRenderCapacities(8));
        var interpolator = new PhysicsRenderInterpolator(new PhysicsRenderCapacities(8));
        Publish(channel, step: 1, seconds: 1.0, [(1, 0, 0, 0)]);
        _ = interpolator.TryIngest(channel);
        _ = interpolator.Update(1.0);

        interpolator.Reset();
        PhysicsRenderUpdateResult afterReset = interpolator.Update(1.0);

        await Assert.That(afterReset.Status).IsEqualTo(PhysicsRenderUpdateStatus.Empty);
        await Assert.That(interpolator.Overrides.IsEmpty).IsTrue();
        await Assert.That(interpolator.HasSnapshot).IsFalse();
        await Assert.That(interpolator.IngestedSnapshots).IsEqualTo(0L);
    }

    [Test]
    public async Task IngestIsSkippedWhenNothingNewWasPublished()
    {
        var channel = new PhysicsRenderChannel(new PhysicsRenderCapacities(8));
        var interpolator = new PhysicsRenderInterpolator(new PhysicsRenderCapacities(8));
        Publish(channel, step: 1, seconds: 1.0, [(1, 0, 0, 0)]);

        await Assert.That(interpolator.TryIngest(channel)).IsTrue();
        await Assert.That(interpolator.TryIngest(channel)).IsFalse();
        await Assert.That(interpolator.IngestedSnapshots).IsEqualTo(1L);
    }

    [Test]
    public async Task OverrideRevisionAdvancesOnEveryUpdate()
    {
        var channel = new PhysicsRenderChannel(new PhysicsRenderCapacities(4));
        var interpolator = new PhysicsRenderInterpolator(new PhysicsRenderCapacities(4));
        Publish(channel, step: 1, seconds: 1.0, [(1, 0, 0, 0)]);
        _ = interpolator.TryIngest(channel);

        ulong first = interpolator.Update(1.0).Revision;
        ulong second = interpolator.Update(1.0).Revision;

        await Assert.That(second).IsGreaterThan(first);
        await Assert.That(interpolator.Overrides.Revision).IsEqualTo(second);
    }

    [Test]
    public async Task UnsupportedDomainsAreReportedWithoutStoppingRigidRendering()
    {
        var channel = new PhysicsRenderChannel(new PhysicsRenderCapacities(4, 1, 3));
        var interpolator = new PhysicsRenderInterpolator(new PhysicsRenderCapacities(4, 1, 3));
        PhysicsRenderSnapshot snapshot = channel.TryBeginWrite()!;
        snapshot.BeginWrite(1, 1, 1.0, 1.0, 1.0 / 60);
        _ = snapshot.TryAddBody(PhysicsRenderSnapshotTests.Body(1, 5, 0, 0));
        snapshot.SetDomainStatus(
            PhysicsRenderDomain.Vehicle,
            PhysicsRenderDomainStatus.Unsupported);
        _ = snapshot.TryAddDeformable(
            new PhysicsRenderObjectId(7, PhysicsRenderObjectKind.Deformable),
            PhysicsRenderDomain.Cloth,
            [0, 0, 0, 1, 1, 1],
            topologyRevision: 3);
        snapshot.EndWrite();
        _ = channel.Publish(snapshot);
        _ = interpolator.TryIngest(channel);

        PhysicsRenderUpdateResult result = interpolator.Update(1.0);

        await Assert.That(result.Count).IsEqualTo(1);
        await Assert.That(interpolator.GetDomain(PhysicsRenderDomain.RigidBody).IsRenderable)
            .IsTrue();
        await Assert.That(interpolator.GetDomain(PhysicsRenderDomain.Vehicle).Status)
            .IsEqualTo(PhysicsRenderDomainStatus.Unsupported);
        await Assert.That(interpolator.GetDeformables().Length).IsEqualTo(1);
        await Assert.That(
            interpolator.GetDeformableVertices(interpolator.GetDeformables()[0]).Length)
            .IsEqualTo(6);
    }

    [Test]
    public async Task WarmedRenderUpdateDoesNotAllocate()
    {
        var channel = new PhysicsRenderChannel(new PhysicsRenderCapacities(64));
        var interpolator = new PhysicsRenderInterpolator(new PhysicsRenderCapacities(64));
        const int bodyCount = 64;
        const int warmupSteps = 32;
        const int measuredSteps = 1000;
        const int maximumMeasuredPasses = 8;
        const int requiredConsecutiveZeroPasses = 2;

        static void run(
            PhysicsRenderChannel channel,
            PhysicsRenderInterpolator interpolator,
            ulong firstStep,
            int steps,
            int bodies)
        {
            for (int index = 0; index < steps; index++)
            {
                ulong step = firstStep + (ulong)index;
                PhysicsRenderSnapshot? snapshot = channel.TryBeginWrite();
                if (snapshot is not null)
                {
                    snapshot.BeginWrite(step, 1, step * 0.01, step * 0.01, 0.01);
                    for (int body = 0; body < bodies; body++)
                    {
                        _ = snapshot.TryAddBody(PhysicsRenderSnapshotTests.Body(
                            (ulong)body + 1,
                            step * 0.5,
                            body,
                            0));
                    }
                    snapshot.EndWrite();
                    _ = channel.Publish(snapshot);
                }

                _ = interpolator.TryIngest(channel);
                _ = interpolator.Update((step * 0.01) - 0.005);
            }
        }

        run(channel, interpolator, 1, warmupSteps, bodyCount);

        int consecutiveZeroPasses = 0;
        long allocated = 0;
        for (int pass = 0; pass < maximumMeasuredPasses; pass++)
        {
            ulong firstStep = 1_000 + ((ulong)pass * measuredSteps);
            long before = GC.GetAllocatedBytesForCurrentThread();
            run(channel, interpolator, firstStep, measuredSteps, bodyCount);
            allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            consecutiveZeroPasses = allocated == 0 ? consecutiveZeroPasses + 1 : 0;
            if (consecutiveZeroPasses == requiredConsecutiveZeroPasses)
            {
                break;
            }
        }

        await Assert.That(consecutiveZeroPasses).IsEqualTo(requiredConsecutiveZeroPasses);
        await Assert.That(allocated).IsEqualTo(0L);
        await Assert.That(interpolator.InterpolatedEntities).IsGreaterThan(0L);
    }

    private static PhysicsRenderOrientation Blend(
        PhysicsRenderChannel channel,
        PhysicsRenderInterpolator interpolator,
        PhysicsRenderOrientation start,
        PhysicsRenderOrientation end)
    {
        PublishOriented(channel, step: 1, seconds: 1.0, start);
        _ = interpolator.TryIngest(channel);
        PublishOriented(channel, step: 2, seconds: 2.0, end);
        _ = interpolator.TryIngest(channel);
        _ = interpolator.Update(1.5);
        return interpolator.Overrides.Items[0].Orientation;
    }

    private static PhysicsRenderOrientation AboutZ(double radians) =>
        new(0, 0, Math.Sin(radians / 2), Math.Cos(radians / 2));

    private static void PublishOriented(
        PhysicsRenderChannel channel,
        ulong step,
        double seconds,
        PhysicsRenderOrientation orientation)
    {
        PhysicsRenderSnapshot snapshot = channel.TryBeginWrite()!;
        snapshot.BeginWrite(step, 1, seconds, seconds, 1.0 / 60);
        _ = snapshot.TryAddBody(new PhysicsRenderBodyState(
            new PhysicsRenderObjectId(1, PhysicsRenderObjectKind.RigidBody),
            new UsdVec3d(0, 0, 0),
            orientation,
            IsSleeping: false,
            IsKinematic: false));
        snapshot.EndWrite();
        _ = channel.Publish(snapshot);
    }

    private static void Publish(
        PhysicsRenderChannel channel,
        ulong step,
        double seconds,
        (ulong Id, double X, double Y, double Z)[] bodies,
        ulong identityRevision = 1)
    {
        PhysicsRenderSnapshot snapshot = channel.TryBeginWrite() ??
            throw new InvalidOperationException("The channel refused a write.");
        snapshot.BeginWrite(step, identityRevision, seconds, seconds, 1.0 / 60);
        foreach ((ulong id, double x, double y, double z) in bodies)
        {
            _ = snapshot.TryAddBody(PhysicsRenderSnapshotTests.Body(id, x, y, z));
        }
        snapshot.EndWrite();
        _ = channel.Publish(snapshot);
    }
}
