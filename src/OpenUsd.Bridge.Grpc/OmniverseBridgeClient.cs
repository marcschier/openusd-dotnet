// Copyright (c) marcschier. Licensed under the MIT License.

using System.Globalization;
using System.Threading.Channels;
using Google.Protobuf;
using Grpc.Core;
using OpenUsd.Bridge.Protocol;
using OpenUsd.Bridge.Protocol.Wire;
using OpenUsd.LiveAuthoring;

namespace OpenUsd.Bridge.Grpc;

/// <summary>
/// Connects one <see cref="LiveAuthoringSessionCoordinator"/> to a peer that speaks
/// <c>openusd.bridge.v1</c> over gRPC.
/// </summary>
/// <remarks>
/// <para>
/// The client owns the transport and nothing else. Duplicate, gap, conflict, epoch, loop, and
/// overlay-budget semantics stay with the coordinator: this adapter decodes a frame, hands it over,
/// and reports the coordinator's verdict back to the peer. There is no merge engine here, and there
/// is deliberately no second copy of the ordering rules that could disagree with the first.
/// </para>
/// <para>
/// The connection is a small explicit state machine.
/// <see cref="BridgeConnectionState.Connecting"/> establishes a transport,
/// <see cref="BridgeConnectionState.Negotiating"/> agrees a version, capabilities, and limits
/// before any mutation is attempted, <see cref="BridgeConnectionState.Resynchronizing"/> takes a
/// bounded full snapshot, and <see cref="BridgeConnectionState.Streaming"/> applies ordered deltas.
/// Any transport failure returns to <see cref="BridgeConnectionState.Backoff"/> with bounded
/// exponential backoff and full jitter; any lost baseline returns to
/// <see cref="BridgeConnectionState.Resynchronizing"/> without dropping the connection.
/// </para>
/// <para>
/// Local edits travel outward only through <see cref="PublishLocalBatchAsync"/>. The client never
/// observes the stage, subscribes to a change feed, or synthesizes an edit: the host publishes
/// batches it already owns, the bounded channel refuses rather than grows when the peer is slow,
/// and every queued batch gets an eventual <see cref="BridgeLocalPublicationResult"/> instead of
/// only a queue receipt.
/// </para>
/// <para>
/// The negotiated capabilities and effective limits govern the whole session in both directions. A
/// capability the peers did not both advertise is never used and never accepted, and a message past
/// the negotiated bounds — in updates or in encoded bytes — is refused even when it would fit
/// inside the local bounds. Those bounds belong to one connection: when it ends they are cleared,
/// so a status never describes a session that is no longer negotiated, and a batch queued while
/// disconnected is re-checked against whatever the next handshake actually agrees.
/// </para>
/// </remarks>
public sealed class OmniverseBridgeClient : IAsyncDisposable
{
    private const int MaxSnapshotAttemptsPerConnection = 3;

    /// <summary>The one reason disposal gives, both to the drain and to a racing admission.</summary>
    private const string DisposedDetail =
        "The client was disposed before the batch was published.";

    /// <summary>
    /// How many times admission re-authorizes a batch when the negotiated session changes
    /// underneath it. The last attempt admits rather than looping: the publish pump re-authorizes
    /// every batch immediately before it is sent, so a session that keeps churning delays a verdict
    /// instead of spinning here.
    /// </summary>
    private const int MaxAdmissionAttempts = 3;

    private readonly LiveAuthoringSessionCoordinator _coordinator;
    private readonly BridgeConnectionFactory _connectionFactory;
    private readonly BridgeClientOptions _options;
    private readonly Channel<BridgePendingPublication> _outbound;

    /// <summary>
    /// Batches taken out of the channel that still need publishing, newest-failed first.
    /// </summary>
    /// <remarks>
    /// A linked list rather than a queue because a failed attempt goes back to the <em>head</em>:
    /// local publications are an ordered sequence, and putting a failed batch behind newer ones
    /// would invert the order the host authored them in.
    /// </remarks>
    private readonly LinkedList<BridgePendingPublication> _retry = new();
    private readonly CancellationTokenSource _lifetime = new();
    private readonly object _stateGate = new();
    private readonly Random _jitter = new();
    private BridgeCapability[] _capabilities = [];
    private long _connectAttemptCount;
    private long _deltaAppliedCount;
    private int _disposed;
    private long _duplicateBatchCount;
    private BridgeLimits _effectiveLimits;
    private long _epoch;
    private string? _lastFailureDetail;
    private string? _lastObserverFailureDetail;
    private bool _negotiated;
    private long _observerFailureCount;
    private BridgeProtocolVersion _peerVersion;

    /// <summary>
    /// The one retention counter: batches waiting in the channel plus batches held for retry.
    /// </summary>
    /// <remarks>
    /// Counting the two structures separately would let a reconnect double the retention a host
    /// configured, because a batch pulled out of the channel frees a channel slot while still being
    /// held. One counter, incremented at admission and decremented exactly once at completion,
    /// keeps <see cref="BridgeClientOptions.OutboundQueueCapacity"/> the total bound it claims to be.
    /// </remarks>
    private int _pendingCount;
    private long _protocolRejectionCount;
    private long _publishedBatchCount;
    private long _reconnectCount;
    private long _refusedBatchCount;
    private long _resyncCount;
    private Task _run = Task.CompletedTask;
    private int _running;
    private string? _sessionId;

    /// <summary>
    /// Counts every adoption and release of a negotiated session, so admission can tell that the
    /// session it authorized a batch against is still the session it is about to queue it for.
    /// </summary>
    private long _sessionGeneration;
    private long _snapshotAppliedCount;
    private BridgeConnectionState _state = BridgeConnectionState.Disconnected;

    /// <summary>
    /// The outcome every later admission is answered with once the loop or the client has ended,
    /// or <see langword="null"/> while publications may still be queued.
    /// </summary>
    /// <remarks>
    /// This is what closes the window between admission and a terminal drain. The flag is set under
    /// <see cref="_stateGate"/> before the drain runs, and admission reserves, constructs, and
    /// writes a batch under the same lock, so a batch is either queued before the drain — and
    /// therefore drained — or refused after it. It can never be accepted into a queue nothing will
    /// ever read again.
    /// </remarks>
    private BridgeLocalPublicationOutcome? _terminalOutcome;
    private string? _terminalDetail;

    /// <summary>Initializes a client over a real gRPC transport.</summary>
    /// <param name="coordinator">The session coordinator that owns recovery semantics.</param>
    /// <param name="options">The bounded transport and security configuration.</param>
    public OmniverseBridgeClient(
        LiveAuthoringSessionCoordinator coordinator,
        BridgeClientOptions options)
        : this(coordinator, options, connectionFactory: null)
    {
    }

    internal OmniverseBridgeClient(
        LiveAuthoringSessionCoordinator coordinator,
        BridgeClientOptions options,
        BridgeConnectionFactory? connectionFactory)
    {
        ArgumentNullException.ThrowIfNull(coordinator);
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();
        if (!string.Equals(options.BridgeRootPath, coordinator.BridgeRootPath, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "The client's bridge root path must match the coordinator's; otherwise the peer " +
                "negotiates a root the coordinator will reject on the first snapshot.",
                nameof(options));
        }
        if (options.LocalOriginId is string configuredOrigin &&
            !string.Equals(configuredOrigin, coordinator.LocalOriginId, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "The client's local origin identifier must match the coordinator's; otherwise an " +
                "echo of a local edit is reapplied instead of being suppressed.",
                nameof(options));
        }

        _coordinator = coordinator;
        _options = options;
        // One identity, resolved once. Leaving BridgeClientOptions.LocalOriginId unset adopts the
        // coordinator's resolved identifier rather than a literal both sides happen to share, so the
        // handshake advertises exactly the origin the coordinator suppresses echoes for and exactly
        // the origin every derived idempotency key names.
        LocalOriginId = coordinator.LocalOriginId;
        _connectionFactory = connectionFactory ??
            (_ => ValueTask.FromResult(BridgeChannelFactory.Create(options)));
        _outbound = Channel.CreateBounded<BridgePendingPublication>(
            new BoundedChannelOptions(options.OutboundQueueCapacity)
            {
                FullMode = BoundedChannelFullMode.Wait,
                SingleReader = true,
                SingleWriter = false
            });
    }

    /// <summary>Gets the current connection state.</summary>
    public BridgeConnectionState State
    {
        get
        {
            lock (_stateGate)
            {
                return _state;
            }
        }
    }

    /// <summary>
    /// Gets the opaque origin identifier this client publishes under, which is always the
    /// coordinator's own <see cref="LiveAuthoringSessionCoordinator.LocalOriginId"/>.
    /// </summary>
    /// <remarks>
    /// A host builds its <see cref="BridgeLocalBatch"/> instances with this identifier. A batch that
    /// names any other origin is refused rather than sent: it would be echoed back under an origin
    /// the coordinator does not recognize as its own and reapplied as if it were a remote edit, and
    /// its derived idempotency key would name a publisher this session does not have.
    /// </remarks>
    public string LocalOriginId { get; }

    /// <summary>Returns a bounded, redacted view of connection health.</summary>
    public BridgeClientStatus GetStatus()
    {
        lock (_stateGate)
        {
            return new BridgeClientStatus(
                _state,
                _negotiated,
                _peerVersion,
                _connectAttemptCount,
                _reconnectCount,
                _snapshotAppliedCount,
                _deltaAppliedCount,
                _protocolRejectionCount,
                _resyncCount,
                _publishedBatchCount,
                _duplicateBatchCount,
                _refusedBatchCount,
                _pendingCount,
                _sessionId,
                _epoch,
                _effectiveLimits,
                Array.AsReadOnly(_capabilities),
                DateTimeOffset.UtcNow,
                _lastFailureDetail,
                _observerFailureCount,
                _lastObserverFailureDetail);
        }
    }

