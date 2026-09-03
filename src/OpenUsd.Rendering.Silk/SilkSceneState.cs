// Copyright (c) marcschier. Licensed under the MIT License.

using System.Buffers.Binary;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text;

namespace OpenUsd.Rendering.Silk;

/// <summary>
/// Retains the latest scene resources produced by hdSilk command pages.
/// </summary>
public sealed class SilkSceneState
{
    private readonly Dictionary<ulong, SilkMeshData> _meshes = [];
    private readonly Dictionary<(string Path, int InstanceIndex), SilkMeshData> _meshesByPath =
        [];
    private readonly Dictionary<ulong, string> _pathsByHash = [];
    private readonly Dictionary<string, List<SilkMeshData>> _instancesByPath =
        new(StringComparer.Ordinal);

    // The instance index that currently carries the ABI v8 prototype payload for a path. hdSilk
    // publishes it on the lowest instance index of a prototype, which is not index zero once an
    // instancer has several prototypes or hides instances: with proto indices [0, 1, 0, 1] the
    // second prototype owns instancer instances 1 and 3 and never publishes an index-zero record.
    private readonly Dictionary<string, int> _prototypeInstanceByPath =
        new(StringComparer.Ordinal);
    private readonly Dictionary<string, SilkMaterialData> _materials =
        new(StringComparer.Ordinal);
    private readonly Dictionary<string, SilkEnvironmentData> _environments =
        new(StringComparer.Ordinal);

    // The authored mesh of every mesh whose points are currently simulated. A deformation
    // destructively replaces the retained points, so without this the authored geometry exists
    // nowhere once the first replacement is published: stopping the simulation would leave the last
    // simulated pose on screen until USD happened to dirty the prim. One entry per driven mesh, and
    // it is dropped the moment the mesh is restored, re-authored by a page, or removed, so the
    // table can never outgrow the driven set.
    private readonly Dictionary<ulong, SilkMeshData> _authoredMeshes = [];
    private readonly Func<string, ulong>? _pathHasher;

    // The undo journal one page is applied under. A page is a transaction: it
    // either applies completely or changes nothing, and the state-dependent
    // checks -- a stable hash that does not match its path, a hash that collides
    // with another prim, a replacement that changes an identity without evidence
    // -- can only be made against the state the commands before it produced, so
    // they cannot all be hoisted ahead of the mutation. Recording the inverse of
    // every write instead is what lets a page whose fourth command is rejected
    // put the first three back exactly as they were, rather than retaining a
    // scene no producer ever published and no delta ever described.
    private readonly List<SilkStateUndo> _undo = [];
    private HashSet<string>? _journaledInstancePaths;
    private SilkFrameState? _frameUndo;
    private SilkLightLinkTable? _lightLinksUndo;
    private SilkShadowTable? _shadowsUndo;
    private bool _journaling;
    private bool _frameJournaled;
    private bool _lightLinksJournaled;
    private bool _shadowsJournaled;
    private ulong _undoRevision;
    private ulong _undoGeometryRevision;
    private ulong _undoMaterialRevision;
    private ulong _undoEnvironmentRevision;
    private ulong _undoDeformationRevision;

    /// <summary>Initializes an empty retained scene and pick identity table.</summary>
    public SilkSceneState()
        : this(pathHasher: null)
    {
    }

    internal SilkSceneState(Func<string, ulong>? pathHasher)
    {
        _pathHasher = pathHasher;
        PickIdentities = new SilkPickIdentityTable(uint.MaxValue, pathHasher);
    }

    /// <summary>Gets the latest page revision.</summary>
    public ulong Revision { get; private set; }

    /// <summary>Gets the retained frame state.</summary>
    public SilkFrameState Frame { get; } = new();

    /// <summary>Gets retained meshes by explicit Hydra prim ID.</summary>
    public IReadOnlyDictionary<ulong, SilkMeshData> Meshes => _meshes;

    /// <summary>
    /// Gets retained meshes keyed by USD prim path and instance ordinal. A
    /// point-instanced prototype contributes one entry per instance under the
    /// same authoritative path.
    /// </summary>
    public IReadOnlyDictionary<(string Path, int InstanceIndex), SilkMeshData> MeshesByPath =>
        _meshesByPath;

    /// <summary>Gets retained future-GPU token ranges and resolved identities.</summary>
    public SilkPickIdentityTable PickIdentities { get; }

    /// <summary>
    /// Gets retained materials keyed by USD material path, which is what a mesh's
    /// <see cref="SilkMeshData.MaterialPath"/> references.
    /// </summary>
    public IReadOnlyDictionary<string, SilkMaterialData> Materials => _materials;

    /// <summary>
    /// Gets retained textured dome-light environments keyed by USD prim path.
    /// </summary>
    /// <remarks>
    /// Untextured dome lights are not here: they stay part of the frame ambient
    /// term hdSilk already resolves, and only a dome that carries an image needs
    /// state the ambient colour cannot express.
    /// </remarks>
    public IReadOnlyDictionary<string, SilkEnvironmentData> Environments => _environments;

    /// <summary>
    /// Gets the retained UsdLux light and shadow link table.
    /// </summary>
    /// <remarks>
    /// Sparse and default-free: a scene that authors no linking retains nothing
    /// here and every prim resolves to <see cref="SilkLightLinkMasks.All"/>.
    /// </remarks>
    public SilkLightLinkTable LightLinks { get; } = new();

    /// <summary>
    /// Gets the retained bounded raster shadow-map descriptor table.
    /// </summary>
    /// <remarks>
    /// Empty for a scene that authors no shadow, so nothing is allocated and no
    /// shadow work is submitted until a page publishes a descriptor.
    /// </remarks>
    public SilkShadowTable Shadows { get; } = new();

    /// <summary>
    /// Gets the revision of the retained caster geometry, which changes whenever
    /// a mesh record is published, replaced, deformed, or removed.
    /// </summary>
    /// <remarks>
    /// A shadow map is a function of the caster set as well as of the light-space
    /// camera the page publishes. The page republishes its descriptors when the
    /// caster world bounds move, but geometry can change inside unchanged bounds
    /// -- a deformation, an instance transform, a topology replacement -- and a
    /// map rendered from the previous pose would then be silently stale. This
    /// revision is what a retained map is validated against, so an unchanged
    /// scene reuses its maps and a changed one re-renders exactly once.
    /// </remarks>
    public ulong GeometryRevision { get; private set; }

    /// <summary>
    /// Advances the geometry revision because a consumer rebuilt retained
    /// geometry from unchanged scene data.
    /// </summary>
    /// <remarks>
    /// A repaired texture asset changes what a displaced prim's vertices are
    /// without changing a single published byte, so nothing in the page can move
    /// this revision. A retained shadow map is validated against it, and a map
    /// rendered from the pre-repair vertices would otherwise be reused forever.
    /// </remarks>
    internal void AdvanceGeometryRevisionForRebuild() => GeometryRevision++;

    /// <summary>
    /// Gets the revision of the retained material set, which changes whenever a
    /// material is published, replaced, or removed.
    /// </summary>
    /// <remarks>
    /// Whether a prim casts a shadow depends on its material as well as on its
    /// geometry: an opacity-masked caster is excluded from every shadow map,
    /// because the depth-only program binds no material and cannot discard. A
    /// material can turn opaque or masked with no mesh command at all -- the same
    /// prim keeps its binding while the material behind it is re-authored -- so
    /// <see cref="GeometryRevision"/> alone would let a retained map, and the
    /// diagnostic that named its skipped casters, survive the change that
    /// invalidated both. A material binding change is already covered, because
    /// re-binding a prim republishes its mesh record.
    /// </remarks>
    public ulong MaterialRevision { get; private set; }

    /// <summary>
    /// Gets the revision of the retained environment set, which changes whenever
    /// an environment is published, replaced, or removed.
    /// </summary>
    /// <remarks>
    /// The frame constants carry the resolved environment contribution, and the
    /// frame's own revision does not move when only an environment changed. A
    /// separate revision is what makes those constants re-pack when a dome's
    /// texture or emission is re-authored.
    /// </remarks>
    public ulong EnvironmentRevision { get; private set; }

    /// <summary>
    /// Gets the revision of the retained bounded deformation rigs, which changes
    /// whenever a published rig's identity changes, whenever a prim starts or
    /// stops publishing one, and whenever a prim that carries one is removed.
    /// </summary>
    /// <remarks>
    /// The caster geometry revision already moves for every mesh command, so a
    /// consumer that deforms on the CPU needs nothing more. This revision exists
    /// for a consumer that evaluates the published rig instead: for such a
    /// consumer the pose lives in the rig rather than in the point array, and a
    /// retained shadow map rendered from the previous palette would be stale
    /// while every geometry input it was keyed on was unchanged. Keying the map
    /// on this as well is what makes the deformation identity part of shadow
    /// invalidation rather than an input only the colour pass sees.
    /// </remarks>
    public ulong DeformationRevision { get; private set; }

    /// <summary>Applies one dirty page and returns resource-change counts.</summary>
    public SilkSceneDelta Apply(OpenUsdSilkPage page)
    {
        ArgumentNullException.ThrowIfNull(page);
        bool requiresJournal = Preflight(page.GetEnumerator());
        return Apply(page.GetEnumerator(), page.Revision, requiresJournal);
    }

    /// <summary>
    /// Replaces the points of one retained mesh with externally simulated ones.
    /// </summary>
    /// <param name="path">The absolute authored prim path of the retained mesh.</param>
    /// <param name="instanceIndex">The zero-based instance ordinal.</param>
    /// <param name="points">The simulated points, three components per vertex.</param>
    /// <param name="meshId">
    /// Receives the retained mesh key whenever a retained mesh was found, whether or not its points
    /// changed, so a settled body stays addressable.
    /// </param>
    /// <returns>What the replacement did, distinguishing settled from refused.</returns>
    /// <remarks>
    /// <para>
    /// Only the point positions are replaced. The topology, attributes, material, and transform are
    /// the authored ones, because a deforming body moves its vertices without changing what it is.
    /// A point count that does not match the retained mesh is refused rather than resized: that is
    /// a different mesh, and drawing a solver's vertices against another mesh's indices would
    /// produce geometry neither side described.
    /// </para>
    /// <para>
    /// Authored per-vertex normals are dropped rather than carried over, because they describe the
    /// rest pose: keeping them shades a bent cloth as if it were still flat. An empty normal set is
    /// what makes the geometry builder recompute normals from the deformed topology, which is the
    /// same path an unauthored mesh has always taken. The authored mesh, normals included, is
    /// retained here and returned by <see cref="RestoreAuthoredPoints"/>.
    /// </para>
    /// <para>
    /// The retained mesh is immutable, so a replacement is a new instance published in its place.
    /// Callers must invalidate the retained GPU geometry of the returned mesh, because vertex
    /// buffers are rebuilt from a scene delta and this call emits none on its own.
    /// </para>
    /// </remarks>
    public SilkDeformationResult ReplacePoints(
        string path,
        int instanceIndex,
        ReadOnlySpan<float> points,
        out ulong meshId)
    {
        ArgumentNullException.ThrowIfNull(path);
        meshId = 0;
        if (!_meshesByPath.TryGetValue((path, instanceIndex), out SilkMeshData? mesh))
        {
            return SilkDeformationResult.MeshMissing;
        }

        meshId = mesh.Id;
        ReadOnlySpan<float> retained = mesh.GetPointSpan();
        if (points.Length != retained.Length)
        {
            return SilkDeformationResult.VertexCountMismatch;
        }

        for (int index = 0; index < points.Length; index++)
        {
            if (!float.IsFinite(points[index]))
            {
                return SilkDeformationResult.NonFiniteValue;
            }
        }

        // A settled body republishes the points it already carries. That is a
        // success with no work to do, and it must not be mistaken for a refusal
        // or the body would drop out of the driven set the moment it stops
        // moving.
        if (points.SequenceEqual(retained))
        {
            return SilkDeformationResult.Unchanged;
        }

        SilkMeshData replacement = mesh.WithSimulatedPoints(points);

        // The authored mesh is stashed on the first replacement only. While a mesh stays driven the
        // retained entry is the simulated one, so re-stashing it here would overwrite the rest pose
        // with a simulated one and make restoration a no-op that draws the last frame forever. A
        // page that re-authors the mesh drops the stash instead, so the next replacement captures
        // the newly authored geometry.
        _ = _authoredMeshes.TryAdd(mesh.Id, mesh);
        _meshes[replacement.Id] = replacement;
        _meshesByPath[(path, instanceIndex)] = replacement;
        if (_instancesByPath.TryGetValue(path, out List<SilkMeshData>? instances))
        {
            for (int index = 0; index < instances.Count; index++)
            {
                if (instances[index].InstanceIndex == instanceIndex)
                {
                    instances[index] = replacement;
                    break;
                }
            }
        }

        meshId = replacement.Id;
        GeometryRevision++;
        return SilkDeformationResult.Applied;
    }

    /// <summary>Puts the authored geometry of one simulated mesh back into the retained scene.</summary>
    /// <param name="meshId">The retained mesh key a deformation was applied to.</param>
    /// <returns>
    /// <see langword="true"/> when a mesh was restored, so its retained GPU geometry must be
    /// invalidated by the caller; <see langword="false"/> when the mesh was not simulated, was
    /// already re-authored by a page, or has been removed from the scene.
    /// </returns>
    /// <remarks>
    /// <para>
    /// This is the other half of <see cref="ReplacePoints"/>, and stopping a simulation is not
    /// complete without it. The replacement is destructive - the authored points are not retained
    /// anywhere else in the scene - so dropping a deformation restores nothing on its own and the
    /// last simulated pose stays on screen until USD happens to republish the prim, which for a
    /// static stage may be never.
    /// </para>
    /// <para>
    /// Like a replacement, this emits no delta of its own. The caller collects the restored mesh
    /// keys and invalidates exactly those, so restoring costs one upload per mesh that was
    /// simulated and nothing for the rest of the scene.
    /// </para>
    /// </remarks>
    public bool RestoreAuthoredPoints(ulong meshId)
    {
        if (!_authoredMeshes.Remove(meshId, out SilkMeshData? authored))
        {
            return false;
        }

        // A mesh the scene no longer retains has nothing to restore. Its stash is still dropped
        // above, because the mesh it described is gone.
        if (!_meshes.ContainsKey(meshId))
        {
            return false;
        }

        _meshes[authored.Id] = authored;
        _meshesByPath[(authored.Path, authored.InstanceIndex)] = authored;
        if (_instancesByPath.TryGetValue(authored.Path, out List<SilkMeshData>? instances))
        {
            for (int index = 0; index < instances.Count; index++)
            {
                if (instances[index].InstanceIndex == authored.InstanceIndex)
                {
                    instances[index] = authored;
                    break;
                }
            }
        }

        GeometryRevision++;
        return true;
    }

    /// <summary>Reports whether the retained points of one mesh are currently simulated.</summary>
    /// <param name="meshId">The retained mesh key.</param>
    /// <returns><see langword="true"/> when authored geometry is retained for restoration.</returns>
    public bool HasAuthoredGeometry(ulong meshId) => _authoredMeshes.ContainsKey(meshId);

    /// <summary>Applies command bytes from a test or recorded page.</summary>
    public SilkSceneDelta Apply(
        ReadOnlySpan<byte> data,
        uint commandCount,
        ulong revision)
    {
        bool requiresJournal = Preflight(SilkCommandParser.Enumerate(data, commandCount));
        return Apply(
            SilkCommandParser.Enumerate(data, commandCount),
            revision,
            requiresJournal);
    }

    /// <summary>
    /// Walks the whole page once, validating every command and the relationships
    /// between them, before a single byte of retained state is touched.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Constructing a command view is what validates it, and the mutating pass
    /// constructs them one at a time as it applies them -- so a page whose fourth
    /// command is malformed used to retain the first three and then throw, leaving
    /// the scene in a state no producer ever published. Running the same
    /// constructors here first makes rejection whole: a page either applies
    /// completely or changes nothing.
    /// </para>
    /// <para>
    /// It is also the only place the relationships <em>between</em> commands can
    /// be checked. The frame's dome table is the authority: the masks a
    /// <c>LIGHT_LINK</c> command carries index it, and the <c>dome_index</c> an
    /// <c>ENVIRONMENT</c> record claims is an entry in it. A page whose three
    /// commands disagree describes no scene at all, and applying two thirds of it
    /// would light prims from domes the frame never published.
    /// </para>
    /// </remarks>
    /// <param name="commands">The page to validate, which is consumed.</param>
    /// <returns>
    /// Whether the page carries a command whose validation depends on retained
    /// state, and which therefore has to be applied under the undo journal.
    /// </returns>
    /// <exception cref="InvalidDataException">The page is malformed or inconsistent.</exception>
    private bool Preflight(SilkCommandEnumerator commands)
    {
        uint domeCount = Frame.DomeCount;
        uint lightCount = Frame.LightCount;
        Span<bool> environmentDomes = stackalloc bool[(int)SilkFrameCommand.MaximumDomes];
        for (int dome = 0; dome < environmentDomes.Length; dome++)
        {
            environmentDomes[dome] = Frame.Domes[dome].IsPresent &&
                Frame.Domes[dome].IsTextured;
        }

        bool requiresJournal = false;

        // The effective light link table: the page's, or the retained one when
        // the page publishes none. A frame-only page changes the ordering the
        // retained masks index, so validating only what the page carries would
        // let a camera update silently reinterpret every retained mask.
        bool linkIsCanonicalEmpty = LightLinks.IsCanonicalEmpty;
        uint linkDomeCount = LightLinks.DomeCount;
        uint linkLightCount = LightLinks.LightCount;

        // The environment records this page leaves behind, keyed by path, built
        // by replaying the page's upserts and removals in order over the retained
        // set. Keying by path is what makes a record that a later command
        // supersedes irrelevant -- only the final shape of each path is a state
        // the renderer will ever resolve -- and it is what keeps a page with more
        // environment commands than the dome budget from overrunning anything:
        // the number of commands bounds nothing, the number of distinct paths
        // does. Allocated only for a page that carries an environment command at
        // all, so a frame-only page stays allocation free.
        Dictionary<string, uint?>? effectiveEnvironments = null;
        using (commands)
        {
            while (commands.MoveNext())
            {
                switch (commands.Current.Type)
                {
                    case SilkCommandType.Frame:
                        SilkFrameCommand frame = commands.Current.AsFrame();
                        domeCount = frame.DomeCount;
                        lightCount = frame.LightCount;
                        for (int dome = 0; dome < environmentDomes.Length; dome++)
                        {
                            SilkFrameDomeState flags = frame.GetDomeFlags(dome);
                            environmentDomes[dome] = dome < domeCount &&
                                (flags & SilkFrameDomeState.Present) != 0 &&
                                (flags & SilkFrameDomeState.Textured) != 0;
                        }
                        break;
                    case SilkCommandType.MeshUpsert:
                        _ = commands.Current.AsMeshUpsert();
                        requiresJournal = true;
                        break;
                    case SilkCommandType.MeshRemove:
                        _ = commands.Current.AsMeshRemove();
                        requiresJournal = true;
                        break;
                    case SilkCommandType.MaterialUpsert:
                        _ = commands.Current.AsMaterialUpsert();
                        requiresJournal = true;
                        break;
                    case SilkCommandType.MaterialRemove:
                        _ = commands.Current.AsMaterialRemove();
                        requiresJournal = true;
                        break;
                    case SilkCommandType.EnvironmentUpsert:
                        SilkEnvironmentUpsertCommand upsert =
                            commands.Current.AsEnvironmentUpsert();
                        requiresJournal = true;
                        RequireEffectiveEnvironments(ref effectiveEnvironments)[upsert.Path] =
                            upsert.DomeIndex;
                        break;
                    case SilkCommandType.EnvironmentRemove:
                        requiresJournal = true;
                        RequireEffectiveEnvironments(ref effectiveEnvironments)[
                            commands.Current.AsEnvironmentRemove().Path] = null;
                        break;
                    case SilkCommandType.LightLink:
                        SilkLightLinkCommand links = commands.Current.AsLightLink();
                        requiresJournal = true;
                        linkDomeCount = links.DomeCount;
                        linkLightCount = links.LightCount;
                        linkIsCanonicalEmpty = links.EntryCount == 0 &&
                            links.LightCount == 0 &&
                            links.DomeCount == 0;
                        break;
                    case SilkCommandType.Shadow:
                        _ = commands.Current.AsShadow();
                        requiresJournal = true;
                        break;
                    default:
                        throw new InvalidDataException(
                            $"Unsupported hdSilk command {commands.Current.Type}.");
                }
            }
        }

        // Every record the page leaves behind is validated against the frame the
        // page leaves behind, and the mapping between the two must be a bijection
        // over the textured domes: each textured entry of the frame dome table is
        // one dome's image, so exactly one record names it. A record naming an
        // absent or untextured entry describes a dome nobody published; two
        // records naming the same entry make one dome's mask select the other's
        // sky; and a textured entry no record names is a dome the renderer has no
        // image for and no prim can be excluded from.
        Span<bool> claimed = stackalloc bool[(int)SilkFrameCommand.MaximumDomes];
        if (effectiveEnvironments is null)
        {
            foreach (KeyValuePair<string, SilkEnvironmentData> retained in _environments)
            {
                ClaimEnvironmentDome(
                    retained.Value.HasDomeIndex
                        ? retained.Value.DomeIndex
                        : SilkEnvironmentUpsertCommand.NoDomeIndex,
                    domeCount,
                    environmentDomes,
                    claimed);
            }
        }
        else
        {
            foreach (KeyValuePair<string, SilkEnvironmentData> retained in _environments)
            {
                if (!effectiveEnvironments.ContainsKey(retained.Key))
                {
                    effectiveEnvironments[retained.Key] = retained.Value.DomeIndex;
                }
            }
            foreach (uint? index in effectiveEnvironments.Values)
            {
                // A null entry is a path this page retired, which claims nothing.
                if (index is { } claimedIndex)
                {
                    ClaimEnvironmentDome(
                        claimedIndex,
                        domeCount,
                        environmentDomes,
                        claimed);
                }
            }
        }

        for (int dome = 0; dome < environmentDomes.Length; dome++)
        {
            if (environmentDomes[dome] && !claimed[dome])
            {
                throw new InvalidDataException(
                    $"The frame dome table publishes textured dome {dome}, which no " +
                    "environment record supplies an image for.");
            }
        }
        // A canonical empty table is how a page says "linking was retired", and it
        // is valid against any frame precisely because it indexes nothing. Every
        // other table's masks are read against the frame's light and dome
        // orderings, so a count that disagrees with the frame names a different
        // set of lights or domes than the masks were resolved against.
        if (!linkIsCanonicalEmpty)
        {
            if (linkLightCount != lightCount)
            {
                throw new InvalidDataException(
                    $"The light link table indexes {linkLightCount} lights while the " +
                    $"frame publishes {lightCount}.");
            }
            if (linkDomeCount != domeCount)
            {
                throw new InvalidDataException(
                    $"The light link table indexes {linkDomeCount} domes while the frame " +
                    $"publishes {domeCount}.");
            }
        }

        return requiresJournal;
    }

    /// <summary>
    /// Gets the effective environment map, seeded on first use so a page with no
    /// environment command allocates nothing.
    /// </summary>
    private static Dictionary<string, uint?> RequireEffectiveEnvironments(
        ref Dictionary<string, uint?>? effective) =>
        effective ??= new Dictionary<string, uint?>(StringComparer.Ordinal);

    /// <summary>
    /// Records one effective record's claim on a dome table entry, refusing an
    /// entry that does not exist, is not textured, or is already claimed.
    /// </summary>
    private static void ClaimEnvironmentDome(
        uint index,
        uint domeCount,
        ReadOnlySpan<bool> environmentDomes,
        Span<bool> claimed)
    {
        if (index == SilkEnvironmentUpsertCommand.NoDomeIndex)
        {
            // An unindexed record is only meaningful while there is no dome table
            // to index. Once the frame publishes one, every textured dome has an
            // entry in it by construction, so a record that declines to name one
            // is a producer that resolved its domes and its environments against
            // different orderings -- and the renderer would silently give that
            // dome's sky to every prim, including the ones whose collection
            // excludes it.
            if (domeCount != 0)
            {
                throw new InvalidDataException(
                    "An environment record carries no dome index while the frame " +
                    $"publishes {domeCount} domes.");
            }
            return;
        }

        if (index >= domeCount || !environmentDomes[(int)index])
        {
            throw new InvalidDataException(
                $"An environment record claims dome index {index}, which the " +
                "frame dome table does not publish as a textured dome.");
        }
        if (claimed[(int)index])
        {
            throw new InvalidDataException(
                $"Two environment records claim frame dome index {index}.");
        }
        claimed[(int)index] = true;
    }

    private SilkSceneDelta Apply(
        SilkCommandEnumerator commands,
        ulong revision,
        bool requiresJournal)
    {
        if (!requiresJournal)
        {
            // Nothing in this page can be rejected against retained state, so
            // there is nothing to put back and the journal would be pure cost.
            // This is the frame-only page every interactive session publishes.
            return ApplyCore(commands, revision);
        }

        BeginTransaction();
        bool committed = false;
        try
        {
            SilkSceneDelta delta = ApplyCore(commands, revision);
            CommitTransaction();
            committed = true;
            return delta;
        }
        finally
        {
            if (!committed)
            {
                RollbackTransaction();
            }
        }
    }

    private SilkSceneDelta ApplyCore(SilkCommandEnumerator commands, ulong revision)
    {
        List<ulong>? upserts = null;
        List<ulong>? removals = null;
        List<string>? materialChanges = null;
        using (commands)
        {
            while (commands.MoveNext())
            {
                switch (commands.Current.Type)
                {
                    case SilkCommandType.Frame:
                        JournalFrame();
                        Frame.Update(commands.Current.AsFrame());
                        break;
                    case SilkCommandType.MeshUpsert:
                        SilkMeshUpsertCommand upsert = commands.Current.AsMeshUpsert();
                        SilkMeshData mesh = CopyMeshFrom(upsert);
                        if (UpsertMesh(mesh) is { } replacedId)
                        {
                            (removals ??= []).Add(replacedId);
                        }

                        // Registering only after the record is retained keeps the pointer from
                        // naming a record a validation failure rejected.
                        if (!upsert.IsInstanceReference)
                        {
                            SetPrototypeInstance(mesh.Path, mesh.InstanceIndex);
                        }

                        (upserts ??= []).Add(mesh.Id);
                        break;
                    case SilkCommandType.MeshRemove:
                        SilkMeshRemoveCommand removal =
                            commands.Current.AsMeshRemove();
                        if (RemoveMesh(removal, out ulong removedId))
                        {
                            (removals ??= []).Add(removedId);
                        }
                        break;
                    case SilkCommandType.MaterialUpsert:
                        SilkMaterialData material = SilkMaterialData.CopyFrom(
                            commands.Current.AsMaterialUpsert());
                        VerifyStableHash(material.Path, material.StableHash);
                        SetMaterial(material.Path, material);
                        (materialChanges ??= []).Add(material.Path);
                        MaterialRevision++;
                        break;
                    case SilkCommandType.MaterialRemove:
                        SilkMaterialRemoveCommand materialRemoval =
                            commands.Current.AsMaterialRemove();
                        VerifyStableHash(
                            materialRemoval.Path,
                            materialRemoval.StableHash);
                        if (RemoveMaterial(materialRemoval.Path))
                        {
                            MaterialRevision++;
                        }
                        (materialChanges ??= []).Add(materialRemoval.Path);
                        break;
                    case SilkCommandType.EnvironmentUpsert:
                        SilkEnvironmentData environment = SilkEnvironmentData.CopyFrom(
                            commands.Current.AsEnvironmentUpsert());
                        VerifyStableHash(environment.Path, environment.StableHash);
                        SetEnvironment(environment.Path, environment);
                        EnvironmentRevision++;
                        break;
                    case SilkCommandType.EnvironmentRemove:
                        SilkEnvironmentRemoveCommand environmentRemoval =
                            commands.Current.AsEnvironmentRemove();
                        VerifyStableHash(
                            environmentRemoval.Path,
                            environmentRemoval.StableHash);
                        if (RemoveEnvironment(environmentRemoval.Path))
                        {
                            EnvironmentRevision++;
                        }
                        break;
                    case SilkCommandType.LightLink:
                        JournalLightLinks();
                        LightLinks.Update(commands.Current.AsLightLink());
                        break;
                    case SilkCommandType.Shadow:
                        JournalShadows();
                        Shadows.Update(commands.Current.AsShadow());
                        break;
                    default:
                        throw new InvalidDataException(
                            $"Unsupported hdSilk command {commands.Current.Type}.");
                }
            }
        }

        Revision = revision;
        return new SilkSceneDelta(
            upserts?.ToArray() ?? [],
            removals?.ToArray() ?? [],
            materialChanges?.ToArray() ?? []);
    }

    /// <summary>Opens the undo journal one page is applied under.</summary>
    private void BeginTransaction()
    {
        _undo.Clear();
        _journaledInstancePaths?.Clear();
        _frameJournaled = false;
        _lightLinksJournaled = false;
        _shadowsJournaled = false;
        _undoRevision = Revision;
        _undoGeometryRevision = GeometryRevision;
        _undoMaterialRevision = MaterialRevision;
        _undoEnvironmentRevision = EnvironmentRevision;
        _undoDeformationRevision = DeformationRevision;
        _journaling = true;
        PickIdentities.BeginTransaction();
    }

    /// <summary>Accepts every write the page made and drops the journal.</summary>
    private void CommitTransaction()
    {
        _journaling = false;
        _undo.Clear();
        _journaledInstancePaths?.Clear();
        PickIdentities.CommitTransaction();
    }

    /// <summary>
    /// Puts every write the rejected page made back, newest first.
    /// </summary>
    /// <remarks>
    /// Reverse order is what makes a key that was written more than once by the
    /// same page end up with the value it had before the page, rather than with
    /// the value the page's first write replaced.
    /// </remarks>
    private void RollbackTransaction()
    {
        _journaling = false;
        for (int index = _undo.Count - 1; index >= 0; index--)
        {
            SilkStateUndo entry = _undo[index];
            switch (entry.Kind)
            {
                case SilkStateUndoKind.MeshById:
                    if (entry.Existed)
                    {
                        _meshes[entry.Key] = (SilkMeshData)entry.Value!;
                    }
                    else
                    {
                        _ = _meshes.Remove(entry.Key);
                    }
                    break;
                case SilkStateUndoKind.MeshByPath:
                    if (entry.Existed)
                    {
                        _meshesByPath[(entry.Path!, entry.Index)] =
                            (SilkMeshData)entry.Value!;
                    }
                    else
                    {
                        _ = _meshesByPath.Remove((entry.Path!, entry.Index));
                    }
                    break;
                case SilkStateUndoKind.PathByHash:
                    if (entry.Existed)
                    {
                        _pathsByHash[entry.Key] = (string)entry.Value!;
                    }
                    else
                    {
                        _ = _pathsByHash.Remove(entry.Key);
                    }
                    break;
                case SilkStateUndoKind.PrototypeInstance:
                    if (entry.Existed)
                    {
                        _prototypeInstanceByPath[entry.Path!] = entry.Index;
                    }
                    else
                    {
                        _ = _prototypeInstanceByPath.Remove(entry.Path!);
                    }
                    break;
                case SilkStateUndoKind.AuthoredMesh:
                    if (entry.Existed)
                    {
                        _authoredMeshes[entry.Key] = (SilkMeshData)entry.Value!;
                    }
                    else
                    {
                        _ = _authoredMeshes.Remove(entry.Key);
                    }
                    break;
                case SilkStateUndoKind.Material:
                    if (entry.Existed)
                    {
                        _materials[entry.Path!] = (SilkMaterialData)entry.Value!;
                    }
                    else
                    {
                        _ = _materials.Remove(entry.Path!);
                    }
                    break;
                case SilkStateUndoKind.Environment:
                    if (entry.Existed)
                    {
                        _environments[entry.Path!] = (SilkEnvironmentData)entry.Value!;
                    }
                    else
                    {
                        _ = _environments.Remove(entry.Path!);
                    }
                    break;
                case SilkStateUndoKind.InstanceList:
                    if (entry.Existed)
                    {
                        _instancesByPath[entry.Path!] = (List<SilkMeshData>)entry.Value!;
                    }
                    else
                    {
                        _ = _instancesByPath.Remove(entry.Path!);
                    }
                    break;
                default:
                    throw new InvalidOperationException(
                        $"The Silk scene undo journal has an unknown entry {entry.Kind}.");
            }
        }

        _undo.Clear();
        _journaledInstancePaths?.Clear();
        if (_frameJournaled)
        {
            Frame.CopyFrom(_frameUndo!);
        }
        if (_lightLinksJournaled)
        {
            LightLinks.SwapWith(_lightLinksUndo!);
        }
        if (_shadowsJournaled)
        {
            Shadows.RestoreFrom(_shadowsUndo!);
        }
        Revision = _undoRevision;
        GeometryRevision = _undoGeometryRevision;
        MaterialRevision = _undoMaterialRevision;
        EnvironmentRevision = _undoEnvironmentRevision;
        DeformationRevision = _undoDeformationRevision;
        PickIdentities.RollbackTransaction();
    }

    private void JournalFrame()
    {
        if (!_journaling || _frameJournaled)
        {
            return;
        }
        _frameUndo ??= new SilkFrameState();
        _frameUndo.CopyFrom(Frame);
        _frameJournaled = true;
    }

    private void JournalLightLinks()
    {
        if (!_journaling || _lightLinksJournaled)
        {
            return;
        }
        _lightLinksUndo ??= new SilkLightLinkTable();
        _lightLinksUndo.CopyFrom(LightLinks);
        _lightLinksJournaled = true;
    }

    private void JournalShadows()
    {
        if (!_journaling || _shadowsJournaled)
        {
            return;
        }
        _shadowsUndo ??= new SilkShadowTable();
        Shadows.CopyAsideInto(_shadowsUndo);
        _shadowsJournaled = true;
    }

    private void SetMeshById(ulong id, SilkMeshData mesh)
    {
        if (_journaling)
        {
            bool existed = _meshes.TryGetValue(id, out SilkMeshData? previous);
            _undo.Add(new SilkStateUndo(
                SilkStateUndoKind.MeshById, id, null, 0, previous, existed));
        }
        _meshes[id] = mesh;
    }

    private void RemoveMeshById(ulong id)
    {
        if (_journaling)
        {
            bool existed = _meshes.TryGetValue(id, out SilkMeshData? previous);
            _undo.Add(new SilkStateUndo(
                SilkStateUndoKind.MeshById, id, null, 0, previous, existed));
        }
        _ = _meshes.Remove(id);
    }

    private void SetMeshByPath((string Path, int InstanceIndex) key, SilkMeshData mesh)
    {
        if (_journaling)
        {
            bool existed = _meshesByPath.TryGetValue(key, out SilkMeshData? previous);
            _undo.Add(new SilkStateUndo(
                SilkStateUndoKind.MeshByPath,
                0,
                key.Path,
                key.InstanceIndex,
                previous,
                existed));
        }
        _meshesByPath[key] = mesh;
    }

    private void RemoveMeshByPath((string Path, int InstanceIndex) key)
    {
        if (_journaling)
        {
            bool existed = _meshesByPath.TryGetValue(key, out SilkMeshData? previous);
            _undo.Add(new SilkStateUndo(
                SilkStateUndoKind.MeshByPath,
                0,
                key.Path,
                key.InstanceIndex,
                previous,
                existed));
        }
        _ = _meshesByPath.Remove(key);
    }

    private void SetPathByHash(ulong hash, string path)
    {
        if (_journaling)
        {
            bool existed = _pathsByHash.TryGetValue(hash, out string? previous);
            _undo.Add(new SilkStateUndo(
                SilkStateUndoKind.PathByHash, hash, null, 0, previous, existed));
        }
        _pathsByHash[hash] = path;
    }

    private void RemovePathByHash(ulong hash)
    {
        if (_journaling)
        {
            bool existed = _pathsByHash.TryGetValue(hash, out string? previous);
            _undo.Add(new SilkStateUndo(
                SilkStateUndoKind.PathByHash, hash, null, 0, previous, existed));
        }
        _ = _pathsByHash.Remove(hash);
    }

    private void SetPrototypeInstance(string path, int instanceIndex)
    {
        if (_journaling)
        {
            bool existed = _prototypeInstanceByPath.TryGetValue(path, out int previous);
            _undo.Add(new SilkStateUndo(
                SilkStateUndoKind.PrototypeInstance, 0, path, previous, null, existed));
        }
        _prototypeInstanceByPath[path] = instanceIndex;
    }

    private void RemovePrototypeInstance(string path)
    {
        if (_journaling)
        {
            bool existed = _prototypeInstanceByPath.TryGetValue(path, out int previous);
            _undo.Add(new SilkStateUndo(
                SilkStateUndoKind.PrototypeInstance, 0, path, previous, null, existed));
        }
        _ = _prototypeInstanceByPath.Remove(path);
    }

    private void RemoveAuthoredMesh(ulong id)
    {
        if (_journaling)
        {
            bool existed = _authoredMeshes.TryGetValue(id, out SilkMeshData? previous);
            _undo.Add(new SilkStateUndo(
                SilkStateUndoKind.AuthoredMesh, id, null, 0, previous, existed));
        }
        _ = _authoredMeshes.Remove(id);
    }

    private void SetMaterial(string path, SilkMaterialData material)
    {
        if (_journaling)
        {
            bool existed = _materials.TryGetValue(path, out SilkMaterialData? previous);
            _undo.Add(new SilkStateUndo(
                SilkStateUndoKind.Material, 0, path, 0, previous, existed));
        }
        _materials[path] = material;
    }

    private bool RemoveMaterial(string path)
    {
        if (_journaling)
        {
            bool existed = _materials.TryGetValue(path, out SilkMaterialData? previous);
            _undo.Add(new SilkStateUndo(
                SilkStateUndoKind.Material, 0, path, 0, previous, existed));
        }
        return _materials.Remove(path);
    }

    private void SetEnvironment(string path, SilkEnvironmentData environment)
    {
        if (_journaling)
        {
            bool existed = _environments.TryGetValue(
                path,
                out SilkEnvironmentData? previous);
            _undo.Add(new SilkStateUndo(
                SilkStateUndoKind.Environment, 0, path, 0, previous, existed));
        }
        _environments[path] = environment;
    }

    private bool RemoveEnvironment(string path)
    {
        if (_journaling)
        {
            bool existed = _environments.TryGetValue(
                path,
                out SilkEnvironmentData? previous);
            _undo.Add(new SilkStateUndo(
                SilkStateUndoKind.Environment, 0, path, 0, previous, existed));
        }
        return _environments.Remove(path);
    }

    /// <summary>
    /// Records the instance list of one path once per page, and replaces it with
    /// a copy so the page's edits never touch the list the journal holds.
    /// </summary>
    /// <remarks>
    /// The list is mutated in place rather than replaced, so its previous
    /// contents cannot be recovered from the dictionary entry the way every other
    /// retained value can. Copying on the first touch of a page is what makes the
    /// rollback exact without an undo entry per element, and the copy is taken
    /// only once because every later edit of the same page already lands on it.
    /// </remarks>
    private void JournalInstanceList(string path)
    {
        if (!_journaling)
        {
            return;
        }
        _journaledInstancePaths ??= new HashSet<string>(StringComparer.Ordinal);
        if (!_journaledInstancePaths.Add(path))
        {
            return;
        }
        bool existed = _instancesByPath.TryGetValue(
            path,
            out List<SilkMeshData>? previous);
        _undo.Add(new SilkStateUndo(
            SilkStateUndoKind.InstanceList,
            0,
            path,
            0,
            previous,
            existed));
        if (existed)
        {
            _instancesByPath[path] = [.. previous!];
        }
    }

    /// <summary>Gets the retained instance list of one path, creating it once.</summary>
    private List<SilkMeshData> RequireInstanceList(string path)
    {
        if (_instancesByPath.TryGetValue(path, out List<SilkMeshData>? instances))
        {
            return instances;
        }
        instances = [];
        _instancesByPath[path] = instances;
        return instances;
    }

    private enum SilkStateUndoKind
    {
        MeshById,
        MeshByPath,
        PathByHash,
        PrototypeInstance,
        AuthoredMesh,
        Material,
        Environment,
        InstanceList,
    }

    private readonly record struct SilkStateUndo(
        SilkStateUndoKind Kind,
        ulong Key,
        string? Path,
        int Index,
        object? Value,
        bool Existed);

    private SilkMeshData CopyMeshFrom(SilkMeshUpsertCommand command)
    {
        if (!command.IsInstanceReference)
        {
            return SilkMeshData.CopyFrom(command);
        }

        if (!TryGetPrototype(command.Path, out SilkMeshData? prototype))
        {
            throw new InvalidDataException(
                $"hdSilk instance '{command.Path}' index {command.InstanceIndex} " +
                "arrived before its prototype geometry.");
        }
        return SilkMeshData.CopyInstanceFrom(command, prototype);
    }

    /// <summary>
    /// Resolves the retained record that carries the prototype payload of one
    /// authoritative path.
    /// </summary>
    /// <remarks>
    /// hdSilk publishes the payload on the lowest instance index a prototype
    /// owns, so the fallback to index zero only covers a page whose payload
    /// record was retained before this session started tracking the pointer.
    /// </remarks>
    private bool TryGetPrototype(
        string path,
        [NotNullWhen(true)] out SilkMeshData? prototype)
    {
        if (_prototypeInstanceByPath.TryGetValue(path, out int prototypeIndex) &&
            _meshesByPath.TryGetValue((path, prototypeIndex), out prototype))
        {
            return true;
        }
        return _meshesByPath.TryGetValue((path, 0), out prototype);
    }

    /// <summary>
    /// Requires the wire hash to match the path it indexes. The hash is an index
    /// only, so a mismatch means the page is inconsistent rather than merely
    /// colliding, and must fail loudly here.
    /// </summary>
    private void VerifyStableHash(string path, ulong stableHash)
    {
        ulong expected = _pathHasher is null
            ? SilkWireFormat.ComputeStableHash(path)
            : _pathHasher(path);
        if (stableHash != expected)
        {
            throw new InvalidDataException(
                $"The hdSilk material hash for '{path}' does not match its path.");
        }
    }

    private ulong? UpsertMesh(SilkMeshData mesh)
    {

        ulong expectedHash = _pathHasher is null
            ? SilkWireFormat.ComputeStableHash(mesh.Path)
            : _pathHasher(mesh.Path);
        if (mesh.StableHash != expectedHash)
        {
            throw new InvalidDataException(
                $"hdSilk path '{mesh.Path}' has stable hash " +
                $"0x{mesh.StableHash:X16}, expected 0x{expectedHash:X16}.");
        }
        if (_pathsByHash.TryGetValue(mesh.StableHash, out string? hashPath) &&
            !string.Equals(hashPath, mesh.Path, StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                $"hdSilk path hash 0x{mesh.StableHash:X16} collides for " +
                $"'{hashPath}' and '{mesh.Path}'.");
        }

        if (_meshes.TryGetValue(mesh.Id, out SilkMeshData? primMesh) &&
            !string.Equals(primMesh.Path, mesh.Path, StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                $"hdSilk prim ID {mesh.PrimId} instance {mesh.InstanceIndex} is " +
                $"shared by '{primMesh.Path}' and '{mesh.Path}'.");
        }

        ulong? replacedId = null;
        (string Path, int InstanceIndex) pathKey = (mesh.Path, mesh.InstanceIndex);
        if (_meshesByPath.TryGetValue(pathKey, out SilkMeshData? pathMesh))
        {
            if (pathMesh.StableHash != mesh.StableHash)
            {
                throw new InvalidDataException(
                    $"hdSilk changed the path-derived hash for '{mesh.Path}'.");
            }

            bool implicitRecreation =
                pathMesh.PrimId != mesh.PrimId ||
                mesh.TopologyRevision < pathMesh.TopologyRevision;
            if (implicitRecreation && pathMesh.Id != mesh.Id)
            {
                replacedId = pathMesh.Id;
            }
        }

        PickIdentities.Upsert(mesh);
        if (replacedId is { } oldId)
        {
            RemoveMeshById(oldId);
            RemoveAuthoredMesh(oldId);
        }

        // A page re-authors the geometry, so whatever rest pose was stashed for this mesh describes
        // a version the stage has replaced. Dropping it here is what lets a mesh that is still
        // driven capture the new authored geometry on its next replacement, and what makes a later
        // restore return the newest authored points rather than a stale copy.
        RemoveAuthoredMesh(mesh.Id);
        SetMeshById(mesh.Id, mesh);
        SetMeshByPath(pathKey, mesh);
        SetPathByHash(mesh.StableHash, mesh.Path);
        GeometryRevision++;
        // Only a pose that actually moved advances the deformation revision, so
        // a page that republishes an unchanged rig -- which happens whenever a
        // material or a transform dirties a skinned prim -- does not re-render
        // every retained shadow map.
        ulong previousIdentity = pathMesh?.DeformationIdentity ?? 0;
        if (previousIdentity != mesh.DeformationIdentity)
        {
            DeformationRevision++;
        }

        JournalInstanceList(mesh.Path);
        List<SilkMeshData> instances = RequireInstanceList(mesh.Path);
        int existing = instances.FindIndex(
            candidate => candidate.InstanceIndex == mesh.InstanceIndex);
        if (existing >= 0)
        {
            instances[existing] = mesh;
        }
        else
        {
            instances.Add(mesh);
        }
        return replacedId;
    }

    /// <summary>
    /// Gets every retained instance of one authoritative prim path. A prim with
    /// no instancer yields a single entry.
    /// </summary>
    internal IReadOnlyList<SilkMeshData> GetInstances(string path) =>
        _instancesByPath.TryGetValue(path, out List<SilkMeshData>? instances)
            ? instances
            : [];

    private bool RemoveMesh(
        SilkMeshRemoveCommand removal,
        out ulong removedId)
    {
        removedId = 0;
        ulong expectedHash = _pathHasher is null
            ? SilkWireFormat.ComputeStableHash(removal.Path)
            : _pathHasher(removal.Path);
        if (removal.StableHash != expectedHash)
        {
            throw new InvalidDataException(
                $"hdSilk removal path '{removal.Path}' has stable hash " +
                $"0x{removal.StableHash:X16}, expected 0x{expectedHash:X16}.");
        }
        if (_pathsByHash.TryGetValue(removal.StableHash, out string? hashPath) &&
            !string.Equals(hashPath, removal.Path, StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                $"hdSilk removal hash 0x{removal.StableHash:X16} names " +
                $"'{hashPath}', not '{removal.Path}'.");
        }
        (string Path, int InstanceIndex) pathKey =
            (removal.Path, removal.InstanceIndex);
        if (!_meshesByPath.TryGetValue(pathKey, out SilkMeshData? mesh))
        {
            return false;
        }
        if (mesh.StableHash != removal.StableHash)
        {
            throw new InvalidDataException(
                $"hdSilk removal for '{removal.Path}' has a different stable hash.");
        }

        removedId = mesh.Id;
        RemoveMeshByPath(pathKey);
        RemoveMeshById(mesh.Id);
        GeometryRevision++;
        if (mesh.DeformationIdentity != 0)
        {
            DeformationRevision++;
        }

        // A removed mesh can never be restored, so its stashed rest pose retires with it rather
        // than keeping a copy of every mesh the simulation ever drove.
        RemoveAuthoredMesh(mesh.Id);

        // Pick identity is per instance, so it retires with this record. The path hash index
        // is shared by every instance of a prototype, so it survives until the last one goes.
        _ = PickIdentities.Remove(mesh.Path, mesh.InstanceIndex);
        if (_prototypeInstanceByPath.TryGetValue(
                removal.Path,
                out int prototypeIndex) &&
            prototypeIndex == removal.InstanceIndex)
        {
            // The payload record is gone. A page always republishes the payload on the
            // prototype's new lowest index before retiring the old one, so this only clears
            // a pointer the page already replaced or the last instance of the path.
            RemovePrototypeInstance(removal.Path);
        }
        JournalInstanceList(removal.Path);
        if (_instancesByPath.TryGetValue(removal.Path, out List<SilkMeshData>? instances))
        {
            int instanceIndex = removal.InstanceIndex;
            _ = instances.RemoveAll(
                candidate => candidate.InstanceIndex == instanceIndex);
            if (instances.Count == 0)
            {
                _ = _instancesByPath.Remove(removal.Path);
                RemovePrototypeInstance(removal.Path);
                RemovePathByHash(mesh.StableHash);
            }
        }
        return true;
    }
}

/// <summary>
/// Retained camera and viewport state.
/// </summary>
public sealed class SilkFrameState
{
    internal const int MaximumLights = 8;
    internal const int MaximumDomes = 8;
    private readonly double[] _view = new double[16];
    private readonly double[] _projection = new double[16];
    private readonly double[] _clipPlanes = new double[32];
    private readonly SilkFrameLight[] _lights = new SilkFrameLight[MaximumLights];
    private readonly SilkFrameDome[] _domes = new SilkFrameDome[MaximumDomes];
    private Vector4 _ambientLight;

    /// <summary>Initializes an identity camera and viewport state.</summary>
    public SilkFrameState()
    {
        for (int i = 0; i < 16; i += 5)
        {
            _view[i] = 1;
            _projection[i] = 1;
        }
    }

    /// <summary>Gets the viewport width.</summary>
    public int Width { get; private set; }

    /// <summary>Gets the viewport height.</summary>
    public int Height { get; private set; }

    /// <summary>Gets the row-major world-to-view matrix.</summary>
    public ReadOnlyMemory<double> View => _view;

    /// <summary>Gets the row-major projection matrix.</summary>
    public ReadOnlyMemory<double> Projection => _projection;

    /// <summary>Gets the number of eye-space clip planes.</summary>
    internal uint ClipPlaneCount { get; private set; }

    /// <summary>Gets the eye-space clip plane table.</summary>
    internal ReadOnlyMemory<double> ClipPlanes => _clipPlanes;

    internal ReadOnlySpan<SilkFrameLight> Lights => _lights;

    /// <summary>
    /// Gets the bounded dome table the page published, which is the ordering a
    /// per-prim dome link mask indexes.
    /// </summary>
    internal ReadOnlySpan<SilkFrameDome> Domes => _domes;

    /// <summary>Gets the number of published dome table entries.</summary>
    internal uint DomeCount { get; private set; }

    internal Vector4 AmbientLight => _ambientLight;

    internal uint LightCount { get; private set; }

    /// <summary>Gets the revision of the retained camera or viewport state.</summary>
    public ulong Revision { get; private set; }

    /// <summary>
    /// Replaces this state with a copy of another, in place and without
    /// allocating.
    /// </summary>
    /// <remarks>
    /// A page is applied as a transaction, and a frame command replaces the whole
    /// retained camera rather than one field of it, so the only way to put a
    /// rejected page's frame back is to have copied the previous one aside. The
    /// copy is written into a state the scene keeps, so a page that is applied
    /// and rolled back a thousand times allocates nothing.
    /// </remarks>
    internal void CopyFrom(SilkFrameState other)
    {
        ArgumentNullException.ThrowIfNull(other);
        Width = other.Width;
        Height = other.Height;
        ClipPlaneCount = other.ClipPlaneCount;
        LightCount = other.LightCount;
        DomeCount = other.DomeCount;
        Revision = other.Revision;
        _ambientLight = other._ambientLight;
        other._view.CopyTo(_view.AsSpan());
        other._projection.CopyTo(_projection.AsSpan());
        other._clipPlanes.CopyTo(_clipPlanes.AsSpan());
        other._lights.CopyTo(_lights.AsSpan());
        other._domes.CopyTo(_domes.AsSpan());
    }

    internal void Update(SilkFrameCommand command)
    {
        bool changed = Width != command.Width ||
            Height != command.Height ||
            ClipPlaneCount != command.ClipPlaneCount ||
            LightCount != command.LightCount;
        Width = command.Width;
        Height = command.Height;
        ClipPlaneCount = command.ClipPlaneCount;
        LightCount = command.LightCount;
        for (int i = 0; i < 16; i++)
        {
            double view = command.GetViewElement(i);
            double projection = command.GetProjectionElement(i);
            changed |= _view[i] != view || _projection[i] != projection;
            _view[i] = view;
            _projection[i] = projection;
        }
        for (int i = 0; i < _clipPlanes.Length; i++)
        {
            double clipPlane = command.GetClipPlaneElement(i / 4, i % 4);
            changed |= _clipPlanes[i] != clipPlane;
            _clipPlanes[i] = clipPlane;
        }
        for (int lightIndex = 0; lightIndex < MaximumLights; lightIndex++)
        {
            SilkFrameLight light = SilkFrameLight.CopyFrom(command, lightIndex);
            changed |= !_lights[lightIndex].Equals(light);
            _lights[lightIndex] = light;
        }
        var ambient = new Vector4(
            command.GetAmbientColor(0),
            command.GetAmbientColor(1),
            command.GetAmbientColor(2),
            command.AmbientIntensity);
        changed |= _ambientLight != ambient;
        _ambientLight = ambient;
        changed |= DomeCount != command.DomeCount;
        DomeCount = command.DomeCount;
        for (int domeIndex = 0; domeIndex < MaximumDomes; domeIndex++)
        {
            SilkFrameDome dome = SilkFrameDome.CopyFrom(command, domeIndex);
            changed |= !_domes[domeIndex].Equals(dome);
            _domes[domeIndex] = dome;
        }
        if (changed)
        {
            Revision++;
        }
    }
}

/// <summary>
/// One entry of the retained frame dome table: what a single dome light
/// contributes on its own, and whether it contributes it as an image.
/// </summary>
/// <param name="AmbientColor">
/// The dome's own summand of the scene-wide ambient term. Zero for a textured
/// dome, whose emission arrives as an environment record instead.
/// </param>
/// <param name="Flags">Whether the entry is published, and whether it is textured.</param>
internal readonly record struct SilkFrameDome(Vector3 AmbientColor, SilkFrameDomeState Flags)
{
    internal static SilkFrameDome CopyFrom(SilkFrameCommand command, int dome) =>
        new(
            new Vector3(
                command.GetDomeAmbientColor(dome, 0),
                command.GetDomeAmbientColor(dome, 1),
                command.GetDomeAmbientColor(dome, 2)),
            command.GetDomeFlags(dome));

    /// <summary>Gets whether the entry names a published dome light.</summary>
    internal bool IsPresent => (Flags & SilkFrameDomeState.Present) != 0;

    /// <summary>Gets whether the dome carries an authored texture.</summary>
    internal bool IsTextured => (Flags & SilkFrameDomeState.Textured) != 0;
}

internal readonly record struct SilkFrameLight(
    uint Type,
    uint ShadowEnabled,
    float ShapeX,
    float ShapeY,
    Vector3 Color,
    float Intensity,
    Matrix4x4 Transform,
    float Exposure,
    float Diffuse,
    float Specular,
    float Radius)
{
    internal static SilkFrameLight CopyFrom(SilkFrameCommand command, int light) =>
        new(
            command.GetLightType(light),
            command.GetLightShadowEnabled(light),
            command.GetLightShapeX(light),
            command.GetLightShapeY(light),
            new Vector3(
                command.GetLightColor(light, 0),
                command.GetLightColor(light, 1),
                command.GetLightColor(light, 2)),
            command.GetLightIntensity(light),
            ReadTransform(command, light),
            command.GetLightExposure(light),
            command.GetLightDiffuse(light),
            command.GetLightSpecular(light),
            command.GetLightRadius(light));

    private static Matrix4x4 ReadTransform(SilkFrameCommand command, int light) =>
        new(
            ToSingle(command.GetLightTransformElement(light, 0)),
            ToSingle(command.GetLightTransformElement(light, 1)),
            ToSingle(command.GetLightTransformElement(light, 2)),
            ToSingle(command.GetLightTransformElement(light, 3)),
            ToSingle(command.GetLightTransformElement(light, 4)),
            ToSingle(command.GetLightTransformElement(light, 5)),
            ToSingle(command.GetLightTransformElement(light, 6)),
            ToSingle(command.GetLightTransformElement(light, 7)),
            ToSingle(command.GetLightTransformElement(light, 8)),
            ToSingle(command.GetLightTransformElement(light, 9)),
            ToSingle(command.GetLightTransformElement(light, 10)),
            ToSingle(command.GetLightTransformElement(light, 11)),
            ToSingle(command.GetLightTransformElement(light, 12)),
            ToSingle(command.GetLightTransformElement(light, 13)),
            ToSingle(command.GetLightTransformElement(light, 14)),
            ToSingle(command.GetLightTransformElement(light, 15)));

    private static float ToSingle(double value) =>
        double.IsFinite(value) ? (float)value : 0;
}

internal static class SilkFrameUniformWriter
{
    internal const int ByteSize = 1856;
    private const int ShadowMatrixOffset = 1056;
    private const int ShadowTileOffset = 1312;
    private const int ShadowControlOffset = 1376;
    private const int ShadowSlotOffset = 1440;
    private const int EnvironmentControlOffset = 1568;
    private const int DomeControlOffset = 1584;
    private const int DomeAmbientOffset = 1600;
    private const int DomeEnvironmentOffset = 1728;

    internal static void Write(
        SilkFrameState frame,
        Span<byte> destination,
        bool flipClipSpaceY,
        RenderOutputTransform outputTransform,
        float exposure,
        Vector3 environmentAmbient = default,
        SilkShadowFrameBinding? shadows = null,
        SilkEnvironmentFrameBinding environment = default,
        SilkDomeAmbientTable domeAmbient = default)
    {
        if (destination.Length != ByteSize)
        {
            throw new ArgumentException(
                $"Frame constants must be exactly {ByteSize} bytes.",
                nameof(destination));
        }

        Span<double> projection = stackalloc double[16];
        ConvertOpenGlDepthToZeroToOne(frame.Projection.Span, projection);
        if (flipClipSpaceY)
        {
            MirrorClipSpaceY(projection);
        }

        Matrix4x4 projected = ToMatrix4x4(projection);
        if (!Matrix4x4.Invert(projected, out Matrix4x4 clipToEye))
        {
            throw new InvalidDataException("The frame projection matrix is not invertible.");
        }

        WriteMatrixTranspose(destination, 0, clipToEye);
        WriteSingle(destination, 64, frame.ClipPlaneCount);
        WriteSingle(destination, 68, (uint)outputTransform);
        WriteSingle(destination, 72, exposure);
        WriteSingle(destination, 76, 0u);

        ReadOnlySpan<double> planes = frame.ClipPlanes.Span;
        for (int i = 0; i < 32; i++)
        {
            float value = ToFiniteSingle(planes[i], $"clipPlanes[{i / 4},{i % 4}]");
            WriteSingle(destination, 80 + (i * sizeof(float)), value);
        }

        Matrix4x4 worldToEye = ToMatrix4x4(frame.View.Span);
        if (!Matrix4x4.Invert(worldToEye, out Matrix4x4 eyeToWorld))
        {
            throw new InvalidDataException("The frame view matrix is not invertible.");
        }

        Vector4 ambient = frame.AmbientLight;

        // hdSilk keeps a textured dome out of its ambient accumulation, because
        // the single ambient colour cannot describe an image. The resolved
        // environment term is added back here, so the constants carry exactly one
        // ambient contribution per dome and a textured dome is never counted both
        // as an image and as an untextured approximation of itself.
        //
        // The aggregate and the per-dome table are accumulated by one function in
        // one order, so a prim linked to every dome and a prim that sums its
        // linked domes cannot disagree by a rounding: they are the same sum of the
        // same summands. That matters exactly where it is least obvious -- a scene
        // that interleaves untextured domes with textured ones that fell back --
        // because there the producer's aggregate and the consumer's fallback sum
        // group their terms differently, and the two groupings are not equal in
        // float.
        Vector3 aggregate = AccumulateDomeAmbient(frame, environmentAmbient, domeAmbient);
        WriteVector4(
            destination,
            208,
            Finite(aggregate.X, "ambient red"),
            Finite(aggregate.Y, "ambient green"),
            Finite(aggregate.Z, "ambient blue"),
            (float)Math.Min(frame.LightCount, (uint)SilkFrameState.MaximumLights));
        ReadOnlySpan<SilkFrameLight> lights = frame.Lights;
        for (int i = 0; i < SilkFrameState.MaximumLights; i++)
        {
            WriteLight(destination, i, lights[i]);
        }
        WriteMatrixTranspose(destination, 992, eyeToWorld);
        WriteShadows(destination, shadows ?? SilkShadowFrameBinding.None);

        // hdSilk sets the ambient intensity to one when the scene authors an
        // *untextured* dome, whatever colour that dome resolves to. That bit has
        // nowhere else to go: the ambient slot's w component is repurposed here as
        // the direct-light count, so without folding it into the environment block
        // it would be discarded, and a black or zero-diffuse untextured dome would
        // acquire a headlight it was never meant to have.
        WriteEnvironment(
            destination,
            environment,
            authoredAmbientDome: ambient.W > 0.5f);
        WriteDomes(destination, frame, environment, domeAmbient);
    }

    /// <summary>
    /// Accumulates the scene-wide ambient term from the same per-dome summands,
    /// in the same order, that the dome table publishes.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The one place either quantity is produced. Summing a prim's linked domes
    /// and reading this aggregate are then the same arithmetic on the same
    /// values, so the two can only agree -- rather than agreeing on the scenes
    /// that happen to make the producer's grouping and the consumer's grouping
    /// coincide.
    /// </para>
    /// <para>
    /// A page that publishes no dome table takes the pre-v21 grouping instead:
    /// the producer's ambient colour plus the consumer's fallback sum. There is
    /// no dome ordering to accumulate in on that page -- no dome holds a bit, and
    /// every prim receives every dome -- and keeping that grouping is what makes
    /// a scene whose domes are not addressable render the bytes it always did.
    /// </para>
    /// <para>
    /// Where a dome table is published, the grouping is preserved wherever it is
    /// observable. A scene of untextured domes adds exact zeros for the fallback
    /// terms, and a scene of textured domes adds exact zeros for the producer's
    /// terms, so both reproduce the pre-v21 value bit for bit; only a scene that
    /// interleaves the two regroups, and that is the scene whose two groupings
    /// were never equal in the first place.
    /// </para>
    /// </remarks>
    private static Vector3 AccumulateDomeAmbient(
        SilkFrameState frame,
        Vector3 environmentAmbient,
        SilkDomeAmbientTable domeAmbient)
    {
        uint domeCount = Math.Min(frame.DomeCount, (uint)SilkFrameState.MaximumDomes);
        if (domeCount == 0)
        {
            // The pre-v21 grouping. environmentAmbient is already the whole
            // fallback sum, unattributed terms included, so nothing is added to
            // it here: every dome is unaddressable on this page by construction.
            Vector4 ambient = frame.AmbientLight;
            return new Vector3(
                ambient.X + environmentAmbient.X,
                ambient.Y + environmentAmbient.Y,
                ambient.Z + environmentAmbient.Z);
        }

        Vector3 total = Vector3.Zero;
        ReadOnlySpan<SilkFrameDome> domes = frame.Domes;
        for (int dome = 0; dome < domeCount; dome++)
        {
            total += ResolveDomeAmbient(domes[dome], domeAmbient, dome);
        }

        // Zero on every page a consumer applies: an environment record whose dome
        // index names no published dome is refused by the page preflight. Added
        // anyway so the writer is total-preserving by construction rather than by
        // an invariant stated somewhere else.
        return total + domeAmbient.Unattributed;
    }

    /// <summary>Resolves what one dome bit contributes on its own.</summary>
    private static Vector3 ResolveDomeAmbient(
        SilkFrameDome dome,
        SilkDomeAmbientTable fallback,
        int index) =>
        dome.IsPresent ? dome.AmbientColor + fallback.GetAmbient(index) : Vector3.Zero;

    /// <summary>
    /// Writes the bounded dome block: how many domes a per-prim mask addresses,
    /// what each contributes on its own, and which environment group each reads.
    /// </summary>
    /// <remarks>
    /// The per-dome ambient here and the scene-wide ambient above are two views of
    /// one quantity, not two quantities: <see cref="AccumulateDomeAmbient"/> sums
    /// exactly these entries in exactly this order. The shader still reads the
    /// aggregate for a fully linked prim rather than re-summing, because a shader
    /// compiler is free to reassociate a loop-carried sum that the CPU is not, and
    /// the two must be equal by construction rather than by agreement.
    /// </remarks>
    private static void WriteDomes(
        Span<byte> destination,
        SilkFrameState frame,
        SilkEnvironmentFrameBinding environment,
        SilkDomeAmbientTable domeAmbient)
    {
        uint domeCount = Math.Min(frame.DomeCount, (uint)SilkFrameState.MaximumDomes);
        WriteVector4(
            destination,
            DomeControlOffset,
            domeCount,
            environment.GroupCount,
            environment.ComposedGroup,
            environment.IrradianceSliceHeight);
        ReadOnlySpan<SilkFrameDome> domes = frame.Domes;
        for (int dome = 0; dome < SilkFrameState.MaximumDomes; dome++)
        {
            bool present = dome < domeCount && domes[dome].IsPresent;
            Vector3 color = present
                ? ResolveDomeAmbient(domes[dome], domeAmbient, dome)
                : Vector3.Zero;
            WriteVector4(
                destination,
                DomeAmbientOffset + (dome * 16),
                Finite(color.X, "dome ambient red"),
                Finite(color.Y, "dome ambient green"),
                Finite(color.Z, "dome ambient blue"),
                present ? 1 : 0);
            WriteVector4(
                destination,
                DomeEnvironmentOffset + (dome * 16),
                present ? environment.DomeGroups.GetGroup(dome) : SilkDomeGroupTable.NoGroup,
                0,
                0,
                0);
        }
    }

    /// <summary>
    /// Writes the prefiltered environment block: whether the two environment maps
    /// are live, and how many prefiltered specular levels they carry.
    /// </summary>
    /// <remarks>
    /// A frame with no textured dome, or one whose every dome fell back to the
    /// mean-radiance ambient term, writes zero here. The checked fragment then
    /// never samples either map, so the block costs the same bytes for every
    /// scene and an unsupported environment costs no shading work at all.
    /// </remarks>
    private static void WriteEnvironment(
        Span<byte> destination,
        SilkEnvironmentFrameBinding environment,
        bool authoredAmbientDome)
    {
        WriteVector4(
            destination,
            EnvironmentControlOffset,
            environment.Enabled ? 1f : 0f,
            environment.SpecularSliceCount,
            environment.SpecularSliceHeight,
            environment.AuthoredSceneLighting || authoredAmbientDome ? 1f : 0f);
    }

    /// <summary>
    /// Writes the resolved shadow block: one light-space clip matrix, atlas tile
    /// and control vector per bound map, and the map slot each direct light reads.
    /// </summary>
    /// <remarks>
    /// A frame that casts no shadow writes zeroed matrices, zeroed tiles, and
    /// <c>-1</c> in every light's slot, so the checked fragment never reaches the
    /// atlas at all and the block costs the same bytes for every scene.
    /// </remarks>
    private static void WriteShadows(
        Span<byte> destination,
        SilkShadowFrameBinding shadows)
    {
        for (int slot = 0; slot < SilkShadowCommand.MaximumMaps; slot++)
        {
            WriteMatrixTranspose(
                destination,
                ShadowMatrixOffset + (slot * 64),
                slot < shadows.Count ? shadows.GetWorldToLightClip(slot) : default);
            Vector4 tile = slot < shadows.Count ? shadows.GetTile(slot) : default;
            WriteVector4(
                destination,
                ShadowTileOffset + (slot * 16),
                Finite(tile.X, "shadow tile offset u"),
                Finite(tile.Y, "shadow tile offset v"),
                Finite(tile.Z, "shadow tile scale u"),
                Finite(tile.W, "shadow tile scale v"));
            Vector4 controls = slot < shadows.Count ? shadows.GetControls(slot) : default;
            WriteVector4(
                destination,
                ShadowControlOffset + (slot * 16),
                Finite(controls.X, "shadow depth bias"),
                Finite(controls.Y, "shadow normal bias"),
                Finite(controls.Z, "shadow filter radius"),
                Finite(controls.W, "shadow texel size"));
        }
        for (int light = 0; light < SilkFrameState.MaximumLights; light++)
        {
            WriteVector4(
                destination,
                ShadowSlotOffset + (light * 16),
                shadows.GetSlotForLight(light),
                0,
                0,
                0);
        }
    }

    private static void WriteLight(
        Span<byte> destination,
        int index,
        SilkFrameLight light)
    {
        int positionOffset = 224 + (index * 16);
        int directionOffset = 352 + (index * 16);
        int colorOffset = 480 + (index * 16);
        int controlOffset = 608 + (index * 16);
        int tangentOffset = 736 + (index * 16);
        int bitangentOffset = 864 + (index * 16);
        if (light.Type == 0)
        {
            WriteVector4(destination, positionOffset, 0, 0, 0, 0);
            WriteVector4(destination, directionOffset, 0, 0, 1, 0);
            WriteVector4(destination, colorOffset, 0, 0, 0, 0);
            WriteVector4(destination, controlOffset, 0, 0, 0, 0);
            WriteVector4(destination, tangentOffset, 1, 0, 0, 0);
            WriteVector4(destination, bitangentOffset, 0, 1, 0, 0);
            return;
        }

        Vector3 position = new(light.Transform.M41, light.Transform.M42, light.Transform.M43);
        Vector3 direction = Vector3.TransformNormal(Vector3.UnitZ, light.Transform);
        direction = Normalize(direction, Vector3.UnitZ);
        Vector3 tangent = Vector3.TransformNormal(Vector3.UnitX, light.Transform);
        tangent = Normalize(tangent, Vector3.UnitX);
        Vector3 bitangent = Vector3.TransformNormal(Vector3.UnitY, light.Transform);
        bitangent = Normalize(bitangent, Vector3.UnitY);
        float exposed = light.Intensity * MathF.Pow(2.0f, light.Exposure);
        WriteVector4(destination, positionOffset, position.X, position.Y, position.Z, light.Type);
        WriteVector4(destination, directionOffset, direction.X, direction.Y, direction.Z, light.Radius);
        WriteVector4(
            destination,
            colorOffset,
            light.Color.X,
            light.Color.Y,
            light.Color.Z,
            exposed);
        WriteVector4(
            destination,
            controlOffset,
            light.Diffuse,
            light.Specular,
            light.ShadowEnabled,
            0);
        WriteVector4(destination, tangentOffset, tangent.X, tangent.Y, tangent.Z, light.ShapeX);
        WriteVector4(destination, bitangentOffset, bitangent.X, bitangent.Y, bitangent.Z, light.ShapeY);
    }

    private static Vector3 Normalize(Vector3 value, Vector3 fallback)
    {
        float lengthSquared = value.LengthSquared();
        if (!float.IsFinite(lengthSquared) || lengthSquared <= float.Epsilon)
        {
            return fallback;
        }
        return value / MathF.Sqrt(lengthSquared);
    }

    private static Matrix4x4 ToMatrix4x4(ReadOnlySpan<double> values) =>
        new(
            ToFiniteSingle(values[0], "projection[0,0]"),
            ToFiniteSingle(values[1], "projection[0,1]"),
            ToFiniteSingle(values[2], "projection[0,2]"),
            ToFiniteSingle(values[3], "projection[0,3]"),
            ToFiniteSingle(values[4], "projection[1,0]"),
            ToFiniteSingle(values[5], "projection[1,1]"),
            ToFiniteSingle(values[6], "projection[1,2]"),
            ToFiniteSingle(values[7], "projection[1,3]"),
            ToFiniteSingle(values[8], "projection[2,0]"),
            ToFiniteSingle(values[9], "projection[2,1]"),
            ToFiniteSingle(values[10], "projection[2,2]"),
            ToFiniteSingle(values[11], "projection[2,3]"),
            ToFiniteSingle(values[12], "projection[3,0]"),
            ToFiniteSingle(values[13], "projection[3,1]"),
            ToFiniteSingle(values[14], "projection[3,2]"),
            ToFiniteSingle(values[15], "projection[3,3]"));

    private static void ConvertOpenGlDepthToZeroToOne(
        ReadOnlySpan<double> source,
        Span<double> destination)
    {
        for (int row = 0; row < 4; row++)
        {
            int offset = row * 4;
            destination[offset] = source[offset];
            destination[offset + 1] = source[offset + 1];
            destination[offset + 2] = (source[offset + 2] + source[offset + 3]) * 0.5;
            destination[offset + 3] = source[offset + 3];
        }
    }

    private static void MirrorClipSpaceY(Span<double> projection)
    {
        for (int row = 0; row < 4; row++)
        {
            projection[(row * 4) + 1] = -projection[(row * 4) + 1];
        }
    }

    private static void WriteMatrixTranspose(
        Span<byte> destination,
        int offset,
        Matrix4x4 matrix)
    {
        Span<float> values =
        [
            matrix.M11, matrix.M12, matrix.M13, matrix.M14,
            matrix.M21, matrix.M22, matrix.M23, matrix.M24,
            matrix.M31, matrix.M32, matrix.M33, matrix.M34,
            matrix.M41, matrix.M42, matrix.M43, matrix.M44
        ];
        for (int row = 0; row < 4; row++)
        {
            for (int column = 0; column < 4; column++)
            {
                WriteSingle(
                    destination,
                    offset + (((row * 4) + column) * sizeof(float)),
                    values[(column * 4) + row]);
            }
        }
    }

    private static float ToFiniteSingle(double value, string name)
    {
        if (!double.IsFinite(value) || value > float.MaxValue || value < -float.MaxValue)
        {
            throw new InvalidDataException($"The frame {name} value is invalid.");
        }
        return (float)value;
    }

    private static float Finite(float value, string name)
    {
        if (!float.IsFinite(value))
        {
            throw new InvalidDataException($"The frame {name} value is invalid.");
        }
        return value;
    }

    private static void WriteSingle(Span<byte> destination, int offset, float value) =>
        BinaryPrimitives.WriteInt32LittleEndian(
            destination.Slice(offset, sizeof(float)),
            BitConverter.SingleToInt32Bits(value));

    private static void WriteVector4(
        Span<byte> destination,
        int offset,
        float x,
        float y,
        float z,
        float w)
    {
        WriteSingle(destination, offset, x);
        WriteSingle(destination, offset + 4, y);
        WriteSingle(destination, offset + 8, z);
        WriteSingle(destination, offset + 12, w);
    }

    private static void WriteSingle(Span<byte> destination, int offset, uint value) =>
        BinaryPrimitives.WriteUInt32LittleEndian(
            destination.Slice(offset, sizeof(uint)),
            value);
}

/// <summary>
/// Retained triangulated mesh data.
/// </summary>
public sealed class SilkMeshData
{
    private readonly float[] _points;
    private readonly uint[] _indices;
    private readonly int[] _triangleSubprims;
    private readonly float[] _displayColor;
    private readonly double[] _transform;
    private readonly float[] _authoredNormals = [];
    private readonly SilkVertexAttributeData[] _attributes = [];
    private readonly int[] _pointOrigins = [];
    private readonly int[] _cornerEdges = [];
    private readonly SilkInstancerContextEntry[]? _instancerContext;
    private readonly SilkSubprimTableCache _subprimTables = new();

    /// <summary>Initializes immutable retained mesh data.</summary>
    public SilkMeshData(
        int primId,
        string path,
        ulong stableHash,
        int instanceId,
        int instanceIndex,
        SilkTopologyKind topologyKind,
        ulong topologyRevision,
        float[] points,
        uint[] indices,
        int[] triangleSubprims,
        float[] displayColor,
        double[] transform,
        bool doubleSided = true,
        SilkMeshCullStyle cullStyle = SilkMeshCullStyle.BackUnlessDoubleSided)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(points);
        ArgumentNullException.ThrowIfNull(indices);
        ArgumentNullException.ThrowIfNull(triangleSubprims);
        ArgumentNullException.ThrowIfNull(displayColor);
        ArgumentNullException.ThrowIfNull(transform);
        if (!Enum.IsDefined(cullStyle))
        {
            throw new ArgumentOutOfRangeException(nameof(cullStyle));
        }
        PrimId = primId;
        Path = path;
        StableHash = stableHash;
        InstanceId = instanceId;
        InstanceIndex = instanceIndex;
        TopologyKind = topologyKind;
        TopologyRevision = topologyRevision;
        DoubleSided = doubleSided;
        CullStyle = cullStyle;
        _points = (float[])points.Clone();
        _indices = (uint[])indices.Clone();
        _triangleSubprims = (int[])triangleSubprims.Clone();
        _displayColor = (float[])displayColor.Clone();
        _transform = (double[])transform.Clone();
        TopologyFingerprint = SilkTopologyFingerprint.Compute(
            TopologyKind,
            _points.Length / 3,
            _indices,
            _triangleSubprims);
    }

    private SilkMeshData(
        int primId,
        string path,
        ulong stableHash,
        int instanceId,
        int instanceIndex,
        SilkTopologyKind topologyKind,
        ulong topologyRevision,
        float[] points,
        uint[] indices,
        int[] triangleSubprims,
        float[] displayColor,
        double[] transform,
        bool doubleSided,
        SilkMeshCullStyle cullStyle,
        ulong topologyFingerprint)
    {
        PrimId = primId;
        Path = path;
        StableHash = stableHash;
        InstanceId = instanceId;
        InstanceIndex = instanceIndex;
        TopologyKind = topologyKind;
        TopologyRevision = topologyRevision;
        DoubleSided = doubleSided;
        CullStyle = cullStyle;
        _points = points;
        _indices = indices;
        _triangleSubprims = triangleSubprims;
        _displayColor = displayColor;
        _transform = transform;
        TopologyFingerprint = topologyFingerprint;
    }

    internal SilkMeshData(
        int primId,
        string path,
        ulong stableHash,
        int instanceId,
        int instanceIndex,
        SilkTopologyKind topologyKind,
        ulong topologyRevision,
        float[] points,
        uint[] indices,
        int[] triangleSubprims,
        float[] displayColor,
        double[] transform,
        ulong topologyFingerprint,
        bool doubleSided,
        SilkMeshCullStyle cullStyle,
        float[] authoredNormals,
        string materialPath)
        : this(
            primId,
            path,
            stableHash,
            instanceId,
            instanceIndex,
            topologyKind,
            topologyRevision,
            points,
            indices,
            triangleSubprims,
            displayColor,
            transform,
            doubleSided,
            cullStyle,
            topologyFingerprint)
    {
        _authoredNormals = authoredNormals;
        MaterialPath = materialPath;
    }

    internal SilkMeshData(
        int primId,
        string path,
        ulong stableHash,
        int instanceId,
        int instanceIndex,
        SilkTopologyKind topologyKind,
        ulong topologyRevision,
        float[] points,
        uint[] indices,
        int[] triangleSubprims,
        float[] displayColor,
        double[] transform,
        ulong topologyFingerprint,
        bool doubleSided,
        SilkMeshCullStyle cullStyle,
        float[] authoredNormals,
        string materialPath,
        SilkVertexAttributeData[] attributes)
        : this(
            primId,
            path,
            stableHash,
            instanceId,
            instanceIndex,
            topologyKind,
            topologyRevision,
            points,
            indices,
            triangleSubprims,
            displayColor,
            transform,
            topologyFingerprint,
            doubleSided,
            cullStyle,
            authoredNormals,
            materialPath)
    {
        _attributes = attributes;
    }

    /// <summary>
    /// Gets every authored vertex attribute the delegate could resolve onto the
    /// emitted vertices, in a stable order.
    /// </summary>
    /// <remarks>
    /// Includes normals, which are also exposed pre-expanded through
    /// <see cref="AuthoredNormals"/> for the vertex builder's hot path. An
    /// attribute absent here was either not authored or authored with an
    /// interpolation the delegate could not resolve; in neither case may a
    /// consumer invent one.
    /// </remarks>
    public IReadOnlyList<SilkVertexAttributeData> Attributes => _attributes;

    /// <summary>
    /// Finds a texture coordinate set by authored primvar name, which is how a
    /// <c>UsdUVTexture</c> reader selects among several sets. Returns null when
    /// the mesh carries no such set.
    /// </summary>
    public SilkVertexAttributeData? FindTexCoord(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        foreach (SilkVertexAttributeData attribute in _attributes)
        {
            if (attribute.Semantic == SilkAttributeSemantic.TexCoord &&
                string.Equals(attribute.Name, name, StringComparison.Ordinal))
            {
                return attribute;
            }
        }
        return null;
    }

    /// <summary>
    /// Gets the authored per-vertex normals, empty when the mesh authored none
    /// that this delegate could resolve onto emitted vertices. When empty the
    /// renderer computes normals from topology as it always has.
    /// </summary>
    public ReadOnlyMemory<float> AuthoredNormals => _authoredNormals;

    /// <summary>
    /// Gets the bounded deformation rig published beside this record's points,
    /// or <see langword="null"/> when the prim published none.
    /// </summary>
    /// <remarks>
    /// The rig never replaces <see cref="Points"/> or
    /// <see cref="AuthoredNormals"/>: hdSilk resolves the supported UsdSkel
    /// subset itself and publishes the result, so a consumer that ignores the
    /// rig draws exactly the same surface. It is carried so a consumer that
    /// wants to evaluate the deformation itself has every input bounded and in
    /// bulk, without a second page or a per-element call.
    /// </remarks>
    public SilkMeshDeformationData? Deformation { get; internal init; }

    /// <summary>
    /// Gets what a deformed prim did not publish a rig for. It is non-zero only
    /// when the prim was deformed and hdSilk refused to describe the rig, which
    /// is a diagnosis rather than a defect: the deformed points travelled
    /// regardless.
    /// </summary>
    public SilkDeformationUnsupportedFeatures DeformationUnsupportedFeatures
    {
        get;
        internal init;
    }

    /// <summary>
    /// Gets the identity of the published rig, or zero when there is none. It
    /// changes with every pose, which is what lets a retained deformation
    /// resource and a retained shadow map be keyed on it.
    /// </summary>
    public ulong DeformationIdentity => Deformation?.Identity ?? 0;

    /// <summary>
    /// Gets the pick targets this record answers with the identity the scene
    /// authored.
    /// </summary>
    /// <remarks>
    /// A cleared flag is a refusal rather than missing data:
    /// <see cref="SubprimUnsupported"/> names why the delegate could not map the
    /// emitted components onto authored ones, and a consumer must refuse the
    /// target rather than returning an emitted index as authored identity.
    /// </remarks>
    public SilkSubprimIdentity SubprimIdentity { get; internal init; }

    /// <summary>Gets why this record refuses an exact subprim pick target.</summary>
    public SilkSubprimUnsupportedReason SubprimUnsupported { get; internal init; }

    /// <summary>
    /// Gets the authored point every emitted vertex came from, or an empty span
    /// when <see cref="SubprimIdentity"/> does not include
    /// <see cref="SilkSubprimIdentity.Point"/>.
    /// </summary>
    /// <remarks>
    /// An entry is -1 for an emitted vertex with no authored origin. The table
    /// is what makes a point pick answer with one authored index even when a
    /// face-varying attribute duplicated that point across every corner it
    /// touches.
    /// </remarks>
    public ReadOnlyMemory<int> PointOrigins
    {
        get => _pointOrigins;
        internal init => _pointOrigins = value.ToArray();
    }

    /// <summary>
    /// Sets the authored point-origin table without copying it, for a
    /// lightweight instance record that shares its prototype's table.
    /// </summary>
    internal int[] PointOriginArray
    {
        get => _pointOrigins;
        private init => _pointOrigins = value;
    }

    /// <summary>
    /// Gets the authored mesh edge every emitted primitive corner spans, or an
    /// empty span when <see cref="SubprimIdentity"/> does not include
    /// <see cref="SilkSubprimIdentity.Edge"/>.
    /// </summary>
    /// <remarks>
    /// Entry 3t+c of a triangle list is the edge from corner c to corner
    /// (c + 1) % 3 of triangle t, and an entry is -1 when that corner is a
    /// triangulation diagonal the scene never authored. A diagonal is therefore
    /// never exposed as an authored edge.
    /// </remarks>
    public ReadOnlyMemory<int> CornerEdges
    {
        get => _cornerEdges;
        internal init => _cornerEdges = value.ToArray();
    }

    /// <summary>
    /// Sets the authored corner-edge table without copying it, for a lightweight
    /// instance record that shares its prototype's table.
    /// </summary>
    internal int[] CornerEdgeArray
    {
        get => _cornerEdges;
        private init => _cornerEdges = value;
    }

    /// <summary>Gets one past the largest authored edge index this record names.</summary>
    public int AuthoredEdgeCount { get; internal init; }

    /// <summary>Gets one past the largest authored point index this record names.</summary>
    public int AuthoredPointCount { get; internal init; }

    /// <summary>
    /// Gets the absolute path of the owning instancer, empty when the prim has
    /// no instancer.
    /// </summary>
    /// <remarks>
    /// This is the authoritative instance identity a pick reports.
    /// <see cref="InstanceId"/> is a hash and cannot be inverted into a path.
    /// </remarks>
    public string InstancerPath { get; internal init; } = string.Empty;

    /// <summary>
    /// Gets the complete ordered instancing chain, outermost level first and
    /// innermost last. Empty when the prim has no instancer.
    /// </summary>
    /// <remarks>
    /// A nested instance has one index per level, and
    /// <see cref="InstanceIndex"/> is a composed ordinal in an hdSilk-private
    /// space rather than any level's own index, so the pair
    /// (<see cref="InstancerPath"/>, <see cref="InstanceIndex"/>) describes a
    /// scene instance only when the chain has exactly one level. This chain is
    /// the authoritative description for every depth.
    /// </remarks>
    public IReadOnlyList<SilkInstancerContextEntry> InstancerContext
    {
        get => _instancerContext ?? [];
        internal init => _instancerContext = value is null or { Count: 0 }
            ? null
            : [.. value];
    }

    /// <summary>Gets the bound material path, empty when the mesh has none.</summary>
    public string MaterialPath { get; } = string.Empty;

    /// <summary>
    /// Initializes non-instanced triangle data for callers that do not retain wire identity.
    /// </summary>
    public SilkMeshData(
        ulong primId,
        string path,
        float[] points,
        uint[] indices,
        float[] displayColor,
        double[] transform)
        : this(
            checked((int)primId),
            path,
            SilkWireFormat.ComputeStableHash(path),
            0,
            0,
            SilkTopologyKind.TriangleList,
            1,
            points,
            indices,
            new int[indices.Length / 3],
            displayColor,
            transform)
    {
    }

    /// <summary>Gets Hydra's explicit Rprim identifier.</summary>
    public int PrimId { get; }

    /// <summary>
    /// Gets the retained resource key. A prim with no instancer keeps Hydra's
    /// explicit prim ID so existing identities are unchanged. Point-instanced
    /// records past instance zero pack the prim ID and instance ordinal behind
    /// a high marker bit, because every instance of a prototype shares one prim
    /// ID and would otherwise collide.
    /// </summary>
    public ulong Id => InstanceIndex == 0
        ? checked((ulong)PrimId)
        : (1UL << 63) |
            ((ulong)checked((uint)PrimId) << 32) |
            checked((uint)InstanceIndex);

    /// <summary>Gets the authoritative USD prim path.</summary>
    public string Path { get; }

    /// <summary>Gets the collision-checked FNV-1a path hash index.</summary>
    public ulong StableHash { get; }

    /// <summary>
    /// Gets the stable, diagnostic-only identifier of the owning instancer, or
    /// zero when the prim is not instanced.
    /// </summary>
    public int InstanceId { get; }

    /// <summary>
    /// Gets the instance's own index inside its owning instancer, which is the
    /// index UsdImaging decodes back to a scene instance. A prototype that
    /// covers only part of an instancer therefore publishes a sparse set of
    /// indices rather than a dense zero-based range. A prim with no instancer
    /// always reports zero.
    /// </summary>
    public int InstanceIndex { get; }

    /// <summary>Gets the emitted topology kind.</summary>
    public SilkTopologyKind TopologyKind { get; }

    /// <summary>Gets the topology-only mesh revision.</summary>
    public ulong TopologyRevision { get; }

    /// <summary>Gets whether Hydra resolved the mesh as double-sided.</summary>
    public bool DoubleSided { get; }

    /// <summary>Gets Hydra's resolved cull style for this mesh.</summary>
    public SilkMeshCullStyle CullStyle { get; }

    /// <summary>Gets a deterministic defensive 64-bit topology fingerprint.</summary>
    /// <remarks>
    /// The topology revision is authoritative. This non-cryptographic value
    /// detects accidental same-revision conflicts in constant time, but a
    /// deliberate or extremely unlikely 64-bit collision can evade that
    /// defensive check.
    /// </remarks>
    public ulong TopologyFingerprint { get; }

    /// <summary>Gets a defensive read-only view of point components.</summary>
    public ReadOnlyMemory<float> Points => _points;

    /// <summary>Gets the retained point components without copying them.</summary>
    internal ReadOnlySpan<float> GetPointSpan() => _points;

    /// <summary>
    /// Produces the same retained mesh with externally simulated point positions.
    /// </summary>
    /// <param name="points">The simulated point components, three per vertex.</param>
    /// <returns>A new immutable mesh carrying the simulated points and no authored normals.</returns>
    /// <remarks>
    /// <para>
    /// The topology fingerprint is deliberately carried over rather than recomputed: it describes
    /// the element connectivity, which a deformation never changes. Recomputing it would be
    /// harmless but would hide that a deformed mesh is the same mesh, and the geometry cache
    /// already separates deformed geometry through its own point fingerprint.
    /// </para>
    /// <para>
    /// Authored normals are dropped, because they describe the rest pose. Carrying them over shades
    /// a bent cloth with the normals of the flat one it used to be, which is a lighting error no
    /// amount of correct vertex positions can undo. An empty set is exactly what makes
    /// <c>SilkMeshGeometryBuilder</c> recompute normals from the simulated topology, so a deformed
    /// mesh follows the same path a mesh that authored no normals has always taken. The authored
    /// normals are not lost: the scene retains the whole authored mesh for restoration.
    /// </para>
    /// </remarks>
    internal SilkMeshData WithSimulatedPoints(ReadOnlySpan<float> points) =>
        new(
            PrimId,
            Path,
            StableHash,
            InstanceId,
            InstanceIndex,
            TopologyKind,
            TopologyRevision,
            points.ToArray(),
            _indices,
            _triangleSubprims,
            _displayColor,
            _transform,
            TopologyFingerprint,
            DoubleSided,
            CullStyle,
            authoredNormals: [],
            MaterialPath,
            _attributes)
        {
            // Simulation replaces the point positions, not the topology, so
            // every emitted vertex still came from the same authored point and
            // every corner still spans the same authored edge.
            SubprimIdentity = SubprimIdentity,
            SubprimUnsupported = SubprimUnsupported,
            PointOrigins = _pointOrigins,
            CornerEdges = _cornerEdges,
            AuthoredEdgeCount = AuthoredEdgeCount,
            AuthoredPointCount = AuthoredPointCount,
            InstancerPath = InstancerPath,
            InstancerContext = InstancerContext
        };

    /// <summary>Gets a defensive read-only view of triangle indices.</summary>
    public ReadOnlyMemory<uint> Indices => _indices;

    /// <summary>Gets one authored USD face/subprim per emitted triangle.</summary>
    public ReadOnlyMemory<int> TriangleSubprims => _triangleSubprims;

    /// <summary>Gets the canonical display color.</summary>
    public ReadOnlyMemory<float> DisplayColor => _displayColor;

    /// <summary>Gets the row-major local-to-world transform.</summary>
    public ReadOnlyMemory<double> Transform => _transform;

    /// <summary>Gets the emitted triangle count.</summary>
    public int TriangleCount => _triangleSubprims.Length;

    /// <summary>
    /// Gets the derivation cache one prototype shares with every lightweight
    /// instance record of it.
    /// </summary>
    /// <remarks>
    /// A lightweight instance reuses the prototype's emitted topology and its
    /// ABI v22 identity tables verbatim, so the edge and point draw tables
    /// derived from them are the same tables. Sharing the cache, rather than the
    /// derived result, keeps the derivation lazy while still paying for it once
    /// per prototype instead of once per instance.
    /// </remarks>
    internal SilkSubprimTableCache SubprimTableCache
    {
        get => _subprimTables;
        private init => _subprimTables = value;
    }

    /// <summary>Gets the shared derived edge and point draw tables.</summary>
    internal SilkSubprimTables SubprimTables => _subprimTables.Resolve(this);

    /// <summary>Gets the retained emitted-primitive subprim table without copying it.</summary>
    internal int[] TriangleSubprimArray => _triangleSubprims;

    /// <summary>Gets the retained instancing chain without copying it.</summary>
    internal SilkInstancerContextEntry[]? InstancerContextArray => _instancerContext;

    internal static SilkMeshData CopyFrom(SilkMeshUpsertCommand command)
    {
        if (command.IsInstanceReference)
        {
            throw new InvalidDataException(
                "A lightweight mesh instance requires prototype geometry.");
        }

        var points = new float[command.PointCount * 3];
        for (int point = 0; point < command.PointCount; point++)
        {
            for (int component = 0; component < 3; component++)
            {
                points[(point * 3) + component] = command.GetPointComponent(point, component);
            }
        }

        var indices = new uint[command.IndexCount];
        var fingerprint = new SilkTopologyFingerprintBuilder(
            command.TopologyKind,
            command.PointCount,
            command.IndexCount,
            command.TriangleCount);
        for (int i = 0; i < indices.Length; i++)
        {
            uint index = command.GetIndex(i);
            indices[i] = index;
            fingerprint.AddIndex(index);
        }

        var triangleSubprims = new int[command.TriangleCount];
        for (int triangle = 0; triangle < triangleSubprims.Length; triangle++)
        {
            int subprim = command.GetTriangleSubprim(triangle);
            triangleSubprims[triangle] = subprim;
            fingerprint.AddSubprim(subprim);
        }

        var color = new float[4];
        for (int i = 0; i < color.Length; i++)
        {
            color[i] = command.GetDisplayColor(i);
        }

        var transform = new double[16];
        for (int i = 0; i < transform.Length; i++)
        {
            transform[i] = command.GetTransformElement(i);
        }

        int[] pointOrigins = command.PointOriginCount == 0
            ? []
            : new int[command.PointOriginCount];
        for (int index = 0; index < pointOrigins.Length; index++)
        {
            pointOrigins[index] = command.GetPointOrigin(index);
        }

        int[] cornerEdges = command.CornerEdgeCount == 0
            ? []
            : new int[command.CornerEdgeCount];
        for (int index = 0; index < cornerEdges.Length; index++)
        {
            cornerEdges[index] = command.GetCornerEdge(index);
        }

        float[] authoredNormals = [];
        SilkVertexAttributeData[] attributes = command.AttributeCount == 0
            ? []
            : new SilkVertexAttributeData[command.AttributeCount];
        for (int index = 0; index < command.AttributeCount; index++)
        {
            SilkMeshAttributeEntry attribute = command.GetAttribute(index);
            float[] data = new float[attribute.ElementCount * attribute.ComponentCount];
            for (int element = 0; element < attribute.ElementCount; element++)
            {
                for (int component = 0; component < attribute.ComponentCount; component++)
                {
                    data[(element * attribute.ComponentCount) + component] =
                        attribute.GetComponent(element, component);
                }
            }
            attributes[index] = new SilkVertexAttributeData(
                attribute.Name,
                attribute.Semantic,
                attribute.Interpolation,
                attribute.ComponentCount,
                data);

            if (attribute.Semantic != SilkAttributeSemantic.Normal ||
                attribute.ComponentCount != 3 ||
                authoredNormals.Length != 0)
            {
                continue;
            }
            // A constant normal is expanded here so the vertex builder only ever
            // sees one shape, and so the GPU layout stays identical either way.
            authoredNormals = new float[command.PointCount * 3];
            bool constant = attribute.Interpolation == SilkAttributeInterpolation.Constant;
            for (int point = 0; point < command.PointCount; point++)
            {
                int element = constant ? 0 : point;
                for (int component = 0; component < 3; component++)
                {
                    authoredNormals[(point * 3) + component] =
                        attribute.GetComponent(element, component);
                }
            }
        }

        return new SilkMeshData(
            command.PrimId,
            command.Path,
            command.StableHash,
            command.InstanceId,
            command.InstanceIndex,
            command.TopologyKind,
            command.TopologyRevision,
            points,
            indices,
            triangleSubprims,
            color,
            transform,
            fingerprint.Value,
            command.DoubleSided,
            command.CullStyle,
            authoredNormals,
            command.MaterialPath,
            attributes)
        {
            Deformation = command.CopyDeformation(),
            DeformationUnsupportedFeatures = command.DeformationUnsupportedFeatures,
            SubprimIdentity = command.SubprimIdentity,
            SubprimUnsupported = command.SubprimUnsupported,
            PointOrigins = pointOrigins,
            CornerEdges = cornerEdges,
            AuthoredEdgeCount = command.AuthoredEdgeCount,
            AuthoredPointCount = command.AuthoredPointCount,
            InstancerPath = command.InstancerPath,
            InstancerContext = [.. command.InstancerContext]
        };
    }

    internal static SilkMeshData CopyInstanceFrom(
        SilkMeshUpsertCommand command,
        SilkMeshData prototype)
    {
        ArgumentNullException.ThrowIfNull(prototype);
        if (!command.IsInstanceReference)
        {
            throw new ArgumentException(
                "The mesh command is not a lightweight instance reference.",
                nameof(command));
        }
        if (!string.Equals(command.Path, prototype.Path, StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                "A lightweight mesh instance must reference its prototype path.");
        }
        if (command.TopologyKind != prototype.TopologyKind ||
            command.TopologyRevision != prototype.TopologyRevision)
        {
            throw new InvalidDataException(
                "A lightweight mesh instance does not match its prototype topology.");
        }

        var color = new float[4];
        for (int i = 0; i < color.Length; i++)
        {
            color[i] = command.GetDisplayColor(i);
        }

        var transform = new double[16];
        for (int i = 0; i < transform.Length; i++)
        {
            transform[i] = command.GetTransformElement(i);
        }

        return new SilkMeshData(
            command.PrimId,
            command.Path,
            command.StableHash,
            command.InstanceId,
            command.InstanceIndex,
            prototype.TopologyKind,
            prototype.TopologyRevision,
            // Every prototype payload array is shared, never copied. A retained
            // mesh is immutable and only ever hands out read-only views of these
            // arrays, so sharing is safe -- and copying them made a prototype
            // with a million points cost a million floats per instance, which is
            // the O(points x instances) growth an instance reference exists to
            // avoid in the first place.
            prototype._points,
            prototype._indices,
            prototype._triangleSubprims,
            color,
            transform,
            prototype.TopologyFingerprint,
            command.DoubleSided,
            command.CullStyle,
            prototype._authoredNormals,
            prototype.MaterialPath,
            prototype._attributes)
        {
            // The rig belongs to the prototype, so every instance of it reuses
            // the same one exactly as it reuses the same geometry.
            Deformation = prototype.Deformation,
            DeformationUnsupportedFeatures = prototype.DeformationUnsupportedFeatures,
            // Subprim identity is a property of the prototype's authored
            // topology, so an instance composes the prototype's authored face,
            // edge and point identity with its own instance index rather than
            // publishing a second, possibly disagreeing table.
            SubprimIdentity = prototype.SubprimIdentity,
            SubprimUnsupported = prototype.SubprimUnsupported,
            PointOriginArray = prototype._pointOrigins,
            CornerEdgeArray = prototype._cornerEdges,
            AuthoredEdgeCount = prototype.AuthoredEdgeCount,
            AuthoredPointCount = prototype.AuthoredPointCount,
            // The derived edge and point draw tables follow the shared identity
            // tables, so the whole prototype family derives them at most once.
            SubprimTableCache = prototype.SubprimTableCache,
            // The instancer path and the ordered chain are this record's own
            // instance identity rather than the prototype payload's, so they
            // come from the command. Two instances of one prototype differ in
            // exactly this: the prototype's geometry is shared and its place in
            // the instancing hierarchy is not.
            InstancerPath = command.InstancerPath,
            InstancerContext = [.. command.InstancerContext]
        };
    }
}

internal static class SilkTopologyFingerprint
{
    internal static ulong Compute(
        SilkTopologyKind topologyKind,
        int pointCount,
        ReadOnlySpan<uint> indices,
        ReadOnlySpan<int> triangleSubprims)
    {
        var builder = new SilkTopologyFingerprintBuilder(
            topologyKind,
            pointCount,
            indices.Length,
            triangleSubprims.Length);
        foreach (uint index in indices)
        {
            builder.AddIndex(index);
        }
        foreach (int subprim in triangleSubprims)
        {
            builder.AddSubprim(subprim);
        }
        return builder.Value;
    }
}

internal struct SilkTopologyFingerprintBuilder
{
    private const ulong OffsetBasis = 14695981039346656037;
    private const ulong Prime = 1099511628211;
    private ulong _value;

    internal SilkTopologyFingerprintBuilder(
        SilkTopologyKind topologyKind,
        int pointCount,
        int indexCount,
        int triangleCount)
    {
        if (!Enum.IsDefined(topologyKind))
        {
            throw new ArgumentOutOfRangeException(nameof(topologyKind));
        }
        ArgumentOutOfRangeException.ThrowIfNegative(pointCount);
        ArgumentOutOfRangeException.ThrowIfNegative(indexCount);
        ArgumentOutOfRangeException.ThrowIfNegative(triangleCount);
        _value = OffsetBasis;
        AddUInt32(0x53494C4B);
        AddUInt32((uint)topologyKind);
        AddUInt32(checked((uint)pointCount));
        AddUInt32(checked((uint)indexCount));
        AddUInt32(checked((uint)triangleCount));
    }

    internal readonly ulong Value => _value;

    internal void AddIndex(uint index) => AddUInt32(index);

    internal void AddSubprim(int subprim)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(subprim);
        AddUInt32(checked((uint)subprim));
    }

    private void AddUInt32(uint value)
    {
        unchecked
        {
            _value = (_value ^ (byte)value) * Prime;
            _value = (_value ^ (byte)(value >> 8)) * Prime;
            _value = (_value ^ (byte)(value >> 16)) * Prime;
            _value = (_value ^ (byte)(value >> 24)) * Prime;
        }
    }
}

/// <summary>
/// Counts resource changes applied from one command page.
/// </summary>
public readonly record struct SilkSceneDelta(
    ReadOnlyMemory<ulong> UpsertedMeshIds,
    ReadOnlyMemory<ulong> RemovedMeshIds)
{
    internal SilkSceneDelta(
        ReadOnlyMemory<ulong> upsertedMeshIds,
        ReadOnlyMemory<ulong> removedMeshIds,
        ReadOnlyMemory<string> changedMaterialPaths)
        : this(upsertedMeshIds, removedMeshIds)
    {
        ChangedMaterialPaths = changedMaterialPaths;
    }

    /// <summary>Gets the number of created or updated meshes.</summary>
    public int MeshUpserts => UpsertedMeshIds.Length;

    /// <summary>Gets the number of removed meshes.</summary>
    public int MeshRemovals => RemovedMeshIds.Length;

    internal ReadOnlyMemory<string> ChangedMaterialPaths { get; }

    /// <summary>Gets the number of changed material records.</summary>
    internal int MaterialChanges => ChangedMaterialPaths.Length;
}

/// <summary>
/// Owns backend buffers corresponding to retained hdSilk mesh resources.
/// </summary>
public sealed class SilkSceneGpuResources : IDisposable
{
    private const int DiagnosticCapacity = 128;
    private const int MaximumUdimAtlasCells = 256;

    // A bounded default keeps ordinary material texture anisotropy from exceeding a
    // reasonable, well-supported level even on devices that advertise a much higher ceiling.
    private const float MaxMaterialAnisotropy = 8f;

    private readonly ISilkGraphicsDevice _device;
    private readonly Func<string, bool, SilkDecodedImage> _imageDecoder;
    private readonly Func<string, SilkImageDescription> _imageDescriber;
    private readonly Func<string, IReadOnlyList<SilkUdimTile>> _udimResolver;
    private readonly Dictionary<ulong, SilkMeshGpuResource> _meshes = [];
    private ulong _deformationDeviceGeneration;
    private ulong _deformationDispatches;
    private ulong _deformationFallbacks;
    private bool _deformationDisabled;
    private Func<Exception>? _deformationSetupFailureForTesting;
    private Func<Exception>? _deformationDispatchFailureForTesting;
    private readonly Dictionary<SilkMeshGpuGeometryKey, List<SilkMeshGpuGeometryResource>> _geometries =
        [];
    private readonly Dictionary<SurfaceBufferKey, SurfaceBuffer> _surfaceBuffers = [];
    // The packed masks the retained link table can still return, rebuilt whenever
    // its revision moves. It is the set a cached per-mask surface block has to be
    // in to survive: a live-edited collection walks through many masks, and
    // nothing else ever drops the blocks it leaves behind.
    private readonly HashSet<uint> _liveLinkMasks = [];
    private ulong _surfaceLinkRevision = ulong.MaxValue;
    private readonly Dictionary<TextureCacheKey, TextureCacheEntry> _textures = [];
    private readonly Dictionary<TextureCacheKey, TextureCacheEntry> _failedTextures = [];
    private readonly Dictionary<string, TextureCacheEntry> _volumeTextures =
        new(StringComparer.Ordinal);
    // Decoded displacement height fields, keyed by the identity that also keys the
    // retained geometry they displaced. Bounded by MaximumDisplacementImageBytes
    // and released with the resource.
    private readonly Dictionary<ulong, DisplacementCacheEntry> _displacementImages = [];
    private readonly Dictionary<DisplacedPrimKey, DisplacementVerdict> _displacementVerdicts =
        [];
    private ulong _displacementImageBytes;
    private ulong _displacementUseClock;
    private ulong _displacementResolves;
    private ulong _displacementSampledPoints;
    private ulong _displacementImageDecodes;
    private ulong _geometryCacheHits;
    private ulong _deformationDisplacementFallbacks;
    private ulong _displacementVerdictRevision;
    private ulong _displacementReportedRevision = ulong.MaxValue;
    private ulong _displacementReportedShadowRevision = ulong.MaxValue;
    private int _maximumDisplacedPoints = SilkDisplacementField.MaximumDisplacedPoints;
    private int _maximumDisplacementTexels = SilkDisplacementField.MaximumImageTexels;
    private readonly Dictionary<SilkSamplerDescriptor, ISilkGraphicsSampler> _samplers = [];
    private readonly Dictionary<string, RenderDiagnostic> _diagnostics =
        new(StringComparer.Ordinal);
    private ISilkGraphicsBuffer? _frameBuffer;
    private readonly byte[] _frameBytes = new byte[SilkFrameUniformWriter.ByteSize];
    private ulong _frameRevision = ulong.MaxValue;
    private ulong _frameEnvironmentRevision = ulong.MaxValue;
    private ulong _frameEnvironmentBindingRevision = ulong.MaxValue;
    private ulong _shadowDiagnosticRevision = ulong.MaxValue;
    private ulong _shadowDiagnosticTableRevision = ulong.MaxValue;
    private ulong _shadowCasterDiagnosticRevision = ulong.MaxValue;
    private ulong _frameShadowRevision = ulong.MaxValue;
    private RenderOutputTransform _frameOutputTransform = (RenderOutputTransform)(-1);
    private float _frameExposure = float.NaN;
    private readonly SilkEnvironmentMeanRadianceCache _environmentMeanRadiance;
    private readonly SilkEnvironmentLightingCache _environmentLighting;
    private readonly SilkEnvironmentPrefilterOptions _environmentPrefilterOptions;
    private readonly Func<string, SilkEnvironmentAssetStamp> _environmentStampReader;
    private readonly string _environmentIdentityContext;
    private static long _environmentContextSequence;
    private readonly bool _environmentDescriberAvailable;
    private readonly Dictionary<
        (string Asset, SilkEnvironmentAssetStamp Stamp),
        SilkImageDescription?> _environmentDescriptions = [];
    private SilkEnvironmentMaps? _environmentPayload;
    private readonly HashSet<string> _environmentLitDomes = new(StringComparer.Ordinal);

    /// <summary>
    /// The domes whose source the prefilter could not read on the last resolve.
    /// </summary>
    /// <remarks>
    /// A dome lands here only after it was accepted as a candidate and then failed
    /// inside the source stream, so it excludes the domes refused for a reason
    /// that is a property of the scene -- an unsupported mapping, an authored
    /// control -- which would fail identically on every retry.
    /// </remarks>
    private readonly HashSet<string> _environmentPrefilterSkipped =
        new(StringComparer.Ordinal);

    /// <summary>
    /// The prefilter failures already retried once, keyed by dome and asset state.
    /// </summary>
    /// <remarks>
    /// Bounds the retry. A source the prefilter refuses and the fallback reads is
    /// a contradiction worth resolving once; a source that reproduces the
    /// contradiction against the same bytes is a real disagreement between the two
    /// paths, and retrying it every frame would decode the image forever.
    /// </remarks>
    private readonly HashSet<string> _environmentPrefilterRetried =
        new(StringComparer.Ordinal);

    private ISilkGraphicsTexture? _environmentIrradianceTexture;
    private ISilkGraphicsTexture? _environmentSpecularTexture;
    private ISilkGraphicsTexture? _environmentBrdfTexture;
    private ISilkGraphicsTexture? _environmentStandIn;
    private ISilkGraphicsSampler? _environmentSampler;
    private ISilkGraphicsSampler? _environmentBrdfSampler;
    private SilkEnvironmentFrameBinding _environmentBinding = SilkEnvironmentFrameBinding.None;
    private ulong _environmentBindingRevision;
    private string? _environmentUploadedIdentity;
    private string _environmentAssetRevision = string.Empty;
    private bool _environmentMapsUploaded;
    private bool _environmentBrdfUploaded;
    // Recorded but not yet known to have executed. A submission that fails, or a
    // command list that is abandoned, resets these rather than the committed
    // flags above, so the next frame records the copy again.
    private bool _environmentMapsUploadPending;
    private bool _environmentBrdfUploadPending;
    private ulong _environmentPendingUploadBytes;
    private bool _environmentAuthoredSceneLighting;
    private ulong _environmentLightingRevision = ulong.MaxValue;
    private ulong _environmentLightingDeviceGeneration = ulong.MaxValue;
    private ulong _environmentUploadBytes;
    private Vector3 _environmentAmbient;
    private SilkDomeAmbientTable _environmentDomeAmbient;
    private bool _environmentPerDomeGroups;
    private ulong _environmentRevision;
    private string _environmentFallbackRevision = string.Empty;
    private ulong _environmentAmbientRevision;
    private ulong _frameEnvironmentAmbientRevision = ulong.MaxValue;
    private bool _environmentsResolved;
    private readonly SilkTextureResidencyOptions _residencyOptions;
    private ulong _textureUseClock;
    private ulong _textureEntrySequence;
    // The _textureUseClock value as of the end of the previous TrimTextureResidency call. Entries
    // last used at or before this boundary were not touched during the frame(s) recorded since
    // that trim and are the only eviction candidates the next trim may consider; this is what
    // protects the current-frame working set from decode/upload thrash. See TrimTextureResidency.
    private ulong _textureUseClockBoundary;
    private ulong _decodedTextureResidentBytes;
    private ulong _gpuTextureResidentBytes;
    private ulong _peakDecodedTextureResidentBytes;
    private ulong _peakGpuTextureResidentBytes;
    private ulong _textureEvictionCount;
    private bool _disposed;

    private readonly record struct SurfaceBuffer(
        ISilkGraphicsBuffer? Buffer,
        ulong MaterialHash);

    /// <summary>
    /// Identity of one packed surface constant block.
    /// </summary>
    /// <remarks>
    /// The block is a property of the material, so it is shared by every prim
    /// bound to that material -- except for the light-link masks, which are a
    /// property of the prim. Keying by both keeps the shared case exactly as it
    /// was, because a scene with no authored linking resolves every prim to the
    /// same masks and therefore to a single block per material, while a linked
    /// scene allocates one block per distinct mask the material is drawn with.
    /// The material path is empty for the shared default block.
    /// </remarks>
    private readonly record struct SurfaceBufferKey(string MaterialPath, uint Masks);

    /// <summary>
    /// A retained texture and its decoded-CPU/GPU-resident accounting. <see cref="Pixels"/> stays
    /// retained for the lifetime of the entry — it is only ever released by disposing the entry
    /// itself through safe LRU eviction (<see cref="TrimTextureResidency"/>) — so the decoded CPU
    /// byte budget in <see cref="SilkTextureResidencyOptions"/> is a real, independently
    /// enforceable budget rather than one that only ever measures a near-zero residency between
    /// draws.
    /// </summary>
    private sealed class TextureCacheEntry(
        ISilkGraphicsTexture texture,
        byte[] pixels,
        ulong gpuBytes,
        bool isUdim = false,
        TextureDependency[]? dependencies = null)
    {
        internal ISilkGraphicsTexture Texture { get; } = texture;

        internal byte[] Pixels { get; } = pixels;

        internal ulong GpuBytes { get; } = gpuBytes;

        internal bool IsUdim { get; } = isUdim;

        internal TextureDependency[]? Dependencies { get; } = dependencies;

        internal bool Uploaded { get; set; }

        /// <summary>Gets the decoded CPU byte count retained by <see cref="Pixels"/>.</summary>
        internal ulong DecodedBytes => checked((ulong)Pixels.Length);

        /// <summary>Gets or sets the monotonically increasing stamp used for LRU ordering.</summary>
        internal ulong LastUsedStamp { get; set; }

        /// <summary>Gets the creation-order number used as a stable LRU tie-breaker.</summary>
        internal ulong SequenceId { get; set; }
    }

    private enum TextureCacheEntryKind
    {
        Ordinary,
        Failed,
        Volume,
    }

    /// <summary>
    /// Identifies one eviction candidate found by <see cref="TryFindStaleEvictionCandidate"/>.
    /// Exactly one of <see cref="OrdinaryKey"/> (for <see cref="TextureCacheEntryKind.Ordinary"/>
    /// or <see cref="TextureCacheEntryKind.Failed"/>) or <see cref="VolumeKey"/> (for
    /// <see cref="TextureCacheEntryKind.Volume"/>) identifies the owning dictionary's key; the
    /// other is unused for that <see cref="Kind"/> and must not be read.
    /// </summary>
    private readonly record struct EvictionCandidate(
        TextureCacheEntryKind Kind,
        TextureCacheKey OrdinaryKey,
        string? VolumeKey,
        TextureCacheEntry Entry);

    private readonly record struct TextureDependency(
        string Asset,
        long Length,
        DateTime LastWriteTimeUtc);

    private readonly record struct TextureCacheKey(
        string MaterialPath,
        string Asset,
        SilkColorSpace ColorSpace,
        SilkMaterialParameter Parameter,
        SilkTextureChannel Channel,
        SilkCompositeOperator CompositeOperator);

    /// <summary>Initializes GPU resource retention for one backend device.</summary>
    public SilkSceneGpuResources(ISilkGraphicsDevice device)
        : this(
            device,
            SilkNativeImageDecoder.Decode,
            SilkNativeImageDecoder.ResolveUdimTiles,
            residencyOptions: null,
            imageDescriber: SilkNativeImageDecoder.Describe)
    {
    }

    /// <summary>
    /// Initializes GPU resource retention for one backend device, with explicit decoded CPU and
    /// estimated GPU texture cache residency budgets.
    /// </summary>
    /// <param name="device">The backend graphics device.</param>
    /// <param name="residencyOptions">
    /// The decoded CPU and estimated GPU texture cache residency budgets enforced by
    /// <see cref="TrimTextureResidency"/>.
    /// </param>
    public SilkSceneGpuResources(ISilkGraphicsDevice device, SilkTextureResidencyOptions residencyOptions)
        : this(
            device,
            SilkNativeImageDecoder.Decode,
            SilkNativeImageDecoder.ResolveUdimTiles,
            RequireResidencyOptions(residencyOptions),
            imageDescriber: SilkNativeImageDecoder.Describe)
    {
    }

    private static SilkTextureResidencyOptions RequireResidencyOptions(
        SilkTextureResidencyOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return options;
    }

    /// <summary>
    /// Derives the prefilter's source budgets from the one decoded-image ceiling a
    /// caller configures.
    /// </summary>
    /// <remarks>
    /// The prefiltered path and the mean-radiance fallback have to accept the same
    /// images. Two independently configured per-image ceilings would let a dome be
    /// small enough to prefilter and too large to fall back to, or the reverse --
    /// and both are states with no correct behaviour, because the fallback is what
    /// the prefilter degrades to. So the per-image ceiling is the decode budget
    /// itself.
    /// <para>
    /// The aggregate ceiling is <em>not</em> derived from it. Deriving it as
    /// "per-image times the dome bound" made it a restatement of the per-image
    /// rule rather than a second bound: it could never refuse a set whose members
    /// each fit, which is exactly the case it exists to refuse. It is the smaller
    /// of its own stated default and that product, so raising the per-image
    /// ceiling cannot raise the total this renderer will decode past the total it
    /// declares, and lowering the per-image ceiling still lowers the total.
    /// </para>
    /// </remarks>
    private static SilkEnvironmentPrefilterOptions DeriveEnvironmentOptions(
        ulong decodeByteBudget)
    {
        ulong product = decodeByteBudget <=
            ulong.MaxValue / (ulong)SilkEnvironmentPrefilterOptions.DefaultMaximumDomeLights
            ? decodeByteBudget *
                (ulong)SilkEnvironmentPrefilterOptions.DefaultMaximumDomeLights
            : ulong.MaxValue;
        return SilkEnvironmentPrefilterOptions.Default with
        {
            MaximumSourceBytes = decodeByteBudget,
            MaximumAggregateSourceBytes = Math.Min(
                SilkEnvironmentPrefilterOptions.DefaultMaximumAggregateSourceBytes,
                product),
        };
    }

    internal SilkSceneGpuResources(
        ISilkGraphicsDevice device,
        Func<string, bool, SilkDecodedImage> imageDecoder,
        Func<string, IReadOnlyList<SilkUdimTile>>? udimResolver = null,
        SilkTextureResidencyOptions? residencyOptions = null,
        ulong environmentDecodeByteBudget =
            SilkEnvironmentMeanRadianceCache.DefaultDecodeByteBudget,
        Func<string, SilkImageDescription>? imageDescriber = null,
        SilkEnvironmentPrefilterOptions? environmentPrefilterOptions = null,
        Func<string, SilkEnvironmentAssetStamp>? environmentStampReader = null)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(imageDecoder);
        _device = device;
        _imageDecoder = imageDecoder;
        // A describer reads an image's declared shape without decoding it, which
        // is what lets the displacement budgets be enforced before an allocation.
        // A caller that supplied its own decoder but no describer gets one backed
        // by that decoder: it is the only honest shape for those pixels, and a
        // case that needs the preflight measured on its own supplies both.
        _imageDescriber = imageDescriber ?? (asset =>
        {
            SilkDecodedImage decoded = imageDecoder(asset, false);
            return new SilkImageDescription(decoded.Width, decoded.Height, decoded.Format);
        });
        _udimResolver = udimResolver ?? SilkNativeImageDecoder.ResolveUdimTiles;
        _residencyOptions = residencyOptions ?? SilkTextureResidencyOptions.Default;
        _environmentMeanRadiance = new SilkEnvironmentMeanRadianceCache(
            environmentDecodeByteBudget);
        _environmentPrefilterOptions =
            environmentPrefilterOptions ?? DeriveEnvironmentOptions(environmentDecodeByteBudget);
        _environmentPrefilterOptions.Validate();
        _environmentLighting = new SilkEnvironmentLightingCache();
        // The stamp is what makes an edited HDR invalidate a prefiltered
        // environment whose path never changed. A caller that supplies its own
        // decoder is not necessarily reading the local file system at all, so it
        // supplies its own stamp reader too rather than being silently measured
        // against files that are not there.
        _environmentStampReader = environmentStampReader ?? SilkEnvironmentAssetStamp.Read;
        // Whether a *real* describer exists, as opposed to the decoder-backed
        // stand-in the constructor synthesizes above. The environment path reads
        // an image's shape to decide a mapping and a colour space, and doing that
        // through a describer that decodes would triple the decode cost of every
        // dome. A harness that supplies only a decoder therefore gets no
        // observation at all -- which is the honest answer, and which makes an
        // `automatic` mapping refuse rather than guess.
        _environmentDescriberAvailable = imageDescriber is not null;
        // The context half of the cache identity. It names the backend and this
        // resource set's own instance, so a prefiltered environment can never be
        // served to a different device or a substituted decoder: both are fixed
        // for the lifetime of one instance, and the instance number separates two
        // that would otherwise compose the same string. It is deliberately not
        // derived by reflecting over the decoder delegate, which would be a
        // trimming and NativeAOT hazard for a value that only has to be distinct.
        _environmentIdentityContext = string.Create(
            CultureInfo.InvariantCulture,
            $"{device.Backend}/{Interlocked.Increment(ref _environmentContextSequence)}");
        SilkManagedDiagnostics.GpuSceneCreated();
    }

    /// <summary>Gets uploaded mesh resources by explicit Hydra prim ID.</summary>
    public IReadOnlyDictionary<ulong, SilkMeshGpuResource> Meshes => _meshes;

    /// <summary>Gets a bounded snapshot of material and texture degradation diagnostics.</summary>
    public RenderDiagnosticsState Diagnostics =>
        _diagnostics.Count == 0
            ? RenderDiagnosticsState.Empty
            : new RenderDiagnosticsState(
                _diagnostics
                    .OrderBy(static pair => pair.Key, StringComparer.Ordinal)
                    .Select(static pair => pair.Value));

    /// <summary>
    /// Discards failed texture fallbacks so the next render retries assets that may have changed.
    /// </summary>
    /// <remarks>
    /// Only what this overload can put back is discarded. The decoded height
    /// fields are dropped, because the next resolution rebuilds them from the
    /// same authored inputs; the retained displaced geometry and its verdicts are
    /// *not*, because rebuilding one needs the scene the mesh record and its
    /// material came from and this overload does not have it. Discarding them
    /// here would leave a displaced prim drawn from vertices whose verdict the
    /// renderer had thrown away and could not restate. Use
    /// <see cref="RetryFailedTextures(SilkSceneState)"/> to re-resolve them.
    /// </remarks>
    public void RetryFailedTextures()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ClearFailedTextureCache();
        RemoveTextureDiagnostics();
        DiscardDisplacementImages();
    }

    /// <summary>
    /// Discards failed texture fallbacks and re-resolves every displaced prim, so
    /// a repaired asset reaches the next render.
    /// </summary>
    /// <param name="scene">The retained scene the prims were published from.</param>
    /// <remarks>
    /// <para>
    /// The parameterless overload cannot do the second half: a displaced prim's
    /// vertices are baked into its retained geometry, so a repaired height field
    /// only reaches the image if that geometry is rebuilt, and rebuilding one
    /// needs the scene the mesh record and its material came from. Both the
    /// retained height fields and the retained geometries that consumed them have
    /// to stop satisfying the fast path, because a repair that leaves the file's
    /// own stamp unchanged -- a permission fix, a resolver that started answering
    /// -- would otherwise resolve to the identity that already failed.
    /// </para>
    /// <para>
    /// The whole retry is one transaction. Nothing a rollback could not put back
    /// is destroyed before every replacement exists: the failed-texture entries
    /// are moved aside rather than disposed, and the diagnostics, decoded height
    /// fields, verdicts and published geometry keys the rebuild displaces are
    /// captured first. If any replacement fails to build -- a decode that throws,
    /// an allocation the device refuses -- the partial work is disposed, every
    /// captured value is put back exactly, and neither revision moves, so a
    /// caller's retained selection and the shadow atlas still name live resources
    /// and a failed retry is indistinguishable from one that never ran. Only after
    /// every replacement exists is membership published, the two revisions
    /// advanced, and the retired resources released.
    /// </para>
    /// <para>
    /// Cumulative work counters -- geometry builds, displacement resolves, image
    /// decodes -- are deliberately *not* rolled back. They count work this call
    /// actually performed, and a failed attempt performed it.
    /// </para>
    /// </remarks>
    public void RetryFailedTextures(SilkSceneState scene)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(scene);

        RetryTransaction transaction = BeginRetryTransaction();
        var replacements = new List<(ulong Id, SilkMeshGpuResource Resource)>();
        try
        {
            foreach (ulong id in transaction.DisplacedMeshIds)
            {
                if (scene.Meshes.TryGetValue(id, out SilkMeshData? mesh))
                {
                    replacements.Add((id, CreateMesh(scene, mesh)));
                }
            }
        }
        catch
        {
            foreach ((ulong _, SilkMeshGpuResource resource) in replacements)
            {
                DisposeMesh(resource);
            }
            RollBackRetryTransaction(transaction);
            throw;
        }

        // Past this point nothing can fail, so the commit is a straight line:
        // membership first, then the revisions every retained consumer validates
        // against, and only then the disposals.
        var retired = new List<SilkMeshGpuResource>(replacements.Count);
        foreach ((ulong id, SilkMeshGpuResource resource) in replacements)
        {
            if (_meshes.TryGetValue(id, out SilkMeshGpuResource? previous))
            {
                retired.Add(previous);
            }
            _meshes[id] = resource;
        }
        Revision++;
        scene.AdvanceGeometryRevisionForRebuild();
        foreach (SilkMeshGpuResource resource in retired)
        {
            DisposeMesh(resource);
        }
        CommitRetryTransaction(transaction);
        RefreshDisplacementDiagnostics(scene);
    }

    /// <summary>
    /// Everything a retry displaces, captured so a failed retry can put it back.
    /// </summary>
    /// <remarks>
    /// The failed-texture entries are carried by value rather than by reference
    /// because they own GPU resources: disposing them up front is what a rollback
    /// could not undo, so they are moved out of the live cache and disposed only
    /// once the retry has committed.
    /// </remarks>
    private readonly record struct RetryTransaction(
        List<ulong> DisplacedMeshIds,
        Dictionary<TextureCacheKey, TextureCacheEntry> FailedTextures,
        Dictionary<string, RenderDiagnostic> Diagnostics,
        Dictionary<ulong, DisplacementCacheEntry> DisplacementImages,
        Dictionary<DisplacedPrimKey, DisplacementVerdict> Verdicts,
        Dictionary<SilkMeshGpuGeometryKey, List<SilkMeshGpuGeometryResource>> Geometries,
        ulong DisplacementImageBytes,
        ulong DisplacementUseClock,
        ulong VerdictRevision,
        ulong ReportedRevision,
        ulong ReportedShadowRevision);

    private RetryTransaction BeginRetryTransaction()
    {
        var displaced = new List<ulong>();
        foreach (KeyValuePair<ulong, SilkMeshGpuResource> pair in _meshes)
        {
            if (pair.Value.Geometry.Key.DisplacementIdentity != 0)
            {
                displaced.Add(pair.Key);
            }
        }

        var transaction = new RetryTransaction(
            displaced,
            new Dictionary<TextureCacheKey, TextureCacheEntry>(_failedTextures),
            new Dictionary<string, RenderDiagnostic>(_diagnostics, StringComparer.Ordinal),
            new Dictionary<ulong, DisplacementCacheEntry>(_displacementImages),
            new Dictionary<DisplacedPrimKey, DisplacementVerdict>(_displacementVerdicts),
            _geometries.ToDictionary(
                static pair => pair.Key,
                static pair => new List<SilkMeshGpuGeometryResource>(pair.Value)),
            _displacementImageBytes,
            _displacementUseClock,
            _displacementVerdictRevision,
            _displacementReportedRevision,
            _displacementReportedShadowRevision);

        // Moved aside, not disposed: a rollback puts these back, and a commit
        // disposes them once the replacements that made them obsolete exist.
        _failedTextures.Clear();
        RemoveTextureDiagnostics();
        DiscardDisplacementResolutions();
        return transaction;
    }

    private void RollBackRetryTransaction(RetryTransaction transaction)
    {
        // The partial replacements have already been disposed, which released
        // every geometry they created or reused; what remains is to restore the
        // published state exactly as it was found.
        _failedTextures.Clear();
        foreach (KeyValuePair<TextureCacheKey, TextureCacheEntry> pair in transaction.FailedTextures)
        {
            _failedTextures[pair.Key] = pair.Value;
        }
        _diagnostics.Clear();
        foreach (KeyValuePair<string, RenderDiagnostic> pair in transaction.Diagnostics)
        {
            _diagnostics[pair.Key] = pair.Value;
        }
        _displacementImages.Clear();
        foreach (KeyValuePair<ulong, DisplacementCacheEntry> pair in transaction.DisplacementImages)
        {
            _displacementImages[pair.Key] = pair.Value;
        }
        _displacementVerdicts.Clear();
        foreach (KeyValuePair<DisplacedPrimKey, DisplacementVerdict> pair in transaction.Verdicts)
        {
            _displacementVerdicts[pair.Key] = pair.Value;
        }
        _geometries.Clear();
        foreach (KeyValuePair<SilkMeshGpuGeometryKey, List<SilkMeshGpuGeometryResource>> pair in
            transaction.Geometries)
        {
            _geometries[pair.Key] = pair.Value;
        }
        _displacementImageBytes = transaction.DisplacementImageBytes;
        _displacementUseClock = transaction.DisplacementUseClock;
        _displacementVerdictRevision = transaction.VerdictRevision;
        _displacementReportedRevision = transaction.ReportedRevision;
        _displacementReportedShadowRevision = transaction.ReportedShadowRevision;
    }

    private void CommitRetryTransaction(RetryTransaction transaction)
    {
        foreach (TextureCacheEntry entry in transaction.FailedTextures.Values)
        {
            DisposeEntry(entry);
        }
    }

    /// <summary>
    /// Drops every retained displacement resolution: the decoded height fields,
    /// the geometries that were built from them, and the verdicts they produced.
    /// </summary>
    /// <remarks>
    /// The geometries are only unpublished from the lookup, not disposed: each is
    /// still owned by the prim that draws it and is released by reference count
    /// when that prim is replaced. Unpublishing is what makes the next resolution
    /// a miss even when the authored inputs and the file stamp are unchanged.
    /// </remarks>
    private void DiscardDisplacementImages()
    {
        _displacementImages.Clear();
        _displacementImageBytes = 0;
    }

    private void DiscardDisplacementResolutions()
    {
        DiscardDisplacementImages();
        if (_displacementVerdicts.Count != 0)
        {
            _displacementVerdicts.Clear();
            _displacementVerdictRevision++;
        }
        foreach (SilkMeshGpuGeometryKey key in _geometries.Keys
            .Where(static candidate => candidate.DisplacementIdentity != 0)
            .ToArray())
        {
            _geometries.Remove(key);
        }
    }

    internal Dictionary<ulong, SilkMeshGpuResource>.ValueCollection MeshValues =>
        _meshes.Values;

    /// <summary>Gets the number of distinct retained geometry payloads.</summary>
    /// <remarks>
    /// A retention diagnostic. Geometry is shared by fingerprint and released by reference count,
    /// so a scene whose points change every frame must return to the same count once the previous
    /// payload is released rather than growing one entry per frame.
    /// </remarks>
    internal int GeometryResourceCount
    {
        get
        {
            int count = 0;
            foreach (List<SilkMeshGpuGeometryResource> matches in _geometries.Values)
            {
                count += matches.Count;
            }

            return count;
        }
    }

    /// <summary>Gets the revision of retained mesh-resource membership or metadata.</summary>
    public ulong Revision { get; private set; }

    /// <summary>Gets cumulative upload and resource-churn diagnostics.</summary>
    public SilkSceneGpuStatistics Statistics => new(
        _meshes.Count,
        _geometryBuilds,
        _vertexUploads,
        _indexUploads,
        _uniformUploads,
        _bufferAllocationBytes,
        _bufferWriteBytes,
        _textureUploadBytes,
        _decodedTextureResidentBytes,
        _gpuTextureResidentBytes,
        _peakDecodedTextureResidentBytes,
        _peakGpuTextureResidentBytes,
        _residencyOptions.MaxDecodedCpuBytes,
        _residencyOptions.MaxGpuBytes,
        _textures.Count + _failedTextures.Count + _volumeTextures.Count,
        _textureEvictionCount);

    private ulong _geometryBuilds;
    private ulong _vertexUploads;
    private ulong _indexUploads;
    private ulong _uniformUploads;
    private ulong _bufferAllocationBytes;
    private ulong _bufferWriteBytes;
    private ulong _textureUploadBytes;

    /// <summary>Applies only the mesh changes reported by a scene delta.</summary>
    public void Apply(SilkSceneState scene, SilkSceneDelta delta)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(scene);
        bool changed = delta.MeshRemovals != 0 ||
            delta.MeshUpserts != 0 ||
            delta.MaterialChanges != 0;

        foreach (ulong id in delta.RemovedMeshIds.Span)
        {
            if (_meshes.Remove(id, out SilkMeshGpuResource? removed))
            {
                // A removed prim keeps no displacement verdict: nothing is drawn
                // for it, so a retained report would name a prim the consumer can
                // no longer see. Only this instance's verdict is dropped -- a
                // sibling instance of the same prototype is still drawn, and still
                // earns the report its own verdict produces.
                ForgetDisplacementVerdict(removed.Mesh);
                DisposeMesh(removed);
            }
        }

        foreach (ulong id in delta.UpsertedMeshIds.Span)
        {
            if (!scene.Meshes.TryGetValue(id, out SilkMeshData? mesh))
            {
                throw new InvalidDataException(
                    $"Scene delta references missing mesh {id}.");
            }

            if (_meshes.TryGetValue(id, out SilkMeshGpuResource? existing) &&
                existing.HasSameGeometry(mesh))
            {
                existing.UpdateMesh(mesh);
                continue;
            }

            SilkMeshGpuResource replacement = CreateMesh(scene, mesh);
            if (_meshes.Remove(id, out SilkMeshGpuResource? previous))
            {
                DisposeMesh(previous);
            }
            _meshes.Add(id, replacement);
        }
        foreach (string materialPath in delta.ChangedMaterialPaths.ToArray())
        {
            List<ulong>? affected = null;
            foreach (KeyValuePair<ulong, SilkMeshGpuResource> pair in _meshes)
            {
                if (string.Equals(pair.Value.Mesh.MaterialPath, materialPath, StringComparison.Ordinal))
                {
                    (affected ??= []).Add(pair.Key);
                }
            }
            if (affected is null)
            {
                continue;
            }
            foreach (ulong id in affected)
            {
                SilkMeshData mesh = scene.Meshes[id];
                SilkMeshGpuResource replacement = CreateMesh(scene, mesh);
                SilkMeshGpuResource previous = _meshes[id];
                _meshes[id] = replacement;
                DisposeMesh(previous);
            }
            RemoveSurfaceBuffers(materialPath);
        }
        if (delta.MaterialChanges != 0)
        {
            RemoveChangedMaterialTextureCacheEntries(delta.ChangedMaterialPaths.Span);
            RemoveMaterialDiagnostics();
        }
        else if (delta.MeshUpserts != 0 || delta.MeshRemovals != 0)
        {
            RemoveMaterialResolutionDiagnostics();
            PruneInactiveTextureFailures(scene);
        }
        if (changed)
        {
            Revision++;
        }
        // The displacement verdicts depend on the published shadow table as well
        // as on which prims are displaced, and neither is a property of the other.
        // Recomputing here is what makes enabling shadows after a displaced prim
        // raise the bounds verdict, and retiring them clear it.
        RefreshDisplacementDiagnostics(scene);

        // Once per page, whether or not anything is drawable. A scene whose last
        // mesh was removed still has to drop the diagnostics and the per-mask
        // surface blocks its retired link table left behind: the draw loop is
        // where the observation used to happen, and an empty scene never reaches
        // it, so an emptied stage warned forever about a table it no longer had.
        ObserveLightLinkRevision(scene);
    }

    /// <summary>Updates only changed per-mesh SceneParameters constants.</summary>
    public int UpdateUniforms(SilkFrameState frame) => UpdateUniforms(frame, overrides: null);

    /// <summary>
    /// Updates only changed per-mesh SceneParameters constants, replacing the authored transform of
    /// every mesh a physics transform override drives.
    /// </summary>
    /// <param name="frame">The frame state the constants are projected with.</param>
    /// <param name="overrides">
    /// The resolved physics transform overrides, or <see langword="null"/> to draw every mesh from
    /// its authored transform.
    /// </param>
    /// <returns>The number of uniform blocks uploaded.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="frame"/> is null.</exception>
    public int UpdateUniforms(SilkFrameState frame, SilkPhysicsTransformOverrides? overrides)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(frame);
        int uploads = 0;
        bool hasOverrides = overrides is not null && overrides.HasOverrides;
        Span<byte> constants = stackalloc byte[SilkSceneUniformWriter.ByteSize];
        foreach (KeyValuePair<ulong, SilkMeshGpuResource> pair in _meshes)
        {
            ReadOnlySpan<double> transform = hasOverrides
                ? overrides!.GetTransform(pair.Key)
                : default;
            if (pair.Value.UpdateUniform(frame, constants, _device.ClipSpaceYPointsDown, transform))
            {
                uploads++;
                _uniformUploads++;
                _bufferWriteBytes += SilkSceneUniformWriter.ByteSize;
            }
        }
        return uploads;
    }

    /// <summary>Returns the per-frame constants the mesh shader reads.</summary>
    /// <remarks>
    /// Takes the whole retained scene rather than just its frame state because the
    /// ambient term the constants carry is a function of both: hdSilk publishes a
    /// textured dome light as environment state instead of folding it into the
    /// frame ambient colour, and the resolved environment contribution is added
    /// here.
    /// </remarks>
    internal ISilkGraphicsBuffer RequireFrameBuffer(
        SilkSceneState scene,
        RenderOutputTransform outputTransform,
        float exposure,
        SilkShadowFrameBinding? shadows = null,
        ulong shadowBindingRevision = 0)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(scene);
        SilkFrameState frame = scene.Frame;
        Vector3 environmentAmbient = RequireEnvironmentAmbient(scene);
        bool created = _frameBuffer is null;
        _frameBuffer ??= CreateTrackedBuffer(
            SilkFrameUniformWriter.ByteSize,
            SilkBufferUsage.Storage | SilkBufferUsage.Upload);
        if (_frameRevision != frame.Revision ||
            _frameEnvironmentRevision != scene.EnvironmentRevision ||
            _frameEnvironmentAmbientRevision != _environmentAmbientRevision ||
            _frameEnvironmentBindingRevision != _environmentBindingRevision ||
            _frameShadowRevision != shadowBindingRevision ||
            _frameOutputTransform != outputTransform ||
            _frameExposure != exposure)
        {
            Span<byte> constants = stackalloc byte[SilkFrameUniformWriter.ByteSize];
            SilkFrameUniformWriter.Write(
                frame,
                constants,
                _device.ClipSpaceYPointsDown,
                outputTransform,
                exposure,
                environmentAmbient,
                shadows,
                _environmentBinding,
                _environmentDomeAmbient);
            if (created || !constants.SequenceEqual(_frameBytes))
            {
                WriteTracked(_frameBuffer, constants);
                constants.CopyTo(_frameBytes);
            }
            _frameRevision = frame.Revision;
            _frameEnvironmentRevision = scene.EnvironmentRevision;
            _frameEnvironmentAmbientRevision = _environmentAmbientRevision;
            _frameEnvironmentBindingRevision = _environmentBindingRevision;
            _frameShadowRevision = shadowBindingRevision;
            _frameOutputTransform = outputTransform;
            _frameExposure = exposure;
        }
        DiagnoseShadows(scene);
        return _frameBuffer;
    }

    /// <summary>
    /// Reports every direct light whose authored <c>inputs:shadow:enable</c> did
    /// not produce a shadow map, and why.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A light that authors shadows and got a map is silent: the image shows the
    /// occlusion. Everything else is named against the light's own table index and
    /// resolved type -- the frame light table carries no prim path, because lights
    /// reach the renderer as a fixed-size table of resolved values rather than as
    /// named records -- so an unshadowed light is never mistaken for a deliberate
    /// one. Silent success and silent failure are indistinguishable otherwise,
    /// which is the failure mode this profile exists to avoid.
    /// </para>
    /// <para>
    /// Re-derived whenever the frame or the shadow table moves, so a light that
    /// stops asking for shadows, or that starts getting a map, stops being
    /// reported.
    /// </para>
    /// </remarks>
    private void DiagnoseShadows(SilkSceneState scene)
    {
        SilkFrameState frame = scene.Frame;
        SilkShadowTable shadows = scene.Shadows;
        if (_shadowDiagnosticRevision == frame.Revision &&
            _shadowDiagnosticTableRevision == shadows.Revision)
        {
            return;
        }

        RemoveDiagnostics(static code => code == SilkRenderDiagnosticCodes.ShadowUnsupported);
        _shadowDiagnosticRevision = frame.Revision;
        _shadowDiagnosticTableRevision = shadows.Revision;
        bool deviceSupportsShadows = _device.Capabilities.SupportsRasterShadows;
        ReadOnlySpan<SilkFrameLight> lights = frame.Lights;
        uint count = Math.Min(frame.LightCount, (uint)SilkFrameState.MaximumLights);
        for (int index = 0; index < count; index++)
        {
            SilkFrameLight light = lights[index];
            if (light.Type == 0 || light.ShadowEnabled == 0)
            {
                continue;
            }
            if (deviceSupportsShadows && shadows.ResolveSlot(index) >= 0)
            {
                continue;
            }

            string reason = !deviceSupportsShadows
                ? $"the {_device.Backend} device cannot record a depth-only pass, so no " +
                    "shadow map is allocated or rendered"
                : DescribeMissingShadow(light.Type, shadows.UnsupportedFeatures);
            AddDiagnostic(
                SilkRenderDiagnosticCodes.ShadowUnsupported,
                $"light[{index}]",
                RenderDiagnosticSeverity.Warning,
                $"Direct light {index} ({DescribeLightType(light.Type)}) authors " +
                $"inputs:shadow:enable but casts no shadow: {reason}. The light is " +
                "rendered without occlusion.");
        }
    }

    private static string DescribeMissingShadow(
        uint lightType,
        SilkShadowUnsupportedFeatures unsupported)
    {
        if (lightType != 1)
        {
            return "only a distant light has an exact light-space projection here, and " +
                "no sphere, rect, disk or cylinder projection is derived";
        }
        if ((unsupported & SilkShadowUnsupportedFeatures.NoCasters) != 0)
        {
            return "the published geometry has no world extent to derive a light-space " +
                "projection from";
        }
        if ((unsupported & SilkShadowUnsupportedFeatures.MapBudget) != 0)
        {
            return $"more lights asked for a shadow map than the page budget of " +
                $"{SilkShadowCommand.MaximumMaps} allows";
        }
        return "the page published no shadow descriptor for it";
    }

    /// <summary>
    /// Reports every prim that was dropped from the shadow maps because its
    /// material is opacity-masked.
    /// </summary>
    /// <remarks>
    /// Re-derived whenever the cache re-collects its casters, so a prim whose
    /// material stops being opacity-masked stops being reported. Named by prim
    /// path, because unlike a light a caster has one.
    /// </remarks>
    internal void ReportUnsupportedShadowCasters(
        IReadOnlyList<string> paths,
        ulong revision)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(paths);
        if (_shadowCasterDiagnosticRevision == revision)
        {
            return;
        }

        _shadowCasterDiagnosticRevision = revision;
        RemoveDiagnostics(
            static code => code == SilkRenderDiagnosticCodes.ShadowCasterUnsupported);
        foreach (string path in paths)
        {
            AddDiagnostic(
                SilkRenderDiagnosticCodes.ShadowCasterUnsupported,
                path,
                RenderDiagnosticSeverity.Warning,
                $"Prim '{path}' casts no shadow because its material is opacity-masked. " +
                "The depth-only shadow caster program binds no material and cannot " +
                "discard a fragment, so drawing it would cast the solid shadow of its " +
                "geometry rather than of its visible coverage. The prim is still lit " +
                "and still receives shadows.");
        }
    }

    private static string DescribeLightType(uint type) =>
        type switch
        {
            1 => "distant",
            2 => "sphere",
            3 => "rect",
            4 => "disk",
            5 => "cylinder",
            _ => "unknown"
        };

    /// <summary>
    /// Resolves the mean-radiance ambient fallback of every retained textured
    /// dome light the prefiltered environment does not carry, and diagnoses every
    /// authored dome control this renderer did not apply.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A dome that reached the prefiltered environment contributes nothing to the
    /// ambient term: its whole emission is in the irradiance and specular maps,
    /// and adding an approximation of the same dome on top would count it twice.
    /// Every other textured dome falls back to the mean-radiance term -- the
    /// solid-angle-weighted mean radiance of its image, scaled by the authored
    /// colour, intensity, exposure and diffuse contribution, and normalized by the
    /// same unit-white-dome factor an untextured dome uses -- so a dome whose
    /// image is constant 1.0 falls back to exactly the ambient the untextured dome
    /// it replaced produced.
    /// </para>
    /// <para>
    /// The <em>diagnostics</em>, unlike the ambient term, are emitted for every
    /// dome whether or not it was prefiltered. An authored control this renderer
    /// did not carry is unapplied either way, and reporting it only on the failure
    /// path would mean a scene that succeeded looked clean while still silently
    /// dropping a colour temperature.
    /// </para>
    /// <para>
    /// The fallback is not image-based lighting and must not be described as one.
    /// The image is collapsed to a single colour, so every surface normal receives
    /// the same value and none of the sky's directionality survives. Reaching it
    /// is always diagnosed against the dome's own prim path, so a scene that
    /// quietly lost its directional response is not a state this renderer can be
    /// in, and it degrades to the previous behaviour rather than to darkness.
    /// </para>
    /// </remarks>
    internal Vector3 RequireEnvironmentAmbient(SilkSceneState scene)
    {
        ArgumentNullException.ThrowIfNull(scene);

        // The observed state of every asset the *fallback* reads, re-read on every
        // call. Without it the ambient term was a function of the scene revision
        // alone, so a dome whose file was repaired or re-exported in place under a
        // running session kept lighting from the bytes that were no longer there
        // until some unrelated command moved the revision. It is the same rule the
        // prefiltered path already followed, applied to the path a refused dome
        // actually lands on.
        string fallbackRevision = ComposeEnvironmentFallbackRevision(scene);
        if (_environmentRevision == scene.EnvironmentRevision &&
            _environmentsResolved &&
            string.Equals(
                _environmentFallbackRevision,
                fallbackRevision,
                StringComparison.Ordinal))
        {
            return _environmentAmbient;
        }

        RemoveEnvironmentDiagnostics();
        Vector3 ambient = Vector3.Zero;
        var domeAmbient = default(SilkDomeAmbientTable);
        foreach (KeyValuePair<string, SilkEnvironmentData> pair in scene.Environments
            .OrderBy(static pair => pair.Key, StringComparer.Ordinal))
        {
            SilkEnvironmentData dome = pair.Value;
            DiagnoseEnvironmentControls(dome, _environmentLitDomes.Contains(pair.Key));
            if (_environmentLitDomes.Contains(pair.Key))
            {
                continue;
            }
            Vector3 contribution = ResolveEnvironment(dome);
            ambient += contribution;

            // Attributed to the dome's own bit so a per-draw mask can select it.
            // A dome the page could not give a bit to is summed into the
            // scene-wide term only, which is the same thing as saying it lights
            // every prim -- and it is diagnosed as unmaskable by hdSilk.
            if (dome.HasDomeIndex && dome.DomeIndex < SilkFrameCommand.MaximumDomes)
            {
                domeAmbient.AddAmbient((int)dome.DomeIndex, contribution);
            }
            else
            {
                domeAmbient.AddUnattributed(contribution);
            }
        }

        _environmentAmbient = ambient;
        _environmentDomeAmbient = domeAmbient;
        _environmentRevision = scene.EnvironmentRevision;
        _environmentFallbackRevision = fallbackRevision;
        _environmentAmbientRevision++;
        _environmentsResolved = true;
        return ambient;
    }

    /// <summary>
    /// Gets the mean-radiance ambient term each dome bit contributes on its own.
    /// </summary>
    /// <remarks>
    /// Resolved by <see cref="RequireEnvironmentAmbient"/> and cached beside the
    /// scene-wide sum it was accumulated into, so the two can never describe
    /// different sets of domes.
    /// </remarks>
    internal SilkDomeAmbientTable EnvironmentDomeAmbient => _environmentDomeAmbient;

    /// <summary>
    /// Composes the observed state of every asset the mean-radiance fallback
    /// would read into one comparable token.
    /// </summary>
    /// <remarks>
    /// Scoped to the domes that are <em>not</em> carried by the prefiltered
    /// environment, because those are the only ones whose file contents reach the
    /// ambient term. A prefiltered dome's asset already moves the environment
    /// identity, and including it here would re-resolve an ambient that does not
    /// depend on it.
    /// </remarks>
    private string ComposeEnvironmentFallbackRevision(SilkSceneState scene)
    {
        if (scene.Environments.Count == 0)
        {
            return string.Empty;
        }

        var builder = new StringBuilder(64);
        foreach (KeyValuePair<string, SilkEnvironmentData> pair in scene.Environments
            .OrderBy(static pair => pair.Key, StringComparer.Ordinal))
        {
            if (_environmentLitDomes.Contains(pair.Key))
            {
                continue;
            }
            SilkEnvironmentAssetStamp stamp =
                _environmentStampReader(pair.Value.TexturePath);
            _ = builder
                .Append(pair.Key)
                .Append('|')
                .Append(pair.Value.TexturePath)
                .Append('|')
                .Append(stamp.Length.ToString(CultureInfo.InvariantCulture))
                .Append('|')
                .Append(stamp.LastWriteUtcTicks.ToString(CultureInfo.InvariantCulture))
                .Append(';');
        }
        return builder.ToString();
    }

    /// <summary>
    /// Names every authored dome control this renderer did not apply, whether or
    /// not the dome reached the prefiltered environment.
    /// </summary>
    private void DiagnoseEnvironmentControls(SilkEnvironmentData dome, bool prefiltered)
    {
        if (dome.UnsupportedFeatures != SilkEnvironmentUnsupportedFeatures.None)
        {
            string effect = dome.SemanticsInvalidatingFeatures !=
                SilkEnvironmentUnsupportedFeatures.None
                ? "it invalidates the emission or orientation the prefiltered " +
                    "environment would claim, so this dome resolves its " +
                    "mean-radiance ambient term instead"
                : "the authored emission is used without it";
            AddDiagnostic(
                SilkRenderDiagnosticCodes.EnvironmentFeatureUnsupported,
                dome.Path,
                RenderDiagnosticSeverity.Warning,
                $"Dome light '{dome.Path}' authors {dome.UnsupportedFeatures}, " +
                $"which hdSilk does not carry; {effect}.");
        }

        // Only meaningful for a dome that did *not* reach the prefiltered
        // environment: the fallback collapses the sky to one colour, so there is
        // no directionality left to reflect. Reflecting that single value would
        // put every mirror-like surface at the average colour of the sky, so the
        // authored specular contribution is named instead of faked. A dome the
        // prefiltered environment carries resolves its specular contribution and
        // is silent.
        if (!prefiltered && dome.Specular != 0)
        {
            AddDiagnostic(
                SilkRenderDiagnosticCodes.EnvironmentSpecularUnsupported,
                dome.Path,
                RenderDiagnosticSeverity.Warning,
                $"Dome light '{dome.Path}' authors a specular contribution " +
                $"of {dome.Specular.ToString(CultureInfo.InvariantCulture)}, " +
                "which this dome's mean-radiance ambient fallback cannot resolve.");
        }
    }

    /// <summary>
    /// Forces one more prefilter attempt when the fallback read a source the
    /// prefilter refused.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The two paths decode the same asset, so a refusal on one and a success on
    /// the other in the same revision is a contradiction rather than a verdict.
    /// Settling the directional loss there meant a momentarily unavailable file
    /// cost the scene its directionality until some unrelated authoring moved the
    /// environment revision -- even though the very next frame would have read it.
    /// </para>
    /// <para>
    /// Retried once per dome and asset state. A source that reproduces the
    /// contradiction against the same bytes is a real disagreement between the two
    /// paths rather than a transient one, and retrying it every frame would decode
    /// the image forever.
    /// </para>
    /// </remarks>
    private void RetryTransientPrefilterFailure(SilkEnvironmentData environment)
    {
        if (!_environmentPrefilterSkipped.Contains(environment.Path))
        {
            return;
        }

        SilkEnvironmentAssetStamp stamp = _environmentStampReader(environment.TexturePath);
        string key = string.Concat(
            environment.Path,
            "\u001f",
            environment.TexturePath,
            "\u001f",
            stamp.Length.ToString(CultureInfo.InvariantCulture),
            "\u001f",
            stamp.LastWriteUtcTicks.ToString(CultureInfo.InvariantCulture));
        if (!_environmentPrefilterRetried.Add(key))
        {
            return;
        }

        _ = _environmentPrefilterSkipped.Remove(environment.Path);
        InvalidateEnvironmentRevision();
    }

    private Vector3 ResolveEnvironment(SilkEnvironmentData environment)
    {
        Vector3 emission = environment.AmbientEmissionScale;
        SilkImageDescription? description = TryDescribeEnvironment(environment.TexturePath);
        if (!environment.IsMappingSupported(description))
        {
            AddDiagnostic(
                SilkRenderDiagnosticCodes.EnvironmentMappingUnsupported,
                environment.Path,
                RenderDiagnosticSeverity.Warning,
                $"Dome light '{environment.Path}' declares texture:format " +
                $"{environment.TextureFormat}, which this renderer does not " +
                $"{DescribeMappingRefusal(environment, description)}; its " +
                "untextured emission is used instead.");
            return emission;
        }

        try
        {
            Vector3 mean = _environmentMeanRadiance.Resolve(
                environment.TexturePath,
                environment.SourceColorSpace,
                _environmentStampReader(environment.TexturePath),
                _imageDecoder,
                TryDescribeEnvironment);

            // The prefilter refused this source and the fallback has just read it.
            // Those two verdicts cannot both be right about the same bytes, so the
            // refusal was transient -- an asset momentarily unavailable, a decoder
            // that failed once -- and the loss of directionality must not be
            // settled on it. Invalidating the revision makes the next prepare
            // retry with no scene change at all; it succeeds, the environment is
            // enabled, this dome leaves the ambient sum, and the stale diagnostic
            // is dropped by the prefilter layer's own clear.
            RetryTransientPrefilterFailure(environment);
            return emission * mean;
        }
        catch (SilkEnvironmentBudgetExceededException exception)
        {
            AddDiagnostic(
                SilkRenderDiagnosticCodes.EnvironmentBudgetExceeded,
                environment.Path,
                RenderDiagnosticSeverity.Warning,
                $"Dome light '{environment.Path}': {exception.Message} " +
                "Its untextured emission is used instead.");
        }
        catch (FileNotFoundException exception)
        {
            AddDiagnostic(
                SilkRenderDiagnosticCodes.EnvironmentAssetNotFound,
                environment.Path,
                RenderDiagnosticSeverity.Error,
                $"Dome light '{environment.Path}' references environment texture " +
                $"'{environment.TexturePath}', which was not found: " +
                $"{exception.Message}");
        }
        catch (InvalidDataException exception)
        {
            AddDiagnostic(
                SilkRenderDiagnosticCodes.EnvironmentDecodeFailed,
                environment.Path,
                RenderDiagnosticSeverity.Error,
                $"Dome light '{environment.Path}' references environment texture " +
                $"'{environment.TexturePath}', which could not be decoded: " +
                $"{exception.Message}");
        }
        return emission;
    }

    /// <summary>
    /// States why one declared mapping was refused, distinguishing a named
    /// projection this renderer does not implement from an <c>automatic</c>
    /// declaration whose image does not observably carry one.
    /// </summary>
    private static string DescribeMappingRefusal(
        SilkEnvironmentData environment,
        SilkImageDescription? description)
    {
        if (environment.TextureFormat != SilkDomeTextureFormat.Automatic)
        {
            return "implement -- each of mirroredBall, angular and " +
                "cubeMapVerticalCross parameterizes the sphere differently, and " +
                "integrating one as equirectangular weights the wrong parts of " +
                "the image";
        }

        string shape = description is { } observed
            ? $"{observed.Width.ToString(CultureInfo.InvariantCulture)}x" +
                observed.Height.ToString(CultureInfo.InvariantCulture)
            : "an image whose shape could not be observed";
        return "derive from the image, because an equirectangular map is twice " +
            $"as wide as it is tall and this one is {shape}";
    }

    /// <summary>
    /// Reads one environment image's declared shape without decoding it, or
    /// <see langword="null"/> when it cannot be described.
    /// </summary>
    /// <remarks>
    /// The describer is what makes every budget below a preflight rather than a
    /// post-mortem: it reports the width, height, format and observed colour space
    /// from the file's header, so an image over a byte budget is refused before an
    /// allocator is ever asked for it. A describer that cannot answer -- a missing
    /// file, a format it does not recognize -- returns null, and the caller
    /// diagnoses the same way it would for a failed decode.
    /// </remarks>
    private SilkImageDescription? TryDescribeEnvironment(string asset)
    {
        if (!_environmentDescriberAvailable)
        {
            return null;
        }

        var key = (asset, _environmentStampReader(asset));
        if (_environmentDescriptions.TryGetValue(key, out SilkImageDescription? cached))
        {
            return cached;
        }

        SilkImageDescription? description;
        try
        {
            description = _imageDescriber(asset);
        }
        catch (Exception exception) when (
            exception is FileNotFoundException or DirectoryNotFoundException or
                IOException or InvalidDataException or NotSupportedException or
                ArgumentException)
        {
            description = null;
        }

        // Bounded, and keyed by the file's own stamp, so a rewritten asset is
        // described again while a stable one is described once. The bound is small
        // because the number of distinct dome textures a stage cycles through is.
        if (_environmentDescriptions.Count >= MaximumEnvironmentDescriptions)
        {
            _environmentDescriptions.Clear();
        }
        _environmentDescriptions[key] = description;
        return description;
    }

    /// <summary>
    /// The number of environment image descriptions retained. Small on purpose:
    /// this exists to stop one resolve pass describing the same asset several
    /// times, not to be a texture cache.
    /// </summary>
    private const int MaximumEnvironmentDescriptions = 16;

    private void RemoveEnvironmentDiagnostics()
    {
        // Scoped to the fallback layer. The prefilter layer emits the same codes
        // against the same domes to say something different -- that the dome lost
        // its directional response -- and it resolves earlier in the frame, so
        // clearing by code alone erased it every time the fallback then succeeded.
        RemoveDiagnosticsByKey(static key =>
            !key.Contains(EnvironmentPrefilterLayer, StringComparison.Ordinal) &&
            (key.StartsWith(SilkRenderDiagnosticCodes.EnvironmentAssetNotFound, StringComparison.Ordinal) ||
                key.StartsWith(SilkRenderDiagnosticCodes.EnvironmentDecodeFailed, StringComparison.Ordinal) ||
                key.StartsWith(SilkRenderDiagnosticCodes.EnvironmentBudgetExceeded, StringComparison.Ordinal) ||
                key.StartsWith(SilkRenderDiagnosticCodes.EnvironmentMappingUnsupported, StringComparison.Ordinal) ||
                key.StartsWith(SilkRenderDiagnosticCodes.EnvironmentFeatureUnsupported, StringComparison.Ordinal) ||
                key.StartsWith(SilkRenderDiagnosticCodes.EnvironmentSpecularUnsupported, StringComparison.Ordinal)));
    }

    /// <summary>
    /// Marks a diagnostic as belonging to the prefilter layer rather than to the
    /// mean-radiance fallback layer.
    /// </summary>
    private const string EnvironmentPrefilterLayer = "\u0001prefilter";

    /// <summary>
    /// Reports one dome's loss of its directional response, from the prefilter
    /// layer, so the fallback that follows cannot erase it.
    /// </summary>
    private void AddEnvironmentPrefilterDiagnostic(
        string code,
        string domePath,
        string message) =>
        AddDiagnostic(
            code,
            string.Concat(domePath, EnvironmentPrefilterLayer),
            RenderDiagnosticSeverity.Warning,
            message);

    /// <summary>Drops every diagnostic the prefilter layer emitted.</summary>
    private void RemoveEnvironmentPrefilterDiagnostics() =>
        RemoveDiagnosticsByKey(static key =>
            key.Contains(EnvironmentPrefilterLayer, StringComparison.Ordinal));

    /// <summary>Gets the resolved environment block the frame constants carry.</summary>
    internal SilkEnvironmentFrameBinding EnvironmentBinding => _environmentBinding;

    /// <summary>
    /// Gets a revision that changes only when the resolved environment block or
    /// its retained maps change, so the frame constants re-pack exactly when they
    /// must.
    /// </summary>
    internal ulong EnvironmentBindingRevision => _environmentBindingRevision;

    /// <summary>Gets the retained prefiltered payload, for tests.</summary>
    internal SilkEnvironmentMaps? EnvironmentPayloadForTesting => _environmentPayload;

    /// <summary>Gets the number of environment source decodes performed.</summary>
    /// <remarks>
    /// Exists so that the single-pass rule is gated by counting rather than by
    /// inspection: a resolve that re-decoded a prefix after dropping a dome would
    /// be indistinguishable from one that did not, in every observable except
    /// this.
    /// </remarks>
    internal int EnvironmentDecodeCount { get; private set; }

    /// <summary>Gets the decoded bytes every environment source produced.</summary>
    internal ulong EnvironmentDecodedBytes { get; private set; }

    /// <summary>Gets the dome prim paths the prefiltered environment carries.</summary>
    internal IReadOnlyCollection<string> EnvironmentLitDomes => _environmentLitDomes;

    /// <summary>Gets the number of prefiltered environments built since construction.</summary>
    internal int EnvironmentPrefilterBuilds => _environmentLighting.BuildCount;

    /// <summary>Gets the number of prefiltered environment bytes uploaded.</summary>
    internal ulong EnvironmentUploadBytes => _environmentUploadBytes;

    /// <summary>
    /// Prefilters every accepted textured dome light into the shared world-space
    /// environment maps and allocates the GPU resources the checked fragment
    /// samples them through.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Separate from <see cref="RequireFrameBuffer"/>, and run before it, because
    /// the two answer different questions and only this one allocates. A consumer
    /// that never calls it -- a harness with no device that only wants the frame
    /// constants -- gets the mean-radiance ambient term for every dome and an
    /// environment block that reports itself disabled, which is exactly the
    /// behaviour that existed before the directional response did.
    /// </para>
    /// <para>
    /// The work is redone when the environment revision moves, when any dome's
    /// asset stamp moves, or when the device generation changes, and reused byte
    /// for byte otherwise. The stamps are re-read on every call rather than only
    /// when a scene command arrives -- a bounded handful of file stats -- which is
    /// what lets an HDR repaired or re-exported under a running session reach the
    /// image without any authoring at all. A device generation change is a device
    /// loss: every retained GPU object belongs to the dead device, so all of them
    /// are dropped and rebuilt rather than rebound.
    /// </para>
    /// <para>
    /// Nothing here caches a failure. A dome whose asset could not be described,
    /// read or decoded is left out of the composed set and reaches the
    /// mean-radiance fallback, which names it; the next resolve that repairs the
    /// asset composes it back in.
    /// </para>
    /// </remarks>
    internal void PrepareEnvironmentLighting(SilkSceneState scene)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(scene);
        ulong deviceGeneration = SilkDeviceGeneration.Read(_device);
        if (_environmentLightingDeviceGeneration != deviceGeneration)
        {
            // A device loss invalidates every environment-owned GPU object, not
            // only the two maps: the sampler, the stand-in and the BRDF table
            // belong to the dead device too.
            ReleaseEnvironmentDeviceResources();
            _environmentLightingDeviceGeneration = deviceGeneration;
            _environmentLightingRevision = ulong.MaxValue;
        }

        // Ordered by prim path so that which domes are composed, and in which
        // order they are summed, is a property of the scene rather than of
        // dictionary iteration order. The bake is a sum, so the order does not
        // change the result, but it does change the cache identity, and an
        // identity that depended on hash ordering would miss at random.
        SilkEnvironmentData[] published = [.. scene.Environments
            .OrderBy(static pair => pair.Key, StringComparer.Ordinal)
            .Select(static pair => pair.Value)];

        // The authored fact, recorded before anything is resolved. Every dome
        // hdSilk publishes as an environment record is a dome the author placed,
        // so the scene is lit whatever this renderer subsequently manages to do
        // with it -- and a dome that resolves to nothing measurable is precisely
        // the case a headlight must not rescue.
        SetEnvironmentAuthoredSceneLighting(published.Length > 0);

        // The observed state of every published dome's asset, re-read every time.
        // It is part of the identity, so a file rewritten in place moves it and
        // rebuilds without any scene command at all.
        string assetRevision = ComposeEnvironmentAssetRevision(published);

        // Whether any prim excludes a dome. It is part of what the resolve
        // produces, because it decides whether the bake carries one group per
        // dome or a single composed group -- and a scene that stops linking domes
        // has to go back to the single-group layout on the same frame, or it
        // would keep rendering the grouped atlas that a byte-exact unlinked
        // comparison must not see.
        bool perDomeGroups = scene.LightLinks.HasDomeLinks;
        if (_environmentLightingRevision == scene.EnvironmentRevision &&
            _environmentPerDomeGroups == perDomeGroups &&
            string.Equals(_environmentAssetRevision, assetRevision, StringComparison.Ordinal))
        {
            return;
        }
        _environmentPerDomeGroups = perDomeGroups;

        // Deliberately *not* committed yet. A device that refuses one of the
        // environment's five GPU objects has not necessarily refused it forever --
        // a transient allocation failure is the ordinary case -- and committing the
        // revision before the allocation succeeded meant the next frame saw
        // nothing to redo and the scene stayed on the fallback until some
        // unrelated authoring moved the revision. The revision is committed at the
        // end, on the paths that reached a settled state.
        // The prefilter layer's diagnostics are dropped here, at the start of the
        // resolve that re-emits them, rather than by the mean-radiance fallback
        // that runs afterwards. The two layers report the same codes about the
        // same domes to say different things -- one that the dome lost its
        // directional response, the other that it could not even be reduced to a
        // colour -- and clearing by code let the fallback erase the loss every time
        // it then succeeded, which is exactly the case that must stay reported.
        RemoveEnvironmentPrefilterDiagnostics();
        RemoveDiagnostics(static code =>
            code is SilkRenderDiagnosticCodes.EnvironmentLightingLimitExceeded or
                SilkRenderDiagnosticCodes.EnvironmentLightingUnavailable or
                SilkRenderDiagnosticCodes.EnvironmentDomeLinkUnavailable);

        var candidates = new List<SilkEnvironmentData>();
        foreach (SilkEnvironmentData dome in published)
        {
            // The mapping verdict reads the image's observed shape, and the
            // control verdict reads what hdSilk could not carry. Both are refusals
            // to *prefilter*; each dome they refuse still reaches the fallback,
            // which names it.
            if (!dome.IsMappingSupported(TryDescribeEnvironment(dome.TexturePath)) ||
                dome.SemanticsInvalidatingFeatures !=
                    SilkEnvironmentUnsupportedFeatures.None)
            {
                continue;
            }
            if (candidates.Count == _environmentPrefilterOptions.MaximumDomeLights)
            {
                AddDiagnostic(
                    SilkRenderDiagnosticCodes.EnvironmentLightingLimitExceeded,
                    dome.Path,
                    RenderDiagnosticSeverity.Warning,
                    $"Dome light '{dome.Path}' is beyond the " +
                    $"{_environmentPrefilterOptions.MaximumDomeLights.ToString(CultureInfo.InvariantCulture)}" +
                    " textured dome lights this renderer composes into one " +
                    "prefiltered environment; it falls back to its mean-radiance " +
                    "ambient term instead.");
                continue;
            }
            candidates.Add(dome);
        }

        var previousLit = new HashSet<string>(_environmentLitDomes, StringComparer.Ordinal);
        _environmentLitDomes.Clear();
        _environmentPrefilterSkipped.Clear();
        SilkEnvironmentMaps? maps = BuildEnvironmentMaps(
            candidates,
            perDomeGroups,
            out string identity,
            out bool settled);
        if (maps is null)
        {
            // A settled state: every dome was refused for a reason that is a
            // property of the scene, not of the device, and re-running the resolve
            // next frame would refuse them again. The revision is committed so the
            // refusal is paid for once.
            ReleaseEnvironmentMaps();
            if (settled)
            {
                CommitEnvironmentRevision(scene.EnvironmentRevision, assetRevision);
            }
            else
            {
                InvalidateEnvironmentRevision();
            }
            InvalidateEnvironmentAmbient(previousLit);
            return;
        }

        try
        {
            EnsureEnvironmentTextures(maps, identity, candidates);
        }
        catch (Exception exception) when (
            exception is InvalidOperationException or NotSupportedException or
                ArgumentException or OutOfMemoryException or OverflowException)
        {
            // Not a settled state. The scene is unchanged and the payload is
            // still cached, so the only thing that failed is an allocation the
            // device may well satisfy next frame. The revision is left where it
            // was, which is what makes the next resolve retry -- and because the
            // prefiltered payload is retained under its identity, that retry costs
            // allocations and not a second convolution.
            ReleaseEnvironmentMaps();
            InvalidateEnvironmentRevision();
            AddDiagnostic(
                SilkRenderDiagnosticCodes.EnvironmentLightingUnavailable,
                string.Empty,
                RenderDiagnosticSeverity.Warning,
                "The prefiltered environment could not be allocated on this device: " +
                $"{exception.Message} Every textured dome light falls back to its " +
                "mean-radiance ambient term.");
            InvalidateEnvironmentAmbient(previousLit);
            return;
        }

        foreach (SilkEnvironmentData dome in candidates)
        {
            _environmentLitDomes.Add(dome.Path);
        }
        CommitEnvironmentRevision(scene.EnvironmentRevision, assetRevision);
        InvalidateEnvironmentAmbient(previousLit);
    }

    /// <summary>Records that one environment resolve reached a settled state.</summary>
    private void CommitEnvironmentRevision(ulong environmentRevision, string assetRevision)
    {
        _environmentLightingRevision = environmentRevision;
        _environmentAssetRevision = assetRevision;
    }

    /// <summary>
    /// Forces the next resolve to run again, without any scene change.
    /// </summary>
    /// <remarks>
    /// Used when a resolve failed for a reason that is a property of the device
    /// rather than of the scene. Leaving the revision at its previous value would
    /// be wrong too: the previous value may equal the current one, and the resolve
    /// would then be skipped for exactly the reason it needs to be repeated.
    /// </remarks>
    private void InvalidateEnvironmentRevision()
    {
        _environmentLightingRevision = ulong.MaxValue;
        _environmentAssetRevision = "\u0001retry";
    }

    /// <summary>
    /// Composes the observed state of every published dome's asset into one
    /// comparable token.
    /// </summary>
    private string ComposeEnvironmentAssetRevision(SilkEnvironmentData[] domes)
    {
        if (domes.Length == 0)
        {
            return string.Empty;
        }

        var builder = new StringBuilder(64);
        foreach (SilkEnvironmentData dome in domes)
        {
            SilkEnvironmentAssetStamp stamp = _environmentStampReader(dome.TexturePath);
            builder.Append(dome.TexturePath).Append('\u001f');
            builder.Append(stamp.Length.ToString(CultureInfo.InvariantCulture)).Append('\u001f');
            builder.Append(stamp.LastWriteUtcTicks.ToString(CultureInfo.InvariantCulture));
            builder.Append('\u001e');
        }
        return builder.ToString();
    }

    /// <summary>
    /// Uploads the prefiltered environment maps and the BRDF table, once per
    /// rebuild.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Separate from <see cref="BindEnvironment"/> because an upload is a copy
    /// and a copy cannot be recorded inside a rendering scope on any backend,
    /// while the binding has to happen per draw inside one. This is the same
    /// split the material textures already use.
    /// </para>
    /// <para>
    /// Recording a copy is not performing one. The upload is marked
    /// <em>pending</em> here and committed only by
    /// <see cref="CommitPendingUploads"/>, after the submission that carries it
    /// has been waited on. A command list that is abandoned, or a submission that
    /// fails, leaves textures whose contents were never written -- and a flag set
    /// at record time would have said they were, so every later frame would have
    /// skipped the upload and sampled undefined memory as the sky.
    /// </para>
    /// </remarks>
    internal void UploadEnvironment(ISilkGraphicsCommandList commands)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(commands);
        if (_environmentMapsUploaded ||
            _environmentMapsUploadPending ||
            _environmentIrradianceTexture is null ||
            _environmentSpecularTexture is null ||
            _environmentPayload is null)
        {
            return;
        }

        // The BRDF table is scene independent, but it is created and uploaded
        // here rather than eagerly: a stage with no textured dome never reads it,
        // and allocating a table for every scene that will not use one is a cost
        // with no image behind it.
        if (!_environmentBrdfUploaded && !_environmentBrdfUploadPending)
        {
            commands.UploadTexture(RequireEnvironmentBrdfTexture(), SilkEnvironmentBrdf.Pixels);
            _environmentBrdfUploadPending = true;
        }
        commands.UploadTexture(
            _environmentIrradianceTexture,
            _environmentPayload.IrradiancePixels);
        commands.UploadTexture(
            _environmentSpecularTexture,
            _environmentPayload.SpecularPixels);
        _environmentMapsUploadPending = true;
        _environmentPendingUploadBytes = _environmentPayload.ByteSize;
    }

    /// <summary>
    /// Marks every recorded upload as performed, after its submission completed.
    /// </summary>
    /// <remarks>
    /// Called once the submission carrying the recorded copies has been waited
    /// on, which is the first moment the texture contents are known to exist.
    /// </remarks>
    internal void CommitPendingUploads()
    {
        if (_environmentBrdfUploadPending)
        {
            _environmentBrdfUploaded = true;
            _environmentBrdfUploadPending = false;
            _environmentUploadBytes += SilkEnvironmentBrdf.ByteSize;
        }
        if (_environmentMapsUploadPending)
        {
            _environmentMapsUploaded = true;
            _environmentMapsUploadPending = false;
            _environmentUploadBytes += _environmentPendingUploadBytes;
            _environmentPendingUploadBytes = 0;
        }
    }

    /// <summary>
    /// Drops every recorded-but-unperformed upload so the next frame records it
    /// again.
    /// </summary>
    /// <remarks>
    /// The recorded copies are gone with the command list that carried them, and
    /// the textures they targeted still hold whatever they held before. Clearing
    /// the pending marks -- and leaving the committed ones alone -- is what makes
    /// the retry an upload rather than a bind of undefined memory.
    /// </remarks>
    internal void AbandonPendingUploads()
    {
        _environmentBrdfUploadPending = false;
        _environmentMapsUploadPending = false;
        _environmentPendingUploadBytes = 0;
    }

    /// <summary>
    /// Binds the prefiltered environment resources and their samplers for one draw.
    /// </summary>
    /// <remarks>
    /// Always bound, because the checked mesh fragment references every slot in
    /// every permutation and a backend pipeline layout requires every declared
    /// descriptor to be populated. A frame with no live environment binds the same
    /// one-texel stand-in for the two maps and never samples them, because the
    /// frame constants report the environment as disabled.
    /// </remarks>
    internal void BindEnvironment(ISilkGraphicsCommandList commands)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(commands);
        ISilkGraphicsTexture irradiance = _environmentIrradianceTexture ??
            RequireEnvironmentStandIn();
        ISilkGraphicsTexture specular = _environmentSpecularTexture ??
            RequireEnvironmentStandIn();
        commands.SetSampler(
            0,
            SilkBindingLayoutDescriptor.EnvironmentSamplerBinding,
            RequireEnvironmentSampler());
        commands.SetTexture(
            0,
            SilkBindingLayoutDescriptor.EnvironmentIrradianceTextureBinding,
            irradiance);
        commands.SetTexture(
            0,
            SilkBindingLayoutDescriptor.EnvironmentSpecularTextureBinding,
            specular);
        commands.SetSampler(
            0,
            SilkBindingLayoutDescriptor.EnvironmentBrdfSamplerBinding,
            RequireEnvironmentBrdfSampler());
        commands.SetTexture(
            0,
            SilkBindingLayoutDescriptor.EnvironmentBrdfTextureBinding,
            _environmentBrdfTexture ?? RequireEnvironmentStandIn());
    }

    /// <summary>
    /// Preflights every candidate once, then builds or reuses the prefiltered
    /// environment of the domes that survived.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Two phases, and both are single-pass. The first describes each candidate --
    /// which never decodes -- and refuses, with a diagnostic against the dome that
    /// caused it, any image over the per-image ceiling and any image that would
    /// take the composed set over the aggregate ceiling. The second streams only
    /// the survivors, decoding each exactly once.
    /// </para>
    /// <para>
    /// It used to restart the whole resolve whenever one dome failed, which
    /// re-decoded every dome before it: three valid domes followed by a corrupt
    /// one cost six decodes rather than four, and the cost grew quadratically in
    /// the number of broken assets. A dome that fails during the stream is now
    /// skipped in place and recorded, so no source is ever opened twice, and the
    /// identity the result is retained under is recomposed from the domes that
    /// were actually consumed rather than the ones that were offered.
    /// </para>
    /// </remarks>
    private SilkEnvironmentMaps? BuildEnvironmentMaps(
        List<SilkEnvironmentData> candidates,
        bool perDomeGroups,
        out string identity,
        out bool settled)
    {
        identity = string.Empty;
        settled = true;
        PreflightEnvironmentCandidates(candidates);
        if (candidates.Count == 0)
        {
            return null;
        }

        identity = ComposeEnvironmentIdentity(candidates, perDomeGroups);
        if (_environmentLighting.TryGet(identity) is { } cached)
        {
            return cached;
        }

        // The grouped bake is a multiple of the composed one, so a byte budget
        // that admits the composed environment can still refuse the groups. The
        // exact subset that survives is the composed sky itself: the scene keeps
        // its directional response and loses only the per-dome *selection* of it,
        // which is named rather than left to be inferred from a flat image. It is
        // checked here, before the decode stream is opened, because refusing after
        // the decode would pay for a traversal the caller then discards.
        if (perDomeGroups)
        {
            try
            {
                _environmentPrefilterOptions.ValidateGroupBudget(candidates.Count + 1);
            }
            catch (SilkEnvironmentBudgetExceededException exception)
            {
                AddDiagnostic(
                    SilkRenderDiagnosticCodes.EnvironmentDomeLinkUnavailable,
                    string.Empty,
                    RenderDiagnosticSeverity.Warning,
                    "The per-dome prefiltered environment groups this scene's " +
                    $"UsdLux dome linking needs do not fit: {exception.Message} " +
                    "Every prim receives the composed sky of every dome, so a " +
                    "dome's collection:lightLink no longer selects which sky it " +
                    "reflects. Its ambient contribution is still masked.");
                perDomeGroups = false;
                identity = ComposeEnvironmentIdentity(candidates, perDomeGroups: false);
                if (_environmentLighting.TryGet(identity) is { } composed)
                {
                    return composed;
                }
            }
        }

        var stream = new EnvironmentSourceStream(this, candidates);
        SilkEnvironmentMaps maps;
        try
        {
            maps = SilkEnvironmentPrefilter.Build(
                stream,
                _environmentPrefilterOptions,
                perDomeGroups);
            _environmentLighting.CountBuild();
        }
        catch (ArgumentException) when (stream.SkippedIndices.Count == candidates.Count)
        {
            // Every candidate was skipped, so the prefilter was handed an empty
            // set and refused it. Each dome has already been diagnosed against its
            // own prim, and all of them fall back; the refusal itself is not a
            // second failure to report.
            foreach (SilkEnvironmentData skipped in candidates)
            {
                _ = _environmentPrefilterSkipped.Add(skipped.Path);
            }
            EnvironmentDecodeCount += stream.DecodeCount;
            EnvironmentDecodedBytes += stream.DecodedBytes;
            candidates.Clear();
            identity = string.Empty;
            return null;
        }
        catch (Exception exception) when (
            exception is FileNotFoundException or DirectoryNotFoundException or
                IOException or InvalidDataException or OutOfMemoryException or
                OverflowException or SilkEnvironmentBudgetExceededException)
        {
            // The prefilter itself refused the composed set, so no one dome is
            // answerable for it and the whole environment falls back; the
            // mean-radiance path names each dome in turn. An exhaustion or an
            // overflow is a property of this attempt rather than of the scene, so
            // it is not settled and the next resolve tries again.
            settled = exception is not (OutOfMemoryException or OverflowException);
            EnvironmentDecodeCount += stream.DecodeCount;
            EnvironmentDecodedBytes += stream.DecodedBytes;
            candidates.Clear();
            identity = string.Empty;
            return null;
        }
        EnvironmentDecodeCount += stream.DecodeCount;
        EnvironmentDecodedBytes += stream.DecodedBytes;

        // Every dome the stream could not read has already been diagnosed against
        // its own prim. Removing them here is what keeps the retained identity a
        // description of the payload rather than of the request.
        if (stream.SkippedIndices.Count > 0)
        {
            for (int index = candidates.Count - 1; index >= 0; index--)
            {
                if (stream.SkippedIndices.Contains(index))
                {
                    // Recorded so that the mean-radiance fallback can contradict
                    // it: a source the prefilter could not read and the fallback
                    // then reads was unavailable transiently, not unreadable, and
                    // the directional response must not be settled on that.
                    _ = _environmentPrefilterSkipped.Add(candidates[index].Path);
                    candidates.RemoveAt(index);
                }
            }
            if (candidates.Count == 0)
            {
                identity = string.Empty;
                return null;
            }
            identity = ComposeEnvironmentIdentity(candidates, perDomeGroups);
            if (_environmentLighting.TryGet(identity) is { } reused)
            {
                return reused;
            }
        }

        try
        {
            _environmentLighting.Add(identity, maps);
        }
        catch (SilkEnvironmentBudgetExceededException)
        {
            // Retaining it is what exceeded the budget, not producing it. The
            // payload is still correct, so the frame uses it and simply does not
            // keep it; the next revision rebuilds.
        }
        return maps;
    }

    /// <summary>Composes the cache identity of one accepted candidate set.</summary>
    private string ComposeEnvironmentIdentity(
        List<SilkEnvironmentData> candidates,
        bool perDomeGroups)
    {
        var stamps = new SilkEnvironmentAssetStamp[candidates.Count];
        for (int index = 0; index < candidates.Count; index++)
        {
            stamps[index] = _environmentStampReader(candidates[index].TexturePath);
        }
        return SilkEnvironmentIdentity.Compose(
            _environmentIdentityContext,
            candidates,
            stamps,
            _environmentPrefilterOptions,
            perDomeGroups);
    }

    /// <summary>
    /// Removes, and diagnoses, every candidate whose described size is over the
    /// per-image ceiling or would take the set over the aggregate ceiling.
    /// </summary>
    /// <remarks>
    /// Describing is not decoding, so this is the cheap phase and it runs to
    /// completion before a single pixel is produced. A dome refused here keeps its
    /// mean-radiance ambient term, which is named against its own prim: a set that
    /// is collectively too large costs the scene the domes that did not fit, not
    /// the directionality of the ones that did.
    /// <para>
    /// When no describer is available nothing can be preflighted, and the
    /// post-decode checks inside the stream are the whole of the bound. That is
    /// stated rather than worked around: a decoder-only harness has no way to
    /// learn an image's size without decoding it.
    /// </para>
    /// </remarks>
    private void PreflightEnvironmentCandidates(List<SilkEnvironmentData> candidates)
    {
        SilkEnvironmentPrefilterOptions options = _environmentPrefilterOptions;
        ulong aggregate = 0;
        for (int index = 0; index < candidates.Count; index++)
        {
            SilkEnvironmentData dome = candidates[index];
            SilkImageDescription? description = TryDescribeEnvironment(dome.TexturePath);
            if (description is not { } shape)
            {
                continue;
            }

            ulong preflight;
            try
            {
                preflight = SilkEnvironmentPrefilter.EstimateDecodedBytes(shape);
            }
            catch (Exception exception) when (
                exception is InvalidDataException or OverflowException)
            {
                // A shape that cannot even be multiplied out is over every budget
                // this renderer states, so it is refused as one rather than
                // escaping the frame as an arithmetic fault.
                AddEnvironmentPrefilterDiagnostic(
                    SilkRenderDiagnosticCodes.EnvironmentBudgetExceeded,
                    dome.Path,
                    $"Dome light '{dome.Path}' declares an environment texture " +
                    $"whose decoded size cannot be represented: {exception.Message} " +
                    "It falls back to its mean-radiance ambient term.");
                candidates.RemoveAt(index--);
                continue;
            }

            if (preflight > options.MaximumSourceBytes)
            {
                AddEnvironmentPrefilterDiagnostic(
                    SilkRenderDiagnosticCodes.EnvironmentBudgetExceeded,
                    dome.Path,
                    $"Dome light '{dome.Path}' declares an environment texture that " +
                    $"decodes to {preflight.ToString(CultureInfo.InvariantCulture)} bytes, " +
                    "which exceeds the " +
                    $"{options.MaximumSourceBytes.ToString(CultureInfo.InvariantCulture)}" +
                    " byte per-image environment budget; it falls back to its " +
                    "mean-radiance ambient term.");
                candidates.RemoveAt(index--);
                continue;
            }

            ulong projected = preflight > ulong.MaxValue - aggregate
                ? ulong.MaxValue
                : aggregate + preflight;
            if (projected > options.MaximumAggregateSourceBytes)
            {
                AddEnvironmentPrefilterDiagnostic(
                    SilkRenderDiagnosticCodes.EnvironmentBudgetExceeded,
                    dome.Path,
                    $"Dome light '{dome.Path}' would take the composed environment to " +
                    $"{projected.ToString(CultureInfo.InvariantCulture)} decoded bytes, " +
                    "which exceeds the " +
                    $"{options.MaximumAggregateSourceBytes.ToString(CultureInfo.InvariantCulture)}" +
                    " byte aggregate environment budget; it falls back to its " +
                    "mean-radiance ambient term.");
                candidates.RemoveAt(index--);
                continue;
            }
            aggregate = projected;
        }
    }

    /// <summary>
    /// Opens each accepted dome's decoded image in turn, one at a time, and
    /// records the ones it could not read.
    /// </summary>
    /// <remarks>
    /// <para>
    /// It is an enumerable rather than an array on purpose. The prefilter
    /// accumulates one source into the shared world lattice and then moves on, so
    /// yielding lazily means exactly one decoded image is reachable at a time --
    /// four 4K float environments materialized together would be a gigabyte of
    /// transient allocation for one frame.
    /// </para>
    /// <para>
    /// A dome that cannot be decoded, that is malformed, or whose decoded bytes
    /// contradict the shape its describer reported is skipped in place and
    /// recorded, rather than aborting the enumeration. That is what lets one
    /// broken asset cost the scene one dome instead of a restart that re-decodes
    /// every source before it.
    /// </para>
    /// </remarks>
    private sealed class EnvironmentSourceStream(
        SilkSceneGpuResources owner,
        List<SilkEnvironmentData> candidates)
        : IEnumerable<SilkEnvironmentSource>
    {
        /// <summary>Gets the candidate indices whose sources could not be read.</summary>
        internal HashSet<int> SkippedIndices { get; } = [];

        /// <summary>Gets the number of decodes this stream performed.</summary>
        internal int DecodeCount { get; private set; }

        /// <summary>Gets the decoded bytes this stream produced in total.</summary>
        internal ulong DecodedBytes { get; private set; }

        public IEnumerator<SilkEnvironmentSource> GetEnumerator()
        {
            SkippedIndices.Clear();
            DecodeCount = 0;
            DecodedBytes = 0;
            for (int index = 0; index < candidates.Count; index++)
            {
                SilkEnvironmentSource? source = TryOpen(index);
                if (source is { } opened)
                {
                    yield return opened;
                }
            }
        }

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() =>
            GetEnumerator();

        /// <summary>
        /// Decodes and validates one candidate, or records why it could not be.
        /// </summary>
        /// <remarks>
        /// Separated from the iterator because a <c>yield return</c> cannot sit
        /// inside a <c>try</c> that has a <c>catch</c>. Returning null rather than
        /// throwing is what keeps a broken dome from ending the enumeration and
        /// costing every dome after it its directional response.
        /// </remarks>
        private SilkEnvironmentSource? TryOpen(int index)
        {
            SilkEnvironmentData dome = candidates[index];
            SilkEnvironmentPrefilterOptions options = owner._environmentPrefilterOptions;
            try
            {
                SilkImageDescription? description =
                    owner.TryDescribeEnvironment(dome.TexturePath);
                SilkColorSpace colorSpace = SilkEnvironmentMeanRadiance.ResolveColorSpace(
                    dome.SourceColorSpace,
                    description,
                    description?.Format ?? SilkTextureFormat.Rgba32Float);
                // Counted before the call, so it counts decode *attempts*. That is
                // what proves no prefix is re-read: a decode that threw still
                // opened and traversed the file.
                DecodeCount++;
                SilkDecodedImage image = owner._imageDecoder(dome.TexturePath, false);
                ulong decodedBytes = checked((ulong)image.Pixels.LongLength);
                DecodedBytes = checked(DecodedBytes + decodedBytes);

                // Re-checked after the decode. The preflight ran against the
                // describer's shape, and a describer and a decoder can disagree;
                // the bytes that were actually produced are the ones the budget has
                // to hold. It is also the whole of the bound when no describer
                // exists to preflight against.
                if (decodedBytes > options.MaximumSourceBytes)
                {
                    throw new SilkEnvironmentBudgetExceededException(
                        dome.TexturePath,
                        decodedBytes,
                        options.MaximumSourceBytes);
                }
                if (DecodedBytes > options.MaximumAggregateSourceBytes)
                {
                    throw new SilkEnvironmentBudgetExceededException(
                        dome.TexturePath,
                        DecodedBytes,
                        options.MaximumAggregateSourceBytes);
                }

                // Validated here, with the candidate index still in hand, so one
                // malformed or non-finite dome can be dropped without taking the
                // directional response away from the valid ones.
                SilkEnvironmentPrefilter.ValidateSource(image, colorSpace);

                return new SilkEnvironmentSource(
                    image,
                    colorSpace,
                    dome.LightToWorld,
                    dome.AmbientEmissionScale,
                    dome.SpecularEmissionScale);
            }
            catch (SilkEnvironmentBudgetExceededException exception)
            {
                SkippedIndices.Add(index);
                owner.AddEnvironmentPrefilterDiagnostic(
                    SilkRenderDiagnosticCodes.EnvironmentBudgetExceeded,
                    dome.Path,
                    $"Dome light '{dome.Path}' exceeded an environment decode " +
                    $"budget: {exception.Message} It falls back to its " +
                    "mean-radiance ambient term.");
                return null;
            }
            catch (Exception exception) when (
                exception is OutOfMemoryException or OverflowException)
            {
                // An image the process cannot hold, or one whose byte count does
                // not fit the accumulator, is over every budget this renderer
                // states. Naming it as one keeps the dome on its untextured
                // emission instead of failing the frame.
                SkippedIndices.Add(index);
                owner.AddEnvironmentPrefilterDiagnostic(
                    SilkRenderDiagnosticCodes.EnvironmentBudgetExceeded,
                    dome.Path,
                    $"Dome light '{dome.Path}' could not be decoded within the " +
                    $"environment budget: {exception.Message} It falls back to its " +
                    "mean-radiance ambient term.");
                return null;
            }
            catch (Exception exception) when (
                exception is FileNotFoundException or DirectoryNotFoundException or
                    IOException or InvalidDataException or NotSupportedException)
            {
                SkippedIndices.Add(index);
                owner.AddEnvironmentPrefilterDiagnostic(
                    SilkRenderDiagnosticCodes.EnvironmentDecodeFailed,
                    dome.Path,
                    $"Dome light '{dome.Path}' could not read its environment " +
                    $"texture: {exception.Message} It falls back to its " +
                    "mean-radiance ambient term.");
                return null;
            }
        }
    }

    /// <summary>
    /// Allocates every GPU object the enabled environment needs, or none of them.
    /// </summary>
    /// <remarks>
    /// The two maps, the BRDF table and the two samplers are one transaction.
    /// They used to be three: the maps here, and the table and samplers created
    /// lazily on first upload and first bind. That let a device refuse the table
    /// <em>after</em> the frame constants had already declared the environment
    /// enabled, which is a state with no correct rendering -- the shader would
    /// read a one-texel stand-in as its split-sum table and light every surface
    /// from it -- and it surfaced the refusal from inside a render rather than
    /// from the prepare that a caller can fall back from. Allocating all five
    /// under one guard means an enablement either has every resource it declares
    /// or does not happen, and the caller keeps the mean-radiance fallback.
    /// </remarks>
    private void EnsureEnvironmentTextures(
        SilkEnvironmentMaps maps,
        string identity,
        List<SilkEnvironmentData> composed)
    {
        if (_environmentIrradianceTexture is not null &&
            _environmentSpecularTexture is not null &&
            _environmentBrdfTexture is not null &&
            _environmentSampler is not null &&
            _environmentBrdfSampler is not null &&
            string.Equals(_environmentUploadedIdentity, identity, StringComparison.Ordinal))
        {
            // The payload is already resident, but which dome bit reads which
            // group is a property of the scene rather than of the payload: the
            // same composed set can be republished under different dome indices
            // when the scene''s dome ordering moves. Re-resolving here keeps the
            // binding truthful without a rebuild.
            UpdateEnvironmentBinding(maps, composed);
            return;
        }

        ISilkGraphicsTexture? irradiance = null;
        ISilkGraphicsTexture? specular = null;
        ISilkGraphicsTexture? brdf = null;
        ISilkGraphicsSampler? sampler = null;
        ISilkGraphicsSampler? brdfSampler = null;
        try
        {
            irradiance = _device.CreateTexture2D(new SilkTextureDescriptor(
                maps.IrradianceWidth,
                maps.IrradianceAtlasHeight,
                SilkEnvironmentMaps.Format,
                EnvironmentTextureUsage));
            specular = _device.CreateTexture2D(new SilkTextureDescriptor(
                maps.SpecularWidth,
                maps.SpecularAtlasHeight,
                SilkEnvironmentMaps.Format,
                EnvironmentTextureUsage));
            if (_environmentBrdfTexture is null)
            {
                brdf = _device.CreateTexture2D(new SilkTextureDescriptor(
                    SilkEnvironmentBrdf.Size,
                    SilkEnvironmentBrdf.Size,
                    SilkEnvironmentMaps.Format,
                    EnvironmentTextureUsage));
            }
            if (_environmentSampler is null)
            {
                sampler = _device.CreateSampler(EnvironmentSamplerDescriptor);
            }
            if (_environmentBrdfSampler is null)
            {
                brdfSampler = _device.CreateSampler(SilkSamplerDescriptor.LinearClamp);
            }
        }
        catch
        {
            // Only the objects this call created are disposed. A sampler that was
            // already resident belongs to an earlier, successful transaction and
            // is still bound by the stand-in path.
            brdfSampler?.Dispose();
            sampler?.Dispose();
            brdf?.Dispose();
            specular?.Dispose();
            irradiance?.Dispose();
            throw;
        }

        _environmentIrradianceTexture?.Dispose();
        _environmentSpecularTexture?.Dispose();
        _environmentIrradianceTexture = irradiance;
        _environmentSpecularTexture = specular;
        if (brdf is not null)
        {
            _environmentBrdfTexture = brdf;
            _environmentBrdfUploaded = false;
            _environmentBrdfUploadPending = false;
        }
        _environmentSampler ??= sampler;
        _environmentBrdfSampler ??= brdfSampler;
        _environmentPayload = maps;
        _environmentUploadedIdentity = identity;
        _environmentMapsUploaded = false;
        // A recorded upload of the payload this call just replaced targets a
        // texture that no longer exists, so it is dropped rather than committed.
        _environmentMapsUploadPending = false;
        _environmentPendingUploadBytes = 0;
        UpdateEnvironmentBinding(maps, composed);
    }

    /// <summary>
    /// Publishes the environment block, including which group each dome bit reads.
    /// </summary>
    /// <remarks>
    /// The dome-to-group table is indexed by the dome bit hdSilk assigned, not by
    /// the position a dome happens to occupy in the composed set: a dome the
    /// prefilter refused holds a bit and no group, and its bit must therefore
    /// resolve to no group rather than to whichever composed dome inherited its
    /// index. A dome the page could not give a bit to has nothing to record here
    /// at all and reaches every prim through the composed group.
    /// </remarks>
    private void UpdateEnvironmentBinding(
        SilkEnvironmentMaps maps,
        List<SilkEnvironmentData> composed)
    {
        SilkDomeGroupTable groups = SilkDomeGroupTable.Empty;
        if (maps.GroupCount > 1)
        {
            for (int index = 0; index < composed.Count; index++)
            {
                SilkEnvironmentData dome = composed[index];
                if (!dome.HasDomeIndex || dome.DomeIndex >= SilkFrameCommand.MaximumDomes)
                {
                    continue;
                }
                groups = groups.WithGroup((int)dome.DomeIndex, (uint)index);
            }
        }

        var binding = new SilkEnvironmentFrameBinding(
            true,
            maps.SpecularSliceCount,
            maps.SpecularSliceHeight,
            _environmentAuthoredSceneLighting,
            maps.GroupCount,
            maps.ComposedGroup,
            maps.IrradianceHeight,
            groups);
        if (_environmentBinding == binding)
        {
            return;
        }
        _environmentBinding = binding;
        _environmentBindingRevision++;
    }

    private const SilkTextureUsage EnvironmentTextureUsage =
        SilkTextureUsage.Sampled |
        SilkTextureUsage.CopySource |
        SilkTextureUsage.CopyDestination;

    private void ReleaseEnvironmentMaps()
    {
        _environmentIrradianceTexture?.Dispose();
        _environmentIrradianceTexture = null;
        _environmentSpecularTexture?.Dispose();
        _environmentSpecularTexture = null;
        _environmentPayload = null;
        _environmentUploadedIdentity = null;
        _environmentMapsUploaded = false;
        _environmentMapsUploadPending = false;
        _environmentPendingUploadBytes = 0;

        // The authored flag survives, because releasing the maps is this
        // renderer's verdict on the dome and not the author's. A scene whose only
        // light is a dome the prefilter refused is still a lit scene, and must
        // still not acquire a headlight.
        var released = new SilkEnvironmentFrameBinding(
            false,
            0,
            0,
            _environmentAuthoredSceneLighting);
        if (_environmentBinding != released)
        {
            _environmentBinding = released;
            _environmentBindingRevision++;
        }
    }

    /// <summary>
    /// Records whether the scene authors any dome light at all, independently of
    /// whether one was resolved.
    /// </summary>
    private void SetEnvironmentAuthoredSceneLighting(bool authored)
    {
        if (_environmentAuthoredSceneLighting == authored)
        {
            return;
        }
        _environmentAuthoredSceneLighting = authored;
        _environmentBinding = _environmentBinding with { AuthoredSceneLighting = authored };
        _environmentBindingRevision++;
    }

    /// <summary>
    /// Releases every GPU object the environment owns, which is what a device
    /// loss requires.
    /// </summary>
    /// <remarks>
    /// The two maps are the obvious half. The two samplers, the one-texel stand-in
    /// and the BRDF table are the half that is easy to miss: they are created once
    /// and reused across every scene edit, so a release that only dropped the maps
    /// would rebind objects belonging to a device that no longer exists.
    /// </remarks>
    private void ReleaseEnvironmentDeviceResources()
    {
        ReleaseEnvironmentMaps();
        _environmentStandIn?.Dispose();
        _environmentStandIn = null;
        _environmentSampler?.Dispose();
        _environmentSampler = null;
        _environmentBrdfSampler?.Dispose();
        _environmentBrdfSampler = null;
        _environmentBrdfTexture?.Dispose();
        _environmentBrdfTexture = null;
        _environmentBrdfUploaded = false;
        _environmentBrdfUploadPending = false;
    }

    /// <summary>
    /// Forces the mean-radiance fallback to be re-resolved when the set of domes
    /// the prefiltered environment carries has changed.
    /// </summary>
    /// <remarks>
    /// The fallback is cached against the environment revision alone, which is
    /// correct while the lit set is stable and wrong the moment it is not: a dome
    /// that stopped being prefiltered has to start contributing its ambient term
    /// on the same frame, and one that started has to stop.
    /// </remarks>
    private void InvalidateEnvironmentAmbient(HashSet<string> previousLit)
    {
        if (previousLit.SetEquals(_environmentLitDomes))
        {
            return;
        }
        _environmentsResolved = false;
    }

    private ISilkGraphicsSampler RequireEnvironmentSampler() =>
        _environmentSampler ??= _device.CreateSampler(EnvironmentSamplerDescriptor);

    private ISilkGraphicsSampler RequireEnvironmentBrdfSampler() =>
        _environmentBrdfSampler ??=
            _device.CreateSampler(SilkSamplerDescriptor.LinearClamp);

    private ISilkGraphicsTexture RequireEnvironmentBrdfTexture() =>
        _environmentBrdfTexture ??= _device.CreateTexture2D(new SilkTextureDescriptor(
            SilkEnvironmentBrdf.Size,
            SilkEnvironmentBrdf.Size,
            SilkEnvironmentMaps.Format,
            EnvironmentTextureUsage));

    /// <summary>
    /// The sampler both environment maps are read through: linear filtering,
    /// wrapping in U and clamping in V.
    /// </summary>
    /// <remarks>
    /// The address modes are not interchangeable. Longitude is periodic, so a
    /// clamped U leaves a visible seam down the back of every reflection; latitude
    /// is not, so a wrapped V folds the north pole onto the south one and puts the
    /// sky under the floor. The BRDF table gets its own clamped sampler instead,
    /// because it is a function on a bounded square and a wrapped incidence of one
    /// would read the grazing end of the table.
    /// </remarks>
    private static SilkSamplerDescriptor EnvironmentSamplerDescriptor => new(
        SilkSamplerFilter.Linear,
        SilkSamplerFilter.Linear,
        SilkSamplerAddressMode.Repeat,
        SilkSamplerAddressMode.ClampToEdge,
        SilkSamplerAddressMode.ClampToEdge);

    private ISilkGraphicsTexture RequireEnvironmentStandIn()
    {
        if (_environmentStandIn is not null)
        {
            return _environmentStandIn;
        }

        _environmentStandIn = _device.CreateTexture2D(new SilkTextureDescriptor(
            1,
            1,
            SilkEnvironmentMaps.Format,
            EnvironmentTextureUsage));
        return _environmentStandIn;
    }

    /// <summary>
    /// Gets the number of retained per-material, per-mask surface constant
    /// blocks.
    /// </summary>
    /// <remarks>
    /// Exposed so the eviction that follows a link-table edit is gated by
    /// counting rather than by inspection: a cache that kept every mask it ever
    /// resolved is indistinguishable from one that keeps only the live masks in
    /// every observable except this.
    /// </remarks>
    internal int SurfaceBufferCount => _surfaceBuffers.Count;

    /// <summary>
    /// Drops the state a previous link table left behind, whenever the retained
    /// table changes.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Two kinds of state outlive a table that is edited, repaired or retired.
    /// The first is the diagnostics the previous table produced: a truncated
    /// table, a dome budget it over-ran, a prim it linked that binds a generated
    /// MaterialX fragment. Those are re-emitted by the very next draw that still
    /// warrants them, so clearing them here is what makes a repaired scene stop
    /// warning instead of warning forever about a table that no longer exists.
    /// </para>
    /// <para>
    /// The second is the per-mask surface constant blocks. Those are cached by
    /// (material, packed masks), and a live-edited collection walks through many
    /// masks: without eviction a stage whose author drags a light through a
    /// hierarchy accumulates one retained buffer per mask it ever resolved, and
    /// nothing ever drops them because the material never changed. Only the masks
    /// the current table can still return survive.
    /// </para>
    /// </remarks>
    /// <summary>
    /// Observes the retained link table's revision, dropping the diagnostics and
    /// the per-mask surface blocks a previous table produced.
    /// </summary>
    /// <remarks>
    /// Called once per page and once per frame rather than only from the draw
    /// loop, so that a scene with nothing drawable still retires what its last
    /// table left behind.
    /// </remarks>
    internal void ObserveLightLinks(SilkSceneState scene)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(scene);
        ObserveLightLinkRevision(scene);
    }

    private void ObserveLightLinkRevision(SilkSceneState scene)
    {
        if (_surfaceLinkRevision == scene.LightLinks.Revision)
        {
            return;
        }

        _surfaceLinkRevision = scene.LightLinks.Revision;
        RemoveDiagnostics(static code =>
            code is SilkRenderDiagnosticCodes.LightLinkTruncated or
                SilkRenderDiagnosticCodes.LightLinkDomeBudget or
                SilkRenderDiagnosticCodes.LightLinkGeneratedShaderUnsupported);

        scene.LightLinks.CollectPackedMasks(_liveLinkMasks);
        List<SurfaceBufferKey>? stale = null;
        foreach (SurfaceBufferKey key in _surfaceBuffers.Keys)
        {
            if (!_liveLinkMasks.Contains(key.Masks))
            {
                (stale ??= []).Add(key);
            }
        }
        if (stale is null)
        {
            return;
        }
        foreach (SurfaceBufferKey key in stale)
        {
            if (_surfaceBuffers.Remove(key, out SurfaceBuffer surface))
            {
                surface.Buffer?.Dispose();
            }
        }
    }

    /// <summary>
    /// Returns the surface constants for one material path, creating and uploading
    /// the block on first use and reusing it afterwards.
    /// </summary>
    /// <remarks>
    /// Keyed by material rather than by mesh because the constants are a property
    /// of the material, and because a per-mesh block would allocate for every prim
    /// in a scene that shares one material. Meshes with no supported material share
    /// a single default block whose shaded flag is zero, so slot 7 is always bound:
    /// leaving it unbound renders correctly on D3D12 and Vulkan and produces
    /// nothing at all on Metal.
    /// </remarks>
    internal ISilkGraphicsBuffer RequireSurfaceBuffer(
        SilkSceneState scene,
        SilkMeshData mesh,
        RenderHeadlight light)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(mesh);
        ObserveLightLinkRevision(scene);
        SilkLightLinkMasks masks = scene.LightLinks.Resolve(mesh.Path, mesh.InstanceIndex);
        uint packedMasks = PackLinkMasks(masks);
        if (scene.LightLinks.UnsupportedFeatures.HasFlag(SilkLightLinkUnsupportedFeatures.Truncated))
        {
            AddDiagnostic(
                SilkRenderDiagnosticCodes.LightLinkTruncated,
                string.Empty,
                RenderDiagnosticSeverity.Warning,
                "The hdSilk light link table exceeded the page budget of " +
                $"{SilkLightLinkCommand.MaximumEntries} entries. Prims that did not " +
                "fit are lit by every light regardless of their authored collections.");
        }
        if (scene.LightLinks.UnsupportedFeatures.HasFlag(
            SilkLightLinkUnsupportedFeatures.DomeBudget))
        {
            AddDiagnostic(
                SilkRenderDiagnosticCodes.LightLinkDomeBudget,
                string.Empty,
                RenderDiagnosticSeverity.Warning,
                "The scene authors more dome lights than the " +
                $"{SilkFrameCommand.MaximumDomes.ToString(CultureInfo.InvariantCulture)} " +
                "the page dome table admits, so hdSilk published no dome link " +
                "mask. Every dome light illuminates every prim regardless of its " +
                "authored collection:lightLink.");
        }
        SilkMaterialData? material = null;
        string path = mesh.MaterialPath;
        if (!string.IsNullOrEmpty(path))
        {
            _ = scene.Materials.TryGetValue(path, out material);
        }

        if (material is not { IsSupported: true })
        {
            if (!string.IsNullOrEmpty(path))
            {
                if (material is null)
                {
                    AddDiagnostic(
                        SilkRenderDiagnosticCodes.MaterialUnresolved,
                        path,
                        RenderDiagnosticSeverity.Warning,
                        $"Mesh material '{path}' is not present in retained scene state; default shading was used.");
                }
                else if (material.IsMdlUnavailable)
                {
                    AddDiagnostic(
                        SilkRenderDiagnosticCodes.MaterialMdlUnavailable,
                        path,
                        RenderDiagnosticSeverity.Warning,
                        $"Material '{path}' binds an MDL-only surface this runtime " +
                        "did not distil, so default shading was used. Install the " +
                        "optional openusd_mdl adapter, or author a UsdPreviewSurface " +
                        "or MaterialX context on the material.");
                }
                else
                {
                    AddDiagnostic(
                        SilkRenderDiagnosticCodes.MaterialUnsupported,
                        path,
                        RenderDiagnosticSeverity.Warning,
                        $"Material '{path}' uses unsupported surface kind " +
                        $"{material.SurfaceKind}; default shading was used.");
                }
            }

            var defaultKey = new SurfaceBufferKey(string.Empty, packedMasks);
            if (_surfaceBuffers.TryGetValue(defaultKey, out SurfaceBuffer defaultSurface) &&
                defaultSurface.Buffer is { } retainedDefault)
            {
                return retainedDefault;
            }

            ISilkGraphicsBuffer createdDefault = CreateSurfaceBuffer(null, light, masks);
            _surfaceBuffers[defaultKey] = new SurfaceBuffer(createdDefault, 0);
            return createdDefault;
        }

        var key = new SurfaceBufferKey(material.Path, packedMasks);
        if (masks != SilkLightLinkMasks.All &&
            material.SurfaceKind == SilkSurfaceKind.MaterialXGenerated)
        {
            // The generated fragment carries MaterialX's own lighting, not the
            // checked permutation's frame light loop, so the mask packed into the
            // block below is never read by it. Naming that is the only honest
            // option: the prim is lit by every light whatever its collections say.
            AddDiagnostic(
                SilkRenderDiagnosticCodes.LightLinkGeneratedShaderUnsupported,
                mesh.Path,
                RenderDiagnosticSeverity.Warning,
                $"Prim '{mesh.Path}' authors UsdLux light, shadow or dome linking but binds " +
                $"material '{material.Path}', which is drawn through a " +
                "runtime-generated MaterialX fragment. That fragment does not read " +
                "the per-draw light or dome mask, so the prim is lit by every light.");
        }
        if (_surfaceBuffers.TryGetValue(key, out SurfaceBuffer existing) &&
            existing.Buffer is { } retained)
        {
            if (existing.MaterialHash != material.StableHash)
            {
                // The material changed in place, so refresh the block rather than
                // allocating a second buffer for the same path.
                WriteSurface(retained, material, light, masks);
                _surfaceBuffers[key] = new SurfaceBuffer(retained, material.StableHash);
            }

            return retained;
        }

        ISilkGraphicsBuffer created = CreateSurfaceBuffer(material, light, masks);
        _surfaceBuffers[key] = new SurfaceBuffer(created, material.StableHash);
        return created;
    }

    /// <summary>
    /// Folds the three link masks into the single key the surface block cache and
    /// the per-draw batch key both compare.
    /// </summary>
    /// <remarks>
    /// The dome mask is in the key, not merely in the block. Two prims that share
    /// a material but link different domes must not share a surface buffer or a
    /// draw: the dome mask is a per-draw constant, and batching them together
    /// would give both of them whichever mask was written last.
    /// </remarks>
    internal static uint PackLinkMasks(SilkLightLinkMasks masks) => masks.Packed;

    /// <summary>
    /// Drops every packed block one material owns, across every link mask it was
    /// drawn with, so a re-authored material cannot leave a stale block behind
    /// for one of its masks.
    /// </summary>
    private void RemoveSurfaceBuffers(string materialPath)
    {
        List<SurfaceBufferKey>? stale = null;
        foreach (SurfaceBufferKey key in _surfaceBuffers.Keys)
        {
            if (string.Equals(key.MaterialPath, materialPath, StringComparison.Ordinal))
            {
                (stale ??= []).Add(key);
            }
        }
        if (stale is null)
        {
            return;
        }
        foreach (SurfaceBufferKey key in stale)
        {
            if (_surfaceBuffers.Remove(key, out SurfaceBuffer surface))
            {
                surface.Buffer?.Dispose();
            }
        }
    }

    private ISilkGraphicsBuffer CreateSurfaceBuffer(
        SilkMaterialData? material,
        RenderHeadlight light,
        SilkLightLinkMasks masks)
    {
        ISilkGraphicsBuffer? buffer = null;
        try
        {
            buffer = CreateTrackedBuffer(
                SilkSurfaceUniformWriter.ByteSize,
                SilkBufferUsage.Storage | SilkBufferUsage.Upload);
            WriteSurface(buffer, material, light, masks);
            return buffer;
        }
        catch
        {
            buffer?.Dispose();
            throw;
        }
    }

    private void WriteSurface(
        ISilkGraphicsBuffer buffer,
        SilkMaterialData? material,
        RenderHeadlight light,
        SilkLightLinkMasks masks)
    {
        Span<byte> constants = stackalloc byte[SilkSurfaceUniformWriter.ByteSize];
        SilkSurfaceUniformWriter.Write(
            material,
            light,
            constants,
            _device is ISilkVolumeTextureGraphicsDevice,
            masks);
        WriteTracked(buffer, constants);
    }

    internal void BindMaterialTexture(
        ISilkGraphicsCommandList commands,
        SilkMaterialData material,
        SilkMaterialParameter parameter) =>
        BindMaterialTexture(commands, material, parameter, parameter);

    /// <summary>
    /// Binds the material's single composite image, or the supplied stand-in when
    /// the material has none.
    /// </summary>
    /// <remarks>
    /// The slot is declared by every MAP_MATERIAL pipeline because the checked
    /// binary references it in every one of them, and a D3D12 root signature
    /// requires every declared descriptor to be populated. A material with no
    /// composite therefore binds the same stand-in the unused material slots bind;
    /// the shader never samples it, because the composite target written into the
    /// surface constants matches no slot bit.
    /// </remarks>
    internal void BindCompositeTexture(
        ISilkGraphicsCommandList commands,
        SilkMaterialData material,
        SilkMaterialParameter standInParameter)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(commands);
        ArgumentNullException.ThrowIfNull(material);
        SilkMaterialTexture texture = material.GetCompositeTexture() ??
            material.GetTexture(standInParameter) ??
            throw new InvalidDataException(
                $"Material '{material.Path}' has no texture for {standInParameter}.");
        BindTextureEntry(
            commands,
            material.Path,
            texture,
            SilkBindingLayoutDescriptor.CompositeSamplerBinding,
            SilkBindingLayoutDescriptor.CompositeTextureBinding);
    }

    /// <summary>Uploads the material's composite image when it has one.</summary>
    internal void UploadCompositeTexture(
        ISilkGraphicsCommandList commands,
        SilkMaterialData material)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(commands);
        ArgumentNullException.ThrowIfNull(material);
        if (material.GetCompositeTexture() is not { } texture)
        {
            return;
        }
        UploadTextureEntry(commands, RequireTexture(material.Path, texture));
    }

    internal void BindMaterialTexture(
        ISilkGraphicsCommandList commands,
        SilkMaterialData material,
        SilkMaterialParameter parameter,
        SilkMaterialParameter bindingParameter)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(commands);
        ArgumentNullException.ThrowIfNull(material);
        SilkMaterialTexture texture = material.GetTexture(parameter) ??
            throw new InvalidDataException(
                $"Material '{material.Path}' has no texture for {parameter}.");
        (uint samplerBinding, uint textureBinding) =
            SilkBindingLayoutDescriptor.GetMaterialTextureBindings(bindingParameter);
        BindTextureEntry(commands, material.Path, texture, samplerBinding, textureBinding);
    }

    private void BindTextureEntry(
        ISilkGraphicsCommandList commands,
        string materialPath,
        SilkMaterialTexture texture,
        uint samplerBinding,
        uint textureBinding)
    {
        TextureCacheEntry entry = RequireTexture(materialPath, texture);
        if (!entry.Uploaded)
        {
            commands.UploadTexture(entry.Texture, entry.Pixels);
            _textureUploadBytes += checked((ulong)entry.Pixels.Length);
            entry.Uploaded = true;
        }
        commands.SetSampler(
            0,
            samplerBinding,
            RequireSampler(
                texture,
                entry.Texture.Format,
                entry.IsUdim,
                entry.Texture.MipLevelCount));
        commands.SetTexture(0, textureBinding, entry.Texture);
    }

    internal void BindVolumeDensityTexture(
        ISilkGraphicsCommandList commands,
        SilkMaterialData material)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(commands);
        ArgumentNullException.ThrowIfNull(material);
        SilkMaterialTexture texture = material.GetTexture(SilkMaterialParameter.VolumeDensity) ??
            throw new InvalidDataException(
                $"Material '{material.Path}' has no sampled density volume.");
        if (commands is not ISilkVolumeTextureCommandList volumeCommands ||
            _device is not ISilkVolumeTextureGraphicsDevice)
        {
            return;
        }
        TextureCacheEntry entry = RequireVolumeTexture(texture);
        if (!entry.Uploaded)
        {
            volumeCommands.UploadTexture3D(entry.Texture, entry.Pixels);
            _textureUploadBytes += checked((ulong)entry.Pixels.Length);
            entry.Uploaded = true;
        }
        commands.SetSampler(
            0,
            SilkBindingLayoutDescriptor.VolumeSamplerBinding,
            RequireSampler(texture, entry.Texture.Format));
        commands.SetTexture(
            0,
            SilkBindingLayoutDescriptor.VolumeDensityTextureBinding,
            entry.Texture);
    }

    internal void UploadVolumeDensityTexture(
        ISilkGraphicsCommandList commands,
        SilkMaterialData material)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(commands);
        ArgumentNullException.ThrowIfNull(material);
        SilkMaterialTexture? texture = material.GetTexture(SilkMaterialParameter.VolumeDensity);
        if (texture is null ||
            commands is not ISilkVolumeTextureCommandList volumeCommands ||
            _device is not ISilkVolumeTextureGraphicsDevice)
        {
            return;
        }
        TextureCacheEntry entry = RequireVolumeTexture(texture);
        if (!entry.Uploaded)
        {
            volumeCommands.UploadTexture3D(entry.Texture, entry.Pixels);
            _textureUploadBytes += checked((ulong)entry.Pixels.Length);
            entry.Uploaded = true;
        }
    }

    internal void UploadMaterialTexture(
        ISilkGraphicsCommandList commands,
        SilkMaterialData material,
        SilkMaterialParameter parameter)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(commands);
        ArgumentNullException.ThrowIfNull(material);
        SilkMaterialTexture texture = material.GetTexture(parameter) ??
            throw new InvalidDataException(
                $"Material '{material.Path}' has no texture for {parameter}.");
        UploadTextureEntry(commands, RequireTexture(material.Path, texture));
    }

    private void UploadTextureEntry(
        ISilkGraphicsCommandList commands,
        TextureCacheEntry entry)
    {
        if (entry.Uploaded)
        {
            return;
        }
        commands.UploadTexture(entry.Texture, entry.Pixels);
        _textureUploadBytes += checked((ulong)entry.Pixels.Length);
        entry.Uploaded = true;
    }

    private TextureCacheEntry RequireTexture(
        string materialPath,
        SilkMaterialTexture texture)
    {
        SilkColorSpace effectiveColorSpace = GetEffectiveColorSpace(texture);
        // The channel is part of the identity: one packed occlusion/roughness/
        // metallic file feeds two inputs from two different channels, and each
        // needs its own swizzled copy. Two entries for one asset is the honest
        // cost of that, and it never silently merges two different channels.
        // The composite operator is part of the identity as well as the channel:
        // a two-image input publishes two entries for one parameter, and the two
        // may name the same asset with the same channel while carrying different
        // folded affines. Without it the second entry would serve the first's
        // decoded pixels.
        var key = new TextureCacheKey(
            materialPath,
            texture.Asset,
            effectiveColorSpace,
            texture.Parameter,
            texture.Channel,
            texture.CompositeOperator);
        if (_textures.TryGetValue(key, out TextureCacheEntry? entry))
        {
            if (!DependenciesChanged(entry.Dependencies))
            {
                TouchEntry(entry);
                return entry;
            }
            _textures.Remove(key);
            DisposeEntry(entry);
        }
        if (_failedTextures.TryGetValue(key, out entry))
        {
            TouchEntry(entry);
            return entry;
        }

        SilkDecodedImage image;
        string[] dependencies = [texture.Asset];
        try
        {
            image = texture.Asset.Contains("<UDIM>", StringComparison.Ordinal)
                ? CreateUdimAtlas(texture, effectiveColorSpace, out dependencies)
                : DecodeMaterialImage(texture.Asset, texture, effectiveColorSpace);
        }
        catch (FileNotFoundException exception)
        {
            return CreateFailedTexture(
                key,
                materialPath,
                texture,
                SilkRenderDiagnosticCodes.TextureAssetNotFound,
                exception.Message);
        }
        catch (DirectoryNotFoundException exception)
        {
            return CreateFailedTexture(
                key,
                materialPath,
                texture,
                SilkRenderDiagnosticCodes.TextureAssetNotFound,
                exception.Message);
        }
        catch (InvalidDataException exception)
        {
            return CreateFailedTexture(
                key,
                materialPath,
                texture,
                SilkRenderDiagnosticCodes.TextureDecodeFailed,
                exception.Message);
        }
        return CreateTextureEntry(
            key,
            image,
            _textures,
            texture.Asset.Contains("<UDIM>", StringComparison.Ordinal),
            dependencies,
            texture.Parameter == SilkMaterialParameter.Normal);
    }

    private TextureCacheEntry CreateFailedTexture(
        TextureCacheKey key,
        string materialPath,
        SilkMaterialTexture texture,
        string code,
        string detail)
    {
        AddDiagnostic(
            code,
            string.Concat(materialPath, "\0", texture.Asset),
            RenderDiagnosticSeverity.Warning,
            $"Material '{materialPath}' texture '{texture.Asset}' failed: {detail}");
        AddDiagnostic(
            SilkRenderDiagnosticCodes.TextureFallbackUsed,
            string.Concat(materialPath, "\0", texture.Asset, "\0", texture.Parameter),
            RenderDiagnosticSeverity.Warning,
            $"Material '{materialPath}' used the authored fallback for {texture.Parameter} texture '{texture.Asset}'.");
        return CreateTextureEntry(
            key,
            CreateFallbackImage(texture),
            _failedTextures,
            isUdim: false,
            dependencies: null,
            isNormalMap: texture.Parameter == SilkMaterialParameter.Normal);
    }

    private TextureCacheEntry CreateTextureEntry(
        TextureCacheKey key,
        SilkDecodedImage image,
        Dictionary<TextureCacheKey, TextureCacheEntry> cache,
        bool isUdim = false,
        IReadOnlyList<string>? dependencies = null,
        bool isNormalMap = false)
    {
        ISilkGraphicsTexture? gpuTexture = null;
        try
        {
            // UDIM atlases carry sparse per-tile metadata and gutter padding that a naive box
            // filter would corrupt across tile boundaries, so they always stay single-level in
            // this slice. Ordinary material images generate a full CPU mip chain instead.
            byte[] pixels = image.Pixels;
            uint mipLevelCount = 1;
            if (!isUdim)
            {
                pixels = SilkMipGenerator.GenerateChain(
                    image.Pixels,
                    image.Width,
                    image.Height,
                    image.Format,
                    isNormalMap,
                    out mipLevelCount);
            }
            gpuTexture = _device.CreateTexture2D(
                new SilkTextureDescriptor(
                    image.Width,
                    image.Height,
                    image.Format,
                    SilkTextureUsage.Sampled |
                        SilkTextureUsage.CopySource |
                        SilkTextureUsage.CopyDestination,
                    mipLevelCount));
            var entry = new TextureCacheEntry(
                gpuTexture,
                pixels,
                checked((ulong)pixels.Length),
                isUdim,
                CaptureDependencies(dependencies));
            RegisterEntry(entry);
            cache.Add(key, entry);
            return entry;
        }
        catch
        {
            gpuTexture?.Dispose();
            throw;
        }
    }

    private SilkDecodedImage DecodeMaterialImage(
        string asset,
        SilkMaterialTexture texture,
        SilkColorSpace effectiveColorSpace)
    {
        SilkDecodedImage image = _imageDecoder(
            asset,
            effectiveColorSpace == SilkColorSpace.Srgb);
        ValidateDecodedImage(image);
        FlipRows(image.Pixels, image.Width, image.Height, image.Format);
        ApplyScaleBias(image.Pixels, image.Format, texture);
        ApplyOutputChannel(image.Pixels, image.Format, texture);
        return image;
    }

    private SilkDecodedImage CreateUdimAtlas(
        SilkMaterialTexture texture,
        SilkColorSpace effectiveColorSpace,
        out string[] dependencies)
    {
        IReadOnlyList<SilkUdimTile> tiles = _udimResolver(texture.Asset);
        if (tiles.Count == 0)
        {
            throw new FileNotFoundException(
                $"UDIM texture '{texture.Asset}' resolved no tiles.",
                texture.Asset);
        }
        dependencies = tiles.Select(static tile => tile.Asset).ToArray();

        SilkDecodedImage[] images = new SilkDecodedImage[tiles.Count];
        int minU = int.MaxValue;
        int minV = int.MaxValue;
        int maxU = int.MinValue;
        int maxV = int.MinValue;
        for (int index = 0; index < tiles.Count; index++)
        {
            SilkUdimTile tile = tiles[index];
            int offset = checked((int)tile.Number - 1001);
            int u = offset % 10;
            int v = offset / 10;
            minU = Math.Min(minU, u);
            minV = Math.Min(minV, v);
            maxU = Math.Max(maxU, u);
            maxV = Math.Max(maxV, v);
            images[index] = DecodeMaterialImage(
                tile.Asset,
                texture,
                effectiveColorSpace);
            if (index != 0 &&
                (images[index].Width != images[0].Width ||
                 images[index].Height != images[0].Height ||
                 images[index].Format != images[0].Format))
            {
                throw new InvalidDataException(
                    $"UDIM texture '{texture.Asset}' tiles must have identical dimensions and formats.");
            }
        }

        int columns = checked(maxU - minU + 1);
        int rows = checked(maxV - minV + 1);
        if (checked(columns * rows) > MaximumUdimAtlasCells)
        {
            throw new InvalidDataException(
                $"UDIM texture '{texture.Asset}' spans more than {MaximumUdimAtlasCells} atlas cells.");
        }

        SilkDecodedImage first = images[0];
        int bytesPerPixel = checked((int)SilkTextureFormats.GetBytesPerPixel(first.Format));
        int cellWidth = checked((int)first.Width + 2);
        int cellHeight = checked((int)first.Height + 2);
        int atlasWidth = checked(columns * cellWidth);
        int atlasHeight = checked(1 + (rows * cellHeight));
        byte[] fallback = CreateFallbackPixel(texture, first.Format);
        byte[] pixels = new byte[checked(atlasWidth * atlasHeight * bytesPerPixel)];
        for (int offset = 0; offset < pixels.Length; offset += bytesPerPixel)
        {
            fallback.CopyTo(pixels, offset);
        }
        WriteUdimMetadata(pixels, first.Format, minU, minV, columns, rows);

        for (int index = 0; index < tiles.Count; index++)
        {
            int tileOffset = checked((int)tiles[index].Number - 1001);
            int cellX = (tileOffset % 10) - minU;
            int cellY = (tileOffset / 10) - minV;
            CopyUdimTile(
                images[index],
                pixels,
                atlasWidth,
                cellX * cellWidth,
                1 + (cellY * cellHeight));
        }
        return new SilkDecodedImage(
            checked((uint)atlasWidth),
            checked((uint)atlasHeight),
            pixels,
            first.Format);
    }

    private static TextureDependency[] CaptureDependencies(
        IReadOnlyList<string>? dependencies)
    {
        if (dependencies is null || dependencies.Count == 0)
        {
            return [];
        }
        var result = new List<TextureDependency>(dependencies.Count);
        foreach (string asset in dependencies)
        {
            var file = new FileInfo(asset);
            if (file.Exists)
            {
                result.Add(new TextureDependency(
                    asset,
                    file.Length,
                    file.LastWriteTimeUtc));
            }
        }
        return result.ToArray();
    }

    private static bool DependenciesChanged(TextureDependency[]? dependencies)
    {
        if (dependencies is null)
        {
            return false;
        }
        foreach (TextureDependency dependency in dependencies)
        {
            var file = new FileInfo(dependency.Asset);
            if (!file.Exists ||
                file.Length != dependency.Length ||
                file.LastWriteTimeUtc != dependency.LastWriteTimeUtc)
            {
                return true;
            }
        }
        return false;
    }

    private static byte[] CreateFallbackPixel(
        SilkMaterialTexture texture,
        SilkTextureFormat format)
    {
        if (format == SilkTextureFormat.Rgba32Float)
        {
            float[] values = new float[4];
            for (int component = 0; component < values.Length; component++)
            {
                float source = component < texture.Fallback.Count
                    ? texture.Fallback[component]
                    : component == 3 ? 1 : 0;
                values[component] =
                    (source * texture.Scale[component]) + texture.Bias[component];
                if (!float.IsFinite(values[component]))
                {
                    throw new InvalidDataException(
                        "UDIM fallback scale and bias produced a non-finite channel.");
                }
            }
            byte[] bytes = MemoryMarshal.AsBytes(values.AsSpan()).ToArray();
            ApplyOutputChannel(bytes, format, texture);
            return bytes;
        }
        return CreateFallbackImage(texture).Pixels;
    }

    private static void WriteUdimMetadata(
        byte[] pixels,
        SilkTextureFormat format,
        int minU,
        int minV,
        int columns,
        int rows)
    {
        Span<float> values = stackalloc float[4]
        {
            minU / 255f,
            minV / 255f,
            columns / 255f,
            rows / 255f
        };
        if (format == SilkTextureFormat.Rgba32Float)
        {
            MemoryMarshal.AsBytes(values).CopyTo(pixels);
            return;
        }
        for (int component = 0; component < 4; component++)
        {
            pixels[component] = checked((byte)MathF.Round(values[component] * 255));
        }
    }

    private static void CopyUdimTile(
        SilkDecodedImage image,
        byte[] atlas,
        int atlasWidth,
        int cellX,
        int cellY)
    {
        int bytesPerPixel = checked((int)SilkTextureFormats.GetBytesPerPixel(image.Format));
        int tileWidth = checked((int)image.Width);
        int tileHeight = checked((int)image.Height);
        int atlasStride = checked(atlasWidth * bytesPerPixel);
        int tileStride = checked(tileWidth * bytesPerPixel);
        for (int y = 0; y < tileHeight; y++)
        {
            int source = y * tileStride;
            int destination = checked(
                ((cellY + y + 1) * atlasStride) + ((cellX + 1) * bytesPerPixel));
            Buffer.BlockCopy(image.Pixels, source, atlas, destination, tileStride);
            Buffer.BlockCopy(image.Pixels, source, atlas, destination - bytesPerPixel, bytesPerPixel);
            Buffer.BlockCopy(
                image.Pixels,
                source + tileStride - bytesPerPixel,
                atlas,
                destination + tileStride,
                bytesPerPixel);
        }
        int paddedStride = checked((tileWidth + 2) * bytesPerPixel);
        int firstRow = checked(cellY * atlasStride + cellX * bytesPerPixel);
        int lastRow = checked((cellY + tileHeight + 1) * atlasStride + cellX * bytesPerPixel);
        Buffer.BlockCopy(atlas, firstRow + atlasStride, atlas, firstRow, paddedStride);
        Buffer.BlockCopy(atlas, lastRow - atlasStride, atlas, lastRow, paddedStride);
    }

    private TextureCacheEntry RequireVolumeTexture(SilkMaterialTexture texture)
    {
        if (_volumeTextures.TryGetValue(texture.Asset, out TextureCacheEntry? entry))
        {
            TouchEntry(entry);
            return entry;
        }
        if (_device is not ISilkVolumeTextureGraphicsDevice volumeDevice)
        {
            throw new NotSupportedException(
                "The current backend does not support sampled volume textures.");
        }
        SilkVolumeTextureExtent info = SilkVolumeTextureExtent.Parse(texture.UvPrimvar);
        byte[] pixels = File.ReadAllBytes(texture.Asset);
        int requiredLength =
            checked((int)(info.Width * info.Height * info.Depth * sizeof(float)));
        if (pixels.Length != requiredLength)
        {
            throw new InvalidDataException(
                $"Volume texture '{texture.Asset}' contains {pixels.Length} bytes, expected {requiredLength}.");
        }
        ISilkGraphicsTexture? gpuTexture = null;
        try
        {
            gpuTexture = volumeDevice.CreateTexture3D(
                info.Width,
                info.Height,
                info.Depth,
                SilkTextureFormat.R32Float);
            entry = new TextureCacheEntry(gpuTexture, pixels, checked((ulong)pixels.Length));
            RegisterEntry(entry);
            _volumeTextures.Add(texture.Asset, entry);
            return entry;
        }
        catch
        {
            gpuTexture?.Dispose();
            throw;
        }
    }

    private static SilkDecodedImage CreateFallbackImage(SilkMaterialTexture texture)
    {
        byte[] pixels = new byte[4];
        for (int component = 0; component < 4; component++)
        {
            float value = component < texture.Fallback.Count
                ? texture.Fallback[component]
                : component == 3 ? 1 : 0;
            value = (value * texture.Scale[component]) + texture.Bias[component];
            pixels[component] = (byte)Math.Clamp(MathF.Round(value * 255), 0, 255);
        }
        // The authored fallback is a float4 read through the same output port as the
        // texel would have been, so the same channel selection applies to it.
        ApplyOutputChannel(pixels, SilkTextureFormat.Rgba8Unorm, texture);
        return new SilkDecodedImage(1, 1, pixels);
    }

    private static void FlipRows(
        byte[] pixels,
        uint width,
        uint height,
        SilkTextureFormat format)
    {
        int stride = checked((int)(width * SilkTextureFormats.GetBytesPerPixel(format)));
        byte[] row = new byte[stride];
        int last = checked((int)height) - 1;
        for (int y = 0; y < height / 2; y++)
        {
            int top = y * stride;
            int bottom = (last - y) * stride;
            Buffer.BlockCopy(pixels, top, row, 0, stride);
            Buffer.BlockCopy(pixels, bottom, pixels, top, stride);
            Buffer.BlockCopy(row, 0, pixels, bottom, stride);
        }
    }

    private static void ApplyScaleBias(
        byte[] pixels,
        SilkTextureFormat format,
        SilkMaterialTexture texture)
    {
        if (format == SilkTextureFormat.Rgba32Float)
        {
            Span<float> values = MemoryMarshal.Cast<byte, float>(pixels.AsSpan());
            for (int offset = 0; offset < values.Length; offset += 4)
            {
                for (int component = 0; component < 4; component++)
                {
                    values[offset + component] =
                        (values[offset + component] * texture.Scale[component]) +
                        texture.Bias[component];
                    if (!float.IsFinite(values[offset + component]))
                    {
                        throw new InvalidDataException(
                            "Decoded texture scale and bias produced a non-finite channel.");
                    }
                }
            }
            return;
        }
        if (format != SilkTextureFormat.Rgba8Unorm)
        {
            throw new InvalidDataException(
                $"Decoded material texture format {format} does not support scale and bias.");
        }
        for (int offset = 0; offset < pixels.Length; offset += 4)
        {
            for (int component = 0; component < 4; component++)
            {
                float value = pixels[offset + component] / 255f;
                value = (value * texture.Scale[component]) + texture.Bias[component];
                pixels[offset + component] =
                    (byte)Math.Clamp(MathF.Round(value * 255), 0, 255);
            }
        }
    }

    /// <summary>
    /// Replicates the connected UsdUVTexture output channel into every component so
    /// a scalar map is read from one canonical place by every shader permutation.
    /// </summary>
    /// <remarks>
    /// UsdUVTexture applies <c>scale</c> and <c>bias</c> to the sampled texel and only
    /// then exposes the per-channel outputs, so this runs after
    /// <see cref="ApplyScaleBias"/> and reads the already-scaled value. Doing the
    /// selection on the CPU avoids a per-parameter channel index in the surface
    /// block that every backend and checked shader would otherwise have to carry.
    /// <see cref="SilkTextureChannel.Rgb"/> is a no-op, so colour and vector maps
    /// (base colour, emissive, normal) keep their full RGBA. Preview Surface
    /// opacity remains an independent material input.
    /// </remarks>
    private static void ApplyOutputChannel(
        byte[] pixels,
        SilkTextureFormat format,
        SilkMaterialTexture texture)
    {
        if (texture.Channel == SilkTextureChannel.Rgb)
        {
            return;
        }
        int source = (int)texture.Channel;
        if (source is < 0 or > 3)
        {
            throw new InvalidDataException(
                $"Material texture channel {texture.Channel} is not a known UsdUVTexture output.");
        }
        if (format == SilkTextureFormat.Rgba32Float)
        {
            Span<float> values = MemoryMarshal.Cast<byte, float>(pixels.AsSpan());
            for (int offset = 0; offset < values.Length; offset += 4)
            {
                float selected = values[offset + source];
                values[offset] = selected;
                values[offset + 1] = selected;
                values[offset + 2] = selected;
                values[offset + 3] = selected;
            }
            return;
        }
        if (format != SilkTextureFormat.Rgba8Unorm)
        {
            throw new InvalidDataException(
                $"Decoded material texture format {format} does not support channel selection.");
        }
        for (int offset = 0; offset < pixels.Length; offset += 4)
        {
            byte selected = pixels[offset + source];
            pixels[offset] = selected;
            pixels[offset + 1] = selected;
            pixels[offset + 2] = selected;
            pixels[offset + 3] = selected;
        }
    }

    private static void ValidateDecodedImage(SilkDecodedImage image)
    {
        if (image.Width == 0 || image.Height == 0)
        {
            throw new InvalidDataException("Decoded texture dimensions must be non-zero.");
        }
        if (image.Format is not (
            SilkTextureFormat.Rgba8Unorm or
            SilkTextureFormat.Rgba32Float))
        {
            throw new InvalidDataException(
                $"Decoded material texture format {image.Format} is unsupported.");
        }
        int expectedLength = checked(
            (int)(image.Width *
                image.Height *
                SilkTextureFormats.GetBytesPerPixel(image.Format)));
        if (image.Pixels.Length != expectedLength)
        {
            throw new InvalidDataException(
                $"Decoded texture contains {image.Pixels.Length} bytes; expected {expectedLength}.");
        }
    }

    private static SilkColorSpace GetEffectiveColorSpace(SilkMaterialTexture texture) =>
        texture.SourceColorSpace switch
        {
            SilkColorSpace.Raw => SilkColorSpace.Raw,
            SilkColorSpace.Srgb => SilkColorSpace.Srgb,
            SilkColorSpace.Auto => texture.Parameter is SilkMaterialParameter.DiffuseColor or
                SilkMaterialParameter.EmissiveColor or
                SilkMaterialParameter.SpecularColor
                    ? SilkColorSpace.Srgb
                    : SilkColorSpace.Raw,
            _ => throw new ArgumentOutOfRangeException(nameof(texture))
        };

    private ISilkGraphicsSampler RequireSampler(
        SilkMaterialTexture texture,
        SilkTextureFormat format,
        bool isUdim = false,
        uint mipLevelCount = 1)
    {
        SilkSamplerAddressMode addressU = isUdim
            ? SilkSamplerAddressMode.ClampToEdge
            : GetAddressMode(texture.WrapS);
        SilkSamplerAddressMode addressV = isUdim
            ? SilkSamplerAddressMode.ClampToEdge
            : GetAddressMode(texture.WrapT);
        SilkSamplerFilter filter = format == SilkTextureFormat.Rgba32Float
            ? SilkSamplerFilter.Nearest
            : SilkSamplerFilter.Linear;
        // Anisotropic filtering only helps a linearly filtered, actually mipmapped, ordinary
        // (non-UDIM) material texture; UDIM atlases, single-level volume density textures, and
        // nearest-only Rgba32Float sampling all fall through to the 1x descriptor default. The
        // device capability is the hard ceiling: never request more than it advertises.
        float maxAnisotropy = 1f;
        if (!isUdim &&
            mipLevelCount > 1 &&
            filter == SilkSamplerFilter.Linear &&
            _device.Capabilities.MaxSamplerAnisotropy > 1f)
        {
            maxAnisotropy = Math.Min(_device.Capabilities.MaxSamplerAnisotropy, MaxMaterialAnisotropy);
        }
        var descriptor = new SilkSamplerDescriptor(
            filter,
            filter,
            addressU,
            addressV,
            SilkSamplerAddressMode.ClampToEdge,
            maxAnisotropy);
        if (_samplers.TryGetValue(descriptor, out ISilkGraphicsSampler? sampler))
        {
            return sampler;
        }
        sampler = _device.CreateSampler(descriptor);
        _samplers.Add(descriptor, sampler);
        return sampler;
    }

    /// <summary>
    /// Maps a wire wrap mode onto a sampler address mode.
    /// </summary>
    /// <remarks>
    /// <see cref="SilkTextureWrap.Black"/> resolves to clamp-to-edge rather than
    /// to a border colour: the wire carries no border colour, so there is nothing
    /// to hand a backend that supports one. It therefore renders identically to
    /// <see cref="SilkTextureWrap.Clamp"/>, and a sample outside the unit range
    /// returns the edge texel. This is the documented approximation for
    /// UsdUVTexture's <c>black</c> wrap and MaterialX <c>constant</c> addressing.
    /// <see cref="SilkTextureWrap.UseMetadata"/> resolves the same way for a
    /// different reason: this renderer reads no wrap metadata out of an image
    /// file, and USD's documented fallback when no metadata is present is
    /// <c>black</c>. The vertex-stage displacement sampler, which owns its own
    /// addressing, implements both exactly instead; see
    /// <c>SilkDisplacementField</c>.
    /// </remarks>
    private static SilkSamplerAddressMode GetAddressMode(SilkTextureWrap wrap) =>
        wrap switch
        {
            SilkTextureWrap.Repeat => SilkSamplerAddressMode.Repeat,
            SilkTextureWrap.Mirror => SilkSamplerAddressMode.MirrorRepeat,
            SilkTextureWrap.Clamp or
                SilkTextureWrap.Black or
                SilkTextureWrap.UseMetadata => SilkSamplerAddressMode.ClampToEdge,
            _ => throw new ArgumentOutOfRangeException(nameof(wrap))
        };

    private void ClearTextureCache()
    {
        foreach (TextureCacheEntry entry in _textures.Values)
        {
            DisposeEntry(entry);
        }
        _textures.Clear();
        ClearFailedTextureCache();
        foreach (TextureCacheEntry entry in _volumeTextures.Values)
        {
            DisposeEntry(entry);
        }
        _volumeTextures.Clear();
    }

    private void ClearFailedTextureCache()
    {
        foreach (TextureCacheEntry entry in _failedTextures.Values)
        {
            DisposeEntry(entry);
        }
        _failedTextures.Clear();
    }

    /// <summary>
    /// Disposes the cached ordinary/UDIM/fallback texture entries belonging to materials this
    /// delta changed, rather than the entire ordinary/UDIM/fallback texture cache, and
    /// unconditionally disposes <i>every</i> retained volume density texture entry on any
    /// material change. Editing one material's texture assignment must not force every other
    /// still-resident material's decoded/uploaded texture work to be redone next frame, which
    /// would defeat both the retained CPU budget (see <see cref="TextureCacheEntry.Pixels"/>) and
    /// the frame working-set protection in <see cref="TrimTextureResidency"/>. Volume density
    /// textures are keyed by asset path rather than material path (see
    /// <see cref="_volumeTextures"/>), so they carry no material identity to prune selectively
    /// by; there is no way to tell which, if any, volume texture belongs to a changed material,
    /// so all of them are cleared and disposed here — the same coarse, whole-cache treatment
    /// <see cref="ClearTextureCache"/> gives them. This also guarantees a volume texture is never
    /// left stale after its owning material changes to reuse the same asset path with different
    /// dimensions: <see cref="RequireVolumeTexture"/> keys purely on asset path, so without this
    /// unconditional clear a dimension change alone could silently keep serving the old texture.
    /// </summary>
    private void RemoveChangedMaterialTextureCacheEntries(ReadOnlySpan<string> changedMaterialPaths)
    {
        if (changedMaterialPaths.Length == 0)
        {
            return;
        }
        var changed = new HashSet<string>(changedMaterialPaths.ToArray(), StringComparer.Ordinal);
        RemoveMatchingTextureCacheEntries(_textures, changed);
        RemoveMatchingTextureCacheEntries(_failedTextures, changed);
        foreach (TextureCacheEntry entry in _volumeTextures.Values)
        {
            DisposeEntry(entry);
        }
        _volumeTextures.Clear();
        foreach (string materialPath in changed)
        {
            RemoveTextureDiagnostics(materialPath);
        }
    }

    private void RemoveMatchingTextureCacheEntries(
        Dictionary<TextureCacheKey, TextureCacheEntry> cache,
        HashSet<string> changedMaterialPaths)
    {
        foreach (TextureCacheKey key in cache.Keys
            .Where(key => changedMaterialPaths.Contains(key.MaterialPath))
            .ToArray())
        {
            TextureCacheEntry entry = cache[key];
            cache.Remove(key);
            DisposeEntry(entry);
        }
    }

    private void PruneInactiveTextureFailures(SilkSceneState scene)
    {
        if (_failedTextures.Count == 0)
        {
            return;
        }
        if (_diagnostics.Values.Any(static diagnostic =>
                diagnostic.Code == SilkRenderDiagnosticCodes.CapacityExceeded))
        {
            ClearFailedTextureCache();
            RemoveTextureDiagnostics();
            return;
        }

        var activeMaterials = new HashSet<string>(
            scene.Meshes.Values
                .Select(static mesh => mesh.MaterialPath)
                .Where(static path => !string.IsNullOrEmpty(path)),
            StringComparer.Ordinal);
        foreach (TextureCacheKey key in _failedTextures.Keys
            .Where(key => !activeMaterials.Contains(key.MaterialPath))
            .ToArray())
        {
            TextureCacheEntry entry = _failedTextures[key];
            _failedTextures.Remove(key);
            DisposeEntry(entry);
            RemoveTextureDiagnostics(key.MaterialPath);
        }
    }

    /// <summary>Records a freshly created entry's initial LRU stamp and residency accounting.</summary>
    private void RegisterEntry(TextureCacheEntry entry)
    {
        entry.SequenceId = ++_textureEntrySequence;
        entry.LastUsedStamp = ++_textureUseClock;
        _decodedTextureResidentBytes = checked(_decodedTextureResidentBytes + entry.DecodedBytes);
        _gpuTextureResidentBytes = checked(_gpuTextureResidentBytes + entry.GpuBytes);
        if (_decodedTextureResidentBytes > _peakDecodedTextureResidentBytes)
        {
            _peakDecodedTextureResidentBytes = _decodedTextureResidentBytes;
        }
        if (_gpuTextureResidentBytes > _peakGpuTextureResidentBytes)
        {
            _peakGpuTextureResidentBytes = _gpuTextureResidentBytes;
        }
    }

    /// <summary>
    /// Bumps an existing entry's monotonically increasing last-use stamp on cache hit, marking it
    /// part of the current frame's working set and pinning it against eviction until it is no
    /// longer touched by a subsequent frame. See <see cref="TrimTextureResidency"/>.
    /// </summary>
    private void TouchEntry(TextureCacheEntry entry) => entry.LastUsedStamp = ++_textureUseClock;

    /// <summary>
    /// Disposes a texture cache entry's GPU texture and removes its residency accounting. The
    /// caller must have already removed the entry from whichever cache dictionary owned it.
    /// </summary>
    private void DisposeEntry(TextureCacheEntry entry)
    {
        _decodedTextureResidentBytes = checked(_decodedTextureResidentBytes - entry.DecodedBytes);
        _gpuTextureResidentBytes = checked(_gpuTextureResidentBytes - entry.GpuBytes);
        entry.Texture.Dispose();
    }

    /// <summary>
    /// Evicts least-recently-used ordinary, UDIM, fallback, and volume texture cache entries until
    /// decoded CPU and estimated GPU-resident bytes are both within the configured
    /// <see cref="SilkTextureResidencyOptions"/> budgets, or until only the current frame's
    /// working set remains.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Safety contract:</b> the caller must guarantee that no command list which may still
    /// reference a retained texture is unsubmitted, and that every graphics submission which used
    /// a currently retained texture has already completed — i.e. its
    /// <see cref="ISilkGraphicsSubmission.Wait"/> has returned — before calling this method.
    /// Disposing a texture while a command list has merely recorded, but not yet submitted or
    /// awaited, commands referencing it is unsafe on backends whose native resource release is
    /// deferred behind a submission lease. <see cref="SilkMeshRenderer"/> only calls this
    /// immediately after each relevant submission's <c>Wait()</c> has returned (both the
    /// single-mesh and grouped/instanced draw paths). Tests may call this directly only when no
    /// command list referencing scene textures is currently outstanding.
    /// </para>
    /// <para>
    /// <b>Frame working-set protection:</b> only entries whose last-use stamp is at or before the
    /// use-clock boundary recorded by the <i>preceding</i> call to this method are eviction
    /// candidates; every entry touched since then — i.e. referenced while recording the frame(s)
    /// since the last trim — is pinned and never evicted, no matter how far over budget the
    /// working set is. This is what prevents an over-budget current working set from being
    /// decoded, uploaded, evicted, and re-decoded every single frame: a texture referenced again
    /// this frame simply stays resident. On the very first call, the boundary is the clock's
    /// initial value, so every entry touched while assembling that first frame is itself the
    /// pinned working set. If the pinned working set alone still exceeds either budget once no
    /// stale candidate remains, eviction stops and a single bounded
    /// <see cref="SilkRenderDiagnosticCodes.TextureBudgetExceeded"/> diagnostic reports the
    /// violated budget(s), current bytes, and entry count rather than looping or thrashing.
    /// Failed-fallback texture entries are eligible for eviction only as a last resort, after
    /// every stale ordinary and volume candidate: evicting a tiny fallback placeholder only to
    /// immediately repeat its failed decode (and, for filesystem-backed assets, its failed file
    /// read) on the very next reference wastes work for no residency benefit. The use-clock
    /// boundary always advances to the current clock value before this method returns, regardless
    /// of whether eviction fully restored both budgets.
    /// </para>
    /// </remarks>
    internal void TrimTextureResidency()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_textures.Count == 0 && _failedTextures.Count == 0 && _volumeTextures.Count == 0)
        {
            if (_diagnostics.Count != 0)
            {
                RemoveDiagnostics(static code =>
                    code == SilkRenderDiagnosticCodes.TextureBudgetExceeded);
            }
            _textureUseClockBoundary = _textureUseClock;
            return;
        }

        ulong maxDecoded = _residencyOptions.MaxDecodedCpuBytes;
        ulong maxGpu = _residencyOptions.MaxGpuBytes;
        ulong staleBoundary = _textureUseClockBoundary;
        while (IsOverBudget(maxDecoded, maxGpu))
        {
            if (!TryFindStaleEvictionCandidate(staleBoundary, out EvictionCandidate candidate))
            {
                EmitWorkingSetBudgetDiagnostic(maxDecoded, maxGpu);
                break;
            }

            TextureCacheEntry victim = candidate.Entry;
            // Capture sizes before disposal: DisposeEntry mutates the residency accounting that a
            // diagnostic built from stale (post-disposal) totals would misreport.
            ulong victimDecodedBytes = victim.DecodedBytes;
            ulong victimGpuBytes = victim.GpuBytes;
            bool oversizeAlone = victimDecodedBytes > maxDecoded || victimGpuBytes > maxGpu;
            string identity = RemoveEvictionCandidate(candidate);
            DisposeEntry(victim);
            _textureEvictionCount++;

            if (oversizeAlone)
            {
                AddDiagnostic(
                    SilkRenderDiagnosticCodes.TextureBudgetExceeded,
                    identity,
                    RenderDiagnosticSeverity.Warning,
                    $"A single texture cache entry ({victimDecodedBytes} decoded / " +
                        $"{victimGpuBytes} GPU bytes) alone exceeded the configured residency " +
                        $"budget (max {maxDecoded} decoded / {maxGpu} GPU bytes) and was evicted " +
                        "rather than retained.");
            }
        }

        if (!IsOverBudget(maxDecoded, maxGpu))
        {
            RemoveWorkingSetBudgetDiagnostic();
        }
        _textureUseClockBoundary = _textureUseClock;
    }

    private bool IsOverBudget(ulong maxDecoded, ulong maxGpu) =>
        _decodedTextureResidentBytes > maxDecoded || _gpuTextureResidentBytes > maxGpu;

    /// <summary>
    /// Reports that the current frame's pinned working set alone violates one or both configured
    /// residency budgets, with no stale entry left to evict. Identity is a fixed string so the
    /// diagnostic stays a single bounded entry no matter how many frames repeat this condition;
    /// because <see cref="AddDiagnostic"/> is a no-op once that fixed key is already present, the
    /// previous emission is removed first so the byte totals and entry count in the message stay
    /// current for as long as the working set remains over budget, rather than freezing at
    /// whatever they were on the first over-budget frame.
    /// </summary>
    private void EmitWorkingSetBudgetDiagnostic(ulong maxDecoded, ulong maxGpu)
    {
        ulong decodedBytes = _decodedTextureResidentBytes;
        ulong gpuBytes = _gpuTextureResidentBytes;
        int entryCount = _textures.Count + _failedTextures.Count + _volumeTextures.Count;
        var violated = new List<string>(2);
        if (decodedBytes > maxDecoded)
        {
            violated.Add($"decoded CPU bytes {decodedBytes} exceed the {maxDecoded} byte budget");
        }
        if (gpuBytes > maxGpu)
        {
            violated.Add($"GPU bytes {gpuBytes} exceed the {maxGpu} byte budget");
        }
        RemoveWorkingSetBudgetDiagnostic();
        AddDiagnostic(
            SilkRenderDiagnosticCodes.TextureBudgetExceeded,
            WorkingSetBudgetDiagnosticIdentity,
            RenderDiagnosticSeverity.Warning,
            $"The current-frame texture working set ({entryCount} entries) alone violates the " +
                $"configured residency budget: {string.Join("; ", violated)}. No entry in the " +
                "working set was evicted because every one is still referenced by the frame(s) " +
                "recorded since the previous trim.");
    }

    private void RemoveWorkingSetBudgetDiagnostic() =>
        _diagnostics.Remove(
            string.Concat(SilkRenderDiagnosticCodes.TextureBudgetExceeded, "\0", WorkingSetBudgetDiagnosticIdentity));

    private const string WorkingSetBudgetDiagnosticIdentity = "current-frame-working-set";

    /// <summary>
    /// Finds the least-recently-used eviction candidate among entries not touched since
    /// <paramref name="staleBoundary"/>, preferring any stale ordinary or volume entry over a
    /// stale failed-fallback entry regardless of relative age. Returns <see langword="false"/> if
    /// no cache entry qualifies as stale, meaning every remaining entry is part of the pinned
    /// current-frame working set.
    /// </summary>
    private bool TryFindStaleEvictionCandidate(ulong staleBoundary, out EvictionCandidate candidate)
    {
        TextureCacheEntry? best = null;
        TextureCacheKey bestOrdinaryKey = default;
        string? bestVolumeKey = null;
        TextureCacheEntryKind bestKind = TextureCacheEntryKind.Ordinary;
        foreach (KeyValuePair<TextureCacheKey, TextureCacheEntry> pair in _textures)
        {
            if (pair.Value.LastUsedStamp <= staleBoundary && IsOlder(pair.Value, best))
            {
                best = pair.Value;
                bestOrdinaryKey = pair.Key;
                bestKind = TextureCacheEntryKind.Ordinary;
            }
        }
        foreach (KeyValuePair<string, TextureCacheEntry> pair in _volumeTextures)
        {
            if (pair.Value.LastUsedStamp <= staleBoundary && IsOlder(pair.Value, best))
            {
                best = pair.Value;
                bestVolumeKey = pair.Key;
                bestKind = TextureCacheEntryKind.Volume;
            }
        }
        if (best is not null)
        {
            candidate = new EvictionCandidate(bestKind, bestOrdinaryKey, bestVolumeKey, best);
            return true;
        }

        // Failed-fallback entries are eligible only as this last resort; see TrimTextureResidency.
        TextureCacheEntry? bestFailed = null;
        TextureCacheKey bestFailedKey = default;
        foreach (KeyValuePair<TextureCacheKey, TextureCacheEntry> pair in _failedTextures)
        {
            if (pair.Value.LastUsedStamp <= staleBoundary && IsOlder(pair.Value, bestFailed))
            {
                bestFailed = pair.Value;
                bestFailedKey = pair.Key;
            }
        }
        if (bestFailed is not null)
        {
            candidate = new EvictionCandidate(TextureCacheEntryKind.Failed, bestFailedKey, null, bestFailed);
            return true;
        }

        candidate = default;
        return false;
    }

    /// <summary>
    /// Removes an eviction candidate from the cache dictionary it belongs to and returns its
    /// stable diagnostic identity. Throws rather than substituting a placeholder key if a volume
    /// candidate's key is somehow missing, since silently masking that invariant violation would
    /// corrupt both cache bookkeeping and diagnostic identity.
    /// </summary>
    private string RemoveEvictionCandidate(EvictionCandidate candidate)
    {
        switch (candidate.Kind)
        {
            case TextureCacheEntryKind.Ordinary:
                _textures.Remove(candidate.OrdinaryKey);
                return string.Concat(candidate.OrdinaryKey.MaterialPath, "\0", candidate.OrdinaryKey.Asset);
            case TextureCacheEntryKind.Failed:
                _failedTextures.Remove(candidate.OrdinaryKey);
                return string.Concat(candidate.OrdinaryKey.MaterialPath, "\0", candidate.OrdinaryKey.Asset);
            case TextureCacheEntryKind.Volume:
                string volumeAsset = candidate.VolumeKey ??
                    throw new InvalidOperationException(
                        "A volume eviction candidate must carry its owning cache key.");
                _volumeTextures.Remove(volumeAsset);
                return volumeAsset;
            default:
                throw new InvalidOperationException(
                    $"Unrecognized texture cache eviction candidate kind '{candidate.Kind}'.");
        }
    }

    // Ties on the last-use stamp cannot occur in practice, since every touch draws from one
    // strictly increasing clock, but the creation-order sequence number is compared as a stable,
    // deterministic secondary key so eviction order is never dependent on dictionary enumeration.
    private static bool IsOlder(TextureCacheEntry candidate, TextureCacheEntry? current) =>
        current is null ||
        candidate.LastUsedStamp < current.LastUsedStamp ||
        (candidate.LastUsedStamp == current.LastUsedStamp && candidate.SequenceId < current.SequenceId);

    private void AddDiagnostic(
        string code,
        string identity,
        RenderDiagnosticSeverity severity,
        string message)
    {
        string key = string.Concat(code, "\0", identity);
        if (_diagnostics.ContainsKey(key))
        {
            return;
        }
        if (_diagnostics.Count < DiagnosticCapacity - 1)
        {
            _diagnostics.Add(key, new RenderDiagnostic(severity, code, message));
            return;
        }

        const string capacityKey = SilkRenderDiagnosticCodes.CapacityExceeded;
        if (!_diagnostics.ContainsKey(capacityKey))
        {
            _diagnostics.Add(capacityKey, new RenderDiagnostic(
                RenderDiagnosticSeverity.Warning,
                capacityKey,
                "Additional hdSilk material diagnostics were omitted."));
        }
    }

    private void RemoveTextureDiagnostics()
    {
        RemoveDiagnostics(static code =>
            code is SilkRenderDiagnosticCodes.TextureAssetNotFound or
                SilkRenderDiagnosticCodes.TextureDecodeFailed or
                SilkRenderDiagnosticCodes.TextureFallbackUsed or
                SilkRenderDiagnosticCodes.TextureBudgetExceeded or
                SilkRenderDiagnosticCodes.CapacityExceeded);
    }

    private void RemoveMaterialDiagnostics()
    {
        RemoveDiagnostics(static code =>
            code is SilkRenderDiagnosticCodes.MaterialUnresolved or
                SilkRenderDiagnosticCodes.MaterialUnsupported or
                SilkRenderDiagnosticCodes.MaterialMdlUnavailable or
                SilkRenderDiagnosticCodes.CapacityExceeded);
    }

    private void RemoveMaterialResolutionDiagnostics()
    {
        RemoveDiagnostics(static code =>
            code is SilkRenderDiagnosticCodes.MaterialUnresolved or
                SilkRenderDiagnosticCodes.MaterialUnsupported or
                SilkRenderDiagnosticCodes.MaterialMdlUnavailable);
    }

    private void RemoveTextureDiagnostics(string materialPath)
    {
        foreach (string key in _diagnostics
            .Where(pair =>
                (pair.Value.Code is SilkRenderDiagnosticCodes.TextureAssetNotFound or
                    SilkRenderDiagnosticCodes.TextureDecodeFailed or
                    SilkRenderDiagnosticCodes.TextureFallbackUsed or
                    SilkRenderDiagnosticCodes.TextureBudgetExceeded) &&
                pair.Key.StartsWith(
                    string.Concat(pair.Value.Code, "\0", materialPath, "\0"),
                    StringComparison.Ordinal))
            .Select(static pair => pair.Key)
            .ToArray())
        {
            _diagnostics.Remove(key);
        }
    }

    private void RemoveDiagnostics(Func<string, bool> predicate)
    {
        foreach (string key in _diagnostics
            .Where(pair => predicate(pair.Value.Code))
            .Select(static pair => pair.Key)
            .ToArray())
        {
            _diagnostics.Remove(key);
        }
    }

    /// <summary>
    /// Removes diagnostics by their storage key rather than by their code.
    /// </summary>
    /// <remarks>
    /// The environment emits the same codes from two layers that resolve at
    /// different times: the prefilter, which reports that a dome lost its
    /// directional response, and the mean-radiance fallback, which reports that
    /// the dome could not even be reduced to a colour. Clearing by code let the
    /// second layer erase the first -- a dome refused by the aggregate budget was
    /// diagnosed, then fell back successfully, and the successful fallback wiped
    /// the record of the loss. Keying the two layers apart is what keeps a
    /// silently non-directional scene from being a state this renderer can reach.
    /// </remarks>
    private void RemoveDiagnosticsByKey(Func<string, bool> predicate)
    {
        foreach (string key in _diagnostics.Keys
            .Where(predicate)
            .ToArray())
        {
            _diagnostics.Remove(key);
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        foreach (SilkMeshGpuResource mesh in _meshes.Values)
        {
            DisposeMesh(mesh);
        }
        _meshes.Clear();
        _geometries.Clear();
        foreach (SurfaceBuffer surface in _surfaceBuffers.Values)
        {
            surface.Buffer?.Dispose();
        }
        _surfaceBuffers.Clear();
        ClearTextureCache();
        _displacementImages.Clear();
        _displacementVerdicts.Clear();
        _displacementImageBytes = 0;
        foreach (ISilkGraphicsSampler sampler in _samplers.Values)
        {
            sampler.Dispose();
        }
        _samplers.Clear();
        ReleaseEnvironmentDeviceResources();
        _environmentLighting.Clear();
        _environmentMeanRadiance.Clear();
        _environmentDescriptions.Clear();
        _environmentLitDomes.Clear();
        _frameBuffer?.Dispose();
        _frameBuffer = null;
        _disposed = true;
        SilkManagedDiagnostics.GpuSceneDestroyed();
    }

    private SilkMeshGpuResource CreateMesh(SilkSceneState scene, SilkMeshData mesh)
    {
        SilkMeshGpuGeometryResource geometryResource = GetOrCreateGeometry(scene, mesh);
        ISilkGraphicsBuffer? uniformBuffer = null;
        try
        {
            // Storage usage as well as Uniform: the mesh vertex shader always
            // reads its transform from the instance table at slot 6, so a
            // non-instanced draw binds this same 80-byte buffer there as a
            // one-element table. D3D12 and Vulkan happened to render correctly
            // with slot 6 left unbound because their reflection-driven binding
            // aliased it onto the uniform buffer; Metal's explicit [[buffer(6)]]
            // read nothing and collapsed every vertex, which is why hosted macOS
            // produced only clear-color pixels.
            uniformBuffer = CreateTrackedBuffer(
                SilkSceneUniformWriter.ByteSize,
                SilkBufferUsage.Uniform | SilkBufferUsage.Storage |
                    SilkBufferUsage.Upload);
            return new SilkMeshGpuResource(
                mesh,
                geometryResource,
                uniformBuffer);
        }
        catch
        {
            uniformBuffer?.Dispose();
            ReleaseGeometry(geometryResource);
            throw;
        }
    }

    /// <summary>
    /// Resolves the material a prim binds for displacement, without the
    /// shadeability filter.
    /// </summary>
    /// <remarks>
    /// Displacement is a separate material terminal in USD. A material whose
    /// surface hdSilk cannot shade -- an unsupported graph, an undistilled MDL
    /// material, a generated MaterialX fragment -- can still author a
    /// displacement this renderer evaluates exactly, and dropping it because the
    /// surface was unshaded would silently flatten geometry the author asked to
    /// move. The prim is still drawn with the default surface, and the surface is
    /// still reported unshaded.
    /// </remarks>
    private static SilkMaterialData? ResolveDisplacementMaterial(
        SilkSceneState scene,
        SilkMeshData mesh) =>
        string.IsNullOrEmpty(mesh.MaterialPath)
            ? null
            : scene.Materials.TryGetValue(mesh.MaterialPath, out SilkMaterialData? material)
                ? material
                : null;

    private static SilkMaterialData? ResolveMaterial(SilkSceneState scene, SilkMeshData mesh)
    {
        if (string.IsNullOrEmpty(mesh.MaterialPath))
        {
            return null;
        }
        return scene.Materials.TryGetValue(mesh.MaterialPath, out SilkMaterialData? material) &&
            material.IsSupported
            ? material
            : null;
    }

    /// <summary>
    /// Resolves the retained geometry for one prim, consulting the cache before
    /// doing any work that a hit would throw away, and records the prim's
    /// displacement verdict whichever path drew it.
    /// </summary>
    /// <remarks>
    /// The key is formed from inputs that are all cheap to read: the emitted
    /// points, indices and normals, the rig identity, the bound material, the
    /// fingerprint of the texture-coordinate data that material samples through,
    /// and a displacement identity derived from the authored inputs and the
    /// height field's file stamp. None of them needs an image decoded, a vertex
    /// assembled or a point sampled, so a repeated frame, a second instance of
    /// one prototype and a republished page all resolve to the retained resource
    /// without touching any of that.
    /// </remarks>
    private SilkMeshGpuGeometryResource GetOrCreateGeometry(SilkSceneState scene, SilkMeshData mesh)
    {
        SilkMaterialData? material = ResolveMaterial(scene, mesh);
        SilkMaterialData? displacementMaterial = ResolveDisplacementMaterial(scene, mesh);
        string uvPrimvar = material?.GetPrimaryUvPrimvar() ?? string.Empty;
        bool normalMap = material?.GetTexture(SilkMaterialParameter.Normal) is not null;
        SilkVertexAttributeData? uvAttribute = string.IsNullOrEmpty(uvPrimvar)
            ? null
            : mesh.FindTexCoord(uvPrimvar);
        // Exactly the rule SilkMeshGeometryBuilder.Build applies, evaluated here
        // so the key can be formed without building the geometry first.
        bool hasTangents = uvAttribute is not null && normalMap;
        SilkDisplacementPlan plan = PlanDisplacement(mesh, displacementMaterial);
        string materialPath = displacementMaterial?.Path ?? material?.Path ?? string.Empty;
        ulong uvFingerprint = SilkMeshGpuGeometryKey.HashAttribute(uvAttribute);
        var key = SilkMeshGpuGeometryKey.Create(
            mesh,
            uvPrimvar,
            hasTangents,
            plan.Identity,
            materialPath,
            uvFingerprint);

        SilkMeshGpuGeometryResource resource = ResolveGeometryResource(
            scene,
            mesh,
            displacementMaterial,
            plan,
            key,
            uvPrimvar,
            normalMap);
        // The verdict is recorded from the retained resource rather than from the
        // resolution, so every instance of one prototype records its own -- and a
        // GPU-eligible prim whose displacement was refused records the refusal it
        // would otherwise never have reported, because the resolution that reports
        // one runs only on the CPU path.
        RecordDisplacementVerdict(mesh, materialPath, plan, resource);
        RefreshDisplacementDiagnostics(scene);
        return resource;
    }

    private SilkMeshGpuGeometryResource ResolveGeometryResource(
        SilkSceneState scene,
        SilkMeshData mesh,
        SilkMaterialData? displacementMaterial,
        SilkDisplacementPlan plan,
        SilkMeshGpuGeometryKey key,
        string uvPrimvar,
        bool normalMap)
    {
        bool deformationEligible = !_deformationDisabled &&
            !plan.MovesGeometry &&
            _device.Capabilities.SupportsCompute &&
            mesh.Deformation is not null;
        if (!deformationEligible)
        {
            if (plan.MovesGeometry && mesh.Deformation is not null)
            {
                _deformationDisplacementFallbacks++;
            }
            if (TryReuseGeometry(key, mesh, deformationPayload: null, out var reused))
            {
                _geometryCacheHits++;
                return reused;
            }
            float[] amounts = ResolveDisplacementAmounts(
                mesh,
                displacementMaterial,
                plan,
                out SilkDisplacementFallback resolved);
            SilkMeshGeometry cpuGeometry = SilkMeshGeometryBuilder.Build(
                mesh,
                uvPrimvar,
                normalMap,
                amounts);
            // A CPU build declares no payload, so its recoverable-failure guard
            // can never fire and the result is always present.
            _ = TryCreateGeometry(
                key,
                mesh,
                cpuGeometry,
                uvPrimvar,
                StrideFloats(cpuGeometry, mesh),
                deformationPayload: null,
                resolved,
                plan.DisplacementUvPrimvar,
                out SilkMeshGpuGeometryResource? built);
            return built ?? throw new InvalidOperationException(
                "Creating a CPU geometry reported a recoverable GPU failure.");
        }

        // A rig that reaches the kernel needs its emitted vertices either way:
        // they are the pose-independent inputs the payload is built from and the
        // authoritative bytes a recovered geometry uploads.
        SilkMeshGeometry geometry = SilkMeshGeometryBuilder.Build(mesh, uvPrimvar, normalMap);
        uint strideFloats = StrideFloats(geometry, mesh);
        SilkDeformationGpuFallback fallback = SilkDeformationGpuPayload.TryBuild(
            mesh.Deformation,
            strideFloats,
            mesh.Points.Length / 3,
            geometry.HasTangents,
            mesh.TopologyKind,
            out SilkDeformationGpuPayload? deformationPayload,
            ExtractTexCoords(mesh, geometry, strideFloats));
        if (fallback == SilkDeformationGpuFallback.None)
        {
            var gpuKey = SilkMeshGpuGeometryKey.CreateGpuDeformed(
                mesh,
                mesh.Deformation!,
                uvPrimvar,
                geometry.HasTangents,
                key.MaterialPath,
                key.UvFingerprint,
                key.DisplacementIdentity);
            if (TryReuseGeometry(gpuKey, mesh, deformationPayload, out var reused))
            {
                _geometryCacheHits++;
                return reused;
            }
            if (!_deformationDisabled &&
                TryCreateGeometry(
                    gpuKey,
                    mesh,
                    geometry,
                    uvPrimvar,
                    strideFloats,
                    deformationPayload,
                    plan.Fallback,
                    plan.DisplacementUvPrimvar,
                    out SilkMeshGpuGeometryResource? created))
            {
                return created;
            }
        }

        if (TryReuseGeometry(key, mesh, deformationPayload: null, out var cpuReused))
        {
            _geometryCacheHits++;
            return cpuReused;
        }
        _ = TryCreateGeometry(
            key,
            mesh,
            geometry,
            uvPrimvar,
            strideFloats,
            deformationPayload: null,
            plan.Fallback,
            plan.DisplacementUvPrimvar,
            out SilkMeshGpuGeometryResource? cpuCreated);
        return cpuCreated ?? throw new InvalidOperationException(
            "Creating a CPU geometry reported a recoverable GPU failure.");
    }

    private static uint StrideFloats(SilkMeshGeometry geometry, SilkMeshData mesh) =>
        checked((uint)(
            geometry.Vertices.Length / Math.Max(1, mesh.Points.Length / 3)));

    /// <summary>
    /// Decides everything about one material's displacement that can be decided
    /// without reading a pixel.
    /// </summary>
    /// <remarks>
    /// The plan's identity is what the retained geometry key carries, so it has
    /// to separate every case that produces different vertices *and* every case
    /// that produces a different verdict. A refusal therefore carries its reason
    /// in its identity: two materials that both leave the surface undisplaced for
    /// different reasons must not share one retained geometry, or the diagnostic
    /// naming the first reason would survive a change to the second.
    /// </remarks>
    private SilkDisplacementPlan PlanDisplacement(SilkMeshData mesh, SilkMaterialData? material)
    {
        if (material is null)
        {
            return SilkDisplacementPlan.NotAuthored;
        }
        SilkMaterialTexture? texture = material.GetTexture(SilkMaterialParameter.Displacement);
        ReadOnlySpan<float> scalar = material.GetScalar(SilkMaterialParameter.Displacement);

        // A two-image composite operand is resolved in the fragment stage from
        // two bound samplers, and the composite is not affine in either image, so
        // it cannot be folded into one per-vertex amount. Checked before the
        // authored test so an operand that reached here without its primary half
        // is reported rather than read as an unauthored input.
        SilkMaterialTexture? composite = material.GetCompositeTexture();
        if (composite is not null &&
            composite.Parameter == SilkMaterialParameter.Displacement)
        {
            return refuse(SilkDisplacementFallback.UnsupportedComposite);
        }
        if (texture is null && scalar.IsEmpty)
        {
            return SilkDisplacementPlan.NotAuthored;
        }
        if (mesh.TopologyKind != SilkTopologyKind.TriangleList)
        {
            return refuse(SilkDisplacementFallback.UnsupportedTopology);
        }
        if (mesh.Points.Length / 3 > _maximumDisplacedPoints)
        {
            return refuse(SilkDisplacementFallback.VertexBudget);
        }

        if (texture is null)
        {
            float amount = scalar[0];
            if (!float.IsFinite(amount))
            {
                return refuse(SilkDisplacementFallback.NonFiniteAmount);
            }
            if (amount == 0)
            {
                // Zero and unauthored are different statements but the same
                // vertices, so they deliberately share one retained geometry.
                return SilkDisplacementPlan.NotAuthored with
                {
                    Fallback = SilkDisplacementFallback.AuthoredZero
                };
            }
            ulong constantIdentity = MixDisplacementIdentity(
                MixDisplacementIdentity(DisplacementIdentityBasis, ConstantIdentityTag),
                BitConverter.SingleToUInt32Bits(amount));
            return new SilkDisplacementPlan(
                SilkDisplacementFallback.None,
                SilkDisplacementSource.Constant,
                constantIdentity,
                amount,
                null,
                SilkColorSpace.Raw,
                null);
        }

        if (texture.Asset.Contains("<UDIM>", StringComparison.Ordinal))
        {
            return refuse(SilkDisplacementFallback.UnsupportedUdim);
        }
        SilkVertexAttributeData? uv = string.IsNullOrEmpty(texture.UvPrimvar)
            ? null
            : mesh.FindTexCoord(texture.UvPrimvar);
        if (uv is null)
        {
            // The name is carried through the refusal, and folded into its
            // identity, precisely because this refusal is about an attribute that
            // is *absent*: a mesh republished with that primvar added has to stop
            // matching the refused geometry, and a refusal naming a different
            // primvar is a different refusal.
            return refuse(SilkDisplacementFallback.UnsupportedUvSet, texture.UvPrimvar);
        }
        // The coordinate data this height field is sampled through is part of the
        // identity in its own right: it may be a different primvar from the one
        // the material's surface textures use, so the key's own UV fingerprint
        // does not cover it.
        ulong identity = MixDisplacementIdentity(
            ComputeDisplacementTextureIdentity(material, texture),
            unchecked((uint)SilkMeshGpuGeometryKey.HashAttribute(uv)));
        return new SilkDisplacementPlan(
            SilkDisplacementFallback.None,
            SilkDisplacementSource.Texture,
            identity,
            0,
            texture,
            texture.SourceColorSpace,
            material.UvTransform);

        static SilkDisplacementPlan refuse(
            SilkDisplacementFallback fallback,
            string requestedUvPrimvar = "")
        {
            ulong identity = MixDisplacementIdentity(
                MixDisplacementIdentity(DisplacementIdentityBasis, RefusalIdentityTag),
                (uint)fallback);
            foreach (char character in requestedUvPrimvar)
            {
                identity = MixDisplacementIdentity(identity, character);
            }
            return SilkDisplacementPlan.Refused(fallback, requestedUvPrimvar) with
            {
                Identity = identity
            };
        }
    }

    /// <summary>
    /// Resolves one prim's per-point displacement amounts, decoding and sampling
    /// only because the retained-geometry cache already missed.
    /// </summary>
    private float[] ResolveDisplacementAmounts(
        SilkMeshData mesh,
        SilkMaterialData? material,
        SilkDisplacementPlan plan,
        out SilkDisplacementFallback resolved)
    {
        resolved = plan.Fallback;
        if (material is null || !plan.MovesGeometry)
        {
            return [];
        }

        SilkDisplacementField? field;
        if (plan.Source == SilkDisplacementSource.Constant)
        {
            field = SilkDisplacementField.Constant(plan.ConstantAmount, plan.Identity);
        }
        else
        {
            resolved = TryResolveDisplacementImage(plan, out field);
        }
        if (field is null)
        {
            return [];
        }

        _displacementResolves++;
        int pointCount = mesh.Points.Length / 3;
        SilkVertexAttributeData? uv = field.IsTextured
            ? mesh.FindTexCoord(field.UvPrimvar)
            : null;
        _displacementSampledPoints += checked((ulong)pointCount);
        return field.TryResolveAmounts(pointCount, uv, out float[] amounts) ? amounts : [];
    }

    /// <summary>
    /// Decodes, bounds and retains one displacement height field, or names why it
    /// could not be used.
    /// </summary>
    /// <remarks>
    /// The image's declared shape is read from its header first, and both budgets
    /// are decided from that shape in widened arithmetic that cannot overflow, so
    /// an image whose header alone claims more than this renderer will retain is
    /// refused before any decoder allocates it. An image that cannot be read at
    /// all is not the same condition: UsdUVTexture defines <c>fallback</c> as
    /// what the reader produces then, so the authored fallback becomes a constant
    /// displacement and the substitution is reported.
    /// </remarks>
    private SilkDisplacementFallback TryResolveDisplacementImage(
        SilkDisplacementPlan plan,
        out SilkDisplacementField? field)
    {
        field = null;
        SilkMaterialTexture texture = plan.Texture ??
            throw new InvalidOperationException("A textured plan must carry its texture.");
        _displacementUseClock++;
        if (_displacementImages.TryGetValue(plan.Identity, out DisplacementCacheEntry? cached))
        {
            cached.LastUsedStamp = _displacementUseClock;
            field = cached.Field;
            return SilkDisplacementFallback.None;
        }

        int channel = DisplacementChannelIndex(texture.Channel);
        float scale = texture.Scale[channel];
        float bias = texture.Bias[channel];
        if (!float.IsFinite(scale) || !float.IsFinite(bias))
        {
            return SilkDisplacementFallback.NonFiniteAmount;
        }

        SilkImageDescription description;
        try
        {
            description = _imageDescriber(texture.Asset);
        }
        catch (Exception exception) when (IsUnreadableImage(exception))
        {
            field = CreateDisplacementFallbackField(texture, plan.Identity, channel);
            return field is null
                ? SilkDisplacementFallback.NonFiniteAmount
                : SilkDisplacementFallback.TextureUnavailable;
        }
        if (!TryBoundDisplacementImage(description, out int texelCount))
        {
            return SilkDisplacementFallback.TextureBudget;
        }

        // Both deferred inputs are resolved from what the image library observed,
        // and refused by name when it observed nothing.
        SilkDisplacementFallback deferred = TryResolveDeferredInputs(
            texture,
            description,
            out SilkColorSpace effectiveColorSpace,
            out SilkTextureWrap wrapS,
            out SilkTextureWrap wrapT);
        if (deferred != SilkDisplacementFallback.None)
        {
            return deferred;
        }

        SilkDecodedImage image;
        try
        {
            image = _imageDecoder(texture.Asset, effectiveColorSpace == SilkColorSpace.Srgb);
            ValidateDecodedImage(image);
            _displacementImageDecodes++;
        }
        catch (Exception exception) when (IsUnreadableImage(exception))
        {
            field = CreateDisplacementFallbackField(texture, plan.Identity, channel);
            return field is null
                ? SilkDisplacementFallback.NonFiniteAmount
                : SilkDisplacementFallback.TextureUnavailable;
        }
        if (image.Width != description.Width ||
            image.Height != description.Height ||
            image.Format != description.Format)
        {
            // The header and the decode disagreed. The bound is re-applied to
            // what was actually produced rather than trusting the preflight.
            if (!TryBoundDisplacementImage(
                    new SilkImageDescription(image.Width, image.Height, image.Format),
                    out texelCount))
            {
                return SilkDisplacementFallback.TextureBudget;
            }
        }

        FlipRows(image.Pixels, image.Width, image.Height, image.Format);
        // Raw sampled values, with no affine folded in: the authored scale and
        // bias belong after filtering, where UsdUVTexture puts them and where a
        // transparent-black border receives the bias exactly once.
        float[] texels = new float[texelCount];
        if (image.Format == SilkTextureFormat.Rgba32Float)
        {
            ReadOnlySpan<float> values = MemoryMarshal.Cast<byte, float>(image.Pixels);
            for (int texel = 0; texel < texels.Length; texel++)
            {
                float value = values[(texel * 4) + channel];
                if (!float.IsFinite(value))
                {
                    return SilkDisplacementFallback.NonFiniteAmount;
                }
                texels[texel] = value;
            }
        }
        else
        {
            for (int texel = 0; texel < texels.Length; texel++)
            {
                texels[texel] = image.Pixels[(texel * 4) + channel] / 255f;
            }
        }

        var resolved = SilkDisplacementField.Textured(
            texels,
            checked((int)image.Width),
            checked((int)image.Height),
            wrapS,
            wrapT,
            plan.UvTransform ?? IdentityUvTransform,
            scale,
            bias,
            texture.UvPrimvar,
            plan.Identity);
        RetainDisplacementImage(
            plan.Identity,
            resolved,
            checked((ulong)texels.Length * sizeof(float)));
        field = resolved;
        return SilkDisplacementFallback.None;
    }

    /// <summary>
    /// Resolves the two UsdUVTexture inputs that defer to the image file, or
    /// names the one that could not be resolved.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>sourceColorSpace = auto</c> means "use the image's own colour-space
    /// metadata". hdSilk asks the image library, which applies its own auto
    /// resolution -- metadata first, then format and channel count -- so an
    /// untagged one-channel height map stays raw instead of being linearized as
    /// if it were an sRGB colour. When the library was not consulted at all, the
    /// case is refused rather than guessed.
    /// </para>
    /// <para>
    /// <c>wrap = useMetadata</c> means "use the wrap mode in the image file". An
    /// axis the library answered for is honoured; an axis it was asked about and
    /// reported nothing for is USD's documented "no metadata" case and resolves
    /// to <c>black</c>; an axis nobody asked about is refused. A mode the wire
    /// cannot carry is refused rather than rounded.
    /// </para>
    /// </remarks>
    private static SilkDisplacementFallback TryResolveDeferredInputs(
        SilkMaterialTexture texture,
        SilkImageDescription description,
        out SilkColorSpace colorSpace,
        out SilkTextureWrap wrapS,
        out SilkTextureWrap wrapT)
    {
        colorSpace = SilkColorSpace.Raw;
        wrapS = texture.WrapS;
        wrapT = texture.WrapT;
        bool queried = description.Observed.HasFlag(SilkImageObservation.Queried);

        switch (texture.SourceColorSpace)
        {
            case SilkColorSpace.Raw:
                colorSpace = SilkColorSpace.Raw;
                break;
            case SilkColorSpace.Srgb:
                colorSpace = SilkColorSpace.Srgb;
                break;
            default:
                if (!queried || !description.Observed.HasFlag(SilkImageObservation.ColorSpace))
                {
                    return SilkDisplacementFallback.MetadataUnavailable;
                }
                colorSpace = description.ColorSpace == SilkImageColorSpaceObservation.Srgb
                    ? SilkColorSpace.Srgb
                    : SilkColorSpace.Raw;
                break;
        }

        if (texture.WrapS == SilkTextureWrap.UseMetadata)
        {
            SilkDisplacementFallback reason = ResolveMetadataWrap(
                queried,
                description.Observed.HasFlag(SilkImageObservation.AddressU),
                description.AddressU,
                out wrapS);
            if (reason != SilkDisplacementFallback.None)
            {
                return reason;
            }
        }
        if (texture.WrapT == SilkTextureWrap.UseMetadata)
        {
            SilkDisplacementFallback reason = ResolveMetadataWrap(
                queried,
                description.Observed.HasFlag(SilkImageObservation.AddressV),
                description.AddressV,
                out wrapT);
            if (reason != SilkDisplacementFallback.None)
            {
                return reason;
            }
        }
        return SilkDisplacementFallback.None;
    }

    private static SilkDisplacementFallback ResolveMetadataWrap(
        bool queried,
        bool observed,
        SilkImageAddressObservation address,
        out SilkTextureWrap wrap)
    {
        wrap = SilkTextureWrap.Black;
        if (!queried)
        {
            return SilkDisplacementFallback.MetadataUnavailable;
        }
        if (!observed)
        {
            // Asked and answered: the file carries no wrap metadata, and USD's
            // documented fallback for useMetadata in that case is black.
            return SilkDisplacementFallback.None;
        }
        switch (address)
        {
            case SilkImageAddressObservation.Repeat:
                wrap = SilkTextureWrap.Repeat;
                return SilkDisplacementFallback.None;
            case SilkImageAddressObservation.MirrorRepeat:
                wrap = SilkTextureWrap.Mirror;
                return SilkDisplacementFallback.None;
            case SilkImageAddressObservation.ClampToEdge:
                wrap = SilkTextureWrap.Clamp;
                return SilkDisplacementFallback.None;
            case SilkImageAddressObservation.ClampToBorder:
                wrap = SilkTextureWrap.Black;
                return SilkDisplacementFallback.None;
            default:
                return SilkDisplacementFallback.MetadataUnsupported;
        }
    }

    private static bool IsUnreadableImage(Exception exception) =>
        exception is FileNotFoundException or
            DirectoryNotFoundException or
            InvalidDataException or
            IOException;

    /// <summary>
    /// The output channel a displacement reads, as an index into a decoded RGBA
    /// texel.
    /// </summary>
    /// <remarks>
    /// A one-component input connected to the whole <c>rgb</c> output is refused
    /// upstream, so <see cref="SilkTextureChannel.Rgb"/> only reaches here from a
    /// wire this renderer did not produce; reading its first component is the
    /// same reduction the fragment stage's channel replication performs.
    /// </remarks>
    private static int DisplacementChannelIndex(SilkTextureChannel channel) =>
        channel switch
        {
            SilkTextureChannel.G => 1,
            SilkTextureChannel.B => 2,
            SilkTextureChannel.A => 3,
            _ => 0
        };

    /// <summary>
    /// Bounds one image's declared shape against the texel and byte budgets, in
    /// arithmetic that cannot overflow.
    /// </summary>
    /// <remarks>
    /// Both dimensions are 32-bit, so their product is computed in 64 bits and
    /// the retained byte count in 64 bits again. Nothing is narrowed until both
    /// bounds have passed, so a hostile header claiming four billion by four
    /// billion is refused by comparison rather than by an allocation that would
    /// have wrapped.
    /// </remarks>
    private bool TryBoundDisplacementImage(SilkImageDescription description, out int texelCount)
    {
        texelCount = 0;
        if (description.Width == 0 || description.Height == 0)
        {
            return false;
        }
        ulong texels = (ulong)description.Width * description.Height;
        if (texels > (ulong)_maximumDisplacementTexels)
        {
            return false;
        }
        ulong retainedBytes = texels * sizeof(float);
        if (retainedBytes > MaximumDisplacementImageBytes)
        {
            return false;
        }
        ulong sourceBytes = texels *
            SilkTextureFormats.GetBytesPerPixel(description.Format);
        if (sourceBytes > int.MaxValue)
        {
            return false;
        }
        texelCount = (int)texels;
        return true;
    }

    /// <summary>
    /// Builds the constant displacement an unreadable height field resolves to.
    /// </summary>
    private static SilkDisplacementField? CreateDisplacementFallbackField(
        SilkMaterialTexture texture,
        ulong identity,
        int channel)
    {
        float value = (texture.Fallback[channel] * texture.Scale[channel]) +
            texture.Bias[channel];
        return float.IsFinite(value)
            ? SilkDisplacementField.Constant(value, identity)
            : null;
    }

    private void RetainDisplacementImage(
        ulong identity,
        SilkDisplacementField field,
        ulong bytes)
    {
        _displacementImages[identity] = new DisplacementCacheEntry(field, bytes)
        {
            LastUsedStamp = _displacementUseClock
        };
        _displacementImageBytes = checked(_displacementImageBytes + bytes);
        while (_displacementImageBytes > MaximumDisplacementImageBytes &&
            _displacementImages.Count > 1)
        {
            ulong oldestKey = 0;
            DisplacementCacheEntry? oldest = null;
            foreach (KeyValuePair<ulong, DisplacementCacheEntry> pair in _displacementImages)
            {
                if (pair.Key == identity)
                {
                    continue;
                }
                if (oldest is null || pair.Value.LastUsedStamp < oldest.LastUsedStamp)
                {
                    oldestKey = pair.Key;
                    oldest = pair.Value;
                }
            }
            if (oldest is null)
            {
                break;
            }
            _displacementImages.Remove(oldestKey);
            _displacementImageBytes -= oldest.Bytes;
        }
    }

    /// <summary>
    /// Lowers the displacement vertex and texel budgets so a conformance case can
    /// prove the bounded refusal without publishing millions of points or
    /// materializing a hostile image.
    /// </summary>
    internal void SetDisplacementBudgetsForTesting(int maximumPoints, int maximumTexels)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(maximumPoints);
        ArgumentOutOfRangeException.ThrowIfNegative(maximumTexels);
        _maximumDisplacedPoints = maximumPoints;
        _maximumDisplacementTexels = maximumTexels;
    }

    /// <summary>Gets the decoded bytes the displacement image cache retains.</summary>
    internal ulong DisplacementImageBytes => _displacementImageBytes;

    /// <summary>Gets the number of retained decoded displacement images.</summary>
    internal int DisplacementImageCount => _displacementImages.Count;

    /// <summary>Gets how many times a displacement field was sampled onto points.</summary>
    internal ulong DisplacementResolves => _displacementResolves;

    /// <summary>Gets how many points a displacement field has been sampled onto.</summary>
    internal ulong DisplacementSampledPoints => _displacementSampledPoints;

    /// <summary>Gets how many displacement height fields were decoded.</summary>
    internal ulong DisplacementImageDecodes => _displacementImageDecodes;

    /// <summary>Gets how many times a retained geometry answered a resolution.</summary>
    internal ulong GeometryCacheHits => _geometryCacheHits;

    /// <summary>
    /// Gets how many rigs were drawn from CPU points because their material
    /// displaces the surface.
    /// </summary>
    internal ulong DeformationDisplacementFallbacks => _deformationDisplacementFallbacks;

    /// <summary>Gets the retained displacement verdict count, one per drawn prim.</summary>
    internal int DisplacementVerdictCount => _displacementVerdicts.Count;

    /// <summary>
    /// Records one prim's displacement verdict, keyed by the prim *and* its
    /// instance so retiring one instance leaves its siblings' verdicts alone.
    /// </summary>
    private void RecordDisplacementVerdict(
        SilkMeshData mesh,
        string materialPath,
        SilkDisplacementPlan plan,
        SilkMeshGpuGeometryResource resource)
    {
        var key = new DisplacedPrimKey(mesh.Path, mesh.InstanceIndex);
        SilkDisplacementFallback fallback = resource.DisplacementFallback;
        if (fallback is SilkDisplacementFallback.None &&
            !resource.Displaced &&
            plan.Fallback is SilkDisplacementFallback.NotAuthored or
                SilkDisplacementFallback.AuthoredZero)
        {
            if (_displacementVerdicts.Remove(key))
            {
                _displacementVerdictRevision++;
            }
            return;
        }
        var verdict = new DisplacementVerdict(
            materialPath,
            fallback,
            resource.DisplacedVertexCount,
            resource.MaximumDisplacement);
        if (_displacementVerdicts.TryGetValue(key, out DisplacementVerdict existing) &&
            existing == verdict)
        {
            return;
        }
        _displacementVerdicts[key] = verdict;
        _displacementVerdictRevision++;
    }

    private void ForgetDisplacementVerdict(SilkMeshData mesh)
    {
        if (_displacementVerdicts.Remove(new DisplacedPrimKey(mesh.Path, mesh.InstanceIndex)))
        {
            _displacementVerdictRevision++;
        }
    }

    /// <summary>
    /// Rebuilds every displacement diagnostic from the retained per-instance
    /// verdicts and the published shadow table.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Diagnostics are per prim path, and a prototype drawn as several instances
    /// is one path. Aggregating rather than emitting per instance is what keeps
    /// one instance's retirement from clearing a report its siblings still earn,
    /// and what keeps a hundred instances of one displaced prototype from
    /// exhausting the bounded diagnostic snapshot.
    /// </para>
    /// <para>
    /// The shadow-bounds verdict is rebuilt here too, because it is not a
    /// property of a prim alone: it exists only while some light publishes a
    /// raster shadow map, whose light-space projection arrives already fitted to
    /// hdSilk's undisplaced caster bounds. Enabling shadows after a prim was
    /// displaced must raise it and retiring them must clear it, neither of which
    /// touches the prim's geometry or material.
    /// </para>
    /// </remarks>
    private void RefreshDisplacementDiagnostics(SilkSceneState scene)
    {
        ulong shadowRevision = scene.Shadows.Revision;
        if (_displacementReportedRevision == _displacementVerdictRevision &&
            _displacementReportedShadowRevision == shadowRevision)
        {
            return;
        }
        _displacementReportedRevision = _displacementVerdictRevision;
        _displacementReportedShadowRevision = shadowRevision;
        RemoveDiagnostics(static code =>
            code is SilkRenderDiagnosticCodes.DisplacementApplied or
                SilkRenderDiagnosticCodes.DisplacementUnsupported or
                SilkRenderDiagnosticCodes.DisplacementBudgetExceeded or
                SilkRenderDiagnosticCodes.DisplacementShadowBoundsUnverified);
        if (_displacementVerdicts.Count == 0)
        {
            return;
        }

        var aggregated = new Dictionary<(string Path, string MaterialPath), DisplacementVerdict>();
        foreach (KeyValuePair<DisplacedPrimKey, DisplacementVerdict> entry in _displacementVerdicts)
        {
            var group = (entry.Key.Path, entry.Value.MaterialPath);
            if (aggregated.TryGetValue(group, out DisplacementVerdict existing))
            {
                aggregated[group] = new DisplacementVerdict(
                    existing.MaterialPath,
                    // A refusal outranks an application: an instance the renderer
                    // could not displace is what a consumer has to act on.
                    existing.Fallback == SilkDisplacementFallback.None
                        ? entry.Value.Fallback
                        : existing.Fallback,
                    Math.Max(existing.VertexCount, entry.Value.VertexCount),
                    Math.Max(existing.MaximumDisplacement, entry.Value.MaximumDisplacement));
                continue;
            }
            aggregated[group] = entry.Value;
        }

        foreach (KeyValuePair<(string Path, string MaterialPath), DisplacementVerdict> entry in
            aggregated.OrderBy(static pair => pair.Key.Path, StringComparer.Ordinal)
                .ThenBy(static pair => pair.Key.MaterialPath, StringComparer.Ordinal))
        {
            string meshPath = entry.Key.Path;
            DisplacementVerdict verdict = entry.Value;
            if (verdict.Fallback != SilkDisplacementFallback.None)
            {
                ReportDisplacementFallback(verdict.MaterialPath, meshPath, verdict.Fallback);
            }
            if (verdict.VertexCount == 0)
            {
                continue;
            }
            ReportDisplacementApplied(
                verdict.MaterialPath,
                meshPath,
                verdict.VertexCount,
                verdict.MaximumDisplacement);
            if (scene.Shadows.HasShadows && verdict.MaximumDisplacement != 0)
            {
                AddDiagnostic(
                    SilkRenderDiagnosticCodes.DisplacementShadowBoundsUnverified,
                    DisplacementDiagnosticIdentity(meshPath, verdict.MaterialPath),
                    RenderDiagnosticSeverity.Information,
                    $"Prim '{meshPath}' is displaced by up to " +
                    $"{verdict.MaximumDisplacement.ToString(CultureInfo.InvariantCulture)} " +
                    "scene units into raster shadow maps whose light-space projection " +
                    "hdSilk derived from its undisplaced caster bounds.");
            }
        }
    }

    private void ReportDisplacementFallback(
        string materialPath,
        string meshPath,
        SilkDisplacementFallback fallback)
    {
        if (fallback is SilkDisplacementFallback.None or
            SilkDisplacementFallback.NotAuthored or
            SilkDisplacementFallback.AuthoredZero)
        {
            return;
        }
        string code = fallback is SilkDisplacementFallback.VertexBudget or
            SilkDisplacementFallback.TextureBudget
            ? SilkRenderDiagnosticCodes.DisplacementBudgetExceeded
            : SilkRenderDiagnosticCodes.DisplacementUnsupported;
        string outcome = fallback == SilkDisplacementFallback.TextureUnavailable
            ? "was displaced by the authored fallback instead"
            : "was drawn without";
        AddDiagnostic(
            code,
            DisplacementDiagnosticIdentity(meshPath, materialPath),
            RenderDiagnosticSeverity.Warning,
            $"Material '{materialPath}' authors a displacement that prim '{meshPath}' " +
            $"{outcome}, because {DescribeDisplacementFallback(fallback)}.");
    }

    /// <summary>
    /// The diagnostic identity of one prim's displacement verdict, keyed by the
    /// prim first so every verdict for that prim can be dropped together.
    /// </summary>
    private static string DisplacementDiagnosticIdentity(string meshPath, string materialPath) =>
        string.Concat(meshPath, "\0", materialPath);

    private void ReportDisplacementApplied(
        string materialPath,
        string meshPath,
        int vertexCount,
        float maximum)
    {
        // The emitted point count is the tessellation density the displacement was
        // evaluated at. hdSilk publishes the refined cage when the display style
        // asks for one and the control cage at complexity Low, and the wire carries
        // no refinement level, so naming the count is the only honest statement of
        // the density the height field was sampled at.
        //
        // Formatted invariantly: a diagnostic is a stable, machine-readable
        // statement of what the renderer did, and a host locale must not change
        // the decimal separator a consumer parses.
        AddDiagnostic(
            SilkRenderDiagnosticCodes.DisplacementApplied,
            DisplacementDiagnosticIdentity(meshPath, materialPath),
            RenderDiagnosticSeverity.Information,
            $"Prim '{meshPath}' was displaced by material '{materialPath}' at " +
            $"{vertexCount} emitted vertices, by at most " +
            $"{maximum.ToString(CultureInfo.InvariantCulture)} scene units along the " +
            "shading normal.");
    }

    private string DescribeDisplacementFallback(SilkDisplacementFallback fallback) =>
        fallback switch
        {
            SilkDisplacementFallback.UnsupportedTopology =>
                "the emitted topology is not a triangle list and carries no surface normal",
            SilkDisplacementFallback.UnsupportedComposite =>
                "the input is driven by the material's two-image composite operand",
            SilkDisplacementFallback.UnsupportedUdim =>
                "the displacement texture is a UDIM tile set",
            SilkDisplacementFallback.UnsupportedUvSet =>
                "the displacement texture names a texture coordinate set the mesh does not carry",
            SilkDisplacementFallback.NonFiniteAmount =>
                "the authored amount is not finite",
            SilkDisplacementFallback.VertexBudget =>
                $"the prim carries more than {_maximumDisplacedPoints} points",
            SilkDisplacementFallback.TextureBudget =>
                $"the displacement image carries more than {_maximumDisplacementTexels} texels",
            SilkDisplacementFallback.TextureUnavailable =>
                "the displacement image could not be found or decoded",
            SilkDisplacementFallback.MetadataUnavailable =>
                "the input defers to image metadata this renderer did not observe",
            SilkDisplacementFallback.MetadataUnsupported =>
                "the image's own wrap metadata names an addressing mode the wire cannot carry",
            _ => "the input could not be represented exactly"
        };

    /// <summary>
    /// The identity of one displacement height field, covering everything that
    /// can change the amounts it resolves to.
    /// </summary>
    private static ulong ComputeDisplacementTextureIdentity(
        SilkMaterialData material,
        SilkMaterialTexture texture)
    {
        ulong hash = MixDisplacementIdentity(DisplacementIdentityBasis, TextureIdentityTag);
        foreach (char character in texture.Asset)
        {
            hash = MixDisplacementIdentity(hash, character);
        }
        foreach (char character in texture.UvPrimvar)
        {
            hash = MixDisplacementIdentity(hash, character);
        }
        hash = MixDisplacementIdentity(hash, (uint)texture.WrapS);
        hash = MixDisplacementIdentity(hash, (uint)texture.WrapT);
        hash = MixDisplacementIdentity(hash, (uint)texture.SourceColorSpace);
        hash = MixDisplacementIdentity(hash, (uint)texture.Channel);
        for (int component = 0; component < 4; component++)
        {
            hash = MixDisplacementIdentity(
                hash,
                BitConverter.SingleToUInt32Bits(texture.Scale[component]));
            hash = MixDisplacementIdentity(
                hash,
                BitConverter.SingleToUInt32Bits(texture.Bias[component]));
            // The authored fallback is part of the identity because it is what
            // the amounts become when the file cannot be read.
            hash = MixDisplacementIdentity(
                hash,
                BitConverter.SingleToUInt32Bits(texture.Fallback[component]));
        }
        foreach (float element in material.UvTransform)
        {
            hash = MixDisplacementIdentity(hash, BitConverter.SingleToUInt32Bits(element));
        }
        // The file's own stamp, so a rewritten displacement map produces different
        // vertices instead of reusing the ones the previous file displaced, and so
        // a file that appears or disappears changes the verdict.
        try
        {
            var info = new FileInfo(texture.Asset);
            if (info.Exists)
            {
                hash = MixDisplacementIdentity(hash, (uint)info.Length);
                hash = MixDisplacementIdentity(hash, (uint)(info.Length >> 32));
                long ticks = info.LastWriteTimeUtc.Ticks;
                hash = MixDisplacementIdentity(hash, (uint)ticks);
                hash = MixDisplacementIdentity(hash, (uint)(ticks >> 32));
            }
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            // An unreadable path is decoded and reported by the resolve path; the
            // identity simply carries no stamp for it.
        }
        return hash;
    }

    private static ulong MixDisplacementIdentity(ulong hash, uint value)
    {
        unchecked
        {
            hash ^= value;
            return hash * 1099511628211;
        }
    }

    private const ulong DisplacementIdentityBasis = 14695981039346656037;

    private const uint ConstantIdentityTag = 1;

    private const uint TextureIdentityTag = 2;

    private const uint RefusalIdentityTag = 3;

    /// <summary>
    /// The largest number of decoded displacement bytes retained across every
    /// material at once.
    /// </summary>
    private const ulong MaximumDisplacementImageBytes = 64UL * 1024 * 1024;

    /// <summary>
    /// The affine a displacement field samples through when the material folded
    /// none of its own.
    /// </summary>
    private static readonly float[] IdentityUvTransform = [1, 0, 0, 1, 0, 0];

    /// <summary>One drawn prim, distinguished from its sibling instances.</summary>
    private readonly record struct DisplacedPrimKey(string Path, int InstanceIndex);

    /// <summary>What one drawn prim's displacement resolved to.</summary>
    private readonly record struct DisplacementVerdict(
        string MaterialPath,
        SilkDisplacementFallback Fallback,
        int VertexCount,
        float MaximumDisplacement);

    private sealed class DisplacementCacheEntry(SilkDisplacementField field, ulong bytes)
    {
        internal SilkDisplacementField Field { get; } = field;

        internal ulong Bytes { get; } = bytes;

        internal ulong LastUsedStamp { get; set; }
    }

    /// <summary>
    /// Returns a retained geometry for one key, refreshing a GPU-deformed one's
    /// pose, or false when nothing matches.
    /// </summary>
    private bool TryReuseGeometry(
        SilkMeshGpuGeometryKey key,
        SilkMeshData mesh,
        SilkDeformationGpuPayload? deformationPayload,
        [NotNullWhen(true)] out SilkMeshGpuGeometryResource? resource)
    {
        resource = null;
        if (!_geometries.TryGetValue(key, out List<SilkMeshGpuGeometryResource>? matches))
        {
            return false;
        }
        foreach (SilkMeshGpuGeometryResource candidate in matches)
        {
            if (!candidate.HasSameGeometry(mesh))
            {
                continue;
            }
            // The pose is re-uploaded on a hit, so a repeated frame at one time
            // code uploads nothing and a scrub uploads once.
            if (deformationPayload is not null && candidate.Deformation is { } retained)
            {
                ulong generation = ReadDeformationDeviceGeneration();
                try
                {
                    retained.UpdatePose(_device, deformationPayload);
                }
                catch (Exception exception)
                    when (IsRecoverableDeformationFailure(exception, generation))
                {
                    // The rig's new palette could not be uploaded. Retiring the
                    // resource drops it out of the cache and puts the
                    // authoritative CPU vertices under the draw, and the caller
                    // falls through to the CPU key.
                    RetireDeformation(candidate);
                    return false;
                }
            }
            candidate.AddReference();
            resource = candidate;
            return true;
        }
        return false;
    }

    /// <summary>
    /// Builds one geometry, returning false when a GPU-deformed build failed
    /// recoverably and the caller must build the CPU geometry instead.
    /// </summary>
    private bool TryCreateGeometry(
        SilkMeshGpuGeometryKey key,
        SilkMeshData mesh,
        SilkMeshGeometry geometry,
        string uvPrimvar,
        uint strideFloats,
        SilkDeformationGpuPayload? deformationPayload,
        SilkDisplacementFallback displacementFallback,
        string displacementUvPrimvar,
        [NotNullWhen(true)] out SilkMeshGpuGeometryResource? resource)
    {
        resource = null;
        bool gpuDeformed = deformationPayload is not null;
        ReadOnlySpan<byte> vertexBytes = MemoryMarshal.AsBytes(geometry.Vertices.AsSpan());
        ReadOnlySpan<byte> indexBytes = MemoryMarshal.AsBytes(geometry.Indices.AsSpan());
        ISilkGraphicsBuffer? vertexBuffer = null;
        ISilkGraphicsBuffer? indexBuffer = null;
        SilkMeshGpuDeformation? deformation = null;
        ulong generation = ReadDeformationDeviceGeneration();
        try
        {
            // A GPU-deformed geometry writes its vertices with a kernel, so its
            // vertex buffer lives on the device heap where an unordered-access
            // view is legal, and is never uploaded to. Every other geometry
            // keeps the uploadable buffer it has always had.
            vertexBuffer = CreateTrackedBuffer(
                GetAllocationSize(vertexBytes.Length),
                gpuDeformed
                    ? SilkBufferUsage.Vertex | SilkBufferUsage.Storage
                    : SilkBufferUsage.Vertex | SilkBufferUsage.Upload);
            if (!vertexBytes.IsEmpty && !gpuDeformed)
            {
                WriteTracked(vertexBuffer, vertexBytes);
                _vertexUploads++;
            }
            indexBuffer = CreateTrackedBuffer(
                GetAllocationSize(indexBytes.Length),
                SilkBufferUsage.Index | SilkBufferUsage.Upload);
            if (!indexBytes.IsEmpty)
            {
                WriteTracked(indexBuffer, indexBytes);
                _indexUploads++;
            }
            if (gpuDeformed)
            {
                deformation = CreateDeformation(deformationPayload!, strideFloats);
            }

            // The instance buffer is allocated on first instanced draw, not here.
            // Most meshes are drawn once, so allocating eagerly cost one storage
            // buffer per unique geometry for nothing.
            resource = new SilkMeshGpuGeometryResource(
                key,
                mesh,
                geometry.IndexCount,
                geometry.VertexLayout,
                geometry.UvPrimvar,
                geometry.HasTangents,
                vertexBuffer,
                indexBuffer,
                gpuDeformed ? vertexBytes.ToArray() : null,
                displacementFallback,
                geometry.Displaced ? mesh.Points.Length / 3 : 0,
                geometry.MaximumDisplacement,
                displacementUvPrimvar,
                SilkMeshGpuGeometryKey.HashAttribute(
                    string.IsNullOrEmpty(displacementUvPrimvar)
                        ? null
                        : mesh.FindTexCoord(displacementUvPrimvar)))
            {
                Deformation = deformation
            };
            if (!_geometries.TryGetValue(key, out List<SilkMeshGpuGeometryResource>? matches))
            {
                matches = [];
            }
            matches.Add(resource);
            _geometries[key] = matches;
            _geometryBuilds++;
            return true;
        }
        catch (Exception exception)
            when (gpuDeformed && IsRecoverableDeformationFailure(exception, generation))
        {
            // Nothing GPU-deformed was published, so the only state to undo is
            // what this call allocated. The caller builds the same geometry from
            // the same authoritative CPU vertices instead.
            deformation?.Dispose();
            indexBuffer?.Dispose();
            vertexBuffer?.Dispose();
            _deformationDisabled = true;
            _deformationFallbacks++;
            return false;
        }
        catch
        {
            deformation?.Dispose();
            indexBuffer?.Dispose();
            vertexBuffer?.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Installs one-shot failure factories for the deformation setup and
    /// dispatch paths, so a conformance case can prove the recovery without a
    /// device that actually refuses an allocation.
    /// </summary>
    /// <remarks>
    /// The factory rather than a flag is what lets a case distinguish the two
    /// outcomes the path must tell apart: a factory that only returns an
    /// exception is a recoverable failure, and one that also advances the
    /// device's generation before returning is a device loss the reset path
    /// must see, which is exactly how a real backend reports one.
    /// </remarks>
    internal void InjectDeformationFailuresForTesting(
        Func<Exception>? onSetup,
        Func<Exception>? onDispatch)
    {
        _deformationSetupFailureForTesting = onSetup;
        _deformationDispatchFailureForTesting = onDispatch;
    }

    /// <summary>
    /// The emitted texture coordinates a GPU-deformed vertex carries, empty for
    /// a layout that has none.
    /// </summary>
    private static ReadOnlySpan<float> ExtractTexCoords(
        SilkMeshData mesh,
        SilkMeshGeometry geometry,
        uint strideFloats)
    {
        if (strideFloats < 8 || string.IsNullOrEmpty(geometry.UvPrimvar))
        {
            return default;
        }
        int points = mesh.Points.Length / 3;
        float[] coordinates = new float[points * 2];
        ReadOnlySpan<float> vertices = geometry.Vertices;
        for (int point = 0; point < points; point++)
        {
            coordinates[point * 2] = vertices[(point * (int)strideFloats) + 6];
            coordinates[(point * 2) + 1] = vertices[(point * (int)strideFloats) + 7];
        }
        return coordinates;
    }

    /// <summary>
    /// Builds the deformation kernel's pipeline and uploads its inputs. The
    /// pipeline is per stride because the writable slot's declared element
    /// stride is what bounds a dispatch against the vertex buffer, and there are
    /// only two supported strides.
    /// </summary>
    private SilkMeshGpuDeformation CreateDeformation(
        SilkDeformationGpuPayload payload,
        uint strideFloats)
    {
        SilkDeformComputeReflection reflection = SilkCheckedShaderAssets.DeformCompute;
        if (Interlocked.Exchange(ref _deformationSetupFailureForTesting, null)
            is { } injected)
        {
            throw injected();
        }
        List<SilkComputeSlot> slots = [.. reflection.Layout.Slots];
        slots[0] = slots[0] with { ElementStride = strideFloats * sizeof(float) };
        ISilkComputeBindingLayout? layout = null;
        ISilkGraphicsShaderModule? module = null;
        ISilkComputeShaderProgram? program = null;
        ISilkComputePipeline? pipeline = null;
        List<ISilkGraphicsBuffer> buffers = [];
        try
        {
            layout = _device.CreateComputeBindingLayout(
                new SilkComputeBindingLayoutDescriptor(slots));
            module = _device.CreateShaderModule(
                SilkCheckedShaderAssets.LoadDeformCompute(DeformShaderFormat));
            program = _device.CreateComputeShaderProgram(
                new SilkComputeShaderProgramDescriptor(module, layout));
            pipeline = _device.CreateComputePipeline(
                new SilkComputePipelineDescriptor(
                    program,
                    reflection.ThreadGroupSizeX,
                    reflection.ThreadGroupSizeY,
                    reflection.ThreadGroupSizeZ));
            ISilkGraphicsBuffer bindPose = Track(buffers, payload.BindPose);
            ISilkGraphicsBuffer jointIndices = Track(buffers, payload.JointIndices);
            ISilkGraphicsBuffer jointWeights = Track(buffers, payload.JointWeights);
            ISilkGraphicsBuffer texCoords = Track(buffers, payload.TexCoords);
            ISilkGraphicsBuffer matrices = Track(buffers, payload.Matrices);
            ISilkGraphicsBuffer blendWeights = Track(buffers, payload.BlendWeights);
            ISilkGraphicsBuffer blendSpans = Track(buffers, payload.BlendSpans);
            ISilkGraphicsBuffer blendDeltas = Track(buffers, payload.BlendDeltas);
            ISilkGraphicsBuffer parameters =
                SilkDeformationGpuBuffers.CreateParameters(_device, payload.Parameters);
            buffers.Add(parameters);
            var deformation = new SilkMeshGpuDeformation(
                pipeline,
                payload,
                bindPose,
                jointIndices,
                jointWeights,
                texCoords,
                matrices,
                blendWeights,
                blendSpans,
                blendDeltas,
                parameters);
            // The pipeline owns leases on the program, module and layout, so
            // releasing the local handles here leaves exactly one owner.
            program.Dispose();
            module.Dispose();
            layout.Dispose();
            return deformation;
        }
        catch
        {
            foreach (ISilkGraphicsBuffer buffer in buffers)
            {
                buffer.Dispose();
            }
            pipeline?.Dispose();
            program?.Dispose();
            module?.Dispose();
            layout?.Dispose();
            throw;
        }
    }

    /// <summary>The checked binary format this device consumes.</summary>
    private SilkShaderBinaryFormat DeformShaderFormat => _device.Backend switch
    {
        SilkGraphicsBackend.D3D12 => SilkShaderBinaryFormat.Dxil,
        SilkGraphicsBackend.Vulkan => SilkShaderBinaryFormat.SpirV,
        SilkGraphicsBackend.Metal => SilkShaderBinaryFormat.MetalLibrary,
        _ => throw new NotSupportedException(
            $"Unsupported Silk graphics backend '{_device.Backend}'.")
    };

    private ISilkGraphicsBuffer Track(
        List<ISilkGraphicsBuffer> buffers,
        ReadOnlySpan<float> values)
    {
        ISilkGraphicsBuffer buffer = SilkDeformationGpuBuffers.Create(_device, values);
        buffers.Add(buffer);
        _bufferAllocationBytes += buffer.Size;
        return buffer;
    }

    private ISilkGraphicsBuffer Track(
        List<ISilkGraphicsBuffer> buffers,
        ReadOnlySpan<uint> values)
    {
        ISilkGraphicsBuffer buffer = SilkDeformationGpuBuffers.Create(_device, values);
        buffers.Add(buffer);
        _bufferAllocationBytes += buffer.Size;
        return buffer;
    }

    /// <summary>
    /// Records every deformation whose pose has not reached its vertex buffer,
    /// and returns how many were dispatched.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This runs on its own command list, submitted and waited before the shadow
    /// maps are prepared, so the shadow depth pass and the colour pass both
    /// fetch vertices the kernel already wrote. Ordering by submission rather
    /// than by intra-list barriers alone is what makes it correct across the
    /// shadow cache's own internal submission, which this renderer does not
    /// control.
    /// </para>
    /// <para>
    /// A device generation change drops what every resource believes reached its
    /// vertex buffer, so the frame after a reset dispatches everything once and
    /// then settles back to dispatching nothing.
    /// </para>
    /// <para>
    /// A recoverable failure while recording or submitting -- an allocation the
    /// device refused, a descriptor pool it could not grow -- retires every
    /// pending deformation onto the authoritative CPU vertices rather than
    /// aborting the frame or leaving the bind pose under the draw. A device
    /// loss is not recoverable and propagates, because the reset path is what
    /// rebuilds everything keyed on the generation.
    /// </para>
    /// </remarks>
    internal int DispatchDeformations(ulong deviceGeneration)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_deformationDeviceGeneration != deviceGeneration)
        {
            _deformationDeviceGeneration = deviceGeneration;
            foreach (List<SilkMeshGpuGeometryResource> matches in _geometries.Values)
            {
                foreach (SilkMeshGpuGeometryResource geometry in matches)
                {
                    geometry.Deformation?.InvalidateDispatch();
                }
            }
        }

        List<SilkMeshGpuGeometryResource> pending = [];
        foreach (List<SilkMeshGpuGeometryResource> matches in _geometries.Values)
        {
            foreach (SilkMeshGpuGeometryResource geometry in matches)
            {
                if (geometry.Deformation is { NeedsDispatch: true })
                {
                    pending.Add(geometry);
                }
            }
        }
        if (pending.Count == 0)
        {
            return 0;
        }

        // Read the classifier's own generation rather than trusting the one the
        // caller keys invalidation on: the two answer different questions and a
        // caller is free to key on anything, so comparing across them would make
        // every failure look like a reset.
        ulong observed = ReadDeformationDeviceGeneration();
        try
        {
            if (Interlocked.Exchange(ref _deformationDispatchFailureForTesting, null)
                is { } injected)
            {
                throw injected();
            }
            using ISilkGraphicsCommandList commands = _device.CreateCommandList();
            foreach (SilkMeshGpuGeometryResource geometry in pending)
            {
                geometry.Deformation!.Record(commands, geometry.VertexBuffer);
            }
            using ISilkGraphicsSubmission submission = _device.Submit(commands);
            submission.Wait();
        }
        catch (Exception exception)
            when (IsRecoverableDeformationFailure(exception, observed))
        {
            foreach (SilkMeshGpuGeometryResource geometry in pending)
            {
                RetireDeformation(geometry);
            }
            return 0;
        }
        foreach (SilkMeshGpuGeometryResource geometry in pending)
        {
            geometry.Deformation!.MarkDispatched();
        }
        _deformationDispatches += checked((ulong)pending.Count);
        return pending.Count;
    }

    /// <summary>
    /// Whether a failure from the deformation path is one the CPU geometry can
    /// recover from, rather than a device loss the reset path must see.
    /// </summary>
    /// <remarks>
    /// The backends report a lost device the same way they report every other
    /// failure -- a Vulkan <c>Result</c> in an <see cref="InvalidOperationException"/>
    /// message, an HRESULT from the Direct3D marshaller -- so the exception's
    /// type cannot classify it. What can is the device generation: every backend
    /// advances it when it notices a lost device, and that advance is what the
    /// reset path already keys on. A generation that moved across the failure is
    /// therefore a device loss and propagates; one that did not is a capacity or
    /// allocation failure this prim can survive by drawing what hdSilk resolved.
    /// A disposal is never recoverable either: nothing can be rebuilt on an
    /// object that is going away.
    /// </remarks>
    private bool IsRecoverableDeformationFailure(Exception exception, ulong generation) =>
        exception is not ObjectDisposedException &&
        exception is not OperationCanceledException &&
        ReadDeformationDeviceGeneration() == generation;

    /// <summary>
    /// Reads the device generation the deformation path classifies failures
    /// against, treating a device that publishes none as never resetting.
    /// </summary>
    /// <remarks>
    /// Both signals are mixed because they cover different resets and neither
    /// covers the other. The device-loss generation advances on every detected
    /// loss including one detected by an ordinary submission, which is the only
    /// kind the deformation pass ever makes; the selection-outline generation
    /// advances when that subsystem invalidates its resources, which need not be
    /// a loss at all. Reading only the second is what let a lost device on a
    /// plain queue submission look like a refused allocation.
    /// </remarks>
    private ulong ReadDeformationDeviceGeneration() =>
        SilkDeviceGeneration.Read(_device);

    /// <summary>
    /// Drops one geometry's kernel and puts the authoritative CPU vertices under
    /// its draw, so the current frame draws the resolved surface rather than the
    /// bind pose the kernel never wrote.
    /// </summary>
    /// <remarks>
    /// The resource also leaves the geometry cache: its key carries a bind
    /// identity, so a later prim carrying the same rig at a different pose would
    /// otherwise find it and draw the pose these vertices happen to hold. It
    /// stays alive for the meshes already referencing it, and the next upsert
    /// builds a fresh CPU geometry.
    /// </remarks>
    private void RetireDeformation(SilkMeshGpuGeometryResource geometry)
    {
        _deformationDisabled = true;
        _deformationFallbacks++;
        ISilkGraphicsBuffer replacement = CreateTrackedBuffer(
            geometry.VertexBuffer.Size,
            SilkBufferUsage.Vertex | SilkBufferUsage.Upload);
        try
        {
            ReadOnlySpan<byte> vertices = geometry.CpuVertices;
            if (!vertices.IsEmpty)
            {
                WriteTracked(replacement, vertices);
                _vertexUploads++;
            }
        }
        catch
        {
            replacement.Dispose();
            throw;
        }
        if (_geometries.TryGetValue(
                geometry.Key,
                out List<SilkMeshGpuGeometryResource>? matches))
        {
            _ = matches.Remove(geometry);
            if (matches.Count == 0)
            {
                _ = _geometries.Remove(geometry.Key);
            }
        }
        geometry.RetireDeformation(replacement);
    }

    /// <summary>
    /// Gets how many times a recoverable GPU deformation failure fell back to
    /// the authoritative CPU geometry.
    /// </summary>
    internal ulong DeformationFallbacks => _deformationFallbacks;

    /// <summary>Gets the number of deformation dispatches recorded so far.</summary>
    internal ulong DeformationDispatches => _deformationDispatches;


    private void DisposeMesh(SilkMeshGpuResource mesh)
    {
        SilkMeshGpuGeometryResource geometry = mesh.Geometry;
        mesh.Dispose();
        ReleaseGeometry(geometry);
    }

    private void ReleaseGeometry(SilkMeshGpuGeometryResource geometry)
    {
        if (geometry.ReleaseReference())
        {
            return;
        }

        if (_geometries.TryGetValue(
                geometry.Key,
                out List<SilkMeshGpuGeometryResource>? matches))
        {
            _ = matches.Remove(geometry);
            if (matches.Count == 0)
            {
                _geometries.Remove(geometry.Key);
            }
        }
        geometry.Dispose();
    }

    private static nuint GetAllocationSize(int dataLength) =>
        checked((nuint)Math.Max(dataLength, sizeof(uint)));

    private ISilkGraphicsBuffer CreateTrackedBuffer(nuint size, SilkBufferUsage usage)
    {
        _bufferAllocationBytes += checked((ulong)size);
        return _device.CreateBuffer(size, usage);
    }

    private void WriteTracked(ISilkGraphicsBuffer buffer, ReadOnlySpan<byte> data, nuint offset = 0)
    {
        buffer.Write(data, offset);
        _bufferWriteBytes += checked((ulong)data.Length);
    }
}

/// <summary>
/// GPU buffers for one retained mesh.
/// </summary>
public sealed class SilkMeshGpuResource : IDisposable
{
    private readonly byte[] _uniformBytes = new byte[SilkSceneUniformWriter.ByteSize];
    private readonly SilkMeshGpuGeometryResource _geometry;
    private SilkMeshData? _uniformMesh;
    private ulong _uniformFrameRevision = ulong.MaxValue;
    private bool _uniformOverridden;
    private bool _disposed;

    internal SilkMeshGpuResource(
        SilkMeshData mesh,
        SilkMeshGpuGeometryResource geometry,
        ISilkGraphicsBuffer uniformBuffer)
    {
        ArgumentNullException.ThrowIfNull(geometry);
        Mesh = mesh;
        _geometry = geometry;
        UniformBuffer = uniformBuffer;
        SilkManagedDiagnostics.GpuMeshCreated();
    }

    /// <summary>Gets the retained CPU mesh metadata.</summary>
    public SilkMeshData Mesh { get; private set; }

    /// <summary>Gets interleaved float3 position and float3 normal data.</summary>
    public ISilkGraphicsBuffer VertexBuffer => _geometry.VertexBuffer;

    /// <summary>Gets packed 16-bit triangle index data.</summary>
    public ISilkGraphicsBuffer IndexBuffer => _geometry.IndexBuffer;

    internal SilkVertexLayoutDescriptor VertexLayout => _geometry.VertexLayout;

    /// <summary>Gets the reusable 80-byte SceneParameters buffer.</summary>
    public ISilkGraphicsBuffer UniformBuffer { get; }

    /// <summary>Gets the indexed triangle-list element count.</summary>
    public uint IndexCount => _geometry.IndexCount;

    internal SilkMeshGpuGeometryResource Geometry => _geometry;

    internal bool HasSameGeometry(SilkMeshData mesh) =>
        Mesh.TopologyKind == mesh.TopologyKind &&
        Mesh.Points.Span.SequenceEqual(mesh.Points.Span) &&
        Mesh.Indices.Span.SequenceEqual(mesh.Indices.Span) &&
        Mesh.AuthoredNormals.Span.SequenceEqual(mesh.AuthoredNormals.Span) &&
        // A rebinding is a different resolution even when the points did not
        // move: the retained resource carries the previous material's texture
        // coordinate stream, tangent decision and displacement verdict.
        string.Equals(Mesh.MaterialPath, mesh.MaterialPath, StringComparison.Ordinal) &&
        HasSameDeformation(mesh) &&
        _geometry.HasSameMaterialGeometry(mesh);

    /// <summary>
    /// Whether the retained vertices already carry this record's pose.
    /// </summary>
    /// <remarks>
    /// A GPU-deformed geometry's vertex buffer is written by the kernel from the
    /// rig, not from the record's points, so two records whose points happen to
    /// match are still different pictures when their rigs are at different time
    /// codes. Comparing the pose identity here is what routes a scrub back
    /// through the geometry cache, where the retained resource takes the new
    /// matrices and schedules exactly one dispatch.
    /// </remarks>
    private bool HasSameDeformation(SilkMeshData mesh) =>
        !_geometry.Key.IsGpuDeformed ||
        (Mesh.Deformation?.Identity ?? 0) == (mesh.Deformation?.Identity ?? 0);

    internal void UpdateMesh(SilkMeshData mesh)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        Mesh = mesh;
    }

    internal bool UpdateUniform(
        SilkFrameState frame,
        Span<byte> destination,
        bool flipClipSpaceY) =>
        UpdateUniform(frame, destination, flipClipSpaceY, default);

    internal bool UpdateUniform(
        SilkFrameState frame,
        Span<byte> destination,
        bool flipClipSpaceY,
        ReadOnlySpan<double> overrideTransform)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        bool overridden = !overrideTransform.IsEmpty;
        if (!overridden &&
            !_uniformOverridden &&
            ReferenceEquals(_uniformMesh, Mesh) &&
            _uniformFrameRevision == frame.Revision)
        {
            return false;
        }

        SilkSceneUniformWriter.Write(Mesh, frame, destination, flipClipSpaceY, overrideTransform);
        _uniformMesh = Mesh;
        _uniformFrameRevision = frame.Revision;
        _uniformOverridden = overridden;
        if (destination.SequenceEqual(_uniformBytes))
        {
            return false;
        }
        UniformBuffer.Write(destination);
        destination.CopyTo(_uniformBytes);
        return true;
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        UniformBuffer.Dispose();
        _disposed = true;
        SilkManagedDiagnostics.GpuMeshDestroyed();
    }
}

internal sealed class SilkMeshGpuGeometryResource : IDisposable
{
    private readonly float[] _points;
    private readonly uint[] _indices;
    private readonly float[] _authoredNormals;
    private readonly string _uvPrimvar;
    private readonly bool _hasTangents;
    private byte[] _cpuVertices;
    private bool _deformationRetired;
    private readonly List<InstanceSlot> _instanceSlots = [];
    private Func<Exception>? _publicationFailureForTesting;
    private byte[] _shadowInstanceBytes = [];
    private int _shadowInstanceCapacity;
    private int _referenceCount = 1;
    private bool _disposed;

    internal SilkMeshGpuGeometryResource(
        SilkMeshGpuGeometryKey key,
        SilkMeshData mesh,
        uint indexCount,
        SilkVertexLayoutDescriptor vertexLayout,
        string uvPrimvar,
        bool hasTangents,
        ISilkGraphicsBuffer vertexBuffer,
        ISilkGraphicsBuffer indexBuffer,
        byte[]? cpuVertices = null,
        SilkDisplacementFallback displacementFallback = SilkDisplacementFallback.None,
        int displacedVertexCount = 0,
        float maximumDisplacement = 0,
        string displacementUvPrimvar = "",
        ulong displacementUvFingerprint = 0)
    {
        DisplacementUvPrimvar = displacementUvPrimvar;
        DisplacementUvFingerprint = displacementUvFingerprint;
        DisplacementFallback = displacementFallback;
        DisplacedVertexCount = displacedVertexCount;
        MaximumDisplacement = maximumDisplacement;
        Key = key;
        IndexCount = indexCount;
        VertexLayout = vertexLayout;
        VertexBuffer = vertexBuffer;
        IndexBuffer = indexBuffer;
        _cpuVertices = cpuVertices ?? [];
        _points = mesh.Points.ToArray();
        _indices = mesh.Indices.ToArray();
        _authoredNormals = mesh.AuthoredNormals.ToArray();
        _uvPrimvar = uvPrimvar;
        _hasTangents = hasTangents;
    }

    internal SilkMeshGpuGeometryKey Key { get; }

    /// <summary>
    /// Gets why this geometry's material displacement was not applied, resolved
    /// once when the geometry was built.
    /// </summary>
    /// <remarks>
    /// Retained on the resource rather than recomputed per prim because a second
    /// instance, and a repeated frame, answer from the cache without re-resolving
    /// anything -- and a verdict they could not restate would silently disappear
    /// for every prim but the first.
    /// </remarks>
    internal SilkDisplacementFallback DisplacementFallback { get; }

    /// <summary>Gets the emitted vertex count a displacement moved, or zero.</summary>
    internal int DisplacedVertexCount { get; }

    /// <summary>Gets the largest amount a displacement moved a point by.</summary>
    internal float MaximumDisplacement { get; }

    /// <summary>Gets whether a material displacement moved these vertices.</summary>
    internal bool Displaced => DisplacedVertexCount != 0;

    /// <summary>
    /// Gets the coordinate set the material's displacement is sampled through,
    /// empty when it samples none.
    /// </summary>
    /// <remarks>
    /// Kept separately from <see cref="SilkMeshGpuGeometryKey.UvPrimvar"/>, which
    /// is the *surface* stream. A displacement may sample a different primvar
    /// entirely, and does so even when the surface is unshadeable and names no
    /// stream at all -- in which case the surface fingerprint is zero and would
    /// hide every edit to the coordinates the height field actually reads.
    /// </remarks>
    internal string DisplacementUvPrimvar { get; }

    /// <summary>Gets the fingerprint of that coordinate data.</summary>
    internal ulong DisplacementUvFingerprint { get; }

    /// <summary>
    /// Gets the GPU deformation state, present only when the deformation kernel
    /// produces this geometry's vertices.
    /// </summary>
    internal SilkMeshGpuDeformation? Deformation { get; set; }

    /// <summary>
    /// Gets the interleaved vertices the CPU geometry builder produced, retained
    /// only while the kernel owns this geometry's vertex buffer.
    /// </summary>
    /// <remarks>
    /// A GPU-deformed vertex buffer lives on the device heap and cannot be
    /// written by the host, so recovering from a dispatch that could not be
    /// recorded needs a second, uploadable buffer carrying these bytes. They
    /// are the same authoritative vertices every non-deformed geometry uploads,
    /// so the recovered geometry is the picture hdSilk resolved on the CPU
    /// rather than a bind pose.
    /// </remarks>
    internal ReadOnlySpan<byte> CpuVertices => _cpuVertices;

    internal ISilkGraphicsBuffer VertexBuffer { get; private set; }

    /// <summary>
    /// Whether the kernel that owned this geometry's vertices gave up and the
    /// authoritative CPU vertices were uploaded in their place.
    /// </summary>
    internal bool DeformationRetired => _deformationRetired;

    /// <summary>
    /// Replaces the kernel-written vertex buffer with one carrying the retained
    /// CPU vertices, and releases the deformation that was producing it.
    /// </summary>
    internal void RetireDeformation(ISilkGraphicsBuffer cpuVertexBuffer)
    {
        ArgumentNullException.ThrowIfNull(cpuVertexBuffer);
        ObjectDisposedException.ThrowIf(_disposed, this);
        Deformation?.Dispose();
        Deformation = null;
        ISilkGraphicsBuffer previous = VertexBuffer;
        VertexBuffer = cpuVertexBuffer;
        _deformationRetired = true;
        _cpuVertices = [];
        previous.Dispose();
    }

    internal ISilkGraphicsBuffer IndexBuffer { get; }

    internal SilkVertexLayoutDescriptor VertexLayout { get; }

    /// <summary>
    /// Gets the number of retained per-batch instance transform tables, which is
    /// zero until an instanced draw first needs one.
    /// </summary>
    /// <remarks>
    /// One table per <em>batch</em> rather than one per geometry. A single
    /// geometry is split across several batches whenever anything in the batch
    /// key differs between its instances -- a material, a cull mode, or a UsdLux
    /// light, shadow or dome mask -- and every batch of a frame is recorded before
    /// any of them is submitted. A shared mutable table would therefore be
    /// rewritten by the second batch while the first batch's draw still referenced
    /// it, and both draws would read the last batch's transforms: some prims drawn
    /// twice, others not at all. Each batch keeps its own table for the lifetime of
    /// the submission instead.
    /// </remarks>
    internal int InstanceSlotCount => _instanceSlots.Count;

    /// <summary>
    /// Gets the per-caster light-space transform table, null until this geometry
    /// first casts a shadow.
    /// </summary>
    internal ISilkGraphicsBuffer? ShadowInstanceBuffer { get; private set; }

    internal uint IndexCount { get; }

    internal bool HasSameGeometry(SilkMeshData mesh) =>
        Key.TopologyKind == mesh.TopologyKind &&
        (Key.IsGpuDeformed && !_deformationRetired
            // A GPU-produced geometry holds no resolved point array to compare:
            // its vertices are whatever the kernel last wrote. What identifies
            // it is the emitted topology and the rig's bind pose, and the bind
            // pose is already part of the key, so the indices are the only thing
            // left to check. A retired one carries CPU vertices again, so it is
            // compared as the CPU geometry it has become.
            ? _indices.AsSpan().SequenceEqual(mesh.Indices.Span)
            : _points.AsSpan().SequenceEqual(mesh.Points.Span) &&
                _indices.AsSpan().SequenceEqual(mesh.Indices.Span) &&
                _authoredNormals.AsSpan().SequenceEqual(mesh.AuthoredNormals.Span)) &&
        HasSameMaterialGeometry(mesh);

    internal bool HasSameMaterialGeometry(SilkMeshData mesh) =>
        (string.IsNullOrEmpty(_uvPrimvar) || mesh.FindTexCoord(_uvPrimvar) is not null) &&
        _hasTangents == VertexLayout.Equals(SilkVertexLayoutDescriptor.PositionNormalTexCoordTangent) &&
        HasSameUvData(mesh);

    /// <summary>
    /// Whether the texture-coordinate data the bound material samples through is
    /// still the data these vertices were built and displaced from.
    /// </summary>
    /// <remarks>
    /// The presence check above is not enough on its own. A mesh republished with
    /// the same points but edited <c>st</c> values used to satisfy every identity
    /// test in the retained fast path, so it kept vertices carrying the previous
    /// coordinates -- and, once displacement sampled a height field through them,
    /// the previous heights as well.
    /// </remarks>
    internal bool HasSameUvData(SilkMeshData mesh) =>
        Key.UvFingerprint == SilkMeshGpuGeometryKey.HashAttribute(
            string.IsNullOrEmpty(Key.UvPrimvar) ? null : mesh.FindTexCoord(Key.UvPrimvar)) &&
        DisplacementUvFingerprint == SilkMeshGpuGeometryKey.HashAttribute(
            string.IsNullOrEmpty(DisplacementUvPrimvar)
                ? null
                : mesh.FindTexCoord(DisplacementUvPrimvar));

    /// <summary>
    /// Returns the instance buffer one batch slot uses, which an instanced draw
    /// must have created.
    /// </summary>
    internal ISilkGraphicsBuffer RequireInstanceBuffer(int slot)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(slot);
        if (slot >= _instanceSlots.Count || _instanceSlots[slot].Buffer is not { } buffer)
        {
            throw new InvalidOperationException(
                "An instanced draw requires UpdateInstanceBuffer to have run first.");
        }
        return buffer;
    }

    /// <summary>
    /// Returns the per-caster light-space transform table for one shadow map,
    /// uploading it first.
    /// </summary>
    /// <remarks>
    /// A second buffer rather than a reuse of an instance slot: those hold the
    /// camera's object-to-clip transforms and are bound by the colour pass of the
    /// same frame, so writing light-space matrices into one would draw the scene
    /// from the light. It is allocated the first time this geometry casts
    /// and stays sized to the largest caster batch it has seen, which is bounded by
    /// the number of instances the geometry already has.
    /// </remarks>
    internal ISilkGraphicsBuffer RequireShadowInstanceBuffer(
        ISilkGraphicsDevice device,
        IReadOnlyList<SilkShadowCaster> casters)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(casters);
        if (casters.Count == 0)
        {
            throw new ArgumentException(
                "A shadow caster batch must contain at least one draw.",
                nameof(casters));
        }

        int required = checked(casters.Count * SilkSceneUniformWriter.ByteSize);
        if (casters.Count > _shadowInstanceCapacity)
        {
            ShadowInstanceBuffer?.Dispose();
            _shadowInstanceCapacity = Math.Max(casters.Count, _shadowInstanceCapacity * 2);
            ShadowInstanceBuffer = device.CreateBuffer(
                checked((nuint)(_shadowInstanceCapacity * SilkSceneUniformWriter.ByteSize)),
                SilkBufferUsage.Storage | SilkBufferUsage.Upload);
            _shadowInstanceBytes =
                new byte[_shadowInstanceCapacity * SilkSceneUniformWriter.ByteSize];
        }

        for (int index = 0; index < casters.Count; index++)
        {
            SilkShadowInstanceWriter.Write(
                casters[index].ObjectToLightClip,
                _shadowInstanceBytes.AsSpan(
                    index * SilkSceneUniformWriter.ByteSize,
                    SilkSceneUniformWriter.ByteSize));
        }
        ISilkGraphicsBuffer buffer = ShadowInstanceBuffer ??
            throw new InvalidOperationException("The shadow instance buffer was not created.");
        buffer.Write(_shadowInstanceBytes.AsSpan(0, required));
        return buffer;
    }

    /// <summary>
    /// Uploads one batch's per-instance transform table into that batch's own
    /// slot, allocating the slot on first use.
    /// </summary>
    /// <param name="device">The device the slot's buffer belongs to.</param>
    /// <param name="frame">The frame the transforms are projected with.</param>
    /// <param name="instances">The batch's meshes, in draw order.</param>
    /// <param name="flipClipSpaceY">Whether the backend's clip space points down.</param>
    /// <param name="slot">
    /// The batch's ordinal among the batches of this frame that draw this
    /// geometry. Slots are assigned in batch order, so a scene that does not
    /// change its batching keeps writing the same slot and keeps the delta upload
    /// below.
    /// </param>
    /// <remarks>
    /// Every part of the update is staged and swapped in only once the buffer was
    /// created and every write it needed succeeded. A device that refuses the
    /// allocation, or a write that fails part way through the table, used to leave
    /// the slot claiming a capacity it had no buffer for and a retained byte image
    /// the GPU had never received -- and because the next frame compares against
    /// that image to find its delta, the rows that failed would never be uploaded
    /// again. Staging first is what makes a refused frame retryable: the slot is
    /// exactly what it was, so the retry re-uploads everything it must.
    /// </remarks>
    internal void UpdateInstanceBuffer(
        ISilkGraphicsDevice device,
        SilkFrameState frame,
        IReadOnlyList<SilkMeshGpuResource> instances,
        bool flipClipSpaceY,
        int slot = 0)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(frame);
        ArgumentNullException.ThrowIfNull(instances);
        ArgumentOutOfRangeException.ThrowIfNegative(slot);
        if (slot > _instanceSlots.Count)
        {
            throw new ArgumentOutOfRangeException(
                nameof(slot),
                slot,
                "Instance slots are assigned in batch order and cannot be skipped.");
        }

        InstanceSlot? table = slot < _instanceSlots.Count ? _instanceSlots[slot] : null;
        if (table is not null &&
            table.FrameRevision == frame.Revision &&
            table.Meshes.Length == instances.Count)
        {
            bool unchanged = true;
            for (int index = 0; index < instances.Count; index++)
            {
                unchanged &= ReferenceEquals(table.Meshes[index], instances[index].Mesh);
            }
            if (unchanged)
            {
                return;
            }
        }

        int capacity = table?.Capacity ?? 0;
        int uploadedCount = table?.UploadedCount ?? 0;
        ISilkGraphicsBuffer? buffer = table?.Buffer;
        byte[] retained = table?.Bytes ?? [];
        byte[] staging = table?.Staging ?? [];
        SilkMeshData?[] meshes = table?.MeshesStaging ?? [];
        ISilkGraphicsBuffer? created = null;
        try
        {
            if (buffer is null || instances.Count > capacity)
            {
                capacity = Math.Max(instances.Count, capacity * 2);
                created = device.CreateBuffer(
                    checked((nuint)(capacity * SilkSceneUniformWriter.ByteSize)),
                    SilkBufferUsage.Storage | SilkBufferUsage.Upload);
                buffer = created;

                // A fresh buffer holds nothing, so nothing may be skipped as
                // already uploaded, and both byte images have to match its size.
                retained = new byte[capacity * SilkSceneUniformWriter.ByteSize];
                staging = new byte[capacity * SilkSceneUniformWriter.ByteSize];
                uploadedCount = 0;
            }
            if (staging.Length != retained.Length)
            {
                staging = new byte[retained.Length];
            }
            if (meshes.Length != instances.Count)
            {
                meshes = new SilkMeshData?[instances.Count];
            }

            Span<byte> encoded = stackalloc byte[SilkSceneUniformWriter.ByteSize];
            int changedStart = -1;
            int changedLength = 0;
            for (int index = 0; index < instances.Count; index++)
            {
                SilkMeshData mesh = instances[index].Mesh;
                meshes[index] = mesh;
                SilkSceneUniformWriter.Write(mesh, frame, encoded, flipClipSpaceY);
                int offset = index * SilkSceneUniformWriter.ByteSize;
                encoded.CopyTo(staging.AsSpan(offset, SilkSceneUniformWriter.ByteSize));

                // Only a row the device is known to already hold may be skipped.
                // A row past the last successful upload has no known device
                // content, whatever the retained image happens to say.
                if (index < uploadedCount &&
                    encoded.SequenceEqual(
                        retained.AsSpan(offset, SilkSceneUniformWriter.ByteSize)))
                {
                    if (changedStart >= 0)
                    {
                        buffer.Write(
                            staging.AsSpan(changedStart, changedLength),
                            checked((nuint)changedStart));
                        changedStart = -1;
                        changedLength = 0;
                    }
                    continue;
                }

                if (changedStart < 0)
                {
                    changedStart = offset;
                    changedLength = SilkSceneUniformWriter.ByteSize;
                }
                else
                {
                    changedLength += SilkSceneUniformWriter.ByteSize;
                }
            }
            if (changedStart >= 0)
            {
                buffer.Write(
                    staging.AsSpan(changedStart, changedLength),
                    checked((nuint)changedStart));
            }
        }
        catch
        {
            created?.Dispose();
            throw;
        }

        // The slot object and the room for it in the list are obtained under the
        // same guard that owns the buffer: allocating either can fail, and a
        // failure after the buffer exists but before anything references it
        // leaks a device allocation that nothing will ever dispose. Only the
        // reserved insert below is outside the guard, and a List<T>.Add into
        // reserved capacity cannot fail.
        InstanceSlot published;
        try
        {
            if (_publicationFailureForTesting is { } injected)
            {
                _publicationFailureForTesting = null;
                throw injected();
            }
            published = table ?? new InstanceSlot();
            if (table is null)
            {
                _instanceSlots.EnsureCapacity(_instanceSlots.Count + 1);
            }
        }
        catch
        {
            created?.Dispose();
            throw;
        }

        if (table is null)
        {
            _instanceSlots.Add(published);
        }

        InstanceSlot slotTable = published;
        ISilkGraphicsBuffer? previous = slotTable.Buffer;
        slotTable.Buffer = buffer;
        slotTable.Capacity = capacity;
        slotTable.Bytes = staging;
        slotTable.Staging = retained;
        slotTable.MeshesStaging = slotTable.Meshes;
        slotTable.Meshes = meshes;
        slotTable.UploadedCount = instances.Count;
        slotTable.FrameRevision = frame.Revision;
        if (previous is not null && !ReferenceEquals(previous, buffer))
        {
            previous.Dispose();
        }
    }

    /// <summary>
    /// Makes the next instance-slot publication throw, after the buffer exists
    /// and before anything references it.
    /// </summary>
    /// <remarks>
    /// That window is invisible from the outside: the update simply fails, and
    /// whether the buffer it had already created was disposed or leaked is only
    /// observable by counting the device's live allocations.
    /// </remarks>
    internal void FailNextInstanceSlotPublicationForTesting(Func<Exception> failure) =>
        _publicationFailureForTesting = failure;

    /// <summary>One batch's retained instance transform table.</summary>
    private sealed class InstanceSlot
    {
        internal ISilkGraphicsBuffer? Buffer { get; set; }

        /// <summary>The byte image the device is known to hold.</summary>
        internal byte[] Bytes { get; set; } = [];

        /// <summary>
        /// The image the next update is encoded into, swapped with
        /// <see cref="Bytes"/> only once every write it needed succeeded.
        /// </summary>
        internal byte[] Staging { get; set; } = [];

        internal SilkMeshData?[] Meshes { get; set; } = [];

        /// <summary>The mesh table the next update is staged into.</summary>
        internal SilkMeshData?[] MeshesStaging { get; set; } = [];

        /// <summary>
        /// How many leading rows of <see cref="Bytes"/> the device is known to
        /// hold. Zero after the buffer is recreated, because a fresh allocation
        /// holds nothing whatever the retained image says.
        /// </summary>
        internal int UploadedCount { get; set; }

        internal ulong FrameRevision { get; set; } = ulong.MaxValue;

        // Starts at zero so the first instanced draw always allocates; the buffer
        // does not exist until then.
        internal int Capacity { get; set; }
    }

    internal void AddReference()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _referenceCount++;
    }

    internal bool ReleaseReference()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _referenceCount--;
        if (_referenceCount < 0)
        {
            throw new InvalidOperationException(
                "The Silk mesh geometry reference count became negative.");
        }
        return _referenceCount != 0;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        foreach (InstanceSlot slot in _instanceSlots)
        {
            slot.Buffer?.Dispose();
        }
        _instanceSlots.Clear();
        ShadowInstanceBuffer?.Dispose();
        // The deformation owns its pipeline and every input buffer, so a
        // geometry that is retired releases the whole GPU deformation with it
        // rather than leaving a pipeline alive behind a dropped reference.
        Deformation?.Dispose();
        IndexBuffer.Dispose();
        VertexBuffer.Dispose();
        _disposed = true;
    }
}

internal readonly record struct SilkMeshGpuGeometryKey(
    string Path,
    SilkTopologyKind TopologyKind,
    ulong TopologyFingerprint,
    ulong PointFingerprint,
    ulong NormalFingerprint,
    // The identity of the bounded deformation rig this mesh published, or zero
    // when it published none. The point fingerprint already moves whenever the
    // CPU-resolved pose does, so this is not what makes an animated mesh rebuild
    // its geometry; it is what keeps two poses that resolve to the same points
    // from sharing one retained resource while carrying different rigs, which is
    // exactly what a deformation-aware consumer would then get wrong.
    ulong DeformationIdentity,
    // Non-zero only for a geometry the GPU deformation kernel produces. It
    // covers the rig's time-independent inputs, so one resource serves every
    // pose of one rig, and it is what makes a CPU-built and a GPU-produced
    // geometry structurally unable to share a cache entry: a CPU entry always
    // carries point and normal fingerprints and a zero bind identity, and a GPU
    // entry always carries the reverse.
    ulong DeformationBindIdentity,
    string UvPrimvar,
    bool HasTangents,
    // The identity of the material displacement that moved this geometry's
    // points, or the reason nothing moved them. It covers the authored constant,
    // the displacement image's asset, addressing, channel, affine, authored
    // fallback and file stamp, and -- for a refusal -- the reason itself, so a
    // material edit that changes only the height field, or only why the height
    // field was refused, produces a different retained geometry rather than
    // reusing vertices and a verdict the previous material produced.
    ulong DisplacementIdentity = 0,
    // The bound material. Two materials that agree on every geometry-affecting
    // input do produce identical vertices, but a prim rebound between them is a
    // different binding, and the retained resource carries that binding's
    // displacement verdict as well as its vertices.
    string MaterialPath = "",
    // The fingerprint of the texture-coordinate data the bound material samples
    // through. Comparing only the primvar's presence let an edit to its values
    // reuse vertices carrying the previous coordinates -- and, once displacement
    // sampled through them, the previous heights.
    ulong UvFingerprint = 0)
{
    internal static SilkMeshGpuGeometryKey Create(
        SilkMeshData mesh,
        string uvPrimvar,
        bool hasTangents,
        ulong displacementIdentity = 0,
        string materialPath = "",
        ulong uvFingerprint = 0) =>
        new(
            mesh.Path,
            mesh.TopologyKind,
            mesh.TopologyFingerprint,
            HashFloats(mesh.Points.Span),
            HashFloats(mesh.AuthoredNormals.Span),
            mesh.DeformationIdentity,
            0,
            uvPrimvar,
            hasTangents,
            displacementIdentity,
            materialPath,
            uvFingerprint);

    /// <summary>
    /// Fingerprints one bound vertex attribute, or zero when the mesh carries
    /// none by that name.
    /// </summary>
    internal static ulong HashAttribute(SilkVertexAttributeData? attribute)
    {
        if (attribute is null)
        {
            return 0;
        }
        ulong hash = HashFloats(attribute.Data.Span);
        unchecked
        {
            hash ^= (uint)attribute.ComponentCount;
            hash *= 1099511628211;
            hash ^= (uint)attribute.Interpolation;
            hash *= 1099511628211;
        }
        // Zero is reserved for "no such attribute", so a real attribute that
        // hashes to it is nudged rather than being indistinguishable from one
        // the mesh does not carry.
        return hash == 0 ? 1 : hash;
    }

    /// <summary>
    /// The key of a geometry the deformation kernel produces, which depends on
    /// the emitted topology and the rig's bind pose rather than on any resolved
    /// point array.
    /// </summary>
    internal static SilkMeshGpuGeometryKey CreateGpuDeformed(
        SilkMeshData mesh,
        SilkMeshDeformationData deformation,
        string uvPrimvar,
        bool hasTangents,
        string materialPath = "",
        ulong uvFingerprint = 0,
        ulong displacementIdentity = 0) =>
        new(
            mesh.Path,
            mesh.TopologyKind,
            mesh.TopologyFingerprint,
            0,
            0,
            0,
            deformation.BindIdentity,
            uvPrimvar,
            hasTangents,
            // A GPU-deformed geometry never moves under a displacement -- a
            // moving one is refused by the kernel -- but it still carries the
            // *verdict*, and two prims refused for different reasons must not
            // share one retained resource, or the first reason would survive a
            // change to the second.
            displacementIdentity,
            materialPath,
            uvFingerprint);

    /// <summary>Gets whether the deformation kernel produces this geometry.</summary>
    internal bool IsGpuDeformed => DeformationBindIdentity != 0;

    private static ulong HashFloats(ReadOnlySpan<float> values)
    {
        const ulong offsetBasis = 14695981039346656037;
        const ulong prime = 1099511628211;
        ulong hash = offsetBasis;
        foreach (float value in values)
        {
            unchecked
            {
                hash ^= (uint)BitConverter.SingleToInt32Bits(value);
                hash *= prime;
            }
        }
        return hash;
    }
}

/// <summary>Cumulative retained-scene GPU upload diagnostics.</summary>
public readonly record struct SilkSceneGpuStatistics(
    int MeshCount,
    ulong GeometryBuilds,
    ulong VertexUploads,
    ulong IndexUploads,
    ulong UniformUploads,
    ulong BufferAllocationBytes,
    ulong BufferWriteBytes,
    ulong TextureUploadBytes,
    ulong TextureResidentDecodedBytes = 0,
    ulong TextureResidentGpuBytes = 0,
    ulong PeakTextureResidentDecodedBytes = 0,
    ulong PeakTextureResidentGpuBytes = 0,
    ulong MaxDecodedCpuTextureBytes = 0,
    ulong MaxGpuTextureBytes = 0,
    int TextureCacheEntryCount = 0,
    ulong TextureEvictionCount = 0);
