// Copyright (c) marcschier. Licensed under the MIT License.

using OpenUsd.Bridge.Protocol.Wire;
using OpenUsd.LiveAuthoring;

namespace OpenUsd.Bridge.Protocol;

/// <summary>
/// Maps whole messages between the wire model and the transport-neutral authoring types.
/// </summary>
/// <remarks>
/// The gRPC adapter uses these conversions directly, so a gRPC call never re-encodes a message it
/// already holds. The public byte-level API in <see cref="BridgeWireCodec"/> is the same conversion
/// with a serialization step, for a transport that carries opaque frames instead of typed messages.
/// </remarks>
internal static class BridgeMessageCodec
{
    /// <summary>
    /// The smallest Unix-millisecond timestamp <see cref="DateTimeOffset"/> can represent, which is
    /// <see cref="DateTimeOffset.MinValue"/>.
    /// </summary>
    internal const long MinTimestampUnixMillis = -62_135_596_800_000L;

    /// <summary>
    /// The largest Unix-millisecond timestamp <see cref="DateTimeOffset"/> can represent, which is
    /// <see cref="DateTimeOffset.MaxValue"/>.
    /// </summary>
    internal const long MaxTimestampUnixMillis = 253_402_300_799_999L;

    /// <summary>
    /// Converts an untrusted <c>timestamp_unix_millis</c> field into a
    /// <see cref="DateTimeOffset"/>, or returns a bounded error.
    /// </summary>
    /// <remarks>
    /// A peer controls this field completely, and <see cref="DateTimeOffset"/> covers only part of
    /// the <c>int64</c> range it is carried in. Range-checking it here is what keeps the contract's
    /// promise that decoding never throws: without the check, a single out-of-range integer would
    /// throw <see cref="ArgumentOutOfRangeException"/> out of a decode path and take down the
    /// receive loop that is supposed to reject the frame and stay alive.
    /// </remarks>
    internal static bool TryFromWireTimestamp(
        long timestampUnixMillis,
        string messageKind,
        out DateTimeOffset timestamp,
        out BridgeWireError error)
    {
        if (timestampUnixMillis is < MinTimestampUnixMillis or > MaxTimestampUnixMillis)
        {
            timestamp = default;
            error = BridgeWireError.Create(
                BridgeWireErrorCode.FieldOutOfRange,
                $"The {messageKind} carried a timestamp outside the representable range of " +
                $"{MinTimestampUnixMillis} to {MaxTimestampUnixMillis} Unix milliseconds.");
            return false;
        }

        timestamp = DateTimeOffset.FromUnixTimeMilliseconds(timestampUnixMillis);
        error = BridgeWireError.None;
        return true;
    }

    internal static SessionEpoch ToWire(LiveAuthoringRemoteEpoch epoch) =>
        new()
        {
            RemoteOriginId = epoch.RemoteOriginId,
            SessionId = epoch.SessionId,
            Epoch = epoch.Epoch
        };

    internal static bool TryFromWire(
        SessionEpoch? wire,
        out LiveAuthoringRemoteEpoch? epoch,
        out BridgeWireError error)
    {
        epoch = null;
        if (wire is null)
        {
            error = BridgeWireError.Create(
                BridgeWireErrorCode.MissingField,
                "The message carried no session epoch.");
            return false;
        }
        if (wire.Epoch < 0)
        {
            error = BridgeWireError.Create(
                BridgeWireErrorCode.FieldOutOfRange,
                "A session epoch cannot be negative.");
            return false;
        }

        try
        {
            epoch = new LiveAuthoringRemoteEpoch(wire.RemoteOriginId, wire.SessionId, wire.Epoch);
        }
        catch (ArgumentException exception)
        {
            error = BridgeWireError.Create(
                BridgeWireErrorCode.FieldOutOfRange,
                $"The session epoch was rejected: {exception.Message}");
            return false;
        }

        error = BridgeWireError.None;
        return true;
    }

    internal static StageSnapshot ToWire(LiveAuthoringSnapshot snapshot)
    {
        var wire = new StageSnapshot
        {
            Epoch = ToWire(snapshot.Epoch),
            Sequence = snapshot.Sequence,
            BridgeRootPath = snapshot.BridgeRootPath
        };
        for (int index = 0; index < snapshot.Updates.Count; index++)
        {
            wire.Updates.Add(BridgeUpdateCodec.ToWire(snapshot.Updates[index]));
        }
        if (snapshot.CorrelationId is not null)
        {
            wire.CorrelationId = snapshot.CorrelationId;
        }

        return wire;
    }

    internal static bool TryFromWire(
        StageSnapshot? wire,
        out LiveAuthoringSnapshot? snapshot,
        out BridgeWireError error)
    {
        snapshot = null;
        if (wire is null)
        {
            error = BridgeWireError.Create(
                BridgeWireErrorCode.MissingField,
                "The frame carried no snapshot.");
            return false;
        }
        if (!TryFromWire(wire.Epoch, out LiveAuthoringRemoteEpoch? epoch, out error))
        {
            return false;
        }
        if (wire.Sequence < 0)
        {
            error = BridgeWireError.Create(
                BridgeWireErrorCode.FieldOutOfRange,
                "A snapshot sequence cannot be negative.");
            return false;
        }
        if (!TryConvertUpdates(wire.Updates, out LiveStageUpdate[]? updates, out error))
        {
            return false;
        }

        try
        {
            snapshot = new LiveAuthoringSnapshot(
                epoch!,
                wire.Sequence,
                wire.BridgeRootPath,
                updates!,
                wire.HasCorrelationId ? wire.CorrelationId : null);
        }
        catch (ArgumentException exception)
        {
            error = BridgeWireError.Create(
                BridgeWireErrorCode.LimitExceeded,
                $"The snapshot was rejected by the authoring layer: {exception.Message}");
            return false;
        }

        error = BridgeWireError.None;
        return true;
    }

