// Copyright (c) marcschier. Licensed under the MIT License.

namespace OpenUsd.LiveAuthoring;

/// <summary>
/// Coordinates one bridge session over an existing <see cref="ILiveAuthoringSink"/>: explicit session
/// states, one authoritative remote origin and epoch, duplicate/gap and idempotent-replay rules, loop
/// prevention, full-snapshot recovery of a bridge-owned overlay, and structured state/health events.
/// </summary>
/// <remarks>
/// <para>
/// The coordinator is transport-neutral. It never opens a socket, negotiates a protocol, or serializes
/// a message: an adapter decodes whatever wire format it uses into <see cref="LiveAuthoringSnapshot"/>
/// and <see cref="LiveAuthoringDelta"/> values and calls this type. That keeps every recovery rule
/// testable without networking and lets the wire contract land separately.
/// </para>
/// <para>
/// Raw <see cref="QueuedLiveAuthoringSink"/> semantics are untouched. The sink still admits, orders,
/// coalesces, and applies batches exactly as before, including its explicit partial-failure behaviour
/// for a multi-update batch. Recovery lives here instead: the coordinator observes each applied result
/// and converts a failure into <see cref="LiveAuthoringSessionState.ResyncRequired"/> rather than
/// asking the sink to roll anything back.
/// </para>
/// <para>
/// Every operation is serialized on one gate, so the session state, the accepted/applied sequences, and
/// the overlay model advance together. A delta is applied to a candidate copy of the overlay model and
/// the copy is adopted only after the stage edit succeeds, so a rejected or failed delta never leaves
/// the model describing content the stage does not hold.
/// </para>
/// <para>
/// Once a batch is admitted the coordinator always observes its applied result, even if the caller's
/// token is cancelled: an admitted batch is ordered work that will reach the stage regardless, and
/// abandoning the wait would let the model and the stage diverge. Cancellation therefore applies to
/// admission, not to an already admitted edit.
/// </para>
/// </remarks>
public sealed class LiveAuthoringSessionCoordinator : IAsyncDisposable
{
    private const int MaxDetailLength = 256;

    private readonly TaskCompletionSource _disposed =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly CancellationTokenSource _lifetime = new();
    private readonly IProgress<LiveAuthoringSessionEvent>? _observer;
    private readonly bool _ownsSink;
    private readonly ILiveAuthoringSink _sink;
    private readonly object _stateGate = new();
    private long _appliedDeltaCount;
    private long _appliedSnapshotCount;
    private int _disposeState;
    private long _duplicateDeltaCount;
    private LiveAuthoringRemoteEpoch? _epoch;
    private long _lastAcceptedSequence;
    private long _lastAppliedSequence;
    private string? _lastFailureDetail;
    private string? _lastObserverFailureDetail;
    private long _loopSuppressedDeltaCount;
    private long _nextBatchSequence;
    private long _observerFailureCount;
    private LiveAuthoringOverlayModel _overlay;
    private readonly LiveAuthoringReplayLedger _replayLedger;
    private long _rejectedDeltaCount;
    private long _resyncRequiredCount;
    private LiveAuthoringSessionState _state = LiveAuthoringSessionState.Disconnected;

    /// <summary>Initializes a coordinator over an existing ordered sink.</summary>
    /// <param name="sink">
    /// The sink that admits and applies batches. The coordinator must be its only producer while the
    /// session is bound, because the sink enforces strictly increasing admission order.
    /// </param>
    /// <param name="options">Optional session configuration.</param>
    public LiveAuthoringSessionCoordinator(
        ILiveAuthoringSink sink,
        LiveAuthoringSessionOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(sink);
        options ??= new LiveAuthoringSessionOptions();
        LiveAuthoringValidation.ValidateBridgeRootPath(
            options.BridgeRootPath,
            $"{nameof(options)}.{nameof(options.BridgeRootPath)}");
        string localOriginId = LiveAuthoringOriginId.Resolve(
            options.LocalOriginId,
            options.LocalOriginIdFactory,
            $"{nameof(options)}.{nameof(options.LocalOriginId)}");
        ArgumentOutOfRangeException.ThrowIfNegative(
            options.InitialBatchSequence,
            $"{nameof(options)}.{nameof(options.InitialBatchSequence)}");
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(
            options.ReplayWindowLength,
            $"{nameof(options)}.{nameof(options.ReplayWindowLength)}");
        ArgumentOutOfRangeException.ThrowIfGreaterThan(
            options.ReplayWindowLength,
            LiveAuthoringValidation.MaxReplayWindowLength,
            $"{nameof(options)}.{nameof(options.ReplayWindowLength)}");

        _sink = sink;
        _observer = options.SessionObserver;
        _ownsSink = options.OwnsSink;
        BridgeRootPath = options.BridgeRootPath;
        LocalOriginId = localOriginId;
        _nextBatchSequence = options.InitialBatchSequence;
        _overlay = new LiveAuthoringOverlayModel(BridgeRootPath);
        _replayLedger = new LiveAuthoringReplayLedger(options.ReplayWindowLength);
    }

