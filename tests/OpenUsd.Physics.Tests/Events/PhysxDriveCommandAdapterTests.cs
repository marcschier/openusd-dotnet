// Copyright (c) marcschier. Licensed under the MIT License.

using OpenUsd.Physics.Interop;

namespace OpenUsd.Physics.Tests.Events;

/// <summary>
/// Asserts the runtime command translations the interactive viewer depends on: character
/// controller movement and vehicle driver input, both of which the retained world ABI carries and
/// both of which it rejects outright when a value leaves its documented range.
/// </summary>
public sealed class PhysxDriveCommandAdapterTests
{
    private static readonly UsdPhysicsObjectId Controller =
        new(7, UsdPhysicsObjectKind.Controller);

    private static readonly UsdPhysicsObjectId Vehicle = new(9, UsdPhysicsObjectKind.Vehicle);

    [Test]
    public async Task AControllerMoveTranslatesAsADisplacementVector()
    {
        var command = new UsdPhysicsCommand(
            UsdPhysicsCommandKind.ControllerMove,
            Controller,
            new UsdVec3d(0, 0, -1));

        await Assert.That(PhysxCommandAdapter.TryTranslate(
            command, out PhysxCommand native, out string? rejection)).IsTrue();
        await Assert.That(rejection).IsNull();
        await Assert.That(native.Type).IsEqualTo((uint)PhysxCommandType.MoveController);
        await Assert.That(native.Vector.Z).IsEqualTo(-1F);
        await Assert.That(native.Flags).IsEqualTo((uint)PhysxCommandFlags.None);
    }

    [Test]
    public async Task AControllerMoveMayCarryADirectionAndADistance()
    {
        var command = new UsdPhysicsCommand(
            UsdPhysicsCommandKind.ControllerMove,
            Controller,
            new UsdVec3d(0, 0, -1),
            0.25);

        await Assert.That(PhysxCommandAdapter.TryTranslate(
            command, out PhysxCommand native, out _)).IsTrue();
        await Assert.That(native.Flags).IsEqualTo((uint)PhysxCommandFlags.Magnitude);
        await Assert.That(native.Scalar).IsEqualTo(0.25F);
    }

    [Test]
    public async Task AControllerMoveRefusesAnApplicationPointItDoesNotRead()
    {
        var command = new UsdPhysicsCommand(
            UsdPhysicsCommandKind.ControllerMove,
            Controller,
            new UsdVec3d(0, 0, -1))
        {
            Point = new UsdVec3d(1, 0, 0),
        };

        await Assert.That(PhysxCommandAdapter.TryTranslate(command, out _, out string? rejection))
            .IsFalse();
        await Assert.That(rejection).IsNotNull();
    }

    [Test]
    public async Task AVehicleInputPacksEveryChannelTheAbiDocuments()
    {
        var command = new UsdPhysicsCommand(
            UsdPhysicsCommandKind.VehicleInput,
            Vehicle,
            new UsdVec3d(0.75, 0.25, -0.5))
        {
            Point = new UsdVec3d(0.1, 0.2, 3),
        };

        await Assert.That(PhysxCommandAdapter.TryTranslate(
            command, out PhysxCommand native, out string? rejection)).IsTrue();
        await Assert.That(rejection).IsNull();
        await Assert.That(native.Type).IsEqualTo((uint)PhysxCommandType.VehicleInput);
        await Assert.That(native.Vector.X).IsEqualTo(0.75F);
        await Assert.That(native.Vector.Y).IsEqualTo(0.25F);
        await Assert.That(native.Vector.Z).IsEqualTo(-0.5F);
        await Assert.That(native.Point.X).IsEqualTo(0.1F);
        await Assert.That(native.Point.Y).IsEqualTo(0.2F);
        await Assert.That(native.Point.Z).IsEqualTo(3F);
    }