    internal static StageDelta ToWire(LiveAuthoringDelta delta)
    {
        var wire = new StageDelta
        {
            Epoch = ToWire(delta.Epoch),
            Sequence = delta.Sequence
        };
        for (int index = 0; index < delta.Updates.Count; index++)
        {
            wire.Updates.Add(BridgeUpdateCodec.ToWire(delta.Updates[index]));
        }
        if (delta.CoalescingKey is not null)
        {
            wire.CoalescingKey = delta.CoalescingKey;
        }
        if (delta.CorrelationId is not null)
        {
            wire.CorrelationId = delta.CorrelationId;
        }
        if (delta.OriginId is not null)
        {
            wire.OriginId = delta.OriginId;
        }

        return wire;
    }

    internal static bool TryFromWire(
        StageDelta? wire,
        out LiveAuthoringDelta? delta,
        out BridgeWireError error)
    {
        delta = null;
        if (wire is null)
        {
            error = BridgeWireError.Create(
                BridgeWireErrorCode.MissingField,
                "The frame carried no delta.");
            return false;
        }
        if (!TryFromWire(wire.Epoch, out LiveAuthoringRemoteEpoch? epoch, out error))
        {
            return false;
        }
        if (wire.Sequence <= 0)
        {
            error = BridgeWireError.Create(
                BridgeWireErrorCode.FieldOutOfRange,
                "A delta sequence must be positive.");
            return false;
        }
        if (!TryConvertUpdates(wire.Updates, out LiveStageUpdate[]? updates, out error))
        {
            return false;
        }

        try
        {
            delta = new LiveAuthoringDelta(
                epoch!,
                wire.Sequence,
                updates!,
                wire.HasCoalescingKey ? wire.CoalescingKey : null,
                wire.HasCorrelationId ? wire.CorrelationId : null,
                wire.HasOriginId ? wire.OriginId : null);
        }
        catch (ArgumentException exception)
        {
            error = BridgeWireError.Create(
                BridgeWireErrorCode.LimitExceeded,
                $"The delta was rejected by the authoring layer: {exception.Message}");
            return false;
        }

        error = BridgeWireError.None;
        return true;
    }

    internal static PublishLocalBatchRequest ToWire(BridgeLocalBatch batch)
    {
        var wire = new PublishLocalBatchRequest
        {
            Epoch = ToWire(batch.Epoch),
            Sequence = batch.Sequence,
            OriginId = batch.OriginId,
            IdempotencyKey = batch.IdempotencyKey
        };
        for (int index = 0; index < batch.Updates.Count; index++)
        {
            wire.Updates.Add(BridgeUpdateCodec.ToWire(batch.Updates[index]));
        }
        if (batch.CoalescingKey is not null)
        {
            wire.CoalescingKey = batch.CoalescingKey;
        }
        if (batch.CorrelationId is not null)
        {
            wire.CorrelationId = batch.CorrelationId;
        }

        return wire;
    }

    internal static bool TryFromWire(
        PublishLocalBatchRequest? wire,
        out BridgeLocalBatch? batch,
        out BridgeWireError error)
    {
        batch = null;
        if (wire is null)
        {
            error = BridgeWireError.Create(
                BridgeWireErrorCode.MissingField,
                "The frame carried no local batch.");
            return false;
        }
        if (!TryFromWire(wire.Epoch, out LiveAuthoringRemoteEpoch? epoch, out error))
        {
            return false;
        }
        if (!TryConvertUpdates(wire.Updates, out LiveStageUpdate[]? updates, out error))
        {
            return false;
        }

        try
        {
            batch = new BridgeLocalBatch(
                epoch!,
                wire.Sequence,
                updates!,
                wire.OriginId,
                string.IsNullOrEmpty(wire.IdempotencyKey) ? null : wire.IdempotencyKey,
                wire.HasCoalescingKey ? wire.CoalescingKey : null,
                wire.HasCorrelationId ? wire.CorrelationId : null);
        }
        catch (ArgumentException exception)
        {
            // ArgumentOutOfRangeException derives from ArgumentException, so a non-positive
            // sequence and an over-long identifier are both reported here as one bounded error.
            error = BridgeWireError.Create(
                BridgeWireErrorCode.FieldOutOfRange,
                $"The local batch was rejected: {exception.Message}");
            return false;
        }

        error = BridgeWireError.None;
        return true;
    }

    internal static Acknowledgement ToWire(LiveAuthoringSessionResult result)
    {
        var wire = new Acknowledgement
        {
            Outcome = ToWire(result.Outcome),
            Rejection = ToWire(result.Rejection),
            Sequence = result.Sequence,
            State = ToWire(result.State),
            LastAcceptedSequence = result.LastAcceptedSequence,
            LastAppliedSequence = result.LastAppliedSequence
        };
        if (result.CorrelationId is not null)
        {
            wire.CorrelationId = result.CorrelationId;
        }
        if (result.Detail is not null)
        {
            wire.Detail = BridgeWireError.Create(BridgeWireErrorCode.None, result.Detail).Detail;
        }

        return wire;
    }

