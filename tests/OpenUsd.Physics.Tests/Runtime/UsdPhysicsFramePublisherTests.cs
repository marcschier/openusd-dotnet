// Copyright (c) marcschier. Licensed under the MIT License.

namespace OpenUsd.Physics.Tests;

public sealed class UsdPhysicsFramePublisherTests
{
    [Test]
    public async Task NothingIsAcquirableBeforeTheFirstPublication()
    {
        var publisher = new UsdPhysicsFramePublisher(2);

        await Assert.That(publisher.TryAcquire(out UsdPhysicsFrameLease lease)).IsFalse();
        await Assert.That(lease.IsValid).IsFalse();
        await Assert.That(publisher.Revision).IsEqualTo(0ul);
    }

    [Test]
    public async Task LatestCompleteFrameWins()
    {
        var publisher = new UsdPhysicsFramePublisher(2);

        for (int step = 1; step <= 5; step++)
        {
            UsdPhysicsFrame? frame = publisher.TryClaimWriteBuffer();
            await Assert.That(frame).IsNotNull();
            frame!.StepIndex = (ulong)step;
            publisher.Publish(frame);
        }

        await Assert.That(publisher.TryAcquire(out UsdPhysicsFrameLease lease)).IsTrue();
        using (lease)
        {
            await Assert.That(lease.Frame.StepIndex).IsEqualTo(5ul);
            await Assert.That(lease.Frame.Revision).IsEqualTo(5ul);
        }

        await Assert.That(publisher.DroppedPublications).IsEqualTo(0L);
    }

    [Test]
    public async Task CopiedLeaseReleasesFrameOnlyOnce()
    {
        var publisher = new UsdPhysicsFramePublisher(1);
        UsdPhysicsFrame published = publisher.TryClaimWriteBuffer()!;
        publisher.Publish(published);

        await Assert.That(publisher.TryAcquire(out UsdPhysicsFrameLease lease)).IsTrue();
        UsdPhysicsFrameLease copy = lease;

        copy.Dispose();
        lease.Dispose();

        UsdPhysicsFrame first = publisher.TryClaimWriteBuffer()!;
        UsdPhysicsFrame second = publisher.TryClaimWriteBuffer()!;
        await Assert.That(publisher.TryClaimWriteBuffer()).IsNull();
        UsdPhysicsFramePublisher.Abandon(first);
        UsdPhysicsFramePublisher.Abandon(second);
    }

    [Test]
    public async Task CopiesOfOneLeaseAreEqualAndDistinctAcquisitionsAreNot()
    {
        var publisher = new UsdPhysicsFramePublisher(1);
        publisher.Publish(publisher.TryClaimWriteBuffer()!);

        await Assert.That(publisher.TryAcquire(out UsdPhysicsFrameLease first)).IsTrue();
        UsdPhysicsFrameLease copy = first;
        await Assert.That(publisher.TryAcquire(out UsdPhysicsFrameLease second)).IsTrue();

        await Assert.That(copy == first).IsTrue();
        await Assert.That(copy.GetHashCode()).IsEqualTo(first.GetHashCode());
        await Assert.That(second != first).IsTrue();
        await Assert.That(ReferenceEquals(second.Frame, first.Frame)).IsTrue();

        first.Dispose();
        second.Dispose();
        await Assert.That(publisher.LiveLeaseCount).IsEqualTo(0);
    }

    [Test]
    public async Task TheDefaultLeasePinsNothing()
    {
        UsdPhysicsFrameLease lease = default;

        await Assert.That(lease.IsValid).IsFalse();
        await Assert.That(lease == default).IsTrue();
        await Assert.That(lease.GetHashCode()).IsEqualTo(0);
        lease.Dispose();
        lease.Dispose();
        await Assert.That(() => lease.Frame).Throws<ObjectDisposedException>();
    }

