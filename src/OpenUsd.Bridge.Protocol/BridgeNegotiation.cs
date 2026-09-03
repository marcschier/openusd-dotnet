// Copyright (c) marcschier. Licensed under the MIT License.

using OpenUsd.LiveAuthoring;

namespace OpenUsd.Bridge.Protocol;

/// <summary>Identifies why a handshake was refused, before any mutation was attempted.</summary>
public enum BridgeHandshakeRejection
{
    /// <summary>The handshake was accepted.</summary>
    None = 0,

    /// <summary>The peer's major protocol version is not supported.</summary>
    Version,

    /// <summary>A capability this session requires is not advertised by both peers.</summary>
    Capability,

    /// <summary>The peer's declared limits cannot satisfy this session.</summary>
    Limits,

    /// <summary>
    /// Authentication or authorization failed. The detail never carries the credential.
    /// </summary>
    Unauthenticated,

    /// <summary>The peer does not own the requested bridge root path.</summary>
    BridgeRoot,

    /// <summary>The peer refuses new sessions, for example while draining.</summary>
    Unavailable,

    /// <summary>A field was missing, malformed, or out of range.</summary>
    Malformed
}

/// <summary>
/// The client's negotiation offer: the protocol version it speaks, the capabilities it supports, the
/// bounds it enforces, the origin identifier that names it, and the bridge root it reserves.
/// </summary>
/// <remarks>
/// A handshake never carries a credential. Authentication is a transport concern: the gRPC adapter
/// presents call credentials, and this message stays safe to log after redaction of nothing at all.
/// </remarks>
public sealed class BridgeHandshakeRequest
{
    /// <summary>Initializes a negotiation offer.</summary>
    /// <param name="version">The protocol version the client speaks.</param>
    /// <param name="capabilities">The capabilities the client supports.</param>
    /// <param name="clientOriginId">The opaque origin identifier naming the client process.</param>
    /// <param name="bridgeRootPath">The absolute prim path the client reserves for the overlay.</param>
    /// <param name="limits">The bounds the client enforces.</param>
    /// <param name="requestedSessionId">An optional session identifier to rejoin.</param>
    /// <param name="correlationId">An optional opaque tracing identifier.</param>
    public BridgeHandshakeRequest(
        BridgeProtocolVersion version,
        IEnumerable<BridgeCapability> capabilities,
        string clientOriginId,
        string bridgeRootPath,
        BridgeLimits limits,
        string? requestedSessionId = null,
        string? correlationId = null)
    {
        ArgumentNullException.ThrowIfNull(capabilities);
        BridgeValidation.ValidateOpaqueIdentity(
            clientOriginId,
            nameof(clientOriginId),
            "A client origin identifier");
        BridgeValidation.ValidateBridgeRootPath(bridgeRootPath, nameof(bridgeRootPath));
        if (requestedSessionId is not null)
        {
            BridgeValidation.ValidateOpaqueIdentity(
                requestedSessionId,
                nameof(requestedSessionId),
                "A session identifier");
        }
        BridgeValidation.ValidateOptionalCorrelationId(correlationId, nameof(correlationId));

        BridgeCapability[] materialized = [.. capabilities];
        foreach (BridgeCapability capability in materialized)
        {
            if (!BridgeProtocol.IsSupported(capability))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(capabilities),
                    capability,
                    "An unknown capability cannot be advertised.");
            }
        }

        Version = version;
        Capabilities = Array.AsReadOnly(materialized);
        ClientOriginId = clientOriginId;
        BridgeRootPath = bridgeRootPath;
        Limits = limits;
        RequestedSessionId = requestedSessionId;
        CorrelationId = correlationId;
    }

    /// <summary>Gets the protocol version the client speaks.</summary>
    public BridgeProtocolVersion Version { get; }

    /// <summary>Gets the capabilities the client supports.</summary>
    public IReadOnlyList<BridgeCapability> Capabilities { get; }

    /// <summary>Gets the opaque origin identifier naming the client process.</summary>
    public string ClientOriginId { get; }

    /// <summary>Gets the absolute prim path the client reserves for the bridge-owned overlay.</summary>
    public string BridgeRootPath { get; }

    /// <summary>Gets the bounds the client enforces.</summary>
    public BridgeLimits Limits { get; }

    /// <summary>Gets the optional session identifier the client wants to rejoin.</summary>
    public string? RequestedSessionId { get; }

    /// <summary>Gets the optional opaque tracing identifier.</summary>
    public string? CorrelationId { get; }

    /// <summary>
    /// Creates the offer this implementation makes: the current version, every supported capability,
    /// and the locally enforced bounds.
    /// </summary>
    public static BridgeHandshakeRequest CreateLocal(
        string clientOriginId,
        string bridgeRootPath,
        string? requestedSessionId = null,
        string? correlationId = null) =>
        new(
            BridgeProtocol.Version,
            BridgeProtocol.SupportedCapabilities,
            clientOriginId,
            bridgeRootPath,
            BridgeLimits.Local,
            requestedSessionId,
            correlationId);
}

