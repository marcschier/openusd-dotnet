// Copyright (c) marcschier. Licensed under the MIT License.

using OpenUsd.Bridge.Protocol;
using OpenUsd.LiveAuthoring;

namespace OpenUsd.Bridge.Grpc;

/// <summary>
/// Configures one bridge client: where it connects, how it authenticates, what it will accept, and
/// how it backs off when the peer is gone.
/// </summary>
/// <remarks>
/// <para>
/// The defaults are the safe ones. A client connects to loopback only, requires a credential
/// provider, bounds every call with a deadline, bounds every frame with the protocol's own frame
/// budget, and retries only read-only calls. Widening any of those is an explicit decision the host
/// records here rather than a behaviour it inherits.
/// </para>
/// <para>
/// <see cref="Validate"/> is called by the client constructor, so a misconfiguration fails at
/// construction rather than at the first reconnect.
/// </para>
/// </remarks>
public sealed class BridgeClientOptions
{
    /// <summary>The default per-call deadline.</summary>
    public static readonly TimeSpan DefaultCallDeadline = TimeSpan.FromSeconds(30);

    /// <summary>The default initial reconnect backoff.</summary>
    public static readonly TimeSpan DefaultInitialBackoff = TimeSpan.FromMilliseconds(250);

    /// <summary>The default maximum reconnect backoff.</summary>
    public static readonly TimeSpan DefaultMaxBackoff = TimeSpan.FromSeconds(30);

    /// <summary>The default transport keepalive ping interval.</summary>
    public static readonly TimeSpan DefaultKeepAliveInterval = TimeSpan.FromSeconds(20);

    /// <summary>The default transport keepalive ping timeout.</summary>
    public static readonly TimeSpan DefaultKeepAliveTimeout = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Gets or sets the peer endpoint. The default is loopback on the conventional bridge port; a
    /// non-loopback host requires both <see cref="AllowNonLoopback"/> and an <c>https</c> scheme.
    /// </summary>
    public Uri Endpoint { get; set; } = new("http://127.0.0.1:53017");

    /// <summary>
    /// Gets or sets whether a non-loopback endpoint is permitted. The default is
    /// <see langword="false"/>: a bridge is a local process boundary, and exposing it on a routable
    /// address is a deployment decision, not a default.
    /// </summary>
    public bool AllowNonLoopback { get; set; }

    /// <summary>
    /// Gets or sets the credential provider. It is required: there is no anonymous mode, because an
    /// unauthenticated local socket is reachable by every process on the machine.
    /// </summary>
    public IBridgeCallCredentialProvider? Credentials { get; set; }

    /// <summary>Gets or sets the absolute prim path reserved for the bridge-owned overlay.</summary>
    /// <remarks>
    /// It must match the coordinator's own bridge root. The client checks that at construction, so
    /// a mismatch cannot reach a peer as a negotiated path the coordinator will then reject.
    /// </remarks>
    public string BridgeRootPath { get; set; } = "/Bridge";

    /// <summary>
    /// Gets or sets the opaque origin identifier naming this publisher, or <see langword="null"/> to
    /// adopt the coordinator's own <see cref="LiveAuthoringSessionCoordinator.LocalOriginId"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// It is advertised during negotiation so the peer can echo it, which is what lets the
    /// coordinator suppress echoes of local edits instead of reapplying them, and it is part of
    /// every derived idempotency key. Two publishers that share one identifier therefore suppress
    /// each other's edits and derive colliding keys a peer reads as replays.
    /// </para>
    /// <para>
    /// The default is <see langword="null"/> rather than a shared literal: adopting the
    /// coordinator's resolved identity makes the two sides agree by construction, and that identity
    /// is itself per-instance unique unless the host chose otherwise. A value set here must equal
    /// the coordinator's, and the client checks that at construction.
    /// </para>
    /// </remarks>
    public string? LocalOriginId { get; set; }

    /// <summary>Gets or sets the per-call deadline.</summary>
    public TimeSpan CallDeadline { get; set; } = DefaultCallDeadline;

    /// <summary>Gets or sets the initial reconnect backoff.</summary>
    public TimeSpan InitialBackoff { get; set; } = DefaultInitialBackoff;

    /// <summary>Gets or sets the maximum reconnect backoff.</summary>
    public TimeSpan MaxBackoff { get; set; } = DefaultMaxBackoff;

    /// <summary>Gets or sets the transport keepalive ping interval.</summary>
    public TimeSpan KeepAliveInterval { get; set; } = DefaultKeepAliveInterval;

    /// <summary>Gets or sets the transport keepalive ping timeout.</summary>
    public TimeSpan KeepAliveTimeout { get; set; } = DefaultKeepAliveTimeout;

    /// <summary>Gets or sets the maximum receive size, bounded by the protocol frame budget.</summary>
    public long MaxReceiveMessageBytes { get; set; } = BridgeProtocol.MaxFrameBytes;

    /// <summary>Gets or sets the maximum send size, bounded by the protocol frame budget.</summary>
    public long MaxSendMessageBytes { get; set; } = BridgeProtocol.MaxFrameBytes;