    [Test]
    public async Task AcquisitionIsRefusedWhenEveryLeaseSlotIsRented()
    {
        var publisher = new UsdPhysicsFramePublisher(1, leaseCapacity: 2);
        UsdPhysicsFrame published = publisher.TryClaimWriteBuffer()!;
        publisher.Publish(published);

        await Assert.That(publisher.LeaseCapacity).IsEqualTo(2);
        await Assert.That(publisher.TryAcquire(out UsdPhysicsFrameLease first)).IsTrue();
        await Assert.That(publisher.TryAcquire(out UsdPhysicsFrameLease second)).IsTrue();

        await Assert.That(publisher.TryAcquire(out UsdPhysicsFrameLease refused)).IsFalse();
        await Assert.That(refused.IsValid).IsFalse();
        await Assert.That(publisher.ExhaustedLeaseAcquisitions).IsEqualTo(1L);
        await Assert.That(publisher.LiveLeaseCount).IsEqualTo(2);

        // The refused acquisition must not have leaked the reference it took on the frame.
        await Assert.That(published.References).IsEqualTo(3);

        first.Dispose();
        second.Dispose();
        await Assert.That(publisher.LiveLeaseCount).IsEqualTo(0);
        await Assert.That(published.References).IsEqualTo(1);
    }

    [Test]
    public async Task DisposedLeaseSlotsAreReusedWithoutAllocating()
    {
        var publisher = new UsdPhysicsFramePublisher(1, leaseCapacity: 1);
        publisher.Publish(publisher.TryClaimWriteBuffer()!);

        for (int iteration = 0; iteration < 16; iteration++)
        {
            await Assert.That(publisher.TryAcquire(out UsdPhysicsFrameLease lease)).IsTrue();
            await Assert.That(publisher.LiveLeaseCount).IsEqualTo(1);
            await Assert.That(publisher.TryAcquire(out _)).IsFalse();
            lease.Dispose();
            await Assert.That(publisher.LiveLeaseCount).IsEqualTo(0);
        }

        await Assert.That(publisher.ExhaustedLeaseAcquisitions).IsEqualTo(16L);
    }

    [Test]
    public async Task AStaleCopyNeverReleasesALaterLeaseOfTheSameSlot()
    {
        var publisher = new UsdPhysicsFramePublisher(1, leaseCapacity: 1);
        UsdPhysicsFrame published = publisher.TryClaimWriteBuffer()!;
        publisher.Publish(published);

        await Assert.That(publisher.TryAcquire(out UsdPhysicsFrameLease original)).IsTrue();
        UsdPhysicsFrameLease stale = original;
        original.Dispose();

        // The only slot is free again, so the next acquisition necessarily recycles it.
        await Assert.That(publisher.TryAcquire(out UsdPhysicsFrameLease renewed)).IsTrue();
        await Assert.That(stale.IsValid).IsFalse();
        await Assert.That(stale != renewed).IsTrue();

        int referencesBefore = published.References;
        stale.Dispose();
        stale.Dispose();

        await Assert.That(published.References).IsEqualTo(referencesBefore);
        await Assert.That(renewed.IsValid).IsTrue();
        await Assert.That(ReferenceEquals(renewed.Frame, published)).IsTrue();
        await Assert.That(publisher.LiveLeaseCount).IsEqualTo(1);

        renewed.Dispose();
        await Assert.That(published.References).IsEqualTo(1);
    }

