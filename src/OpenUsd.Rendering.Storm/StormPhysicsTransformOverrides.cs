// Copyright (c) marcschier. Licensed under the MIT License.

using System.Globalization;
using System.Text;

namespace OpenUsd.Rendering.Storm;

/// <summary>
/// Reports what Storm did with the last accepted transform override batch.
/// </summary>
/// <param name="AppliedCount">The number of prims whose transform Storm is overriding.</param>
/// <param name="UnresolvedCount">
/// The number of batch entries whose render prim path did not resolve on the stage.
/// </param>
/// <param name="DroppedCount">
/// The number of overrides the managed batch refused because it was already full.
/// </param>
/// <param name="UnsupportedCount">
/// The number of batch entries Storm cannot override individually, such as point-instancer
/// members.
/// </param>
/// <param name="Capacity">The maximum number of entries one batch can carry.</param>
/// <param name="Revision">The revision of the applied override set.</param>
/// <param name="AppliedBatchCount">The number of batches Storm has applied.</param>
/// <param name="RejectedBatchCount">The number of batches Storm refused to apply.</param>
/// <param name="DirtiedPrimCount">The number of prim invalidations Storm has emitted.</param>
public readonly record struct StormPhysicsOverrideDiagnostics(
    int AppliedCount,
    int UnresolvedCount,
    int DroppedCount,
    int UnsupportedCount,
    int Capacity,
    ulong Revision,
    ulong AppliedBatchCount,
    ulong RejectedBatchCount,
    ulong DirtiedPrimCount)
{
    /// <summary>Gets the diagnostics reported before any batch is applied.</summary>
    public static StormPhysicsOverrideDiagnostics Empty => default;

    /// <summary>
    /// Gets a value indicating whether every override in the last batch reached a render prim.
    /// </summary>
    public bool IsComplete =>
        UnresolvedCount == 0 && DroppedCount == 0 && UnsupportedCount == 0;

    /// <summary>Describes the diagnostics for logs and evidence artifacts.</summary>
    /// <returns>The invariant single-line description.</returns>
    public string Describe() => string.Create(
        CultureInfo.InvariantCulture,
        $"storm physics overrides: applied={AppliedCount} unresolved={UnresolvedCount} " +
        $"dropped={DroppedCount} unsupported={UnsupportedCount} capacity={Capacity} " +
        $"revision={Revision} batches={AppliedBatchCount} rejected={RejectedBatchCount} " +
        $"dirtied={DirtiedPrimCount}");
}

/// <summary>
/// Packs renderer-neutral physics transform overrides into the single batched Storm update the
/// project-owned C ABI accepts.
/// </summary>
/// <remarks>
/// <para>
/// The batch is bounded and reused, so a warmed refresh allocates nothing: prim path bytes and
/// packed items live in arrays sized once at construction. Overrides beyond the configured
/// capacity are counted rather than silently discarded, and identities without a render binding
/// are counted separately so a partially bound scene still renders every bound rigid body.
/// </para>
/// <para>
/// Nothing here touches USD authoring, PhysX handles, or per-element interop: the whole batch
/// crosses the ABI in one call and Storm copies it synchronously.
/// </para>
/// <para>
/// Each item carries a rotation and a translation only and asks Storm to keep the scale and shear
/// the rendered prim already carries, so an authored scaled or sheared body keeps its shape under
/// simulation without any managed component reading the stage.
/// </para>
/// </remarks>
public sealed class StormPhysicsTransformOverrides
{
    /// <summary>Gets the largest batch the Storm C ABI accepts.</summary>
    public const int MaximumCapacity = 4096;

    /// <summary>Gets the largest packed prim path payload the Storm C ABI accepts.</summary>
    public const int MaximumPathBytes = 1024 * 1024;

    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    private readonly StormPhysicsOverrideInterop.NativeTransformOverrideItem[] _items;
    private readonly byte[] _pathBytes;
    private readonly double[] _transform = new double[PhysicsRenderTransforms.ElementCount];
    private int _count;
    private int _pathByteCount;
    private long _droppedOverrides;
    private long _unboundOverrides;
    private long _refreshCount;

    /// <summary>Initializes a bounded reusable Storm override batch.</summary>
    /// <param name="capacity">The maximum number of overrides one batch carries.</param>
    /// <param name="pathByteCapacity">The maximum packed prim path payload, in bytes.</param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="capacity"/> or <paramref name="pathByteCapacity"/> is outside the range the
    /// Storm C ABI accepts.
    /// </exception>
    public StormPhysicsTransformOverrides(
        int capacity = MaximumCapacity,
        int pathByteCapacity = MaximumPathBytes)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(capacity, MaximumCapacity);
        ArgumentOutOfRangeException.ThrowIfLessThan(pathByteCapacity, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(pathByteCapacity, MaximumPathBytes);
        _items =
            new StormPhysicsOverrideInterop.NativeTransformOverrideItem[capacity];
        _pathBytes = new byte[pathByteCapacity];
    }

    /// <summary>Gets the maximum number of overrides one batch carries.</summary>
    public int Capacity => _items.Length;

    /// <summary>Gets the maximum packed prim path payload, in bytes.</summary>
    public int PathByteCapacity => _pathBytes.Length;

    /// <summary>Gets the number of overrides in the current batch.</summary>
    public int Count => _count;

    /// <summary>Gets the packed prim path payload size of the current batch, in bytes.</summary>
    public int PathByteCount => _pathByteCount;

    /// <summary>Gets the revision of the current batch.</summary>
    public ulong Revision { get; private set; }

