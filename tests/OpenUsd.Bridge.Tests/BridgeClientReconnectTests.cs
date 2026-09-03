// Copyright (c) marcschier. Licensed under the MIT License.

using Grpc.Core;
using OpenUsd.Bridge.Grpc;
using OpenUsd.Bridge.Protocol;
using OpenUsd.LiveAuthoring;

using Wire = OpenUsd.Bridge.Protocol.Wire;

namespace OpenUsd.Bridge.Tests;

/// <summary>
/// Drives the client's connect, negotiate, resync, stream, and reconnect state machine against an
/// in-memory peer. Duplicate, gap, and conflict semantics stay with the coordinator; what is proven
/// here is that the adapter reacts to the coordinator's verdicts correctly.
/// </summary>
public sealed class BridgeClientReconnectTests
{
    [Test]
    public async Task TheClientNegotiatesTakesASnapshotAndAppliesOrderedDeltas()
    {
        await using var harness = await BridgeClientHarness.StartAsync();

        await harness.WaitForStateAsync(BridgeConnectionState.Streaming);
        harness.Server.Send(BridgeTestData.DeltaFrame(1));
        harness.Server.Send(BridgeTestData.DeltaFrame(2));
        await harness.WaitForAsync(() => harness.Client.GetStatus().DeltaAppliedCount >= 2);

        BridgeClientStatus status = harness.Client.GetStatus();
        await Assert.That(status.SnapshotAppliedCount).IsGreaterThanOrEqualTo(1);
        await Assert.That(status.DeltaAppliedCount).IsEqualTo(2L);
        await Assert.That(harness.Coordinator.State)
            .IsEqualTo(LiveAuthoringSessionState.Synchronized);
        await Assert.That(harness.Coordinator.GetStatus().LastAppliedSequence).IsEqualTo(2L);
    }

    [Test]
    public async Task EveryAppliedMessageIsAcknowledgedBackToThePeer()
    {
        await using var harness = await BridgeClientHarness.StartAsync();

        await harness.WaitForStateAsync(BridgeConnectionState.Streaming);
        harness.Server.Send(BridgeTestData.DeltaFrame(1));
        await harness.WaitForAsync(() => harness.Client.GetStatus().DeltaAppliedCount >= 1);
        await harness.WaitForAsync(() => harness.Server.Received.Any(
            request => request.RequestCase ==
                Wire.ChangeStreamRequest.RequestOneofCase.Acknowledgement));

        Wire.ChangeStreamRequest acknowledgement = harness.Server.Received.First(
            request => request.RequestCase ==
                Wire.ChangeStreamRequest.RequestOneofCase.Acknowledgement);
        await Assert.That(acknowledgement.Acknowledgement.Sequence).IsEqualTo(1L);
        await Assert.That(acknowledgement.Acknowledgement.Outcome)
            .IsEqualTo(Wire.SessionOutcome.Applied);
    }

    [Test]
    public async Task ASequenceGapTriggersAFullResyncRatherThanASynthesizedRepair()
    {
        await using var harness = await BridgeClientHarness.StartAsync();

        await harness.WaitForStateAsync(BridgeConnectionState.Streaming);
        harness.Server.Send(BridgeTestData.DeltaFrame(1));
        await harness.WaitForAsync(() => harness.Client.GetStatus().DeltaAppliedCount >= 1);

        harness.Server.SnapshotSequence = 5;
        harness.Server.Send(BridgeTestData.DeltaFrame(5));
        await harness.WaitForAsync(() => harness.Client.GetStatus().SnapshotAppliedCount >= 2);

        BridgeClientStatus status = harness.Client.GetStatus();
        await Assert.That(status.ResyncCount).IsGreaterThanOrEqualTo(2L);
        await Assert.That(harness.Coordinator.State)
            .IsEqualTo(LiveAuthoringSessionState.Synchronized);
        await Assert.That(harness.Coordinator.GetStatus().LastAppliedSequence).IsEqualTo(5L);
    }

