// Copyright (c) marcschier. Licensed under the MIT License.

using System.Buffers.Binary;
using System.Text;

namespace OpenUsd.Rendering.Silk;

/// <summary>
/// A zero-copy view over one command in an hdSilk page.
/// </summary>
public readonly ref struct SilkCommand
{
    private readonly ReadOnlySpan<byte> _bytes;

    internal SilkCommand(SilkCommandType type, ReadOnlySpan<byte> bytes)
    {
        Type = type;
        _bytes = bytes;
    }

    /// <summary>Gets the command type.</summary>
    public SilkCommandType Type { get; }

    /// <summary>Gets a frame command view.</summary>
    public SilkFrameCommand AsFrame()
    {
        EnsureType(SilkCommandType.Frame);
        return new SilkFrameCommand(_bytes);
    }

    /// <summary>Gets a mesh upsert command view.</summary>
    public SilkMeshUpsertCommand AsMeshUpsert()
    {
        EnsureType(SilkCommandType.MeshUpsert);
        return new SilkMeshUpsertCommand(_bytes);
    }

    /// <summary>Gets a mesh removal command view.</summary>
    public SilkMeshRemoveCommand AsMeshRemove()
    {
        EnsureType(SilkCommandType.MeshRemove);
        return new SilkMeshRemoveCommand(_bytes);
    }

    private void EnsureType(SilkCommandType expected)
    {
        if (Type != expected)
        {
            throw new InvalidOperationException($"Command is {Type}, not {expected}.");
        }
    }
}

/// <summary>
/// Camera and viewport state for a frame.
/// </summary>
public readonly ref struct SilkFrameCommand
{
    private const int MinimumSize = 272;
    private readonly ReadOnlySpan<byte> _bytes;

    internal SilkFrameCommand(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length != MinimumSize)
        {
            throw new InvalidDataException("The frame command must be exactly 272 bytes.");
        }
        _bytes = bytes;
    }

    /// <summary>Gets the viewport width.</summary>
    public int Width => BinaryPrimitives.ReadInt32LittleEndian(_bytes[8..12]);

    /// <summary>Gets the viewport height.</summary>
    public int Height => BinaryPrimitives.ReadInt32LittleEndian(_bytes[12..16]);

    /// <summary>Gets an element from the row-major world-to-view matrix.</summary>
    public double GetViewElement(int index) => ReadMatrixElement(16, index);

    /// <summary>Gets an element from the row-major projection matrix.</summary>
    public double GetProjectionElement(int index) => ReadMatrixElement(144, index);

    private double ReadMatrixElement(int offset, int index)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, 16);
        return BinaryPrimitives.ReadDoubleLittleEndian(_bytes.Slice(offset + (index * 8), 8));
    }
}

