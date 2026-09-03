// Copyright (c) marcschier. Licensed under the MIT License.

using OpenUsd.Bridge.Grpc;
using OpenUsd.Bridge.Protocol;
using OpenUsd.LiveAuthoring;

namespace OpenUsd.Bridge.Tests;

/// <summary>
/// The origin identifier is an identity, not a label, so two publishers never share one by default.
/// </summary>
/// <remarks>
/// <para>
/// A shared origin fails twice and silently. The coordinator suppresses inbound deltas carrying its
/// own origin as echoes of local edits, so a second publisher under the same identifier has its
/// edits acknowledged and never authored; and a derived idempotency key names the origin, so two
/// publishers under one identifier derive colliding keys a peer's ledger reads as replays.
/// </para>
/// <para>
/// These tests require the default to be unique per instance, the injected factory to make it
/// deterministic without reintroducing a shared literal, the client to adopt exactly the
/// coordinator's resolved identity, and an outbound batch naming any other origin to be refused.
/// </para>
/// </remarks>
public sealed class BridgePublisherIdentityTests
{
    [Test]
    public async Task TwoDefaultCoordinatorsInOneProcessGetDistinctOrigins()
    {
        await using CoordinatorFixture first = CoordinatorFixture.Create();
        await using CoordinatorFixture second = CoordinatorFixture.Create();

        await Assert.That(first.Coordinator.LocalOriginId)
            .IsNotEqualTo(second.Coordinator.LocalOriginId);
        await Assert.That(first.Coordinator.LocalOriginId.Length).IsGreaterThan(0);
        await Assert.That(
            first.Coordinator.LocalOriginId.Length <= LiveAuthoringValidation.MaxOpaqueIdLength)
            .IsTrue();
        await Assert.That(LiveAuthoringValidation.IsWellFormedUtf16(first.Coordinator.LocalOriginId))
            .IsTrue();
    }

    /// <summary>
    /// Two clients built from default options publish under different origins, derive different
    /// idempotency keys for the same sequence, and do not suppress each other's edits as echoes.
    /// </summary>
    [Test]
    public async Task TwoDefaultClientsGetDistinctOriginsKeysAndDoNotSuppressEachOthersEchoes()
    {
        await using CoordinatorFixture firstFixture = CoordinatorFixture.Create();
        await using CoordinatorFixture secondFixture = CoordinatorFixture.Create();
        await using var firstClient = new OmniverseBridgeClient(
            firstFixture.Coordinator,
            CreateOptions());
        await using var secondClient = new OmniverseBridgeClient(
            secondFixture.Coordinator,
            CreateOptions());

        await Assert.That(firstClient.LocalOriginId).IsNotEqualTo(secondClient.LocalOriginId);
        await Assert.That(firstClient.LocalOriginId)
            .IsEqualTo(firstFixture.Coordinator.LocalOriginId);

        // Same session, same epoch, same sequence: only the publisher differs, and that alone must
        // keep the derived keys apart or one peer ledger entry would swallow both edits.
        var firstBatch = new BridgeLocalBatch(
            BridgeTestData.Epoch(1),
            5,
            [new SetActiveUpdate($"{BridgeTestData.BridgeRoot}/Cube", true)],
            firstClient.LocalOriginId);
        var secondBatch = new BridgeLocalBatch(
            BridgeTestData.Epoch(1),
            5,
            [new SetActiveUpdate($"{BridgeTestData.BridgeRoot}/Cube", true)],
            secondClient.LocalOriginId);
        await Assert.That(firstBatch.IdempotencyKey).IsNotEqualTo(secondBatch.IdempotencyKey);

        // The second coordinator must author the first publisher's edit rather than mistake it for
        // an echo of its own, which is exactly what a shared default origin would have caused.
        await secondFixture.SynchronizeAsync();
        LiveAuthoringSessionResult applied = await secondFixture.Coordinator.ApplyDeltaAsync(
            CreateDelta(sequence: 1, originId: firstClient.LocalOriginId),
            CancellationToken.None);
        await Assert.That(applied.Outcome).IsEqualTo(LiveAuthoringSessionOutcome.Applied);

        // Its own origin is still suppressed, so uniqueness did not disable loop prevention.
        LiveAuthoringSessionResult suppressed = await secondFixture.Coordinator.ApplyDeltaAsync(
            CreateDelta(sequence: 2, originId: secondClient.LocalOriginId),
            CancellationToken.None);
        await Assert.That(suppressed.Outcome)
            .IsEqualTo(LiveAuthoringSessionOutcome.LoopSuppressed);
    }

