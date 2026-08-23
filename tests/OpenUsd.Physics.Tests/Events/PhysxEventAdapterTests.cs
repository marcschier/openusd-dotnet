// Copyright (c) marcschier. Licensed under the MIT License.

using OpenUsd.Physics.Interop;

namespace OpenUsd.Physics.Tests.Events;

public sealed class PhysxEventAdapterTests
{
    [Test]
    public async Task ContactEventsCarryBothActorAndColliderIdentities()
    {
        var record = new PhysxEventRecord
        {
            Id0 = 100,
            Id1 = 200,
            Detail0 = 101,
            Detail1 = 201,
            StepIndex = 12,
            Type = (uint)PhysxEventType.ContactFound,
            Flags = (uint)(PhysxEventFlags.DetailIsShape | PhysxEventFlags.HasPosition |
                PhysxEventFlags.HasNormal | PhysxEventFlags.HasImpulse),
            Position = new PhysxVec3f(1.0F, 2.0F, 3.0F),
            Normal = new PhysxVec3f(0.0F, 1.0F, 0.0F),
            Impulse = 7.5F
        };

        UsdPhysicsEvent detached = PhysxEventAdapter.Detach(record, 24.5);

        await Assert.That(detached.Kind).IsEqualTo(UsdPhysicsEventKind.ContactBegan);
        await Assert.That(detached.Primary.Value).IsEqualTo(100UL);
        await Assert.That(detached.Secondary!.Value.Value).IsEqualTo(200UL);
        await Assert.That(detached.PrimaryElement!.Value.Value).IsEqualTo(101UL);
        await Assert.That(detached.PrimaryElement!.Value.Kind).IsEqualTo(UsdPhysicsObjectKind.Collider);
        await Assert.That(detached.SecondaryElement!.Value.Value).IsEqualTo(201UL);
        await Assert.That(detached.StepIndex).IsEqualTo(12UL);
        await Assert.That(detached.TimeCode).IsEqualTo(24.5);
        await Assert.That(detached.Position!.Value.Y).IsEqualTo(2.0);
        await Assert.That(detached.Normal!.Value.Y).IsEqualTo(1.0);
        await Assert.That(detached.Impulse!.Value).IsEqualTo(7.5);
    }

    [Test]
    public async Task AbsentGeometryIsReportedAsAbsentRatherThanAsZero()
    {
        var record = new PhysxEventRecord
        {
            Id0 = 100,
            Id1 = 200,
            Type = (uint)PhysxEventType.ContactLost,
            Flags = (uint)PhysxEventFlags.DetailIsShape
        };

        UsdPhysicsEvent detached = PhysxEventAdapter.Detach(record, 0);

        await Assert.That(detached.Kind).IsEqualTo(UsdPhysicsEventKind.ContactEnded);
        await Assert.That(detached.Position).IsNull();
        await Assert.That(detached.Normal).IsNull();
        await Assert.That(detached.Impulse).IsNull();
        await Assert.That(detached.PrimaryElement).IsNull();
        await Assert.That(detached.SecondaryElement).IsNull();
    }

    [Test]
    public async Task SleepAndWakeShareOneKindAndDifferByState()
    {
        var sleep = new PhysxEventRecord { Id0 = 5, Type = (uint)PhysxEventType.Sleep, StepIndex = 3 };
        var wake = new PhysxEventRecord { Id0 = 5, Type = (uint)PhysxEventType.Wake, StepIndex = 4 };

        UsdPhysicsEvent detachedSleep = PhysxEventAdapter.Detach(sleep, 1.0);
        UsdPhysicsEvent detachedWake = PhysxEventAdapter.Detach(wake, 1.0);

        await Assert.That(detachedSleep.Kind).IsEqualTo(UsdPhysicsEventKind.SleepStateChanged);
        await Assert.That(detachedWake.Kind).IsEqualTo(UsdPhysicsEventKind.SleepStateChanged);
        await Assert.That(detachedSleep.IsAsleep).IsTrue();
        await Assert.That(detachedWake.IsAsleep).IsFalse();
        await Assert.That(detachedSleep.Secondary).IsNull();
    }

    [Test]
    public async Task JointBreakNamesTheJointAndBothJointedBodies()
    {
        var record = new PhysxEventRecord
        {
            Id0 = 900,
            Id1 = 10,
            Detail0 = 11,
            StepIndex = 2,
            Type = (uint)PhysxEventType.JointBreak
        };

        UsdPhysicsEvent detached = PhysxEventAdapter.Detach(record, 0);

        await Assert.That(detached.Kind).IsEqualTo(UsdPhysicsEventKind.JointBreak);
        await Assert.That(detached.Primary.Kind).IsEqualTo(UsdPhysicsObjectKind.Joint);
        await Assert.That(detached.Secondary!.Value.Kind).IsEqualTo(UsdPhysicsObjectKind.RigidBody);
        await Assert.That(detached.PrimaryElement!.Value.Value).IsEqualTo(11UL);
        await Assert.That(detached.PrimaryElement!.Value.Kind).IsEqualTo(UsdPhysicsObjectKind.RigidBody);
    }

