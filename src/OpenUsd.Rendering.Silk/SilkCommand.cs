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

    /// <summary>Gets a dome-light environment upsert command view.</summary>
    public SilkEnvironmentUpsertCommand AsEnvironmentUpsert()
    {
        EnsureType(SilkCommandType.EnvironmentUpsert);
        return new SilkEnvironmentUpsertCommand(_bytes);
    }

    /// <summary>Gets a dome-light environment removal command view.</summary>
    public SilkEnvironmentRemoveCommand AsEnvironmentRemove()
    {
        EnsureType(SilkCommandType.EnvironmentRemove);
        return new SilkEnvironmentRemoveCommand(_bytes);
    }

    /// <summary>Gets a light and shadow link table command view.</summary>
    public SilkLightLinkCommand AsLightLink()
    {
        EnsureType(SilkCommandType.LightLink);
        return new SilkLightLinkCommand(_bytes);
    }

    /// <summary>Gets a raster shadow-map descriptor table command view.</summary>
    public SilkShadowCommand AsShadow()
    {
        EnsureType(SilkCommandType.Shadow);
        return new SilkShadowCommand(_bytes);
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
    private const int LightingSize = 1976;
    private const int DomeSize = 2248;
    private const int ClipPlaneOffset = MinimumSize + 8;
    private const int LightCountOffset = ExtendedSize;
    private const int LightTableOffset = ExtendedSize + 16;
    private const int LightEntrySize = 176;
    private const int AmbientOffset = LightTableOffset + (8 * LightEntrySize);
    private const int DomeCountOffset = LightingSize;
    private const int DomeTableOffset = LightingSize + 16;
    private const int DomeEntrySize = 32;
    private readonly ReadOnlySpan<byte> _bytes;

    /// <summary>Gets the fixed number of direct lights a frame table can carry.</summary>
    public const uint MaximumLights = 8;

    /// <summary>Gets the fixed number of dome lights a frame table can carry.</summary>
    /// <remarks>
    /// The same bound as <see cref="MaximumLights"/>, because a dome bit and a
    /// direct light bit are bounded the same way: one constant sizes both, and a
    /// consumer never has to ask which mask it is holding.
    /// </remarks>
    public const uint MaximumDomes = 8;

    internal SilkFrameCommand(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length != MinimumSize &&
            bytes.Length != ExtendedSize &&
            bytes.Length != LightingSize &&
            bytes.Length != DomeSize)
        {
            throw new InvalidDataException(
                "The frame command must be exactly 272, 536, 1976, or 2248 bytes.");
        }
        if (bytes.Length >= ExtendedSize &&
            BinaryPrimitives.ReadUInt32LittleEndian(bytes[MinimumSize..(MinimumSize + 4)]) > 8)
        {
            throw new InvalidDataException("The frame command clip plane count is invalid.");
        }
        if (bytes.Length >= LightingSize &&
            BinaryPrimitives.ReadUInt32LittleEndian(bytes[LightCountOffset..(LightCountOffset + 4)]) > 8)
        {
            throw new InvalidDataException("The frame command light count is invalid.");
        }
        if (bytes.Length == DomeSize)
        {
            uint domeCount = BinaryPrimitives.ReadUInt32LittleEndian(
                bytes[DomeCountOffset..(DomeCountOffset + 4)]);
            if (domeCount > MaximumDomes)
            {
                throw new InvalidDataException("The frame command dome count is invalid.");
            }
            ValidateDomeTable(bytes, domeCount);
        }
        _bytes = bytes;
    }

    /// <summary>
    /// Checks the fixed dome table before any accessor indexes into it.
    /// </summary>
    /// <remarks>
    /// The table is the ordering a per-prim dome mask names, so a malformed entry
    /// is not a cosmetic defect: an entry that claims to be absent while a mask
    /// sets its bit, or one carrying a non-finite ambient colour, produces a draw
    /// that is lit by a dome the frame never published or by a NaN. The tail past
    /// <paramref name="domeCount"/> must be zeroed, which is what makes "present"
    /// a property of the entry rather than of the reader's arithmetic.
    /// </remarks>
    private static void ValidateDomeTable(ReadOnlySpan<byte> bytes, uint domeCount)
    {
        const uint knownFlags =
            (uint)(SilkFrameDomeState.Present | SilkFrameDomeState.Textured);
        for (uint dome = 0; dome < MaximumDomes; dome++)
        {
            int entry = DomeTableOffset + checked((int)(dome * DomeEntrySize));
            uint flags = BinaryPrimitives.ReadUInt32LittleEndian(
                bytes.Slice(entry + 16, sizeof(uint)));
            if (dome >= domeCount)
            {
                if (flags != 0)
                {
                    throw new InvalidDataException(
                        "A frame dome entry past the published count is not zeroed.");
                }
            }
            else
            {
                if ((flags & ~knownFlags) != 0)
                {
                    throw new InvalidDataException(
                        "A frame dome entry carries unknown flags.");
                }
                if ((flags & (uint)SilkFrameDomeState.Present) == 0)
                {
                    throw new InvalidDataException(
                        "A published frame dome entry is not marked present.");
                }
            }
            for (int component = 0; component < 3; component++)
            {
                if (!float.IsFinite(BinaryPrimitives.ReadSingleLittleEndian(
                    bytes.Slice(entry + (component * sizeof(float)), sizeof(float)))))
                {
                    throw new InvalidDataException(
                        "A frame dome ambient colour is not finite.");
                }
            }
            if (BinaryPrimitives.ReadUInt32LittleEndian(
                    bytes.Slice(entry + 12, sizeof(uint))) != 0 ||
                BinaryPrimitives.ReadUInt32LittleEndian(
                    bytes.Slice(entry + 20, sizeof(uint))) != 0 ||
                BinaryPrimitives.ReadUInt32LittleEndian(
                    bytes.Slice(entry + 24, sizeof(uint))) != 0 ||
                BinaryPrimitives.ReadUInt32LittleEndian(
                    bytes.Slice(entry + 28, sizeof(uint))) != 0)
            {
                throw new InvalidDataException(
                    "A frame dome entry reserved field is not zero.");
            }
        }
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
        _bytes.Length >= ExtendedSize
            ? BinaryPrimitives.ReadUInt32LittleEndian(_bytes[MinimumSize..(MinimumSize + 4)])
            : 0;

    /// <summary>Gets an element from the eye-space clip plane table.</summary>
    internal double GetClipPlaneElement(int plane, int component)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(plane);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(plane, 8);
        ArgumentOutOfRangeException.ThrowIfNegative(component);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(component, 4);
        if (_bytes.Length < ExtendedSize)
        {
            return 0;
        }
        return BinaryPrimitives.ReadDoubleLittleEndian(
            _bytes.Slice(ClipPlaneOffset + (((plane * 4) + component) * 8), 8));
    }

    internal uint LightCount =>
        _bytes.Length >= LightingSize
            ? BinaryPrimitives.ReadUInt32LittleEndian(_bytes[LightCountOffset..(LightCountOffset + 4)])
            : 0;

    internal uint GetLightType(int light) => ReadLightUInt32(light, 0);

    internal uint GetLightShadowEnabled(int light) => ReadLightUInt32(light, 4);

    internal float GetLightShapeX(int light) => ReadLightSingle(light, 8);

    internal float GetLightShapeY(int light) => ReadLightSingle(light, 12);

    internal float GetLightColor(int light, int component) =>
        ReadLightSingle(light, 16 + (component * 4));

    internal float GetLightIntensity(int light) => ReadLightSingle(light, 28);

    internal double GetLightTransformElement(int light, int index)
    {
        ValidateLight(light);
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, 16);
        if (_bytes.Length < LightingSize)
        {
            return index % 5 == 0 ? 1 : 0;
        }
        return BinaryPrimitives.ReadDoubleLittleEndian(
            _bytes.Slice(LightTableOffset + (light * LightEntrySize) + 32 + (index * 8), 8));
    }

    internal float GetLightExposure(int light) => ReadLightSingle(light, 160);

    internal float GetLightDiffuse(int light) => ReadLightSingle(light, 164);

    internal float GetLightSpecular(int light) => ReadLightSingle(light, 168);

    internal float GetLightRadius(int light) => ReadLightSingle(light, 172);

    internal float GetAmbientColor(int component)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(component);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(component, 3);
        return _bytes.Length >= LightingSize
            ? BinaryPrimitives.ReadSingleLittleEndian(_bytes.Slice(AmbientOffset + (component * 4), 4))
            : 0;
    }

    internal float AmbientIntensity =>
        _bytes.Length >= LightingSize
            ? BinaryPrimitives.ReadSingleLittleEndian(_bytes.Slice(AmbientOffset + 12, 4))
            : 0;

    /// <summary>
    /// Gets the number of dome lights the ABI v21 dome table publishes, and
    /// therefore the number of bits a dome link mask can set.
    /// </summary>
    /// <remarks>
    /// Zero for a page that predates the table and for one whose scene authored
    /// more domes than the bounded table admits. Both mean the same thing to a
    /// consumer -- no dome is individually addressable, so every dome lights
    /// every prim -- which is why they are not distinguished here. The loss of a
    /// capability is reported on the light-link command instead.
    /// </remarks>
    internal uint DomeCount =>
        _bytes.Length >= DomeSize
            ? BinaryPrimitives.ReadUInt32LittleEndian(_bytes[DomeCountOffset..(DomeCountOffset + 4)])
            : 0;

    /// <summary>
    /// Gets one component of the ambient colour dome <paramref name="dome"/>
    /// contributes on its own.
    /// </summary>
    /// <remarks>
    /// Zero for a textured dome, whose emission is its image and reaches the
    /// consumer as an environment record instead. Summing this over every
    /// published dome reproduces <see cref="GetAmbientColor"/> bit for bit,
    /// because the producer accumulated that value from these exact floats in
    /// this exact order.
    /// </remarks>
    internal float GetDomeAmbientColor(int dome, int component)
    {
        ValidateDome(dome);
        ArgumentOutOfRangeException.ThrowIfNegative(component);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(component, 3);
        return _bytes.Length >= DomeSize
            ? BinaryPrimitives.ReadSingleLittleEndian(
                _bytes.Slice(DomeTableOffset + (dome * DomeEntrySize) + (component * 4), 4))
            : 0;
    }

    /// <summary>Gets the flags of one published dome table entry.</summary>
    internal SilkFrameDomeState GetDomeFlags(int dome)
    {
        ValidateDome(dome);
        return _bytes.Length >= DomeSize
            ? (SilkFrameDomeState)BinaryPrimitives.ReadUInt32LittleEndian(
                _bytes.Slice(DomeTableOffset + (dome * DomeEntrySize) + 16, 4))
            : SilkFrameDomeState.None;
    }

    private uint ReadLightUInt32(int light, int offset)
    {
        ValidateLight(light);
        return _bytes.Length >= LightingSize
            ? BinaryPrimitives.ReadUInt32LittleEndian(
                _bytes.Slice(LightTableOffset + (light * LightEntrySize) + offset, 4))
            : 0;
    }

    private float ReadLightSingle(int light, int offset)
    {
        ValidateLight(light);
        return _bytes.Length >= LightingSize
            ? BinaryPrimitives.ReadSingleLittleEndian(
                _bytes.Slice(LightTableOffset + (light * LightEntrySize) + offset, 4))
            : 0;
    }

    private static void ValidateLight(int light)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(light);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(light, 8);
    }

    private static void ValidateDome(int dome)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(dome);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(dome, (int)MaximumDomes);
    }

    private double ReadMatrixElement(int offset, int index)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, 16);
        return BinaryPrimitives.ReadDoubleLittleEndian(_bytes.Slice(offset + (index * 8), 8));
    }
}

