// Copyright (c) marcschier. Licensed under the MIT License.

using System.Buffers.Binary;
using System.Text;
using OpenUsd.Physics.Extraction;

namespace OpenUsd.Physics.Tests.Extraction;

/// <summary>Builds byte exact extraction pages so the managed reader can be tested alone.</summary>
internal sealed class UsdPhysicsExtractionPageFixture
{
    private readonly List<byte> _strings = [0];
    private readonly List<byte[]> _objects = [];
    private readonly List<byte[]> _properties = [];
    private readonly List<byte[]> _relationships = [];
    private readonly List<byte[]> _targets = [];
    private readonly List<double> _numbers = [];
    private readonly List<(uint Offset, uint Length)> _texts = [];
    private readonly List<(float X, float Y, float Z)> _points = [];
    private readonly List<uint> _indices = [];
    private readonly List<byte[]> _diagnostics = [];

    internal double MetersPerUnit { get; set; } = 1.0;

    internal double KilogramsPerUnit { get; set; } = 1.0;

    internal double TimeCodesPerSecond { get; set; } = 24.0;

    internal double StartTimeCode { get; set; }

    internal double EndTimeCode { get; set; } = 1.0;

    internal double TimeCode { get; set; }

    internal uint UpAxis { get; set; }

    internal uint Flags { get; set; }

    internal int DefaultSceneIndex { get; set; } = -1;

    internal ulong FingerprintLow { get; set; } = 0x0123456789ABCDEFUL;

    internal ulong FingerprintHigh { get; set; } = 0xFEDCBA9876543210UL;

    internal uint AddString(string value)
    {
        var offset = (uint)_strings.Count;
        _strings.AddRange(Encoding.UTF8.GetBytes(value));
        _strings.Add(0);
        return offset;
    }

    internal int AddNumber(double value)
    {
        _numbers.Add(value);
        return _numbers.Count - 1;
    }

    internal int AddText(string value)
    {
        uint offset = AddString(value);
        _texts.Add((offset, (uint)Encoding.UTF8.GetByteCount(value)));
        return _texts.Count - 1;
    }

    internal int AddPoint(float x, float y, float z)
    {
        _points.Add((x, y, z));
        return _points.Count - 1;
    }

    internal int AddIndex(uint value)
    {
        _indices.Add(value);
        return _indices.Count - 1;
    }

    internal int AddObject(
        ulong id,
        string path,
        string name,
        UsdPhysicsExtractionObjectKind kind,
        UsdPhysicsExtractionDomains domains,
        UsdPhysicsExtractionObjectTraits traits,
        UsdPhysicsExtractionGeometryKind geometry = UsdPhysicsExtractionGeometryKind.None,
        string typeName = "")
    {
        var record = new byte[PhysicsExtractAbi.ObjectRecordBytes];
        WriteU64(record, 0, id);
        WriteU32(record, 24, AddString(path));
        WriteU32(record, 28, AddString(name));
        WriteU32(record, 32, AddString(typeName));
        WriteU32(record, 36, (uint)kind);
        WriteU32(record, 40, (uint)domains);
        WriteU32(record, 44, (uint)traits);
        WriteU32(record, 48, (uint)geometry);
        WriteI32(record, 52, -1);
        WriteI32(record, 56, -1);
        WriteF64(record, 152, 1.0);
        WriteF64(record, 160, 1.0);
        WriteF64(record, 168, 1.0);
        _objects.Add(record);
        return _objects.Count - 1;
    }

    internal int AddProperty(
        UsdPhysicsExtractionKey key,
        string name,
        UsdPhysicsExtractionValueKind valueKind,
        UsdPhysicsExtractionSource source,
        double scalar,
        int valueStart = 0,
        int valueCount = 0,
        UsdPhysicsExtractionPropertyTraits traits = UsdPhysicsExtractionPropertyTraits.None)
    {
        var record = new byte[PhysicsExtractAbi.PropertyRecordBytes];
        WriteU32(record, 0, (uint)key);
        WriteU32(record, 4, AddString(name));
        WriteU32(record, 8, (uint)valueKind);
        WriteU32(record, 12, (uint)traits);
        WriteU32(record, 16, (uint)source);
        WriteU32(record, 20, (uint)valueStart);
        WriteU32(record, 24, (uint)valueCount);
        WriteF64(record, 32, scalar);
        _properties.Add(record);
        return _properties.Count - 1;
    }

