// Copyright (c) marcschier. Licensed under the MIT License.

using System.Globalization;

namespace OpenUsd.Viewer;

/// <summary>Identifies one interactive runtime command the viewer submits to the solver.</summary>
internal enum ViewerPhysicsRuntimeCommandKind
{
    /// <summary>Apply a continuous force.</summary>
    Force,

    /// <summary>Apply an instantaneous impulse.</summary>
    Impulse,

    /// <summary>Apply a continuous torque.</summary>
    Torque,

    /// <summary>Apply an instantaneous angular impulse.</summary>
    AngularImpulse,

    /// <summary>Replace the linear velocity.</summary>
    LinearVelocity,

    /// <summary>Replace the angular velocity.</summary>
    AngularVelocity,

    /// <summary>Discard the accumulated force and impulse.</summary>
    ClearForce,

    /// <summary>Discard the accumulated torque and angular impulse.</summary>
    ClearTorque,

    /// <summary>Wake a sleeping body.</summary>
    Wake,

    /// <summary>Put a body to sleep.</summary>
    Sleep,

    /// <summary>Replace one scene's gravity vector.</summary>
    SceneGravity,

    /// <summary>Move a character controller by a displacement.</summary>
    ControllerMove,

    /// <summary>Submit a vehicle driver input.</summary>
    VehicleInput,
}

/// <summary>Selects how a force or impulse value is integrated.</summary>
internal enum ViewerPhysicsForceMode
{
    /// <summary>The value is scaled by the inverse mass.</summary>
    Default,

    /// <summary>The value is an acceleration and ignores the mass.</summary>
    Acceleration,

    /// <summary>The value is applied directly as a velocity change.</summary>
    VelocityChange,
}

/// <summary>Selects where a force or impulse is applied on the target.</summary>
internal enum ViewerPhysicsApplication
{
    /// <summary>Apply at the centre of mass, producing no torque.</summary>
    CenterOfMass,

    /// <summary>Apply at a point expressed in stage space.</summary>
    World,

    /// <summary>Apply at a point expressed in the object's local space.</summary>
    Local,
}

/// <summary>One interactive runtime command, expressed without any physics type.</summary>
/// <param name="Kind">The command to apply.</param>
/// <param name="TargetId">The stable simulation identity the command targets.</param>
/// <param name="Vector">The command's primary vector.</param>
/// <param name="Magnitude">
/// The magnitude a non-zero value gives <paramref name="Vector"/>, which the runtime then treats as
/// a direction. A zero magnitude uses the vector unchanged.
/// </param>
internal sealed record ViewerPhysicsRuntimeCommand(
    ViewerPhysicsRuntimeCommandKind Kind,
    ulong TargetId,
    ViewerPhysicsVector3 Vector,
    double Magnitude = 0d)
{
    /// <summary>Gets how a force or impulse value is integrated.</summary>
    internal ViewerPhysicsForceMode Mode { get; init; }

    /// <summary>Gets where a force or impulse is applied on the target.</summary>
    internal ViewerPhysicsApplication Application { get; init; }

    /// <summary>Gets the application point used when the application is not the centre of mass.</summary>
    internal ViewerPhysicsVector3 Point { get; init; }

    /// <summary>Gets a value indicating whether applying this command wakes a sleeping target.</summary>
    internal bool WakeTarget { get; init; } = true;
}

/// <summary>Reports what submitting one runtime command batch achieved.</summary>
/// <param name="Accepted">The commands staged for the next simulation step.</param>
/// <param name="Rejected">The commands the world refused.</param>
/// <param name="Message">A sentence describing the outcome, always non-empty.</param>
internal readonly record struct ViewerPhysicsCommandOutcome(
    int Accepted,
    int Rejected,
    string Message)
{
    /// <summary>Gets the outcome of submitting nothing.</summary>
    internal static ViewerPhysicsCommandOutcome None =>
        new(0, 0, "No interactive command was submitted.");

    /// <summary>Gets a value indicating whether every command was staged.</summary>
    internal bool Succeeded => Rejected == 0 && Accepted > 0;
}

