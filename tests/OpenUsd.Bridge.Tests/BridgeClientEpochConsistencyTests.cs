// Copyright (c) marcschier. Licensed under the MIT License.

using Grpc.Core;
using OpenUsd.Bridge.Grpc;
using OpenUsd.Bridge.Protocol;
using OpenUsd.LiveAuthoring;

namespace OpenUsd.Bridge.Tests;

/// <summary>
/// A connection agrees one epoch together with one capability set and one limit set. Every snapshot
/// and delta it carries must belong to exactly that epoch: a newer one renegotiates, an older or
/// foreign one is a protocol violation, and neither is adopted in band under a negotiation that
/// never covered it.
/// </summary>
public sealed class BridgeClientEpochConsistencyTests
{
    [Test]
    public async Task AStreamSnapshotForANewerEpochRenegotiatesInsteadOfBeingApplied()
    {
        await using var harness = await BridgeClientHarness.StartAsync();
        await harness.WaitForStateAsync(BridgeConnectionState.Streaming);
        long appliedBefore = harness.Client.GetStatus().SnapshotAppliedCount;
        int streamsBefore = harness.Server.StreamCount;
        int negotiationsBefore = harness.Server.NegotiateCount;

        harness.Server.Send(BridgeTestData.SnapshotFrame(sequence: 5, epoch: 9));
        await harness.WaitForAsync(() => harness.Server.NegotiateCount > negotiationsBefore);
        await harness.WaitForStateAsync(BridgeConnectionState.Streaming);

        // The advanced-epoch snapshot was never applied; a full handshake ran instead.
        await Assert.That(harness.Server.StreamCount).IsGreaterThan(streamsBefore);
        await Assert.That(harness.Coordinator.GetStatus().Epoch).IsEqualTo(1L);
        await Assert.That(harness.Client.GetStatus().Epoch).IsEqualTo(1L);
        await Assert.That(harness.Client.GetStatus().SnapshotAppliedCount)
            .IsGreaterThan(appliedBefore);
        await Assert.That(harness.Events.Any(observed =>
            (observed.Detail ?? string.Empty).Contains(
                "Renegotiating",
                StringComparison.Ordinal))).IsTrue();
    }

    [Test]
    public async Task AStreamDeltaForANewerEpochRenegotiatesInsteadOfBeingApplied()
    {
        await using var harness = await BridgeClientHarness.StartAsync();
        await harness.WaitForStateAsync(BridgeConnectionState.Streaming);
        long deltasBefore = harness.Client.GetStatus().DeltaAppliedCount;
        int negotiationsBefore = harness.Server.NegotiateCount;

        harness.Server.Send(BridgeTestData.DeltaFrame(sequence: 1, epoch: 4));
        await harness.WaitForAsync(() => harness.Server.NegotiateCount > negotiationsBefore);
        await harness.WaitForStateAsync(BridgeConnectionState.Streaming);

        await Assert.That(harness.Client.GetStatus().DeltaAppliedCount).IsEqualTo(deltasBefore);
        await Assert.That(harness.Coordinator.GetStatus().Epoch).IsEqualTo(1L);
    }

    [Test]
    public async Task AStreamDeltaForARetiredEpochIsAProtocolViolationAndRestarts()
    {
        var server = new FakeBridgeServer(epoch: 3);
        await using var harness = await BridgeClientHarness.StartAsync(server);
        await harness.WaitForStateAsync(BridgeConnectionState.Streaming);
        long rejectionsBefore = harness.Client.GetStatus().ProtocolRejectionCount;
        int negotiationsBefore = server.NegotiateCount;

        harness.Server.Send(BridgeTestData.DeltaFrame(sequence: 1, epoch: 2));
        await harness.WaitForAsync(
            () => harness.Client.GetStatus().ProtocolRejectionCount > rejectionsBefore);
        await harness.WaitForAsync(() => server.NegotiateCount > negotiationsBefore);

        await Assert.That(harness.Client.GetStatus().DeltaAppliedCount).IsEqualTo(0L);
        await Assert.That(harness.Events.Any(observed =>
            observed.Kind == BridgeClientEventKind.ProtocolRejected &&
            (observed.Detail ?? string.Empty).Contains(
                "retired epoch",
                StringComparison.Ordinal))).IsTrue();
    }

