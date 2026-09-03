// Copyright (c) marcschier. Licensed under the MIT License.

using OpenUsd.Bridge.Grpc;
using OpenUsd.Bridge.Protocol;

namespace OpenUsd.Bridge.Tests;

/// <summary>
/// Admission and the drains that end a queue's life are exercised against each other at an exactly
/// controlled interleaving, not by hoping a timer lands in the window.
/// </summary>
/// <remarks>
/// <para>
/// The window is real: a publisher authorizes its batch against whatever session exists at the
/// moment it calls, and the queue it then writes to may be drained for the last time in between —
/// by a pump for a session that cannot export, or by a loop that has faulted. A receipt that lands
/// in a queue nothing will read again is worse than a refusal: the host is told the batch was
/// accepted and then waits forever for an answer that no code path can produce.
/// </para>
/// <para>
/// The interleaving is driven by <c>AdmissionBarrier</c>, a test-only pause between the
/// authorization and the reservation it has to stay consistent with. Each test parks a publisher
/// there, drives the client to the exact state it is racing, and only then lets the publisher
/// continue.
/// </para>
/// </remarks>
public sealed class BridgeAdmissionRaceTests
{
    /// <summary>
    /// A publisher authorized before negotiation, released only after a no-export pump has drained,
    /// is answered rather than left holding a receipt on a session that can never carry it.
    /// </summary>
    [Test]
    public async Task ABatchAuthorizedBeforeNegotiationIsAnsweredAfterTheNoExportPumpDrains()
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
        await using var harness = await BridgeClientHarness.StartAsync(
            server,
            waitForStreaming: false,
            startRunLoop: false);

        // Queued before any loop exists, so the pump's first drain is what finds it.
        BridgeLocalPublicationReceipt drained =
            await harness.Client.PublishLocalBatchAsync(harness.LocalBatch(1));
        await Assert.That(drained.Accepted).IsTrue();

        using var barrier = new AdmissionBarrier(harness.Client);
        Task<BridgeLocalPublicationReceipt> racing = Task.Run(async () =>
            await harness.Client.PublishLocalBatchAsync(harness.LocalBatch(2)));
        await barrier.WaitUntilReachedAsync();

        // The session is negotiated and the pump has drained everything it could see: the batch it
        // found is answered, which is how the test knows the drain has already happened.
        harness.Start();
        BridgeLocalPublicationResult drainedResult =
            await drained.Published.WaitAsync(TimeSpan.FromSeconds(30));
        await Assert.That(drainedResult.Outcome)
            .IsEqualTo(BridgeLocalPublicationOutcome.NotPermitted);
        await harness.WaitForStateAsync(BridgeConnectionState.Streaming);

        // Only now does the parked publisher reach the queue, strictly after that drain.
        barrier.Release();
        BridgeLocalPublicationReceipt receipt = await racing.WaitAsync(TimeSpan.FromSeconds(30));
        BridgeLocalPublicationResult result =
            await receipt.Published.WaitAsync(TimeSpan.FromSeconds(30));

        await Assert.That(result.Outcome).IsEqualTo(BridgeLocalPublicationOutcome.NotPermitted);
        await Assert.That(result.Detail!).Contains("local-edit-export");
        await Assert.That(harness.Server.Published.Count).IsEqualTo(0);

