// Copyright (c) marcschier. Licensed under the MIT License.

namespace OpenUsd.Rendering.Silk;

// Only the producer that writes corner i from retained mesh.Indices[i] may
// issue this certificate. Counts and identity indices validate its shape, not
// its provenance. Issuance and consumption require exclusive source access.
// The snapshot includes the pick-only tail but grants draw access only to the
// indexed prefix. No snapshot memory is exposed or used by GPU submissions.
internal sealed class SilkPtexCornerCertificate : IDisposable
{
    private SilkMeshData? _mesh;
    private SilkMeshGeometry? _geometry;
    private SilkPtexSourceSnapshot? _snapshot;
    private readonly SilkCpuTextureBudget _budget;
    internal object PublicationIdentity { get; } = new();

    private SilkPtexCornerCertificate(
        SilkMeshData mesh, SilkMeshGeometry geometry, SilkPtexSourceSnapshot snapshot,
        SilkCpuTextureBudget budget)
    {
        _mesh = mesh;
        _geometry = geometry;
        _snapshot = snapshot;
        _budget = budget;
    }

    internal static ulong RequiredBytes(SilkMeshData mesh, SilkMeshGeometry geometry) =>
        SilkPtexSourceSnapshot.RequiredBytes(mesh, geometry);

    internal static (SilkMeshGeometry Geometry, SilkPtexCornerCertificate? Certificate) BuildCandidateGeometry(
        SilkMeshData mesh,
        SilkCpuTextureBudget budget,
        string uvPrimvar = "",
        bool requireTangents = false,
        ReadOnlySpan<float> displacementAmounts = default,
        bool requireTextureCoordinates = false,
        string secondaryUvPrimvar = "",
        bool tangentsFromSecondary = false)
    {
        ArgumentNullException.ThrowIfNull(mesh);
        ArgumentNullException.ThrowIfNull(budget);
        SilkMeshGeometry geometry = SilkMeshGeometryBuilder.Build(
            mesh,
            uvPrimvar,
            requireTangents,
            displacementAmounts,
            requireTextureCoordinates,
            secondaryUvPrimvar,
            tangentsFromSecondary);
        SilkPtexCornerCertificate? certificate = geometry.DerivedCornerVertices
            ? IssueForRetainedCornerPrefix(mesh, geometry, budget)
            : null;
        return (geometry, certificate);
    }

    private static SilkPtexCornerCertificate IssueForRetainedCornerPrefix(
        SilkMeshData mesh, SilkMeshGeometry geometry, SilkCpuTextureBudget budget)
    {
        ArgumentNullException.ThrowIfNull(mesh);
        ArgumentNullException.ThrowIfNull(geometry);
        ArgumentNullException.ThrowIfNull(budget);
        if (!geometry.DerivedCornerVertices || mesh.TopologyKind != SilkTopologyKind.TriangleList ||
            mesh.Indices.Length % 3 != 0 ||
            geometry.Indices.Length != mesh.Indices.Length ||
            geometry.VertexLayout.Stride == 0 || geometry.VertexLayout.Stride % sizeof(float) != 0 ||
            geometry.Vertices.Length % (geometry.VertexLayout.Stride / sizeof(float)) != 0 ||
            geometry.VertexCount < geometry.IndexCount)
        {
            throw new InvalidDataException("A certified Ptex source requires a retained triangle-corner prefix.");
        }
        for (int corner = 0; corner < geometry.Indices.Length; corner++)
        {
            if (geometry.Indices[corner] != (uint)corner)
            {
                throw new InvalidDataException("A certified Ptex corner prefix requires identity indices.");
            }
        }
        SilkPtexSourceSnapshot snapshot = SilkPtexSourceSnapshot.Capture(mesh, geometry, budget);
        try
        {
            return new SilkPtexCornerCertificate(mesh, geometry, snapshot, budget);
        }
        catch
        {
            snapshot.Dispose();
            throw;
        }
    }

    internal void Validate(
        SilkMeshData mesh, SilkMeshGeometry geometry, SilkCpuTextureBudget budget)
    {
        ObjectDisposedException.ThrowIf(_snapshot is null, this);
        if (!ReferenceEquals(_mesh, mesh) || !ReferenceEquals(_geometry, geometry) ||
            !ReferenceEquals(_budget, budget))
        {
            throw new InvalidDataException("The Ptex corner certificate belongs to a different source publication.");
        }
        if (!_snapshot!.Matches(mesh, geometry))
        {
            throw new InvalidDataException("The certified Ptex source buffers changed after issuance.");
        }
    }

    public void Dispose()
    {
        Interlocked.Exchange(ref _snapshot, null)?.Dispose();
        _mesh = null;
        _geometry = null;
    }
}
