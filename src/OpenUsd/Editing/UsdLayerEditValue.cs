// Copyright (c) marcschier. Licensed under the MIT License.

using System.Diagnostics.CodeAnalysis;

namespace OpenUsd.Editing;

/// <summary>The closed native value domain; metadata tags are distinct from ordinary attribute arrays.</summary>
[SuppressMessage("Naming", "CA1720:Identifier contains type name",
    Justification = "These names identify the exact native wire types, without numeric coercion.")]
public enum UsdLayerEditValueKind
{
    /// <summary>No authored field/value.</summary>
    Absent = 0,
    /// <summary>An authored Sdf value block.</summary>
    Block = 1,
    /// <summary>A Boolean.</summary>
    Boolean = 2,
    /// <summary>A signed 32-bit integer.</summary>
    Int32 = 3,
    /// <summary>A signed 64-bit integer.</summary>
    Int64 = 4,
    /// <summary>An IEEE single-precision number.</summary>
    Float = 5,
    /// <summary>An IEEE double-precision number.</summary>
    Double = 6,
    /// <summary>A TfToken.</summary>
    Token = 7,
    /// <summary>A string.</summary>
    String = 8,
    /// <summary>An asset path with distinct authored, evaluated and resolved strings.</summary>
    AssetPath = 9,
    /// <summary>A two-component single-precision vector.</summary>
    Vec2f = 10,
    /// <summary>A three-component single-precision vector.</summary>
    Vec3f = 11,
    /// <summary>A three-component double-precision vector.</summary>
    Vec3d = 12,
    /// <summary>A four-component single-precision vector.</summary>
    Vec4f = 13,
    /// <summary>A scalar-first single-precision quaternion.</summary>
    Quatf = 14,
    /// <summary>A row-major double-precision matrix.</summary>
    Matrix4d = 15,
    /// <summary>A six-bucket Sdf path list operation.</summary>
    PathList = 16,
    /// <summary>A native metadata token vector, not token[].</summary>
    TokenVector = 17,
    /// <summary>A native metadata string vector, not string[].</summary>
    StringVector = 18,
    /// <summary>A native specifier: def, over or class.</summary>
    Specifier = 19,
    /// <summary>A native declaration variability.</summary>
    Variability = 20,
    /// <summary>A checkpoint's exact numeric time-sample map.</summary>
    TimeSamples = 21,
    /// <summary>A recursively typed metadata dictionary.</summary>
    Dictionary = 22,
    /// <summary>A native metadata token list operation, not a path list.</summary>
    TokenList = 23,
    /// <summary>A Boolean array.</summary>
    BooleanArray = 258,
    /// <summary>A signed 32-bit integer array.</summary>
    Int32Array = 259,
    /// <summary>A signed 64-bit integer array.</summary>
    Int64Array = 260,
    /// <summary>An IEEE single-precision array.</summary>
    FloatArray = 261,
    /// <summary>An IEEE double-precision array.</summary>
    DoubleArray = 262,
    /// <summary>An ordinary token attribute array.</summary>
    TokenArray = 263,
    /// <summary>An ordinary string attribute array.</summary>
    StringArray = 264
}

/// <summary>Exact authored/evaluated/resolved asset strings, without implicit resolution.</summary>
/// <param name="AuthoredPath">The authored path.</param>
/// <param name="EvaluatedPath">The expression-evaluated path, if present.</param>
/// <param name="ResolvedPath">The cached resolved path, if present.</param>
public readonly record struct UsdLayerAssetPath(
    string AuthoredPath, string EvaluatedPath, string ResolvedPath) : IUsdDetachedResult;

/// <summary>A deeply immutable typed value preserving all native IEEE bits and array ownership.</summary>
/// <remarks>
/// No arbitrary objects, coercion or reflection are used. Array accessors return independent copies.
/// Metadata kinds may be inspected in captured opinions but cannot be manufactured as ordinary edits.
/// </remarks>
public sealed class UsdLayerEditValue : IEquatable<UsdLayerEditValue>, IUsdDetachedResult
{
    private readonly byte[] _bytes;

    internal UsdLayerEditValue(ReadOnlySpan<byte> validatedBytes)
    {
        _bytes = validatedBytes.ToArray();
        Kind = (UsdLayerEditValueKind)System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(_bytes);
    }

    /// <summary>Gets the exact native value kind.</summary>
    public UsdLayerEditValueKind Kind { get; }
    /// <summary>Gets the absent opinion sentinel.</summary>
    public static UsdLayerEditValue Absent { get; } = Create(UsdLayerEditValueKind.Absent, static _ => { });
    /// <summary>Gets the authored block sentinel; use an edit's Block factory to author it.</summary>
    public static UsdLayerEditValue Block { get; } = Create(UsdLayerEditValueKind.Block, static _ => { });

