// Copyright (c) marcschier. Licensed under the MIT License.

namespace OpenUsd.Physics;

/// <summary>
/// One preallocated slot backing a live <see cref="UsdPhysicsFrameLease"/>.
/// </summary>
/// <remarks>
/// <para>
/// A slot is never freed and never reallocated: it is rented while a lease is live and returned when
/// that lease is disposed. <see cref="Token"/> is a monotonically increasing generation counter that
/// makes every rental of the same slot distinguishable. An even token means the slot is free; an odd
/// token means exactly one live lease owns it, and that lease carries the odd value.
/// </para>
/// <para>
/// The generation is what makes copies of a lease safe. Every copy carries the same odd token, so the
/// single compare-and-swap that turns the token even can only ever succeed once, and a copy that was
/// kept past the disposal of its lease can never match a later rental of the same slot.
/// </para>
/// </remarks>
internal sealed class UsdPhysicsLeaseSlot
{
    /// <summary>The frame the current rental pins, valid only while <see cref="Token"/> is unchanged.</summary>
    /// <remarks>
    /// The field is deliberately not cleared on release. Clearing it would race a rental that started
    /// immediately after the release, and the token already invalidates every stale reader.
    /// </remarks>
    internal UsdPhysicsFrame? Frame;

    /// <summary>The rental generation; even when free, odd while a lease owns this slot.</summary>
    internal long Token;
}

/// <summary>
/// A bounded, preallocated pool of lease slots shared by every consumer of one publisher.
/// </summary>
/// <remarks>
/// <para>
/// Leases must stay value types with copy semantics, and a copied lease must release its frame exactly
/// once. Allocating per acquisition would satisfy that but would allocate on the warm consumer path,
/// so the state every copy shares is rented from this fixed pool instead. Renting and returning are
/// single compare-and-swap operations over a bounded scan, so acquisition never blocks, never waits on
/// the physics worker, and never allocates.
/// </para>
/// <para>
/// The pool is bounded on purpose. An unbounded pool would let a consumer that leaks leases grow the
/// process without limit; a bounded pool makes that failure visible immediately as a refused
/// acquisition, which the consumer sees as "no frame available right now" rather than as a leak.
/// </para>
/// </remarks>
internal sealed class UsdPhysicsLeasePool
{
    private readonly UsdPhysicsLeaseSlot[] _slots;
    private int _hint;
    private long _exhaustedRentals;

    /// <summary>Preallocates every lease slot.</summary>
    /// <param name="capacity">The maximum number of leases that may be live at the same time.</param>
    internal UsdPhysicsLeasePool(int capacity)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capacity);
        _slots = new UsdPhysicsLeaseSlot[capacity];
        for (int index = 0; index < capacity; index++)
        {
            _slots[index] = new UsdPhysicsLeaseSlot();
        }
    }

    /// <summary>Gets the maximum number of simultaneously live leases.</summary>
    internal int Capacity => _slots.Length;

    /// <summary>Gets the number of acquisitions refused because every slot was already rented.</summary>
    internal long ExhaustedRentals => Interlocked.Read(ref _exhaustedRentals);

    /// <summary>Gets the number of slots currently rented.</summary>
    internal int RentedCount
    {
        get
        {
            int rented = 0;
            for (int index = 0; index < _slots.Length; index++)
            {
                if ((Volatile.Read(ref _slots[index].Token) & 1) != 0)
                {
                    rented++;
                }
            }
            return rented;
        }
    }

    /// <summary>Rents a slot for one lease over <paramref name="frame"/>.</summary>
    /// <param name="frame">The frame the lease pins.</param>
    /// <param name="lease">The rented lease, or the default lease when the pool is exhausted.</param>
    /// <returns><see langword="true"/> when a slot was rented.</returns>
    internal bool TryRent(UsdPhysicsFrame frame, out UsdPhysicsFrameLease lease)
    {
        int start = Interlocked.Increment(ref _hint);
        for (int offset = 0; offset < _slots.Length; offset++)
        {
            int index = (int)((uint)(start + offset) % (uint)_slots.Length);
            UsdPhysicsLeaseSlot slot = _slots[index];
            long token = Volatile.Read(ref slot.Token);
            if ((token & 1) != 0)
            {
                continue;
            }

            if (Interlocked.CompareExchange(ref slot.Token, token + 1, token) != token)
            {
                continue;
            }

            // The slot is exclusively owned by this rental now, so the frame can be published to it
            // before the lease that reads it is handed back to the caller.
            Volatile.Write(ref slot.Frame, frame);
            lease = new UsdPhysicsFrameLease(slot, token + 1);
            return true;
        }

        Interlocked.Increment(ref _exhaustedRentals);
        lease = default;
        return false;
    }
}