    /// <summary>
    /// Gets or sets how many attempts a read-only call may make. Mutating calls are never retried
    /// by the transport; see <see cref="OmniverseBridgeClient"/> for the explicit publish rule.
    /// </summary>
    public int MaxReadOnlyCallAttempts { get; set; } = 3;

    /// <summary>
    /// Gets or sets how many transport attempts one local publication may make in total, across
    /// reconnects. Every attempt carries the same idempotency key, so a peer that already observed
    /// an earlier attempt recognizes the replay instead of applying it twice.
    /// </summary>
    /// <remarks>
    /// This bounds retry, it does not remove it. A transport failure never proves the peer did not
    /// act, so abandoning the batch after one failure would silently lose an authoritative local
    /// edit; retrying it forever would let one unreachable peer stall the queue.
    /// </remarks>
    public int MaxPublishAttempts { get; set; } = 3;

    /// <summary>
    /// Gets or sets how many local batches may wait in the outbound channel before
    /// <see cref="OmniverseBridgeClient.PublishLocalBatchAsync"/> refuses a new one. The same bound
    /// applies to the retry queue that preserves batches across a reconnect.
    /// </summary>
    public int OutboundQueueCapacity { get; set; } = 64;

    /// <summary>
    /// Gets or sets the opaque identifier of a session this client asks the peer to join, or
    /// <see langword="null"/> to let the peer choose.
    /// </summary>
    /// <remarks>
    /// <para>
    /// It is a request, not an assertion. A peer is free to answer with a different session, and
    /// the identifier the peer returns is the one the client adopts and then carries on the stream
    /// handshake, so a rejoin cannot silently target a session the peer never agreed to.
    /// </para>
    /// <para>
    /// The value is bounded and validated like every other opaque identity on the wire, because it
    /// reaches the peer inside a handshake this client sends before anything is authenticated
    /// end-to-end.
    /// </para>
    /// </remarks>
    public string? RequestedSessionId { get; set; }

    /// <summary>
    /// Gets or sets an optional observer for bounded, redacted client events. A
    /// <see langword="null"/> observer disables event reporting; status polling stays available.
    /// </summary>
    public IProgress<BridgeClientEvent>? Observer { get; set; }

    /// <summary>Returns an independent copy of these options.</summary>
    /// <remarks>
    /// <para>
    /// An integration that needs to observe a client it did not configure - the Viewer's bridge
    /// provider is the motivating case - must not reach into the host's own options object to
    /// install its observer. Doing so mutates state the host still owns and, when the host reuses
    /// one options instance across reconnects, chains a new observer onto every previous one and
    /// keeps each of them alive. Copying first makes the composition local to one client.
    /// </para>
    /// <para>
    /// <see cref="Credentials"/> and <see cref="Observer"/> are copied by reference on purpose:
    /// both are live host services, and duplicating a credential provider would either be
    /// impossible or would defeat the per-attempt acquisition the security model depends on. The
    /// copy is otherwise complete, so mutating the returned instance never affects the original.
    /// </para>
    /// </remarks>
    public BridgeClientOptions Clone() => new()
    {
        Endpoint = Endpoint,
        AllowNonLoopback = AllowNonLoopback,
        Credentials = Credentials,
        BridgeRootPath = BridgeRootPath,
        LocalOriginId = LocalOriginId,
        RequestedSessionId = RequestedSessionId,
        CallDeadline = CallDeadline,
        InitialBackoff = InitialBackoff,
        MaxBackoff = MaxBackoff,
        KeepAliveInterval = KeepAliveInterval,
        KeepAliveTimeout = KeepAliveTimeout,
        MaxReceiveMessageBytes = MaxReceiveMessageBytes,
        MaxSendMessageBytes = MaxSendMessageBytes,
        MaxReadOnlyCallAttempts = MaxReadOnlyCallAttempts,
        MaxPublishAttempts = MaxPublishAttempts,
        OutboundQueueCapacity = OutboundQueueCapacity,
        Observer = Observer
    };

