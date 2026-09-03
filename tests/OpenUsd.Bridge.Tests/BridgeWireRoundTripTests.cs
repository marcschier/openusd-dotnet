// Copyright (c) marcschier. Licensed under the MIT License.

using OpenUsd.Bridge.Protocol;
using OpenUsd.LiveAuthoring;

namespace OpenUsd.Bridge.Tests;

/// <summary>
/// Every update kind, value kind, and session message survives an encode/decode round trip without
/// losing a field, and the contract covers every authoring case rather than a convenient subset.
/// </summary>
public sealed class BridgeWireRoundTripTests
{
    private const string BridgeRoot = "/Bridge";
    private const string RemoteOrigin = "kit-bridge";
    private const string SessionId = "session-a";

    [Test]
    public async Task EveryStageUpdateKindSurvivesADeltaRoundTrip()
    {
        LiveStageUpdate[] updates = [.. BridgeTestData.AllUpdateKinds()];
        var delta = new LiveAuthoringDelta(
            BridgeTestData.Epoch(3),
            9,
            updates,
            coalescingKey: "coalesce",
            correlationId: "correlation",
            originId: RemoteOrigin);

        byte[] payload = BridgeWireCodec.EncodeDelta(delta);
        bool decoded = BridgeWireCodec.TryDecodeDelta(
            payload,
            out LiveAuthoringDelta? roundTripped,
            out BridgeWireError error);

        await Assert.That(decoded).IsTrue().Because(error.ToString());
        await Assert.That(roundTripped!.Sequence).IsEqualTo(9L);
        await Assert.That(roundTripped.Epoch.RemoteOriginId).IsEqualTo(RemoteOrigin);
        await Assert.That(roundTripped.Epoch.SessionId).IsEqualTo(SessionId);
        await Assert.That(roundTripped.Epoch.Epoch).IsEqualTo(3L);
        await Assert.That(roundTripped.CoalescingKey).IsEqualTo("coalesce");
        await Assert.That(roundTripped.CorrelationId).IsEqualTo("correlation");
        await Assert.That(roundTripped.OriginId).IsEqualTo(RemoteOrigin);
        await Assert.That(roundTripped.Updates.Count).IsEqualTo(updates.Length);
        for (int index = 0; index < updates.Length; index++)
        {
            await Assert.That(BridgeTestData.Describe(roundTripped.Updates[index]))
                .IsEqualTo(BridgeTestData.Describe(updates[index]));
        }
    }

    [Test]
    public async Task TheContractCoversEveryUpdateTypeExceptTheOverlayReplacement()
    {
        HashSet<string> covered = [.. BridgeTestData.AllUpdateKinds().Select(u => u.GetType().Name)];
        string[] authoringUpdateTypes =
        [
            .. typeof(LiveStageUpdate).Assembly
                .GetTypes()
                .Where(type => type.IsSealed &&
                    type.IsSubclassOf(typeof(LiveStageUpdate)) &&
                    type != typeof(ReplaceBridgeOverlayUpdate))
                .Select(type => type.Name)
        ];

        await Assert.That(authoringUpdateTypes.Length).IsGreaterThan(0);
        foreach (string name in authoringUpdateTypes)
        {
            await Assert.That(covered.Contains(name)).IsTrue()
                .Because($"'{name}' has no wire case or no round-trip coverage.");
        }
    }

    [Test]
    public async Task EveryAttributeValueKindSurvivesARoundTrip()
    {
        foreach (LiveAttributeKind kind in Enum.GetValues<LiveAttributeKind>())
        {
            LiveAttributeValue value = BridgeTestData.CreateAttributeValue(kind);
            var delta = new LiveAuthoringDelta(
                BridgeTestData.Epoch(1),
                1,
                [new SetAttributeUpdate($"{BridgeRoot}/Prim", "custom:value", value)],
                originId: RemoteOrigin);

            bool decoded = BridgeWireCodec.TryDecodeDelta(
                BridgeWireCodec.EncodeDelta(delta),
                out LiveAuthoringDelta? roundTripped,
                out BridgeWireError error);

            await Assert.That(decoded).IsTrue().Because($"{kind}: {error}");
            var update = (SetAttributeUpdate)roundTripped!.Updates[0];
            await Assert.That(update.Value.Kind).IsEqualTo(kind);
            await Assert.That(update.Value).IsEqualTo(value);
        }
    }