    [Test]
    public async Task AcquiringAndReleasingLeasesDoesNotAllocate()
    {
        var publisher = new UsdPhysicsFramePublisher(4, leaseCapacity: 4);
        publisher.Publish(publisher.TryClaimWriteBuffer()!);

        for (int warmup = 0; warmup < 256; warmup++)
        {
            publisher.TryAcquire(out UsdPhysicsFrameLease warm);
            _ = warm.IsValid;
            warm.Dispose();
        }

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int iteration = 0; iteration < 512; iteration++)
        {
            publisher.TryAcquire(out UsdPhysicsFrameLease lease);
            UsdPhysicsFrameLease copy = lease;
            _ = copy.IsValid;
            copy.Dispose();
            lease.Dispose();
        }

        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        await Assert.That(allocated).IsEqualTo(0L);
    }

    [Test]
    public async Task ClaimedBuffersAreNeverThePublishedBuffer()
    {
        var publisher = new UsdPhysicsFramePublisher(2);
        UsdPhysicsFrame first = publisher.TryClaimWriteBuffer()!;
        publisher.Publish(first);

        UsdPhysicsFrame second = publisher.TryClaimWriteBuffer()!;
        await Assert.That(ReferenceEquals(first, second)).IsFalse();

        publisher.TryAcquire(out UsdPhysicsFrameLease lease);
        using (lease)
        {
            await Assert.That(ReferenceEquals(lease.Frame, first)).IsTrue();
        }

        publisher.Publish(second);
    }

    [Test]
    public async Task ExhaustedRingReportsDroppedPublicationsInsteadOfBlocking()
    {
        var publisher = new UsdPhysicsFramePublisher(1);
        UsdPhysicsFrame published = publisher.TryClaimWriteBuffer()!;
        publisher.Publish(published);

        UsdPhysicsFrame held1 = publisher.TryClaimWriteBuffer()!;
        UsdPhysicsFrame held2 = publisher.TryClaimWriteBuffer()!;

        await Assert.That(publisher.TryClaimWriteBuffer()).IsNull();
        await Assert.That(publisher.DroppedPublications).IsEqualTo(1L);

        UsdPhysicsFramePublisher.Abandon(held1);
        await Assert.That(publisher.TryClaimWriteBuffer()).IsNotNull();
        UsdPhysicsFramePublisher.Abandon(held2);
    }

    [Test]
    public async Task LeasesKeepAFrameFromBeingReclaimed()
    {
        var publisher = new UsdPhysicsFramePublisher(1);
        UsdPhysicsFrame first = publisher.TryClaimWriteBuffer()!;
        first.StepIndex = 1;
        publisher.Publish(first);

        publisher.TryAcquire(out UsdPhysicsFrameLease lease);

        UsdPhysicsFrame second = publisher.TryClaimWriteBuffer()!;
        second.StepIndex = 2;
        publisher.Publish(second);

        UsdPhysicsFrame third = publisher.TryClaimWriteBuffer()!;
        await Assert.That(ReferenceEquals(third, first)).IsFalse();
        await Assert.That(lease.Frame.StepIndex).IsEqualTo(1ul);

        lease.Dispose();
        UsdPhysicsFramePublisher.Abandon(third);

        await Assert.That(publisher.TryAcquire(out UsdPhysicsFrameLease latest)).IsTrue();
        using (latest)
        {
            await Assert.That(latest.Frame.StepIndex).IsEqualTo(2ul);
        }
    }

    [Test]
    public async Task InvalidateUnpublishesEveryBuffer()
    {
        var publisher = new UsdPhysicsFramePublisher(1);
        publisher.Publish(publisher.TryClaimWriteBuffer()!);

        publisher.Invalidate();

        await Assert.That(publisher.TryAcquire(out _)).IsFalse();
    }

    [Test]
    public async Task DisposingALeaseTwiceIsSafe()
    {
        var publisher = new UsdPhysicsFramePublisher(1);
        publisher.Publish(publisher.TryClaimWriteBuffer()!);
        publisher.TryAcquire(out UsdPhysicsFrameLease lease);

        lease.Dispose();
        lease.Dispose();

        await Assert.That(lease.IsValid).IsFalse();
        await Assert.That(() => lease.Frame).Throws<ObjectDisposedException>();
    }

    [Test]
    public async Task ConcurrentConsumersNeverObserveAPartialFrame()
    {
        var publisher = new UsdPhysicsFramePublisher(8);
        using var stop = new CancellationTokenSource();
        int torn = 0;

        Task[] readers = Enumerable.Range(0, 4).Select(_ => Task.Run(() =>
        {
            while (!stop.IsCancellationRequested)
            {
                if (!publisher.TryAcquire(out UsdPhysicsFrameLease lease))
                {
                    continue;
                }

                using (lease)
                {
                    ReadOnlySpan<UsdPhysicsBodyPose> bodies = lease.Frame.Bodies;
                    ulong expected = lease.Frame.StepIndex;
                    for (int index = 0; index < bodies.Length; index++)
                    {
                        if ((ulong)bodies[index].Position.Y != expected)
                        {
                            Interlocked.Increment(ref torn);
                        }
                    }
                }
            }
        })).ToArray();

        for (ulong step = 1; step <= 20000; step++)
        {
            UsdPhysicsFrame? frame = publisher.TryClaimWriteBuffer();
            if (frame is null)
            {
                continue;
            }

            Span<UsdPhysicsBodyPose> bodies = frame.BodyBuffer;
            for (int index = 0; index < bodies.Length; index++)
            {
                bodies[index] = new UsdPhysicsBodyPose(
                    new UsdPhysicsObjectId((ulong)index + 1, UsdPhysicsObjectKind.RigidBody),
                    new UsdVec3d(index, step, 0),
                    UsdPhysicsOrientation.Identity,
                    default,
                    default,
                    false,
                    false);
            }

            frame.SetBodyCount(bodies.Length);
            frame.StepIndex = step;
            publisher.Publish(frame);
        }

        await stop.CancelAsync();
        await Task.WhenAll(readers);

        await Assert.That(torn).IsEqualTo(0);
    }
}