    [Test]
    public async Task AServerResyncDemandTakesAFreshSnapshotWithoutReconnecting()
    {
        await using var harness = await BridgeClientHarness.StartAsync();

        await harness.WaitForStateAsync(BridgeConnectionState.Streaming);
        int streamsBefore = harness.Server.StreamCount;
        harness.Server.SnapshotSequence = 9;
        harness.Server.Send(BridgeTestData.ResyncFrame(BridgeResyncReason.ServerRequested));
        await harness.WaitForAsync(() => harness.Client.GetStatus().SnapshotAppliedCount >= 2);

        await Assert.That(harness.Server.StreamCount).IsEqualTo(streamsBefore);
        await Assert.That(harness.Coordinator.GetStatus().LastAppliedSequence).IsEqualTo(9L);
    }

    [Test]
    public async Task ADroppedStreamReconnectsAndTakesANewSnapshot()
    {
        await using var harness = await BridgeClientHarness.StartAsync();

        await harness.WaitForStateAsync(BridgeConnectionState.Streaming);
        int snapshotsBefore = harness.Server.SnapshotRequestCount;
        harness.Server.CloseStream();

        await harness.WaitForAsync(() => harness.Server.StreamCount >= 2);
        await harness.WaitForStateAsync(BridgeConnectionState.Streaming);

        await Assert.That(harness.Server.NegotiateCount).IsGreaterThanOrEqualTo(2);
        await Assert.That(harness.Server.SnapshotRequestCount).IsGreaterThan(snapshotsBefore);
        await Assert.That(harness.Client.GetStatus().ReconnectCount).IsGreaterThanOrEqualTo(1L);
    }

    [Test]
    public async Task AnAdvancedEpochAfterAReconnectStillReachesSynchronized()
    {
        await using var harness = await BridgeClientHarness.StartAsync();

        await harness.WaitForStateAsync(BridgeConnectionState.Streaming);
        harness.Server.Epoch = 2;
        harness.Server.SnapshotSequence = 0;
        harness.Server.CloseStream();

        await harness.WaitForAsync(() => harness.Client.GetStatus().Epoch == 2);
        await harness.WaitForStateAsync(BridgeConnectionState.Streaming);

        await Assert.That(harness.Coordinator.State)
            .IsEqualTo(LiveAuthoringSessionState.Synchronized);
        await Assert.That(harness.Coordinator.GetStatus().Epoch).IsEqualTo(2L);
    }

    [Test]
    public async Task AnIncompatiblePeerVersionFaultsWithoutRetrying()
    {
        var server = new FakeBridgeServer { Accept = false, Rejection = BridgeHandshakeRejection.Version };
        await using var harness = await BridgeClientHarness.StartAsync(server, waitForStreaming: false);

        await harness.WaitForStateAsync(BridgeConnectionState.Faulted);
        await harness.Run.WaitAsync(TimeSpan.FromSeconds(10));

        await Assert.That(server.NegotiateCount).IsEqualTo(1);
        await Assert.That(harness.Client.GetStatus().State).IsEqualTo(BridgeConnectionState.Faulted);
    }

    [Test]
    public async Task AnUnauthenticatedPeerFaultsWithoutRetrying()
    {
        var server = new FakeBridgeServer { NegotiateFailure = StatusCode.Unauthenticated };
        await using var harness = await BridgeClientHarness.StartAsync(server, waitForStreaming: false);

        await harness.WaitForStateAsync(BridgeConnectionState.Faulted);
        await harness.Run.WaitAsync(TimeSpan.FromSeconds(10));

        await Assert.That(server.NegotiateCount).IsEqualTo(1);
    }

    [Test]
    public async Task AnUnavailablePeerBacksOffAndReconnects()
    {
        var server = new FakeBridgeServer { NegotiateFailure = StatusCode.Unavailable };
        await using var harness = await BridgeClientHarness.StartAsync(server, waitForStreaming: false);

        await harness.WaitForAsync(() => server.NegotiateCount >= 2);
        server.NegotiateFailure = null;
        await harness.WaitForStateAsync(BridgeConnectionState.Streaming);

        await Assert.That(harness.Client.GetStatus().ReconnectCount).IsGreaterThanOrEqualTo(1L);
    }

