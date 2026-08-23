// Copyright (c) marcschier. Licensed under the MIT License.

using OpenUsd.Rendering;
using OpenUsd.Rendering.Silk;

namespace OpenUsd.Viewer;

/// <summary>What a backend actually did with one override batch, after it consumed it.</summary>
/// <param name="Revision">The override revision the report describes.</param>
/// <param name="Applied">The overrides the backend actually resolved and drew.</param>
/// <param name="Unresolved">The overrides the backend could not resolve to anything drawable.</param>
/// <remarks>
/// A backend that stages a batch for its own thread cannot know at staging time how much of it
/// will resolve. Reporting only what the backend staged would make every dropped or unbound
/// override look applied, so the capability matrix would claim a frozen scene is being drawn.
/// </remarks>
internal readonly record struct ViewerPhysicsOverrideReport(
    ulong Revision,
    int Applied,
    int Unresolved);

/// <summary>
/// Applies one bounded batch of physics transform overrides to the active render backend.
/// </summary>
/// <remarks>
/// The target is the only seam between simulated poses and a backend. It receives renderer-neutral
/// overrides plus the binding table that names the authored prim each stable identity drives, so a
/// backend never sees a USD prim, a stage, or a solver handle, and the bridge never needs to know
/// whether it is talking to Storm, Silk, or a test double.
/// </remarks>
internal interface IViewerPhysicsOverrideTarget
{
    /// <summary>Gets a value indicating whether the backend can draw physics overrides.</summary>
    bool SupportsPhysicsTransformOverrides { get; }

    /// <summary>Applies one complete override batch, replacing whatever was applied before.</summary>
    /// <param name="overrides">The overrides one render update produced.</param>
    /// <param name="bindings">The table naming the prim each identity drives.</param>
    /// <returns>The number of overrides the backend accepted for delivery.</returns>
    int ApplyPhysicsOverrides(
        in PhysicsRenderOverrideView overrides,
        PhysicsRenderBindingTable bindings);

    /// <summary>Applies one complete deformable geometry batch, replacing whatever came before.</summary>
    /// <param name="deformations">The deformable geometry one render update produced.</param>
    /// <param name="bindings">The table naming the prim each identity drives.</param>
    /// <returns>The number of regions the backend accepted for delivery.</returns>
    /// <remarks>
    /// Deformable geometry is a separate batch because it is per vertex rather than per prim, and
    /// because a backend can legitimately draw rigid poses while drawing no deformable geometry at
    /// all. A backend that cannot upload geometry returns zero, which is reported as an unsupported
    /// domain rather than as a failure, so unsupported deformables never stop rigid rendering.
    ///
    /// Returning zero is deliberately a decision each implementation states rather than a default
    /// this interface supplies. A default body made an adapter that simply forgot to forward the
    /// call indistinguishable from a backend that cannot upload geometry, and that is exactly what
    /// happened: the viewer's own backend adapter inherited the default and no deformable region
    /// ever reached Storm or Silk, with nothing failing anywhere to say so.
    /// </remarks>
    int ApplyPhysicsDeformations(
        in PhysicsRenderDeformationView deformations,
        PhysicsRenderBindingTable bindings);

    /// <summary>Takes the newest report of what the backend resolved, if it published one.</summary>
    /// <param name="report">Receives the newest unread report.</param>
    /// <returns><see langword="true"/> when a report was taken.</returns>
    /// <remarks>
    /// Reports are published by whichever thread owns the backend's resources, once it has
    /// consumed a batch, and are read by the render loop. Only the newest is retained: an older
    /// report describes a batch that has already been replaced on screen.
    /// </remarks>
    bool TryTakeOverrideReport(out ViewerPhysicsOverrideReport report);

    /// <summary>Restores the authored transforms by dropping every retained override.</summary>
    void ClearPhysicsOverrides();
}

/// <summary>The bounded storage every physics render buffer in the viewer is sized for.</summary>
internal static class ViewerPhysicsRenderCapacities
{
    /// <summary>The maximum number of simulated bodies one batch carries.</summary>
    internal const int BodyCapacity = 4096;

    /// <summary>The maximum number of deformable regions one batch carries.</summary>
    internal const int DeformableCapacity = 256;

    /// <summary>The maximum number of deformable vertex triples one batch carries.</summary>
    /// <remarks>
    /// A particle system is bounded far higher than this in the solver, so the viewer's own budget
    /// is what decides how much deformable geometry one frame carries. Regions that do not fit are
    /// dropped whole and counted, which is visible in the domain report rather than silent.
    /// </remarks>
    internal const int DeformableVertexCapacity = 1 << 18;

