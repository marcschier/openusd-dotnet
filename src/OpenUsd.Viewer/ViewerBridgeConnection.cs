// Copyright (c) marcschier. Licensed under the MIT License.

namespace OpenUsd.Viewer;

/// <summary>
/// The renderer-neutral connection state a host reports for a live bridge session.
/// </summary>
/// <remarks>
/// The names deliberately mirror what an operator sees rather than a transport's own state
/// machine: the Viewer never learns whether a session is carried over gRPC, a pipe, or an
/// in-process loop, so nothing here may imply a transport, a credential, or a native handle.
/// </remarks>
public enum ViewerBridgeConnectionState
{
    /// <summary>No session is established and none is being attempted.</summary>
    Disconnected = 0,

    /// <summary>A session is being established.</summary>
    Connecting = 1,

    /// <summary>A session is established and agreeing versions and capabilities.</summary>
    Negotiating = 2,

    /// <summary>A full baseline is being requested and applied.</summary>
    Resynchronizing = 3,

    /// <summary>A baseline is in place and ordered updates are flowing.</summary>
    Streaming = 4,

    /// <summary>The session dropped and the host is retrying on its own schedule.</summary>
    Reconnecting = 5,

    /// <summary>The session stopped for a reason a retry cannot fix.</summary>
    Faulted = 6
}

/// <summary>
/// The bounds every value crossing the Viewer's bridge seam is held to.
/// </summary>
/// <remarks>
/// A provider runs in the host's process and can be as wrong as any other caller-supplied
/// code. Bounding here, rather than trusting the provider, is what keeps one misbehaving
/// integration from growing the Viewer's status text without limit, filling the marshalling
/// queue, or pushing an unbounded session list into a menu.
/// </remarks>
public static class ViewerBridgeLimits
{
    /// <summary>The maximum length of any single displayed string.</summary>
    public const int MaxTextLength = 200;

    /// <summary>The maximum number of session choices the Viewer will offer.</summary>
    public const int MaxSessionCount = 64;

    /// <summary>The maximum number of provider notifications held for the UI thread.</summary>
    public const int MaxPendingStatusEvents = 32;
}

/// <summary>
/// One session a host offers the operator, identified by an opaque identifier the Viewer
/// never interprets.
/// </summary>
/// <param name="Id">The opaque session identifier passed back on connect.</param>
/// <param name="DisplayName">The label shown in the session list.</param>
/// <param name="Description">Optional secondary text, for example a redacted endpoint.</param>
public sealed record ViewerBridgeSession(string Id, string DisplayName, string? Description = null);

/// <summary>
/// A bounded, detached, point-in-time view of a bridge session's health.
/// </summary>
/// <remarks>
/// Every member is a value or an immutable string, so a snapshot handed to the UI thread can
/// never be mutated afterwards by the provider that produced it. Nothing here carries a
/// credential, a token, a native handle, or an authored payload; <paramref name="Endpoint"/>
/// is a display string a host has already redacted, and the Viewer redacts it again.
/// </remarks>
/// <param name="State">The connection state.</param>
/// <param name="SessionId">The opaque identifier of the connected session, when there is one.</param>
/// <param name="Endpoint">A redacted endpoint description, when the host chooses to show one.</param>
/// <param name="ConnectAttemptCount">How many connection attempts the host has made.</param>
/// <param name="AppliedUpdateCount">How many inbound updates the host has applied.</param>
/// <param name="PendingOutboundCount">How many local batches are waiting to be published.</param>
/// <param name="TimestampUtc">When the host produced the snapshot.</param>
/// <param name="Detail">Bounded, redacted detail, for example the reason a session faulted.</param>
public readonly record struct ViewerBridgeStatus(
    ViewerBridgeConnectionState State,
    string? SessionId,
    string? Endpoint,
    long ConnectAttemptCount,
    long AppliedUpdateCount,
    int PendingOutboundCount,
    DateTimeOffset TimestampUtc,
    string? Detail)
{
    /// <summary>A disconnected snapshot with no session, no endpoint, and no detail.</summary>
    public static ViewerBridgeStatus Disconnected { get; } = new(
        ViewerBridgeConnectionState.Disconnected,
        SessionId: null,
        Endpoint: null,
        ConnectAttemptCount: 0,
        AppliedUpdateCount: 0,
        PendingOutboundCount: 0,
        TimestampUtc: default,
        Detail: null);

    /// <summary>
    /// Returns whether the session is doing work an operator would expect to see reported,
    /// which is every state except a settled <see cref="ViewerBridgeConnectionState.Disconnected"/>.
    /// </summary>
    public bool IsActive => State != ViewerBridgeConnectionState.Disconnected;
}

/// <summary>Carries a detached <see cref="ViewerBridgeStatus"/> from a host to the Viewer.</summary>
public sealed class ViewerBridgeStatusChangedEventArgs : EventArgs
{
    /// <summary>Initializes the event with the snapshot the host observed.</summary>
    /// <param name="status">The detached snapshot.</param>
    public ViewerBridgeStatusChangedEventArgs(ViewerBridgeStatus status) => Status = status;

    /// <summary>Gets the detached snapshot the host observed.</summary>
    public ViewerBridgeStatus Status { get; }
}

