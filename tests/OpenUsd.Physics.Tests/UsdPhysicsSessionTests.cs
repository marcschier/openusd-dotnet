// Copyright (c) marcschier. Licensed under the MIT License.

namespace OpenUsd.Physics.Tests;

public sealed class UsdPhysicsSessionTests
{
    [Test]
    public async Task BuildWithDefaultBackendReportsNoCapabilitiesAndDiagnostic()
    {
        await using UsdPhysicsSession session = await UsdPhysicsSession.BuildForTestingAsync(
            scheduler: null,
            options: null,
            new UsdPhysicsNotSupportedBackend());

        await Assert.That(session.State).IsEqualTo(UsdPhysicsSessionState.Ready);
        await Assert.That(session.Capabilities).IsEqualTo(UsdPhysicsCapabilities.None);
        await Assert.That(session.Diagnostics.HasErrors).IsFalse();
        await Assert.That(session.Diagnostics.Entries.Any(
            diagnostic => diagnostic.Code == "OPENUSD_PHYSICS_BACKEND_UNAVAILABLE")).IsTrue();
        await Assert.That(session.LatestSnapshot).IsEqualTo(UsdPhysicsSnapshot.Empty);
        await Assert.That(session.Options).IsEqualTo(UsdPhysicsSessionOptions.Default);
    }

    [Test]
    public async Task StepRequiresValidOwnershipAndPublishesSnapshot()
    {
        await using UsdPhysicsSession session = await UsdPhysicsSession.BuildForTestingAsync(
            null, null, new UsdPhysicsNotSupportedBackend());
        using UsdPhysicsStepOwnership owner = session.AcquireStepOwnership();

        UsdPhysicsStepResult result = session.Step(owner, new UsdPhysicsStepRequest(1.0 / 60));

        await Assert.That(result.Snapshot.StepIndex).IsEqualTo(1ul);
        await Assert.That(session.LatestSnapshot).IsEqualTo(result.Snapshot);
    }

    [Test]
    public async Task StepWithoutOwnershipTokenThrows()
    {
        await using UsdPhysicsSession session = await UsdPhysicsSession.BuildForTestingAsync(
            null, null, new UsdPhysicsNotSupportedBackend());
        await using UsdPhysicsSession other = await UsdPhysicsSession.BuildForTestingAsync(
            null, null, new UsdPhysicsNotSupportedBackend());
        using UsdPhysicsStepOwnership foreignOwner = other.AcquireStepOwnership();

        await Assert.That(() => session.Step(foreignOwner, new UsdPhysicsStepRequest(1.0 / 60)))
            .Throws<UsdPhysicsStepOwnershipException>();
    }

    [Test]
    public async Task StepAfterOwnershipDisposalThrows()
    {
        await using UsdPhysicsSession session = await UsdPhysicsSession.BuildForTestingAsync(
            null, null, new UsdPhysicsNotSupportedBackend());
        UsdPhysicsStepOwnership owner = session.AcquireStepOwnership();
        owner.Dispose();

        await Assert.That(() => session.Step(owner, new UsdPhysicsStepRequest(1.0 / 60)))
            .Throws<UsdPhysicsStepOwnershipException>();
    }

    [Test]
    public async Task AcquireStepOwnershipIsExclusiveUntilDisposed()
    {
        await using UsdPhysicsSession session = await UsdPhysicsSession.BuildForTestingAsync(
            null, null, new UsdPhysicsNotSupportedBackend());
        UsdPhysicsStepOwnership first = session.AcquireStepOwnership();

        await Assert.That(session.AcquireStepOwnership)
            .Throws<UsdPhysicsStepOwnershipException>();

        first.Dispose();

        UsdPhysicsStepOwnership second = session.AcquireStepOwnership();
        second.Dispose();
    }

