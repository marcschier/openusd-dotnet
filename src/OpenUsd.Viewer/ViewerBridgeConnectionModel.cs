// Copyright (c) marcschier. Licensed under the MIT License.

using System.Text;

namespace OpenUsd.Viewer;

/// <summary>
/// Everything the bridge surface needs to render itself, computed away from any control.
/// </summary>
/// <remarks>
/// The Viewer's bridge UI is a projection of this record and nothing else, so the visibility,
/// enablement, and wording rules can be tested without a window, a dispatcher, or a
/// transport. <see cref="DroppedStatusEventCount"/> is part of the state on purpose: a bound
/// that silently discards notifications and reports nothing is indistinguishable from a
/// provider that stopped sending them.
/// </remarks>
internal readonly record struct ViewerBridgeViewState(
    bool MenuVisible,
    bool MenuEnabled,
    string MenuToolTip,
    bool StatusVisible,
    string StatusText,
    bool CanConnect,
    bool CanDisconnect,
    bool CanResync,
    bool Busy,
    ViewerBridgeStatus Status,
    string? ErrorMessage,
    long DroppedStatusEventCount)
{
    /// <summary>The state of a Viewer with no bridge provider injected at all.</summary>
    internal static ViewerBridgeViewState Absent { get; } = new(
        MenuVisible: false,
        MenuEnabled: false,
        MenuToolTip: NoProviderToolTip,
        StatusVisible: false,
        StatusText: string.Empty,
        CanConnect: false,
        CanDisconnect: false,
        CanResync: false,
        Busy: false,
        Status: ViewerBridgeStatus.Disconnected,
        ErrorMessage: null,
        DroppedStatusEventCount: 0);

    /// <summary>The tooltip shown when no host injected a provider.</summary>
    internal const string NoProviderToolTip =
        "No Omniverse Bridge provider is installed or configured.";
}

/// <summary>
/// Drives the Viewer's bridge surface from a host-injected provider.
/// </summary>
/// <remarks>
/// <para>
/// The model owns three things the UI must not do for itself. It marshals: a provider raises
/// <see cref="IViewerBridgeConnectionProvider.StatusChanged"/> from its own transport thread,
/// and the handler here only copies a detached snapshot into a bounded queue and asks the
/// caller-supplied post delegate to drain it, so no provider thread ever touches a control
/// and no provider can block rendering by taking its time in an event handler. It bounds: the
/// queue drops the oldest snapshot and counts the drop rather than growing, and every string
/// is scrubbed and truncated. It isolates failure: a provider that throws, faults, or is
/// cancelled produces a redacted message in the view state instead of an exception crossing
/// back into UI code that has no way to handle it.
/// </para>
/// <para>
/// Exactly one command runs at a time. Overlapping a connect with a disconnect would leave
/// the operator looking at a status line that describes neither, and the underlying providers
/// are not required to be re-entrant.
/// </para>
/// </remarks>
internal sealed class ViewerBridgeConnectionModel : IDisposable
{
    private readonly IViewerBridgeConnectionProvider? _provider;
    private readonly Action<Action> _post;
    private readonly Action<ViewerBridgeViewState> _publish;
    private readonly int _capacity;
    private readonly string _providerDisplayName = "Omniverse Bridge";
    private readonly Lock _gate = new();
    private readonly Queue<ViewerBridgeStatus> _pending = new();
    private readonly CancellationTokenSource _lifetime = new();
    private readonly CancellationToken _lifetimeToken;
    private bool _drainScheduled;
    private bool _busy;
    private bool _configured;
    private long _dropped;
    private string? _error;
    private ViewerBridgeStatus _status = ViewerBridgeStatus.Disconnected;
    private int _disposed;

