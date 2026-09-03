// Copyright (c) marcschier. Licensed under the MIT License.

using OpenUsd.LiveAuthoring;

namespace OpenUsd.Bridge.Protocol;

/// <summary>Identifies why a peer asked for, or was told to take, a full snapshot.</summary>
public enum BridgeResyncReason
{
    /// <summary>The session has no baseline yet.</summary>
    Initial = 1,

    /// <summary>The transport reconnected, so the previous sequence agreement is void.</summary>
    Reconnected = 2,

    /// <summary>The remote epoch changed, so sequences are no longer comparable.</summary>
    EpochChanged = 3,

    /// <summary>A delta skipped one or more sequences.</summary>
    SequenceGap = 4,

    /// <summary>An admitted message failed while applying.</summary>
    ApplyFailed = 5,

    /// <summary>The peer asked for a resync.</summary>
    ServerRequested = 6
}

/// <summary>Identifies which payload a decoded change-stream frame carries.</summary>
public enum BridgeStreamFrameKind
{
    /// <summary>A negotiation answer.</summary>
    Handshake = 1,

    /// <summary>A bounded full snapshot.</summary>
    Snapshot = 2,

    /// <summary>An ordered delta.</summary>
    Delta = 3,

    /// <summary>An acknowledgement of a previously sent message.</summary>
    Acknowledgement = 4,

    /// <summary>A structured session event.</summary>
    SessionEvent = 5,

    /// <summary>A bounded session status report.</summary>
    SessionStatus = 6,

    /// <summary>A peer-initiated demand for a full resync.</summary>
    ResyncRequired = 7,

    /// <summary>An application-level liveness probe.</summary>
    KeepAlive = 8
}

/// <summary>
/// One decoded change-stream frame. Exactly one payload is present, named by <see cref="Kind"/>;
/// every accessor for another kind throws rather than returning a silently empty value.
/// </summary>
public sealed class BridgeStreamFrame
{
    private readonly object? _payload;

    private BridgeStreamFrame(
        BridgeStreamFrameKind kind,
        object? payload,
        BridgeResyncReason resyncReason = BridgeResyncReason.Initial,
        LiveAuthoringRemoteEpoch? epoch = null,
        string? detail = null,
        DateTimeOffset timestampUtc = default)
    {
        Kind = kind;
        _payload = payload;
        ResyncReason = resyncReason;
        Epoch = epoch;
        Detail = detail;
        TimestampUtc = timestampUtc;
    }

    /// <summary>Gets which payload this frame carries.</summary>
    public BridgeStreamFrameKind Kind { get; }

    /// <summary>Gets the epoch a resync demand or keepalive belongs to, when present.</summary>
    public LiveAuthoringRemoteEpoch? Epoch { get; }

    /// <summary>Gets the reason carried by a <see cref="BridgeStreamFrameKind.ResyncRequired"/> frame.</summary>
    public BridgeResyncReason ResyncReason { get; }

    /// <summary>Gets the bounded, redacted detail carried by a resync demand.</summary>
    public string? Detail { get; }

    /// <summary>Gets the timestamp carried by a keepalive frame.</summary>
    public DateTimeOffset TimestampUtc { get; }

    /// <summary>Gets the negotiation answer.</summary>
    public BridgeHandshakeResponse Handshake => Require<BridgeHandshakeResponse>(
        BridgeStreamFrameKind.Handshake);

    /// <summary>Gets the full snapshot.</summary>
    public LiveAuthoringSnapshot Snapshot => Require<LiveAuthoringSnapshot>(
        BridgeStreamFrameKind.Snapshot);

    /// <summary>Gets the ordered delta.</summary>
    public LiveAuthoringDelta Delta => Require<LiveAuthoringDelta>(BridgeStreamFrameKind.Delta);

    /// <summary>Gets the acknowledgement.</summary>
    public LiveAuthoringSessionResult Acknowledgement =>
        RequireValue<LiveAuthoringSessionResult>(BridgeStreamFrameKind.Acknowledgement);

    /// <summary>Gets the structured session event.</summary>
    public LiveAuthoringSessionEvent SessionEvent =>
        RequireValue<LiveAuthoringSessionEvent>(BridgeStreamFrameKind.SessionEvent);

    /// <summary>Gets the bounded session status.</summary>
    public LiveAuthoringSessionStatus SessionStatus =>
        RequireValue<LiveAuthoringSessionStatus>(BridgeStreamFrameKind.SessionStatus);

    internal static BridgeStreamFrame ForHandshake(BridgeHandshakeResponse response) =>
        new(BridgeStreamFrameKind.Handshake, response);

    internal static BridgeStreamFrame ForSnapshot(LiveAuthoringSnapshot snapshot) =>
        new(BridgeStreamFrameKind.Snapshot, snapshot);

    internal static BridgeStreamFrame ForDelta(LiveAuthoringDelta delta) =>
        new(BridgeStreamFrameKind.Delta, delta);

    internal static BridgeStreamFrame ForAcknowledgement(LiveAuthoringSessionResult result) =>
        new(BridgeStreamFrameKind.Acknowledgement, result);

    internal static BridgeStreamFrame ForEvent(LiveAuthoringSessionEvent sessionEvent) =>
        new(BridgeStreamFrameKind.SessionEvent, sessionEvent);

    internal static BridgeStreamFrame ForStatus(LiveAuthoringSessionStatus status) =>
        new(BridgeStreamFrameKind.SessionStatus, status);

    internal static BridgeStreamFrame ForResync(
        LiveAuthoringRemoteEpoch epoch,
        BridgeResyncReason reason,
        string? detail) =>
        new(BridgeStreamFrameKind.ResyncRequired, payload: null, reason, epoch, detail);

    internal static BridgeStreamFrame ForKeepAlive(DateTimeOffset timestampUtc) =>
        new(
            BridgeStreamFrameKind.KeepAlive,
            payload: null,
            timestampUtc: timestampUtc);

    private T Require<T>(BridgeStreamFrameKind kind)
        where T : class =>
        Kind == kind && _payload is T typed
            ? typed
            : throw new InvalidOperationException($"The frame carries {Kind}, not {kind}.");

    private T RequireValue<T>(BridgeStreamFrameKind kind)
        where T : struct =>
        Kind == kind && _payload is T typed
            ? typed
            : throw new InvalidOperationException($"The frame carries {Kind}, not {kind}.");
}
