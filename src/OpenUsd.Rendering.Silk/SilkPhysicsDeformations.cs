// Copyright (c) marcschier. Licensed under the MIT License.

namespace OpenUsd.Rendering.Silk;

/// <summary>
/// Reports what replacing one retained mesh's points with simulated ones did.
/// </summary>
/// <remarks>
/// A single boolean cannot carry this. "The prim is not in the rendered scene", "the solver and the
/// mesh disagree on vertex count", "a simulated point component is not finite" and "the body has
/// settled and republished the points it already had" are four different answers, and the last one
/// is a success. Conflating them makes a settled body look like a topology failure in the
/// diagnostics and makes it vanish from the driven set on the frame it stops moving.
/// </remarks>
public enum SilkDeformationResult
{
    /// <summary>The retained points were replaced with different simulated points.</summary>
    Applied,

    /// <summary>
    /// The simulated points are identical to the retained ones, so the mesh is driven and already
    /// correct and needs no upload.
    /// </summary>
    Unchanged,

    /// <summary>The bound prim path names no retained mesh.</summary>
    MeshMissing,

    /// <summary>The region's vertex count disagrees with the retained mesh.</summary>
    VertexCountMismatch,

    /// <summary>A simulated point component is not finite.</summary>
    NonFiniteValue
}

/// <summary>
/// Resolves renderer-neutral deformable geometry onto retained hdSilk meshes.
/// </summary>
/// <remarks>
/// <para>
/// This is the geometry half of the physics render path. It consumes only the immutable deformation
/// set a <see cref="PhysicsRenderInterpolator"/> produced, keyed by stable simulation identity, and
/// resolves it through a <see cref="PhysicsRenderBindingTable"/> onto the meshes hdSilk already
/// retains. No stage, prim, or solver handle is read, and nothing is authored back into USD.
/// </para>
/// <para>
/// The batch is retained rather than consumed, for two reasons. An authored scene page republishes
/// authored geometry on every frame, so a batch applied once would survive exactly until the next
/// page; and replacing points in the CPU scene is only half the work, because retained GPU geometry
/// is rebuilt from a delta. <see cref="Delta"/> is the invalidation that reaches the vertex buffers,
/// and it is produced by the apply, so the apply and the upload have to stay together on the far
/// side of the authored page. <see cref="Stage"/> is what lets a caller hand over a batch without
/// splitting them.
/// </para>
/// <para>
/// A region whose vertex count does not match the retained mesh is refused and counted rather than
/// resized, because a solver window and a render mesh that disagree on vertex count are not the
/// same geometry and drawing one against the other's indices would invent a shape.
/// </para>
/// <para>
/// Replacement is destructive, so dropping a batch is not the end of the work: a mesh that stops
/// being driven is restored from the authored geometry the scene retained for it, and the restored
/// meshes appear in the same <see cref="Delta"/> as deformed ones. That is what makes stopping a
/// simulation put the rest pose back on a stage that authors nothing further, which for a static
/// stage is the difference between a restored scene and the last simulated frame forever.
/// </para>
/// </remarks>
public sealed class SilkPhysicsDeformations
{
    private HashSet<ulong> _applied = [];
    private HashSet<ulong> _previousApplied = [];
    private readonly List<ulong> _changed = [];
    private readonly List<Region> _retained = [];
    private float[] _vertices = [];
    private int _vertexComponentCount;
    private PhysicsRenderBindingTable? _bindings;
    private long _unresolvedRegions;
    private long _missingMeshRegions;
    private long _mismatchedRegions;
    private long _nonFiniteRegions;
    private long _unchangedRegions;
    private long _restoredMeshes;

    /// <summary>One retained region: where its points live and which prim it drives.</summary>
    private readonly record struct Region(
        PhysicsRenderObjectId Id,
        int ComponentOffset,
        int ComponentCount);

    /// <summary>Gets the number of meshes the batch drives, settled ones included.</summary>
    public int Count => _applied.Count;

    /// <summary>Gets the monotonic revision advanced only when a driven mesh actually changed.</summary>
    /// <remarks>
    /// A settled body republishes the points it already had on every frame. Advancing the revision
    /// for that would make an unchanging scene look like it is churning, so the revision tracks real
    /// change while <see cref="Count"/> keeps reporting the settled body as driven.
    /// </remarks>
    public ulong Revision { get; private set; }