    /// <summary>The maximum packed prim path payload one Storm batch carries, in bytes.</summary>
    internal const int StormPathBytes = 256 * 1024;

    /// <summary>Gets the renderer-neutral capacities the bridge preallocates.</summary>
    internal static PhysicsRenderCapacities Default =>
        new(BodyCapacity, DeformableCapacity, DeformableVertexCapacity);

    /// <summary>
    /// Sizes the render staging from what one built world can actually publish.
    /// </summary>
    /// <param name="deformationCapacity">The world's deformation body capacity.</param>
    /// <param name="deformationVertexCapacity">The world's deformation vertex capacity.</param>
    /// <returns>Capacities large enough for the world, bounded by the viewer's own budget.</returns>
    /// <remarks>
    /// A world that publishes no deformation still stages none, so a CPU only stage costs nothing.
    /// A world that publishes more than the viewer budgets for is clamped rather than trusted,
    /// because the staging is preallocated once and a stage decides how large its own content is.
    /// </remarks>
    internal static PhysicsRenderCapacities ForWorld(
        int deformationCapacity,
        int deformationVertexCapacity) =>
        new(
            BodyCapacity,
            Math.Clamp(deformationCapacity, 0, DeformableCapacity),
            Math.Clamp(deformationVertexCapacity, 0, DeformableVertexCapacity));
}

/// <summary>
/// Applies one staged physics override batch to a retained hdSilk mesh renderer.
/// </summary>
internal static class ViewerSilkPhysicsOverrideApplier
{
    /// <summary>Resolves and applies the newest staged batch on the presenting thread.</summary>
    /// <param name="stage">The stage the render loop wrote the batch into.</param>
    /// <param name="overrides">The reusable resolved hdSilk override table.</param>
    /// <param name="renderer">The mesh renderer that owns the retained scene.</param>
    internal static void Apply(
        ViewerPhysicsOverrideStage stage,
        SilkPhysicsTransformOverrides overrides,
        SilkMeshRenderer renderer)
    {
        ArgumentNullException.ThrowIfNull(stage);
        ArgumentNullException.ThrowIfNull(overrides);
        ArgumentNullException.ThrowIfNull(renderer);
        if (!stage.TryTake(out ViewerPhysicsOverrideBatch batch))
        {
            return;
        }

        using (batch)
        {
            int resolved = overrides.Refresh(renderer.Scene, batch.Bindings, batch.Overrides);
            stage.PublishReport(
                batch.Overrides.Revision,
                resolved,
                Math.Max(0, batch.Overrides.Count - resolved));
        }

        renderer.PhysicsOverrides = overrides;
    }

    /// <summary>
    /// Stages the newest deformation batch so the renderer applies it for the next frame.
    /// </summary>
    /// <param name="stage">The stage the render loop wrote the batch into.</param>
    /// <param name="deformations">The reusable resolved hdSilk deformation table.</param>
    /// <param name="renderer">The mesh renderer that owns the retained scene.</param>
    /// <returns>The number of regions staged, or the driven mesh count when nothing was staged.</returns>
    /// <remarks>
    /// <para>
    /// This runs on the presenting thread, immediately before the renderer applies the authored
    /// page and draws. It deliberately does not touch the retained scene: the renderer applies the
    /// staged batch after that page and uploads what changed, which is the only ordering in which
    /// one apply both wins over authored geometry and produces the geometry delta that reaches the
    /// vertex buffers.
    /// </para>
    /// <para>
    /// Applying here as well produced a frame that drew the authored rest pose: the scene already
    /// held the simulated points by the time the renderer applied, so its apply reported every mesh
    /// unchanged, emitted an empty delta, and uploaded nothing.
    /// </para>
    /// </remarks>
    internal static int ApplyDeformations(
        ViewerPhysicsOverrideStage stage,
        SilkPhysicsDeformations deformations,
        SilkMeshRenderer renderer)
    {
        ArgumentNullException.ThrowIfNull(stage);
        ArgumentNullException.ThrowIfNull(deformations);
        ArgumentNullException.ThrowIfNull(renderer);

        // The renderer owns the ordering: it applies the retained batch after
        // the authored page and before the draw, and uploads the meshes that
        // changed. Handing it the batch here is all this step does, so a batch
        // staged before a page can no longer be overwritten by that page.
        renderer.PhysicsDeformations = deformations;
        if (!stage.TryTakeDeformations(out PhysicsRenderDeformationView batch))
        {
            return deformations.Count;
        }

        return deformations.Stage(stage.Bindings, batch);
    }
}

