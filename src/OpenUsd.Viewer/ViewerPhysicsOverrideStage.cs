// Copyright (c) marcschier. Licensed under the MIT License.

using OpenUsd.Rendering;

namespace OpenUsd.Viewer;

/// <summary>
/// One override batch borrowed from a <see cref="ViewerPhysicsOverrideStage"/>.
/// </summary>
/// <remarks>
/// The batch owns its buffer for as long as it is alive: the render loop cannot stage into that
/// buffer again until the batch is disposed, which is what makes reading it outside the stage lock
/// safe. Disposing returns the buffer to the stage; failing to dispose only costs the stage one
/// buffer, never correctness.
/// </remarks>
internal readonly struct ViewerPhysicsOverrideBatch
    : IDisposable, IEquatable<ViewerPhysicsOverrideBatch>
{
    private readonly ViewerPhysicsOverrideStage? _stage;
    private readonly int _bufferIndex;

    /// <summary>Initializes a batch that borrows one staged buffer.</summary>
    /// <param name="stage">The stage the buffer is returned to.</param>
    /// <param name="bufferIndex">The borrowed buffer.</param>
    /// <param name="overrides">The staged overrides.</param>
    /// <param name="bindings">The binding table the batch was staged with.</param>
    internal ViewerPhysicsOverrideBatch(
        ViewerPhysicsOverrideStage stage,
        int bufferIndex,
        PhysicsRenderOverrideView overrides,
        PhysicsRenderBindingTable bindings)
    {
        _stage = stage;
        _bufferIndex = bufferIndex;
        Overrides = overrides;
        Bindings = bindings;
    }

    /// <summary>Gets the staged overrides.</summary>
    internal PhysicsRenderOverrideView Overrides { get; }

    /// <summary>Gets the binding table naming the prim each identity drives.</summary>
    internal PhysicsRenderBindingTable Bindings { get; }

    /// <summary>Returns the borrowed buffer to the stage.</summary>
    public void Dispose() => _stage?.Release(_bufferIndex);

    /// <inheritdoc/>
    public bool Equals(ViewerPhysicsOverrideBatch other) =>
        ReferenceEquals(_stage, other._stage) &&
        _bufferIndex == other._bufferIndex &&
        Overrides.Equals(other.Overrides);

    /// <inheritdoc/>
    public override bool Equals(object? obj) =>
        obj is ViewerPhysicsOverrideBatch other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode() => HashCode.Combine(_stage, _bufferIndex);

    /// <summary>Compares two batches.</summary>
    /// <param name="left">The first batch.</param>
    /// <param name="right">The second batch.</param>
    /// <returns><see langword="true"/> when both borrow the same buffer.</returns>
    public static bool operator ==(
        ViewerPhysicsOverrideBatch left,
        ViewerPhysicsOverrideBatch right) => left.Equals(right);

    /// <summary>Compares two batches.</summary>
    /// <param name="left">The first batch.</param>
    /// <param name="right">The second batch.</param>
    /// <returns><see langword="true"/> when the batches borrow different buffers.</returns>
    public static bool operator !=(
        ViewerPhysicsOverrideBatch left,
        ViewerPhysicsOverrideBatch right) => !left.Equals(right);
}

/// <summary>
/// Carries one override batch from the render loop across a backend's thread boundary.
/// </summary>
/// <remarks>
/// <para>
/// Backends own their poses on their own thread: Storm asserts its OpenGL context thread, and the
/// hdSilk mesh renderers resolve overrides against a retained scene the presenting thread owns. The
/// stage therefore takes a private copy of the batch on the render loop and hands it over the next
/// time the owning thread is inside its frame, which keeps override delivery correct without any
/// blocking hand-off between the two.
/// </para>
/// <para>
/// The copy is written into one of three rotating buffers, never into the buffer a consumer is
/// reading. A single shared buffer would let the render loop overwrite the poses the presenting
/// thread is halfway through resolving, so one drawn batch could be partly one simulated frame and
/// partly the next - bodies visibly teleporting relative to each other. Three buffers is exactly
/// what one producer and one consumer need to never block and never tear: one being written, one
/// published, one borrowed.
/// </para>
/// <para>
/// Only the newest batch is retained. A backend that misses frames must not replay stale poses; it
/// must show the newest pose it can, which is what a simulation viewer means by dropping frames.
/// The warm path never allocates: each buffer grows once to the batch size and is then reused.
/// </para>
/// </remarks>
internal sealed class ViewerPhysicsOverrideStage
{
    /// <summary>The number of rotating buffers one producer and one consumer need.</summary>
    private const int BufferCount = 3;

