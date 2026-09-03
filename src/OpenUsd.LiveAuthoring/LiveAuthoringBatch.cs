// Copyright (c) marcschier. Licensed under the MIT License.

using System.Collections.ObjectModel;

namespace OpenUsd.LiveAuthoring;

/// <summary>An ordered, optionally supersedable group of stage updates.</summary>
public sealed class LiveAuthoringBatch
{
    private readonly ReadOnlyCollection<LiveStageUpdate> _updates;

    /// <summary>Initializes an ordered update batch.</summary>
    /// <param name="sequence">The strictly increasing producer sequence.</param>
    /// <param name="updates">The updates to apply, in order.</param>
    /// <param name="coalescingKey">
    /// An optional snapshot key. A newer pending batch with the same key may supersede this one.
    /// </param>
    /// <param name="correlationId">
    /// An optional opaque identifier the caller assigns and never interprets by this library. It is
    /// echoed on the admission receipt, the applied result, and health events for external tracing.
    /// </param>
    /// <param name="originId">
    /// An optional opaque identifier naming the producer or upstream system that authored this batch.
    /// Like <paramref name="correlationId"/>, it is carried through without interpretation.
    /// </param>
    public LiveAuthoringBatch(
        long sequence,
        IEnumerable<LiveStageUpdate> updates,
        string? coalescingKey = null,
        string? correlationId = null,
        string? originId = null)
    {
        ArgumentNullException.ThrowIfNull(updates);
        LiveStageUpdate[] materialized = updates.ToArray();
        LiveAuthoringValidation.Validate(
            sequence,
            materialized,
            coalescingKey,
            correlationId,
            originId,
            nameof(updates));

        Sequence = sequence;
        CoalescingKey = coalescingKey;
        CorrelationId = correlationId;
        OriginId = originId;
        LiveStageUpdate[] snapshots = materialized.Select(LiveStageUpdateSnapshot.Snapshot).ToArray();
        _updates = Array.AsReadOnly(snapshots);
        Invalidation = snapshots.Max(static update => update.Invalidation);
    }

    /// <summary>Gets the strictly increasing producer sequence.</summary>
    public long Sequence { get; }

    /// <summary>
    /// Gets the optional snapshot key. A newer pending batch with the same key may supersede this one.
    /// </summary>
    public string? CoalescingKey { get; }

    /// <summary>Gets the optional opaque correlation identifier.</summary>
    public string? CorrelationId { get; }

    /// <summary>Gets the optional opaque origin identifier.</summary>
    public string? OriginId { get; }

    /// <summary>Gets the updates in application order.</summary>
    public IReadOnlyList<LiveStageUpdate> Updates => _updates;

    /// <summary>Gets the strongest renderer invalidation in the batch.</summary>
    public UsdStageInvalidationKind Invalidation { get; }
}

/// <summary>A detached, eventual applied result safe to return from a scheduler callback.</summary>
public readonly record struct LiveAuthoringBatchResult(
    long FirstSequence,
    long LastSequence,
    int BatchCount,
    int UpdateCount,
    UsdStageInvalidationKind Invalidation,
    ulong BeforeChangeSerial,
    ulong AfterChangeSerial,
    string EditTargetLayerIdentifier,
    string? CorrelationId = null,
    string? OriginId = null) : IUsdDetachedResult;

/// <summary>
/// Acknowledges that <see cref="ILiveAuthoringSink.ApplyAsync"/> admitted a batch, separately from its
/// eventual applied result. The receipt itself is the sequence acknowledgement: a caller that observes
/// a receipt knows the batch is queued, in strict sequence order, for execution by the sink's single
/// worker, even before <see cref="Applied"/> completes.
/// </summary>
/// <remarks>
/// Admission is an in-process memory guarantee only. It is not a durability guarantee: a process crash,
/// restart, or unhandled fault before <see cref="Applied"/> completes discards any batch that has not
/// yet reached the stage, the same as it would for any other in-memory queue. Persisting a producer's
/// unacknowledged batches, replaying them after a restart, or otherwise surviving process loss is a
/// resync/recovery concern that this package does not implement.
/// </remarks>
public sealed class LiveAuthoringAdmissionReceipt
{
    /// <summary>Initializes an admission receipt.</summary>
    public LiveAuthoringAdmissionReceipt(
        long sequence,
        string? correlationId,
        string? originId,
        bool coalesced,
        Task<LiveAuthoringBatchResult> applied)
    {
        ArgumentNullException.ThrowIfNull(applied);
        Sequence = sequence;
        CorrelationId = correlationId;
        OriginId = originId;
        Coalesced = coalesced;
        Applied = applied;
    }

    /// <summary>Gets the admitted batch's producer sequence.</summary>
    public long Sequence { get; }

    /// <summary>Gets the batch's opaque correlation identifier, if any.</summary>
    public string? CorrelationId { get; }

    /// <summary>Gets the batch's opaque origin identifier, if any.</summary>
    public string? OriginId { get; }

    /// <summary>
    /// Gets whether admission coalesced this batch into an already-pending tail batch rather than
    /// enqueuing a new entry.
    /// </summary>
    public bool Coalesced { get; }

    /// <summary>
    /// Gets the task that completes with the eventual applied result, or faults if execution or a
    /// coalesced supersession failed. This task is independent of the caller's own cancellation token:
    /// once admitted, the batch remains ordered work regardless of whether the caller keeps waiting.
    /// </summary>
    public Task<LiveAuthoringBatchResult> Applied { get; }

    /// <summary>Awaits <see cref="Applied"/>, optionally cancelling only this caller's wait.</summary>
    public async ValueTask<LiveAuthoringBatchResult> WaitForResultAsync(
        CancellationToken cancellationToken = default) =>
        await Applied.WaitAsync(cancellationToken).ConfigureAwait(false);
}