    internal static bool TryFromWire(
        Acknowledgement? wire,
        out LiveAuthoringSessionResult result,
        out BridgeWireError error)
    {
        result = default;
        if (wire is null)
        {
            error = BridgeWireError.Create(
                BridgeWireErrorCode.MissingField,
                "The frame carried no acknowledgement.");
            return false;
        }
        if (!TryFromWire(wire.Outcome, out LiveAuthoringSessionOutcome outcome, out error) ||
            !TryFromWire(wire.Rejection, out LiveAuthoringSessionRejection rejection, out error) ||
            !TryFromWire(wire.State, out LiveAuthoringSessionState state, out error))
        {
            return false;
        }

        result = new LiveAuthoringSessionResult(
            outcome,
            rejection,
            wire.Sequence,
            state,
            wire.LastAcceptedSequence,
            wire.LastAppliedSequence,
            wire.HasCorrelationId ? wire.CorrelationId : null,
            wire.HasDetail ? wire.Detail : null);
        error = BridgeWireError.None;
        return true;
    }

    internal static Wire.SessionEvent ToWire(LiveAuthoringSessionEvent sessionEvent)
    {
        var wire = new Wire.SessionEvent
        {
            Kind = ToWire(sessionEvent.Kind),
            PreviousState = ToWire(sessionEvent.PreviousState),
            State = ToWire(sessionEvent.State),
            Epoch = sessionEvent.Epoch,
            LastAcceptedSequence = sessionEvent.LastAcceptedSequence,
            LastAppliedSequence = sessionEvent.LastAppliedSequence,
            TimestampUnixMillis = sessionEvent.TimestampUtc.ToUnixTimeMilliseconds()
        };
        if (sessionEvent.RemoteOriginId is not null)
        {
            wire.RemoteOriginId = sessionEvent.RemoteOriginId;
        }
        if (sessionEvent.SessionId is not null)
        {
            wire.SessionId = sessionEvent.SessionId;
        }
        if (sessionEvent.CorrelationId is not null)
        {
            wire.CorrelationId = sessionEvent.CorrelationId;
        }
        if (sessionEvent.Detail is not null)
        {
            wire.Detail = BridgeWireError.Create(BridgeWireErrorCode.None, sessionEvent.Detail).Detail;
        }

        return wire;
    }

    internal static bool TryFromWire(
        Wire.SessionEvent? wire,
        out LiveAuthoringSessionEvent sessionEvent,
        out BridgeWireError error)
    {
        sessionEvent = default;
        if (wire is null)
        {
            error = BridgeWireError.Create(
                BridgeWireErrorCode.MissingField,
                "The frame carried no session event.");
            return false;
        }
        if (!TryFromWire(wire.Kind, out LiveAuthoringSessionEventKind kind, out error) ||
            !TryFromWire(wire.PreviousState, out LiveAuthoringSessionState previous, out error) ||
            !TryFromWire(wire.State, out LiveAuthoringSessionState state, out error) ||
            !TryFromWireTimestamp(
                wire.TimestampUnixMillis,
                "session event",
                out DateTimeOffset timestamp,
                out error))
        {
            return false;
        }

        sessionEvent = new LiveAuthoringSessionEvent(
            kind,
            previous,
            state,
            wire.HasRemoteOriginId ? wire.RemoteOriginId : null,
            wire.HasSessionId ? wire.SessionId : null,
            wire.Epoch,
            wire.LastAcceptedSequence,
            wire.LastAppliedSequence,
            wire.HasCorrelationId ? wire.CorrelationId : null,
            timestamp,
            wire.HasDetail ? wire.Detail : null);
        error = BridgeWireError.None;
        return true;
    }

    internal static Wire.SessionStatus ToWire(LiveAuthoringSessionStatus status)
    {
        var wire = new Wire.SessionStatus
        {
            State = ToWire(status.State),
            Epoch = status.Epoch,
            LastAcceptedSequence = status.LastAcceptedSequence,
            LastAppliedSequence = status.LastAppliedSequence,
            AppliedSnapshotCount = status.AppliedSnapshotCount,
            AppliedDeltaCount = status.AppliedDeltaCount,
            DuplicateDeltaCount = status.DuplicateDeltaCount,
            RejectedDeltaCount = status.RejectedDeltaCount,
            LoopSuppressedDeltaCount = status.LoopSuppressedDeltaCount,
            ResyncRequiredCount = status.ResyncRequiredCount,
            OverlayPrimCount = status.OverlayPrimCount,
            OverlayUpdateCount = status.OverlayUpdateCount,
            ReplayWindowLength = status.ReplayWindowLength,
            ReplayLedgerCount = status.ReplayLedgerCount,
            ReplayLedgerBytes = status.ReplayLedgerBytes,
            OldestRetainedSequence = status.OldestRetainedSequence,
            TimestampUnixMillis = status.TimestampUtc.ToUnixTimeMilliseconds(),
            SessionObserverFailureCount = status.SessionObserverFailureCount
        };
        if (status.RemoteOriginId is not null)
        {
            wire.RemoteOriginId = status.RemoteOriginId;
        }
        if (status.SessionId is not null)
        {
            wire.SessionId = status.SessionId;
        }
        if (status.LastFailureDetail is not null)
        {
            wire.LastFailureDetail =
                BridgeWireError.Create(BridgeWireErrorCode.None, status.LastFailureDetail).Detail;
        }
        if (status.LastSessionObserverFailureDetail is not null)
        {
            wire.LastSessionObserverFailureDetail = BridgeWireError
                .Create(BridgeWireErrorCode.None, status.LastSessionObserverFailureDetail)
                .Detail;
        }

        return wire;
    }

