// Copyright (c) marcschier. Licensed under the MIT License.

using System.Collections.Immutable;

namespace OpenUsd.Physics;

/// <summary>
/// Identifies the kind of a <see cref="UsdPhysicsQueryRequest"/>.
/// </summary>
public enum UsdPhysicsQueryKind
{
    /// <summary>Cast an infinitesimally thin ray.</summary>
    Raycast,

    /// <summary>Sweep a sphere along a direction.</summary>
    Sweep,

    /// <summary>Report every collider overlapping a sphere.</summary>
    Overlap
}

/// <summary>
/// Filters which colliders a scene query can hit.
/// </summary>
public readonly record struct UsdPhysicsQueryFilter
{
    /// <summary>Gets a filter that includes every collider and excludes none.</summary>
    public static UsdPhysicsQueryFilter Default { get; } = new(uint.MaxValue, 0);

    /// <summary>Initializes a query filter from inclusion and exclusion masks.</summary>
    public UsdPhysicsQueryFilter(uint includeMask, uint excludeMask)
    {
        IncludeMask = includeMask;
        ExcludeMask = excludeMask;
    }

    /// <summary>Gets the collision-group bits a collider must have at least one of to be considered.</summary>
    public uint IncludeMask { get; }

    /// <summary>Gets the collision-group bits that exclude a collider even if included.</summary>
    public uint ExcludeMask { get; }
}

/// <summary>
/// Describes one immutable batched raycast, sweep, or overlap request.
/// </summary>
public sealed record UsdPhysicsQueryRequest
{
    /// <summary>Initializes a validated scene query request.</summary>
    public UsdPhysicsQueryRequest(
        UsdPhysicsQueryKind kind,
        UsdVec3d origin,
        UsdVec3d direction,
        double maxDistance,
        double radius = 0,
        UsdPhysicsQueryFilter? filter = null)
    {
        if (kind is < UsdPhysicsQueryKind.Raycast or > UsdPhysicsQueryKind.Overlap)
        {
            throw new ArgumentOutOfRangeException(nameof(kind));
        }
        ArgumentOutOfRangeException.ThrowIfNegative(maxDistance);
        if (!double.IsFinite(maxDistance))
        {
            throw new ArgumentOutOfRangeException(nameof(maxDistance), "The max distance must be finite.");
        }
        ArgumentOutOfRangeException.ThrowIfNegative(radius);
        if (kind is UsdPhysicsQueryKind.Sweep or UsdPhysicsQueryKind.Overlap)
        {
            ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(radius, 0);
        }

        Kind = kind;
        Origin = origin;
        Direction = direction;
        MaxDistance = maxDistance;
        Radius = radius;
        Filter = filter ?? UsdPhysicsQueryFilter.Default;
    }

    /// <summary>Gets the requested query kind.</summary>
    public UsdPhysicsQueryKind Kind { get; }

    /// <summary>Gets the world-space origin.</summary>
    public UsdVec3d Origin { get; }

    /// <summary>
    /// Gets the normalized world-space direction. Ignored for
    /// <see cref="UsdPhysicsQueryKind.Overlap"/>.
    /// </summary>
    public UsdVec3d Direction { get; }

    /// <summary>Gets the maximum world-space distance to travel or search.</summary>
    public double MaxDistance { get; }

    /// <summary>Gets the sphere radius for a sweep or overlap request.</summary>
    public double Radius { get; }

    /// <summary>Gets the collider inclusion/exclusion filter.</summary>
    /// <remarks>
    /// A collider is considered when it carries at least one bit of
    /// <see cref="UsdPhysicsQueryFilter.IncludeMask"/> and no bit of
    /// <see cref="UsdPhysicsQueryFilter.ExcludeMask"/>. A filter whose exclusions cancel every one of
    /// its inclusions can never hit and is rejected rather than being widened to accept everything.
    /// </remarks>
    public UsdPhysicsQueryFilter Filter { get; }

    /// <summary>Gets the caller-defined identifier echoed on every hit this request produces.</summary>
    public ulong UserId { get; init; }

    /// <summary>Gets the index of the physics scene to query.</summary>
    public uint SceneIndex { get; init; }

