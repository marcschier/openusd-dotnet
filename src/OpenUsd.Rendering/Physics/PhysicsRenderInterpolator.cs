// Copyright (c) marcschier. Licensed under the MIT License.

namespace OpenUsd.Rendering;

/// <summary>
/// Reports how a render update derived its transform overrides.
/// </summary>
public enum PhysicsRenderUpdateStatus
{
    /// <summary>No complete snapshot was available, so no override was produced.</summary>
    Empty = 0,

    /// <summary>
    /// The two latest snapshots were not continuous, so every entity was snapped to the latest
    /// pose instead of being interpolated.
    /// </summary>
    Snapped = 1,

    /// <summary>The two latest complete snapshots were interpolated.</summary>
    Interpolated = 2
}

/// <summary>
/// Reports the outcome of one physics render update.
/// </summary>
/// <param name="Status">How the update derived its overrides.</param>
/// <param name="Alpha">The interpolation factor used between the two latest snapshots.</param>
/// <param name="InterpolatedCount">The number of entities interpolated.</param>
/// <param name="SnappedCount">The number of entities snapped to the latest pose.</param>
/// <param name="DroppedCount">The number of entities dropped because override storage was full.</param>
/// <param name="Revision">The monotonic revision of the produced override set.</param>
public readonly record struct PhysicsRenderUpdateResult(
    PhysicsRenderUpdateStatus Status,
    double Alpha,
    int InterpolatedCount,
    int SnappedCount,
    int DroppedCount,
    ulong Revision)
{
    /// <summary>Gets the number of entities the update produced an override for.</summary>
    public int Count => checked(InterpolatedCount + SnappedCount);
}

/// <summary>
/// Reports one entity's renderer-neutral transform override for the current render update.
/// </summary>
/// <param name="Id">The stable simulation identity of the entity.</param>
/// <param name="Position">The world-space position, in stage units.</param>
/// <param name="Orientation">The canonical world-space orientation.</param>
/// <param name="Snapped">
/// Whether the pose was snapped rather than interpolated because the entity was new, missing from
/// the earlier snapshot, or carried by discontinuous snapshots.
/// </param>
public readonly record struct PhysicsRenderTransformOverride(
    PhysicsRenderObjectId Id,
    UsdVec3d Position,
    PhysicsRenderOrientation Orientation,
    bool Snapped);

/// <summary>
/// Exposes the transform overrides produced by one render update without copying them.
/// </summary>
public readonly record struct PhysicsRenderOverrideView
{
    private readonly ReadOnlyMemory<PhysicsRenderTransformOverride> _items;

    /// <summary>Initializes a view over produced overrides.</summary>
    /// <param name="items">The produced overrides.</param>
    /// <param name="revision">The monotonic revision of the override set.</param>
    public PhysicsRenderOverrideView(
        ReadOnlyMemory<PhysicsRenderTransformOverride> items,
        ulong revision)
    {
        _items = items;
        Revision = revision;
    }

    /// <summary>Gets the view that carries no override.</summary>
    public static PhysicsRenderOverrideView Empty => default;

    /// <summary>Gets the monotonic revision of the override set.</summary>
    public ulong Revision { get; }

    /// <summary>Gets the number of overrides in the view.</summary>
    public int Count => _items.Length;

    /// <summary>Gets a value indicating whether the view carries no override.</summary>
    public bool IsEmpty => _items.IsEmpty;

    /// <summary>Gets the produced overrides.</summary>
    public ReadOnlySpan<PhysicsRenderTransformOverride> Items => _items.Span;
}

