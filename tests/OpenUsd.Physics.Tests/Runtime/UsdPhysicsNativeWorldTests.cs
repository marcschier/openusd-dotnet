// Copyright (c) marcschier. Licensed under the MIT License.

using OpenUsd.Physics.Interop;

namespace OpenUsd.Physics.Tests;

/// <summary>
/// Integration coverage for the retained native world. Every test is gated on the native physics
/// runtime being present so the suite stays green on machines without the compiled binary.
/// </summary>
public sealed class UsdPhysicsNativeWorldTests
{
    [Test]
    [Arguments(true, 0, false)]
    [Arguments(true, 1, false)]
    [Arguments(true, 2, true)]
    [Arguments(true, 3, true)]
    [Arguments(false, 0, false)]
    [Arguments(false, 1, false)]
    [Arguments(false, 2, true)]
    [Arguments(false, 3, true)]
    public async Task ActorAndColliderLimitsRejectIncompleteWorlds(bool limitActors, int capacity, bool expected)
    {
        CpuDomainFixture.RequireRuntime();
        using var fixture = CpuDomainFixture.Create(nameof(ActorAndColliderLimitsRejectIncompleteWorlds));
        fixture.WriteStage("""
            #usda 1.0
            (metersPerUnit = 1)
            def Cube "Moving" (prepend apiSchemas = ["PhysicsRigidBodyAPI", "PhysicsCollisionAPI"]) {
                double3 xformOp:translate = (0,4,0)
                uniform token[] xformOpOrder = ["xformOp:translate"]
            }
            def Cube "Static" (prepend apiSchemas = ["PhysicsCollisionAPI"]) {
                double3 xformOp:translate = (10,0,0)
                uniform token[] xformOpOrder = ["xformOp:translate"]
            }
            """);
        using var world = new UsdPhysicsNativeWorld { ExtractionPage = fixture.Extract() };
        var options = new UsdPhysicsSessionOptions(
            maxRigidBodies: limitActors ? capacity : 10,
            maxColliders: limitActors ? 10 : capacity);
        UsdPhysicsFixedStep step = UsdPhysicsFixedStep.Resolve(UsdPhysicsTimeline.Default, 60);
        UsdPhysicsWorldBuildResult result = world.Build(
            UsdPhysicsTimeline.Default, step, options, CancellationToken.None);
        await Assert.That(result.Succeeded).IsEqualTo(expected);
        if (expected)
        {
            var shapes = world.InspectRigidShapes();
            await Assert.That(shapes.Info.ActorCount).IsEqualTo(2u);
            await Assert.That(shapes.Info.ShapeCount).IsEqualTo(2u);
            var frame = new UsdPhysicsFrame(result.BodyCapacity);
            await Assert.That(world.TryStep(step.Seconds, 1, frame)).IsTrue();
            await Assert.That(frame.BodyCount).IsEqualTo(1);
        }
        else
        {
            string option = limitActors ? nameof(options.MaxRigidBodies) : nameof(options.MaxColliders);
            await Assert.That(result.Diagnostics.Entries.Any(entry =>
                entry.Code == "OPENUSD_PHYSICS_WORLD_CAPACITY_EXCEEDED" &&
                entry.Severity == UsdPhysicsDiagnosticSeverity.Error &&
                entry.Message.Contains(option, StringComparison.Ordinal))).IsTrue();
            await Assert.That(world.LastComposition).IsNull();
        }
    }

