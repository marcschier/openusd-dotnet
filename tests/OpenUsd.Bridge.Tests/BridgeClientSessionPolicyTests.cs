// Copyright (c) marcschier. Licensed under the MIT License.

using OpenUsd.Bridge.Grpc;
using OpenUsd.Bridge.Protocol;
using OpenUsd.LiveAuthoring;

namespace OpenUsd.Bridge.Tests;

/// <summary>
/// What the session negotiated is what the session may do. A capability the peers did not both
/// advertise is never used and never accepted, and a message past the negotiated bounds is refused
/// even when it would fit inside the local ones.
/// </summary>
public sealed class BridgeClientSessionPolicyTests
{
    [Test]
    public async Task NegotiatedCapabilitiesAndLimitsAreRecordedOnStatus()
    {
        var server = new FakeBridgeServer
        {
            Limits = BridgeLimits.Local with { MaxUpdatesPerMessage = 4 }
        };
        await using var harness = await BridgeClientHarness.StartAsync(server);

        BridgeClientStatus status = harness.Client.GetStatus();

        await Assert.That(status.Negotiated).IsTrue();
        await Assert.That(status.EffectiveLimits.MaxUpdatesPerMessage).IsEqualTo(4);
        await Assert.That(status.Supports(BridgeCapability.LocalEditExport)).IsTrue();
        await Assert.That(status.NegotiatedCapabilities.Count)
            .IsEqualTo(BridgeProtocol.SupportedCapabilities.Count);
    }

    [Test]
    public async Task LocalExportIsRefusedWhenTheSessionDidNotAgreeTheCapability()
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

        BridgeLocalPublicationReceipt receipt =
            await harness.Client.PublishLocalBatchAsync(harness.LocalBatch(1));
        BridgeLocalPublicationResult result =
            await receipt.Published.WaitAsync(TimeSpan.FromSeconds(15));