    private readonly object _gate = new();
    private readonly PhysicsRenderTransformOverride[][] _buffers = [[], [], []];
    private readonly int[] _counts = new int[BufferCount];
    private readonly ulong[] _revisions = new ulong[BufferCount];
    private readonly bool[] _borrowed = new bool[BufferCount];
    private PhysicsRenderBindingTable _bindings = new(1);
    private int _pendingIndex = -1;
    private int _writeIndex;
    private long _stagedBatches;
    private long _consumedBatches;
    private long _droppedBatches;
    private ViewerPhysicsOverrideReport _report;
    private bool _hasReport;

    // The deformable half of one staged frame. It is guarded by the same gate and
    // published with the same discipline as the transform half, but it is stored
    // separately because a frame very often carries transforms and no geometry,
    // and a per vertex buffer that is copied for every such frame would dominate
    // the cost of staging.
    private readonly object _deformationGate = new();
    private PhysicsRenderDeformableRegion[] _pendingRegions = [];
    private float[] _pendingVertices = [];
    private int _pendingRegionCount;
    private int _pendingVertexComponentCount;
    private ulong _pendingDeformationRevision;
    private bool _hasPendingDeformations;

    /// <summary>Gets the binding table the newest staged batch was staged with.</summary>
    internal PhysicsRenderBindingTable Bindings
    {
        get
        {
            lock (_gate)
            {
                return _bindings;
            }
        }
    }

    /// <summary>Stages the deformable half of one frame, replacing whatever was staged.</summary>
    /// <param name="deformations">The deformable geometry one render update produced.</param>
    /// <returns>The number of regions staged.</returns>
    internal int StageDeformations(in PhysicsRenderDeformationView deformations)
    {
        ReadOnlySpan<PhysicsRenderDeformableRegion> regions = deformations.Regions;
        ReadOnlySpan<float> vertices = deformations.Vertices;
        lock (_deformationGate)
        {
            if (_pendingRegions.Length < regions.Length)
            {
                _pendingRegions = new PhysicsRenderDeformableRegion[regions.Length];
            }
            if (_pendingVertices.Length < vertices.Length)
            {
                _pendingVertices = new float[vertices.Length];
            }

            regions.CopyTo(_pendingRegions);
            vertices.CopyTo(_pendingVertices);
            _pendingRegionCount = regions.Length;
            _pendingVertexComponentCount = vertices.Length;
            _pendingDeformationRevision = deformations.Revision;
            _hasPendingDeformations = true;
        }

        return regions.Length;
    }

    /// <summary>Takes the staged deformable half, if one was staged since the last take.</summary>
    /// <param name="deformations">Receives a private copy the caller owns for the call.</param>
    /// <returns><see langword="true"/> when a batch was taken.</returns>
    /// <remarks>
    /// An empty batch is still a batch. The renderer replaces every retained deformation with the
    /// batch it is handed, so handing it an empty one is the only thing that restores the authored
    /// points; treating "no regions" as "nothing staged" would leave a stopped simulation's last
    /// geometry on screen for the life of the renderer. What decides is therefore whether a batch
    /// is pending, not how many regions it carries. The staged buffers are copied out under the
    /// gate, so a newer batch never mutates a view a consumer is still reading.
    /// </remarks>
    internal bool TryTakeDeformations(out PhysicsRenderDeformationView deformations)
    {
        lock (_deformationGate)
        {
            if (!_hasPendingDeformations)
            {
                deformations = PhysicsRenderDeformationView.Empty;
                return false;
            }

            deformations = _pendingRegionCount == 0
                ? new PhysicsRenderDeformationView(
                    ReadOnlyMemory<PhysicsRenderDeformableRegion>.Empty,
                    ReadOnlyMemory<float>.Empty,
                    _pendingDeformationRevision)
                : new PhysicsRenderDeformationView(
                    _pendingRegions.AsMemory(0, _pendingRegionCount).ToArray(),
                    _pendingVertices.AsMemory(0, _pendingVertexComponentCount).ToArray(),
                    _pendingDeformationRevision);
            _pendingRegionCount = 0;
            _pendingVertexComponentCount = 0;
            _hasPendingDeformations = false;
            return true;
        }
    }

    /// <summary>Stages an empty batch so the renderer restores the authored points.</summary>
    internal void ClearDeformations()
    {
        lock (_deformationGate)
        {
            _pendingRegionCount = 0;
            _pendingVertexComponentCount = 0;
            _pendingDeformationRevision = 0;
            _hasPendingDeformations = true;
        }
    }

    /// <summary>Gets the number of batches the render loop staged.</summary>
    internal long StagedBatches => Interlocked.Read(ref _stagedBatches);

    /// <summary>Gets the number of batches an owning thread consumed.</summary>
    internal long ConsumedBatches => Interlocked.Read(ref _consumedBatches);

    /// <summary>Gets the number of staged batches a newer batch replaced before consumption.</summary>
    internal long DroppedBatches => Interlocked.Read(ref _droppedBatches);

    /// <summary>Gets the revision of the newest staged batch.</summary>
    internal ulong Revision
    {
        get
        {
            lock (_gate)
            {
                return _pendingIndex < 0 ? 0UL : _revisions[_pendingIndex];
            }
        }
    }

