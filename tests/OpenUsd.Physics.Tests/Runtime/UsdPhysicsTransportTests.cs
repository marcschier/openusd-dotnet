// Copyright (c) marcschier. Licensed under the MIT License.

namespace OpenUsd.Physics.Tests;

public sealed class UsdPhysicsTransportTests
{
    private const double TimeCodesPerSecond = 24.0;
    private const double EndTimeCode = 24.0;
    private const double FrequencyHz = 60.0;
    private const double StepSeconds = 1.0 / FrequencyHz;

    private static UsdPhysicsTransportOptions CreateOptions(
        bool loop = false,
        int queueCapacity = 64,
        int checkpointInterval = 0,
        int maxCheckpoints = 0,
        int maxSubStepsPerTick = 8) =>
        new(
            new UsdPhysicsSessionOptions(
                maxSubStepsPerTick: maxSubStepsPerTick,
                fixedFrequencyOverrideHz: FrequencyHz,
                checkpointInterval: checkpointInterval,
                maxCheckpoints: maxCheckpoints),
            loop,
            queueCapacity);

    private static UsdPhysicsTransport CreateTransport(
        FakePhysicsWorld world,
        FakePhysicsClock clock,
        UsdPhysicsTransportOptions? options = null,
        double endTimeCode = EndTimeCode) =>
        UsdPhysicsTransport.CreateForTesting(
            world,
            new UsdPhysicsTimeline(TimeCodesPerSecond, 0, endTimeCode),
            options ?? CreateOptions(),
            clock);

    private static async Task RunAsync(UsdPhysicsTransport transport, Task request)
    {
        transport.Pump();
        await request;
    }

    private static async Task<Exception?> CaptureAsync(UsdPhysicsTransport transport, Task request)
    {
        transport.Pump();
        try
        {
            await request;
            return null;
        }
        catch (Exception exception)
        {
            return exception;
        }
    }

    [Test]
    public async Task WorldWorkOnlyHappensOnThePumpingThread()
    {
        var world = new FakePhysicsWorld();
        var clock = new FakePhysicsClock();
        await using UsdPhysicsTransport transport = CreateTransport(world, clock);

        Task build = transport.BuildAsync();
        await Assert.That(world.BuildCount).IsEqualTo(0);

        await RunAsync(transport, build);
        await Assert.That(world.BuildCount).IsEqualTo(1);
    }

    [Test]
    public async Task BuildPublishesAnInitialPausedFrame()
    {
        var world = new FakePhysicsWorld();
        var clock = new FakePhysicsClock();
        await using UsdPhysicsTransport transport = CreateTransport(world, clock);

        await RunAsync(transport, transport.BuildAsync());

        UsdPhysicsTransportStatus status = transport.Status;
        await Assert.That(status.State).IsEqualTo(UsdPhysicsTransportState.Paused);
        await Assert.That(status.StepIndex).IsEqualTo(0ul);
        await Assert.That(status.TimeCode).IsEqualTo(0.0);
        await Assert.That(transport.Capabilities.Supports(UsdPhysicsCapability.RigidBodies)).IsTrue();
        await Assert.That(transport.FixedStep.FrequencyHz).IsEqualTo(FrequencyHz);

        await Assert.That(transport.TryAcquireLatestFrame(out UsdPhysicsFrameLease lease)).IsTrue();
        using (lease)
        {
            await Assert.That(lease.Frame.BodyCount).IsEqualTo(4);
            await Assert.That(lease.Frame.StepIndex).IsEqualTo(0ul);
        }
    }

    [Test]
    public async Task FailedBuildFaultsTheTransport()
    {
        var world = new FakePhysicsWorld { BuildSucceeds = false };
        var clock = new FakePhysicsClock();
        await using UsdPhysicsTransport transport = CreateTransport(world, clock);

        await RunAsync(transport, transport.BuildAsync());

        await Assert.That(transport.Status.State).IsEqualTo(UsdPhysicsTransportState.Faulted);
        await Assert.That(transport.Capabilities).IsEqualTo(UsdPhysicsCapabilities.None);
    }

