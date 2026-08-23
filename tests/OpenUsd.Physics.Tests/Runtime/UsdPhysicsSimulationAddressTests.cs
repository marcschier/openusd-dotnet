// Copyright (c) marcschier. Licensed under the MIT License.

using OpenUsd.Physics.Interop;

namespace OpenUsd.Physics.Tests;

/// <summary>
/// Proves that <see cref="UsdPhysicsIdentities.ForSimulatedObject"/> names the identity the
/// retained world actually holds, by driving the real world with commands built from it.
/// </summary>
/// <remarks>
/// <para>
/// The composer gives a character controller, a collision shape, and a vehicle addresses of their
/// own, and only a rigid actor is addressed by its plain prim path. A caller that used the plain
/// path for all of them would build commands the world silently refuses, which looks exactly like
/// a simulation that ignores its input. Nothing but a real composition can prove the published
/// mapping and the composer still agree, so every assertion here runs against a built world.
/// </para>
/// <para>
/// The negative half matters as much as the positive half: sending the wrong address has to be
/// observably refused, or a test that only checks the right address cannot tell a working mapping
/// from a world that accepts anything.
/// </para>
/// </remarks>
public sealed class UsdPhysicsSimulationAddressTests
{
    private const string Stage =
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

        def Cube "Box" (
            prepend apiSchemas = ["PhysicsCollisionAPI", "PhysicsRigidBodyAPI", "PhysicsMassAPI"]
        )
        {
            double size = 1
            float physics:mass = 4
            bool physics:sleepEnabled = 0
            double3 xformOp:translate = (0, 2, 0)
            uniform token[] xformOpOrder = ["xformOp:translate"]
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
            double3 xformOp:translate = (6, 2, 0)
            uniform token[] xformOpOrder = ["xformOp:translate"]
        }
        """;

    [Test]
    public async Task TheAddressOfEachComposedKindDiffersFromThePlainPrimIdentity()
    {
        // No runtime is needed to state the contract, only to prove the world agrees with it.
        ulong plain = UsdPhysicsIdentities.FromPrimPath("/Walker").Value;
        ulong body = UsdPhysicsIdentities
            .ForSimulatedObject("/Walker", UsdPhysicsObjectKind.RigidBody).Value;
        ulong controller = UsdPhysicsIdentities
            .ForSimulatedObject("/Walker", UsdPhysicsObjectKind.Controller).Value;
        ulong vehicle = UsdPhysicsIdentities
            .ForSimulatedObject("/Walker", UsdPhysicsObjectKind.Vehicle).Value;
        ulong collider = UsdPhysicsIdentities
            .ForSimulatedObject("/Walker", UsdPhysicsObjectKind.Collider).Value;

        await Assert.That(body).IsEqualTo(plain);
        await Assert.That(controller).IsNotEqualTo(plain);
        await Assert.That(vehicle).IsNotEqualTo(plain);
        await Assert.That(collider).IsNotEqualTo(plain);
        await Assert.That(controller).IsNotEqualTo(vehicle);
        await Assert.That(controller).IsNotEqualTo(collider);
        await Assert.That(vehicle).IsNotEqualTo(collider);

        // A kind the composer does not address from a path alone must say so rather than return a
        // plausible looking identity the world will never contain.
        await Assert.That(
                UsdPhysicsIdentities
                    .ForSimulatedObject("/Walker", UsdPhysicsObjectKind.Unknown).IsNone)
            .IsTrue();
    }

    [Test]
    public async Task TheControllerAddressMovesTheControllerAndThePrimAddressDoesNot()
    {
        CpuDomainFixture.RequireRuntime();

        using CpuDomainFixture fixture = CpuDomainFixture.Create(nameof(
            TheControllerAddressMovesTheControllerAndThePrimAddressDoesNot));
        fixture.WriteStage(Stage);

        using UsdPhysicsSimulation simulation = fixture.BuildSimulation();
        await Assert.That(simulation.Build.Succeeded).IsTrue();
        await Assert.That(simulation.Composition!.Controllers).IsEqualTo(1);

        simulation.Step(60);
        _ = simulation.World.DrainDiagnostics();
        UsdPhysicsBodyPose landed = RequirePose(simulation, "/Walker", UsdPhysicsObjectKind.Controller);

        // The plain prim identity is the address of a rigid actor, and this prim composed none, so
        // the world must refuse the move and the controller must not budge.
        PhysxCommand wrong = Move(UsdPhysicsIdentities.FromPrimPath("/Walker").Value);
        simulation.Step(30, new[] { wrong });
        UsdPhysicsBodyPose unmoved = RequirePose(simulation, "/Walker", UsdPhysicsObjectKind.Controller);
        await Assert.That(Math.Abs(unmoved.Position.X - landed.Position.X)).IsLessThan(0.05);
        await Assert.That(DiagnosticCodes(simulation)).Contains(TargetMissingCode);

        // The composed address is the one the world holds, so the same command reaches it.
        PhysxCommand right = Move(
            UsdPhysicsIdentities
                .ForSimulatedObject("/Walker", UsdPhysicsObjectKind.Controller).Value);
        simulation.Step(60, new[] { right });
        UsdPhysicsBodyPose walked = RequirePose(simulation, "/Walker", UsdPhysicsObjectKind.Controller);
        await Assert.That(walked.Position.X - unmoved.Position.X).IsGreaterThan(0.5);
        await Assert.That(DiagnosticCodes(simulation)).DoesNotContain(TargetMissingCode);
    }

    [Test]
    public async Task TheBodyAddressTakesForcesAndTheColliderAddressIsRefused()
    {
        CpuDomainFixture.RequireRuntime();

        using CpuDomainFixture fixture = CpuDomainFixture.Create(nameof(
            TheBodyAddressTakesForcesAndTheColliderAddressIsRefused));
        fixture.WriteStage(Stage);

        using UsdPhysicsSimulation simulation = fixture.BuildSimulation();
        await Assert.That(simulation.Build.Succeeded).IsTrue();

        simulation.Step(30);
        _ = simulation.World.DrainDiagnostics();
        UsdPhysicsBodyPose start = RequirePose(simulation, "/Box", UsdPhysicsObjectKind.RigidBody);

        // A collider composes into a shape, and a shape is not something a force can be applied to.
        // This is exactly why the viewer resolves a collider section to the body that owns it.
        PhysxCommand shape = Push(
            UsdPhysicsIdentities.ForSimulatedObject("/Box", UsdPhysicsObjectKind.Collider).Value);
        simulation.Step(30, new[] { shape });
        UsdPhysicsBodyPose unpushed = RequirePose(simulation, "/Box", UsdPhysicsObjectKind.RigidBody);
        await Assert.That(Math.Abs(unpushed.Position.X - start.Position.X)).IsLessThan(0.05);
        await Assert.That(DiagnosticCodes(simulation)).Contains(TargetMissingCode);

        PhysxCommand actor = Push(
            UsdPhysicsIdentities.ForSimulatedObject("/Box", UsdPhysicsObjectKind.RigidBody).Value);
        simulation.Step(60, new[] { actor });
        UsdPhysicsBodyPose pushed = RequirePose(simulation, "/Box", UsdPhysicsObjectKind.RigidBody);
        await Assert.That(pushed.Position.X - unpushed.Position.X).IsGreaterThan(0.1);
        await Assert.That(DiagnosticCodes(simulation)).DoesNotContain(TargetMissingCode);
    }

    [Test]
    public async Task AnArticulationLinkAcceptsBodyCommandsAtItsPlainPrimAddress()
    {
        CpuDomainFixture.RequireRuntime();

        using CpuDomainFixture fixture = CpuDomainFixture.Create(nameof(
            AnArticulationLinkAcceptsBodyCommandsAtItsPlainPrimAddress));
        fixture.WriteStage(ArticulationStage);

        using UsdPhysicsSimulation simulation = fixture.BuildSimulation();
        await Assert.That(simulation.Build.Succeeded).IsTrue();
        await Assert.That(simulation.Composition!.Articulations).IsGreaterThanOrEqualTo(1);

        // A link is composed into the articulation rather than into the actor table, but it is
        // still addressed by its own prim path, and the viewer offers force and drag on it. If the
        // world cannot find that address the interaction is refused and nothing moves, which is
        // indistinguishable from a simulation that ignores its input.
        simulation.Step(30);
        _ = simulation.World.DrainDiagnostics();
        UsdPhysicsBodyPose start = RequirePose(
            simulation, "/Articulation/Link2", UsdPhysicsObjectKind.RigidBody);

        ulong target = UsdPhysicsIdentities
            .ForSimulatedObject("/Articulation/Link2", UsdPhysicsObjectKind.RigidBody).Value;
        PhysxCommand[] push = [Push(target)];
        simulation.Step(60, push);

        UsdPhysicsBodyPose pushed = RequirePose(
            simulation, "/Articulation/Link2", UsdPhysicsObjectKind.RigidBody);
        await Assert.That(DiagnosticCodes(simulation)).DoesNotContain(TargetMissingCode);
        await Assert.That(pushed.Position.X - start.Position.X).IsGreaterThan(0.05);
    }

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

        def Sphere "Free" (
            prepend apiSchemas = ["PhysicsCollisionAPI", "PhysicsRigidBodyAPI", "PhysicsMassAPI"]
        )
        {
            double radius = 0.2
            float physics:mass = 1
            bool physics:sleepEnabled = 0
            double3 xformOp:translate = (6, 5, 0)
            uniform token[] xformOpOrder = ["xformOp:translate"]
        }

        def Xform "Articulation" (
            prepend apiSchemas = ["PhysicsArticulationRootAPI"]
        )
        {
            def Sphere "Root" (
                prepend apiSchemas = ["PhysicsCollisionAPI", "PhysicsRigidBodyAPI", "PhysicsMassAPI"]
            )            {
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

    [Test]
    public async Task ALinkCommandReachesTheLinkPathAndNotTheActorPath()
    {
        CpuDomainFixture.RequireRuntime();

        using CpuDomainFixture fixture = CpuDomainFixture.Create(nameof(
            ALinkCommandReachesTheLinkPathAndNotTheActorPath));
        fixture.WriteStage(ArticulationStage);

        using UsdPhysicsSimulation simulation = fixture.BuildSimulation();
        await Assert.That(simulation.Build.Succeeded).IsTrue();
        simulation.Step(20);
        _ = simulation.World.DrainDiagnostics();

        ulong link = UsdPhysicsIdentities
            .ForSimulatedObject("/Articulation/Link2", UsdPhysicsObjectKind.RigidBody).Value;

        // This is the discriminator. A dynamic ACTOR accepts a linear velocity silently; the
        // articulation link path refuses it with a rejection diagnostic. Seeing the rejection is
        // the only way to prove the command reached ApplyArticulationLinkCommand rather than
        // the actor branch, and seeing no missing-target proves it resolved at all.
        simulation.Step(1, new[] { Velocity(link) });
        string codes = DiagnosticCodes(simulation);
        await Assert.That(codes).Contains(RejectedCode);
        await Assert.That(codes).DoesNotContain(TargetMissingCode);

        // The control. /Free is a dynamic rigid body outside every articulation, so it is composed
        // into the actor table. The SAME command must be accepted there and must actually move it.
        // Without this the rejection above could just as well mean "the world refuses this command
        // everywhere", which would prove nothing about the link routing.
        ulong free = UsdPhysicsIdentities
            .ForSimulatedObject("/Free", UsdPhysicsObjectKind.RigidBody).Value;
        await Assert.That(free).IsNotEqualTo(link);

        _ = simulation.World.DrainDiagnostics();
        UsdPhysicsBodyPose before = RequirePose(simulation, "/Free", UsdPhysicsObjectKind.RigidBody);
        simulation.Step(10, new[] { Velocity(free) });
        UsdPhysicsBodyPose after = RequirePose(simulation, "/Free", UsdPhysicsObjectKind.RigidBody);

        string freeCodes = DiagnosticCodes(simulation);
        await Assert.That(freeCodes).DoesNotContain(RejectedCode);
        await Assert.That(freeCodes).DoesNotContain(TargetMissingCode);
        await Assert.That(after.Position.X - before.Position.X).IsGreaterThan(0.05);
    }

    [Test]
    public async Task AnArticulationLinkRefusesEveryCommandItCannotExpress()
    {
        CpuDomainFixture.RequireRuntime();

        using CpuDomainFixture fixture = CpuDomainFixture.Create(nameof(
            AnArticulationLinkRefusesEveryCommandItCannotExpress));
        fixture.WriteStage(ArticulationStage);

        using UsdPhysicsSimulation simulation = fixture.BuildSimulation();
        await Assert.That(simulation.Build.Succeeded).IsTrue();
        simulation.Step(20);

        ulong link = UsdPhysicsIdentities
            .ForSimulatedObject("/Articulation/Link2", UsdPhysicsObjectKind.RigidBody).Value;

        // PhysX documents that the impulse and velocity change force modes cannot be applied to an
        // articulation link, and that a link's velocity and pose follow its joint degrees of
        // freedom. Every one of these has to be refused with a reason, not handed to the SDK.
        foreach (PhysxCommand refused in new[]
        {
            Command(link, PhysxCommandType.AddImpulse),
            Command(link, PhysxCommandType.AddAngularImpulse),
            Command(link, PhysxCommandType.AddImpulseAtPoint),
            Velocity(link),
            Command(link, PhysxCommandType.SetAngularVelocity),
            Teleport(link),
        })
        {
            _ = simulation.World.DrainDiagnostics();
            simulation.Step(1, new[] { refused });
            string codes = DiagnosticCodes(simulation);
            await Assert.That(codes).Contains(RejectedCode);
            await Assert.That(codes).DoesNotContain(TargetMissingCode);
        }

        // And everything it can express is accepted silently.
        foreach (PhysxCommand accepted in new[]
        {
            Push(link),
            Command(link, PhysxCommandType.AddTorque),
            Command(link, PhysxCommandType.ClearForce),
            Command(link, PhysxCommandType.ClearTorque),
            Command(link, PhysxCommandType.Wake),
            Command(link, PhysxCommandType.Sleep),
        })
        {
            _ = simulation.World.DrainDiagnostics();
            simulation.Step(1, new[] { accepted });
            string codes = DiagnosticCodes(simulation);
            await Assert.That(codes).DoesNotContain(RejectedCode);
            await Assert.That(codes).DoesNotContain(TargetMissingCode);
        }
    }

    private static PhysxCommand Command(ulong target, PhysxCommandType type) => new()
    {
        TargetId = target,
        Type = (uint)type,
        Flags = 0,
        Vector = UsesVector(type) ? new PhysxVec3f(1.0F, 0.0F, 0.0F) : default,
        Point = default,
    };

    /// <summary>Mirrors the native validator's rule about which types read a vector.</summary>
    /// <remarks>
    /// A command that carries a vector its type does not read is refused by the whole-batch
    /// validator before it reaches the world, so a probe has to leave the vector zero for the
    /// types that do not read one or it would be testing the validator instead of the routing.
    /// </remarks>
    private static bool UsesVector(PhysxCommandType type) => type is
        PhysxCommandType.SetLinearVelocity or
        PhysxCommandType.SetAngularVelocity or
        PhysxCommandType.AddForce or
        PhysxCommandType.AddTorque or
        PhysxCommandType.AddImpulse or
        PhysxCommandType.AddAngularImpulse or
        PhysxCommandType.AddForceAtPoint or
        PhysxCommandType.AddImpulseAtPoint or
        PhysxCommandType.SetSceneGravity or
        PhysxCommandType.MoveController or
        PhysxCommandType.VehicleInput;

    private static PhysxCommand Velocity(ulong target) => new()
    {
        TargetId = target,
        Type = (uint)PhysxCommandType.SetLinearVelocity,
        Flags = 0,
        Vector = new PhysxVec3f(1.0F, 0.0F, 0.0F),
        Point = default,
    };

    private static PhysxCommand Teleport(ulong target) => new()
    {
        TargetId = target,
        Type = (uint)PhysxCommandType.Teleport,
        Flags = 0,
        Vector = default,
        Point = default,
        Pose = new PhysxTransform(new PhysxVec3f(0.0F, 6.0F, 0.0F), PhysxQuatf.Identity),
    };

    /// <summary>The exact diagnostic codes the world emits, not a substring of them.</summary>
    /// <remarks>
    /// The helper below joins the codes into one string, so a bare substring would match today.
    /// It would silently stop matching the moment that helper returned a collection instead, where
    /// the assertion becomes an exact element comparison - an assertion that passes for the wrong
    /// reason is worse than no assertion, so the full code is spelled out.
    /// </remarks>
    private const string TargetMissingCode = "OPENUSD_PHYSICS_COMMAND_TARGET_MISSING";

    private const string RejectedCode = "OPENUSD_PHYSICS_COMMAND_REJECTED";

    private static PhysxCommand Move(ulong target) => new()
    {
        TargetId = target,
        Type = (uint)PhysxCommandType.MoveController,
        Flags = 0,
        Vector = new PhysxVec3f(0.02F, 0.0F, 0.0F),
        Point = default,
    };

    private static PhysxCommand Push(ulong target) => new()
    {
        TargetId = target,
        Type = (uint)PhysxCommandType.AddForce,
        Flags = 0,
        Vector = new PhysxVec3f(400.0F, 0.0F, 0.0F),
        Point = default,
    };

    private static UsdPhysicsBodyPose RequirePose(
        UsdPhysicsSimulation simulation,
        string primPath,
        UsdPhysicsObjectKind kind)
    {
        ulong expected = UsdPhysicsIdentities.ForSimulatedObject(primPath, kind).Value;
        foreach (UsdPhysicsBodyPose pose in simulation.Frame.Bodies)
        {
            if (pose.Id.Value == expected)
            {
                return pose;
            }
        }

        throw new InvalidOperationException(
            $"The world published no body for the composed {kind} at '{primPath}'.");
    }

    private static string DiagnosticCodes(UsdPhysicsSimulation simulation) =>
        string.Join(
            "; ",
            simulation.World.DrainDiagnostics().Entries.Select(entry => entry.Code));
}