    [Test]
    public async Task ResetAsyncRestoresReadyStateAndClearsSnapshot()
    {
        await using UsdPhysicsSession session = await UsdPhysicsSession.BuildForTestingAsync(
            null, null, new UsdPhysicsNotSupportedBackend());
        using (UsdPhysicsStepOwnership owner = session.AcquireStepOwnership())
        {
            _ = session.Step(owner, new UsdPhysicsStepRequest(1.0 / 60));
        }

        await session.ResetAsync();

        await Assert.That(session.State).IsEqualTo(UsdPhysicsSessionState.Ready);
        await Assert.That(session.LatestSnapshot).IsEqualTo(UsdPhysicsSnapshot.Empty);
    }

    [Test]
    public async Task SeekAsyncRejectsNonFiniteTimeCode()
    {
        await using UsdPhysicsSession session = await UsdPhysicsSession.BuildForTestingAsync(
            null, null, new UsdPhysicsNotSupportedBackend());

        await Assert.That(() => session.SeekAsync(double.NaN))
            .Throws<ArgumentOutOfRangeException>();
    }

    [Test]
    public async Task SeekAsyncPublishesNewSnapshot()
    {
        await using UsdPhysicsSession session = await UsdPhysicsSession.BuildForTestingAsync(
            null, null, new UsdPhysicsNotSupportedBackend());

        await session.SeekAsync(12.5);

        await Assert.That(session.LatestSnapshot.TimeCode).IsEqualTo(12.5);
    }

    [Test]
    public async Task BakeAsyncReportsNotSupportedWithoutModifyingSampleCount()
    {
        await using UsdPhysicsSession session = await UsdPhysicsSession.BuildForTestingAsync(
            null, null, new UsdPhysicsNotSupportedBackend());

        UsdPhysicsBakeResult result = await session.BakeAsync(
            new UsdPhysicsBakeRequest("/baked.usda", 0, 1, 1.0 / 60));

        await Assert.That(result.Status).IsEqualTo(UsdPhysicsBakeStatus.NotSupported);
        await Assert.That(result.SampleCount).IsEqualTo(0);
        await Assert.That(session.Diagnostics.HasErrors).IsFalse();
    }

    [Test]
    public async Task DisposeAsyncThrowsWhileStepOwnershipIsActive()
    {
        UsdPhysicsSession session = await UsdPhysicsSession.BuildForTestingAsync(
            null, null, new UsdPhysicsNotSupportedBackend());
        UsdPhysicsStepOwnership owner = session.AcquireStepOwnership();

        await Assert.That(() => session.DisposeAsync().AsTask())
            .Throws<InvalidOperationException>();

        await Assert.That(session.State).IsNotEqualTo(UsdPhysicsSessionState.Disposed);

        owner.Dispose();
        await session.DisposeAsync();

        await Assert.That(session.State).IsEqualTo(UsdPhysicsSessionState.Disposed);
    }

    [Test]
    public async Task DisposeAsyncIsIdempotent()
    {
        UsdPhysicsSession session = await UsdPhysicsSession.BuildForTestingAsync(
            null, null, new UsdPhysicsNotSupportedBackend());

        await session.DisposeAsync();
        await session.DisposeAsync();

        await Assert.That(session.State).IsEqualTo(UsdPhysicsSessionState.Disposed);
    }

    [Test]
    public async Task OperationsAfterDisposeThrowObjectDisposedException()
    {
        UsdPhysicsSession session = await UsdPhysicsSession.BuildForTestingAsync(
            null, null, new UsdPhysicsNotSupportedBackend());
        await session.DisposeAsync();

        await Assert.That(() => session.ResetAsync()).Throws<ObjectDisposedException>();
        await Assert.That(() => session.SeekAsync(0)).Throws<ObjectDisposedException>();
        await Assert.That(session.AcquireStepOwnership).Throws<ObjectDisposedException>();
    }

    [Test]
    public async Task BuildAsyncThrowsForPreCanceledToken()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Task<UsdPhysicsSession> buildTask = UsdPhysicsSession.BuildForTestingAsync(
            null, null, new UsdPhysicsNotSupportedBackend(), cts.Token);

