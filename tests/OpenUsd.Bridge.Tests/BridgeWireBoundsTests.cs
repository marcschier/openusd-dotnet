// Copyright (c) marcschier. Licensed under the MIT License.

using Google.Protobuf;
using OpenUsd.Bridge.Protocol;
using OpenUsd.Bridge.Protocol.Wire;
using OpenUsd.LiveAuthoring;

using Wire = OpenUsd.Bridge.Protocol.Wire;

namespace OpenUsd.Bridge.Tests;

/// <summary>
/// Untrusted input is rejected with a bounded, specific error and never throws out of a decode path,
/// never allocates past a documented bound, and never silently accepts an unknown case.
/// </summary>
public sealed class BridgeWireBoundsTests
{
    /// <summary>The smallest timestamp the contract accepts, mirrored from the codec.</summary>
    private const long MinTimestampUnixMillis = BridgeMessageCodec.MinTimestampUnixMillis;

    /// <summary>The largest timestamp the contract accepts, mirrored from the codec.</summary>
    private const long MaxTimestampUnixMillis = BridgeMessageCodec.MaxTimestampUnixMillis;

    [Test]
    public async Task RandomBytesAreRejectedWithoutThrowing()
    {
        var random = new Random(20260901);
        int rejected = 0;
        for (int iteration = 0; iteration < 512; iteration++)
        {
            var payload = new byte[random.Next(1, 512)];
            random.NextBytes(payload);

            bool decodedDelta = BridgeWireCodec.TryDecodeDelta(payload, out _, out _);
            bool decodedSnapshot = BridgeWireCodec.TryDecodeSnapshot(payload, out _, out _);
            bool decodedFrame = BridgeWireCodec.TryDecodeStreamFrame(payload, out _, out _);
            bool decodedStatus = BridgeWireCodec.TryDecodeSessionStatus(payload, out _, out _);
            if (!decodedDelta && !decodedSnapshot && !decodedFrame && !decodedStatus)
            {
                rejected++;
            }
        }

        // Random bytes occasionally form a structurally valid but semantically empty message; the
        // contract requires that such a message still fails its own required-field checks, which is
        // why every decode above must reject rather than return a half-built value.
        await Assert.That(rejected).IsEqualTo(512);
    }

    [Test]
    public async Task TruncatedPayloadsAreRejectedAsMalformed()
    {
        byte[] payload = BridgeWireCodec.EncodeDelta(BridgeTestData.Delta(3));
        for (int length = 1; length < payload.Length; length++)
        {
            bool decoded = BridgeWireCodec.TryDecodeDelta(
                payload.AsSpan(0, length),
                out LiveAuthoringDelta? delta,
                out BridgeWireError error);
            if (decoded)
            {
                // A prefix can still be a valid message when the truncation lands on a field
                // boundary; when it decodes it must decode to a complete, bounded value.
                await Assert.That(delta).IsNotNull();
                continue;
            }

            await Assert.That(error.IsError).IsTrue();
            await Assert.That(error.Detail.Length).IsLessThanOrEqualTo(BridgeWireError.MaxDetailLength);
        }
    }

    [Test]
    public async Task AnUnsetUpdateOneofIsRejectedAsAnUnknownUpdateKind()
    {
        var wire = new StageDelta
        {
            Epoch = new SessionEpoch
            {
                RemoteOriginId = BridgeTestData.RemoteOrigin,
                SessionId = BridgeTestData.SessionId,
                Epoch = 1
            },
            Sequence = 1,
            Updates = { new StageUpdate() }
        };

        bool decoded = BridgeWireCodec.TryDecodeDelta(
            wire.ToByteArray(),
            out _,
            out BridgeWireError error);

        await Assert.That(decoded).IsFalse();
        await Assert.That(error.Code).IsEqualTo(BridgeWireErrorCode.UnknownUpdateKind);
    }