    /// <summary>Creates a Boolean value.</summary>
    public static UsdLayerEditValue FromBoolean(bool value) =>
        Create(UsdLayerEditValueKind.Boolean, writer => writer.U32(value ? 1u : 0u));
    /// <summary>Creates an int32 value.</summary>
    public static UsdLayerEditValue FromInt32(int value) =>
        Create(UsdLayerEditValueKind.Int32, writer => writer.U32(unchecked((uint)value)));
    /// <summary>Creates an int64 value.</summary>
    public static UsdLayerEditValue FromInt64(long value) =>
        Create(UsdLayerEditValueKind.Int64, writer => writer.U64(unchecked((ulong)value)));
    /// <summary>Creates a float without canonicalizing NaNs or signed zero.</summary>
    public static UsdLayerEditValue FromFloat(float value) =>
        Create(UsdLayerEditValueKind.Float, writer => writer.F32(value));
    /// <summary>Creates a double without canonicalizing NaNs or signed zero.</summary>
    public static UsdLayerEditValue FromDouble(double value) =>
        Create(UsdLayerEditValueKind.Double, writer => writer.F64(value));
    /// <summary>Creates a token, distinct from a string.</summary>
    public static UsdLayerEditValue FromToken(string value) =>
        Create(UsdLayerEditValueKind.Token, writer => writer.Text(value));
    /// <summary>Creates a string, distinct from a token.</summary>
    public static UsdLayerEditValue FromString(string value) =>
        Create(UsdLayerEditValueKind.String, writer => writer.Text(value));
    /// <summary>Creates an authored asset path with empty evaluated and resolved caches.</summary>
    public static UsdLayerEditValue FromAssetPath(string authoredPath) =>
        Create(UsdLayerEditValueKind.AssetPath, writer =>
        {
            writer.Text(authoredPath);
            writer.Text(string.Empty);
            writer.Text(string.Empty);
        });
    /// <summary>Creates a two-component float vector.</summary>
    public static UsdLayerEditValue FromVec2f(UsdVec2f value) =>
        Create(UsdLayerEditValueKind.Vec2f, writer =>
        {
            writer.F32(value.X);
            writer.F32(value.Y);
        });
    /// <summary>Creates a three-component float vector.</summary>
    public static UsdLayerEditValue FromVec3f(UsdVec3f value) =>
        Create(UsdLayerEditValueKind.Vec3f, writer =>
        {
            writer.F32(value.X);
            writer.F32(value.Y);
            writer.F32(value.Z);
        });
    /// <summary>Creates a three-component double vector.</summary>
    public static UsdLayerEditValue FromVec3d(UsdVec3d value) =>
        Create(UsdLayerEditValueKind.Vec3d, writer =>
        {
            writer.F64(value.X);
            writer.F64(value.Y);
            writer.F64(value.Z);
        });
    /// <summary>Creates a four-component float vector.</summary>
    public static UsdLayerEditValue FromVec4f(UsdVec4f value) =>
        Create(UsdLayerEditValueKind.Vec4f, writer =>
        {
            writer.F32(value.X);
            writer.F32(value.Y);
            writer.F32(value.Z);
            writer.F32(value.W);
        });
    /// <summary>Creates a scalar-first quaternion.</summary>
    public static UsdLayerEditValue FromQuatf(UsdQuatf value) =>
        Create(UsdLayerEditValueKind.Quatf, writer =>
        {
            writer.F32(value.Real);
            writer.F32(value.X);
            writer.F32(value.Y);
            writer.F32(value.Z);
        });
    /// <summary>Creates a matrix in native row-major order without transposition.</summary>
    public static UsdLayerEditValue FromMatrix4d(UsdMatrix4d value) =>
        Create(UsdLayerEditValueKind.Matrix4d, writer =>
        {
            for (int row = 0; row < 4; row++)
            {
                for (int column = 0; column < 4; column++)
                {
                    writer.F64(value[row, column]);
                }
            }
        });
    /// <summary>Copies an exact six-bucket path list operation.</summary>
    public static UsdLayerEditValue FromPathList(UsdPathListEdit value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return Create(UsdLayerEditValueKind.PathList, writer => writer.PathList(value));
    }