/// <summary>The peer's answer to a negotiation offer.</summary>
public sealed class BridgeHandshakeResponse
{
    /// <summary>Initializes a negotiation answer.</summary>
    /// <param name="accepted">Whether the peer accepted the offer.</param>
    /// <param name="version">The protocol version the peer speaks.</param>
    /// <param name="capabilities">The capabilities the peer supports.</param>
    /// <param name="epoch">The authoritative remote identity for the session, when accepted.</param>
    /// <param name="bridgeRootPath">The bridge root path the peer owns.</param>
    /// <param name="effectiveLimits">The bounds the peer proposes for the session.</param>
    /// <param name="rejection">Why the offer was refused, when it was refused.</param>
    /// <param name="detail">A bounded, redacted detail.</param>
    public BridgeHandshakeResponse(
        bool accepted,
        BridgeProtocolVersion version,
        IEnumerable<BridgeCapability> capabilities,
        LiveAuthoringRemoteEpoch? epoch,
        string bridgeRootPath,
        BridgeLimits effectiveLimits,
        BridgeHandshakeRejection rejection = BridgeHandshakeRejection.None,
        string? detail = null)
    {
        ArgumentNullException.ThrowIfNull(capabilities);
        ArgumentNullException.ThrowIfNull(bridgeRootPath);

        Accepted = accepted;
        Version = version;
        Capabilities = Array.AsReadOnly((BridgeCapability[])[.. capabilities]);
        Epoch = epoch;
        BridgeRootPath = bridgeRootPath;
        EffectiveLimits = effectiveLimits;
        Rejection = rejection;
        Detail = detail;
    }

    /// <summary>Gets whether the peer accepted the offer.</summary>
    public bool Accepted { get; }

    /// <summary>Gets the protocol version the peer speaks.</summary>
    public BridgeProtocolVersion Version { get; }

    /// <summary>Gets the capabilities the peer supports.</summary>
    public IReadOnlyList<BridgeCapability> Capabilities { get; }

    /// <summary>Gets the authoritative remote identity for the session, when accepted.</summary>
    public LiveAuthoringRemoteEpoch? Epoch { get; }

    /// <summary>Gets the bridge root path the peer owns.</summary>
    public string BridgeRootPath { get; }

    /// <summary>Gets the bounds the peer proposes for the session.</summary>
    public BridgeLimits EffectiveLimits { get; }

    /// <summary>Gets why the offer was refused, when it was refused.</summary>
    public BridgeHandshakeRejection Rejection { get; }

    /// <summary>Gets the bounded, redacted detail.</summary>
    public string? Detail { get; }
}

/// <summary>The bounded outcome of evaluating a peer's answer against the local offer.</summary>
public sealed class BridgeNegotiationResult
{
    internal BridgeNegotiationResult(
        bool accepted,
        BridgeHandshakeRejection rejection,
        BridgeProtocolVersion peerVersion,
        BridgeLimits effectiveLimits,
        IReadOnlyList<BridgeCapability> agreedCapabilities,
        LiveAuthoringRemoteEpoch? epoch,
        string detail)
    {
        Accepted = accepted;
        Rejection = rejection;
        PeerVersion = peerVersion;
        EffectiveLimits = effectiveLimits;
        AgreedCapabilities = agreedCapabilities;
        Epoch = epoch;
        Detail = detail;
    }

    /// <summary>Gets whether the session may proceed.</summary>
    public bool Accepted { get; }

    /// <summary>Gets why the session may not proceed.</summary>
    public BridgeHandshakeRejection Rejection { get; }

    /// <summary>Gets the peer's protocol version.</summary>
    public BridgeProtocolVersion PeerVersion { get; }

