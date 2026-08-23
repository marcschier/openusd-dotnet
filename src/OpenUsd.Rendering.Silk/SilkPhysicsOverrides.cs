// Copyright (c) marcschier. Licensed under the MIT License.

using System.Globalization;

namespace OpenUsd.Rendering.Silk;

/// <summary>
/// Resolves renderer-neutral physics transform overrides onto retained hdSilk meshes.
/// </summary>
/// <remarks>
/// <para>
/// The table is the backend half of the physics render path. It consumes only the immutable
/// override set a <see cref="PhysicsRenderInterpolator"/> produced, keyed by stable simulation
/// identity, and resolves it through a <see cref="PhysicsRenderBindingTable"/> onto the mesh keys
/// hdSilk already retains. No stage, prim, or solver handle is read, and nothing is authored back
/// into USD: the override only replaces the local-to-world transform the uniform writer consumes
/// for the frame it was produced for.
/// </para>
/// <para>
/// Storage is bounded and reused. A refresh clears and refills preallocated buffers, so a warmed
/// refresh allocates nothing. Overrides that resolve to no retained mesh, and overrides that do not
/// fit the bounded capacity, are counted and diagnosed individually rather than growing storage or
/// aborting the update.
/// </para>
/// </remarks>
public sealed class SilkPhysicsTransformOverrides
{
    private readonly Dictionary<ulong, int> _slots;
    private readonly double[] _transforms;
    private long _unresolvedOverrides;
    private long _droppedOverrides;

