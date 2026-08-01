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

    /// <summary>Gets a material upsert command view.</summary>
    public SilkMaterialUpsertCommand AsMaterialUpsert()
    {
        EnsureType(SilkCommandType.MaterialUpsert);
        return new SilkMaterialUpsertCommand(_bytes);
    }

    /// <summary>Gets a material removal command view.</summary>
    public SilkMaterialRemoveCommand AsMaterialRemove()
    {
        EnsureType(SilkCommandType.MaterialRemove);
        return new SilkMaterialRemoveCommand(_bytes);
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
    private const int ExtendedSize = 536;
    private const int ClipPlaneOffset = MinimumSize + 8;
    private readonly ReadOnlySpan<byte> _bytes;

    internal SilkFrameCommand(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length != MinimumSize && bytes.Length != ExtendedSize)
        {
            throw new InvalidDataException("The frame command must be exactly 272 or 536 bytes.");
        }
        if (bytes.Length == ExtendedSize &&
            BinaryPrimitives.ReadUInt32LittleEndian(bytes[MinimumSize..(MinimumSize + 4)]) > 8)
        {
            throw new InvalidDataException("The frame command clip plane count is invalid.");
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

    /// <summary>Gets the number of eye-space clip planes.</summary>
    internal uint ClipPlaneCount =>
        _bytes.Length == ExtendedSize
            ? BinaryPrimitives.ReadUInt32LittleEndian(_bytes[MinimumSize..(MinimumSize + 4)])
            : 0;

    /// <summary>Gets an element from the eye-space clip plane table.</summary>
    internal double GetClipPlaneElement(int plane, int component)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(plane);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(plane, 8);
        ArgumentOutOfRangeException.ThrowIfNegative(component);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(component, 4);
        if (_bytes.Length != ExtendedSize)
        {
            return 0;
        }
        return BinaryPrimitives.ReadDoubleLittleEndian(
            _bytes.Slice(ClipPlaneOffset + (((plane * 4) + component) * 8), 8));
    }

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
    private const int FixedSize = 224;
    private const int AttributeFixedSize = 20;
    private readonly ReadOnlySpan<byte> _bytes;
    private readonly string _path;
    private readonly string _materialPath;
    private readonly int _pathLength;
    private readonly int _pointCount;
    private readonly int _indexCount;
    private readonly int _triangleCount;
    private readonly int _materialPathLength;
    private readonly int _attributeCount;
    private readonly int _attributeOffset;

    internal SilkMeshUpsertCommand(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < FixedSize)
        {
            throw new InvalidDataException("The mesh command is truncated.");
        }

        _pathLength = ReadCount(bytes, 48, "path byte");
        _pointCount = ReadCount(bytes, 52, "point");
        _indexCount = ReadCount(bytes, 56, "index");
        _triangleCount = ReadCount(bytes, 60, "triangle");
        _materialPathLength = ReadCount(bytes, 216, "material path byte");
        _attributeCount = ReadCount(bytes, 220, "attribute");
        SilkTopologyKind topologyKind =
            (SilkTopologyKind)BinaryPrimitives.ReadUInt32LittleEndian(bytes[28..32]);
        int indicesPerPrimitive = topologyKind switch
        {
            SilkTopologyKind.TriangleList => 3,
            SilkTopologyKind.LineList => 2,
            _ => throw new InvalidDataException("The mesh topology kind is unsupported.")
        };
        if ((long)_triangleCount * indicesPerPrimitive != _indexCount)
        {
            throw new InvalidDataException(
                "The mesh index count must match the primitive count and topology kind.");
        }

        long fixedAndArrays = FixedSize +
            _pathLength +
            ((long)_pointCount * 12) +
            ((long)_indexCount * sizeof(uint)) +
            ((long)_triangleCount * sizeof(uint)) +
            _materialPathLength;
        if (fixedAndArrays > int.MaxValue || bytes.Length < fixedAndArrays)
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
        if (BinaryPrimitives.ReadUInt64LittleEndian(bytes[32..40]) == 0)
        {
            throw new InvalidDataException("The mesh topology revision must be non-zero.");
        }
        if (BinaryPrimitives.ReadUInt32LittleEndian(bytes[40..44]) > 1)
        {
            throw new InvalidDataException("The mesh double-sided flag is unsupported.");
        }
        if (BinaryPrimitives.ReadUInt32LittleEndian(bytes[44..48]) >
            (uint)SilkMeshCullStyle.FrontUnlessDoubleSided)
        {
            throw new InvalidDataException("The mesh cull style is unsupported.");
        }

        try
        {
            _path = SilkWireFormat.DecodePath(bytes.Slice(FixedSize, _pathLength));
        }
        catch (ArgumentOutOfRangeException exception)
        {
            throw new InvalidDataException(
                "The mesh command size does not match its declared counts.",
                exception);
        }
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
        int materialPathOffset = checked(subprimOffset + (_triangleCount * sizeof(uint)));
        _materialPath = _materialPathLength == 0
            ? string.Empty
            : SilkWireFormat.DecodePath(bytes.Slice(materialPathOffset, _materialPathLength));
        _attributeOffset = checked(materialPathOffset + _materialPathLength);

        // Walk the table once so every accessor can trust its bounds, and so a
        // truncated or over-long command fails here rather than mid-render.
        long walked = _attributeOffset;
        for (int attribute = 0; attribute < _attributeCount; attribute++)
        {
            if (walked + AttributeFixedSize > bytes.Length)
            {
                throw new InvalidDataException("A mesh attribute header is truncated.");
            }
            int start = (int)walked;
            int componentCount = ReadCount(bytes, start + 4, "attribute component");
            int nameLength = ReadCount(bytes, start + 12, "attribute name byte");
            int elementCount = ReadCount(bytes, start + 16, "attribute element");
            if (componentCount is < 1 or > 4)
            {
                throw new InvalidDataException(
                    "A mesh attribute must have one to four components.");
            }
            uint interpolation = BinaryPrimitives.ReadUInt32LittleEndian(
                bytes.Slice(start + 8, sizeof(uint)));
            int expectedElements = interpolation == (uint)SilkAttributeInterpolation.Constant
                ? 1
                : _pointCount;
            if (interpolation > (uint)SilkAttributeInterpolation.Vertex)
            {
                throw new InvalidDataException(
                    "A mesh attribute interpolation is unsupported.");
            }
            if (elementCount != expectedElements)
            {
                throw new InvalidDataException(
                    "A mesh attribute element count does not match its interpolation.");
            }
            walked = checked(walked + AttributeFixedSize + nameLength +
                ((long)elementCount * componentCount * sizeof(float)));
        }
        if (walked != bytes.Length)
        {
            throw new InvalidDataException(
                "The mesh command size does not match its declared counts.");
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

    /// <summary>Gets whether Hydra resolved the mesh as double-sided.</summary>
    public bool DoubleSided => BinaryPrimitives.ReadUInt32LittleEndian(_bytes[40..44]) != 0;

    /// <summary>Gets Hydra's resolved cull style for this mesh.</summary>
    public SilkMeshCullStyle CullStyle =>
        (SilkMeshCullStyle)BinaryPrimitives.ReadUInt32LittleEndian(_bytes[44..48]);

    /// <summary>Gets the USD prim path.</summary>
    public string Path => _path;

    /// <summary>Gets the number of mesh points.</summary>
    public int PointCount => _pointCount;

    /// <summary>Gets the number of topology indices.</summary>
    public int IndexCount => _indexCount;

    /// <summary>Gets the number of emitted primitives and subprim mappings.</summary>
    public int TriangleCount => _triangleCount;

    /// <summary>Gets a display-color component.</summary>
    public float GetDisplayColor(int index)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, 4);
        return BinaryPrimitives.ReadSingleLittleEndian(_bytes.Slice(64 + (index * 4), 4));
    }

    /// <summary>Gets an element from the row-major local-to-world transform.</summary>
    public double GetTransformElement(int index)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, 16);
        return BinaryPrimitives.ReadDoubleLittleEndian(_bytes.Slice(80 + (index * 8), 8));
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

    /// <summary>Gets one topology index.</summary>
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

    /// <summary>Gets the bound material path, empty when the mesh has none.</summary>
    public string MaterialPath => _materialPath;

    /// <summary>Gets the FNV-1a material path hash used only as an identity index.</summary>
    public ulong MaterialBindingHash =>
        BinaryPrimitives.ReadUInt64LittleEndian(_bytes[208..216]);

    /// <summary>Gets the number of vertex attributes carried with the mesh.</summary>
    public int AttributeCount => _attributeCount;

    /// <summary>Gets one vertex attribute.</summary>
    public SilkMeshAttributeEntry GetAttribute(int attributeIndex)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(attributeIndex);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(
            attributeIndex,
            _attributeCount);
        int offset = _attributeOffset;
        for (int index = 0; index < attributeIndex; index++)
        {
            offset = checked(offset + AttributeSize(offset));
        }
        return new SilkMeshAttributeEntry(_bytes.Slice(offset, AttributeSize(offset)));
    }

    private int AttributeSize(int offset)
    {
        int componentCount = ReadCount(_bytes, offset + 4, "attribute component");
        int nameLength = ReadCount(_bytes, offset + 12, "attribute name byte");
        int elementCount = ReadCount(_bytes, offset + 16, "attribute element");
        return checked(AttributeFixedSize + nameLength +
            (elementCount * componentCount * sizeof(float)));
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
/// Interpolation of a mesh vertex attribute, already resolved onto the emitted
/// triangle-list vertices.
/// </summary>
public enum SilkAttributeInterpolation
{
    /// <summary>One element for the whole mesh.</summary>
    Constant = 0,

    /// <summary>One element per emitted vertex.</summary>
    Vertex = 1
}

/// <summary>Hydra cull style resolved for one mesh.</summary>
public enum SilkMeshCullStyle
{
    /// <summary>No authored preference.</summary>
    DontCare = 0,

    /// <summary>Do not cull faces.</summary>
    Nothing = 1,

    /// <summary>Cull back-facing triangles.</summary>
    Back = 2,

    /// <summary>Cull front-facing triangles.</summary>
    Front = 3,

    /// <summary>Cull back-facing triangles only when the mesh is single-sided.</summary>
    BackUnlessDoubleSided = 4,

    /// <summary>Cull front-facing triangles only when the mesh is single-sided.</summary>
    FrontUnlessDoubleSided = 5
}

/// <summary>
/// Semantic of a mesh vertex attribute. <see cref="Custom"/> is identified by
/// its authored primvar name alone.
/// </summary>
public enum SilkAttributeSemantic
{
    /// <summary>An authored primvar with no renderer-bound meaning.</summary>
    Custom = 0,

    /// <summary>Authored surface normals.</summary>
    Normal = 1,

    /// <summary>Texture coordinates.</summary>
    TexCoord = 2,

    /// <summary>Colour.</summary>
    Color = 3,

    /// <summary>Surface tangents.</summary>
    Tangent = 4
}

/// <summary>
/// One vertex attribute carried with a mesh upsert. The data is always float
/// and always already resolved onto the emitted vertices, so a consumer never
/// re-indexes it against the topology.
/// </summary>
public readonly ref struct SilkMeshAttributeEntry
{
    private const int FixedSize = 20;
    private readonly ReadOnlySpan<byte> _bytes;
    private readonly string _name;
    private readonly int _nameLength;

    internal SilkMeshAttributeEntry(ReadOnlySpan<byte> bytes)
    {
        _bytes = bytes;
        _nameLength = (int)BinaryPrimitives.ReadUInt32LittleEndian(bytes[12..16]);
        _name = _nameLength == 0
            ? string.Empty
            : SilkWireFormat.DecodeText(bytes.Slice(FixedSize, _nameLength));
    }

    /// <summary>Gets the renderer-bound semantic.</summary>
    public SilkAttributeSemantic Semantic =>
        (SilkAttributeSemantic)BinaryPrimitives.ReadUInt32LittleEndian(_bytes[0..4]);

    /// <summary>Gets the component count, one to four.</summary>
    public int ComponentCount =>
        (int)BinaryPrimitives.ReadUInt32LittleEndian(_bytes[4..8]);

    /// <summary>Gets the interpolation.</summary>
    public SilkAttributeInterpolation Interpolation =>
        (SilkAttributeInterpolation)BinaryPrimitives.ReadUInt32LittleEndian(_bytes[8..12]);

    /// <summary>Gets the authored primvar name, empty for a bound semantic.</summary>
    public string Name => _name;

    /// <summary>Gets the number of elements.</summary>
    public int ElementCount =>
        (int)BinaryPrimitives.ReadUInt32LittleEndian(_bytes[16..20]);

    /// <summary>Gets one component of one element.</summary>
    public float GetComponent(int elementIndex, int component)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(elementIndex);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(elementIndex, ElementCount);
        ArgumentOutOfRangeException.ThrowIfNegative(component);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(component, ComponentCount);
        int offset = FixedSize + _nameLength +
            (((elementIndex * ComponentCount) + component) * sizeof(float));
        return BinaryPrimitives.ReadSingleLittleEndian(_bytes.Slice(offset, sizeof(float)));
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

/// <summary>
/// Surface model a material describes. <see cref="Unsupported"/> is published
/// rather than omitted so an unsupported shading graph is diagnosable instead
/// of being silently approximated.
/// </summary>
public enum SilkSurfaceKind : uint
{
    /// <summary>The bound network is not a UsdPreviewSurface.</summary>
    Unsupported = 0,

    /// <summary>A UsdPreviewSurface network.</summary>
    PreviewSurface = 1
}

/// <summary>
/// A UsdPreviewSurface input carried by a material command. A parameter absent
/// from both tables stays at the consumer's UsdPreviewSurface default.
/// </summary>
public enum SilkMaterialParameter : uint
{
    /// <summary>No parameter; never written to the wire.</summary>
    None = 0,

    /// <summary>diffuseColor, three components.</summary>
    DiffuseColor = 1,

    /// <summary>emissiveColor, three components.</summary>
    EmissiveColor = 2,

    /// <summary>specularColor, three components.</summary>
    SpecularColor = 3,

    /// <summary>metallic, one component.</summary>
    Metallic = 4,

    /// <summary>roughness, one component.</summary>
    Roughness = 5,

    /// <summary>clearcoat, one component.</summary>
    Clearcoat = 6,

    /// <summary>clearcoatRoughness, one component.</summary>
    ClearcoatRoughness = 7,

    /// <summary>opacity, one component.</summary>
    Opacity = 8,

    /// <summary>opacityThreshold, one component.</summary>
    OpacityThreshold = 9,

    /// <summary>ior, one component.</summary>
    Ior = 10,

    /// <summary>normal, three components.</summary>
    Normal = 11,

    /// <summary>displacement, one component.</summary>
    Displacement = 12,

    /// <summary>occlusion, one component.</summary>
    Occlusion = 13,

    /// <summary>useSpecularWorkflow, one component.</summary>
    UseSpecularWorkflow = 14
}

/// <summary>UsdUVTexture wrap mode.</summary>
public enum SilkTextureWrap : uint
{
    /// <summary>Sample black outside the unit range.</summary>
    Black = 0,

    /// <summary>Clamp to edge.</summary>
    Clamp = 1,

    /// <summary>Repeat.</summary>
    Repeat = 2,

    /// <summary>Mirror.</summary>
    Mirror = 3
}

/// <summary>UsdUVTexture sourceColorSpace.</summary>
public enum SilkColorSpace : uint
{
    /// <summary>Decided by the image's own metadata.</summary>
    Auto = 0,

    /// <summary>Linear, no transfer function applied.</summary>
    Raw = 1,

    /// <summary>sRGB transfer function applied on read.</summary>
    Srgb = 2
}

/// <summary>
/// One constant UsdPreviewSurface input carried with a material upsert.
/// </summary>
public readonly ref struct SilkMaterialScalarEntry
{
    internal const int FixedSize = 8;
    private readonly ReadOnlySpan<byte> _bytes;

    internal SilkMaterialScalarEntry(ReadOnlySpan<byte> bytes) => _bytes = bytes;

    /// <summary>Gets the parameter this value drives.</summary>
    public SilkMaterialParameter Parameter =>
        (SilkMaterialParameter)BinaryPrimitives.ReadUInt32LittleEndian(_bytes[0..4]);

    /// <summary>Gets the component count, one to four.</summary>
    public int ComponentCount =>
        (int)BinaryPrimitives.ReadUInt32LittleEndian(_bytes[4..8]);

    /// <summary>Gets one component of the value.</summary>
    public float GetComponent(int component)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(component);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(component, ComponentCount);
        int offset = FixedSize + (component * sizeof(float));
        return BinaryPrimitives.ReadSingleLittleEndian(_bytes.Slice(offset, sizeof(float)));
    }
}

/// <summary>
/// One UsdPreviewSurface input driven by a connected UsdUVTexture.
/// </summary>
public readonly ref struct SilkMaterialTextureEntry
{
    internal const int FixedSize = 76;
    private readonly ReadOnlySpan<byte> _bytes;
    private readonly string _asset;
    private readonly string _uvPrimvar;

    internal SilkMaterialTextureEntry(ReadOnlySpan<byte> bytes)
    {
        _bytes = bytes;
        int assetLength = (int)BinaryPrimitives.ReadUInt32LittleEndian(bytes[16..20]);
        int uvLength = (int)BinaryPrimitives.ReadUInt32LittleEndian(bytes[20..24]);
        _asset = SilkWireFormat.DecodeText(bytes.Slice(FixedSize, assetLength));
        _uvPrimvar = uvLength == 0
            ? string.Empty
            : SilkWireFormat.DecodeText(bytes.Slice(FixedSize + assetLength, uvLength));
    }

    /// <summary>Gets the parameter this texture drives.</summary>
    public SilkMaterialParameter Parameter =>
        (SilkMaterialParameter)BinaryPrimitives.ReadUInt32LittleEndian(_bytes[0..4]);

    /// <summary>Gets the horizontal wrap mode.</summary>
    public SilkTextureWrap WrapS =>
        (SilkTextureWrap)BinaryPrimitives.ReadUInt32LittleEndian(_bytes[4..8]);

    /// <summary>Gets the vertical wrap mode.</summary>
    public SilkTextureWrap WrapT =>
        (SilkTextureWrap)BinaryPrimitives.ReadUInt32LittleEndian(_bytes[8..12]);

    /// <summary>Gets the authored source color space.</summary>
    public SilkColorSpace SourceColorSpace =>
        (SilkColorSpace)BinaryPrimitives.ReadUInt32LittleEndian(_bytes[12..16]);

    /// <summary>Gets how many channels the bound input consumes.</summary>
    public int ComponentCount =>
        (int)BinaryPrimitives.ReadUInt32LittleEndian(_bytes[24..28]);

    /// <summary>Gets one component of the multiply applied after sampling.</summary>
    public float GetScale(int component) => ReadVector(28, component);

    /// <summary>Gets one component of the offset applied after scaling.</summary>
    public float GetBias(int component) => ReadVector(44, component);

    /// <summary>Gets one component of the value used when the asset cannot load.</summary>
    public float GetFallback(int component) => ReadVector(60, component);

    /// <summary>Gets the resolved texture asset path.</summary>
    public string Asset => _asset;

    /// <summary>
    /// Gets the primvar supplying texture coordinates, empty when the texture
    /// has no resolvable reader connection.
    /// </summary>
    public string UvPrimvar => _uvPrimvar;

    private float ReadVector(int baseOffset, int component)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(component);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(component, 4);
        int offset = baseOffset + (component * sizeof(float));
        return BinaryPrimitives.ReadSingleLittleEndian(_bytes.Slice(offset, sizeof(float)));
    }
}