/// <summary>
/// What the operator asked the host to connect to.
/// </summary>
/// <param name="SessionId">
/// The opaque identifier of a session the host previously offered, or <see langword="null"/>
/// to let the host use its own configured default.
/// </param>
public sealed record ViewerBridgeConnectRequest(string? SessionId = null);

/// <summary>
/// The seam a host implements to expose one live bridge session to the Viewer.
/// </summary>
/// <remarks>
/// <para>
/// This is deliberately the whole contract. There is no static registration and no
/// discovery: a provider only reaches the Viewer by being handed to
/// <see cref="ViewerHostOptions.BridgeConnection"/>, so a process that never injects one has
/// no bridge surface at all, and an embedding host cannot acquire one by accident through a
/// transitive package reference.
/// </para>
/// <para>
/// The Viewer calls these members from a background continuation, never from a render or
/// input path, and never awaits one of them while holding the UI thread. An implementation
/// may therefore take as long as its transport does. It must not, however, assume the Viewer
/// is on any particular thread: <see cref="StatusChanged"/> is expected to be raised from
/// whatever thread the host's own transport uses, and the Viewer marshals the detached
/// snapshot itself.
/// </para>
/// <para>
/// An implementation receives its endpoint, credential provider, and configuration
/// programmatically from the embedding host. Nothing in this contract lets the Viewer read,
/// write, persist, or display a credential.
/// </para>
/// </remarks>
public interface IViewerBridgeConnectionProvider
{
    /// <summary>
    /// Gets the short name shown for this integration, for example the product whose bridge
    /// it speaks to.
    /// </summary>
    string DisplayName { get; }

    /// <summary>
    /// Gets whether the provider can currently be used. A provider that is injected but
    /// reports <see langword="false"/> keeps the menu entry visible and disabled, which tells
    /// an operator the integration exists but is not usable right now; that is strictly more
    /// useful than hiding it and leaving them to guess.
    /// </summary>
    bool IsAvailable { get; }

    /// <summary>Returns the current detached status snapshot.</summary>
    ViewerBridgeStatus GetStatus();

    /// <summary>Returns the sessions the operator may choose between.</summary>
    /// <param name="cancellationToken">Cancels the enumeration.</param>
    /// <returns>
    /// A bounded list; the Viewer keeps at most
    /// <see cref="ViewerBridgeLimits.MaxSessionCount"/> entries. An empty list means the host
    /// exposes no explicit choice and connect uses the host's own default.
    /// </returns>
    ValueTask<IReadOnlyList<ViewerBridgeSession>> GetSessionsAsync(
        CancellationToken cancellationToken = default);

    /// <summary>Connects, or reconnects, to the requested session.</summary>
    /// <param name="request">The session the operator chose.</param>
    /// <param name="cancellationToken">Cancels the attempt.</param>
    ValueTask ConnectAsync(
        ViewerBridgeConnectRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>Ends the current session.</summary>
    /// <param name="cancellationToken">Cancels the attempt.</param>
    ValueTask DisconnectAsync(CancellationToken cancellationToken = default);

    /// <summary>Drops the current baseline and takes a fresh full snapshot.</summary>
    /// <param name="cancellationToken">Cancels the attempt.</param>
    ValueTask ResyncAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Raised when the host observes a new status. Handlers are cheap and non-blocking: the
    /// Viewer enqueues the detached snapshot and returns, so a host may raise this from its
    /// transport thread.
    /// </summary>
    event EventHandler<ViewerBridgeStatusChangedEventArgs>? StatusChanged;
}

/// <summary>
/// Turns an endpoint into something safe to display.
/// </summary>
/// <remarks>
/// This is public because the integration packages that adapt a real transport need exactly
/// the same rule the Viewer applies, and two independent implementations of a redaction rule
/// is one implementation too many. Userinfo, query, and fragment are dropped outright rather
/// than masked: a masked value still tells an observer how long a token was.
/// </remarks>
public static class ViewerBridgeEndpoint
{
    /// <summary>Returns a redacted, bounded display form of <paramref name="endpoint"/>.</summary>
    /// <param name="endpoint">The endpoint to redact.</param>
    /// <returns>Scheme, host, port, and path only.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="endpoint"/> is <c>null</c>.</exception>
    public static string Redact(Uri endpoint)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        if (!endpoint.IsAbsoluteUri)
        {
            return ViewerBridgeText.Bound(endpoint.OriginalString) ?? string.Empty;
        }

        string path = endpoint.AbsolutePath;
        if (string.Equals(path, "/", StringComparison.Ordinal))
        {
            path = string.Empty;
        }
        string authority = endpoint.IsDefaultPort
            ? endpoint.Host
            : $"{endpoint.Host}:{endpoint.Port}";
        return ViewerBridgeText.Bound($"{endpoint.Scheme}://{authority}{path}") ?? string.Empty;
    }

    /// <summary>
    /// Returns a redacted, bounded display form of <paramref name="endpoint"/>, or
    /// <see langword="null"/> when there is nothing to show.
    /// </summary>
    /// <param name="endpoint">The endpoint text to redact.</param>
    public static string? Redact(string? endpoint)
    {
        if (string.IsNullOrWhiteSpace(endpoint))
        {
            return null;
        }
        return Uri.TryCreate(endpoint.Trim(), UriKind.Absolute, out Uri? parsed)
            ? Redact(parsed)
            : ViewerBridgeText.Bound(endpoint);
    }
}