    [Test]
    public async Task TriggerEventsAlwaysNameTheTriggerFirst()
    {
        var enter = new PhysxEventRecord
        {
            Id0 = 300,
            Id1 = 20,
            Detail0 = 301,
            Detail1 = 21,
            Type = (uint)PhysxEventType.TriggerEnter,
            Flags = (uint)PhysxEventFlags.DetailIsShape
        };
        var leave = enter;
        leave.Type = (uint)PhysxEventType.TriggerLeave;

        UsdPhysicsEvent detachedEnter = PhysxEventAdapter.Detach(enter, 0);
        UsdPhysicsEvent detachedLeave = PhysxEventAdapter.Detach(leave, 0);

        await Assert.That(detachedEnter.Kind).IsEqualTo(UsdPhysicsEventKind.TriggerEnter);
        await Assert.That(detachedLeave.Kind).IsEqualTo(UsdPhysicsEventKind.TriggerExit);
        await Assert.That(detachedEnter.Primary.Value).IsEqualTo(300UL);
        await Assert.That(detachedLeave.Primary.Value).IsEqualTo(300UL);
        await Assert.That(detachedEnter.PrimaryElement!.Value.Value).IsEqualTo(301UL);
    }

    [Test]
    public async Task ControllerHitsNameTheControllerAndTheHitCollider()
    {
        var record = new PhysxEventRecord
        {
            Id0 = 400,
            Id1 = 40,
            Detail0 = 41,
            Type = (uint)PhysxEventType.ControllerHit,
            Flags = (uint)PhysxEventFlags.DetailIsShape
        };

        UsdPhysicsEvent detached = PhysxEventAdapter.Detach(record, 0);

        await Assert.That(detached.Kind).IsEqualTo(UsdPhysicsEventKind.ControllerHit);
        await Assert.That(detached.Primary.Kind).IsEqualTo(UsdPhysicsObjectKind.Controller);
        await Assert.That(detached.Secondary!.Value.Value).IsEqualTo(40UL);
        await Assert.That(detached.PrimaryElement!.Value.Value).IsEqualTo(41UL);
        await Assert.That(detached.SecondaryElement).IsNull();
    }

    [Test]
    public async Task IdentitiesAreStableAcrossRepeatedDetaching()
    {
        PhysxEventRecord[] records = PhysxEventOrderTests.SampleEvents();

        UsdPhysicsEventBatch first = PhysxEventAdapter.Detach(records, 3.5, 0);
        UsdPhysicsEventBatch second = PhysxEventAdapter.Detach(records, 3.5, 0);

        await Assert.That(first).IsEqualTo(second);
        for (int index = 0; index < records.Length; index++)
        {
            await Assert.That(first.Entries[index].Primary.Value).IsEqualTo(records[index].Id0);
            await Assert.That(first.Entries[index].StepIndex).IsEqualTo(records[index].StepIndex);
        }
    }

    [Test]
    public async Task OverflowRetainsTheDeterministicPrefixAndCountsTheRemainder()
    {
        PhysxEventRecord[] all = PhysxEventOrderTests.SampleEvents();
        PhysxEventOrder.Sort(all);
        const int capacity = 4;
        uint dropped = (uint)(all.Length - capacity);

        UsdPhysicsEventBatch batch = PhysxEventAdapter.Detach(all.AsSpan(0, capacity), 0, dropped);

        await Assert.That(batch.Entries.Count).IsEqualTo(capacity);
        await Assert.That(batch.DroppedCount).IsEqualTo((int)dropped);
        await Assert.That(batch.IsOverflowed).IsTrue();
        for (int index = 0; index < capacity; index++)
        {
            await Assert.That(batch.Entries[index].StepIndex).IsEqualTo(all[index].StepIndex);
            await Assert.That(batch.Entries[index].Primary.Value).IsEqualTo(all[index].Id0);
        }
    }

    [Test]
    public async Task AnEmptyNonOverflowedPrefixDetachesToTheSharedEmptyBatch()
    {
        await Assert.That(PhysxEventAdapter.Detach([], 0, 0)).IsSameReferenceAs(UsdPhysicsEventBatch.Empty);
        await Assert.That(PhysxEventAdapter.Detach([], 0, 3).DroppedCount).IsEqualTo(3);
    }

    [Test]
    public async Task AnUnknownEventTypeIsRejectedRatherThanGuessed()
    {
        var record = new PhysxEventRecord { Type = (uint)PhysxEventType.Count };

        await Assert.That(() => PhysxEventAdapter.Detach(record, 0)).Throws<ArgumentOutOfRangeException>();
    }

    [Test]
    public async Task DetachingIsSafeFromConcurrentWorldsSharingNoState()
    {
        PhysxEventRecord[] worldA = PhysxEventOrderTests.SampleEvents();
        PhysxEventRecord[] worldB = PhysxEventOrderTests.SampleEvents();
        for (int index = 0; index < worldB.Length; index++)
        {
            worldB[index].Id0 += 1_000_000;
            worldB[index].Id1 += worldB[index].Id1 == 0 ? 0UL : 1_000_000UL;
        }

        UsdPhysicsEventBatch expectedA = PhysxEventAdapter.Detach(worldA, 1.0, 0);
        UsdPhysicsEventBatch expectedB = PhysxEventAdapter.Detach(worldB, 2.0, 0);

        var results = new UsdPhysicsEventBatch[64];
        await Task.WhenAll(Enumerable.Range(0, results.Length).Select(index => Task.Run(() =>
        {
            results[index] = (index % 2) == 0
                ? PhysxEventAdapter.Detach(worldA, 1.0, 0)
                : PhysxEventAdapter.Detach(worldB, 2.0, 0);
        })));

        for (int index = 0; index < results.Length; index++)
        {
            await Assert.That(results[index]).IsEqualTo((index % 2) == 0 ? expectedA : expectedB);
        }
    }
}
