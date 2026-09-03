// Copyright (c) marcschier. Licensed under the MIT License.

using Grpc.Core;
using OpenUsd.Bridge.Grpc;
using OpenUsd.Bridge.Protocol;

namespace OpenUsd.Bridge.Tests;

/// <summary>
/// A publication that has already been sent is never answered with an outcome that asserts the peer
/// did not see it.
/// </summary>
/// <remarks>
/// <para>
/// <c>NotPermitted</c>, <c>EpochRetired</c>, and <c>Cancelled</c> are claims about the peer: they
/// say the batch never reached it. That is knowable while the batch is still queued and stops being
/// knowable the moment one request leaves the client, because the request may have been applied and
/// only the answer lost — which is exactly why every attempt carries the same idempotency key.
/// </para>
/// <para>
/// Each test here drives one reason a sent batch can no longer be retried or acknowledged — tighter
/// limits, a lost export capability, a retired epoch, a stopped loop, disposal — and requires
/// <c>Indeterminate</c> rather than a definitive refusal a host would wrongly trust. The
/// counterpart tests require the definitive outcome to survive when nothing was ever sent.
/// </para>
/// </remarks>
public sealed class BridgeIndeterminateOutcomeTests
{
    /// <summary>
    /// A batch that was sent once and comes back to a session whose negotiated bounds no longer
    /// allow it cannot be published — and cannot be claimed never to have arrived either.
    /// </summary>
    [Test]
    public async Task ASentBatchRefusedByTighterLimitsOnTheNextSessionIsIndeterminate()
    {
        var server = new FakeBridgeServer
        {
            PublishFailure = StatusCode.Unavailable,
            PublishFailureCount = 1
        };
        await using var harness = await BridgeClientHarness.StartAsync(server);

        // The limits tighten exactly when the attempt reaches the peer, so the reconnect that
        // follows the lost answer negotiates bounds this batch no longer fits.
        server.OnPublish = _ =>
            server.Limits = BridgeLimits.Local with { MaxUpdatesPerMessage = 1 };

        BridgeLocalPublicationResult result =
            await harness.PublishAndWaitAsync(harness.OversizedLocalBatch(1, updateCount: 8));

        await Assert.That(result.Outcome).IsEqualTo(BridgeLocalPublicationOutcome.Indeterminate);
        await Assert.That(result.IsIndeterminate).IsTrue();
        await Assert.That(result.IsDelivered).IsFalse();
        await Assert.That(result.Attempts).IsEqualTo(1);
        await Assert.That(result.Detail!).Contains("negotiated bounds");
        await Assert.That(result.Detail!).Contains("unknown");
        await Assert.That(server.Published.Count).IsEqualTo(0);
    }

    /// <summary>
    /// The same batch, refused by the same bounds but never sent, keeps the definitive answer: here
    /// the client really does know the peer never saw it.
    /// </summary>
    [Test]
    public async Task ABatchRefusedByTighterLimitsBeforeAnyAttemptStaysNotPermitted()
    {
        var server = new FakeBridgeServer
        {
            Limits = BridgeLimits.Local with { MaxUpdatesPerMessage = 2 }
        };
        await using var harness = await BridgeClientHarness.StartAsync(
            server,
            waitForStreaming: false,
            startRunLoop: false);

        BridgeLocalPublicationReceipt receipt = await harness.Client
            .PublishLocalBatchAsync(harness.OversizedLocalBatch(1, updateCount: 8));
        harness.Start();
        BridgeLocalPublicationResult result =
            await receipt.Published.WaitAsync(TimeSpan.FromSeconds(20));

        await Assert.That(result.Outcome).IsEqualTo(BridgeLocalPublicationOutcome.NotPermitted);
        await Assert.That(result.IsIndeterminate).IsFalse();
        await Assert.That(result.Attempts).IsEqualTo(0);
    }

    /// <summary>
    /// A session that comes back without the export capability cannot carry a batch it already
    /// carried once, and must not pretend the earlier attempt never happened.
    /// </summary>
    [Test]
    public async Task ASentBatchOnASessionThatLosesExportIsIndeterminate()
    {
        var server = new FakeBridgeServer
        {
            PublishFailure = StatusCode.Unavailable,
            PublishFailureCount = 1
        };
        await using var harness = await BridgeClientHarness.StartAsync(server);

        server.OnPublish = _ => server.Capabilities =
        [
            BridgeCapability.FullSnapshot,
            BridgeCapability.OrderedDelta,
            BridgeCapability.HealthStatus
        ];

        BridgeLocalPublicationResult result =
            await harness.PublishAndWaitAsync(harness.LocalBatch(1));

        await Assert.That(result.Outcome).IsEqualTo(BridgeLocalPublicationOutcome.Indeterminate);
        await Assert.That(result.Attempts).IsEqualTo(1);
        await Assert.That(result.Detail!).Contains("local-edit-export");
        await Assert.That(result.Detail!).Contains("unknown");
        await Assert.That(server.Published.Count).IsEqualTo(0);
    }

