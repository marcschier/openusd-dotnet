// Copyright (c) marcschier. Licensed under the MIT License.

using System.Runtime.CompilerServices;

namespace OpenUsd.Rendering.Silk;

internal readonly record struct SilkPtexSlotSource(
    SilkMaterialParameter Parameter,
    ulong Identity,
    SilkDecodedPtexFaces Decoded);

// CPU publication candidate, deliberately not wired to production scene resources.
// Atlases and lookup tables become visible together; GPU ownership and bindings
// still require the qualified backend contract.
internal sealed class SilkPtexSlotCandidateCache : IDisposable
{
    private bool _disposed;
    internal Candidate? Current { get; private set; }

    internal PreparedUpdate Prepare(
        SilkMeshData mesh,
        SilkMeshGeometry ordinaryGeometry,
        IReadOnlyList<SilkPtexSlotSource> sources,
        uint maximumDimension,
        SilkCpuTextureBudget budget,
        ulong retryRevision,
        SilkPtexCornerCertificate? cornerCertificate = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(mesh);
        ArgumentNullException.ThrowIfNull(ordinaryGeometry);
        ArgumentNullException.ThrowIfNull(sources);
        ArgumentNullException.ThrowIfNull(budget);
        ordinaryGeometry.VertexLayout.Validate();
        cornerCertificate?.Validate(mesh, ordinaryGeometry, budget);
        if (ordinaryGeometry.DerivedCornerVertices && cornerCertificate is null)
        {
            throw new InvalidDataException("Derived Ptex source geometry requires a producer corner certificate.");
        }
        Candidate? previous = Current;
        if (previous is not null &&
            previous.Matches(mesh, ordinaryGeometry, sources, maximumDimension, retryRevision,
                cornerCertificate?.PublicationIdentity))
        {
            return new PreparedUpdate(this, previous, previous);
        }
        Candidate replacement = Candidate.Create(
            mesh, ordinaryGeometry, sources, maximumDimension, budget, retryRevision, cornerCertificate);
        return new PreparedUpdate(this, previous, replacement);
    }

    internal sealed class PreparedUpdate(
        SilkPtexSlotCandidateCache owner,
        Candidate? previous,
        Candidate replacement) : IDisposable
    {
        private bool _finished;
        internal Candidate Value => !_finished
            ? replacement
            : throw new InvalidOperationException("The Ptex slot update is no longer pending.");

        internal void Commit()
        {
            ObjectDisposedException.ThrowIf(owner._disposed, owner);
            if (_finished || !ReferenceEquals(owner.Current, previous))
            {
                throw new InvalidOperationException("The Ptex slot publication changed during preparation.");
            }
            owner.Current = replacement;
            _finished = true;
            if (!ReferenceEquals(previous, replacement))
            {
                previous?.Dispose();
            }
        }

        public void Dispose()
        {
            if (!_finished && !ReferenceEquals(previous, replacement))
            {
                replacement.Dispose();
            }
            _finished = true;
        }
    }

    internal sealed class Candidate : IDisposable
    {
        private readonly SilkMeshData _mesh;
        private readonly SilkMeshGeometry _ordinaryGeometry;
        private readonly (SilkMaterialParameter Parameter, ulong Identity)[] _sources;
        private readonly SilkPackedPtexAtlas[] _atlases;
        private readonly uint _maximumDimension;
        private readonly ulong _retryRevision;
        private readonly object? _cornerPublicationIdentity;
        private readonly SilkPtexSourceSnapshot _sourceSnapshot;
        private readonly SilkPtexDecodedSnapshot[] _decodedSnapshots;
        private IDisposable? _reservation;
        private bool _disposed;

        private Candidate(
            SilkMeshData mesh,
            SilkMeshGeometry ordinaryGeometry,
            SilkMeshGeometry geometry,
            (SilkMaterialParameter Parameter, ulong Identity)[] sources,
            SilkPackedPtexAtlas[] atlases,
            SilkPtexSlotLookup lookup,
            uint maximumDimension,
            ulong retryRevision,
            IDisposable reservation,
            object? cornerPublicationIdentity,
            SilkPtexSourceSnapshot sourceSnapshot,
            SilkPtexDecodedSnapshot[] decodedSnapshots)
        {
            _mesh = mesh;
            _ordinaryGeometry = ordinaryGeometry;
            Geometry = geometry;
            _sources = sources;
            _atlases = atlases;
            Lookup = lookup;
            _maximumDimension = maximumDimension;
            _retryRevision = retryRevision;
            _reservation = reservation;
            _cornerPublicationIdentity = cornerPublicationIdentity;
            _sourceSnapshot = sourceSnapshot;
            _decodedSnapshots = decodedSnapshots;
        }

        internal SilkMeshGeometry Geometry { get; }
        internal SilkPtexSlotLookup Lookup { get; }
        internal object PublicationIdentity { get; } = new();
        internal int SlotCount => _sources.Length;

        internal SilkMaterialParameter GetParameter(int slot)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return _sources[slot].Parameter;
        }

