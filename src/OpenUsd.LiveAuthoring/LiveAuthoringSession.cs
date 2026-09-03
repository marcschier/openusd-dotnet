// Copyright (c) marcschier. Licensed under the MIT License.

namespace OpenUsd.LiveAuthoring;

/// <summary>
/// The explicit, transport-neutral state of one bridge session. The state machine describes what the
/// coordinator will accept next; it deliberately says nothing about sockets, streams, or protocols, so
/// the same states apply whether an adapter carries messages over a socket, a file drop, or a test
/// harness.
/// </summary>
public enum LiveAuthoringSessionState
{
    /// <summary>
    /// No remote session is bound. Snapshots and deltas are rejected until
    /// <see cref="LiveAuthoringSessionCoordinator.ConnectAsync"/> binds an epoch.
    /// </summary>
    Disconnected = 0,

    /// <summary>
    /// A remote epoch is bound but no full snapshot has been applied yet. Deltas are rejected because
    /// there is no agreed baseline to apply them against.
    /// </summary>
    Connecting = 1,

    /// <summary>
    /// A full snapshot has been applied and in-order deltas from the authoritative remote origin are
    /// accepted. This is the only state in which a delta can be applied.
    /// </summary>
    Synchronized = 2,

    /// <summary>
    /// The session lost its guaranteed baseline: a gap, an advanced epoch, an overlay-budget breach, or
    /// a failed apply occurred. Deltas are rejected until a newer full snapshot succeeds. There is no
    /// per-batch checkpoint to roll back to, and none is created.
    /// </summary>
    ResyncRequired = 3,

    /// <summary>
    /// Disposal or an explicit disconnect is draining in-flight work. Nothing new is accepted; this
    /// state is observable so a health endpoint can distinguish an orderly stop from a fault.
    /// </summary>
    Stopping = 4,

    /// <summary>
    /// The session cannot continue without operator attention, for example because the underlying sink
    /// was disposed beneath the coordinator. Only disconnect and disposal are accepted.
    /// </summary>
    Faulted = 5
}

/// <summary>
/// Identifies the one authoritative remote origin and the exact session/epoch generation its ordered
/// sequences belong to.
/// </summary>
/// <remarks>
/// <para>
/// A sequence number is only meaningful inside one epoch. A remote that restarts, rejoins, or otherwise
/// loses its outbound ordering must advance <see cref="Epoch"/>, which invalidates every previously
/// accepted sequence and forces a full resync instead of silently resuming a stale numbering.
/// </para>
/// <para>
/// <see cref="RemoteOriginId"/> is the single authoritative writer for the bridge-owned overlay.
/// Deltas carrying any other remote origin are rejected rather than merged: this package is a bridge
/// coordinator, not a distributed merge engine.
/// </para>
/// </remarks>
public sealed record LiveAuthoringRemoteEpoch
{
    /// <summary>Initializes an authoritative remote identity for one epoch.</summary>
    /// <param name="remoteOriginId">The one authoritative remote origin identifier.</param>
    /// <param name="sessionId">The remote session identifier this epoch belongs to.</param>
    /// <param name="epoch">
    /// The non-negative epoch generation. It must never decrease for the same
    /// <paramref name="sessionId"/>.
    /// </param>
    public LiveAuthoringRemoteEpoch(string remoteOriginId, string sessionId, long epoch)
    {
        LiveAuthoringValidation.ValidateOpaqueIdentity(
            remoteOriginId,
            nameof(remoteOriginId),
            "A remote origin identifier");
        LiveAuthoringValidation.ValidateOpaqueIdentity(
            sessionId,
            nameof(sessionId),
            "A session identifier");
        ArgumentOutOfRangeException.ThrowIfNegative(epoch);
        RemoteOriginId = remoteOriginId;
        SessionId = sessionId;
        Epoch = epoch;
    }

    /// <summary>Gets the one authoritative remote origin identifier.</summary>
    public string RemoteOriginId { get; }

    /// <summary>Gets the remote session identifier.</summary>
    public string SessionId { get; }

    /// <summary>Gets the epoch generation within <see cref="SessionId"/>.</summary>
    public long Epoch { get; }