    /// <summary>Gets the number of regions that resolved to no bound prim.</summary>
    public long UnresolvedRegions => _unresolvedRegions;

    /// <summary>Gets the number of regions whose bound prim is not in the rendered scene.</summary>
    public long MissingMeshRegions => _missingMeshRegions;

    /// <summary>Gets the number of regions whose vertex count did not match its mesh.</summary>
    public long MismatchedRegions => _mismatchedRegions;

    /// <summary>Gets the number of regions carrying a point component that is not finite.</summary>
    public long NonFiniteRegions => _nonFiniteRegions;

    /// <summary>Gets the number of regions that already carried the points they published.</summary>
    public long UnchangedRegions => _unchangedRegions;

    /// <summary>Gets the number of meshes that stopped being driven and were put back.</summary>
    /// <remarks>
    /// A mesh counts here when the batch stopped naming it - a stopped simulation, a body that
    /// stopped publishing geometry, or a shrinking batch - whether or not authored geometry was
    /// still retained for it. A mesh that a page re-authored or that was removed from the scene in
    /// the same frame therefore counts as dropped without producing an upload.
    /// </remarks>
    public long RestoredMeshes => _restoredMeshes;

    /// <summary>Gets the meshes whose geometry the last apply changed, as a scene delta.</summary>
    /// <remarks>
    /// Retained GPU geometry is rebuilt from a delta, so replacing points in the CPU scene alone
    /// leaves the vertex buffers holding authored geometry. This invalidates exactly the affected
    /// meshes and nothing else; a settled body contributes nothing to it and costs no upload.
    /// </remarks>
    public SilkSceneDelta Delta => new(_changed.ToArray(), ReadOnlyMemory<ulong>.Empty);

    /// <summary>Gets a value indicating whether any mesh needs its geometry re-uploaded.</summary>
    public bool HasPendingGeometry => _changed.Count != 0;

    /// <summary>Gets a value indicating whether a batch is retained for re-application.</summary>
    public bool HasBatch => _bindings is not null;

    /// <summary>Reports whether this backend can draw one deformable domain.</summary>
    /// <param name="domain">The domain the caller wants to draw.</param>
    /// <returns><see langword="true"/> when hdSilk can upload the domain's geometry.</returns>
    /// <remarks>
    /// A cloth and a volume deformable publish one simulated position per rendered vertex, so the
    /// retained mesh can be driven directly. A particle system publishes positions that belong to
    /// no rendered mesh vertex, so it is reported as unsupported here rather than drawn against
    /// whatever mesh happens to be bound to it.
    /// </remarks>
    public static bool IsDomainSupported(PhysicsRenderDomain domain) => domain is
        PhysicsRenderDomain.Cloth or
        PhysicsRenderDomain.Deformable;