/// <summary>
/// A create or update command for one resolved material.
/// </summary>
public readonly ref struct SilkMaterialUpsertCommand
{
    private const int FixedSize = 32;
    private readonly ReadOnlySpan<byte> _bytes;
    private readonly string _path;
    private readonly int _pathLength;

    internal SilkMaterialUpsertCommand(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < FixedSize)
        {
            throw new InvalidDataException("The material upsert command is truncated.");
        }
        uint pathLength = BinaryPrimitives.ReadUInt32LittleEndian(bytes[16..20]);
        if (pathLength is 0 or > int.MaxValue)
        {
            throw new InvalidDataException(
                "The material upsert path must be present and within the managed page limit.");
        }
        _pathLength = (int)pathLength;
        if (bytes.Length < FixedSize + _pathLength)
        {
            throw new InvalidDataException("The material upsert path is truncated.");
        }
        _path = SilkWireFormat.DecodePath(bytes.Slice(FixedSize, _pathLength));
        _bytes = bytes;

        // Walking both tables in the constructor means a truncated or
        // inconsistent table fails once here rather than at an arbitrary later
        // accessor, which is the same guarantee the mesh commands give.
        int offset = FixedSize + _pathLength;
        for (int index = 0; index < ScalarCount; index++)
        {
            offset = AdvanceScalar(bytes, offset);
        }
        for (int index = 0; index < TextureCount; index++)
        {
            offset = AdvanceTexture(bytes, offset);
        }
        if (offset != bytes.Length)
        {
            throw new InvalidDataException(
                "The material upsert size does not match its parameter tables.");
        }
    }

    /// <summary>Gets the FNV-1a path hash used only as an identity index.</summary>
    public ulong StableHash => BinaryPrimitives.ReadUInt64LittleEndian(_bytes[8..16]);

    /// <summary>Gets the surface model this material describes.</summary>
    public SilkSurfaceKind SurfaceKind =>
        (SilkSurfaceKind)BinaryPrimitives.ReadUInt32LittleEndian(_bytes[20..24]);

    /// <summary>Gets the number of constant inputs.</summary>
    public int ScalarCount =>
        (int)BinaryPrimitives.ReadUInt32LittleEndian(_bytes[24..28]);

    /// <summary>Gets the number of texture-driven inputs.</summary>
    public int TextureCount =>
        (int)BinaryPrimitives.ReadUInt32LittleEndian(_bytes[28..32]);

    /// <summary>Gets the authoritative USD material path.</summary>
    public string Path => _path;

    /// <summary>Gets one constant input.</summary>
    public SilkMaterialScalarEntry GetScalar(int index)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, ScalarCount);
        int offset = FixedSize + _pathLength;
        for (int current = 0; current < index; current++)
        {
            offset = AdvanceScalar(_bytes, offset);
        }
        return new SilkMaterialScalarEntry(_bytes[offset..]);
    }

    /// <summary>Gets one texture-driven input.</summary>
    public SilkMaterialTextureEntry GetTexture(int index)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, TextureCount);
        int offset = FixedSize + _pathLength;
        for (int current = 0; current < ScalarCount; current++)
        {
            offset = AdvanceScalar(_bytes, offset);
        }
        for (int current = 0; current < index; current++)
        {
            offset = AdvanceTexture(_bytes, offset);
        }
        return new SilkMaterialTextureEntry(_bytes[offset..]);
    }

    private static int AdvanceScalar(ReadOnlySpan<byte> bytes, int offset)
    {
        if (offset + SilkMaterialScalarEntry.FixedSize > bytes.Length)
        {
            throw new InvalidDataException("A material scalar entry is truncated.");
        }
        uint components =
            BinaryPrimitives.ReadUInt32LittleEndian(bytes.Slice(offset + 4, 4));
        if (components is 0 or > 4)
        {
            throw new InvalidDataException(
                "A material scalar entry must carry one to four components.");
        }
        int size = SilkMaterialScalarEntry.FixedSize + ((int)components * sizeof(float));
        if (offset + size > bytes.Length)
        {
            throw new InvalidDataException("A material scalar entry is truncated.");
        }
        return offset + size;
    }

    private static int AdvanceTexture(ReadOnlySpan<byte> bytes, int offset)
    {
        if (offset + SilkMaterialTextureEntry.FixedSize > bytes.Length)
        {
            throw new InvalidDataException("A material texture entry is truncated.");
        }
        uint assetLength =
            BinaryPrimitives.ReadUInt32LittleEndian(bytes.Slice(offset + 16, 4));
        uint uvLength =
            BinaryPrimitives.ReadUInt32LittleEndian(bytes.Slice(offset + 20, 4));
        uint components =
            BinaryPrimitives.ReadUInt32LittleEndian(bytes.Slice(offset + 24, 4));
        if (assetLength == 0)
        {
            throw new InvalidDataException(
                "A material texture entry requires a resolved asset path.");
        }
        if (components is 0 or > 4)
        {
            throw new InvalidDataException(
                "A material texture entry must consume one to four components.");
        }
        long size = SilkMaterialTextureEntry.FixedSize + (long)assetLength + uvLength;
        if (offset + size > bytes.Length)
        {
            throw new InvalidDataException("A material texture entry is truncated.");
        }
        return offset + (int)size;
    }
}

