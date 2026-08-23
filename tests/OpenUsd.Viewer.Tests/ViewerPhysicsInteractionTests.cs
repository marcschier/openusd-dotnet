// Copyright (c) marcschier. Licensed under the MIT License.

namespace OpenUsd.Viewer.Tests;

/// <summary>
/// Asserts the interactive drive models: the drag spring, the character controller displacement,
/// the vehicle driver input, and the one-shot force and impulse builder.
/// </summary>
public sealed class ViewerPhysicsInteractionTests
{
    [Test]
    public async Task ADragPullsTheGrabbedPointTowardThePointerAtTheGrabDepth()
    {
        var drag = new ViewerPhysicsDragModel();
        await Assert.That(drag.Begin(7UL, new ViewerPhysicsVector3(0.5d, 0d, 0d), 10d)).IsTrue();
        await Assert.That(drag.IsActive).IsTrue();

        var ray = new ViewerGizmoRay(new ViewerPhysicsVector3(0d, 0d, 0d), new(0d, 0d, -1d));
        await Assert.That(drag.TryUpdate(
            new ViewerPhysicsVector3(0d, 0d, -9d),
            ray,
            1d / 60d,
            out ViewerPhysicsRuntimeCommand command)).IsTrue();

        await Assert.That(command.Kind).IsEqualTo(ViewerPhysicsRuntimeCommandKind.Force);
        await Assert.That(command.TargetId).IsEqualTo(7UL);
        await Assert.That(command.Application).IsEqualTo(ViewerPhysicsApplication.Local);
        await Assert.That(command.Point.X).IsEqualTo(0.5d);

        // The target sits one unit further along the ray than the grabbed point, so the spring
        // pushes the body in the direction the pointer went.
        await Assert.That(command.Vector.Z).IsLessThan(0d);
    }

    [Test]
    public async Task AGrabAtTheBodyOriginAppliesAtTheCentreOfMassRatherThanAddingATorque()
    {
        var drag = new ViewerPhysicsDragModel();
        drag.Begin(7UL, ViewerPhysicsVector3.Zero, 10d);
        var ray = new ViewerGizmoRay(ViewerPhysicsVector3.Zero, new(0d, 0d, -1d));

        await Assert.That(drag.TryUpdate(
            new ViewerPhysicsVector3(0d, 0d, -9d),
            ray,
            1d / 60d,
            out ViewerPhysicsRuntimeCommand command)).IsTrue();

        await Assert.That(command.Application)
            .IsEqualTo(ViewerPhysicsApplication.CenterOfMass);
        await Assert.That(command.Point.Length).IsEqualTo(0d);
    }

    [Test]
    public async Task ADragForceIsBoundedSoAPointerFlickCannotLaunchTheBody()
    {
        var drag = new ViewerPhysicsDragModel(new ViewerPhysicsDragGains(1e6d, 0d, 100d));
        drag.Begin(3UL, ViewerPhysicsVector3.Zero, 5d);
        var ray = new ViewerGizmoRay(ViewerPhysicsVector3.Zero, new(1d, 0d, 0d));

        await Assert.That(drag.TryUpdate(
            new ViewerPhysicsVector3(-1000d, 0d, 0d),
            ray,
            1d / 60d,
            out ViewerPhysicsRuntimeCommand command)).IsTrue();

        await Assert.That(command.Vector.Length).IsLessThanOrEqualTo(100.0001d);
    }

    [Test]
    public async Task ADragDampsTheGrabbedPointVelocityItObservesItself()
    {
        var drag = new ViewerPhysicsDragModel(new ViewerPhysicsDragGains(1d, 100d, 1e6d));
        drag.Begin(3UL, ViewerPhysicsVector3.Zero, 1d);
        var ray = new ViewerGizmoRay(ViewerPhysicsVector3.Zero, new(1d, 0d, 0d));

        // The first update has no history, so it is pure spring.
        drag.TryUpdate(new ViewerPhysicsVector3(1d, 0d, 0d), ray, 1d, out var first);

        // The second sees the point moving away from the target at one unit per second, so the
        // damping term now opposes it.
        drag.TryUpdate(new ViewerPhysicsVector3(2d, 0d, 0d), ray, 1d, out var second);

        await Assert.That(second.Vector.X).IsLessThan(first.Vector.X);
    }

    [Test]
    public async Task ADragThatIsAlreadyUnderThePointerAsksForNothing()
    {
        var drag = new ViewerPhysicsDragModel();
        drag.Begin(3UL, ViewerPhysicsVector3.Zero, 4d);
        var ray = new ViewerGizmoRay(ViewerPhysicsVector3.Zero, new(0d, 0d, -1d));

        await Assert.That(drag.TryUpdate(
            new ViewerPhysicsVector3(0d, 0d, -4d), ray, 1d / 60d, out _)).IsFalse();
    }

