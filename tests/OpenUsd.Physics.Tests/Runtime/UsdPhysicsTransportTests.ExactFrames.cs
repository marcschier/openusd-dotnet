// Copyright (c) marcschier. Licensed under the MIT License.

namespace OpenUsd.Physics.Tests;

public sealed partial class UsdPhysicsTransportTests
{
    [Test]
    [Arguments(0)]
    [Arguments(3)]
    public async Task ExactStepDoesNotRoundAPartialFinalStepPastTheAuthoredEnd(int completed)
    {
        var world = new FakePhysicsWorld();
        await using UsdPhysicsTransport transport = CreateTransport(world, new FakePhysicsClock(),
            endTimeCode: (completed + 0.5) * StepSeconds * TimeCodesPerSecond);
        await RunAsync(transport, transport.BuildAsync());
        if (completed > 0)
        {
            Task<UsdPhysicsFrameLease> first = transport.StepAndAcquireFrameAsync(completed);
            transport.Pump();
            using UsdPhysicsFrameLease firstFrame = await first;
            await Assert.That(firstFrame.Frame.StepIndex).IsEqualTo((ulong)completed);
        }
        Exception? failure = await CaptureAsync(transport, transport.StepAndAcquireFrameAsync(1));
        await Assert.That(failure).IsTypeOf<ArgumentOutOfRangeException>();
        await Assert.That(world.Counter).IsEqualTo((ulong)completed);
        await Assert.That(transport.Status.StepIndex).IsEqualTo((ulong)completed);
        await Assert.That(transport.Status.State).IsEqualTo(UsdPhysicsTransportState.Paused);
    }

    [Test]
    public async Task ExactStepKeepsFaultStateAndReleasesReservationsWhenDiagnosticsAlsoFail()
    {
        var world = new FakePhysicsWorld();
        var options = new UsdPhysicsTransportOptions(CreateOptions().Session, maxConcurrentFrameLeases: 1);
        await using UsdPhysicsTransport transport = CreateTransport(world, new FakePhysicsClock(), options);
        await RunAsync(transport, transport.BuildAsync());
        var solverFailure = new InvalidOperationException("solver failure");
        var diagnosticFailure = new IOException("diagnostic failure");
        world.OnStep = _ => throw solverFailure;
        world.DiagnosticFailure = diagnosticFailure;
        Exception? failure = await CaptureAsync(transport, transport.StepAndAcquireFrameAsync(1));
        await Assert.That(failure).IsTypeOf<AggregateException>();
        var aggregate = (AggregateException)failure!;
        await Assert.That(aggregate.InnerExceptions.Count).IsEqualTo(2);
        await Assert.That(aggregate.InnerExceptions[0]).IsSameReferenceAs(solverFailure);
        await Assert.That(aggregate.InnerExceptions[1]).IsSameReferenceAs(diagnosticFailure);
        await Assert.That(transport.Status.State).IsEqualTo(UsdPhysicsTransportState.Faulted);
        await Assert.That(transport.TryAcquireLatestFrame(out _)).IsFalse();
        world.OnStep = null;
        world.DiagnosticFailure = null;
        await RunAsync(transport, transport.ResetAsync());
        Task<UsdPhysicsFrameLease> retry = transport.StepAndAcquireFrameAsync(1);
        transport.Pump();
        using UsdPhysicsFrameLease result = await retry;
        await Assert.That(result.Frame.StepIndex).IsEqualTo(1ul);
    }

    [Test]
    public async Task ExactStepLeaseRemainsReadableAfterTransportDisposal()
    {
        var world = new FakePhysicsWorld();
        await using UsdPhysicsTransport transport = CreateTransport(world, new FakePhysicsClock());
        await RunAsync(transport, transport.BuildAsync());
        Task<UsdPhysicsFrameLease> request = transport.StepAndAcquireFrameAsync(2);
        transport.Pump();
        using UsdPhysicsFrameLease frame = await request;
        await transport.DisposeAsync();
        await Assert.That(world.IsDisposed).IsTrue();
        await Assert.That(frame.Frame.StepIndex).IsEqualTo(2ul);
        await Assert.That(frame.Frame.Bodies[0].Position.Y).IsEqualTo(2d);
    }