        await Assert.That(buildTask).Throws<OperationCanceledException>();
    }

    [Test]
    public async Task CustomBackendNegotiatesReportedCapabilities()
    {
        await using UsdPhysicsSession session = await UsdPhysicsSession.BuildForTestingAsync(
            null,
            new UsdPhysicsSessionOptions(UsdPhysicsCapability.RigidBodies),
            new FakeBackend(UsdPhysicsCapability.RigidBodies));

        await Assert.That(session.Capabilities.Supports(UsdPhysicsCapability.RigidBodies)).IsTrue();
        await Assert.That(session.Diagnostics.Entries).IsEmpty();
    }

    [Test]
    public async Task TwoConcurrentSeeksAreSerializedNotRaced()
    {
        var backend = new SerializingGuardBackend();
        await using UsdPhysicsSession session = await UsdPhysicsSession.BuildForTestingAsync(null, null, backend);

        Task first = session.SeekAsync(1.0);
        Task second = session.SeekAsync(2.0);
        await Task.WhenAll(first, second);

        await Assert.That(backend.MaxObservedConcurrency).IsEqualTo(1);
    }

    [Test]
    public async Task SeekAndBakeAreSerializedNotRaced()
    {
        var backend = new SerializingGuardBackend();
        await using UsdPhysicsSession session = await UsdPhysicsSession.BuildForTestingAsync(null, null, backend);

        Task seek = session.SeekAsync(1.0);
        Task<UsdPhysicsBakeResult> bake = session.BakeAsync(new UsdPhysicsBakeRequest("/baked.usda", 0, 1, 1.0 / 60));
        await Task.WhenAll(seek, bake);

        await Assert.That(backend.MaxObservedConcurrency).IsEqualTo(1);
    }

    [Test]
    public async Task SeekAndResetAreSerializedNotRaced()
    {
        var backend = new SerializingGuardBackend();
        await using UsdPhysicsSession session = await UsdPhysicsSession.BuildForTestingAsync(null, null, backend);

        Task seek = session.SeekAsync(1.0);
        Task reset = session.ResetAsync();
        await Task.WhenAll(seek, reset);

        await Assert.That(backend.MaxObservedConcurrency).IsEqualTo(1);
    }

    [Test]
    public async Task ConcurrentDisposeCallsAreSerializedAndIdempotent()
    {
        var backend = new SerializingGuardBackend();
        UsdPhysicsSession session = await UsdPhysicsSession.BuildForTestingAsync(null, null, backend);

        ValueTask first = session.DisposeAsync();
        ValueTask second = session.DisposeAsync();
        await first;
        await second;

        await Assert.That(session.State).IsEqualTo(UsdPhysicsSessionState.Disposed);
        await Assert.That(backend.MaxObservedConcurrency).IsEqualTo(1);
        await Assert.That(backend.DisposeCallCount).IsEqualTo(1);
    }

    [Test]
    public async Task ResetSeekAndBakeThrowWhileStepOwnershipIsActive()
    {
        await using UsdPhysicsSession session = await UsdPhysicsSession.BuildForTestingAsync(
            null, null, new UsdPhysicsNotSupportedBackend());
        using UsdPhysicsStepOwnership owner = session.AcquireStepOwnership();

        await Assert.That(() => session.ResetAsync()).Throws<UsdPhysicsStepOwnershipException>();
        await Assert.That(() => session.SeekAsync(1.0)).Throws<UsdPhysicsStepOwnershipException>();

        Task<UsdPhysicsBakeResult> bakeTask =
            session.BakeAsync(new UsdPhysicsBakeRequest("/baked.usda", 0, 1, 1.0 / 60));
        await Assert.That(bakeTask).Throws<UsdPhysicsStepOwnershipException>();
    }

    [Test]
    public async Task StepFromWrongThreadThrows()
    {
        await using UsdPhysicsSession session = await UsdPhysicsSession.BuildForTestingAsync(
            null, null, new UsdPhysicsNotSupportedBackend());
        using UsdPhysicsStepOwnership owner = session.AcquireStepOwnership();

        // A dedicated thread rather than Task.Run: the pool is free to run the work item on the very
        // thread this test is awaiting on, and the step would then correctly arrive on the owning
        // thread and succeed, making the assertion racy rather than wrong.
        Exception? captured = null;
        var stepThread = new Thread(() =>
        {
            try
            {
                _ = session.Step(owner, new UsdPhysicsStepRequest(1.0 / 60));
            }
#pragma warning disable CA1031 // The failure is asserted on the calling thread.
            catch (Exception exception)
#pragma warning restore CA1031
            {
                captured = exception;
            }
        });

        stepThread.Start();
        stepThread.Join();

        await Assert.That(captured).IsTypeOf<UsdPhysicsStepOwnershipException>();
    }

    [Test]
    public async Task StepWithCanceledTokenThrowsBeforeEnteringBackend()
    {
        var backend = new SerializingGuardBackend();
        await using UsdPhysicsSession session = await UsdPhysicsSession.BuildForTestingAsync(null, null, backend);
        using UsdPhysicsStepOwnership owner = session.AcquireStepOwnership();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.That(() => session.Step(owner, new UsdPhysicsStepRequest(1.0 / 60), cts.Token))
            .Throws<OperationCanceledException>();
        await Assert.That(backend.StepCallCount).IsEqualTo(0);
    }

    [Test]
    public async Task StepResultDefensivelyCopiesQueryResults()
    {
        var mutableResults = new List<UsdPhysicsQueryResult> { UsdPhysicsQueryResult.Empty };
        var result = new UsdPhysicsStepResult(
            UsdPhysicsSnapshot.Empty,
            UsdPhysicsEventBatch.Empty,
            mutableResults,
            UsdPhysicsDiagnostics.Empty);

        mutableResults.Add(UsdPhysicsQueryResult.Empty);
        mutableResults.Clear();

        await Assert.That(result.QueryResults.Count).IsEqualTo(1);
    }

    [Test]
    public async Task CapabilitiesSupportsNoneIsAlwaysTrue()
    {
        await Assert.That(UsdPhysicsCapabilities.None.Supports(UsdPhysicsCapability.None)).IsTrue();
        await Assert.That(new UsdPhysicsCapabilities(UsdPhysicsCapability.RigidBodies)
            .Supports(UsdPhysicsCapability.None)).IsTrue();
    }

    [Test]
    public async Task CommandConstructorRejectsOutOfRangeKind()
    {
        await Assert.That(() => _ = new UsdPhysicsCommand((UsdPhysicsCommandKind)(-1), default, default))
            .Throws<ArgumentOutOfRangeException>();
    }

    /// <summary>
    /// Repeatedly races several concurrent <see cref="UsdPhysicsSession.DisposeAsync"/> calls against
    /// one another, using a <see cref="Barrier"/> to align their start so genuine overlap at the
    /// world gate is forced deterministically rather than relying on artificial delays. The world
    /// gate must never itself be disposed (see the disposal-semantics remarks on
    /// <see cref="UsdPhysicsSession.DisposeAsync"/>), so every racer must either complete
    /// successfully (idempotent no-op) with no exception at all -- disposal is never rejected, only
    /// deduplicated -- and exactly one of them must perform the underlying backend disposal.
    /// </summary>
    [Test]
    public async Task ConcurrentDisposeRacesNeverDisposeTheWorldGate()
    {
        const int racerCount = 8;
        for (int iteration = 0; iteration < 100; iteration++)
        {
            var backend = new ImmediatelyCompletingBackend();
            UsdPhysicsSession session = await UsdPhysicsSession.BuildForTestingAsync(null, null, backend);
            using var startGate = new Barrier(racerCount);

            Task[] racers = Enumerable.Range(0, racerCount)
                .Select(_ => Task.Run(async () =>
                {
                    startGate.SignalAndWait();
                    await session.DisposeAsync();
                }))
                .ToArray();

            // No racer may throw: dispose is idempotent, and the gate backing it is never disposed,
            // so a losing racer simply observes the already-disposed state and returns, it never
            // faults on a disposed SemaphoreSlim.
            await Task.WhenAll(racers);

            await Assert.That(session.State).IsEqualTo(UsdPhysicsSessionState.Disposed);
            await Assert.That(backend.DisposeCallCount).IsEqualTo(1);
        }
    }

    /// <summary>
    /// Repeatedly races <see cref="UsdPhysicsSession.ResetAsync"/>, <see cref="UsdPhysicsSession.SeekAsync"/>,
    /// and <see cref="UsdPhysicsSession.BakeAsync"/> against a concurrent <see cref="UsdPhysicsSession.DisposeAsync"/>
    /// call, aligned with a <see cref="Barrier"/> so the race is forced on every iteration instead of
    /// depending on scheduling luck. Whichever operations lose the race must fail with the session's
    /// own <see cref="ObjectDisposedException"/> -- identified by its <c>ObjectName</c> naming
    /// <see cref="UsdPhysicsSession"/> -- and never with an exception thrown by the underlying,
    /// never-disposed <see cref="SemaphoreSlim"/> world gate.
    /// </summary>
    [Test]
    public async Task ConcurrentOperationsRacingDisposeReportSessionDisposedNotSemaphoreDisposed()
    {
        for (int iteration = 0; iteration < 100; iteration++)
        {
            var backend = new ImmediatelyCompletingBackend();
            UsdPhysicsSession session = await UsdPhysicsSession.BuildForTestingAsync(null, null, backend);
            using var startGate = new Barrier(4);
            var observedExceptions = new List<Exception>();
            var observedExceptionsLock = new object();

            void observe(Exception exception)
            {
                lock (observedExceptionsLock)
                {
                    observedExceptions.Add(exception);
                }
            }

            Task racer(Func<Task> operation) => Task.Run(async () =>
            {
                startGate.SignalAndWait();
                try
                {
                    await operation().ConfigureAwait(false);
                }
                catch (Exception exception)
                {
                    observe(exception);
                }
            });

            await Task.WhenAll(
                racer(() => session.DisposeAsync().AsTask()),
                racer(() => session.ResetAsync()),
                racer(() => session.SeekAsync(1.0)),
                racer(() => session.BakeAsync(new UsdPhysicsBakeRequest("/baked.usda", 0, 1, 1.0 / 60))));

            await Assert.That(session.State).IsEqualTo(UsdPhysicsSessionState.Disposed);

            foreach (Exception exception in observedExceptions)
            {
                await Assert.That(exception).IsTypeOf<ObjectDisposedException>();
                var disposedException = (ObjectDisposedException)exception;
                await Assert.That(disposedException.ObjectName).IsEqualTo(typeof(UsdPhysicsSession).FullName);
            }
        }
    }

    /// <summary>
    /// Directly confirms that a lifecycle operation attempted strictly after a session has already
    /// completed disposal fails with an <see cref="ObjectDisposedException"/> whose <c>ObjectName</c>
    /// identifies <see cref="UsdPhysicsSession"/> itself, not the internal world gate, guarding
    /// against a regression back to disposing the gate.
    /// </summary>
    [Test]
    public async Task PostDisposeObjectDisposedExceptionOriginatesFromSessionNotWorldGate()
    {
        var backend = new ImmediatelyCompletingBackend();
        UsdPhysicsSession session = await UsdPhysicsSession.BuildForTestingAsync(null, null, backend);
        await session.DisposeAsync();

        Task resetTask = session.ResetAsync();
        await Assert.That(resetTask).Throws<ObjectDisposedException>();

        try
        {
            await session.SeekAsync(1.0);
            throw new InvalidOperationException("Expected SeekAsync to throw after disposal.");
        }
        catch (ObjectDisposedException disposedException)
        {
            await Assert.That(disposedException.ObjectName).IsEqualTo(typeof(UsdPhysicsSession).FullName);
        }
    }

    /// <summary>
    /// A backend that fails a call as soon as it observes more than one backend method executing
    /// concurrently, used to prove that <see cref="UsdPhysicsSession"/> genuinely serializes
    /// <c>Reset</c>/<c>Seek</c>/<c>Bake</c>/<c>Step</c>/<c>Dispose</c> against one another instead of
    /// merely appearing to by chance. Each guarded call sleeps briefly to widen the window in which
    /// an unserialized caller would be observed.
    /// </summary>
    private sealed class SerializingGuardBackend : IUsdPhysicsBackend
    {
        private int _activeCalls;
        private int _maxObservedConcurrency;
        private int _stepCallCount;
        private int _disposeCallCount;

        internal int MaxObservedConcurrency => Volatile.Read(ref _maxObservedConcurrency);

        internal int StepCallCount => Volatile.Read(ref _stepCallCount);

        internal int DisposeCallCount => Volatile.Read(ref _disposeCallCount);

        public async Task<UsdPhysicsBuildOutcome> BuildAsync(
            UsdStageScheduler? scheduler,
            UsdPhysicsSessionOptions options,
            CancellationToken cancellationToken)
        {
            await GuardedAsync(cancellationToken).ConfigureAwait(false);
            return new UsdPhysicsBuildOutcome(UsdPhysicsCapabilities.None, UsdPhysicsDiagnostics.Empty);
        }

        public Task<UsdPhysicsBuildOutcome> ResetAsync(
            UsdStageScheduler? scheduler,
            UsdPhysicsSessionOptions options,
            CancellationToken cancellationToken) =>
            BuildAsync(scheduler, options, cancellationToken);

        public async Task<UsdPhysicsSnapshot> SeekAsync(double timeCode, CancellationToken cancellationToken)
        {
            await GuardedAsync(cancellationToken).ConfigureAwait(false);
            return new UsdPhysicsSnapshot(1, timeCode, 0, UsdPhysicsDiagnostics.Empty);
        }

        public UsdPhysicsStepResult Step(UsdPhysicsStepRequest request)
        {
            Interlocked.Increment(ref _stepCallCount);
            EnterGuarded();
            try
            {
                Thread.Sleep(20);
                return new UsdPhysicsStepResult(
                    new UsdPhysicsSnapshot(1, request.DeltaSeconds, 1, UsdPhysicsDiagnostics.Empty),
                    UsdPhysicsEventBatch.Empty,
                    [],
                    UsdPhysicsDiagnostics.Empty);
            }
            finally
            {
                ExitGuarded();
            }
        }

        public async Task<UsdPhysicsBakeResult> BakeAsync(
            UsdStageScheduler? scheduler,
            UsdPhysicsBakeRequest request,
            CancellationToken cancellationToken)
        {
            await GuardedAsync(cancellationToken).ConfigureAwait(false);
            return new UsdPhysicsBakeResult(UsdPhysicsBakeStatus.NotSupported, 0, UsdPhysicsDiagnostics.Empty);
        }

        public async ValueTask DisposeAsync()
        {
            Interlocked.Increment(ref _disposeCallCount);
            await GuardedAsync(CancellationToken.None).ConfigureAwait(false);
        }

        private async Task GuardedAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            EnterGuarded();
            try
            {
                await Task.Delay(20, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                ExitGuarded();
            }
        }

        private void EnterGuarded()
        {
            int concurrency = Interlocked.Increment(ref _activeCalls);
            InterlockedMax(ref _maxObservedConcurrency, concurrency);
        }

        private void ExitGuarded() => Interlocked.Decrement(ref _activeCalls);

        private static void InterlockedMax(ref int target, int value)
        {
            int initial;
            do
            {
                initial = Volatile.Read(ref target);
                if (value <= initial)
                {
                    return;
                }
            } while (Interlocked.CompareExchange(ref target, value, initial) != initial);
        }
    }

    private sealed class FakeBackend(UsdPhysicsCapability supported) : IUsdPhysicsBackend
    {
        public Task<UsdPhysicsBuildOutcome> BuildAsync(
            UsdStageScheduler? scheduler,
            UsdPhysicsSessionOptions options,
            CancellationToken cancellationToken) =>
            Task.FromResult(new UsdPhysicsBuildOutcome(
                new UsdPhysicsCapabilities(supported & options.RequestedCapabilities),
                UsdPhysicsDiagnostics.Empty));

        public Task<UsdPhysicsBuildOutcome> ResetAsync(
            UsdStageScheduler? scheduler,
            UsdPhysicsSessionOptions options,
            CancellationToken cancellationToken) =>
            BuildAsync(scheduler, options, cancellationToken);

        public Task<UsdPhysicsSnapshot> SeekAsync(double timeCode, CancellationToken cancellationToken) =>
            Task.FromResult(new UsdPhysicsSnapshot(1, timeCode, 0, UsdPhysicsDiagnostics.Empty));

        public UsdPhysicsStepResult Step(UsdPhysicsStepRequest request) =>
            new(
                new UsdPhysicsSnapshot(1, request.DeltaSeconds, 1, UsdPhysicsDiagnostics.Empty),
                UsdPhysicsEventBatch.Empty,
                [],
                UsdPhysicsDiagnostics.Empty);

        public Task<UsdPhysicsBakeResult> BakeAsync(
            UsdStageScheduler? scheduler,
            UsdPhysicsBakeRequest request,
            CancellationToken cancellationToken) =>
            Task.FromResult(new UsdPhysicsBakeResult(
                UsdPhysicsBakeStatus.Completed,
                1,
                UsdPhysicsDiagnostics.Empty));

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    /// <summary>
    /// A backend whose every method completes immediately with an already-completed task, used for
    /// deterministic concurrency stress tests: with no artificial delay, many racing iterations can
    /// run quickly, and any observed serialization violation or exception is attributable purely to
    /// <see cref="UsdPhysicsSession"/>'s own gating, never to timing-dependent luck.
    /// </summary>
    private sealed class ImmediatelyCompletingBackend : IUsdPhysicsBackend
    {
        private int _disposeCallCount;

        internal int DisposeCallCount => Volatile.Read(ref _disposeCallCount);

        public Task<UsdPhysicsBuildOutcome> BuildAsync(
            UsdStageScheduler? scheduler,
            UsdPhysicsSessionOptions options,
            CancellationToken cancellationToken) =>
            Task.FromResult(new UsdPhysicsBuildOutcome(UsdPhysicsCapabilities.None, UsdPhysicsDiagnostics.Empty));

        public Task<UsdPhysicsBuildOutcome> ResetAsync(
            UsdStageScheduler? scheduler,
            UsdPhysicsSessionOptions options,
            CancellationToken cancellationToken) =>
            BuildAsync(scheduler, options, cancellationToken);

        public Task<UsdPhysicsSnapshot> SeekAsync(double timeCode, CancellationToken cancellationToken) =>
            Task.FromResult(new UsdPhysicsSnapshot(1, timeCode, 0, UsdPhysicsDiagnostics.Empty));

        public UsdPhysicsStepResult Step(UsdPhysicsStepRequest request) =>
            new(
                new UsdPhysicsSnapshot(1, request.DeltaSeconds, 1, UsdPhysicsDiagnostics.Empty),
                UsdPhysicsEventBatch.Empty,
                [],
                UsdPhysicsDiagnostics.Empty);

        public Task<UsdPhysicsBakeResult> BakeAsync(
            UsdStageScheduler? scheduler,
            UsdPhysicsBakeRequest request,
            CancellationToken cancellationToken) =>
            Task.FromResult(new UsdPhysicsBakeResult(
                UsdPhysicsBakeStatus.NotSupported, 0, UsdPhysicsDiagnostics.Empty));

        public ValueTask DisposeAsync()
        {
            Interlocked.Increment(ref _disposeCallCount);
            return ValueTask.CompletedTask;
        }
    }
}