/// <summary>The gains one interactive body drag uses.</summary>
/// <param name="Stiffness">The spring constant pulling the grabbed point to the pointer.</param>
/// <param name="Damping">The damping applied to the grabbed point's own velocity.</param>
/// <param name="MaxForce">The largest force magnitude the drag may request.</param>
/// <remarks>
/// The force is bounded because a spring with no bound turns a fast pointer flick into an impulse
/// large enough to launch the body out of the scene, and the solver has no way to tell that apart
/// from an intended input.
/// </remarks>
internal readonly record struct ViewerPhysicsDragGains(
    double Stiffness,
    double Damping,
    double MaxForce)
{
    /// <summary>Gets the gains the viewer drags with by default.</summary>
    internal static ViewerPhysicsDragGains Default => new(120d, 12d, 5000d);

    /// <summary>Gets a value indicating whether every gain is usable.</summary>
    internal bool IsValid =>
        double.IsFinite(Stiffness) && Stiffness > 0d &&
        double.IsFinite(Damping) && Damping >= 0d &&
        double.IsFinite(MaxForce) && MaxForce > 0d;
}

/// <summary>
/// Turns a pointer drag over a simulated body into the forces the solver applies to it.
/// </summary>
/// <remarks>
/// <para>
/// The drag is a spring, not a teleport. Setting the body's pose directly would make it pass
/// through everything it meets and would discard the momentum the solver had given it, so what the
/// user would be dragging is no longer the simulated object. A damped spring applied at the grabbed
/// point keeps the body inside the simulation: it collides on the way, it rotates around the point
/// that was grabbed, and letting go leaves it with the velocity it actually had.
/// </para>
/// <para>
/// The model estimates the grabbed point's velocity from its own history rather than asking the
/// solver, because the render bridge publishes poses and not velocities. That estimate is exactly
/// what the damping term needs and it costs nothing extra per frame.
/// </para>
/// </remarks>
internal sealed class ViewerPhysicsDragModel
{
    private readonly ViewerPhysicsDragGains _gains;
    private ViewerPhysicsVector3 _previousGrabPoint;
    private bool _hasPrevious;

    /// <summary>Initializes a drag model.</summary>
    /// <param name="gains">The spring gains, or the defaults.</param>
    /// <exception cref="ArgumentOutOfRangeException">A gain is not usable.</exception>
    internal ViewerPhysicsDragModel(ViewerPhysicsDragGains gains = default)
    {
        ViewerPhysicsDragGains resolved =
            gains.Equals(default(ViewerPhysicsDragGains)) ? ViewerPhysicsDragGains.Default : gains;
        if (!resolved.IsValid)
        {
            throw new ArgumentOutOfRangeException(
                nameof(gains),
                resolved,
                "The drag gains must be finite, positive, and non-negative for damping.");
        }

        _gains = resolved;
    }

    /// <summary>Gets the identity being dragged, or zero when no drag is active.</summary>
    internal ulong TargetId { get; private set; }

    /// <summary>Gets the grabbed point in the body's local space.</summary>
    internal ViewerPhysicsVector3 LocalPoint { get; private set; }

    /// <summary>Gets the parametric depth along the pointer ray the grab was made at.</summary>
    internal double GrabDistance { get; private set; }

    /// <summary>Gets a value indicating whether a drag is active.</summary>
    internal bool IsActive => TargetId != 0UL;

    /// <summary>Begins a drag on one simulated identity.</summary>
    /// <param name="targetId">The stable simulation identity being grabbed.</param>
    /// <param name="localPoint">The grabbed point in the body's local space.</param>
    /// <param name="grabDistance">The parametric depth along the pointer ray.</param>
    /// <returns><see langword="true"/> when the drag started.</returns>
    internal bool Begin(ulong targetId, ViewerPhysicsVector3 localPoint, double grabDistance)
    {
        if (targetId == 0UL || !localPoint.IsFinite ||
            !double.IsFinite(grabDistance) || grabDistance <= 0d)
        {
            return false;
        }

        TargetId = targetId;
        LocalPoint = localPoint;
        GrabDistance = grabDistance;
        _hasPrevious = false;
        _previousGrabPoint = ViewerPhysicsVector3.Zero;
        return true;
    }

