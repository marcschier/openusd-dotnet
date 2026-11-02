// Copyright (c) marcschier. Licensed under the MIT License.

namespace OpenUsd.Rendering.Silk;

internal sealed class SilkPtexMeshSelection : IDisposable
{
    private readonly object _gate = new();
    private uint[]? _faceIds;
    private uint[]? _triangleOrdinals;
    private IDisposable? _faceReservation;
    private IDisposable? _ordinalReservation;
    private int _leases;
    private bool _disposed;

    private SilkPtexMeshSelection(
        uint[] faceIds,
        uint[] triangleOrdinals,
        SilkCpuTextureBudget? budget,
        IDisposable? faceReservation,
        IDisposable? ordinalReservation)
    {
        _faceIds = faceIds;
        _triangleOrdinals = triangleOrdinals;
        Budget = budget;
        _faceReservation = faceReservation;
        _ordinalReservation = ordinalReservation;
    }

    internal SilkCpuTextureBudget? Budget { get; }

    internal Lease Acquire()
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            var lease = new Lease(this, _faceIds!, _triangleOrdinals!);
            _leases = checked(_leases + 1);
            return lease;
        }
    }

    internal static SilkPtexMeshSelection Create(
        SilkMeshData mesh,
        SilkCpuTextureBudget? budget = null)
    {
        ArgumentNullException.ThrowIfNull(mesh);
        if (mesh.PtexMappingStatus != SilkPtexMappingStatus.Mapped)
        {
            throw new NotSupportedException(
                $"Mesh '{mesh.Path}' has no usable Ptex mapping: {mesh.PtexMappingStatus}.");
        }
        int indexCount = mesh.Indices.Length;
        if (mesh.TopologyKind != SilkTopologyKind.TriangleList ||
            indexCount % 3 != 0 ||
            mesh.PtexFaceIndices.Length != indexCount / 3 ||
            mesh.PtexCoordinates.LongLength != (long)indexCount * 2)
        {
            throw new InvalidDataException(
                $"Mesh '{mesh.Path}' has inconsistent Ptex triangle mappings.");
        }
        return Create(mesh.PtexFaceIndices, budget);
    }

    internal static SilkPtexMeshSelection Create(
        ReadOnlySpan<int> triangleFaceIds,
        SilkCpuTextureBudget? budget = null)
    {
        if (triangleFaceIds.IsEmpty)
        {
            return new SilkPtexMeshSelection([], [], budget, null, null);
        }

        ulong triangleBytes = checked((ulong)triangleFaceIds.Length * sizeof(uint));
        IDisposable? ordinalReservation = null;
        IDisposable? scratchReservation = null;
        IDisposable? faceReservation = null;
        try
        {
            ordinalReservation = budget?.Reserve(triangleBytes);
            var triangleOrdinals = new uint[triangleFaceIds.Length];
            for (int triangle = 0; triangle < triangleFaceIds.Length; triangle++)
            {
                int faceId = triangleFaceIds[triangle];
                if (faceId < 0)
                {
                    throw new InvalidDataException(
                        $"Ptex triangle {triangle} has a negative face index.");
                }
                triangleOrdinals[triangle] = (uint)faceId;
            }

            scratchReservation = budget?.Reserve(triangleBytes);
            uint[] sorted = (uint[])triangleOrdinals.Clone();
            Array.Sort(sorted);
            int faceCount = 0;
            for (int index = 0; index < sorted.Length; index++)
            {
                uint faceId = sorted[index];
                if (faceCount != 0 && sorted[faceCount - 1] == faceId)
                {
                    continue;
                }
                if (faceCount == SilkNativePtexDecoder.MaximumRequestedFaces)
                {
                    throw new InvalidDataException(
                        "The Ptex mesh selection exceeds the face-count limit.");
                }
                sorted[faceCount++] = faceId;
            }

            ulong faceBytes = checked((ulong)faceCount * sizeof(uint));
            faceReservation = budget?.Reserve(faceBytes);
            var faceIds = new uint[faceCount];
            sorted.AsSpan(0, faceCount).CopyTo(faceIds);
            for (int triangle = 0; triangle < triangleOrdinals.Length; triangle++)
            {
                int ordinal = Array.BinarySearch(faceIds, triangleOrdinals[triangle]);
                if (ordinal < 0)
                {
                    throw new InvalidDataException(
                        $"Ptex triangle {triangle} references an absent selected face.");
                }
                triangleOrdinals[triangle] = (uint)ordinal;
            }

            var result = new SilkPtexMeshSelection(
                faceIds, triangleOrdinals, budget, faceReservation, ordinalReservation);
            faceReservation = null;
            ordinalReservation = null;
            return result;
        }
        finally
        {
            scratchReservation?.Dispose();
            faceReservation?.Dispose();
            ordinalReservation?.Dispose();
        }
    }

    public void Dispose()
    {
        IDisposable? faceReservation = null;
        IDisposable? ordinalReservation = null;
        lock (_gate)
        {
            if (!_disposed)
            {
                _disposed = true;
                _faceIds = null;
                _triangleOrdinals = null;
                if (_leases == 0)
                {
                    faceReservation = _faceReservation;
                    ordinalReservation = _ordinalReservation;
                    _faceReservation = null;
                    _ordinalReservation = null;
                }
            }
        }
        faceReservation?.Dispose();
        ordinalReservation?.Dispose();
    }

    private void Release()
    {
        IDisposable? faceReservation = null;
        IDisposable? ordinalReservation = null;
        lock (_gate)
        {
            _leases--;
            if (_disposed && _leases == 0)
            {
                faceReservation = _faceReservation;
                ordinalReservation = _ordinalReservation;
                _faceReservation = null;
                _ordinalReservation = null;
            }
        }
        faceReservation?.Dispose();
        ordinalReservation?.Dispose();
    }

    internal sealed class Lease(
        SilkPtexMeshSelection owner,
        uint[] faceIds,
        uint[] triangleOrdinals) : IDisposable
    {
        private SilkPtexMeshSelection? _owner = owner;
        private uint[]? _faceIds = faceIds;
        private uint[]? _triangleOrdinals = triangleOrdinals;

        internal ReadOnlySpan<uint> FaceIds
        {
            get
            {
                ObjectDisposedException.ThrowIf(Volatile.Read(ref _owner) is null, this);
                return Volatile.Read(ref _faceIds) ?? throw new ObjectDisposedException(nameof(Lease));
            }
        }

        internal ReadOnlySpan<uint> TriangleOrdinals
        {
            get
            {
                ObjectDisposedException.ThrowIf(Volatile.Read(ref _owner) is null, this);
                return Volatile.Read(ref _triangleOrdinals) ?? throw new ObjectDisposedException(nameof(Lease));
            }
        }

        public void Dispose()
        {
            SilkPtexMeshSelection? released = Interlocked.Exchange(ref _owner, null);
            if (released is not null)
            {
                _faceIds = null;
                _triangleOrdinals = null;
                released.Release();
            }
        }
    }
}
