// Copyright (c) marcschier. Licensed under the MIT License.

using OpenUsd.Physics.Extraction;

namespace OpenUsd.Physics;

/// <summary>
/// Publishes one atomically consistent <see cref="UsdPhysicsTransportStatus"/> without allocating.
/// </summary>
/// <remarks>
/// The cell is a sequence lock. The physics worker brackets every field update with an odd and then
/// an even sequence number, and a reader accepts a snapshot only when the sequence did not change
/// while it was reading. A reader therefore never composes a status out of two different ticks, never
/// blocks the worker, and never causes an allocation on the warm path.
/// </remarks>
internal sealed class UsdPhysicsStatusCell
{
    private int _sequence;
    private int _state;
    private int _queueDepth;
    private ulong _revision;
    private ulong _stepIndex;
    private double _timeCode;
    private double _simulationSeconds;
    private double _backlogSeconds;
    private long _catchUpLimitedTicks;
    private long _droppedPublications;
    private long _loopCount;

    /// <summary>Publishes one complete status snapshot from the owning worker thread.</summary>
    internal void Write(
        UsdPhysicsTransportState state,
        ulong revision,
        ulong stepIndex,
        double timeCode,
        double simulationSeconds,
        double backlogSeconds,
        long catchUpLimitedTicks,
        long droppedPublications,
        long loopCount,
        int queueDepth)
    {
        Interlocked.Increment(ref _sequence);
        Thread.MemoryBarrier();
        _state = (int)state;
        _revision = revision;
        _stepIndex = stepIndex;
        _timeCode = timeCode;
        _simulationSeconds = simulationSeconds;
        _backlogSeconds = backlogSeconds;
        _catchUpLimitedTicks = catchUpLimitedTicks;
        _droppedPublications = droppedPublications;
        _loopCount = loopCount;
        _queueDepth = queueDepth;
        Interlocked.Increment(ref _sequence);
    }

    /// <summary>Reads one complete status snapshot from any thread.</summary>
    /// <remarks>
    /// The barriers matter on weakly ordered architectures such as ARM64: without them the payload
    /// reads may be observed out of order with respect to the sequence reads that validate them, and
    /// the reader could accept a torn snapshot that the writer never published.
    /// </remarks>
    internal UsdPhysicsTransportStatus Read()
    {
        while (true)
        {
            int start = Volatile.Read(ref _sequence);
            if ((start & 1) != 0)
            {
                Thread.SpinWait(1);
                continue;
            }

            Thread.MemoryBarrier();
            var status = new UsdPhysicsTransportStatus(
                (UsdPhysicsTransportState)Volatile.Read(ref _state),
                Volatile.Read(ref _revision),
                Volatile.Read(ref _stepIndex),
                Volatile.Read(ref _timeCode),
                Volatile.Read(ref _simulationSeconds),
                Volatile.Read(ref _backlogSeconds),
                Volatile.Read(ref _catchUpLimitedTicks),
                Volatile.Read(ref _droppedPublications),
                Volatile.Read(ref _loopCount),
                Volatile.Read(ref _queueDepth));

            Thread.MemoryBarrier();
            if (Volatile.Read(ref _sequence) == start)
            {
                return status;
            }
        }
    }
}

/// <summary>
/// Advances a retained physics world at a fixed timestep on a dedicated worker thread and publishes
/// complete simulation frames to consumers without ever blocking them.
/// </summary>
/// <remarks>
/// <para>
/// <b>Thread ownership.</b> Exactly one thread ever touches the retained world, the tick scheduler,
/// and the checkpoint cache. Every lifecycle operation - build, reset, seek, play, pause, loop, and
/// invalidate - is a request placed on a bounded queue and executed on that one thread, which is what
/// serializes lifecycle work against stepping without a single lock on the warm path.
/// </para>
/// <para>
/// <b>Fixed timestep.</b> The step is derived from the stage's authored <c>timeCodesPerSecond</c>,
/// or from an explicit <see cref="UsdPhysicsSessionOptions.FixedFrequencyOverrideHz"/>, and is then
/// clamped into 24-240 Hz. A clamp is never silent: it is reported through
/// <see cref="Diagnostics"/> and through <see cref="UsdPhysicsFixedStep.WasClamped"/>.
/// </para>
/// <para>
/// <b>Catch-up.</b> A tick advances at most <see cref="UsdPhysicsTransportOptions.MaxCatchUpSubSteps"/>
/// fixed sub-steps. Wall-clock time beyond that stays in the backlog, so a host that stalls sees
/// playback slow down; simulated time is never skipped and physics is never dropped.
/// </para>
/// <para>
/// <b>Publication.</b> Frames are published through a bounded triple buffer. The latest complete frame
/// always wins, consumers never block and never observe a partially written frame, and a consumer that
/// holds every buffer only costs a redundant intermediate frame, counted by
/// <see cref="UsdPhysicsTransportStatus.DroppedPublications"/>.
/// </para>
/// <para>
/// <b>Allocation.</b> Once built, the warm step and publication path performs no managed allocation:
/// frames, the scratch frame, the checkpoint ring, and the status cell are all preallocated and reused.
/// </para>
/// </remarks>
public sealed class UsdPhysicsTransport : IAsyncDisposable, IUsdPhysicsWorkerHost
{
    /// <summary>Reported when a physics-relevant edit invalidated the built world.</summary>
    public const string InvalidatedDiagnosticCode = "OPENUSD_PHYSICS_TRANSPORT_INVALIDATED";

