// Copyright (c) marcschier. Licensed under the MIT License.

using System.Buffers;
using System.Buffers.Binary;
using System.Text;

namespace OpenUsd.Editing;

internal sealed class UsdEditWriter
{
    private readonly ArrayBufferWriter<byte> _buffer = new();

    internal ReadOnlySpan<byte> Written => _buffer.WrittenSpan;

    internal void U32(uint value)
    {
        BinaryPrimitives.WriteUInt32LittleEndian(Allocate(4), value);
        _buffer.Advance(4);
    }

    internal void U64(ulong value)
    {
        BinaryPrimitives.WriteUInt64LittleEndian(Allocate(8), value);
        _buffer.Advance(8);
    }

    internal void F32(float value) => U32(BitConverter.SingleToUInt32Bits(value));
    internal void F64(double value) => U64(BitConverter.DoubleToUInt64Bits(value));

    internal void Text(string value)
    {
        int count = UsdEditingValidation.Text(value, nameof(value));
        U32((uint)count);
        if (count != 0)
        {
            _ = UsdEditingValidation.Utf8.GetBytes(value.AsSpan(), Allocate(count));
            _buffer.Advance(count);
        }
    }

    internal void Bytes(ReadOnlySpan<byte> bytes)
    {
        if (!bytes.IsEmpty)
        {
            bytes.CopyTo(Allocate(bytes.Length));
            _buffer.Advance(bytes.Length);
        }
    }

    internal void Header(uint kind)
    {
        U32(0x31444555);
        U32(1);
        U32(kind);
    }

    internal void Address(UsdLayerEditAddress address)
    {
        address.Validate();
        Text(address.Path);
        U32((uint)address.Field);
        F64(address.TimeCode);
    }

    internal void PathList(UsdPathListEdit value)
    {
        U32(value.IsExplicit ? 1u : 0u);
        Strings(value.ExplicitItems);
        Strings(value.AddedItems);
        Strings(value.PrependedItems);
        Strings(value.AppendedItems);
        Strings(value.DeletedItems);
        Strings(value.OrderedItems);
    }

    private void Strings(IReadOnlyList<string> values)
    {
        U32((uint)values.Count);
        foreach (string value in values)
        {
            Text(value);
        }
    }

    private Span<byte> Allocate(int count)
    {
        if (count > UsdEditingValidation.MaximumBytes - _buffer.WrittenCount)
        {
            throw new ArgumentException("The authored-layer editing packet exceeds 4 MiB.");
        }
        return _buffer.GetSpan(count)[..count];
    }
}

