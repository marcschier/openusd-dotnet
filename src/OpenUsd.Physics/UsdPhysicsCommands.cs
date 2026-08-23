// Copyright (c) marcschier. Licensed under the MIT License.

namespace OpenUsd.Physics;

/// <summary>
/// Identifies the kind of runtime command submitted with a <see cref="UsdPhysicsStepRequest"/>.
/// </summary>
public enum UsdPhysicsCommandKind
{
    /// <summary>Apply a continuous or one-shot force at the object's center of mass or a point.</summary>
    Force,

    /// <summary>Apply an instantaneous impulse at the object's center of mass or a point.</summary>
    Impulse,

    /// <summary>Teleport the object to an absolute pose, bypassing interpolation.</summary>
    Teleport,

    /// <summary>Set the next kinematic target pose for a kinematic rigid body.</summary>
    KinematicTarget,

    /// <summary>Move a character controller by a requested displacement.</summary>
    ControllerMove,

    /// <summary>Submit a vehicle drivetrain input (throttle, brake, steering).</summary>
    /// <remarks>
    /// <see cref="UsdPhysicsCommand.Vector"/> carries <c>(throttle, brake, steer)</c> and
    /// <see cref="UsdPhysicsCommand.Point"/> carries <c>(hand brake, clutch, gear)</c>. Throttle,
    /// brake, hand brake, and clutch are in <c>[0, 1]</c>, steer is in <c>[-1, 1]</c>, and the gear
    /// component is a non-negative whole number where <c>0</c> leaves the gear to the drivetrain
    /// and <c>n</c> selects gear index <c>n - 1</c>, which is reverse at <c>1</c>, neutral at
    /// <c>2</c>, and the first forward gear at <c>3</c>. Gear <c>0</c> asks the autobox to choose
    /// on a vehicle that declares one and otherwise holds the gear already targeted. An input
    /// outside those ranges rejects the whole step instead of being clamped.
    /// </remarks>
    VehicleInput,

    /// <summary>Apply a continuous or one-shot torque.</summary>
    Torque,

    /// <summary>Apply an instantaneous angular impulse.</summary>
    AngularImpulse,

    /// <summary>Discard the force and impulse accumulated for the object this step.</summary>
    ClearForce,

    /// <summary>Discard the torque and angular impulse accumulated for the object this step.</summary>
    ClearTorque,

    /// <summary>Replace the linear velocity of a dynamic rigid body.</summary>
    LinearVelocity,

    /// <summary>Replace the angular velocity of a dynamic rigid body.</summary>
    AngularVelocity,

    /// <summary>Wake a sleeping rigid body.</summary>
    Wake,

    /// <summary>Put a rigid body to sleep.</summary>
    Sleep,

    /// <summary>Replace the gravity vector of one physics scene.</summary>
    SceneGravity
}

/// <summary>
/// Selects how a <see cref="UsdPhysicsCommandKind.Force"/> or
/// <see cref="UsdPhysicsCommandKind.Impulse"/> value is integrated.
/// </summary>
public enum UsdPhysicsForceMode
{
    /// <summary>The value is a force or an impulse and is scaled by the inverse mass.</summary>
    Default,

    /// <summary>The value is an acceleration and ignores the mass of the object.</summary>
    Acceleration,

    /// <summary>The value is applied directly as a velocity change.</summary>
    VelocityChange
}

/// <summary>
/// Selects where a force or impulse command is applied on the target object.
/// </summary>
public enum UsdPhysicsApplicationPoint
{
    /// <summary>Apply at the center of mass, producing no torque.</summary>
    CenterOfMass,

    /// <summary>Apply at <see cref="UsdPhysicsCommand.Point"/> expressed in world space.</summary>
    World,

    /// <summary>Apply at <see cref="UsdPhysicsCommand.Point"/> expressed in object local space.</summary>
    Local
}

