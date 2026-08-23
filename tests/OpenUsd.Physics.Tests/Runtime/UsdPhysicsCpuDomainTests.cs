// Copyright (c) marcschier. Licensed under the MIT License.

using OpenUsd.Physics.Extraction;
using OpenUsd.Physics.Interop;

namespace OpenUsd.Physics.Tests;

/// <summary>
/// End to end coverage of the CPU simulation domains, driven by a real stage rather than by a
/// hand built page.
/// </summary>
/// <remarks>
/// <para>
/// Every test here runs the whole chain: author a stage, extract it with the native extractor,
/// compose the extraction page onto the world build page, build the retained native world, step
/// it, and read the results back. A hand built page can only prove that the managed mirror agrees
/// with itself, whereas this suite proves that the authored scene actually simulates.
/// </para>
/// <para>
/// The suite is skipped rather than failed when the native runtime is not staged, because the
/// managed unit tests must stay runnable on a machine that has no compiled native tree.
/// </para>
/// </remarks>
public sealed class UsdPhysicsCpuDomainTests
{
    private const double Gravity = 9.81;

    [Test]
    public async Task AnAuthoredStageSimulatesItsRigidBodiesThroughTheWholeChain()
    {
        CpuDomainFixture.RequireRuntime();


        using CpuDomainFixture fixture = CpuDomainFixture.Create(nameof(
            AnAuthoredStageSimulatesItsRigidBodiesThroughTheWholeChain));
        fixture.WriteStage(
            """
            #usda 1.0
            (
                upAxis = "Y"
                metersPerUnit = 1
                kilogramsPerUnit = 1
                timeCodesPerSecond = 24
                startTimeCode = 0
                endTimeCode = 24
            )

            def PhysicsScene "Scene"
            {
                vector3f physics:gravityDirection = (0, -1, 0)
                float physics:gravityMagnitude = 9.81
            }

            def Cube "Ground" (prepend apiSchemas = ["PhysicsCollisionAPI"])
            {
                double size = 2
                float3 xformOp:scale = (50, 1, 50)
                double3 xformOp:translate = (0, -1, 0)
                uniform token[] xformOpOrder = ["xformOp:translate", "xformOp:scale"]
            }

            def Sphere "Falling" (
                prepend apiSchemas = ["PhysicsCollisionAPI", "PhysicsRigidBodyAPI", "PhysicsMassAPI"]
            )
            {
                double radius = 0.5
                float physics:mass = 2
                double3 xformOp:translate = (0, 10, 0)
                uniform token[] xformOpOrder = ["xformOp:translate"]
            }
            """);

        using UsdPhysicsSimulation simulation = fixture.BuildSimulation();

        await Assert.That(simulation.Build.Succeeded).IsTrue();
        await Assert.That(simulation.Composition).IsNotNull();
        await Assert.That(simulation.Composition!.Scenes).IsEqualTo(1);
        await Assert.That(simulation.Composition.Actors).IsGreaterThanOrEqualTo(2);
        await Assert.That(simulation.Composition.Shapes).IsGreaterThanOrEqualTo(2);

        // The world must report the falling body, and only a body that is really integrated
        // reaches the closed form free fall distance.
        await Assert.That(simulation.World.TryFetch(simulation.Frame)).IsTrue();
        UsdPhysicsBodyPose start = simulation.RequirePose("/Falling");
        await Assert.That(start.Position.Y).IsEqualTo(10.0).Within(0.001);

        double seconds = simulation.Step(30);
        UsdPhysicsBodyPose after = simulation.RequirePose("/Falling");
        double expected = 10.0 - (0.5 * Gravity * seconds * seconds);

        await Assert.That(after.Position.Y).IsEqualTo(expected).Within(0.05);
        await Assert.That(after.LinearVelocity.Y).IsEqualTo(-Gravity * seconds).Within(0.2);
    }

    [Test]
    public async Task AFallingBodyComesToRestOnAnAuthoredGround()
    {
        CpuDomainFixture.RequireRuntime();


        using CpuDomainFixture fixture = CpuDomainFixture.Create(nameof(
            AFallingBodyComesToRestOnAnAuthoredGround));
        fixture.WriteStage(
            """
            #usda 1.0
            (
                upAxis = "Y"
                metersPerUnit = 1
                kilogramsPerUnit = 1
                timeCodesPerSecond = 24
                startTimeCode = 0
                endTimeCode = 24
            )

            def PhysicsScene "Scene"
            {
                vector3f physics:gravityDirection = (0, -1, 0)
                float physics:gravityMagnitude = 9.81
            }

            def Cube "Ground" (prepend apiSchemas = ["PhysicsCollisionAPI"])
            {
                double size = 2
                float3 xformOp:scale = (50, 1, 50)
                double3 xformOp:translate = (0, -1, 0)
                uniform token[] xformOpOrder = ["xformOp:translate", "xformOp:scale"]
            }

            def Sphere "Ball" (
                prepend apiSchemas = ["PhysicsCollisionAPI", "PhysicsRigidBodyAPI", "PhysicsMassAPI"]
            )
            {
                double radius = 0.5
                float physics:mass = 1
                double3 xformOp:translate = (0, 4, 0)
                uniform token[] xformOpOrder = ["xformOp:translate"]
            }
            """);

        using UsdPhysicsSimulation simulation = fixture.BuildSimulation();
        await Assert.That(simulation.Build.Succeeded).IsTrue();

        // Three seconds is far more than the drop needs, so the body must be resting on the
        // ground rather than still moving or fallen through it.
        simulation.Step(180);

        UsdPhysicsBodyPose rest = simulation.RequirePose("/Ball");
        await Assert.That(rest.Position.Y).IsEqualTo(0.5).Within(0.06);
        await Assert.That(Math.Abs(rest.LinearVelocity.Y)).IsLessThan(0.2);
    }

    [Test]
    public async Task AKinematicBodyFollowsItsAuthoredPoseAndNeverFalls()
    {
        CpuDomainFixture.RequireRuntime();


        using CpuDomainFixture fixture = CpuDomainFixture.Create(nameof(
            AKinematicBodyFollowsItsAuthoredPoseAndNeverFalls));
        fixture.WriteStage(
            """
            #usda 1.0
            (
                upAxis = "Y"
                metersPerUnit = 1
                kilogramsPerUnit = 1
                timeCodesPerSecond = 24
                startTimeCode = 0
                endTimeCode = 24
            )

            def PhysicsScene "Scene"
            {
                vector3f physics:gravityDirection = (0, -1, 0)
                float physics:gravityMagnitude = 9.81
            }

            def Cube "Platform" (
                prepend apiSchemas = ["PhysicsCollisionAPI", "PhysicsRigidBodyAPI"]
            )
            {
                double size = 2
                bool physics:kinematicEnabled = 1
                double3 xformOp:translate = (0, 3, 0)
                uniform token[] xformOpOrder = ["xformOp:translate"]
            }
            """);

        using UsdPhysicsSimulation simulation = fixture.BuildSimulation();
        await Assert.That(simulation.Build.Succeeded).IsTrue();

        simulation.Step(120);

        UsdPhysicsBodyPose pose = simulation.RequirePose("/Platform");
        await Assert.That(pose.Position.Y).IsEqualTo(3.0).Within(0.0001);
        await Assert.That(pose.IsKinematic).IsTrue();
    }

    [Test]
    public async Task ARevoluteJointHoldsItsBodyAgainstGravity()
    {
        CpuDomainFixture.RequireRuntime();


        using CpuDomainFixture fixture = CpuDomainFixture.Create(nameof(
            ARevoluteJointHoldsItsBodyAgainstGravity));
        fixture.WriteStage(
            """
            #usda 1.0
            (
                upAxis = "Y"
                metersPerUnit = 1
                kilogramsPerUnit = 1
                timeCodesPerSecond = 24
                startTimeCode = 0
                endTimeCode = 24
            )

            def PhysicsScene "Scene"
            {
                vector3f physics:gravityDirection = (0, -1, 0)
                float physics:gravityMagnitude = 9.81
            }

            def Sphere "Pendulum" (
                prepend apiSchemas = ["PhysicsCollisionAPI", "PhysicsRigidBodyAPI", "PhysicsMassAPI"]
            )
            {
                double radius = 0.25
                float physics:mass = 1
                double3 xformOp:translate = (1, 5, 0)
                uniform token[] xformOpOrder = ["xformOp:translate"]
            }

            def PhysicsRevoluteJoint "Hinge"
            {
                rel physics:body1 = </Pendulum>
                uniform token physics:axis = "Z"
                point3f physics:localPos0 = (0, 5, 0)
                point3f physics:localPos1 = (-1, 0, 0)
            }
            """);

        using UsdPhysicsSimulation simulation = fixture.BuildSimulation();
        await Assert.That(simulation.Build.Succeeded).IsTrue();
        await Assert.That(simulation.Composition!.Joints).IsEqualTo(1);

        simulation.Step(120);

        // A hinge anchored one unit from the body keeps it on a unit circle about the anchor no
        // matter how far it swings, which no unconstrained body would do under gravity.
        UsdPhysicsBodyPose pose = simulation.RequirePose("/Pendulum");
        double dx = pose.Position.X;
        double dy = pose.Position.Y - 5.0;
        await Assert.That(Math.Sqrt((dx * dx) + (dy * dy))).IsEqualTo(1.0).Within(0.08);
        await Assert.That(Math.Abs(pose.Position.Z)).IsLessThan(0.05);
    }