    [Test]
    public async Task EveryCallCarriesACredentialAndNoEventEverContainsIt()
    {
        await using var harness = await BridgeClientHarness.StartAsync();

        await harness.WaitForStateAsync(BridgeConnectionState.Streaming);

        await Assert.That(harness.Server.AuthorizationHeaders.Count).IsGreaterThanOrEqualTo(2);
        foreach (string header in harness.Server.AuthorizationHeaders)
        {
            await Assert.That(header).IsEqualTo($"Bearer {BridgeClientHarness.Token}");
        }

        foreach (BridgeClientEvent observed in harness.Events)
        {
            await Assert.That(observed.Detail ?? string.Empty)
                .DoesNotContain(BridgeClientHarness.Token);
        }
    }

    [Test]
    public async Task ALocalBatchIsPublishedOutwardWithItsIdempotencyKeyAndAcknowledged()
    {
        await using var harness = await BridgeClientHarness.StartAsync();

        await harness.WaitForStateAsync(BridgeConnectionState.Streaming);
        BridgeLocalPublicationReceipt receipt =
            await harness.Client.PublishLocalBatchAsync(harness.LocalBatch(1));

        await Assert.That(receipt.Accepted).IsTrue();
        BridgeLocalPublicationResult result =
            await receipt.WaitForResultAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(15));

