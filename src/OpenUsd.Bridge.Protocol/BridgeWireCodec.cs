// Copyright (c) marcschier. Licensed under the MIT License.

using Google.Protobuf;
using OpenUsd.Bridge.Protocol.Wire;
using OpenUsd.LiveAuthoring;

namespace OpenUsd.Bridge.Protocol;

/// <summary>
/// Encodes and decodes <c>openusd.bridge.v1</c> frames as opaque bytes, so any transport can carry
/// the contract: gRPC, a WebSocket, a message bus, or a test harness.
/// </summary>
/// <remarks>
/// <para>
/// Encoding throws <see cref="BridgeProtocolException"/> when the caller asks for something the
/// contract cannot represent, because that is a local programming error. Decoding never throws for
/// bad input: an inbound frame is untrusted, so every failure is returned as a bounded
/// <see cref="BridgeWireError"/> and the receive loop stays alive.
/// </para>
/// <para>
/// Every decode enforces the same bounds as <see cref="LiveAuthoringValidation"/>: frame size,
/// update count, collection sizes, string lengths, and aggregate payload budget. A frame larger
/// than <see cref="BridgeProtocol.MaxFrameBytes"/> is rejected before it is parsed, so an oversized
/// buffer is never materialized into messages.
/// </para>
/// </remarks>
public static class BridgeWireCodec
{
    /// <summary>Encodes a negotiation offer.</summary>
    public static byte[] EncodeHandshakeRequest(BridgeHandshakeRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return Encode(BridgeMessageCodec.ToWire(request));
    }

    /// <summary>Decodes a negotiation offer.</summary>
    public static bool TryDecodeHandshakeRequest(
        ReadOnlySpan<byte> payload,
        out BridgeHandshakeRequest? request,
        out BridgeWireError error)
    {
        request = null;
        if (!TryParse(payload, HandshakeRequest.Parser, out HandshakeRequest? wire, out error))
        {
            return false;
        }

        return BridgeMessageCodec.TryFromWire(wire, out request, out error);
    }

    /// <summary>Encodes a negotiation answer.</summary>
    public static byte[] EncodeHandshakeResponse(BridgeHandshakeResponse response)
    {
        ArgumentNullException.ThrowIfNull(response);
        return Encode(BridgeMessageCodec.ToWire(response));
    }

    /// <summary>Decodes a negotiation answer.</summary>
    public static bool TryDecodeHandshakeResponse(
        ReadOnlySpan<byte> payload,
        out BridgeHandshakeResponse? response,
        out BridgeWireError error)
    {
        response = null;
        if (!TryParse(payload, HandshakeResponse.Parser, out HandshakeResponse? wire, out error))
        {
            return false;
        }

        return BridgeMessageCodec.TryFromWire(wire, out response, out error);
    }

    /// <summary>Encodes a bounded full snapshot.</summary>
    public static byte[] EncodeSnapshot(LiveAuthoringSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        return Encode(BridgeMessageCodec.ToWire(snapshot));
    }

    /// <summary>Decodes a bounded full snapshot.</summary>
    public static bool TryDecodeSnapshot(
        ReadOnlySpan<byte> payload,
        out LiveAuthoringSnapshot? snapshot,
        out BridgeWireError error)
    {
        snapshot = null;
        if (!TryParse(payload, StageSnapshot.Parser, out StageSnapshot? wire, out error))
        {
            return false;
        }

        return BridgeMessageCodec.TryFromWire(wire, out snapshot, out error);
    }

    /// <summary>Encodes an ordered delta.</summary>
    public static byte[] EncodeDelta(LiveAuthoringDelta delta)
    {
        ArgumentNullException.ThrowIfNull(delta);
        return Encode(BridgeMessageCodec.ToWire(delta));
    }