    [Test]
    public async Task EndingADragClearsTheForceItStaged()
    {
        var drag = new ViewerPhysicsDragModel();
        drag.Begin(11UL, ViewerPhysicsVector3.Zero, 3d);

        await Assert.That(drag.TryEnd(out ViewerPhysicsRuntimeCommand command)).IsTrue();
        await Assert.That(command.Kind).IsEqualTo(ViewerPhysicsRuntimeCommandKind.ClearForce);
        await Assert.That(command.TargetId).IsEqualTo(11UL);
        await Assert.That(drag.IsActive).IsFalse();
        await Assert.That(drag.TryEnd(out _)).IsFalse();
    }

    [Test]
    public async Task ADragRefusesAnUnusableGrab()
    {
        var drag = new ViewerPhysicsDragModel();

        await Assert.That(drag.Begin(0UL, ViewerPhysicsVector3.Zero, 1d)).IsFalse();
        await Assert.That(drag.Begin(1UL, ViewerPhysicsVector3.Zero, 0d)).IsFalse();
        await Assert.That(drag.Begin(
            1UL, new ViewerPhysicsVector3(double.NaN, 0d, 0d), 1d)).IsFalse();
    }

    [Test]
    public async Task ControllerMovementIsCameraRelativeAndProjectedOffTheUpAxis()
    {
        await Assert.That(ViewerPhysicsControllerInput.TryBuild(
            5UL,
            ViewerPhysicsControllerDirection.Forward,
            new ViewerPhysicsVector3(0d, -0.5d, -1d),
            new ViewerPhysicsVector3(1d, 0d, 0d),
            new ViewerPhysicsVector3(0d, 1d, 0d),
            2d,
            0.5d,
            out ViewerPhysicsRuntimeCommand command)).IsTrue();

        await Assert.That(command.Kind).IsEqualTo(ViewerPhysicsRuntimeCommandKind.ControllerMove);
        await Assert.That(command.Magnitude).IsEqualTo(1d);
        await Assert.That(Math.Abs(command.Vector.Y)).IsLessThan(1e-9d);
        await Assert.That(command.Vector.Z).IsLessThan(0d);
    }

    [Test]
    public async Task DiagonalControllerMovementIsNormalizedSoItIsNotFaster()
    {
        ViewerPhysicsControllerInput.TryBuild(
            5UL,
            ViewerPhysicsControllerDirection.Forward | ViewerPhysicsControllerDirection.Right,
            new ViewerPhysicsVector3(0d, 0d, -1d),
            new ViewerPhysicsVector3(1d, 0d, 0d),
            new ViewerPhysicsVector3(0d, 1d, 0d),
            3d,
            1d,
            out ViewerPhysicsRuntimeCommand command);

        await Assert.That(Math.Abs(command.Vector.Length - 1d)).IsLessThan(1e-9d);
        await Assert.That(command.Magnitude).IsEqualTo(3d);
    }

    [Test]
    public async Task OppositeControllerKeysCancelInsteadOfMovingSideways()
    {
        await Assert.That(ViewerPhysicsControllerInput.TryBuild(
            5UL,
            ViewerPhysicsControllerDirection.Forward | ViewerPhysicsControllerDirection.Back,
            new ViewerPhysicsVector3(0d, 0d, -1d),
            new ViewerPhysicsVector3(1d, 0d, 0d),
            new ViewerPhysicsVector3(0d, 1d, 0d),
            3d,
            1d,
            out _)).IsFalse();
    }

    [Test]
    public async Task ControllerMovementRefusesUnusableSpeedAndTime()
    {
        await Assert.That(ViewerPhysicsControllerInput.TryBuild(
            5UL,
            ViewerPhysicsControllerDirection.Forward,
            new ViewerPhysicsVector3(0d, 0d, -1d),
            new ViewerPhysicsVector3(1d, 0d, 0d),
            new ViewerPhysicsVector3(0d, 1d, 0d),
            0d,
            1d,
            out _)).IsFalse();
        await Assert.That(ViewerPhysicsControllerInput.TryBuild(
            5UL,
            ViewerPhysicsControllerDirection.Forward,
            new ViewerPhysicsVector3(0d, 0d, -1d),
            new ViewerPhysicsVector3(1d, 0d, 0d),
            new ViewerPhysicsVector3(0d, 1d, 0d),
            1d,
            double.NaN,
            out _)).IsFalse();
    }

    [Test]
    public async Task VehicleInputIsClampedIntoTheRangesTheRuntimeAccepts()
    {
        var input = new ViewerPhysicsVehicleInput(2d, -1d, 5d, double.NaN, 0.5d, 999);

        ViewerPhysicsVehicleInput clamped = input.Clamped();

        await Assert.That(input.IsValid).IsFalse();
        await Assert.That(clamped.IsValid).IsTrue();
        await Assert.That(clamped.Throttle).IsEqualTo(1d);
        await Assert.That(clamped.Brake).IsEqualTo(0d);
        await Assert.That(clamped.Steer).IsEqualTo(1d);
        await Assert.That(clamped.HandBrake).IsEqualTo(0d);
        await Assert.That(clamped.Gear).IsEqualTo(ViewerPhysicsVehicleInput.MaxGear);
    }