    [Test]
    [Arguments("MaxRigidBodies", 0)]
    [Arguments("MaxRigidBodies", 1)]
    [Arguments("MaxColliders", 0)]
    [Arguments("MaxColliders", 1)]
    [Arguments("MaxControllers", 0)]
    [Arguments("MaxControllers", 1)]
    public async Task ControllerImplicitResourcesRespectTheirLimits(string option, int capacity)
    {
        CpuDomainFixture.RequireRuntime();
        using var fixture = CpuDomainFixture.Create(nameof(ControllerImplicitResourcesRespectTheirLimits));
        fixture.WriteStage("""
            #usda 1.0
            (metersPerUnit = 1)
            def PhysicsScene "Scene" {}
            def Xform "Walker" (prepend apiSchemas = ["OpenUsdPhysicsCharacterControllerAPI"]) {
                uniform token openUsdPhysics:controller:shapeType = "capsule"
                double openUsdPhysics:controller:radius = 0.3
                double openUsdPhysics:controller:height = 1
                double openUsdPhysics:controller:stepOffset = 0.3
                double openUsdPhysics:controller:slopeLimit = 45
                float3 openUsdPhysics:controller:upAxis = (0,1,0)
            }
            """);
        using var world = new UsdPhysicsNativeWorld { ExtractionPage = fixture.Extract() };
        var options = new UsdPhysicsSessionOptions(
            maxRigidBodies: option == "MaxRigidBodies" ? capacity : 10,
            maxColliders: option == "MaxColliders" ? capacity : 10,
            maxControllers: option == "MaxControllers" ? capacity : 10);
        UsdPhysicsFixedStep step = UsdPhysicsFixedStep.Resolve(UsdPhysicsTimeline.Default, 60);
        UsdPhysicsWorldBuildResult result = world.Build(
            UsdPhysicsTimeline.Default, step, options, CancellationToken.None);
        await Assert.That(result.Succeeded).IsEqualTo(capacity == 1);
        if (capacity == 0)
        {
            await Assert.That(result.Diagnostics.Entries.Any(entry =>
                entry.Code == "OPENUSD_PHYSICS_WORLD_CAPACITY_EXCEEDED" &&
                entry.Message.Contains(option, StringComparison.Ordinal))).IsTrue();
        }
        else
        {
            await Assert.That(world.LastComposition!.Controllers).IsEqualTo(1);
            var frame = new UsdPhysicsFrame(result.BodyCapacity);
            await Assert.That(world.TryStep(step.Seconds, 1, frame)).IsTrue();
            await Assert.That(frame.BodyCount).IsEqualTo(1);
        }
    }

    [Test]
    public async Task ZeroDomainCapacitiesAcceptAnEmptyWorld()
    {
        CpuDomainFixture.RequireRuntime();
        using var world = new UsdPhysicsNativeWorld();
        UsdPhysicsFixedStep step = UsdPhysicsFixedStep.Resolve(UsdPhysicsTimeline.Default, 60);
        UsdPhysicsWorldBuildResult result = world.Build(UsdPhysicsTimeline.Default, step,
            new UsdPhysicsSessionOptions(maxRigidBodies: 0, maxColliders: 0, maxJoints: 0,
                maxArticulations: 0, maxControllers: 0, maxVehicles: 0, maxEventsPerStep: 0,
                maxQueryHitsPerRequest: 0), CancellationToken.None);
        await Assert.That(result.Succeeded).IsTrue();
        await Assert.That(result.BodyCapacity).IsEqualTo(0);
        var frame = new UsdPhysicsFrame(0);
        await Assert.That(world.TryStep(step.Seconds, 1, frame)).IsTrue();
        await Assert.That(frame.BodyCount).IsEqualTo(0);
    }

