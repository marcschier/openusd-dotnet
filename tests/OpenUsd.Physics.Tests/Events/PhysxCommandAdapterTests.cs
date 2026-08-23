// Copyright (c) marcschier. Licensed under the MIT License.

using OpenUsd.Physics.Interop;

namespace OpenUsd.Physics.Tests.Events;

public sealed class PhysxCommandAdapterTests
{
    private static readonly UsdPhysicsObjectId Target = new(42, UsdPhysicsObjectKind.RigidBody);

    [Test]
    public async Task AVectorForceTranslatesWithoutAMagnitudeModifier()
    {
        var command = new UsdPhysicsCommand(UsdPhysicsCommandKind.Force, Target, new UsdVec3d(0, -9.81, 0));

        await Assert.That(
            PhysxCommandAdapter.TryTranslate(command, out PhysxCommand native, out string? rejection))
            .IsTrue();
        await Assert.That(rejection).IsNull();
        await Assert.That(native.Type).IsEqualTo((uint)PhysxCommandType.AddForce);
        await Assert.That(native.Flags).IsEqualTo((uint)PhysxCommandFlags.None);
        await Assert.That(native.TargetId).IsEqualTo(42UL);
        await Assert.That(native.Vector.Y).IsEqualTo(-9.81F);
        await Assert.That(native.Scalar).IsEqualTo(0F);
    }

    [Test]
    public async Task AMagnitudeForceCarriesTheDirectionAndTheScalarSeparately()
    {
        var command = new UsdPhysicsCommand(UsdPhysicsCommandKind.Force, Target, new UsdVec3d(0, 1, 0), 250);

        await Assert.That(PhysxCommandAdapter.TryTranslate(command, out PhysxCommand native, out _)).IsTrue();
        await Assert.That(native.Flags).IsEqualTo((uint)PhysxCommandFlags.Magnitude);
        await Assert.That(native.Scalar).IsEqualTo(250F);
        await Assert.That(native.Vector.Y).IsEqualTo(1F);
    }

    [Test]
    public async Task ApplicationPointModesSelectTheMatchingNativeCommand()
    {
        var center = new UsdPhysicsCommand(UsdPhysicsCommandKind.Impulse, Target, new UsdVec3d(1, 0, 0));
        var world = center with { Application = UsdPhysicsApplicationPoint.World, Point = new UsdVec3d(0, 2, 0) };
        var local = center with { Application = UsdPhysicsApplicationPoint.Local, Point = new UsdVec3d(0, 0, 3) };

        await Assert.That(PhysxCommandAdapter.TryTranslate(center, out PhysxCommand nativeCenter, out _)).IsTrue();
        await Assert.That(PhysxCommandAdapter.TryTranslate(world, out PhysxCommand nativeWorld, out _)).IsTrue();
        await Assert.That(PhysxCommandAdapter.TryTranslate(local, out PhysxCommand nativeLocal, out _)).IsTrue();

        await Assert.That(nativeCenter.Type).IsEqualTo((uint)PhysxCommandType.AddImpulse);
        await Assert.That(nativeWorld.Type).IsEqualTo((uint)PhysxCommandType.AddImpulseAtPoint);
        await Assert.That(nativeWorld.Flags).IsEqualTo((uint)PhysxCommandFlags.None);
        await Assert.That(nativeWorld.Point.Y).IsEqualTo(2F);
        await Assert.That(nativeLocal.Type).IsEqualTo((uint)PhysxCommandType.AddImpulseAtPoint);
        await Assert.That(nativeLocal.Flags).IsEqualTo((uint)PhysxCommandFlags.PointLocal);
        await Assert.That(nativeLocal.Point.Z).IsEqualTo(3F);
    }