/// <summary>
/// Exposes the deformable geometry produced by one render update without copying it.
/// </summary>
/// <remarks>
/// <para>
/// Deformed geometry is never blended between two snapshots. A region is a window into a shared
/// vertex buffer, and two snapshots only describe the same window while the region's topology
/// revision is unchanged; blending across a topology change would mix vertices that do not
/// correspond. A region is therefore always the latest published one, whole, which is also what
/// keeps a partially filled region from ever reaching a backend.
/// </para>
/// <para>
/// The view borrows the producing snapshot's buffers, so a backend must consume it inside the
/// update that produced it. Nothing here allocates.
/// </para>
/// </remarks>
public readonly record struct PhysicsRenderDeformationView
{
    private readonly ReadOnlyMemory<PhysicsRenderDeformableRegion> _regions;
    private readonly ReadOnlyMemory<float> _vertices;

    /// <summary>Initializes a view over produced deformable geometry.</summary>
    /// <param name="regions">The produced regions.</param>
    /// <param name="vertices">The shared vertex components, three per vertex.</param>
    /// <param name="revision">The monotonic revision of the deformation set.</param>
    public PhysicsRenderDeformationView(
        ReadOnlyMemory<PhysicsRenderDeformableRegion> regions,
        ReadOnlyMemory<float> vertices,
        ulong revision)
    {
        _regions = regions;
        _vertices = vertices;
        Revision = revision;
    }

    /// <summary>Gets the view that carries no deformable geometry.</summary>
    public static PhysicsRenderDeformationView Empty => default;

    /// <summary>Gets the monotonic revision of the deformation set.</summary>
    public ulong Revision { get; }

    /// <summary>Gets the number of regions in the view.</summary>
    public int Count => _regions.Length;

    /// <summary>Gets a value indicating whether the view carries no region.</summary>
    public bool IsEmpty => _regions.IsEmpty;

    /// <summary>Gets the produced regions.</summary>
    public ReadOnlySpan<PhysicsRenderDeformableRegion> Regions => _regions.Span;

    /// <summary>Gets the shared vertex components, three per vertex.</summary>
    public ReadOnlySpan<float> Vertices => _vertices.Span;

    /// <summary>Returns the vertex components of one region.</summary>
    /// <param name="region">The region whose vertices are read.</param>
    /// <returns>The region's vertex components, three per vertex.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// The region does not lie inside this view's vertex buffer.
    /// </exception>
    public ReadOnlySpan<float> GetVertices(PhysicsRenderDeformableRegion region)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(region.VertexOffset);
        ArgumentOutOfRangeException.ThrowIfNegative(region.VertexCount);
        int end = checked((region.VertexOffset + region.VertexCount) * 3);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(end, _vertices.Length);
        return _vertices.Span.Slice(region.VertexOffset * 3, region.VertexCount * 3);
    }
}

/// <summary>
/// Turns published physics snapshots into smooth renderer-neutral transform overrides.
/// </summary>
/// <remarks>
/// <para>
/// The interpolator keeps the two latest complete snapshots and blends them so a fixed-step
/// simulation renders smoothly at an unrelated display rate. Orientations are blended along the
/// shortest arc and canonicalized, so a body never spins the long way round between two frames.
/// </para>
/// <para>
/// Interpolation is only ever applied between snapshots that describe the same objects: a changed
/// identity revision, a step index that did not advance (reset, seek, or loop wrap), an incomplete
/// snapshot, or an entity the earlier snapshot did not contain all snap to the latest pose instead
/// of blending against stale values. Deleted entities simply stop producing an override, which
/// restores the authored render state for the prims they were bound to.
/// </para>
/// <para>
/// Every buffer is allocated when the interpolator is created; ingesting and updating never
/// allocate.
/// </para>
/// </remarks>
public sealed class PhysicsRenderInterpolator
{
    private readonly PhysicsRenderSnapshot[] _history;
    private readonly Dictionary<PhysicsRenderObjectId, int> _previousIndex;
    private readonly PhysicsRenderTransformOverride[] _overrides;
    private int _currentIndex;
    private bool _hasCurrent;
    private bool _hasPrevious;
    private bool _previousIndexed;
    private int _overrideCount;
    private ulong _overrideRevision;
    private long _ingestedSnapshots;
    private long _discontinuities;
    private long _snappedEntities;
    private long _interpolatedEntities;
    private long _droppedOverrides;

    /// <summary>Allocates every history and override buffer up front.</summary>
    /// <param name="capacities">The bounded storage the interpolator preallocates.</param>
    public PhysicsRenderInterpolator(PhysicsRenderCapacities capacities)
    {
        Capacities = capacities;
        _history =
        [
            new PhysicsRenderSnapshot(capacities),
            new PhysicsRenderSnapshot(capacities)
        ];
        _overrides = capacities.BodyCapacity == 0
            ? []
            : new PhysicsRenderTransformOverride[capacities.BodyCapacity];
        _previousIndex = new Dictionary<PhysicsRenderObjectId, int>(capacities.BodyCapacity);
    }

    /// <summary>Gets the bounded storage this interpolator preallocated.</summary>
    public PhysicsRenderCapacities Capacities { get; }

    /// <summary>Gets the overrides produced by the last update.</summary>
    public PhysicsRenderOverrideView Overrides =>
        new(_overrides.AsMemory(0, _overrideCount), _overrideRevision);

    /// <summary>Gets a value indicating whether a complete snapshot has been ingested.</summary>
    public bool HasSnapshot => _hasCurrent;

    /// <summary>Gets the simulated seconds of the latest ingested snapshot.</summary>
    public double LatestSimulationSeconds => _hasCurrent ? Current.SimulationSeconds : 0;

    /// <summary>Gets the authored time code of the latest ingested snapshot.</summary>
    public double LatestTimeCode => _hasCurrent ? Current.TimeCode : 0;

