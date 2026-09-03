// Copyright (c) marcschier. Licensed under the MIT License.

using OpenUsd.Bridge.Protocol;
using OpenUsd.LiveAuthoring;

namespace OpenUsd.Bridge.Grpc;

/// <summary>Identifies how one local publication ended.</summary>
/// <remarks>
/// Queuing a batch is an admission receipt, not a delivery guarantee, exactly as admission is in
/// <see cref="LiveAuthoringSessionCoordinator"/>. These outcomes are the eventual answer, so a host
/// that publishes an authoritative local edit can tell "the peer applied it" from "the peer refused
/// it" from "the transport never delivered it" instead of inferring from a queue receipt.
/// </remarks>
public enum BridgeLocalPublicationOutcome
{
    /// <summary>The publication has not finished yet.</summary>
    Pending = 0,

    /// <summary>The peer acknowledged the batch as applied.</summary>
    Published,

    /// <summary>
    /// The peer acknowledged the batch as a duplicate of one it already holds. That is a success:
    /// it is precisely what the idempotency key exists to make recognizable across a retry.
    /// </summary>
    Duplicate,

    /// <summary>
    /// The peer refused the batch on its own terms, for example because the session needs a
    /// resync. A semantic refusal is never retried: replaying it would only be refused again.
    /// </summary>
    RemoteRejected,

    /// <summary>
    /// The peer's answer violated the contract: it could not be decoded, named another sequence,
    /// echoed another correlation identifier, or carried a protocol-only rejection.
    /// </summary>
    ProtocolRejected,

    /// <summary>
    /// The transport failed on every bounded attempt. Whether the peer acted on the batch is
    /// unknown, which is exactly why the retries carried the same idempotency key.
    /// </summary>
    TransportFailed,

    /// <summary>
    /// The batch belongs to an epoch the session has left, so its sequence no longer means
    /// anything on the peer and it is dropped rather than replayed into a new epoch.
    /// </summary>
    EpochRetired,

    /// <summary>The bounded outbound channel was full, so the batch was never queued.</summary>
    Refused,

    /// <summary>
    /// The session did not agree the local-edit-export capability, or the batch needs an update
    /// capability the session did not agree, or it exceeds the negotiated bounds.
    /// </summary>
    NotPermitted,

    /// <summary>The client was disposed or cancelled before the batch was published.</summary>
    Cancelled,

    /// <summary>
    /// The batch was sent at least once and the client can no longer learn what the peer did with
    /// it. Whether the peer applied it is genuinely unknown.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is what an attempted send collapses to when the answer is lost and the reason it cannot
    /// be re-asked is not an exhausted transport retry: the session came back on tighter limits or
    /// without the export capability, the epoch was retired, the client was cancelled or disposed,
    /// or a terminal drain answered it. Reporting <see cref="NotPermitted"/>,
    /// <see cref="EpochRetired"/>, or <see cref="Cancelled"/> in those cases would assert the peer
    /// never saw the batch, which the client has no way to know once a request has left it.
    /// </para>
    /// <para>
    /// A host that must not lose an authoritative local edit treats this exactly like
    /// <see cref="TransportFailed"/>: republish under the same idempotency key on a session that can
    /// carry it, or reconcile against the peer. A publication that never made an attempt keeps its
    /// definitive outcome, because there the client does know.
    /// </para>
    /// </remarks>
    Indeterminate
}

/// <summary>The bounded, eventual result of one local publication.</summary>
public readonly record struct BridgeLocalPublicationResult(
    BridgeLocalPublicationOutcome Outcome,
    long Sequence,
    string? CorrelationId,
    string IdempotencyKey,
    int Attempts,
    LiveAuthoringSessionOutcome RemoteOutcome,
    LiveAuthoringSessionRejection RemoteRejection,
    string? Detail)
{
    /// <summary>Gets whether the peer holds the batch, either freshly applied or as a duplicate.</summary>
    public bool IsDelivered =>
        Outcome is BridgeLocalPublicationOutcome.Published or BridgeLocalPublicationOutcome.Duplicate;

    /// <summary>
    /// Gets whether the batch was sent and its effect on the peer is unknown, so a host that must
    /// not lose the edit has to republish it under the same idempotency key or reconcile.
    /// </summary>
    public bool IsIndeterminate =>
        Outcome is BridgeLocalPublicationOutcome.TransportFailed
            or BridgeLocalPublicationOutcome.Indeterminate;
}