    [Test]
    public async Task FailedRebuildKeepsThePreviouslyBuiltWorldPlayable()
    {
        var world = new FakePhysicsWorld();
        var clock = new FakePhysicsClock();
        await using UsdPhysicsTransport transport = CreateTransport(world, clock);
        await RunAsync(transport, transport.BuildAsync());
        await RunAsync(transport, transport.StepAsync(3));

        world.BuildSucceeds = false;
        await RunAsync(transport, transport.BuildAsync());

        // The build never committed, so the transport keeps the world it already had rather than
        // faulting into a state that can only publish an empty frame.
        await Assert.That(transport.Status.State).IsEqualTo(UsdPhysicsTransportState.Paused);
        await Assert.That(world.Counter).IsEqualTo(3ul);

        await RunAsync(transport, transport.PlayAsync());
        clock.Advance(2 * StepSeconds);
        transport.Pump();

        UsdPhysicsTransportStatus status = transport.Status;
        await Assert.That(status.State).IsEqualTo(UsdPhysicsTransportState.Playing);
        await Assert.That(world.Counter).IsEqualTo(5ul);
    }

    [Test]
    public async Task ThrowingRebuildKeepsThePreviouslyBuiltWorldPlayable()
    {
        var world = new FakePhysicsWorld();
        var clock = new FakePhysicsClock();
        await using UsdPhysicsTransport transport = CreateTransport(world, clock);
        await RunAsync(transport, transport.BuildAsync());
        await RunAsync(transport, transport.StepAsync(2));

        world.BuildThrows = new InvalidOperationException("the stage cannot be composed");
        Exception? failure = await CaptureAsync(transport, transport.BuildAsync());
        await Assert.That(failure).IsNotNull();

        // A build that throws must not strand the transport in Building, which would leave every
        // later request rejected while the world underneath is still perfectly usable.
        await Assert.That(transport.Status.State).IsEqualTo(UsdPhysicsTransportState.Paused);

        world.BuildThrows = null;
        await RunAsync(transport, transport.StepAsync(1));
        await Assert.That(world.Counter).IsEqualTo(3ul);
    }

    [Test]
    public async Task CancelledRebuildKeepsThePreviouslyBuiltWorldPlayable()
    {
        var world = new FakePhysicsWorld();
        var clock = new FakePhysicsClock();
        await using UsdPhysicsTransport transport = CreateTransport(world, clock);
        await RunAsync(transport, transport.BuildAsync());
        await RunAsync(transport, transport.StepAsync(4));

        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        Exception? failure = await CaptureAsync(transport, transport.BuildAsync(cancellation.Token));
        await Assert.That(failure).IsTypeOf<TaskCanceledException>();

        await Assert.That(transport.Status.State).IsEqualTo(UsdPhysicsTransportState.Paused);
        await RunAsync(transport, transport.StepAsync(1));
        await Assert.That(world.Counter).IsEqualTo(5ul);
    }

    [Test]
    public async Task FirstBuildThatThrowsFaultsTheTransportAndForbidsPlay()
    {
        var world = new FakePhysicsWorld
        {
            BuildThrows = new InvalidOperationException("the stage cannot be composed")
        };
        var clock = new FakePhysicsClock();
        await using UsdPhysicsTransport transport = CreateTransport(world, clock);

        Exception? failure = await CaptureAsync(transport, transport.BuildAsync());
        await Assert.That(failure).IsNotNull();

        // There is no world to preserve, so the transport must fault instead of pretending it is
        // paused on a world that was never created.
        await Assert.That(transport.Status.State).IsEqualTo(UsdPhysicsTransportState.Faulted);

        world.BuildThrows = null;
        world.BuildSucceeds = false;
        Exception? refused = await CaptureAsync(transport, transport.PlayAsync());
        await Assert.That(refused).IsTypeOf<UsdPhysicsTransportStateException>();
        await Assert.That(transport.Status.State).IsEqualTo(UsdPhysicsTransportState.Faulted);
    }

    [Test]
    public async Task PlayAdvancesAtTheFixedStep()
    {
        var world = new FakePhysicsWorld();
        var clock = new FakePhysicsClock();
        await using UsdPhysicsTransport transport = CreateTransport(world, clock);
        await RunAsync(transport, transport.BuildAsync());
        await RunAsync(transport, transport.PlayAsync());

        clock.Advance(3 * StepSeconds);
        transport.Pump();

        UsdPhysicsTransportStatus status = transport.Status;
        await Assert.That(status.State).IsEqualTo(UsdPhysicsTransportState.Playing);
        await Assert.That(status.StepIndex).IsEqualTo(3ul);
        await Assert.That(status.SimulationSeconds).IsEqualTo(3 * StepSeconds);
        await Assert.That(world.SubStepsAdvanced).IsEqualTo(3L);

        await Assert.That(transport.TryAcquireLatestFrame(out UsdPhysicsFrameLease lease)).IsTrue();
        using (lease)
        {
            await Assert.That(lease.Frame.StepIndex).IsEqualTo(3ul);
            await Assert.That(lease.Frame.SubStepCount).IsEqualTo(3);
            await Assert.That(lease.Frame.Bodies[0].Position.Y).IsEqualTo(3.0);
        }
    }