    /// <summary>Initializes the model for an optional provider.</summary>
    /// <param name="provider">
    /// The host-injected provider, or <see langword="null"/> when no host injected one. A
    /// <see langword="null"/> provider is the normal case and is not an error: it produces a
    /// Viewer with no bridge surface whatsoever.
    /// </param>
    /// <param name="post">
    /// Marshals a callback onto the thread that owns the controls. In the shell this posts to
    /// the Avalonia dispatcher; tests supply a synchronous or deferred pump.
    /// </param>
    /// <param name="publish">Applies a computed view state, always on the posted thread.</param>
    /// <param name="eventCapacity">How many notifications may wait for the posted thread.</param>
    internal ViewerBridgeConnectionModel(
        IViewerBridgeConnectionProvider? provider,
        Action<Action> post,
        Action<ViewerBridgeViewState> publish,
        int eventCapacity = ViewerBridgeLimits.MaxPendingStatusEvents)
    {
        ArgumentNullException.ThrowIfNull(post);
        ArgumentNullException.ThrowIfNull(publish);
        ArgumentOutOfRangeException.ThrowIfLessThan(eventCapacity, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(
            eventCapacity,
            ViewerBridgeLimits.MaxPendingStatusEvents);
        _provider = provider;
        _post = post;
        _publish = publish;
        _capacity = eventCapacity;
        _lifetimeToken = _lifetime.Token;
        if (provider is not null)
        {
            try
            {
                _providerDisplayName =
                    ViewerBridgeText.Bound(provider.DisplayName) ?? _providerDisplayName;
            }
#pragma warning disable CA1031 // A host provider is caller code; a defect must not reach the shell.
            catch (Exception exception)
#pragma warning restore CA1031
            {
                _error = ViewerBridgeText.Describe(
                    "Reading the bridge provider name",
                    exception);
            }
            _status = Sanitize(SafeGetStatus(provider));
            provider.StatusChanged += OnProviderStatusChanged;
        }
    }

    /// <summary>Gets whether a host injected a provider at all.</summary>
    internal bool HasProvider => _provider is not null;

    /// <summary>Gets the bounded provider name captured at the host seam.</summary>
    internal string ProviderDisplayName => _providerDisplayName;

    /// <summary>Gets how many notifications the bound discarded.</summary>
    internal long DroppedStatusEventCount => Interlocked.Read(ref _dropped);

    /// <summary>Gets the current view state without publishing it.</summary>
    internal ViewerBridgeViewState CurrentState => CreateState();

    /// <summary>Publishes the current view state, for example right after the window is wired.</summary>
    internal void Refresh() => Publish();

    /// <summary>Connects to <paramref name="sessionId"/>, or the host's default when null.</summary>
    /// <param name="sessionId">The opaque session identifier the operator chose.</param>
    /// <returns><see langword="true"/> when the provider accepted the request.</returns>
    internal Task<bool> ConnectAsync(string? sessionId) =>
        ExecuteAsync(
            "Connect",
            static state => state.CanConnect,
            (provider, token) => provider.ConnectAsync(
                new ViewerBridgeConnectRequest(ViewerBridgeText.Bound(sessionId)),
                token),
            configured: true);

    /// <summary>Ends the current session.</summary>
    /// <returns><see langword="true"/> when the provider accepted the request.</returns>
    internal Task<bool> DisconnectAsync() =>
        ExecuteAsync(
            "Disconnect",
            static state => state.CanDisconnect,
            static (provider, token) => provider.DisconnectAsync(token),
            configured: false);

    /// <summary>Drops the baseline and takes a fresh full snapshot.</summary>
    /// <returns><see langword="true"/> when the provider accepted the request.</returns>
    internal Task<bool> ResyncAsync() =>
        ExecuteAsync(
            "Resync",
            static state => state.CanResync,
            static (provider, token) => provider.ResyncAsync(token),
            configured: true);

    /// <summary>Reads the session choices the provider currently offers.</summary>
    /// <returns>
    /// A bounded, sanitized list. A provider failure yields an empty list and a redacted
    /// message in the view state rather than an exception.
    /// </returns>
    internal async Task<IReadOnlyList<ViewerBridgeSession>> GetSessionsAsync()
    {
        IViewerBridgeConnectionProvider? provider = _provider;
        if (provider is null || Volatile.Read(ref _disposed) != 0)
        {
            return [];
        }

        try
        {
            IReadOnlyList<ViewerBridgeSession> sessions =
                await provider.GetSessionsAsync(_lifetimeToken).ConfigureAwait(false);
            return Sanitize(sessions);
        }
        catch (OperationCanceledException)
        {
            SetError("Reading sessions was cancelled.");
            return [];
        }
#pragma warning disable CA1031 // A host provider is caller code; a defect in it must not reach the shell.
        catch (Exception exception)
#pragma warning restore CA1031
        {
            SetError(ViewerBridgeText.Describe("Reading sessions", exception));
            return [];
        }
    }

    /// <summary>Unsubscribes, cancels in-flight work, and refuses every later command.</summary>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        if (_provider is not null)
        {
            _provider.StatusChanged -= OnProviderStatusChanged;
        }
        _lifetime.Cancel();
        _lifetime.Dispose();
        using (_gate.EnterScope())
        {
            _pending.Clear();
        }
    }