    internal static bool TryFromWire(
        Wire.SessionStatus? wire,
        out LiveAuthoringSessionStatus status,
        out BridgeWireError error)
    {
        status = default;
        if (wire is null)
        {
            error = BridgeWireError.Create(
                BridgeWireErrorCode.MissingField,
                "The frame carried no session status.");
            return false;
        }
        if (!TryFromWire(wire.State, out LiveAuthoringSessionState state, out error) ||
            !TryFromWireTimestamp(
                wire.TimestampUnixMillis,
                "session status",
                out DateTimeOffset timestamp,
                out error))
        {
            return false;
        }

        status = new LiveAuthoringSessionStatus(
            state,
            wire.HasRemoteOriginId ? wire.RemoteOriginId : null,
            wire.HasSessionId ? wire.SessionId : null,
            wire.Epoch,
            wire.LastAcceptedSequence,
            wire.LastAppliedSequence,
            wire.AppliedSnapshotCount,
            wire.AppliedDeltaCount,
            wire.DuplicateDeltaCount,
            wire.RejectedDeltaCount,
            wire.LoopSuppressedDeltaCount,
            wire.ResyncRequiredCount,
            wire.OverlayPrimCount,
            wire.OverlayUpdateCount,
            wire.ReplayWindowLength,
            wire.ReplayLedgerCount,
            wire.ReplayLedgerBytes,
            wire.OldestRetainedSequence,
            wire.HasLastFailureDetail ? wire.LastFailureDetail : null,
            timestamp,
            wire.SessionObserverFailureCount,
            wire.HasLastSessionObserverFailureDetail
                ? wire.LastSessionObserverFailureDetail
                : null);
        error = BridgeWireError.None;
        return true;
    }

    internal static HandshakeRequest ToWire(BridgeHandshakeRequest request)
    {
        var wire = new HandshakeRequest
        {
            ClientVersion = new ProtocolVersion
            {
                Major = ToUnsigned(request.Version.Major, nameof(request.Version)),
                Minor = ToUnsigned(request.Version.Minor, nameof(request.Version))
            },
            ClientOriginId = request.ClientOriginId,
            BridgeRootPath = request.BridgeRootPath,
            ClientLimits = ToWire(request.Limits)
        };
        for (int index = 0; index < request.Capabilities.Count; index++)
        {
            wire.ClientCapabilities.Add(ToWire(request.Capabilities[index]));
        }
        if (request.RequestedSessionId is not null)
        {
            wire.RequestedSessionId = request.RequestedSessionId;
        }
        if (request.CorrelationId is not null)
        {
            wire.CorrelationId = request.CorrelationId;
        }

        return wire;
    }

    internal static bool TryFromWire(
        HandshakeRequest? wire,
        out BridgeHandshakeRequest? request,
        out BridgeWireError error)
    {
        request = null;
        if (wire is null || wire.ClientVersion is null || wire.ClientLimits is null)
        {
            error = BridgeWireError.Create(
                BridgeWireErrorCode.MissingField,
                "The handshake request is missing its version or limits.");
            return false;
        }
        if (!TryFromWire(wire.ClientCapabilities, out BridgeCapability[]? capabilities, out error))
        {
            return false;
        }

        try
        {
            request = new BridgeHandshakeRequest(
                new BridgeProtocolVersion((int)wire.ClientVersion.Major, (int)wire.ClientVersion.Minor),
                capabilities!,
                wire.ClientOriginId,
                wire.BridgeRootPath,
                FromWire(wire.ClientLimits),
                wire.HasRequestedSessionId ? wire.RequestedSessionId : null,
                wire.HasCorrelationId ? wire.CorrelationId : null);
        }
        catch (ArgumentException exception)
        {
            error = BridgeWireError.Create(
                BridgeWireErrorCode.FieldOutOfRange,
                $"The handshake request was rejected: {exception.Message}");
            return false;
        }

        error = BridgeWireError.None;
        return true;
    }

    internal static HandshakeResponse ToWire(BridgeHandshakeResponse response)
    {
        var wire = new HandshakeResponse
        {
            Accepted = response.Accepted,
            ServerVersion = new ProtocolVersion
            {
                Major = ToUnsigned(response.Version.Major, nameof(response.Version)),
                Minor = ToUnsigned(response.Version.Minor, nameof(response.Version))
            },
            BridgeRootPath = response.BridgeRootPath,
            EffectiveLimits = ToWire(response.EffectiveLimits),
            Rejection = ToWire(response.Rejection)
        };
        for (int index = 0; index < response.Capabilities.Count; index++)
        {
            wire.ServerCapabilities.Add(ToWire(response.Capabilities[index]));
        }
        if (response.Epoch is not null)
        {
            wire.Epoch = ToWire(response.Epoch);
        }
        if (response.Detail is not null)
        {
            wire.Detail = BridgeWireError.Create(BridgeWireErrorCode.None, response.Detail).Detail;
        }

        return wire;
    }

    internal static bool TryFromWire(
        HandshakeResponse? wire,
        out BridgeHandshakeResponse? response,
        out BridgeWireError error)
    {
        response = null;
        if (wire is null || wire.ServerVersion is null || wire.EffectiveLimits is null)
        {
            error = BridgeWireError.Create(
                BridgeWireErrorCode.MissingField,
                "The handshake response is missing its version or limits.");
            return false;
        }
        if (!TryFromWire(wire.ServerCapabilities, out BridgeCapability[]? capabilities, out error) ||
            !TryFromWire(wire.Rejection, out BridgeHandshakeRejection rejection, out error))
        {
            return false;
        }

        LiveAuthoringRemoteEpoch? epoch = null;
        if (wire.Epoch is not null && !TryFromWire(wire.Epoch, out epoch, out error))
        {
            return false;
        }

        response = new BridgeHandshakeResponse(
            wire.Accepted,
            new BridgeProtocolVersion((int)wire.ServerVersion.Major, (int)wire.ServerVersion.Minor),
            capabilities!,
            epoch,
            wire.BridgeRootPath,
            FromWire(wire.EffectiveLimits),
            rejection,
            wire.HasDetail ? wire.Detail : null);
        error = BridgeWireError.None;
        return true;
    }