    /// <summary>Copies a Boolean array.</summary>
    public static UsdLayerEditValue FromBooleanArray(ReadOnlySpan<bool> values) =>
        CreateArray(UsdLayerEditValueKind.BooleanArray, values, static (writer, item) => writer.U32(item ? 1u : 0u));
    /// <summary>Copies an int32 array.</summary>
    public static UsdLayerEditValue FromInt32Array(ReadOnlySpan<int> values) =>
        CreateArray(UsdLayerEditValueKind.Int32Array, values,
            static (writer, item) => writer.U32(unchecked((uint)item)));
    /// <summary>Copies an int64 array.</summary>
    public static UsdLayerEditValue FromInt64Array(ReadOnlySpan<long> values) =>
        CreateArray(UsdLayerEditValueKind.Int64Array, values,
            static (writer, item) => writer.U64(unchecked((ulong)item)));
    /// <summary>Copies a float array, retaining every element's bits.</summary>
    public static UsdLayerEditValue FromFloatArray(ReadOnlySpan<float> values) =>
        CreateArray(UsdLayerEditValueKind.FloatArray, values, static (writer, item) => writer.F32(item));
    /// <summary>Copies a double array, retaining every element's bits.</summary>
    public static UsdLayerEditValue FromDoubleArray(ReadOnlySpan<double> values) =>
        CreateArray(UsdLayerEditValueKind.DoubleArray, values, static (writer, item) => writer.F64(item));
    /// <summary>Copies an ordinary token array, not a metadata token vector.</summary>
    public static UsdLayerEditValue FromTokenArray(ReadOnlySpan<string> values) =>
        CreateArray(UsdLayerEditValueKind.TokenArray, values, static (writer, item) => writer.Text(item));
    /// <summary>Copies an ordinary string array, not a metadata string vector.</summary>
    public static UsdLayerEditValue FromStringArray(ReadOnlySpan<string> values) =>
        CreateArray(UsdLayerEditValueKind.StringArray, values, static (writer, item) => writer.Text(item));

    /// <summary>Reads a Boolean, rejecting other kinds.</summary>
    public bool AsBoolean() => Reader(UsdLayerEditValueKind.Boolean).U32() != 0;
    /// <summary>Reads an int32, rejecting other kinds.</summary>
    public int AsInt32() => unchecked((int)Reader(UsdLayerEditValueKind.Int32).U32());
    /// <summary>Reads an int64, rejecting other kinds.</summary>
    public long AsInt64() => unchecked((long)Reader(UsdLayerEditValueKind.Int64).U64());
    /// <summary>Reads a float without numeric conversion.</summary>
    public float AsFloat() => Reader(UsdLayerEditValueKind.Float).F32();
    /// <summary>Reads a double without numeric conversion.</summary>
    public double AsDouble() => Reader(UsdLayerEditValueKind.Double).F64();
    /// <summary>Reads a token, rejecting string values.</summary>
    public string AsToken() => Reader(UsdLayerEditValueKind.Token).Text();
    /// <summary>Reads a string, rejecting token values.</summary>
    public string AsString() => Reader(UsdLayerEditValueKind.String).Text();
    /// <summary>Reads all three asset strings without resolving them.</summary>
    public UsdLayerAssetPath AsAssetPath()
    {
        UsdEditReader reader = Reader(UsdLayerEditValueKind.AssetPath);
        return new UsdLayerAssetPath(reader.Text(), reader.Text(), reader.Text());
    }

    /// <summary>Reads a two-component float vector.</summary>
    public UsdVec2f AsVec2f()
    {
        UsdEditReader reader = Reader(UsdLayerEditValueKind.Vec2f);
        return new UsdVec2f(reader.F32(), reader.F32());
    }

    /// <summary>Reads a three-component float vector.</summary>
    public UsdVec3f AsVec3f()
    {
        UsdEditReader reader = Reader(UsdLayerEditValueKind.Vec3f);
        return new UsdVec3f(reader.F32(), reader.F32(), reader.F32());
    }

    /// <summary>Reads a three-component double vector.</summary>
    public UsdVec3d AsVec3d()
    {
        UsdEditReader reader = Reader(UsdLayerEditValueKind.Vec3d);
        return new UsdVec3d(reader.F64(), reader.F64(), reader.F64());
    }

    /// <summary>Reads a four-component float vector.</summary>
    public UsdVec4f AsVec4f()
    {
        UsdEditReader reader = Reader(UsdLayerEditValueKind.Vec4f);
        return new UsdVec4f(reader.F32(), reader.F32(), reader.F32(), reader.F32());
    }

    /// <summary>Reads a scalar-first quaternion.</summary>
    public UsdQuatf AsQuatf()
    {
        UsdEditReader reader = Reader(UsdLayerEditValueKind.Quatf);
        return new UsdQuatf(reader.F32(), reader.F32(), reader.F32(), reader.F32());
    }