/// <summary>
/// One level of the ordered instancing chain a retained record belongs to.
/// </summary>
/// <remarks>
/// Nested instancing has no single "the" instancer, so an instance is described
/// by one entry per level, ordered outermost to innermost, each naming the
/// instancer at that level and the instance's own index inside it. This is the
/// only description that decodes back to a scene instance; the composed ordinal
/// a record carries beside it keys the retained identity tables and counts in an
/// hdSilk-private space.
/// </remarks>
public readonly record struct SilkInstancerContextEntry
{
    /// <summary>Initializes one instancing level.</summary>
    /// <param name="instancerPath">The absolute path of the instancer at this level.</param>
    /// <param name="instanceIndex">The instance's own zero-based index inside it.</param>
    public SilkInstancerContextEntry(string instancerPath, int instanceIndex)
    {
        ArgumentNullException.ThrowIfNull(instancerPath);
        if (instancerPath.Length == 0 || instancerPath[0] != '/')
        {
            throw new ArgumentException(
                "An instancer context level requires an absolute prim path.",
                nameof(instancerPath));
        }
        ArgumentOutOfRangeException.ThrowIfNegative(instanceIndex);
        InstancerPath = instancerPath;
        InstanceIndex = instanceIndex;
    }

    /// <summary>Gets the absolute path of the instancer at this level.</summary>
    public string InstancerPath { get; }

    /// <summary>Gets the instance's own zero-based index inside that instancer.</summary>
    public int InstanceIndex { get; }
}