    /// <summary>Validates the options and throws when a value would weaken a documented guarantee.</summary>
    public void Validate()
    {
        ArgumentNullException.ThrowIfNull(Endpoint);
        if (!Endpoint.IsAbsoluteUri)
        {
            throw new ArgumentException(
                "The bridge endpoint must be an absolute URI.",
                nameof(Endpoint));
        }
        if (!string.Equals(Endpoint.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(Endpoint.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                "The bridge endpoint must use http or https.",
                nameof(Endpoint));
        }

        bool loopback = Endpoint.IsLoopback;
        if (!loopback && !AllowNonLoopback)
        {
            throw new ArgumentException(
                "The bridge connects to loopback by default. Set AllowNonLoopback to connect to " +
                $"'{Endpoint.Host}', and use https so the session is not carried in clear text.",
                nameof(Endpoint));
        }
        if (!loopback &&
            !string.Equals(Endpoint.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                "A non-loopback bridge endpoint requires https. There is no opt-out: a routable " +
                "endpoint without transport security exposes both the session and its credential.",
                nameof(Endpoint));
        }

        if (Credentials is null)
        {
            throw new ArgumentException(
                "A credential provider is required. A local socket without authentication is " +
                "reachable by every process on the machine.",
                nameof(Credentials));
        }

        BridgeClientValidation.ValidateBridgeRootPath(BridgeRootPath, nameof(BridgeRootPath));
        if (LocalOriginId is not null)
        {
            BridgeClientValidation.ValidateOpaqueIdentity(
                LocalOriginId,
                nameof(LocalOriginId),
                "A local origin identifier");
        }
        if (RequestedSessionId is not null)
        {
            BridgeClientValidation.ValidateOpaqueIdentity(
                RequestedSessionId,
                nameof(RequestedSessionId),
                "A requested session identifier");
        }

        ValidatePositive(CallDeadline, nameof(CallDeadline));
        ValidatePositive(InitialBackoff, nameof(InitialBackoff));
        ValidatePositive(MaxBackoff, nameof(MaxBackoff));
        ValidatePositive(KeepAliveInterval, nameof(KeepAliveInterval));
        ValidatePositive(KeepAliveTimeout, nameof(KeepAliveTimeout));
        if (MaxBackoff < InitialBackoff)
        {
            throw new ArgumentException(
                "MaxBackoff cannot be shorter than InitialBackoff.",
                nameof(MaxBackoff));
        }

        ValidateFrameBudget(MaxReceiveMessageBytes, nameof(MaxReceiveMessageBytes));
        ValidateFrameBudget(MaxSendMessageBytes, nameof(MaxSendMessageBytes));
        ArgumentOutOfRangeException.ThrowIfLessThan(
            MaxReadOnlyCallAttempts,
            1,
            nameof(MaxReadOnlyCallAttempts));
        ArgumentOutOfRangeException.ThrowIfGreaterThan(
            MaxReadOnlyCallAttempts,
            10,
            nameof(MaxReadOnlyCallAttempts));
        ArgumentOutOfRangeException.ThrowIfLessThan(
            MaxPublishAttempts,
            1,
            nameof(MaxPublishAttempts));
        ArgumentOutOfRangeException.ThrowIfGreaterThan(
            MaxPublishAttempts,
            10,
            nameof(MaxPublishAttempts));
        ArgumentOutOfRangeException.ThrowIfLessThan(
            OutboundQueueCapacity,
            1,
            nameof(OutboundQueueCapacity));
        ArgumentOutOfRangeException.ThrowIfGreaterThan(
            OutboundQueueCapacity,
            LiveAuthoringValidation.MaxReplayWindowLength,
            nameof(OutboundQueueCapacity));
    }

    private static void ValidatePositive(TimeSpan value, string parameterName)
    {
        if (value <= TimeSpan.Zero)
        {
            throw new ArgumentException($"{parameterName} must be positive.", parameterName);
        }
        if (value > TimeSpan.FromMinutes(10))
        {
            throw new ArgumentException(
                $"{parameterName} cannot exceed ten minutes; every wait in this client is bounded.",
                parameterName);
        }
    }

    private static void ValidateFrameBudget(long value, string parameterName)
    {
        if (value <= 0)
        {
            throw new ArgumentException($"{parameterName} must be positive.", parameterName);
        }
        if (value > BridgeProtocol.MaxFrameBytes)
        {
            throw new ArgumentException(
                $"{parameterName} cannot exceed the protocol frame budget of " +
                $"{BridgeProtocol.MaxFrameBytes} bytes.",
                parameterName);
        }
    }
}

/// <summary>Bounded checks shared by the client options and the client itself.</summary>
internal static class BridgeClientValidation
{
    internal static void ValidateOpaqueIdentity(string? value, string parameterName, string description)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException($"{description} cannot be empty.", parameterName);
        }
        if (value.Length > LiveAuthoringValidation.MaxOpaqueIdLength)
        {
            throw new ArgumentException(
                $"{description} cannot exceed {LiveAuthoringValidation.MaxOpaqueIdLength} characters.",
                parameterName);
        }

        // Rejected here, before the identifier is UTF-8 encoded onto the wire or folded into an
        // idempotency key: the default encoder silently maps an unpaired surrogate to U+FFFD, so two
        // different identifiers would otherwise collide into one on the peer.
        if (!LiveAuthoringValidation.IsWellFormedUtf16(value))
        {
            throw new ArgumentException(
                $"{description} cannot contain an unpaired surrogate: such a value has no UTF-8 " +
                "encoding, so two different identifiers would hash and compare as one.",
                parameterName);
        }
    }

    internal static void ValidateBridgeRootPath(string? value, string parameterName)
    {
        if (string.IsNullOrEmpty(value) || value[0] != '/' || value.Length == 1)
        {
            throw new ArgumentException(
                "A bridge root path must be an absolute prim path below the pseudo-root.",
                parameterName);
        }
        if (value.Length > LiveAuthoringValidation.MaxPathLength)
        {
            throw new ArgumentException(
                $"A bridge root path cannot exceed {LiveAuthoringValidation.MaxPathLength} characters.",
                parameterName);
        }
    }
}
