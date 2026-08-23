// Copyright (c) marcschier. Licensed under the MIT License.

using System.Globalization;
using System.Text;

namespace OpenUsd.Rendering.Storm;

/// <summary>
/// Reports what Storm did with the last deformation override batch it accepted.
/// </summary>
/// <param name="AppliedCount">The number of prims currently drawn with simulated points.</param>
/// <param name="UnresolvedCount">Regions whose prim path is absent from the rendered scene.</param>
/// <param name="DroppedCount">Regions refused because a bounded capacity was reached.</param>
/// <param name="UnsupportedCount">Regions the backend cannot apply, currently instanced ones.</param>
/// <param name="MismatchedCount">
/// Regions refused because the rendered prim's element topology does not accept that many points.
/// </param>
/// <param name="Capacity">The number of regions one batch can carry.</param>
/// <param name="Revision">The revision of the last accepted batch.</param>
/// <param name="AppliedBatchCount">How many batches Storm has accepted.</param>
/// <param name="RejectedBatchCount">How many batches Storm refused after validating them.</param>
/// <param name="DirtiedPrimCount">How many prims Storm has dirtied for those batches.</param>
public readonly record struct StormPhysicsDeformationDiagnostics(
    int AppliedCount,
    int UnresolvedCount,
    int DroppedCount,
    int UnsupportedCount,
    int MismatchedCount,
    int Capacity,
    ulong Revision,
    ulong AppliedBatchCount,
    ulong RejectedBatchCount,
    ulong DirtiedPrimCount)
{
    /// <summary>Gets the diagnostics of a renderer that has accepted no batch.</summary>
    public static StormPhysicsDeformationDiagnostics Empty => default;

    /// <summary>
    /// Gets a value indicating whether every region of the last batch reached a rendered prim.
    /// </summary>
    public bool IsComplete =>
        UnresolvedCount == 0 && DroppedCount == 0 && UnsupportedCount == 0 && MismatchedCount == 0;

    /// <summary>Describes the diagnostics for logs and evidence artifacts.</summary>
    /// <returns>The invariant single-line description.</returns>
    public string Describe() => string.Create(
        CultureInfo.InvariantCulture,
        $"storm deformation state: applied={AppliedCount}/{Capacity} " +
        $"unresolved={UnresolvedCount} dropped={DroppedCount} " +
        $"unsupported={UnsupportedCount} mismatched={MismatchedCount} " +
        $"revision={Revision} batches={AppliedBatchCount} " +
        $"rejected={RejectedBatchCount} dirtied={DirtiedPrimCount}");
}

/// <summary>
/// Packs renderer-neutral deformable geometry into the bounded batch the Storm C ABI accepts.
/// </summary>
/// <remarks>
/// <para>
/// The batch is the backend half of the geometry path, and it is the exact mirror of
/// <see cref="StormPhysicsTransformOverrides"/> for the transform path. It consumes only the
/// immutable deformation view a <see cref="PhysicsRenderInterpolator"/> produced, resolves each
/// region through a <see cref="PhysicsRenderBindingTable"/> onto an authored prim path, and packs
/// one region record plus one shared point page. Nothing is authored into USD and no stage, prim,
/// or solver handle is read.
/// </para>
/// <para>
/// One batch crosses the boundary per frame, in one call. Storage is bounded and reused, so a
/// warmed refresh allocates nothing, and a region that does not fit is counted and diagnosed rather
/// than growing storage or truncating the page.
/// </para>
/// </remarks>
public sealed class StormPhysicsDeformationOverrides
{
    /// <summary>Gets the largest batch the Storm C ABI accepts.</summary>
    public const int MaximumCapacity = 1024;

    /// <summary>Gets the largest shared point page the Storm C ABI accepts, in points.</summary>
    public const int MaximumPoints = 4194304;

    /// <summary>Gets the largest packed prim path payload the Storm C ABI accepts.</summary>
    public const int MaximumPathBytes = 1024 * 1024;

    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    private readonly StormPhysicsOverrideInterop.NativeDeformationOverrideItem[] _items;
    private readonly float[] _points;
    private readonly byte[] _pathBytes;
    private int _count;
    private int _pointCount;
    private int _pathByteCount;
    private long _droppedRegions;
    private long _unboundRegions;
    private long _refreshCount;