    /// <summary>Decodes an ordered delta.</summary>
    public static bool TryDecodeDelta(
        ReadOnlySpan<byte> payload,
        out LiveAuthoringDelta? delta,
        out BridgeWireError error)
    {
        delta = null;
        if (!TryParse(payload, StageDelta.Parser, out StageDelta? wire, out error))
        {
            return false;
        }

        return BridgeMessageCodec.TryFromWire(wire, out delta, out error);
    }

    /// <summary>Encodes a local publication.</summary>
    public static byte[] EncodeLocalBatch(BridgeLocalBatch batch)
    {
        ArgumentNullException.ThrowIfNull(batch);
        return Encode(BridgeMessageCodec.ToWire(batch));
    }

    /// <summary>Decodes a local publication.</summary>
    public static bool TryDecodeLocalBatch(
        ReadOnlySpan<byte> payload,
        out BridgeLocalBatch? batch,
        out BridgeWireError error)
    {
        batch = null;
        if (!TryParse(
            payload,
            PublishLocalBatchRequest.Parser,
            out PublishLocalBatchRequest? wire,
            out error))
        {
            return false;
        }

        return BridgeMessageCodec.TryFromWire(wire, out batch, out error);
    }

    /// <summary>Encodes an acknowledgement.</summary>
    public static byte[] EncodeAcknowledgement(LiveAuthoringSessionResult result) =>
        Encode(BridgeMessageCodec.ToWire(result));

    /// <summary>Decodes an acknowledgement.</summary>
    public static bool TryDecodeAcknowledgement(
        ReadOnlySpan<byte> payload,
        out LiveAuthoringSessionResult result,
        out BridgeWireError error)
    {
        result = default;
        if (!TryParse(payload, Acknowledgement.Parser, out Acknowledgement? wire, out error))
        {
            return false;
        }

        return BridgeMessageCodec.TryFromWire(wire, out result, out error);
    }

    /// <summary>Encodes a bounded session status.</summary>
    public static byte[] EncodeSessionStatus(LiveAuthoringSessionStatus status) =>
        Encode(BridgeMessageCodec.ToWire(status));

    /// <summary>Decodes a bounded session status.</summary>
    public static bool TryDecodeSessionStatus(
        ReadOnlySpan<byte> payload,
        out LiveAuthoringSessionStatus status,
        out BridgeWireError error)
    {
        status = default;
        if (!TryParse(payload, Wire.SessionStatus.Parser, out Wire.SessionStatus? wire, out error))
        {
            return false;
        }

        return BridgeMessageCodec.TryFromWire(wire, out status, out error);
    }

    /// <summary>Encodes a structured session event.</summary>
    public static byte[] EncodeSessionEvent(LiveAuthoringSessionEvent sessionEvent) =>
        Encode(BridgeMessageCodec.ToWire(sessionEvent));

    /// <summary>Decodes a structured session event.</summary>
    public static bool TryDecodeSessionEvent(
        ReadOnlySpan<byte> payload,
        out LiveAuthoringSessionEvent sessionEvent,
        out BridgeWireError error)
    {
        sessionEvent = default;
        if (!TryParse(payload, Wire.SessionEvent.Parser, out Wire.SessionEvent? wire, out error))
        {
            return false;
        }

        return BridgeMessageCodec.TryFromWire(wire, out sessionEvent, out error);
    }

    /// <summary>Encodes one server-to-client change-stream frame.</summary>
    public static byte[] EncodeStreamFrame(BridgeStreamFrame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        return Encode(ToWire(frame));
    }

    /// <summary>Decodes one server-to-client change-stream frame.</summary>
    public static bool TryDecodeStreamFrame(
        ReadOnlySpan<byte> payload,
        out BridgeStreamFrame? frame,
        out BridgeWireError error)
    {
        frame = null;
        if (!TryParse(payload, ChangeStreamMessage.Parser, out ChangeStreamMessage? wire, out error))
        {
            return false;
        }

        return TryFromWire(wire!, out frame, out error);
    }