    /// <summary>Reported when seeking replays canonically because checkpoints are not replay-equivalent.</summary>
    public const string CanonicalReplayDiagnosticCode = "OPENUSD_PHYSICS_TRANSPORT_CANONICAL_REPLAY";

    private readonly UsdPhysicsStatusCell _status = new();
    private readonly IUsdPhysicsWorld _world;
    private readonly IUsdPhysicsClock _clock;
    private readonly UsdPhysicsWorker _worker;

    private UsdPhysicsFramePublisher? _publisher;
    private UsdPhysicsFrame? _scratch;
    private UsdPhysicsTickScheduler? _tick;
    private UsdPhysicsCheckpointCache? _checkpoints;
    private UsdPhysicsDiagnostics _diagnostics = UsdPhysicsDiagnostics.Empty;
    private int _capabilityFeatures;
    private bool _replayEquivalentCheckpoints;
    private bool _loop;
    private int _state;
    private int _disposed;

    private UsdPhysicsTransport(
        IUsdPhysicsWorld world,
        UsdPhysicsTimeline timeline,
        UsdPhysicsTransportOptions options,
        IUsdPhysicsClock clock,
        bool useDedicatedThread)
    {
        _world = world;
        _clock = clock;
        Options = options;
        Timeline = timeline;
        FixedStep = UsdPhysicsFixedStep.Resolve(timeline, options.Session.FixedFrequencyOverrideHz);
        _loop = options.Loop;
        _worker = new UsdPhysicsWorker(
            this,
            options.RequestQueueCapacity,
            options.TickIntervalMilliseconds,
            useDedicatedThread);
        if (FixedStep.WasClamped && FixedStep.CreateClampDiagnostic() is { } clamped)
        {
            _diagnostics = new UsdPhysicsDiagnostics([clamped]);
        }
        PublishStatus();
    }

    /// <summary>Gets the options this transport was created with.</summary>
    public UsdPhysicsTransportOptions Options { get; }

    /// <summary>Gets the authored timeline the fixed step and the loop range are derived from.</summary>
    public UsdPhysicsTimeline Timeline { get; }

    /// <summary>Gets the resolved and clamped fixed simulation step.</summary>
    public UsdPhysicsFixedStep FixedStep { get; }

    /// <summary>Gets the capabilities the built world actually provides.</summary>
    public UsdPhysicsCapabilities Capabilities =>
        new((UsdPhysicsCapability)Volatile.Read(ref _capabilityFeatures));

    /// <summary>Gets every diagnostic produced by the most recent lifecycle operation.</summary>
    public UsdPhysicsDiagnostics Diagnostics => Volatile.Read(ref _diagnostics);

    /// <summary>Gets one atomically consistent snapshot of transport progress.</summary>
    public UsdPhysicsTransportStatus Status => _status.Read();

    /// <summary>Gets a value indicating whether playback wraps at the authored end time code.</summary>
    public bool Loop => Volatile.Read(ref _loop);

    /// <summary>Gets a value indicating whether seeking may be accelerated by retained checkpoints.</summary>
    /// <remarks>
    /// Checkpoint acceleration is used only for a world that proves restoring a checkpoint reproduces
    /// the trajectory a canonical replay produces. Every other world replays from the authored start.
    /// </remarks>
    public bool UsesCheckpointAcceleration => Volatile.Read(ref _replayEquivalentCheckpoints);

    /// <summary>Creates a transport bound to the retained native physics world.</summary>
    /// <param name="scheduler">The scheduler owning the stage the authored timeline is read from.</param>
    /// <param name="options">The transport options, or <see langword="null"/> for the defaults.</param>
    /// <param name="cancellationToken">Cancels reading the authored timeline.</param>
    public static async ValueTask<UsdPhysicsTransport> CreateAsync(
        UsdStageScheduler scheduler,
        UsdPhysicsTransportOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scheduler);
        options ??= UsdPhysicsTransportOptions.Default;

        UsdPhysicsTimeline timeline =
            await UsdPhysicsTimeline.ReadAsync(scheduler, cancellationToken).ConfigureAwait(false);