/// <summary>A target for a backend that cannot draw physics overrides.</summary>
internal sealed class ViewerPhysicsUnsupportedOverrideTarget : IViewerPhysicsOverrideTarget
{
    /// <summary>Gets the shared unsupported target.</summary>
    internal static ViewerPhysicsUnsupportedOverrideTarget Instance { get; } = new();

    /// <inheritdoc/>
    public bool SupportsPhysicsTransformOverrides => false;

    /// <inheritdoc/>
    public int ApplyPhysicsOverrides(
        in PhysicsRenderOverrideView overrides,
        PhysicsRenderBindingTable bindings) => 0;

    /// <inheritdoc/>
    public int ApplyPhysicsDeformations(
        in PhysicsRenderDeformationView deformations,
        PhysicsRenderBindingTable bindings) => 0;

    /// <inheritdoc/>
    public bool TryTakeOverrideReport(out ViewerPhysicsOverrideReport report)
    {
        report = default;
        return false;
    }

    /// <inheritdoc/>
    public void ClearPhysicsOverrides()
    {
    }
}

/// <summary>Reports what one render-frame physics pump did.</summary>
/// <param name="Ingested">Whether a newly published snapshot was consumed.</param>
/// <param name="Applied">The number of overrides the backend accepted.</param>
/// <param name="Revision">The override revision that was applied.</param>
internal readonly record struct ViewerPhysicsFramePumpResult(
    bool Ingested,
    int Applied,
    ulong Revision);

/// <summary>
/// Bridges published simulation snapshots into the active render backend, once per rendered frame.
/// </summary>
/// <remarks>
/// <para>
/// The bridge does exactly one bounded thing per rendered frame: take the latest complete snapshot
/// if one is available, interpolate it to the frame's render time, and hand the resulting override
/// batch to the backend. It never waits for the physics worker, never applies a partially written
/// snapshot, and never grows a buffer, so a slow simulation degrades to a repeated pose instead of
/// stalling or dropping the render loop.
/// </para>
/// <para>
/// Every buffer is allocated when the bridge is created, so the warm per-frame path allocates
/// nothing at all.
/// </para>
/// </remarks>
internal sealed class ViewerPhysicsRenderBridge
{
    private readonly PhysicsRenderChannel _channel;
    private readonly PhysicsRenderInterpolator _interpolator;
    private readonly int _bindingCapacity;
    private PhysicsRenderBindingTable _bindings;
    private ulong _appliedRevision;
    private int _appliedOverrides;
    private int _appliedDeformations;
    private int _unresolvedOverrides;
    private int _targetSupported;
    private bool _hasApplied;
    private bool _hasClockAnchor;
    private double _anchorRenderSeconds;
    private double _anchorSimulationSeconds;

    /// <summary>Initializes a bridge with bounded, preallocated storage.</summary>
    /// <param name="capacities">The bounded storage every buffer is sized for.</param>
    internal ViewerPhysicsRenderBridge(PhysicsRenderCapacities capacities)
    {
        _channel = new PhysicsRenderChannel(capacities);
        _interpolator = new PhysicsRenderInterpolator(capacities);
        _bindingCapacity = Math.Max(capacities.BodyCapacity, 1);
        _bindings = new PhysicsRenderBindingTable(_bindingCapacity);
    }

    /// <summary>Gets the channel published simulation snapshots are written into.</summary>
    internal PhysicsRenderChannel Channel => _channel;

    /// <summary>Gets the table naming the prim each stable identity drives.</summary>
    internal PhysicsRenderBindingTable Bindings => Volatile.Read(ref _bindings);

    /// <summary>Gets the number of bindings the table can hold.</summary>
    internal int BindingCapacity => _bindingCapacity;

    /// <summary>Gets the number of overrides the backend could not resolve to a bound prim.</summary>
    internal int UnresolvedOverrides => Volatile.Read(ref _unresolvedOverrides);

    /// <summary>Gets the number of overrides the backend reported it actually drew.</summary>
    internal int AppliedOverrides => Volatile.Read(ref _appliedOverrides);

    /// <summary>Gets the number of deformable regions the backend accepted from the last pump.</summary>
    internal int AppliedDeformations => Volatile.Read(ref _appliedDeformations);

    /// <summary>
    /// Gets a value indicating whether a backend has confirmed it drew a whole override batch.
    /// </summary>
    /// <remarks>
    /// A batch handed to a backend is not a batch that reached the screen. An in-process backend
    /// stages the batch for its own thread and only resolves it inside its next frame, so the count
    /// it returns immediately is what it accepted for delivery, not what it drew. Only a report
    /// published after that thread consumed the batch proves the simulation is visible.
    /// </remarks>
    internal bool HasAppliedBatch => Volatile.Read(ref _appliedOverrides) > 0;