    [Test]
    public async Task EveryMetadataValueKindSurvivesARoundTrip()
    {
        foreach (LiveMetadataKind kind in Enum.GetValues<LiveMetadataKind>())
        {
            LiveMetadataValue value = BridgeTestData.CreateMetadataValue(kind);
            var delta = new LiveAuthoringDelta(
                BridgeTestData.Epoch(1),
                1,
                [new SetMetadataUpdate($"{BridgeRoot}/Prim", "customKey", value)],
                originId: RemoteOrigin);

            bool decoded = BridgeWireCodec.TryDecodeDelta(
                BridgeWireCodec.EncodeDelta(delta),
                out LiveAuthoringDelta? roundTripped,
                out BridgeWireError error);

            await Assert.That(decoded).IsTrue().Because($"{kind}: {error}");
            var update = (SetMetadataUpdate)roundTripped!.Updates[0];
            await Assert.That(update.Value.Kind).IsEqualTo(kind);
            await Assert.That(update.Value).IsEqualTo(value);
        }
    }

    [Test]
    public async Task EveryClearTargetKindSurvivesARoundTrip()
    {
        foreach (LiveClearTargetKind kind in Enum.GetValues<LiveClearTargetKind>())
        {
            string? name = kind is LiveClearTargetKind.AttributeValue or LiveClearTargetKind.Metadata
                ? "custom:value"
                : kind == LiveClearTargetKind.RelationshipTargets
                    ? "material:binding"
                    : null;
            var delta = new LiveAuthoringDelta(
                BridgeTestData.Epoch(1),
                1,
                [new ClearUpdate($"{BridgeRoot}/Prim", kind, name)],
                originId: RemoteOrigin);

            bool decoded = BridgeWireCodec.TryDecodeDelta(
                BridgeWireCodec.EncodeDelta(delta),
                out LiveAuthoringDelta? roundTripped,
                out BridgeWireError error);

            await Assert.That(decoded).IsTrue().Because($"{kind}: {error}");
            var update = (ClearUpdate)roundTripped!.Updates[0];
            await Assert.That(update.TargetKind).IsEqualTo(kind);
            await Assert.That(update.Name).IsEqualTo(name);
        }
    }

    [Test]
    public async Task ASnapshotSurvivesARoundTripIncludingAnEmptyOverlay()
    {
        var snapshot = new LiveAuthoringSnapshot(
            BridgeTestData.Epoch(2),
            41,
            BridgeRoot,
            [],
            correlationId: "resync-1");

        bool decoded = BridgeWireCodec.TryDecodeSnapshot(
            BridgeWireCodec.EncodeSnapshot(snapshot),
            out LiveAuthoringSnapshot? roundTripped,
            out BridgeWireError error);

        await Assert.That(decoded).IsTrue().Because(error.ToString());
        await Assert.That(roundTripped!.Sequence).IsEqualTo(41L);
        await Assert.That(roundTripped.BridgeRootPath).IsEqualTo(BridgeRoot);
        await Assert.That(roundTripped.Updates.Count).IsEqualTo(0);
        await Assert.That(roundTripped.CorrelationId).IsEqualTo("resync-1");
    }

    [Test]
    public async Task AnAcknowledgementSurvivesARoundTripForEveryAuthoringRejection()
    {
        foreach (LiveAuthoringSessionRejection rejection in
            Enum.GetValues<LiveAuthoringSessionRejection>())
        {
            var result = new LiveAuthoringSessionResult(
                rejection == LiveAuthoringSessionRejection.None
                    ? LiveAuthoringSessionOutcome.Applied
                    : LiveAuthoringSessionOutcome.Rejected,
                rejection,
                17,
                LiveAuthoringSessionState.ResyncRequired,
                17,
                16,
                "correlation",
                "detail");

            bool decoded = BridgeWireCodec.TryDecodeAcknowledgement(
                BridgeWireCodec.EncodeAcknowledgement(result),
                out LiveAuthoringSessionResult roundTripped,
                out BridgeWireError error);

            await Assert.That(decoded).IsTrue().Because($"{rejection}: {error}");
            await Assert.That(roundTripped).IsEqualTo(result);
        }
    }

