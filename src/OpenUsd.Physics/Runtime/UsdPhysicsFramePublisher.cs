// Copyright (c) marcschier. Licensed under the MIT License.

namespace OpenUsd.Physics;

/// <summary>
/// Publishes complete simulation frames through a bounded, allocation-free triple buffer.
/// </summary>
/// <remarks>
/// <para>
/// Exactly <see cref="UsdPhysicsTransportOptions.PublicationBufferCount"/> frames are allocated once
/// and then reused forever. The physics worker claims a free buffer, fills it, and publishes it with
/// a single atomic exchange; consumers atomically pin whatever is published at the moment they ask.
/// Latest complete frame always wins: a consumer that asks twice may skip intermediate frames, but it
/// never observes a frame that is still being written and never waits for the worker.
/// </para>
/// <para>
/// The worker never waits either. A buffer is free only when nothing references it, so a consumer
/// that holds leases longer than the worker's tick interval can exhaust the ring. That case is
/// reported as a dropped publication rather than by blocking the fixed simulation step or by growing
/// the ring, because dropping a publication loses only a redundant intermediate frame while blocking
/// the worker would lose simulated time.
/// </para>
/// <para>
/// Leases are value types whose shared state is rented from a bounded preallocated pool, so acquiring
/// and releasing a lease allocates nothing. A consumer that leaks leases exhausts that pool, and
/// acquisition then simply refuses rather than allocating without limit.
/// </para>
/// </remarks>
internal sealed class UsdPhysicsFramePublisher
{
    private readonly UsdPhysicsFrame[] _frames;
    private readonly UsdPhysicsLeasePool _leases;
    private UsdPhysicsFrame? _published;
    private long _revision;
    private long _droppedPublications;

    /// <summary>Allocates every publication buffer and lease slot up front.</summary>
    /// <param name="bodyCapacity">The number of body poses each buffer can carry.</param>
    /// <param name="bufferCount">The number of publication buffers; at least three.</param>
    /// <param name="leaseCapacity">The number of leases that may be live at the same time.</param>
    internal UsdPhysicsFramePublisher(
        int bodyCapacity,
        int bufferCount = UsdPhysicsTransportOptions.PublicationBufferCount,
        int leaseCapacity = UsdPhysicsTransportOptions.DefaultMaxConcurrentFrameLeases)
        : this(bodyCapacity, 0, 0, bufferCount, leaseCapacity)
    {
    }

    /// <summary>Allocates every publication buffer and lease slot up front.</summary>
    /// <param name="bodyCapacity">The number of body poses each buffer can carry.</param>
    /// <param name="deformationCapacity">The number of deformable bodies each buffer can carry.</param>
    /// <param name="deformationVertexCapacity">The number of simulated vertices each buffer can carry.</param>
    /// <param name="bufferCount">The number of publication buffers; at least three.</param>
    /// <param name="leaseCapacity">The number of leases that may be live at the same time.</param>
    /// <param name="revisionSeed">
    /// The revision the first published frame follows, so a rebuilt world continues the monotonic
    /// sequence its predecessor published instead of restarting it.
    /// </param>
    internal UsdPhysicsFramePublisher(
        int bodyCapacity,
        int deformationCapacity,
        int deformationVertexCapacity,
        int bufferCount,
        int leaseCapacity,
        ulong revisionSeed = 0)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(bufferCount, 3);
        _revision = (long)revisionSeed;
        _frames = new UsdPhysicsFrame[bufferCount];
        for (int index = 0; index < bufferCount; index++)
        {
            _frames[index] = new UsdPhysicsFrame(bodyCapacity, deformationCapacity, deformationVertexCapacity);
        }

        _leases = new UsdPhysicsLeasePool(leaseCapacity);
    }

    /// <summary>Gets the number of frames that were simulated but could not be published.</summary>
    internal long DroppedPublications => Interlocked.Read(ref _droppedPublications);

    /// <summary>Gets the number of leases that may be live at the same time.</summary>
    internal int LeaseCapacity => _leases.Capacity;

    /// <summary>Gets the number of acquisitions refused because every lease slot was rented.</summary>
    internal long ExhaustedLeaseAcquisitions => _leases.ExhaustedRentals;

    /// <summary>Gets the number of lease slots currently rented.</summary>
    internal int LiveLeaseCount => _leases.RentedCount;

    /// <summary>Gets the revision of the most recently published frame.</summary>
    internal ulong Revision => (ulong)Interlocked.Read(ref _revision);

    /// <summary>
    /// Claims a buffer the worker may write into, or <see langword="null"/> when every buffer is
    /// published or leased.
    /// </summary>
    /// <remarks>
    /// The claim is an atomic zero-to-one transition on the buffer's reference count, so a consumer
    /// that is mid-way through pinning a buffer conservatively blocks the claim instead of racing
    /// the worker into a half-written frame.
    /// </remarks>
    internal UsdPhysicsFrame? TryClaimWriteBuffer()
    {
        UsdPhysicsFrame? published = Volatile.Read(ref _published);
        for (int index = 0; index < _frames.Length; index++)
        {
            UsdPhysicsFrame candidate = _frames[index];
            if (ReferenceEquals(candidate, published))
            {
                continue;
            }
            if (Interlocked.CompareExchange(ref candidate.References, 1, 0) == 0)
            {
                return candidate;
            }
        }

        Interlocked.Increment(ref _droppedPublications);
        return null;
    }

    /// <summary>Publishes a previously claimed buffer and releases the buffer it replaces.</summary>
    /// <remarks>
    /// The claimed buffer's single reference becomes the publication reference, so a published frame
    /// is never reclaimed for writing while it is still the latest frame.
    /// </remarks>
    internal ulong Publish(UsdPhysicsFrame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);

        ulong revision = (ulong)Interlocked.Increment(ref _revision);
        frame.Revision = revision;
        UsdPhysicsFrame? previous = Interlocked.Exchange(ref _published, frame);
        if (previous is not null)
        {
            Release(previous);
        }
        return revision;
    }

    /// <summary>Returns a claimed buffer that will not be published.</summary>
    internal static void Abandon(UsdPhysicsFrame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        Release(frame);
    }

    /// <summary>Pins the latest published frame without blocking the worker.</summary>
    /// <remarks>
    /// Acquisition fails, rather than waiting or allocating, when nothing has been published yet or
    /// when every lease slot is already rented by an undisposed lease.
    /// </remarks>
    internal bool TryAcquire(out UsdPhysicsFrameLease lease)
    {
        while (true)
        {
            UsdPhysicsFrame? frame = Volatile.Read(ref _published);
            if (frame is null)
            {
                lease = default;
                return false;
            }

            Interlocked.Increment(ref frame.References);
            if (!ReferenceEquals(Volatile.Read(ref _published), frame))
            {
                Release(frame);
                continue;
            }

            if (_leases.TryRent(frame, out lease))
            {
                return true;
            }

            Release(frame);
            lease = default;
            return false;
        }
    }

    /// <summary>Releases one reference on a buffer.</summary>
    internal static void Release(UsdPhysicsFrame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        Interlocked.Decrement(ref frame.References);
    }

    /// <summary>Unpublishes the current frame so every buffer becomes reclaimable after a reset.</summary>
    internal void Invalidate()
    {
        UsdPhysicsFrame? previous = Interlocked.Exchange(ref _published, null);
        if (previous is not null)
        {
            Release(previous);
        }
    }
}
