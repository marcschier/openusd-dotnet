// Copyright (c) marcschier. Licensed under the MIT License.

namespace OpenUsd.Rendering;

/// <summary>
/// Hands complete physics snapshots to the renderer through a bounded, latest-wins buffer ring.
/// </summary>
/// <remarks>
/// <para>
/// Exactly <see cref="BufferCount"/> snapshots are allocated when the channel is created and are
/// then reused forever. A producer claims a free buffer, fills it, and publishes it with a single
/// atomic exchange; the renderer copies whatever is published at the moment it asks. The latest
/// complete snapshot always wins: the renderer may skip intermediate snapshots, but it never
/// observes a snapshot that is still being written and never blocks the simulation.
/// </para>
/// <para>
/// The producer never blocks either. A buffer is free only when nothing references it, so a
/// producer that outruns the renderer reports a dropped publication instead of waiting or growing
/// the ring, because dropping an intermediate snapshot only loses a redundant frame while blocking
/// the producer would lose simulated time.
/// </para>
/// </remarks>
public sealed class PhysicsRenderChannel
{
    /// <summary>Gets the smallest number of buffers a channel can own.</summary>
    public const int MinimumBufferCount = 3;

    private readonly PhysicsRenderSnapshot[] _buffers;
    private readonly int[] _references;
    private PhysicsRenderSnapshot? _published;
    private long _revision;
    private long _droppedPublications;
    private long _refusedWrites;
    private long _truncatedReads;