    [Test]
    public async Task SessionStatusAndEventsSurviveARoundTrip()
    {
        var status = new LiveAuthoringSessionStatus(
            LiveAuthoringSessionState.Synchronized,
            RemoteOrigin,
            SessionId,
            4,
            12,
            12,
            2,
            10,
            1,
            3,
            1,
            2,
            5,
            6,
            64,
            7,
            448,
            5,
            "last failure",
            DateTimeOffset.FromUnixTimeMilliseconds(1_700_000_000_123),
            3,
            "observer failure");

        bool statusDecoded = BridgeWireCodec.TryDecodeSessionStatus(
            BridgeWireCodec.EncodeSessionStatus(status),
            out LiveAuthoringSessionStatus roundTrippedStatus,
            out BridgeWireError statusError);

        await Assert.That(statusDecoded).IsTrue().Because(statusError.ToString());
        await Assert.That(roundTrippedStatus).IsEqualTo(status);

        foreach (LiveAuthoringSessionEventKind kind in Enum.GetValues<LiveAuthoringSessionEventKind>())
        {
            var sessionEvent = new LiveAuthoringSessionEvent(
                kind,
                LiveAuthoringSessionState.Connecting,
                LiveAuthoringSessionState.Synchronized,
                RemoteOrigin,
                SessionId,
                4,
                12,
                12,
                "correlation",
                DateTimeOffset.FromUnixTimeMilliseconds(1_700_000_000_456),
                "detail");

            bool decoded = BridgeWireCodec.TryDecodeSessionEvent(
                BridgeWireCodec.EncodeSessionEvent(sessionEvent),
                out LiveAuthoringSessionEvent roundTripped,
                out BridgeWireError error);

            await Assert.That(decoded).IsTrue().Because($"{kind}: {error}");
            await Assert.That(roundTripped).IsEqualTo(sessionEvent);
        }
    }

    [Test]
    public async Task ALocalBatchSurvivesARoundTripAndKeepsItsIdempotencyKey()
    {
        var batch = new BridgeLocalBatch(
            BridgeTestData.Epoch(2),
            5,
            [new SetActiveUpdate($"{BridgeRoot}/Prim", false)],
            "openusd-local",
            idempotencyKey: "publish-5",
            coalescingKey: "coalesce",
            correlationId: "correlation");

        bool decoded = BridgeWireCodec.TryDecodeLocalBatch(
            BridgeWireCodec.EncodeLocalBatch(batch),
            out BridgeLocalBatch? roundTripped,
            out BridgeWireError error);

        await Assert.That(decoded).IsTrue().Because(error.ToString());
        await Assert.That(roundTripped!.IdempotencyKey).IsEqualTo("publish-5");
        await Assert.That(roundTripped.OriginId).IsEqualTo("openusd-local");
        await Assert.That(roundTripped.Sequence).IsEqualTo(5L);
        await Assert.That(roundTripped.Updates.Count).IsEqualTo(1);
    }