    [Test]
    public async Task CatchUpIsBoundedAndSlowsDownWithoutSkippingPhysics()
    {
        var world = new FakePhysicsWorld();
        var clock = new FakePhysicsClock();
        await using UsdPhysicsTransport transport = CreateTransport(world, clock);
        await RunAsync(transport, transport.BuildAsync());
        await RunAsync(transport, transport.PlayAsync());

        clock.Advance(20 * StepSeconds);
        transport.Pump();

        UsdPhysicsTransportStatus limited = transport.Status;
        await Assert.That(limited.StepIndex).IsEqualTo(8ul);
        await Assert.That(limited.CatchUpLimitedTicks).IsEqualTo(1L);
        await Assert.That(limited.BacklogSeconds).IsGreaterThan(0.0);

        for (int tick = 0; tick < 4; tick++)
        {
            transport.Pump();
        }

        UsdPhysicsTransportStatus caughtUp = transport.Status;
        await Assert.That(caughtUp.StepIndex).IsEqualTo(20ul);
        await Assert.That(world.SubStepsAdvanced).IsEqualTo(20L);
        await Assert.That(caughtUp.BacklogSeconds).IsLessThan(StepSeconds);
    }

    [Test]
    public async Task ReachingTheAuthoredEndStopsPlaybackWhenLoopingIsDisabled()
    {
        var world = new FakePhysicsWorld();
        var clock = new FakePhysicsClock();
        await using UsdPhysicsTransport transport = CreateTransport(world, clock);
        await RunAsync(transport, transport.BuildAsync());
        await RunAsync(transport, transport.PlayAsync());

        clock.Advance(5.0);
        for (int tick = 0; tick < 32; tick++)
        {
            transport.Pump();
            if (transport.Status.State != UsdPhysicsTransportState.Playing)
            {
                break;
            }
        }

        UsdPhysicsTransportStatus status = transport.Status;
        await Assert.That(status.State).IsEqualTo(UsdPhysicsTransportState.Ended);
        await Assert.That(status.StepIndex).IsEqualTo(60ul);
        await Assert.That(status.TimeCode).IsEqualTo(EndTimeCode);
        await Assert.That(status.LoopCount).IsEqualTo(0L);
        await Assert.That(status.BacklogSeconds).IsEqualTo(0.0);
    }

    [Test]
    public async Task LoopingResetsToTheAuthoredStartWithoutStopping()
    {
        var world = new FakePhysicsWorld();
        var clock = new FakePhysicsClock();
        await using UsdPhysicsTransport transport = CreateTransport(world, clock, CreateOptions(loop: true));
        await RunAsync(transport, transport.BuildAsync());
        await RunAsync(transport, transport.PlayAsync());

        clock.Advance(1.5);
        for (int tick = 0; tick < 32; tick++)
        {
            transport.Pump();
            if (transport.Status.LoopCount > 0)
            {
                break;
            }
        }

        UsdPhysicsTransportStatus status = transport.Status;
        await Assert.That(status.LoopCount).IsEqualTo(1L);
        await Assert.That(status.State).IsEqualTo(UsdPhysicsTransportState.Playing);
        await Assert.That(status.StepIndex).IsEqualTo(0ul);
        await Assert.That(world.ResetCount).IsGreaterThan(0);
        await Assert.That(world.SubStepsAdvanced).IsEqualTo(60L);
    }

    [Test]
    public async Task SetLoopChangesWrappingBehaviorAtRuntime()
    {
        var world = new FakePhysicsWorld();
        var clock = new FakePhysicsClock();
        await using UsdPhysicsTransport transport = CreateTransport(world, clock);
        await RunAsync(transport, transport.BuildAsync());

        await Assert.That(transport.Loop).IsFalse();
        await RunAsync(transport, transport.SetLoopAsync(true));
        await Assert.That(transport.Loop).IsTrue();
    }

