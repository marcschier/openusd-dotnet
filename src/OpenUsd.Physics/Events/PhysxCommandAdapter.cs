// Copyright (c) marcschier. Licensed under the MIT License.

using System.Globalization;

namespace OpenUsd.Physics.Interop;

/// <summary>
/// Translates immutable public runtime commands into the native command record and validates them.
/// </summary>
/// <remarks>
/// Validation is deliberately duplicated here and in the native runtime. The managed check rejects a
/// malformed command before it costs an interop transition and turns the rejection into a diagnostic
/// with a stable code; the native check is authoritative because the native runtime also accepts
/// commands from callers that never went through this adapter. Both use the same rules, so a command
/// that this adapter accepts is never rejected natively for a reason this adapter could have caught.
///
/// Ordering is submission ordering: the runtime applies the staged records in array order before the
/// fixed sub-steps advance, so a clear command placed after an accumulating command in the same
/// batch discards what was accumulated before it. Nothing here allocates per command.
/// </remarks>
internal static class PhysxCommandAdapter
{
    /// <summary>Translates one public command into its native record.</summary>
    /// <param name="command">The immutable public command.</param>
    /// <param name="native">The translated native record; undefined when translation fails.</param>
    /// <param name="rejection">The rejection reason; <see langword="null"/> on success.</param>
    /// <returns><see langword="true"/> when the command was accepted.</returns>
    internal static bool TryTranslate(
        UsdPhysicsCommand command,
        out PhysxCommand native,
        out string? rejection)
    {
        ArgumentNullException.ThrowIfNull(command);
        native = default;

        if (!TryMapType(command, out PhysxCommandType type, out rejection))
        {
            return false;
        }

        if (command.Target.IsNone)
        {
            rejection = "The command targets the reserved zero identity.";
            return false;
        }

        PhysxCommandFlags flags = MapFlags(command);
        uint allowed = (uint)AllowedFlags(type);
        if (((uint)flags & ~allowed) != 0)
        {
            rejection = string.Create(
                CultureInfo.InvariantCulture,
                $"The command kind {command.Kind} does not accept the requested modifiers.");
            return false;
        }

        bool usesVector = UsesVector(type);
        bool usesPoint = UsesPoint(type) && (flags & PhysxCommandFlags.PointCenterOfMass) == 0;

        if (!IsFinite(command.Vector))
        {
            rejection = "The command declares a non finite vector.";
            return false;
        }

        if (!usesVector && !IsZero(command.Vector))
        {
            rejection = string.Create(
                CultureInfo.InvariantCulture,
                $"The command kind {command.Kind} does not read a vector.");
            return false;
        }

        if ((flags & PhysxCommandFlags.Magnitude) != 0 && IsZero(command.Vector))
        {
            rejection = "The command declares a magnitude with a zero length direction.";
            return false;
        }

        if (!usesPoint && !IsZero(command.Point))
        {
            rejection = string.Create(
                CultureInfo.InvariantCulture,
                $"The command kind {command.Kind} does not read an application point.");
            return false;
        }

        native = new PhysxCommand
        {
            TargetId = command.Target.Value,
            Type = (uint)type,
            Flags = (uint)flags,
            Vector = usesVector ? Vector(command.Vector) : default,
            Point = usesPoint ? Vector(command.Point) : default,
            Scalar = (flags & PhysxCommandFlags.Magnitude) != 0 ? (float)command.Magnitude : 0
        };

        if (type == PhysxCommandType.VehicleInput && !ValidateVehicleInput(in native, out rejection))
        {
            native = default;
            return false;
        }

        rejection = null;
        return true;
    }