    internal static Limits ToWire(BridgeLimits limits) =>
        new()
        {
            MaxUpdatesPerMessage = ToUnsigned(limits.MaxUpdatesPerMessage, nameof(limits)),
            MaxCollectionElementCount = ToUnsigned(limits.MaxCollectionElementCount, nameof(limits)),
            MaxMessagePayloadBytes = ToUnsignedLong(limits.MaxMessagePayloadBytes, nameof(limits)),
            MaxIdentifierLength = ToUnsigned(limits.MaxIdentifierLength, nameof(limits)),
            MaxPathLength = ToUnsigned(limits.MaxPathLength, nameof(limits)),
            MaxTextValueLength = ToUnsigned(limits.MaxTextValueLength, nameof(limits)),
            MaxOpaqueIdLength = ToUnsigned(limits.MaxOpaqueIdLength, nameof(limits)),
            MaxTotalCollectionElementCount =
                ToUnsignedLong(limits.MaxTotalCollectionElementCount, nameof(limits))
        };

    internal static BridgeLimits FromWire(Limits wire) =>
        new(
            ClampToInt(wire.MaxUpdatesPerMessage),
            ClampToInt(wire.MaxCollectionElementCount),
            ClampToLong(wire.MaxMessagePayloadBytes),
            ClampToInt(wire.MaxIdentifierLength),
            ClampToInt(wire.MaxPathLength),
            ClampToInt(wire.MaxTextValueLength),
            ClampToInt(wire.MaxOpaqueIdLength),
            ClampToLong(wire.MaxTotalCollectionElementCount));

    internal static Capability ToWire(BridgeCapability capability) => capability switch
    {
        BridgeCapability.FullSnapshot => Capability.FullSnapshot,
        BridgeCapability.OrderedDelta => Capability.OrderedDelta,
        BridgeCapability.LocalEditExport => Capability.LocalEditExport,
        BridgeCapability.HealthStatus => Capability.HealthStatus,
        BridgeCapability.PointInstancerOrientations => Capability.PointInstancerOrientations,
        BridgeCapability.ApiSchema => Capability.ApiSchema,
        BridgeCapability.ServerResyncRequest => Capability.ServerResyncRequest,
        _ => throw new BridgeProtocolException(
            BridgeWireError.Create(
                BridgeWireErrorCode.UnknownEnumValue,
                $"The capability '{capability}' has no wire case."))
    };

    internal static ResyncReason ToWire(BridgeResyncReason reason) => reason switch
    {
        BridgeResyncReason.Initial => ResyncReason.Initial,
        BridgeResyncReason.Reconnected => ResyncReason.Reconnected,
        BridgeResyncReason.EpochChanged => ResyncReason.EpochChanged,
        BridgeResyncReason.SequenceGap => ResyncReason.SequenceGap,
        BridgeResyncReason.ApplyFailed => ResyncReason.ApplyFailed,
        BridgeResyncReason.ServerRequested => ResyncReason.ServerRequested,
        _ => throw new BridgeProtocolException(
            BridgeWireError.Create(
                BridgeWireErrorCode.UnknownEnumValue,
                $"The resync reason '{reason}' has no wire case."))
    };

    internal static bool TryFromWire(
        ResyncReason wire,
        out BridgeResyncReason reason,
        out BridgeWireError error)
    {
        switch (wire)
        {
            case ResyncReason.Initial:
                reason = BridgeResyncReason.Initial;
                break;
            case ResyncReason.Reconnected:
                reason = BridgeResyncReason.Reconnected;
                break;
            case ResyncReason.EpochChanged:
                reason = BridgeResyncReason.EpochChanged;
                break;
            case ResyncReason.SequenceGap:
                reason = BridgeResyncReason.SequenceGap;
                break;
            case ResyncReason.ApplyFailed:
                reason = BridgeResyncReason.ApplyFailed;
                break;
            case ResyncReason.ServerRequested:
                reason = BridgeResyncReason.ServerRequested;
                break;
            case ResyncReason.Unspecified:
            default:
                reason = default;
                error = BridgeWireError.Create(
                    BridgeWireErrorCode.UnknownEnumValue,
                    $"The resync reason '{(int)wire}' is not supported by this version.");
                return false;
        }

        error = BridgeWireError.None;
        return true;
    }