    /// <summary>Ends the drag and clears the force it accumulated.</summary>
    /// <param name="command">Receives the command that clears the accumulated force.</param>
    /// <returns><see langword="true"/> when a drag was active.</returns>
    /// <remarks>
    /// The clear matters: the runtime applies the staged force before the next sub-steps, so a drag
    /// that simply stopped submitting would still push the body once more with whatever it staged
    /// last.
    /// </remarks>
    internal bool TryEnd(out ViewerPhysicsRuntimeCommand command)
    {
        if (TargetId == 0UL)
        {
            command = new ViewerPhysicsRuntimeCommand(
                ViewerPhysicsRuntimeCommandKind.ClearForce, 0UL, ViewerPhysicsVector3.Zero);
            return false;
        }

        command = new ViewerPhysicsRuntimeCommand(
            ViewerPhysicsRuntimeCommandKind.ClearForce,
            TargetId,
            ViewerPhysicsVector3.Zero);
        TargetId = 0UL;
        LocalPoint = ViewerPhysicsVector3.Zero;
        GrabDistance = 0d;
        _hasPrevious = false;
        return true;
    }

    /// <summary>Computes the force one drag step applies.</summary>
    /// <param name="grabWorldPoint">Where the grabbed point is now, in stage space.</param>
    /// <param name="pointerRay">The ray under the pointer now.</param>
    /// <param name="deltaSeconds">The wall-clock time since the previous update.</param>
    /// <param name="command">Receives the force command.</param>
    /// <returns><see langword="true"/> when a usable force was produced.</returns>
    internal bool TryUpdate(
        ViewerPhysicsVector3 grabWorldPoint,
        ViewerGizmoRay pointerRay,
        double deltaSeconds,
        out ViewerPhysicsRuntimeCommand command)
    {
        command = new ViewerPhysicsRuntimeCommand(
            ViewerPhysicsRuntimeCommandKind.Force, TargetId, ViewerPhysicsVector3.Zero);
        if (TargetId == 0UL || !grabWorldPoint.IsFinite || !pointerRay.IsValid ||
            !double.IsFinite(deltaSeconds) || deltaSeconds <= 0d)
        {
            return false;
        }

        ViewerPhysicsVector3 target = pointerRay.Origin;
        ViewerPhysicsVector3 direction = pointerRay.Direction.Normalized();
        if (direction.Length <= 0d)
        {
            return false;
        }

        target = ViewerPhysicsVector3.Add(
            target,
            ViewerPhysicsVector3.Scale(direction, GrabDistance));

        ViewerPhysicsVector3 offset = ViewerPhysicsVector3.Subtract(target, grabWorldPoint);
        ViewerPhysicsVector3 velocity = ViewerPhysicsVector3.Zero;
        if (_hasPrevious)
        {
            velocity = ViewerPhysicsVector3.Scale(
                ViewerPhysicsVector3.Subtract(grabWorldPoint, _previousGrabPoint),
                1d / deltaSeconds);
        }

        _previousGrabPoint = grabWorldPoint;
        _hasPrevious = true;

        ViewerPhysicsVector3 force = ViewerPhysicsVector3.Subtract(
            ViewerPhysicsVector3.Scale(offset, _gains.Stiffness),
            ViewerPhysicsVector3.Scale(velocity, _gains.Damping));
        if (!force.IsFinite)
        {
            return false;
        }

        double magnitude = force.Length;
        if (magnitude <= 1e-6d)
        {
            // The body already sits under the pointer, so there is nothing to push. Submitting a
            // zero force would still be a command the world has to validate and apply.
            return false;
        }

        if (magnitude > _gains.MaxForce)
        {
            force = ViewerPhysicsVector3.Scale(force, _gains.MaxForce / magnitude);
        }

        command = new ViewerPhysicsRuntimeCommand(
            ViewerPhysicsRuntimeCommandKind.Force,
            TargetId,
            force)
        {
            // A grab at the body's own origin has no offset to rotate about, so it is applied at
            // the centre of mass: pushing at the frame origin instead would add a torque the user
            // never asked for whenever the centre of mass is not at that origin.
            Application = LocalPoint.Length > 0d
                ? ViewerPhysicsApplication.Local
                : ViewerPhysicsApplication.CenterOfMass,
            Point = LocalPoint.Length > 0d ? LocalPoint : ViewerPhysicsVector3.Zero,
            WakeTarget = true,
        };
        return true;
    }
}