    /// <summary>
    /// Gets a value indicating whether the backend seen by the most recent pump can draw overrides.
    /// </summary>
    internal bool TargetSupportsOverrides => Volatile.Read(ref _targetSupported) != 0;

    /// <summary>
    /// Publishes a new binding table for every subsequent override batch.
    /// </summary>
    /// <remarks>
    /// A table is never mutated once it has been handed to a backend. Publishing a whole new table
    /// instead is what lets a rebuild rebind every identity while the render loop is mid-frame: the
    /// frame in flight keeps resolving against the table it started with, and the next frame picks
    /// up the new one, so no backend ever reads a half-rebound table.
    /// </remarks>
    /// <param name="bindings">The table produced by the most recent build.</param>
    /// <exception cref="ArgumentNullException"><paramref name="bindings"/> is null.</exception>
    internal void SetBindings(PhysicsRenderBindingTable bindings)
    {
        ArgumentNullException.ThrowIfNull(bindings);
        Volatile.Write(ref _bindings, bindings);
        Volatile.Write(ref _unresolvedOverrides, 0);
        Volatile.Write(ref _appliedOverrides, 0);
    }

    /// <summary>Gets the overrides the most recent update produced.</summary>
    internal PhysicsRenderOverrideView Overrides => _interpolator.Overrides;

    /// <summary>Gets a value indicating whether a complete snapshot has been consumed.</summary>
    internal bool HasSnapshot => _interpolator.HasSnapshot;

    /// <summary>Gets the number of snapshots consumed so far.</summary>
    internal long IngestedSnapshots => _interpolator.IngestedSnapshots;

    /// <summary>Gets the authored time code of the latest consumed snapshot.</summary>
    internal double LatestTimeCode => _interpolator.LatestTimeCode;

    /// <summary>Reports one domain's renderable state for the capability matrix.</summary>
    /// <param name="domain">The domain to report.</param>
    /// <returns>The domain report.</returns>
    internal PhysicsRenderDomainReport GetDomain(PhysicsRenderDomain domain) =>
        _interpolator.GetDomain(domain);

    /// <summary>Consumes the latest complete snapshot and applies one bounded override batch.</summary>
    /// <param name="renderSeconds">The render clock the overrides are interpolated to.</param>
    /// <param name="target">The active backend the batch is applied to.</param>
    /// <returns>What the pump did.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="target"/> is null.</exception>
    internal ViewerPhysicsFramePumpResult Pump(
        double renderSeconds,
        IViewerPhysicsOverrideTarget target)
    {
        ArgumentNullException.ThrowIfNull(target);

        // The simulated time of the snapshot that is on screen right now, read before the ingest
        // that may replace it. That is the point the render clock has to be anchored to: the
        // interpolator blends from the previous snapshot toward the newest one, so anchoring on
        // the newest would leave every frame at the far end of the interval, which is a snap.
        double displayedSimulationSeconds = _interpolator.LatestSimulationSeconds;
        bool ingested = _interpolator.TryIngest(_channel);
        DrainReports(target);
        if (!_interpolator.HasSnapshot)
        {
            // Backend support is only recorded once a batch is actually offered to a backend.
            // Recording it earlier would let the capability matrix claim the scene is drawn before
            // anything has ever been handed to a renderer.
            return new ViewerPhysicsFramePumpResult(ingested, 0, _interpolator.Overrides.Revision);
        }

        PhysicsRenderUpdateResult update = _interpolator.Update(
            RebaseClock(renderSeconds, ingested, displayedSimulationSeconds));
        _ = update;
        PhysicsRenderOverrideView view = _interpolator.Overrides;
        Volatile.Write(ref _targetSupported, target.SupportsPhysicsTransformOverrides ? 1 : 0);
        if (!target.SupportsPhysicsTransformOverrides)
        {
            Volatile.Write(ref _appliedOverrides, 0);
            return new ViewerPhysicsFramePumpResult(ingested, 0, view.Revision);
        }

        PhysicsRenderBindingTable bindings = Volatile.Read(ref _bindings);
        int accepted = target.ApplyPhysicsOverrides(in view, bindings);
        // Deformable geometry is applied as its own batch so a backend that draws
        // rigid poses but uploads no geometry still draws everything it can. The
        // empty case is applied too: a backend replaces every retained region
        // with the batch it is handed, so an empty batch is what restores the
        // authored points when a body stops publishing geometry.
        PhysicsRenderDeformationView deformations = _interpolator.Deformations;
        _appliedDeformations = target.ApplyPhysicsDeformations(in deformations, bindings);
        _appliedRevision = view.Revision;
        _hasApplied = true;
        DrainReports(target);
        return new ViewerPhysicsFramePumpResult(ingested, accepted, view.Revision);
    }