    /// <summary>
    /// A never-sent batch on a session without export keeps the definitive refusal, so a host can
    /// still tell "this session cannot carry it" from "I do not know what happened to it".
    /// </summary>
    [Test]
    public async Task AnUnsentBatchOnASessionWithoutExportStaysNotPermitted()
    {
        var server = new FakeBridgeServer
        {
            Capabilities =
            [
                BridgeCapability.FullSnapshot,
                BridgeCapability.OrderedDelta,
                BridgeCapability.HealthStatus
            ]
        };
        await using var harness = await BridgeClientHarness.StartAsync(server);

        BridgeLocalPublicationResult result =
            await harness.PublishAndWaitAsync(harness.LocalBatch(1));

        await Assert.That(result.Outcome).IsEqualTo(BridgeLocalPublicationOutcome.NotPermitted);
        await Assert.That(result.IsIndeterminate).IsFalse();
        await Assert.That(result.Attempts).IsEqualTo(0);
    }

    /// <summary>
    /// A batch held for retry on a peer that never comes back is answered by the stop drain, and
    /// the attempt it already made is what makes that answer indeterminate rather than cancelled.
    /// </summary>
    [Test]
    public async Task ASentBatchDrainedByAStoppedLoopIsIndeterminate()
    {
        var server = new FakeBridgeServer
        {
            PublishFailure = StatusCode.Unavailable,
            PublishFailureCount = 1
        };
        await using var harness = await BridgeClientHarness.StartAsync(
            server,
            maxPublishAttempts: 10);

        // After the attempt is lost, the peer refuses every later handshake, so the batch stays on
        // the retry list with exactly one attempt behind it until the loop stops.
        server.OnPublish = _ => server.NegotiateFailure = StatusCode.Unavailable;

        BridgeLocalPublicationReceipt receipt =
            await harness.Client.PublishLocalBatchAsync(harness.LocalBatch(1));
        await harness.WaitForAsync(() => server.PublishAttemptCount >= 1);
        await harness.WaitForAsync(() => server.NegotiateCount >= 2);
        await harness.StopAsync();

        BridgeLocalPublicationResult result =
            await receipt.Published.WaitAsync(TimeSpan.FromSeconds(20));

        await Assert.That(result.Outcome).IsEqualTo(BridgeLocalPublicationOutcome.Indeterminate);
        await Assert.That(result.Attempts).IsEqualTo(1);
        await Assert.That(result.Detail!).Contains("Cancelled");
        await Assert.That(harness.Client.GetStatus().PendingOutboundBatchCount).IsEqualTo(0);
    }

    /// <summary>The disposal drain follows the same rule as the stop drain.</summary>
    [Test]
    public async Task ASentBatchDrainedByDisposalIsIndeterminate()
    {
        var server = new FakeBridgeServer
        {
            PublishFailure = StatusCode.Unavailable,
            PublishFailureCount = 1
        };
        var harness = await BridgeClientHarness.StartAsync(server, maxPublishAttempts: 10);
        server.OnPublish = _ => server.NegotiateFailure = StatusCode.Unavailable;

        BridgeLocalPublicationReceipt receipt =
            await harness.Client.PublishLocalBatchAsync(harness.LocalBatch(1));
        await harness.WaitForAsync(() => server.PublishAttemptCount >= 1);
        await harness.WaitForAsync(() => server.NegotiateCount >= 2);
        await harness.Client.DisposeAsync();

        BridgeLocalPublicationResult result =
            await receipt.Published.WaitAsync(TimeSpan.FromSeconds(20));

        await Assert.That(result.Outcome).IsEqualTo(BridgeLocalPublicationOutcome.Indeterminate);
        await Assert.That(result.Attempts).IsEqualTo(1);
        await Assert.That(result.Detail!).Contains("unknown");
        await harness.DisposeAsync();
    }

    /// <summary>
    /// A batch that never left the client is still cancelled by disposal, because there the client
    /// genuinely knows the peer never saw it.
    /// </summary>
    [Test]
    public async Task AnUnsentBatchDrainedByDisposalStaysCancelled()
    {
        var harness = await BridgeClientHarness.StartAsync(
            waitForStreaming: false,
            startRunLoop: false);

        BridgeLocalPublicationReceipt receipt =
            await harness.Client.PublishLocalBatchAsync(harness.LocalBatch(1));
        await harness.Client.DisposeAsync();

        BridgeLocalPublicationResult result =
            await receipt.Published.WaitAsync(TimeSpan.FromSeconds(20));

        await Assert.That(result.Outcome).IsEqualTo(BridgeLocalPublicationOutcome.Cancelled);
        await Assert.That(result.IsIndeterminate).IsFalse();
        await Assert.That(result.Attempts).IsEqualTo(0);
        await harness.DisposeAsync();
    }

    /// <summary>
    /// An exhausted bounded retry keeps reporting <c>TransportFailed</c>: it is already the honest
    /// answer for a lost response, and both it and <c>Indeterminate</c> tell a host the same thing.
    /// </summary>
    [Test]
    public async Task AnExhaustedRetryStaysTransportFailedAndReadsAsIndeterminate()
    {
        var server = new FakeBridgeServer
        {
            PublishFailure = StatusCode.Unavailable,
            PublishFailureCount = 10
        };
        await using var harness = await BridgeClientHarness.StartAsync(
            server,
            maxPublishAttempts: 2);

        BridgeLocalPublicationResult result =
            await harness.PublishAndWaitAsync(harness.LocalBatch(1));

        await Assert.That(result.Outcome).IsEqualTo(BridgeLocalPublicationOutcome.TransportFailed);
        await Assert.That(result.IsIndeterminate).IsTrue();
        await Assert.That(result.Attempts).IsEqualTo(2);
    }
}