    [Test]
    public async Task VehicleInputPacksEveryChannelWhereTheCommandAbiExpectsIt()
    {
        var input = new ViewerPhysicsVehicleInput(0.6d, 0.2d, -0.4d, 0.1d, 0.3d, 3);

        ViewerPhysicsRuntimeCommand command = input.ToCommand(42UL);

        await Assert.That(command.Kind).IsEqualTo(ViewerPhysicsRuntimeCommandKind.VehicleInput);
        await Assert.That(command.TargetId).IsEqualTo(42UL);
        await Assert.That(command.Vector.X).IsEqualTo(0.6d);
        await Assert.That(command.Vector.Y).IsEqualTo(0.2d);
        await Assert.That(command.Vector.Z).IsEqualTo(-0.4d);
        await Assert.That(command.Point.X).IsEqualTo(0.1d);
        await Assert.That(command.Point.Y).IsEqualTo(0.3d);
        await Assert.That(command.Point.Z).IsEqualTo(3d);
        await Assert.That(command.Magnitude).IsEqualTo(0d);
    }

    [Test]
    public async Task VehicleGearsAreDescribedTheWayTheRuntimeNumbersThem()
    {
        await Assert.That(ViewerPhysicsVehicleInput.Neutral.DescribeGear()).IsEqualTo("gear auto");
        await Assert.That((ViewerPhysicsVehicleInput.Neutral with { Gear = 1 }).DescribeGear())
            .IsEqualTo("gear reverse");
        await Assert.That((ViewerPhysicsVehicleInput.Neutral with { Gear = 2 }).DescribeGear())
            .IsEqualTo("gear neutral");
        await Assert.That((ViewerPhysicsVehicleInput.Neutral with { Gear = 3 }).DescribeGear())
            .IsEqualTo("gear 1");
        await Assert.That(ViewerPhysicsVehicleInput.Neutral.Describe()).Contains("throttle");
    }

    [Test]
    public async Task AnImpulseNeedsATargetADirectionAndAPositiveMagnitude()
    {
        await Assert.That(ViewerPhysicsImpulseBuilder.TryBuild(
            ViewerPhysicsRuntimeCommandKind.Impulse,
            0UL,
            new ViewerPhysicsVector3(0d, 1d, 0d),
            5d,
            ViewerPhysicsForceMode.Default,
            out _,
            out string noTarget)).IsFalse();
        await Assert.That(noTarget).IsNotEmpty();

        await Assert.That(ViewerPhysicsImpulseBuilder.TryBuild(
            ViewerPhysicsRuntimeCommandKind.Impulse,
            9UL,
            ViewerPhysicsVector3.Zero,
            5d,
            ViewerPhysicsForceMode.Default,
            out _,
            out string noDirection)).IsFalse();
        await Assert.That(noDirection).IsNotEmpty();

        await Assert.That(ViewerPhysicsImpulseBuilder.TryBuild(
            ViewerPhysicsRuntimeCommandKind.Impulse,
            9UL,
            new ViewerPhysicsVector3(0d, 1d, 0d),
            0d,
            ViewerPhysicsForceMode.Default,
            out _,
            out string noMagnitude)).IsFalse();
        await Assert.That(noMagnitude).IsNotEmpty();
    }

    [Test]
    public async Task AnImpulseNormalizesItsDirectionAndCarriesTheMagnitudeSeparately()
    {
        await Assert.That(ViewerPhysicsImpulseBuilder.TryBuild(
            ViewerPhysicsRuntimeCommandKind.Force,
            9UL,
            new ViewerPhysicsVector3(0d, 5d, 0d),
            12d,
            ViewerPhysicsForceMode.Acceleration,
            out ViewerPhysicsRuntimeCommand command,
            out _)).IsTrue();

        await Assert.That(command.Vector.Y).IsEqualTo(1d);
        await Assert.That(command.Magnitude).IsEqualTo(12d);
        await Assert.That(command.Mode).IsEqualTo(ViewerPhysicsForceMode.Acceleration);
        await Assert.That(command.WakeTarget).IsTrue();
    }

    [Test]
    public async Task OnlyTheCommandKindsThatTakeAMagnitudeAreBuiltThisWay()
    {
        await Assert.That(ViewerPhysicsImpulseBuilder.TryBuild(
            ViewerPhysicsRuntimeCommandKind.Wake,
            9UL,
            new ViewerPhysicsVector3(0d, 1d, 0d),
            1d,
            ViewerPhysicsForceMode.Default,
            out _,
            out string error)).IsFalse();
        await Assert.That(error).IsNotEmpty();
    }
}