    [Test]
    public async Task ZeroEventCapacityDisablesCollectionWithoutBreakingContactSimulation()
    {
        CpuDomainFixture.RequireRuntime();
        using var fixture = CpuDomainFixture.Create(nameof(ZeroEventCapacityDisablesCollectionWithoutBreakingContactSimulation));
        fixture.WriteStage("""
            #usda 1.0
            (metersPerUnit = 1)
            def Cube "Ground" (prepend apiSchemas = ["PhysicsCollisionAPI"]) {
                double size = 2
                double3 xformOp:translate = (0,-1,0)
                uniform token[] xformOpOrder = ["xformOp:translate"]
            }
            def Sphere "Falling" (prepend apiSchemas = ["PhysicsRigidBodyAPI", "PhysicsCollisionAPI", "PhysicsMassAPI"]) {
                double radius = 0.25
                float physics:mass = 1
                double3 xformOp:translate = (0,2,0)
                uniform token[] xformOpOrder = ["xformOp:translate"]
            }
            """);
        using var world = new UsdPhysicsNativeWorld { ExtractionPage = fixture.Extract() };
        UsdPhysicsFixedStep step = UsdPhysicsFixedStep.Resolve(UsdPhysicsTimeline.Default, 60);
        UsdPhysicsWorldBuildResult result = world.Build(UsdPhysicsTimeline.Default, step,
            new UsdPhysicsSessionOptions(maxEventsPerStep: 0), CancellationToken.None);
        await Assert.That(result.Succeeded).IsTrue();
        var frame = new UsdPhysicsFrame(result.BodyCapacity);
        for (int i = 0; i < 120; i++)
        {
            await Assert.That(world.TryStep(step.Seconds, 1, frame)).IsTrue();
        }
        await Assert.That(frame.BodyCount).IsEqualTo(1);
        await Assert.That(frame.Bodies[0].Position.Y).IsEqualTo(0.25).Within(0.03);
        await Assert.That(world.IsFaulted).IsFalse();
    }

    [Test]
    public async Task FailedMeshPreparationReportsTheBuildFailureAndPreservesTheLiveWorld()
    {
        CpuDomainFixture.RequireRuntime();
        using var original = CpuDomainFixture.Create(nameof(FailedMeshPreparationReportsTheBuildFailureAndPreservesTheLiveWorld));
        original.WriteStage("""
            #usda 1.0
            (metersPerUnit = 1)
            def Cube "Body" (prepend apiSchemas = ["PhysicsRigidBodyAPI", "PhysicsCollisionAPI"]) {
                double3 xformOp:translate = (0,4,0)
                uniform token[] xformOpOrder = ["xformOp:translate"]
            }
            """);
        using var invalid = CpuDomainFixture.Create("InvalidPreparedHull");
        invalid.WriteStage("""
            #usda 1.0
            (metersPerUnit = 1)
            def Mesh "Bad" (prepend apiSchemas = ["PhysicsCollisionAPI", "PhysicsMeshCollisionAPI"]) {
                uniform token physics:approximation = "convexHull"
                point3f[] points = [(0,0,0), (0,0,0), (0,0,0), (0,0,0)]
            }
            """);
        using UsdPhysicsSimulation simulation = original.BuildSimulation();
        await Assert.That(simulation.Build.Succeeded).IsTrue();
        simulation.World.AttachExtraction(invalid.Extract());
        UsdPhysicsWorldBuildResult failed = simulation.World.Build(UsdPhysicsTimeline.Default,
            UsdPhysicsFixedStep.Resolve(UsdPhysicsTimeline.Default, 60),
            UsdPhysicsSessionOptions.Default, CancellationToken.None);
        await Assert.That(failed.Succeeded).IsFalse();
        await Assert.That(failed.Diagnostics.Entries.Any(entry =>
            entry.Code == UsdPhysicsNativeWorld.BuildFailedCode &&
            entry.Message.Contains("/Bad", StringComparison.Ordinal) &&
            entry.Message.Contains("no spatial extent", StringComparison.Ordinal))).IsTrue();
        await Assert.That(simulation.World.TryFetch(simulation.Frame)).IsTrue();
        await Assert.That(simulation.RequirePose("/Body").Position.Y).IsEqualTo(4).Within(0.001);
        simulation.Step(1);
        await Assert.That(simulation.RequirePose("/Body").Position.Y).IsLessThan(4);
    }

