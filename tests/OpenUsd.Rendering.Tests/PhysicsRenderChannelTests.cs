// Copyright (c) marcschier. Licensed under the MIT License.

namespace OpenUsd.Rendering.Tests;

public sealed class PhysicsRenderChannelTests
{
    [Test]
    public async Task PublishedSnapshotIsCopiedToTheRenderer()
    {
        var channel = new PhysicsRenderChannel(new PhysicsRenderCapacities(4));
        var destination = new PhysicsRenderSnapshot(new PhysicsRenderCapacities(4));

        await Assert.That(channel.HasSnapshot).IsFalse();
        await Assert.That(channel.TryCopyLatest(destination)).IsFalse();

        ulong revision = PublishStep(channel, step: 1, seconds: 0.1, bodies: 2);

        await Assert.That(revision).IsEqualTo(1ul);
        await Assert.That(channel.HasSnapshot).IsTrue();
        await Assert.That(channel.TryCopyLatest(destination)).IsTrue();
        await Assert.That(destination.Revision).IsEqualTo(1ul);
        await Assert.That(destination.BodyCount).IsEqualTo(2);
        await Assert.That(destination.SimulationSeconds).IsEqualTo(0.1);
    }

    [Test]
    public async Task LatestPublicationWinsOverIntermediateOnes()
    {
        var channel = new PhysicsRenderChannel(new PhysicsRenderCapacities(4));
        var destination = new PhysicsRenderSnapshot(new PhysicsRenderCapacities(4));

        _ = PublishStep(channel, step: 1, seconds: 0.1, bodies: 1);
        _ = PublishStep(channel, step: 2, seconds: 0.2, bodies: 2);
        ulong third = PublishStep(channel, step: 3, seconds: 0.3, bodies: 3);

        await Assert.That(channel.TryCopyLatest(destination)).IsTrue();
        await Assert.That(destination.Revision).IsEqualTo(third);
        await Assert.That(destination.StepIndex).IsEqualTo(3ul);
        await Assert.That(destination.BodyCount).IsEqualTo(3);
        await Assert.That(channel.DroppedPublications).IsEqualTo(0L);
    }

    [Test]
    public async Task ExhaustedRingRefusesWritesInsteadOfBlocking()
    {
        var channel = new PhysicsRenderChannel(new PhysicsRenderCapacities(1));
        PhysicsRenderSnapshot? first = channel.TryBeginWrite();
        PhysicsRenderSnapshot? second = channel.TryBeginWrite();
        PhysicsRenderSnapshot? third = channel.TryBeginWrite();

        PhysicsRenderSnapshot? refused = channel.TryBeginWrite();

        await Assert.That(first).IsNotNull();
        await Assert.That(second).IsNotNull();
        await Assert.That(third).IsNotNull();
        await Assert.That(refused).IsNull();
        await Assert.That(channel.RefusedWrites).IsEqualTo(1L);
        await Assert.That(channel.DroppedPublications).IsEqualTo(1L);

        channel.Abandon(first!);
        await Assert.That(channel.TryBeginWrite()).IsNotNull();
    }

    [Test]
    public async Task TruncatedReadsAreCounted()
    {
        var channel = new PhysicsRenderChannel(new PhysicsRenderCapacities(8));
        var destination = new PhysicsRenderSnapshot(new PhysicsRenderCapacities(2));

        _ = PublishStep(channel, step: 1, seconds: 0.1, bodies: 5);

        await Assert.That(channel.TryCopyLatest(destination)).IsTrue();
        await Assert.That(destination.BodyCount).IsEqualTo(2);
        await Assert.That(channel.TruncatedReads).IsEqualTo(1L);
        await Assert.That(destination.GetDomain(PhysicsRenderDomain.RigidBody).DroppedCount)
            .IsEqualTo(3);
    }

    [Test]
    public async Task InvalidateStopsPublishingStatePoses()
    {
        var channel = new PhysicsRenderChannel(new PhysicsRenderCapacities(4));
        var destination = new PhysicsRenderSnapshot(new PhysicsRenderCapacities(4));
        _ = PublishStep(channel, step: 1, seconds: 0.1, bodies: 2);

        channel.Invalidate();

        await Assert.That(channel.HasSnapshot).IsFalse();
        await Assert.That(channel.TryCopyLatest(destination)).IsFalse();
        for (int index = 0; index < channel.BufferCount; index++)
        {
            await Assert.That(channel.TryBeginWrite()).IsNotNull();
        }
    }