    [Test]
    public async Task TwoWorldsBuiltFromTheSameStageStepIndependentlyAndAgree()
    {
        CpuDomainFixture.RequireRuntime();


        using CpuDomainFixture fixture = CpuDomainFixture.Create(nameof(
            TwoWorldsBuiltFromTheSameStageStepIndependentlyAndAgree));
        fixture.WriteStage(SimpleDropStage);

        using UsdPhysicsSimulation first = fixture.BuildSimulation();
        using UsdPhysicsSimulation second = fixture.BuildSimulation();

        await Assert.That(first.Build.Succeeded).IsTrue();
        await Assert.That(second.Build.Succeeded).IsTrue();

        // The second world advances twice as far, so a shared native scene would show up as
        // identical poses instead of the expected separation.
        first.Step(20);
        second.Step(40);

        UsdPhysicsBodyPose slow = first.RequirePose("/Ball");
        UsdPhysicsBodyPose fast = second.RequirePose("/Ball");
        await Assert.That(slow.Position.Y).IsGreaterThan(fast.Position.Y + 0.5);

        // Catching the first world up must land it exactly where the second already is, which is
        // what proves the two worlds are stepping the same deterministic simulation.
        first.Step(20);
        UsdPhysicsBodyPose caughtUp = first.RequirePose("/Ball");
        await Assert.That(caughtUp.Position.Y).IsEqualTo(fast.Position.Y).Within(0.0001);
    }

    [Test]
    public async Task ConcurrentWorldsStepTheSameStageOnManyThreads()
    {
        CpuDomainFixture.RequireRuntime();


        using CpuDomainFixture fixture = CpuDomainFixture.Create(nameof(
            ConcurrentWorldsStepTheSameStageOnManyThreads));
        fixture.WriteStage(SimpleDropStage);

        UsdPhysicsExtractionPage extraction = fixture.Extract();
        var heights = new double[4];
        var failures = new string?[4];

        Parallel.For(0, heights.Length, index =>
        {
            try
            {
                using var simulation = UsdPhysicsSimulation.Create(extraction);
                if (!simulation.Build.Succeeded)
                {
                    failures[index] = "the world refused to build";
                    return;
                }
                simulation.Step(60);
                heights[index] = simulation.RequirePose("/Ball").Position.Y;
            }
            catch (Exception exception)
            {
                failures[index] = exception.ToString();
            }
        });

        await Assert.That(failures.Where(static value => value is not null)).IsEmpty();
        foreach (double height in heights)
        {
            await Assert.That(height).IsEqualTo(heights[0]).Within(0.0001);
        }
    }

    [Test]
    public async Task ABodyCapacitySmallerThanTheSceneIsReportedAsTruncationRatherThanCorruption()
    {
        CpuDomainFixture.RequireRuntime();


        using CpuDomainFixture fixture = CpuDomainFixture.Create(nameof(
            ABodyCapacitySmallerThanTheSceneIsReportedAsTruncationRatherThanCorruption));
        fixture.WriteStage(ThreeBodyStage);

        using UsdPhysicsSimulation simulation = fixture.BuildSimulation();
        await Assert.That(simulation.Build.Succeeded).IsTrue();
        await Assert.That(simulation.Build.BodyCapacity).IsGreaterThanOrEqualTo(3);

        // One slot for three bodies: the step must still succeed and must say it truncated,
        // instead of writing past the frame or silently dropping the overflow.
        var narrow = new UsdPhysicsFrame(1);
        await Assert.That(simulation.World.TryStep(simulation.StepSeconds, 1, narrow)).IsTrue();
        await Assert.That(narrow.BodyCount).IsEqualTo(1);
        await Assert.That(narrow.BodiesTruncated).IsTrue();
    }

    [Test]
    public async Task AMalformedObjectIsIsolatedAndTheRestOfTheStageStillSimulates()
    {
        CpuDomainFixture.RequireRuntime();


        using CpuDomainFixture fixture = CpuDomainFixture.Create(nameof(
            AMalformedObjectIsIsolatedAndTheRestOfTheStageStillSimulates));

        // The joint names a body that does not exist, which the composer must drop on its own
        // without taking the scene, the ground, or the falling body down with it.
        fixture.WriteStage(
            """
            #usda 1.0
            (
                upAxis = "Y"
                metersPerUnit = 1
                kilogramsPerUnit = 1
                timeCodesPerSecond = 24
                startTimeCode = 0
                endTimeCode = 24
            )

            def PhysicsScene "Scene"
            {
                vector3f physics:gravityDirection = (0, -1, 0)
                float physics:gravityMagnitude = 9.81
            }

            def Cube "Ground" (prepend apiSchemas = ["PhysicsCollisionAPI"])
            {
                double size = 2
                float3 xformOp:scale = (50, 1, 50)
                double3 xformOp:translate = (0, -1, 0)
                uniform token[] xformOpOrder = ["xformOp:translate", "xformOp:scale"]
            }

            def Sphere "Ball" (
                prepend apiSchemas = ["PhysicsCollisionAPI", "PhysicsRigidBodyAPI", "PhysicsMassAPI"]
            )
            {
                double radius = 0.5
                float physics:mass = 1
                double3 xformOp:translate = (0, 4, 0)
                uniform token[] xformOpOrder = ["xformOp:translate"]
            }

            def PhysicsFixedJoint "Broken"
            {
                rel physics:body0 = </DoesNotExist>
                rel physics:body1 = </AlsoMissing>
            }
            """);

        using UsdPhysicsSimulation simulation = fixture.BuildSimulation();

        await Assert.That(simulation.Build.Succeeded).IsTrue();
        await Assert.That(simulation.Composition!.Joints).IsEqualTo(0);
        await Assert.That(simulation.Composition.Actors).IsGreaterThanOrEqualTo(2);

        simulation.Step(180);
        UsdPhysicsBodyPose rest = simulation.RequirePose("/Ball");
        await Assert.That(rest.Position.Y).IsEqualTo(0.5).Within(0.06);
    }

