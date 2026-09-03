// Copyright (c) marcschier. Licensed under the MIT License.

using OpenUsd.Bridge.Protocol;
using OpenUsd.LiveAuthoring;

namespace OpenUsd.Bridge.Grpc;

/// <summary>
/// The explicit state of one bridge client connection. It describes what the adapter is doing with
/// the transport; the authoritative session state stays with
/// <see cref="LiveAuthoringSessionCoordinator"/>.
/// </summary>
public enum BridgeConnectionState
{
    /// <summary>The client has not started, or has stopped.</summary>
    Disconnected = 0,

    /// <summary>A transport connection is being established.</summary>
    Connecting = 1,

    /// <summary>The transport is up and version/capability negotiation is in progress.</summary>
    Negotiating = 2,

    /// <summary>Negotiation succeeded and a full snapshot is being requested and applied.</summary>
    Resynchronizing = 3,

    /// <summary>A baseline is in place and ordered deltas are being applied.</summary>
    Streaming = 4,

    /// <summary>The connection failed and the client is waiting out a bounded backoff.</summary>
    Backoff = 5,

    /// <summary>
    /// A publication or an acknowledgement failure ended the connection deliberately, so the
    /// session can renegotiate and take a fresh baseline. It is transient, like
    /// <see cref="Backoff"/>, but names a cause the transport itself did not report.
    /// </summary>
    ConnectionRestartRequested = 6,

    /// <summary>
    /// The client stopped for a reason a retry cannot fix, such as an incompatible protocol major
    /// version, a missing required capability, or a refused credential.
    /// </summary>
    Faulted = 7
}

/// <summary>Identifies what a <see cref="BridgeClientEvent"/> reports.</summary>
public enum BridgeClientEventKind
{
    /// <summary>The client is attempting a transport connection.</summary>
    Connecting,

    /// <summary>Version and capability negotiation succeeded.</summary>
    Negotiated,

    /// <summary>Negotiation was refused.</summary>
    NegotiationRejected,

    /// <summary>A full snapshot was requested.</summary>
    SnapshotRequested,

    /// <summary>A full snapshot was applied and the session has a baseline.</summary>
    SnapshotApplied,

    /// <summary>An inbound delta was handed to the coordinator.</summary>
    DeltaApplied,

    /// <summary>An inbound message was refused before it reached the coordinator.</summary>
    ProtocolRejected,

    /// <summary>The session lost its baseline and a full resync was scheduled.</summary>
    ResyncScheduled,

    /// <summary>A local batch was published outward.</summary>
    LocalBatchPublished,

    /// <summary>A local batch could not be accepted into the bounded outbound channel.</summary>
    LocalBatchRefused,

    /// <summary>The transport failed and a bounded backoff started.</summary>
    Backoff,

    /// <summary>
    /// A publication or an acknowledgement forced the connection to end so the session can
    /// renegotiate and take a fresh baseline.
    /// </summary>
    ConnectionRestartRequested,

    /// <summary>The client stopped for a reason a retry cannot fix.</summary>
    Faulted,

    /// <summary>The client stopped.</summary>
    Stopped
}

/// <summary>
/// A bounded, redacted client notification. Every field is a fixed-size value or a length-capped
/// string, and no field ever carries a credential, an endpoint password, or an authored payload.
/// </summary>
/// <remarks>
/// <see cref="PreviousState"/> is the state the client was in before this event, not a copy of
/// <see cref="State"/>. A transition that reports the same value twice tells an observer nothing,
/// and an observer that has to infer transitions by remembering the last event cannot tell a
/// missed event from a repeated state.
/// </remarks>
public readonly record struct BridgeClientEvent(
    BridgeClientEventKind Kind,
    BridgeConnectionState PreviousState,
    BridgeConnectionState State,
    long Attempt,
    long Sequence,
    string? CorrelationId,
    DateTimeOffset TimestampUtc,
    string? Detail);

/// <summary>A bounded, point-in-time view of client connection health.</summary>
/// <remarks>
/// <see cref="ObserverFailureCount"/> and <see cref="LastObserverFailureDetail"/> report exceptions
/// thrown by a caller-supplied observer. They are kept apart from
/// <see cref="LastFailureDetail"/> on purpose: a broken observer is a defect in the host's
/// reporting code, and folding it into the transport's last-failure detail would erase the reason
/// the connection actually dropped.
/// </remarks>
public readonly record struct BridgeClientStatus(
    BridgeConnectionState State,
    bool Negotiated,
    BridgeProtocolVersion PeerVersion,
    long ConnectAttemptCount,
    long ReconnectCount,
    long SnapshotAppliedCount,
    long DeltaAppliedCount,
    long ProtocolRejectionCount,
    long ResyncCount,
    long PublishedBatchCount,
    long DuplicateBatchCount,
    long RefusedBatchCount,
    int PendingOutboundBatchCount,
    string? SessionId,
    long Epoch,
    BridgeLimits EffectiveLimits,
    IReadOnlyList<BridgeCapability> NegotiatedCapabilities,
    DateTimeOffset TimestampUtc,
    string? LastFailureDetail,
    long ObserverFailureCount,
    string? LastObserverFailureDetail)
{
    /// <summary>Returns whether the current session agreed <paramref name="capability"/>.</summary>
    public bool Supports(BridgeCapability capability)
    {
        IReadOnlyList<BridgeCapability> capabilities = NegotiatedCapabilities;
        if (capabilities is null)
        {
            return false;
        }

        for (int index = 0; index < capabilities.Count; index++)
        {
            if (capabilities[index] == capability)
            {
                return true;
            }
        }

        return false;
    }
}