/// <summary>
/// Acknowledges that a local batch was queued for publication, and exposes its eventual result
/// separately.
/// </summary>
/// <remarks>
/// <see cref="Accepted"/> answers "did the bounded channel take it"; <see cref="Published"/> answers
/// "what happened to it". The two are deliberately separate, because the second answer can only
/// arrive after a round trip, a reconnect, or a bounded retry, and a host that must not lose an
/// authoritative local edit needs the second one.
/// </remarks>
public sealed class BridgeLocalPublicationReceipt
{
    internal BridgeLocalPublicationReceipt(
        BridgeLocalBatch batch,
        bool accepted,
        Task<BridgeLocalPublicationResult> published)
    {
        Sequence = batch.Sequence;
        CorrelationId = batch.CorrelationId;
        IdempotencyKey = batch.IdempotencyKey;
        Accepted = accepted;
        Published = published;
    }

    /// <summary>Gets the batch's per-epoch local sequence.</summary>
    public long Sequence { get; }

    /// <summary>Gets the batch's opaque correlation identifier, if any.</summary>
    public string? CorrelationId { get; }

    /// <summary>Gets the key every attempt for this batch carries.</summary>
    public string IdempotencyKey { get; }

    /// <summary>Gets whether the bounded outbound channel accepted the batch.</summary>
    public bool Accepted { get; }

    /// <summary>
    /// Gets the task that completes with the eventual publication result. It is independent of the
    /// caller's own cancellation: once queued, the batch stays the client's responsibility until it
    /// is delivered, refused, or the client stops.
    /// </summary>
    public Task<BridgeLocalPublicationResult> Published { get; }

    /// <summary>Awaits <see cref="Published"/>, cancelling only this caller's wait.</summary>
    public async ValueTask<BridgeLocalPublicationResult> WaitForResultAsync(
        CancellationToken cancellationToken = default) =>
        await Published.WaitAsync(cancellationToken).ConfigureAwait(false);
}

/// <summary>One queued publication and the receipt waiting on it.</summary>
internal sealed class BridgePendingPublication
{
    private readonly TaskCompletionSource<BridgeLocalPublicationResult> _completion =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    internal BridgePendingPublication(BridgeLocalBatch batch)
    {
        Batch = batch;
        Receipt = new BridgeLocalPublicationReceipt(batch, accepted: true, _completion.Task);
    }

    internal BridgeLocalBatch Batch { get; }

    internal BridgeLocalPublicationReceipt Receipt { get; }

    /// <summary>Gets how many transport attempts this batch has already made, across connections.</summary>
    internal int Attempts { get; private set; }

    internal int RecordAttempt() => ++Attempts;

    internal bool Complete(
        BridgeLocalPublicationOutcome outcome,
        string? detail,
        LiveAuthoringSessionOutcome remoteOutcome = default,
        LiveAuthoringSessionRejection remoteRejection = LiveAuthoringSessionRejection.None) =>
        _completion.TrySetResult(new BridgeLocalPublicationResult(
            outcome,
            Batch.Sequence,
            Batch.CorrelationId,
            Batch.IdempotencyKey,
            Attempts,
            remoteOutcome,
            remoteRejection,
            detail));

    internal static BridgeLocalPublicationReceipt CreateRefused(
        BridgeLocalBatch batch,
        BridgeLocalPublicationOutcome outcome,
        string detail) =>
        new(
            batch,
            accepted: false,
            Task.FromResult(new BridgeLocalPublicationResult(
                outcome,
                batch.Sequence,
                batch.CorrelationId,
                batch.IdempotencyKey,
                0,
                default,
                LiveAuthoringSessionRejection.None,
                detail)));
}