    /// <summary>Gets the effective bounds: the element-wise minimum of both peers' limits.</summary>
    public BridgeLimits EffectiveLimits { get; }

    /// <summary>Gets the capabilities both peers advertised.</summary>
    public IReadOnlyList<BridgeCapability> AgreedCapabilities { get; }

    /// <summary>Gets the authoritative remote identity for the session, when accepted.</summary>
    public LiveAuthoringRemoteEpoch? Epoch { get; }

    /// <summary>Gets a bounded, redacted detail describing the outcome.</summary>
    public string Detail { get; }

    /// <summary>Returns whether the session agreed on <paramref name="capability"/>.</summary>
    public bool Supports(BridgeCapability capability)
    {
        for (int index = 0; index < AgreedCapabilities.Count; index++)
        {
            if (AgreedCapabilities[index] == capability)
            {
                return true;
            }
        }

        return false;
    }
}

/// <summary>
/// Evaluates a peer's handshake answer against the local offer. Negotiation is mandatory: a client
/// must not send a mutating message until <see cref="BridgeNegotiationResult.Accepted"/> is
/// <see langword="true"/>, and the adapter in <c>OpenUsd.Bridge.Grpc</c> enforces exactly that.
/// </summary>
public static class BridgeNegotiator
{
    /// <summary>Evaluates <paramref name="response"/> against <paramref name="request"/>.</summary>
    /// <remarks>
    /// The order of the checks is deliberate. Version incompatibility is reported before capability
    /// or limit mismatches, because a peer speaking another major version may describe capabilities
    /// and limits that do not mean what this version thinks they mean.
    /// </remarks>
    public static BridgeNegotiationResult Evaluate(
        BridgeHandshakeRequest request,
        BridgeHandshakeResponse response)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(response);

        BridgeLimits effective = request.Limits.Intersect(response.EffectiveLimits);
        BridgeCapability[] agreed = [.. response.Capabilities.Where(request.Capabilities.Contains)];

        if (!request.Version.IsCompatibleWith(response.Version))
        {
            return Reject(
                BridgeHandshakeRejection.Version,
                response,
                effective,
                agreed,
                $"The peer speaks protocol {response.Version}; this client speaks {request.Version}.");
        }

        if (!response.Accepted)
        {
            BridgeHandshakeRejection rejection = response.Rejection == BridgeHandshakeRejection.None
                ? BridgeHandshakeRejection.Malformed
                : response.Rejection;
            return Reject(
                rejection,
                response,
                effective,
                agreed,
                response.Detail ?? "The peer refused the handshake without a detail.");
        }

        foreach (BridgeCapability required in BridgeProtocol.RequiredCapabilities)
        {
            if (Array.IndexOf(agreed, required) < 0)
            {
                return Reject(
                    BridgeHandshakeRejection.Capability,
                    response,
                    effective,
                    agreed,
                    $"The peer does not support the required capability '{required}'.");
            }
        }

        if (!effective.IsUsable)
        {
            return Reject(
                BridgeHandshakeRejection.Limits,
                response,
                effective,
                agreed,
                "The peer's declared limits are not usable for this session.");
        }

        if (!string.Equals(request.BridgeRootPath, response.BridgeRootPath, StringComparison.Ordinal))
        {
            return Reject(
                BridgeHandshakeRejection.BridgeRoot,
                response,
                effective,
                agreed,
                "The peer owns a different bridge root path than the client reserved.");
        }

        if (response.Epoch is null)
        {
            return Reject(
                BridgeHandshakeRejection.Malformed,
                response,
                effective,
                agreed,
                "The peer accepted the handshake without an authoritative epoch.");
        }

        return new BridgeNegotiationResult(
            accepted: true,
            BridgeHandshakeRejection.None,
            response.Version,
            effective,
            Array.AsReadOnly(agreed),
            response.Epoch,
            "The handshake was accepted.");
    }

    private static BridgeNegotiationResult Reject(
        BridgeHandshakeRejection rejection,
        BridgeHandshakeResponse response,
        BridgeLimits effective,
        BridgeCapability[] agreed,
        string detail) =>
        new(
            accepted: false,
            rejection,
            response.Version,
            effective,
            Array.AsReadOnly(agreed),
            epoch: null,
            BridgeWireError.Create(BridgeWireErrorCode.None, detail).Detail);
}