    [Test]
    public async Task PauseStopsSteppingAndDropsTheBacklog()
    {
        var world = new FakePhysicsWorld();
        var clock = new FakePhysicsClock();
        await using UsdPhysicsTransport transport = CreateTransport(world, clock);
        await RunAsync(transport, transport.BuildAsync());
        await RunAsync(transport, transport.PlayAsync());

        clock.Advance(20 * StepSeconds);
        transport.Pump();
        await RunAsync(transport, transport.PauseAsync());

        long advanced = world.SubStepsAdvanced;
        clock.Advance(1.0);
        transport.Pump();

        await Assert.That(transport.Status.State).IsEqualTo(UsdPhysicsTransportState.Paused);
        await Assert.That(world.SubStepsAdvanced).IsEqualTo(advanced);
        await Assert.That(transport.Status.BacklogSeconds).IsEqualTo(0.0);
    }

    [Test]
    public async Task ExplicitStepsAdvanceExactlyOneFixedStepEachAndIgnoreWallClock()
    {
        var world = new FakePhysicsWorld();
        var clock = new FakePhysicsClock();
        await using UsdPhysicsTransport transport = CreateTransport(world, clock);
        await RunAsync(transport, transport.BuildAsync());

        // A viewer that paces playback itself asks for whole steps, so an accumulated wall-clock
        // backlog must never turn one requested step into several.
        clock.Advance(10 * StepSeconds);
        await RunAsync(transport, transport.StepAsync(1));

        UsdPhysicsTransportStatus status = transport.Status;
        await Assert.That(status.State).IsEqualTo(UsdPhysicsTransportState.Paused);
        await Assert.That(status.StepIndex).IsEqualTo(1ul);
        await Assert.That(world.SubStepsAdvanced).IsEqualTo(1L);
        await Assert.That(status.SimulationSeconds).IsEqualTo(StepSeconds);

        await RunAsync(transport, transport.StepAsync(3));
        await Assert.That(transport.Status.StepIndex).IsEqualTo(4ul);
        await Assert.That(world.SubStepsAdvanced).IsEqualTo(4L);
    }

    [Test]
    public async Task ExplicitStepsPublishAFrameTheRendererCanConsume()
    {
        var world = new FakePhysicsWorld();
        var clock = new FakePhysicsClock();
        await using UsdPhysicsTransport transport = CreateTransport(world, clock);
        await RunAsync(transport, transport.BuildAsync());

        await RunAsync(transport, transport.StepAsync(2));

        await Assert.That(transport.TryAcquireLatestFrame(out UsdPhysicsFrameLease lease)).IsTrue();
        using (lease)
        {
            await Assert.That(lease.Frame.StepIndex).IsEqualTo(2ul);
            await Assert.That(lease.Frame.Bodies[0].Position.Y).IsEqualTo(2.0);
        }
    }

    [Test]
    public async Task ExplicitStepsAreRefusedWhilePlaybackOwnsTheWorld()
    {
        var world = new FakePhysicsWorld();
        var clock = new FakePhysicsClock();
        await using UsdPhysicsTransport transport = CreateTransport(world, clock);
        await RunAsync(transport, transport.BuildAsync());
        await RunAsync(transport, transport.PlayAsync());

        Exception? failure = await CaptureAsync(transport, transport.StepAsync(1));

        await Assert.That(failure).IsTypeOf<UsdPhysicsTransportStateException>();
    }

    [Test]
    public async Task ExplicitStepsStopAtTheAuthoredEndUnlessLoopingIsEnabled()
    {
        var world = new FakePhysicsWorld();
        var clock = new FakePhysicsClock();
        await using UsdPhysicsTransport transport = CreateTransport(
            world,
            clock,
            CreateOptions(maxSubStepsPerTick: 64),
            endTimeCode: 1.0);
        await RunAsync(transport, transport.BuildAsync());

        await RunAsync(transport, transport.StepAsync(1000));
        await Assert.That(transport.Status.State).IsEqualTo(UsdPhysicsTransportState.Ended);

        long advanced = world.SubStepsAdvanced;
        await RunAsync(transport, transport.StepAsync(4));
        await Assert.That(world.SubStepsAdvanced).IsEqualTo(advanced);

        await RunAsync(transport, transport.SetLoopAsync(true));
        await RunAsync(transport, transport.StepAsync(2));

        await Assert.That(transport.Status.State).IsEqualTo(UsdPhysicsTransportState.Paused);
        await Assert.That(transport.Status.StepIndex).IsEqualTo(2ul);
    }