    /// <summary>Gets the absolute prim path that roots the bridge-owned overlay.</summary>
    public string BridgeRootPath { get; }

    /// <summary>Gets the opaque identifier naming this coordinator as a change origin.</summary>
    /// <remarks>
    /// This is the resolved identity, not the configured one: when
    /// <see cref="LiveAuthoringSessionOptions.LocalOriginId"/> is left unset it is the generated
    /// per-instance value. An adapter that publishes local edits must carry exactly this identifier,
    /// or the peer's echoes of those edits will not be recognized as echoes.
    /// </remarks>
    public string LocalOriginId { get; }

    /// <summary>Gets the current session state.</summary>
    public LiveAuthoringSessionState State
    {
        get
        {
            lock (_stateGate)
            {
                return _state;
            }
        }
    }

    /// <summary>Returns a bounded, point-in-time view of session recovery health.</summary>
    public LiveAuthoringSessionStatus GetStatus()
    {
        lock (_stateGate)
        {
            return CreateStatusLocked();
        }
    }

    /// <summary>
    /// Returns the bridge-owned overlay as a canonical, ordered update list. The list describes only
    /// what this bridge authored: user, physics, and root-layer opinions are never included.
    /// </summary>
    public IReadOnlyList<LiveStageUpdate> ExportOverlayUpdates()
    {
        lock (_stateGate)
        {
            return _overlay.Export();
        }
    }

    /// <summary>
    /// Exports the bridge-owned overlay as a bounded full snapshot at the last applied sequence, for
    /// handoff, diagnostics, or a symmetric round-trip test.
    /// </summary>
    /// <exception cref="InvalidOperationException">No remote epoch is bound.</exception>
    public LiveAuthoringSnapshot ExportSnapshot(string? correlationId = null)
    {
        lock (_stateGate)
        {
            if (_epoch is null)
            {
                throw new InvalidOperationException(
                    "A snapshot cannot be exported before a remote epoch is bound. Call ConnectAsync " +
                    "first, or use ExportOverlayUpdates for the raw overlay content.");
            }

            return new LiveAuthoringSnapshot(
                _epoch,
                _lastAppliedSequence,
                BridgeRootPath,
                _overlay.Export(),
                correlationId);
        }
    }

    /// <summary>
    /// Binds an authoritative remote epoch and enters <see cref="LiveAuthoringSessionState.Connecting"/>.
    /// </summary>
    /// <remarks>
    /// Connecting is also the reconnect path. It always discards the previous sequence agreement, so a
    /// reconnect can only reach <see cref="LiveAuthoringSessionState.Synchronized"/> through a full
    /// snapshot. The stage keeps whatever the bridge overlay last held until that snapshot replaces it,
    /// so a viewer does not flash empty while the remote is coming back.
    /// </remarks>
    public async ValueTask<LiveAuthoringSessionStatus> ConnectAsync(
        LiveAuthoringRemoteEpoch epoch,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(epoch);
        using GateScope scope = await EnterAsync(cancellationToken).ConfigureAwait(false);
        LiveAuthoringSessionState previous;
        LiveAuthoringSessionStatus status;
        lock (_stateGate)
        {
            if (_state == LiveAuthoringSessionState.Stopping)
            {
                throw new InvalidOperationException(
                    "The session is stopping and cannot bind a new remote epoch.");
            }
            if (_epoch is not null && _epoch.IsSameSession(epoch) && epoch.Epoch < _epoch.Epoch)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(epoch),
                    epoch.Epoch,
                    "A remote epoch cannot move backwards for the same session identifier.");
            }

