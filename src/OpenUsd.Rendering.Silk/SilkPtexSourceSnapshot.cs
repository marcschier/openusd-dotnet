// Copyright (c) marcschier. Licensed under the MIT License.

using System.Buffers.Binary;
using System.Runtime.InteropServices;

namespace OpenUsd.Rendering.Silk;

// Byte identity only: this does not assert retained-corner provenance.
// Callers serialize capture/comparison with source edits and disposal.
internal sealed class SilkPtexSourceSnapshot : IDisposable
{
    private const ulong LayoutHeaderBytes = 2 * sizeof(uint);
    private const ulong AttributeBytes = 4 * sizeof(uint);
    private byte[]? _bytes;
    private IDisposable? _reservation;

    private SilkPtexSourceSnapshot(byte[] bytes, IDisposable reservation)
    {
        _bytes = bytes;
        _reservation = reservation;
    }

    internal static ulong RequiredBytes(SilkMeshData mesh, SilkMeshGeometry geometry) =>
        checked(((13ul + (ulong)geometry.Vertices.Length + (ulong)geometry.Indices.Length +
            (ulong)mesh.Points.Length + (ulong)mesh.Indices.Length +
            (ulong)mesh.AuthoredNormals.Length +
            (ulong)mesh.PtexFaceIndices.Length + (ulong)mesh.PtexCoordinates.Length +
            (ulong)mesh.PointOrigins.Length +
            (ulong)mesh.NormalIdentityArray.Length + (ulong)mesh.NormalCornerArray.Length +
            (ulong)mesh.NormalExtraPointArray.Length) * sizeof(uint)) +
            sizeof(int) + LayoutHeaderBytes +
            ((ulong)geometry.VertexLayout.Attributes.Count * (sizeof(int) + AttributeBytes)));

    internal static SilkPtexSourceSnapshot Capture(
        SilkMeshData mesh, SilkMeshGeometry geometry, SilkCpuTextureBudget budget)
    {
        ulong bytes = RequiredBytes(mesh, geometry);
        int length = checked((int)bytes);
        IDisposable reservation = budget.Reserve(bytes);
        try
        {
            var snapshot = new SilkPtexSourceSnapshot(new byte[length], reservation);
            _ = snapshot.Visit(mesh, geometry, capture: true);
            return snapshot;
        }
        catch
        {
            reservation.Dispose();
            throw;
        }
    }

    internal bool Matches(SilkMeshData mesh, SilkMeshGeometry geometry)
    {
        ObjectDisposedException.ThrowIf(_bytes is null, this);
        return RequiredBytes(mesh, geometry) == (ulong)_bytes!.Length &&
            Visit(mesh, geometry, capture: false);
    }

    private bool Visit(SilkMeshData mesh, SilkMeshGeometry geometry, bool capture)
    {
        int offset = 0;
        return VisitBytes(MemoryMarshal.AsBytes(geometry.Vertices.AsSpan()), capture, ref offset) &&
            VisitBytes(MemoryMarshal.AsBytes(geometry.Indices.AsSpan()), capture, ref offset) &&
            VisitBytes(MemoryMarshal.AsBytes(mesh.Points.Span), capture, ref offset) &&
            VisitBytes(MemoryMarshal.AsBytes(mesh.Indices.Span), capture, ref offset) &&
            VisitBytes(MemoryMarshal.AsBytes(mesh.AuthoredNormals.Span), capture, ref offset) &&
            VisitBytes(MemoryMarshal.AsBytes(mesh.PtexFaceIndices.AsSpan()), capture, ref offset) &&
            VisitBytes(MemoryMarshal.AsBytes(mesh.PtexCoordinates.AsSpan()), capture, ref offset) &&
            VisitBytes(MemoryMarshal.AsBytes(mesh.PointOrigins.Span), capture, ref offset) &&
            VisitBytes(MemoryMarshal.AsBytes(mesh.NormalIdentityArray.AsSpan()), capture, ref offset) &&
            VisitBytes(MemoryMarshal.AsBytes(mesh.NormalCornerArray.AsSpan()), capture, ref offset) &&
            VisitBytes(MemoryMarshal.AsBytes(mesh.NormalExtraPointArray.AsSpan()), capture, ref offset) &&
            VisitSubprimIdentity(mesh.SubprimIdentity, capture, ref offset) &&
            VisitLayout(geometry.VertexLayout, capture, ref offset) &&
            offset == _bytes!.Length;
    }

    private bool VisitSubprimIdentity(SilkSubprimIdentity identity, bool capture, ref int offset)
    {
        Span<byte> bytes = stackalloc byte[sizeof(uint)];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, (uint)identity);
        return VisitBytes(bytes, capture, ref offset);
    }

    private bool VisitLayout(SilkVertexLayoutDescriptor layout, bool capture, ref int offset)
    {
        Span<byte> header = stackalloc byte[8];
        BinaryPrimitives.WriteUInt32LittleEndian(header, layout.Stride);
        BinaryPrimitives.WriteInt32LittleEndian(header[4..], layout.Attributes.Count);
        if (!VisitBytes(header, capture, ref offset))
        {
            return false;
        }

        Span<byte> bytes = stackalloc byte[16];
        foreach (SilkVertexAttributeDescriptor attribute in layout.Attributes)
        {
            BinaryPrimitives.WriteInt32LittleEndian(bytes, (int)attribute.Semantic);
            BinaryPrimitives.WriteUInt32LittleEndian(bytes[4..], attribute.Location);
            BinaryPrimitives.WriteUInt32LittleEndian(bytes[8..], attribute.Offset);
            BinaryPrimitives.WriteInt32LittleEndian(bytes[12..], (int)attribute.Format);
            if (!VisitBytes(bytes, capture, ref offset))
            {
                return false;
            }
        }
        return true;
    }

    private bool VisitBytes(ReadOnlySpan<byte> source, bool capture, ref int offset)
    {
        if (_bytes!.Length - offset < sizeof(int))
        {
            return false;
        }
        Span<byte> length = _bytes.AsSpan(offset, sizeof(int));
        if (capture)
        {
            BinaryPrimitives.WriteInt32LittleEndian(length, source.Length);
        }
        else if (BinaryPrimitives.ReadInt32LittleEndian(length) != source.Length)
        {
            return false;
        }
        offset += sizeof(int);
        if (source.Length > _bytes.Length - offset)
        {
            return false;
        }
        Span<byte> destination = _bytes.AsSpan(offset, source.Length);
        offset = checked(offset + source.Length);
        if (capture)
        {
            source.CopyTo(destination);
            return true;
        }
        return source.SequenceEqual(destination);
    }

    public void Dispose()
    {
        _bytes = null;
        Interlocked.Exchange(ref _reservation, null)?.Dispose();
    }
}