    [Test]
    public async Task AnInjectedFactoryMakesTheDefaultOriginDeterministic()
    {
        await using CoordinatorFixture fixture = CoordinatorFixture.Create(
            factory: () => "fixed-origin");
        await using var client = new OmniverseBridgeClient(fixture.Coordinator, CreateOptions());

        await Assert.That(fixture.Coordinator.LocalOriginId).IsEqualTo("fixed-origin");
        await Assert.That(client.LocalOriginId).IsEqualTo("fixed-origin");
    }

    [Test]
    public async Task AFactoryThatReturnsAnUnusableOriginIsRefused()
    {
        await using var executor = new RecordingOverlayExecutor();
        await using var sink = new QueuedLiveAuthoringSink(executor, capacity: 4);

        ArgumentException exception = Assert.Throws<ArgumentException>(
            () => _ = new LiveAuthoringSessionCoordinator(
                sink,
                new LiveAuthoringSessionOptions
                {
                    BridgeRootPath = BridgeTestData.BridgeRoot,
                    LocalOriginIdFactory = () => "   "
                }));

        await Assert.That(exception.Message).Contains("origin identifier");
    }

    [Test]
    public async Task AClientWithNoConfiguredOriginAdoptsTheCoordinatorsResolvedIdentity()
    {
        await using CoordinatorFixture fixture = CoordinatorFixture.Create();
        BridgeClientOptions options = CreateOptions();
        await using var client = new OmniverseBridgeClient(fixture.Coordinator, options);

        await Assert.That(options.LocalOriginId).IsNull();
        await Assert.That(client.LocalOriginId).IsEqualTo(fixture.Coordinator.LocalOriginId);
    }

    /// <summary>
    /// A batch naming another publisher is refused before it can be queued, because the peer's echo
    /// of it would not be recognized as a local edit.
    /// </summary>
    [Test]
    public async Task AnOutboundBatchNamingAnotherOriginIsRefused()
    {
        await using var harness = await BridgeClientHarness.StartAsync();

        var foreign = new BridgeLocalBatch(
            BridgeTestData.Epoch(harness.Server.Epoch),
            1,
            [new SetActiveUpdate($"{BridgeTestData.BridgeRoot}/Cube", true)],
            "someone-elses-origin",
            correlationId: "local-1");

        BridgeLocalPublicationReceipt receipt =
            await harness.Client.PublishLocalBatchAsync(foreign);
        BridgeLocalPublicationResult result =
            await receipt.Published.WaitAsync(TimeSpan.FromSeconds(20));

        await Assert.That(receipt.Accepted).IsFalse();
        await Assert.That(result.Outcome).IsEqualTo(BridgeLocalPublicationOutcome.NotPermitted);
        await Assert.That(result.Attempts).IsEqualTo(0);
        await Assert.That(result.Detail!).Contains("origin identifier");
        await Assert.That(harness.Server.Published.Count).IsEqualTo(0);

        // The refusal costs the session nothing: a batch under the client's own identity publishes.
        BridgeLocalPublicationResult published =
            await harness.PublishAndWaitAsync(harness.LocalBatch(2));
        await Assert.That(published.Outcome).IsEqualTo(BridgeLocalPublicationOutcome.Published);
    }

