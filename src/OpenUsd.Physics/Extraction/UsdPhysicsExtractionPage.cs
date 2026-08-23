// Copyright (c) marcschier. Licensed under the MIT License.

using System.Buffers.Binary;
using System.Text;

namespace OpenUsd.Physics.Extraction;

/// <summary>
/// One immutable, pointer-free physics extraction page produced by a single stage traversal.
/// </summary>
/// <remarks>
/// A page owns a private copy of the extraction bytes and never refers to the stage it came
/// from. Reading it is therefore safe from any thread at any time, including long after the
/// stage was disposed.
/// </remarks>
public sealed class UsdPhysicsExtractionPage : IUsdDetachedResult
{
    private readonly byte[] _bytes;
    private readonly int[] _offsets = new int[PhysicsExtractAbi.SectionCount];
    private readonly int[] _counts = new int[PhysicsExtractAbi.SectionCount];

    private UsdPhysicsExtractionPage(byte[] bytes)
    {
        _bytes = bytes;
        for (int section = 0; section < PhysicsExtractAbi.SectionCount; section++)
        {
            int span = PhysicsExtractAbi.OffsetSpans + (section * 8);
            _offsets[section] = (int)ReadUInt32(bytes, span);
            _counts[section] = (int)ReadUInt32(bytes, span + 4);
        }
    }

    /// <summary>Gets the number of bytes in the page.</summary>
    public int ByteSize => _bytes.Length;

    /// <summary>Gets the low half of the deterministic content fingerprint.</summary>
    public ulong FingerprintLow => ReadUInt64(_bytes, PhysicsExtractAbi.OffsetFingerprintLow);

    /// <summary>Gets the high half of the deterministic content fingerprint.</summary>
    public ulong FingerprintHigh => ReadUInt64(_bytes, PhysicsExtractAbi.OffsetFingerprintHigh);

    /// <summary>Gets the stage metersPerUnit the page was extracted with.</summary>
    public double MetersPerUnit => ReadDouble(_bytes, PhysicsExtractAbi.OffsetMetersPerUnit);

    /// <summary>Gets the stage kilogramsPerUnit the page was extracted with.</summary>
    public double KilogramsPerUnit => ReadDouble(_bytes, PhysicsExtractAbi.OffsetKilogramsPerUnit);

    /// <summary>Gets the stage timeCodesPerSecond the page was extracted with.</summary>
    public double TimeCodesPerSecond =>
        ReadDouble(_bytes, PhysicsExtractAbi.OffsetTimeCodesPerSecond);

    /// <summary>Gets the authored start time code, or zero when none was authored.</summary>
    public double StartTimeCode => ReadDouble(_bytes, PhysicsExtractAbi.OffsetStartTimeCode);

    /// <summary>Gets the authored end time code, or zero when none was authored.</summary>
    public double EndTimeCode => ReadDouble(_bytes, PhysicsExtractAbi.OffsetEndTimeCode);

    /// <summary>Gets the time code the stage was sampled at.</summary>
    public double TimeCode => ReadDouble(_bytes, PhysicsExtractAbi.OffsetTimeCode);

    /// <summary>Gets the stage up axis the page was extracted from.</summary>
    public UsdPhysicsExtractionUpAxis UpAxis =>
        (UsdPhysicsExtractionUpAxis)ReadUInt32(_bytes, PhysicsExtractAbi.OffsetUpAxis);

    /// <summary>Gets the stage-wide observations recorded during extraction.</summary>
    public UsdPhysicsExtractionPageTraits Flags =>
        (UsdPhysicsExtractionPageTraits)ReadUInt32(_bytes, PhysicsExtractAbi.OffsetFlags);

    /// <summary>Gets the per-section truncation mask, which is zero for a complete page.</summary>
    public uint TruncationFlags => ReadUInt32(_bytes, PhysicsExtractAbi.OffsetTruncationFlags);

    /// <summary>Gets the object index of the default simulation scene, or <c>-1</c>.</summary>
    public int DefaultSceneIndex => ReadInt32(_bytes, PhysicsExtractAbi.OffsetDefaultSceneIndex);