    /// <summary>Stages a whole command batch, reporting the first command that was rejected.</summary>
    /// <remarks>
    /// The destination span is caller owned and already sized for the batch, so staging performs no
    /// allocation. Accepted commands keep their submission index, which is also their apply order.
    /// </remarks>
    internal static bool TryTranslateBatch(
        IReadOnlyList<UsdPhysicsCommand> commands,
        Span<PhysxCommand> destination,
        out int acceptedCount,
        out int rejectedIndex,
        out string? rejection)
    {
        ArgumentNullException.ThrowIfNull(commands);
        acceptedCount = 0;
        rejectedIndex = -1;
        rejection = null;

        if (commands.Count > destination.Length)
        {
            rejectedIndex = destination.Length;
            rejection = string.Create(
                CultureInfo.InvariantCulture,
                $"The command batch of {commands.Count} exceeds the staged capacity of {destination.Length}.");
            return false;
        }

        // The whole batch is validated before any of it is staged. Translating in place would
        // leave the destination holding the prefix of a batch the caller was told was refused, and
        // a caller that trusted the refusal would then submit those commands on the next step
        // without ever having asked for them. Validation is pure, so the second pass cannot fail.
        for (int index = 0; index < commands.Count; index++)
        {
            if (!TryTranslate(commands[index], out _, out rejection))
            {
                rejectedIndex = index;
                return false;
            }
        }

        for (int index = 0; index < commands.Count; index++)
        {
            if (!TryTranslate(commands[index], out PhysxCommand native, out rejection))
            {
                // Unreachable: the pass above accepted every command, and translation reads only
                // the immutable command record. Restoring the count keeps the contract honest even
                // if that ever stops being true.
                rejectedIndex = index;
                acceptedCount = 0;
                return false;
            }

            destination[index] = native;
            acceptedCount++;
        }
        return true;
    }

    /// <summary>Returns the modifier flags the given native command type accepts.</summary>
    internal static PhysxCommandFlags AllowedFlags(PhysxCommandType type) => type switch
    {
        PhysxCommandType.Teleport => PhysxCommandFlags.NoWake,
        PhysxCommandType.SetLinearVelocity or PhysxCommandType.SetAngularVelocity =>
            PhysxCommandFlags.Magnitude | PhysxCommandFlags.NoWake,
        PhysxCommandType.AddForce or PhysxCommandType.AddTorque =>
            PhysxCommandFlags.Magnitude | PhysxCommandFlags.ModeAcceleration | PhysxCommandFlags.NoWake,
        PhysxCommandType.AddImpulse or PhysxCommandType.AddAngularImpulse =>
            PhysxCommandFlags.Magnitude | PhysxCommandFlags.ModeVelocityChange | PhysxCommandFlags.NoWake,
        // An application point is delivered through PxRigidBodyExt, which supports only the plain
        // force and impulse modes because it has to convert the force into a torque about the
        // centre of mass. The command type already selects which of the two applies, so no force
        // mode modifier is accepted; an acceleration or a velocity change at the centre of mass is
        // asked for with AddForce or AddImpulse, which is equivalent and unrestricted.
        PhysxCommandType.AddForceAtPoint or PhysxCommandType.AddImpulseAtPoint =>
            PhysxCommandFlags.Magnitude | PhysxCommandFlags.PointLocal |
            PhysxCommandFlags.PointCenterOfMass | PhysxCommandFlags.NoWake,
        PhysxCommandType.SetSceneGravity => PhysxCommandFlags.Magnitude,

        // A controller move may be authored as a direction plus a distance, exactly as the native
        // validator allows; every remaining type carries no modifier at all.
        PhysxCommandType.MoveController => PhysxCommandFlags.Magnitude,
        _ => PhysxCommandFlags.None
    };

    /// <summary>Applies the vehicle input ranges the native ABI documents.</summary>
    /// <remarks>
    /// The gear is the dangerous component: the runtime narrows it into an index over fixed size
    /// gearbox and autobox arrays, so a negative, fractional, or oversized value would read outside
    /// them. Rejecting rather than clamping is deliberate - a silently clamped throttle would make
    /// the vehicle behave differently from what the control that produced it shows.
    /// </remarks>
    private static bool ValidateVehicleInput(in PhysxCommand native, out string? rejection)
    {
        if (!IsUnit(native.Vector.X) ||
            !IsUnit(native.Vector.Y) ||
            !IsUnit(native.Point.X) ||
            !IsUnit(native.Point.Y))
        {
            rejection =
                "The vehicle input declares a throttle, brake, hand brake, or clutch outside [0, 1].";
            return false;
        }

        if (!float.IsFinite(native.Vector.Z) || native.Vector.Z < -1f || native.Vector.Z > 1f)
        {
            rejection = "The vehicle input declares a steer outside [-1, 1].";
            return false;
        }

        float gear = native.Point.Z;
        if (!float.IsFinite(gear) || gear < 0f || MathF.Floor(gear) != gear)
        {
            rejection = "The vehicle input declares a gear that is not a non negative whole number.";
            return false;
        }

        if (gear > PhysxAbi.MaxVehicleGears)
        {
            rejection = string.Create(
                CultureInfo.InvariantCulture,
                $"The vehicle input declares a gear beyond the budget of {PhysxAbi.MaxVehicleGears}.");
            return false;
        }

        rejection = null;
        return true;
    }