    /// <summary>
    /// Restates a monotonic render clock on the simulated timeline the interpolator blends on.
    /// </summary>
    /// <param name="renderSeconds">The caller's monotonic clock, in seconds.</param>
    /// <param name="ingested">Whether this pump consumed a newly published snapshot.</param>
    /// <param name="displayedSimulationSeconds">
    /// The simulated time of the snapshot that was on screen before this pump ingested.
    /// </param>
    /// <returns>The simulated seconds the rendered frame should display.</returns>
    /// <remarks>
    /// The interpolator blends between two snapshots by comparing the frame time against their
    /// <c>SimulationSeconds</c>, which start at zero and restart whenever the world is rebuilt or
    /// reset. A caller's clock has neither property: the viewer supplies a process-wide performance
    /// counter, which is enormous next to a simulated timeline, so every alpha clamped to one and
    /// no frame was ever blended. Re-anchoring on each ingested snapshot turns the caller's clock
    /// into an offset from the pose that is already on screen, which stays correct across a reset
    /// without the caller having to know a reset happened.
    /// </remarks>
    private double RebaseClock(
        double renderSeconds,
        bool ingested,
        double displayedSimulationSeconds)
    {
        if (!double.IsFinite(renderSeconds))
        {
            return _interpolator.LatestSimulationSeconds;
        }

        if (ingested || !_hasClockAnchor)
        {
            _anchorRenderSeconds = renderSeconds;
            _anchorSimulationSeconds = displayedSimulationSeconds;
            _hasClockAnchor = true;
        }

        double elapsed = renderSeconds - _anchorRenderSeconds;
        return elapsed <= 0 ? _anchorSimulationSeconds : _anchorSimulationSeconds + elapsed;
    }

    /// <summary>
    /// Re-applies the latest override batch after a context loss or a backend switch.
    /// </summary>
    /// <param name="target">The backend that lost its retained overrides.</param>
    /// <returns>The number of overrides the backend accepted.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="target"/> is null.</exception>
    internal int ReplayLatest(IViewerPhysicsOverrideTarget target)
    {
        ArgumentNullException.ThrowIfNull(target);
        if (!_hasApplied || !target.SupportsPhysicsTransformOverrides)
        {
            return 0;
        }

        PhysicsRenderOverrideView view = _interpolator.Overrides;
        PhysicsRenderBindingTable bindings = Volatile.Read(ref _bindings);
        int accepted = target.ApplyPhysicsOverrides(in view, bindings);
        _appliedRevision = view.Revision;

        // The new backend has not resolved anything yet, so the previous backend's counts no
        // longer describe what is on screen.
        Volatile.Write(ref _appliedOverrides, 0);
        Volatile.Write(ref _unresolvedOverrides, 0);
        DrainReports(target);
        return accepted;
    }

    private void DrainReports(IViewerPhysicsOverrideTarget target)
    {
        var drained = false;
        ViewerPhysicsOverrideReport newest = default;
        while (target.TryTakeOverrideReport(out ViewerPhysicsOverrideReport report))
        {
            newest = report;
            drained = true;
        }

        if (!drained)
        {
            return;
        }

        Volatile.Write(ref _appliedOverrides, Math.Max(0, newest.Applied));
        Volatile.Write(ref _unresolvedOverrides, Math.Max(0, newest.Unresolved));
        if (newest.Applied > 0)
        {
            _appliedRevision = newest.Revision;
            _hasApplied = true;
        }
    }

    /// <summary>Gets the override revision most recently applied to a backend.</summary>
    internal ulong AppliedRevision => _appliedRevision;

    /// <summary>Drops every override and restores the authored render state.</summary>
    /// <param name="target">The backend whose overrides are cleared.</param>
    /// <exception cref="ArgumentNullException"><paramref name="target"/> is null.</exception>
    internal void Clear(IViewerPhysicsOverrideTarget target)
    {
        ArgumentNullException.ThrowIfNull(target);
        _channel.Invalidate();
        _interpolator.Reset();
        _hasApplied = false;
        _appliedRevision = 0;
        Volatile.Write(ref _unresolvedOverrides, 0);
        Volatile.Write(ref _appliedOverrides, 0);
        target.ClearPhysicsOverrides();

        // A report published for a batch that has just been cleared describes overrides that are
        // no longer on screen, so it must not resurrect the applied counts.
        while (target.TryTakeOverrideReport(out _))
        {
        }
    }
}