    [Test]
    public async Task AnUnsetValueOneofIsRejectedAsAnUnknownValueKind()
    {
        var wire = new StageDelta
        {
            Epoch = ValidEpoch(),
            Sequence = 1,
            Updates =
            {
                new StageUpdate
                {
                    SetAttribute = new SetAttribute
                    {
                        PrimPath = "/Bridge/Cube",
                        AttributeName = "custom:value",
                        Value = new AttributeValue()
                    }
                }
            }
        };

        bool decoded = BridgeWireCodec.TryDecodeDelta(
            wire.ToByteArray(),
            out _,
            out BridgeWireError error);

        await Assert.That(decoded).IsFalse();
        await Assert.That(error.Code).IsEqualTo(BridgeWireErrorCode.UnknownValueKind);
    }

    [Test]
    public async Task AnUnspecifiedEnumIsRejectedRatherThanDefaulted()
    {
        var wire = new StageDelta
        {
            Epoch = ValidEpoch(),
            Sequence = 1,
            Updates =
            {
                new StageUpdate
                {
                    Clear = new Wire.Clear
                    {
                        PrimPath = "/Bridge/Cube",
                        Target = ClearTarget.Unspecified
                    }
                }
            }
        };

        bool decoded = BridgeWireCodec.TryDecodeDelta(
            wire.ToByteArray(),
            out _,
            out BridgeWireError error);

        await Assert.That(decoded).IsFalse();
        await Assert.That(error.Code).IsEqualTo(BridgeWireErrorCode.UnknownEnumValue);
    }

    [Test]
    public async Task AnUnknownEnumValueFromANewerPeerIsRejectedExplicitly()
    {
        var wire = new StageDelta
        {
            Epoch = ValidEpoch(),
            Sequence = 1,
            Updates =
            {
                new StageUpdate
                {
                    ApiSchema = new Wire.ApiSchema
                    {
                        PrimPath = "/Bridge/Cube",
                        SchemaToken = "AssetPreviewsAPI",
                        Operation = (ApiSchemaOperation)9999
                    }
                }
            }
        };

        bool decoded = BridgeWireCodec.TryDecodeDelta(
            wire.ToByteArray(),
            out _,
            out BridgeWireError error);

        await Assert.That(decoded).IsFalse();
        await Assert.That(error.Code).IsEqualTo(BridgeWireErrorCode.UnknownEnumValue);
    }

    [Test]
    public async Task AnUnsetStreamFrameIsRejected()
    {
        bool decoded = BridgeWireCodec.TryDecodeStreamFrame(
            new ChangeStreamMessage().ToByteArray(),
            out _,
            out BridgeWireError error);

        await Assert.That(decoded).IsFalse();
        await Assert.That(error.Code).IsEqualTo(BridgeWireErrorCode.UnknownStreamFrame);
    }

    [Test]
    public async Task AMalformedMatrixIsRejectedWithItsComponentCount()
    {
        var value = new AttributeValue { Matrix4DValue = new Matrix4d { Values = { 1.0, 2.0 } } };
        var wire = new StageDelta
        {
            Epoch = ValidEpoch(),
            Sequence = 1,
            Updates =
            {
                new StageUpdate
                {
                    SetAttribute = new SetAttribute
                    {
                        PrimPath = "/Bridge/Cube",
                        AttributeName = "custom:matrix",
                        Value = value
                    }
                }
            }
        };

        bool decoded = BridgeWireCodec.TryDecodeDelta(
            wire.ToByteArray(),
            out _,
            out BridgeWireError error);

        await Assert.That(decoded).IsFalse();
        await Assert.That(error.Code).IsEqualTo(BridgeWireErrorCode.FieldOutOfRange);
    }

