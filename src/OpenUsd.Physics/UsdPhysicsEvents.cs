// Copyright (c) marcschier. Licensed under the MIT License.

using System.Collections.Immutable;

namespace OpenUsd.Physics;

/// <summary>
/// Identifies the kind of a <see cref="UsdPhysicsEvent"/>.
/// </summary>
public enum UsdPhysicsEventKind
{
    /// <summary>Two colliders began touching.</summary>
    ContactBegan,

    /// <summary>Two colliders stopped touching.</summary>
    ContactEnded,

    /// <summary>An object entered a trigger volume.</summary>
    TriggerEnter,

    /// <summary>An object exited a trigger volume.</summary>
    TriggerExit,

    /// <summary>A rigid body changed its sleep state.</summary>
    SleepStateChanged,

    /// <summary>A joint exceeded its configured break force or torque.</summary>
    JointBreak,

    /// <summary>A character controller hit another collider while moving.</summary>
    ControllerHit,

    /// <summary>A vehicle drivetrain changed gear.</summary>
    VehicleGearChange
}

/// <summary>
/// Describes one immutable simulation event with a stable identity and step/time revision.
/// </summary>
/// <remarks>
/// Events of one step are reported in one deterministic total order that does not depend on the
/// worker thread count, on solver iteration order, or on how many events were dropped: step index,
/// then kind, then <see cref="Primary"/>, <see cref="Secondary"/>, <see cref="PrimaryElement"/>, and
/// <see cref="SecondaryElement"/>. Identities are the stable object identities of the build page and
/// never a native handle, a prim, or a stage reference.
/// </remarks>
public sealed record UsdPhysicsEvent(
    UsdPhysicsEventKind Kind,
    UsdPhysicsObjectId Primary,
    UsdPhysicsObjectId? Secondary,
    ulong StepIndex,
    double TimeCode) : IUsdDetachedResult
{
    private readonly double? _impulse;

    /// <summary>Gets the simulation time code, in stage time units, at which the event occurred.</summary>
    public double TimeCode { get; } = double.IsFinite(TimeCode)
        ? TimeCode
        : throw new ArgumentOutOfRangeException(nameof(TimeCode), TimeCode, "The time code must be finite.");

    /// <summary>Gets the collider identity on the <see cref="Primary"/> side, when one is attributed.</summary>
    /// <remarks>
    /// For a contact this is the collider of <see cref="Primary"/>, for a trigger event it is the
    /// trigger collider, for a controller hit it is unused, and for a joint break it is the second
    /// jointed body rather than a collider.
    /// </remarks>
    public UsdPhysicsObjectId? PrimaryElement { get; init; }

    /// <summary>Gets the collider identity on the <see cref="Secondary"/> side, when one is attributed.</summary>
    public UsdPhysicsObjectId? SecondaryElement { get; init; }

    /// <summary>Gets the world-space contact point, when the runtime reported one.</summary>
    public UsdVec3d? Position { get; init; }

    /// <summary>Gets the world-space contact normal, when the runtime reported one.</summary>
    public UsdVec3d? Normal { get; init; }

    /// <summary>Gets the contact impulse magnitude, when the runtime reported one.</summary>
    public double? Impulse
    {
        get => _impulse;
        init => _impulse = value is null || double.IsFinite(value.GetValueOrDefault())
            ? value
            : throw new ArgumentOutOfRangeException(nameof(value), value, "The impulse must be finite.");
    }

    /// <summary>Gets a value indicating whether the event reports a sleeping rather than a waking body.</summary>
    /// <remarks>Only meaningful for <see cref="UsdPhysicsEventKind.SleepStateChanged"/>.</remarks>
    public bool IsAsleep { get; init; }

    /// <summary>Gets the gear the drivetrain left, when the runtime reported a gear change.</summary>
    /// <remarks>
    /// Only reported for <see cref="UsdPhysicsEventKind.VehicleGearChange"/>. Gear index 0 is
    /// reverse, 1 is neutral, and 2 and above are the authored forward gears in order.
    /// </remarks>
    public int? PreviousGear { get; init; }

    /// <summary>Gets the gear the drivetrain entered, when the runtime reported a gear change.</summary>
    /// <remarks>Only reported for <see cref="UsdPhysicsEventKind.VehicleGearChange"/>.</remarks>
    public int? Gear { get; init; }
}

/// <summary>
/// Contains an immutable ordered set of simulation events produced by one step.
/// </summary>
/// <remarks>
/// Events beyond <see cref="UsdPhysicsSessionOptions.MaxEventsPerStep"/> are dropped; the exact
/// deterministic prefix is retained and <see cref="DroppedCount"/> reports how many were discarded.
/// </remarks>
public sealed class UsdPhysicsEventBatch : IUsdDetachedResult, IEquatable<UsdPhysicsEventBatch>
{
    private readonly ImmutableArray<UsdPhysicsEvent> _entries;

    /// <summary>Gets an empty event batch.</summary>
    public static UsdPhysicsEventBatch Empty { get; } = new([], droppedCount: 0);

    /// <summary>Initializes an event batch by defensively copying entries.</summary>
    public UsdPhysicsEventBatch(IEnumerable<UsdPhysicsEvent> entries, int droppedCount)
    {
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentOutOfRangeException.ThrowIfNegative(droppedCount);

        var builder = ImmutableArray.CreateBuilder<UsdPhysicsEvent>();
        foreach (UsdPhysicsEvent entry in entries)
        {
            ArgumentNullException.ThrowIfNull(entry);
            builder.Add(entry);
        }
        _entries = builder.ToImmutable();
        DroppedCount = droppedCount;
    }

    /// <summary>Gets retained events in deterministic order.</summary>
    public IReadOnlyList<UsdPhysicsEvent> Entries => _entries;

    /// <summary>Gets the number of events dropped because the batch reached its bounded capacity.</summary>
    public int DroppedCount { get; }

    /// <summary>Gets a value indicating whether any event was dropped this step.</summary>
    public bool IsOverflowed => DroppedCount > 0;

    /// <inheritdoc/>
    public bool Equals(UsdPhysicsEventBatch? other) =>
        other is not null &&
        DroppedCount == other.DroppedCount &&
        _entries.AsSpan().SequenceEqual(other._entries.AsSpan());

    /// <inheritdoc/>
    public override bool Equals(object? obj) =>
        obj is UsdPhysicsEventBatch other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(DroppedCount);
        foreach (UsdPhysicsEvent entry in _entries)
        {
            hash.Add(entry);
        }
        return hash.ToHashCode();
    }

    /// <summary>Determines whether two event batches have equal entries and dropped counts.</summary>
    public static bool operator ==(UsdPhysicsEventBatch? left, UsdPhysicsEventBatch? right) =>
        EqualityComparer<UsdPhysicsEventBatch>.Default.Equals(left, right);

    /// <summary>Determines whether two event batches differ.</summary>
    public static bool operator !=(UsdPhysicsEventBatch? left, UsdPhysicsEventBatch? right) =>
        !(left == right);
}
