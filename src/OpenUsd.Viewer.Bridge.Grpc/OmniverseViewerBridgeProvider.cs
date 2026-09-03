// Copyright (c) marcschier. Licensed under the MIT License.

using OpenUsd.Bridge.Grpc;

namespace OpenUsd.Viewer.Bridge.Grpc;

/// <summary>
/// Adapts an <see cref="OmniverseBridgeClient"/> to the Viewer's host-injected bridge seam.
/// </summary>
/// <remarks>
/// <para>
/// A host constructs this, hands it to <c>ViewerHostOptions.BridgeConnection</c>, and keeps
/// ownership: the Viewer never constructs a provider, never discovers one, and never learns
/// what is behind it. The Viewer sees only <see cref="ViewerBridgeStatus"/> snapshots, so no
/// gRPC type, native handle, or credential crosses the seam even though this class holds all
/// three sides of that connection itself.
/// </para>
/// <para>
/// Connect is transactional. It builds one client, runs its connect/negotiate/resync/stream
/// loop on a background task, and waits - with a bound - only until the session is streaming
/// or has failed. If readiness faults, times out, or the caller cancels, the partially started
/// session is stopped and disposed before the failure is rethrown, so a refused connect cannot
/// leave a client behind that keeps reconnecting, keeps presenting credentials, and keeps
/// raising events into a Viewer that already reported the attempt as failed.
/// </para>
/// <para>
/// Disconnect cancels the loop and disposes the client. Resync is a deliberate stop-and-restart
/// rather than a separate message, because the client's own contract is that a fresh connection
/// renegotiates and takes a fresh baseline; there is no in-band resync to call.
/// </para>
/// </remarks>
public sealed class OmniverseViewerBridgeProvider : IViewerBridgeConnectionProvider, IAsyncDisposable
{
    private readonly OmniverseViewerBridgeOptions _options;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly List<ViewerBridgeSession> _sessions;
    private readonly Lock _stateGate = new();
    private OmniverseBridgeClient? _client;
    private CancellationTokenSource? _run;
    private BridgeEventRelay? _relay;
    private Task _loop = Task.CompletedTask;
    private string? _endpoint;
    private string? _sessionId;
    private string? _detail;
    private string? _diagnostic;
    private long _connectAttempts;
    private long _observerFailures;
    private long _subscriberFailures;
    private int _disposed;

    /// <summary>Initializes the provider for the supplied host configuration.</summary>
    /// <param name="options">The host's configuration.</param>
    /// <exception cref="ArgumentNullException"><paramref name="options"/> is <c>null</c>.</exception>
    /// <exception cref="ArgumentException">The configuration is incomplete.</exception>
    public OmniverseViewerBridgeProvider(OmniverseViewerBridgeOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();
        _options = options;
        _sessions = [.. options.Sessions];
    }

    /// <inheritdoc/>
    public event EventHandler<ViewerBridgeStatusChangedEventArgs>? StatusChanged;

    /// <inheritdoc/>
    public string DisplayName => _options.DisplayName;

    /// <inheritdoc/>
    public bool IsAvailable => Volatile.Read(ref _disposed) == 0;

    /// <summary>
    /// Gets how many times an observer the host installed on its own
    /// <see cref="BridgeClientOptions.Observer"/> threw while this provider was forwarding an
    /// event to it.
    /// </summary>
    public long HostObserverFailureCount => Interlocked.Read(ref _observerFailures);

    /// <summary>Gets how many times a <see cref="StatusChanged"/> subscriber threw.</summary>
    public long SubscriberFailureCount => Interlocked.Read(ref _subscriberFailures);

    /// <summary>
    /// Gets the last bounded, redacted note about a defect in caller-supplied reporting code,
    /// or <see langword="null"/>.
    /// </summary>
    /// <remarks>
    /// It is kept apart from the transport's own failure detail for the same reason the client
    /// keeps its observer-failure detail apart from its last failure detail: a broken handler is
    /// a defect in reporting code, and folding it into the transport detail would erase the
    /// reason a session actually dropped.
    /// </remarks>
    public string? LastDiagnostic
    {
        get
        {
            using (_stateGate.EnterScope())
            {
                return _diagnostic;
            }
        }
    }