    private async Task<bool> ExecuteAsync(
        string operation,
        Func<ViewerBridgeViewState, bool> permitted,
        Func<IViewerBridgeConnectionProvider, CancellationToken, ValueTask> action,
        bool configured)
    {
        IViewerBridgeConnectionProvider? provider = _provider;
        if (provider is null || Volatile.Read(ref _disposed) != 0 || !permitted(CreateState()))
        {
            return false;
        }

        using (_gate.EnterScope())
        {
            if (_busy)
            {
                return false;
            }
            _busy = true;
            _error = null;
        }
        Publish();

        bool succeeded = false;
        try
        {
            await action(provider, _lifetimeToken).ConfigureAwait(false);
            succeeded = true;
            using (_gate.EnterScope())
            {
                _configured = configured;
            }
            SetStatus(Sanitize(SafeGetStatus(provider)));
        }
        catch (OperationCanceledException)
        {
            SetError($"{operation} was cancelled.");
        }
#pragma warning disable CA1031 // A host provider is caller code; a defect in it must not reach the shell.
        catch (Exception exception)
#pragma warning restore CA1031
        {
            SetError(ViewerBridgeText.Describe(operation, exception));
        }
        finally
        {
            using (_gate.EnterScope())
            {
                _busy = false;
            }
            Publish();
        }
        return succeeded;
    }

    /// <summary>
    /// Accepts one provider notification. It runs on the provider's own thread - in the gRPC
    /// integration, inside the transport's observer callback - so it must never throw back at
    /// the caller: a Viewer-side defect that escaped here would be attributed to the transport's
    /// event reporting and, in the worst case, would end the session that raised it.
    /// </summary>
    private void OnProviderStatusChanged(object? sender, ViewerBridgeStatusChangedEventArgs e)
    {
        if (e is null || Volatile.Read(ref _disposed) != 0)
        {
            return;
        }

        try
        {
            bool schedule;
            using (_gate.EnterScope())
            {
                while (_pending.Count >= _capacity)
                {
                    _ = _pending.Dequeue();
                    Interlocked.Increment(ref _dropped);
                }
                _pending.Enqueue(Sanitize(e.Status));
                schedule = !_drainScheduled;
                _drainScheduled = true;
            }

            if (schedule)
            {
                _post(Drain);
            }
        }
#pragma warning disable CA1031 // The transport must never learn about a Viewer-side defect.
        catch (Exception exception)
#pragma warning restore CA1031
        {
            // A failed hand-off is a lost notification, which is exactly what the drop counter
            // already means, so it is counted rather than invented as a new kind of failure.
            Interlocked.Increment(ref _dropped);
            using (_gate.EnterScope())
            {
                _drainScheduled = false;
            }
            SetError(ViewerBridgeText.Describe("Receiving a bridge status", exception));
        }
    }

    private void Drain()
    {
        while (true)
        {
            ViewerBridgeStatus next;
            using (_gate.EnterScope())
            {
                if (_pending.Count == 0)
                {
                    _drainScheduled = false;
                    return;
                }
                next = _pending.Dequeue();
                _status = next;
                if (next.IsActive)
                {
                    _configured = true;
                }
            }
            PublishNow();
        }
    }

    private void SetStatus(ViewerBridgeStatus status)
    {
        using (_gate.EnterScope())
        {
            _status = status;
        }
    }

    private void SetError(string message)
    {
        using (_gate.EnterScope())
        {
            _error = ViewerBridgeText.Bound(message);
        }
    }

    private void Publish()
    {
        if (Volatile.Read(ref _disposed) != 0)
        {
            return;
        }
        _post(PublishNow);
    }

    private void PublishNow() => _publish(CreateState());