/// <summary>
/// A create or update command for one triangulated mesh.
/// </summary>
public readonly ref struct SilkMeshUpsertCommand
{
    private const int FixedSize = 200;
    private readonly ReadOnlySpan<byte> _bytes;
    private readonly string _path;
    private readonly int _pathLength;
    private readonly int _pointCount;
    private readonly int _indexCount;
    private readonly int _triangleCount;

    internal SilkMeshUpsertCommand(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < FixedSize)
        {
            throw new InvalidDataException("The mesh command is truncated.");
        }

        _pathLength = ReadCount(bytes, 40, "path byte");
        _pointCount = ReadCount(bytes, 44, "point");
        _indexCount = ReadCount(bytes, 48, "index");
        _triangleCount = ReadCount(bytes, 52, "triangle");
        if ((long)_triangleCount * 3 != _indexCount)
        {
            throw new InvalidDataException(
                "The mesh index count must equal three times the triangle count.");
        }

        long expected = FixedSize +
            _pathLength +
            ((long)_pointCount * 12) +
            ((long)_indexCount * sizeof(uint)) +
            ((long)_triangleCount * sizeof(uint));
        if (expected > int.MaxValue || bytes.Length != expected)
        {
            throw new InvalidDataException(
                "The mesh command size does not match its declared counts.");
        }
        if (BinaryPrimitives.ReadInt32LittleEndian(bytes[16..20]) < 0)
        {
            throw new InvalidDataException("The mesh prim ID must be non-negative.");
        }
        if (BinaryPrimitives.ReadInt32LittleEndian(bytes[24..28]) < 0)
        {
            throw new InvalidDataException(
                "The mesh instance index must be non-negative.");
        }
        if (BinaryPrimitives.ReadUInt32LittleEndian(bytes[28..32]) !=
            (uint)SilkTopologyKind.TriangleList)
        {
            throw new InvalidDataException("The mesh topology kind is unsupported.");
        }
        if (BinaryPrimitives.ReadUInt64LittleEndian(bytes[32..40]) == 0)
        {
            throw new InvalidDataException("The mesh topology revision must be non-zero.");
        }

        _path = SilkWireFormat.DecodePath(bytes.Slice(FixedSize, _pathLength));
        int subprimOffset = checked(
            FixedSize + _pathLength + (_pointCount * 12) + (_indexCount * sizeof(uint)));
        for (int triangle = 0; triangle < _triangleCount; triangle++)
        {
            uint subprim = BinaryPrimitives.ReadUInt32LittleEndian(
                bytes.Slice(subprimOffset + (triangle * sizeof(uint)), sizeof(uint)));
            if (subprim > int.MaxValue)
            {
                throw new InvalidDataException(
                    "A mesh triangle subprim index exceeds the managed identity range.");
            }
        }
        _bytes = bytes;
    }

    /// <summary>Gets the FNV-1a path hash used only as an identity index.</summary>
    public ulong StableHash => BinaryPrimitives.ReadUInt64LittleEndian(_bytes[8..16]);

    /// <summary>Gets Hydra's explicit Rprim identifier.</summary>
    public int PrimId => BinaryPrimitives.ReadInt32LittleEndian(_bytes[16..20]);

    /// <summary>
    /// Gets the stable, diagnostic-only identifier of the owning instancer, or
    /// zero when the prim is not instanced.
    /// </summary>
    public int InstanceId => BinaryPrimitives.ReadInt32LittleEndian(_bytes[20..24]);

    /// <summary>
    /// Gets the zero-based instance ordinal. A prim with no instancer always
    /// reports zero; a point-instanced prototype reports one record per
    /// instance, so (<see cref="Path"/>, <see cref="InstanceIndex"/>) is the
    /// retained identity.
    /// </summary>
    public int InstanceIndex => BinaryPrimitives.ReadInt32LittleEndian(_bytes[24..28]);

    /// <summary>Gets the emitted topology kind.</summary>
    public SilkTopologyKind TopologyKind =>
        (SilkTopologyKind)BinaryPrimitives.ReadUInt32LittleEndian(_bytes[28..32]);

    /// <summary>Gets the revision that changes only with dirty topology.</summary>
    public ulong TopologyRevision =>
        BinaryPrimitives.ReadUInt64LittleEndian(_bytes[32..40]);

    /// <summary>Gets the USD prim path.</summary>
    public string Path => _path;

    /// <summary>Gets the number of mesh points.</summary>
    public int PointCount => _pointCount;

    /// <summary>Gets the number of triangle indices.</summary>
    public int IndexCount => _indexCount;

    /// <summary>Gets the number of emitted triangles and subprim mappings.</summary>
    public int TriangleCount => _triangleCount;

    /// <summary>Gets a display-color component.</summary>
    public float GetDisplayColor(int index)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, 4);
        return BinaryPrimitives.ReadSingleLittleEndian(_bytes.Slice(56 + (index * 4), 4));
    }

    /// <summary>Gets an element from the row-major local-to-world transform.</summary>
    public double GetTransformElement(int index)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, 16);
        return BinaryPrimitives.ReadDoubleLittleEndian(_bytes.Slice(72 + (index * 8), 8));
    }

    /// <summary>Gets one point component, where component is 0=x, 1=y, 2=z.</summary>
    public float GetPointComponent(int pointIndex, int component)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(pointIndex);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(pointIndex, _pointCount);
        ArgumentOutOfRangeException.ThrowIfNegative(component);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(component, 3);
        int offset = FixedSize + _pathLength + (((pointIndex * 3) + component) * 4);
        return BinaryPrimitives.ReadSingleLittleEndian(_bytes.Slice(offset, 4));
    }

    /// <summary>Gets one triangle index.</summary>
    public uint GetIndex(int index)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, _indexCount);
        int offset = FixedSize + _pathLength + (_pointCount * 12) + (index * 4);
        return BinaryPrimitives.ReadUInt32LittleEndian(_bytes.Slice(offset, 4));
    }

    /// <summary>Gets the authored USD face/subprim for one emitted triangle.</summary>
    public int GetTriangleSubprim(int triangleIndex)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(triangleIndex);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(
            triangleIndex,
            _triangleCount);
        int offset = FixedSize +
            _pathLength +
            (_pointCount * 12) +
            (_indexCount * sizeof(uint)) +
            (triangleIndex * sizeof(uint));
        return checked((int)BinaryPrimitives.ReadUInt32LittleEndian(
            _bytes.Slice(offset, sizeof(uint))));
    }

    private static int ReadCount(ReadOnlySpan<byte> bytes, int offset, string name)
    {
        uint count = BinaryPrimitives.ReadUInt32LittleEndian(
            bytes.Slice(offset, sizeof(uint)));
        if (count > int.MaxValue)
        {
            throw new InvalidDataException(
                $"The mesh {name} count exceeds the managed page limit.");
        }
        return (int)count;
    }
}