    private static bool TryFromWire(
        Google.Protobuf.Collections.RepeatedField<Capability> wire,
        out BridgeCapability[]? capabilities,
        out BridgeWireError error)
    {
        var converted = new List<BridgeCapability>(wire.Count);
        for (int index = 0; index < wire.Count; index++)
        {
            switch (wire[index])
            {
                case Capability.FullSnapshot:
                    converted.Add(BridgeCapability.FullSnapshot);
                    break;
                case Capability.OrderedDelta:
                    converted.Add(BridgeCapability.OrderedDelta);
                    break;
                case Capability.LocalEditExport:
                    converted.Add(BridgeCapability.LocalEditExport);
                    break;
                case Capability.HealthStatus:
                    converted.Add(BridgeCapability.HealthStatus);
                    break;
                case Capability.PointInstancerOrientations:
                    converted.Add(BridgeCapability.PointInstancerOrientations);
                    break;
                case Capability.ApiSchema:
                    converted.Add(BridgeCapability.ApiSchema);
                    break;
                case Capability.ServerResyncRequest:
                    converted.Add(BridgeCapability.ServerResyncRequest);
                    break;
                case Capability.Unspecified:
                default:
                    // A capability this version does not know is ignored rather than rejected: a
                    // newer minor version may advertise more, and the session simply does not use
                    // what it cannot name. An unspecified value is a malformed advertisement.
                    if (wire[index] == Capability.Unspecified)
                    {
                        capabilities = null;
                        error = BridgeWireError.Create(
                            BridgeWireErrorCode.UnknownEnumValue,
                            "A capability list carried an unspecified capability.");
                        return false;
                    }

                    break;
            }
        }

        capabilities = [.. converted];
        error = BridgeWireError.None;
        return true;
    }

    private static bool TryConvertUpdates(
        Google.Protobuf.Collections.RepeatedField<StageUpdate> wire,
        out LiveStageUpdate[]? updates,
        out BridgeWireError error)
    {
        updates = null;
        if (wire.Count > LiveAuthoringValidation.MaxUpdatesPerBatch)
        {
            error = BridgeWireError.Create(
                BridgeWireErrorCode.LimitExceeded,
                $"A message carries at most {LiveAuthoringValidation.MaxUpdatesPerBatch} updates; " +
                $"the message carried {wire.Count}.");
            return false;
        }

        var converted = new LiveStageUpdate[wire.Count];
        for (int index = 0; index < wire.Count; index++)
        {
            if (!BridgeUpdateCodec.TryFromWire(wire[index], out LiveStageUpdate? update, out error))
            {
                return false;
            }

            converted[index] = update!;
        }

        updates = converted;
        error = BridgeWireError.None;
        return true;
    }

    private static SessionOutcome ToWire(LiveAuthoringSessionOutcome outcome) => outcome switch
    {
        LiveAuthoringSessionOutcome.Applied => SessionOutcome.Applied,
        LiveAuthoringSessionOutcome.Duplicate => SessionOutcome.Duplicate,
        LiveAuthoringSessionOutcome.LoopSuppressed => SessionOutcome.LoopSuppressed,
        LiveAuthoringSessionOutcome.Rejected => SessionOutcome.Rejected,
        _ => throw new BridgeProtocolException(
            BridgeWireError.Create(
                BridgeWireErrorCode.UnknownEnumValue,
                $"The session outcome '{outcome}' has no wire case."))
    };

    private static bool TryFromWire(
        SessionOutcome wire,
        out LiveAuthoringSessionOutcome outcome,
        out BridgeWireError error)
    {
        switch (wire)
        {
            case SessionOutcome.Applied:
                outcome = LiveAuthoringSessionOutcome.Applied;
                break;
            case SessionOutcome.Duplicate:
                outcome = LiveAuthoringSessionOutcome.Duplicate;
                break;
            case SessionOutcome.LoopSuppressed:
                outcome = LiveAuthoringSessionOutcome.LoopSuppressed;
                break;
            case SessionOutcome.Rejected:
                outcome = LiveAuthoringSessionOutcome.Rejected;
                break;
            case SessionOutcome.Unspecified:
            default:
                outcome = default;
                error = BridgeWireError.Create(
                    BridgeWireErrorCode.UnknownEnumValue,
                    $"The session outcome '{(int)wire}' is not supported by this version.");
                return false;
        }

        error = BridgeWireError.None;
        return true;
    }

    private static SessionRejection ToWire(LiveAuthoringSessionRejection rejection) => rejection switch
    {
        LiveAuthoringSessionRejection.None => SessionRejection.None,
        LiveAuthoringSessionRejection.SessionState => SessionRejection.SessionState,
        LiveAuthoringSessionRejection.ResyncRequired => SessionRejection.ResyncRequired,
        LiveAuthoringSessionRejection.RemoteOrigin => SessionRejection.RemoteOrigin,
        LiveAuthoringSessionRejection.SessionIdentity => SessionRejection.SessionIdentity,
        LiveAuthoringSessionRejection.EpochRetired => SessionRejection.EpochRetired,
        LiveAuthoringSessionRejection.EpochAdvanced => SessionRejection.EpochAdvanced,
        LiveAuthoringSessionRejection.SequenceGap => SessionRejection.SequenceGap,
        LiveAuthoringSessionRejection.BridgeScope => SessionRejection.BridgeScope,
        LiveAuthoringSessionRejection.OverlayBudget => SessionRejection.OverlayBudget,
        LiveAuthoringSessionRejection.DuplicateConflict => SessionRejection.DuplicateConflict,
        LiveAuthoringSessionRejection.ReplayExpired => SessionRejection.ReplayExpired,
        LiveAuthoringSessionRejection.ApplyFailed => SessionRejection.ApplyFailed,
        _ => throw new BridgeProtocolException(
            BridgeWireError.Create(
                BridgeWireErrorCode.UnknownEnumValue,
                $"The session rejection '{rejection}' has no wire case."))
    };