    private static bool TryMapType(
        UsdPhysicsCommand command,
        out PhysxCommandType type,
        out string? rejection)
    {
        bool atPoint = command.Application != UsdPhysicsApplicationPoint.CenterOfMass;
        switch (command.Kind)
        {
            case UsdPhysicsCommandKind.Force:
                type = atPoint ? PhysxCommandType.AddForceAtPoint : PhysxCommandType.AddForce;
                break;
            case UsdPhysicsCommandKind.Impulse:
                type = atPoint ? PhysxCommandType.AddImpulseAtPoint : PhysxCommandType.AddImpulse;
                break;
            case UsdPhysicsCommandKind.Torque:
                type = PhysxCommandType.AddTorque;
                break;
            case UsdPhysicsCommandKind.AngularImpulse:
                type = PhysxCommandType.AddAngularImpulse;
                break;
            case UsdPhysicsCommandKind.ClearForce:
                type = PhysxCommandType.ClearForce;
                break;
            case UsdPhysicsCommandKind.ClearTorque:
                type = PhysxCommandType.ClearTorque;
                break;
            case UsdPhysicsCommandKind.LinearVelocity:
                type = PhysxCommandType.SetLinearVelocity;
                break;
            case UsdPhysicsCommandKind.AngularVelocity:
                type = PhysxCommandType.SetAngularVelocity;
                break;
            case UsdPhysicsCommandKind.Wake:
                type = PhysxCommandType.Wake;
                break;
            case UsdPhysicsCommandKind.Sleep:
                type = PhysxCommandType.Sleep;
                break;
            case UsdPhysicsCommandKind.SceneGravity:
                type = PhysxCommandType.SetSceneGravity;
                break;
            case UsdPhysicsCommandKind.ControllerMove:
                type = PhysxCommandType.MoveController;
                break;
            case UsdPhysicsCommandKind.VehicleInput:
                type = PhysxCommandType.VehicleInput;
                break;
            default:
                type = default;
                rejection = string.Create(
                    CultureInfo.InvariantCulture,
                    $"The command kind {command.Kind} is not carried by the retained world command ABI.");
                return false;
        }

        rejection = null;
        return true;
    }

    private static PhysxCommandFlags MapFlags(UsdPhysicsCommand command)
    {
        PhysxCommandFlags flags = PhysxCommandFlags.None;
        if (command.Magnitude != 0)
        {
            flags |= PhysxCommandFlags.Magnitude;
        }
        if (!command.WakeTarget)
        {
            flags |= PhysxCommandFlags.NoWake;
        }

        flags |= command.Application switch
        {
            UsdPhysicsApplicationPoint.Local => PhysxCommandFlags.PointLocal,
            _ => PhysxCommandFlags.None
        };

        flags |= command.Mode switch
        {
            UsdPhysicsForceMode.Acceleration => PhysxCommandFlags.ModeAcceleration,
            UsdPhysicsForceMode.VelocityChange => PhysxCommandFlags.ModeVelocityChange,
            _ => PhysxCommandFlags.None
        };
        return flags;
    }

    private static bool UsesVector(PhysxCommandType type) => type switch
    {
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
        PhysxCommandType.VehicleInput => true,
        _ => false
    };

    private static bool UsesPoint(PhysxCommandType type) =>
        type is PhysxCommandType.AddForceAtPoint
            or PhysxCommandType.AddImpulseAtPoint
            or PhysxCommandType.VehicleInput;

    private static bool IsFinite(UsdVec3d value) =>
        double.IsFinite(value.X) && double.IsFinite(value.Y) && double.IsFinite(value.Z);

    private static bool IsZero(UsdVec3d value) => value.X == 0 && value.Y == 0 && value.Z == 0;

    private static bool IsUnit(float value) => float.IsFinite(value) && value >= 0f && value <= 1f;

    private static PhysxVec3f Vector(UsdVec3d value) => new((float)value.X, (float)value.Y, (float)value.Z);
}