    [Test]
    public async Task ResetReturnsToTheAuthoredStart()
    {
        var world = new FakePhysicsWorld();
        var clock = new FakePhysicsClock();
        await using UsdPhysicsTransport transport = CreateTransport(world, clock);
        await RunAsync(transport, transport.BuildAsync());
        await RunAsync(transport, transport.PlayAsync());

        clock.Advance(8 * StepSeconds);
        transport.Pump();
        await RunAsync(transport, transport.ResetAsync());

        UsdPhysicsTransportStatus status = transport.Status;
        await Assert.That(status.State).IsEqualTo(UsdPhysicsTransportState.Paused);
        await Assert.That(status.StepIndex).IsEqualTo(0ul);
        await Assert.That(world.Counter).IsEqualTo(0ul);
        await Assert.That(world.BuildCount).IsEqualTo(1);
    }

    [Test]
    public async Task SeekReplaysCanonicallyWhenCheckpointsAreNotReplayEquivalent()
    {
        var world = new FakePhysicsWorld { ReplayEquivalentCheckpoints = false };
        var clock = new FakePhysicsClock();
        await using UsdPhysicsTransport transport = CreateTransport(
            world,
            clock,
            CreateOptions(checkpointInterval: 10, maxCheckpoints: 8));
        await RunAsync(transport, transport.BuildAsync());

        await Assert.That(transport.UsesCheckpointAcceleration).IsFalse();
        await RunAsync(transport, transport.SeekAsync(12));

        UsdPhysicsTransportStatus status = transport.Status;
        await Assert.That(status.StepIndex).IsEqualTo(30ul);
        await Assert.That(status.TimeCode).IsEqualTo(12.0);
        await Assert.That(world.Counter).IsEqualTo(30ul);
        await Assert.That(world.RestoreCount).IsEqualTo(0);
        await Assert.That(transport.Diagnostics.Entries.Any(
            entry => entry.Code == UsdPhysicsTransport.CanonicalReplayDiagnosticCode)).IsTrue();
    }

    [Test]
    public async Task SeekUsesCheckpointsOnlyForReplayEquivalentWorlds()
    {
        var world = new FakePhysicsWorld { ReplayEquivalentCheckpoints = true };
        var clock = new FakePhysicsClock();
        await using UsdPhysicsTransport transport = CreateTransport(
            world,
            clock,
            CreateOptions(checkpointInterval: 10, maxCheckpoints: 8));
        await RunAsync(transport, transport.BuildAsync());
        await RunAsync(transport, transport.PlayAsync());

        await Assert.That(transport.UsesCheckpointAcceleration).IsTrue();

        clock.Advance(40 * StepSeconds);
        for (int tick = 0; tick < 8; tick++)
        {
            transport.Pump();
        }

        await Assert.That(transport.Status.StepIndex).IsEqualTo(40ul);
        await Assert.That(world.CaptureCount).IsGreaterThan(0);

        long stepCallsBefore = world.StepCalls;
        await RunAsync(transport, transport.SeekAsync(12));

        await Assert.That(world.RestoreCount).IsEqualTo(1);
        await Assert.That(transport.Status.StepIndex).IsEqualTo(30ul);
        await Assert.That(world.Counter).IsEqualTo(30ul);
        await Assert.That(world.StepCalls - stepCallsBefore).IsLessThan(30);
        await Assert.That(transport.Diagnostics.Entries.Any(
            entry => entry.Code == UsdPhysicsTransport.CanonicalReplayDiagnosticCode)).IsFalse();
    }

    [Test]
    public async Task SeekReproducesTheCanonicalReplayState()
    {
        var replayWorld = new FakePhysicsWorld();
        var replayClock = new FakePhysicsClock();
        await using (UsdPhysicsTransport replay = CreateTransport(replayWorld, replayClock))
        {
            await RunAsync(replay, replay.BuildAsync());
            await RunAsync(replay, replay.PlayAsync());
            replayClock.Advance(30 * StepSeconds);
            for (int tick = 0; tick < 8; tick++)
            {
                replay.Pump();
            }
        }

        var seekWorld = new FakePhysicsWorld();
        var seekClock = new FakePhysicsClock();
        await using UsdPhysicsTransport transport = CreateTransport(seekWorld, seekClock);
        await RunAsync(transport, transport.BuildAsync());
        await RunAsync(transport, transport.SeekAsync(12));

        await Assert.That(seekWorld.Counter).IsEqualTo(replayWorld.Counter);
    }