    /// <inheritdoc/>
    public ViewerBridgeStatus GetStatus()
    {
        OmniverseBridgeClient? client = Volatile.Read(ref _client);
        string? endpoint;
        string? sessionId;
        string? detail;
        string? diagnostic;
        using (_stateGate.EnterScope())
        {
            endpoint = _endpoint;
            sessionId = _sessionId;
            detail = _detail;
            diagnostic = _diagnostic;
        }

        if (client is null)
        {
            return ViewerBridgeStatus.Disconnected with
            {
                SessionId = sessionId,
                ConnectAttemptCount = Interlocked.Read(ref _connectAttempts),
                Endpoint = endpoint,
                TimestampUtc = DateTimeOffset.UtcNow,
                Detail = detail ?? diagnostic
            };
        }

        BridgeClientStatus status = client.GetStatus();

        // Everything textual is redacted and bounded here, at the boundary, rather than trusting
        // the Viewer to do it later. This snapshot is public: a host that reads it directly must
        // not be handed a transport message that still quotes an endpoint with its userinfo, or a
        // token in a query string.
        return new ViewerBridgeStatus(
            Map(status.State),
            ViewerBridgeText.Bound(status.SessionId ?? sessionId),
            endpoint,
            status.ConnectAttemptCount,
            status.SnapshotAppliedCount + status.DeltaAppliedCount,
            status.PendingOutboundBatchCount,
            status.TimestampUtc,
            ViewerBridgeText.Bound(status.LastFailureDetail) ?? detail ?? diagnostic);
    }