    internal int AddRelationship(
        UsdPhysicsExtractionKey key, string name, int targetStart, int targetCount)
    {
        var record = new byte[PhysicsExtractAbi.RelationshipRecordBytes];
        WriteU32(record, 0, (uint)key);
        WriteU32(record, 4, AddString(name));
        WriteU32(record, 8, (uint)targetStart);
        WriteU32(record, 12, (uint)targetCount);
        _relationships.Add(record);
        return _relationships.Count - 1;
    }

    internal int AddTarget(ulong id, string path, int objectIndex)
    {
        var record = new byte[PhysicsExtractAbi.TargetRecordBytes];
        WriteU64(record, 0, id);
        WriteU32(record, 8, AddString(path));
        WriteI32(record, 12, objectIndex);
        _targets.Add(record);
        return _targets.Count - 1;
    }

    internal int AddDiagnostic(
        UsdPhysicsExtractionSeverity severity,
        UsdPhysicsExtractionCategory category,
        UsdPhysicsExtractionCode code,
        int objectIndex,
        string message,
        ulong objectId = 0,
        UsdPhysicsExtractionKey key = UsdPhysicsExtractionKey.Unmapped)
    {
        var record = new byte[PhysicsExtractAbi.DiagnosticRecordBytes];
        WriteU32(record, 0, (uint)severity);
        WriteU32(record, 4, (uint)category);
        WriteU32(record, 8, (uint)code);
        WriteI32(record, 12, objectIndex);
        WriteU32(record, 16, AddString(message));
        WriteU32(record, 20, (uint)key);
        WriteU64(record, 24, objectId);
        _diagnostics.Add(record);
        return _diagnostics.Count - 1;
    }

    internal void SetObjectRange(
        int objectIndex,
        int propertyStart,
        int propertyCount,
        int relationshipStart,
        int relationshipCount)
    {
        byte[] record = _objects[objectIndex];
        WriteU32(record, 60, (uint)propertyStart);
        WriteU32(record, 64, (uint)propertyCount);
        WriteU32(record, 68, (uint)relationshipStart);
        WriteU32(record, 72, (uint)relationshipCount);
    }

    internal void SetObjectLinks(int objectIndex, int sceneIndex, int parentBodyIndex)
    {
        byte[] record = _objects[objectIndex];
        WriteI32(record, 52, sceneIndex);
        WriteI32(record, 56, parentBodyIndex);
    }

    internal void SetObjectGeometry(
        int objectIndex, int pointStart, int pointCount, int indexStart, int indexCount)
    {
        byte[] record = _objects[objectIndex];
        WriteU32(record, 76, (uint)pointStart);
        WriteU32(record, 80, (uint)pointCount);
        WriteU32(record, 84, (uint)indexStart);
        WriteU32(record, 88, (uint)indexCount);
    }

    internal void SetObjectScale(int objectIndex, (double X, double Y, double Z) scale)
    {
        byte[] record = _objects[objectIndex];
        WriteF64(record, 152, scale.X);
        WriteF64(record, 160, scale.Y);
        WriteF64(record, 168, scale.Z);
    }

    internal void SetObjectTransform(
        int objectIndex,
        (double X, double Y, double Z) position,
        (double W, double X, double Y, double Z) rotation,
        (double X, double Y, double Z) extent)
    {
        byte[] record = _objects[objectIndex];
        WriteF64(record, 96, position.X);
        WriteF64(record, 104, position.Y);
        WriteF64(record, 112, position.Z);
        WriteF64(record, 120, rotation.W);
        WriteF64(record, 128, rotation.X);
        WriteF64(record, 136, rotation.Y);
        WriteF64(record, 144, rotation.Z);
        WriteF64(record, 176, extent.X);
        WriteF64(record, 184, extent.Y);
        WriteF64(record, 192, extent.Z);
    }