    /// <summary>Allocates bounded override storage up front.</summary>
    /// <param name="capacity">The number of meshes an override set can drive.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="capacity"/> is negative.</exception>
    public SilkPhysicsTransformOverrides(int capacity)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(capacity);
        Capacity = capacity;
        _slots = new Dictionary<ulong, int>(capacity);
        _transforms = capacity == 0
            ? []
            : new double[capacity * PhysicsRenderTransforms.ElementCount];
    }

    /// <summary>Gets the number of meshes the table can override.</summary>
    public int Capacity { get; }

    /// <summary>Gets the number of meshes the last refresh produced an override for.</summary>
    public int Count => _slots.Count;

    /// <summary>Gets the monotonic revision advanced by every refresh that changed the table.</summary>
    public ulong Revision { get; private set; }

    /// <summary>Gets the number of overrides that resolved to no retained mesh.</summary>
    public long UnresolvedOverrides => _unresolvedOverrides;

    /// <summary>Gets the number of overrides dropped because bounded storage was full.</summary>
    public long DroppedOverrides => _droppedOverrides;

    /// <summary>Gets a value indicating whether the table currently overrides any mesh.</summary>
    public bool HasOverrides => _slots.Count != 0;

    /// <summary>Reports whether this backend can draw one simulation domain.</summary>
    /// <param name="domain">The domain the caller wants to draw.</param>
    /// <returns><see langword="true"/> when hdSilk can draw the domain.</returns>
    /// <remarks>
    /// hdSilk drives retained mesh transforms, so every domain whose renderable result is a rigid
    /// transform is supported. Domains whose renderable result is streamed geometry are reported as
    /// unsupported until the backend grows a geometry upload path for them; reporting is per domain
    /// so an unsupported domain never stops rigid rendering.
    /// </remarks>
    public static bool IsDomainSupported(PhysicsRenderDomain domain) => domain is
        PhysicsRenderDomain.RigidBody or
        PhysicsRenderDomain.Articulation or
        PhysicsRenderDomain.Controller or
        PhysicsRenderDomain.Vehicle;

    /// <summary>Describes how one domain renders through this backend.</summary>
    /// <param name="interpolator">The interpolator holding the latest snapshot.</param>
    /// <param name="domain">The described domain.</param>
    /// <returns>
    /// The snapshot's own report for a supported domain, or an unsupported report for a domain this
    /// backend cannot draw.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="interpolator"/> is null.</exception>
    public static PhysicsRenderDomainReport Describe(
        PhysicsRenderInterpolator interpolator,
        PhysicsRenderDomain domain)
    {
        ArgumentNullException.ThrowIfNull(interpolator);
        return IsDomainSupported(domain)
            ? interpolator.GetDomain(domain)
            : PhysicsRenderDomainReport.Unsupported(domain);
    }

    /// <summary>Rebuilds the mesh overrides from one produced override set.</summary>
    /// <param name="scene">The retained hdSilk scene the overrides are resolved against.</param>
    /// <param name="bindings">The table naming the renderable entity each identity drives.</param>
    /// <param name="overrides">The overrides one render update produced.</param>
    /// <returns>The number of meshes the table now overrides.</returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="scene"/> or <paramref name="bindings"/> is null.
    /// </exception>
    public int Refresh(
        SilkSceneState scene,
        PhysicsRenderBindingTable bindings,
        PhysicsRenderOverrideView overrides)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(bindings);
        _slots.Clear();
        ReadOnlySpan<PhysicsRenderTransformOverride> items = overrides.Items;
        for (int index = 0; index < items.Length; index++)
        {
            ref readonly PhysicsRenderTransformOverride value = ref items[index];
            if (!bindings.TryResolve(value.Id, out PhysicsRenderBinding binding))
            {
                _unresolvedOverrides++;
                continue;
            }

            if (!scene.MeshesByPath.TryGetValue(
                    (binding.PrimPath, binding.InstanceIndex),
                    out SilkMeshData? mesh))
            {
                _unresolvedOverrides++;
                continue;
            }

            // Two overrides can resolve onto the same retained mesh, because two composed
            // identities of one prim - a body and its articulation link, say - name the same path.
            // The slot index is the dictionary count, so overwriting an existing key without
            // reusing its slot would leave the count unchanged and hand the next mesh the slot
            // this one just wrote, drawing one prim with another prim's pose.
            if (!_slots.TryGetValue(mesh.Id, out int slot))
            {
                if (_slots.Count >= Capacity)
                {
                    _droppedOverrides++;
                    continue;
                }

                slot = _slots.Count;
                _slots[mesh.Id] = slot;
            }

            PhysicsRenderTransforms.Compose(
                value,
                mesh.Transform.Span,
                _transforms.AsSpan(
                    slot * PhysicsRenderTransforms.ElementCount,
                    PhysicsRenderTransforms.ElementCount));
        }

        Revision++;
        return _slots.Count;
    }

    /// <summary>Returns the composed transform of one retained mesh.</summary>
    /// <param name="meshId">The retained hdSilk mesh key.</param>
    /// <returns>
    /// The row-major composed transform, or an empty span when the mesh is not overridden.
    /// </returns>
    public ReadOnlySpan<double> GetTransform(ulong meshId) =>
        _slots.TryGetValue(meshId, out int slot)
            ? _transforms.AsSpan(
                slot * PhysicsRenderTransforms.ElementCount,
                PhysicsRenderTransforms.ElementCount)
            : default;

    /// <summary>Reports whether one retained mesh is currently overridden.</summary>
    /// <param name="meshId">The retained hdSilk mesh key.</param>
    /// <returns><see langword="true"/> when the mesh is overridden.</returns>
    public bool Contains(ulong meshId) => _slots.ContainsKey(meshId);

    /// <summary>Creates the diagnostics the last refresh warrants.</summary>
    /// <param name="destination">The list the diagnostics are added to.</param>
    /// <exception cref="ArgumentNullException"><paramref name="destination"/> is null.</exception>
    public void CollectDiagnostics(IList<RenderDiagnostic> destination)
    {
        ArgumentNullException.ThrowIfNull(destination);
        if (_unresolvedOverrides != 0)
        {
            destination.Add(new RenderDiagnostic(
                RenderDiagnosticSeverity.Information,
                PhysicsRenderDiagnosticCodes.OverrideUnresolved,
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"{_unresolvedOverrides} physics transform overrides resolved to no retained mesh.")));
        }
        if (_droppedOverrides != 0)
        {
            destination.Add(new RenderDiagnostic(
                RenderDiagnosticSeverity.Warning,
                PhysicsRenderDiagnosticCodes.OverrideCapacityExceeded,
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"{_droppedOverrides} physics transform overrides were dropped because the " +
                        $"backend capacity is {Capacity}.")));
        }
    }

    /// <summary>Removes every override.</summary>
    /// <remarks>
    /// Used on reset, stop, and invalidation. The next uniform update rewrites every affected mesh
    /// from its authored transform, so the authored render state is restored exactly.
    /// </remarks>
    public void Clear()
    {
        if (_slots.Count == 0)
        {
            return;
        }

        _slots.Clear();
        Revision++;
    }
}