        await Assert.That(receipt.Accepted).IsFalse();
        await Assert.That(result.Outcome).IsEqualTo(BridgeLocalPublicationOutcome.NotPermitted);
        await Assert.That(result.Detail).Contains("local-edit-export");
        await Assert.That(harness.Server.Published.Count).IsEqualTo(0);
    }

    [Test]
    public async Task AQueuedBatchIsCompletedWhenTheSessionCannotExportAtAll()
    {
        var server = new FakeBridgeServer
        {
            Capabilities = [BridgeCapability.FullSnapshot, BridgeCapability.OrderedDelta]
        };
        await using var harness = await BridgeClientHarness.StartAsync(
            server,
            waitForStreaming: false,
            startRunLoop: false);

        // Queued before negotiation, so the queue cannot know yet; the pump refuses it once the
        // session exists rather than holding it forever.
        BridgeLocalPublicationReceipt receipt =
            await harness.Client.PublishLocalBatchAsync(harness.LocalBatch(1));
        harness.Start();
        BridgeLocalPublicationResult result =
            await receipt.Published.WaitAsync(TimeSpan.FromSeconds(15));

        await Assert.That(receipt.Accepted).IsTrue();
        await Assert.That(result.Outcome).IsEqualTo(BridgeLocalPublicationOutcome.NotPermitted);
        await Assert.That(harness.Server.Published.Count).IsEqualTo(0);
    }

    [Test]
    public async Task AnUpdateNeedingAnUnagreedCapabilityIsRefusedBeforeItIsSent()
    {
        var server = new FakeBridgeServer
        {
            Capabilities =
            [
                BridgeCapability.FullSnapshot,
                BridgeCapability.OrderedDelta,
                BridgeCapability.LocalEditExport
            ]
        };
        await using var harness = await BridgeClientHarness.StartAsync(server);

        BridgeLocalPublicationReceipt receipt = await harness.Client
            .PublishLocalBatchAsync(harness.CapabilityBoundLocalBatch(1));
        BridgeLocalPublicationResult result =
            await receipt.Published.WaitAsync(TimeSpan.FromSeconds(15));

        await Assert.That(result.Outcome).IsEqualTo(BridgeLocalPublicationOutcome.NotPermitted);
        await Assert.That(result.Detail).Contains(nameof(BridgeCapability.ApiSchema));
        await Assert.That(harness.Server.Published.Count).IsEqualTo(0);
    }

    [Test]
    public async Task ABatchPastTheNegotiatedBoundIsRefusedEvenThoughItFitsLocally()
    {
        var server = new FakeBridgeServer
        {
            Limits = BridgeLimits.Local with { MaxUpdatesPerMessage = 2 }
        };
        await using var harness = await BridgeClientHarness.StartAsync(server);

        BridgeLocalPublicationReceipt receipt = await harness.Client
            .PublishLocalBatchAsync(harness.OversizedLocalBatch(1, updateCount: 8));
        BridgeLocalPublicationResult result =
            await receipt.Published.WaitAsync(TimeSpan.FromSeconds(15));

        await Assert.That(result.Outcome).IsEqualTo(BridgeLocalPublicationOutcome.NotPermitted);
        await Assert.That(result.Detail).Contains("negotiated bounds");
        await Assert.That(harness.Server.Published.Count).IsEqualTo(0);
        // The local bound would have accepted it, which is exactly the point of the check.
        await Assert.That(BridgeLimits.Local.Allows(8, 0)).IsTrue();
    }

    [Test]
    public async Task AnInboundDeltaUsingAnUnagreedCapabilityIsRejectedAndNotApplied()
    {
        var server = new FakeBridgeServer
        {
            Capabilities =
            [
                BridgeCapability.FullSnapshot,
                BridgeCapability.OrderedDelta,
                BridgeCapability.LocalEditExport
            ]
        };
        await using var harness = await BridgeClientHarness.StartAsync(server);

        long appliedBefore = harness.Client.GetStatus().DeltaAppliedCount;
        harness.Server.Send(BridgeTestData.CapabilityBoundDeltaFrame(1));
        await harness.WaitForAsync(() => harness.Client.GetStatus().ProtocolRejectionCount >= 1);

        BridgeClientStatus status = harness.Client.GetStatus();
        await Assert.That(status.DeltaAppliedCount).IsEqualTo(appliedBefore);
        await Assert.That(status.LastFailureDetail).Contains(nameof(BridgeCapability.ApiSchema));
    }

    [Test]
    public async Task AnInboundSnapshotPastTheNegotiatedBoundIsRejected()
    {
        var server = new FakeBridgeServer
        {
            Limits = BridgeLimits.Local with { MaxUpdatesPerMessage = 1 },
            SnapshotExtraUpdates =
            [
                new SetActiveUpdate($"{BridgeTestData.BridgeRoot}/Cube", true),
                new SetActiveUpdate($"{BridgeTestData.BridgeRoot}/Other", true)
            ]
        };
        await using var harness = await BridgeClientHarness.StartAsync(
            server,
            waitForStreaming: false);

        await harness.WaitForAsync(() => harness.Client.GetStatus().ProtocolRejectionCount >= 1);

        BridgeClientStatus status = harness.Client.GetStatus();
        await Assert.That(status.SnapshotAppliedCount).IsEqualTo(0L);
        // The rejection is asserted on the event rather than the status detail, because the resync
        // loop records its own "no snapshot could be applied" failure after the third refusal.
        await Assert.That(harness.Events.Any(observed =>
            observed.Kind == BridgeClientEventKind.ProtocolRejected &&
            (observed.Detail ?? string.Empty).Contains(
                "negotiated bound",
                StringComparison.Ordinal))).IsTrue();
        await Assert.That(harness.Coordinator.State).IsNotEqualTo(
            LiveAuthoringSessionState.Synchronized);
    }

    [Test]
    public async Task APeerWithLowerLimitsStillStreamsWithinThem()
    {
        var server = new FakeBridgeServer
        {
            Limits = BridgeLimits.Local with { MaxUpdatesPerMessage = 4 }
        };
        await using var harness = await BridgeClientHarness.StartAsync(server);

        harness.Server.Send(BridgeTestData.DeltaFrame(1));
        await harness.WaitForAsync(() => harness.Client.GetStatus().DeltaAppliedCount >= 1);

        await Assert.That(harness.Coordinator.State)
            .IsEqualTo(LiveAuthoringSessionState.Synchronized);
    }

    [Test]
    public async Task ABatchPastTheNegotiatedByteBoundIsRefusedEvenThoughItFitsLocally()
    {
        var server = new FakeBridgeServer
        {
            Limits = BridgeLimits.Local with { MaxMessagePayloadBytes = 32 }
        };
        // A 32-byte bound also refuses the peer's own snapshot, so this session never reaches
        // Streaming. The publication is still judged: whichever side of the queue sees it, the
        // negotiated byte bound is what refuses it.
        await using var harness = await BridgeClientHarness.StartAsync(
            server,
            waitForStreaming: false);
        await harness.WaitForAsync(() => server.NegotiateCount >= 1);

        BridgeLocalBatch batch = harness.LocalBatch(1);
        int encodedBytes = BridgeWireCodec.EncodeLocalBatch(batch).Length;
        BridgeLocalPublicationReceipt receipt = await harness.Client.PublishLocalBatchAsync(batch);
        BridgeLocalPublicationResult result =
            await receipt.Published.WaitAsync(TimeSpan.FromSeconds(20));

        // One update, so only the byte bound can refuse it -- which is the point.
        await Assert.That(batch.Updates.Count).IsEqualTo(1);
        await Assert.That(encodedBytes).IsGreaterThan(32);
        await Assert.That(BridgeLimits.Local.Allows(1, encodedBytes)).IsTrue();
        await Assert.That(result.Outcome).IsEqualTo(BridgeLocalPublicationOutcome.NotPermitted);
        await Assert.That(result.Detail).Contains("negotiated bounds");
        await Assert.That(harness.Server.Published.Count).IsEqualTo(0);
    }

    [Test]
    public async Task AnInboundSnapshotPastTheNegotiatedByteBoundIsRejectedBeforeItIsDecoded()
    {
        var server = new FakeBridgeServer
        {
            Limits = BridgeLimits.Local with { MaxMessagePayloadBytes = 32 }
        };
        await using var harness = await BridgeClientHarness.StartAsync(
            server,
            waitForStreaming: false);

        await harness.WaitForAsync(() => harness.Client.GetStatus().ProtocolRejectionCount >= 1);

        BridgeClientStatus status = harness.Client.GetStatus();
        await Assert.That(status.SnapshotAppliedCount).IsEqualTo(0L);
        await Assert.That(harness.Events.Any(observed =>
            observed.Kind == BridgeClientEventKind.ProtocolRejected &&
            (observed.Detail ?? string.Empty).Contains("bytes", StringComparison.Ordinal))).IsTrue();
        // The count bound alone would have accepted this snapshot.
        await Assert.That(BridgeLimits.Local.Allows(1, 0)).IsTrue();
    }

    [Test]
    public async Task ABatchQueuedBeforeNegotiationIsReauthorizedAgainstTheNewSession()
    {
        var server = new FakeBridgeServer
        {
            Capabilities =
            [
                BridgeCapability.FullSnapshot,
                BridgeCapability.OrderedDelta,
                BridgeCapability.LocalEditExport
            ]
        };
        await using var harness = await BridgeClientHarness.StartAsync(
            server,
            waitForStreaming: false,
            startRunLoop: false);

        // Nothing is negotiated yet, so the queue cannot judge it; the pump must re-check it once
        // the session exists rather than sending an update the peer never agreed to.
        BridgeLocalPublicationReceipt receipt = await harness.Client
            .PublishLocalBatchAsync(harness.CapabilityBoundLocalBatch(1));
        await Assert.That(receipt.Accepted).IsTrue();

        harness.Start();
        BridgeLocalPublicationResult result =
            await receipt.Published.WaitAsync(TimeSpan.FromSeconds(15));

        await Assert.That(result.Outcome).IsEqualTo(BridgeLocalPublicationOutcome.NotPermitted);
        await Assert.That(result.Detail).Contains(nameof(BridgeCapability.ApiSchema));
        await Assert.That(harness.Server.Published.Count).IsEqualTo(0);
    }

    [Test]
    public async Task ABatchQueuedBeforeNegotiationIsReauthorizedAgainstTheNewLimits()
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
            await receipt.Published.WaitAsync(TimeSpan.FromSeconds(15));

        await Assert.That(receipt.Accepted).IsTrue();
        await Assert.That(result.Outcome).IsEqualTo(BridgeLocalPublicationOutcome.NotPermitted);
        await Assert.That(result.Detail).Contains("negotiated bounds");
        await Assert.That(harness.Server.Published.Count).IsEqualTo(0);
    }

    /// <summary>
    /// A batch that is legal by the authoring layer's own estimate but oversized once encoded is a
    /// refusal, not a fault. Queued before negotiation, it reaches the pump's reauthorization: the
    /// pump must answer its receipt, keep running, and keep publishing the batches behind it.
    /// </summary>
    [Test]
    public async Task AWireOversizedBatchQueuedBeforeNegotiationIsRefusedWithoutFaultingTheLoop()
    {
        var harness = await BridgeClientHarness.StartAsync(
            waitForStreaming: false,
            startRunLoop: false);

        BridgeLocalPublicationReceipt oversized =
            await harness.Client.PublishLocalBatchAsync(harness.WireOversizedLocalBatch(1));
        BridgeLocalPublicationReceipt healthy =
            await harness.Client.PublishLocalBatchAsync(harness.LocalBatch(2));

        // Nothing is negotiated yet, so the queue has no session to measure against and the pump
        // owns the verdict. The receipt is accepted exactly as a well-formed batch would be.
        await Assert.That(oversized.Accepted).IsTrue();

        harness.Start();
        BridgeLocalPublicationResult refused =
            await oversized.Published.WaitAsync(TimeSpan.FromSeconds(30));
        BridgeLocalPublicationResult published =
            await healthy.Published.WaitAsync(TimeSpan.FromSeconds(30));

        await Assert.That(refused.Outcome).IsEqualTo(BridgeLocalPublicationOutcome.NotPermitted);
        await Assert.That(refused.Detail).IsNotNull();
        await Assert.That(refused.Detail!).Contains("bytes");

        // The loop survived the refusal: the batch queued behind it still went out, and the
        // connection is still streaming rather than faulted.
        await Assert.That(published.Outcome).IsEqualTo(BridgeLocalPublicationOutcome.Published);
        await harness.WaitForStateAsync(BridgeConnectionState.Streaming);
        await Assert.That(harness.Run.IsFaulted).IsFalse();
        await Assert.That(harness.Server.Published.Count).IsEqualTo(1);

        // Disposal completes and repeats without throwing, and leaves the client disconnected.
        await harness.Client.DisposeAsync();
        await harness.Client.DisposeAsync();
        await Assert.That(harness.Client.State).IsEqualTo(BridgeConnectionState.Disconnected);
        await harness.DisposeAsync();
    }

    /// <summary>
    /// The same batch queued while a session exists is refused at the queue, by measurement rather
    /// than by encoding: the caller gets a receipt instead of an exception.
    /// </summary>
    [Test]
    public async Task AWireOversizedBatchQueuedAfterNegotiationIsRefusedAtTheQueue()
    {
        await using var harness = await BridgeClientHarness.StartAsync();
        BridgeLocalBatch batch = harness.WireOversizedLocalBatch(1);

        BridgeLocalPublicationReceipt receipt = await harness.Client.PublishLocalBatchAsync(batch);
        BridgeLocalPublicationResult result =
            await receipt.Published.WaitAsync(TimeSpan.FromSeconds(30));

        await Assert.That(receipt.Accepted).IsFalse();
        await Assert.That(result.Outcome).IsEqualTo(BridgeLocalPublicationOutcome.NotPermitted);
        await Assert.That(result.Detail).IsNotNull();
        await Assert.That(result.Detail!).Contains("bytes");
        await Assert.That(harness.Server.Published.Count).IsEqualTo(0);

        // The refusal cost the connection nothing: the next batch publishes normally.
        BridgeLocalPublicationResult published =
            await harness.PublishAndWaitAsync(harness.LocalBatch(2));
        await Assert.That(published.Outcome).IsEqualTo(BridgeLocalPublicationOutcome.Published);
        await Assert.That(harness.Client.State).IsEqualTo(BridgeConnectionState.Streaming);
        await Assert.That(harness.Run.IsFaulted).IsFalse();
    }

    /// <summary>
    /// A failure the loop does not expect must not become a faulted client task or a receipt that
    /// never completes, and disposal must still clean up and leave the explicit outcome intact.
    /// </summary>
    [Test]
    public async Task AnUnexpectedLoopFailureCompletesReceiptsAndDisposesCleanly()
    {
        var harness = await BridgeClientHarness.StartAsync(
            waitForStreaming: false,
            startRunLoop: false,
            connectionFactory: _ => throw new NotSupportedException(
                "The transport factory failed in a way the loop does not model."));

        BridgeLocalPublicationReceipt receipt =
            await harness.Client.PublishLocalBatchAsync(harness.LocalBatch(1));
        harness.Start();
        BridgeLocalPublicationResult result =
            await receipt.Published.WaitAsync(TimeSpan.FromSeconds(15));

        await Assert.That(result.Outcome).IsEqualTo(BridgeLocalPublicationOutcome.NotPermitted);

        // The client stopped and said so; it did not hand the host a faulted task.
        await harness.Run.WaitAsync(TimeSpan.FromSeconds(15));
        await Assert.That(harness.Run.IsFaulted).IsFalse();
        await Assert.That(harness.Client.State).IsEqualTo(BridgeConnectionState.Faulted);

        await harness.Client.DisposeAsync();
        await harness.Client.DisposeAsync();

        await Assert.That(harness.Client.State).IsEqualTo(BridgeConnectionState.Disconnected);

        // Disposal completes what is still pending without rewriting an answer already given.
        BridgeLocalPublicationResult afterDisposal = await receipt.Published;
        await Assert.That(afterDisposal.Outcome)
            .IsEqualTo(BridgeLocalPublicationOutcome.NotPermitted);
        await harness.DisposeAsync();
    }

    [Test]
    public async Task AnEndedConnectionClearsTheNegotiatedSessionFromStatus()
    {
        var server = new FakeBridgeServer();
        await using var harness = await BridgeClientHarness.StartAsync(server);
        await Assert.That(harness.Client.GetStatus().Negotiated).IsTrue();

        // The peer goes away and stays away, so no new session replaces the old one.
        server.NegotiateFailure = global::Grpc.Core.StatusCode.Unavailable;
        server.CloseStream();
        await harness.WaitForAsync(() => server.NegotiateCount >= 2);
        await harness.WaitForAsync(() => !harness.Client.GetStatus().Negotiated);

        BridgeClientStatus status = harness.Client.GetStatus();
        await Assert.That(status.Negotiated).IsFalse();
        await Assert.That(status.SessionId).IsNull();
        await Assert.That(status.Epoch).IsEqualTo(0L);
        await Assert.That(status.NegotiatedCapabilities.Count).IsEqualTo(0);
    }

    [Test]
    public async Task ProtocolCapabilityMappingNamesEveryOptionalUpdateKind()
    {
        await Assert.That(BridgeProtocol.GetRequiredCapability(
                new ApiSchemaUpdate("/Bridge/Cube", "AssetPreviewsAPI", LiveApiSchemaOperation.Apply)))
            .IsEqualTo(BridgeCapability.ApiSchema);
        await Assert.That(BridgeProtocol.GetRequiredCapability(
                new SetPointInstancerOrientationsUpdate("/Bridge/Points", [new UsdQuatf(1, 0, 0, 0)])))
            .IsEqualTo(BridgeCapability.PointInstancerOrientations);
        await Assert.That(BridgeProtocol.GetRequiredCapability(
                new SetActiveUpdate("/Bridge/Cube", true)))
            .IsNull();
    }
}