    [Test]
    [Arguments("convexHull", false)]
    [Arguments("convexDecomposition", true)]
    public async Task AuthoredDecompositionPreservesTheCollisionGap(string approximation, bool fallsThrough)
    {
        CpuDomainFixture.RequireRuntime();
        using var fixture = CpuDomainFixture.Create(nameof(AuthoredDecompositionPreservesTheCollisionGap));
        fixture.WriteStage($$"""
            #usda 1.0
            (
                metersPerUnit = 1
                upAxis = "Y"
            )
            def PhysicsScene "Physics" {
                vector3f physics:gravityDirection = (0, -1, 0)
                float physics:gravityMagnitude = 9.81
            }
            def Mesh "Collider" (prepend apiSchemas = ["PhysicsCollisionAPI", "PhysicsMeshCollisionAPI"]) {
                uniform token physics:approximation = "{{approximation}}"
                point3f[] points = [
                    (-2.5,-0.5,-0.5), (-1.5,-0.5,-0.5), (-2.5,0.5,-0.5), (-1.5,0.5,-0.5),
                    (-2.5,-0.5,0.5), (-1.5,-0.5,0.5), (-2.5,0.5,0.5), (-1.5,0.5,0.5),
                    (1.5,-0.5,-0.5), (2.5,-0.5,-0.5), (1.5,0.5,-0.5), (2.5,0.5,-0.5),
                    (1.5,-0.5,0.5), (2.5,-0.5,0.5), (1.5,0.5,0.5), (2.5,0.5,0.5)]
                int[] faceVertexCounts = [3,3,3,3,3,3,3,3,3,3,3,3,3,3,3,3,3,3,3,3,3,3,3,3]
                int[] faceVertexIndices = [
                    0,2,1,1,2,3,4,5,6,5,7,6,0,1,4,1,5,4,2,6,3,3,6,7,0,4,2,2,4,6,1,3,5,3,7,5,
                    8,10,9,9,10,11,12,13,14,13,15,14,8,9,12,9,13,12,10,14,11,11,14,15,8,12,10,10,12,14,9,11,13,11,15,13]
            }
            def Cube "Falling" (prepend apiSchemas = ["PhysicsRigidBodyAPI", "PhysicsCollisionAPI", "PhysicsMassAPI"]) {
                double size = 0.25
                float physics:mass = 1
                double3 xformOp:translate = (0,3,0)
                uniform token[] xformOpOrder = ["xformOp:translate"]
            }
            """);
        using UsdPhysicsSimulation simulation = fixture.BuildSimulation();
        await Assert.That(simulation.Build.Succeeded).IsTrue();
        await Assert.That(simulation.Composition).IsNotNull();
        await Assert.That(simulation.Composition!.Skipped).IsEmpty();
        await Assert.That(simulation.Composition.Actors).IsEqualTo(2);
        if (fallsThrough)
        {
            await Assert.That(simulation.Composition.Shapes).IsGreaterThanOrEqualTo(3);
            UsdPhysicsWorldBuildResult refused = simulation.World.Build(
                UsdPhysicsTimeline.Default, UsdPhysicsFixedStep.Resolve(UsdPhysicsTimeline.Default, 60),
                new UsdPhysicsSessionOptions(maxColliders: 2), CancellationToken.None);
            await Assert.That(refused.Succeeded).IsFalse()
                .Because("Two source colliders require more than two actual shapes after decomposition.");
            await Assert.That(refused.Diagnostics.Entries.Any(entry =>
                entry.Code == "OPENUSD_PHYSICS_WORLD_CAPACITY_EXCEEDED" &&
                entry.Message.Contains("MaxColliders", StringComparison.Ordinal))).IsTrue();
            await Assert.That(simulation.World.LastComposition).IsSameReferenceAs(simulation.Composition);
        }
        else
        {
            await Assert.That(simulation.Composition.Shapes).IsEqualTo(2);
        }
        simulation.Step(60);
        await Assert.That(simulation.Frame.BodyCount).IsEqualTo(1)
            .Because("Static collision actors do not produce rigid-pose publications.");
        double height = simulation.RequirePose("/Falling").Position.Y;
        if (fallsThrough)
        {
            await Assert.That(height).IsLessThan(-1);
        }
        else
        {
            await Assert.That(height).IsGreaterThan(0.5);
        }
        simulation.World.ResetToStart();
        await Assert.That(simulation.World.TryFetch(simulation.Frame)).IsTrue();
        await Assert.That(simulation.RequirePose("/Falling").Position.Y).IsEqualTo(3).Within(0.001);
    }