    /// <summary>
    /// Gets the maximum number of hits to retain for this request; zero means unbounded within the
    /// per request hit budget of the session.
    /// </summary>
    /// <remarks>
    /// Every request is bounded, because the runtime writes into caller-owned buffers whose capacity
    /// is fixed before the first query runs. Zero therefore requests the whole budget declared by
    /// <see cref="UsdPhysicsSessionOptions.MaxQueryHitsPerRequest"/>, and a larger bound is lowered
    /// onto that same budget. The retained hits are always the nearest ones in deterministic order,
    /// so shrinking this bound removes the farthest hits rather than an arbitrary subset.
    /// </remarks>
    public uint MaxHits { get; init; }

    /// <summary>Gets a value indicating whether the query stops at the first hit found.</summary>
    public bool AnyHit { get; init; }

    /// <summary>Gets a value indicating whether static colliders are ignored.</summary>
    public bool ExcludeStatic { get; init; }

    /// <summary>Gets a value indicating whether movable colliders are ignored.</summary>
    public bool ExcludeDynamic { get; init; }

    /// <summary>Gets a value indicating whether trigger colliders are ignored.</summary>
    public bool ExcludeTriggers { get; init; }

    /// <summary>Gets a value indicating whether a sweep reports colliders it already overlaps.</summary>
    public bool ReportInitialOverlap { get; init; }
}

/// <summary>
/// Names the optional geometry a <see cref="UsdPhysicsQueryHit"/> carries.
/// </summary>
/// <remarks>
/// A field that the underlying runtime cannot attribute is reported as absent rather than as a
/// guessed default, so an overlap hit typically carries neither a position nor a distance.
/// </remarks>
[Flags]
public enum UsdPhysicsQueryHitFields
{
    /// <summary>The hit carries no optional geometry.</summary>
    None = 0,

    /// <summary>The hit position is meaningful.</summary>
    Position = 1 << 0,

    /// <summary>The hit normal is meaningful.</summary>
    Normal = 1 << 1,

    /// <summary>The hit distance is meaningful.</summary>
    Distance = 1 << 2,

    /// <summary>The face index names an element of the hit geometry.</summary>
    FaceIndex = 1 << 3,

    /// <summary>The sweep started already overlapping the hit collider.</summary>
    InitialOverlap = 1 << 4,

    /// <summary>The hit collider is a trigger volume.</summary>
    Trigger = 1 << 5
}

/// <summary>
/// Describes one immutable scene query hit.
/// </summary>
public sealed record UsdPhysicsQueryHit(
    UsdPhysicsObjectId ObjectId,
    UsdVec3d Position,
    UsdVec3d Normal,
    double Distance) : IUsdDetachedResult
{
    /// <summary>Gets the distance from the query origin to the hit, in stage units.</summary>
    public double Distance { get; } = double.IsFinite(Distance) && Distance >= 0
        ? Distance
        : throw new ArgumentOutOfRangeException(
            nameof(Distance),
            Distance,
            "The distance must be finite and non-negative.");

    /// <summary>Gets the collider identity of the hit, when the runtime attributed one.</summary>
    public UsdPhysicsObjectId? ColliderId { get; init; }

    /// <summary>Gets which optional fields of this hit are meaningful.</summary>
    public UsdPhysicsQueryHitFields Fields { get; init; }

    /// <summary>Gets the face or element index of the hit geometry.</summary>
    /// <remarks>
    /// Only meaningful when <see cref="Fields"/> includes
    /// <see cref="UsdPhysicsQueryHitFields.FaceIndex"/>.
    /// </remarks>
    public uint FaceIndex { get; init; }

    /// <summary>Gets a value indicating whether the hit collider is a trigger volume.</summary>
    public bool IsTrigger => (Fields & UsdPhysicsQueryHitFields.Trigger) != 0;

    /// <summary>Gets a value indicating whether a sweep started already overlapping the hit collider.</summary>
    public bool HadInitialOverlap => (Fields & UsdPhysicsQueryHitFields.InitialOverlap) != 0;
}