/// <summary>The directions a character controller may be asked to move in.</summary>
[Flags]
internal enum ViewerPhysicsControllerDirection
{
    /// <summary>No requested movement.</summary>
    None = 0,

    /// <summary>Move along the camera's forward direction.</summary>
    Forward = 1,

    /// <summary>Move against the camera's forward direction.</summary>
    Back = 2,

    /// <summary>Move against the camera's right direction.</summary>
    Left = 4,

    /// <summary>Move along the camera's right direction.</summary>
    Right = 8,

    /// <summary>Move along the stage up axis.</summary>
    Up = 16,

    /// <summary>Move against the stage up axis.</summary>
    Down = 32,
}

/// <summary>
/// Turns held movement keys into the displacement a character controller is asked to move by.
/// </summary>
/// <remarks>
/// The displacement is camera relative and is projected off the up axis, which is what makes "walk
/// forward" mean the direction the user is looking along the ground rather than into it. Diagonal
/// input is normalized so holding two keys does not move the controller faster than holding one.
/// </remarks>
internal static class ViewerPhysicsControllerInput
{
    /// <summary>Builds the move command one controller step requests.</summary>
    /// <param name="targetId">The controller's stable simulation identity.</param>
    /// <param name="directions">The directions currently held.</param>
    /// <param name="cameraForward">The camera's forward direction, in stage space.</param>
    /// <param name="cameraRight">The camera's right direction, in stage space.</param>
    /// <param name="upAxis">The stage up axis.</param>
    /// <param name="speed">The requested speed, in stage linear units per second.</param>
    /// <param name="deltaSeconds">The simulated time the displacement covers.</param>
    /// <param name="command">Receives the move command.</param>
    /// <returns><see langword="true"/> when a usable displacement was produced.</returns>
    internal static bool TryBuild(
        ulong targetId,
        ViewerPhysicsControllerDirection directions,
        ViewerPhysicsVector3 cameraForward,
        ViewerPhysicsVector3 cameraRight,
        ViewerPhysicsVector3 upAxis,
        double speed,
        double deltaSeconds,
        out ViewerPhysicsRuntimeCommand command)
    {
        command = new ViewerPhysicsRuntimeCommand(
            ViewerPhysicsRuntimeCommandKind.ControllerMove, targetId, ViewerPhysicsVector3.Zero);
        if (targetId == 0UL ||
            directions == ViewerPhysicsControllerDirection.None ||
            !double.IsFinite(speed) || speed <= 0d ||
            !double.IsFinite(deltaSeconds) || deltaSeconds <= 0d)
        {
            return false;
        }

        ViewerPhysicsVector3 up = upAxis.Normalized();
        ViewerPhysicsVector3 forward = Project(cameraForward, up);
        ViewerPhysicsVector3 right = Project(cameraRight, up);
        if (forward.Length <= 0d || right.Length <= 0d)
        {
            return false;
        }

        ViewerPhysicsVector3 move = ViewerPhysicsVector3.Zero;
        if ((directions & ViewerPhysicsControllerDirection.Forward) != 0)
        {
            move = ViewerPhysicsVector3.Add(move, forward);
        }

        if ((directions & ViewerPhysicsControllerDirection.Back) != 0)
        {
            move = ViewerPhysicsVector3.Subtract(move, forward);
        }

        if ((directions & ViewerPhysicsControllerDirection.Right) != 0)
        {
            move = ViewerPhysicsVector3.Add(move, right);
        }

        if ((directions & ViewerPhysicsControllerDirection.Left) != 0)
        {
            move = ViewerPhysicsVector3.Subtract(move, right);
        }

        if ((directions & ViewerPhysicsControllerDirection.Up) != 0)
        {
            move = ViewerPhysicsVector3.Add(move, up);
        }

        if ((directions & ViewerPhysicsControllerDirection.Down) != 0)
        {
            move = ViewerPhysicsVector3.Subtract(move, up);
        }

        ViewerPhysicsVector3 direction = move.Normalized();
        if (direction.Length <= 0d)
        {
            // Opposite keys cancel exactly, which is the same as asking for no movement at all.
            return false;
        }

        command = new ViewerPhysicsRuntimeCommand(
            ViewerPhysicsRuntimeCommandKind.ControllerMove,
            targetId,
            direction,
            speed * deltaSeconds);
        return true;
    }