    /// <summary>Reads a native row-major matrix.</summary>
    public UsdMatrix4d AsMatrix4d()
    {
        UsdEditReader reader = Reader(UsdLayerEditValueKind.Matrix4d);
        return new UsdMatrix4d(
            reader.F64(), reader.F64(), reader.F64(), reader.F64(),
            reader.F64(), reader.F64(), reader.F64(), reader.F64(),
            reader.F64(), reader.F64(), reader.F64(), reader.F64(),
            reader.F64(), reader.F64(), reader.F64(), reader.F64());
    }

    /// <summary>Reads an immutable path list operation.</summary>
    public UsdPathListEdit AsPathList() => Reader(UsdLayerEditValueKind.PathList).PathList();

    /// <summary>Returns an independent Boolean array.</summary>
    public bool[] AsBooleanArray() =>
        ReadArray(UsdLayerEditValueKind.BooleanArray, static (ref UsdEditReader reader) => reader.U32() != 0);
    /// <summary>Returns an independent int32 array.</summary>
    public int[] AsInt32Array() =>
        ReadArray(UsdLayerEditValueKind.Int32Array, static (ref UsdEditReader reader) => unchecked((int)reader.U32()));
    /// <summary>Returns an independent int64 array.</summary>
    public long[] AsInt64Array() =>
        ReadArray(UsdLayerEditValueKind.Int64Array, static (ref UsdEditReader reader) => unchecked((long)reader.U64()));
    /// <summary>Returns an independent float array.</summary>
    public float[] AsFloatArray() =>
        ReadArray(UsdLayerEditValueKind.FloatArray, static (ref UsdEditReader reader) => reader.F32());
    /// <summary>Returns an independent double array.</summary>
    public double[] AsDoubleArray() =>
        ReadArray(UsdLayerEditValueKind.DoubleArray, static (ref UsdEditReader reader) => reader.F64());
    /// <summary>Returns an independent token array.</summary>
    public string[] AsTokenArray() =>
        ReadArray(UsdLayerEditValueKind.TokenArray, static (ref UsdEditReader reader) => reader.Text());
    /// <summary>Returns an independent string array.</summary>
    public string[] AsStringArray() =>
        ReadArray(UsdLayerEditValueKind.StringArray, static (ref UsdEditReader reader) => reader.Text());

    /// <inheritdoc/>
    public bool Equals(UsdLayerEditValue? other) =>
        other is not null && _bytes.AsSpan().SequenceEqual(other._bytes);
    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is UsdLayerEditValue other && Equals(other);
    /// <inheritdoc/>
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.AddBytes(_bytes);
        return hash.ToHashCode();
    }

    internal ReadOnlySpan<byte> Payload => _bytes;

    internal bool IsOrdinaryValue =>
        Kind is >= UsdLayerEditValueKind.Boolean and <= UsdLayerEditValueKind.PathList or
            >= UsdLayerEditValueKind.BooleanArray and <= UsdLayerEditValueKind.StringArray;

    private delegate T ReadItem<T>(ref UsdEditReader reader);

    private T[] ReadArray<T>(UsdLayerEditValueKind kind, ReadItem<T> read)
    {
        UsdEditReader reader = Reader(kind);
        int count = reader.Count(UsdEditingValidation.MaximumItems, 4);
        var result = new T[count];
        for (int index = 0; index < count; index++)
        {
            result[index] = read(ref reader);
        }
        return result;
    }

    private UsdEditReader Reader(UsdLayerEditValueKind kind)
    {
        if (Kind != kind)
        {
            throw new InvalidOperationException($"Expected {kind}, but the exact value kind is {Kind}.");
        }
        return new UsdEditReader(_bytes.AsSpan(4));
    }

    private static UsdLayerEditValue Create(UsdLayerEditValueKind kind, Action<UsdEditWriter> write)
    {
        var writer = new UsdEditWriter();
        writer.U32((uint)kind);
        write(writer);
        return new UsdLayerEditValue(writer.Written);
    }

    private static UsdLayerEditValue CreateArray<T>(
        UsdLayerEditValueKind kind, ReadOnlySpan<T> values, Action<UsdEditWriter, T> write)
    {
        UsdEditingValidation.ItemCount(values.Length);
        var writer = new UsdEditWriter();
        writer.U32((uint)kind);
        writer.U32((uint)values.Length);
        foreach (T value in values)
        {
            write(writer, value);
        }
        return new UsdLayerEditValue(writer.Written);
    }
}