/// <summary>
/// Disposal is deterministic: it cancels the run loop, waits for it, completes every queued
/// publication, and is safe to repeat.
/// </summary>
public sealed class BridgeClientLifetimeTests
{
    [Test]
    public async Task DisposalCancelsAndAwaitsTheRunLoop()
    {
        var harness = await BridgeClientHarness.StartAsync();
        await harness.WaitForStateAsync(BridgeConnectionState.Streaming);
        int streamsBefore = harness.Server.StreamCount;

        await harness.Client.DisposeAsync();

        await Assert.That(harness.Run.IsCompleted).IsTrue();
        await Assert.That(harness.Client.State).IsEqualTo(BridgeConnectionState.Disconnected);

        // A disposed client must not keep reconnecting behind the host's back.
        harness.Server.CloseStream();
        await Task.Delay(200);
        await Assert.That(harness.Server.StreamCount).IsEqualTo(streamsBefore);
        await harness.DisposeAsync();
    }

    [Test]
    public async Task DisposalIsIdempotent()
    {
        await using var harness = await BridgeClientHarness.StartAsync();

        await harness.Client.DisposeAsync();
        await harness.Client.DisposeAsync();
        await harness.Client.DisposeAsync();

        await Assert.That(harness.Client.State).IsEqualTo(BridgeConnectionState.Disconnected);
    }

