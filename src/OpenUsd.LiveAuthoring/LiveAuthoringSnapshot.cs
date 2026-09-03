// Copyright (c) marcschier. Licensed under the MIT License.

using System.Collections.ObjectModel;

namespace OpenUsd.LiveAuthoring;

/// <summary>
/// A bounded, transport-neutral full snapshot of the bridge-owned overlay at one remote sequence.
/// </summary>
/// <remarks>
/// <para>
/// A snapshot is complete and self-contained: applying it replaces the whole bridge-owned overlay
/// rather than merging into it. That is what makes recovery cheap to reason about — there is exactly
/// one baseline, and a session either has it or is in
/// <see cref="LiveAuthoringSessionState.ResyncRequired"/>.
/// </para>
/// <para>
/// A snapshot is bounded by <see cref="LiveAuthoringValidation.MaxBridgeOverlayUpdates"/> and
/// <see cref="LiveAuthoringValidation.MaxBridgeOverlayPayloadBytes"/>. It is an overlay handoff, not a
/// whole-scene dump; a scene larger than those bounds belongs in a referenced layer, not in a live
/// bridge message.
/// </para>
/// <para>
/// An empty update list is valid and means "the bridge overlay is empty", which is exactly what a
/// remote sends after it removes everything it owned.
/// </para>
/// </remarks>
public sealed class LiveAuthoringSnapshot
{
    private readonly ReadOnlyCollection<LiveStageUpdate> _updates;

    /// <summary>Initializes a bounded full snapshot.</summary>
    /// <param name="epoch">The authoritative remote identity and epoch this snapshot belongs to.</param>
    /// <param name="sequence">
    /// The last remote sequence included in this snapshot, or <c>0</c> when the remote has not yet
    /// produced a delta in this epoch. Deltas resume at <c>sequence + 1</c>.
    /// </param>
    /// <param name="bridgeRootPath">The absolute prim path that roots the bridge-owned overlay.</param>
    /// <param name="updates">The complete, ordered overlay content.</param>
    /// <param name="correlationId">An optional opaque identifier echoed on results and events.</param>
    public LiveAuthoringSnapshot(
        LiveAuthoringRemoteEpoch epoch,
        long sequence,
        string bridgeRootPath,
        IEnumerable<LiveStageUpdate> updates,
        string? correlationId = null)
    {
        ArgumentNullException.ThrowIfNull(epoch);
        ArgumentOutOfRangeException.ThrowIfNegative(sequence);
        ArgumentNullException.ThrowIfNull(updates);
        LiveAuthoringValidation.ValidateBridgeRootPath(bridgeRootPath, nameof(bridgeRootPath));
        LiveAuthoringValidation.ValidateOptionalCorrelationId(correlationId, nameof(correlationId));
        LiveStageUpdate[] materialized = updates
            .Select(static update => update is null
                ? throw new ArgumentException(
                    "A snapshot cannot contain null updates.",
                    nameof(updates))
                : LiveStageUpdateSnapshot.Snapshot(update))
            .ToArray();
        LiveAuthoringValidation.ValidateBridgeOverlayUpdates(
            bridgeRootPath,
            materialized,
            nameof(updates));

        Epoch = epoch;
        Sequence = sequence;
        BridgeRootPath = bridgeRootPath;
        CorrelationId = correlationId;
        _updates = Array.AsReadOnly(materialized);
    }

    /// <summary>Gets the authoritative remote identity and epoch this snapshot belongs to.</summary>
    public LiveAuthoringRemoteEpoch Epoch { get; }

    /// <summary>Gets the last remote sequence included in this snapshot.</summary>
    public long Sequence { get; }

    /// <summary>Gets the absolute prim path that roots the bridge-owned overlay.</summary>
    public string BridgeRootPath { get; }

    /// <summary>Gets the optional opaque correlation identifier.</summary>
    public string? CorrelationId { get; }

    /// <summary>Gets the complete, ordered overlay content.</summary>
    public IReadOnlyList<LiveStageUpdate> Updates => _updates;
}

/// <summary>
/// A bounded, transport-neutral incremental change from the authoritative remote origin, carrying the
/// epoch and remote sequence the duplicate/gap rules are evaluated against.
/// </summary>
public sealed class LiveAuthoringDelta
{
    private readonly ReadOnlyCollection<LiveStageUpdate> _updates;

    /// <summary>Initializes a bounded incremental change.</summary>
    /// <param name="epoch">The authoritative remote identity and epoch this delta belongs to.</param>
    /// <param name="sequence">The positive, per-epoch remote sequence.</param>
    /// <param name="updates">The ordered updates to apply.</param>
    /// <param name="coalescingKey">An optional snapshot key forwarded to the sink unchanged.</param>
    /// <param name="correlationId">An optional opaque identifier echoed on results and events.</param>
    /// <param name="originId">
    /// The opaque identifier of the system that authored this change. A delta whose origin equals the
    /// coordinator's local origin is suppressed as an echo instead of being reapplied.
    /// </param>
    public LiveAuthoringDelta(
        LiveAuthoringRemoteEpoch epoch,
        long sequence,
        IEnumerable<LiveStageUpdate> updates,
        string? coalescingKey = null,
        string? correlationId = null,
        string? originId = null)
    {
        ArgumentNullException.ThrowIfNull(epoch);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sequence);
        ArgumentNullException.ThrowIfNull(updates);
        LiveStageUpdate[] materialized = updates.ToArray();
        LiveAuthoringValidation.Validate(
            sequence,
            materialized,
            coalescingKey,
            correlationId,
            originId,
            nameof(updates));