    [Test]
    public async Task ForceModesMapOntoTheModifierTheirCommandTypeAccepts()
    {
        var acceleration = new UsdPhysicsCommand(UsdPhysicsCommandKind.Force, Target, new UsdVec3d(1, 0, 0))
        {
            Mode = UsdPhysicsForceMode.Acceleration
        };
        var velocityChange = new UsdPhysicsCommand(UsdPhysicsCommandKind.Impulse, Target, new UsdVec3d(1, 0, 0))
        {
            Mode = UsdPhysicsForceMode.VelocityChange
        };

        await Assert.That(
            PhysxCommandAdapter.TryTranslate(acceleration, out PhysxCommand nativeAcceleration, out _))
            .IsTrue();
        await Assert.That(
            PhysxCommandAdapter.TryTranslate(velocityChange, out PhysxCommand nativeVelocity, out _))
            .IsTrue();

        await Assert.That(nativeAcceleration.Flags).IsEqualTo((uint)PhysxCommandFlags.ModeAcceleration);
        await Assert.That(nativeVelocity.Flags).IsEqualTo((uint)PhysxCommandFlags.ModeVelocityChange);
    }

    [Test]
    public async Task AForceModeThatItsCommandTypeDoesNotAcceptIsRejected()
    {
        var command = new UsdPhysicsCommand(UsdPhysicsCommandKind.Force, Target, new UsdVec3d(1, 0, 0))
        {
            Mode = UsdPhysicsForceMode.VelocityChange
        };

        await Assert.That(PhysxCommandAdapter.TryTranslate(command, out _, out string? rejection)).IsFalse();
        await Assert.That(rejection).IsNotNull();
    }

    [Test]
    public async Task NotWakingTheTargetIsCarriedAsAModifier()
    {
        var command = new UsdPhysicsCommand(UsdPhysicsCommandKind.Torque, Target, new UsdVec3d(0, 0, 1))
        {
            WakeTarget = false
        };

        await Assert.That(PhysxCommandAdapter.TryTranslate(command, out PhysxCommand native, out _)).IsTrue();
        await Assert.That(native.Flags).IsEqualTo((uint)PhysxCommandFlags.NoWake);
        await Assert.That(native.Type).IsEqualTo((uint)PhysxCommandType.AddTorque);
    }

    [Test]
    public async Task ClearCommandsCarryNoVectorPointOrModifier()
    {
        var clearForce = new UsdPhysicsCommand(UsdPhysicsCommandKind.ClearForce, Target, default);
        var clearTorque = new UsdPhysicsCommand(UsdPhysicsCommandKind.ClearTorque, Target, default);

        await Assert.That(PhysxCommandAdapter.TryTranslate(clearForce, out PhysxCommand nativeForce, out _)).IsTrue();
        await Assert.That(PhysxCommandAdapter.TryTranslate(clearTorque, out PhysxCommand nativeTorque, out _)).IsTrue();

        await Assert.That(nativeForce.Type).IsEqualTo((uint)PhysxCommandType.ClearForce);
        await Assert.That(nativeTorque.Type).IsEqualTo((uint)PhysxCommandType.ClearTorque);
        await Assert.That(nativeForce.Flags).IsEqualTo(0u);
        await Assert.That(nativeForce.Vector.X).IsEqualTo(0F);
    }

    [Test]
    public async Task StagingPreservesSubmissionOrderSoAClearAfterAnAddWins()
    {
        UsdPhysicsCommand[] batch =
        [
            new(UsdPhysicsCommandKind.Force, Target, new UsdVec3d(0, 100, 0)),
            new(UsdPhysicsCommandKind.Impulse, Target, new UsdVec3d(5, 0, 0)),
            new(UsdPhysicsCommandKind.ClearForce, Target, default)
        ];
        var staged = new PhysxCommand[8];

        bool accepted = PhysxCommandAdapter.TryTranslateBatch(
            batch,
            staged,
            out int acceptedCount,
            out int rejectedIndex,
            out string? rejection);

        await Assert.That(accepted).IsTrue();
        await Assert.That(acceptedCount).IsEqualTo(3);
        await Assert.That(rejectedIndex).IsEqualTo(-1);
        await Assert.That(rejection).IsNull();
        await Assert.That(staged[0].Type).IsEqualTo((uint)PhysxCommandType.AddForce);
        await Assert.That(staged[1].Type).IsEqualTo((uint)PhysxCommandType.AddImpulse);
        await Assert.That(staged[2].Type).IsEqualTo((uint)PhysxCommandType.ClearForce);
    }