    [Test]
    public async Task SeekIsCancellableAndLeavesADefinedPosition()
    {
        var world = new FakePhysicsWorld();
        var clock = new FakePhysicsClock();
        await using UsdPhysicsTransport transport = CreateTransport(
            world,
            clock,
            CreateOptions(),
            endTimeCode: 24000);
        await RunAsync(transport, transport.BuildAsync());

        using var cancellation = new CancellationTokenSource();
        world.OnStep = _ => cancellation.Cancel();

        Exception? failure = await CaptureAsync(transport, transport.SeekAsync(12000, cancellation.Token));

        await Assert.That(failure is OperationCanceledException).IsTrue();
        await Assert.That(world.StepCalls).IsEqualTo(1);
        await Assert.That(transport.Status.State).IsEqualTo(UsdPhysicsTransportState.Paused);
        await Assert.That(transport.Status.StepIndex).IsEqualTo(0ul);
        await Assert.That(world.Counter).IsEqualTo(0ul);
    }

    [Test]
    public async Task ARequestCancelledBeforeExecutionNeverTouchesTheWorld()
    {
        var world = new FakePhysicsWorld();
        var clock = new FakePhysicsClock();
        await using UsdPhysicsTransport transport = CreateTransport(world, clock);
        await RunAsync(transport, transport.BuildAsync());

        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        Exception? failure = await CaptureAsync(transport, transport.SeekAsync(12, cancellation.Token));

        await Assert.That(failure is OperationCanceledException).IsTrue();
        await Assert.That(world.StepCalls).IsEqualTo(0);
        await Assert.That(transport.Status.StepIndex).IsEqualTo(0ul);
    }

    [Test]
    public async Task InvalidationPausesDropsCheckpointsAndRequiresAReset()
    {
        var world = new FakePhysicsWorld { ReplayEquivalentCheckpoints = true };
        var clock = new FakePhysicsClock();
        await using UsdPhysicsTransport transport = CreateTransport(
            world,
            clock,
            CreateOptions(checkpointInterval: 5, maxCheckpoints: 8));
        await RunAsync(transport, transport.BuildAsync());
        await RunAsync(transport, transport.PlayAsync());
        clock.Advance(20 * StepSeconds);
        transport.Pump();

        await RunAsync(transport, transport.InvalidateAsync(UsdPhysicsInvalidationReason.PhysicsEdit));

        await Assert.That(transport.Status.State).IsEqualTo(UsdPhysicsTransportState.Invalidated);
        await Assert.That(transport.Diagnostics.Entries.Any(
            entry => entry.Code == UsdPhysicsTransport.InvalidatedDiagnosticCode)).IsTrue();

        Exception? failure = await CaptureAsync(transport, transport.PlayAsync());
        await Assert.That(failure).IsTypeOf<UsdPhysicsTransportStateException>();

        await RunAsync(transport, transport.ResetAsync());
        await Assert.That(transport.Status.State).IsEqualTo(UsdPhysicsTransportState.Paused);
        await Assert.That(world.BuildCount).IsEqualTo(2);
    }

    [Test]
    public async Task ARejectedStepFaultsTheTransport()
    {
        var world = new FakePhysicsWorld();
        var clock = new FakePhysicsClock();
        await using UsdPhysicsTransport transport = CreateTransport(world, clock);
        await RunAsync(transport, transport.BuildAsync());
        await RunAsync(transport, transport.PlayAsync());

        world.FailNextStep = true;
        clock.Advance(StepSeconds);
        transport.Pump();

        await Assert.That(transport.Status.State).IsEqualTo(UsdPhysicsTransportState.Faulted);
        await Assert.That(transport.Status.StepIndex).IsEqualTo(0ul);
    }