    /// <summary>Gets the number of extracted objects.</summary>
    public int ObjectCount => _counts[PhysicsExtractAbi.SectionObjects];

    /// <summary>Gets the number of resolved properties across every object.</summary>
    public int PropertyCount => _counts[PhysicsExtractAbi.SectionProperties];

    /// <summary>Gets the number of resolved relationships across every object.</summary>
    public int RelationshipCount => _counts[PhysicsExtractAbi.SectionRelationships];

    /// <summary>Gets the number of relationship targets across every object.</summary>
    public int TargetCount => _counts[PhysicsExtractAbi.SectionTargets];

    /// <summary>Gets the number of shared numbers.</summary>
    public int NumberCount => _counts[PhysicsExtractAbi.SectionNumbers];

    /// <summary>Gets the number of shared texts.</summary>
    public int TextCount => _counts[PhysicsExtractAbi.SectionTexts];

    /// <summary>Gets the number of collider points across every object.</summary>
    public int PointCount => _counts[PhysicsExtractAbi.SectionPoints];

    /// <summary>Gets the number of collider triangle indices across every object.</summary>
    public int IndexCount => _counts[PhysicsExtractAbi.SectionIndices];

    /// <summary>Gets the number of ordered diagnostics.</summary>
    public int DiagnosticCount => _counts[PhysicsExtractAbi.SectionDiagnostics];

    /// <summary>Validates the bytes and wraps a defensive copy as an immutable page.</summary>
    /// <param name="bytes">The page bytes.</param>
    /// <returns>The validated page.</returns>
    /// <exception cref="UsdPhysicsExtractionException">The bytes are not a usable page.</exception>
    public static UsdPhysicsExtractionPage Create(ReadOnlySpan<byte> bytes)
    {
        UsdPhysicsExtractionPageValidator.Validate(bytes);
        return new UsdPhysicsExtractionPage(bytes.ToArray());
    }

    /// <summary>Copies the raw page bytes.</summary>
    /// <returns>A fresh copy of the page bytes.</returns>
    public byte[] ToArray() => (byte[])_bytes.Clone();

    /// <summary>Reads one extracted object.</summary>
    /// <param name="index">The zero based object index.</param>
    /// <returns>A lightweight view of the object.</returns>
    public UsdPhysicsExtractionObject GetObject(int index)
    {
        ValidateIndex(PhysicsExtractAbi.SectionObjects, index);
        return new UsdPhysicsExtractionObject(this, index);
    }

    /// <summary>Reads one resolved property.</summary>
    /// <param name="index">The zero based property index.</param>
    /// <returns>A lightweight view of the property.</returns>
    public UsdPhysicsExtractionProperty GetProperty(int index)
    {
        ValidateIndex(PhysicsExtractAbi.SectionProperties, index);
        return new UsdPhysicsExtractionProperty(this, index);
    }

    /// <summary>Reads one resolved relationship.</summary>
    /// <param name="index">The zero based relationship index.</param>
    /// <returns>A lightweight view of the relationship.</returns>
    public UsdPhysicsExtractionRelationship GetRelationship(int index)
    {
        ValidateIndex(PhysicsExtractAbi.SectionRelationships, index);
        return new UsdPhysicsExtractionRelationship(this, index);
    }

    /// <summary>Reads one relationship target.</summary>
    /// <param name="index">The zero based target index.</param>
    /// <returns>A lightweight view of the target.</returns>
    public UsdPhysicsExtractionTarget GetTarget(int index)
    {
        ValidateIndex(PhysicsExtractAbi.SectionTargets, index);
        return new UsdPhysicsExtractionTarget(this, index);
    }

    /// <summary>Reads one diagnostic.</summary>
    /// <param name="index">The zero based diagnostic index.</param>
    /// <returns>A lightweight view of the diagnostic.</returns>
    public UsdPhysicsExtractionDiagnostic GetDiagnostic(int index)
    {
        ValidateIndex(PhysicsExtractAbi.SectionDiagnostics, index);
        return new UsdPhysicsExtractionDiagnostic(this, index);
    }