internal ref struct UsdEditReader
{
    private readonly ReadOnlySpan<byte> _bytes;
    private int _position;

    internal UsdEditReader(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length > UsdEditingValidation.MaximumBytes)
        {
            throw UsdEditingValidation.InvalidPacket("packet exceeds 4 MiB");
        }
        _bytes = bytes;
        _position = 0;
    }

    internal uint U32() => BinaryPrimitives.ReadUInt32LittleEndian(Take(4));
    internal ulong U64() => BinaryPrimitives.ReadUInt64LittleEndian(Take(8));
    internal float F32() => BitConverter.UInt32BitsToSingle(U32());
    internal double F64() => BitConverter.UInt64BitsToDouble(U64());

    internal bool Boolean()
    {
        uint flag = U32();
        if (flag > 1)
        {
            throw UsdEditingValidation.InvalidPacket("invalid Boolean or explicit-mode flag");
        }
        return flag != 0;
    }

    internal int Count(int limit, int minimumItemBytes)
    {
        uint count = U32();
        if (count > limit || count > (_bytes.Length - _position) / minimumItemBytes)
        {
            throw UsdEditingValidation.InvalidPacket("item count exceeds its budget or remaining bytes");
        }
        return (int)count;
    }

    internal string Text()
    {
        int count = Count(UsdEditingValidation.MaximumTextBytes, 1);
        ReadOnlySpan<byte> bytes = Take(count);
        if (bytes.Contains((byte)0))
        {
            throw UsdEditingValidation.InvalidPacket("NUL in text");
        }
        try
        {
            return UsdEditingValidation.Utf8.GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            throw UsdEditingValidation.InvalidPacket("invalid UTF-8");
        }
    }

    internal void Header(uint kind)
    {
        if (U32() != 0x31444555 || U32() != 1 || U32() != kind)
        {
            throw UsdEditingValidation.InvalidPacket("invalid magic, version or kind");
        }
    }

    internal (UsdLayerIdentity Identity, ulong Revision) Identity()
    {
        var identity = new UsdLayerIdentity(U64(), U64(), U64());
        ulong revision = U64();
        if (identity.StageId == 0 || identity.LayerId == 0 || identity.Generation == 0)
        {
            throw UsdEditingValidation.InvalidPacket("zero logical identity");
        }
        return (identity, revision);
    }

    internal UsdLayerEditAddress Address()
    {
        string path = Text();
        uint field = U32();
        double time = F64();
        try
        {
            return new UsdLayerEditAddress(path, (UsdLayerEditField)field, time);
        }
        catch (ArgumentException)
        {
            throw UsdEditingValidation.InvalidPacket("invalid property address or time");
        }
    }

    internal UsdLayerEditValue Value() => new(ValuePayload());

    internal ReadOnlySpan<byte> ValuePayload()
    {
        int start = _position;
        _ = SkipValue(0);
        return _bytes[start.._position];
    }

    internal string[] Strings(bool unique)
    {
        int count = Count(UsdEditingValidation.MaximumItems, 4);
        var result = new string[count];
        HashSet<string>? keys = unique ? new(StringComparer.Ordinal) : null;
        for (int index = 0; index < count; index++)
        {
            string text = Text();
            if (keys is not null && !keys.Add(text))
            {
                throw UsdEditingValidation.InvalidPacket("duplicate list-bucket item");
            }
            result[index] = text;
        }
        return result;
    }

    internal UsdPathListEdit PathList()
    {
        bool explicitMode = Boolean();
        string[] explicitItems = Strings(unique: true);
        string[] added = Strings(unique: true);
        string[] prepended = Strings(unique: true);
        string[] appended = Strings(unique: true);
        string[] deleted = Strings(unique: true);
        string[] ordered = Strings(unique: true);
        try
        {
            return new UsdPathListEdit(explicitMode, explicitItems, added, prepended, appended, deleted, ordered);
        }
        catch (ArgumentException)
        {
            throw UsdEditingValidation.InvalidPacket("mixed explicit/non-explicit list operation");
        }
    }

    internal void End()
    {
        if (_position != _bytes.Length)
        {
            throw UsdEditingValidation.InvalidPacket("trailing bytes");
        }
    }

    private UsdLayerEditValueKind SkipValue(int depth)
    {
        if (depth >= UsdEditingValidation.MaximumDepth)
        {
            throw UsdEditingValidation.InvalidPacket("value nesting exceeds 16");
        }
        var kind = (UsdLayerEditValueKind)U32();
        switch (kind)
        {
            case UsdLayerEditValueKind.Absent:
            case UsdLayerEditValueKind.Block:
                break;
            case UsdLayerEditValueKind.Boolean:
            case UsdLayerEditValueKind.Variability:
                _ = Boolean();
                break;
            case UsdLayerEditValueKind.Int32:
            case UsdLayerEditValueKind.Float:
                _ = Take(4);
                break;
            case UsdLayerEditValueKind.Int64:
            case UsdLayerEditValueKind.Double:
            case UsdLayerEditValueKind.Vec2f:
                _ = Take(8);
                break;
            case UsdLayerEditValueKind.Token:
            case UsdLayerEditValueKind.String:
                _ = Text();
                break;
            case UsdLayerEditValueKind.AssetPath:
                _ = Text();
                _ = Text();
                _ = Text();
                break;
            case UsdLayerEditValueKind.Vec3f:
                _ = Take(12);
                break;
            case UsdLayerEditValueKind.Vec3d:
                _ = Take(24);
                break;
            case UsdLayerEditValueKind.Vec4f:
            case UsdLayerEditValueKind.Quatf:
                _ = Take(16);
                break;
            case UsdLayerEditValueKind.Matrix4d:
                _ = Take(128);
                break;
            case UsdLayerEditValueKind.PathList:
            case UsdLayerEditValueKind.TokenList:
                SkipList();
                break;
            case UsdLayerEditValueKind.Specifier:
                if (U32() > 2)
                {
                    throw UsdEditingValidation.InvalidPacket("invalid specifier");
                }
                break;
            case UsdLayerEditValueKind.TimeSamples:
                SkipTimeSamples(depth);
                break;
            case UsdLayerEditValueKind.Dictionary:
                SkipDictionary(depth);
                break;
            case UsdLayerEditValueKind.BooleanArray:
                int count = Count(UsdEditingValidation.MaximumItems, 4);
                for (int index = 0; index < count; index++)
                {
                    _ = Boolean();
                }
                break;
            case UsdLayerEditValueKind.Int32Array:
            case UsdLayerEditValueKind.FloatArray:
                _ = Take(Count(UsdEditingValidation.MaximumItems, 4) * 4);
                break;
            case UsdLayerEditValueKind.Int64Array:
            case UsdLayerEditValueKind.DoubleArray:
                _ = Take(Count(UsdEditingValidation.MaximumItems, 8) * 8);
                break;
            case UsdLayerEditValueKind.TokenVector:
            case UsdLayerEditValueKind.StringVector:
            case UsdLayerEditValueKind.TokenArray:
            case UsdLayerEditValueKind.StringArray:
                SkipStrings(unique: false);
                break;
            default:
                throw UsdEditingValidation.InvalidPacket("unsupported value tag");
        }
        return kind;
    }

    private int SkipStrings(bool unique)
    {
        int count = Count(UsdEditingValidation.MaximumItems, 4);
        HashSet<string>? keys = unique ? new(StringComparer.Ordinal) : null;
        for (int index = 0; index < count; index++)
        {
            string text = Text();
            if (keys is not null && !keys.Add(text))
            {
                throw UsdEditingValidation.InvalidPacket("duplicate list-bucket item");
            }
        }
        return count;
    }

    private void SkipList()
    {
        bool explicitMode = Boolean();
        for (int bucket = 0; bucket < 6; bucket++)
        {
            int count = SkipStrings(unique: true);
            if (count != 0 && (explicitMode ? bucket != 0 : bucket == 0))
            {
                throw UsdEditingValidation.InvalidPacket("mixed explicit/non-explicit list operation");
            }
        }
    }

    private void SkipTimeSamples(int depth)
    {
        int count = Count(UsdEditingValidation.MaximumItems, 12);
        var times = new HashSet<double>();
        for (int index = 0; index < count; index++)
        {
            double time = F64();
            if (!double.IsFinite(time) || !times.Add(time))
            {
                throw UsdEditingValidation.InvalidPacket("invalid or duplicate sample time");
            }
            if (SkipValue(depth + 1) == UsdLayerEditValueKind.Absent)
            {
                throw UsdEditingValidation.InvalidPacket("empty time-sample value");
            }
        }
    }

    private void SkipDictionary(int depth)
    {
        int count = Count(UsdEditingValidation.MaximumItems, 8);
        var keys = new HashSet<string>(StringComparer.Ordinal);
        for (int index = 0; index < count; index++)
        {
            if (!keys.Add(Text()))
            {
                throw UsdEditingValidation.InvalidPacket("duplicate dictionary key");
            }
            _ = SkipValue(depth + 1);
        }
    }

    private ReadOnlySpan<byte> Take(int count)
    {
        if (count < 0 || count > _bytes.Length - _position)
        {
            throw UsdEditingValidation.InvalidPacket("truncated packet");
        }
        ReadOnlySpan<byte> result = _bytes.Slice(_position, count);
        _position += count;
        return result;
    }
}