    [Test]
    public async Task DisposalCompletesEveryQueuedPublication()
    {
        var harness = await BridgeClientHarness.StartAsync(
            waitForStreaming: false,
            startRunLoop: false);

        BridgeLocalPublicationReceipt receipt =
            await harness.Client.PublishLocalBatchAsync(harness.LocalBatch(1));
        await harness.Client.DisposeAsync();

        BridgeLocalPublicationResult result =
            await receipt.Published.WaitAsync(TimeSpan.FromSeconds(15));

        await Assert.That(result.Outcome).IsEqualTo(BridgeLocalPublicationOutcome.Cancelled);
        await harness.DisposeAsync();
    }

    [Test]
    public async Task PublishingAfterDisposalThrows()
    {
        var harness = await BridgeClientHarness.StartAsync(
            waitForStreaming: false,
            startRunLoop: false);
        BridgeLocalBatch batch = harness.LocalBatch(1);
        await harness.Client.DisposeAsync();

        await Assert.That(async () => await harness.Client.PublishLocalBatchAsync(batch))
            .Throws<ObjectDisposedException>();
        await harness.DisposeAsync();
    }

    [Test]
    public async Task RunningAfterDisposalThrows()
    {
        var harness = await BridgeClientHarness.StartAsync(
            waitForStreaming: false,
            startRunLoop: false);
        await harness.Client.DisposeAsync();

        await Assert.That(async () => await harness.Client.RunAsync(CancellationToken.None))
            .Throws<ObjectDisposedException>();
        await harness.DisposeAsync();
    }