    /// <summary>
    /// A session identifier may be as long as the contract allows, so the derived key must be
    /// bounded rather than a concatenation that no peer would accept.
    /// </summary>
    [Test]
    public async Task AMaximalSessionIdentifierStillDerivesABoundedIdempotencyKey()
    {
        string sessionId = new('s', LiveAuthoringValidation.MaxOpaqueIdLength);
        var epoch = new LiveAuthoringRemoteEpoch(RemoteOrigin, sessionId, 4);

        BridgeLocalBatch batch = CreateDerivedKeyBatch(epoch, 5);
        BridgeLocalBatch again = CreateDerivedKeyBatch(epoch, 5);
        BridgeLocalBatch nextSequence = CreateDerivedKeyBatch(epoch, 6);
        BridgeLocalBatch nextEpoch = CreateDerivedKeyBatch(
            new LiveAuthoringRemoteEpoch(RemoteOrigin, sessionId, 5),
            5);
        BridgeLocalBatch otherSession = CreateDerivedKeyBatch(
            new LiveAuthoringRemoteEpoch(
                RemoteOrigin,
                sessionId[..^1] + "t",
                4),
            5);

        await Assert.That(batch.IdempotencyKey.Length)
            .IsLessThanOrEqualTo(LiveAuthoringValidation.MaxOpaqueIdLength);
        await Assert.That(batch.IdempotencyKey.Length).IsGreaterThan(0);

        // Deterministic for the same publication, and distinct for every neighbouring one: the
        // sequence, the epoch, and the session all still separate two keys.
        await Assert.That(again.IdempotencyKey).IsEqualTo(batch.IdempotencyKey);
        await Assert.That(nextSequence.IdempotencyKey).IsNotEqualTo(batch.IdempotencyKey);
        await Assert.That(nextEpoch.IdempotencyKey).IsNotEqualTo(batch.IdempotencyKey);
        await Assert.That(otherSession.IdempotencyKey).IsNotEqualTo(batch.IdempotencyKey);

        // The derived key is a legal opaque identifier, so a batch that carries it round-trips
        // instead of being refused when the peer re-validates it.
        bool decoded = BridgeWireCodec.TryDecodeLocalBatch(
            BridgeWireCodec.EncodeLocalBatch(batch),
            out BridgeLocalBatch? roundTripped,
            out BridgeWireError error);

        await Assert.That(decoded).IsTrue().Because(error.ToString());
        await Assert.That(roundTripped!.IdempotencyKey).IsEqualTo(batch.IdempotencyKey);
        await Assert.That(roundTripped.Epoch.SessionId).IsEqualTo(sessionId);
        await Assert.That(roundTripped.Sequence).IsEqualTo(5L);
    }

    /// <summary>
    /// A short origin and session identifier keep the readable origin/session/epoch/sequence key,
    /// so the common case is still diagnosable by eye.
    /// </summary>
    [Test]
    public async Task AShortSessionIdentifierKeepsTheReadableDerivedKey()
    {
        BridgeLocalBatch batch = CreateDerivedKeyBatch(BridgeTestData.Epoch(2), 5);

        bool decoded = BridgeWireCodec.TryDecodeLocalBatch(
            BridgeWireCodec.EncodeLocalBatch(batch),
            out BridgeLocalBatch? roundTripped,
            out BridgeWireError error);

        await Assert.That(batch.IdempotencyKey).IsEqualTo($"openusd-local:{SessionId}:2:5");
        await Assert.That(decoded).IsTrue().Because(error.ToString());
        await Assert.That(roundTripped!.IdempotencyKey).IsEqualTo(batch.IdempotencyKey);
    }

    /// <summary>
    /// Two hosts publishing into one session allocate their own per-epoch sequences, so a derived
    /// key that named only the session, epoch, and sequence would collide across them — and a
    /// colliding key is exactly what a peer's idempotency ledger reads as "already applied". The
    /// origin is therefore part of both the readable and the hashed form.
    /// </summary>
    [Test]
    public async Task DerivedKeysFromDifferentOriginsAreDistinctInBothForms()
    {
        LiveAuthoringRemoteEpoch shortSession = BridgeTestData.Epoch(2);
        var longSession = new LiveAuthoringRemoteEpoch(
            RemoteOrigin,
            new string('s', LiveAuthoringValidation.MaxOpaqueIdLength),
            2);

        BridgeLocalBatch readableFirst = CreateDerivedKeyBatch(shortSession, 5, "openusd-local");
        BridgeLocalBatch readableSecond = CreateDerivedKeyBatch(shortSession, 5, "openusd-other");
        BridgeLocalBatch hashedFirst = CreateDerivedKeyBatch(longSession, 5, "openusd-local");
        BridgeLocalBatch hashedSecond = CreateDerivedKeyBatch(longSession, 5, "openusd-other");

        // The same session, epoch, and sequence from two origins are two publications.
        await Assert.That(readableFirst.IdempotencyKey)
            .IsNotEqualTo(readableSecond.IdempotencyKey);
        await Assert.That(hashedFirst.IdempotencyKey).IsNotEqualTo(hashedSecond.IdempotencyKey);
        await Assert.That(readableFirst.IdempotencyKey).Contains("openusd-local");
        await Assert.That(hashedFirst.IdempotencyKey).StartsWith("sha256-");

        // Both forms stay deterministic and inside the opaque-identifier bound.
        await Assert.That(CreateDerivedKeyBatch(shortSession, 5, "openusd-local").IdempotencyKey)
            .IsEqualTo(readableFirst.IdempotencyKey);
        await Assert.That(CreateDerivedKeyBatch(longSession, 5, "openusd-local").IdempotencyKey)
            .IsEqualTo(hashedFirst.IdempotencyKey);
        await Assert.That(hashedFirst.IdempotencyKey.Length)
            .IsLessThanOrEqualTo(LiveAuthoringValidation.MaxOpaqueIdLength);
        await Assert.That(readableFirst.IdempotencyKey.Length)
            .IsLessThanOrEqualTo(LiveAuthoringValidation.MaxOpaqueIdLength);

        // And both survive the wire, so the peer re-validates the same key it will ledger.
        foreach (BridgeLocalBatch batch in new[] { readableFirst, readableSecond, hashedFirst, hashedSecond })
        {
            bool decoded = BridgeWireCodec.TryDecodeLocalBatch(
                BridgeWireCodec.EncodeLocalBatch(batch),
                out BridgeLocalBatch? roundTripped,
                out BridgeWireError error);

            await Assert.That(decoded).IsTrue().Because(error.ToString());
            await Assert.That(roundTripped!.IdempotencyKey).IsEqualTo(batch.IdempotencyKey);
            await Assert.That(roundTripped.OriginId).IsEqualTo(batch.OriginId);
        }
    }

