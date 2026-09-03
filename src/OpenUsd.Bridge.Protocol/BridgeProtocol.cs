// Copyright (c) marcschier. Licensed under the MIT License.

using OpenUsd.LiveAuthoring;

namespace OpenUsd.Bridge.Protocol;

/// <summary>
/// A protocol version. A major version change is incompatible: a peer advertising a different major
/// version is rejected before any mutation is attempted. A minor version change is additive, so a
/// newer minor version may add capabilities but never changes the meaning of an existing field.
/// </summary>
public readonly record struct BridgeProtocolVersion(int Major, int Minor)
{
    /// <summary>Gets whether this version can talk to <paramref name="other"/>.</summary>
    /// <remarks>
    /// Compatibility is major-version equality. Minor versions are deliberately not compared: an
    /// older peer simply advertises fewer capabilities, and capability negotiation, not the minor
    /// version, decides what a session may use.
    /// </remarks>
    public bool IsCompatibleWith(BridgeProtocolVersion other) => Major == other.Major;

    /// <inheritdoc/>
    public override string ToString() => $"{Major}.{Minor}";
}

/// <summary>
/// A named optional protocol behaviour. A capability that is not advertised by both peers must not
/// be used: the client refuses the negotiation instead of attempting the call and discovering the
/// gap mid-session.
/// </summary>
public enum BridgeCapability
{
    /// <summary>Bounded full snapshots of the bridge-owned overlay.</summary>
    FullSnapshot = 1,

    /// <summary>Ordered per-epoch deltas.</summary>
    OrderedDelta = 2,

    /// <summary>Client-to-server publication of already-authoritative local batches.</summary>
    LocalEditExport = 3,

    /// <summary>Session status and structured session events.</summary>
    HealthStatus = 4,

    /// <summary>Point-instancer orientation updates.</summary>
    PointInstancerOrientations = 5,

    /// <summary>API-schema apply and remove updates.</summary>
    ApiSchema = 6,

    /// <summary>Server-initiated resync requests carried on the change stream.</summary>
    ServerResyncRequest = 7
}

/// <summary>
/// The exact numeric bounds one peer enforces. Every value mirrors a
/// <see cref="LiveAuthoringValidation"/> constant, so the wire model never accepts a message the
/// in-process authoring layer would reject.
/// </summary>
public readonly record struct BridgeLimits(
    int MaxUpdatesPerMessage,
    int MaxCollectionElementCount,
    long MaxMessagePayloadBytes,
    int MaxIdentifierLength,
    int MaxPathLength,
    int MaxTextValueLength,
    int MaxOpaqueIdLength,
    long MaxTotalCollectionElementCount)
{
    /// <summary>
    /// Gets the limits this implementation enforces, taken directly from
    /// <see cref="LiveAuthoringValidation"/>.
    /// </summary>
    public static BridgeLimits Local { get; } = new(
        LiveAuthoringValidation.MaxUpdatesPerBatch,
        LiveAuthoringValidation.MaxCollectionElementCount,
        LiveAuthoringValidation.MaxEstimatedBatchPayloadBytes,
        LiveAuthoringValidation.MaxIdentifierLength,
        LiveAuthoringValidation.MaxPathLength,
        LiveAuthoringValidation.MaxTextValueLength,
        LiveAuthoringValidation.MaxOpaqueIdLength,
        LiveAuthoringValidation.MaxTotalCollectionElementCountPerBatch);

    /// <summary>
    /// Returns the element-wise minimum of two limit sets, which is the only safe effective limit:
    /// neither peer can be pushed past what it will accept.
    /// </summary>
    public BridgeLimits Intersect(BridgeLimits other) => new(
        Math.Min(MaxUpdatesPerMessage, other.MaxUpdatesPerMessage),
        Math.Min(MaxCollectionElementCount, other.MaxCollectionElementCount),
        Math.Min(MaxMessagePayloadBytes, other.MaxMessagePayloadBytes),
        Math.Min(MaxIdentifierLength, other.MaxIdentifierLength),
        Math.Min(MaxPathLength, other.MaxPathLength),
        Math.Min(MaxTextValueLength, other.MaxTextValueLength),
        Math.Min(MaxOpaqueIdLength, other.MaxOpaqueIdLength),
        Math.Min(MaxTotalCollectionElementCount, other.MaxTotalCollectionElementCount));

    /// <summary>
    /// Gets whether every bound is positive and no bound exceeds <see cref="Local"/>. A peer that
    /// advertises a larger bound than this implementation enforces is not trusted to raise it.
    /// </summary>
    public bool IsUsable =>
        MaxUpdatesPerMessage > 0 &&
        MaxCollectionElementCount > 0 &&
        MaxMessagePayloadBytes > 0 &&
        MaxIdentifierLength > 0 &&
        MaxPathLength > 0 &&
        MaxTextValueLength > 0 &&
        MaxOpaqueIdLength > 0 &&
        MaxTotalCollectionElementCount > 0 &&
        MaxUpdatesPerMessage <= Local.MaxUpdatesPerMessage &&
        MaxCollectionElementCount <= Local.MaxCollectionElementCount &&
        MaxMessagePayloadBytes <= Local.MaxMessagePayloadBytes &&
        MaxIdentifierLength <= Local.MaxIdentifierLength &&
        MaxPathLength <= Local.MaxPathLength &&
        MaxTextValueLength <= Local.MaxTextValueLength &&
        MaxOpaqueIdLength <= Local.MaxOpaqueIdLength &&
        MaxTotalCollectionElementCount <= Local.MaxTotalCollectionElementCount;

    /// <summary>
    /// Returns whether a message of <paramref name="updateCount"/> updates and
    /// <paramref name="payloadBytes"/> encoded bytes fits inside these bounds.
    /// </summary>
    /// <remarks>
    /// The local bounds are not the whole answer once a session is negotiated: a peer may enforce
    /// smaller ones, and sending a message it will refuse wastes a round trip and leaves the local
    /// edit in an unknown state. The effective session bounds are checked against this method
    /// before a message is sent and after one is decoded.
    /// </remarks>
    public bool Allows(int updateCount, long payloadBytes) =>
        updateCount >= 0 &&
        payloadBytes >= 0 &&
        updateCount <= MaxUpdatesPerMessage &&
        payloadBytes <= MaxMessagePayloadBytes;
}

