// Copyright (c) marcschier. Licensed under the MIT License.

using System.Collections.Immutable;

namespace OpenUsd.Physics.Interop;

/// <summary>
/// Detaches native simulation event records onto the immutable public event contract.
/// </summary>
/// <remarks>
/// Detaching is the only place a native record becomes a managed object. It copies every field the
/// runtime attributed and reports the rest as absent rather than as a guessed default, so a
/// consumer can always tell "no impulse was reported" from "the impulse was zero". Nothing here
/// retains a pointer, a native handle, a prim, or a stage reference: the result is safe to publish
/// after the leased native buffers have been reused by the next step.
/// </remarks>
internal static class PhysxEventAdapter
{
    /// <summary>Detaches one retained event prefix onto the public batch contract.</summary>
    internal static UsdPhysicsEventBatch Detach(
        ReadOnlySpan<PhysxEventRecord> records,
        double timeCode,
        uint droppedCount)
    {
        if (records.IsEmpty && droppedCount == 0)
        {
            return UsdPhysicsEventBatch.Empty;
        }

        var entries = ImmutableArray.CreateBuilder<UsdPhysicsEvent>(records.Length);
        foreach (PhysxEventRecord record in records)
        {
            entries.Add(Detach(record, timeCode));
        }
        return new UsdPhysicsEventBatch(entries.ToImmutable(), (int)Math.Min(droppedCount, int.MaxValue));
    }

    /// <summary>Detaches one native event record onto the public event contract.</summary>
    internal static UsdPhysicsEvent Detach(in PhysxEventRecord record, double timeCode)
    {
        var type = (PhysxEventType)record.Type;
        var flags = (PhysxEventFlags)record.Flags;
        bool detailIsShape = (flags & PhysxEventFlags.DetailIsShape) != 0;
        bool isGearChange = type == PhysxEventType.VehicleGearChange;

        return new UsdPhysicsEvent(
            MapKind(type),
            new UsdPhysicsObjectId(record.Id0, PrimaryKind(type)),
            Identity(record.Id1, SecondaryKind(type)),
            record.StepIndex,
            timeCode)
        {
            PrimaryElement = isGearChange
                ? null
                : Identity(record.Detail0, detailIsShape ? UsdPhysicsObjectKind.Collider : SecondaryKind(type)),
            SecondaryElement = isGearChange
                ? null
                : Identity(
                    record.Detail1,
                    detailIsShape ? UsdPhysicsObjectKind.Collider : UsdPhysicsObjectKind.Unknown),
            Position = (flags & PhysxEventFlags.HasPosition) != 0 ? Vector(record.Position) : null,
            Normal = (flags & PhysxEventFlags.HasNormal) != 0 ? Vector(record.Normal) : null,
            Impulse = (flags & PhysxEventFlags.HasImpulse) != 0 ? record.Impulse : null,
            IsAsleep = type == PhysxEventType.Sleep,
            PreviousGear = isGearChange ? Gear(record.Detail0) : null,
            Gear = isGearChange ? Gear(record.Detail1) : null
        };
    }

    /// <summary>Maps a native event type onto the public event kind.</summary>
    internal static UsdPhysicsEventKind MapKind(PhysxEventType type) => type switch
    {
        PhysxEventType.Sleep or PhysxEventType.Wake => UsdPhysicsEventKind.SleepStateChanged,
        PhysxEventType.JointBreak => UsdPhysicsEventKind.JointBreak,
        PhysxEventType.ContactFound => UsdPhysicsEventKind.ContactBegan,
        PhysxEventType.ContactLost => UsdPhysicsEventKind.ContactEnded,
        PhysxEventType.TriggerEnter => UsdPhysicsEventKind.TriggerEnter,
        PhysxEventType.TriggerLeave => UsdPhysicsEventKind.TriggerExit,
        PhysxEventType.ControllerHit => UsdPhysicsEventKind.ControllerHit,
        PhysxEventType.VehicleGearChange => UsdPhysicsEventKind.VehicleGearChange,
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, "The runtime reported an unknown event type.")
    };

    private static UsdPhysicsObjectKind PrimaryKind(PhysxEventType type) => type switch
    {
        PhysxEventType.JointBreak => UsdPhysicsObjectKind.Joint,
        PhysxEventType.ControllerHit => UsdPhysicsObjectKind.Controller,
        PhysxEventType.VehicleGearChange => UsdPhysicsObjectKind.Vehicle,
        _ => UsdPhysicsObjectKind.Unknown
    };

    private static UsdPhysicsObjectKind SecondaryKind(PhysxEventType type) => type switch
    {
        PhysxEventType.JointBreak => UsdPhysicsObjectKind.RigidBody,
        _ => UsdPhysicsObjectKind.Unknown
    };

    private static UsdPhysicsObjectId? Identity(ulong value, UsdPhysicsObjectKind kind) =>
        value == PhysxAbi.InvalidId ? null : new UsdPhysicsObjectId(value, kind);

    private static int Gear(ulong value) => (int)Math.Min(value, int.MaxValue);

    private static UsdVec3d Vector(in PhysxVec3f value) => new(value.X, value.Y, value.Z);
}
