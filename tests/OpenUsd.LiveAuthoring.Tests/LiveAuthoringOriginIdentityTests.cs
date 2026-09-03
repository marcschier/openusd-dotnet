// Copyright (c) marcschier. Licensed under the MIT License.

using System.Text;

namespace OpenUsd.LiveAuthoring.Tests;

/// <summary>
/// The coordinator's local origin is an identity: unique by default, deterministic when a host or a
/// test supplies a factory, and never accepted in a form that two different values could collapse
/// into on the wire.
/// </summary>
/// <remarks>
/// Echo suppression compares origins, and the replay ledger fingerprints them. A default shared by
/// every coordinator in a process makes each suppress the other's edits as its own echoes; an
/// identity that is not well-formed UTF-16 makes two different origins hash and encode as one. Both
/// failures are silent — an edit is acknowledged and never authored — so both are rejected at
/// construction rather than diagnosed later.
/// </remarks>
public sealed class LiveAuthoringOriginIdentityTests
{
    private const string BridgeRoot = "/Bridge";
    private const string RemoteOrigin = "kit-bridge";
    private const string SessionId = "session-a";
    private const string LoneHigh = "origin-\ud800";
    private const string LoneLow = "origin-\udc00";

    [Test]
    public async Task TwoCoordinatorsWithDefaultOptionsDoNotShareAnOrigin()
    {
        await using Fixture first = Fixture.Create();
        await using Fixture second = Fixture.Create();

        await Assert.That(first.Coordinator.LocalOriginId)
            .IsNotEqualTo(second.Coordinator.LocalOriginId);
        await Assert.That(LiveAuthoringValidation.IsWellFormedUtf16(first.Coordinator.LocalOriginId))
            .IsTrue();
        await Assert.That(first.Coordinator.LocalOriginId.Length)
            .IsLessThanOrEqualTo(LiveAuthoringValidation.MaxOpaqueIdLength);
    }

    /// <summary>
    /// The generated identity does not weaken loop prevention: a coordinator still suppresses its
    /// own echoes and still authors another publisher's edits.
    /// </summary>
    [Test]
    public async Task AGeneratedOriginStillSuppressesItsOwnEchoesAndOnlyItsOwn()
    {
        await using Fixture fixture = Fixture.Create();
        await using Fixture other = Fixture.Create();
        await fixture.ConnectAndSynchronizeAsync();

        LiveAuthoringSessionResult echo = await fixture.Coordinator.ApplyDeltaAsync(
            Delta(1, fixture.Coordinator.LocalOriginId));
        LiveAuthoringSessionResult foreign = await fixture.Coordinator.ApplyDeltaAsync(
            Delta(2, other.Coordinator.LocalOriginId));

        await Assert.That(echo.Outcome).IsEqualTo(LiveAuthoringSessionOutcome.LoopSuppressed);
        await Assert.That(foreign.Outcome).IsEqualTo(LiveAuthoringSessionOutcome.Applied);
    }

    [Test]
    public async Task AnInjectedFactoryReplacesTheGeneratedDefault()
    {
        await using Fixture fixture = Fixture.Create(factory: () => "deterministic-origin");

        await Assert.That(fixture.Coordinator.LocalOriginId).IsEqualTo("deterministic-origin");
    }

    [Test]
    public async Task AnExplicitOriginWinsOverTheFactory()
    {
        await using Fixture fixture = Fixture.Create(
            configured: "explicit-origin",
            factory: () => "factory-origin");

        await Assert.That(fixture.Coordinator.LocalOriginId).IsEqualTo("explicit-origin");
    }

    /// <summary>
    /// Two origins that differ only in an unpaired surrogate encode to one byte sequence, so they
    /// would fingerprint and compare as one identity. Neither is accepted.
    /// </summary>
    [Test]
    public async Task AnIllFormedOriginIsRefusedBecauseItWouldCollideWithAnother()
    {
        // The premise, asserted rather than assumed: distinct strings, identical UTF-8 bytes.
        await Assert.That(LoneHigh).IsNotEqualTo(LoneLow);
        await Assert.That(Convert.ToHexString(Encoding.UTF8.GetBytes(LoneHigh)))
            .IsEqualTo(Convert.ToHexString(Encoding.UTF8.GetBytes(LoneLow)));

        await Assert.That(Assert.Throws<ArgumentException>(() => Fixture.Create(LoneHigh)).Message)
            .Contains("unpaired surrogate");
        await Assert.That(Assert.Throws<ArgumentException>(() => Fixture.Create(LoneLow)).Message)
            .Contains("unpaired surrogate");
        await Assert.That(
            Assert.Throws<ArgumentException>(
                () => Fixture.Create(configured: null, factory: () => LoneHigh)).Message)
            .Contains("unpaired surrogate");
    }