        Epoch = epoch;
        Sequence = sequence;
        CoalescingKey = coalescingKey;
        CorrelationId = correlationId;
        OriginId = originId;
        _updates = Array.AsReadOnly(
            materialized.Select(LiveStageUpdateSnapshot.Snapshot).ToArray());
    }

    /// <summary>Gets the authoritative remote identity and epoch this delta belongs to.</summary>
    public LiveAuthoringRemoteEpoch Epoch { get; }

    /// <summary>Gets the per-epoch remote sequence.</summary>
    public long Sequence { get; }

    /// <summary>Gets the optional coalescing key forwarded to the sink unchanged.</summary>
    public string? CoalescingKey { get; }

    /// <summary>Gets the optional opaque correlation identifier.</summary>
    public string? CorrelationId { get; }

    /// <summary>Gets the opaque identifier of the system that authored this change.</summary>
    public string? OriginId { get; }

    /// <summary>Gets the ordered updates to apply.</summary>
    public IReadOnlyList<LiveStageUpdate> Updates => _updates;
}

/// <summary>Identifies how a session coordinator handled one inbound message.</summary>
public enum LiveAuthoringSessionOutcome
{
    /// <summary>The message was applied to the bridge-owned overlay.</summary>
    Applied,

    /// <summary>
    /// The message was already covered by the accepted sequence and was ignored idempotently. Replaying
    /// an acknowledged delta is not an error and never mutates the stage twice.
    /// </summary>
    Duplicate,

    /// <summary>The message carried the local origin and was suppressed as an echo.</summary>
    LoopSuppressed,

    /// <summary>The message was rejected; <see cref="LiveAuthoringSessionRejection"/> says why.</summary>
    Rejected
}

/// <summary>Identifies why a session coordinator rejected one inbound message.</summary>
public enum LiveAuthoringSessionRejection
{
    /// <summary>The message was not rejected.</summary>
    None,

    /// <summary>The current session state does not accept this message kind.</summary>
    SessionState,

    /// <summary>
    /// The session requires a full snapshot before it accepts deltas again, and the message was a delta.
    /// </summary>
    ResyncRequired,

    /// <summary>The message came from an origin other than the one authoritative remote origin.</summary>
    RemoteOrigin,

    /// <summary>The message belongs to a different remote session identifier.</summary>
    SessionIdentity,

    /// <summary>The message belongs to an epoch older than the bound epoch.</summary>
    EpochRetired,

    /// <summary>
    /// The message belongs to a newer epoch. Sequences are only comparable inside one epoch, so the
    /// session requires a full snapshot for the new epoch before it accepts its deltas.
    /// </summary>
    EpochAdvanced,

    /// <summary>
    /// The message skipped one or more sequences. The session cannot synthesize the missing changes and
    /// requires a full snapshot.
    /// </summary>
    SequenceGap,

    /// <summary>The message targets prims outside the bridge-owned overlay root.</summary>
    BridgeScope,

    /// <summary>Applying the message would push the bridge-owned overlay past its bounds.</summary>
    OverlayBudget,

    /// <summary>
    /// The message reuses a retained sequence with different content: its epoch, effective origin,
    /// correlation identifier, coalescing key, or update payload does not match the message already
    /// accepted at that sequence. Two different messages cannot occupy the same place in one ordered
    /// stream, so the session requires a full snapshot instead of acknowledging a false duplicate.
    /// </summary>
    DuplicateConflict,

    /// <summary>
    /// The message replays a sequence that has fallen out of the bounded replay window, so the session
    /// cannot prove it is the same message already accepted. Claiming an unprovable duplicate would be
    /// a silent correctness risk, so the session requires a full snapshot instead. Size
    /// <see cref="LiveAuthoringSessionOptions.ReplayWindowLength"/> at or above the adapter's deepest
    /// retransmission to avoid this.
    /// </summary>
    ReplayExpired,

    /// <summary>
    /// The message was admitted but failed while applying. There is no per-batch checkpoint, so the
    /// session requires a full snapshot instead of an incremental repair.
    /// </summary>
    ApplyFailed
}

/// <summary>The bounded outcome of one snapshot or delta submitted to a session coordinator.</summary>
public readonly record struct LiveAuthoringSessionResult(
    LiveAuthoringSessionOutcome Outcome,
    LiveAuthoringSessionRejection Rejection,
    long Sequence,
    LiveAuthoringSessionState State,
    long LastAcceptedSequence,
    long LastAppliedSequence,
    string? CorrelationId,
    string? Detail)
{
    /// <summary>Gets whether the message reached the bridge-owned overlay.</summary>
    public bool IsApplied => Outcome == LiveAuthoringSessionOutcome.Applied;
}