    /// <summary>Stages one complete batch, replacing any batch that was not consumed yet.</summary>
    /// <param name="overrides">The overrides one render update produced.</param>
    /// <param name="bindings">The table naming the prim each identity drives.</param>
    /// <returns>The number of overrides staged.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="bindings"/> is null.</exception>
    internal int Stage(in PhysicsRenderOverrideView overrides, PhysicsRenderBindingTable bindings)
    {
        ArgumentNullException.ThrowIfNull(bindings);
        ReadOnlySpan<PhysicsRenderTransformOverride> items = overrides.Items;
        lock (_gate)
        {
            int index = AcquireWriteBuffer();
            if (index < 0)
            {
                return 0;
            }

            PhysicsRenderTransformOverride[] buffer = _buffers[index];
            if (buffer.Length < items.Length)
            {
                buffer = new PhysicsRenderTransformOverride[items.Length];
                _buffers[index] = buffer;
            }

            items.CopyTo(buffer);
            _counts[index] = items.Length;
            _revisions[index] = overrides.Revision;
            _bindings = bindings;
            Publish(index);
        }

        Interlocked.Increment(ref _stagedBatches);
        return items.Length;
    }

    /// <summary>Takes the newest staged batch, if the owning thread has not consumed it.</summary>
    /// <param name="batch">Receives the borrowed batch, which the caller must dispose.</param>
    /// <returns><see langword="true"/> when a batch was taken.</returns>
    internal bool TryTake(out ViewerPhysicsOverrideBatch batch)
    {
        lock (_gate)
        {
            int index = _pendingIndex;
            if (index < 0)
            {
                batch = default;
                return false;
            }

            _pendingIndex = -1;
            _borrowed[index] = true;
            batch = new ViewerPhysicsOverrideBatch(
                this,
                index,
                new PhysicsRenderOverrideView(
                    _buffers[index].AsMemory(0, _counts[index]),
                    _revisions[index]),
                _bindings);
        }

        Interlocked.Increment(ref _consumedBatches);
        return true;
    }

    /// <summary>Stages an empty batch so the owning thread restores the authored transforms.</summary>
    internal void Clear()
    {
        lock (_gate)
        {
            int index = AcquireWriteBuffer();
            if (index < 0)
            {
                return;
            }

            _counts[index] = 0;
            _revisions[index] = 0;
            Publish(index);
        }

        Interlocked.Increment(ref _stagedBatches);
    }

    /// <summary>Returns one borrowed buffer so the render loop may stage into it again.</summary>
    /// <param name="bufferIndex">The buffer the consumer borrowed.</param>
    internal void Release(int bufferIndex)
    {
        if ((uint)bufferIndex >= BufferCount)
        {
            return;
        }

        lock (_gate)
        {
            _borrowed[bufferIndex] = false;
        }
    }

    /// <summary>Publishes what the owning thread actually resolved from one consumed batch.</summary>
    /// <param name="revision">The override revision the report describes.</param>
    /// <param name="applied">The overrides that resolved to something drawable.</param>
    /// <param name="unresolved">The overrides that resolved to nothing.</param>
    /// <remarks>
    /// Only the newest report is kept. The render loop reads it to decide whether the backend is
    /// really drawing the simulation; an unread older report describes a batch the newer one has
    /// already replaced on screen, so retaining it would only report stale counts.
    /// </remarks>
    internal void PublishReport(ulong revision, int applied, int unresolved)
    {
        lock (_gate)
        {
            _report = new ViewerPhysicsOverrideReport(revision, applied, unresolved);
            _hasReport = true;
        }
    }

    /// <summary>Takes the newest unread report of what the owning thread resolved.</summary>
    /// <param name="report">Receives the newest unread report.</param>
    /// <returns><see langword="true"/> when a report was taken.</returns>
    internal bool TryTakeReport(out ViewerPhysicsOverrideReport report)
    {
        lock (_gate)
        {
            if (!_hasReport)
            {
                report = default;
                return false;
            }

            report = _report;
            _hasReport = false;
            return true;
        }
    }

    private int AcquireWriteBuffer()
    {
        for (int attempt = 0; attempt < BufferCount; attempt++)
        {
            int index = _writeIndex;
            _writeIndex = (_writeIndex + 1) % BufferCount;
            if (!_borrowed[index] && index != _pendingIndex)
            {
                return index;
            }
        }

        // One producer and one consumer can hold at most two of the three buffers, so this is
        // unreachable in the viewer. Refusing rather than overwriting keeps a misuse - two
        // consumers borrowing at once - from tearing a batch that is being read.
        return -1;
    }

    private void Publish(int index)
    {
        if (_pendingIndex >= 0)
        {
            Interlocked.Increment(ref _droppedBatches);
        }

        _pendingIndex = index;
    }
}