    [Test]
    public async Task TooManyUpdatesAreRejectedBeforeAnyUpdateIsConverted()
    {
        var wire = new StageDelta { Epoch = ValidEpoch(), Sequence = 1 };
        for (int index = 0; index <= LiveAuthoringValidation.MaxUpdatesPerBatch; index++)
        {
            wire.Updates.Add(new StageUpdate
            {
                SetActive = new SetActive { PrimPath = "/Bridge/Cube", Active = true }
            });
        }

        bool decoded = BridgeWireCodec.TryDecodeDelta(
            wire.ToByteArray(),
            out _,
            out BridgeWireError error);

        await Assert.That(decoded).IsFalse();
        await Assert.That(error.Code).IsEqualTo(BridgeWireErrorCode.LimitExceeded);
    }

    [Test]
    public async Task AnOversizedTextValueIsRejectedByTheAuthoringBound()
    {
        var wire = new StageDelta
        {
            Epoch = ValidEpoch(),
            Sequence = 1,
            Updates =
            {
                new StageUpdate
                {
                    SetAttribute = new SetAttribute
                    {
                        PrimPath = "/Bridge/Cube",
                        AttributeName = "custom:value",
                        Value = new AttributeValue
                        {
                            StringValue = new string('x', LiveAuthoringValidation.MaxTextValueLength + 1)
                        }
                    }
                }
            }
        };

        bool decoded = BridgeWireCodec.TryDecodeDelta(
            wire.ToByteArray(),
            out _,
            out BridgeWireError error);

        // The authoring layer measures text bounds when the delta is assembled, not when a single
        // value is created, so the breach surfaces as the delta's own limit rejection. What matters
        // is that it is refused with a bounded, specific error rather than authored.
        await Assert.That(decoded).IsFalse();
        await Assert.That(error.Code).IsEqualTo(BridgeWireErrorCode.LimitExceeded);
        await Assert.That(error.Detail.Length).IsLessThanOrEqualTo(BridgeWireError.MaxDetailLength);
    }

    [Test]
    public async Task AnOversizedFrameIsRejectedBeforeItIsParsed()
    {
        var payload = new byte[BridgeProtocol.MaxFrameBytes + 1];

        bool decoded = BridgeWireCodec.TryDecodeDelta(payload, out _, out BridgeWireError error);

        await Assert.That(decoded).IsFalse();
        await Assert.That(error.Code).IsEqualTo(BridgeWireErrorCode.FrameTooLarge);
    }

    [Test]
    public async Task AMissingEpochIsRejectedAsAMissingField()
    {
        var wire = new StageDelta { Sequence = 1 };

        bool decoded = BridgeWireCodec.TryDecodeDelta(
            wire.ToByteArray(),
            out _,
            out BridgeWireError error);

        await Assert.That(decoded).IsFalse();
        await Assert.That(error.Code).IsEqualTo(BridgeWireErrorCode.MissingField);
    }

    [Test]
    public async Task ANonPositiveDeltaSequenceIsRejected()
    {
        var wire = new StageDelta { Epoch = ValidEpoch(), Sequence = 0 };

        bool decoded = BridgeWireCodec.TryDecodeDelta(
            wire.ToByteArray(),
            out _,
            out BridgeWireError error);

        await Assert.That(decoded).IsFalse();
        await Assert.That(error.Code).IsEqualTo(BridgeWireErrorCode.FieldOutOfRange);
    }

    [Test]
    public async Task AProtocolOnlyRejectionIsNotMappedOntoAnAuthoringRejection()
    {
        var wire = new Acknowledgement
        {
            Outcome = SessionOutcome.Rejected,
            Rejection = SessionRejection.ProtocolViolation,
            Sequence = 4,
            State = Wire.SessionState.ResyncRequired
        };

        bool decoded = BridgeWireCodec.TryDecodeAcknowledgement(
            wire.ToByteArray(),
            out _,
            out BridgeWireError error);

        await Assert.That(decoded).IsFalse();
        await Assert.That(error.Code).IsEqualTo(BridgeWireErrorCode.UnknownEnumValue);
    }