    internal static ChangeStreamMessage ToWire(BridgeStreamFrame frame) => frame.Kind switch
    {
        BridgeStreamFrameKind.Handshake => new ChangeStreamMessage
        {
            Handshake = BridgeMessageCodec.ToWire(frame.Handshake)
        },
        BridgeStreamFrameKind.Snapshot => new ChangeStreamMessage
        {
            Snapshot = BridgeMessageCodec.ToWire(frame.Snapshot)
        },
        BridgeStreamFrameKind.Delta => new ChangeStreamMessage
        {
            Delta = BridgeMessageCodec.ToWire(frame.Delta)
        },
        BridgeStreamFrameKind.Acknowledgement => new ChangeStreamMessage
        {
            Acknowledgement = BridgeMessageCodec.ToWire(frame.Acknowledgement)
        },
        BridgeStreamFrameKind.SessionEvent => new ChangeStreamMessage
        {
            Event = BridgeMessageCodec.ToWire(frame.SessionEvent)
        },
        BridgeStreamFrameKind.SessionStatus => new ChangeStreamMessage
        {
            Status = BridgeMessageCodec.ToWire(frame.SessionStatus)
        },
        BridgeStreamFrameKind.ResyncRequired => CreateResyncFrame(frame),
        BridgeStreamFrameKind.KeepAlive => new ChangeStreamMessage
        {
            KeepAlive = new KeepAlive
            {
                TimestampUnixMillis = frame.TimestampUtc.ToUnixTimeMilliseconds()
            }
        },
        _ => throw new BridgeProtocolException(
            BridgeWireError.Create(
                BridgeWireErrorCode.UnknownStreamFrame,
                $"The stream frame kind '{frame.Kind}' has no wire case."))
    };

    internal static bool TryFromWire(
        ChangeStreamMessage wire,
        out BridgeStreamFrame? frame,
        out BridgeWireError error)
    {
        frame = null;
        switch (wire.MessageCase)
        {
            case ChangeStreamMessage.MessageOneofCase.Handshake:
                if (!BridgeMessageCodec.TryFromWire(
                    wire.Handshake,
                    out BridgeHandshakeResponse? handshake,
                    out error))
                {
                    return false;
                }

                frame = BridgeStreamFrame.ForHandshake(handshake!);
                return true;
            case ChangeStreamMessage.MessageOneofCase.Snapshot:
                if (!BridgeMessageCodec.TryFromWire(
                    wire.Snapshot,
                    out LiveAuthoringSnapshot? snapshot,
                    out error))
                {
                    return false;
                }

                frame = BridgeStreamFrame.ForSnapshot(snapshot!);
                return true;
            case ChangeStreamMessage.MessageOneofCase.Delta:
                if (!BridgeMessageCodec.TryFromWire(
                    wire.Delta,
                    out LiveAuthoringDelta? delta,
                    out error))
                {
                    return false;
                }

                frame = BridgeStreamFrame.ForDelta(delta!);
                return true;
            case ChangeStreamMessage.MessageOneofCase.Acknowledgement:
                if (!BridgeMessageCodec.TryFromWire(
                    wire.Acknowledgement,
                    out LiveAuthoringSessionResult result,
                    out error))
                {
                    return false;
                }

                frame = BridgeStreamFrame.ForAcknowledgement(result);
                return true;
            case ChangeStreamMessage.MessageOneofCase.Event:
                if (!BridgeMessageCodec.TryFromWire(
                    wire.Event,
                    out LiveAuthoringSessionEvent sessionEvent,
                    out error))
                {
                    return false;
                }

                frame = BridgeStreamFrame.ForEvent(sessionEvent);
                return true;
            case ChangeStreamMessage.MessageOneofCase.Status:
                if (!BridgeMessageCodec.TryFromWire(
                    wire.Status,
                    out LiveAuthoringSessionStatus status,
                    out error))
                {
                    return false;
                }

                frame = BridgeStreamFrame.ForStatus(status);
                return true;
            case ChangeStreamMessage.MessageOneofCase.ResyncRequired:
                return TryConvertResync(wire.ResyncRequired, out frame, out error);
            case ChangeStreamMessage.MessageOneofCase.KeepAlive:
                if (!BridgeMessageCodec.TryFromWireTimestamp(
                    wire.KeepAlive.TimestampUnixMillis,
                    "keep-alive",
                    out DateTimeOffset keepAlive,
                    out error))
                {
                    return false;
                }

                frame = BridgeStreamFrame.ForKeepAlive(keepAlive);
                error = BridgeWireError.None;
                return true;
            case ChangeStreamMessage.MessageOneofCase.None:
            default:
                error = BridgeWireError.Create(
                    BridgeWireErrorCode.UnknownStreamFrame,
                    $"The stream frame case '{wire.MessageCase}' is not supported by this version.");
                return false;
        }
    }