    /// <summary>Reads one number from the shared number section.</summary>
    /// <param name="index">The zero based number index.</param>
    /// <returns>The decoded number.</returns>
    public double GetNumber(int index) =>
        ReadDouble(_bytes, RecordOffset(PhysicsExtractAbi.SectionNumbers, index));

    /// <summary>Reads one text from the shared text section.</summary>
    /// <param name="index">The zero based text index.</param>
    /// <returns>The decoded text.</returns>
    public string GetText(int index) =>
        ReadString(ReadUInt32(_bytes, RecordOffset(PhysicsExtractAbi.SectionTexts, index)));

    /// <summary>Reads one collider point in the local space of its object.</summary>
    /// <param name="index">The zero based point index.</param>
    /// <returns>The decoded point components.</returns>
    public (float X, float Y, float Z) GetPoint(int index)
    {
        int at = RecordOffset(PhysicsExtractAbi.SectionPoints, index);
        return (
            BitConverter.Int32BitsToSingle(ReadInt32(_bytes, at)),
            BitConverter.Int32BitsToSingle(ReadInt32(_bytes, at + 4)),
            BitConverter.Int32BitsToSingle(ReadInt32(_bytes, at + 8)));
    }

    /// <summary>Reads one collider triangle index.</summary>
    /// <param name="index">The zero based index slot.</param>
    /// <returns>The decoded point index.</returns>
    public int GetIndex(int index) =>
        (int)ReadUInt32(_bytes, RecordOffset(PhysicsExtractAbi.SectionIndices, index));

    /// <summary>Wraps already owned page bytes without copying them again.</summary>
    /// <param name="bytes">The owned page bytes.</param>
    /// <returns>The validated page.</returns>
    internal static UsdPhysicsExtractionPage Adopt(byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        UsdPhysicsExtractionPageValidator.Validate(bytes);
        return new UsdPhysicsExtractionPage(bytes);
    }

    internal uint FieldU32(int section, int index, int fieldOffset) =>
        ReadUInt32(_bytes, RecordOffset(section, index) + fieldOffset);

    internal int FieldI32(int section, int index, int fieldOffset) =>
        ReadInt32(_bytes, RecordOffset(section, index) + fieldOffset);

    internal ulong FieldU64(int section, int index, int fieldOffset) =>
        ReadUInt64(_bytes, RecordOffset(section, index) + fieldOffset);

    internal double FieldF64(int section, int index, int fieldOffset) =>
        ReadDouble(_bytes, RecordOffset(section, index) + fieldOffset);

    internal string FieldText(int section, int index, int fieldOffset) =>
        ReadString(ReadUInt32(_bytes, RecordOffset(section, index) + fieldOffset));

    private static uint ReadUInt32(ReadOnlySpan<byte> bytes, int offset) =>
        BinaryPrimitives.ReadUInt32LittleEndian(bytes[offset..]);

    private static int ReadInt32(ReadOnlySpan<byte> bytes, int offset) =>
        BinaryPrimitives.ReadInt32LittleEndian(bytes[offset..]);

    private static ulong ReadUInt64(ReadOnlySpan<byte> bytes, int offset) =>
        BinaryPrimitives.ReadUInt64LittleEndian(bytes[offset..]);

    private static double ReadDouble(ReadOnlySpan<byte> bytes, int offset) =>
        BitConverter.Int64BitsToDouble(BinaryPrimitives.ReadInt64LittleEndian(bytes[offset..]));

    private void ValidateIndex(int section, int index)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, _counts[section]);
    }

    private int RecordOffset(int section, int index)
    {
        ValidateIndex(section, index);
        return _offsets[section] + (index * PhysicsExtractAbi.RecordBytes(section));
    }

    private string ReadString(uint offset)
    {
        int stringCount = _counts[PhysicsExtractAbi.SectionStrings];
        if (offset == 0 || offset >= (uint)stringCount)
        {
            return string.Empty;
        }
        int start = _offsets[PhysicsExtractAbi.SectionStrings] + (int)offset;
        int limit = _offsets[PhysicsExtractAbi.SectionStrings] + stringCount;
        int end = start;
        while (end < limit && _bytes[end] != 0)
        {
            end++;
        }
        return Encoding.UTF8.GetString(_bytes, start, end - start);
    }
}