    [Test]
    public async Task PlayingBeforeBuildingIsRejected()
    {
        var world = new FakePhysicsWorld();
        var clock = new FakePhysicsClock();
        await using UsdPhysicsTransport transport = CreateTransport(world, clock);

        Exception? failure = await CaptureAsync(transport, transport.PlayAsync());
        await Assert.That(failure).IsTypeOf<UsdPhysicsTransportStateException>();
        await Assert.That(transport.Status.State).IsEqualTo(UsdPhysicsTransportState.Unbuilt);
    }

    [Test]
    public async Task PlayingAfterTheEndRestartsFromTheAuthoredStart()
    {
        var world = new FakePhysicsWorld();
        var clock = new FakePhysicsClock();
        await using UsdPhysicsTransport transport = CreateTransport(world, clock);
        await RunAsync(transport, transport.BuildAsync());
        await RunAsync(transport, transport.PlayAsync());

        clock.Advance(5.0);
        for (int tick = 0; tick < 32 && transport.Status.State == UsdPhysicsTransportState.Playing; tick++)
        {
            transport.Pump();
        }

        await Assert.That(transport.Status.State).IsEqualTo(UsdPhysicsTransportState.Ended);

        await RunAsync(transport, transport.PlayAsync());
        await Assert.That(transport.Status.State).IsEqualTo(UsdPhysicsTransportState.Playing);
        await Assert.That(transport.Status.StepIndex).IsEqualTo(0ul);
        await Assert.That(world.Counter).IsEqualTo(0ul);
    }

    [Test]
    public async Task TheRequestQueueIsBounded()
    {
        var world = new FakePhysicsWorld();
        var clock = new FakePhysicsClock();
        await using UsdPhysicsTransport transport = CreateTransport(
            world,
            clock,
            CreateOptions(queueCapacity: 1));

        Task build = transport.BuildAsync();
        await Assert.That(() => { _ = transport.PauseAsync(); })
            .Throws<UsdPhysicsTransportQueueFullException>();

        await RunAsync(transport, build);
        await Assert.That(transport.Status.State).IsEqualTo(UsdPhysicsTransportState.Paused);
    }

    [Test]
    public async Task LifecycleRequestsAreExecutedInOrderOnOnePump()
    {
        var world = new FakePhysicsWorld();
        var clock = new FakePhysicsClock();
        await using UsdPhysicsTransport transport = CreateTransport(world, clock);

        Task build = transport.BuildAsync();
        Task play = transport.PlayAsync();
        Task pause = transport.PauseAsync();

        transport.Pump();
        await Task.WhenAll(build, play, pause);

        await Assert.That(world.BuildCount).IsEqualTo(1);
        await Assert.That(transport.Status.State).IsEqualTo(UsdPhysicsTransportState.Paused);
    }

    [Test]
    public async Task PublicationIsBoundedAndDropsRatherThanBlocks()
    {
        var world = new FakePhysicsWorld();
        var clock = new FakePhysicsClock();
        await using UsdPhysicsTransport transport = CreateTransport(world, clock);
        await RunAsync(transport, transport.BuildAsync());
        await RunAsync(transport, transport.PlayAsync());

        clock.Advance(StepSeconds);
        transport.Pump();
        transport.TryAcquireLatestFrame(out UsdPhysicsFrameLease first);
        clock.Advance(StepSeconds);
        transport.Pump();
        transport.TryAcquireLatestFrame(out UsdPhysicsFrameLease second);
        clock.Advance(StepSeconds);
        transport.Pump();
        transport.TryAcquireLatestFrame(out UsdPhysicsFrameLease third);

        clock.Advance(StepSeconds);
        transport.Pump();

        await Assert.That(transport.Status.DroppedPublications).IsGreaterThan(0L);
        await Assert.That(transport.Status.StepIndex).IsEqualTo(4ul);
        await Assert.That(world.SubStepsAdvanced).IsEqualTo(4L);

        first.Dispose();
        second.Dispose();
        third.Dispose();
    }

    [Test]
    public async Task DisposeReleasesTheWorld()
    {
        var world = new FakePhysicsWorld();
        var clock = new FakePhysicsClock();
        UsdPhysicsTransport transport = CreateTransport(world, clock);
        await RunAsync(transport, transport.BuildAsync());

        await transport.DisposeAsync();
        await transport.DisposeAsync();

        await Assert.That(world.IsDisposed).IsTrue();
        await Assert.That(transport.Status.State).IsEqualTo(UsdPhysicsTransportState.Disposed);
        await Assert.That(() => { _ = transport.BuildAsync(); }).Throws<ObjectDisposedException>();
    }