    internal static byte[] Encode(IMessage message)
    {
        int size = message.CalculateSize();
        if (size > BridgeProtocol.MaxFrameBytes)
        {
            throw new BridgeProtocolException(
                BridgeWireError.Create(
                    BridgeWireErrorCode.FrameTooLarge,
                    $"An encoded frame of {size} bytes exceeds the " +
                    $"{BridgeProtocol.MaxFrameBytes}-byte frame budget."));
        }

        return message.ToByteArray();
    }

    private static ChangeStreamMessage CreateResyncFrame(BridgeStreamFrame frame)
    {
        if (frame.Epoch is null)
        {
            throw new BridgeProtocolException(
                BridgeWireError.Create(
                    BridgeWireErrorCode.MissingField,
                    "A resync demand requires the epoch it applies to."));
        }

        var resync = new ResyncRequired
        {
            Epoch = BridgeMessageCodec.ToWire(frame.Epoch),
            Reason = BridgeMessageCodec.ToWire(frame.ResyncReason)
        };
        if (frame.Detail is not null)
        {
            resync.Detail = BridgeWireError.Create(BridgeWireErrorCode.None, frame.Detail).Detail;
        }

        return new ChangeStreamMessage { ResyncRequired = resync };
    }

    private static bool TryConvertResync(
        ResyncRequired wire,
        out BridgeStreamFrame? frame,
        out BridgeWireError error)
    {
        frame = null;
        if (!BridgeMessageCodec.TryFromWire(
            wire.Epoch,
            out LiveAuthoringRemoteEpoch? epoch,
            out error))
        {
            return false;
        }
        if (!BridgeMessageCodec.TryFromWire(wire.Reason, out BridgeResyncReason reason, out error))
        {
            return false;
        }

        frame = BridgeStreamFrame.ForResync(
            epoch!,
            reason,
            wire.HasDetail ? wire.Detail : null);
        error = BridgeWireError.None;
        return true;
    }

    private static bool TryParse<T>(
        ReadOnlySpan<byte> payload,
        MessageParser<T> parser,
        out T? message,
        out BridgeWireError error)
        where T : class, IMessage<T>
    {
        message = null;
        if (payload.Length > BridgeProtocol.MaxFrameBytes)
        {
            error = BridgeWireError.Create(
                BridgeWireErrorCode.FrameTooLarge,
                $"A frame of {payload.Length} bytes exceeds the " +
                $"{BridgeProtocol.MaxFrameBytes}-byte frame budget.");
            return false;
        }

        try
        {
            message = parser.ParseFrom(payload);
        }
        catch (InvalidProtocolBufferException)
        {
            // The exception message can quote payload bytes, so it is deliberately not forwarded:
            // a decode failure must not leak untrusted content into a diagnostic.
            error = BridgeWireError.Create(
                BridgeWireErrorCode.MalformedPayload,
                $"A {typeof(T).Name} frame of {payload.Length} bytes could not be parsed.");
            return false;
        }

        error = BridgeWireError.None;
        return true;
    }
}