    [Test]
    public async Task AnIllFormedRemoteIdentityOrCorrelationIsRefused()
    {
        ArgumentException remoteOrigin = Assert.Throws<ArgumentException>(
            () => _ = new LiveAuthoringRemoteEpoch(LoneHigh, SessionId, 1));
        ArgumentException sessionId = Assert.Throws<ArgumentException>(
            () => _ = new LiveAuthoringRemoteEpoch(RemoteOrigin, LoneLow, 1));
        ArgumentException correlation = Assert.Throws<ArgumentException>(
            () => _ = new LiveAuthoringBatch(
                1,
                [new DefinePrimUpdate($"{BridgeRoot}/Cube")],
                correlationId: LoneHigh));
        ArgumentException batchOrigin = Assert.Throws<ArgumentException>(
            () => _ = new LiveAuthoringBatch(
                1,
                [new DefinePrimUpdate($"{BridgeRoot}/Cube")],
                originId: LoneLow));

        await Assert.That(remoteOrigin.Message).Contains("unpaired surrogate");
        await Assert.That(sessionId.Message).Contains("unpaired surrogate");
        await Assert.That(correlation.Message).Contains("unpaired surrogate");
        await Assert.That(batchOrigin.Message).Contains("unpaired surrogate");
    }

    [Test]
    public async Task AWellFormedSupplementaryCharacterIsStillAccepted()
    {
        // Rejecting ill-formed UTF-16 must not reject legal astral code points.
        await using Fixture fixture = Fixture.Create("origin-\ud83d\ude00");

        await Assert.That(fixture.Coordinator.LocalOriginId).IsEqualTo("origin-\ud83d\ude00");
    }

    private static LiveAuthoringDelta Delta(long sequence, string originId) =>
        new(
            new LiveAuthoringRemoteEpoch(RemoteOrigin, SessionId, 1),
            sequence,
            [
                new SetAttributeUpdate(
                    $"{BridgeRoot}/Cube",
                    "custom:pressure",
                    LiveAttributeValue.FromDouble(sequence))
            ],
            correlationId: null,
            originId: originId);

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly QueuedLiveAuthoringSink _sink;

        private Fixture(QueuedLiveAuthoringSink sink, LiveAuthoringSessionCoordinator coordinator)
        {
            _sink = sink;
            Coordinator = coordinator;
        }

        internal LiveAuthoringSessionCoordinator Coordinator { get; }

        internal static Fixture Create(string? configured = null, Func<string>? factory = null)
        {
            var sink = new QueuedLiveAuthoringSink(new NoOpExecutor(), capacity: 8);
            try
            {
                var coordinator = new LiveAuthoringSessionCoordinator(
                    sink,
                    new LiveAuthoringSessionOptions
                    {
                        BridgeRootPath = BridgeRoot,
                        LocalOriginId = configured,
                        LocalOriginIdFactory = factory
                    });
                return new Fixture(sink, coordinator);
            }
            catch
            {
                sink.DisposeAsync().AsTask().GetAwaiter().GetResult();
                throw;
            }
        }

        internal async Task ConnectAndSynchronizeAsync()
        {
            var epoch = new LiveAuthoringRemoteEpoch(RemoteOrigin, SessionId, 1);
            await Coordinator.ConnectAsync(epoch);
            await Coordinator.ApplySnapshotAsync(new LiveAuthoringSnapshot(
                epoch,
                0,
                BridgeRoot,
                [new DefinePrimUpdate($"{BridgeRoot}/Cube", "Xform")]));
        }

        public async ValueTask DisposeAsync()
        {
            await Coordinator.DisposeAsync();
            await _sink.DisposeAsync();
        }
    }

    private sealed class NoOpExecutor : ILiveAuthoringBatchExecutor
    {
        private ulong _serial;

        public ValueTask<LiveAuthoringBatchResult> ExecuteAsync(
            LiveAuthoringBatch batch,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ulong before = _serial++;
            return ValueTask.FromResult(new LiveAuthoringBatchResult(
                batch.Sequence,
                batch.Sequence,
                1,
                batch.Updates.Count,
                batch.Invalidation,
                before,
                _serial,
                "memory://session",
                batch.CorrelationId,
                batch.OriginId));
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