    [Test]
    public async Task ASecondConcurrentRunIsRefused()
    {
        await using var harness = await BridgeClientHarness.StartAsync();
        await harness.WaitForStateAsync(BridgeConnectionState.Streaming);

        await Assert.That(async () => await harness.Client.RunAsync(CancellationToken.None))
            .Throws<InvalidOperationException>();
    }
}

/// <summary>
/// Events report the real transition, and a broken observer is isolated on its own counters instead
/// of overwriting the reason the transport failed.
/// </summary>
public sealed class BridgeClientObserverTests
{
    [Test]
    public async Task EventsCarryTheRealPreviousState()
    {
        await using var harness = await BridgeClientHarness.StartAsync();
        await harness.WaitForStateAsync(BridgeConnectionState.Streaming);

        BridgeClientEvent negotiated = harness.Events.First(
            observed => observed.Kind == BridgeClientEventKind.Negotiated);
        BridgeClientEvent connecting = harness.Events.First(
            observed => observed.Kind == BridgeClientEventKind.Connecting);

        await Assert.That(connecting.PreviousState).IsEqualTo(BridgeConnectionState.Disconnected);
        await Assert.That(connecting.State).IsEqualTo(BridgeConnectionState.Connecting);
        await Assert.That(negotiated.PreviousState).IsEqualTo(BridgeConnectionState.Connecting);
        await Assert.That(negotiated.State).IsEqualTo(BridgeConnectionState.Negotiating);
        await Assert.That(harness.Events.Any(
            observed => observed.PreviousState != observed.State)).IsTrue();
    }

