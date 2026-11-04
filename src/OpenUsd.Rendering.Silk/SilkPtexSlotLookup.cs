// Copyright (c) marcschier. Licensed under the MIT License.

using System.Runtime.CompilerServices;

namespace OpenUsd.Rendering.Silk;

internal readonly record struct SilkPtexSlotLayout(
    SilkMaterialParameter Parameter,
    ulong SourceIdentity,
    SilkPtexAtlasLayout Layout);

// Candidate transport only. Production bindings must not consume this until the
// face-index attribute, per-slot lookup bindings and all backend shaders qualify.
internal sealed class SilkPtexSlotLookup : IDisposable
{
    private IDisposable? _reservation;
    private readonly SilkPtexSlotLayout[] _slots;
    private uint[] _cornerFaces;
    private float[] _cornerCoordinates;
    private float[] _transforms;
    private readonly object _gate = new();
    private int _leases;
    private bool _disposed;

    private SilkPtexSlotLookup(
        SilkPtexSlotLayout[] slots,
        uint[] cornerFaces,
        float[] cornerCoordinates,
        float[] transforms,
        int faceCount,
        IDisposable reservation)
    {
        _slots = slots;
        _cornerFaces = cornerFaces;
        _cornerCoordinates = cornerCoordinates;
        _transforms = transforms;
        FaceCount = faceCount;
        _reservation = reservation;
    }