    /// <inheritdoc/>
    public ValueTask<IReadOnlyList<ViewerBridgeSession>> GetSessionsAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult<IReadOnlyList<ViewerBridgeSession>>(_sessions.AsReadOnly());
    }

    /// <inheritdoc/>
    public async ValueTask ConnectAsync(
        ViewerBridgeConnectRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await StopAsync().ConfigureAwait(false);
            await StartAsync(request.SessionId, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
        Publish();
    }

    /// <inheritdoc/>
    public async ValueTask DisconnectAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await StopAsync().ConfigureAwait(false);
            using (_stateGate.EnterScope())
            {
                // An explicit disconnect is the one place the last requested session and the last
                // failure stop being interesting: the operator asked for nothing to be connected.
                _detail = null;
                _sessionId = null;
            }
        }
        finally
        {
            _gate.Release();
        }
        Publish();
    }

    /// <inheritdoc/>
    public async ValueTask ResyncAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (Volatile.Read(ref _client) is null)
            {
                throw new InvalidOperationException(
                    "There is no bridge session to resynchronize.");
            }

            string? sessionId;
            using (_stateGate.EnterScope())
            {
                sessionId = _sessionId;
            }
            await StopAsync().ConfigureAwait(false);
            await StartAsync(sessionId, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
        Publish();
    }

    /// <summary>Stops the session, disposes the client, and refuses every later command.</summary>
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            await StopAsync().ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
        _gate.Dispose();
        StatusChanged = null;
    }

    /// <summary>
    /// Builds and starts one session, and leaves nothing behind if it does not become ready.
    /// </summary>
    private async Task StartAsync(string? sessionId, CancellationToken cancellationToken)
    {
        OmniverseViewerBridgeSessionConfiguration configuration =
            await _options.SessionFactory!(sessionId, cancellationToken).ConfigureAwait(false) ??
            throw new InvalidOperationException(
                "The host's bridge session factory returned no configuration.");

        // The host still owns the options object it just returned, and it may well be the same
        // instance on every call. Mutating it to install the relay would chain a new observer
        // onto every previous one across reconnects, keep every dead relay and its readiness
        // source alive, and leave the host's own configuration quietly rewritten. Copy first.
        BridgeClientOptions options = configuration.Options.Clone();
        options.RequestedSessionId =
            ResolveRequestedSessionId(sessionId, configuration.Options.RequestedSessionId);

        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        ObserveEventualFault(ready.Task);
        var relay = new BridgeEventRelay(this, configuration.Options.Observer, ready);
        options.Observer = relay;

        using (_stateGate.EnterScope())
        {
            _sessionId = options.RequestedSessionId ?? ViewerBridgeText.Bound(sessionId);
            _endpoint = ViewerBridgeEndpoint.Redact(options.Endpoint);
            _detail = null;
        }
        Interlocked.Increment(ref _connectAttempts);

        OmniverseBridgeClient? client = null;
        CancellationTokenSource? run = null;
        try
        {
            client = new OmniverseBridgeClient(configuration.Coordinator, options);
            // The caller token bounds this connect operation, not the lifetime of a
            // session that has already become ready. StopAsync owns the established
            // session lifetime after ConnectAsync returns.
            run = new CancellationTokenSource();
            Volatile.Write(ref _client, client);
            _run = run;
            _relay = relay;
            _loop = RunLoopAsync(client, ready, run.Token);
            await ready.Task
                .WaitAsync(_options.ReadyTimeout, cancellationToken)
                .ConfigureAwait(false);
        }
        catch
        {
            // A partially started session is a leak with teeth: it keeps reconnecting, keeps
            // asking the host's credential provider for a token, and keeps raising events at a
            // Viewer that has already been told the attempt failed. Tear it down here, on every
            // failure path - construction, fault, timeout, and caller cancellation alike -
            // before the caller ever sees the exception.
            relay.Detach();
            if (Volatile.Read(ref _client) is not null)
            {
                await StopAsync().ConfigureAwait(false);
            }
            else
            {
                run?.Dispose();
                if (client is not null)
                {
                    await client.DisposeAsync().ConfigureAwait(false);
                }
            }
            throw;
        }
    }

    /// <summary>
    /// Runs the client loop and never rethrows: nothing awaits it once a session is established,
    /// so a fault has to become status rather than an unobserved exception.
    /// </summary>
    private async Task RunLoopAsync(
        OmniverseBridgeClient client,
        TaskCompletionSource ready,
        CancellationToken cancellationToken)
    {
        try
        {
            await client.RunAsync(cancellationToken).ConfigureAwait(false);
            _ = ready.TrySetException(new InvalidOperationException(
                "The bridge session stopped before it started streaming."));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Only our own cancellation is cancellation. A transport that surfaces a
            // cancellation-shaped failure for a deadline or a dropped socket is a failure, and
            // reporting it as a cancelled connect would hide it from the operator entirely.
            _ = ready.TrySetCanceled(CancellationToken.None);
        }
#pragma warning disable CA1031 // The transport is the outermost boundary here; a fault becomes status.
        catch (Exception exception)
#pragma warning restore CA1031
        {
            string detail = ViewerBridgeText.Describe("The bridge session", exception);
            SetDetail(detail);
            _ = ready.TrySetException(new InvalidOperationException(detail));
        }
        finally
        {
            Publish();
        }
    }

    private async Task StopAsync()
    {
        CancellationTokenSource? run = _run;
        OmniverseBridgeClient? client = Volatile.Read(ref _client);
        BridgeEventRelay? relay = _relay;
        _run = null;
        _relay = null;
        Volatile.Write(ref _client, null);

        // Detaching first is what makes teardown quiet: an event already in flight inside the
        // transport cannot resurrect a readiness source the attempt has given up on, and cannot
        // publish a status for a client that is being disposed.
        relay?.Detach();
        if (run is not null)
        {
            await run.CancelAsync().ConfigureAwait(false);
        }

        if (client is not null)
        {
            await client.DisposeAsync().ConfigureAwait(false);
        }

        Task loop = _loop;
        _loop = Task.CompletedTask;
        try
        {
            // RunLoopAsync never faults, so this is a join rather than an error path. It still
            // matters: returning before the loop has actually stopped would let a disposed
            // session's last activity race the next connect.
            await loop.ConfigureAwait(false);
        }
        finally
        {
            run?.Dispose();
        }
    }

    /// <summary>
    /// Resolves what the peer is asked to join. The operator's selection wins when there is one,
    /// because a session list the Viewer showed and a session the client asks for must not be
    /// allowed to disagree; a factory that wants the last word can set its own
    /// <see cref="BridgeClientOptions.RequestedSessionId"/> and be called with no selection.
    /// </summary>
    private static string? ResolveRequestedSessionId(string? selected, string? configured) =>
        ViewerBridgeText.Bound(selected) ?? configured;

    private void SetDetail(string? detail)
    {
        using (_stateGate.EnterScope())
        {
            _detail = ViewerBridgeText.Bound(detail);
        }
    }

    private void SetDiagnostic(string operation, Exception exception)
    {
        using (_stateGate.EnterScope())
        {
            _diagnostic = ViewerBridgeText.Bound(
                $"{operation} threw {exception.GetType().Name}.");
        }
    }

    /// <summary>
    /// Applies one client event to provider state. It is deliberately total: every path is
    /// non-throwing, because it runs before the host's own observer and a failure here would be
    /// exactly the readiness stall this class exists to prevent.
    /// </summary>
    private void ApplyClientEvent(BridgeClientEvent value, TaskCompletionSource ready)
    {
        if (value.Detail is { Length: > 0 } &&
            value.Kind is BridgeClientEventKind.Faulted
                or BridgeClientEventKind.NegotiationRejected
                or BridgeClientEventKind.Backoff)
        {
            // The event detail is peer- and transport-authored text. It is redacted and bounded
            // before it is stored, not when it is later read, so no code path can return it raw.
            SetDetail(value.Detail);
        }

        switch (value.Kind)
        {
            case BridgeClientEventKind.SnapshotApplied:
                _ = ready.TrySetResult();
                break;
            case BridgeClientEventKind.Faulted:
                _ = ready.TrySetException(new InvalidOperationException(
                    ViewerBridgeText.Bound(value.Detail) ?? "The bridge session faulted."));
                break;
            default:
                break;
        }

        Publish();
    }

    private void RecordHostObserverFailure(Exception exception)
    {
        Interlocked.Increment(ref _observerFailures);
        SetDiagnostic("A host bridge observer", exception);
    }

    /// <summary>
    /// Raises <see cref="StatusChanged"/> with every subscriber isolated from every other.
    /// </summary>
    /// <remarks>
    /// This runs inside the transport's own observer callback. One subscriber that throws must
    /// not skip the rest and must not surface as a transport-level observer failure, because the
    /// transport would then attribute a Viewer defect to the client's event reporting.
    /// </remarks>
    private void Publish()
    {
        EventHandler<ViewerBridgeStatusChangedEventArgs>? handler = StatusChanged;
        if (handler is null)
        {
            return;
        }

        ViewerBridgeStatus status;
        try
        {
            status = GetStatus();
        }
#pragma warning disable CA1031 // Status is reporting; a failure to read it must not stop the session.
        catch (Exception exception)
#pragma warning restore CA1031
        {
            SetDiagnostic("Reading bridge status", exception);
            return;
        }

        var args = new ViewerBridgeStatusChangedEventArgs(status);
        foreach (Delegate subscriber in handler.GetInvocationList())
        {
            try
            {
                ((EventHandler<ViewerBridgeStatusChangedEventArgs>)subscriber)(this, args);
            }
#pragma warning disable CA1031 // A subscriber is caller code; its defect stays its own.
            catch (Exception exception)
#pragma warning restore CA1031
            {
                Interlocked.Increment(ref _subscriberFailures);
                SetDiagnostic("A bridge status subscriber", exception);
            }
        }
    }

    /// <summary>
    /// Makes a readiness fault observed even when no caller is left to await it, so a connect
    /// that already returned a timeout cannot later raise an unobserved task exception.
    /// </summary>
    private static void ObserveEventualFault(Task task) =>
        _ = task.ContinueWith(
            static completed => _ = completed.Exception,
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);

    private static ViewerBridgeConnectionState Map(BridgeConnectionState state) => state switch
    {
        BridgeConnectionState.Connecting => ViewerBridgeConnectionState.Connecting,
        BridgeConnectionState.Negotiating => ViewerBridgeConnectionState.Negotiating,
        BridgeConnectionState.Resynchronizing => ViewerBridgeConnectionState.Resynchronizing,
        BridgeConnectionState.Streaming => ViewerBridgeConnectionState.Streaming,
        BridgeConnectionState.Backoff => ViewerBridgeConnectionState.Reconnecting,
        BridgeConnectionState.ConnectionRestartRequested =>
            ViewerBridgeConnectionState.Reconnecting,
        BridgeConnectionState.Faulted => ViewerBridgeConnectionState.Faulted,
        _ => ViewerBridgeConnectionState.Disconnected
    };

    /// <summary>
    /// Forwards every client event to the provider first and to the host's own observer second.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The order is the point. A host observer is caller code that can throw, and reporting to
    /// it first meant one bad handler could stop the provider ever seeing
    /// <see cref="BridgeClientEventKind.SnapshotApplied"/>, turning a healthy connection into a
    /// connect timeout. Provider state and readiness are therefore settled before the host is
    /// told anything.
    /// </para>
    /// <para>
    /// A host observer failure is recorded here as a bounded provider diagnostic - type name
    /// only, never the event payload - and then rethrown, because the client's own observer
    /// failure counters are the documented place a host looks for a defect in its reporting
    /// code, and swallowing it here would silently empty them.
    /// </para>
    /// </remarks>
    private sealed class BridgeEventRelay(
        OmniverseViewerBridgeProvider provider,
        IProgress<BridgeClientEvent>? hostObserver,
        TaskCompletionSource ready) : IProgress<BridgeClientEvent>
    {
        private int _detached;

        internal void Detach() => Interlocked.Exchange(ref _detached, 1);

        public void Report(BridgeClientEvent value)
        {
            if (Volatile.Read(ref _detached) == 0)
            {
                provider.ApplyClientEvent(value, ready);
            }

            if (hostObserver is null)
            {
                return;
            }

            try
            {
                hostObserver.Report(value);
            }
#pragma warning disable CA1031 // Recorded as a provider diagnostic, then rethrown for the client.
            catch (Exception exception)
#pragma warning restore CA1031
            {
                provider.RecordHostObserverFailure(exception);
                throw;
            }
        }
    }
}