/// <summary>
/// Describes one immutable batched runtime command targeting a simulated object.
/// </summary>
/// <remarks>
/// Commands are submitted once per <see cref="UsdPhysicsSession.Step"/> call and applied in
/// submission order before the fixed sub-steps advance. A command targeting an object the active
/// backend does not support, or does not recognize, is reported as a diagnostic rather than applied.
/// Submission order is also replace order: a <see cref="UsdPhysicsCommandKind.ClearForce"/> placed
/// after an accumulating command in the same batch discards everything accumulated before it.
/// </remarks>
public sealed record UsdPhysicsCommand(
    UsdPhysicsCommandKind Kind,
    UsdPhysicsObjectId Target,
    UsdVec3d Vector,
    double Magnitude = 0) : IUsdDetachedResult
{
    private readonly UsdVec3d _point;

    /// <summary>Gets the kind of command applied to <see cref="Target"/>.</summary>
    public UsdPhysicsCommandKind Kind { get; } =
        Kind is < UsdPhysicsCommandKind.Force or > UsdPhysicsCommandKind.SceneGravity
            ? throw new ArgumentOutOfRangeException(nameof(Kind))
            : Kind;

    /// <summary>Gets the force, impulse, or input magnitude associated with <see cref="Kind"/>.</summary>
    /// <remarks>
    /// A non-zero magnitude makes <see cref="Vector"/> a direction: the runtime normalizes it and
    /// scales it by this value. A zero magnitude uses <see cref="Vector"/> unchanged.
    /// </remarks>
    public double Magnitude { get; } = double.IsFinite(Magnitude)
        ? Magnitude
        : throw new ArgumentOutOfRangeException(nameof(Magnitude), Magnitude, "The magnitude must be finite.");

    /// <summary>Gets how a force or impulse value is integrated.</summary>
    public UsdPhysicsForceMode Mode { get; init; }

    /// <summary>Gets where the force or impulse is applied on the target.</summary>
    public UsdPhysicsApplicationPoint Application { get; init; }

    /// <summary>Gets the application point used when <see cref="Application"/> is not the center of mass.</summary>
    public UsdVec3d Point
    {
        get => _point;
        init => _point = double.IsFinite(value.X) && double.IsFinite(value.Y) && double.IsFinite(value.Z)
            ? value
            : throw new ArgumentOutOfRangeException(nameof(value), value, "The application point must be finite.");
    }

    /// <summary>Gets a value indicating whether applying this command wakes a sleeping target.</summary>
    public bool WakeTarget { get; init; } = true;
}

/// <summary>
/// Reports what one <see cref="UsdPhysicsTransport.SubmitCommandsAsync"/> batch achieved.
/// </summary>
/// <param name="Accepted">The commands staged for the next simulation step.</param>
/// <param name="Rejected">The commands the world refused.</param>
/// <param name="Message">A sentence describing the outcome, always non-empty.</param>
/// <remarks>
/// A refusal is always reported rather than swallowed. A control that shows a throttle the runtime
/// never accepted is worse than one that says the input was refused, because the user would go on
/// steering a vehicle that is not being driven.
/// </remarks>
public sealed record UsdPhysicsCommandSubmission(int Accepted, int Rejected, string Message)
    : IUsdDetachedResult
{
    /// <summary>Gets the outcome of submitting nothing.</summary>
    public static UsdPhysicsCommandSubmission Empty { get; } =
        new(0, 0, "The runtime command batch was empty.");

    /// <summary>Gets the number of commands staged for the next simulation step.</summary>
    public int Accepted { get; } = Accepted >= 0
        ? Accepted
        : throw new ArgumentOutOfRangeException(nameof(Accepted), Accepted, "The accepted count must not be negative.");

    /// <summary>Gets the number of commands the world refused.</summary>
    public int Rejected { get; } = Rejected >= 0
        ? Rejected
        : throw new ArgumentOutOfRangeException(nameof(Rejected), Rejected, "The rejected count must not be negative.");

    /// <summary>Gets the sentence describing the outcome.</summary>
    public string Message { get; } = string.IsNullOrWhiteSpace(Message)
        ? throw new ArgumentException("The outcome message must not be blank.", nameof(Message))
        : Message;

    /// <summary>Gets a value indicating whether every submitted command was staged.</summary>
    public bool IsComplete => Rejected == 0;
}