    /// <summary>Gets whether this identity names the same origin and session as another identity.</summary>
    public bool IsSameSession(LiveAuthoringRemoteEpoch? other) =>
        other is not null &&
        string.Equals(RemoteOriginId, other.RemoteOriginId, StringComparison.Ordinal) &&
        string.Equals(SessionId, other.SessionId, StringComparison.Ordinal);
}

/// <summary>Identifies what a <see cref="LiveAuthoringSessionEvent"/> reports.</summary>
public enum LiveAuthoringSessionEventKind
{
    /// <summary>The session bound a remote epoch and is waiting for a full snapshot.</summary>
    Connecting,

    /// <summary>A full snapshot replaced the bridge-owned overlay and the session is synchronized.</summary>
    SnapshotApplied,

    /// <summary>A full snapshot was rejected or failed to apply.</summary>
    SnapshotRejected,

    /// <summary>An in-order delta was applied to the bridge-owned overlay.</summary>
    DeltaApplied,

    /// <summary>A delta at or below the last accepted sequence was ignored idempotently.</summary>
    DeltaDuplicate,

    /// <summary>A delta was rejected without being applied.</summary>
    DeltaRejected,

    /// <summary>A delta carrying the local origin identifier was suppressed as an echo.</summary>
    LoopSuppressed,

    /// <summary>The session lost its baseline and now requires a full snapshot.</summary>
    ResyncRequired,

    /// <summary>The session released its remote epoch.</summary>
    Disconnected,

    /// <summary>The session cannot continue without operator attention.</summary>
    Faulted,

    /// <summary>The coordinator finished disposal.</summary>
    Disposed
}

/// <summary>
/// A bounded, structured session notification. Every field is a fixed-size value or a length-capped
/// string, so an adapter may forward instances to an external health endpoint without further bounding.
/// </summary>
public readonly record struct LiveAuthoringSessionEvent(
    LiveAuthoringSessionEventKind Kind,
    LiveAuthoringSessionState PreviousState,
    LiveAuthoringSessionState State,
    string? RemoteOriginId,
    string? SessionId,
    long Epoch,
    long LastAcceptedSequence,
    long LastAppliedSequence,
    string? CorrelationId,
    DateTimeOffset TimestampUtc,
    string? Detail);

/// <summary>
/// A bounded, point-in-time view of session recovery health, suitable for polling from an external
/// health endpoint alongside <see cref="LiveAuthoringHealthSnapshot"/>.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="ReplayWindowLength"/>, <see cref="ReplayLedgerCount"/>,
/// <see cref="ReplayLedgerBytes"/>, and <see cref="OldestRetainedSequence"/> describe the bounded
/// replay ledger that backs idempotent duplicate acknowledgement. A replay at or below
/// <see cref="LastAcceptedSequence"/> but below <see cref="OldestRetainedSequence"/> can no longer be
/// proven to be the same message and is rejected with
/// <see cref="LiveAuthoringSessionRejection.ReplayExpired"/> rather than acknowledged.
/// </para>
/// <para>
/// <see cref="SessionObserverFailureCount"/> and <see cref="LastSessionObserverFailureDetail"/> report
/// exceptions thrown by a caller-supplied <see cref="IProgress{T}"/> session observer. Every such
/// exception is isolated here rather than propagated, exactly as
/// <see cref="QueuedLiveAuthoringSink"/> isolates health-observer failures: a broken observer must never
/// change acceptance, rejection, or resync semantics.
/// </para>
/// </remarks>
public readonly record struct LiveAuthoringSessionStatus(
    LiveAuthoringSessionState State,
    string? RemoteOriginId,
    string? SessionId,
    long Epoch,
    long LastAcceptedSequence,
    long LastAppliedSequence,
    long AppliedSnapshotCount,
    long AppliedDeltaCount,
    long DuplicateDeltaCount,
    long RejectedDeltaCount,
    long LoopSuppressedDeltaCount,
    long ResyncRequiredCount,
    int OverlayPrimCount,
    int OverlayUpdateCount,
    int ReplayWindowLength,
    int ReplayLedgerCount,
    long ReplayLedgerBytes,
    long OldestRetainedSequence,
    string? LastFailureDetail,
    DateTimeOffset TimestampUtc,
    long SessionObserverFailureCount = 0,
    string? LastSessionObserverFailureDetail = null);