    [Test]
    public async Task ABatchLargerThanTheStagedCapacityIsRejectedWithoutWriting()
    {
        UsdPhysicsCommand[] batch =
        [
            new(UsdPhysicsCommandKind.Wake, Target, default),
            new(UsdPhysicsCommandKind.Sleep, Target, default)
        ];
        var staged = new PhysxCommand[1];

        bool accepted = PhysxCommandAdapter.TryTranslateBatch(
            batch, staged, out int count, out int index, out string? rejection);

        await Assert.That(accepted).IsFalse();
        await Assert.That(count).IsEqualTo(0);
        await Assert.That(index).IsEqualTo(1);
        await Assert.That(rejection).IsNotNull();
        await Assert.That(staged[0].Type).IsEqualTo(0u);
    }

    [Test]
    public async Task TheFirstRejectedCommandStopsTheBatchAndIsReported()
    {
        UsdPhysicsCommand[] batch =
        [
            new(UsdPhysicsCommandKind.Force, Target, new UsdVec3d(1, 0, 0)),
            new(UsdPhysicsCommandKind.Wake, Target, new UsdVec3d(1, 0, 0))
        ];
        var staged = new PhysxCommand[4];

        bool accepted = PhysxCommandAdapter.TryTranslateBatch(
            batch, staged, out int count, out int index, out string? rejection);

        await Assert.That(accepted).IsFalse();

        // Nothing is staged when anything is refused. A partially staged batch would leave the
        // destination holding commands the caller was told were rejected.
        await Assert.That(count).IsEqualTo(0);
        await Assert.That(index).IsEqualTo(1);
        await Assert.That(rejection).IsNotNull();
    }

    [Test]
    [Arguments(UsdPhysicsCommandKind.KinematicTarget)]
    [Arguments(UsdPhysicsCommandKind.Teleport)]
    public async Task KindsThatTheRetainedWorldCommandAbiDoesNotCarryAreRejected(UsdPhysicsCommandKind kind)
    {
        // Both remaining kinds carry an absolute pose, which the public command record does not
        // express, so translating one would have to invent an orientation the caller never gave.
        var command = new UsdPhysicsCommand(kind, Target, default);

        await Assert.That(PhysxCommandAdapter.TryTranslate(command, out _, out string? rejection)).IsFalse();
        await Assert.That(rejection).IsNotNull();
    }

    [Test]
    [Arguments(UsdPhysicsCommandKind.ControllerMove)]
    [Arguments(UsdPhysicsCommandKind.VehicleInput)]
    public async Task InteractiveDriveKindsAreCarriedByTheRetainedWorldCommandAbi(UsdPhysicsCommandKind kind)
    {
        // The interactive viewer drives character controllers and vehicles through these two, so a
        // translation that refused them would leave those controls with nowhere to send input.
        var command = new UsdPhysicsCommand(kind, Target, default);

        await Assert.That(PhysxCommandAdapter.TryTranslate(command, out _, out string? rejection)).IsTrue();
        await Assert.That(rejection).IsNull();
    }

    [Test]
    public async Task TheReservedZeroIdentityIsRejected()
    {
        var command = new UsdPhysicsCommand(UsdPhysicsCommandKind.Wake, UsdPhysicsObjectId.None, default);

        await Assert.That(PhysxCommandAdapter.TryTranslate(command, out _, out string? rejection)).IsFalse();
        await Assert.That(rejection).IsNotNull();
    }

    [Test]
    public async Task ANonFiniteVectorIsRejected()
    {
        var command = new UsdPhysicsCommand(UsdPhysicsCommandKind.Force, Target, new UsdVec3d(double.NaN, 0, 0));

        await Assert.That(PhysxCommandAdapter.TryTranslate(command, out _, out string? rejection)).IsFalse();
        await Assert.That(rejection).IsNotNull();
    }