    [Test]
    public async Task AStreamFrameForAnotherSessionIsAProtocolViolationAndRestarts()
    {
        await using var harness = await BridgeClientHarness.StartAsync();
        await harness.WaitForStateAsync(BridgeConnectionState.Streaming);
        long rejectionsBefore = harness.Client.GetStatus().ProtocolRejectionCount;
        int negotiationsBefore = harness.Server.NegotiateCount;

        harness.Server.Send(BridgeTestData.DeltaFrame(sequence: 1, epoch: 1, sessionId: "other"));
        await harness.WaitForAsync(
            () => harness.Client.GetStatus().ProtocolRejectionCount > rejectionsBefore);
        await harness.WaitForAsync(() => harness.Server.NegotiateCount > negotiationsBefore);

        await Assert.That(harness.Client.GetStatus().DeltaAppliedCount).IsEqualTo(0L);
        await Assert.That(harness.Events.Any(observed =>
            observed.Kind == BridgeClientEventKind.ProtocolRejected &&
            (observed.Detail ?? string.Empty).Contains(
                "another origin or session",
                StringComparison.Ordinal))).IsTrue();
    }

    [Test]
    public async Task AUnarySnapshotForANewerEpochRenegotiatesInsteadOfBeingApplied()
    {
        var server = new FakeBridgeServer { SnapshotEpochOverride = 12 };
        await using var harness = await BridgeClientHarness.StartAsync(
            server,
            waitForStreaming: false);

        await harness.WaitForAsync(() => server.NegotiateCount >= 2);

        // The resync answer named an epoch this connection never negotiated, so no snapshot was
        // applied and the client is renegotiating rather than adopting it.
        await Assert.That(harness.Client.GetStatus().SnapshotAppliedCount).IsEqualTo(0L);
        await Assert.That(harness.Coordinator.State)
            .IsNotEqualTo(LiveAuthoringSessionState.Synchronized);
        await Assert.That(harness.Events.Any(observed =>
            (observed.Detail ?? string.Empty).Contains(
                "Renegotiating",
                StringComparison.Ordinal))).IsTrue();
    }

    [Test]
    public async Task AUnarySnapshotForAnotherSessionIsAProtocolViolation()
    {
        var server = new FakeBridgeServer { SnapshotSessionIdOverride = "other-session" };
        await using var harness = await BridgeClientHarness.StartAsync(
            server,
            waitForStreaming: false);

        await harness.WaitForAsync(() => harness.Client.GetStatus().ProtocolRejectionCount >= 1);

        await Assert.That(harness.Client.GetStatus().SnapshotAppliedCount).IsEqualTo(0L);
        await Assert.That(harness.Events.Any(observed =>
            observed.Kind == BridgeClientEventKind.ProtocolRejected &&
            (observed.Detail ?? string.Empty).Contains(
                "another origin or session",
                StringComparison.Ordinal))).IsTrue();
    }

    [Test]
    public async Task AResyncDemandForANewerEpochRenegotiatesInsteadOfResyncingInBand()
    {
        await using var harness = await BridgeClientHarness.StartAsync();
        await harness.WaitForStateAsync(BridgeConnectionState.Streaming);
        int snapshotsBefore = harness.Server.SnapshotRequestCount;
        int negotiationsBefore = harness.Server.NegotiateCount;

        harness.Server.Send(BridgeTestData.ResyncFrame(BridgeResyncReason.EpochChanged, epoch: 6));
        await harness.WaitForAsync(() => harness.Server.NegotiateCount > negotiationsBefore);

        // A resync taken under the old negotiation would have been wrong: the epoch, the
        // capabilities, and the limits are agreed together or not at all.
        await Assert.That(harness.Server.SnapshotRequestCount)
            .IsGreaterThanOrEqualTo(snapshotsBefore);
        await Assert.That(harness.Client.GetStatus().Epoch).IsEqualTo(1L);
    }