    [Test]
    public async Task ErrorDetailsNeverExceedTheBoundedLength()
    {
        BridgeWireError error = BridgeWireError.Create(
            BridgeWireErrorCode.MalformedPayload,
            new string('d', BridgeWireError.MaxDetailLength * 4));

        await Assert.That(error.Detail.Length).IsEqualTo(BridgeWireError.MaxDetailLength);
    }

    [Test]
    public async Task DecodedLimitsAreClampedAndNeverExceedTheLocalBound()
    {
        var wire = new HandshakeResponse
        {
            Accepted = true,
            ServerVersion = new ProtocolVersion { Major = 1, Minor = 0 },
            BridgeRootPath = BridgeTestData.BridgeRoot,
            EffectiveLimits = new Limits
            {
                MaxUpdatesPerMessage = uint.MaxValue,
                MaxCollectionElementCount = uint.MaxValue,
                MaxMessagePayloadBytes = ulong.MaxValue,
                MaxIdentifierLength = uint.MaxValue,
                MaxPathLength = uint.MaxValue,
                MaxTextValueLength = uint.MaxValue,
                MaxOpaqueIdLength = uint.MaxValue,
                MaxTotalCollectionElementCount = ulong.MaxValue
            },
            Rejection = HandshakeRejection.None,
            Epoch = ValidEpoch()
        };

        bool decoded = BridgeWireCodec.TryDecodeHandshakeResponse(
            wire.ToByteArray(),
            out BridgeHandshakeResponse? response,
            out BridgeWireError error);

        await Assert.That(decoded).IsTrue().Because(error.ToString());
        BridgeLimits effective = BridgeLimits.Local.Intersect(response!.EffectiveLimits);
        await Assert.That(effective).IsEqualTo(BridgeLimits.Local);
        await Assert.That(effective.IsUsable).IsTrue();
    }

    /// <summary>
    /// The accepted timestamp range is exactly what <see cref="DateTimeOffset"/> can represent, so
    /// the check can never be looser than the conversion it guards.
    /// </summary>
    [Test]
    public async Task TheTimestampBoundsAreExactlyWhatDateTimeOffsetCanRepresent()
    {
        await Assert.That(DateTimeOffset.MinValue.ToUnixTimeMilliseconds())
            .IsEqualTo(MinTimestampUnixMillis);
        await Assert.That(DateTimeOffset.MaxValue.ToUnixTimeMilliseconds())
            .IsEqualTo(MaxTimestampUnixMillis);
    }

    /// <summary>
    /// A timestamp is an <c>int64</c> on the wire and a peer owns every bit of it, but
    /// <see cref="DateTimeOffset"/> covers only part of that range. The exact bounds must decode.
    /// </summary>
    [Test]
    [Arguments(MinTimestampUnixMillis)]
    [Arguments(MaxTimestampUnixMillis)]
    [Arguments(0L)]
    public async Task ATimestampAtTheRepresentableBoundsIsDecoded(long timestampUnixMillis)
    {
        bool keepAlive = BridgeWireCodec.TryDecodeStreamFrame(
            new ChangeStreamMessage
            {
                KeepAlive = new KeepAlive { TimestampUnixMillis = timestampUnixMillis }
            }.ToByteArray(),
            out BridgeStreamFrame? frame,
            out BridgeWireError keepAliveError);

        bool sessionEvent = BridgeWireCodec.TryDecodeSessionEvent(
            new Wire.SessionEvent
            {
                Kind = SessionEventKind.Connecting,
                PreviousState = Wire.SessionState.Disconnected,
                State = Wire.SessionState.Synchronized,
                TimestampUnixMillis = timestampUnixMillis
            }.ToByteArray(),
            out LiveAuthoringSessionEvent decodedEvent,
            out BridgeWireError eventError);

        bool status = BridgeWireCodec.TryDecodeSessionStatus(
            new Wire.SessionStatus
            {
                State = Wire.SessionState.Synchronized,
                TimestampUnixMillis = timestampUnixMillis
            }.ToByteArray(),
            out LiveAuthoringSessionStatus decodedStatus,
            out BridgeWireError statusError);

        await Assert.That(keepAlive).IsTrue().Because(keepAliveError.ToString());
        await Assert.That(sessionEvent).IsTrue().Because(eventError.ToString());
        await Assert.That(status).IsTrue().Because(statusError.ToString());
        DateTimeOffset expected = DateTimeOffset.FromUnixTimeMilliseconds(timestampUnixMillis);
        await Assert.That(frame!.TimestampUtc).IsEqualTo(expected);
        await Assert.That(decodedEvent.TimestampUtc).IsEqualTo(expected);
        await Assert.That(decodedStatus.TimestampUtc).IsEqualTo(expected);
    }