    /// <summary>Gets the number of overrides refused because the batch was full.</summary>
    public long DroppedOverrides => _droppedOverrides;

    /// <summary>Gets the number of overrides refused because no render binding resolved.</summary>
    public long UnboundOverrides => _unboundOverrides;

    /// <summary>Gets the number of completed refreshes.</summary>
    public long RefreshCount => _refreshCount;

    /// <summary>
    /// Reports whether Storm can override the transforms produced for a physics domain.
    /// </summary>
    /// <param name="domain">The physics domain.</param>
    /// <returns>
    /// <see langword="true"/> when the domain produces rigid transforms Storm overrides directly.
    /// </returns>
    /// <remarks>
    /// Storm overrides world transforms, so every domain whose render contribution is a rigid
    /// transform is supported. Domains whose render contribution is deforming geometry are
    /// diagnosed individually and never stop supported rigid rendering.
    /// </remarks>
    public static bool IsDomainSupported(PhysicsRenderDomain domain) => domain switch
    {
        PhysicsRenderDomain.RigidBody => true,
        PhysicsRenderDomain.Articulation => true,
        PhysicsRenderDomain.Controller => true,
        PhysicsRenderDomain.Vehicle => true,
        _ => false,
    };

    /// <summary>Rebuilds the batch from one render update without allocating when warmed.</summary>
    /// <param name="overrides">The renderer-neutral overrides produced by the render update.</param>
    /// <param name="bindings">The identity to render prim path bindings.</param>
    /// <returns>The number of overrides packed into the batch.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="bindings"/> is null.</exception>
    public int Refresh(in PhysicsRenderOverrideView overrides, PhysicsRenderBindingTable bindings)
    {
        ArgumentNullException.ThrowIfNull(bindings);
        _count = 0;
        _pathByteCount = 0;
        Revision = overrides.Revision;
        _refreshCount++;

        ReadOnlySpan<PhysicsRenderTransformOverride> items = overrides.Items;
        for (int index = 0; index < items.Length; index++)
        {
            ref readonly PhysicsRenderTransformOverride value = ref items[index];
            if (!bindings.TryResolve(value.Id, out PhysicsRenderBinding binding))
            {
                _unboundOverrides++;
                continue;
            }
            if (_count == _items.Length)
            {
                _droppedOverrides++;
                continue;
            }

            int required = StrictUtf8.GetByteCount(binding.PrimPath);
            if (required == 0 || required > _pathBytes.Length - _pathByteCount)
            {
                _droppedOverrides++;
                continue;
            }

            // The authored span is deliberately empty: Storm has no managed copy of the
            // rendered basis, so the batch carries rotation and translation only and asks
            // the renderer to keep the scale and shear the rendered prim already carries.
            PhysicsRenderTransforms.Compose(value, default, _transform);
            int written = StrictUtf8.GetBytes(
                binding.PrimPath,
                _pathBytes.AsSpan(_pathByteCount));
            ref StormPhysicsOverrideInterop.NativeTransformOverrideItem item =
                ref _items[_count];
            item.ObjectId = value.Id.Value;
            item.PathOffset = checked((uint)_pathByteCount);
            item.PathLength = checked((uint)written);
            item.InstanceIndex = binding.InstanceIndex > 0 ? binding.InstanceIndex : -1;
            item.Flags = value.Snapped
                ? StormPhysicsOverrideInterop.ItemSnapped |
                    StormPhysicsOverrideInterop.ItemPreserveStretch
                : StormPhysicsOverrideInterop.ItemPreserveStretch;
            item.SetTransform(_transform);
            _pathByteCount += written;
            _count++;
        }

        return _count;
    }

    /// <summary>Empties the batch so applying it restores every authored transform.</summary>
    /// <param name="revision">The revision the emptied batch reports.</param>
    public void Clear(ulong revision = 0)
    {
        _count = 0;
        _pathByteCount = 0;
        Revision = revision;
    }

    /// <summary>Resets the batch and every counter.</summary>
    public void Reset()
    {
        Clear();
        _droppedOverrides = 0;
        _unboundOverrides = 0;
        _refreshCount = 0;
    }

    /// <summary>Describes the batch for logs and evidence artifacts.</summary>
    /// <returns>The invariant single-line description.</returns>
    public string Describe() => string.Create(
        CultureInfo.InvariantCulture,
        $"storm physics batch: count={_count} capacity={Capacity} " +
        $"pathBytes={_pathByteCount}/{PathByteCapacity} revision={Revision} " +
        $"dropped={_droppedOverrides} unbound={_unboundOverrides} refreshes={_refreshCount}");

    internal ReadOnlySpan<StormPhysicsOverrideInterop.NativeTransformOverrideItem> Items =>
        _items.AsSpan(0, _count);

    internal ReadOnlySpan<byte> PathBytes => _pathBytes.AsSpan(0, _pathByteCount);

    internal void CopyTransform(int index, Span<double> destination)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, _count);
        _items[index].CopyTransformTo(destination);
    }

    internal string PathAt(int index)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, _count);
        ref StormPhysicsOverrideInterop.NativeTransformOverrideItem item = ref _items[index];
        return StrictUtf8.GetString(
            _pathBytes.AsSpan(checked((int)item.PathOffset), checked((int)item.PathLength)));
    }

    internal ulong ObjectIdAt(int index)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, _count);
        return _items[index].ObjectId;
    }

    internal uint FlagsAt(int index)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, _count);
        return _items[index].Flags;
    }

    internal int InstanceIndexAt(int index)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, _count);
        return _items[index].InstanceIndex;
    }
}