/// <summary>
/// A create or update command for one triangulated mesh.
/// </summary>
public readonly ref struct SilkMeshUpsertCommand
{
    private const int FixedSize = 268;
    private const int AttributeFixedSize = 20;
    private const int DeformationFixedSize = 96;
    private const int DeformationBlendRangeSize = 16;
    private const int DeformationBlendDeltaSize = 28;

    /// <summary>
    /// The exact ABI v22 byte ceiling of one record's two subprim-identity
    /// tables together, mirroring OPENUSD_SILK_MAX_SUBPRIM_IDENTITY_BYTES.
    /// </summary>
    /// <remarks>
    /// Checked against the sizes the declared counts imply, before anything is
    /// allocated or indexed, so a page that declares an enormous table is
    /// refused rather than sized against.
    /// </remarks>
    internal const long MaximumSubprimIdentityBytes = 67108864;

    /// <summary>
    /// The wire entry an emitted component with no authored counterpart uses.
    /// </summary>
    internal const uint SubprimNone = 0xFFFFFFFFu;

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
    private readonly int _deformationOffset;
    private readonly int _deformationLength;
    private readonly int _pointOriginCount;
    private readonly int _cornerEdgeCount;
    private readonly int _pointOriginOffset;
    private readonly int _cornerEdgeOffset;
    private readonly int _instancerPathLength;
    private readonly string _instancerPath;
    private readonly SilkInstancerContextEntry[] _instancerContext;

    /// <summary>
    /// The exact ABI v23 ceiling on the number of instancing levels one record
    /// may publish, mirroring OPENUSD_SILK_MAX_INSTANCER_CONTEXT_ENTRIES.
    /// </summary>
    /// <remarks>
    /// Checked against the declared count before the chain is walked or any
    /// managed storage is allocated for it, so a page that declares an
    /// implausible nesting depth is refused rather than sized against.
    /// </remarks>
    internal const int MaximumInstancerContextEntries = 64;

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
        _deformationLength = ReadCount(bytes, 232, "deformation byte");
        _pointOriginCount = ReadCount(bytes, 244, "point origin");
        _cornerEdgeCount = ReadCount(bytes, 248, "corner edge");
        _instancerPathLength = ReadCount(bytes, 260, "instancer path byte");
        int instancerContextCount = ReadCount(bytes, 264, "instancer context");
        if (instancerContextCount > MaximumInstancerContextEntries)
        {
            throw new InvalidDataException(
                "The mesh instancer context exceeds the ABI level budget.");
        }
        SilkTopologyKind topologyKind =
            (SilkTopologyKind)BinaryPrimitives.ReadUInt32LittleEndian(bytes[28..32]);
        int indicesPerPrimitive = topologyKind switch
        {
            SilkTopologyKind.TriangleList => 3,
            SilkTopologyKind.LineList => 2,
            SilkTopologyKind.PointList => 1,
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
        if (walked + _deformationLength > bytes.Length)
        {
            throw new InvalidDataException(
                "The mesh command size does not match its declared counts.");
        }
        _deformationOffset = (int)walked;
        ValidateDeformation(bytes, _deformationOffset, _deformationLength, _pointCount);
        long subprimOffsetStart = checked(walked + _deformationLength);
        long subprimBytes = checked(
            ((long)_pointOriginCount * sizeof(uint)) +
            ((long)_cornerEdgeCount * sizeof(uint)));
        // The ABI budget is checked against the declared counts before the
        // command is bounds-checked against them, so a page that declares an
        // enormous table is refused rather than sized against.
        if (subprimBytes > MaximumSubprimIdentityBytes)
        {
            throw new InvalidDataException(
                "The mesh subprim identity tables exceed the ABI byte budget.");
        }
        if (subprimOffsetStart + subprimBytes + _instancerPathLength > bytes.Length)
        {
            throw new InvalidDataException(
                "The mesh command size does not match its declared counts.");
        }
        _pointOriginOffset = (int)subprimOffsetStart;
        _cornerEdgeOffset = checked(
            _pointOriginOffset + (_pointOriginCount * sizeof(uint)));
        int instancerPathOffset = checked(
            _cornerEdgeOffset + (_cornerEdgeCount * sizeof(uint)));
        _instancerPath = _instancerPathLength == 0
            ? string.Empty
            : SilkWireFormat.DecodePath(
                bytes.Slice(instancerPathOffset, _instancerPathLength));

        // ABI v23. The ordered chain closes the record, so walking it is what
        // keeps the exact-size check exact. Every level is decoded here rather
        // than lazily, because the chain is small, bounded, and every consumer
        // that reads it reads all of it.
        _instancerContext = instancerContextCount == 0
            ? []
            : new SilkInstancerContextEntry[instancerContextCount];
        long contextOffset = checked(instancerPathOffset + _instancerPathLength);
        for (int level = 0; level < instancerContextCount; level++)
        {
            if (contextOffset + 8 > bytes.Length)
            {
                throw new InvalidDataException(
                    "A mesh instancer context entry is truncated.");
            }
            int entryPathLength = ReadCount(
                bytes,
                (int)contextOffset,
                "instancer context path byte");
            int entryIndex = BinaryPrimitives.ReadInt32LittleEndian(
                bytes.Slice((int)contextOffset + 4, sizeof(int)));
            if (entryIndex < 0)
            {
                throw new InvalidDataException(
                    "A mesh instancer context index must be non-negative.");
            }
            if (contextOffset + 8 + entryPathLength > bytes.Length)
            {
                throw new InvalidDataException(
                    "A mesh instancer context entry is truncated.");
            }
            _instancerContext[level] = new SilkInstancerContextEntry(
                SilkWireFormat.DecodePath(
                    bytes.Slice((int)contextOffset + 8, entryPathLength)),
                entryIndex);
            contextOffset += 8 + entryPathLength;
        }
        if (contextOffset != bytes.Length)
        {
            throw new InvalidDataException(
                "The mesh command size does not match its declared counts.");
        }

        // An instance index with no instancer to index is not an identity a
        // consumer can round-trip, so the two are required together.
        int instanceId = BinaryPrimitives.ReadInt32LittleEndian(bytes[20..24]);
        if ((_instancerPathLength == 0) != (instanceId == 0))
        {
            throw new InvalidDataException(
                "A mesh must carry an instancer path exactly when it belongs to " +
                "an instancer.");
        }

        // The chain is published exactly when the record belongs to an
        // instancer, and it ends at the instancer the record separately names.
        // A record that contradicted its own chain would describe two different
        // instances for one hit.
        if ((instancerContextCount == 0) != (_instancerPathLength == 0))
        {
            throw new InvalidDataException(
                "A mesh must carry an instancer context exactly when it belongs " +
                "to an instancer.");
        }
        if (instancerContextCount != 0 &&
            !string.Equals(
                _instancerContext[^1].InstancerPath,
                _instancerPath,
                StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                "A mesh instancer context must end at the instancer the record " +
                "names.");
        }
        ValidateSubprimIdentity(
            bytes,
            _pointOriginOffset,
            _pointOriginCount,
            _cornerEdgeOffset,
            _cornerEdgeCount,
            _pointCount,
            _triangleCount,
            topologyKind);
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
    /// Gets the instance's own index inside its owning instancer. A prim with
    /// no instancer always reports zero; a point-instanced prototype reports
    /// one full record on the lowest index it owns and lightweight records for
    /// its later instances, so (<see cref="Path"/>, <see cref="InstanceIndex"/>)
    /// is the retained identity.
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

    /// <summary>
    /// Gets whether this ABI v8 record is a lightweight point-instance record
    /// that reuses the full prototype geometry carried by the lowest instance
    /// index the prototype owns.
    /// </summary>
    internal bool IsInstanceReference =>
        InstanceId != 0 &&
        InstanceIndex > 0 &&
        PointCount == 0 &&
        IndexCount == 0 &&
        TriangleCount == 0 &&
        AttributeCount == 0 &&
        !HasDeformation &&
        SubprimIdentity == SilkSubprimIdentity.None &&
        MaterialPath.Length == 0;

    /// <summary>
    /// Gets the pick targets this ABI v22 record answers with authored identity.
    /// </summary>
    /// <remarks>
    /// A cleared flag is a refusal, not missing data: the delegate could not map
    /// the emitted components onto authored ones, and
    /// <see cref="SubprimUnsupported"/> names why. A consumer must refuse the
    /// target rather than substituting an emitted index.
    /// </remarks>
    public SilkSubprimIdentity SubprimIdentity =>
        (SilkSubprimIdentity)BinaryPrimitives.ReadUInt32LittleEndian(
            _bytes[236..240]);

    /// <summary>Gets why this record refuses an exact subprim target.</summary>
    public SilkSubprimUnsupportedReason SubprimUnsupported =>
        (SilkSubprimUnsupportedReason)BinaryPrimitives.ReadUInt32LittleEndian(
            _bytes[240..244]);

    /// <summary>Gets the published point-origin entry count, zero when absent.</summary>
    public int PointOriginCount => _pointOriginCount;

    /// <summary>Gets the published corner-edge entry count, zero when absent.</summary>
    public int CornerEdgeCount => _cornerEdgeCount;

    /// <summary>Gets one past the largest authored edge index the record names.</summary>
    public int AuthoredEdgeCount =>
        checked((int)BinaryPrimitives.ReadUInt32LittleEndian(_bytes[252..256]));

    /// <summary>Gets one past the largest authored point index the record names.</summary>
    public int AuthoredPointCount =>
        checked((int)BinaryPrimitives.ReadUInt32LittleEndian(_bytes[256..260]));

    /// <summary>
    /// Gets the absolute path of the owning instancer, empty when the prim has
    /// no instancer.
    /// </summary>
    /// <remarks>
    /// This is the authoritative instance identity. <see cref="InstanceId"/> is
    /// a hash and tells two instancers apart for diagnostics only; it cannot be
    /// turned back into the path a selection has to name.
    /// </remarks>
    public string InstancerPath => _instancerPath;

    /// <summary>
    /// Gets the complete ordered instancing chain, outermost level first and
    /// innermost last. Empty when the prim has no instancer.
    /// </summary>
    /// <remarks>
    /// Nested instancing has no single "the" instancer. A prototype instanced by
    /// an inner instancer that is itself instanced by an outer one has one index
    /// per level, and <see cref="InstanceIndex"/> is a composed ordinal in an
    /// hdSilk-private space rather than any level's own index. Reporting that
    /// ordinal beside <see cref="InstancerPath"/> would describe an instance
    /// that does not exist, so a consumer that has to name a scene instance
    /// reads this chain. For the overwhelmingly common single-level scene the
    /// chain has one entry whose index is exactly
    /// <see cref="InstanceIndex"/>, which is what keeps the flattened pair a
    /// truthful convenience there.
    /// </remarks>
    public ReadOnlySpan<SilkInstancerContextEntry> InstancerContext =>
        _instancerContext;

    /// <summary>
    /// Gets the authored point one emitted vertex came from, or -1 when the
    /// vertex has no authored origin.
    /// </summary>
    public int GetPointOrigin(int pointIndex)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(pointIndex);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(
            pointIndex,
            _pointOriginCount);
        uint origin = BinaryPrimitives.ReadUInt32LittleEndian(
            _bytes.Slice(_pointOriginOffset + (pointIndex * sizeof(uint)), sizeof(uint)));
        return origin == SubprimNone ? -1 : checked((int)origin);
    }

    /// <summary>
    /// Gets the authored mesh edge one emitted primitive corner spans, or -1
    /// when the corner is a triangulation diagonal the scene never authored.
    /// </summary>
    public int GetCornerEdge(int cornerIndex)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(cornerIndex);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(
            cornerIndex,
            _cornerEdgeCount);
        uint edge = BinaryPrimitives.ReadUInt32LittleEndian(
            _bytes.Slice(_cornerEdgeOffset + (cornerIndex * sizeof(uint)), sizeof(uint)));
        return edge == SubprimNone ? -1 : checked((int)edge);
    }

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

    /// <summary>
    /// Gets the optional sections the ABI v20 deformation block carries.
    /// </summary>
    public SilkDeformationOptions DeformationFlags =>
        (SilkDeformationOptions)BinaryPrimitives.ReadUInt32LittleEndian(
            _bytes[224..228]);

    /// <summary>
    /// Gets why a deformed prim published no bounded rig. hdSilk always
    /// publishes the CPU-resolved points, so this names what a consumer that
    /// wanted to evaluate the rig itself did not receive rather than a defect
    /// in the record.
    /// </summary>
    public SilkDeformationUnsupportedFeatures DeformationUnsupportedFeatures =>
        (SilkDeformationUnsupportedFeatures)BinaryPrimitives.ReadUInt32LittleEndian(
            _bytes[228..232]);

    /// <summary>Gets whether this record carries a bounded deformation rig.</summary>
    public bool HasDeformation => _deformationLength != 0;

    /// <summary>
    /// Copies the bounded deformation rig, or returns <see langword="null"/>
    /// when the record carries none.
    /// </summary>
    public SilkMeshDeformationData? CopyDeformation() =>
        _deformationLength == 0
            ? null
            : SilkMeshDeformationData.Decode(
                _bytes.Slice(_deformationOffset, _deformationLength),
                DeformationFlags,
                DeformationUnsupportedFeatures);

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

    /// <summary>
    /// Validates the ABI v20 deformation block before any accessor indexes into
    /// it. Every bound the ABI declares is checked here rather than at
    /// evaluation time, because a rig is read once per frame for every point it
    /// describes and a consumer must be able to trust the counts it declares.
    /// </summary>
    /// <summary>
    /// Validates the ABI v22 subprim-identity tables against the emitted arrays
    /// they describe and against the identity the record claims.
    /// </summary>
    /// <remarks>
    /// A table is either absent or complete. A partial table would let a
    /// consumer read authored identity for some emitted components and an
    /// emitted index for the rest, which is exactly the confusion these tables
    /// exist to remove, so a partial table is malformed rather than degraded.
    /// </remarks>
    private static void ValidateSubprimIdentity(
        ReadOnlySpan<byte> bytes,
        int pointOriginOffset,
        int pointOriginCount,
        int cornerEdgeOffset,
        int cornerEdgeCount,
        int pointCount,
        int primitiveCount,
        SilkTopologyKind topologyKind)
    {
        uint identity = BinaryPrimitives.ReadUInt32LittleEndian(bytes[236..240]);
        uint unsupported = BinaryPrimitives.ReadUInt32LittleEndian(bytes[240..244]);
        uint authoredEdgeCount =
            BinaryPrimitives.ReadUInt32LittleEndian(bytes[252..256]);
        uint authoredPointCount =
            BinaryPrimitives.ReadUInt32LittleEndian(bytes[256..260]);
        if ((identity & ~(uint)(
                SilkSubprimIdentity.Face |
                SilkSubprimIdentity.Edge |
                SilkSubprimIdentity.Point)) != 0)
        {
            throw new InvalidDataException(
                "A mesh command declares an unknown subprim identity flag.");
        }
        if ((unsupported & ~(uint)(
                SilkSubprimUnsupportedReason.RefinedSubdivision |
                SilkSubprimUnsupportedReason.TopologyMode |
                SilkSubprimUnsupportedReason.Geometry |
                SilkSubprimUnsupportedReason.Budget)) != 0)
        {
            throw new InvalidDataException(
                "A mesh command declares an unknown subprim unsupported reason.");
        }
        if (authoredEdgeCount > int.MaxValue || authoredPointCount > int.MaxValue)
        {
            throw new InvalidDataException(
                "A mesh authored subprim count exceeds the managed identity range.");
        }

        bool claimsPoints = (identity & (uint)SilkSubprimIdentity.Point) != 0;
        if (claimsPoints != (pointOriginCount != 0))
        {
            throw new InvalidDataException(
                "A mesh point-origin table must be published exactly when the " +
                "record claims authored point identity.");
        }
        if (pointOriginCount != 0)
        {
            if (pointOriginCount != pointCount)
            {
                throw new InvalidDataException(
                    "A mesh requires one point origin per emitted vertex.");
            }
            long largestPoint = -1;
            for (int index = 0; index < pointOriginCount; index++)
            {
                uint origin = BinaryPrimitives.ReadUInt32LittleEndian(
                    bytes.Slice(
                        pointOriginOffset + (index * sizeof(uint)),
                        sizeof(uint)));
                if (origin == SubprimNone)
                {
                    continue;
                }
                if (origin >= authoredPointCount)
                {
                    throw new InvalidDataException(
                        "A mesh point origin is outside the authored point count " +
                        "the record declares.");
                }
                largestPoint = Math.Max(largestPoint, origin);
            }

            // The ABI defines authored_point_count as one past the largest
            // authored index the table names. Enforcing that exactly is what
            // keeps a record from declaring an enormous authored space behind a
            // one-entry table: every authored index a consumer can ever be
            // handed is named by an entry it already read.
            if (authoredPointCount != largestPoint + 1)
            {
                throw new InvalidDataException(
                    "A mesh authored point count is not one past the largest " +
                    "authored index its point-origin table names.");
            }
        }
        else if (authoredPointCount != 0)
        {
            throw new InvalidDataException(
                "A mesh declares authored points with no point-origin table.");
        }

        bool claimsEdges = (identity & (uint)SilkSubprimIdentity.Edge) != 0;
        if (claimsEdges != (cornerEdgeCount != 0))
        {
            throw new InvalidDataException(
                "A mesh corner-edge table must be published exactly when the " +
                "record claims authored edge identity.");
        }
        if (cornerEdgeCount != 0)
        {
            int cornersPerPrimitive = topologyKind switch
            {
                SilkTopologyKind.TriangleList => 3,
                SilkTopologyKind.LineList => 1,
                _ => 0
            };
            if (cornersPerPrimitive == 0 ||
                cornerEdgeCount != primitiveCount * cornersPerPrimitive)
            {
                throw new InvalidDataException(
                    "A mesh requires one corner edge per emitted primitive corner.");
            }
            long largestEdge = -1;
            for (int index = 0; index < cornerEdgeCount; index++)
            {
                uint edge = BinaryPrimitives.ReadUInt32LittleEndian(
                    bytes.Slice(
                        cornerEdgeOffset + (index * sizeof(uint)),
                        sizeof(uint)));
                if (edge == SubprimNone)
                {
                    continue;
                }
                if (edge >= authoredEdgeCount)
                {
                    throw new InvalidDataException(
                        "A mesh corner edge is outside the authored edge count " +
                        "the record declares.");
                }
                largestEdge = Math.Max(largestEdge, edge);
            }
            if (authoredEdgeCount != largestEdge + 1)
            {
                throw new InvalidDataException(
                    "A mesh authored edge count is not one past the largest " +
                    "authored index its corner-edge table names.");
            }
        }
        else if (authoredEdgeCount != 0)
        {
            throw new InvalidDataException(
                "A mesh declares authored edges with no corner-edge table.");
        }
    }

    private static void ValidateDeformation(
        ReadOnlySpan<byte> bytes,
        int offset,
        int length,
        int pointCount)
    {
        SilkDeformationOptions flags =
            (SilkDeformationOptions)BinaryPrimitives.ReadUInt32LittleEndian(
                bytes[224..228]);
        SilkDeformationUnsupportedFeatures unsupported =
            (SilkDeformationUnsupportedFeatures)BinaryPrimitives.ReadUInt32LittleEndian(
                bytes[228..232]);
        if ((unsupported & ~SilkDeformationLimits.KnownUnsupportedFeatures) != 0)
        {
            throw new InvalidDataException(
                "The mesh deformation names an unknown unsupported feature.");
        }
        if (length == 0)
        {
            if (flags != SilkDeformationOptions.None)
            {
                throw new InvalidDataException(
                    "A mesh without a deformation block declared deformation flags.");
            }
            return;
        }
        if ((flags & ~SilkDeformationLimits.KnownOptions) != 0)
        {
            throw new InvalidDataException("The mesh deformation has an unknown flag.");
        }
        if (length < DeformationFixedSize)
        {
            throw new InvalidDataException("The mesh deformation block is truncated.");
        }
        if (length > SilkDeformationLimits.MaximumBytes)
        {
            throw new InvalidDataException(
                "The mesh deformation block exceeds the page byte budget.");
        }

        ReadOnlySpan<byte> block = bytes.Slice(offset, length);
        int jointCount = ReadCount(block, 0, "deformation joint");
        int influences = ReadCount(block, 4, "deformation influence");
        int bindPointCount = ReadCount(block, 8, "deformation bind point");
        int blendRangeCount = ReadCount(block, 12, "deformation blend range");
        int blendDeltaCount = ReadCount(block, 16, "deformation blend delta");
        if (BinaryPrimitives.ReadUInt32LittleEndian(block[20..24]) != 0)
        {
            throw new InvalidDataException(
                "The mesh deformation reserved field must be zero.");
        }
        if (jointCount is < 1 || jointCount > SilkDeformationLimits.MaximumJoints)
        {
            throw new InvalidDataException(
                "The mesh deformation joint count is outside the page budget.");
        }
        if (influences is < 1 || influences > SilkDeformationLimits.MaximumInfluences)
        {
            throw new InvalidDataException(
                "The mesh deformation influence width is outside the page budget.");
        }
        if (blendRangeCount > SilkDeformationLimits.MaximumBlendRanges ||
            blendDeltaCount > SilkDeformationLimits.MaximumBlendDeltas)
        {
            throw new InvalidDataException(
                "The mesh deformation blend tables are outside the page budget.");
        }
        if (bindPointCount != pointCount)
        {
            throw new InvalidDataException(
                "The mesh deformation must bind one point per emitted point.");
        }

        bool hasBindNormals = flags.HasFlag(SilkDeformationOptions.BindNormals);
        long expected = DeformationFixedSize +
            ((long)bindPointCount * 3 * sizeof(float)) +
            (hasBindNormals ? (long)bindPointCount * 3 * sizeof(float) : 0) +
            ((long)bindPointCount * influences * sizeof(uint)) +
            ((long)bindPointCount * influences * sizeof(float)) +
            ((long)jointCount * 16 * sizeof(float)) +
            ((long)blendRangeCount * DeformationBlendRangeSize) +
            ((long)blendDeltaCount * DeformationBlendDeltaSize);
        if (expected != length)
        {
            throw new InvalidDataException(
                "The mesh deformation block size does not match its declared counts.");
        }

        int influenceCount = bindPointCount * influences;
        int normalsOffset = DeformationFixedSize +
            (bindPointCount * 3 * sizeof(float));
        int indicesOffset = normalsOffset +
            (hasBindNormals ? bindPointCount * 3 * sizeof(float) : 0);
        for (int slot = 0; slot < influenceCount; slot++)
        {
            uint joint = BinaryPrimitives.ReadUInt32LittleEndian(
                block.Slice(indicesOffset + (slot * sizeof(uint)), sizeof(uint)));
            if (joint >= (uint)jointCount)
            {
                throw new InvalidDataException(
                    "A mesh deformation joint index is outside the joint palette.");
            }
        }

        // Every floating stream is checked before anything indexes into it. A
        // non-finite weight or matrix element does not fail loudly downstream:
        // it propagates through the whole evaluation and lands as a NaN vertex,
        // which a rasterizer silently drops, so the surface simply loses
        // triangles with nothing naming why.
        int weightsOffset = indicesOffset + (influenceCount * sizeof(uint));
        int matricesOffset = weightsOffset + (influenceCount * sizeof(float));
        ValidateFiniteFloats(
            block.Slice(32, 16 * sizeof(float)),
            "geom bind transform");
        ValidateFiniteFloats(
            block.Slice(DeformationFixedSize, bindPointCount * 3 * sizeof(float)),
            "bind point");
        if (hasBindNormals)
        {
            ValidateFiniteFloats(
                block.Slice(normalsOffset, bindPointCount * 3 * sizeof(float)),
                "bind normal");
        }
        ValidateFiniteFloats(
            block.Slice(weightsOffset, influenceCount * sizeof(float)),
            "joint weight");
        ValidateFiniteFloats(
            block.Slice(matricesOffset, jointCount * 16 * sizeof(float)),
            "joint matrix");

        int rangeOffset = matricesOffset + (jointCount * 16 * sizeof(float));
        for (int range = 0; range < blendRangeCount; range++)
        {
            int entry = rangeOffset + (range * DeformationBlendRangeSize);
            uint first = BinaryPrimitives.ReadUInt32LittleEndian(
                block.Slice(entry, sizeof(uint)));
            uint count = BinaryPrimitives.ReadUInt32LittleEndian(
                block.Slice(entry + 4, sizeof(uint)));
            ValidateFiniteFloats(
                block.Slice(entry + 8, sizeof(float)),
                "blend range weight");
            if (BinaryPrimitives.ReadUInt32LittleEndian(
                    block.Slice(entry + 12, sizeof(uint))) != 0)
            {
                throw new InvalidDataException(
                    "A mesh deformation blend range reserved field must be zero.");
            }
            if ((long)first + count > blendDeltaCount)
            {
                throw new InvalidDataException(
                    "A mesh deformation blend range is outside the delta table.");
            }
        }

        int deltaOffset = rangeOffset + (blendRangeCount * DeformationBlendRangeSize);
        for (int delta = 0; delta < blendDeltaCount; delta++)
        {
            int entry = deltaOffset + (delta * DeformationBlendDeltaSize);
            uint point = BinaryPrimitives.ReadUInt32LittleEndian(
                block.Slice(entry, sizeof(uint)));
            if (point >= (uint)bindPointCount)
            {
                throw new InvalidDataException(
                    "A mesh deformation blend delta is outside the point array.");
            }
            ValidateFiniteFloats(
                block.Slice(entry + 4, 3 * sizeof(float)),
                "blend delta position offset");
            ValidateFiniteFloats(
                block.Slice(entry + 16, 3 * sizeof(float)),
                "blend delta normal offset");
        }

        // The identity is recomputed here, in the production parser, rather
        // than only in a test. It indexes the rig for the retained geometry key
        // and for shadow-map invalidation, so a page whose identity does not
        // cover the bytes it shipped would let a changed pose reuse a resource
        // keyed on the previous one. Recomputing it also catches a block whose
        // content was altered while its declared identity stayed put, which no
        // other check in this method sees.
        ulong declared = BinaryPrimitives.ReadUInt64LittleEndian(block[24..32]);
        ulong computed = 14695981039346656037UL;
        foreach (byte value in block[32..])
        {
            computed ^= value;
            computed *= 1099511628211UL;
        }
        if (declared != computed)
        {
            throw new InvalidDataException(
                "The mesh deformation identity does not cover its published bytes.");
        }
    }

    private static void ValidateFiniteFloats(ReadOnlySpan<byte> bytes, string name)
    {
        for (int offset = 0; offset + sizeof(float) <= bytes.Length; offset += sizeof(float))
        {
            if (!float.IsFinite(
                    BinaryPrimitives.ReadSingleLittleEndian(
                        bytes.Slice(offset, sizeof(float)))))
            {
                throw new InvalidDataException(
                    $"A mesh deformation {name} is not finite.");
            }
        }
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
    Tangent = 4,

    /// <summary>
    /// Authored curve or point widths, resolved onto the emitted vertices.
    /// </summary>
    /// <remarks>
    /// A line list emitted from linear segmented basis curves carries this
    /// entry under the authored name <c>widths</c> with one component. It does
    /// not change the emitted geometry: every supported backend rasterizes a
    /// line at exactly one pixel, matching Storm's measured behaviour for
    /// linear curves, so a consumer that wants ribbons builds them itself from
    /// these values.
    /// </remarks>
    Width = 5
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
    PreviewSurface = 1,

    /// <summary>A MaterialX standard_surface projected to PreviewSurface-compatible inputs.</summary>
    MaterialXProjected = 2,

    /// <summary>A MaterialX graph compiled to a runtime fragment shader.</summary>
    MaterialXGenerated = 3,

    /// <summary>A uniform density volume proxy rendered with emission/absorption.</summary>
    VolumeDensity = 4,

    /// <summary>
    /// An MDL material whose accepted parameter subset was distilled into the
    /// PreviewSurface-compatible tables.
    /// </summary>
    /// <remarks>
    /// Provenance and cache identity, not a second shading model: a distilled
    /// MDL material carries the same scalar and texture tables a
    /// UsdPreviewSurface one does and is shaded by the same pipeline. The
    /// original MDL network is untouched on the stage; distillation exists only
    /// so a bound MDL-only material can be shaded at all.
    /// </remarks>
    MdlDistilled = 5,

    /// <summary>
    /// A material whose only surface terminal is authored in the MDL render
    /// context and that this runtime could not distil.
    /// </summary>
    /// <remarks>
    /// Published with empty tables when no optional MDL adapter is installed --
    /// the state of every base package -- or when the module, the material, or
    /// its authored inputs fall outside the accepted distillation subset. It is
    /// distinct from <see cref="Unsupported"/> so a consumer can name MDL as the
    /// cause rather than reporting an unrecognised shading graph.
    /// </remarks>
    MdlUnavailable = 6
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
    UseSpecularWorkflow = 14,

    /// <summary>Uniform volume density, one component.</summary>
    VolumeDensity = 15
}