        internal SilkPackedPtexAtlas GetAtlas(int slot)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return _atlases[slot];
        }

        internal static Candidate Create(
            SilkMeshData mesh,
            SilkMeshGeometry ordinaryGeometry,
            IReadOnlyList<SilkPtexSlotSource> sources,
            uint maximumDimension,
            SilkCpuTextureBudget budget,
            ulong retryRevision,
            SilkPtexCornerCertificate? cornerCertificate = null)
        {
            if (sources.Count is < 1 or > 16)
            {
                throw new InvalidDataException("A Ptex candidate requires between one and sixteen slots.");
            }
            foreach (SilkPtexSlotSource source in sources)
            {
                if (source.Parameter is SilkMaterialParameter.Normal or SilkMaterialParameter.DetailNormal or
                    SilkMaterialParameter.Displacement or SilkMaterialParameter.Opacity or
                    SilkMaterialParameter.VolumeDensity)
                {
                    throw new NotSupportedException(
                        "The Ptex candidate has no qualified normal, displacement or shadow contract " +
                        $"for {source.Parameter}.");
                }
            }
            cornerCertificate?.Validate(mesh, ordinaryGeometry, budget);
            if ((ordinaryGeometry.DerivedCornerVertices && cornerCertificate is null) ||
                (!ordinaryGeometry.DerivedCornerVertices &&
                    !ordinaryGeometry.Indices.AsSpan().SequenceEqual(mesh.Indices.Span)))
            {
                // Only a producer certificate can establish derived-corner
                // correspondence; equal counts and indices cannot.
                throw new InvalidDataException(
                    "The Ptex candidate requires geometry indexed by the retained mesh before corner expansion.");
            }
            ArgumentOutOfRangeException.ThrowIfZero(maximumDimension);
            int stride = checked((int)(ordinaryGeometry.VertexLayout.Stride / sizeof(float)));
            int pointCount = mesh.Points.Length / 3;
            bool hasPointIdentity = mesh.SubprimIdentity.HasFlag(SilkSubprimIdentity.Point) &&
                mesh.PointOrigins.Length == pointCount;
            // Covers the referenced bitmap, growing list and returned point-index array.
            using IDisposable? pointScratch = hasPointIdentity
                ? budget.Reserve(checked(
                    (ulong)pointCount * (sizeof(byte) + (3ul * sizeof(int))) + (4ul * sizeof(int))))
                : null;
            int[] unreferencedPoints = hasPointIdentity
                ? SilkSubprimPickGeometry.FindUnreferencedAuthoredPoints(mesh)
                : [];
            if (ordinaryGeometry.Vertices.Length % stride != 0 ||
                ordinaryGeometry.VertexCount != (ordinaryGeometry.DerivedCornerVertices
                    ? checked((uint)(ordinaryGeometry.Indices.Length + unreferencedPoints.Length))
                    : checked((uint)pointCount)))
            {
                throw new InvalidDataException("The Ptex source geometry does not match its retained pick vertices.");
            }
            if (ordinaryGeometry.DerivedCornerVertices)
            {
                // The producer appends pick vertices after its indexed corner prefix.
                for (int point = 0; point < unreferencedPoints.Length; point++)
                {
                    unreferencedPoints[point] = checked(ordinaryGeometry.Indices.Length + point);
                }
            }
            ulong geometryBytes = checked(
                ((ulong)ordinaryGeometry.Indices.Length + (ulong)unreferencedPoints.Length) *
                    (ulong)stride * sizeof(float) +
                (ulong)ordinaryGeometry.Indices.Length * sizeof(uint) +
                ((ulong)ordinaryGeometry.VertexLayout.Attributes.Count *
                    (ulong)Unsafe.SizeOf<SilkVertexAttributeDescriptor>()));
            ulong metadataBytes = checked((ulong)sources.Count *
                (2ul * (ulong)IntPtr.Size + (ulong)Unsafe.SizeOf<SilkPtexSlotLayout>() +
                    (ulong)Unsafe.SizeOf<(SilkMaterialParameter, ulong)>()));
            IDisposable reservation = budget.Reserve(checked(geometryBytes + metadataBytes));
            SilkPackedPtexAtlas[] atlases;
            try
            {
                atlases = new SilkPackedPtexAtlas[sources.Count];
            }
            catch
            {
                reservation.Dispose();
                throw;
            }
            SilkPtexSlotLookup? lookup = null;
            SilkPtexSourceSnapshot? sourceSnapshot = null;
            SilkPtexDecodedSnapshot[]? decodedSnapshots = null;
            try
            {
                decodedSnapshots = new SilkPtexDecodedSnapshot[sources.Count];
                sourceSnapshot = SilkPtexSourceSnapshot.Capture(mesh, ordinaryGeometry, budget);
                using IDisposable selectionReservation =
                    budget.Reserve(checked((ulong)mesh.PtexFaceIndices.Length * sizeof(uint)));
                var sampled = new uint[mesh.PtexFaceIndices.Length];
                for (int face = 0; face < sampled.Length; face++)
                {
                    sampled[face] = checked((uint)mesh.PtexFaceIndices[face]);
                }
                var layouts = new SilkPtexSlotLayout[sources.Count];
                var identities = new (SilkMaterialParameter Parameter, ulong Identity)[sources.Count];
                for (int slot = 0; slot < sources.Count; slot++)
                {
                    SilkPtexSlotSource source = sources[slot];
                    decodedSnapshots[slot] = SilkPtexDecodedSnapshot.Capture(source.Decoded, budget);
                    atlases[slot] = SilkPtexAtlasPacker.Pack(
                        decodedSnapshots[slot].Owned, sampled, maximumDimension, budget);
                    identities[slot] = (source.Parameter, source.Identity);
                    layouts[slot] = new(source.Parameter, source.Identity, atlases[slot].Layout);
                }
                lookup = SilkPtexSlotLookup.Prepare(mesh, layouts, budget);
                (float[] vertices, uint[] indices) = SilkMeshGeometryBuilder.ExpandTriangleCorners(
                    ordinaryGeometry.Vertices, ordinaryGeometry.Indices, stride, unreferencedPoints);
                SilkMeshGeometry geometry = ordinaryGeometry with
                {
                    Vertices = vertices,
                    Indices = indices,
                    VertexLayout = ordinaryGeometry.VertexLayout with
                    {
                        Attributes = Array.AsReadOnly(ordinaryGeometry.VertexLayout.Attributes.ToArray()),
                    },
                    DerivedCornerVertices = true,
                };
                SilkPtexCandidateBinding.ValidateDraw(geometry, mesh.Indices.Length, 0, 0, 0, false);
                return new Candidate(mesh, ordinaryGeometry, geometry, identities, atlases,
                    lookup, maximumDimension, retryRevision, reservation,
                    cornerCertificate?.PublicationIdentity, sourceSnapshot, decodedSnapshots);
            }
            catch
            {
                lookup?.Dispose();
                sourceSnapshot?.Dispose();
                if (decodedSnapshots is not null)
                {
                    foreach (SilkPtexDecodedSnapshot? snapshot in decodedSnapshots)
                    {
                        snapshot?.Dispose();
                    }
                }
                foreach (SilkPackedPtexAtlas? atlas in atlases)
                {
                    atlas?.Dispose();
                }
                reservation.Dispose();
                throw;
            }
        }

        internal bool Matches(
            SilkMeshData mesh,
            SilkMeshGeometry geometry,
            IReadOnlyList<SilkPtexSlotSource> sources,
            uint maximumDimension,
            ulong retryRevision,
            object? cornerPublicationIdentity = null)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (!ReferenceEquals(_cornerPublicationIdentity, cornerPublicationIdentity) ||
                !_sourceSnapshot.Matches(mesh, geometry) ||
                _maximumDimension != maximumDimension || _retryRevision != retryRevision ||
                sources.Count != _sources.Length ||
                _mesh.Path != mesh.Path || _mesh.MaterialPath != mesh.MaterialPath ||
                _mesh.TopologyKind != mesh.TopologyKind ||
                _mesh.DeformationIdentity != mesh.DeformationIdentity ||
                !_mesh.Indices.Span.SequenceEqual(mesh.Indices.Span) ||
                !_mesh.NormalIdentityArray.AsSpan().SequenceEqual(mesh.NormalIdentityArray) ||
                !_mesh.NormalCornerArray.AsSpan().SequenceEqual(mesh.NormalCornerArray) ||
                !_mesh.NormalExtraPointArray.AsSpan().SequenceEqual(mesh.NormalExtraPointArray) ||
                _mesh.PtexMappingStatus != mesh.PtexMappingStatus ||
                !_mesh.PtexFaceIndices.AsSpan().SequenceEqual(mesh.PtexFaceIndices) ||
                !_mesh.PtexCoordinates.AsSpan().SequenceEqual(mesh.PtexCoordinates) ||
                !(_ordinaryGeometry with { Vertices = geometry.Vertices, Indices = geometry.Indices })
                    .Equals(geometry) ||
                !_ordinaryGeometry.Vertices.AsSpan().SequenceEqual(geometry.Vertices) ||
                !_ordinaryGeometry.Indices.AsSpan().SequenceEqual(geometry.Indices))
            {
                return false;
            }
            for (int slot = 0; slot < sources.Count; slot++)
            {
                if (_sources[slot] != (sources[slot].Parameter, sources[slot].Identity) ||
                    !_decodedSnapshots[slot].Matches(sources[slot].Decoded))
                {
                    return false;
                }
            }
            return true;
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }
            Lookup.Dispose();
            _sourceSnapshot.Dispose();
            foreach (SilkPtexDecodedSnapshot snapshot in _decodedSnapshots)
            {
                snapshot.Dispose();
            }
            foreach (SilkPackedPtexAtlas atlas in _atlases)
            {
                atlas.Dispose();
            }
            Interlocked.Exchange(ref _reservation, null)?.Dispose();
            _disposed = true;
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        Current?.Dispose();
        Current = null;
        _disposed = true;
    }
}