    [Test]
    public async Task TheWarmStepPathAllocatesNothing()
    {
        CpuDomainFixture.RequireRuntime();


        using CpuDomainFixture fixture = CpuDomainFixture.Create(nameof(TheWarmStepPathAllocatesNothing));
        fixture.WriteStage(ThreeBodyStage);

        using UsdPhysicsSimulation simulation = fixture.BuildSimulation();
        await Assert.That(simulation.Build.Succeeded).IsTrue();

        // Warm the whole path first, so nothing measured below is first call setup: both the step
        // and the fetch marshalling stubs must already exist before the measurement starts.
        simulation.Step(16);
        _ = simulation.World.TryFetch(simulation.Frame);

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int index = 0; index < 64; index++)
        {
            _ = simulation.World.TryStep(simulation.StepSeconds, 1, simulation.Frame);
            _ = simulation.World.TryFetch(simulation.Frame);
        }
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        await Assert.That(allocated).IsEqualTo(0L);
    }

    [Test]
    public async Task TheNativeRuntimeReportsTheCpuDomainCapabilitiesItImplements()
    {
        CpuDomainFixture.RequireRuntime();


        PhysxRuntimeInfo runtime = PhysxRuntime.Info;
        await Assert.That(runtime.IsAvailable).IsTrue();
        await Assert.That(runtime.Abi.AbiVersion).IsEqualTo(PhysxAbi.Version);
        await Assert.That(runtime.Abi.HeightfieldSampleSize)
            .IsEqualTo((uint)PhysxAbi.RecordSizes.HeightfieldSample);

        var flags = (PhysxCapabilityFlags)runtime.Capabilities.Flags;
        await Assert.That(flags.HasFlag(PhysxCapabilityFlags.CpuRigidBodies)).IsTrue();
        await Assert.That(flags.HasFlag(PhysxCapabilityFlags.Joints)).IsTrue();
        await Assert.That(flags.HasFlag(PhysxCapabilityFlags.MeshCooking)).IsTrue();
        await Assert.That(flags.HasFlag(PhysxCapabilityFlags.ConvexCoreShapes)).IsTrue();
        await Assert.That(flags.HasFlag(PhysxCapabilityFlags.HeightfieldShapes)).IsTrue();
        await Assert.That(flags.HasFlag(PhysxCapabilityFlags.D6JointDrives)).IsTrue();
        await Assert.That(flags.HasFlag(PhysxCapabilityFlags.ShapeOffsets)).IsTrue();
        await Assert.That(flags.HasFlag(PhysxCapabilityFlags.RigidBodyTuning)).IsTrue();
        await Assert.That(flags.HasFlag(PhysxCapabilityFlags.Articulations)).IsTrue();
        await Assert.That(flags.HasFlag(PhysxCapabilityFlags.CharacterControllers)).IsTrue();
        await Assert.That(flags.HasFlag(PhysxCapabilityFlags.ArticulationTendons)).IsTrue();
        await Assert.That(flags.HasFlag(PhysxCapabilityFlags.ArticulationMimicJoints)).IsTrue();
        await Assert.That(flags.HasFlag(PhysxCapabilityFlags.Vehicles)).IsTrue();

        // Every completed native domain must reach the public capability set.
        UsdPhysicsCapabilities published = PhysxRuntime.MapCapabilities(flags);
        await Assert.That(published.Supports(UsdPhysicsCapability.RigidBodies)).IsTrue();
        await Assert.That(published.Supports(UsdPhysicsCapability.Articulations)).IsTrue();
        await Assert.That(published.Supports(UsdPhysicsCapability.Controllers)).IsTrue();
        await Assert.That(published.Supports(UsdPhysicsCapability.Vehicles)).IsTrue();
    }

    private const string SimpleDropStage =
        """
        #usda 1.0
        (
            upAxis = "Y"
            metersPerUnit = 1
            kilogramsPerUnit = 1
            timeCodesPerSecond = 24
            startTimeCode = 0
            endTimeCode = 24
        )

        def PhysicsScene "Scene"
        {
            vector3f physics:gravityDirection = (0, -1, 0)
            float physics:gravityMagnitude = 9.81
        }

        def Sphere "Ball" (
            prepend apiSchemas = ["PhysicsCollisionAPI", "PhysicsRigidBodyAPI", "PhysicsMassAPI"]
        )
        {
            double radius = 0.5
            float physics:mass = 1
            double3 xformOp:translate = (0, 20, 0)
            uniform token[] xformOpOrder = ["xformOp:translate"]
        }
        """;

    private const string ThreeBodyStage =
        """
        #usda 1.0
        (
            upAxis = "Y"
            metersPerUnit = 1
            kilogramsPerUnit = 1
            timeCodesPerSecond = 24
            startTimeCode = 0
            endTimeCode = 24
        )

        def PhysicsScene "Scene"
        {
            vector3f physics:gravityDirection = (0, -1, 0)
            float physics:gravityMagnitude = 9.81
        }

        def Sphere "A" (
            prepend apiSchemas = ["PhysicsCollisionAPI", "PhysicsRigidBodyAPI", "PhysicsMassAPI"]
        )
        {
            double radius = 0.5
            float physics:mass = 1
            double3 xformOp:translate = (0, 20, 0)
            uniform token[] xformOpOrder = ["xformOp:translate"]
        }

        def Sphere "B" (
            prepend apiSchemas = ["PhysicsCollisionAPI", "PhysicsRigidBodyAPI", "PhysicsMassAPI"]
        )
        {
            double radius = 0.5
            float physics:mass = 1
            double3 xformOp:translate = (4, 20, 0)
            uniform token[] xformOpOrder = ["xformOp:translate"]
        }

        def Sphere "C" (
            prepend apiSchemas = ["PhysicsCollisionAPI", "PhysicsRigidBodyAPI", "PhysicsMassAPI"]
        )
        {
            double radius = 0.5
            float physics:mass = 1
            double3 xformOp:translate = (8, 20, 0)
            uniform token[] xformOpOrder = ["xformOp:translate"]
        }
        """;

    // ---- Articulation and character controller tests (ABI v5) ----

    [Test]
    public async Task AnAuthoredArticulationSimulatesAsAReducedCoordinateChain()
    {
        CpuDomainFixture.RequireRuntime();

        using CpuDomainFixture fixture = CpuDomainFixture.Create(nameof(
            AnAuthoredArticulationSimulatesAsAReducedCoordinateChain));
        fixture.WriteStage(ArticulationStage);

        using UsdPhysicsSimulation simulation = fixture.BuildSimulation();
        await Assert.That(simulation.Build.Succeeded).IsTrue();
        UsdPhysicsCompositionReport composition = simulation.Composition!;

        // The chain must reach the world as one articulation, not as loose actors joined by
        // maximal coordinate joints, otherwise the reduced coordinate solver never sees it.
        await Assert.That(composition.Articulations).IsEqualTo(1);

        await Assert.That(simulation.World.TryFetch(simulation.Frame)).IsTrue();
        UsdPhysicsBodyPose startTip = simulation.RequirePose("/Articulation/Link2");
        simulation.Step(120);

        UsdPhysicsBodyPose root = simulation.RequirePose("/Articulation/Root");
        UsdPhysicsBodyPose tip = simulation.RequirePose("/Articulation/Link2");

        // A fixed base pins the root while gravity swings the free links away from rest.
        await Assert.That(Math.Abs(root.Position.Y - 5.0)).IsLessThan(0.05);
        await Assert.That(Math.Abs(tip.Position.X - startTip.Position.X)).IsGreaterThan(0.2);
    }

    [Test]
    public async Task AnArticulationLinkIsNeverAlsoSimulatedAsALooseActor()
    {
        CpuDomainFixture.RequireRuntime();

        using CpuDomainFixture fixture = CpuDomainFixture.Create(nameof(
            AnArticulationLinkIsNeverAlsoSimulatedAsALooseActor));
        fixture.WriteStage(ArticulationStage);

        using UsdPhysicsSimulation simulation = fixture.BuildSimulation();
        await Assert.That(simulation.Build.Succeeded).IsTrue();
        UsdPhysicsCompositionReport composition = simulation.Composition!;

        // Only the ground reaches the actor section; the three links belong to the articulation
        // and the two joints between them are expressed by the articulation, not by the joint
        // section, so the same prim is never simulated twice.
        await Assert.That(composition.Actors).IsEqualTo(1);
        await Assert.That(composition.Joints).IsEqualTo(0);
    }

    [Test]
    public async Task EveryArticulationLinkPublishesItsOwnStableIdentity()
    {
        CpuDomainFixture.RequireRuntime();

        using CpuDomainFixture fixture = CpuDomainFixture.Create(nameof(
            EveryArticulationLinkPublishesItsOwnStableIdentity));
        fixture.WriteStage(ArticulationStage);

        ulong[] first = ArticulationLinkIdentities(fixture);
        ulong[] second = ArticulationLinkIdentities(fixture);

        // Every link is published under the identity of its own prim, so the three links never
        // collapse onto one identity and never depend on the order the chain was walked in.
        await Assert.That(first.Length).IsEqualTo(3);
        await Assert.That(first.Distinct().Count()).IsEqualTo(3);
        await Assert.That(first.Any(static value => value == 0UL)).IsFalse();

        // Rebuilding the same stage reproduces the same identities, which is what lets a consumer
        // correlate results across a rebuild.
        await Assert.That(second).IsEquivalentTo(first);
    }

    [Test]
    public async Task AnArticulationLinkWithAStaticOnlyColliderIsIsolatedWholeAndTheStageStillRuns()
    {
        CpuDomainFixture.RequireRuntime();

        using CpuDomainFixture fixture = CpuDomainFixture.Create(nameof(
            AnArticulationLinkWithAStaticOnlyColliderIsIsolatedWholeAndTheStageStillRuns));

        // A reduced coordinate link is always movable, so a plane collider on one link makes the
        // whole chain unbuildable. It has to be dropped as one unit before anything is staged,
        // leaving no half written links or shape references behind to invalidate the page.
        fixture.WriteStage(
            """
            #usda 1.0
            (
                upAxis = "Y"
                metersPerUnit = 1
                kilogramsPerUnit = 1
                timeCodesPerSecond = 24
                startTimeCode = 0
                endTimeCode = 24
            )

            def PhysicsScene "Scene"
            {
                vector3f physics:gravityDirection = (0, -1, 0)
                float physics:gravityMagnitude = 9.81
            }

            def Cube "Ground" (prepend apiSchemas = ["PhysicsCollisionAPI"])
            {
                double size = 2
                float3 xformOp:scale = (50, 1, 50)
                double3 xformOp:translate = (0, -1, 0)
                uniform token[] xformOpOrder = ["xformOp:translate", "xformOp:scale"]
            }

            def Sphere "Ball" (
                prepend apiSchemas = ["PhysicsCollisionAPI", "PhysicsRigidBodyAPI", "PhysicsMassAPI"]
            )
            {
                double radius = 0.5
                float physics:mass = 1
                double3 xformOp:translate = (0, 4, 0)
                uniform token[] xformOpOrder = ["xformOp:translate"]
            }

            def Xform "Broken" (
                prepend apiSchemas = ["PhysicsArticulationRootAPI"]
            )
            {
                def Sphere "Root" (
                    prepend apiSchemas = ["PhysicsCollisionAPI", "PhysicsRigidBodyAPI", "PhysicsMassAPI"]
                )
                {
                    double radius = 0.2
                    float physics:mass = 1
                    double3 xformOp:translate = (6, 5, 0)
                    uniform token[] xformOpOrder = ["xformOp:translate"]
                }

                def PhysicsFixedJoint "RootAnchor"
                {
                    rel physics:body1 = </Broken/Root>
                }

                def Plane "Blade" (
                    prepend apiSchemas = ["PhysicsCollisionAPI", "PhysicsRigidBodyAPI", "PhysicsMassAPI"]
                )
                {
                    uniform token axis = "Y"
                    float physics:mass = 1
                    double3 xformOp:translate = (7, 5, 0)
                    uniform token[] xformOpOrder = ["xformOp:translate"]
                }

                def PhysicsRevoluteJoint "Hinge"
                {
                    uniform token physics:axis = "Z"
                    rel physics:body0 = </Broken/Root>
                    rel physics:body1 = </Broken/Blade>
                    point3f physics:localPos0 = (0.5, 0, 0)
                    point3f physics:localPos1 = (-0.5, 0, 0)
                }
            }
            """);

        using UsdPhysicsSimulation simulation = fixture.BuildSimulation();
        await Assert.That(simulation.Build.Succeeded).IsTrue();
        UsdPhysicsCompositionReport composition = simulation.Composition!;

        await Assert.That(composition.Articulations).IsEqualTo(0);
        await Assert.That(composition.Skipped.Any(static note =>
            note.Contains("/Broken/Blade", StringComparison.Ordinal))).IsTrue();

        // No link of the rejected chain reaches the world at all, and the rest of the stage still
        // simulates, which proves the page stayed consistent.
        simulation.Step(180);
        await Assert.That(simulation.HasPose("/Broken/Root")).IsFalse();
        await Assert.That(simulation.HasPose("/Broken/Blade")).IsFalse();
        UsdPhysicsBodyPose rest = simulation.RequirePose("/Ball");
        await Assert.That(rest.Position.Y).IsEqualTo(0.5).Within(0.06);
    }

    // ---- Joint drive tests (ABI v6) ----

    [Test]
    public async Task ARevoluteDriveHoldsItsPendulumAtTheAuthoredTargetAngle()
    {
        CpuDomainFixture.RequireRuntime();

        using CpuDomainFixture fixture = CpuDomainFixture.Create(nameof(
            ARevoluteDriveHoldsItsPendulumAtTheAuthoredTargetAngle));

        // The arm starts along positive x and the drive asks for a quarter turn about z, which
        // only a real position drive can hold against gravity.
        fixture.WriteStage(DriveStage(
            "PhysicsRevoluteJoint",
            "angular",
            """
                    uniform token physics:axis = "Z"
            """,
            """
                    float drive:angular:physics:stiffness = 4000
                    float drive:angular:physics:damping = 400
                    float drive:angular:physics:targetPosition = 90
            """));

        using UsdPhysicsSimulation simulation = fixture.BuildSimulation();
        await Assert.That(simulation.Build.Succeeded).IsTrue();
        await Assert.That(simulation.Composition!.Joints).IsEqualTo(1);

        simulation.Step(120);
        UsdPhysicsBodyPose settling = simulation.RequirePose("/Arm");
        simulation.Step(60);
        UsdPhysicsBodyPose pose = simulation.RequirePose("/Arm");

        // A quarter turn stands the arm on end over the anchor and holds it there. A hinge with
        // no working drive swings instead: it never sits still off the horizontal.
        await Assert.That(Math.Abs(pose.Position.X)).IsLessThan(0.35);
        await Assert.That(Math.Abs(pose.Position.Y - 5.0)).IsGreaterThan(0.7);
        await Assert.That(Math.Abs(pose.Position.Y - settling.Position.Y)).IsLessThan(0.1);
    }

    [Test]
    public async Task ARevoluteAccelerationDriveSpinsItsPendulumInsteadOfFreeWheeling()
    {
        CpuDomainFixture.RequireRuntime();

        using CpuDomainFixture fixture = CpuDomainFixture.Create(nameof(
            ARevoluteAccelerationDriveSpinsItsPendulumInsteadOfFreeWheeling));

        // An acceleration drive must reach the solver as a drive, never as a free spinning axis,
        // so a velocity target has to turn the arm all the way around against gravity.
        fixture.WriteStage(DriveStage(
            "PhysicsRevoluteJoint",
            "angular",
            """
                    uniform token physics:axis = "Z"
            """,
            """
                    uniform token drive:angular:physics:type = "acceleration"
                    float drive:angular:physics:damping = 2000
                    float drive:angular:physics:targetVelocity = 180
            """));

        using UsdPhysicsSimulation simulation = fixture.BuildSimulation();
        await Assert.That(simulation.Build.Succeeded).IsTrue();

        double travelled = 0.0;
        var previous = (X: 1.0, Y: 5.0);
        for (int index = 0; index < 180; index++)
        {
            simulation.Step(1);
            UsdPhysicsBodyPose sample = simulation.RequirePose("/Arm");
            double dx = sample.Position.X - previous.X;
            double dy = sample.Position.Y - previous.Y;
            travelled += Math.Sqrt((dx * dx) + (dy * dy));
            previous = (sample.Position.X, sample.Position.Y);
        }

        // Half a turn a second for three seconds is several full circles of arc length, which no
        // damped or free hanging pendulum ever covers.
        await Assert.That(travelled).IsGreaterThan(6.0);
    }

    [Test]
    public async Task APrismaticDriveHoldsItsBodyAgainstGravity()
    {
        CpuDomainFixture.RequireRuntime();

        using CpuDomainFixture fixture = CpuDomainFixture.Create(nameof(
            APrismaticDriveHoldsItsBodyAgainstGravity));

        // The slide runs along y through the body itself, so only a linear drive that honours
        // its stiffness keeps the body at the authored offset instead of letting it sink.
        fixture.WriteStage(DriveStage(
            "PhysicsPrismaticJoint",
            "linear",
            """
                    uniform token physics:axis = "Y"
            """,
            """
                    float drive:linear:physics:stiffness = 6000
                    float drive:linear:physics:damping = 600
                    float drive:linear:physics:targetPosition = 0
            """,
            localPos0: "(1, 5, 0)",
            localPos1: "(0, 0, 0)"));

        using UsdPhysicsSimulation simulation = fixture.BuildSimulation();
        await Assert.That(simulation.Build.Succeeded).IsTrue();
        await Assert.That(simulation.Composition!.Joints).IsEqualTo(1);

        simulation.Step(180);
        UsdPhysicsBodyPose pose = simulation.RequirePose("/Arm");

        // The body is held at the anchor height rather than sliding away under its own weight.
        await Assert.That(pose.Position.Y).IsEqualTo(5.0).Within(0.1);
        await Assert.That(pose.Position.X).IsEqualTo(1.0).Within(0.05);
    }

    [Test]
    public async Task ASphericalDriveSwingsItsBodyOutOfThePlaneGravityWouldKeepItIn()
    {
        CpuDomainFixture.RequireRuntime();

        using CpuDomainFixture fixture = CpuDomainFixture.Create(nameof(
            ASphericalDriveSwingsItsBodyOutOfThePlaneGravityWouldKeepItIn));

        // A swing drive turns the arm about the up axis, which moves it in z. Gravity never does
        // that, so any z travel is the drive and nothing else.
        fixture.WriteStage(DriveStage(
            "PhysicsSphericalJoint",
            "angular",
            string.Empty,
            """
                    float drive:angular:physics:stiffness = 4000
                    float drive:angular:physics:damping = 400
                    float drive:angular:physics:targetPosition = 45
            """));

        using UsdPhysicsSimulation simulation = fixture.BuildSimulation();
        await Assert.That(simulation.Build.Succeeded).IsTrue();
        await Assert.That(simulation.Composition!.Joints).IsEqualTo(1);

        simulation.Step(180);
        UsdPhysicsBodyPose pose = simulation.RequirePose("/Arm");
        await Assert.That(Math.Abs(pose.Position.Z)).IsGreaterThan(0.3);
    }

    private static ulong[] ArticulationLinkIdentities(CpuDomainFixture fixture)
    {
        using UsdPhysicsSimulation simulation = fixture.BuildSimulation();
        if (!simulation.Build.Succeeded || !simulation.World.TryFetch(simulation.Frame))
        {
            throw new InvalidOperationException("The articulation stage did not build.");
        }

        return
        [
            simulation.RequirePose("/Articulation/Root").Id.Value,
            simulation.RequirePose("/Articulation/Link1").Id.Value,
            simulation.RequirePose("/Articulation/Link2").Id.Value,
        ];
    }

    /// <summary>Writes one anchored arm driven by a single axis joint of the named type.</summary>
    private static string DriveStage(
        string jointType,
        string instance,
        string axis,
        string drive,
        string localPos0 = "(0, 5, 0)",
        string localPos1 = "(-1, 0, 0)") =>
        $$"""
        #usda 1.0
        (
            upAxis = "Y"
            metersPerUnit = 1
            kilogramsPerUnit = 1
            timeCodesPerSecond = 24
            startTimeCode = 0
            endTimeCode = 24
        )

        def PhysicsScene "Scene"
        {
            vector3f physics:gravityDirection = (0, -1, 0)
            float physics:gravityMagnitude = 9.81
        }

        def Sphere "Arm" (
            prepend apiSchemas = ["PhysicsCollisionAPI", "PhysicsRigidBodyAPI", "PhysicsMassAPI"]
        )
        {
            double radius = 0.2
            float physics:mass = 1
            double3 xformOp:translate = (1, 5, 0)
            uniform token[] xformOpOrder = ["xformOp:translate"]
        }

        def {{jointType}} "Drive" (
            prepend apiSchemas = ["PhysicsDriveAPI:{{instance}}"]
        )
        {
            rel physics:body1 = </Arm>
        {{axis}}
            point3f physics:localPos0 = {{localPos0}}
            point3f physics:localPos1 = {{localPos1}}
        {{drive}}
        }
        """;

    [Test]
    public async Task AnAuthoredCharacterControllerWalksUnderMoveCommands()
    {
        CpuDomainFixture.RequireRuntime();

        using CpuDomainFixture fixture = CpuDomainFixture.Create(nameof(
            AnAuthoredCharacterControllerWalksUnderMoveCommands));
        fixture.WriteStage(
            """
            #usda 1.0
            (
                upAxis = "Y"
                metersPerUnit = 1
                kilogramsPerUnit = 1
                timeCodesPerSecond = 24
                startTimeCode = 0
                endTimeCode = 24
            )

            def PhysicsScene "Scene"
            {
                vector3f physics:gravityDirection = (0, -1, 0)
                float physics:gravityMagnitude = 9.81
            }

            def Cube "Ground" (prepend apiSchemas = ["PhysicsCollisionAPI"])
            {
                double size = 2
                float3 xformOp:scale = (50, 1, 50)
                double3 xformOp:translate = (0, -1, 0)
                uniform token[] xformOpOrder = ["xformOp:translate", "xformOp:scale"]
            }

            def Xform "Walker" (
                prepend apiSchemas = ["OpenUsdPhysicsCharacterControllerAPI"]
            )
            {
                uniform token openUsdPhysics:controller:shapeType = "capsule"
                double openUsdPhysics:controller:radius = 0.3
                double openUsdPhysics:controller:height = 1
                double openUsdPhysics:controller:stepOffset = 0.3
                double openUsdPhysics:controller:slopeLimit = 45
                float3 openUsdPhysics:controller:upAxis = (0, 1, 0)
                double3 xformOp:translate = (0, 2, 0)
                uniform token[] xformOpOrder = ["xformOp:translate"]
            }

            def Capsule "Passenger" (
                prepend apiSchemas = ["PhysicsCollisionAPI", "PhysicsRigidBodyAPI"]
            )
            {
                double radius = 0.3
                double height = 1
                uniform token axis = "Y"
                double3 xformOp:translate = (5, 2, 0)
                uniform token[] xformOpOrder = ["xformOp:translate"]
            }
            """);

        using UsdPhysicsSimulation simulation = fixture.BuildSimulation();
        await Assert.That(simulation.Build.Succeeded).IsTrue();
        UsdPhysicsCompositionReport composition = simulation.Composition!;
        await Assert.That(composition.Controllers).IsEqualTo(1);

        // The controller falls onto the ground under its own gravity integration first.
        simulation.Step(60);
        UsdPhysicsBodyPose landed = simulation.RequirePose("/Walker.controller");
        await Assert.That(landed.Position.Y).IsLessThan(2.0);
        await Assert.That(landed.Position.Y).IsGreaterThan(0.0);

        // A move command must move the controller along the ground rather than through it.
        PhysxCommand[] walk = [UsdPhysicsSimulation.ControllerMove("/Walker", 0.02F, 0.0F, 0.0F)];
        double startX = landed.Position.X;
        double groundY = landed.Position.Y;
        simulation.Step(60, walk);
        UsdPhysicsBodyPose walked = simulation.RequirePose("/Walker.controller");

        await Assert.That(walked.Position.X - startX).IsGreaterThan(0.5);
        await Assert.That(Math.Abs(walked.Position.Y - groundY)).IsLessThan(0.05);

        // The rest of the stage still simulates next to the controller.
        await Assert.That(simulation.HasPose("/Passenger")).IsTrue();
    }

    [Test]
    public async Task AnAuthoredVehicleAcceleratesSteersAndBrakesThroughTheWholeChain()
    {
        CpuDomainFixture.RequireRuntime();

        using CpuDomainFixture fixture = CpuDomainFixture.Create(nameof(
            AnAuthoredVehicleAcceleratesSteersAndBrakesThroughTheWholeChain));
        fixture.WriteStage(VehicleStage);

        using UsdPhysicsSimulation simulation = fixture.BuildSimulation();
        await Assert.That(simulation.Build.Succeeded).IsTrue();
        await Assert.That(simulation.Composition).IsNotNull();
        await Assert.That(simulation.Composition!.Vehicles).IsEqualTo(1);

        PhysxCommand[] parked = [UsdPhysicsSimulation.VehicleInput("/Car", 0.0F, 1.0F, 0.0F, 1.0F)];
        PhysxCommand[] accelerate = [UsdPhysicsSimulation.VehicleInput("/Car", 1.0F, 0.0F, 0.0F)];
        PhysxCommand[] brake = [UsdPhysicsSimulation.VehicleInput("/Car", 0.0F, 1.0F, 0.0F)];
        PhysxCommand[] steer = [UsdPhysicsSimulation.VehicleInput("/Car", 0.6F, 0.0F, 1.0F)];

        // Settling on the suspension proves the road query found the ground rather than
        // launching the chassis, which is what an unfiltered wheel raycast would do.
        simulation.Step(120, parked);
        UsdPhysicsBodyPose settled = simulation.RequirePose("/Car");
        await Assert.That(settled.Position.Y).IsGreaterThan(0.1);
        await Assert.That(settled.Position.Y).IsLessThan(1.0);
        await Assert.That(Math.Abs(settled.LinearVelocity.Z)).IsLessThan(0.5);

        // The world must publish one body per wheel on top of the chassis, which is what proves
        // the wheel simulation reached the result page rather than only the chassis rigid body.
        await Assert.That(simulation.HasPose("/Car/WheelFrontLeft.vehicleWheel")).IsTrue();
        await Assert.That(simulation.HasPose("/Car/WheelFrontRight.vehicleWheel")).IsTrue();
        await Assert.That(simulation.HasPose("/Car/WheelRearLeft.vehicleWheel")).IsTrue();
        await Assert.That(simulation.HasPose("/Car/WheelRearRight.vehicleWheel")).IsTrue();

        double startZ = settled.Position.Z;
        UsdPhysicsBodyPose startWheel = simulation.RequirePose("/Car/WheelFrontLeft.vehicleWheel");
        simulation.Step(180, accelerate);
        UsdPhysicsBodyPose moving = simulation.RequirePose("/Car");
        UsdPhysicsBodyPose movedWheel = simulation.RequirePose("/Car/WheelFrontLeft.vehicleWheel");
        await Assert.That(moving.Position.Z - startZ).IsGreaterThan(3.0);
        await Assert.That(moving.LinearVelocity.Z).IsGreaterThan(3.0);

        // The wheel travels with the chassis, so its published pose is a live simulation result.
        await Assert.That(movedWheel.Position.Z - startWheel.Position.Z).IsGreaterThan(3.0);

        // Braking from a straight run must remove most of the speed.
        double speedBeforeBrake = moving.LinearVelocity.Z;
        simulation.Step(180, brake);
        UsdPhysicsBodyPose stopped = simulation.RequirePose("/Car");
        await Assert.That(stopped.LinearVelocity.Z).IsLessThan(speedBeforeBrake * 0.25);

        // Steering must move the car off the straight line it was travelling on.
        double straightX = stopped.Position.X;
        simulation.Step(240, steer);
        UsdPhysicsBodyPose turned = simulation.RequirePose("/Car");
        await Assert.That(Math.Abs(turned.Position.X - straightX)).IsGreaterThan(0.5);
    }

    [Test]
    public async Task AVehicleWithoutAnAutoGearBoxDrivesOnItsAuthoredGear()
    {
        CpuDomainFixture.RequireRuntime();

        using CpuDomainFixture fixture = CpuDomainFixture.Create(nameof(
            AVehicleWithoutAnAutoGearBoxDrivesOnItsAuthoredGear));
        fixture.WriteStage(ManualVehicleStage);

        using UsdPhysicsSimulation simulation = fixture.BuildSimulation();
        await Assert.That(simulation.Build.Succeeded).IsTrue();
        await Assert.That(simulation.Composition).IsNotNull();
        await Assert.That(simulation.Composition!.Vehicles).IsEqualTo(1);

        PhysxCommand[] parked = [UsdPhysicsSimulation.VehicleInput("/Car", 0.0F, 1.0F, 0.0F, 1.0F)];
        PhysxCommand[] accelerate = [UsdPhysicsSimulation.VehicleInput("/Car", 1.0F, 0.0F, 0.0F)];
        PhysxCommand[] brake = [UsdPhysicsSimulation.VehicleInput("/Car", 0.0F, 1.0F, 0.0F)];

        // Gear one is the first gearbox ratio, which this project encodes as reverse.
        PhysxCommand[] reverse = [UsdPhysicsSimulation.VehicleInput("/Car", 1.0F, 0.0F, 0.0F, 0.0F, 1)];

        simulation.Step(120, parked);
        UsdPhysicsBodyPose settled = simulation.RequirePose("/Car");

        // With no auto gear box the vehicle must still start in a forward gear, otherwise it
        // would sit in neutral and never move no matter how much throttle it is given.
        double startZ = settled.Position.Z;
        simulation.Step(180, accelerate);
        UsdPhysicsBodyPose moving = simulation.RequirePose("/Car");
        await Assert.That(moving.Position.Z - startZ).IsGreaterThan(2.0);

        simulation.Step(180, brake);
        UsdPhysicsBodyPose stopped = simulation.RequirePose("/Car");
        await Assert.That(Math.Abs(stopped.LinearVelocity.Z)).IsLessThan(1.0);

        // An explicit manual gear must be honoured with the auto gear box switched off.
        double stoppedZ = stopped.Position.Z;
        simulation.Step(240, reverse);
        UsdPhysicsBodyPose reversed = simulation.RequirePose("/Car");
        await Assert.That(reversed.Position.Z - stoppedZ).IsLessThan(-0.5);
    }

    [Test]
    public async Task AnAuthoredMimicJointCouplesTwoArticulationAxes()
    {
        CpuDomainFixture.RequireRuntime();

        using CpuDomainFixture fixture = CpuDomainFixture.Create(nameof(
            AnAuthoredMimicJointCouplesTwoArticulationAxes));
        fixture.WriteStage(MimicArticulationStage);

        using UsdPhysicsSimulation simulation = fixture.BuildSimulation();
        await Assert.That(simulation.Build.Succeeded).IsTrue();
        await Assert.That(simulation.Composition).IsNotNull();
        await Assert.That(simulation.Composition!.Articulations).IsEqualTo(1);
        await Assert.That(simulation.Composition.MimicJoints).IsEqualTo(1);

        await Assert.That(simulation.World.TryFetch(simulation.Frame)).IsTrue();
        simulation.Step(120);

        UsdPhysicsBodyPose root = simulation.RequirePose("/Articulation/Root");
        UsdPhysicsBodyPose link1 = simulation.RequirePose("/Articulation/Link1");
        UsdPhysicsBodyPose link2 = simulation.RequirePose("/Articulation/Link2");

        // The fixed base keeps the root where it was authored, so any motion below it comes
        // from the coupled joints rather than from the whole chain falling.
        await Assert.That(root.Position.Y).IsEqualTo(5.0).Within(0.05);

        // PhysX enforces qA + gearRatio * qB + offset = 0, so a gear ratio of one makes the
        // second joint rotate exactly opposite to the first. The middle link therefore swings
        // while the tip link keeps the world orientation it was authored with, which an
        // uncoupled chain never does.
        double middle = SwingAngle(link1.Orientation);
        double tip = SwingAngle(link2.Orientation);
        await Assert.That(Math.Abs(middle)).IsGreaterThan(0.05);
        await Assert.That(Math.Abs(tip)).IsLessThan(Math.Abs(middle) * 0.5);
    }

    [Test]
    public async Task AnAuthoredFixedTendonHoldsItsArticulationChainUp()
    {
        CpuDomainFixture.RequireRuntime();

        double coupled = MeasureTipDrop(
            nameof(AnAuthoredFixedTendonHoldsItsArticulationChainUp) + "-tendon",
            FixedTendonArticulationStage,
            expectedTendons: 1);
        double control = MeasureTipDrop(
            nameof(AnAuthoredFixedTendonHoldsItsArticulationChainUp) + "-control",
            ArticulationStage,
            expectedTendons: 0);

        // The control chain is the same chain without the tendon, so a smaller drop can only
        // come from a tendon force that actually reached the solver. The fixed tendon drives the
        // gearing weighted sum of the two joint coordinates to its rest length rather than holding
        // the chain rigid, so the remaining sag is the folded shape that constraint allows.
        await Assert.That(control).IsGreaterThan(0.3);
        await Assert.That(coupled).IsLessThan(control * 0.75);
    }

    [Test]
    public async Task AnAuthoredSpatialTendonHoldsItsArticulationChainUp()
    {
        CpuDomainFixture.RequireRuntime();

        double coupled = MeasureTipDrop(
            nameof(AnAuthoredSpatialTendonHoldsItsArticulationChainUp) + "-tendon",
            SpatialTendonArticulationStage,
            expectedTendons: 1);
        double control = MeasureTipDrop(
            nameof(AnAuthoredSpatialTendonHoldsItsArticulationChainUp) + "-control",
            ArticulationStage,
            expectedTendons: 0);

        await Assert.That(control).IsGreaterThan(0.3);
        await Assert.That(coupled).IsLessThan(control * 0.5);
    }

    /// <summary>Builds one articulation stage and reports how far its tip link fell.</summary>
    private static double MeasureTipDrop(string name, string usda, int expectedTendons)
    {
        using CpuDomainFixture fixture = CpuDomainFixture.Create(name);
        fixture.WriteStage(usda);

        using UsdPhysicsSimulation simulation = fixture.BuildSimulation();
        if (!simulation.Build.Succeeded ||
            simulation.Composition is null ||
            simulation.Composition.Articulations != 1 ||
            simulation.Composition.Tendons != expectedTendons)
        {
            throw new InvalidOperationException(
                $"{name} did not compose one articulation with {expectedTendons} tendons: " +
                $"succeeded={simulation.Build.Succeeded} composition={simulation.Composition} " +
                "diagnostics=" +
                string.Join(
                    "; ",
                    simulation.Build.Diagnostics.Entries.Select(
                        static entry => $"{entry.Code}: {entry.Message}")));
        }

        simulation.Step(120);
        return 5.0 - simulation.RequirePose("/Articulation/Link2").Position.Y;
    }

    /// <summary>Reads the rotation about the stage Z axis out of one published orientation.</summary>
    private static double SwingAngle(UsdPhysicsOrientation orientation) =>
        2.0 * Math.Atan2(orientation.Z, orientation.W);

    /// <summary>
    /// The vehicle stage with the auto gear box removed, which is what makes the runtime build the
    /// vehicle without an autobox and start it on an authored manual gear.
    /// </summary>
    private static string ManualVehicleStage => string.Join(
        '\n',
        VehicleStage.Split('\n').Where(static line =>
            !line.Contains("AutoGearBoxAPI", StringComparison.Ordinal) &&
            !line.Contains("autoGearBox:", StringComparison.Ordinal)));

    private const string VehicleStage =
        """
        #usda 1.0
        (
            upAxis = "Y"
            metersPerUnit = 1
            kilogramsPerUnit = 1
            timeCodesPerSecond = 24
            startTimeCode = 0
            endTimeCode = 24
        )

        def PhysicsScene "Scene"
        {
            vector3f physics:gravityDirection = (0, -1, 0)
            float physics:gravityMagnitude = 9.81
        }

        def PhysicsMaterial "RoadMaterial"
        {
            float physics:staticFriction = 1
            float physics:dynamicFriction = 1
            float physics:restitution = 0
            float physics:density = 1000
        }

        def Cube "Ground" (
            prepend apiSchemas = ["PhysicsCollisionAPI", "MaterialBindingAPI"]
        )
        {
            double size = 2
            rel material:binding:physics = </RoadMaterial>
            float3 xformOp:scale = (500, 0.5, 500)
            double3 xformOp:translate = (0, -0.5, 0)
            uniform token[] xformOpOrder = ["xformOp:translate", "xformOp:scale"]
        }

        def Cube "Car" (
            prepend apiSchemas = [
                "PhysicsCollisionAPI",
                "PhysicsRigidBodyAPI",
                "PhysicsMassAPI",
                "MaterialBindingAPI",
                "OpenUsdPhysicsVehicleAPI",
                "OpenUsdPhysicsVehicleEngineAPI",
                "OpenUsdPhysicsVehicleGearsAPI",
                "OpenUsdPhysicsVehicleAutoGearBoxAPI",
                "OpenUsdPhysicsVehicleClutchAPI",
                "OpenUsdPhysicsVehicleDifferentialAPI",
                "OpenUsdPhysicsVehicleBrakesAPI",
                "OpenUsdPhysicsVehicleSteeringAPI"
            ]
        )
        {
            double size = 2
            rel material:binding:physics = </RoadMaterial>
            float physics:mass = 1500
            bool physics:sleepEnabled = 0
            float3 xformOp:scale = (0.9, 0.25, 2)
            double3 xformOp:translate = (0, 0.8, 0)
            uniform token[] xformOpOrder = ["xformOp:translate", "xformOp:scale"]

            bool openUsdPhysics:vehicle:enabled = 1
            uniform token openUsdPhysics:vehicle:driveType = "engine"
            uniform token openUsdPhysics:vehicle:longitudinalAxis = "posZ"
            uniform token openUsdPhysics:vehicle:lateralAxis = "posX"
            uniform token openUsdPhysics:vehicle:verticalAxis = "posY"
            uniform token openUsdPhysics:vehicle:suspensionQueryType = "raycast"

            double openUsdPhysics:engine:peakTorque = 500
            double openUsdPhysics:engine:momentOfInertia = 1
            double openUsdPhysics:engine:idleRotationSpeed = 75
            double openUsdPhysics:engine:maxRotationSpeed = 600
            double openUsdPhysics:engine:dampingRateFullThrottle = 0.15
            double openUsdPhysics:engine:dampingRateZeroThrottleClutchEngaged = 2
            double openUsdPhysics:engine:dampingRateZeroThrottleClutchDisengaged = 0.35

            float[] openUsdPhysics:gears:ratios = [-4, 0, 4, 2, 1.5, 1.1]
            double openUsdPhysics:gears:ratioScale = 4
            double openUsdPhysics:gears:switchTime = 0.5

            float[] openUsdPhysics:autoGearBox:upRatios = [0.65, 0.65, 0.65, 0.65]
            float[] openUsdPhysics:autoGearBox:downRatios = [0.15, 0.15, 0.15, 0.15]
            double openUsdPhysics:autoGearBox:latency = 2

            double openUsdPhysics:clutch:strength = 10

            int[] openUsdPhysics:differential:wheels = [0, 1, 2, 3]
            float[] openUsdPhysics:differential:torqueRatios = [0.25, 0.25, 0.25, 0.25]

            double openUsdPhysics:brakes:primaryMaxBrakeTorque = 3000
            int[] openUsdPhysics:brakes:primaryWheels = [0, 1, 2, 3]
            float[] openUsdPhysics:brakes:primaryTorqueMultipliers = [1, 1, 1, 1]
            double openUsdPhysics:brakes:secondaryMaxBrakeTorque = 5000
            int[] openUsdPhysics:brakes:secondaryWheels = [2, 3]
            float[] openUsdPhysics:brakes:secondaryTorqueMultipliers = [1, 1]

            double openUsdPhysics:steering:maxSteerAngle = 28.6478898
            int[] openUsdPhysics:steering:wheels = [0, 1]
            float[] openUsdPhysics:steering:angleMultipliers = [1, 1]

            def Xform "WheelFrontLeft" (
                prepend apiSchemas = [
                    "OpenUsdPhysicsVehicleWheelAttachmentAPI",
                    "OpenUsdPhysicsVehicleWheelAPI",
                    "OpenUsdPhysicsVehicleSuspensionAPI",
                    "OpenUsdPhysicsVehicleTireAPI"
                ]
            )
            {
                int openUsdPhysics:wheelAttachment:index = 0
                float3 openUsdPhysics:wheelAttachment:suspensionFramePosition = (-0.9, 0, 1.4)
                float3 openUsdPhysics:wheelAttachment:suspensionTravelDirection = (0, -1, 0)
                float3 openUsdPhysics:wheelAttachment:wheelFramePosition = (0, 0, 0)
                double openUsdPhysics:wheel:radius = 0.35
                double openUsdPhysics:wheel:width = 0.3
                double openUsdPhysics:wheel:mass = 20
                double openUsdPhysics:wheel:momentOfInertia = 1.225
                double openUsdPhysics:wheel:dampingRate = 0.25
                double openUsdPhysics:suspension:springStrength = 35000
                double openUsdPhysics:suspension:springDamperRate = 4500
                double openUsdPhysics:suspension:travelDistance = 0.25
                double openUsdPhysics:suspension:sprungMass = 375
                double openUsdPhysics:tire:longitudinalStiffness = 5000
                double openUsdPhysics:tire:camberStiffness = 0
                double openUsdPhysics:tire:restLoad = 0
            }

            def Xform "WheelFrontRight" (
                prepend apiSchemas = [
                    "OpenUsdPhysicsVehicleWheelAttachmentAPI",
                    "OpenUsdPhysicsVehicleWheelAPI",
                    "OpenUsdPhysicsVehicleSuspensionAPI",
                    "OpenUsdPhysicsVehicleTireAPI"
                ]
            )
            {
                int openUsdPhysics:wheelAttachment:index = 1
                float3 openUsdPhysics:wheelAttachment:suspensionFramePosition = (0.9, 0, 1.4)
                float3 openUsdPhysics:wheelAttachment:suspensionTravelDirection = (0, -1, 0)
                float3 openUsdPhysics:wheelAttachment:wheelFramePosition = (0, 0, 0)
                double openUsdPhysics:wheel:radius = 0.35
                double openUsdPhysics:wheel:width = 0.3
                double openUsdPhysics:wheel:mass = 20
                double openUsdPhysics:wheel:momentOfInertia = 1.225
                double openUsdPhysics:wheel:dampingRate = 0.25
                double openUsdPhysics:suspension:springStrength = 35000
                double openUsdPhysics:suspension:springDamperRate = 4500
                double openUsdPhysics:suspension:travelDistance = 0.25
                double openUsdPhysics:suspension:sprungMass = 375
                double openUsdPhysics:tire:longitudinalStiffness = 5000
                double openUsdPhysics:tire:camberStiffness = 0
                double openUsdPhysics:tire:restLoad = 0
            }

            def Xform "WheelRearLeft" (
                prepend apiSchemas = [
                    "OpenUsdPhysicsVehicleWheelAttachmentAPI",
                    "OpenUsdPhysicsVehicleWheelAPI",
                    "OpenUsdPhysicsVehicleSuspensionAPI",
                    "OpenUsdPhysicsVehicleTireAPI"
                ]
            )
            {
                int openUsdPhysics:wheelAttachment:index = 2
                float3 openUsdPhysics:wheelAttachment:suspensionFramePosition = (-0.9, 0, -1.4)
                float3 openUsdPhysics:wheelAttachment:suspensionTravelDirection = (0, -1, 0)
                float3 openUsdPhysics:wheelAttachment:wheelFramePosition = (0, 0, 0)
                double openUsdPhysics:wheel:radius = 0.35
                double openUsdPhysics:wheel:width = 0.3
                double openUsdPhysics:wheel:mass = 20
                double openUsdPhysics:wheel:momentOfInertia = 1.225
                double openUsdPhysics:wheel:dampingRate = 0.25
                double openUsdPhysics:suspension:springStrength = 35000
                double openUsdPhysics:suspension:springDamperRate = 4500
                double openUsdPhysics:suspension:travelDistance = 0.25
                double openUsdPhysics:suspension:sprungMass = 375
                double openUsdPhysics:tire:longitudinalStiffness = 5000
                double openUsdPhysics:tire:camberStiffness = 0
                double openUsdPhysics:tire:restLoad = 0
            }

            def Xform "WheelRearRight" (
                prepend apiSchemas = [
                    "OpenUsdPhysicsVehicleWheelAttachmentAPI",
                    "OpenUsdPhysicsVehicleWheelAPI",
                    "OpenUsdPhysicsVehicleSuspensionAPI",
                    "OpenUsdPhysicsVehicleTireAPI"
                ]
            )
            {
                int openUsdPhysics:wheelAttachment:index = 3
                float3 openUsdPhysics:wheelAttachment:suspensionFramePosition = (0.9, 0, -1.4)
                float3 openUsdPhysics:wheelAttachment:suspensionTravelDirection = (0, -1, 0)
                float3 openUsdPhysics:wheelAttachment:wheelFramePosition = (0, 0, 0)
                double openUsdPhysics:wheel:radius = 0.35
                double openUsdPhysics:wheel:width = 0.3
                double openUsdPhysics:wheel:mass = 20
                double openUsdPhysics:wheel:momentOfInertia = 1.225
                double openUsdPhysics:wheel:dampingRate = 0.25
                double openUsdPhysics:suspension:springStrength = 35000
                double openUsdPhysics:suspension:springDamperRate = 4500
                double openUsdPhysics:suspension:travelDistance = 0.25
                double openUsdPhysics:suspension:sprungMass = 375
                double openUsdPhysics:tire:longitudinalStiffness = 5000
                double openUsdPhysics:tire:camberStiffness = 0
                double openUsdPhysics:tire:restLoad = 0
            }
        }
        """;

    private const string MimicArticulationStage =
        """
        #usda 1.0
        (
            upAxis = "Y"
            metersPerUnit = 1
            kilogramsPerUnit = 1
            timeCodesPerSecond = 24
            startTimeCode = 0
            endTimeCode = 24
        )

        def PhysicsScene "Scene"
        {
            vector3f physics:gravityDirection = (0, -1, 0)
            float physics:gravityMagnitude = 9.81
        }

        def Xform "Articulation" (
            prepend apiSchemas = ["PhysicsArticulationRootAPI"]
        )
        {
            bool physics:articulationEnabled = 1

            def Sphere "Root" (
                prepend apiSchemas = ["PhysicsCollisionAPI", "PhysicsRigidBodyAPI", "PhysicsMassAPI"]
            )
            {
                double radius = 0.2
                float physics:mass = 1
                double3 xformOp:translate = (0, 5, 0)
                uniform token[] xformOpOrder = ["xformOp:translate"]
            }

            def PhysicsFixedJoint "RootAnchor"
            {
                rel physics:body1 = </Articulation/Root>
            }

            def Sphere "Link1" (
                prepend apiSchemas = ["PhysicsCollisionAPI", "PhysicsRigidBodyAPI", "PhysicsMassAPI"]
            )
            {
                double radius = 0.2
                float physics:mass = 1
                double3 xformOp:translate = (1, 5, 0)
                uniform token[] xformOpOrder = ["xformOp:translate"]
            }

            def Sphere "Link2" (
                prepend apiSchemas = ["PhysicsCollisionAPI", "PhysicsRigidBodyAPI", "PhysicsMassAPI"]
            )
            {
                double radius = 0.2
                float physics:mass = 1
                double3 xformOp:translate = (2, 5, 0)
                uniform token[] xformOpOrder = ["xformOp:translate"]
            }

            def PhysicsRevoluteJoint "Joint0"
            {
                uniform token physics:axis = "Z"
                rel physics:body0 = </Articulation/Root>
                rel physics:body1 = </Articulation/Link1>
                point3f physics:localPos0 = (0.5, 0, 0)
                point3f physics:localPos1 = (-0.5, 0, 0)
            }

            def PhysicsRevoluteJoint "Joint1" (
                prepend apiSchemas = ["OpenUsdPhysicsMimicJointAPI"]
            )
            {
                uniform token physics:axis = "Z"
                rel physics:body0 = </Articulation/Link1>
                rel physics:body1 = </Articulation/Link2>
                point3f physics:localPos0 = (0.5, 0, 0)
                point3f physics:localPos1 = (-0.5, 0, 0)

                bool openUsdPhysics:mimicJoint:enabled = 1
                uniform token openUsdPhysics:mimicJoint:axis = "rotZ"
                uniform token openUsdPhysics:mimicJoint:referenceAxis = "rotZ"
                rel openUsdPhysics:mimicJoint:referenceJoint = </Articulation/Joint0>
                double openUsdPhysics:mimicJoint:gearing = 1
                double openUsdPhysics:mimicJoint:offset = 0
            }
        }
        """;

    private const string FixedTendonArticulationStage =
        """
        #usda 1.0
        (
            upAxis = "Y"
            metersPerUnit = 1
            kilogramsPerUnit = 1
            timeCodesPerSecond = 24
            startTimeCode = 0
            endTimeCode = 24
        )

        def PhysicsScene "Scene"
        {
            vector3f physics:gravityDirection = (0, -1, 0)
            float physics:gravityMagnitude = 9.81
        }

        def Cube "Ground" (prepend apiSchemas = ["PhysicsCollisionAPI"])
        {
            double size = 2
            float3 xformOp:scale = (50, 1, 50)
            double3 xformOp:translate = (0, -1, 0)
            uniform token[] xformOpOrder = ["xformOp:translate", "xformOp:scale"]
        }

        def Xform "Articulation" (
            prepend apiSchemas = ["PhysicsArticulationRootAPI"]
        )
        {
            def Sphere "Root" (
                prepend apiSchemas = ["PhysicsCollisionAPI", "PhysicsRigidBodyAPI", "PhysicsMassAPI"]
            )
            {
                double radius = 0.2
                float physics:mass = 1
                double3 xformOp:translate = (0, 5, 0)
                uniform token[] xformOpOrder = ["xformOp:translate"]
            }

            def PhysicsFixedJoint "RootAnchor"
            {
                rel physics:body1 = </Articulation/Root>
            }

            def Sphere "Link1" (
                prepend apiSchemas = ["PhysicsCollisionAPI", "PhysicsRigidBodyAPI", "PhysicsMassAPI"]
            )
            {
                double radius = 0.2
                float physics:mass = 1
                double3 xformOp:translate = (1, 5, 0)
                uniform token[] xformOpOrder = ["xformOp:translate"]
            }

            def Sphere "Link2" (
                prepend apiSchemas = ["PhysicsCollisionAPI", "PhysicsRigidBodyAPI", "PhysicsMassAPI"]
            )
            {
                double radius = 0.2
                float physics:mass = 1
                double3 xformOp:translate = (2, 5, 0)
                uniform token[] xformOpOrder = ["xformOp:translate"]
            }

            def PhysicsRevoluteJoint "Joint0"
            {
                uniform token physics:axis = "Z"
                rel physics:body0 = </Articulation/Root>
                rel physics:body1 = </Articulation/Link1>
                point3f physics:localPos0 = (0.5, 0, 0)
                point3f physics:localPos1 = (-0.5, 0, 0)
            }

            def PhysicsRevoluteJoint "Joint1"
            {
                uniform token physics:axis = "Z"
                rel physics:body0 = </Articulation/Link1>
                rel physics:body1 = </Articulation/Link2>
                point3f physics:localPos0 = (0.5, 0, 0)
                point3f physics:localPos1 = (-0.5, 0, 0)
            }

            def OpenUsdPhysicsFixedTendon "Tendon"
            {
                bool openUsdPhysics:fixedTendon:enabled = 1
                rel openUsdPhysics:fixedTendon:articulation = </Articulation>
                rel openUsdPhysics:fixedTendon:rootJoint = </Articulation/Joint0>
                rel openUsdPhysics:fixedTendon:joints = [</Articulation/Joint1>]
                float[] openUsdPhysics:fixedTendon:gearings = [1, 1]
                float[] openUsdPhysics:fixedTendon:forceCoefficients = [1, 1]
                double openUsdPhysics:fixedTendon:stiffness = 4000
                double openUsdPhysics:fixedTendon:damping = 200
                double openUsdPhysics:fixedTendon:restLength = 0
            }
        }
        """;

    private const string SpatialTendonArticulationStage =
        """
        #usda 1.0
        (
            upAxis = "Y"
            metersPerUnit = 1
            kilogramsPerUnit = 1
            timeCodesPerSecond = 24
            startTimeCode = 0
            endTimeCode = 24
        )

        def PhysicsScene "Scene"
        {
            vector3f physics:gravityDirection = (0, -1, 0)
            float physics:gravityMagnitude = 9.81
        }

        def Cube "Ground" (prepend apiSchemas = ["PhysicsCollisionAPI"])
        {
            double size = 2
            float3 xformOp:scale = (50, 1, 50)
            double3 xformOp:translate = (0, -1, 0)
            uniform token[] xformOpOrder = ["xformOp:translate", "xformOp:scale"]
        }

        def Xform "Articulation" (
            prepend apiSchemas = ["PhysicsArticulationRootAPI"]
        )
        {
            def Sphere "Root" (
                prepend apiSchemas = [
                    "PhysicsCollisionAPI",
                    "PhysicsRigidBodyAPI",
                    "PhysicsMassAPI",
                    "OpenUsdPhysicsTendonAttachmentAPI"
                ]
            )
            {
                double radius = 0.2
                float physics:mass = 1
                double3 xformOp:translate = (0, 5, 0)
                uniform token[] xformOpOrder = ["xformOp:translate"]

                uniform token openUsdPhysics:tendonAttachment:role = "root"
                double openUsdPhysics:tendonAttachment:gearing = 1
                point3f openUsdPhysics:tendonAttachment:localPosition = (0, 1, 0)
            }

            def PhysicsFixedJoint "RootAnchor"
            {
                rel physics:body1 = </Articulation/Root>
            }

            def Sphere "Link1" (
                prepend apiSchemas = ["PhysicsCollisionAPI", "PhysicsRigidBodyAPI", "PhysicsMassAPI"]
            )
            {
                double radius = 0.2
                float physics:mass = 1
                double3 xformOp:translate = (1, 5, 0)
                uniform token[] xformOpOrder = ["xformOp:translate"]
            }

            def Sphere "Link2" (
                prepend apiSchemas = [
                    "PhysicsCollisionAPI",
                    "PhysicsRigidBodyAPI",
                    "PhysicsMassAPI",
                    "OpenUsdPhysicsTendonAttachmentAPI"
                ]
            )
            {
                double radius = 0.2
                float physics:mass = 1
                double3 xformOp:translate = (2, 5, 0)
                uniform token[] xformOpOrder = ["xformOp:translate"]

                uniform token openUsdPhysics:tendonAttachment:role = "leaf"
                double openUsdPhysics:tendonAttachment:gearing = 1
                point3f openUsdPhysics:tendonAttachment:localPosition = (0, 0, 0)
                double openUsdPhysics:tendonAttachment:restLength = 0.5
                rel openUsdPhysics:tendonAttachment:parentAttachment = </Articulation/Root>
            }

            def PhysicsRevoluteJoint "Joint0"
            {
                uniform token physics:axis = "Z"
                rel physics:body0 = </Articulation/Root>
                rel physics:body1 = </Articulation/Link1>
                point3f physics:localPos0 = (0.5, 0, 0)
                point3f physics:localPos1 = (-0.5, 0, 0)
            }

            def PhysicsRevoluteJoint "Joint1"
            {
                uniform token physics:axis = "Z"
                rel physics:body0 = </Articulation/Link1>
                rel physics:body1 = </Articulation/Link2>
                point3f physics:localPos0 = (0.5, 0, 0)
                point3f physics:localPos1 = (-0.5, 0, 0)
            }

            def OpenUsdPhysicsSpatialTendon "Tendon"
            {
                bool openUsdPhysics:spatialTendon:enabled = 1
                rel openUsdPhysics:spatialTendon:articulation = </Articulation>
                rel openUsdPhysics:spatialTendon:rootAttachment = </Articulation/Root>
                double openUsdPhysics:spatialTendon:stiffness = 4000
                double openUsdPhysics:spatialTendon:damping = 200
                double openUsdPhysics:spatialTendon:offset = 0
            }
        }
        """;

    private const string ArticulationStage =
        """
        #usda 1.0
        (
            upAxis = "Y"
            metersPerUnit = 1
            kilogramsPerUnit = 1
            timeCodesPerSecond = 24
            startTimeCode = 0
            endTimeCode = 24
        )

        def PhysicsScene "Scene"
        {
            vector3f physics:gravityDirection = (0, -1, 0)
            float physics:gravityMagnitude = 9.81
        }

        def Cube "Ground" (prepend apiSchemas = ["PhysicsCollisionAPI"])
        {
            double size = 2
            float3 xformOp:scale = (50, 1, 50)
            double3 xformOp:translate = (0, -1, 0)
            uniform token[] xformOpOrder = ["xformOp:translate", "xformOp:scale"]
        }

        def Xform "Articulation" (
            prepend apiSchemas = ["PhysicsArticulationRootAPI"]
        )
        {
            def Sphere "Root" (
                prepend apiSchemas = ["PhysicsCollisionAPI", "PhysicsRigidBodyAPI", "PhysicsMassAPI"]
            )
            {
                double radius = 0.2
                float physics:mass = 1
                bool physics:kinematicEnabled = 0
                double3 xformOp:translate = (0, 5, 0)
                uniform token[] xformOpOrder = ["xformOp:translate"]
            }

            def PhysicsFixedJoint "RootAnchor"
            {
                rel physics:body1 = </Articulation/Root>
            }

            def Sphere "Link1" (
                prepend apiSchemas = ["PhysicsCollisionAPI", "PhysicsRigidBodyAPI", "PhysicsMassAPI"]
            )
            {
                double radius = 0.2
                float physics:mass = 1
                double3 xformOp:translate = (1, 5, 0)
                uniform token[] xformOpOrder = ["xformOp:translate"]
            }

            def Sphere "Link2" (
                prepend apiSchemas = ["PhysicsCollisionAPI", "PhysicsRigidBodyAPI", "PhysicsMassAPI"]
            )
            {
                double radius = 0.2
                float physics:mass = 1
                double3 xformOp:translate = (2, 5, 0)
                uniform token[] xformOpOrder = ["xformOp:translate"]
            }

            def PhysicsRevoluteJoint "Joint0"
            {
                uniform token physics:axis = "Z"
                rel physics:body0 = </Articulation/Root>
                rel physics:body1 = </Articulation/Link1>
                point3f physics:localPos0 = (0.5, 0, 0)
                point3f physics:localPos1 = (-0.5, 0, 0)
            }

            def PhysicsRevoluteJoint "Joint1"
            {
                uniform token physics:axis = "Z"
                rel physics:body0 = </Articulation/Link1>
                rel physics:body1 = </Articulation/Link2>
                point3f physics:localPos0 = (0.5, 0, 0)
                point3f physics:localPos1 = (-0.5, 0, 0)
            }
        }
        """;
}