    /// <summary>Allocates every publication buffer up front.</summary>
    /// <param name="capacities">The bounded storage every buffer preallocates.</param>
    /// <param name="bufferCount">The number of publication buffers.</param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="bufferCount"/> is smaller than <see cref="MinimumBufferCount"/>.
    /// </exception>
    public PhysicsRenderChannel(
        PhysicsRenderCapacities capacities,
        int bufferCount = MinimumBufferCount)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(bufferCount, MinimumBufferCount);
        Capacities = capacities;
        _buffers = new PhysicsRenderSnapshot[bufferCount];
        _references = new int[bufferCount];
        for (int index = 0; index < bufferCount; index++)
        {
            _buffers[index] = new PhysicsRenderSnapshot(capacities);
        }
    }

    /// <summary>Gets the bounded storage every publication buffer preallocated.</summary>
    public PhysicsRenderCapacities Capacities { get; }

    /// <summary>Gets the number of publication buffers this channel owns.</summary>
    public int BufferCount => _buffers.Length;

    /// <summary>Gets the revision of the most recently published snapshot.</summary>
    public ulong Revision => (ulong)Interlocked.Read(ref _revision);

    /// <summary>Gets the number of snapshots that were produced but could not be published.</summary>
    public long DroppedPublications => Interlocked.Read(ref _droppedPublications);

    /// <summary>Gets the number of write claims refused because every buffer was in use.</summary>
    public long RefusedWrites => Interlocked.Read(ref _refusedWrites);

    /// <summary>Gets the number of reads whose destination could not hold every entry.</summary>
    public long TruncatedReads => Interlocked.Read(ref _truncatedReads);

    /// <summary>Gets a value indicating whether a complete snapshot has been published.</summary>
    public bool HasSnapshot => Volatile.Read(ref _published) is not null;

    /// <summary>Claims a buffer the producer may fill, without blocking the renderer.</summary>
    /// <returns>
    /// The claimed buffer, or <see langword="null"/> when every buffer is published or being read.
    /// </returns>
    public PhysicsRenderSnapshot? TryBeginWrite()
    {
        PhysicsRenderSnapshot? published = Volatile.Read(ref _published);
        for (int index = 0; index < _buffers.Length; index++)
        {
            PhysicsRenderSnapshot candidate = _buffers[index];
            if (ReferenceEquals(candidate, published))
            {
                continue;
            }
            if (Interlocked.CompareExchange(ref _references[index], 1, 0) == 0)
            {
                return candidate;
            }
        }

        Interlocked.Increment(ref _refusedWrites);
        Interlocked.Increment(ref _droppedPublications);
        return null;
    }

    /// <summary>Publishes a filled buffer and releases the buffer it replaces.</summary>
    /// <param name="snapshot">The buffer previously claimed from <see cref="TryBeginWrite"/>.</param>
    /// <returns>The monotonic revision the snapshot was published with.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="snapshot"/> is null.</exception>
    /// <exception cref="ArgumentException">
    /// The snapshot is not one of this channel's buffers, or it was never completed.
    /// </exception>
    public ulong Publish(PhysicsRenderSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        _ = IndexOf(snapshot);
        if (!snapshot.IsComplete)
        {
            throw new ArgumentException(
                "Only a completed snapshot can be published.",
                nameof(snapshot));
        }

        ulong revision = (ulong)Interlocked.Increment(ref _revision);
        snapshot.Revision = revision;
        PhysicsRenderSnapshot? previous = Interlocked.Exchange(ref _published, snapshot);
        if (previous is not null)
        {
            Interlocked.Decrement(ref _references[IndexOf(previous)]);
        }

        return revision;
    }

    /// <summary>Returns a claimed buffer that will not be published.</summary>
    /// <param name="snapshot">The buffer previously claimed from <see cref="TryBeginWrite"/>.</param>
    /// <exception cref="ArgumentNullException"><paramref name="snapshot"/> is null.</exception>
    /// <exception cref="ArgumentException">The snapshot is not one of this channel's buffers.</exception>
    public void Abandon(PhysicsRenderSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        Interlocked.Decrement(ref _references[IndexOf(snapshot)]);
    }

    /// <summary>Copies the latest published snapshot without blocking the producer.</summary>
    /// <remarks>
    /// The published buffer is pinned for the duration of the copy, so the producer can never
    /// overwrite it mid-copy and the renderer can never observe a torn snapshot. Copying rather
    /// than leasing keeps the ring free for the producer between render updates.
    /// </remarks>
    /// <param name="destination">The renderer-owned buffer the snapshot is copied into.</param>
    /// <returns>
    /// <see langword="true"/> when a complete snapshot was copied; <see langword="false"/> when
    /// nothing has been published yet.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="destination"/> is null.</exception>
    public bool TryCopyLatest(PhysicsRenderSnapshot destination)
    {
        ArgumentNullException.ThrowIfNull(destination);
        while (true)
        {
            PhysicsRenderSnapshot? snapshot = Volatile.Read(ref _published);
            if (snapshot is null)
            {
                return false;
            }

            int index = IndexOf(snapshot);
            Interlocked.Increment(ref _references[index]);
            if (!ReferenceEquals(Volatile.Read(ref _published), snapshot))
            {
                Interlocked.Decrement(ref _references[index]);
                continue;
            }

            int sourceBodies;
            int sourceRegions;
            try
            {
                sourceBodies = snapshot.BodyCount;
                sourceRegions = snapshot.DeformableCount;
                snapshot.CopyTo(destination);
            }
            finally
            {
                Interlocked.Decrement(ref _references[index]);
            }

            if (destination.BodyCount < sourceBodies ||
                destination.DeformableCount < sourceRegions)
            {
                Interlocked.Increment(ref _truncatedReads);
            }
            return true;
        }
    }

    /// <summary>Unpublishes the current snapshot so every buffer becomes reclaimable.</summary>
    /// <remarks>
    /// Used on reset, stop, and invalidation: after this call the renderer observes no physics
    /// state at all, so it restores authored render state rather than holding a stale pose.
    /// </remarks>
    public void Invalidate()
    {
        PhysicsRenderSnapshot? previous = Interlocked.Exchange(ref _published, null);
        if (previous is not null)
        {
            Interlocked.Decrement(ref _references[IndexOf(previous)]);
        }
    }

    private int IndexOf(PhysicsRenderSnapshot snapshot)
    {
        for (int index = 0; index < _buffers.Length; index++)
        {
            if (ReferenceEquals(_buffers[index], snapshot))
            {
                return index;
            }
        }

        throw new ArgumentException(
            "The snapshot does not belong to this channel.",
            nameof(snapshot));
    }
}