    /// <summary>
    /// One integer outside the representable range must not throw out of a decode path: it is the
    /// difference between a rejected frame and a receive loop that dies on untrusted input.
    /// </summary>
    [Test]
    [Arguments(MinTimestampUnixMillis - 1)]
    [Arguments(MaxTimestampUnixMillis + 1)]
    [Arguments(long.MinValue)]
    [Arguments(long.MaxValue)]
    public async Task ATimestampOutsideTheRepresentableRangeIsRejectedWithoutThrowing(
        long timestampUnixMillis)
    {
        bool keepAlive = BridgeWireCodec.TryDecodeStreamFrame(
            new ChangeStreamMessage
            {
                KeepAlive = new KeepAlive { TimestampUnixMillis = timestampUnixMillis }
            }.ToByteArray(),
            out BridgeStreamFrame? frame,
            out BridgeWireError keepAliveError);

        bool sessionEvent = BridgeWireCodec.TryDecodeSessionEvent(
            new Wire.SessionEvent
            {
                Kind = SessionEventKind.Connecting,
                PreviousState = Wire.SessionState.Disconnected,
                State = Wire.SessionState.Synchronized,
                TimestampUnixMillis = timestampUnixMillis
            }.ToByteArray(),
            out _,
            out BridgeWireError eventError);

        bool status = BridgeWireCodec.TryDecodeSessionStatus(
            new Wire.SessionStatus
            {
                State = Wire.SessionState.Synchronized,
                TimestampUnixMillis = timestampUnixMillis
            }.ToByteArray(),
            out _,
            out BridgeWireError statusError);

        await Assert.That(keepAlive).IsFalse();
        await Assert.That(frame).IsNull();
        await Assert.That(sessionEvent).IsFalse();
        await Assert.That(status).IsFalse();
        foreach (BridgeWireError error in new[] { keepAliveError, eventError, statusError })
        {
            await Assert.That(error.Code).IsEqualTo(BridgeWireErrorCode.FieldOutOfRange);
            await Assert.That(error.Detail.Length)
                .IsLessThanOrEqualTo(BridgeWireError.MaxDetailLength);
        }
    }

    /// <summary>
    /// The same out-of-range timestamp inside a change-stream frame is refused frame-first, so a
    /// transport that only decodes stream frames is protected by the same check.
    /// </summary>
    [Test]
    public async Task AnOutOfRangeTimestampInsideAStreamFrameIsRejected()
    {
        var wire = new ChangeStreamMessage
        {
            Status = new Wire.SessionStatus
            {
                State = Wire.SessionState.Synchronized,
                TimestampUnixMillis = long.MaxValue
            }
        };

        bool decoded = BridgeWireCodec.TryDecodeStreamFrame(
            wire.ToByteArray(),
            out BridgeStreamFrame? frame,
            out BridgeWireError error);

        await Assert.That(decoded).IsFalse();
        await Assert.That(frame).IsNull();
        await Assert.That(error.Code).IsEqualTo(BridgeWireErrorCode.FieldOutOfRange);
    }

    private static SessionEpoch ValidEpoch() =>
        new()
        {
            RemoteOriginId = BridgeTestData.RemoteOrigin,
            SessionId = BridgeTestData.SessionId,
            Epoch = 1
        };
}
