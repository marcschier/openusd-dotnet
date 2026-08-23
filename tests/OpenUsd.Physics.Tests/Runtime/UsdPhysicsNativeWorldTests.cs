// Copyright (c) marcschier. Licensed under the MIT License.

using OpenUsd.Physics.Interop;

namespace OpenUsd.Physics.Tests;

/// <summary>
/// Integration coverage for the retained native world. Every test is gated on the native physics
/// runtime being present so the suite stays green on machines without the compiled binary.
/// </summary>
public sealed class UsdPhysicsNativeWorldTests
{
    private static bool NativeRuntimeAvailable => PhysxRuntime.Info.IsAvailable;

    [Test]
    public async Task AnUnavailableRuntimeFailsTheBuildWithADiagnosticInsteadOfThrowing()
    {
        if (NativeRuntimeAvailable)
        {
            Skip.Test("This test only describes a host with no staged physics runtime.");
            throw new InvalidOperationException("Skip.Test returned unexpectedly.");
        }

        using var world = new UsdPhysicsNativeWorld();
        UsdPhysicsWorldBuildResult result = world.Build(
            UsdPhysicsTimeline.Default,
            UsdPhysicsFixedStep.Resolve(UsdPhysicsTimeline.Default, 60),
            UsdPhysicsSessionOptions.Default,
            CancellationToken.None);

        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(result.Diagnostics.Entries.Count).IsGreaterThan(0);
        await Assert.That(result.Capabilities).IsEqualTo(UsdPhysicsCapabilities.None);
    }

    [Test]
    public async Task TheTransportRefusesToPlayWhenTheNativeWorldCannotBeBuilt()
    {
        if (NativeRuntimeAvailable)
        {
            Skip.Test("This test only describes a host with no staged physics runtime.");
            throw new InvalidOperationException("Skip.Test returned unexpectedly.");
        }

        await using UsdPhysicsTransport transport = UsdPhysicsTransport.CreateForTesting(
            new UsdPhysicsNativeWorld(),
            UsdPhysicsTimeline.Default,
            UsdPhysicsTransportOptions.Default,
            new FakePhysicsClock());

        Task build = transport.BuildAsync();
        transport.Pump();
        await build;

        await Assert.That(transport.Status.State).IsEqualTo(UsdPhysicsTransportState.Faulted);
    }

    [Test]
    public async Task TheNativeWorldBuildsStepsAndReportsItsReplayGuarantees()
    {
        CpuDomainFixture.RequireRuntime();

        using var world = new UsdPhysicsNativeWorld();
        var timeline = new UsdPhysicsTimeline(24, 0, 24);
        UsdPhysicsFixedStep step = UsdPhysicsFixedStep.Resolve(timeline, 60);
        UsdPhysicsWorldBuildResult result = world.Build(
            timeline,
            step,
            UsdPhysicsSessionOptions.Default,
            CancellationToken.None);

        await Assert.That(result.Succeeded).IsTrue();
        await Assert.That(result.SupportsReplayEquivalentCheckpoints).IsFalse();
        await Assert.That(result.BodyCapacity).IsGreaterThan(-1);
        await Assert.That(result.Diagnostics.Entries.Any(
            entry => entry.Code == UsdPhysicsNativeWorld.ExtractionUnavailableCode)).IsTrue();

        var frame = new UsdPhysicsFrame(Math.Max(result.BodyCapacity, 1));
        await Assert.That(world.TryStep(step.Seconds, 1, frame)).IsTrue();
        await Assert.That(world.CaptureState(new UsdPhysicsBodyPose[1])).IsLessThan(0);

        world.ResetToStart();
        await Assert.That(world.TryFetch(frame)).IsTrue();
    }

    [Test]
    public async Task ApproximateResultsAreDiagnosedRatherThanPromised()
    {
        CpuDomainFixture.RequireRuntime();

        using var world = new UsdPhysicsNativeWorld();
        UsdPhysicsWorldBuildResult result = world.Build(
            UsdPhysicsTimeline.Default,
            UsdPhysicsFixedStep.Resolve(UsdPhysicsTimeline.Default, 60),
            UsdPhysicsSessionOptions.Default,
            CancellationToken.None);

        bool cuda = result.Capabilities.Supports(UsdPhysicsCapability.Cuda);
        await Assert.That(result.ResultsAreApproximate).IsEqualTo(cuda);
        if (cuda)
        {
            await Assert.That(result.Diagnostics.Entries.Any(
                entry => entry.Code == UsdPhysicsNativeWorld.CudaApproximateCode)).IsTrue();
        }
    }

    [Test]
    public async Task ACancelledRebuildLeavesThePreviouslyBuiltWorldStepping()
    {
        CpuDomainFixture.RequireRuntime();

        using var world = new UsdPhysicsNativeWorld();
        var timeline = new UsdPhysicsTimeline(24, 0, 24);
        UsdPhysicsFixedStep step = UsdPhysicsFixedStep.Resolve(timeline, 60);
        UsdPhysicsWorldBuildResult built = world.Build(
            timeline, step, UsdPhysicsSessionOptions.Default, CancellationToken.None);
        await Assert.That(built.Succeeded).IsTrue();

        var frame = new UsdPhysicsFrame(Math.Max(built.BodyCapacity, 1));
        await Assert.That(world.TryStep(step.Seconds, 1, frame)).IsTrue();

        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        OperationCanceledException? canceled = null;
        try
        {
            _ = world.Build(timeline, step, UsdPhysicsSessionOptions.Default, cancellation.Token);
        }
        catch (OperationCanceledException exception)
        {
            canceled = exception;
        }

        await Assert.That(canceled).IsNotNull();

        // The build never committed, so the world that was already retained is still the world
        // this instance owns, and it is still steppable rather than released underneath a caller.
        await Assert.That(world.IsFaulted).IsFalse();
        await Assert.That(world.TryStep(step.Seconds, 1, frame)).IsTrue();
        await Assert.That(world.TryFetch(frame)).IsTrue();
    }

    [Test]
    public async Task TheNativeTransportRunsAFixedStepSession()
    {
        CpuDomainFixture.RequireRuntime();

        var clock = new FakePhysicsClock();
        await using UsdPhysicsTransport transport = UsdPhysicsTransport.CreateForTesting(
            new UsdPhysicsNativeWorld(),
            new UsdPhysicsTimeline(24, 0, 24),
            new UsdPhysicsTransportOptions(new UsdPhysicsSessionOptions(fixedFrequencyOverrideHz: 60)),
            clock);

        Task build = transport.BuildAsync();
        transport.Pump();
        await build;
        await Assert.That(transport.Status.State).IsEqualTo(UsdPhysicsTransportState.Paused);

        Task play = transport.PlayAsync();
        transport.Pump();
        await play;

        clock.Advance(4.0 / 60);
        transport.Pump();

        await Assert.That(transport.Status.StepIndex).IsEqualTo(4ul);
        await Assert.That(transport.TryAcquireLatestFrame(out UsdPhysicsFrameLease lease)).IsTrue();
        lease.Dispose();
    }
}