    [Test]
    public async Task AVehicleInputOutsideItsRangeIsRefusedRatherThanClamped()
    {
        // A silently clamped throttle would make the vehicle behave differently from the control
        // the user is holding, so the managed adapter refuses exactly what the runtime refuses.
        var overThrottle = new UsdPhysicsCommand(
            UsdPhysicsCommandKind.VehicleInput, Vehicle, new UsdVec3d(1.5, 0, 0));
        var overSteer = new UsdPhysicsCommand(
            UsdPhysicsCommandKind.VehicleInput, Vehicle, new UsdVec3d(0, 0, -2));
        var negativeBrake = new UsdPhysicsCommand(
            UsdPhysicsCommandKind.VehicleInput, Vehicle, new UsdVec3d(0, -0.1, 0));

        await Assert.That(PhysxCommandAdapter.TryTranslate(overThrottle, out _, out string? first))
            .IsFalse();
        await Assert.That(first).IsNotNull();
        await Assert.That(PhysxCommandAdapter.TryTranslate(overSteer, out _, out string? second))
            .IsFalse();
        await Assert.That(second).IsNotNull();
        await Assert.That(PhysxCommandAdapter.TryTranslate(
            negativeBrake, out _, out string? third)).IsFalse();
        await Assert.That(third).IsNotNull();
    }

    [Test]
    public async Task AVehicleGearMustBeANonNegativeWholeNumberInsideTheGearBudget()
    {
        var fractional = new UsdPhysicsCommand(
            UsdPhysicsCommandKind.VehicleInput, Vehicle, new UsdVec3d(0, 0, 0))
        {
            Point = new UsdVec3d(0, 0, 1.5),
        };
        var negative = new UsdPhysicsCommand(
            UsdPhysicsCommandKind.VehicleInput, Vehicle, new UsdVec3d(0, 0, 0))
        {
            Point = new UsdVec3d(0, 0, -1),
        };
        var beyondBudget = new UsdPhysicsCommand(
            UsdPhysicsCommandKind.VehicleInput, Vehicle, new UsdVec3d(0, 0, 0))
        {
            Point = new UsdVec3d(0, 0, PhysxAbi.MaxVehicleGears + 1),
        };

        await Assert.That(PhysxCommandAdapter.TryTranslate(fractional, out _, out _)).IsFalse();
        await Assert.That(PhysxCommandAdapter.TryTranslate(negative, out _, out _)).IsFalse();
        await Assert.That(PhysxCommandAdapter.TryTranslate(beyondBudget, out _, out _)).IsFalse();
    }

    [Test]
    public async Task AVehicleInputAcceptsEveryGearInsideTheBudget()
    {
        var violations = new List<int>();
        for (int gear = 0; gear <= (int)PhysxAbi.MaxVehicleGears; gear++)
        {
            var command = new UsdPhysicsCommand(
                UsdPhysicsCommandKind.VehicleInput, Vehicle, new UsdVec3d(0, 0, 0))
            {
                Point = new UsdVec3d(0, 0, gear),
            };

            if (!PhysxCommandAdapter.TryTranslate(command, out _, out _))
            {
                violations.Add(gear);
            }
        }

        await Assert.That(violations).IsEmpty();
    }

    [Test]
    public async Task AVehicleInputRefusesAModifierItsTypeDoesNotAccept()
    {
        var withMagnitude = new UsdPhysicsCommand(
            UsdPhysicsCommandKind.VehicleInput, Vehicle, new UsdVec3d(1, 0, 0), 2);

        await Assert.That(PhysxCommandAdapter.TryTranslate(
            withMagnitude, out _, out string? rejection)).IsFalse();
        await Assert.That(rejection).IsNotNull();
    }

    [Test]
    public async Task AWholeDriveBatchStagesInSubmissionOrder()
    {
        UsdPhysicsCommand[] batch =
        [
            new(UsdPhysicsCommandKind.ControllerMove, Controller, new UsdVec3d(1, 0, 0), 0.1),
            new(UsdPhysicsCommandKind.VehicleInput, Vehicle, new UsdVec3d(1, 0, 0)),
        ];
        var destination = new PhysxCommand[batch.Length];

        bool staged = PhysxCommandAdapter.TryTranslateBatch(
            batch, destination, out int accepted, out int rejectedIndex, out _);

        await Assert.That(staged).IsTrue();
        await Assert.That(accepted).IsEqualTo(2);
        await Assert.That(rejectedIndex).IsEqualTo(-1);
        await Assert.That(destination[0].Type).IsEqualTo((uint)PhysxCommandType.MoveController);
        await Assert.That(destination[1].Type).IsEqualTo((uint)PhysxCommandType.VehicleInput);
    }
}