    [Test]
    [Arguments("Y")]
    [Arguments("Z")]
    public async Task PublishedFramesUseAuthoredStageUnitsAndAxes(string axis)
    {
        CpuDomainFixture.RequireRuntime();
        using var fixture = CpuDomainFixture.Create(nameof(PublishedFramesUseAuthoredStageUnitsAndAxes));
        fixture.WriteStage($$"""
            #usda 1.0
            (
                metersPerUnit = 0.01
                upAxis = "{{axis}}"
            )
            def PhysicsScene "Physics" {
                float physics:gravityMagnitude = 0
            }
            def Cube "Body" (prepend apiSchemas = ["PhysicsRigidBodyAPI", "PhysicsCollisionAPI", "PhysicsMassAPI"]) {
                double size = 2
                double3 xformOp:translate = (10,20,-40)
                uniform token[] xformOpOrder = ["xformOp:translate"]
                vector3f physics:velocity = (1,2,3)
                vector3f physics:angularVelocity = (90,0,0)
                float physics:mass = 1
            }
            """);
        using var world = new UsdPhysicsNativeWorld();
        world.AttachExtraction(fixture.Extract());
        UsdPhysicsFixedStep step = UsdPhysicsFixedStep.Resolve(UsdPhysicsTimeline.Default, 60);
        UsdPhysicsWorldBuildResult built = world.Build(
            UsdPhysicsTimeline.Default, step, UsdPhysicsSessionOptions.Default, CancellationToken.None);
        await Assert.That(built.Succeeded).IsTrue();
        var frame = new UsdPhysicsFrame(built.BodyCapacity);
        await Assert.That(world.TryFetch(frame)).IsTrue();
        await Assert.That(frame.BodyCount).IsEqualTo(1);
        UsdPhysicsBodyPose body = frame.Bodies[0];
        await Assert.That(Math.Abs(body.Position.X - 10)).IsLessThan(1e-5);
        await Assert.That(Math.Abs(body.Position.Y - 20)).IsLessThan(1e-5);
        await Assert.That(Math.Abs(body.Position.Z + 40)).IsLessThan(1e-5);
        await Assert.That(Math.Abs(body.LinearVelocity.X - 1)).IsLessThan(1e-5);
        await Assert.That(Math.Abs(body.LinearVelocity.Y - 2)).IsLessThan(1e-5);
        await Assert.That(Math.Abs(body.LinearVelocity.Z - 3)).IsLessThan(1e-5);
        await Assert.That(Math.Abs(body.Orientation.W - 1)).IsLessThan(1e-5);
        await Assert.That(Math.Abs(body.Orientation.X)).IsLessThan(1e-5);
        await Assert.That(Math.Abs(body.AngularVelocity.X - Math.PI / 2)).IsLessThan(1e-5);
        await Assert.That(Math.Abs(body.AngularVelocity.Y)).IsLessThan(1e-5);
        await Assert.That(Math.Abs(body.AngularVelocity.Z)).IsLessThan(1e-5);
        world.AttachExtraction(null);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.That(() => world.Build(
            UsdPhysicsTimeline.Default, step, UsdPhysicsSessionOptions.Default, cancellation.Token))
            .Throws<OperationCanceledException>();
        await Assert.That(world.TryFetch(frame)).IsTrue();
        await Assert.That(Math.Abs(frame.Bodies[0].Position.X - 10)).IsLessThan(1e-5);
        await Assert.That(Math.Abs(frame.Bodies[0].Position.Z + 40)).IsLessThan(1e-5);
    }

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