/// <summary>
/// A removal command for one mesh.
/// </summary>
public readonly ref struct SilkMeshRemoveCommand
{
    private const int FixedSize = 24;
    private readonly ReadOnlySpan<byte> _bytes;
    private readonly string _path;
    private readonly int _pathLength;

    internal SilkMeshRemoveCommand(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < FixedSize)
        {
            throw new InvalidDataException("The mesh removal command is truncated.");
        }
        if (BinaryPrimitives.ReadInt32LittleEndian(bytes[16..20]) < 0)
        {
            throw new InvalidDataException(
                "The mesh removal instance index must be non-negative.");
        }
        uint pathLength = BinaryPrimitives.ReadUInt32LittleEndian(bytes[20..24]);
        if (pathLength > int.MaxValue)
        {
            throw new InvalidDataException(
                "The mesh removal path exceeds the managed page limit.");
        }
        _pathLength = (int)pathLength;
        if (bytes.Length != FixedSize + _pathLength)
        {
            throw new InvalidDataException(
                "The mesh removal size does not match its path length.");
        }
        _path = SilkWireFormat.DecodePath(bytes.Slice(FixedSize, _pathLength));
        _bytes = bytes;
    }

    /// <summary>Gets the FNV-1a path hash used only as an identity index.</summary>
    public ulong StableHash => BinaryPrimitives.ReadUInt64LittleEndian(_bytes[8..16]);

    /// <summary>
    /// Gets the instance ordinal being retired. A shrinking instancer emits one
    /// removal per dropped instance.
    /// </summary>
    public int InstanceIndex => BinaryPrimitives.ReadInt32LittleEndian(_bytes[16..20]);

    /// <summary>Gets the removed USD prim path.</summary>
    public string Path => _path;
}

/// <summary>Describes the pointer-free topology emitted by hdSilk.</summary>
public enum SilkTopologyKind : uint
{
    /// <summary>Three indices and one authored face mapping per triangle.</summary>
    TriangleList = 1
}

internal static class SilkWireFormat
{
    private static readonly UTF8Encoding StrictUtf8 = new(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true);

    internal static string DecodePath(ReadOnlySpan<byte> bytes)
    {
        string path;
        try
        {
            path = StrictUtf8.GetString(bytes);
        }
        catch (DecoderFallbackException exception)
        {
            throw new InvalidDataException(
                "The mesh path is not valid UTF-8.",
                exception);
        }

        if (path.Length == 0 || path[0] != '/' || path.Contains('\0'))
        {
            throw new InvalidDataException(
                "The mesh path must be a non-empty absolute USD path.");
        }
        return path;
    }

    internal static ulong ComputeStableHash(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        ulong hash = 14695981039346656037;
        foreach (byte value in Encoding.UTF8.GetBytes(path))
        {
            hash ^= value;
            hash *= 1099511628211;
        }
        return hash;
    }
}