            previous = _state;
            _epoch = epoch;
            _lastAcceptedSequence = 0;
            _lastAppliedSequence = 0;
            _lastFailureDetail = null;
            // Sequences from the previous agreement can be neither duplicates nor conflicts of the new
            // one, so the ledger's evidence is discarded with the agreement it belonged to.
            _replayLedger.Clear();
            _state = LiveAuthoringSessionState.Connecting;
            status = CreateStatusLocked();
        }

        Report(
            LiveAuthoringSessionEventKind.Connecting,
            previous,
            status,
            correlationId: null,
            detail: null);
        return status;
    }

    /// <summary>
    /// Applies a bounded full snapshot, atomically replacing the bridge-owned overlay and, on success,
    /// returning the session to <see cref="LiveAuthoringSessionState.Synchronized"/>.
    /// </summary>
    public async ValueTask<LiveAuthoringSessionResult> ApplySnapshotAsync(
        LiveAuthoringSnapshot snapshot,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        using GateScope scope = await EnterAsync(cancellationToken).ConfigureAwait(false);
        LiveAuthoringOverlayModel candidate = _overlay;
        LiveStageUpdate[] canonical = [];
        LiveAuthoringSessionResult rejected = default;
        PendingReport pending = default;
        lock (_stateGate)
        {
            LiveAuthoringSessionRejection? rejection = ClassifySnapshotLocked(snapshot);
            if (rejection is { } reason)
            {
                rejected = RejectLocked(
                    LiveAuthoringSessionEventKind.SnapshotRejected,
                    reason,
                    snapshot.Sequence,
                    snapshot.CorrelationId,
                    DescribeRejection(reason),
                    countAsDelta: false,
                    out pending);
            }
            else
            {
                try
                {
                    candidate = BuildOverlay(snapshot.Updates);
                    canonical = candidate.Export();
                    _epoch = snapshot.Epoch;
                }
                catch (Exception exception) when (
                    exception is ArgumentException or NotSupportedException)
                {
                    rejected = RejectLocked(
                        LiveAuthoringSessionEventKind.SnapshotRejected,
                        LiveAuthoringSessionRejection.OverlayBudget,
                        snapshot.Sequence,
                        snapshot.CorrelationId,
                        exception.Message,
                        countAsDelta: false,
                        out pending);
                }
            }
        }

        if (pending.HasReport)
        {
            Report(pending);
            return rejected;
        }

        var batch = new LiveAuthoringBatch(
            NextBatchSequence(),
            [new ReplaceBridgeOverlayUpdate(BridgeRootPath, canonical)],
            coalescingKey: null,
            correlationId: snapshot.CorrelationId,
            originId: snapshot.Epoch.RemoteOriginId);
        SubmitOutcome submission = await SubmitAsync(batch, cancellationToken).ConfigureAwait(false);
        if (submission.Failure is not null)
        {
            LiveAuthoringSessionState previousState;
            LiveAuthoringSessionStatus failedStatus;
            lock (_stateGate)
            {
                previousState = _state;
                // The executor leaves the bridge root empty when a replacement fails partway, so the
                // model must agree: the session owns nothing until a newer snapshot succeeds.
                _overlay = new LiveAuthoringOverlayModel(BridgeRootPath);
                _lastAcceptedSequence = 0;
                _lastAppliedSequence = 0;
                _replayLedger.Clear();
                _lastFailureDetail = Truncate(submission.Failure.Message);
                _state = submission.Fatal
                    ? LiveAuthoringSessionState.Faulted
                    : LiveAuthoringSessionState.ResyncRequired;
                _resyncRequiredCount++;
                failedStatus = CreateStatusLocked();
            }

            Report(
                submission.Fatal
                    ? LiveAuthoringSessionEventKind.Faulted
                    : LiveAuthoringSessionEventKind.ResyncRequired,
                previousState,
                failedStatus,
                snapshot.CorrelationId,
                submission.Failure.Message);
            return new LiveAuthoringSessionResult(
                LiveAuthoringSessionOutcome.Rejected,
                LiveAuthoringSessionRejection.ApplyFailed,
                snapshot.Sequence,
                failedStatus.State,
                failedStatus.LastAcceptedSequence,
                failedStatus.LastAppliedSequence,
                snapshot.CorrelationId,
                Truncate(submission.Failure.Message));
        }

        LiveAuthoringSessionState previous;
        LiveAuthoringSessionStatus status;
        lock (_stateGate)
        {
            previous = _state;
            _overlay = candidate;
            _lastAcceptedSequence = snapshot.Sequence;
            _lastAppliedSequence = snapshot.Sequence;
            // A snapshot establishes a new baseline: nothing before it can be replayed, so the ledger
            // starts empty again and a stale replay is reported as expired rather than as a duplicate.
            _replayLedger.Clear();
            _lastFailureDetail = null;
            _appliedSnapshotCount++;
            _state = LiveAuthoringSessionState.Synchronized;
            status = CreateStatusLocked();
        }

        Report(
            LiveAuthoringSessionEventKind.SnapshotApplied,
            previous,
            status,
            snapshot.CorrelationId,
            detail: null);
        return new LiveAuthoringSessionResult(
            LiveAuthoringSessionOutcome.Applied,
            LiveAuthoringSessionRejection.None,
            snapshot.Sequence,
            status.State,
            status.LastAcceptedSequence,
            status.LastAppliedSequence,
            snapshot.CorrelationId,
            Detail: null);
    }

    /// <summary>
    /// Applies one incremental change from the authoritative remote origin, enforcing loop prevention,
    /// epoch agreement, and the duplicate/gap rules before anything reaches the stage.
    /// </summary>
    public async ValueTask<LiveAuthoringSessionResult> ApplyDeltaAsync(
        LiveAuthoringDelta delta,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(delta);
        using GateScope scope = await EnterAsync(cancellationToken).ConfigureAwait(false);
        string effectiveOriginId = delta.OriginId ?? delta.Epoch.RemoteOriginId;
        bool isLocalEcho = string.Equals(delta.OriginId, LocalOriginId, StringComparison.Ordinal);
        // Fingerprinting hashes the whole payload, so it runs before the state lock is taken rather
        // than holding the session gate for the length of a large delta.
        LiveAuthoringDeltaFingerprint fingerprint =
            LiveAuthoringDeltaFingerprint.Compute(delta, effectiveOriginId);
        LiveAuthoringOverlayModel candidate = _overlay;
        LiveAuthoringSessionResult early = default;
        PendingReport pending = default;
        PendingReport resync = default;
        lock (_stateGate)
        {
            LiveAuthoringSessionRejection? identity = ClassifyDeltaIdentityLocked(delta);
            if (identity is { } identityReason)
            {
                // Identity and state are checked before loop suppression on purpose. A message that
                // names a different session, a retired epoch, or an unbound session is not a harmless
                // echo of a local edit even when it carries the local origin identifier, and reporting
                // it as one would hide a misrouted or stale producer behind a benign outcome.
                if (identityReason is LiveAuthoringSessionRejection.EpochAdvanced)
                {
                    EnterResyncRequiredLocked(DescribeRejection(identityReason));
                    resync = new PendingReport(
                        LiveAuthoringSessionEventKind.ResyncRequired,
                        _state,
                        CreateStatusLocked(),
                        delta.CorrelationId,
                        DescribeRejection(identityReason));
                }
                early = RejectLocked(
                    LiveAuthoringSessionEventKind.DeltaRejected,
                    identityReason,
                    delta.Sequence,
                    delta.CorrelationId,
                    DescribeRejection(identityReason),
                    countAsDelta: true,
                    out pending);
            }
            else
            {
                LiveAuthoringReplayMatch match = _replayLedger.Classify(
                    delta.Sequence,
                    _lastAcceptedSequence,
                    fingerprint);
                switch (match)
                {
                    case LiveAuthoringReplayMatch.Identical:
                        _duplicateDeltaCount++;
                        LiveAuthoringSessionStatus duplicate = CreateStatusLocked();
                        pending = new PendingReport(
                            LiveAuthoringSessionEventKind.DeltaDuplicate,
                            duplicate.State,
                            duplicate,
                            delta.CorrelationId,
                            "The delta replays a retained sequence with an identical fingerprint and " +
                            "was acknowledged idempotently.");
                        early = new LiveAuthoringSessionResult(
                            LiveAuthoringSessionOutcome.Duplicate,
                            LiveAuthoringSessionRejection.None,
                            delta.Sequence,
                            duplicate.State,
                            duplicate.LastAcceptedSequence,
                            duplicate.LastAppliedSequence,
                            delta.CorrelationId,
                            Detail: null);
                        break;
                    case LiveAuthoringReplayMatch.Conflict:
                    case LiveAuthoringReplayMatch.Expired:
                        LiveAuthoringSessionRejection replayReason =
                            match == LiveAuthoringReplayMatch.Conflict
                                ? LiveAuthoringSessionRejection.DuplicateConflict
                                : LiveAuthoringSessionRejection.ReplayExpired;
                        EnterResyncRequiredLocked(DescribeRejection(replayReason));
                        resync = new PendingReport(
                            LiveAuthoringSessionEventKind.ResyncRequired,
                            _state,
                            CreateStatusLocked(),
                            delta.CorrelationId,
                            DescribeRejection(replayReason));
                        early = RejectLocked(
                            LiveAuthoringSessionEventKind.DeltaRejected,
                            replayReason,
                            delta.Sequence,
                            delta.CorrelationId,
                            DescribeRejection(replayReason),
                            countAsDelta: true,
                            out pending);
                        break;
                    default:
                        early = AdmitDeltaLocked(
                            delta,
                            fingerprint,
                            isLocalEcho,
                            ref candidate,
                            out resync,
                            out pending);
                        break;
                }
            }
        }

        if (resync.HasReport)
        {
            Report(resync);
        }
        if (pending.HasReport)
        {
            Report(pending);
            return early;
        }

        var batch = new LiveAuthoringBatch(
            NextBatchSequence(),
            delta.Updates,
            delta.CoalescingKey,
            delta.CorrelationId,
            delta.OriginId ?? delta.Epoch.RemoteOriginId);
        SubmitOutcome submission = await SubmitAsync(batch, cancellationToken).ConfigureAwait(false);
        if (submission.Failure is not null)
        {
            LiveAuthoringSessionState previousState;
            LiveAuthoringSessionStatus failedStatus;
            lock (_stateGate)
            {
                previousState = _state;
                _rejectedDeltaCount++;
                if (submission.Fatal)
                {
                    _lastFailureDetail = Truncate(submission.Failure.Message);
                    _state = LiveAuthoringSessionState.Faulted;
                }
                else
                {
                    EnterResyncRequiredLocked(submission.Failure.Message);
                }
                failedStatus = CreateStatusLocked();
            }

            Report(
                submission.Fatal
                    ? LiveAuthoringSessionEventKind.Faulted
                    : LiveAuthoringSessionEventKind.ResyncRequired,
                previousState,
                failedStatus,
                delta.CorrelationId,
                submission.Failure.Message);
            return new LiveAuthoringSessionResult(
                LiveAuthoringSessionOutcome.Rejected,
                LiveAuthoringSessionRejection.ApplyFailed,
                delta.Sequence,
                failedStatus.State,
                failedStatus.LastAcceptedSequence,
                failedStatus.LastAppliedSequence,
                delta.CorrelationId,
                Truncate(submission.Failure.Message));
        }

        LiveAuthoringSessionState previous;
        LiveAuthoringSessionStatus status;
        lock (_stateGate)
        {
            previous = _state;
            _overlay = candidate;
            _lastAppliedSequence = delta.Sequence;
            _appliedDeltaCount++;
            status = CreateStatusLocked();
        }

        Report(
            LiveAuthoringSessionEventKind.DeltaApplied,
            previous,
            status,
            delta.CorrelationId,
            detail: null);
        return new LiveAuthoringSessionResult(
            LiveAuthoringSessionOutcome.Applied,
            LiveAuthoringSessionRejection.None,
            delta.Sequence,
            status.State,
            status.LastAcceptedSequence,
            status.LastAppliedSequence,
            delta.CorrelationId,
            Detail: null);
    }

    /// <summary>
    /// Forces <see cref="LiveAuthoringSessionState.ResyncRequired"/>, for example because an adapter
    /// detected a transport fault the coordinator cannot see. Deltas are rejected until a newer full
    /// snapshot succeeds. A disconnected, stopping, or faulted session is left unchanged, because none
    /// of them has a baseline a snapshot could restore.
    /// </summary>
    public LiveAuthoringSessionStatus RequestResync(string? detail = null)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposeState) != 0, this);
        LiveAuthoringSessionState previous;
        LiveAuthoringSessionStatus status;
        lock (_stateGate)
        {
            previous = _state;
            if (_state is LiveAuthoringSessionState.Disconnected
                or LiveAuthoringSessionState.Stopping
                or LiveAuthoringSessionState.Faulted)
            {
                // A disconnected session has nothing to resynchronize, a stopping one is already
                // draining, and a faulted one cannot reach the stage at all: promoting any of them to
                // ResyncRequired would advertise a recovery path that does not exist.
                return CreateStatusLocked();
            }

            EnterResyncRequiredLocked(detail ?? "An external adapter requested a full resync.");
            status = CreateStatusLocked();
        }

        Report(
            LiveAuthoringSessionEventKind.ResyncRequired,
            previous,
            status,
            correlationId: null,
            detail);
        return status;
    }

    /// <summary>
    /// Releases the remote epoch and returns to <see cref="LiveAuthoringSessionState.Disconnected"/>.
    /// </summary>
    /// <remarks>
    /// The bridge overlay content already on the stage is left alone, because a disconnect is not an
    /// instruction to erase what the remote last published. The overlay model is cleared so a later
    /// reconnect cannot export a stale baseline, and the next accepted snapshot replaces the on-stage
    /// overlay atomically.
    /// </remarks>
    public async ValueTask<LiveAuthoringSessionStatus> DisconnectAsync(
        CancellationToken cancellationToken = default)
    {
        using GateScope scope = await EnterAsync(cancellationToken).ConfigureAwait(false);
        LiveAuthoringSessionState previous;
        LiveAuthoringSessionStatus status;
        lock (_stateGate)
        {
            previous = _state;
            ClearSessionLocked();
            status = CreateStatusLocked();
        }

        if (previous != LiveAuthoringSessionState.Disconnected)
        {
            Report(
                LiveAuthoringSessionEventKind.Disconnected,
                previous,
                status,
                correlationId: null,
                detail: null);
        }
        return status;
    }

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.CompareExchange(ref _disposeState, 1, 0) != 0)
        {
            await _disposed.Task.ConfigureAwait(false);
            return;
        }

        LiveAuthoringSessionState previous;
        lock (_stateGate)
        {
            previous = _state;
            _state = LiveAuthoringSessionState.Stopping;
        }

        try
        {
            // Wait for any in-flight operation to finish before tearing anything down, so a delta that
            // is already admitted is still observed and the model never outlives its own edit.
            await _gate.WaitAsync(CancellationToken.None).ConfigureAwait(false);
            try
            {
                await _lifetime.CancelAsync().ConfigureAwait(false);
                lock (_stateGate)
                {
                    ClearSessionLocked();
                }
                if (_ownsSink && _sink is IAsyncDisposable disposableSink)
                {
                    await disposableSink.DisposeAsync().ConfigureAwait(false);
                }
            }
            finally
            {
                _gate.Release();
            }
            _disposed.TrySetResult();
        }
        catch (Exception exception)
        {
            _disposed.TrySetException(exception);
            throw;
        }
        finally
        {
            _gate.Dispose();
            _lifetime.Dispose();
            Volatile.Write(ref _disposeState, 2);
            LiveAuthoringSessionStatus status;
            lock (_stateGate)
            {
                status = CreateStatusLocked();
            }
            Report(
                LiveAuthoringSessionEventKind.Disposed,
                previous,
                status,
                correlationId: null,
                detail: null);
        }
    }

    private LiveAuthoringSessionRejection? ClassifySnapshotLocked(LiveAuthoringSnapshot snapshot)
    {
        if (!string.Equals(snapshot.BridgeRootPath, BridgeRootPath, StringComparison.Ordinal))
        {
            return LiveAuthoringSessionRejection.BridgeScope;
        }
        if (_state is LiveAuthoringSessionState.Disconnected
            or LiveAuthoringSessionState.Stopping
            or LiveAuthoringSessionState.Faulted)
        {
            return LiveAuthoringSessionRejection.SessionState;
        }
        if (_epoch is null)
        {
            return LiveAuthoringSessionRejection.SessionState;
        }
        if (!string.Equals(
            snapshot.Epoch.RemoteOriginId,
            _epoch.RemoteOriginId,
            StringComparison.Ordinal))
        {
            return LiveAuthoringSessionRejection.RemoteOrigin;
        }
        if (!string.Equals(snapshot.Epoch.SessionId, _epoch.SessionId, StringComparison.Ordinal))
        {
            return LiveAuthoringSessionRejection.SessionIdentity;
        }
        if (snapshot.Epoch.Epoch < _epoch.Epoch)
        {
            return LiveAuthoringSessionRejection.EpochRetired;
        }
        return null;
    }

    /// <summary>
    /// Classifies a delta's session state, authoritative remote origin, session identifier, and epoch.
    /// This runs before loop suppression and before any replay decision, so a message that does not
    /// belong to the current agreement can never be reported as a harmless echo or a benign duplicate.
    /// </summary>
    private LiveAuthoringSessionRejection? ClassifyDeltaIdentityLocked(LiveAuthoringDelta delta)
    {
        if (_state == LiveAuthoringSessionState.ResyncRequired)
        {
            return LiveAuthoringSessionRejection.ResyncRequired;
        }
        if (_state != LiveAuthoringSessionState.Synchronized || _epoch is null)
        {
            return LiveAuthoringSessionRejection.SessionState;
        }
        if (!string.Equals(delta.Epoch.RemoteOriginId, _epoch.RemoteOriginId, StringComparison.Ordinal))
        {
            return LiveAuthoringSessionRejection.RemoteOrigin;
        }
        if (!string.Equals(delta.Epoch.SessionId, _epoch.SessionId, StringComparison.Ordinal))
        {
            return LiveAuthoringSessionRejection.SessionIdentity;
        }
        if (delta.Epoch.Epoch < _epoch.Epoch)
        {
            return LiveAuthoringSessionRejection.EpochRetired;
        }
        if (delta.Epoch.Epoch > _epoch.Epoch)
        {
            return LiveAuthoringSessionRejection.EpochAdvanced;
        }
        return null;
    }

    /// <summary>
    /// Applies the ordering, scope, and overlay-budget rules to a delta the replay ledger has not seen,
    /// then either records it as accepted or returns the rejection that stopped it.
    /// </summary>
    /// <remarks>
    /// A suppressed local echo still consumes its remote sequence. Skipping the sequence would make the
    /// next in-order delta look like a gap and force a resync on every round trip, which would defeat
    /// loop prevention entirely. The echo is therefore recorded in the ledger, advances the accepted and
    /// applied sequences, and is folded into the overlay model — the content is already on the stage,
    /// because the local edit that the remote echoed authored it — but it is deliberately not
    /// re-authored through the sink.
    /// </remarks>
    private LiveAuthoringSessionResult AdmitDeltaLocked(
        LiveAuthoringDelta delta,
        LiveAuthoringDeltaFingerprint fingerprint,
        bool isLocalEcho,
        ref LiveAuthoringOverlayModel candidate,
        out PendingReport resync,
        out PendingReport pending)
    {
        resync = default;
        if (delta.Sequence > _lastAcceptedSequence + 1)
        {
            const LiveAuthoringSessionRejection gap = LiveAuthoringSessionRejection.SequenceGap;
            EnterResyncRequiredLocked(DescribeRejection(gap));
            resync = new PendingReport(
                LiveAuthoringSessionEventKind.ResyncRequired,
                _state,
                CreateStatusLocked(),
                delta.CorrelationId,
                DescribeRejection(gap));
            return RejectLocked(
                LiveAuthoringSessionEventKind.DeltaRejected,
                gap,
                delta.Sequence,
                delta.CorrelationId,
                DescribeRejection(gap),
                countAsDelta: true,
                out pending);
        }

        foreach (LiveStageUpdate update in delta.Updates)
        {
            if (!LiveStageUpdatePaths.IsWithin(
                BridgeRootPath,
                LiveStageUpdatePaths.GetPrimPath(update)))
            {
                const LiveAuthoringSessionRejection scope = LiveAuthoringSessionRejection.BridgeScope;
                return RejectLocked(
                    LiveAuthoringSessionEventKind.DeltaRejected,
                    scope,
                    delta.Sequence,
                    delta.CorrelationId,
                    DescribeRejection(scope),
                    countAsDelta: true,
                    out pending);
            }
        }

        try
        {
            candidate = _overlay.Clone();
            foreach (LiveStageUpdate update in delta.Updates)
            {
                candidate.Apply(update);
            }
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException)
        {
            EnterResyncRequiredLocked(exception.Message);
            resync = new PendingReport(
                LiveAuthoringSessionEventKind.ResyncRequired,
                _state,
                CreateStatusLocked(),
                delta.CorrelationId,
                exception.Message);
            return RejectLocked(
                LiveAuthoringSessionEventKind.DeltaRejected,
                LiveAuthoringSessionRejection.OverlayBudget,
                delta.Sequence,
                delta.CorrelationId,
                exception.Message,
                countAsDelta: true,
                out pending);
        }

        _lastAcceptedSequence = delta.Sequence;
        _replayLedger.Record(delta.Sequence, fingerprint);
        if (!isLocalEcho)
        {
            pending = default;
            return default;
        }

        _overlay = candidate;
        _lastAppliedSequence = delta.Sequence;
        _loopSuppressedDeltaCount++;
        LiveAuthoringSessionStatus suppressed = CreateStatusLocked();
        pending = new PendingReport(
            LiveAuthoringSessionEventKind.LoopSuppressed,
            suppressed.State,
            suppressed,
            delta.CorrelationId,
            "The delta carries the local origin identifier and was suppressed as an echo. Its remote " +
            "sequence is still consumed so the stream stays contiguous.");
        return new LiveAuthoringSessionResult(
            LiveAuthoringSessionOutcome.LoopSuppressed,
            LiveAuthoringSessionRejection.None,
            delta.Sequence,
            suppressed.State,
            suppressed.LastAcceptedSequence,
            suppressed.LastAppliedSequence,
            delta.CorrelationId,
            Detail: null);
    }

    private LiveAuthoringOverlayModel BuildOverlay(IReadOnlyList<LiveStageUpdate> updates)
    {
        var overlay = new LiveAuthoringOverlayModel(BridgeRootPath);
        foreach (LiveStageUpdate update in updates)
        {
            overlay.Apply(update);
        }
        return overlay;
    }

    private long NextBatchSequence() => Interlocked.Increment(ref _nextBatchSequence);

    private async ValueTask<SubmitOutcome> SubmitAsync(
        LiveAuthoringBatch batch,
        CancellationToken cancellationToken)
    {
        LiveAuthoringAdmissionReceipt receipt;
        try
        {
            using CancellationTokenSource linked =
                CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
            receipt = await _sink.ApplyAsync(batch, linked.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (ObjectDisposedException exception)
        {
            return new SubmitOutcome(exception, Fatal: true);
        }
        catch (Exception exception)
        {
            return new SubmitOutcome(exception, Fatal: false);
        }

        try
        {
            // Deliberately not cancellable: the batch is already ordered work that will reach the
            // stage, so abandoning the wait would let the overlay model and the stage diverge.
            await receipt.Applied.ConfigureAwait(false);
            return default;
        }
        catch (Exception exception)
        {
            return new SubmitOutcome(exception, Fatal: false);
        }
    }

    private void EnterResyncRequiredLocked(string? detail)
    {
        if (_state != LiveAuthoringSessionState.ResyncRequired)
        {
            _resyncRequiredCount++;
        }
        _state = LiveAuthoringSessionState.ResyncRequired;
        _lastFailureDetail = Truncate(detail);
    }

    private void ClearSessionLocked()
    {
        _epoch = null;
        _lastAcceptedSequence = 0;
        _lastAppliedSequence = 0;
        _overlay = new LiveAuthoringOverlayModel(BridgeRootPath);
        _replayLedger.Clear();
        _state = LiveAuthoringSessionState.Disconnected;
    }

    private LiveAuthoringSessionResult RejectLocked(
        LiveAuthoringSessionEventKind kind,
        LiveAuthoringSessionRejection rejection,
        long sequence,
        string? correlationId,
        string? detail,
        bool countAsDelta,
        out PendingReport report)
    {
        if (countAsDelta)
        {
            _rejectedDeltaCount++;
        }
        _lastFailureDetail = Truncate(detail);
        LiveAuthoringSessionStatus status = CreateStatusLocked();
        report = new PendingReport(kind, status.State, status, correlationId, detail);
        return new LiveAuthoringSessionResult(
            LiveAuthoringSessionOutcome.Rejected,
            rejection,
            sequence,
            status.State,
            status.LastAcceptedSequence,
            status.LastAppliedSequence,
            correlationId,
            Truncate(detail));
    }

    private LiveAuthoringSessionStatus CreateStatusLocked() =>
        new(
            _state,
            _epoch?.RemoteOriginId,
            _epoch?.SessionId,
            _epoch?.Epoch ?? 0,
            _lastAcceptedSequence,
            _lastAppliedSequence,
            _appliedSnapshotCount,
            _appliedDeltaCount,
            _duplicateDeltaCount,
            _rejectedDeltaCount,
            _loopSuppressedDeltaCount,
            _resyncRequiredCount,
            _overlay.PrimCount,
            _overlay.UpdateCount,
            _replayLedger.WindowLength,
            _replayLedger.Count,
            _replayLedger.EstimatedBytes,
            _replayLedger.OldestRetainedSequence,
            _lastFailureDetail,
            DateTimeOffset.UtcNow,
            Volatile.Read(ref _observerFailureCount),
            _lastObserverFailureDetail);

    /// <summary>
    /// Reports an event captured while <see cref="_stateGate"/> was held. A caller-supplied observer is
    /// untrusted code and must never run inside the lock, so every locked path captures the event and
    /// this overload delivers it after the lock is released.
    /// </summary>
    private void Report(PendingReport report) =>
        Report(
            report.Kind,
            report.PreviousState,
            report.Status,
            report.CorrelationId,
            report.Detail);

    private void Report(
        LiveAuthoringSessionEventKind kind,
        LiveAuthoringSessionState previous,
        LiveAuthoringSessionStatus status,
        string? correlationId,
        string? detail)
    {
        if (_observer is null)
        {
            return;
        }

        // A caller-supplied IProgress<T> is untrusted code. It must never be able to change acceptance,
        // rejection, or resync semantics, so every exception it throws is isolated, counted, and
        // recorded here rather than propagated to the adapter that submitted the message.
        try
        {
            _observer.Report(new LiveAuthoringSessionEvent(
                kind,
                previous,
                status.State,
                status.RemoteOriginId,
                status.SessionId,
                status.Epoch,
                status.LastAcceptedSequence,
                status.LastAppliedSequence,
                correlationId,
                DateTimeOffset.UtcNow,
                Truncate(detail)));
        }
        catch (Exception exception)
        {
            Interlocked.Increment(ref _observerFailureCount);
            lock (_stateGate)
            {
                _lastObserverFailureDetail = Truncate(exception.Message);
            }
        }
    }

    private static string DescribeRejection(LiveAuthoringSessionRejection rejection) =>
        rejection switch
        {
            LiveAuthoringSessionRejection.SessionState =>
                "The session state does not accept this message.",
            LiveAuthoringSessionRejection.ResyncRequired =>
                "The session requires a full snapshot before it accepts deltas again.",
            LiveAuthoringSessionRejection.RemoteOrigin =>
                "The message came from an origin other than the authoritative remote origin.",
            LiveAuthoringSessionRejection.SessionIdentity =>
                "The message belongs to a different remote session identifier.",
            LiveAuthoringSessionRejection.EpochRetired =>
                "The message belongs to a retired epoch.",
            LiveAuthoringSessionRejection.EpochAdvanced =>
                "The message belongs to a newer epoch and requires a full snapshot first.",
            LiveAuthoringSessionRejection.SequenceGap =>
                "The message skipped one or more sequences and requires a full snapshot.",
            LiveAuthoringSessionRejection.BridgeScope =>
                "The message targets prims outside the bridge-owned overlay root.",
            LiveAuthoringSessionRejection.OverlayBudget =>
                "The message would push the bridge-owned overlay past its bounds.",
            LiveAuthoringSessionRejection.DuplicateConflict =>
                "The message reuses a retained sequence with different content and requires a full " +
                "snapshot.",
            LiveAuthoringSessionRejection.ReplayExpired =>
                "The message replays a sequence outside the retained replay window and cannot be " +
                "proven to be a duplicate, so it requires a full snapshot.",
            LiveAuthoringSessionRejection.ApplyFailed =>
                "The message failed while applying and requires a full snapshot.",
            _ => null
        } ?? "The message was rejected.";

    private static string? Truncate(string? detail)
    {
        if (detail is null || detail.Length <= MaxDetailLength)
        {
            return detail;
        }
        return string.Concat(detail.AsSpan(0, MaxDetailLength), "\u2026");
    }

    private async ValueTask<GateScope> EnterAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposeState) != 0, this);
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            using CancellationTokenSource linked =
                CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
            await _gate.WaitAsync(linked.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new ObjectDisposedException(nameof(LiveAuthoringSessionCoordinator));
        }
        catch (ObjectDisposedException)
        {
            throw new ObjectDisposedException(nameof(LiveAuthoringSessionCoordinator));
        }

        return new GateScope(_gate);
    }

    private readonly record struct SubmitOutcome(Exception? Failure, bool Fatal);

    private readonly record struct PendingReport(
        LiveAuthoringSessionEventKind Kind,
        LiveAuthoringSessionState PreviousState,
        LiveAuthoringSessionStatus Status,
        string? CorrelationId,
        string? Detail)
    {
        /// <summary>
        /// Gets whether a locked path captured an event. The default value carries the default status,
        /// whose timestamp is never set by a real capture, so the timestamp is the discriminator.
        /// </summary>
        internal bool HasReport => Status.TimestampUtc != default;
    }

    private readonly struct GateScope(SemaphoreSlim gate) : IDisposable
    {
        public void Dispose() => gate.Release();
    }
}