    [Test]
    public async Task ClampedFixedStepIsAlwaysDiagnosed()
    {
        var world = new FakePhysicsWorld();
        var clock = new FakePhysicsClock();
        var options = new UsdPhysicsTransportOptions(
            new UsdPhysicsSessionOptions(fixedFrequencyOverrideHz: 1000));
        await using UsdPhysicsTransport transport = UsdPhysicsTransport.CreateForTesting(
            world,
            new UsdPhysicsTimeline(TimeCodesPerSecond, 0, EndTimeCode),
            options,
            clock);

        await Assert.That(transport.FixedStep.WasClamped).IsTrue();
        await Assert.That(transport.Diagnostics.Entries.Any(
            entry => entry.Code == UsdPhysicsFixedStep.ClampedDiagnosticCode)).IsTrue();

        await RunAsync(transport, transport.BuildAsync());
        await Assert.That(transport.Diagnostics.Entries.Any(
            entry => entry.Code == UsdPhysicsFixedStep.ClampedDiagnosticCode)).IsTrue();
        await Assert.That(transport.FixedStep.FrequencyHz)
            .IsEqualTo(UsdPhysicsFixedStep.MaximumFrequencyHz);
    }

    [Test]
    public async Task NonFiniteSeekTargetsAreRejected()
    {
        var world = new FakePhysicsWorld();
        var clock = new FakePhysicsClock();
        await using UsdPhysicsTransport transport = CreateTransport(world, clock);

        await Assert.That(() => { _ = transport.SeekAsync(double.NaN); })
            .Throws<ArgumentOutOfRangeException>();
    }

    [Test]
    public async Task TheWarmStepAndPublicationPathDoesNotAllocate()
    {
        var world = new FakePhysicsWorld();
        var clock = new FakePhysicsClock();
        await using UsdPhysicsTransport transport = CreateTransport(world, clock, CreateOptions(loop: true));
        await RunAsync(transport, transport.BuildAsync());
        await RunAsync(transport, transport.PlayAsync());

        for (int warmup = 0; warmup < 256; warmup++)
        {
            clock.Advance(StepSeconds);
            transport.Pump();
            if (transport.TryAcquireLatestFrame(out UsdPhysicsFrameLease warmupLease))
            {
                warmupLease.Dispose();
            }

            _ = transport.Status;
        }

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int tick = 0; tick < 512; tick++)
        {
            clock.Advance(StepSeconds);
            transport.Pump();
            if (transport.TryAcquireLatestFrame(out UsdPhysicsFrameLease lease))
            {
                lease.Dispose();
            }

            _ = transport.Status;
        }

        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        await Assert.That(allocated).IsEqualTo(0L);
        await Assert.That(world.SubStepsAdvanced).IsGreaterThan(512L);
    }

    [Test]
    public async Task PublishedFramesCarryStableIdentitiesAndPoses()
    {
        var world = new FakePhysicsWorld(3);
        var clock = new FakePhysicsClock();
        await using UsdPhysicsTransport transport = CreateTransport(world, clock);
        await RunAsync(transport, transport.BuildAsync());
        await RunAsync(transport, transport.PlayAsync());

        clock.Advance(2 * StepSeconds);
        transport.Pump();

        await Assert.That(transport.TryAcquireLatestFrame(out UsdPhysicsFrameLease lease)).IsTrue();
        using (lease)
        {
            UsdPhysicsBodyPose[] bodies = lease.Frame.Bodies.ToArray();
            bool overflow = lease.Frame.HasOverflow;
            bool truncated = lease.Frame.BodiesTruncated;

            await Assert.That(bodies.Length).IsEqualTo(3);
            for (int index = 0; index < bodies.Length; index++)
            {
                await Assert.That(bodies[index].Id.Value).IsEqualTo((ulong)index + 1);
                await Assert.That(bodies[index].Id.Kind).IsEqualTo(UsdPhysicsObjectKind.RigidBody);
                await Assert.That(bodies[index].Position.Y).IsEqualTo(2.0);
                await Assert.That(bodies[index].Orientation).IsEqualTo(UsdPhysicsOrientation.Identity);
            }

            await Assert.That(overflow).IsFalse();
            await Assert.That(truncated).IsFalse();
        }
    }
}