    private static bool TryFromWire(
        SessionRejection wire,
        out LiveAuthoringSessionRejection rejection,
        out BridgeWireError error)
    {
        switch (wire)
        {
            case SessionRejection.None:
                rejection = LiveAuthoringSessionRejection.None;
                break;
            case SessionRejection.SessionState:
                rejection = LiveAuthoringSessionRejection.SessionState;
                break;
            case SessionRejection.ResyncRequired:
                rejection = LiveAuthoringSessionRejection.ResyncRequired;
                break;
            case SessionRejection.RemoteOrigin:
                rejection = LiveAuthoringSessionRejection.RemoteOrigin;
                break;
            case SessionRejection.SessionIdentity:
                rejection = LiveAuthoringSessionRejection.SessionIdentity;
                break;
            case SessionRejection.EpochRetired:
                rejection = LiveAuthoringSessionRejection.EpochRetired;
                break;
            case SessionRejection.EpochAdvanced:
                rejection = LiveAuthoringSessionRejection.EpochAdvanced;
                break;
            case SessionRejection.SequenceGap:
                rejection = LiveAuthoringSessionRejection.SequenceGap;
                break;
            case SessionRejection.BridgeScope:
                rejection = LiveAuthoringSessionRejection.BridgeScope;
                break;
            case SessionRejection.OverlayBudget:
                rejection = LiveAuthoringSessionRejection.OverlayBudget;
                break;
            case SessionRejection.DuplicateConflict:
                rejection = LiveAuthoringSessionRejection.DuplicateConflict;
                break;
            case SessionRejection.ReplayExpired:
                rejection = LiveAuthoringSessionRejection.ReplayExpired;
                break;
            case SessionRejection.ApplyFailed:
                rejection = LiveAuthoringSessionRejection.ApplyFailed;
                break;
            case SessionRejection.ProtocolViolation:
            case SessionRejection.NotNegotiated:
                // Protocol-only rejections have no authoring counterpart. They are surfaced through
                // the adapter's own diagnostics rather than mapped onto an authoring rejection that
                // would misdescribe what happened.
                rejection = default;
                error = BridgeWireError.Create(
                    BridgeWireErrorCode.UnknownEnumValue,
                    $"The rejection '{wire}' is a protocol rejection, not an authoring rejection.");
                return false;
            case SessionRejection.Unspecified:
            default:
                rejection = default;
                error = BridgeWireError.Create(
                    BridgeWireErrorCode.UnknownEnumValue,
                    $"The session rejection '{(int)wire}' is not supported by this version.");
                return false;
        }

        error = BridgeWireError.None;
        return true;
    }

    internal static Wire.SessionState ToWire(LiveAuthoringSessionState state) => state switch
    {
        LiveAuthoringSessionState.Disconnected => Wire.SessionState.Disconnected,
        LiveAuthoringSessionState.Connecting => Wire.SessionState.Connecting,
        LiveAuthoringSessionState.Synchronized => Wire.SessionState.Synchronized,
        LiveAuthoringSessionState.ResyncRequired => Wire.SessionState.ResyncRequired,
        LiveAuthoringSessionState.Stopping => Wire.SessionState.Stopping,
        LiveAuthoringSessionState.Faulted => Wire.SessionState.Faulted,
        _ => throw new BridgeProtocolException(
            BridgeWireError.Create(
                BridgeWireErrorCode.UnknownEnumValue,
                $"The session state '{state}' has no wire case."))
    };

    private static bool TryFromWire(
        Wire.SessionState wire,
        out LiveAuthoringSessionState state,
        out BridgeWireError error)
    {
        switch (wire)
        {
            case Wire.SessionState.Disconnected:
                state = LiveAuthoringSessionState.Disconnected;
                break;
            case Wire.SessionState.Connecting:
                state = LiveAuthoringSessionState.Connecting;
                break;
            case Wire.SessionState.Synchronized:
                state = LiveAuthoringSessionState.Synchronized;
                break;
            case Wire.SessionState.ResyncRequired:
                state = LiveAuthoringSessionState.ResyncRequired;
                break;
            case Wire.SessionState.Stopping:
                state = LiveAuthoringSessionState.Stopping;
                break;
            case Wire.SessionState.Faulted:
                state = LiveAuthoringSessionState.Faulted;
                break;
            case Wire.SessionState.Unspecified:
            default:
                state = default;
                error = BridgeWireError.Create(
                    BridgeWireErrorCode.UnknownEnumValue,
                    $"The session state '{(int)wire}' is not supported by this version.");
                return false;
        }

        error = BridgeWireError.None;
        return true;
    }

    private static Wire.SessionEventKind ToWire(LiveAuthoringSessionEventKind kind) => kind switch
    {
        LiveAuthoringSessionEventKind.Connecting => Wire.SessionEventKind.Connecting,
        LiveAuthoringSessionEventKind.SnapshotApplied => Wire.SessionEventKind.SnapshotApplied,
        LiveAuthoringSessionEventKind.SnapshotRejected => Wire.SessionEventKind.SnapshotRejected,
        LiveAuthoringSessionEventKind.DeltaApplied => Wire.SessionEventKind.DeltaApplied,
        LiveAuthoringSessionEventKind.DeltaDuplicate => Wire.SessionEventKind.DeltaDuplicate,
        LiveAuthoringSessionEventKind.DeltaRejected => Wire.SessionEventKind.DeltaRejected,
        LiveAuthoringSessionEventKind.LoopSuppressed => Wire.SessionEventKind.LoopSuppressed,
        LiveAuthoringSessionEventKind.ResyncRequired => Wire.SessionEventKind.ResyncRequired,
        LiveAuthoringSessionEventKind.Disconnected => Wire.SessionEventKind.Disconnected,
        LiveAuthoringSessionEventKind.Faulted => Wire.SessionEventKind.Faulted,
        LiveAuthoringSessionEventKind.Disposed => Wire.SessionEventKind.Disposed,
        _ => throw new BridgeProtocolException(
            BridgeWireError.Create(
                BridgeWireErrorCode.UnknownEnumValue,
                $"The session event kind '{kind}' has no wire case."))
    };