    internal int FaceCount { get; }
    internal int SlotCount => _slots.Length;
    internal Lease AcquireLease()
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            var lease = new Lease(this);
            _leases = checked(_leases + 1);
            return lease;
        }
    }

    internal sealed class Lease(SilkPtexSlotLookup owner) : IDisposable
    {
        private bool _disposed;

        internal int ShaderWordCount
        {
            get
            {
                lock (owner._gate)
                {
                    ObjectDisposedException.ThrowIf(_disposed, this);
                    return SilkPtexCandidateBinding.GetWordCount(
                        owner._cornerFaces.Length, owner.FaceCount, owner.SlotCount);
                }
            }
        }

        internal void CopyShaderWords(Span<uint> destination)
        {
            lock (owner._gate)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                int count = ShaderWordCount;
                if (destination.Length != count)
                {
                    throw new ArgumentException("Ptex shader storage must have the exact word count.",
                        nameof(destination));
                }
                destination[0] = checked((uint)owner._cornerFaces.Length);
                destination[1] = checked((uint)owner.FaceCount);
                destination[2] = checked((uint)owner.SlotCount);
                int transformStart = checked(SilkPtexCandidateBinding.HeaderWords +
                    owner._cornerFaces.Length * SilkPtexCandidateBinding.CornerWords);
                destination[3] = checked((uint)transformStart);
                for (int corner = 0; corner < owner._cornerFaces.Length; corner++)
                {
                    int offset = SilkPtexCandidateBinding.HeaderWords +
                        corner * SilkPtexCandidateBinding.CornerWords;
                    destination[offset] = owner._cornerFaces[corner];
                    destination[offset + 1] = BitConverter.SingleToUInt32Bits(owner._cornerCoordinates[corner * 2]);
                    destination[offset + 2] = BitConverter.SingleToUInt32Bits(owner._cornerCoordinates[corner * 2 + 1]);
                    destination[offset + 3] = 0;
                }
                for (int value = 0; value < owner._transforms.Length; value++)
                {
                    destination[transformStart + value] = BitConverter.SingleToUInt32Bits(owner._transforms[value]);
                }
            }
        }

        internal void CopyCornerFaces(Span<uint> destination)
        {
            lock (owner._gate)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                owner._cornerFaces.AsSpan().CopyTo(destination);
            }
        }

        internal void CopyCornerCoordinates(Span<float> destination)
        {
            lock (owner._gate)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                owner._cornerCoordinates.AsSpan().CopyTo(destination);
            }
        }

        internal void CopyTransforms(Span<float> destination)
        {
            lock (owner._gate)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                owner._transforms.AsSpan().CopyTo(destination);
            }
        }

        public void Dispose()
        {
            lock (owner._gate)
            {
                if (_disposed)
                {
                    return;
                }
                _disposed = true;
                owner._leases--;
                if (owner._disposed && owner._leases == 0)
                {
                    owner.ReleaseStorage();
                }
            }
        }
    }

    internal static SilkPtexSlotLookup Prepare(
        SilkMeshData mesh,
        IReadOnlyList<SilkPtexSlotLayout> slots,
        SilkCpuTextureBudget budget)
    {
        ArgumentNullException.ThrowIfNull(mesh);
        ArgumentNullException.ThrowIfNull(slots);
        ArgumentNullException.ThrowIfNull(budget);
        if (slots.Count is < 1 or > 16)
        {
            throw new InvalidDataException("A Ptex lookup requires between one and sixteen slots.");
        }
        int corners = mesh.Indices.Length;
        if (mesh.TopologyKind != SilkTopologyKind.TriangleList ||
            mesh.PtexMappingStatus != SilkPtexMappingStatus.Mapped ||
            corners == 0 || corners % 3 != 0 ||
            mesh.PtexFaceIndices.Length != corners / 3 ||
            mesh.PtexCoordinates.LongLength != (long)corners * 2)
        {
            throw new InvalidDataException("A Ptex lookup requires complete triangle-corner mappings.");
        }
        // Include temporary face-ID sorting storage in admission. No allocation
        // scales with a sparse authored face ID, only with emitted triangles.
        int triangles = corners / 3;
        ulong bytes = checked((ulong)corners * 3 * sizeof(uint) +
            (ulong)triangles * sizeof(int) +
            (ulong)triangles * (ulong)slots.Count * 4 * sizeof(float) +
            (ulong)slots.Count * (ulong)Unsafe.SizeOf<SilkPtexSlotLayout>());
        IDisposable reservation = budget.Reserve(bytes);
        try
        {
            int[] faces = mesh.PtexFaceIndices.ToArray();
            Array.Sort(faces);
            int faceCount = 0;
            foreach (int face in faces)
            {
                if (face < 0)
                {
                    throw new InvalidDataException("A Ptex lookup face ID must be non-negative.");
                }
                if (faceCount == 0 || faces[faceCount - 1] != face)
                {
                    faces[faceCount++] = face;
                }
            }
            var retainedSlots = new SilkPtexSlotLayout[slots.Count];
            var transforms = new float[checked(faceCount * slots.Count * 4)];
            for (int slot = 0; slot < slots.Count; slot++)
            {
                SilkPtexSlotLayout input = slots[slot];
                ArgumentNullException.ThrowIfNull(input.Layout);
                for (int previous = 0; previous < slot; previous++)
                {
                    if (retainedSlots[previous].Parameter == input.Parameter)
                    {
                        throw new InvalidDataException("A Ptex lookup cannot bind a surface slot twice.");
                    }
                }
                retainedSlots[slot] = input;
                for (int face = 0; face < faceCount; face++)
                {
                    uint faceId = checked((uint)faces[face]);
                    (float u0, float v0) = input.Layout.Map(faceId, 0, 0);
                    (float u1, float v1) = input.Layout.Map(faceId, 1, 1);
                    int offset = (slot * faceCount + face) * 4;
                    transforms[offset] = u0;
                    transforms[offset + 1] = v0;
                    transforms[offset + 2] = u1 - u0;
                    transforms[offset + 3] = v1 - v0;
                }
            }
            // Every corner in one emitted triangle carries the same compact
            // face index. A backend must transport it without interpolation;
            // only the face-local coordinates are interpolated.
            var cornerFaces = new uint[corners];
            float[] coordinates = mesh.PtexCoordinates.ToArray();
            for (int corner = 0; corner < corners; corner++)
            {
                cornerFaces[corner] = checked((uint)Array.BinarySearch(
                    faces, 0, faceCount, mesh.PtexFaceIndices[corner / 3]));
                float u = coordinates[corner * 2];
                float v = coordinates[corner * 2 + 1];
                if (!float.IsFinite(u) || !float.IsFinite(v) ||
                    u is < 0 or > 1 || v is < 0 or > 1)
                {
                    throw new InvalidDataException("A Ptex lookup coordinate is outside its unit face.");
                }
            }
            return new SilkPtexSlotLookup(
                retainedSlots, cornerFaces, coordinates, transforms, faceCount, reservation);
        }
        catch
        {
            reservation.Dispose();
            throw;
        }
    }

    internal (float U, float V) MapCorner(int slot, int corner)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            ArgumentOutOfRangeException.ThrowIfNegative(slot);
            ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(slot, SlotCount);
            ArgumentOutOfRangeException.ThrowIfNegative(corner);
            ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(corner, _cornerFaces.Length);
            int offset = checked((slot * FaceCount + (int)_cornerFaces[corner]) * 4);
            return (
                _transforms[offset] + _cornerCoordinates[corner * 2] * _transforms[offset + 2],
                _transforms[offset + 1] + _cornerCoordinates[corner * 2 + 1] * _transforms[offset + 3]);
        }
    }

    internal bool HasSameSources(IReadOnlyList<SilkPtexSlotLayout> slots)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (slots.Count != _slots.Length)
            {
                return false;
            }
            for (int slot = 0; slot < slots.Count; slot++)
            {
                if (slots[slot].Parameter != _slots[slot].Parameter ||
                    slots[slot].SourceIdentity != _slots[slot].SourceIdentity ||
                    !ReferenceEquals(slots[slot].Layout, _slots[slot].Layout))
                {
                    return false;
                }
            }
            return true;
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }
            _disposed = true;
            if (_leases == 0)
            {
                ReleaseStorage();
            }
        }
    }

    private void ReleaseStorage()
    {
        _cornerFaces = [];
        _cornerCoordinates = [];
        _transforms = [];
        Array.Clear(_slots);
        Interlocked.Exchange(ref _reservation, null)?.Dispose();
    }
}