    [Test]
    public async Task IncompleteSnapshotIsNeverPublished()
    {
        var channel = new PhysicsRenderChannel(new PhysicsRenderCapacities(2));
        PhysicsRenderSnapshot snapshot = channel.TryBeginWrite()!;
        snapshot.BeginWrite(1, 1, 0, 0, 1.0 / 60);
        _ = snapshot.TryAddBody(PhysicsRenderSnapshotTests.Body(1, 0, 0, 0));

        _ = await Assert.That(() => channel.Publish(snapshot)).Throws<ArgumentException>();
        await Assert.That(channel.HasSnapshot).IsFalse();
    }

    [Test]
    public async Task ForeignSnapshotIsRejected()
    {
        var channel = new PhysicsRenderChannel(new PhysicsRenderCapacities(2));
        var foreign = new PhysicsRenderSnapshot(new PhysicsRenderCapacities(2));
        foreign.BeginWrite(1, 1, 0, 0, 1.0 / 60);
        foreign.EndWrite();

        _ = await Assert.That(() => channel.Publish(foreign)).Throws<ArgumentException>();
    }

    [Test]
    public async Task ConcurrentPublicationNeverYieldsATornSnapshot()
    {
        var channel = new PhysicsRenderChannel(new PhysicsRenderCapacities(64), bufferCount: 4);
        var destination = new PhysicsRenderSnapshot(new PhysicsRenderCapacities(64));
        const int publications = 4000;
        int torn = 0;
        int reads = 0;

        Task producer = Task.Run(
            () =>
            {
                for (int step = 1; step <= publications; step++)
                {
                    PhysicsRenderSnapshot? snapshot = channel.TryBeginWrite();
                    if (snapshot is null)
                    {
                        continue;
                    }

                    snapshot.BeginWrite(
                        (ulong)step,
                        identityRevision: 1,
                        simulationSeconds: step * 0.01,
                        timeCode: step,
                        fixedStepSeconds: 0.01);
                    int count = (step % 16) + 1;
                    for (int body = 0; body < count; body++)
                    {
                        _ = snapshot.TryAddBody(PhysicsRenderSnapshotTests.Body(
                            (ulong)body + 1,
                            step,
                            step,
                            step));
                    }
                    snapshot.EndWrite();
                    _ = channel.Publish(snapshot);
                }
            });

        Task producerCompletion = producer.WaitAsync(TimeSpan.FromSeconds(30));
        while (!producerCompletion.IsCompleted)
        {
            if (!channel.TryCopyLatest(destination))
            {
                await Task.Yield();
                continue;
            }

            reads++;
            ReadOnlySpan<PhysicsRenderBodyState> bodies = destination.Bodies;
            double expected = destination.SimulationSeconds * 100;
            for (int index = 0; index < bodies.Length; index++)
            {
                // Every body in one snapshot is written from the same step, so a torn read shows
                // up as a body whose position does not match the snapshot's own simulated time.
                if (Math.Abs(bodies[index].Position.X - expected) > 1e-6 ||
                    bodies[index].Position.X != bodies[index].Position.Z)
                {
                    torn++;
                }
            }

            if (destination.BodyCount != ((int)destination.StepIndex % 16) + 1)
            {
                torn++;
            }
        }

        await producerCompletion;
        if (channel.TryCopyLatest(destination))
        {
            reads++;
        }

        await Assert.That(torn).IsEqualTo(0);
        await Assert.That(reads).IsGreaterThan(0);
        await Assert.That(channel.Revision).IsGreaterThan(0ul);
    }

    internal static ulong PublishStep(
        PhysicsRenderChannel channel,
        ulong step,
        double seconds,
        int bodies,
        ulong identityRevision = 1)
    {
        PhysicsRenderSnapshot snapshot = channel.TryBeginWrite() ??
            throw new InvalidOperationException("The channel refused a write.");
        snapshot.BeginWrite(step, identityRevision, seconds, seconds, 1.0 / 60);
        for (int index = 0; index < bodies; index++)
        {
            _ = snapshot.TryAddBody(PhysicsRenderSnapshotTests.Body(
                (ulong)index + 1,
                index + seconds,
                0,
                0));
        }
        snapshot.EndWrite();
        return channel.Publish(snapshot);
    }
}