    private static bool TryFromWire(
        Wire.SessionEventKind wire,
        out LiveAuthoringSessionEventKind kind,
        out BridgeWireError error)
    {
        switch (wire)
        {
            case Wire.SessionEventKind.Connecting:
                kind = LiveAuthoringSessionEventKind.Connecting;
                break;
            case Wire.SessionEventKind.SnapshotApplied:
                kind = LiveAuthoringSessionEventKind.SnapshotApplied;
                break;
            case Wire.SessionEventKind.SnapshotRejected:
                kind = LiveAuthoringSessionEventKind.SnapshotRejected;
                break;
            case Wire.SessionEventKind.DeltaApplied:
                kind = LiveAuthoringSessionEventKind.DeltaApplied;
                break;
            case Wire.SessionEventKind.DeltaDuplicate:
                kind = LiveAuthoringSessionEventKind.DeltaDuplicate;
                break;
            case Wire.SessionEventKind.DeltaRejected:
                kind = LiveAuthoringSessionEventKind.DeltaRejected;
                break;
            case Wire.SessionEventKind.LoopSuppressed:
                kind = LiveAuthoringSessionEventKind.LoopSuppressed;
                break;
            case Wire.SessionEventKind.ResyncRequired:
                kind = LiveAuthoringSessionEventKind.ResyncRequired;
                break;
            case Wire.SessionEventKind.Disconnected:
                kind = LiveAuthoringSessionEventKind.Disconnected;
                break;
            case Wire.SessionEventKind.Faulted:
                kind = LiveAuthoringSessionEventKind.Faulted;
                break;
            case Wire.SessionEventKind.Disposed:
                kind = LiveAuthoringSessionEventKind.Disposed;
                break;
            case Wire.SessionEventKind.Unspecified:
            default:
                kind = default;
                error = BridgeWireError.Create(
                    BridgeWireErrorCode.UnknownEnumValue,
                    $"The session event kind '{(int)wire}' is not supported by this version.");
                return false;
        }

        error = BridgeWireError.None;
        return true;
    }

    private static Wire.HandshakeRejection ToWire(BridgeHandshakeRejection rejection) => rejection switch
    {
        BridgeHandshakeRejection.None => Wire.HandshakeRejection.None,
        BridgeHandshakeRejection.Version => Wire.HandshakeRejection.Version,
        BridgeHandshakeRejection.Capability => Wire.HandshakeRejection.Capability,
        BridgeHandshakeRejection.Limits => Wire.HandshakeRejection.Limits,
        BridgeHandshakeRejection.Unauthenticated => Wire.HandshakeRejection.Unauthenticated,
        BridgeHandshakeRejection.BridgeRoot => Wire.HandshakeRejection.BridgeRoot,
        BridgeHandshakeRejection.Unavailable => Wire.HandshakeRejection.Unavailable,
        BridgeHandshakeRejection.Malformed => Wire.HandshakeRejection.Malformed,
        _ => throw new BridgeProtocolException(
            BridgeWireError.Create(
                BridgeWireErrorCode.UnknownEnumValue,
                $"The handshake rejection '{rejection}' has no wire case."))
    };

    private static bool TryFromWire(
        Wire.HandshakeRejection wire,
        out BridgeHandshakeRejection rejection,
        out BridgeWireError error)
    {
        switch (wire)
        {
            case Wire.HandshakeRejection.None:
                rejection = BridgeHandshakeRejection.None;
                break;
            case Wire.HandshakeRejection.Version:
                rejection = BridgeHandshakeRejection.Version;
                break;
            case Wire.HandshakeRejection.Capability:
                rejection = BridgeHandshakeRejection.Capability;
                break;
            case Wire.HandshakeRejection.Limits:
                rejection = BridgeHandshakeRejection.Limits;
                break;
            case Wire.HandshakeRejection.Unauthenticated:
                rejection = BridgeHandshakeRejection.Unauthenticated;
                break;
            case Wire.HandshakeRejection.BridgeRoot:
                rejection = BridgeHandshakeRejection.BridgeRoot;
                break;
            case Wire.HandshakeRejection.Unavailable:
                rejection = BridgeHandshakeRejection.Unavailable;
                break;
            case Wire.HandshakeRejection.Malformed:
                rejection = BridgeHandshakeRejection.Malformed;
                break;
            case Wire.HandshakeRejection.Unspecified:
            default:
                rejection = default;
                error = BridgeWireError.Create(
                    BridgeWireErrorCode.UnknownEnumValue,
                    $"The handshake rejection '{(int)wire}' is not supported by this version.");
                return false;
        }

        error = BridgeWireError.None;
        return true;
    }

    private static uint ToUnsigned(int value, string parameterName)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(value, parameterName);
        return (uint)value;
    }

    private static ulong ToUnsignedLong(long value, string parameterName)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(value, parameterName);
        return (ulong)value;
    }

    /// <summary>
    /// Clamps a peer-declared bound into the signed range this implementation reasons about. A peer
    /// that declares an absurdly large bound is clamped rather than trusted; the negotiated limit is
    /// still the minimum of both peers, so clamping can only ever lower an effective bound.
    /// </summary>
    private static int ClampToInt(uint value) => value > int.MaxValue ? int.MaxValue : (int)value;

    private static long ClampToLong(ulong value) =>
        value > long.MaxValue ? long.MaxValue : (long)value;
}