/// <summary>Configures one bridge session coordinator.</summary>
public sealed class LiveAuthoringSessionOptions
{
    /// <summary>
    /// Gets or sets the absolute prim path that roots the bridge-owned overlay. The path must be
    /// reserved for the bridge: a full snapshot removes and re-authors this subtree in the live edit
    /// layer. Opinions outside it, including a user-edit layer and a physics overlay, are never touched.
    /// </summary>
    public string BridgeRootPath { get; set; } = "/Bridge";

    /// <summary>
    /// Gets or sets the opaque identifier naming this process as a change origin, or
    /// <see langword="null"/> to take a generated one that is unique to this coordinator instance.
    /// An inbound delta carrying this exact origin is suppressed as an echo of a local edit rather
    /// than reapplied, which is what prevents an authoring loop between the two sides.
    /// </summary>
    /// <remarks>
    /// <para>
    /// There is deliberately no shared literal default. Echo suppression is an identity comparison,
    /// so two coordinators that share one origin identifier suppress each other's edits as if they
    /// were their own echoes, and two publishers that share one origin derive colliding idempotency
    /// keys — which a peer's ledger reads as "already applied" and silently drops. A default that
    /// every process in the fleet inherits is exactly the value that makes both faults likely.
    /// </para>
    /// <para>
    /// Leaving this <see langword="null"/> therefore yields a value from
    /// <see cref="LocalOriginIdFactory"/>, or from
    /// <see cref="LiveAuthoringOriginId.CreateProcessInstanceUnique"/> when no factory is supplied.
    /// A host that needs a stable identity across restarts — to be recognized by a peer that
    /// remembers it — sets the value explicitly and owns its uniqueness.
    /// </para>
    /// </remarks>
    public string? LocalOriginId { get; set; }

    /// <summary>
    /// Gets or sets the factory consulted when <see cref="LocalOriginId"/> is <see langword="null"/>.
    /// </summary>
    /// <remarks>
    /// This is the determinism seam. The generated default is unique on purpose, which is precisely
    /// what a test asserting an exact identifier cannot use; injecting a factory keeps the default
    /// unique in production and fixed in a test without a shared literal reappearing as the fallback.
    /// </remarks>
    public Func<string>? LocalOriginIdFactory { get; set; }

    /// <summary>
    /// Gets or sets the batch sequence already consumed on the supplied sink. The coordinator allocates
    /// strictly increasing sink sequences starting after this value, so a host that already submitted
    /// local batches through the same sink can hand it over without a sequence collision.
    /// </summary>
    public long InitialBatchSequence { get; set; }

    /// <summary>
    /// Gets or sets how many recently accepted remote sequences the session retains in its replay
    /// ledger, bounded by <see cref="LiveAuthoringValidation.MaxReplayWindowLength"/>.
    /// </summary>
    /// <remarks>
    /// The ledger stores one content fingerprint per retained sequence and never the payload, so the
    /// cost is fixed per entry. A replay inside the window is acknowledged as a duplicate only when its
    /// fingerprint matches; a replay below the window cannot be proven and is rejected with
    /// <see cref="LiveAuthoringSessionRejection.ReplayExpired"/>. Size this at or above the deepest
    /// retransmission an adapter can perform after a transport hiccup.
    /// </remarks>
    public int ReplayWindowLength { get; set; } = LiveAuthoringValidation.DefaultReplayWindowLength;

    /// <summary>
    /// Gets or sets whether the coordinator disposes the supplied sink. The default is
    /// <see langword="false"/>, because <see cref="UsdLiveAuthoringHost"/> already owns its sink.
    /// </summary>
    public bool OwnsSink { get; set; }

    /// <summary>
    /// Gets or sets an optional observer notified of bounded, structured session events. A
    /// <see langword="null"/> observer disables event reporting;
    /// <see cref="LiveAuthoringSessionCoordinator.GetStatus"/> remains available either way.
    /// </summary>
    public IProgress<LiveAuthoringSessionEvent>? SessionObserver { get; set; }
}
