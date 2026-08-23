// Copyright (c) marcschier. Licensed under the MIT License.

using OpenUsd.Physics.Interop;

namespace OpenUsd.Physics.Tests.Events;

public sealed class PhysxEventOrderTests
{
    [Test]
    public async Task TheEventOrderIsATotalOrder()
    {
        PhysxEventRecord[] records = SampleEvents();

        foreach (PhysxEventRecord left in records)
        {
            await Assert.That(PhysxEventOrder.Compare(left, left)).IsEqualTo(0);

            foreach (PhysxEventRecord right in records)
            {
                int forward = PhysxEventOrder.Compare(left, right);
                int backward = PhysxEventOrder.Compare(right, left);
                await Assert.That(Math.Sign(forward)).IsEqualTo(-Math.Sign(backward));

                foreach (PhysxEventRecord third in records)
                {
                    if (forward <= 0 && PhysxEventOrder.Compare(right, third) <= 0)
                    {
                        await Assert.That(PhysxEventOrder.Compare(left, third)).IsLessThanOrEqualTo(0);
                    }
                }
            }
        }
    }

    [Test]
    public async Task DistinctEventsNeverCompareEqual()
    {
        PhysxEventRecord[] records = SampleEvents();

        for (int left = 0; left < records.Length; left++)
        {
            for (int right = left + 1; right < records.Length; right++)
            {
                await Assert.That(PhysxEventOrder.Compare(records[left], records[right])).IsNotEqualTo(0);
            }
        }
    }

    [Test]
    public async Task SortingIsIndependentOfArrivalOrder()
    {
        PhysxEventRecord[] ordered = SampleEvents();
        PhysxEventOrder.Sort(ordered);

        var random = new Random(20260716);
        for (int attempt = 0; attempt < 32; attempt++)
        {
            PhysxEventRecord[] shuffled = SampleEvents();
            for (int index = shuffled.Length - 1; index > 0; index--)
            {
                int swap = random.Next(index + 1);
                (shuffled[index], shuffled[swap]) = (shuffled[swap], shuffled[index]);
            }

            PhysxEventOrder.Sort(shuffled);

            await Assert.That(PhysxEventOrder.IsOrdered(shuffled)).IsTrue();
            for (int index = 0; index < ordered.Length; index++)
            {
                await Assert.That(PhysxEventOrder.Compare(shuffled[index], ordered[index])).IsEqualTo(0);
            }
        }
    }

    [Test]
    public async Task TheOrderKeyPrefersStepIndexThenTypeThenIdentity()
    {
        var earlier = new PhysxEventRecord { StepIndex = 4, Type = 9, Id0 = 900 };
        var later = new PhysxEventRecord { StepIndex = 5, Type = 0, Id0 = 1 };
        await Assert.That(PhysxEventOrder.Compare(earlier, later)).IsLessThan(0);

        var sleep = new PhysxEventRecord { StepIndex = 5, Type = (uint)PhysxEventType.Sleep, Id0 = 900 };
        var contact = new PhysxEventRecord { StepIndex = 5, Type = (uint)PhysxEventType.ContactFound, Id0 = 1 };
        await Assert.That(PhysxEventOrder.Compare(sleep, contact)).IsLessThan(0);

        var lowDetail = new PhysxEventRecord { StepIndex = 5, Type = 3, Id0 = 1, Id1 = 2, Detail0 = 7 };
        var highDetail = new PhysxEventRecord { StepIndex = 5, Type = 3, Id0 = 1, Id1 = 2, Detail0 = 8 };
        await Assert.That(PhysxEventOrder.Compare(lowDetail, highDetail)).IsLessThan(0);

        var lowDetail1 = new PhysxEventRecord { StepIndex = 5, Type = 3, Id0 = 1, Id1 = 2, Detail0 = 8, Detail1 = 1 };
        var highDetail1 = lowDetail1;
        highDetail1.Detail1 = 2;
        await Assert.That(PhysxEventOrder.Compare(lowDetail1, highDetail1)).IsLessThan(0);
    }

    [Test]
    public async Task HitsAreOrderedNearestFirstWithDegenerateDistancesLast()
    {
        PhysxQueryHit[] hits =
        [
            new() { ActorId = 3, ShapeId = 30, Distance = float.NaN },
            new() { ActorId = 1, ShapeId = 10, Distance = 4.0F },
            new() { ActorId = 2, ShapeId = 20, Distance = 1.5F },
            new() { ActorId = 2, ShapeId = 21, Distance = 1.5F }
        ];

        Array.Sort(hits, static (left, right) => PhysxEventOrder.CompareHits(left, right));

        await Assert.That(hits[0].ShapeId).IsEqualTo(20UL);
        await Assert.That(hits[1].ShapeId).IsEqualTo(21UL);
        await Assert.That(hits[2].ShapeId).IsEqualTo(10UL);
        await Assert.That(float.IsNaN(hits[3].Distance)).IsTrue();
        await Assert.That(PhysxEventOrder.AreHitsOrdered(hits)).IsTrue();
    }

    [Test]
    public async Task EqualDistanceHitsFallBackToStableIdentityOrder()
    {
        var first = new PhysxQueryHit { ActorId = 5, ShapeId = 50, Distance = 2.0F, FaceIndex = 1 };
        var second = new PhysxQueryHit { ActorId = 5, ShapeId = 50, Distance = 2.0F, FaceIndex = 2 };

        await Assert.That(PhysxEventOrder.CompareHits(first, second)).IsLessThan(0);
        await Assert.That(PhysxEventOrder.CompareHits(second, first)).IsGreaterThan(0);
        await Assert.That(PhysxEventOrder.CompareHits(first, first)).IsEqualTo(0);
    }

    /// <summary>Builds a set of records that differ in every ordering component.</summary>
    internal static PhysxEventRecord[] SampleEvents() =>
    [
        new()
        {
            StepIndex = 6, Type = (uint)PhysxEventType.ContactFound, Id0 = 20, Id1 = 30, Detail0 = 21, Detail1 = 31
        },
        new()
        {
            StepIndex = 6, Type = (uint)PhysxEventType.ContactFound, Id0 = 20, Id1 = 30, Detail0 = 21, Detail1 = 32
        },
        new()
        {
            StepIndex = 6, Type = (uint)PhysxEventType.ContactFound, Id0 = 20, Id1 = 31, Detail0 = 21, Detail1 = 31
        },
        new() { StepIndex = 6, Type = (uint)PhysxEventType.ContactLost, Id0 = 10, Id1 = 11 },
        new() { StepIndex = 6, Type = (uint)PhysxEventType.Sleep, Id0 = 40 },
        new() { StepIndex = 6, Type = (uint)PhysxEventType.Wake, Id0 = 40 },
        new()
        {
            StepIndex = 6, Type = (uint)PhysxEventType.TriggerEnter, Id0 = 50, Id1 = 51, Detail0 = 52, Detail1 = 53
        },
        new() { StepIndex = 7, Type = (uint)PhysxEventType.Sleep, Id0 = 1 },
        new() { StepIndex = 7, Type = (uint)PhysxEventType.JointBreak, Id0 = 60, Id1 = 61, Detail0 = 62 },
        new() { StepIndex = 7, Type = (uint)PhysxEventType.ControllerHit, Id0 = 70, Id1 = 71, Detail0 = 72 }
    ];
}