/// <summary>
/// How one texture entry combines with the primary entry of the same parameter.
/// </summary>
/// <remarks>
/// The renderer binds one composite image per material rather than one per
/// surface input, so a material carries at most one entry whose operator is not
/// <see cref="None"/>. The combination is evaluated in the fragment shader in
/// floating point after both decodes, so unlike the per-entry scale and bias it
/// is not clamped by an eight-bit source.
/// </remarks>
public enum SilkCompositeOperator : uint
{
    /// <summary>This entry is the primary operand and combines with nothing.</summary>
    None = 0,

    /// <summary><c>primary * composite</c>.</summary>
    Multiply = 1,

    /// <summary><c>primary + composite</c>.</summary>
    Add = 2,

    /// <summary><c>primary - composite</c>, in that order.</summary>
    Subtract = 3,

    /// <summary><c>primary * (1 - factor) + composite * factor</c>.</summary>
    Mix = 4
}

/// <summary>Texture wrap mode.</summary>
public enum SilkTextureWrap : uint
{
    /// <summary>
    /// UsdUVTexture's authored <c>black</c>, any wrap token this renderer does
    /// not recognise, and MaterialX <c>constant</c> addressing.
    /// </summary>
    /// <remarks>
    /// The name records the authored intent, not what every stage does with it.
    /// The wire carries no border colour, no supported backend is given one, and
    /// <c>SilkSceneGpuResources</c> resolves this mode to
    /// <see cref="SilkSamplerAddressMode.ClampToEdge"/> for the fragment stage,
    /// so a fragment sample outside the unit range returns the edge texel. The
    /// vertex-stage displacement sampler owns its own addressing and implements
    /// this mode exactly, as a transparent-black border that contributes zero
    /// height to a bilinear blend.
    /// </remarks>
    Black = 0,