    /// <summary>Gets the number of snapshots ingested since the last reset.</summary>
    public long IngestedSnapshots => _ingestedSnapshots;

    /// <summary>Gets the number of updates that snapped because inputs were discontinuous.</summary>
    public long Discontinuities => _discontinuities;

    /// <summary>Gets the number of entity poses snapped rather than interpolated.</summary>
    public long SnappedEntities => _snappedEntities;

    /// <summary>Gets the number of entity poses interpolated.</summary>
    public long InterpolatedEntities => _interpolatedEntities;

    /// <summary>Gets the number of overrides dropped because bounded storage was full.</summary>
    public long DroppedOverrides => _droppedOverrides;

    private PhysicsRenderSnapshot Current => _history[_currentIndex];

    private PhysicsRenderSnapshot Previous => _history[_currentIndex ^ 1];

    /// <summary>Copies the latest published snapshot into the interpolator's history.</summary>
    /// <param name="channel">The channel the producer publishes to.</param>
    /// <returns>
    /// <see langword="true"/> when a newer complete snapshot was ingested; <see langword="false"/>
    /// when nothing new was published, which leaves the existing history untouched.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="channel"/> is null.</exception>
    public bool TryIngest(PhysicsRenderChannel channel)
    {
        ArgumentNullException.ThrowIfNull(channel);
        if (_hasCurrent && channel.Revision == Current.Revision)
        {
            return false;
        }

        int spare = _currentIndex ^ 1;
        if (!channel.TryCopyLatest(_history[spare]))
        {
            return false;
        }

        if (_hasCurrent && _history[spare].Revision <= Current.Revision)
        {
            // The spare buffer no longer holds the earlier snapshot, so interpolating against it
            // would blend stale values; the next update snaps instead.
            _hasPrevious = false;
            _previousIndexed = false;
            return false;
        }

        _hasPrevious = _hasCurrent;
        _currentIndex = spare;
        _hasCurrent = true;
        _previousIndexed = false;
        _ingestedSnapshots++;
        return true;
    }

    /// <summary>Produces transform overrides for one rendered frame.</summary>
    /// <param name="renderSeconds">
    /// The simulated seconds the frame should display, normally the wall-clock time the renderer
    /// has advanced since the simulation started.
    /// </param>
    /// <returns>The outcome of the update.</returns>
    public PhysicsRenderUpdateResult Update(double renderSeconds)
    {
        _overrideRevision++;
        if (!_hasCurrent)
        {
            _overrideCount = 0;
            return new PhysicsRenderUpdateResult(
                PhysicsRenderUpdateStatus.Empty,
                0,
                0,
                0,
                0,
                _overrideRevision);
        }

        PhysicsRenderSnapshot current = Current;
        PhysicsRenderSnapshot previous = Previous;
        bool snapAll = !_hasPrevious || !IsContinuous(previous, current);
        if (snapAll)
        {
            _discontinuities++;
        }
        else
        {
            EnsurePreviousIndexed(previous);
        }

        double alpha = snapAll ? 1 : ComputeAlpha(previous, current, renderSeconds);
        int interpolated = 0;
        int snapped = 0;
        int dropped = 0;
        _overrideCount = 0;

        ReadOnlySpan<PhysicsRenderBodyState> bodies = current.Bodies;
        for (int index = 0; index < bodies.Length; index++)
        {
            PhysicsRenderBodyState body = bodies[index];
            if (_overrideCount >= _overrides.Length)
            {
                dropped++;
                continue;
            }

            if (!snapAll &&
                _previousIndex.TryGetValue(body.Id, out int previousOffset) &&
                TryBlend(previous, previousOffset, body, alpha, out PhysicsRenderTransformOverride blended))
            {
                _overrides[_overrideCount++] = blended;
                interpolated++;
                continue;
            }

            _overrides[_overrideCount++] = new PhysicsRenderTransformOverride(
                body.Id,
                body.Position,
                body.Orientation.Canonical(),
                Snapped: true);
            snapped++;
        }

        _interpolatedEntities += interpolated;
        _snappedEntities += snapped;
        _droppedOverrides += dropped;

        PhysicsRenderUpdateStatus status = snapAll
            ? PhysicsRenderUpdateStatus.Snapped
            : PhysicsRenderUpdateStatus.Interpolated;
        return new PhysicsRenderUpdateResult(
            status,
            alpha,
            interpolated,
            snapped,
            dropped,
            _overrideRevision);
    }