    internal byte[] Build()
    {
        byte[][] payload =
        [
            [.. _strings],
            Concat(_objects),
            Concat(_properties),
            Concat(_relationships),
            Concat(_targets),
            Numbers(),
            Texts(),
            Points(),
            Indices(),
            Concat(_diagnostics),
        ];

        int[] counts =
        [
            _strings.Count,
            _objects.Count,
            _properties.Count,
            _relationships.Count,
            _targets.Count,
            _numbers.Count,
            _texts.Count,
            _points.Count,
            _indices.Count,
            _diagnostics.Count,
        ];

        var offsets = new int[PhysicsExtractAbi.SectionCount];
        int cursor = PhysicsExtractAbi.HeaderBytes;
        for (int section = 0; section < PhysicsExtractAbi.SectionCount; section++)
        {
            if (counts[section] == 0)
            {
                offsets[section] = 0;
                continue;
            }

            offsets[section] = cursor;
            cursor += Align(payload[section].Length);
        }

        var page = new byte[Align(cursor)];
        WriteU64(page, PhysicsExtractAbi.OffsetMagic, PhysicsExtractAbi.PageMagic);
        WriteU32(page, PhysicsExtractAbi.OffsetAbiVersion, PhysicsExtractAbi.AbiVersion);
        WriteU32(page, PhysicsExtractAbi.OffsetHeaderSize, (uint)PhysicsExtractAbi.HeaderBytes);
        WriteU64(page, PhysicsExtractAbi.OffsetByteSize, (ulong)page.Length);
        WriteU64(page, PhysicsExtractAbi.OffsetFingerprintLow, FingerprintLow);
        WriteU64(page, PhysicsExtractAbi.OffsetFingerprintHigh, FingerprintHigh);
        WriteF64(page, PhysicsExtractAbi.OffsetMetersPerUnit, MetersPerUnit);
        WriteF64(page, PhysicsExtractAbi.OffsetKilogramsPerUnit, KilogramsPerUnit);
        WriteF64(page, PhysicsExtractAbi.OffsetTimeCodesPerSecond, TimeCodesPerSecond);
        WriteF64(page, PhysicsExtractAbi.OffsetStartTimeCode, StartTimeCode);
        WriteF64(page, PhysicsExtractAbi.OffsetEndTimeCode, EndTimeCode);
        WriteF64(page, PhysicsExtractAbi.OffsetTimeCode, TimeCode);
        WriteU32(page, PhysicsExtractAbi.OffsetUpAxis, UpAxis);
        WriteU32(page, PhysicsExtractAbi.OffsetFlags, Flags);
        WriteI32(page, PhysicsExtractAbi.OffsetDefaultSceneIndex, DefaultSceneIndex);
        WriteU32(page, PhysicsExtractAbi.OffsetTruncationFlags, 0);
        for (int section = 0; section < PhysicsExtractAbi.SectionCount; section++)
        {
            int span = PhysicsExtractAbi.OffsetSpans + (section * 8);
            WriteU32(page, span, (uint)offsets[section]);
            WriteU32(page, span + 4, (uint)counts[section]);
            if (counts[section] != 0)
            {
                payload[section].CopyTo(page, offsets[section]);
            }
        }

        return page;
    }

    internal UsdPhysicsExtractionPage BuildPage() => UsdPhysicsExtractionPage.Create(Build());

    private static int Align(int value) => (value + 7) & ~7;

    private static byte[] Concat(List<byte[]> records)
    {
        var bytes = new List<byte>();
        foreach (byte[] record in records)
        {
            bytes.AddRange(record);
        }
        return [.. bytes];
    }

    private byte[] Numbers()
    {
        var bytes = new byte[_numbers.Count * 8];
        for (int index = 0; index < _numbers.Count; index++)
        {
            WriteF64(bytes, index * 8, _numbers[index]);
        }
        return bytes;
    }

    private byte[] Texts()
    {
        var bytes = new byte[_texts.Count * 8];
        for (int index = 0; index < _texts.Count; index++)
        {
            WriteU32(bytes, index * 8, _texts[index].Offset);
            WriteU32(bytes, (index * 8) + 4, _texts[index].Length);
        }
        return bytes;
    }

    private byte[] Points()
    {
        var bytes = new byte[_points.Count * 12];
        for (int index = 0; index < _points.Count; index++)
        {
            BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(index * 12), _points[index].X);
            BinaryPrimitives.WriteSingleLittleEndian(
                bytes.AsSpan((index * 12) + 4), _points[index].Y);
            BinaryPrimitives.WriteSingleLittleEndian(
                bytes.AsSpan((index * 12) + 8), _points[index].Z);
        }
        return bytes;
    }

    private byte[] Indices()
    {
        var bytes = new byte[_indices.Count * 4];
        for (int index = 0; index < _indices.Count; index++)
        {
            WriteU32(bytes, index * 4, _indices[index]);
        }
        return bytes;
    }

    private static void WriteU32(byte[] target, int offset, uint value) =>
        BinaryPrimitives.WriteUInt32LittleEndian(target.AsSpan(offset), value);

    private static void WriteI32(byte[] target, int offset, int value) =>
        BinaryPrimitives.WriteInt32LittleEndian(target.AsSpan(offset), value);

    private static void WriteU64(byte[] target, int offset, ulong value) =>
        BinaryPrimitives.WriteUInt64LittleEndian(target.AsSpan(offset), value);

    private static void WriteF64(byte[] target, int offset, double value) =>
        BinaryPrimitives.WriteDoubleLittleEndian(target.AsSpan(offset), value);
}