    /// <summary>Clamp to edge.</summary>
    Clamp = 1,

    /// <summary>Repeat.</summary>
    Repeat = 2,

    /// <summary>Mirror.</summary>
    Mirror = 3,

    /// <summary>
    /// UsdUVTexture's <c>useMetadata</c>, which is also its schema default and
    /// therefore what an unauthored <c>wrap</c> means.
    /// </summary>
    /// <remarks>
    /// Distinct from <see cref="Black"/> because it records different authored
    /// intent: <c>black</c> states the addressing, while <c>useMetadata</c>
    /// defers it to wrap metadata inside the image file. hdSilk reads no image
    /// wrap metadata, and USD's documented fallback when no metadata is present
    /// is <c>black</c>, so both resolve to the same addressing in this renderer
    /// -- but a consumer that reads metadata, or that reports what it resolved,
    /// must be able to tell them apart.
    /// </remarks>
    UseMetadata = 4
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
/// The connected output port of a <c>UsdUVTexture</c>, which decides which channel
/// of the sampled texel drives the bound surface input.
/// </summary>
/// <remarks>
/// These are exactly the outputs <c>UsdUVTexture</c> declares: four single-channel
/// outputs and one three-channel output. There is no unspecified value: hdSilk
/// rejects a connection whose output it cannot resolve rather than publishing a
/// channel nobody authored, so every entry on the wire names a real port.
/// </remarks>
public enum SilkTextureChannel : uint
{
    /// <summary>The <c>outputs:r</c> port.</summary>
    R = 0,

    /// <summary>The <c>outputs:g</c> port.</summary>
    G = 1,

    /// <summary>The <c>outputs:b</c> port.</summary>
    B = 2,

    /// <summary>The <c>outputs:a</c> port.</summary>
    A = 3,

    /// <summary>The three-channel <c>outputs:rgb</c> port.</summary>
    Rgb = 4
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
    internal const int FixedSize = 88;
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

    /// <summary>
    /// Gets the connected UsdUVTexture output port, which selects which channel of
    /// the sampled texel drives the bound input.
    /// </summary>
    public SilkTextureChannel Channel =>
        (SilkTextureChannel)BinaryPrimitives.ReadUInt32LittleEndian(_bytes[76..80]);

    /// <summary>
    /// Gets how this entry combines with the primary entry of the same parameter.
    /// </summary>
    /// <remarks>
    /// <see cref="SilkCompositeOperator.None"/> marks the primary entry itself.
    /// Any other value marks the second operand of a two-image surface input,
    /// which the shader combines with the primary per pixel after both decodes.
    /// </remarks>
    public SilkCompositeOperator CompositeOperator =>
        (SilkCompositeOperator)BinaryPrimitives.ReadUInt32LittleEndian(_bytes[80..84]);

    /// <summary>
    /// Gets the blend factor, which is meaningful only for
    /// <see cref="SilkCompositeOperator.Mix"/> and zero otherwise.
    /// </summary>
    public float CompositeFactor =>
        BinaryPrimitives.ReadSingleLittleEndian(_bytes[84..88]);

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

    /// <summary>
    /// Bytes of the trailing row-major affine (m00, m01, m10, m11, tx, ty) every
    /// texture of this material samples its coordinates through.
    /// </summary>
    private const int UvTransformByteCount = 6 * sizeof(float);

    private readonly ReadOnlySpan<byte> _bytes;
    private readonly string _path;
    private readonly int _pathLength;
    private readonly int _generatedFragmentOffset;
    private readonly int _generatedFragmentLength;
    private readonly int _generatedFragmentMslSourceOffset;
    private readonly int _generatedFragmentMslSourceLength;
    private readonly int _uvTransformOffset;

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
        if (offset + sizeof(uint) > bytes.Length)
        {
            throw new InvalidDataException(
                "The material upsert generated shader payload length is truncated.");
        }
        uint generatedFragmentLength = BinaryPrimitives.ReadUInt32LittleEndian(bytes.Slice(offset, sizeof(uint)));
        if (generatedFragmentLength > int.MaxValue)
        {
            throw new InvalidDataException(
                "The generated MaterialX fragment SPIR-V payload exceeds the managed page limit.");
        }
        _generatedFragmentLength = (int)generatedFragmentLength;
        offset += sizeof(uint);
        if ((_generatedFragmentLength % sizeof(uint)) != 0)
        {
            throw new InvalidDataException(
                "The generated MaterialX fragment SPIR-V payload must be 32-bit aligned.");
        }
        _generatedFragmentOffset = offset;
        if (_generatedFragmentLength > bytes.Length - offset)
        {
            throw new InvalidDataException(
                "The generated MaterialX fragment SPIR-V payload is truncated.");
        }
        offset += _generatedFragmentLength;
        if (offset + sizeof(uint) > bytes.Length)
        {
            throw new InvalidDataException(
                "The material upsert generated MSL source payload length is truncated.");
        }
        uint generatedMslSourceLength = BinaryPrimitives.ReadUInt32LittleEndian(bytes.Slice(offset, sizeof(uint)));
        if (generatedMslSourceLength > int.MaxValue)
        {
            throw new InvalidDataException(
                "The generated MaterialX fragment MSL source payload exceeds the managed page limit.");
        }
        _generatedFragmentMslSourceLength = (int)generatedMslSourceLength;
        offset += sizeof(uint);
        _generatedFragmentMslSourceOffset = offset;
        if (_generatedFragmentMslSourceLength > bytes.Length - offset)
        {
            throw new InvalidDataException(
                "The generated MaterialX fragment MSL source payload is truncated.");
        }
        offset += _generatedFragmentMslSourceLength;
        if (offset + UvTransformByteCount > bytes.Length)
        {
            throw new InvalidDataException(
                "The material upsert UV transform is truncated.");
        }
        _uvTransformOffset = offset;
        offset += UvTransformByteCount;
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