    [Test]
    public async Task StatusNeverReportsAnEpochTheConnectionDidNotNegotiate()
    {
        await using var harness = await BridgeClientHarness.StartAsync();
        await harness.WaitForStateAsync(BridgeConnectionState.Streaming);

        harness.Server.Send(BridgeTestData.SnapshotFrame(sequence: 3, epoch: 8));
        harness.Server.Send(BridgeTestData.DeltaFrame(sequence: 1, epoch: 8));
        await Task.Delay(150);

        BridgeClientStatus status = harness.Client.GetStatus();
        await Assert.That(status.Epoch is 0L or 1L).IsTrue();
        await Assert.That(status.SessionId is null or BridgeTestData.SessionId).IsTrue();
        await Assert.That(harness.Coordinator.GetStatus().Epoch).IsEqualTo(1L);
    }

    [Test]
    public async Task ARenegotiatedEpochLeavesAnAttemptedBatchIndeterminate()
    {
        var server = new FakeBridgeServer();
        await using var harness = await BridgeClientHarness.StartAsync(server);
        await harness.WaitForStateAsync(BridgeConnectionState.Streaming);

        // Queue a batch for the current epoch, let one attempt leave the client and lose its
        // answer, then make the peer come back on a newer epoch. The batch's sequence means nothing
        // in the new epoch, so it must not be replayed -- but the attempt that already left says
        // nothing about whether the peer applied it, so the answer cannot be a definitive
        // EpochRetired either. The epoch moves inside the publish callback, so the ordering is
        // exact rather than raced against the reconnect backoff.
        server.PublishFailure = StatusCode.Unavailable;
        server.PublishFailureCount = 1;
        server.OnPublish = _ => server.Epoch = 5;
        BridgeLocalPublicationReceipt receipt =
            await harness.Client.PublishLocalBatchAsync(harness.LocalBatch(1));

        BridgeLocalPublicationResult result =
            await receipt.Published.WaitAsync(TimeSpan.FromSeconds(20));

        await Assert.That(result.Outcome)
            .IsEqualTo(BridgeLocalPublicationOutcome.Indeterminate);
        await Assert.That(result.IsIndeterminate).IsTrue();
        await Assert.That(result.Attempts).IsGreaterThanOrEqualTo(1);
        await Assert.That(result.Detail!).Contains("retired epoch");
        await Assert.That(server.Published.Count).IsEqualTo(0);
        await harness.WaitForAsync(() => harness.Client.GetStatus().Epoch == 5);
        await Assert.That(harness.Coordinator.GetStatus().Epoch).IsEqualTo(5L);
    }

    [Test]
    public async Task ACoordinatorEpochRejectionRenegotiatesRatherThanResyncingInBand()
    {
        await using var harness = await BridgeClientHarness.StartAsync();
        await harness.WaitForStateAsync(BridgeConnectionState.Streaming);
        int negotiationsBefore = harness.Server.NegotiateCount;

        // Move the coordinator to a newer epoch behind the client's back, with a baseline of its
        // own. The next in-epoch delta passes the client's own guard but the coordinator refuses it
        // as retired, and the client must renegotiate rather than take a snapshot under a
        // negotiation that is out of date.
        await harness.Coordinator.ConnectAsync(BridgeTestData.Epoch(4));
        await harness.Coordinator.ApplySnapshotAsync(BridgeTestData.Snapshot(sequence: 0, epoch: 4));
        harness.Server.Send(BridgeTestData.DeltaFrame(sequence: 1, epoch: 1));

        await harness.WaitForAsync(() => harness.Server.NegotiateCount > negotiationsBefore);

        await Assert.That(harness.Client.GetStatus().DeltaAppliedCount).IsEqualTo(0L);
        // The peer can only offer epoch 1, which the coordinator has left behind. That is refused
        // as a protocol violation and retried under backoff; the run loop keeps running.
        await harness.WaitForAsync(() => harness.Client.GetStatus().ProtocolRejectionCount > 0);
        await Assert.That(harness.Run.IsFaulted).IsFalse();
        await Assert.That(harness.Client.State).IsNotEqualTo(BridgeConnectionState.Faulted);
    }
}