        var world = new UsdPhysicsNativeWorld();
        try
        {
            var transport = new UsdPhysicsTransport(
                world,
                timeline,
                options,
                new UsdPhysicsStopwatchClock(),
                useDedicatedThread: true);
            transport._worker.Start();
            return transport;
        }
        catch
        {
            world.Dispose();
            throw;
        }
    }

    /// <summary>Creates a transport over an injected world and clock, without a dedicated thread.</summary>
    /// <remarks>
    /// The calling thread becomes the owning thread and drives the worker through <see cref="Pump"/>,
    /// which makes every timing, catch-up, loop, and publication rule exactly reproducible in tests
    /// while preserving the single-owner-thread invariant.
    /// </remarks>
    internal static UsdPhysicsTransport CreateForTesting(
        IUsdPhysicsWorld world,
        UsdPhysicsTimeline timeline,
        UsdPhysicsTransportOptions options,
        IUsdPhysicsClock clock)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(clock);

        var transport = new UsdPhysicsTransport(world, timeline, options, clock, useDedicatedThread: false);
        transport._worker.Start();
        return transport;
    }

    /// <summary>Drains queued requests and performs one bounded tick on the calling thread.</summary>
    internal bool Pump() => _worker.Pump();

    /// <summary>Builds or rebuilds the retained world.</summary>
    /// <param name="cancellationToken">Cancels the build before or while it runs.</param>
    public Task BuildAsync(CancellationToken cancellationToken = default) =>
        EnqueueAsync(new UsdPhysicsWorkItem(UsdPhysicsRequestKind.Build, cancellationToken: cancellationToken));

    /// <summary>
    /// Attaches the extracted stage the next build composes its simulation content from.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The extraction is produced on the stage owner's thread and the build runs on the physics
    /// worker, so the page is handed over as a queued request rather than written into the world
    /// directly. Attaching therefore lands strictly before any build queued after it, which is what
    /// makes "extract, attach, build" compose the stage the caller actually extracted.
    /// </para>
    /// <para>
    /// A world with nothing attached builds authored timeline metadata only and reports that as a
    /// diagnostic. That is a usable state - a transport can exist before a stage is extracted - but
    /// it simulates no authored body, so a host that wants a simulation must attach a page.
    /// </para>
    /// </remarks>
    /// <param name="page">The extracted stage, or <see langword="null"/> to detach.</param>
    /// <param name="cancellationToken">Cancels the request before it runs.</param>
    public Task AttachExtractionAsync(
        UsdPhysicsExtractionPage? page,
        CancellationToken cancellationToken = default) =>
        EnqueueAsync(new UsdPhysicsWorkItem(
            UsdPhysicsRequestKind.AttachExtraction,
            extraction: page,
            cancellationToken: cancellationToken));

    /// <summary>Returns the world to the authored start time code.</summary>
    /// <remarks>
    /// A world that was invalidated or faulted is rebuilt rather than rewound, because its retained
    /// state can no longer be trusted to match the authored stage.
    /// </remarks>
    /// <param name="cancellationToken">Cancels the reset before or while it runs.</param>
    public Task ResetAsync(CancellationToken cancellationToken = default) =>
        EnqueueAsync(new UsdPhysicsWorkItem(UsdPhysicsRequestKind.Reset, cancellationToken: cancellationToken));

    /// <summary>Moves the world to an authored time code.</summary>
    /// <param name="timeCode">The authored time code to seek to; clamped into the authored range.</param>
    /// <param name="cancellationToken">Cancels the seek before or while it replays.</param>
    public Task SeekAsync(double timeCode, CancellationToken cancellationToken = default)
    {
        if (!double.IsFinite(timeCode))
        {
            throw new ArgumentOutOfRangeException(nameof(timeCode), timeCode, "The seek target must be finite.");
        }

        return EnqueueAsync(new UsdPhysicsWorkItem(
            UsdPhysicsRequestKind.Seek,
            timeCode,
            cancellationToken: cancellationToken));
    }

    /// <summary>Advances the world by an explicit number of fixed sub-steps.</summary>
    /// <remarks>
    /// <para>
    /// Explicit stepping exists for a host that paces playback itself: an interactive viewer converts
    /// wall-clock time and a playback speed into whole fixed sub-steps and requests exactly those, so
    /// the speed changes how often the world advances and never changes the fixed step it advances
    /// by. It is also the single-frame step a transport control exposes.
    /// </para>
    /// <para>
    /// The request advances in bounded chunks of
    /// <see cref="UsdPhysicsTransportOptions.MaxCatchUpSubSteps"/>, publishes a frame per chunk, and
    /// obeys the authored end exactly like a timed tick: it wraps when <see cref="Loop"/> is set and
    /// otherwise stops in <see cref="UsdPhysicsTransportState.Ended"/>.
    /// </para>
    /// </remarks>
    /// <param name="steps">The number of fixed sub-steps to advance; must be positive.</param>
    /// <param name="cancellationToken">Cancels the request before it runs or between chunks.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="steps"/> is not positive.</exception>
    public Task StepAsync(int steps, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(steps);
        return EnqueueAsync(new UsdPhysicsWorkItem(
            UsdPhysicsRequestKind.Step,
            steps,
            cancellationToken: cancellationToken));
    }

    /// <summary>Begins advancing the world at the fixed step.</summary>
    /// <param name="cancellationToken">Cancels the request before it runs.</param>
    public Task PlayAsync(CancellationToken cancellationToken = default) =>
        EnqueueAsync(new UsdPhysicsWorkItem(UsdPhysicsRequestKind.Play, cancellationToken: cancellationToken));

    /// <summary>
    /// Stages one batch of runtime commands, applied before the next advance's first sub-step.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the only interactive control path: forces and impulses, velocity replacement, sleep
    /// and wake, scene gravity, character controller displacement, and vehicle driver input all
    /// arrive as one batch rather than as one interop call per input. A host that produces an input
    /// per pointer event therefore still costs the world exactly one transition per step.
    /// </para>
    /// <para>
    /// Staging never advances the world. A command submitted while playback is paused is applied by
    /// the next step the host requests, which is what keeps single-stepping an interaction possible.
    /// A world that is unbuilt, invalidated, or faulted refuses the whole batch and says so rather
    /// than silently dropping it.
    /// </para>
    /// </remarks>
    /// <param name="commands">The commands to stage, in submission order.</param>
    /// <param name="cancellationToken">Cancels the request before it runs.</param>
    /// <returns>What the world accepted and what it refused.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="commands"/> is null.</exception>
    public async Task<UsdPhysicsCommandSubmission> SubmitCommandsAsync(
        IReadOnlyList<UsdPhysicsCommand> commands,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(commands);
        if (commands.Count == 0)
        {
            return UsdPhysicsCommandSubmission.Empty;
        }

        var item = new UsdPhysicsWorkItem(
            UsdPhysicsRequestKind.Commands,
            commands: commands,
            cancellationToken: cancellationToken);
        await EnqueueAsync(item).ConfigureAwait(false);
        UsdPhysicsCommandStaging staging = item.Staging;
        return new UsdPhysicsCommandSubmission(
            staging.Accepted,
            staging.Rejected,
            staging.Message.Length == 0
                ? "The runtime command batch produced no outcome."
                : staging.Message);
    }

    /// <summary>Stops advancing the world without discarding it.</summary>
    /// <param name="cancellationToken">Cancels the request before it runs.</param>
    public Task PauseAsync(CancellationToken cancellationToken = default) =>
        EnqueueAsync(new UsdPhysicsWorkItem(UsdPhysicsRequestKind.Pause, cancellationToken: cancellationToken));

    /// <summary>Changes whether playback wraps from the authored end back to the authored start.</summary>
    /// <param name="loop">Whether playback wraps.</param>
    /// <param name="cancellationToken">Cancels the request before it runs.</param>
    public Task SetLoopAsync(bool loop, CancellationToken cancellationToken = default) =>
        EnqueueAsync(new UsdPhysicsWorkItem(
            UsdPhysicsRequestKind.SetLoop,
            flag: loop,
            cancellationToken: cancellationToken));

    /// <summary>Marks the built world stale because a physics-relevant edit changed the stage.</summary>
    /// <remarks>
    /// This is the contract-level invalidation hook. Retained physics extraction does not exist yet, so
    /// nothing classifies authored edits automatically; a host that already knows an edit is
    /// physics-relevant calls this, and the transport pauses, marks itself
    /// <see cref="UsdPhysicsTransportState.Invalidated"/>, and drops every retained checkpoint.
    /// </remarks>
    /// <param name="reason">Why the world was invalidated.</param>
    /// <param name="cancellationToken">Cancels the request before it runs.</param>
    public Task InvalidateAsync(
        UsdPhysicsInvalidationReason reason,
        CancellationToken cancellationToken = default) =>
        EnqueueAsync(new UsdPhysicsWorkItem(
            UsdPhysicsRequestKind.Invalidate,
            reason: reason,
            cancellationToken: cancellationToken));

    /// <summary>Pins the latest complete published frame without blocking the physics worker.</summary>
    /// <param name="lease">The lease pinning the frame; dispose it as soon as the frame is consumed.</param>
    /// <returns>
    /// <see langword="false"/> when nothing has been published yet, or when
    /// <see cref="UsdPhysicsTransportOptions.MaxConcurrentFrameLeases"/> leases are already live and
    /// undisposed. Acquisition never blocks and never allocates, so a refused acquisition means the
    /// caller should retry after disposing the leases it holds.
    /// </returns>
    public bool TryAcquireLatestFrame(out UsdPhysicsFrameLease lease)
    {
        UsdPhysicsFramePublisher? publisher = Volatile.Read(ref _publisher);
        if (publisher is null)
        {
            lease = default;
            return false;
        }

        return publisher.TryAcquire(out lease);
    }

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        if (_worker.HasDedicatedThread)
        {
            try
            {
                await _worker.EnqueueAsync(new UsdPhysicsWorkItem(UsdPhysicsRequestKind.Shutdown))
                    .ConfigureAwait(false);
            }
            catch (UsdPhysicsTransportQueueFullException)
            {
                // The worker is saturated; disposing the worker below still releases the world.
            }
            catch (UsdPhysicsTransportStateException)
            {
                // The worker already stopped.
            }
            catch (ObjectDisposedException)
            {
                // The worker already stopped.
            }
        }
        else
        {
            ExecuteShutdown();
        }

        _worker.Dispose();
        _world.Dispose();
        Volatile.Write(ref _state, (int)UsdPhysicsTransportState.Disposed);
        PublishStatus();
    }

    void IUsdPhysicsWorkerHost.Execute(UsdPhysicsWorkItem item)
    {
        switch (item.Kind)
        {
            case UsdPhysicsRequestKind.Build:
                ExecuteBuild(item.CancellationToken);
                break;
            case UsdPhysicsRequestKind.Reset:
                ExecuteReset(item.CancellationToken);
                break;
            case UsdPhysicsRequestKind.Seek:
                ExecuteSeek(item.Value, item.CancellationToken);
                break;
            case UsdPhysicsRequestKind.Step:
                ExecuteStep(item.Steps, item.CancellationToken);
                break;
            case UsdPhysicsRequestKind.Play:
                ExecutePlay(item.CancellationToken);
                break;
            case UsdPhysicsRequestKind.Pause:
                ExecutePause();
                break;
            case UsdPhysicsRequestKind.SetLoop:
                Volatile.Write(ref _loop, item.Flag);
                break;
            case UsdPhysicsRequestKind.Invalidate:
                ExecuteInvalidate(item.Reason);
                break;
            case UsdPhysicsRequestKind.Commands:
                ExecuteStageCommands(item);
                break;
            case UsdPhysicsRequestKind.AttachExtraction:
                _world.AttachExtraction(item.Extraction);
                break;
            case UsdPhysicsRequestKind.Shutdown:
                ExecuteShutdown();
                break;
            default:
                throw new UsdPhysicsTransportStateException(CurrentState);
        }

        PublishStatus();
    }

    bool IUsdPhysicsWorkerHost.Tick()
    {
        if (CurrentState != UsdPhysicsTransportState.Playing
            || _tick is not { } scheduler
            || _publisher is not { } publisher
            || _scratch is not { } scratch)
        {
            return false;
        }

        UsdPhysicsTickPlan plan = scheduler.Plan(_clock.NextElapsedSeconds());
        if (plan.SubSteps > 0)
        {
            UsdPhysicsFrame? claimed = publisher.TryClaimWriteBuffer();
            UsdPhysicsFrame target = claimed ?? scratch;
            if (!_world.TryStep(FixedStep.Seconds, plan.SubSteps, target))
            {
                if (claimed is not null)
                {
                    UsdPhysicsFramePublisher.Abandon(claimed);
                }

                Fault();
                return false;
            }

            scheduler.Commit(in plan);
            target.StepIndex = scheduler.StepIndex;
            target.SimulationSeconds = scheduler.SimulationSeconds;
            target.TimeCode = scheduler.TimeCode;
            target.SubStepCount = plan.SubSteps;
            target.BacklogSeconds = scheduler.BacklogSeconds;
            if (claimed is not null)
            {
                publisher.Publish(claimed);
            }

            if (_checkpoints is { IsEnabled: true } checkpoints
                && checkpoints.ShouldCapture(scheduler.StepIndex))
            {
                checkpoints.TryCapture(_world, scheduler.StepIndex);
            }
        }
        else
        {
            scheduler.Commit(in plan);
        }

        if (plan.ReachedEnd)
        {
            if (Volatile.Read(ref _loop))
            {
                _world.ResetToStart();
                scheduler.CompleteLoop();
            }
            else
            {
                scheduler.CompleteWithoutLoop();
                Volatile.Write(ref _state, (int)UsdPhysicsTransportState.Ended);
            }
        }

        PublishStatus();
        return CurrentState == UsdPhysicsTransportState.Playing
            && scheduler.BacklogSeconds >= FixedStep.Seconds;
    }

    private UsdPhysicsTransportState CurrentState =>
        (UsdPhysicsTransportState)Volatile.Read(ref _state);

    private Task EnqueueAsync(UsdPhysicsWorkItem item)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        return _worker.EnqueueAsync(item);
    }

    private void ExecuteStageCommands(UsdPhysicsWorkItem item)
    {
        IReadOnlyList<UsdPhysicsCommand> commands = item.Commands ?? [];
        UsdPhysicsTransportState state = CurrentState;
        if (state is UsdPhysicsTransportState.Invalidated
            or UsdPhysicsTransportState.Faulted
            or UsdPhysicsTransportState.Unbuilt
            or UsdPhysicsTransportState.Building
            or UsdPhysicsTransportState.Disposed)
        {
            // Staging into a world that is not built, or one whose retained state no longer matches
            // the authored stage, would push an input at whatever object inherits the identity next.
            item.Staging = new UsdPhysicsCommandStaging(
                0,
                commands.Count,
                "The world is not in a state that accepts runtime commands.");
            return;
        }

        item.Staging = _world.StageCommands(commands);
        PublishDiagnostics(_world.DrainDiagnostics());
    }

    private void ExecuteBuild(CancellationToken cancellationToken)
    {
        // A build that never commits must leave the transport exactly where it was, so the state
        // it is about to leave is remembered before anything is torn down.
        UsdPhysicsTransportState previous = CurrentState;
        bool wasReady = previous is UsdPhysicsTransportState.Paused or UsdPhysicsTransportState.Playing
            && _tick is not null && _publisher is not null;

        Volatile.Write(ref _state, (int)UsdPhysicsTransportState.Building);
        PublishStatus();

        _publisher?.Invalidate();
        _checkpoints?.Clear();

        UsdPhysicsWorldBuildResult result;
        try
        {
            result = _world.Build(Timeline, FixedStep, Options.Session, cancellationToken);
        }
        catch
        {
            // The world build is transactional, so a cancelled or throwing build left the world it
            // had already built untouched. The transport returns to the state that world was in,
            // and only a transport that never had a world becomes faulted.
            Volatile.Write(
                ref _state,
                (int)(wasReady ? UsdPhysicsTransportState.Paused : UsdPhysicsTransportState.Faulted));
            PublishDiagnostics(_world.DrainDiagnostics());
            PublishStatus();
            if (wasReady)
            {
                PublishCurrentFrame();
            }

            throw;
        }

        PublishDiagnostics(result.Diagnostics);

        if (!result.Succeeded)
        {
            // A rejected build leaves the previously built world intact, so a transport that had
            // one keeps stepping it rather than losing it to a stage edit that cannot compose.
            // Its capabilities are left alone for the same reason: the world that is still running
            // still provides them, and a rejected build reports none of its own. Only a transport
            // with no world left to describe drops to none.
            Volatile.Write(
                ref _state,
                (int)(wasReady ? UsdPhysicsTransportState.Paused : UsdPhysicsTransportState.Faulted));
            if (wasReady)
            {
                PublishCurrentFrame();
            }
            else
            {
                Volatile.Write(ref _capabilityFeatures, (int)result.Capabilities.Features);
                Volatile.Write(
                    ref _replayEquivalentCheckpoints,
                    result.SupportsReplayEquivalentCheckpoints);
            }

            return;
        }

        Volatile.Write(ref _capabilityFeatures, (int)result.Capabilities.Features);
        Volatile.Write(ref _replayEquivalentCheckpoints, result.SupportsReplayEquivalentCheckpoints);

        int capacity = Math.Max(result.BodyCapacity, 0);

        // The publication revision is documented as monotonic, and a consumer uses it to decide
        // whether the frame it holds is still the newest one. A new publisher therefore continues
        // the sequence the retired one reached rather than restarting at zero, which would make a
        // frame published after a rebuild indistinguishable from one published before it.
        ulong revisionSeed = Volatile.Read(ref _publisher)?.Revision ?? 0;
        Volatile.Write(
            ref _publisher,
            new UsdPhysicsFramePublisher(
                capacity,
                Math.Max(result.DeformationCapacity, 0),
                Math.Max(result.DeformationVertexCapacity, 0),
                UsdPhysicsTransportOptions.PublicationBufferCount,
                Options.MaxConcurrentFrameLeases,
                revisionSeed));
        _scratch = new UsdPhysicsFrame(
            capacity,
            Math.Max(result.DeformationCapacity, 0),
            Math.Max(result.DeformationVertexCapacity, 0));
        _tick = new UsdPhysicsTickScheduler(Timeline, FixedStep, Options.MaxCatchUpSubSteps);
        _checkpoints = new UsdPhysicsCheckpointCache(
            Options.Session.CheckpointInterval,
            Options.Session.MaxCheckpoints,
            capacity,
            result.SupportsReplayEquivalentCheckpoints);

        Volatile.Write(ref _state, (int)UsdPhysicsTransportState.Paused);
        PublishCurrentFrame();
    }

    private void ExecuteReset(CancellationToken cancellationToken)
    {
        UsdPhysicsTransportState state = CurrentState;
        if (state is UsdPhysicsTransportState.Unbuilt
            or UsdPhysicsTransportState.Invalidated
            or UsdPhysicsTransportState.Faulted
            || _tick is null)
        {
            ExecuteBuild(cancellationToken);
            return;
        }

        Volatile.Write(ref _state, (int)UsdPhysicsTransportState.Building);
        PublishStatus();

        _world.ResetToStart();
        _tick.ResetToStart();
        _checkpoints?.Clear();
        _clock.Restart();
        PublishDiagnostics(_world.DrainDiagnostics());

        Volatile.Write(ref _state, (int)UsdPhysicsTransportState.Paused);
        PublishCurrentFrame();
    }

    private void ExecuteSeek(double timeCode, CancellationToken cancellationToken)
    {
        if (_tick is not { } scheduler || _scratch is not { } scratch)
        {
            throw new UsdPhysicsTransportStateException(CurrentState);
        }

        UsdPhysicsTransportState state = CurrentState;
        if (state is UsdPhysicsTransportState.Invalidated
            or UsdPhysicsTransportState.Faulted
            or UsdPhysicsTransportState.Disposed)
        {
            throw new UsdPhysicsTransportStateException(state);
        }

        Volatile.Write(ref _state, (int)UsdPhysicsTransportState.Building);
        PublishStatus();

        ulong target = scheduler.StepsToTimeCode(timeCode);
        ulong current = 0;
        bool usedCheckpoint = false;
        if (_checkpoints is { IsEnabled: true } checkpoints
            && checkpoints.TryRestore(_world, target, FixedStep.Seconds, out ulong restored))
        {
            current = restored;
            usedCheckpoint = true;
        }
        else
        {
            _world.ResetToStart();
        }

        int chunkLimit = Options.MaxCatchUpSubSteps;
        try
        {
            while (current < target)
            {
                cancellationToken.ThrowIfCancellationRequested();
                ulong remaining = target - current;
                int chunk = remaining > (ulong)chunkLimit ? chunkLimit : (int)remaining;
                if (!_world.TryStep(FixedStep.Seconds, chunk, scratch))
                {
                    Fault();
                    throw new UsdPhysicsTransportStateException(UsdPhysicsTransportState.Faulted);
                }

                current += (ulong)chunk;
                if (_checkpoints is { IsEnabled: true } cache && cache.ShouldCapture(current))
                {
                    cache.TryCapture(_world, current);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // A partially replayed world is not a valid transport position, so the cancellation
            // leaves the transport at the authored start rather than at an arbitrary sub-step.
            _world.ResetToStart();
            scheduler.ResetToStart();
            _clock.Restart();
            Volatile.Write(ref _state, (int)UsdPhysicsTransportState.Paused);
            PublishCurrentFrame();
            throw;
        }

        scheduler.SeekToStep(target);
        _clock.Restart();
        UsdPhysicsDiagnostics seekDiagnostics = _world.DrainDiagnostics();
        if (!usedCheckpoint && target > 0)
        {
            var entries = new List<UsdPhysicsDiagnostic>(seekDiagnostics.Entries.Count + 1)
            {
                new(
                    UsdPhysicsDiagnosticSeverity.Information,
                    UsdPhysicsDiagnosticCategory.Seek,
                    CanonicalReplayDiagnosticCode,
                    $"The seek replayed {target} fixed steps from the authored start because retained " +
                    "checkpoints are unavailable or are not proven replay-equivalent for this world.")
            };
            entries.AddRange(seekDiagnostics.Entries);
            seekDiagnostics = new UsdPhysicsDiagnostics(entries);
        }

        PublishDiagnostics(seekDiagnostics);
        Volatile.Write(ref _state, (int)UsdPhysicsTransportState.Paused);
        PublishCurrentFrame();
    }

    private void ExecuteStep(int steps, CancellationToken cancellationToken)
    {
        if (_tick is not { } scheduler || _publisher is not { } publisher || _scratch is not { } scratch)
        {
            throw new UsdPhysicsTransportStateException(CurrentState);
        }

        UsdPhysicsTransportState state = CurrentState;
        if (state is not (UsdPhysicsTransportState.Paused or UsdPhysicsTransportState.Ended))
        {
            throw new UsdPhysicsTransportStateException(state);
        }

        if (state == UsdPhysicsTransportState.Ended)
        {
            if (!Volatile.Read(ref _loop))
            {
                return;
            }

            _world.ResetToStart();
            scheduler.CompleteLoop();
            Volatile.Write(ref _state, (int)UsdPhysicsTransportState.Paused);
        }

        int remaining = steps;
        while (remaining > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            UsdPhysicsTickPlan plan = scheduler.PlanSteps(remaining);
            if (plan.SubSteps > 0)
            {
                UsdPhysicsFrame? claimed = publisher.TryClaimWriteBuffer();
                UsdPhysicsFrame target = claimed ?? scratch;
                if (!_world.TryStep(FixedStep.Seconds, plan.SubSteps, target))
                {
                    if (claimed is not null)
                    {
                        UsdPhysicsFramePublisher.Abandon(claimed);
                    }

                    Fault();
                    return;
                }

                scheduler.CommitSteps(in plan);
                target.StepIndex = scheduler.StepIndex;
                target.SimulationSeconds = scheduler.SimulationSeconds;
                target.TimeCode = scheduler.TimeCode;
                target.SubStepCount = plan.SubSteps;
                target.BacklogSeconds = scheduler.BacklogSeconds;
                if (claimed is not null)
                {
                    publisher.Publish(claimed);
                }

                if (_checkpoints is { IsEnabled: true } checkpoints
                    && checkpoints.ShouldCapture(scheduler.StepIndex))
                {
                    checkpoints.TryCapture(_world, scheduler.StepIndex);
                }
            }

            // A plan that advanced nothing still consumes one requested step so a degenerate authored
            // range - one whose duration is shorter than a single fixed step - can never spin here.
            remaining -= Math.Max(plan.SubSteps, 1);
            if (!plan.ReachedEnd)
            {
                continue;
            }

            if (Volatile.Read(ref _loop))
            {
                _world.ResetToStart();
                scheduler.CompleteLoop();
                continue;
            }

            scheduler.CompleteWithoutLoop();
            Volatile.Write(ref _state, (int)UsdPhysicsTransportState.Ended);
            return;
        }
    }

    private void ExecutePlay(CancellationToken cancellationToken)
    {
        UsdPhysicsTransportState state = CurrentState;
        if (state is UsdPhysicsTransportState.Unbuilt
            or UsdPhysicsTransportState.Invalidated
            or UsdPhysicsTransportState.Faulted
            or UsdPhysicsTransportState.Disposed)
        {
            throw new UsdPhysicsTransportStateException(state);
        }

        if (state == UsdPhysicsTransportState.Ended)
        {
            ExecuteReset(cancellationToken);
        }

        _tick?.DiscardBacklog();
        _clock.Restart();
        Volatile.Write(ref _state, (int)UsdPhysicsTransportState.Playing);
    }

    private void ExecutePause()
    {
        if (CurrentState != UsdPhysicsTransportState.Playing)
        {
            return;
        }

        _tick?.DiscardBacklog();
        Volatile.Write(ref _state, (int)UsdPhysicsTransportState.Paused);
    }

    private void ExecuteInvalidate(UsdPhysicsInvalidationReason reason)
    {
        if (CurrentState == UsdPhysicsTransportState.Disposed)
        {
            return;
        }

        _checkpoints?.Clear();
        _tick?.DiscardBacklog();
        _world.DiscardStagedCommands();
        Volatile.Write(ref _state, (int)UsdPhysicsTransportState.Invalidated);
        PublishDiagnostics(new UsdPhysicsDiagnostics([
            new UsdPhysicsDiagnostic(
                UsdPhysicsDiagnosticSeverity.Warning,
                UsdPhysicsDiagnosticCategory.Build,
                InvalidatedDiagnosticCode,
                $"A physics-relevant change of kind '{reason}' invalidated the built world; reset the " +
                "transport to rebuild it from the authored stage.")
        ]));
    }

    private void ExecuteShutdown()
    {
        _publisher?.Invalidate();
        _checkpoints?.Clear();
        _world.Dispose();
        Volatile.Write(ref _state, (int)UsdPhysicsTransportState.Disposed);
    }

    private void PublishCurrentFrame()
    {
        if (_publisher is not { } publisher || _tick is not { } scheduler)
        {
            return;
        }

        UsdPhysicsFrame? claimed = publisher.TryClaimWriteBuffer();
        if (claimed is null)
        {
            return;
        }

        if (!_world.TryFetch(claimed))
        {
            claimed.SetBodyCount(0);
        }

        claimed.StepIndex = scheduler.StepIndex;
        claimed.SimulationSeconds = scheduler.SimulationSeconds;
        claimed.TimeCode = scheduler.TimeCode;
        claimed.SubStepCount = 0;
        claimed.BacklogSeconds = scheduler.BacklogSeconds;
        publisher.Publish(claimed);
        PublishStatus();
    }

    private void Fault()
    {
        PublishDiagnostics(_world.DrainDiagnostics());
        Volatile.Write(ref _state, (int)UsdPhysicsTransportState.Faulted);
        PublishStatus();
    }

    private void PublishDiagnostics(UsdPhysicsDiagnostics diagnostics)
    {
        UsdPhysicsDiagnostic? clamped = FixedStep.CreateClampDiagnostic();
        if (clamped is null)
        {
            Volatile.Write(ref _diagnostics, diagnostics);
            return;
        }

        var merged = new List<UsdPhysicsDiagnostic>(diagnostics.Entries.Count + 1) { clamped };
        merged.AddRange(diagnostics.Entries);
        Volatile.Write(ref _diagnostics, new UsdPhysicsDiagnostics(merged));
    }

    private void PublishStatus()
    {
        UsdPhysicsFramePublisher? publisher = Volatile.Read(ref _publisher);
        UsdPhysicsTickScheduler? scheduler = _tick;
        _status.Write(
            CurrentState,
            publisher?.Revision ?? 0,
            scheduler?.StepIndex ?? 0,
            scheduler?.TimeCode ?? Timeline.StartTimeCode,
            scheduler?.SimulationSeconds ?? 0,
            scheduler?.BacklogSeconds ?? 0,
            scheduler?.CatchUpLimitedTicks ?? 0,
            publisher?.DroppedPublications ?? 0,
            scheduler?.LoopCount ?? 0,
            _worker.QueueDepth);
    }
}