    /// <summary>
    /// Gets one element of the constant texture-coordinate transform every
    /// texture of this material samples through, in the row-major order
    /// (m00, m01, m10, m11, tx, ty).
    /// </summary>
    /// <remarks>
    /// The identity (1, 0, 0, 1, 0, 0) unless the graph routed texture
    /// coordinates through a chain of MaterialX place2d or UsdPreviewSurface
    /// UsdTransform2d nodes whose inputs are all constant. hdSilk folds and
    /// composes that chain exactly, so the consumer applies one affine rather
    /// than re-deriving pivot, scale, rotation, offset, and operation order.
    /// </remarks>
    public float GetUvTransform(int index)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, 6);
        return BinaryPrimitives.ReadSingleLittleEndian(
            _bytes.Slice(_uvTransformOffset + (index * sizeof(float)), sizeof(float)));
    }

    /// <summary>Gets generated MaterialX fragment SPIR-V bytes, if this material carries any.</summary>
    public ReadOnlySpan<byte> GeneratedFragmentSpirV =>
        _generatedFragmentLength == 0
            ? []
            : _bytes.Slice(_generatedFragmentOffset, _generatedFragmentLength);

    /// <summary>Gets generated MaterialX fragment MSL source bytes, if this material carries any.</summary>
    public ReadOnlySpan<byte> GeneratedFragmentMslSource =>
        _generatedFragmentMslSourceLength == 0
            ? []
            : _bytes.Slice(_generatedFragmentMslSourceOffset, _generatedFragmentMslSourceLength);

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
        uint channel =
            BinaryPrimitives.ReadUInt32LittleEndian(bytes.Slice(offset + 76, 4));
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
        if (channel > (uint)SilkTextureChannel.Rgb)
        {
            throw new InvalidDataException(
                "A material texture entry must name a known UsdUVTexture output channel.");
        }
        // The channel and the consumed width are two statements about the same
        // connection, so a page that disagrees with itself is malformed rather
        // than something to reconcile by preferring one field over the other.
        bool isRgb = channel == (uint)SilkTextureChannel.Rgb;
        if (isRgb ? components < 3 : components != 1)
        {
            throw new InvalidDataException(
                "A material texture entry must select rgb for a colour or vector input " +
                "and a single channel for a one-component input.");
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

/// <summary>
/// The <c>texture:format</c> a <c>UsdLuxDomeLight</c> declares for its image.
/// </summary>
/// <remarks>
/// <see cref="Automatic"/> is the UsdLux default and asks the consumer to derive
/// the mapping from the image itself. A consumer that cannot derive it treats
/// <see cref="Automatic"/> as <see cref="Latlong"/>, which is what every
/// equirectangular HDR in the accepted corpus is.
/// </remarks>
public enum SilkDomeTextureFormat : uint
{
    /// <summary>Derived from the image.</summary>
    Automatic = 0,

    /// <summary>Equirectangular latitude/longitude.</summary>
    Latlong = 1,

    /// <summary>Mirrored ball.</summary>
    MirroredBall = 2,

    /// <summary>Angular.</summary>
    Angular = 3,

    /// <summary>Vertical-cross cube map.</summary>
    CubeMapVerticalCross = 4
}

/// <summary>
/// Authored dome-light behaviour hdSilk did not put on the wire.
/// </summary>
/// <remarks>
/// Each flag names one slice so a consumer's diagnostic can name it against the
/// affected prim instead of rendering a plausible but wrong result. These are
/// producer-side only: a consumer that cannot implement a given environment
/// response diagnoses that itself.
/// </remarks>
[Flags]
public enum SilkEnvironmentUnsupportedFeatures : uint
{
    /// <summary>Everything authored on the dome reached the wire.</summary>
    None = 0,

    /// <summary><c>enableColorTemperature</c> is on and was not applied.</summary>
    ColorTemperature = 1,

    /// <summary><c>poleAxis</c> is authored to something other than <c>scene</c>.</summary>
    PoleAxis = 2,

    /// <summary>
    /// A <c>collection:lightLink</c> collection is authored on the dome and was
    /// not applied.
    /// </summary>
    /// <remarks>
    /// Since ABI v21 a dome's receiver collection resolves into the per-draw dome
    /// mask, so this names the one case that cannot: a scene with more dome
    /// lights than the bounded dome table admits publishes no dome bits at all,
    /// and every dome then lights every prim.
    /// </remarks>
    LinkCollection = 4,

    /// <summary>
    /// A <c>collection:shadowLink</c> collection is authored on the dome and was
    /// not applied.
    /// </summary>
    /// <remarks>
    /// A dome shadow collection restricts which prims cast that dome's shadow,
    /// and no dome shadow pass exists: the raster shadow slice renders maps for
    /// direct lights only, and a dome has no light-space projection to render one
    /// from. It is named rather than reinterpreted as a receiver restriction,
    /// which is what applying it to the dome mask would silently have made it --
    /// darkening exactly the prims the author asked to keep lit.
    /// </remarks>
    ShadowCollection = 8
}

/// <summary>
/// The state of one entry of the ABI v21 frame dome table.
/// </summary>
[Flags]
internal enum SilkFrameDomeState : uint
{
    /// <summary>The entry is the zeroed tail of the fixed table.</summary>
    None = 0,

    /// <summary>The entry names a published dome light.</summary>
    Present = 1,

    /// <summary>
    /// The dome carries an authored texture and publishes an environment record
    /// instead of an ambient colour.
    /// </summary>
    Textured = 2
}

/// <summary>
/// One textured <c>UsdLuxDomeLight</c> published as scene-wide environment state.
/// </summary>
/// <remarks>
/// The frame ambient term is a single colour and cannot describe an image, so a
/// textured dome is carried here instead of being folded into it. The authored
/// emission controls arrive unmultiplied so a consumer can apply exactly the
/// terms it supports and diagnose the rest against the named prim.
/// </remarks>
public readonly ref struct SilkEnvironmentUpsertCommand
{
    private const int FixedSize = 200;

    /// <summary>
    /// The dome index a record carries when the page publishes no dome table.
    /// </summary>
    /// <remarks>
    /// Deliberately out of the bounded table's range, so it cannot be mistaken
    /// for dome bit zero. A dome that carries it lights every prim, and the loss
    /// of addressability is reported on the light-link command.
    /// </remarks>
    public const uint NoDomeIndex = 0xFFFFFFFF;

    private const SilkEnvironmentUnsupportedFeatures KnownUnsupported =
        SilkEnvironmentUnsupportedFeatures.ColorTemperature |
        SilkEnvironmentUnsupportedFeatures.PoleAxis |
        SilkEnvironmentUnsupportedFeatures.LinkCollection |
        SilkEnvironmentUnsupportedFeatures.ShadowCollection;

    private readonly ReadOnlySpan<byte> _bytes;
    private readonly string _path;
    private readonly string _texturePath;

    internal SilkEnvironmentUpsertCommand(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < FixedSize)
        {
            throw new InvalidDataException("The environment upsert command is truncated.");
        }
        uint pathLength = BinaryPrimitives.ReadUInt32LittleEndian(bytes[16..20]);
        uint textureLength = BinaryPrimitives.ReadUInt32LittleEndian(bytes[20..24]);
        if (pathLength is 0 or > int.MaxValue)
        {
            throw new InvalidDataException(
                "The environment upsert path must be present and within the managed page limit.");
        }

        // An environment record exists only because a dome carries an image. An
        // empty texture path would describe nothing the frame ambient term does
        // not already carry, so it is rejected rather than retained as an
        // environment that can never resolve.
        if (textureLength is 0 or > int.MaxValue)
        {
            throw new InvalidDataException(
                "The environment upsert texture path must be present and within " +
                "the managed page limit.");
        }
        long total = (long)FixedSize + pathLength + textureLength;
        if (bytes.Length != total)
        {
            throw new InvalidDataException(
                "The environment upsert size does not match its path lengths.");
        }
        if (BinaryPrimitives.ReadUInt32LittleEndian(bytes[24..28]) >
            (uint)SilkDomeTextureFormat.CubeMapVerticalCross)
        {
            throw new InvalidDataException(
                "The environment upsert texture format is unknown.");
        }
        if (BinaryPrimitives.ReadUInt32LittleEndian(bytes[28..32]) >
            (uint)SilkColorSpace.Srgb)
        {
            throw new InvalidDataException(
                "The environment upsert source colour space is unknown.");
        }
        uint domeIndex = BinaryPrimitives.ReadUInt32LittleEndian(bytes[36..40]);
        if (domeIndex != NoDomeIndex && domeIndex >= SilkFrameCommand.MaximumDomes)
        {
            throw new InvalidDataException(
                "The environment upsert dome index is outside the bounded dome table.");
        }
        if ((BinaryPrimitives.ReadUInt32LittleEndian(bytes[32..36]) &
            ~(uint)KnownUnsupported) != 0)
        {
            throw new InvalidDataException(
                "The environment upsert unsupported-feature bits are unknown.");
        }
        if (BinaryPrimitives.ReadUInt32LittleEndian(bytes[68..72]) != 0)
        {
            throw new InvalidDataException(
                "The environment upsert reserved field is not zero.");
        }

        // Every emission control and every transform element, checked before the
        // record is retained. A non-finite value does not fail loudly later: it
        // propagates through the prefilter into a NaN texel, and a NaN texel
        // poisons every filtered neighbourhood it touches, so the only symptom is
        // a sky with holes in it and nothing naming the cause.
        for (int offset = 40; offset < 68; offset += sizeof(float))
        {
            if (!float.IsFinite(BinaryPrimitives.ReadSingleLittleEndian(
                bytes.Slice(offset, sizeof(float)))))
            {
                throw new InvalidDataException(
                    "An environment upsert emission control is not finite.");
            }
        }
        for (int element = 0; element < 16; element++)
        {
            if (!double.IsFinite(BinaryPrimitives.ReadDoubleLittleEndian(
                bytes.Slice(72 + (element * sizeof(double)), sizeof(double)))))
            {
                throw new InvalidDataException(
                    "An environment upsert transform element is not finite.");
            }
        }
        _path = SilkWireFormat.DecodePath(bytes.Slice(FixedSize, (int)pathLength));
        _texturePath = SilkWireFormat.DecodeText(
            bytes.Slice(FixedSize + (int)pathLength, (int)textureLength));
        _bytes = bytes;
    }

    /// <summary>Gets the FNV-1a path hash used only as an identity index.</summary>
    public ulong StableHash => BinaryPrimitives.ReadUInt64LittleEndian(_bytes[8..16]);

    /// <summary>Gets the dome light's authoritative USD prim path.</summary>
    public string Path => _path;

    /// <summary>Gets the resolved <c>texture:file</c> asset path.</summary>
    public string TexturePath => _texturePath;

    /// <summary>Gets the declared image mapping.</summary>
    public SilkDomeTextureFormat TextureFormat =>
        (SilkDomeTextureFormat)BinaryPrimitives.ReadUInt32LittleEndian(_bytes[24..28]);

    /// <summary>Gets the declared source colour space of the image.</summary>
    public SilkColorSpace SourceColorSpace =>
        (SilkColorSpace)BinaryPrimitives.ReadUInt32LittleEndian(_bytes[28..32]);

    /// <summary>Gets the authored dome behaviour that did not reach the wire.</summary>
    public SilkEnvironmentUnsupportedFeatures UnsupportedFeatures =>
        (SilkEnvironmentUnsupportedFeatures)BinaryPrimitives.ReadUInt32LittleEndian(
            _bytes[32..36]);

    /// <summary>
    /// Gets this dome's entry in the frame dome table, which is the bit a
    /// per-prim dome link mask sets for it.
    /// </summary>
    /// <remarks>
    /// <see cref="NoDomeIndex"/> when the page publishes no dome table, in which
    /// case the dome lights every prim.
    /// </remarks>
    public uint DomeIndex => BinaryPrimitives.ReadUInt32LittleEndian(_bytes[36..40]);

    /// <summary>Gets one component of the authored emission colour.</summary>
    public float GetColor(int component)
    {
        if ((uint)component >= 3)
        {
            throw new ArgumentOutOfRangeException(nameof(component));
        }
        return BinaryPrimitives.ReadSingleLittleEndian(
            _bytes.Slice(40 + (component * sizeof(float)), sizeof(float)));
    }

    /// <summary>Gets the authored <c>inputs:intensity</c>.</summary>
    public float Intensity => BinaryPrimitives.ReadSingleLittleEndian(_bytes[52..56]);

    /// <summary>Gets the authored <c>inputs:exposure</c> in stops.</summary>
    public float Exposure => BinaryPrimitives.ReadSingleLittleEndian(_bytes[56..60]);

    /// <summary>Gets the authored <c>inputs:diffuse</c> contribution scale.</summary>
    public float Diffuse => BinaryPrimitives.ReadSingleLittleEndian(_bytes[60..64]);

    /// <summary>Gets the authored <c>inputs:specular</c> contribution scale.</summary>
    public float Specular => BinaryPrimitives.ReadSingleLittleEndian(_bytes[64..68]);

    /// <summary>Gets an element from the row-major light-to-world transform.</summary>
    public double GetTransformElement(int index)
    {
        if ((uint)index >= 16)
        {
            throw new ArgumentOutOfRangeException(nameof(index));
        }
        return BinaryPrimitives.ReadDoubleLittleEndian(
            _bytes.Slice(72 + (index * sizeof(double)), sizeof(double)));
    }
}