        // And the connection is still alive: refusing an admission is not a connection failure.
        await Assert.That(harness.Client.State).IsEqualTo(BridgeConnectionState.Streaming);
        await Assert.That(harness.Run.IsFaulted).IsFalse();
    }

    /// <summary>
    /// A no-export session keeps answering admissions for as long as it lasts, rather than draining
    /// once and leaving everything queued afterwards on a pump that already returned.
    /// </summary>
    [Test]
    public async Task ANoExportSessionKeepsAnsweringLaterAdmissions()
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

        for (long sequence = 1; sequence <= 4; sequence++)
        {
            BridgeLocalPublicationReceipt receipt =
                await harness.Client.PublishLocalBatchAsync(harness.LocalBatch(sequence));
            BridgeLocalPublicationResult result =
                await receipt.Published.WaitAsync(TimeSpan.FromSeconds(30));

            await Assert.That(result.Outcome)
                .IsEqualTo(BridgeLocalPublicationOutcome.NotPermitted);
        }

        // Nothing is retained: the receipts were all answered, so the shared retention bound is
        // back where it started and the session is still usable for inbound traffic.
        await Assert.That(harness.Client.GetStatus().PendingOutboundBatchCount).IsEqualTo(0);
        await Assert.That(harness.Server.Published.Count).IsEqualTo(0);
        await Assert.That(harness.Client.State).IsEqualTo(BridgeConnectionState.Streaming);
    }

    /// <summary>
    /// A batch queued before a fatal fault is drained by it, and a batch that reaches the queue
    /// after that drain is refused instead of accepted into a queue nothing will read again.
    /// </summary>
    [Test]
    public async Task ABatchAdmittedAroundAFatalDrainIsNeverLeftPending()
    {
        var server = new FakeBridgeServer
        {
            // Unauthenticated is fatal: the loop stops rather than retrying.
            NegotiateFailure = global::Grpc.Core.StatusCode.Unauthenticated
        };
        var harness = await BridgeClientHarness.StartAsync(
            server,
            waitForStreaming: false,
            startRunLoop: false);

        BridgeLocalPublicationReceipt drained =
            await harness.Client.PublishLocalBatchAsync(harness.LocalBatch(1));
        await Assert.That(drained.Accepted).IsTrue();

        using var barrier = new AdmissionBarrier(harness.Client);
        Task<BridgeLocalPublicationReceipt> racing = Task.Run(async () =>
            await harness.Client.PublishLocalBatchAsync(harness.LocalBatch(2)));
        await barrier.WaitUntilReachedAsync();

        harness.Start();
        await harness.Run.WaitAsync(TimeSpan.FromSeconds(30));

        // The loop ended for a reason a retry cannot fix and drained what it was holding.
        await Assert.That(harness.Client.State).IsEqualTo(BridgeConnectionState.Faulted);
        BridgeLocalPublicationResult drainedResult =
            await drained.Published.WaitAsync(TimeSpan.FromSeconds(30));
        await Assert.That(drainedResult.Outcome)
            .IsEqualTo(BridgeLocalPublicationOutcome.NotPermitted);

        // The parked publisher arrives strictly after the terminal drain.
        barrier.Release();
        BridgeLocalPublicationReceipt receipt = await racing.WaitAsync(TimeSpan.FromSeconds(30));
        BridgeLocalPublicationResult result =
            await receipt.Published.WaitAsync(TimeSpan.FromSeconds(30));

        await Assert.That(receipt.Accepted).IsFalse();
        await Assert.That(result.Outcome).IsEqualTo(BridgeLocalPublicationOutcome.NotPermitted);
        await Assert.That(result.Detail!).Contains("faulted");

        // Every later publication is answered the same way rather than queued behind a dead loop.
        BridgeLocalPublicationReceipt after =
            await harness.Client.PublishLocalBatchAsync(harness.LocalBatch(3));
        await Assert.That(after.Accepted).IsFalse();
        await Assert.That((await after.Published.WaitAsync(TimeSpan.FromSeconds(30))).Outcome)
            .IsEqualTo(BridgeLocalPublicationOutcome.NotPermitted);
        await Assert.That(harness.Client.GetStatus().PendingOutboundBatchCount).IsEqualTo(0);

        await harness.DisposeAsync();
    }

    /// <summary>
    /// The same guarantee holds for an ordinary stop: once the loop has drained for the last time,
    /// admission refuses rather than accepting a batch nothing will publish.
    /// </summary>
    [Test]
    public async Task ABatchAdmittedAfterTheLoopStoppedIsRefusedRatherThanHeld()
    {
        var harness = await BridgeClientHarness.StartAsync();
        await harness.StopAsync();

        BridgeLocalPublicationReceipt receipt =
            await harness.Client.PublishLocalBatchAsync(harness.LocalBatch(1));
        BridgeLocalPublicationResult result =
            await receipt.Published.WaitAsync(TimeSpan.FromSeconds(30));

        await Assert.That(receipt.Accepted).IsFalse();
        await Assert.That(result.Outcome).IsEqualTo(BridgeLocalPublicationOutcome.Cancelled);
        await Assert.That(harness.Client.GetStatus().PendingOutboundBatchCount).IsEqualTo(0);

        await harness.DisposeAsync();
    }

    /// <summary>
    /// A publisher parked mid-admission while the client is disposed is answered by the disposal
    /// drain, not left waiting on a queue the disposal already emptied.
    /// </summary>
    [Test]
    public async Task ABatchAdmittedAroundDisposalIsAnsweredByTheDisposalDrain()
    {
        var harness = await BridgeClientHarness.StartAsync(
            waitForStreaming: false,
            startRunLoop: false);

        using var barrier = new AdmissionBarrier(harness.Client);
        Task<BridgeLocalPublicationReceipt> racing = Task.Run(async () =>
            await harness.Client.PublishLocalBatchAsync(harness.LocalBatch(1)));
        await barrier.WaitUntilReachedAsync();

        await harness.Client.DisposeAsync();
        barrier.Release();

        BridgeLocalPublicationReceipt receipt = await racing.WaitAsync(TimeSpan.FromSeconds(30));
        BridgeLocalPublicationResult result =
            await receipt.Published.WaitAsync(TimeSpan.FromSeconds(30));

        await Assert.That(receipt.Accepted).IsFalse();
        await Assert.That(result.Outcome).IsEqualTo(BridgeLocalPublicationOutcome.Cancelled);
        await Assert.That(harness.Client.GetStatus().PendingOutboundBatchCount).IsEqualTo(0);

        await harness.DisposeAsync();
    }

    /// <summary>
    /// A publisher parked mid-admission while disposal is still in flight — the channel already
    /// completed, the queue still at capacity, the drain not yet run — is answered <c>Cancelled</c>,
    /// never <c>Refused</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the exact window the ordering inside <c>DisposeAsync</c> exists for. If the terminal
    /// answer were installed only by the drain at the end, an admission landing here would find no
    /// terminal state, a completed channel, and a retention counter still at its bound, and would
    /// answer <c>Refused</c> — which reads as backpressure and invites the caller to retry against a
    /// client that is already gone.
    /// </para>
    /// <para>
    /// The interleaving is exact rather than timed: the loop is parked inside a transport factory
    /// the test controls, so disposal is provably still waiting on it when the publisher is
    /// released.
    /// </para>
    /// </remarks>
    [Test]
    public async Task ABatchAdmittedWhileDisposalIsInFlightIsCancelledRatherThanRefused()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var harness = await BridgeClientHarness.StartAsync(
            waitForStreaming: false,
            outboundCapacity: 1,
            startRunLoop: false,
            connectionFactory: async _ =>
            {
                entered.TrySetResult();
                await gate.Task.ConfigureAwait(false);
                throw new OperationCanceledException();
            });

        // The single retention slot is taken, so backpressure is what admission would fall back to
        // if the terminal answer were not already installed.
        BridgeLocalPublicationReceipt queued =
            await harness.Client.PublishLocalBatchAsync(harness.LocalBatch(1));
        await Assert.That(queued.Accepted).IsTrue();
        await Assert.That(harness.Client.GetStatus().PendingOutboundBatchCount).IsEqualTo(1);

        harness.Start();
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(30));
        await harness.WaitForStateAsync(BridgeConnectionState.Connecting);

        using var barrier = new AdmissionBarrier(harness.Client);
        Task<BridgeLocalPublicationReceipt> racing = Task.Run(async () =>
            await harness.Client.PublishLocalBatchAsync(harness.LocalBatch(2)));
        await barrier.WaitUntilReachedAsync();

        Task disposal = Task.Run(async () => await harness.Client.DisposeAsync());
        await Task.Delay(250);

        // Disposal is past its terminal install and its channel completion, and is now waiting on
        // the parked loop. The publisher is released strictly inside that window.
        await Assert.That(disposal.IsCompleted).IsFalse();
        barrier.Release();

        BridgeLocalPublicationReceipt receipt = await racing.WaitAsync(TimeSpan.FromSeconds(30));
        BridgeLocalPublicationResult result =
            await receipt.Published.WaitAsync(TimeSpan.FromSeconds(30));

        await Assert.That(receipt.Accepted).IsFalse();
        await Assert.That(result.Outcome).IsEqualTo(BridgeLocalPublicationOutcome.Cancelled);
        await Assert.That(result.Outcome).IsNotEqualTo(BridgeLocalPublicationOutcome.Refused);

        // Releasing the loop lets disposal finish and drain what it was holding.
        gate.TrySetResult();
        await disposal.WaitAsync(TimeSpan.FromSeconds(30));
        BridgeLocalPublicationResult drained =
            await queued.Published.WaitAsync(TimeSpan.FromSeconds(30));
        await Assert.That(drained.Outcome).IsEqualTo(BridgeLocalPublicationOutcome.Cancelled);
        await Assert.That(drained.Attempts).IsEqualTo(0);
        await Assert.That(harness.Client.GetStatus().PendingOutboundBatchCount).IsEqualTo(0);

        await harness.DisposeAsync();
    }

    /// <summary>
    /// Parks the first publisher that reaches admission and releases it on the test's command.
    /// </summary>
    private sealed class AdmissionBarrier : IDisposable
    {
        private readonly OmniverseBridgeClient _client;
        private readonly TaskCompletionSource _reached =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly ManualResetEventSlim _release = new(initialState: false);

        internal AdmissionBarrier(OmniverseBridgeClient client)
        {
            _client = client;
            client.AdmissionBarrier = () =>
            {
                if (_reached.TrySetResult())
                {
                    // Only the first arrival waits. A re-authorization pass after the session
                    // changed must not park again, or the test would deadlock on its own barrier.
                    _release.Wait(TimeSpan.FromSeconds(60));
                }
            };
        }

        internal async Task WaitUntilReachedAsync() =>
            await _reached.Task.WaitAsync(TimeSpan.FromSeconds(30)).ConfigureAwait(false);

        internal void Release() => _release.Set();

        public void Dispose()
        {
            _client.AdmissionBarrier = null;
            _release.Set();
            _release.Dispose();
        }
    }
}