    [Test]
    public async Task ABrokenObserverIsIsolatedOnItsOwnCounters()
    {
        var observer = new ThrowingProgress();
        await using var harness = await BridgeClientHarness.StartAsync(observer: observer);

        await harness.WaitForStateAsync(BridgeConnectionState.Streaming);
        await harness.WaitForAsync(() => harness.Client.GetStatus().ObserverFailureCount > 0);

        BridgeClientStatus status = harness.Client.GetStatus();
        await Assert.That(status.ObserverFailureCount).IsGreaterThan(0L);
        await Assert.That(status.LastObserverFailureDetail).Contains(nameof(InvalidOperationException));
        // The observer's failure must not be mistaken for a transport or protocol failure.
        await Assert.That(status.LastFailureDetail ?? string.Empty)
            .DoesNotContain("observer");
        await Assert.That(harness.Coordinator.State)
            .IsEqualTo(LiveAuthoringSessionState.Synchronized);
    }

    [Test]
    public async Task ABrokenObserverDoesNotOverwriteTheTransportFailureDetail()
    {
        var observer = new ThrowingProgress();
        var server = new FakeBridgeServer { NegotiateFailure = global::Grpc.Core.StatusCode.Unavailable };
        await using var harness = await BridgeClientHarness.StartAsync(
            server,
            waitForStreaming: false,
            observer: observer);

        await harness.WaitForAsync(() => server.NegotiateCount >= 2);

        BridgeClientStatus status = harness.Client.GetStatus();
        await Assert.That(status.LastFailureDetail).Contains("Unavailable");
        await Assert.That(status.ObserverFailureCount).IsGreaterThan(0L);
    }