/// <summary>
/// The identity of the <c>openusd.bridge.v1</c> contract: its version, the capabilities this
/// implementation supports, the bounds it enforces, and the serialized descriptor a peer in another
/// language can consume without this package.
/// </summary>
/// <remarks>
/// <para>
/// The contract is deliberately transport-neutral. Nothing in this package opens a socket, resolves
/// a host name, or presents a credential; a transport adapter carries the encoded frames. The
/// optional gRPC adapter lives in <c>OpenUsd.Bridge.Grpc</c>.
/// </para>
/// <para>
/// Nothing here depends on, links to, or redistributes any NVIDIA component. The Kit-side peer that
/// speaks this protocol is owned and distributed separately.
/// </para>
/// </remarks>
public static class BridgeProtocol
{
    /// <summary>The protobuf package name that qualifies every message in the contract.</summary>
    public const string PackageName = "openusd.bridge.v1";

    /// <summary>The fully qualified gRPC service name of the optional gRPC surface.</summary>
    public const string ServiceName = "openusd.bridge.v1.LiveBridge";

    /// <summary>The current major protocol version.</summary>
    public const int CurrentMajorVersion = 1;

    /// <summary>The current minor protocol version.</summary>
    public const int CurrentMinorVersion = 0;

    /// <summary>
    /// The largest encoded frame this implementation will produce or accept. It is the batch payload
    /// budget plus a fixed framing allowance, so an overlay that is exactly at its own limit still
    /// fits inside the frame that carries it.
    /// </summary>
    public const long MaxFrameBytes =
        LiveAuthoringValidation.MaxEstimatedBatchPayloadBytes + (64 * 1024);

    private static readonly BridgeCapability[] SupportedCapabilityValues =
    [
        BridgeCapability.FullSnapshot,
        BridgeCapability.OrderedDelta,
        BridgeCapability.LocalEditExport,
        BridgeCapability.HealthStatus,
        BridgeCapability.PointInstancerOrientations,
        BridgeCapability.ApiSchema,
        BridgeCapability.ServerResyncRequest
    ];

    private static readonly BridgeCapability[] RequiredCapabilityValues =
    [
        BridgeCapability.FullSnapshot,
        BridgeCapability.OrderedDelta
    ];

    /// <summary>Gets the current protocol version.</summary>
    public static BridgeProtocolVersion Version { get; } =
        new(CurrentMajorVersion, CurrentMinorVersion);

    /// <summary>Gets every capability this implementation supports.</summary>
    public static IReadOnlyList<BridgeCapability> SupportedCapabilities { get; } =
        Array.AsReadOnly(SupportedCapabilityValues);

    /// <summary>
    /// Gets the capabilities a session cannot run without. A peer that does not advertise all of
    /// them is rejected during negotiation rather than after the first delta arrives.
    /// </summary>
    public static IReadOnlyList<BridgeCapability> RequiredCapabilities { get; } =
        Array.AsReadOnly(RequiredCapabilityValues);

    /// <summary>Gets the bounds this implementation enforces.</summary>
    public static BridgeLimits Limits => BridgeLimits.Local;

    /// <summary>
    /// Returns whether <paramref name="capability"/> is a defined, supported capability. An unknown
    /// value is rejected explicitly instead of being treated as absent.
    /// </summary>
    public static bool IsSupported(BridgeCapability capability) =>
        Array.IndexOf(SupportedCapabilityValues, capability) >= 0;

    /// <summary>
    /// Returns the optional capability an update kind depends on, or <see langword="null"/> when the
    /// kind is part of the always-required core.
    /// </summary>
    /// <remarks>
    /// A session must not send, and must not accept, an update whose capability both peers did not
    /// advertise. Keeping the mapping here rather than in a transport adapter means one table
    /// answers the question for the sender, the receiver, and any future transport.
    /// </remarks>
    public static BridgeCapability? GetRequiredCapability(LiveStageUpdate update)
    {
        ArgumentNullException.ThrowIfNull(update);
        return update switch
        {
            SetPointInstancerOrientationsUpdate => BridgeCapability.PointInstancerOrientations,
            ApiSchemaUpdate => BridgeCapability.ApiSchema,
            _ => null
        };
    }

    /// <summary>
    /// Returns the serialized <c>FileDescriptorSet</c> for the wire contract, suitable for a peer
    /// that generates bindings in another language, for a compatibility gate, or for a diagnostic
    /// dump. The bytes come from the compiled contract itself, so they cannot drift from the code
    /// that encodes and decodes messages.
    /// </summary>
    public static byte[] CreateDescriptorSet() => BridgeDescriptorSet.CreateWireDescriptorSet();
}