    [Test]
    public async Task AMagnitudeWithAZeroLengthDirectionIsRejected()
    {
        var command = new UsdPhysicsCommand(UsdPhysicsCommandKind.Impulse, Target, default, 12);

        await Assert.That(PhysxCommandAdapter.TryTranslate(command, out _, out string? rejection)).IsFalse();
        await Assert.That(rejection).IsNotNull();
    }

    [Test]
    public async Task AVectorOnACommandThatDoesNotReadOneIsRejected()
    {
        var command = new UsdPhysicsCommand(UsdPhysicsCommandKind.ClearTorque, Target, new UsdVec3d(0, 1, 0));

        await Assert.That(PhysxCommandAdapter.TryTranslate(command, out _, out string? rejection)).IsFalse();
        await Assert.That(rejection).IsNotNull();
    }

    [Test]
    public async Task AnApplicationPointOnACommandThatDoesNotReadOneIsRejected()
    {
        var command = new UsdPhysicsCommand(UsdPhysicsCommandKind.Torque, Target, new UsdVec3d(0, 1, 0))
        {
            Point = new UsdVec3d(1, 1, 1)
        };

        await Assert.That(PhysxCommandAdapter.TryTranslate(command, out _, out string? rejection)).IsFalse();
        await Assert.That(rejection).IsNotNull();
    }

    [Test]
    public async Task ANonFiniteMagnitudeOrPointIsRejectedByTheContractItself()
    {
        await Assert.That(() => new UsdPhysicsCommand(
                UsdPhysicsCommandKind.Force, Target, new UsdVec3d(0, 1, 0), double.PositiveInfinity))
            .Throws<ArgumentOutOfRangeException>();
        await Assert.That(() => new UsdPhysicsCommand(UsdPhysicsCommandKind.Force, Target, new UsdVec3d(0, 1, 0))
        {
            Point = new UsdVec3d(double.NaN, 0, 0)
        }).Throws<ArgumentOutOfRangeException>();
        await Assert.That(() => new UsdPhysicsCommand((UsdPhysicsCommandKind)999, Target, default))
            .Throws<ArgumentOutOfRangeException>();
    }

    [Test]
    public async Task TheAllowedModifierSetMatchesTheNativeContract()
    {
        await Assert.That(PhysxCommandAdapter.AllowedFlags(PhysxCommandType.Wake)).IsEqualTo(PhysxCommandFlags.None);
        await Assert.That(
            PhysxCommandAdapter.AllowedFlags(PhysxCommandType.KinematicTarget))
            .IsEqualTo(PhysxCommandFlags.None);
        await Assert.That(
            PhysxCommandAdapter.AllowedFlags(PhysxCommandType.Teleport))
            .IsEqualTo(PhysxCommandFlags.NoWake);
        await Assert.That(
            PhysxCommandAdapter.AllowedFlags(PhysxCommandType.SetSceneGravity))
            .IsEqualTo(PhysxCommandFlags.Magnitude);
        // An application point is delivered through PxRigidBodyExt, which supports only the plain
        // force and impulse modes, so no force-mode modifier is accepted alongside a point.
        await Assert.That(PhysxCommandAdapter.AllowedFlags(PhysxCommandType.AddForceAtPoint))
            .IsEqualTo(PhysxCommandFlags.Magnitude | PhysxCommandFlags.PointLocal |
                PhysxCommandFlags.PointCenterOfMass | PhysxCommandFlags.NoWake);
        await Assert.That(PhysxCommandAdapter.AllowedFlags(PhysxCommandType.AddImpulseAtPoint))
            .IsEqualTo(PhysxCommandFlags.Magnitude | PhysxCommandFlags.PointLocal |
                PhysxCommandFlags.PointCenterOfMass | PhysxCommandFlags.NoWake);
    }
}