    /// <summary>Replaces the points of every retained mesh one deformation set drives.</summary>
    /// <param name="scene">The retained hdSilk scene the regions are resolved against.</param>
    /// <param name="bindings">The table naming the renderable entity each identity drives.</param>
    /// <param name="deformations">The deformable geometry one render update produced.</param>
    /// <returns>The number of meshes the batch drives, settled ones included.</returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="scene"/> or <paramref name="bindings"/> is null.
    /// </exception>
    /// <remarks>
    /// This applies immediately, which is only correct when the caller owns the scene ordering for
    /// the frame. A renderer that applies an authored page after this call must use
    /// <see cref="Stage"/> instead, so the single apply happens after that page and its result is
    /// uploaded; see the remarks on <see cref="Stage"/>.
    /// </remarks>
    public int Refresh(
        SilkSceneState scene,
        PhysicsRenderBindingTable bindings,
        PhysicsRenderDeformationView deformations)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(bindings);
        Retain(bindings, deformations);
        return Apply(scene);
    }

    /// <summary>Retains one deformation set without touching the retained scene.</summary>
    /// <param name="bindings">The table naming the renderable entity each identity drives.</param>
    /// <param name="deformations">The deformable geometry one render update produced.</param>
    /// <returns>The number of regions retained for the next apply.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="bindings"/> is null.</exception>
    /// <remarks>
    /// <para>
    /// Staging exists because applying and uploading are two steps that must not be separated by an
    /// authored page. Applying here and letting the renderer re-apply after its page left the
    /// second apply reporting <see cref="SilkDeformationResult.Unchanged"/> for every mesh - the
    /// points were already simulated - so the geometry delta was empty and the vertex buffers kept
    /// the authored rest pose while the CPU scene reported the simulated one. Nothing failed and
    /// nothing was diagnosed; the frame simply drew the wrong geometry.
    /// </para>
    /// <para>
    /// The renderer performs the one apply, after the authored page and before the draw, and
    /// uploads exactly the meshes that changed. A settled body still applies as
    /// <see cref="SilkDeformationResult.Unchanged"/> there and still costs no upload.
    /// </para>
    /// </remarks>
    public int Stage(
        PhysicsRenderBindingTable bindings,
        PhysicsRenderDeformationView deformations)
    {
        ArgumentNullException.ThrowIfNull(bindings);
        Retain(bindings, deformations);
        return _retained.Count;
    }

    /// <summary>Applies the retained batch over whatever the scene currently holds.</summary>
    /// <param name="scene">The retained hdSilk scene the regions are resolved against.</param>
    /// <returns>The number of meshes the retained batch drives.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="scene"/> is null.</exception>
    /// <remarks>
    /// This is both the first apply of a batch that was only staged and the re-apply that puts the
    /// simulated points back after an authored page republished the authored ones. The renderer
    /// calls it once per frame, after the page and before the draw.
    /// </remarks>
    public int Reapply(SilkSceneState scene)
    {
        ArgumentNullException.ThrowIfNull(scene);
        return _bindings is null ? 0 : Apply(scene);
    }

    /// <summary>Reports whether one retained mesh is currently driven by a deformation.</summary>
    /// <param name="meshId">The retained hdSilk mesh key.</param>
    /// <returns><see langword="true"/> when the mesh's points are simulated.</returns>
    public bool Contains(ulong meshId) => _applied.Contains(meshId);

    /// <summary>
    /// Restores the authored geometry of every driven mesh and drops the retained batch.
    /// </summary>
    /// <param name="scene">The retained hdSilk scene the meshes were driven in.</param>
    /// <returns>The number of meshes whose authored geometry was put back.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="scene"/> is null.</exception>
    /// <remarks>
    /// The restored meshes are reported through <see cref="Delta"/> exactly as a deformation is, so
    /// a caller that uploads the delta puts the authored geometry back on the GPU as well as in the
    /// scene. Stopping a simulation through an empty batch reaches the same code and needs no
    /// separate call; this exists for a caller that is tearing the batch down outside a frame.
    /// </remarks>
    public int Restore(SilkSceneState scene)
    {
        ArgumentNullException.ThrowIfNull(scene);
        _retained.Clear();
        _vertexComponentCount = 0;
        _bindings = null;
        _changed.Clear();

        // The driven set is swapped aside first, because restoration skips whatever is still
        // driven and this call drives nothing any more.
        (_previousApplied, _applied) = (_applied, _previousApplied);
        _applied.Clear();
        int restored = RestoreDropped(scene, _previousApplied);
        _previousApplied.Clear();
        if (restored != 0)
        {
            Revision++;
        }

        return restored;
    }

    /// <summary>Drops the retained batch without touching the retained scene.</summary>
    /// <remarks>
    /// This leaves every driven mesh carrying its simulated points, because the scene is not
    /// reachable from here. A mesh that is dropped this way is restored only when a page re-authors
    /// it, which a static stage may never do. Callers stopping a simulation want
    /// <see cref="Restore"/>, or the empty batch the render path already uses; this exists for a
    /// caller that is discarding the whole scene along with the batch.
    /// </remarks>
    public void Clear()
    {
        _retained.Clear();
        _vertexComponentCount = 0;
        _bindings = null;
        _changed.Clear();
        if (_applied.Count == 0)
        {
            return;
        }

        _applied.Clear();
        _previousApplied.Clear();
        Revision++;
    }

    /// <summary>Resets the retained batch and every counter.</summary>
    public void Reset()
    {
        Clear();
        _unresolvedRegions = 0;
        _missingMeshRegions = 0;
        _mismatchedRegions = 0;
        _nonFiniteRegions = 0;
        _unchangedRegions = 0;
        _restoredMeshes = 0;
    }

    private void Retain(
        PhysicsRenderBindingTable bindings,
        in PhysicsRenderDeformationView deformations)
    {
        _bindings = bindings;
        _retained.Clear();
        _vertexComponentCount = 0;
        ReadOnlySpan<PhysicsRenderDeformableRegion> regions = deformations.Regions;
        for (int index = 0; index < regions.Length; index++)
        {
            PhysicsRenderDeformableRegion region = regions[index];
            if (!IsDomainSupported(region.Domain))
            {
                continue;
            }

            ReadOnlySpan<float> vertices = deformations.GetVertices(region);
            int required = checked(_vertexComponentCount + vertices.Length);
            if (_vertices.Length < required)
            {
                Array.Resize(ref _vertices, required);
            }

            vertices.CopyTo(_vertices.AsSpan(_vertexComponentCount));
            _retained.Add(new Region(region.Id, _vertexComponentCount, vertices.Length));
            _vertexComponentCount = required;
        }
    }

    private int Apply(SilkSceneState scene)
    {
        PhysicsRenderBindingTable bindings = _bindings!;

        // The previous driven set is swapped aside rather than discarded, so a
        // mesh that is driven again this frame is recognised as already driven.
        // Clearing first and then treating every insertion as new would report
        // membership churn on every single frame.
        (_previousApplied, _applied) = (_applied, _previousApplied);
        _applied.Clear();
        _changed.Clear();
        bool membershipChanged = false;
        for (int index = 0; index < _retained.Count; index++)
        {
            Region region = _retained[index];
            if (!bindings.TryResolve(region.Id, out PhysicsRenderBinding binding))
            {
                _unresolvedRegions++;
                continue;
            }

            SilkDeformationResult result = scene.ReplacePoints(
                binding.PrimPath,
                binding.InstanceIndex,
                _vertices.AsSpan(region.ComponentOffset, region.ComponentCount),
                out ulong meshId);
            switch (result)
            {
                case SilkDeformationResult.Applied:
                    _changed.Add(meshId);
                    _ = _applied.Add(meshId);
                    membershipChanged |= !_previousApplied.Contains(meshId);
                    break;
                case SilkDeformationResult.Unchanged:
                    // A settled body is driven and already correct. It stays in
                    // the driven set and it is never counted as a failure.
                    _unchangedRegions++;
                    _ = _applied.Add(meshId);
                    membershipChanged |= !_previousApplied.Contains(meshId);
                    break;
                case SilkDeformationResult.MeshMissing:
                    _missingMeshRegions++;
                    break;
                case SilkDeformationResult.VertexCountMismatch:
                    _mismatchedRegions++;
                    break;
                default:
                    _nonFiniteRegions++;
                    break;
            }
        }

        // A mesh that was driven and is not any more has to get its authored geometry back. The
        // replacement was destructive, so nothing else in the scene still holds the rest pose:
        // without this a stopped simulation, a body that stops publishing, or a batch that shrinks
        // would leave the last simulated pose on screen indefinitely. Restored meshes join the
        // changed set so the same delta that uploads simulated geometry uploads the authored
        // geometry back.
        int restored = RestoreDropped(scene, _previousApplied);

        if (restored != 0 || _changed.Count != 0 || membershipChanged ||
            _applied.Count != _previousApplied.Count)
        {
            Revision++;
        }

        return _applied.Count;
    }

    /// <summary>Restores every mesh in one driven set that the current apply no longer drives.</summary>
    private int RestoreDropped(SilkSceneState scene, HashSet<ulong> previouslyDriven)
    {
        int restored = 0;
        foreach (ulong meshId in previouslyDriven)
        {
            if (_applied.Contains(meshId))
            {
                continue;
            }

            if (scene.RestoreAuthoredPoints(meshId))
            {
                _changed.Add(meshId);
                restored++;
            }

            _restoredMeshes++;
        }

        return restored;
    }
}