    private ViewerBridgeViewState CreateState()
    {
        IViewerBridgeConnectionProvider? provider = _provider;
        if (provider is null)
        {
            return ViewerBridgeViewState.Absent;
        }

        ViewerBridgeStatus status;
        string? error;
        bool busy;
        bool configured;
        using (_gate.EnterScope())
        {
            status = _status;
            error = _error;
            busy = _busy;
            configured = _configured;
        }

        bool available = SafeIsAvailable(provider) && Volatile.Read(ref _disposed) == 0;
        string name = _providerDisplayName;
        bool idle = status.State is ViewerBridgeConnectionState.Disconnected
            or ViewerBridgeConnectionState.Faulted;
        return new ViewerBridgeViewState(
            MenuVisible: true,
            MenuEnabled: available,
            MenuToolTip: available
                ? $"Connect to a {name} session."
                : $"{name} is installed but not available right now.",
            StatusVisible: configured || status.IsActive || error is not null,
            StatusText: FormatStatus(status, error, busy),
            CanConnect: available && !busy && idle,
            CanDisconnect: available && !busy && !idle,
            CanResync: available && !busy &&
                status.State is ViewerBridgeConnectionState.Streaming
                    or ViewerBridgeConnectionState.Resynchronizing,
            Busy: busy,
            Status: status,
            ErrorMessage: error,
            DroppedStatusEventCount: Interlocked.Read(ref _dropped));
    }

    private static string FormatStatus(ViewerBridgeStatus status, string? error, bool busy)
    {
        string state = busy ? "working" : DescribeState(status.State);
        var text = new StringBuilder("Bridge: ").Append(state);
        if (ViewerBridgeText.Bound(status.SessionId) is { } session)
        {
            text.Append(" \u00b7 ").Append(session);
        }
        if (ViewerBridgeEndpoint.Redact(status.Endpoint) is { } endpoint)
        {
            text.Append(" \u00b7 ").Append(endpoint);
        }
        string? detail = error ?? ViewerBridgeText.Bound(status.Detail);
        if (detail is not null)
        {
            text.Append(" \u2014 ").Append(detail);
        }
        return ViewerBridgeText.Bound(text.ToString(), ViewerBridgeLimits.MaxTextLength * 2) ??
            string.Empty;
    }

    private static string DescribeState(ViewerBridgeConnectionState state) => state switch
    {
        ViewerBridgeConnectionState.Disconnected => "disconnected",
        ViewerBridgeConnectionState.Connecting => "connecting",
        ViewerBridgeConnectionState.Negotiating => "negotiating",
        ViewerBridgeConnectionState.Resynchronizing => "resynchronizing",
        ViewerBridgeConnectionState.Streaming => "streaming",
        ViewerBridgeConnectionState.Reconnecting => "reconnecting",
        ViewerBridgeConnectionState.Faulted => "faulted",
        _ => "unknown"
    };

    private static ViewerBridgeStatus Sanitize(ViewerBridgeStatus status) => status with
    {
        SessionId = ViewerBridgeText.Bound(status.SessionId),
        Endpoint = ViewerBridgeEndpoint.Redact(status.Endpoint),
        Detail = ViewerBridgeText.Bound(status.Detail),
        ConnectAttemptCount = Math.Max(status.ConnectAttemptCount, 0),
        AppliedUpdateCount = Math.Max(status.AppliedUpdateCount, 0),
        PendingOutboundCount = Math.Max(status.PendingOutboundCount, 0)
    };

    private static List<ViewerBridgeSession> Sanitize(
        IReadOnlyList<ViewerBridgeSession>? sessions)
    {
        if (sessions is null || sessions.Count == 0)
        {
            return [];
        }

        int count = Math.Min(sessions.Count, ViewerBridgeLimits.MaxSessionCount);
        List<ViewerBridgeSession> bounded = new(count);
        for (int index = 0; index < count; index++)
        {
            ViewerBridgeSession? session = sessions[index];
            if (session is null || ViewerBridgeText.Bound(session.Id) is not { } id)
            {
                continue;
            }
            bounded.Add(new ViewerBridgeSession(
                id,
                ViewerBridgeText.Bound(session.DisplayName) ?? id,
                ViewerBridgeText.Bound(session.Description)));
        }
        return bounded;
    }

    private ViewerBridgeStatus SafeGetStatus(IViewerBridgeConnectionProvider provider)
    {
        try
        {
            return provider.GetStatus();
        }
#pragma warning disable CA1031 // A host provider is caller code; a defect in it must not reach the shell.
        catch (Exception exception)
#pragma warning restore CA1031
        {
            SetError(ViewerBridgeText.Describe("Reading bridge status", exception));
            return ViewerBridgeStatus.Disconnected;
        }
    }

    private bool SafeIsAvailable(IViewerBridgeConnectionProvider provider)
    {
        try
        {
            return provider.IsAvailable;
        }
#pragma warning disable CA1031 // A host provider is caller code; a defect in it must not reach the shell.
        catch (Exception exception)
#pragma warning restore CA1031
        {
            SetError(ViewerBridgeText.Describe("Reading bridge availability", exception));
            return false;
        }
    }
}