/// <summary>
/// A removal command for one dome-light environment.
/// </summary>
public readonly ref struct SilkEnvironmentRemoveCommand
{
    private const int FixedSize = 20;
    private readonly ReadOnlySpan<byte> _bytes;
    private readonly string _path;

    internal SilkEnvironmentRemoveCommand(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < FixedSize)
        {
            throw new InvalidDataException("The environment removal command is truncated.");
        }
        uint pathLength = BinaryPrimitives.ReadUInt32LittleEndian(bytes[16..20]);
        if (pathLength is 0 or > int.MaxValue)
        {
            throw new InvalidDataException(
                "The environment removal path must be present and within the managed page limit.");
        }
        if (bytes.Length != FixedSize + (int)pathLength)
        {
            throw new InvalidDataException(
                "The environment removal size does not match its path length.");
        }
        _path = SilkWireFormat.DecodePath(bytes.Slice(FixedSize, (int)pathLength));
        _bytes = bytes;
    }

    /// <summary>Gets the FNV-1a path hash used only as an identity index.</summary>
    public ulong StableHash => BinaryPrimitives.ReadUInt64LittleEndian(_bytes[8..16]);

    /// <summary>Gets the removed dome light's USD prim path.</summary>
    public string Path => _path;
}

/// <summary>Reports link-table state hdSilk could not fully express.</summary>
[Flags]
public enum SilkLightLinkUnsupportedFeatures : uint
{
    /// <summary>The published table describes every prim it resolved.</summary>
    None = 0,

    /// <summary>
    /// The table exceeded the page budget, so prims that did not fit stay linked
    /// to every light.
    /// </summary>
    Truncated = 1,

    /// <summary>
    /// The scene authors more dome lights than the bounded dome table admits, so
    /// no dome is individually addressable and every dome lights every prim.
    /// </summary>
    /// <remarks>
    /// All-or-nothing on purpose. Publishing the domes that fit would make some
    /// of a scene's skies maskable and the rest not, and no consumer-side sum of
    /// the two halves is the authored image; an unpublished table degrades
    /// exactly to the pre-v21 result and names the loss.
    /// </remarks>
    DomeBudget = 2
}

/// <summary>
/// One resolved light, shadow and dome link entry for a prim, or for one
/// instance of a prim.
/// </summary>
/// <param name="Path">The prim's authoritative USD path.</param>
/// <param name="InstanceIndex">
/// <see cref="SilkLightLinkCommand.AllInstances"/> when the entry applies to
/// every published instance of the path.
/// </param>
/// <param name="LightMask">
/// Bit <c>i</c> is set when direct light <c>i</c> of the frame light table
/// illuminates the prim.
/// </param>
/// <param name="ShadowMask">
/// Bit <c>i</c> is set when the prim casts direct light <c>i</c>'s shadow. This
/// is independent of <paramref name="LightMask"/>: UsdLux resolves
/// <c>collection:lightLink</c> and <c>collection:shadowLink</c> separately, so a
/// prim that occludes a light without being lit by it is a valid combination.
/// </param>
/// <param name="DomeMask">
/// Bit <c>i</c> is set when dome light <c>i</c> of the frame dome table
/// illuminates the prim. It is a third bit space rather than more bits of
/// <paramref name="LightMask"/> because the two tables are two orderings: direct
/// light 0 and dome 0 are different lights. There is no dome shadow mask,
/// because no dome shadow pass exists to restrict.
/// </param>
public readonly record struct SilkLightLinkEntry(
    string Path,
    int InstanceIndex,
    uint LightMask,
    uint ShadowMask,
    uint DomeMask);

/// <summary>
/// The sparse UsdLux light, shadow and dome link table for one page.
/// </summary>
/// <remarks>
/// The table is a complete replacement, and it omits every prim whose masks are
/// the default of "every light links". A consumer that has never seen the
/// command therefore lights every prim with every light, which is the behaviour
/// of every page ABI before 18.
/// </remarks>
public readonly ref struct SilkLightLinkCommand
{
    private const int FixedSize = 24;
    private const int EntryFixedSize = 20;
    private const SilkLightLinkUnsupportedFeatures KnownUnsupported =
        SilkLightLinkUnsupportedFeatures.Truncated |
        SilkLightLinkUnsupportedFeatures.DomeBudget;
    private readonly ReadOnlySpan<byte> _bytes;

    /// <summary>Gets the instance index that means "every instance of the path".</summary>
    public const int AllInstances = -1;

    internal SilkLightLinkCommand(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < FixedSize)
        {
            throw new InvalidDataException("The light link command is truncated.");
        }
        uint entryCount = BinaryPrimitives.ReadUInt32LittleEndian(bytes[8..12]);
        uint lightCount = BinaryPrimitives.ReadUInt32LittleEndian(bytes[12..16]);
        if (lightCount > SilkFrameCommand.MaximumLights)
        {
            throw new InvalidDataException(
                "The light link table indexes more lights than a frame publishes.");
        }
        if ((BinaryPrimitives.ReadUInt32LittleEndian(bytes[16..20]) &
            ~(uint)KnownUnsupported) != 0)
        {
            throw new InvalidDataException("The light link unsupported-feature bits are unknown.");
        }
        uint domeCount = BinaryPrimitives.ReadUInt32LittleEndian(bytes[20..24]);
        if (domeCount > SilkFrameCommand.MaximumDomes)
        {
            throw new InvalidDataException(
                "The light link table indexes more domes than a frame publishes.");
        }
        if (entryCount > MaximumEntries)
        {
            throw new InvalidDataException(
                "The light link table exceeds the page entry budget.");
        }

        // Walk the variable-length entries once here so that a malformed table is
        // rejected whole rather than part way through being applied, and so that
        // every later read is a bounds-checked slice of an already valid span.
        int offset = FixedSize;
        uint maskLimit = lightCount >= 32 ? uint.MaxValue : (1u << (int)lightCount) - 1;
        uint domeLimit = domeCount >= 32 ? uint.MaxValue : (1u << (int)domeCount) - 1;
        for (uint index = 0; index < entryCount; index++)
        {
            if (bytes.Length - offset < EntryFixedSize)
            {
                throw new InvalidDataException("A light link entry is truncated.");
            }
            uint lightMask = BinaryPrimitives.ReadUInt32LittleEndian(
                bytes.Slice(offset, sizeof(uint)));
            uint shadowMask = BinaryPrimitives.ReadUInt32LittleEndian(
                bytes.Slice(offset + 4, sizeof(uint)));
            if ((lightMask | shadowMask) > maskLimit)
            {
                throw new InvalidDataException(
                    "A light link entry names a light the frame table does not publish.");
            }
            uint domeMask = BinaryPrimitives.ReadUInt32LittleEndian(
                bytes.Slice(offset + 8, sizeof(uint)));
            if (domeMask > domeLimit)
            {
                throw new InvalidDataException(
                    "A light link entry names a dome the frame table does not publish.");
            }

            // The masks are deliberately not intersected. UsdLux defines
            // collection:lightLink and collection:shadowLink as separate
            // collections over the same light, so a prim that casts a light's
            // shadow without being lit by it -- an unlit or off-screen blocker --
            // is a valid, publishable combination rather than a malformed one.
            uint pathLength = BinaryPrimitives.ReadUInt32LittleEndian(
                bytes.Slice(offset + 16, sizeof(uint)));
            if (BinaryPrimitives.ReadInt32LittleEndian(bytes.Slice(offset + 12, sizeof(int))) <
                AllInstances)
            {
                throw new InvalidDataException(
                    "A light link entry names a negative instance other than every instance.");
            }
            if (pathLength is 0 or > int.MaxValue ||
                bytes.Length - offset - EntryFixedSize < pathLength)
            {
                throw new InvalidDataException(
                    "A light link entry path must be present and within the page.");
            }
            offset += EntryFixedSize + (int)pathLength;
        }
        if (offset != bytes.Length)
        {
            throw new InvalidDataException(
                "The light link size does not match its entry table.");
        }

        _bytes = bytes;
    }

    /// <summary>Gets the page budget for one light link table.</summary>
    public const uint MaximumEntries = 4096;

    /// <summary>Gets the number of published entries.</summary>
    public uint EntryCount => BinaryPrimitives.ReadUInt32LittleEndian(_bytes[8..12]);

    /// <summary>Gets the number of direct lights the masks index.</summary>
    public uint LightCount => BinaryPrimitives.ReadUInt32LittleEndian(_bytes[12..16]);

    /// <summary>Gets link state hdSilk could not fully express.</summary>
    public SilkLightLinkUnsupportedFeatures UnsupportedFeatures =>
        (SilkLightLinkUnsupportedFeatures)BinaryPrimitives.ReadUInt32LittleEndian(_bytes[16..20]);

    /// <summary>Gets the number of dome lights the dome masks index.</summary>
    public uint DomeCount => BinaryPrimitives.ReadUInt32LittleEndian(_bytes[20..24]);

    /// <summary>Creates a forward enumerator over the entry table.</summary>
    public Enumerator GetEnumerator() => new(_bytes);

    /// <summary>Walks the variable-length entry table without allocating.</summary>
    public ref struct Enumerator
    {
        private readonly ReadOnlySpan<byte> _bytes;
        private int _offset;
        private uint _remaining;

        internal Enumerator(ReadOnlySpan<byte> bytes)
        {
            _bytes = bytes;
            _offset = FixedSize;
            _remaining = BinaryPrimitives.ReadUInt32LittleEndian(bytes[8..12]);
            Current = default;
        }

        /// <summary>Gets the current entry.</summary>
        public SilkLightLinkEntry Current { get; private set; }

        /// <summary>Advances to the next entry.</summary>
        public bool MoveNext()
        {
            if (_remaining == 0)
            {
                return false;
            }

            uint lightMask = BinaryPrimitives.ReadUInt32LittleEndian(
                _bytes.Slice(_offset, sizeof(uint)));
            uint shadowMask = BinaryPrimitives.ReadUInt32LittleEndian(
                _bytes.Slice(_offset + 4, sizeof(uint)));
            uint domeMask = BinaryPrimitives.ReadUInt32LittleEndian(
                _bytes.Slice(_offset + 8, sizeof(uint)));
            int instanceIndex = BinaryPrimitives.ReadInt32LittleEndian(
                _bytes.Slice(_offset + 12, sizeof(int)));
            int pathLength = (int)BinaryPrimitives.ReadUInt32LittleEndian(
                _bytes.Slice(_offset + 16, sizeof(uint)));
            Current = new SilkLightLinkEntry(
                SilkWireFormat.DecodePath(
                    _bytes.Slice(_offset + EntryFixedSize, pathLength)),
                instanceIndex,
                lightMask,
                shadowMask,
                domeMask);
            _offset += EntryFixedSize + pathLength;
            _remaining--;
            return true;
        }
    }
}