    private static ViewerPhysicsVector3 Project(
        ViewerPhysicsVector3 value,
        ViewerPhysicsVector3 up)
    {
        if (up.Length <= 0d)
        {
            return value.Normalized();
        }

        double along = ViewerPhysicsVector3.Dot(value, up);
        return ViewerPhysicsVector3
            .Subtract(value, ViewerPhysicsVector3.Scale(up, along))
            .Normalized();
    }
}

/// <summary>
/// One vehicle driver input, in the exact ranges the runtime command ABI accepts.
/// </summary>
/// <param name="Throttle">The throttle, in <c>[0, 1]</c>.</param>
/// <param name="Brake">The brake, in <c>[0, 1]</c>.</param>
/// <param name="Steer">The steering angle, in <c>[-1, 1]</c>.</param>
/// <param name="HandBrake">The hand brake, in <c>[0, 1]</c>.</param>
/// <param name="Clutch">The clutch, in <c>[0, 1]</c>.</param>
/// <param name="Gear">
/// The requested gear: <c>0</c> leaves the choice to the drivetrain, <c>1</c> is reverse, <c>2</c>
/// is neutral, and <c>3</c> is the first forward gear.
/// </param>
/// <remarks>
/// The ranges are the ABI's, not the control's. The runtime rejects a whole step whose vehicle
/// input falls outside them, so the viewer clamps at the edge of the UI - where the user can still
/// see the control move to its limit - rather than discovering the refusal a frame later.
/// </remarks>
internal readonly record struct ViewerPhysicsVehicleInput(
    double Throttle,
    double Brake,
    double Steer,
    double HandBrake,
    double Clutch,
    int Gear)
{
    /// <summary>Gets the input of a vehicle nobody is driving.</summary>
    internal static ViewerPhysicsVehicleInput Neutral => new(0d, 0d, 0d, 0d, 0d, 0);

    /// <summary>The highest gear index the runtime accepts.</summary>
    internal const int MaxGear = 32;

    /// <summary>Returns the input clamped into the ranges the runtime accepts.</summary>
    /// <returns>The clamped input.</returns>
    internal ViewerPhysicsVehicleInput Clamped() => new(
        Unit(Throttle),
        Unit(Brake),
        Signed(Steer),
        Unit(HandBrake),
        Unit(Clutch),
        Math.Clamp(Gear, 0, MaxGear));

    /// <summary>Reports whether every component is already inside the accepted ranges.</summary>
    /// <returns><see langword="true"/> when the input needs no clamping.</returns>
    internal bool IsValid =>
        InUnit(Throttle) && InUnit(Brake) && InUnit(HandBrake) && InUnit(Clutch) &&
        double.IsFinite(Steer) && Steer >= -1d && Steer <= 1d &&
        Gear >= 0 && Gear <= MaxGear;

    /// <summary>Builds the runtime command this input submits.</summary>
    /// <param name="targetId">The vehicle's stable simulation identity.</param>
    /// <returns>The command, always already clamped.</returns>
    internal ViewerPhysicsRuntimeCommand ToCommand(ulong targetId)
    {
        ViewerPhysicsVehicleInput clamped = Clamped();
        return new ViewerPhysicsRuntimeCommand(
            ViewerPhysicsRuntimeCommandKind.VehicleInput,
            targetId,
            new ViewerPhysicsVector3(clamped.Throttle, clamped.Brake, clamped.Steer))
        {
            Point = new ViewerPhysicsVector3(clamped.HandBrake, clamped.Clutch, clamped.Gear),
        };
    }

    /// <summary>Formats the input for the status line.</summary>
    /// <returns>The status line.</returns>
    internal string Describe() => string.Create(
        CultureInfo.InvariantCulture,
        $"throttle {Throttle:0.00} · brake {Brake:0.00} · steer {Steer:+0.00;-0.00;0.00} · " +
        $"hand {HandBrake:0.00} · clutch {Clutch:0.00} · {DescribeGear()}");

    /// <summary>Formats the requested gear.</summary>
    /// <returns>The gear description.</returns>
    internal string DescribeGear() => Gear switch
    {
        0 => "gear auto",
        1 => "gear reverse",
        2 => "gear neutral",
        _ => string.Create(CultureInfo.InvariantCulture, $"gear {Gear - 2}"),
    };

    private static bool InUnit(double value) =>
        double.IsFinite(value) && value >= 0d && value <= 1d;

    private static double Unit(double value) =>
        double.IsFinite(value) ? Math.Clamp(value, 0d, 1d) : 0d;

    private static double Signed(double value) =>
        double.IsFinite(value) ? Math.Clamp(value, -1d, 1d) : 0d;
}