    /// <summary>Initializes a bounded reusable Storm deformation batch.</summary>
    /// <param name="capacity">The maximum number of regions one batch carries.</param>
    /// <param name="pointCapacity">The maximum number of points the shared page carries.</param>
    /// <param name="pathByteCapacity">The maximum packed prim path payload, in bytes.</param>
    /// <exception cref="ArgumentOutOfRangeException">A capacity is outside the ABI bounds.</exception>
    public StormPhysicsDeformationOverrides(
        int capacity,
        int pointCapacity,
        int pathByteCapacity)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(capacity);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(capacity, MaximumCapacity);
        ArgumentOutOfRangeException.ThrowIfNegative(pointCapacity);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(pointCapacity, MaximumPoints);
        ArgumentOutOfRangeException.ThrowIfNegative(pathByteCapacity);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(pathByteCapacity, MaximumPathBytes);
        _items = capacity == 0
            ? []
            : new StormPhysicsOverrideInterop.NativeDeformationOverrideItem[capacity];
        _points = pointCapacity == 0 ? [] : new float[checked(pointCapacity * 3)];
        _pathBytes = pathByteCapacity == 0 ? [] : new byte[pathByteCapacity];
    }

    /// <summary>Gets the number of regions the batch can carry.</summary>
    public int Capacity => _items.Length;

    /// <summary>Gets the number of points the shared page can carry.</summary>
    public int PointCapacity => _points.Length / 3;

    /// <summary>Gets the packed prim path capacity, in bytes.</summary>
    public int PathByteCapacity => _pathBytes.Length;

    /// <summary>Gets the number of regions the last refresh packed.</summary>
    public int Count => _count;

    /// <summary>Gets the number of points the last refresh packed.</summary>
    public int PointCount => _pointCount;

    /// <summary>Gets the packed prim path payload of the last refresh, in bytes.</summary>
    public int PathByteCount => _pathByteCount;

    /// <summary>Gets the revision of the batch.</summary>
    public ulong Revision { get; private set; }

    /// <summary>Gets the number of regions dropped because a bounded capacity was reached.</summary>
    public long DroppedRegions => _droppedRegions;

    /// <summary>Gets the number of regions that resolved to no bound prim.</summary>
    public long UnboundRegions => _unboundRegions;

    /// <summary>Gets the number of refreshes performed.</summary>
    public long RefreshCount => _refreshCount;

    /// <summary>Reports whether Storm can draw one deformable domain.</summary>
    /// <param name="domain">The domain the caller wants to draw.</param>
    /// <returns><see langword="true"/> when Storm can draw the domain's geometry.</returns>
    /// <remarks>
    /// A cloth and a volume deformable publish one simulated position per rendered vertex, so the
    /// rendered prim's own points can be replaced. A particle system publishes positions that
    /// belong to no rendered mesh vertex, so it is reported unsupported here rather than drawn
    /// against whatever prim happens to be bound to it.
    /// </remarks>
    public static bool IsDomainSupported(PhysicsRenderDomain domain) => domain is
        PhysicsRenderDomain.Cloth or
        PhysicsRenderDomain.Deformable;

    /// <summary>Rebuilds the batch from one render update without allocating when warmed.</summary>
    /// <param name="deformations">The renderer-neutral geometry produced by the render update.</param>
    /// <param name="bindings">The identity to render prim path bindings.</param>
    /// <returns>The number of regions packed into the batch.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="bindings"/> is null.</exception>
    public int Refresh(
        in PhysicsRenderDeformationView deformations,
        PhysicsRenderBindingTable bindings)
    {
        ArgumentNullException.ThrowIfNull(bindings);
        _count = 0;
        _pointCount = 0;
        _pathByteCount = 0;
        Revision = deformations.Revision;
        _refreshCount++;

        ReadOnlySpan<PhysicsRenderDeformableRegion> regions = deformations.Regions;
        for (int index = 0; index < regions.Length; index++)
        {
            PhysicsRenderDeformableRegion region = regions[index];
            if (!IsDomainSupported(region.Domain))
            {
                continue;
            }
            if (!bindings.TryResolve(region.Id, out PhysicsRenderBinding binding))
            {
                _unboundRegions++;
                continue;
            }
            if (_count == _items.Length || region.VertexCount <= 0)
            {
                _droppedRegions++;
                continue;
            }

            int required = StrictUtf8.GetByteCount(binding.PrimPath);
            if (required == 0 || required > _pathBytes.Length - _pathByteCount ||
                region.VertexCount > PointCapacity - _pointCount)
            {
                _droppedRegions++;
                continue;
            }

            ReadOnlySpan<float> vertices = deformations.GetVertices(region);
            vertices.CopyTo(_points.AsSpan(_pointCount * 3));
            int written = StrictUtf8.GetBytes(binding.PrimPath, _pathBytes.AsSpan(_pathByteCount));
            ref StormPhysicsOverrideInterop.NativeDeformationOverrideItem item = ref _items[_count];
            item.ObjectId = region.Id.Value;
            item.PathOffset = checked((uint)_pathByteCount);
            item.PathLength = checked((uint)written);
            item.InstanceIndex = binding.InstanceIndex > 0 ? binding.InstanceIndex : -1;
            item.Flags = 0;
            item.PointOffset = checked((uint)_pointCount);
            item.PointCount = checked((uint)region.VertexCount);
            item.TopologyRevision = region.TopologyRevision;
            _pointCount += region.VertexCount;
            _pathByteCount += written;
            _count++;
        }

        return _count;
    }

    /// <summary>Empties the batch so applying it restores every authored point set.</summary>
    /// <param name="revision">The revision the emptied batch reports.</param>
    public void Clear(ulong revision = 0)
    {
        _count = 0;
        _pointCount = 0;
        _pathByteCount = 0;
        Revision = revision;
    }

    /// <summary>Resets the batch and every counter.</summary>
    public void Reset()
    {
        Clear();
        _droppedRegions = 0;
        _unboundRegions = 0;
        _refreshCount = 0;
    }

    /// <summary>Describes the batch for logs and evidence artifacts.</summary>
    /// <returns>The invariant single-line description.</returns>
    public string Describe() => string.Create(
        CultureInfo.InvariantCulture,
        $"storm deformation batch: count={_count}/{Capacity} " +
        $"points={_pointCount}/{PointCapacity} " +
        $"pathBytes={_pathByteCount}/{PathByteCapacity} revision={Revision} " +
        $"dropped={_droppedRegions} unbound={_unboundRegions} refreshes={_refreshCount}");

    internal ReadOnlySpan<StormPhysicsOverrideInterop.NativeDeformationOverrideItem> Items =>
        _items.AsSpan(0, _count);

    internal ReadOnlySpan<float> Points => _points.AsSpan(0, _pointCount * 3);

    internal ReadOnlySpan<byte> PathBytes => _pathBytes.AsSpan(0, _pathByteCount);

    /// <summary>Reads back the authored prim path one packed region names.</summary>
    /// <param name="index">The zero-based packed region ordinal.</param>
    /// <returns>The authored prim path the region drives.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The ordinal is outside the batch.</exception>
    public string PathAt(int index)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, _count);
        StormPhysicsOverrideInterop.NativeDeformationOverrideItem item = _items[index];
        return StrictUtf8.GetString(
            _pathBytes.AsSpan(checked((int)item.PathOffset), checked((int)item.PathLength)));
    }

    /// <summary>Reads back the stable simulation identity one packed region names.</summary>
    /// <param name="index">The zero-based packed region ordinal.</param>
    /// <returns>The stable identity the region carries.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The ordinal is outside the batch.</exception>
    public ulong ObjectIdAt(int index)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, _count);
        return _items[index].ObjectId;
    }

    /// <summary>Reads back the packed points of one region from the shared page.</summary>
    /// <param name="index">The zero-based packed region ordinal.</param>
    /// <returns>The region's point components, three per point.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The ordinal is outside the batch.</exception>
    public ReadOnlySpan<float> PointsAt(int index)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, _count);
        StormPhysicsOverrideInterop.NativeDeformationOverrideItem item = _items[index];
        return _points.AsSpan(
            checked((int)item.PointOffset) * 3,
            checked((int)item.PointCount) * 3);
    }
}