/// <summary>
/// Contains the immutable ordered hits produced for one <see cref="UsdPhysicsQueryRequest"/>.
/// </summary>
/// <remarks>
/// Hits beyond <see cref="UsdPhysicsSessionOptions.MaxQueryHitsPerRequest"/> are dropped; the
/// nearest deterministic prefix is retained and <see cref="DroppedCount"/> reports how many were
/// discarded.
/// </remarks>
public sealed class UsdPhysicsQueryResult : IUsdDetachedResult, IEquatable<UsdPhysicsQueryResult>
{
    private readonly ImmutableArray<UsdPhysicsQueryHit> _hits;

    /// <summary>Gets an empty, non-overflowed query result.</summary>
    public static UsdPhysicsQueryResult Empty { get; } = new([], droppedCount: 0);

    /// <summary>Initializes a query result by defensively copying hits.</summary>
    /// <param name="hits">The retained hits, ordered nearest first.</param>
    /// <param name="droppedCount">The number of hits that were discarded.</param>
    /// <param name="droppedCountIsLowerBound">
    /// <see langword="true"/> when the simulation backend discarded hits before they could be counted,
    /// which makes <paramref name="droppedCount"/> a lower bound rather than an exact count.
    /// </param>
    public UsdPhysicsQueryResult(
        IEnumerable<UsdPhysicsQueryHit> hits,
        int droppedCount,
        bool droppedCountIsLowerBound = false)
    {
        ArgumentNullException.ThrowIfNull(hits);
        ArgumentOutOfRangeException.ThrowIfNegative(droppedCount);

        var builder = ImmutableArray.CreateBuilder<UsdPhysicsQueryHit>();
        foreach (UsdPhysicsQueryHit hit in hits)
        {
            ArgumentNullException.ThrowIfNull(hit);
            builder.Add(hit);
        }
        _hits = builder.ToImmutable();
        DroppedCount = droppedCount;
        DroppedCountIsLowerBound = droppedCountIsLowerBound;
    }

    /// <summary>Gets retained hits ordered nearest first.</summary>
    public IReadOnlyList<UsdPhysicsQueryHit> Hits => _hits;

    /// <summary>Gets the number of hits dropped because the result reached its bounded capacity.</summary>
    /// <remarks>
    /// This is exact unless <see cref="DroppedCountIsLowerBound"/> is set, in which case at least this
    /// many hits were dropped and the true number cannot be known.
    /// </remarks>
    public int DroppedCount { get; }

    /// <summary>
    /// Gets a value indicating whether <see cref="DroppedCount"/> is only a lower bound.
    /// </summary>
    /// <remarks>
    /// The simulation backend gathers touching hits into a scratch buffer of its own and silently
    /// discards an arbitrary subset once that buffer is full, without reporting how many it discarded.
    /// When that happens the result still carries a deterministic nearest-first prefix, but the number
    /// of hits that never reached the runtime is unknowable and is never claimed to be exact.
    /// </remarks>
    public bool DroppedCountIsLowerBound { get; }

    /// <summary>Gets a value indicating whether any hit was dropped.</summary>
    public bool IsOverflowed => DroppedCount > 0 || DroppedCountIsLowerBound;

    /// <inheritdoc/>
    public bool Equals(UsdPhysicsQueryResult? other) =>
        other is not null &&
        DroppedCount == other.DroppedCount &&
        DroppedCountIsLowerBound == other.DroppedCountIsLowerBound &&
        _hits.AsSpan().SequenceEqual(other._hits.AsSpan());

    /// <inheritdoc/>
    public override bool Equals(object? obj) =>
        obj is UsdPhysicsQueryResult other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(DroppedCount);
        hash.Add(DroppedCountIsLowerBound);
        foreach (UsdPhysicsQueryHit hit in _hits)
        {
            hash.Add(hit);
        }
        return hash.ToHashCode();
    }

    /// <summary>Determines whether two query results have equal hits and dropped counts.</summary>
    public static bool operator ==(UsdPhysicsQueryResult? left, UsdPhysicsQueryResult? right) =>
        EqualityComparer<UsdPhysicsQueryResult>.Default.Equals(left, right);

    /// <summary>Determines whether two query results differ.</summary>
    public static bool operator !=(UsdPhysicsQueryResult? left, UsdPhysicsQueryResult? right) =>
        !(left == right);
}
