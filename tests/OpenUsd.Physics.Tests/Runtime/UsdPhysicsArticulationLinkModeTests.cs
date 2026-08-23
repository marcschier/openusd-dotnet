// Copyright (c) marcschier. Licensed under the MIT License.

using OpenUsd.Physics.Interop;

namespace OpenUsd.Physics.Tests;

/// <summary>
/// Pins why a reduced-coordinate articulation link can never be handed a force mode the pinned
/// simulation SDK refuses.
/// </summary>
/// <remarks>
/// <para>
/// The SDK states on <c>PxRigidBody::addForce</c> that <c>PxForceMode::eIMPULSE</c> and
/// <c>PxForceMode::eVELOCITY_CHANGE</c> cannot be applied to an articulation link. The world's link
/// command path refuses the impulse commands by name, but a force or a torque could in principle
/// carry the same mode spelled as a modifier. It cannot, and this is what makes that true: the
/// command validator only offers <c>ModeVelocityChange</c> on the impulse command types, so an
/// <c>AddForce</c> that declares it is refused for the whole batch before any world sees it.
/// </para>
/// <para>
/// That invariant is load-bearing rather than incidental, because the link path relies on it
/// instead of re-checking the modifier. If the allowed-modifier table ever grew
/// <c>ModeVelocityChange</c> on <c>AddForce</c>, an articulation link would start handing the SDK a
/// mode it rejects, so it is pinned here against a real built world rather than against the table.
/// </para>
/// </remarks>
internal sealed class UsdPhysicsArticulationLinkModeTests
{
    private const string LinkPath = "/Articulation/Link2";

    [Test]
    public async Task AVelocityChangeModifierNeverReachesALinkBecauseTheBatchIsRefused()
    {
        CpuDomainFixture.RequireRuntime();
        using CpuDomainFixture fixture = CpuDomainFixture.Create(
            nameof(AVelocityChangeModifierNeverReachesALinkBecauseTheBatchIsRefused));
        fixture.WriteStage(ArticulationStage);

        using UsdPhysicsSimulation simulation = fixture.BuildSimulation();
        simulation.Step(30);
        _ = simulation.World.DrainDiagnostics();

        // The whole batch is refused, atomically, before the world applies any
        // of it. That is what keeps the link path safe without re-checking the
        // modifier at the point of use.
        PhysxCommand[] batch = [Force(Target(), PhysxCommandFlags.ModeVelocityChange)];
        await Assert.That(() => simulation.Step(1, batch)).Throws<InvalidOperationException>();

        await Assert.That((uint)PhysxCommandFlags.ModeVelocityChange &
                (uint)PhysxCommandAdapter.AllowedFlags(PhysxCommandType.AddForce))
            .IsEqualTo(0u);
        await Assert.That((uint)PhysxCommandFlags.ModeVelocityChange &
                (uint)PhysxCommandAdapter.AllowedFlags(PhysxCommandType.AddTorque))
            .IsEqualTo(0u);

        // It is offered on the impulse commands, and those are the ones the
        // link path refuses by name.
        await Assert.That((uint)PhysxCommandFlags.ModeVelocityChange &
                (uint)PhysxCommandAdapter.AllowedFlags(PhysxCommandType.AddImpulse))
            .IsNotEqualTo(0u);
    }

    [Test]
    public async Task AnAccelerationModifierIsAcceptedOnALink()
    {
        CpuDomainFixture.RequireRuntime();
        using CpuDomainFixture fixture = CpuDomainFixture.Create(
            nameof(AnAccelerationModifierIsAcceptedOnALink));
        fixture.WriteStage(ArticulationStage);

        using UsdPhysicsSimulation simulation = fixture.BuildSimulation();
        simulation.Step(30);
        _ = simulation.World.DrainDiagnostics();

        // Only the impulse and velocity change modes are excluded by the SDK, so
        // an acceleration must reach the link and move it.
        UsdPhysicsBodyPose start = Pose(simulation);
        simulation.Step(60, new[] { Force(Target(), PhysxCommandFlags.ModeAcceleration) });
        UsdPhysicsBodyPose pushed = Pose(simulation);

        IReadOnlyList<string> codes = Codes(simulation);
        await Assert.That(codes).DoesNotContain("OPENUSD_PHYSICS_COMMAND_REJECTED");
        await Assert.That(codes).DoesNotContain("OPENUSD_PHYSICS_COMMAND_TARGET_MISSING");
        await Assert.That(Math.Abs(pushed.Position.X - start.Position.X)).IsGreaterThan(0.01);
    }

    [Test]
    public async Task AnImpulseIsRefusedOnALinkWithItsOwnReason()
    {
        CpuDomainFixture.RequireRuntime();
        using CpuDomainFixture fixture = CpuDomainFixture.Create(
            nameof(AnImpulseIsRefusedOnALinkWithItsOwnReason));
        fixture.WriteStage(ArticulationStage);

        using UsdPhysicsSimulation simulation = fixture.BuildSimulation();
        simulation.Step(30);
        _ = simulation.World.DrainDiagnostics();

        // An impulse is a legal command that a link cannot express, so it is
        // refused per object rather than refusing the batch or reaching the SDK.
        simulation.Step(1, new[] { new PhysxCommand
        {
            TargetId = Target(),
            Type = (uint)PhysxCommandType.AddImpulse,
            Flags = 0,
            Vector = new PhysxVec3f(400.0F, 0.0F, 0.0F),
            Point = default,
        } });

        IReadOnlyList<string> codes = Codes(simulation);
        await Assert.That(codes).Contains("OPENUSD_PHYSICS_COMMAND_REJECTED");
        await Assert.That(codes).DoesNotContain("OPENUSD_PHYSICS_COMMAND_TARGET_MISSING");
    }

    private static ulong Target() =>
        UsdPhysicsIdentities.ForSimulatedObject(LinkPath, UsdPhysicsObjectKind.RigidBody).Value;

    private static UsdPhysicsBodyPose Pose(UsdPhysicsSimulation simulation)
    {
        ulong expected = Target();
        foreach (UsdPhysicsBodyPose pose in simulation.Frame.Bodies)
        {
            if (pose.Id.Value == expected)
            {
                return pose;
            }
        }

        throw new InvalidOperationException($"{LinkPath} published no pose.");
    }

    private static PhysxCommand Force(ulong target, PhysxCommandFlags flags) => new()
    {
        TargetId = target,
        Type = (uint)PhysxCommandType.AddForce,
        Flags = (uint)flags,
        Vector = new PhysxVec3f(400.0F, 0.0F, 0.0F),
        Point = default,
    };

    private static IReadOnlyList<string> Codes(UsdPhysicsSimulation simulation) =>
        [.. simulation.World.DrainDiagnostics().Entries.Select(static entry => entry.Code)];
    /// <summary>An articulation whose links are addressable by their own prim paths.</summary>
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
}