    /// <summary>
    /// An origin or a session identifier that contains the readable separator cannot use the
    /// readable form without becoming ambiguous, so it falls back to the length-prefixed digest.
    /// </summary>
    [Test]
    public async Task AnAmbiguousOriginAndSessionPairDeriveDistinctHashedKeys()
    {
        BridgeLocalBatch first = CreateDerivedKeyBatch(
            new LiveAuthoringRemoteEpoch(RemoteOrigin, "b:c", 2),
            5,
            "a");
        BridgeLocalBatch second = CreateDerivedKeyBatch(
            new LiveAuthoringRemoteEpoch(RemoteOrigin, "c", 2),
            5,
            "a:b");

        await Assert.That(first.IdempotencyKey).StartsWith("sha256-");
        await Assert.That(second.IdempotencyKey).StartsWith("sha256-");
        await Assert.That(first.IdempotencyKey).IsNotEqualTo(second.IdempotencyKey);
    }

    [Test]
    public async Task EveryStreamFrameKindSurvivesARoundTrip()
    {
        BridgeStreamFrame[] frames =
        [
            BridgeTestData.SnapshotFrame(),
            BridgeTestData.DeltaFrame(),
            BridgeTestData.AcknowledgementFrame(),
            BridgeTestData.ResyncFrame(BridgeResyncReason.SequenceGap),
            BridgeTestData.KeepAliveFrame()
        ];

        foreach (BridgeStreamFrame frame in frames)
        {
            bool decoded = BridgeWireCodec.TryDecodeStreamFrame(
                BridgeWireCodec.EncodeStreamFrame(frame),
                out BridgeStreamFrame? roundTripped,
                out BridgeWireError error);

            await Assert.That(decoded).IsTrue().Because($"{frame.Kind}: {error}");
            await Assert.That(roundTripped!.Kind).IsEqualTo(frame.Kind);
        }
    }

    [Test]
    public async Task AnOverlayReplacementCannotBeEncodedAsAnUpdate()
    {
        var delta = new LiveAuthoringDelta(
            BridgeTestData.Epoch(1),
            1,
            [new ReplaceBridgeOverlayUpdate(BridgeRoot, [])],
            originId: RemoteOrigin);

        BridgeProtocolException exception =
            Assert.Throws<BridgeProtocolException>(() => BridgeWireCodec.EncodeDelta(delta));

        await Assert.That(exception.Error.Code)
            .IsEqualTo(BridgeWireErrorCode.OverlayReplacementNotAllowed);
    }

    private static BridgeLocalBatch CreateDerivedKeyBatch(
        LiveAuthoringRemoteEpoch epoch,
        long sequence,
        string originId = "openusd-local") =>
        new(
            epoch,
            sequence,
            [new SetActiveUpdate($"{BridgeRoot}/Prim", false)],
            originId);
}