    /// <summary>
    /// Runs the connect, negotiate, resync, and stream loop until <paramref name="cancellationToken"/>
    /// is cancelled, the client is disposed, or the client faults for a reason a retry cannot fix.
    /// </summary>
    /// <remarks>
    /// One client runs one loop. A second concurrent call throws instead of opening a second
    /// transport onto the same coordinator, which would let two connections race snapshots and
    /// deltas into one session.
    /// </remarks>
    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        if (Interlocked.CompareExchange(ref _running, 1, 0) != 0)
        {
            throw new InvalidOperationException(
                "The client is already running. One client owns one connection loop.");
        }

        using var scope =
            CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
        Task run = RunLoopAsync(scope.Token);
        lock (_stateGate)
        {
            _run = run;
        }

        try
        {
            await run.ConfigureAwait(false);
        }
        finally
        {
            Volatile.Write(ref _running, 0);
        }
    }

    /// <summary>
    /// Queues one already-authoritative local batch for publication and returns its receipt.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <see cref="BridgeLocalPublicationReceipt.Accepted"/> answers only whether the bounded channel
    /// took the batch. <see cref="BridgeLocalPublicationReceipt.Published"/> carries the eventual
    /// outcome, including a peer refusal, a protocol violation in the peer's answer, a retired
    /// epoch, an exhausted bounded retry, or disposal.
    /// </para>
    /// <para>
    /// Admission is atomic with respect to the session and to the client's own end. The batch is
    /// authorized against the session that is negotiated now, and the reservation, the receipt, and
    /// the channel write all happen under one lock that also rechecks that the session did not
    /// change and that the client has not ended. A batch admitted here is therefore always visible
    /// to whatever drains the queue — a pump, a reconnect, a fault, or disposal — instead of
    /// landing in a queue that has already been drained for the last time.
    /// </para>
    /// </remarks>
    public ValueTask<BridgeLocalPublicationReceipt> PublishLocalBatchAsync(
        BridgeLocalBatch batch,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(batch);
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        cancellationToken.ThrowIfCancellationRequested();

        for (int attempt = 1; ; attempt++)
        {
            if (!TryAuthorizeOutbound(batch, out string refusal, out long generation))
            {
                return ValueTask.FromResult(RefuseLocalBatch(
                    batch,
                    BridgeLocalPublicationOutcome.NotPermitted,
                    refusal));
            }

            // A test seam, and the only place one exists on this path: a race between admission and
            // a terminal drain cannot be reproduced deterministically without a controllable pause
            // between the authorization and the reservation it must stay consistent with.
            AdmissionBarrier?.Invoke();

            BridgeAdmission admission = TryAdmit(
                batch,
                generation,
                allowRetry: attempt < MaxAdmissionAttempts,
                out BridgePendingPublication? pending,
                out BridgeLocalPublicationOutcome outcome,
                out string detail);
            switch (admission)
            {
                case BridgeAdmission.Admitted:
                    return ValueTask.FromResult(pending!.Receipt);
                case BridgeAdmission.SessionChanged:
                    continue;
                default:
                    return ValueTask.FromResult(RefuseLocalBatch(batch, outcome, detail));
            }
        }
    }

    /// <summary>
    /// Reserves retention for one batch and writes it to the bounded channel, or says why it could
    /// not, with the session and terminal state rechecked under the same lock.
    /// </summary>
    private BridgeAdmission TryAdmit(
        BridgeLocalBatch batch,
        long generation,
        bool allowRetry,
        out BridgePendingPublication? pending,
        out BridgeLocalPublicationOutcome outcome,
        out string detail)
    {
        pending = null;
        lock (_stateGate)
        {
            if (_terminalOutcome is BridgeLocalPublicationOutcome terminal)
            {
                // Nothing will read the queue again, so accepting the batch would hand the caller a
                // receipt that stays pending forever.
                outcome = terminal;
                detail = _terminalDetail ?? "The client stopped before the batch was published.";
                return BridgeAdmission.Refused;
            }
            if (allowRetry && _sessionGeneration != generation)
            {
                // The session changed between the authorization and this reservation, so the check
                // that just passed described a session this batch will not travel on.
                outcome = default;
                detail = string.Empty;
                return BridgeAdmission.SessionChanged;
            }

            // One bound covers the channel and the retry list together, so a reconnect that holds
            // batches for retry cannot quietly double what the host configured.
            if (_pendingCount >= _options.OutboundQueueCapacity)
            {
                outcome = BridgeLocalPublicationOutcome.Refused;
                detail = "The bounded outbound queue is full.";
                return BridgeAdmission.Refused;
            }

            var admitted = new BridgePendingPublication(batch);
            if (!_outbound.Writer.TryWrite(admitted))
            {
                outcome = BridgeLocalPublicationOutcome.Refused;
                detail = "The bounded outbound channel refused the batch.";
                return BridgeAdmission.Refused;
            }

            _pendingCount++;
            pending = admitted;
            outcome = default;
            detail = string.Empty;
            return BridgeAdmission.Admitted;
        }
    }

    /// <summary>Gets or sets a test-only pause between outbound authorization and admission.</summary>
    internal Action? AdmissionBarrier { get; set; }


    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        // The terminal answer is installed first, under the same lock admission reserves under, and
        // strictly before the channel is completed or the loop is awaited. Without that ordering
        // there is a window in which admission sees no terminal state, finds the channel completed
        // or the retention bound still full, and answers a caller with Refused — backpressure,
        // which invites a retry — for a client that is in fact gone. Installing it first makes every
        // admission that races disposal either land in the queue the drain below empties, or be
        // refused with the outcome that is actually true.
        lock (_stateGate)
        {
            // First answer wins: a loop that already faulted must keep the reason it gave.
            _terminalOutcome ??= BridgeLocalPublicationOutcome.Cancelled;
            _terminalDetail ??= DisposedDetail;
        }

        // Cancelling the lifetime is what stops the loop. Without it a disposed client keeps
        // reconnecting, keeps applying snapshots into a coordinator the host believes it released,
        // and keeps presenting credentials.
        await _lifetime.CancelAsync().ConfigureAwait(false);
        _outbound.Writer.TryComplete();

        Task run;
        lock (_stateGate)
        {
            run = _run;
        }

        try
        {
            await run.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Cancellation is how the loop ends; it is not a disposal failure.
        }
        catch (Exception exception) when (exception is not OutOfMemoryException
            and not StackOverflowException)
        {
            // Disposal is the last chance every queued batch has to be answered, so a loop that
            // ended unexpectedly must not also skip the cleanup below. The failure is recorded
            // rather than rethrown: a host disposing a client cannot act on it, and throwing here
            // would leave the lifetime source and the receipts behind.
            RecordFailure(
                $"The connection loop ended with {exception.GetType().Name} before disposal.");
        }

        CompleteAllPending(
            BridgeLocalPublicationOutcome.Cancelled,
            DisposedDetail,
            terminal: true);
        TransitionTo(BridgeConnectionState.Disconnected);
        _lifetime.Dispose();
    }

    private async Task RunLoopAsync(CancellationToken cancellationToken)
    {
        TimeSpan backoff = _options.InitialBackoff;
        lock (_stateGate)
        {
            // A previous run may have ended terminally. This one can carry batches again, so the
            // refusal it recorded stops applying the moment a loop exists to drain them — unless
            // the client is being disposed, whose terminal answer is final and must not be cleared
            // by a loop that started at the same moment.
            if (Volatile.Read(ref _disposed) == 0)
            {
                _terminalOutcome = null;
                _terminalDetail = null;
            }
        }

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                BridgeConnectionOutcome outcome;
                try
                {
                    outcome = await RunConnectionAsync(cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    break;
                }
                catch (BridgeProtocolException exception)
                {
                    // A codec defect on this connection is not a reason to fault the client. It is
                    // counted as a protocol rejection and the connection is retried under backoff,
                    // exactly like a peer that answered something the contract does not allow.
                    CountProtocolRejection(exception.Error);
                    outcome = BridgeConnectionOutcome.Transient;
                }

                if (outcome == BridgeConnectionOutcome.Fatal)
                {
                    CompleteAllPending(
                        BridgeLocalPublicationOutcome.NotPermitted,
                        "The client faulted before the batch could be published.",
                        terminal: true);
                    return;
                }
                if (cancellationToken.IsCancellationRequested)
                {
                    break;
                }

                BridgeConnectionState previous = TransitionTo(BridgeConnectionState.Backoff);
                TimeSpan delay = NextBackoff(backoff);
                Report(
                    BridgeClientEventKind.Backoff,
                    previous,
                    detail: string.Create(
                        CultureInfo.InvariantCulture,
                        $"Reconnecting after {delay.TotalMilliseconds:F0} ms."));
                try
                {
                    await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }

                backoff = backoff >= _options.MaxBackoff
                    ? _options.MaxBackoff
                    : TimeSpan.FromTicks(Math.Min(backoff.Ticks * 2, _options.MaxBackoff.Ticks));
                lock (_stateGate)
                {
                    _reconnectCount++;
                }
            }
        }
        catch (Exception exception) when (exception is not OutOfMemoryException
            and not StackOverflowException)
        {
            // Defensive only. A loop that faults would otherwise leave every queued batch waiting
            // on a receipt that never completes and a host awaiting a task that only ever throws.
            // The client stops, says why, and answers what it was holding.
            RecordFailure($"The connection loop ended with {exception.GetType().Name}.");
            BridgeConnectionState beforeFault = TransitionTo(BridgeConnectionState.Faulted);
            Report(
                BridgeClientEventKind.Faulted,
                beforeFault,
                detail: "The connection loop ended unexpectedly.");
            CompleteAllPending(
                BridgeLocalPublicationOutcome.NotPermitted,
                "The client faulted before the batch could be published.",
                terminal: true);
            return;
        }

        BridgeConnectionState stopping = TransitionTo(BridgeConnectionState.Disconnected);
        CompleteAllPending(
            BridgeLocalPublicationOutcome.Cancelled,
            "The client stopped before the batch was published.",
            terminal: true);
        Report(BridgeClientEventKind.Stopped, stopping, detail: "The client stopped.");
    }

    private async ValueTask<BridgeConnectionOutcome> RunConnectionAsync(
        CancellationToken cancellationToken)
    {
        lock (_stateGate)
        {
            _connectAttemptCount++;
        }

        BridgeConnectionState previous = TransitionTo(BridgeConnectionState.Connecting);
        Report(
            BridgeClientEventKind.Connecting,
            previous,
            detail: "Establishing the bridge transport.");

        BridgeConnection? connection = null;
        try
        {
            connection = await _connectionFactory(cancellationToken).ConfigureAwait(false);
            var client = new LiveBridge.LiveBridgeClient(connection.Invoker);

            previous = TransitionTo(BridgeConnectionState.Negotiating);
            BridgeNegotiationResult negotiation =
                await NegotiateAsync(client, cancellationToken).ConfigureAwait(false);
            if (!negotiation.Accepted)
            {
                RecordFailure(negotiation.Detail);
                Report(
                    BridgeClientEventKind.NegotiationRejected,
                    previous,
                    detail: $"{negotiation.Rejection}: {negotiation.Detail}");
                if (IsFatal(negotiation.Rejection))
                {
                    BridgeConnectionState beforeFault = TransitionTo(BridgeConnectionState.Faulted);
                    Report(
                        BridgeClientEventKind.Faulted,
                        beforeFault,
                        detail: "Negotiation failed for a reason a retry cannot fix.");
                    return BridgeConnectionOutcome.Fatal;
                }

                return BridgeConnectionOutcome.Transient;
            }

            LiveAuthoringRemoteEpoch epoch = negotiation.Epoch!;
            AdoptSession(negotiation, epoch);
            Report(
                BridgeClientEventKind.Negotiated,
                previous,
                detail: $"Negotiated protocol {negotiation.PeerVersion} for session {epoch.SessionId}.");

            try
            {
                await _coordinator.ConnectAsync(epoch, cancellationToken).ConfigureAwait(false);
            }
            catch (ArgumentOutOfRangeException)
            {
                // The peer came back on an epoch older than the one the coordinator is bound to.
                // Sequences are only comparable inside one epoch, so this session cannot be joined;
                // it is refused and retried under backoff rather than crashing the loop.
                CountProtocolRejection(BridgeWireError.Create(
                    BridgeWireErrorCode.FieldOutOfRange,
                    "The peer negotiated an epoch older than the one the coordinator holds."));
                return BridgeConnectionOutcome.Transient;
            }
            catch (InvalidOperationException exception)
            {
                // The coordinator is stopping or already disposed. Reconnecting cannot change that.
                RecordFailure($"The coordinator refused the session: {exception.Message}");
                BridgeConnectionState beforeFault = TransitionTo(BridgeConnectionState.Faulted);
                Report(
                    BridgeClientEventKind.Faulted,
                    beforeFault,
                    detail: "The coordinator can no longer bind a remote epoch.");
                return BridgeConnectionOutcome.Fatal;
            }

            DrainStaleOutbound(epoch);

            using var connectionScope =
                CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            Task publishPump = Task.Run(
                () => PublishPumpAsync(client, epoch, connectionScope).AsTask(),
                CancellationToken.None);
            try
            {
                return await StreamAsync(client, epoch, connectionScope.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                // The publish pump ended this connection deliberately, so the session can
                // renegotiate and take a fresh baseline. The client itself was not cancelled.
                return BridgeConnectionOutcome.Transient;
            }
            finally
            {
                await connectionScope.CancelAsync().ConfigureAwait(false);
                try
                {
                    await publishPump.ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    // Expected: the pump is cancelled with the connection it belonged to.
                }
            }
        }
        catch (RpcException exception)
        {
            return HandleRpcFailure(exception);
        }
        catch (HttpRequestException exception)
        {
            RecordFailure($"The transport could not be established: {exception.StatusCode}.");
            return BridgeConnectionOutcome.Transient;
        }
        catch (IOException)
        {
            RecordFailure("The transport failed while a message was in flight.");
            return BridgeConnectionOutcome.Transient;
        }
        finally
        {
            // The negotiated session belongs to the connection that agreed it. Clearing it here
            // means a status can never describe an active session while the client is backing off,
            // and a batch queued in the meantime is re-checked against whatever the next handshake
            // agrees rather than against a session that no longer exists.
            ClearSession();
            connection?.Dispose();
        }
    }

    private async ValueTask<BridgeNegotiationResult> NegotiateAsync(
        LiveBridge.LiveBridgeClient client,
        CancellationToken cancellationToken)
    {
        // The configured request is what the operator (or the host) chose. It is only ever a
        // request: whatever the peer answers with becomes the adopted session, and that adopted
        // identifier - not this one - is what the stream handshake later carries.
        var request = BridgeHandshakeRequest.CreateLocal(
            LocalOriginId,
            _options.BridgeRootPath,
            _options.RequestedSessionId);
        HandshakeResponse wire = await client
            .NegotiateAsync(
                BridgeMessageCodec.ToWire(request),
                await CreateCallOptionsAsync(cancellationToken).ConfigureAwait(false))
            .ResponseAsync
            .ConfigureAwait(false);

        if (!BridgeMessageCodec.TryFromWire(
            wire,
            out BridgeHandshakeResponse? response,
            out BridgeWireError error))
        {
            CountProtocolRejection(error);
            return CreateRejectedNegotiation(error);
        }

        return BridgeNegotiator.Evaluate(request, response!);
    }

    private async ValueTask<BridgeConnectionOutcome> StreamAsync(
        LiveBridge.LiveBridgeClient client,
        LiveAuthoringRemoteEpoch epoch,
        CancellationToken cancellationToken)
    {
        using AsyncDuplexStreamingCall<ChangeStreamRequest, ChangeStreamMessage> call =
            client.StreamChanges(await CreateStreamOptionsAsync(cancellationToken).ConfigureAwait(false));

        await call.RequestStream
            .WriteAsync(
                new ChangeStreamRequest
                {
                    // The stream rejoins the session the unary handshake actually adopted. When
                    // the peer honoured BridgeClientOptions.RequestedSessionId these are the same
                    // identifier; when it did not, following the peer is the only correct choice,
                    // because asking to rejoin a session the peer never created would either be
                    // refused or, worse, silently attach to someone else's.
                    Handshake = BridgeMessageCodec.ToWire(
                        BridgeHandshakeRequest.CreateLocal(
                            LocalOriginId,
                            _options.BridgeRootPath,
                            epoch.SessionId))
                },
                cancellationToken)
            .ConfigureAwait(false);

        if (!await ResyncAsync(
            client,
            epoch,
            BridgeResyncReason.Initial,
            cancellationToken).ConfigureAwait(false))
        {
            return BridgeConnectionOutcome.Transient;
        }

        while (await call.ResponseStream.MoveNext(cancellationToken).ConfigureAwait(false))
        {
            ChangeStreamMessage message = call.ResponseStream.Current;

            // The size bound is checked on the wire message, before decoding, so an oversized frame
            // is refused without being turned into authoring values first.
            if (!TryAuthorizeInboundMessage(
                message,
                DescribeFrame(message),
                CountUpdates(message),
                out BridgeWireError oversized))
            {
                CountProtocolRejection(oversized);
                continue;
            }

            if (!BridgeWireCodec.TryFromWire(
                message,
                out BridgeStreamFrame? frame,
                out BridgeWireError error))
            {
                CountProtocolRejection(error);
                continue;
            }

            if (!await HandleFrameAsync(
                client,
                call,
                epoch,
                frame!,
                cancellationToken).ConfigureAwait(false))
            {
                return BridgeConnectionOutcome.Transient;
            }
        }

        RecordFailure("The peer closed the change stream.");
        return BridgeConnectionOutcome.Transient;
    }

    private static string DescribeFrame(ChangeStreamMessage message) => message.MessageCase switch
    {
        ChangeStreamMessage.MessageOneofCase.Snapshot => "snapshot",
        ChangeStreamMessage.MessageOneofCase.Delta => "delta",
        _ => "frame"
    };

    private static int CountUpdates(ChangeStreamMessage message) => message.MessageCase switch
    {
        ChangeStreamMessage.MessageOneofCase.Snapshot => message.Snapshot.Updates.Count,
        ChangeStreamMessage.MessageOneofCase.Delta => message.Delta.Updates.Count,
        _ => 0
    };

    private async ValueTask<bool> HandleFrameAsync(
        LiveBridge.LiveBridgeClient client,
        AsyncDuplexStreamingCall<ChangeStreamRequest, ChangeStreamMessage> call,
        LiveAuthoringRemoteEpoch epoch,
        BridgeStreamFrame frame,
        CancellationToken cancellationToken)
    {
        switch (frame.Kind)
        {
            case BridgeStreamFrameKind.Snapshot:
                return TryAuthorizeEpoch(frame.Snapshot.Epoch, epoch, "snapshot") &&
                    await HandleSnapshotFrameAsync(client, call, epoch, frame, cancellationToken)
                        .ConfigureAwait(false);

            case BridgeStreamFrameKind.Delta:
                return TryAuthorizeEpoch(frame.Delta.Epoch, epoch, "delta") &&
                    await HandleDeltaFrameAsync(client, call, epoch, frame, cancellationToken)
                        .ConfigureAwait(false);

            case BridgeStreamFrameKind.ResyncRequired:
                return TryAuthorizeEpoch(frame.Epoch, epoch, "resync demand") &&
                    await ResyncAsync(
                        client,
                        epoch,
                        frame.ResyncReason,
                        cancellationToken).ConfigureAwait(false);

            case BridgeStreamFrameKind.KeepAlive:
                await WriteKeepAliveAsync(call, cancellationToken).ConfigureAwait(false);
                return true;

            case BridgeStreamFrameKind.SessionEvent:
            case BridgeStreamFrameKind.SessionStatus:
            case BridgeStreamFrameKind.Acknowledgement:
            case BridgeStreamFrameKind.Handshake:
                // Peer-side telemetry and echoes. They carry no authority over the local session,
                // so they are observed and not applied.
                return true;

            default:
                CountProtocolRejection(BridgeWireError.Create(
                    BridgeWireErrorCode.UnknownStreamFrame,
                    $"The frame kind '{frame.Kind}' is not handled by this client."));
                return true;
        }
    }

    /// <summary>
    /// Requires an inbound message to belong to exactly the epoch this connection negotiated.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A connection agrees one epoch together with one capability set and one limit set, and those
    /// three are a package. Accepting a message from a newer epoch on the old connection would
    /// apply it under a negotiation that never covered it, while the client's session identity,
    /// outbound authorization, and reported status still described the old one.
    /// </para>
    /// <para>
    /// So a newer epoch is not adopted in band: the connection is restarted and the whole handshake
    /// runs again, which is the only place an epoch, its capabilities, and its limits are agreed
    /// together. An older epoch, or a different origin or session identifier, is a protocol
    /// violation — the peer is describing a session this connection is not part of — and is counted
    /// as one before the connection restarts.
    /// </para>
    /// </remarks>
    private bool TryAuthorizeEpoch(
        LiveAuthoringRemoteEpoch? incoming,
        LiveAuthoringRemoteEpoch negotiated,
        string messageKind)
    {
        if (incoming is null)
        {
            CountProtocolRejection(BridgeWireError.Create(
                BridgeWireErrorCode.MissingField,
                $"A {messageKind} carried no epoch."));
            RequestConnectionRestart($"A {messageKind} carried no epoch.");
            return false;
        }
        if (!incoming.IsSameSession(negotiated))
        {
            CountProtocolRejection(BridgeWireError.Create(
                BridgeWireErrorCode.FieldOutOfRange,
                $"A {messageKind} named another origin or session than the negotiated one."));
            RequestConnectionRestart(
                $"A {messageKind} named another origin or session than the negotiated one.");
            return false;
        }
        if (incoming.Epoch == negotiated.Epoch)
        {
            return true;
        }
        if (incoming.Epoch < negotiated.Epoch)
        {
            CountProtocolRejection(BridgeWireError.Create(
                BridgeWireErrorCode.FieldOutOfRange,
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"A {messageKind} named retired epoch {incoming.Epoch}; the connection " +
                    $"negotiated {negotiated.Epoch}.")));
            RequestConnectionRestart($"A {messageKind} named a retired epoch.");
            return false;
        }

        RequestConnectionRestart(string.Create(
            CultureInfo.InvariantCulture,
            $"A {messageKind} named epoch {incoming.Epoch}; the connection negotiated " +
            $"{negotiated.Epoch}. Renegotiating rather than adopting it in band."));
        return false;
    }

    /// <summary>
    /// Ends the connection so the session renegotiates from a fresh handshake and full snapshot.
    /// </summary>
    private void RequestConnectionRestart(string detail)
    {
        RecordFailure(detail);
        BridgeConnectionState previous =
            TransitionTo(BridgeConnectionState.ConnectionRestartRequested);
        Report(BridgeClientEventKind.Backoff, previous, detail: detail);
    }

    private async ValueTask<bool> HandleSnapshotFrameAsync(
        LiveBridge.LiveBridgeClient client,
        AsyncDuplexStreamingCall<ChangeStreamRequest, ChangeStreamMessage> call,
        LiveAuthoringRemoteEpoch epoch,
        BridgeStreamFrame frame,
        CancellationToken cancellationToken)
    {
        if (!TryAuthorizeInboundUpdates(frame.Snapshot.Updates, "snapshot", out BridgeWireError violation))
        {
            CountProtocolRejection(violation);
            return true;
        }

        LiveAuthoringSessionResult result = await _coordinator
            .ApplySnapshotAsync(frame.Snapshot, cancellationToken)
            .ConfigureAwait(false);
        await AcknowledgeAsync(call, result, cancellationToken).ConfigureAwait(false);
        if (result.IsApplied)
        {
            BridgeConnectionState previous;
            lock (_stateGate)
            {
                _snapshotAppliedCount++;
                previous = _state;
            }

            Report(
                BridgeClientEventKind.SnapshotApplied,
                previous,
                sequence: result.Sequence,
                correlationId: result.CorrelationId,
                detail: "A full snapshot replaced the bridge overlay.");
            return true;
        }

        return await ResyncAsync(
            client,
            epoch,
            BridgeResyncReason.ApplyFailed,
            cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask<bool> HandleDeltaFrameAsync(
        LiveBridge.LiveBridgeClient client,
        AsyncDuplexStreamingCall<ChangeStreamRequest, ChangeStreamMessage> call,
        LiveAuthoringRemoteEpoch epoch,
        BridgeStreamFrame frame,
        CancellationToken cancellationToken)
    {
        if (!TryAuthorizeInboundUpdates(frame.Delta.Updates, "delta", out BridgeWireError violation))
        {
            CountProtocolRejection(violation);
            return true;
        }

        LiveAuthoringSessionResult result = await _coordinator
            .ApplyDeltaAsync(frame.Delta, cancellationToken)
            .ConfigureAwait(false);
        await AcknowledgeAsync(call, result, cancellationToken).ConfigureAwait(false);
        if (result.Outcome == LiveAuthoringSessionOutcome.Applied)
        {
            BridgeConnectionState previous;
            lock (_stateGate)
            {
                _deltaAppliedCount++;
                previous = _state;
            }

            Report(
                BridgeClientEventKind.DeltaApplied,
                previous,
                sequence: result.Sequence,
                correlationId: result.CorrelationId);
            return true;
        }

        // Duplicate and loop-suppressed outcomes are normal traffic, not failures: the coordinator
        // owns those rules, and the acknowledgement above already told the peer what happened.
        if (RequiresRenegotiation(result.Rejection))
        {
            // The coordinator says this message belongs to another epoch or origin than the one it
            // is bound to. That cannot be repaired by a snapshot taken under this connection's
            // negotiation, because that negotiation is exactly what is out of date.
            RequestConnectionRestart(
                $"The coordinator rejected the delta with {result.Rejection}; renegotiating.");
            return false;
        }
        if (RequiresResync(result.Rejection))
        {
            return await ResyncAsync(
                client,
                epoch,
                ToResyncReason(result.Rejection),
                cancellationToken).ConfigureAwait(false);
        }

        return true;
    }

    private static async ValueTask WriteKeepAliveAsync(
        AsyncDuplexStreamingCall<ChangeStreamRequest, ChangeStreamMessage> call,
        CancellationToken cancellationToken) =>
        await call.RequestStream
            .WriteAsync(
                new ChangeStreamRequest
                {
                    KeepAlive = new KeepAlive
                    {
                        TimestampUnixMillis = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
                    }
                },
                cancellationToken)
            .ConfigureAwait(false);

    private async ValueTask<bool> ResyncAsync(
        LiveBridge.LiveBridgeClient client,
        LiveAuthoringRemoteEpoch epoch,
        BridgeResyncReason reason,
        CancellationToken cancellationToken)
    {
        BridgeConnectionState previous = TransitionTo(BridgeConnectionState.Resynchronizing);
        lock (_stateGate)
        {
            _resyncCount++;
        }

        Report(
            BridgeClientEventKind.ResyncScheduled,
            previous,
            detail: $"A full snapshot is required: {reason}.");

        for (int attempt = 1; attempt <= MaxSnapshotAttemptsPerConnection; attempt++)
        {
            Report(BridgeClientEventKind.SnapshotRequested, State, attempt: attempt);
            StageSnapshot wire = await client
                .GetSnapshotAsync(
                    new SnapshotRequest
                    {
                        Epoch = BridgeMessageCodec.ToWire(epoch),
                        Reason = BridgeMessageCodec.ToWire(reason)
                    },
                    await CreateCallOptionsAsync(cancellationToken).ConfigureAwait(false))
                .ResponseAsync
                .ConfigureAwait(false);

            if (!TryAuthorizeInboundMessage(
                wire,
                "snapshot",
                wire.Updates.Count,
                out BridgeWireError oversized))
            {
                CountProtocolRejection(oversized);
                continue;
            }
            if (!BridgeMessageCodec.TryFromWire(
                wire,
                out LiveAuthoringSnapshot? snapshot,
                out BridgeWireError error))
            {
                CountProtocolRejection(error);
                continue;
            }

            // The snapshot must belong to the epoch this connection negotiated. A peer that answers
            // a resync with a newer epoch is describing a session this connection never agreed, so
            // the connection restarts and renegotiates instead of adopting it here.
            if (!TryAuthorizeEpoch(snapshot!.Epoch, epoch, "snapshot"))
            {
                return false;
            }
            if (!TryAuthorizeInboundUpdates(snapshot.Updates, "snapshot", out BridgeWireError violation))
            {
                CountProtocolRejection(violation);
                continue;
            }

            LiveAuthoringSessionResult result = await _coordinator
                .ApplySnapshotAsync(snapshot, cancellationToken)
                .ConfigureAwait(false);
            if (result.IsApplied)
            {
                lock (_stateGate)
                {
                    _snapshotAppliedCount++;
                }

                BridgeConnectionState beforeStreaming = TransitionTo(BridgeConnectionState.Streaming);
                Report(
                    BridgeClientEventKind.SnapshotApplied,
                    beforeStreaming,
                    sequence: result.Sequence,
                    correlationId: result.CorrelationId,
                    attempt: attempt);
                return true;
            }

            RecordFailure($"The snapshot was not applied: {result.Rejection}.");
        }

        RecordFailure("A full snapshot could not be applied on this connection.");
        return false;
    }

    /// <summary>Publishes queued local batches for the life of one connection.</summary>
    /// <remarks>
    /// The pump drains the retry queue before the channel, so a batch whose attempt failed keeps its
    /// place ahead of newer work. A failure that leaves the connection unusable cancels
    /// <paramref name="connectionScope"/>, which ends the change stream and sends the outer loop
    /// back through backoff, negotiation, and a fresh full snapshot; a queued batch is never left
    /// waiting on a connection that is no longer carrying anything.
    /// </remarks>
    private async ValueTask PublishPumpAsync(
        LiveBridge.LiveBridgeClient client,
        LiveAuthoringRemoteEpoch epoch,
        CancellationTokenSource connectionScope)
    {
        CancellationToken cancellationToken = connectionScope.Token;

        // The batch currently in hand, if any. A pump that ends while holding one must not drop it:
        // it left the channel, so nothing else would ever complete its receipt.
        BridgePendingPublication? current = null;
        try
        {
            if (!Supports(BridgeCapability.LocalEditExport))
            {
                // The session did not agree local export, so nothing may be sent. Queued batches
                // are refused rather than held: holding them would look like backpressure for a
                // capability this session will never have. The refusal keeps running for the life
                // of the connection instead of draining once, because a batch admitted a moment
                // after a single drain would otherwise wait on a pump that already returned.
                await RefuseWhileUnexportableAsync(cancellationToken).ConfigureAwait(false);
                return;
            }

            while (!cancellationToken.IsCancellationRequested)
            {
                BridgePendingPublication? pending = TakeNextPending();
                if (pending is null)
                {
                    if (!await _outbound.Reader
                        .WaitToReadAsync(cancellationToken)
                        .ConfigureAwait(false))
                    {
                        return;
                    }

                    continue;
                }

                current = pending;
                if (!IsCurrentEpoch(pending, epoch))
                {
                    CompleteBounded(
                        pending,
                        BridgeLocalPublicationOutcome.EpochRetired,
                        "The batch belongs to a retired epoch.");
                    current = null;
                    continue;
                }

                BridgePublishDisposition disposition;
                try
                {
                    // Re-authorize immediately before sending. A batch queued while disconnected
                    // was never checked against a session, and a batch retained across a reconnect
                    // was checked against the previous one; the capabilities and bounds that matter
                    // are the ones this connection actually negotiated.
                    if (!TryAuthorizeOutbound(pending.Batch, out string refusal))
                    {
                        CompleteBounded(
                            pending,
                            BridgeLocalPublicationOutcome.NotPermitted,
                            refusal);
                        current = null;
                        continue;
                    }

                    disposition = await PublishAsync(client, pending, cancellationToken)
                        .ConfigureAwait(false);
                }
                catch (BridgeProtocolException exception)
                {
                    // A batch this contract version cannot encode or measure is one batch's
                    // problem. Its receipt is completed and the pump continues: faulting here would
                    // strand every other queued batch on a promise that never completes.
                    CountProtocolRejection(exception.Error);
                    CompleteBounded(
                        pending,
                        BridgeLocalPublicationOutcome.NotPermitted,
                        $"The batch could not be encoded: {exception.Error.Code}.");
                    current = null;
                    continue;
                }

                // PublishAsync either completed the receipt or put the batch back on the retry
                // list, so the pump is no longer the only holder of it.
                current = null;
                if (disposition == BridgePublishDisposition.RestartConnection)
                {
                    BridgeConnectionState previous =
                        TransitionTo(BridgeConnectionState.ConnectionRestartRequested);
                    Report(
                        BridgeClientEventKind.Backoff,
                        previous,
                        sequence: pending.Batch.Sequence,
                        detail: "A publication failure ended the connection.");
                    await connectionScope.CancelAsync().ConfigureAwait(false);
                    return;
                }
            }
        }
        catch (OperationCanceledException)
        {
            // The pump is cancelled with the connection; queued batches stay for the next one, and
            // so does the one it was holding.
            RequeueIfHeld(current);
        }
        catch (RpcException exception)
        {
            RequeueIfHeld(current);
            RecordFailure($"Publishing failed: {exception.StatusCode}.");
            await connectionScope.CancelAsync().ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException
            and not StackOverflowException)
        {
            // Defensive only. The pump is awaited by the connection it belongs to, so letting an
            // unexpected failure escape would fault the whole client instead of ending one
            // connection; the loop reconnects and the queued batches are re-authorized there.
            RequeueIfHeld(current);
            RecordFailure(
                $"The publish pump ended unexpectedly with {exception.GetType().Name}.");
            await connectionScope.CancelAsync().ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Refuses every queued and later-admitted batch for the life of a connection that did not
    /// agree local export.
    /// </summary>
    /// <remarks>
    /// Draining once and returning would leave a window: a batch admitted a moment after the last
    /// read would sit in the channel with nothing reading it until the connection ended, so its
    /// receipt would stay pending on a session that can never carry it. Staying and consuming means
    /// every admission this connection sees is answered, and the loop ends only with the
    /// connection.
    /// </remarks>
    private async ValueTask RefuseWhileUnexportableAsync(CancellationToken cancellationToken)
    {
        const string detail = "The session did not agree the local-edit-export capability.";
        while (!cancellationToken.IsCancellationRequested)
        {
            BridgePendingPublication? pending = TakeNextPending();
            if (pending is null)
            {
                if (!await _outbound.Reader
                    .WaitToReadAsync(cancellationToken)
                    .ConfigureAwait(false))
                {
                    return;
                }

                continue;
            }

            CompleteBounded(pending, BridgeLocalPublicationOutcome.NotPermitted, detail);
        }
    }

    /// <summary>Returns a batch the pump was holding to the head of the retry list.</summary>
    /// <remarks>
    /// A batch taken out of the channel is only reachable through the pump, so a pump that ends
    /// while holding one has to hand it back. Its receipt is left open on purpose: the batch was
    /// neither refused nor delivered, and the next connection re-authorizes it.
    /// </remarks>
    private void RequeueIfHeld(BridgePendingPublication? pending)
    {
        if (pending is not null)
        {
            RequeueForRetry(pending);
        }
    }

    /// <summary>Publishes one batch and interprets the peer's acknowledgement.</summary>
    /// <remarks>
    /// <para>
    /// A transport failure does not prove the peer never acted: the request may have been applied
    /// and only the answer lost. That is exactly why every attempt carries the same idempotency key
    /// — the retry is safe because the peer can recognize the replay, not because the first attempt
    /// is known to have missed. Retries are bounded by
    /// <see cref="BridgeClientOptions.MaxPublishAttempts"/> and survive a reconnect.
    /// </para>
    /// <para>
    /// A semantic refusal is never retried. The peer saw the batch and decided; replaying it would
    /// produce the same refusal and hide that the session needs a resync.
    /// </para>
    /// </remarks>
    private async ValueTask<BridgePublishDisposition> PublishAsync(
        LiveBridge.LiveBridgeClient client,
        BridgePendingPublication pending,
        CancellationToken cancellationToken)
    {
        PublishLocalBatchRequest request = BridgeMessageCodec.ToWire(pending.Batch);
        int attempt = pending.RecordAttempt();
        Acknowledgement wire;
        try
        {
            wire = await client
                .PublishLocalBatchAsync(
                    request,
                    await CreateCallOptionsAsync(cancellationToken).ConfigureAwait(false))
                .ResponseAsync
                .ConfigureAwait(false);
        }
        catch (RpcException exception) when (IsRetryableTransportFailure(exception.StatusCode))
        {
            RecordFailure(
                $"Publishing attempt {attempt.ToString(CultureInfo.InvariantCulture)} failed with " +
                $"{exception.StatusCode}; whether the peer applied it is unknown.");
            if (attempt < _options.MaxPublishAttempts)
            {
                RequeueForRetry(pending);
            }
            else
            {
                CompletePending(
                    pending,
                    BridgeLocalPublicationOutcome.TransportFailed,
                    $"The transport failed on every attempt; last status {exception.StatusCode}.");
            }

            return BridgePublishDisposition.RestartConnection;
        }
        catch (RpcException exception)
        {
            CompletePending(
                pending,
                BridgeLocalPublicationOutcome.RemoteRejected,
                $"The peer refused the publication with {exception.StatusCode}.");
            return BridgePublishDisposition.RestartConnection;
        }

        return InterpretAcknowledgement(pending, wire);
    }

    /// <summary>Decodes and checks the peer's answer before it is allowed to mean anything.</summary>
    /// <remarks>
    /// An answer that arrives is not an answer that is true. It is decoded through the same
    /// validated codec as any other inbound message, and it must name this batch: the same
    /// sequence, the same echoed correlation identifier, a session state that still holds an epoch,
    /// and an outcome that is a real acknowledgement. Anything else is a protocol violation, not a
    /// publication, and the connection restarts rather than continuing against a peer that answered
    /// something the contract does not allow.
    /// </remarks>
    private BridgePublishDisposition InterpretAcknowledgement(
        BridgePendingPublication pending,
        Acknowledgement wire)
    {
        if (!TryAuthorizeInboundMessage(
            wire,
            "acknowledgement",
            updateCount: 0,
            out BridgeWireError oversized))
        {
            CountProtocolRejection(oversized);
            CompletePending(
                pending,
                BridgeLocalPublicationOutcome.ProtocolRejected,
                oversized.Detail);
            return BridgePublishDisposition.RestartConnection;
        }

        if (!BridgeMessageCodec.TryFromWire(
            wire,
            out LiveAuthoringSessionResult result,
            out BridgeWireError error))
        {
            CountProtocolRejection(error);
            CompletePending(
                pending,
                BridgeLocalPublicationOutcome.ProtocolRejected,
                $"The acknowledgement could not be decoded: {error.Code}.");
            return BridgePublishDisposition.RestartConnection;
        }

        if (result.Sequence != pending.Batch.Sequence)
        {
            return RejectAcknowledgement(
                pending,
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"The acknowledgement named sequence {result.Sequence} for batch " +
                    $"{pending.Batch.Sequence}."));
        }
        if (!string.Equals(
            result.CorrelationId,
            pending.Batch.CorrelationId,
            StringComparison.Ordinal))
        {
            return RejectAcknowledgement(
                pending,
                "The acknowledgement did not echo the batch's correlation identifier.");
        }
        if (result.State is LiveAuthoringSessionState.Disconnected
            or LiveAuthoringSessionState.Faulted)
        {
            return RejectAcknowledgement(
                pending,
                $"The peer acknowledged from session state {result.State}, which holds no epoch.");
        }

        switch (result.Outcome)
        {
            case LiveAuthoringSessionOutcome.Applied:
                CompletePending(
                    pending,
                    BridgeLocalPublicationOutcome.Published,
                    detail: null,
                    result.Outcome,
                    result.Rejection);
                return BridgePublishDisposition.Continue;

            case LiveAuthoringSessionOutcome.Duplicate:
                CompletePending(
                    pending,
                    BridgeLocalPublicationOutcome.Duplicate,
                    "The peer already held this batch; the idempotency key identified the replay.",
                    result.Outcome,
                    result.Rejection);
                return BridgePublishDisposition.Continue;

            case LiveAuthoringSessionOutcome.Rejected:
            case LiveAuthoringSessionOutcome.LoopSuppressed:
                CompletePending(
                    pending,
                    BridgeLocalPublicationOutcome.RemoteRejected,
                    $"The peer refused the publication: {result.Outcome}/{result.Rejection}.",
                    result.Outcome,
                    result.Rejection);

                // A refusal that says the peer lost its baseline is a session problem, not a batch
                // problem: the connection restarts so the next one starts from a fresh snapshot.
                return RequiresResync(result.Rejection) ||
                    result.State == LiveAuthoringSessionState.ResyncRequired
                    ? BridgePublishDisposition.RestartConnection
                    : BridgePublishDisposition.Continue;

            default:
                return RejectAcknowledgement(
                    pending,
                    $"The acknowledgement carried outcome {result.Outcome}.");
        }
    }

    private BridgePublishDisposition RejectAcknowledgement(
        BridgePendingPublication pending,
        string detail)
    {
        CountProtocolRejection(BridgeWireError.Create(BridgeWireErrorCode.MalformedPayload, detail));
        CompletePending(pending, BridgeLocalPublicationOutcome.ProtocolRejected, detail);
        return BridgePublishDisposition.RestartConnection;
    }

    private static async ValueTask AcknowledgeAsync(
        AsyncDuplexStreamingCall<ChangeStreamRequest, ChangeStreamMessage> call,
        LiveAuthoringSessionResult result,
        CancellationToken cancellationToken) =>
        await call.RequestStream
            .WriteAsync(
                new ChangeStreamRequest { Acknowledgement = BridgeMessageCodec.ToWire(result) },
                cancellationToken)
            .ConfigureAwait(false);

    private async ValueTask<CallOptions> CreateCallOptionsAsync(CancellationToken cancellationToken)
    {
        Metadata headers = await CreateHeadersAsync(cancellationToken).ConfigureAwait(false);
        return new CallOptions(
            headers,
            DateTime.UtcNow.Add(_options.CallDeadline),
            cancellationToken);
    }

    private async ValueTask<CallOptions> CreateStreamOptionsAsync(CancellationToken cancellationToken)
    {
        // A change stream is long-lived by design, so it carries no per-call deadline. Its bound is
        // the caller's cancellation token plus transport keepalive: a peer that stops answering
        // pings fails the stream instead of holding it open forever.
        Metadata headers = await CreateHeadersAsync(cancellationToken).ConfigureAwait(false);
        return new CallOptions(headers, deadline: null, cancellationToken);
    }

    private async ValueTask<Metadata> CreateHeadersAsync(CancellationToken cancellationToken)
    {
        BridgeCallCredential credential = await _options.Credentials!
            .GetCredentialAsync(cancellationToken)
            .ConfigureAwait(false);
        if (!credential.IsValidAt(DateTimeOffset.UtcNow))
        {
            throw new InvalidOperationException(
                "The credential provider returned an expired or empty credential. The client " +
                "refuses to make an unauthenticated call.");
        }

        // The header value is created here and handed straight to the call. It is never stored on
        // this instance, never included in an event, and never written to a diagnostic. It is
        // re-checked for control characters even though construction already validated them: a
        // provider is host code, and request metadata is the last place to find out.
        string value = credential.ToHeaderValue();
        if (!BridgeCallCredential.IsHeaderSafe(value))
        {
            throw new InvalidOperationException(
                "The credential produced an unsafe header value. The credential itself is not " +
                "reported, only that it was refused.");
        }

        return new Metadata { { "authorization", value } };
    }

    private void AdoptSession(BridgeNegotiationResult negotiation, LiveAuthoringRemoteEpoch epoch)
    {
        lock (_stateGate)
        {
            _negotiated = true;
            _peerVersion = negotiation.PeerVersion;
            _sessionId = epoch.SessionId;
            _epoch = epoch.Epoch;
            _effectiveLimits = negotiation.EffectiveLimits;
            _capabilities = [.. negotiation.AgreedCapabilities];
            _sessionGeneration++;
        }
    }

    /// <summary>Releases the negotiated session when the connection that agreed it ends.</summary>
    /// <remarks>
    /// The peer's protocol version is kept as a last-known diagnostic; everything that authorizes
    /// traffic — the capabilities, the effective limits, the session identity, and the epoch — is
    /// cleared, because none of it is in force once the connection is gone.
    /// </remarks>
    private void ClearSession()
    {
        lock (_stateGate)
        {
            _negotiated = false;
            _capabilities = [];
            _effectiveLimits = default;
            _sessionId = null;
            _epoch = 0;
            _sessionGeneration++;
        }
    }

    private bool Supports(BridgeCapability capability)
    {
        lock (_stateGate)
        {
            return Array.IndexOf(_capabilities, capability) >= 0;
        }
    }

    private static bool IsCurrentEpoch(
        BridgePendingPublication pending,
        LiveAuthoringRemoteEpoch epoch) =>
        pending.Batch.Epoch.IsSameSession(epoch) && pending.Batch.Epoch.Epoch == epoch.Epoch;

    /// <summary>
    /// Checks one outbound batch against the negotiated capabilities and every negotiated bound
    /// before it is queued, so a batch this session can never send is refused at the call site
    /// instead of being discovered by the peer.
    /// </summary>
    /// <remarks>
    /// <para>
    /// All eight negotiated bounds are checked, not only the update count and the encoded size: a
    /// batch that is legal here can still carry a path, an identifier, a text value, an opaque
    /// identifier, or a collection past what the peer said it accepts.
    /// <see cref="BridgeOutboundLimits"/> owns that walk so the check at admission and the check
    /// immediately before the send are the same check.
    /// </para>
    /// <para>
    /// The byte bound is measured with the message's own <c>CalculateSize</c> rather than by
    /// encoding the batch. Encoding to measure would allocate the whole frame for a batch that is
    /// about to be refused, and would throw for the oversized batch this check exists to catch —
    /// turning a refusal that belongs on one receipt into an exception on the publish path and, at
    /// reauthorization, into a faulted pump that strands every other queued batch.
    /// </para>
    /// </remarks>
    private bool TryAuthorizeOutbound(BridgeLocalBatch batch, out string detail) =>
        TryAuthorizeOutbound(batch, out detail, out _);

    /// <summary>
    /// Authorizes one outbound batch and reports the session generation the verdict describes, so
    /// a caller can tell whether that session is still the one the batch would travel on.
    /// </summary>
    private bool TryAuthorizeOutbound(BridgeLocalBatch batch, out string detail, out long generation)
    {
        BridgeCapability[] capabilities;
        BridgeLimits limits;
        bool negotiated;
        lock (_stateGate)
        {
            capabilities = _capabilities;
            limits = _effectiveLimits;
            negotiated = _negotiated;
            generation = _sessionGeneration;
        }

        // Checked before anything else, and independently of whether a session exists: the origin is
        // not a property of the negotiation, it is the identity this client publishes under. A batch
        // naming another origin would be echoed back under an identifier the coordinator does not
        // recognize as its own and reapplied as a remote edit, and its idempotency key would name a
        // publisher this session does not have.
        if (!string.Equals(batch.OriginId, LocalOriginId, StringComparison.Ordinal))
        {
            detail =
                "The batch names another origin identifier than the one this client publishes " +
                "under, so the peer's echo of it could not be suppressed as a local edit.";
            return false;
        }

        if (!negotiated)
        {
            // No session is bound yet, so there is nothing to check against. The pump re-checks the
            // batch once a session exists, and refuses it there if the session cannot carry it.
            detail = string.Empty;
            return true;
        }
        if (Array.IndexOf(capabilities, BridgeCapability.LocalEditExport) < 0)
        {
            detail = "The session did not agree the local-edit-export capability.";
            return false;
        }

        for (int index = 0; index < batch.Updates.Count; index++)
        {
            BridgeCapability? required = BridgeProtocol.GetRequiredCapability(batch.Updates[index]);
            if (required is BridgeCapability capability &&
                Array.IndexOf(capabilities, capability) < 0)
            {
                detail = $"The session did not agree the '{capability}' capability.";
                return false;
            }
        }

        return BridgeOutboundLimits.TryValidate(batch, limits, out detail);
    }

    /// <summary>
    /// Checks one inbound message against the negotiated bounds before it is decoded, using the
    /// message's real encoded size rather than a proxy for it.
    /// </summary>
    /// <remarks>
    /// The update count alone is not the bound a peer agreed to: a single update can carry a large
    /// array, so a message can sit far inside the count bound and far outside the byte bound. The
    /// size is taken from the wire message itself, and the check runs before decoding so an
    /// oversized message is never materialized into authoring values.
    /// </remarks>
    private bool TryAuthorizeInboundMessage(
        IMessage message,
        string messageKind,
        int updateCount,
        out BridgeWireError error)
    {
        BridgeLimits limits;
        bool negotiated;
        lock (_stateGate)
        {
            limits = _effectiveLimits;
            negotiated = _negotiated;
        }

        if (!negotiated)
        {
            error = BridgeWireError.Create(
                BridgeWireErrorCode.MalformedPayload,
                $"A {messageKind} arrived before negotiation completed.");
            return false;
        }

        int size = message.CalculateSize();
        if (!limits.Allows(updateCount, size))
        {
            error = BridgeWireError.Create(
                BridgeWireErrorCode.LimitExceeded,
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"The {messageKind} carried {updateCount} updates in {size} bytes, past the " +
                    $"negotiated bound of {limits.MaxUpdatesPerMessage} updates and " +
                    $"{limits.MaxMessagePayloadBytes} bytes."));
            return false;
        }

        error = BridgeWireError.None;
        return true;
    }

    /// <summary>
    /// Checks the decoded updates of one inbound message against the negotiated capabilities. A
    /// peer that uses a capability the session did not agree is refused even when the message is
    /// otherwise well formed.
    /// </summary>
    private bool TryAuthorizeInboundUpdates(
        IReadOnlyList<LiveStageUpdate> updates,
        string messageKind,
        out BridgeWireError error)
    {
        BridgeCapability[] capabilities;
        lock (_stateGate)
        {
            capabilities = _capabilities;
        }

        for (int index = 0; index < updates.Count; index++)
        {
            BridgeCapability? required = BridgeProtocol.GetRequiredCapability(updates[index]);
            if (required is BridgeCapability capability &&
                Array.IndexOf(capabilities, capability) < 0)
            {
                error = BridgeWireError.Create(
                    BridgeWireErrorCode.UnknownUpdateKind,
                    $"The {messageKind} used the '{capability}' capability, which the session did " +
                    "not agree.");
                return false;
            }
        }

        error = BridgeWireError.None;
        return true;
    }

    private BridgePendingPublication? TakeNextPending()
    {
        lock (_stateGate)
        {
            if (_retry.First is LinkedListNode<BridgePendingPublication> first)
            {
                _retry.RemoveFirst();
                return first.Value;
            }
        }

        return _outbound.Reader.TryRead(out BridgePendingPublication? pending) ? pending : null;
    }

    /// <summary>
    /// Puts a failed batch back at the head of the retry list, ahead of anything queued after it.
    /// </summary>
    /// <remarks>
    /// Local publications are an ordered sequence the host allocated, so a retry that lands behind
    /// a newer batch would deliver them out of order. The retention counter is untouched: the batch
    /// never left the queue's accounting, so a retry cannot grow the bound.
    /// </remarks>
    private void RequeueForRetry(BridgePendingPublication pending)
    {
        BridgePendingPublication? dropped = null;
        lock (_stateGate)
        {
            _retry.AddFirst(pending);
            if (_retry.Count > _options.OutboundQueueCapacity &&
                _retry.Last is LinkedListNode<BridgePendingPublication> last)
            {
                // Defensive only: the shared retention counter already keeps the total at or below
                // the capacity. Dropping the newest rather than the oldest preserves the order of
                // the work that has been waiting longest.
                dropped = last.Value;
                _retry.RemoveLast();
            }
        }

        if (dropped is not null)
        {
            CompletePending(
                dropped,
                BridgeLocalPublicationOutcome.TransportFailed,
                "The bounded retry list overflowed before the batch could be published.");
        }
    }

    /// <summary>
    /// Completes one publication with an outcome that only claims what the client can actually
    /// prove: definitive before the first attempt, indeterminate once a send has left the client.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <paramref name="definitive"/> — <see cref="BridgeLocalPublicationOutcome.NotPermitted"/>,
    /// <see cref="BridgeLocalPublicationOutcome.EpochRetired"/>, or
    /// <see cref="BridgeLocalPublicationOutcome.Cancelled"/> — asserts the peer never saw the batch.
    /// That is true while the batch is still only queued, and it stops being true the moment one
    /// request has been sent: the peer may have applied it and only the answer may have been lost,
    /// which is exactly why every attempt carries the same idempotency key.
    /// </para>
    /// <para>
    /// So an attempted publication that can no longer be retried or acknowledged — because the new
    /// session is tighter, dropped the export capability, retired the epoch, or because the client
    /// is stopping — is reported as
    /// <see cref="BridgeLocalPublicationOutcome.Indeterminate"/> with the reason kept in the detail.
    /// A host that must not lose an authoritative edit can then reconcile instead of believing a
    /// refusal that was never observed on the peer.
    /// </para>
    /// </remarks>
    private void CompleteBounded(
        BridgePendingPublication pending,
        BridgeLocalPublicationOutcome definitive,
        string detail)
    {
        if (pending.Attempts == 0)
        {
            CompletePending(pending, definitive, detail);
            return;
        }

        CompletePending(
            pending,
            BridgeLocalPublicationOutcome.Indeterminate,
            string.Create(
                CultureInfo.InvariantCulture,
                $"{detail} The batch had already been sent {pending.Attempts} time(s), so whether " +
                $"the peer applied it is unknown; the definitive outcome would have been " +
                $"{definitive}."));
    }

    private void CompletePending(
        BridgePendingPublication pending,
        BridgeLocalPublicationOutcome outcome,
        string? detail,
        LiveAuthoringSessionOutcome remoteOutcome = default,
        LiveAuthoringSessionRejection remoteRejection = LiveAuthoringSessionRejection.None)
    {
        if (!pending.Complete(outcome, detail, remoteOutcome, remoteRejection))
        {
            // Already completed: neither the counter nor the diagnostics move twice.
            return;
        }

        bool delivered = outcome is BridgeLocalPublicationOutcome.Published
            or BridgeLocalPublicationOutcome.Duplicate;
        lock (_stateGate)
        {
            _pendingCount--;
            if (delivered)
            {
                if (outcome == BridgeLocalPublicationOutcome.Published)
                {
                    _publishedBatchCount++;
                }
                else
                {
                    _duplicateBatchCount++;
                }
            }
            else
            {
                _refusedBatchCount++;
            }
        }

        Report(
            delivered
                ? BridgeClientEventKind.LocalBatchPublished
                : BridgeClientEventKind.LocalBatchRefused,
            State,
            sequence: pending.Batch.Sequence,
            correlationId: pending.Batch.CorrelationId,
            attempt: pending.Attempts,
            detail: detail);
    }

    private BridgeLocalPublicationReceipt RefuseLocalBatch(
        BridgeLocalBatch batch,
        BridgeLocalPublicationOutcome outcome,
        string detail)
    {
        lock (_stateGate)
        {
            _refusedBatchCount++;
        }

        Report(
            BridgeClientEventKind.LocalBatchRefused,
            State,
            sequence: batch.Sequence,
            correlationId: batch.CorrelationId,
            detail: detail);
        return BridgePendingPublication.CreateRefused(batch, outcome, detail);
    }

    /// <summary>Completes every queued and retained batch with one outcome.</summary>
    /// <param name="outcome">The outcome every held batch is answered with.</param>
    /// <param name="detail">The bounded reason reported on each receipt.</param>
    /// <param name="terminal">
    /// Whether nothing will ever read the outbound queue again. A terminal drain records the
    /// outcome under the state lock <em>before</em> it drains, and admission reserves and writes
    /// under that same lock, so a batch is either queued before the drain — and therefore drained —
    /// or refused after it. Without the flag, a batch admitted between the last read and the
    /// drain's return would keep a receipt that nothing could ever complete.
    /// </param>
    private void CompleteAllPending(
        BridgeLocalPublicationOutcome outcome,
        string detail,
        bool terminal = false)
    {
        if (terminal)
        {
            lock (_stateGate)
            {
                // First answer wins: a fault followed by disposal must not rewrite why the batches
                // were refused.
                _terminalOutcome ??= outcome;
                _terminalDetail ??= detail;
            }
        }

        while (true)
        {
            BridgePendingPublication? pending = TakeNextPending();
            if (pending is null)
            {
                return;
            }

            CompleteBounded(pending, outcome, detail);
        }
    }

    /// <summary>
    /// Completes every queued batch that belongs to a retired epoch and keeps the rest, in order,
    /// at the head of the retry queue so a reconnect neither loses nor reorders local work.
    /// </summary>
    private void DrainStaleOutbound(LiveAuthoringRemoteEpoch epoch)
    {
        var retained = new List<BridgePendingPublication>();
        var retired = new List<BridgePendingPublication>();
        while (true)
        {
            BridgePendingPublication? pending = TakeNextPending();
            if (pending is null)
            {
                break;
            }

            if (IsCurrentEpoch(pending, epoch))
            {
                retained.Add(pending);
            }
            else
            {
                retired.Add(pending);
            }
        }

        lock (_stateGate)
        {
            // Appended in the order they were taken, and the retry list is empty at this point
            // because it was drained above, so the host's publication order is preserved exactly.
            foreach (BridgePendingPublication pending in retained)
            {
                _retry.AddLast(pending);
            }
        }

        foreach (BridgePendingPublication pending in retired)
        {
            CompleteBounded(
                pending,
                BridgeLocalPublicationOutcome.EpochRetired,
                "The batch belongs to a retired epoch.");
        }
    }

    private BridgeConnectionOutcome HandleRpcFailure(RpcException exception)
    {
        RecordFailure($"The call failed with status {exception.StatusCode}.");
        switch (exception.StatusCode)
        {
            case StatusCode.Unauthenticated:
            case StatusCode.PermissionDenied:
            case StatusCode.Unimplemented:
                BridgeConnectionState previous = TransitionTo(BridgeConnectionState.Faulted);
                Report(
                    BridgeClientEventKind.Faulted,
                    previous,
                    detail: $"The peer refused the session: {exception.StatusCode}.");
                return BridgeConnectionOutcome.Fatal;
            default:
                return BridgeConnectionOutcome.Transient;
        }
    }

    private TimeSpan NextBackoff(TimeSpan ceiling)
    {
        // Full jitter over the current ceiling. Two clients that lose the same peer do not come
        // back in lockstep, and no wait is ever unbounded.
        long ticks;
        lock (_stateGate)
        {
            ticks = (long)(_jitter.NextDouble() * ceiling.Ticks);
        }

        TimeSpan floor = TimeSpan.FromMilliseconds(1);
        TimeSpan delay = TimeSpan.FromTicks(ticks);
        return delay < floor ? floor : delay;
    }

    private void CountProtocolRejection(BridgeWireError error)
    {
        BridgeConnectionState state;
        lock (_stateGate)
        {
            _protocolRejectionCount++;
            _lastFailureDetail = error.Detail;
            state = _state;
        }

        Report(
            BridgeClientEventKind.ProtocolRejected,
            state,
            detail: $"{error.Code}: {error.Detail}");
    }

    private void RecordFailure(string detail)
    {
        lock (_stateGate)
        {
            _lastFailureDetail = Truncate(detail);
        }
    }

    private BridgeConnectionState TransitionTo(BridgeConnectionState state)
    {
        lock (_stateGate)
        {
            BridgeConnectionState previous = _state;
            _state = state;
            return previous;
        }
    }

    private void Report(
        BridgeClientEventKind kind,
        BridgeConnectionState previousState,
        long attempt = 0,
        long sequence = 0,
        string? correlationId = null,
        string? detail = null)
    {
        IProgress<BridgeClientEvent>? observer = _options.Observer;
        if (observer is null)
        {
            return;
        }

        BridgeConnectionState state;
        lock (_stateGate)
        {
            state = _state;
        }

        try
        {
            observer.Report(new BridgeClientEvent(
                kind,
                previousState,
                state,
                attempt,
                sequence,
                correlationId,
                DateTimeOffset.UtcNow,
                detail is null ? null : Truncate(detail)));
        }
        catch (Exception exception) when (exception is not OutOfMemoryException
            and not StackOverflowException)
        {
            // A broken observer must never change connection behaviour, exactly as a broken health
            // observer never changes admission behaviour in the authoring layer. It is counted on
            // its own field: overwriting the transport's last failure would erase why the
            // connection actually dropped.
            lock (_stateGate)
            {
                _observerFailureCount++;
                _lastObserverFailureDetail = Truncate(
                    $"The client observer threw {exception.GetType().Name} while reporting {kind}.");
            }
        }
    }

    private static string Truncate(string value) =>
        value.Length <= BridgeWireError.MaxDetailLength
            ? value
            : value[..BridgeWireError.MaxDetailLength];

    /// <summary>
    /// Builds the rejected negotiation result for an answer that could not even be decoded. A
    /// malformed answer is never treated as an acceptance, and it is never retried as if the peer
    /// had merely been unavailable.
    /// </summary>
    private static BridgeNegotiationResult CreateRejectedNegotiation(BridgeWireError error) =>
        new(
            accepted: false,
            BridgeHandshakeRejection.Malformed,
            BridgeProtocol.Version,
            BridgeLimits.Local,
            Array.Empty<BridgeCapability>(),
            epoch: null,
            error.Detail);

    private static bool IsFatal(BridgeHandshakeRejection rejection) => rejection switch
    {
        BridgeHandshakeRejection.Version => true,
        BridgeHandshakeRejection.Capability => true,
        BridgeHandshakeRejection.Limits => true,
        BridgeHandshakeRejection.BridgeRoot => true,
        BridgeHandshakeRejection.Unauthenticated => true,
        _ => false
    };

    /// <summary>
    /// Returns whether a status describes a transport failure whose effect on the peer is unknown,
    /// which is the only class of failure a bounded, idempotency-keyed retry may repeat.
    /// </summary>
    private static bool IsRetryableTransportFailure(StatusCode status) => status switch
    {
        StatusCode.Unavailable => true,
        StatusCode.DeadlineExceeded => true,
        StatusCode.Aborted => true,
        StatusCode.Internal => true,
        StatusCode.Unknown => true,
        StatusCode.ResourceExhausted => true,
        _ => false
    };

    private static bool RequiresResync(LiveAuthoringSessionRejection rejection) => rejection switch
    {
        LiveAuthoringSessionRejection.SequenceGap => true,
        LiveAuthoringSessionRejection.ResyncRequired => true,
        LiveAuthoringSessionRejection.ApplyFailed => true,
        LiveAuthoringSessionRejection.DuplicateConflict => true,
        LiveAuthoringSessionRejection.ReplayExpired => true,
        LiveAuthoringSessionRejection.OverlayBudget => true,
        _ => false
    };

    /// <summary>
    /// Returns whether a coordinator rejection means the session identity itself is out of date, so
    /// a fresh handshake is required rather than a snapshot taken under the current negotiation.
    /// </summary>
    /// <remarks>
    /// The epoch guard normally refuses such a message before the coordinator ever sees it. These
    /// cases remain as defence: if the coordinator and this connection ever disagree about which
    /// epoch is bound, renegotiating is the only step that re-agrees the epoch, the capabilities,
    /// and the limits together.
    /// </remarks>
    private static bool RequiresRenegotiation(LiveAuthoringSessionRejection rejection) =>
        rejection switch
        {
            LiveAuthoringSessionRejection.EpochAdvanced => true,
            LiveAuthoringSessionRejection.EpochRetired => true,
            LiveAuthoringSessionRejection.SessionIdentity => true,
            LiveAuthoringSessionRejection.RemoteOrigin => true,
            _ => false
        };

    private static BridgeResyncReason ToResyncReason(LiveAuthoringSessionRejection rejection) =>
        rejection switch
        {
            LiveAuthoringSessionRejection.SequenceGap => BridgeResyncReason.SequenceGap,
            LiveAuthoringSessionRejection.EpochAdvanced => BridgeResyncReason.EpochChanged,
            LiveAuthoringSessionRejection.ApplyFailed => BridgeResyncReason.ApplyFailed,
            _ => BridgeResyncReason.ServerRequested
        };

    private enum BridgeConnectionOutcome
    {
        Transient,
        Fatal
    }

    private enum BridgePublishDisposition
    {
        Continue,
        RestartConnection
    }

    /// <summary>How one attempt to queue a local batch ended.</summary>
    private enum BridgeAdmission
    {
        /// <summary>The batch is queued and its receipt is the caller's answer.</summary>
        Admitted,

        /// <summary>The batch cannot be queued, and the outcome says why.</summary>
        Refused,

        /// <summary>
        /// The negotiated session changed between authorization and reservation, so the batch has
        /// to be judged again against the session it would actually travel on.
        /// </summary>
        SessionChanged
    }
}