    private sealed class ThrowingProgress : IProgress<BridgeClientEvent>
    {
        public void Report(BridgeClientEvent value) =>
            throw new InvalidOperationException("The observer is broken.");
    }
}

/// <summary>A credential is bounded and header-safe before anything can present it.</summary>
public sealed class BridgeCredentialInjectionTests
{
    [Test]
    public async Task ATokenWithACarriageReturnIsRefused()
    {
        ArgumentException exception = Assert.Throws<ArgumentException>(
            () => _ = new BridgeCallCredential(
                "token\r\nx-injected: true",
                DateTimeOffset.UtcNow.AddMinutes(5)));

        await Assert.That(exception.ParamName).IsEqualTo("token");
        await Assert.That(exception.Message).DoesNotContain("x-injected");
    }

    [Test]
    public async Task ATokenWithAControlCharacterIsRefused()
    {
        ArgumentException exception = Assert.Throws<ArgumentException>(
            () => _ = new BridgeCallCredential("token\u0007", DateTimeOffset.UtcNow.AddMinutes(5)));

        await Assert.That(exception.ParamName).IsEqualTo("token");
    }

    [Test]
    public async Task ATokenWithASpaceIsRefused()
    {
        ArgumentException exception = Assert.Throws<ArgumentException>(
            () => _ = new BridgeCallCredential("two words", DateTimeOffset.UtcNow.AddMinutes(5)));

        await Assert.That(exception.ParamName).IsEqualTo("token");
    }

    [Test]
    public async Task AnOverlongTokenIsRefused()
    {
        ArgumentException exception = Assert.Throws<ArgumentException>(
            () => _ = new BridgeCallCredential(
                new string('t', BridgeCallCredential.MaxTokenLength + 1),
                DateTimeOffset.UtcNow.AddMinutes(5)));

        await Assert.That(exception.ParamName).IsEqualTo("token");
    }

    [Test]
    public async Task ASchemeWithASpaceOrControlCharacterIsRefused()
    {
        ArgumentException injected = Assert.Throws<ArgumentException>(
            () => _ = new BridgeCallCredential(
                "token",
                DateTimeOffset.UtcNow.AddMinutes(5),
                "Bearer\r\nx-injected:"));
        ArgumentException spaced = Assert.Throws<ArgumentException>(
            () => _ = new BridgeCallCredential(
                "token",
                DateTimeOffset.UtcNow.AddMinutes(5),
                "Bearer token"));

        await Assert.That(injected.ParamName).IsEqualTo("scheme");
        await Assert.That(spaced.ParamName).IsEqualTo("scheme");
    }

    [Test]
    public async Task AnOverlongSchemeIsRefused()
    {
        ArgumentException exception = Assert.Throws<ArgumentException>(
            () => _ = new BridgeCallCredential(
                "token",
                DateTimeOffset.UtcNow.AddMinutes(5),
                new string('s', BridgeCallCredential.MaxSchemeLength + 1)));

        await Assert.That(exception.ParamName).IsEqualTo("scheme");
    }

    [Test]
    public async Task AValidCredentialStillProducesExactlyOneHeaderValue()
    {
        var credential = new BridgeCallCredential(
            "abc.def-ghi_jkl~123",
            DateTimeOffset.UtcNow.AddMinutes(5));

        string value = credential.ToHeaderValue();

        await Assert.That(value).IsEqualTo("Bearer abc.def-ghi_jkl~123");
        await Assert.That(value.Count(character => character == ' ')).IsEqualTo(1);
        await Assert.That(credential.ToString()).DoesNotContain("abc.def");
    }
}