    /// <summary>
    /// A batch naming another origin is refused even before a session exists, because the origin is
    /// the client's identity rather than a property of whatever the peer negotiates.
    /// </summary>
    [Test]
    public async Task AnOutboundBatchNamingAnotherOriginIsRefusedBeforeNegotiation()
    {
        var harness = await BridgeClientHarness.StartAsync(
            waitForStreaming: false,
            startRunLoop: false);

        var foreign = new BridgeLocalBatch(
            BridgeTestData.Epoch(1),
            1,
            [new SetActiveUpdate($"{BridgeTestData.BridgeRoot}/Cube", true)],
            "someone-elses-origin");

        BridgeLocalPublicationReceipt receipt =
            await harness.Client.PublishLocalBatchAsync(foreign);
        BridgeLocalPublicationResult result =
            await receipt.Published.WaitAsync(TimeSpan.FromSeconds(20));

        await Assert.That(receipt.Accepted).IsFalse();
        await Assert.That(result.Outcome).IsEqualTo(BridgeLocalPublicationOutcome.NotPermitted);
        await Assert.That(result.Detail!).Contains("origin identifier");
        await harness.DisposeAsync();
    }

    /// <summary>The generated default is unique across many instances, not merely across two.</summary>
    [Test]
    public async Task GeneratedOriginsAreUniqueAcrossManyInstances()
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (int index = 0; index < 128; index++)
        {
            await Assert.That(seen.Add(LiveAuthoringOriginId.CreateProcessInstanceUnique()))
                .IsTrue();
        }
    }

    private static BridgeClientOptions CreateOptions() =>
        new()
        {
            Endpoint = new Uri("http://127.0.0.1:53017"),
            Credentials = new EphemeralBearerTokenProvider(
                "token",
                DateTimeOffset.UtcNow.AddHours(1)),
            BridgeRootPath = BridgeTestData.BridgeRoot
        };

    private static LiveAuthoringDelta CreateDelta(long sequence, string originId) =>
        new(
            BridgeTestData.Epoch(1),
            sequence,
            [
                new SetAttributeUpdate(
                    $"{BridgeTestData.BridgeRoot}/Cube",
                    "custom:pressure",
                    LiveAttributeValue.FromDouble(sequence))
            ],
            correlationId: null,
            originId: originId);

    /// <summary>One coordinator with the sink and executor it owns.</summary>
    private sealed class CoordinatorFixture : IAsyncDisposable
    {
        private readonly RecordingOverlayExecutor _executor;
        private readonly QueuedLiveAuthoringSink _sink;

        private CoordinatorFixture(
            RecordingOverlayExecutor executor,
            QueuedLiveAuthoringSink sink,
            LiveAuthoringSessionCoordinator coordinator)
        {
            _executor = executor;
            _sink = sink;
            Coordinator = coordinator;
        }

        internal LiveAuthoringSessionCoordinator Coordinator { get; }

        internal static CoordinatorFixture Create(Func<string>? factory = null)
        {
            var executor = new RecordingOverlayExecutor();
            var sink = new QueuedLiveAuthoringSink(executor, capacity: 8);
            var coordinator = new LiveAuthoringSessionCoordinator(
                sink,
                new LiveAuthoringSessionOptions
                {
                    BridgeRootPath = BridgeTestData.BridgeRoot,
                    LocalOriginIdFactory = factory
                });
            return new CoordinatorFixture(executor, sink, coordinator);
        }

        internal async Task SynchronizeAsync()
        {
            await Coordinator.ConnectAsync(BridgeTestData.Epoch(1), CancellationToken.None)
                .ConfigureAwait(false);
            await Coordinator.ApplySnapshotAsync(BridgeTestData.Snapshot(), CancellationToken.None)
                .ConfigureAwait(false);
        }

        public async ValueTask DisposeAsync()
        {
            await Coordinator.DisposeAsync().ConfigureAwait(false);
            await _sink.DisposeAsync().ConfigureAwait(false);
            await _executor.DisposeAsync().ConfigureAwait(false);
        }
    }
}
