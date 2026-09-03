// Copyright (c) marcschier. Licensed under the MIT License.

namespace OpenUsd.LiveAuthoring;

/// <summary>Classifies one lookup against the bounded replay ledger.</summary>
internal enum LiveAuthoringReplayMatch
{
    /// <summary>The sequence is new: it is above every sequence the ledger has seen.</summary>
    Unseen,

    /// <summary>
    /// The sequence is retained and the fingerprint matches: the same message arrived twice and may be
    /// acknowledged idempotently.
    /// </summary>
    Identical,

    /// <summary>
    /// The sequence is retained but the fingerprint differs: two different messages claim the same
    /// place in the remote's ordered stream, so the agreement about that stream is broken.
    /// </summary>
    Conflict,

    /// <summary>
    /// The sequence is at or below the last accepted sequence but has fallen out of the retained
    /// window, so the ledger cannot prove whether it is the same message or a different one.
    /// </summary>
    Expired
}

/// <summary>
/// A bounded, per-epoch record of recently accepted remote sequences and their content fingerprints.
/// </summary>
/// <remarks>
/// <para>
/// A duplicate acknowledgement is a promise that nothing was lost, so it must be backed by evidence
/// that the replayed message really is the one already accepted. Comparing sequence numbers alone
/// cannot do that: a remote that reuses a sequence for different content would be silently accepted as
/// a harmless replay while the two sides diverge. The ledger keeps the evidence — a content-derived
/// fingerprint per retained sequence — so an identical replay is idempotent, a conflicting one forces a
/// resync, and an unprovable one is never claimed as a duplicate.
/// </para>
/// <para>
/// The window is bounded in both entries and bytes. Each entry holds one sequence and one fixed-size
/// digest, never the payload, so retention cost is independent of how large the deltas were. The window
/// is a ring: accepting a new sequence evicts the oldest once the configured length is reached.
/// </para>
/// <para>
/// The ledger is scoped to one epoch agreement. Connecting, reconnecting, disconnecting, and applying a
/// full snapshot all clear it, because sequences from a previous agreement can neither be duplicates
/// nor conflicts of the new one.
/// </para>
/// </remarks>
internal sealed class LiveAuthoringReplayLedger
{
    private readonly Dictionary<long, LiveAuthoringDeltaFingerprint> _entries;
    private readonly Queue<long> _order;

    internal LiveAuthoringReplayLedger(int windowLength)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(windowLength);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(
            windowLength,
            LiveAuthoringValidation.MaxReplayWindowLength);
        WindowLength = windowLength;
        _entries = new Dictionary<long, LiveAuthoringDeltaFingerprint>(windowLength);
        _order = new Queue<long>(windowLength);
    }

    /// <summary>Gets the maximum number of retained sequences.</summary>
    internal int WindowLength { get; }

    /// <summary>Gets the number of retained sequences.</summary>
    internal int Count => _entries.Count;

    /// <summary>Gets the retained byte size, counting one sequence and one digest per entry.</summary>
    internal long EstimatedBytes => (long)_entries.Count * LiveAuthoringValidation.ReplayLedgerEntryBytes;

    /// <summary>
    /// Gets the oldest retained sequence, or <c>0</c> when nothing is retained. A replay at or below the
    /// last accepted sequence but below this value is <see cref="LiveAuthoringReplayMatch.Expired"/>.
    /// </summary>
    internal long OldestRetainedSequence { get; private set; }

    /// <summary>Classifies a replayed sequence against the retained window.</summary>
    internal LiveAuthoringReplayMatch Classify(
        long sequence,
        long lastAcceptedSequence,
        LiveAuthoringDeltaFingerprint fingerprint)
    {
        if (sequence > lastAcceptedSequence)
        {
            return LiveAuthoringReplayMatch.Unseen;
        }
        if (!_entries.TryGetValue(sequence, out LiveAuthoringDeltaFingerprint retained))
        {
            return LiveAuthoringReplayMatch.Expired;
        }
        return retained.Equals(fingerprint)
            ? LiveAuthoringReplayMatch.Identical
            : LiveAuthoringReplayMatch.Conflict;
    }

    /// <summary>Records an accepted sequence, evicting the oldest entry once the window is full.</summary>
    internal void Record(long sequence, LiveAuthoringDeltaFingerprint fingerprint)
    {
        if (!_entries.ContainsKey(sequence))
        {
            _order.Enqueue(sequence);
        }
        _entries[sequence] = fingerprint;

        while (_order.Count > WindowLength ||
            (long)_entries.Count * LiveAuthoringValidation.ReplayLedgerEntryBytes >
                LiveAuthoringValidation.MaxReplayLedgerBytes)
        {
            long evicted = _order.Dequeue();
            _entries.Remove(evicted);
        }

        OldestRetainedSequence = _order.Count > 0 ? _order.Peek() : 0;
    }

    /// <summary>Drops every retained sequence, because the epoch agreement they belonged to is gone.</summary>
    internal void Clear()
    {
        _entries.Clear();
        _order.Clear();
        OldestRetainedSequence = 0;
    }
}