/// <summary>Reports shadow state hdSilk could not put on the wire.</summary>
[Flags]
public enum SilkShadowUnsupportedFeatures : uint
{
    /// <summary>Every shadow-enabled light has a published descriptor.</summary>
    None = 0,

    /// <summary>
    /// A shadow-enabled direct light is not a distant light. Only a distant
    /// light has an exact light-space projection here.
    /// </summary>
    LightType = 1,

    /// <summary>
    /// More lights asked for a shadow map than the page budget allows, so the
    /// lights that did not fit are published without occlusion.
    /// </summary>
    MapBudget = 2,

    /// <summary>
    /// A light asked for a shadow map but the published geometry has no world
    /// extent to derive a light-space projection from.
    /// </summary>
    NoCasters = 4
}

/// <summary>
/// One bounded raster shadow-map descriptor.
/// </summary>
/// <param name="LightIndex">The frame light table index this map belongs to.</param>
/// <param name="MapIndex">The map's identity within the published table.</param>
/// <param name="Resolution">The square shadow-map edge length in texels.</param>
/// <param name="Flags">Descriptor flags declared by the page ABI.</param>
/// <param name="DepthBias">
/// The producer's constant depth bias, in the map's own normalized [0,1] depth.
/// </param>
/// <param name="NormalBias">
/// The producer's receiver offset along the shading normal, in world units.
/// </param>
/// <param name="PcfRadius">The producer's filter radius in shadow-map texels.</param>
public readonly record struct SilkShadowDescriptor(
    uint LightIndex,
    uint MapIndex,
    uint Resolution,
    SilkShadowDescriptorOptions Flags,
    float DepthBias,
    float NormalBias,
    float PcfRadius)
{
    /// <summary>Gets the row-major world-to-light view matrix.</summary>
    public required double[] View { get; init; }

    /// <summary>
    /// Gets the row-major light-space projection, in OpenGL [-w, +w] clip depth.
    /// </summary>
    public required double[] Projection { get; init; }
}

/// <summary>Flags a published shadow descriptor may carry.</summary>
[Flags]
public enum SilkShadowDescriptorOptions : uint
{
    /// <summary>No flags.</summary>
    None = 0,

    /// <summary>The projection has no perspective divide.</summary>
    Orthographic = 1,

    /// <summary>
    /// At least one published prim is excluded from this light's caster
    /// collection, so ignoring the link table's shadow mask renders a knowably
    /// different image.
    /// </summary>
    CasterLinked = 2
}

/// <summary>
/// The bounded raster shadow-map descriptor table for one page.
/// </summary>
/// <remarks>
/// The table is a complete replacement. A page whose descriptors are unchanged
/// publishes no command at all, which is exactly how a consumer knows a retained
/// shadow map is still the one these lights and these caster bounds produced. A
/// consumer that has never seen the command casts no shadows, which is the
/// behaviour of every page ABI before 19.
/// </remarks>
public readonly ref struct SilkShadowCommand
{
    private const int FixedSize = 24;
    private const int DescriptorSize = 288;
    private const int ViewOffset = 16;
    private const int ProjectionOffset = 144;
    private readonly ReadOnlySpan<byte> _bytes;

    /// <summary>Gets the page budget for one shadow table.</summary>
    public const uint MaximumMaps = 4;

    /// <summary>Gets the smallest square shadow-map edge length.</summary>
    public const uint MinimumResolution = 256;

    /// <summary>Gets the largest square shadow-map edge length.</summary>
    public const uint MaximumResolution = 2048;

    private const SilkShadowDescriptorOptions KnownFlags =
        SilkShadowDescriptorOptions.Orthographic | SilkShadowDescriptorOptions.CasterLinked;

    private const SilkShadowUnsupportedFeatures KnownUnsupported =
        SilkShadowUnsupportedFeatures.LightType |
        SilkShadowUnsupportedFeatures.MapBudget |
        SilkShadowUnsupportedFeatures.NoCasters;

    internal SilkShadowCommand(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < FixedSize)
        {
            throw new InvalidDataException("The shadow command is truncated.");
        }
        uint descriptorCount = BinaryPrimitives.ReadUInt32LittleEndian(bytes[8..12]);
        uint lightCount = BinaryPrimitives.ReadUInt32LittleEndian(bytes[12..16]);
        if (lightCount > SilkFrameCommand.MaximumLights)
        {
            throw new InvalidDataException(
                "The shadow table indexes more lights than a frame publishes.");
        }
        if ((BinaryPrimitives.ReadUInt32LittleEndian(bytes[16..20]) &
            ~(uint)KnownUnsupported) != 0)
        {
            throw new InvalidDataException("The shadow unsupported-feature bits are unknown.");
        }
        if (descriptorCount > MaximumMaps)
        {
            throw new InvalidDataException("The shadow table exceeds the page map budget.");
        }
        if (BinaryPrimitives.ReadUInt32LittleEndian(bytes[20..24]) != 0)
        {
            throw new InvalidDataException("The shadow header reserved field is not zero.");
        }
        if (bytes.Length != FixedSize + (descriptorCount * DescriptorSize))
        {
            throw new InvalidDataException(
                "The shadow size does not match its descriptor table.");
        }

        // Validated whole here so that a malformed table is rejected before any
        // of it is applied, and so every later read is a bounds-checked slice of
        // an already valid span.
        for (uint index = 0; index < descriptorCount; index++)
        {
            ReadOnlySpan<byte> descriptor =
                bytes.Slice(FixedSize + ((int)index * DescriptorSize), DescriptorSize);
            uint lightIndex = BinaryPrimitives.ReadUInt32LittleEndian(descriptor[..4]);
            if (lightIndex >= lightCount)
            {
                throw new InvalidDataException(
                    "A shadow descriptor names a light the frame table does not publish.");
            }
            if (BinaryPrimitives.ReadUInt32LittleEndian(descriptor[4..8]) != index)
            {
                throw new InvalidDataException(
                    "Shadow descriptor map indices must ascend from zero without gaps.");
            }
            uint resolution = BinaryPrimitives.ReadUInt32LittleEndian(descriptor[8..12]);
            if (resolution < MinimumResolution ||
                resolution > MaximumResolution ||
                (resolution & (resolution - 1)) != 0)
            {
                throw new InvalidDataException(
                    "A shadow descriptor resolution must be a power of two within the ABI bounds.");
            }
            if ((BinaryPrimitives.ReadUInt32LittleEndian(descriptor[12..16]) &
                ~(uint)KnownFlags) != 0)
            {
                throw new InvalidDataException("A shadow descriptor flag is unknown.");
            }
            for (int element = 0; element < 32; element++)
            {
                double value = BinaryPrimitives.ReadDoubleLittleEndian(
                    descriptor.Slice(ViewOffset + (element * sizeof(double)), sizeof(double)));
                if (!double.IsFinite(value))
                {
                    throw new InvalidDataException(
                        "A shadow descriptor matrix element is not finite.");
                }
            }
            for (int element = 0; element < 3; element++)
            {
                float value = BinaryPrimitives.ReadSingleLittleEndian(
                    descriptor.Slice(272 + (element * sizeof(float)), sizeof(float)));
                if (!float.IsFinite(value) || value < 0)
                {
                    throw new InvalidDataException(
                        "A shadow descriptor bias or filter radius must be finite and non-negative.");
                }
            }
            if (BinaryPrimitives.ReadUInt32LittleEndian(descriptor[284..288]) != 0)
            {
                throw new InvalidDataException("A shadow descriptor reserved field is not zero.");
            }
        }

        _bytes = bytes;
    }

    /// <summary>Gets the number of published shadow maps.</summary>
    public uint DescriptorCount => BinaryPrimitives.ReadUInt32LittleEndian(_bytes[8..12]);

    /// <summary>Gets the number of direct lights the descriptors index.</summary>
    public uint LightCount => BinaryPrimitives.ReadUInt32LittleEndian(_bytes[12..16]);

    /// <summary>Gets shadow state hdSilk could not put on the wire.</summary>
    public SilkShadowUnsupportedFeatures UnsupportedFeatures =>
        (SilkShadowUnsupportedFeatures)BinaryPrimitives.ReadUInt32LittleEndian(_bytes[16..20]);

    /// <summary>Reads one published descriptor.</summary>
    /// <param name="index">The zero-based descriptor index.</param>
    /// <returns>The descriptor at <paramref name="index"/>.</returns>
    public SilkShadowDescriptor GetDescriptor(uint index)
    {
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, DescriptorCount);
        ReadOnlySpan<byte> descriptor =
            _bytes.Slice(FixedSize + ((int)index * DescriptorSize), DescriptorSize);
        double[] view = new double[16];
        double[] projection = new double[16];
        for (int element = 0; element < 16; element++)
        {
            view[element] = BinaryPrimitives.ReadDoubleLittleEndian(
                descriptor.Slice(ViewOffset + (element * sizeof(double)), sizeof(double)));
            projection[element] = BinaryPrimitives.ReadDoubleLittleEndian(
                descriptor.Slice(
                    ProjectionOffset + (element * sizeof(double)),
                    sizeof(double)));
        }
        return new SilkShadowDescriptor(
            BinaryPrimitives.ReadUInt32LittleEndian(descriptor[..4]),
            BinaryPrimitives.ReadUInt32LittleEndian(descriptor[4..8]),
            BinaryPrimitives.ReadUInt32LittleEndian(descriptor[8..12]),
            (SilkShadowDescriptorOptions)BinaryPrimitives.ReadUInt32LittleEndian(descriptor[12..16]),
            BinaryPrimitives.ReadSingleLittleEndian(descriptor[272..276]),
            BinaryPrimitives.ReadSingleLittleEndian(descriptor[276..280]),
            BinaryPrimitives.ReadSingleLittleEndian(descriptor[280..284]))
        {
            View = view,
            Projection = projection,
        };
    }
}

/// <summary>Describes the pointer-free topology emitted by hdSilk.</summary>
public enum SilkTopologyKind : uint
{
    /// <summary>Three indices and one authored face mapping per triangle.</summary>
    TriangleList = 1,

    /// <summary>Two indices and one authored segment mapping per line.</summary>
    LineList = 2,

    /// <summary>One index and one authored point mapping per point.</summary>
    PointList = 3
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