    [Test]
    [Arguments(2)]
    [Arguments(8)]
    public async Task ExactStepAcceptsItsConfiguredBatchBoundary(int maximum)
    {
        var world = new FakePhysicsWorld();
        await using UsdPhysicsTransport transport = CreateTransport(
            world, new FakePhysicsClock(), CreateOptions(maxSubStepsPerTick: maximum));
        await RunAsync(transport, transport.BuildAsync());
        await Assert.That(() => transport.StepAndAcquireFrameAsync(maximum + 1))
            .Throws<ArgumentOutOfRangeException>();
        Task<UsdPhysicsFrameLease> request = transport.StepAndAcquireFrameAsync(maximum);
        transport.Pump();
        using UsdPhysicsFrameLease frame = await request;
        await Assert.That(frame.Frame.StepIndex).IsEqualTo((ulong)maximum);
        await Assert.That(frame.Frame.SubStepCount).IsEqualTo(maximum);
        await Assert.That(world.StepCalls).IsEqualTo(1);
        await Assert.That(world.SubStepsAdvanced).IsEqualTo((long)maximum);
    }

    [Test]
    [Arguments("unbuilt")]
    [Arguments("playing")]
    [Arguments("looping")]
    public async Task ExactStepRejectsStatesThatCannotProvideAStableRecordingAdvance(string state)
    {
        var world = new FakePhysicsWorld();
        await using UsdPhysicsTransport transport = CreateTransport(
            world, new FakePhysicsClock(), CreateOptions(loop: state == "looping"));
        if (state != "unbuilt")
        {
            await RunAsync(transport, transport.BuildAsync());
        }
        if (state == "playing")
        {
            await RunAsync(transport, transport.PlayAsync());
        }
        Exception? failure = await CaptureAsync(transport, transport.StepAndAcquireFrameAsync(1));
        await Assert.That(failure is InvalidOperationException).IsTrue();
        await Assert.That(world.StepCalls).IsEqualTo(0);
        await Assert.That(world.Counter).IsEqualTo(0ul);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task CanceledExactStepReturnsNoLeaseAndReleasesAllReservations(bool duringStep)
    {
        var world = new FakePhysicsWorld();
        var options = new UsdPhysicsTransportOptions(CreateOptions().Session, maxConcurrentFrameLeases: 1);
        await using UsdPhysicsTransport transport = CreateTransport(world, new FakePhysicsClock(), options);
        await RunAsync(transport, transport.BuildAsync());
        using var cancellation = new CancellationTokenSource();
        if (duringStep)
        {
            world.OnStep = _ => cancellation.Cancel();
        }
        else
        {
            cancellation.Cancel();
        }
        Exception? failure = await CaptureAsync(
            transport, transport.StepAndAcquireFrameAsync(1, cancellation.Token));
        await Assert.That(failure is OperationCanceledException).IsTrue();
        await Assert.That(world.StepCalls).IsEqualTo(duringStep ? 1 : 0);
        await Assert.That(transport.Status.State).IsEqualTo(
            duringStep ? UsdPhysicsTransportState.Faulted : UsdPhysicsTransportState.Paused);
        world.OnStep = null;
        if (duringStep)
        {
            await RunAsync(transport, transport.ResetAsync());
        }
        Task<UsdPhysicsFrameLease> retry = transport.StepAndAcquireFrameAsync(1);
        transport.Pump();
        using UsdPhysicsFrameLease result = await retry;
        await Assert.That(result.Frame.StepIndex).IsEqualTo(1ul);
    }

    [Test]
    [Arguments("overflow")]
    [Arguments("bodies")]
    [Arguments("deformations")]
    [Arguments("events")]
    public async Task IncompleteExactStepFaultsInsteadOfReturningPartialCaptureData(string failureKind)
    {
        var world = new FakePhysicsWorld();
        var options = new UsdPhysicsTransportOptions(CreateOptions().Session, maxConcurrentFrameLeases: 1);
        await using UsdPhysicsTransport transport = CreateTransport(world, new FakePhysicsClock(), options);
        await RunAsync(transport, transport.BuildAsync());
        world.OnFrameCopied = frame =>
        {
            frame.HasOverflow = failureKind == "overflow";
            frame.BodiesTruncated = failureKind == "bodies";
            frame.DeformationsTruncated = failureKind == "deformations";
            frame.DroppedEventCount = failureKind == "events" ? 1 : 0;
        };
        Exception? failure = await CaptureAsync(transport, transport.StepAndAcquireFrameAsync(1));
        await Assert.That(failure).IsTypeOf<InvalidDataException>();
        await Assert.That(transport.Status.State).IsEqualTo(UsdPhysicsTransportState.Faulted);
        await Assert.That(transport.TryAcquireLatestFrame(out _)).IsFalse();
        world.OnFrameCopied = null;
        await RunAsync(transport, transport.ResetAsync());
        Task<UsdPhysicsFrameLease> retry = transport.StepAndAcquireFrameAsync(1);
        transport.Pump();
        using UsdPhysicsFrameLease result = await retry;
        await Assert.That(result.Frame.StepIndex).IsEqualTo(1ul);
        await Assert.That(result.Frame.HasOverflow).IsFalse();
    }

    [Test]
    public async Task ExactStepLeaseSurvivesLaterQueuedStepsAndReset()
    {
        var world = new FakePhysicsWorld();
        await using UsdPhysicsTransport transport = CreateTransport(world, new FakePhysicsClock());
        await RunAsync(transport, transport.BuildAsync());

        Task<UsdPhysicsFrameLease> capture = transport.StepAndAcquireFrameAsync(3);
        Task later = transport.StepAsync(2);
        Task reset = transport.ResetAsync();
        transport.Pump();
        using UsdPhysicsFrameLease lease = await capture;
        await later;
        await reset;

        await Assert.That(lease.Frame.StepIndex).IsEqualTo(3ul);
        await Assert.That(lease.Frame.SubStepCount).IsEqualTo(3);
        await Assert.That(lease.Frame.SimulationSeconds).IsEqualTo(3 * StepSeconds);
        await Assert.That(lease.Frame.BodyCount).IsEqualTo(4);
        await Assert.That(lease.Frame.Bodies[0].Position.Y).IsEqualTo(3d);
        await Assert.That(transport.Status.StepIndex).IsEqualTo(0ul);
        await Assert.That(world.Counter).IsEqualTo(0ul);
        await Assert.That(transport.Status.Revision).IsGreaterThan(lease.Frame.Revision);
    }

    [Test]
    public async Task ExactStepRefusesLeasePressureBeforeAdvancingAndCanRetry()
    {
        var world = new FakePhysicsWorld();
        var options = new UsdPhysicsTransportOptions(CreateOptions().Session, maxConcurrentFrameLeases: 1);
        await using UsdPhysicsTransport transport = CreateTransport(world, new FakePhysicsClock(), options);
        await RunAsync(transport, transport.BuildAsync());
        await Assert.That(transport.TryAcquireLatestFrame(out UsdPhysicsFrameLease held)).IsTrue();
        try
        {
            Exception? refusal = await CaptureAsync(transport, transport.StepAndAcquireFrameAsync(1));
            await Assert.That(refusal).IsTypeOf<InvalidOperationException>();
            await Assert.That(world.StepCalls).IsEqualTo(0);
            await Assert.That(transport.Status.StepIndex).IsEqualTo(0ul);
            await Assert.That(transport.Status.DroppedPublications).IsEqualTo(0L);
        }
        finally { held.Dispose(); }

        Task<UsdPhysicsFrameLease> retry = transport.StepAndAcquireFrameAsync(1);
        transport.Pump();
        using UsdPhysicsFrameLease result = await retry;
        await Assert.That(result.Frame.StepIndex).IsEqualTo(1ul);
        await Assert.That(world.StepCalls).IsEqualTo(1);
    }

    [Test]
    public async Task ExactStepRefusesPublicationPressureBeforeAdvancing()
    {
        var world = new FakePhysicsWorld();
        await using UsdPhysicsTransport transport = CreateTransport(world, new FakePhysicsClock());
        await RunAsync(transport, transport.BuildAsync());
        var held = new List<UsdPhysicsFrameLease>();
        try
        {
            for (int index = 0; index < 3; index++)
            {
                if (index != 0)
                {
                    await RunAsync(transport, transport.StepAsync(1));
                }
                await Assert.That(transport.TryAcquireLatestFrame(out UsdPhysicsFrameLease lease)).IsTrue();
                held.Add(lease);
            }
            Exception? refusal = await CaptureAsync(transport, transport.StepAndAcquireFrameAsync(1));
            await Assert.That(refusal).IsTypeOf<InvalidOperationException>();
            await Assert.That(world.Counter).IsEqualTo(2ul);
            await Assert.That(transport.Status.DroppedPublications).IsEqualTo(0L);
        }
        finally
        {
            foreach (UsdPhysicsFrameLease lease in held)
            {
                lease.Dispose();
            }
        }
        Task<UsdPhysicsFrameLease> retry = transport.StepAndAcquireFrameAsync(1);
        transport.Pump();
        using UsdPhysicsFrameLease result = await retry;
        await Assert.That(result.Frame.StepIndex).IsEqualTo(3ul);
    }

    [Test]
    public async Task ExactStepAtAuthoredEndReturnsItsFrameButCannotWrap()
    {
        var world = new FakePhysicsWorld();
        await using UsdPhysicsTransport transport = CreateTransport(
            world, new FakePhysicsClock(), endTimeCode: TimeCodesPerSecond * StepSeconds);
        await RunAsync(transport, transport.BuildAsync());
        Exception? tooFar = await CaptureAsync(transport, transport.StepAndAcquireFrameAsync(2));
        await Assert.That(tooFar).IsTypeOf<ArgumentOutOfRangeException>();
        await Assert.That(world.StepCalls).IsEqualTo(0);
        Task<UsdPhysicsFrameLease> last = transport.StepAndAcquireFrameAsync(1);
        transport.Pump();
        using UsdPhysicsFrameLease frame = await last;
        await Assert.That(frame.Frame.StepIndex).IsEqualTo(1ul);
        await Assert.That(transport.Status.State).IsEqualTo(UsdPhysicsTransportState.Ended);
        Exception? ended = await CaptureAsync(transport, transport.StepAndAcquireFrameAsync(1));
        await Assert.That(ended).IsTypeOf<UsdPhysicsTransportStateException>();
        await Assert.That(world.StepCalls).IsEqualTo(1);
    }

    [Test]
    [Arguments(-1)]
    [Arguments(0)]
    [Arguments(9)]
    public async Task ExactStepRejectsAnUnboundedBatch(int steps)
    {
        await using UsdPhysicsTransport transport = CreateTransport(new FakePhysicsWorld(), new FakePhysicsClock());
        await Assert.That(() => transport.StepAndAcquireFrameAsync(steps)).Throws<ArgumentOutOfRangeException>();
        await Assert.That(transport.Status.QueueDepth).IsEqualTo(0);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task FailedExactStepReleasesReservationsAndRequiresReset(bool throwing)
    {
        var world = new FakePhysicsWorld();
        var options = new UsdPhysicsTransportOptions(CreateOptions().Session, maxConcurrentFrameLeases: 1);
        await using UsdPhysicsTransport transport = CreateTransport(world, new FakePhysicsClock(), options);
        await RunAsync(transport, transport.BuildAsync());
        if (throwing)
        {
            world.OnStep = _ => throw new InvalidOperationException("injected solver failure");
        }
        else
        {
            world.FailNextStep = true;
        }
        Exception? failure = await CaptureAsync(transport, transport.StepAndAcquireFrameAsync(1));
        await Assert.That(failure).IsNotNull();
        await Assert.That(transport.Status.State).IsEqualTo(UsdPhysicsTransportState.Faulted);
        await Assert.That(transport.TryAcquireLatestFrame(out _)).IsFalse();
        world.OnStep = null;
        await RunAsync(transport, transport.ResetAsync());
        Task<UsdPhysicsFrameLease> retry = transport.StepAndAcquireFrameAsync(1);
        transport.Pump();
        using UsdPhysicsFrameLease frame = await retry;
        await Assert.That(frame.Frame.StepIndex).IsEqualTo(1ul);
    }
}