/// <summary>
/// A removal command for one material.
/// </summary>
public readonly ref struct SilkMaterialRemoveCommand
{
    private const int FixedSize = 20;
    private readonly ReadOnlySpan<byte> _bytes;
    private readonly string _path;

    internal SilkMaterialRemoveCommand(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < FixedSize)
        {
            throw new InvalidDataException("The material removal command is truncated.");
        }
        uint pathLength = BinaryPrimitives.ReadUInt32LittleEndian(bytes[16..20]);
        if (pathLength is 0 or > int.MaxValue)
        {
            throw new InvalidDataException(
                "The material removal path must be present and within the managed page limit.");
        }
        if (bytes.Length != FixedSize + (int)pathLength)
        {
            throw new InvalidDataException(
                "The material removal size does not match its path length.");
        }
        _path = SilkWireFormat.DecodePath(bytes.Slice(FixedSize, (int)pathLength));
        _bytes = bytes;
    }

    /// <summary>Gets the FNV-1a path hash used only as an identity index.</summary>
    public ulong StableHash => BinaryPrimitives.ReadUInt64LittleEndian(_bytes[8..16]);

    /// <summary>Gets the removed USD material path.</summary>
    public string Path => _path;
}

/// <summary>Describes the pointer-free topology emitted by hdSilk.</summary>
public enum SilkTopologyKind : uint
{
    /// <summary>Three indices and one authored face mapping per triangle.</summary>
    TriangleList = 1,

    /// <summary>Two indices and one authored segment mapping per line.</summary>
    LineList = 2
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

    /// <summary>
    /// Decodes a UTF-8 text field that is not a prim path, such as a texture
    /// asset path or a primvar name. These are validated as strict UTF-8 without
    /// a NUL, but must not be forced to look like an absolute USD path.
    /// </summary>
    internal static string DecodeText(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length == 0)
        {
            return string.Empty;
        }
        string text;
        try
        {
            text = StrictUtf8.GetString(bytes);
        }
        catch (DecoderFallbackException exception)
        {
            throw new InvalidDataException(
                "A material text field is not valid UTF-8.",
                exception);
        }
        if (text.Contains('\0'))
        {
            throw new InvalidDataException(
                "A material text field must not contain a NUL.");
        }
        return text;
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