    /// <summary>Returns the renderable state of one domain in the latest snapshot.</summary>
    /// <param name="domain">The reported domain.</param>
    /// <returns>
    /// The latest snapshot's report, or an unavailable report when nothing has been ingested.
    /// </returns>
    public PhysicsRenderDomainReport GetDomain(PhysicsRenderDomain domain) =>
        _hasCurrent
            ? Current.GetDomain(domain)
            : new PhysicsRenderDomainReport(
                domain,
                PhysicsRenderDomainStatus.Unavailable,
                0,
                0,
                0);

    /// <summary>Returns the deformable regions carried by the latest snapshot.</summary>
    /// <returns>The regions, or an empty span when nothing has been ingested.</returns>
    public ReadOnlySpan<PhysicsRenderDeformableRegion> GetDeformables() =>
        _hasCurrent ? Current.Deformables : default;

    /// <summary>Returns the vertex components of one deformable region.</summary>
    /// <param name="region">The region whose vertices are read.</param>
    /// <returns>The region's vertex components, three per vertex.</returns>
    public ReadOnlySpan<float> GetDeformableVertices(PhysicsRenderDeformableRegion region) =>
        _hasCurrent ? Current.GetDeformableVertices(region) : default;

    /// <summary>Gets the deformable geometry carried by the latest snapshot.</summary>
    /// <remarks>
    /// The revision is the same one the transform overrides carry, so a backend applies both halves
    /// of one published frame together and can tell whether it has already drawn it. Geometry is
    /// never blended, so the view always exposes the latest complete snapshot rather than a value
    /// interpolated towards the render clock.
    /// </remarks>
    public PhysicsRenderDeformationView Deformations =>
        _hasCurrent && Current.DeformableCount != 0
            ? new PhysicsRenderDeformationView(
                Current.DeformablesMemory,
                Current.DeformableVerticesMemory,
                _overrideRevision)
            : PhysicsRenderDeformationView.Empty;

    /// <summary>Drops every retained snapshot and override.</summary>
    /// <remarks>
    /// Used on reset, stop, and invalidation. Afterwards the view carries no override, so a
    /// renderer restores the authored transform of every prim a physics object was bound to.
    /// </remarks>
    public void Reset()
    {
        _hasCurrent = false;
        _hasPrevious = false;
        _previousIndexed = false;
        _overrideCount = 0;
        _overrideRevision++;
        _ingestedSnapshots = 0;
        _previousIndex.Clear();
        _history[0].Clear();
        _history[1].Clear();
    }

    private static bool IsContinuous(PhysicsRenderSnapshot previous, PhysicsRenderSnapshot current) =>
        previous.IsComplete &&
        current.IsComplete &&
        previous.IdentityRevision == current.IdentityRevision &&
        current.StepIndex > previous.StepIndex &&
        current.SimulationSeconds > previous.SimulationSeconds;

    private static double ComputeAlpha(
        PhysicsRenderSnapshot previous,
        PhysicsRenderSnapshot current,
        double renderSeconds)
    {
        if (!double.IsFinite(renderSeconds))
        {
            return 1;
        }

        double span = current.SimulationSeconds - previous.SimulationSeconds;
        if (span <= 0 || !double.IsFinite(span))
        {
            return 1;
        }

        return Math.Clamp((renderSeconds - previous.SimulationSeconds) / span, 0, 1);
    }

    private static bool TryBlend(
        PhysicsRenderSnapshot previous,
        int previousOffset,
        in PhysicsRenderBodyState current,
        double alpha,
        out PhysicsRenderTransformOverride blended)
    {
        ReadOnlySpan<PhysicsRenderBodyState> bodies = previous.Bodies;
        if ((uint)previousOffset >= (uint)bodies.Length)
        {
            blended = default;
            return false;
        }

        PhysicsRenderBodyState earlier = bodies[previousOffset];
        if (earlier.Id != current.Id)
        {
            blended = default;
            return false;
        }

        blended = new PhysicsRenderTransformOverride(
            current.Id,
            new UsdVec3d(
                Lerp(earlier.Position.X, current.Position.X, alpha),
                Lerp(earlier.Position.Y, current.Position.Y, alpha),
                Lerp(earlier.Position.Z, current.Position.Z, alpha)),
            PhysicsRenderOrientation.Slerp(earlier.Orientation, current.Orientation, alpha),
            Snapped: false);
        return true;
    }

    private static double Lerp(double from, double to, double alpha) =>
        from + ((to - from) * alpha);

    private void EnsurePreviousIndexed(PhysicsRenderSnapshot previous)
    {
        if (_previousIndexed)
        {
            return;
        }

        _previousIndex.Clear();
        ReadOnlySpan<PhysicsRenderBodyState> bodies = previous.Bodies;
        for (int index = 0; index < bodies.Length; index++)
        {
            _previousIndex[bodies[index].Id] = index;
        }

        _previousIndexed = true;
    }
}