/// <summary>Builds the one-shot force and impulse commands the inspector's buttons submit.</summary>
internal static class ViewerPhysicsImpulseBuilder
{
    /// <summary>Builds one force or impulse command.</summary>
    /// <param name="kind">Whether the command is a force, impulse, torque, or angular impulse.</param>
    /// <param name="targetId">The stable simulation identity the command targets.</param>
    /// <param name="direction">The direction the command acts in.</param>
    /// <param name="magnitude">The magnitude, which must be positive and finite.</param>
    /// <param name="mode">How the value is integrated.</param>
    /// <param name="command">Receives the command.</param>
    /// <param name="error">Receives the refusal, or an empty string.</param>
    /// <returns><see langword="true"/> when the command is usable.</returns>
    internal static bool TryBuild(
        ViewerPhysicsRuntimeCommandKind kind,
        ulong targetId,
        ViewerPhysicsVector3 direction,
        double magnitude,
        ViewerPhysicsForceMode mode,
        out ViewerPhysicsRuntimeCommand command,
        out string error)
    {
        command = new ViewerPhysicsRuntimeCommand(kind, targetId, ViewerPhysicsVector3.Zero);
        if (targetId == 0UL)
        {
            error = "Select a simulated object first.";
            return false;
        }

        if (kind is not (ViewerPhysicsRuntimeCommandKind.Force
            or ViewerPhysicsRuntimeCommandKind.Impulse
            or ViewerPhysicsRuntimeCommandKind.Torque
            or ViewerPhysicsRuntimeCommandKind.AngularImpulse))
        {
            error = "Only forces, impulses, torques, and angular impulses take a magnitude.";
            return false;
        }

        ViewerPhysicsVector3 unit = direction.Normalized();
        if (unit.Length <= 0d)
        {
            error = "Enter a direction that is not zero.";
            return false;
        }

        if (!double.IsFinite(magnitude) || magnitude <= 0d)
        {
            error = "Enter a positive, finite magnitude.";
            return false;
        }

        command = new ViewerPhysicsRuntimeCommand(kind, targetId, unit, magnitude)
        {
            Mode = mode,
            WakeTarget = true,
        };
        error = string.Empty;
        return true;
    }
}