        await Assert.That(result.Outcome).IsEqualTo(BridgeLocalPublicationOutcome.Published);
        await Assert.That(result.IsDelivered).IsTrue();
        await Assert.That(result.RemoteOutcome).IsEqualTo(LiveAuthoringSessionOutcome.Applied);
        await Assert.That(result.Attempts).IsEqualTo(1);
        Wire.PublishLocalBatchRequest published = harness.Server.Published[0];
        await Assert.That(published.Sequence).IsEqualTo(1L);
        await Assert.That(published.OriginId).IsEqualTo(BridgeTestData.LocalOrigin);
        await Assert.That(published.IdempotencyKey).IsEqualTo(receipt.IdempotencyKey);
        await Assert.That(harness.Client.GetStatus().PublishedBatchCount).IsEqualTo(1L);
    }

    [Test]
    public async Task ADuplicateAcknowledgementCountsAsDeliveredRatherThanFailed()
    {
        var server = new FakeBridgeServer
        {
            AcknowledgementRewrite = static (_, acknowledgement) =>
            {
                acknowledgement.Outcome = Wire.SessionOutcome.Duplicate;
                return acknowledgement;
            }
        };
        await using var harness = await BridgeClientHarness.StartAsync(server);

        BridgeLocalPublicationReceipt receipt =
            await harness.Client.PublishLocalBatchAsync(harness.LocalBatch(1));
        BridgeLocalPublicationResult result =
            await receipt.WaitForResultAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(15));

        await Assert.That(result.Outcome).IsEqualTo(BridgeLocalPublicationOutcome.Duplicate);
        await Assert.That(result.IsDelivered).IsTrue();
        await Assert.That(harness.Client.GetStatus().DuplicateBatchCount).IsEqualTo(1L);
    }

    [Test]
    public async Task AMalformedAcknowledgementIsRejectedAndRestartsTheConnection()
    {
        var server = new FakeBridgeServer
        {
            AcknowledgementRewrite = static (_, acknowledgement) =>
            {
                // An unspecified outcome is not an acknowledgement: the codec refuses it.
                acknowledgement.Outcome = Wire.SessionOutcome.Unspecified;
                return acknowledgement;
            }
        };
        await using var harness = await BridgeClientHarness.StartAsync(server);

        BridgeLocalPublicationResult result = await harness
            .PublishAndWaitAsync(harness.LocalBatch(1));

        await Assert.That(result.Outcome).IsEqualTo(BridgeLocalPublicationOutcome.ProtocolRejected);
        await Assert.That(harness.Client.GetStatus().ProtocolRejectionCount).IsGreaterThan(0L);
        await harness.WaitForAsync(() => harness.Server.StreamCount >= 2);
    }

    [Test]
    public async Task AnAcknowledgementForAnotherSequenceIsRejected()
    {
        var server = new FakeBridgeServer
        {
            AcknowledgementRewrite = static (_, acknowledgement) =>
            {
                acknowledgement.Sequence += 41;
                return acknowledgement;
            }
        };
        await using var harness = await BridgeClientHarness.StartAsync(server);

        BridgeLocalPublicationResult result = await harness
            .PublishAndWaitAsync(harness.LocalBatch(1));

        await Assert.That(result.Outcome).IsEqualTo(BridgeLocalPublicationOutcome.ProtocolRejected);
        await Assert.That(result.Detail).Contains("sequence");
    }

    [Test]
    public async Task AnAcknowledgementThatDropsTheCorrelationIdentifierIsRejected()
    {
        var server = new FakeBridgeServer
        {
            AcknowledgementRewrite = static (_, acknowledgement) =>
            {
                acknowledgement.ClearCorrelationId();
                return acknowledgement;
            }
        };
        await using var harness = await BridgeClientHarness.StartAsync(server);

        BridgeLocalPublicationResult result = await harness
            .PublishAndWaitAsync(harness.LocalBatch(1));

        await Assert.That(result.Outcome).IsEqualTo(BridgeLocalPublicationOutcome.ProtocolRejected);
        await Assert.That(result.Detail).Contains("correlation");
    }

    [Test]
    public async Task AnAcknowledgementFromASessionWithoutAnEpochIsRejected()
    {
        var server = new FakeBridgeServer
        {
            AcknowledgementRewrite = static (_, acknowledgement) =>
            {
                acknowledgement.State = Wire.SessionState.Faulted;
                return acknowledgement;
            }
        };
        await using var harness = await BridgeClientHarness.StartAsync(server);

        BridgeLocalPublicationResult result = await harness
            .PublishAndWaitAsync(harness.LocalBatch(1));

        await Assert.That(result.Outcome).IsEqualTo(BridgeLocalPublicationOutcome.ProtocolRejected);
        await Assert.That(result.Detail).Contains("Faulted");
    }

    [Test]
    public async Task ARemoteRejectionIsReportedAndNeverRetried()
    {
        var server = new FakeBridgeServer
        {
            AcknowledgementRewrite = static (_, acknowledgement) =>
            {
                acknowledgement.Outcome = Wire.SessionOutcome.Rejected;
                acknowledgement.Rejection = Wire.SessionRejection.BridgeScope;
                return acknowledgement;
            }
        };
        await using var harness = await BridgeClientHarness.StartAsync(server);

        BridgeLocalPublicationResult result = await harness
            .PublishAndWaitAsync(harness.LocalBatch(1));

        await Assert.That(result.Outcome).IsEqualTo(BridgeLocalPublicationOutcome.RemoteRejected);
        await Assert.That(result.RemoteRejection)
            .IsEqualTo(LiveAuthoringSessionRejection.BridgeScope);
        await Assert.That(result.Attempts).IsEqualTo(1);
        await Assert.That(harness.Server.PublishAttemptCount).IsEqualTo(1);
    }

    [Test]
    public async Task ARemoteRejectionThatNeedsAResyncRestartsTheConnection()
    {
        var server = new FakeBridgeServer
        {
            AcknowledgementRewrite = static (_, acknowledgement) =>
            {
                acknowledgement.Outcome = Wire.SessionOutcome.Rejected;
                acknowledgement.Rejection = Wire.SessionRejection.ResyncRequired;
                return acknowledgement;
            }
        };
        await using var harness = await BridgeClientHarness.StartAsync(server);
        int streamsBefore = harness.Server.StreamCount;

        BridgeLocalPublicationResult result = await harness
            .PublishAndWaitAsync(harness.LocalBatch(1));

        await Assert.That(result.Outcome).IsEqualTo(BridgeLocalPublicationOutcome.RemoteRejected);
        await harness.WaitForAsync(() => harness.Server.StreamCount > streamsBefore);
        await Assert.That(harness.Server.SnapshotRequestCount).IsGreaterThan(1);
    }

    [Test]
    public async Task AnUnavailablePublishIsRetriedAcrossAReconnectWithTheSameIdempotencyKey()
    {
        var server = new FakeBridgeServer
        {
            PublishFailure = StatusCode.Unavailable,
            PublishFailureCount = 1
        };
        await using var harness = await BridgeClientHarness.StartAsync(server);

        BridgeLocalBatch batch = harness.LocalBatch(1);
        BridgeLocalPublicationResult result = await harness.PublishAndWaitAsync(batch);

        await Assert.That(result.Outcome).IsEqualTo(BridgeLocalPublicationOutcome.Published);
        await Assert.That(result.Attempts).IsEqualTo(2);
        await Assert.That(server.Published.Count).IsEqualTo(1);
        await Assert.That(server.Published[0].IdempotencyKey).IsEqualTo(batch.IdempotencyKey);
        // The failed attempt ended the connection instead of leaving a Streaming client with a lost
        // edit, so the successful attempt ran on a fresh, resynchronized connection.
        await Assert.That(server.StreamCount).IsGreaterThanOrEqualTo(2);
        await Assert.That(server.SnapshotRequestCount).IsGreaterThanOrEqualTo(2);
    }

    [Test]
    public async Task AnExhaustedTransportRetryReportsTheBatchAsUndelivered()
    {
        var server = new FakeBridgeServer
        {
            PublishFailure = StatusCode.Unavailable,
            PublishFailureCount = 10
        };
        await using var harness = await BridgeClientHarness.StartAsync(
            server,
            maxPublishAttempts: 2);

        BridgeLocalPublicationResult result = await harness
            .PublishAndWaitAsync(harness.LocalBatch(1));

        await Assert.That(result.Outcome).IsEqualTo(BridgeLocalPublicationOutcome.TransportFailed);
        await Assert.That(result.Attempts).IsEqualTo(2);
        await Assert.That(server.Published.Count).IsEqualTo(0);
    }

    [Test]
    public async Task ASemanticRejectionIsNeverRepeatedBlindly()
    {
        var server = new FakeBridgeServer
        {
            PublishFailure = StatusCode.InvalidArgument,
            PublishFailureCount = 5
        };
        await using var harness = await BridgeClientHarness.StartAsync(server);

        BridgeLocalPublicationResult result = await harness
            .PublishAndWaitAsync(harness.LocalBatch(1));

        await Assert.That(result.Outcome).IsEqualTo(BridgeLocalPublicationOutcome.RemoteRejected);
        await Assert.That(result.Attempts).IsEqualTo(1);
        await Assert.That(server.PublishFailureCount).IsEqualTo(4);
    }

    [Test]
    public async Task ABatchForARetiredEpochIsCompletedRatherThanReplayed()
    {
        await using var harness = await BridgeClientHarness.StartAsync(
            waitForStreaming: false,
            startRunLoop: false);

        BridgeLocalPublicationReceipt receipt =
            await harness.Client.PublishLocalBatchAsync(harness.LocalBatch(1));
        harness.Server.Epoch = 7;
        harness.Start();
        await harness.WaitForStateAsync(BridgeConnectionState.Streaming);

        BridgeLocalPublicationResult result =
            await receipt.WaitForResultAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(15));

        await Assert.That(result.Outcome).IsEqualTo(BridgeLocalPublicationOutcome.EpochRetired);
        await Assert.That(harness.Server.Published.Count).IsEqualTo(0);
    }

    [Test]
    public async Task PendingBatchesKeepTheirOrderAndKeysAcrossAReconnect()
    {
        var server = new FakeBridgeServer
        {
            PublishFailure = StatusCode.Unavailable,
            PublishFailureCount = 1
        };
        await using var harness = await BridgeClientHarness.StartAsync(server);

        BridgeLocalBatch first = harness.LocalBatch(1);
        BridgeLocalBatch second = harness.LocalBatch(2);
        BridgeLocalBatch third = harness.LocalBatch(3);
        BridgeLocalPublicationReceipt firstReceipt =
            await harness.Client.PublishLocalBatchAsync(first);
        BridgeLocalPublicationReceipt secondReceipt =
            await harness.Client.PublishLocalBatchAsync(second);
        BridgeLocalPublicationReceipt thirdReceipt =
            await harness.Client.PublishLocalBatchAsync(third);

        await firstReceipt.Published.WaitAsync(TimeSpan.FromSeconds(20));
        await secondReceipt.Published.WaitAsync(TimeSpan.FromSeconds(20));
        await thirdReceipt.Published.WaitAsync(TimeSpan.FromSeconds(20));

        // The first batch failed once and was retried after a reconnect. It must still be published
        // before the batches queued behind it: local publications are an ordered sequence.
        long[] expectedOrder = [1L, 2L, 3L];
        await Assert.That(server.Published.Select(request => request.Sequence).ToArray())
            .IsEquivalentTo(expectedOrder);
        await Assert.That(server.Published[0].IdempotencyKey).IsEqualTo(first.IdempotencyKey);
        await Assert.That(server.Published[1].IdempotencyKey).IsEqualTo(second.IdempotencyKey);
        await Assert.That(server.Published[2].IdempotencyKey).IsEqualTo(third.IdempotencyKey);
        await Assert.That(server.StreamCount).IsGreaterThanOrEqualTo(2);
    }

    [Test]
    public async Task OneTotalBoundCoversTheChannelAndTheRetryList()
    {
        var server = new FakeBridgeServer
        {
            PublishFailure = StatusCode.Unavailable,
            PublishFailureCount = 20
        };
        await using var harness = await BridgeClientHarness.StartAsync(
            server,
            outboundCapacity: 2,
            maxPublishAttempts: 10);

        BridgeLocalPublicationReceipt first =
            await harness.Client.PublishLocalBatchAsync(harness.LocalBatch(1));
        BridgeLocalPublicationReceipt second =
            await harness.Client.PublishLocalBatchAsync(harness.LocalBatch(2));

        // Wait until a failed attempt has moved a batch out of the channel and into the retry list;
        // the total retention must not grow because of it.
        await harness.WaitForAsync(() => server.PublishAttemptCount >= 2);
        BridgeLocalPublicationReceipt third =
            await harness.Client.PublishLocalBatchAsync(harness.LocalBatch(3));

        await Assert.That(first.Accepted).IsTrue();
        await Assert.That(second.Accepted).IsTrue();
        await Assert.That(third.Accepted).IsFalse();
        BridgeLocalPublicationResult refused = await third.Published;
        await Assert.That(refused.Outcome).IsEqualTo(BridgeLocalPublicationOutcome.Refused);
        await Assert.That(harness.Client.GetStatus().PendingOutboundBatchCount)
            .IsLessThanOrEqualTo(2);
    }

    [Test]
    public async Task ABoundedOutboundChannelRefusesInsteadOfGrowing()
    {
        await using var harness = await BridgeClientHarness.StartAsync(
            waitForStreaming: false,
            outboundCapacity: 1,
            startRunLoop: false);

        BridgeLocalPublicationReceipt first =
            await harness.Client.PublishLocalBatchAsync(harness.LocalBatch(1));
        BridgeLocalPublicationReceipt second =
            await harness.Client.PublishLocalBatchAsync(harness.LocalBatch(2));

        await Assert.That(first.Accepted).IsTrue();
        await Assert.That(second.Accepted).IsFalse();
        BridgeLocalPublicationResult refused = await second.WaitForResultAsync();
        await Assert.That(refused.Outcome).IsEqualTo(BridgeLocalPublicationOutcome.Refused);
        await Assert.That(harness.Client.GetStatus().RefusedBatchCount).IsEqualTo(1L);
        await Assert.That(harness.Events.Any(
            observed => observed.Kind == BridgeClientEventKind.LocalBatchRefused)).IsTrue();
    }

    [Test]
    public async Task CancellingTheRunLoopStopsTheClient()
    {
        await using var harness = await BridgeClientHarness.StartAsync();

        await harness.WaitForStateAsync(BridgeConnectionState.Streaming);
        await harness.StopAsync();

        await Assert.That(harness.Client.GetStatus().State)
            .IsEqualTo(BridgeConnectionState.Disconnected);
    }
}
