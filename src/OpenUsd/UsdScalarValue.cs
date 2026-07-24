// Copyright (c) marcschier. Licensed under the MIT License.

using OpenUsd.Interop;

namespace OpenUsd;

/// <summary>Identifies the payload held by a <see cref="UsdScalarValue"/>.</summary>
public enum UsdScalarKind
{
    /// <summary>No valid value is present.</summary>
    Invalid = 0,

    /// <summary>A bool value.</summary>
    Boolean = 1,

    /// <summary>An int64 value.</summary>
    Signed64,

    /// <summary>A double value.</summary>
    Number,

    /// <summary>A string value.</summary>
    Text,

    /// <summary>A token value.</summary>
    Token,

    /// <summary>A vec3f value.</summary>
    Vector3,

    /// <summary>A color3f value.</summary>
    Color3,

    /// <summary>A matrix4d value.</summary>
    Matrix4d,

    /// <summary>An int32-array value.</summary>
    Int32Array,

    /// <summary>A float-array value.</summary>
    FloatArray,

    /// <summary>A double-array value.</summary>
    DoubleArray,

    /// <summary>A vec2f-array value.</summary>
    Vec2fArray,

    /// <summary>A vec3f-array value.</summary>
    Vec3fArray
}

/// <summary>Contains one explicitly tagged supported scalar or array USD value.</summary>
public readonly struct UsdScalarValue : IUsdDetachedResult
{
    private readonly bool _boolValue;
    private readonly long _int64Value;
    private readonly double _doubleValue;
    private readonly string? _textValue;
    private readonly UsdVec3f _vec3fValue;
    private readonly UsdMatrix4d _matrix4dValue;
    private readonly int[]? _int32ArrayValue;
    private readonly float[]? _floatArrayValue;
    private readonly double[]? _doubleArrayValue;
    private readonly UsdVec2f[]? _vec2fArrayValue;
    private readonly UsdVec3f[]? _vec3fArrayValue;

    private UsdScalarValue(
        UsdScalarKind kind,
        bool boolValue,
        long int64Value,
        double doubleValue,
        string? textValue,
        UsdVec3f vec3fValue,
        UsdMatrix4d matrix4dValue,
        int[]? int32ArrayValue = null,
        float[]? floatArrayValue = null,
        double[]? doubleArrayValue = null,
        UsdVec2f[]? vec2fArrayValue = null,
        UsdVec3f[]? vec3fArrayValue = null)
    {
        Kind = kind;
        _boolValue = boolValue;
        _int64Value = int64Value;
        _doubleValue = doubleValue;
        _textValue = textValue;
        _vec3fValue = vec3fValue;
        _matrix4dValue = matrix4dValue;
        _int32ArrayValue = int32ArrayValue;
        _floatArrayValue = floatArrayValue;
        _doubleArrayValue = doubleArrayValue;
        _vec2fArrayValue = vec2fArrayValue;
        _vec3fArrayValue = vec3fArrayValue;
    }

    /// <summary>Gets the scalar kind.</summary>
    public UsdScalarKind Kind { get; }

    /// <summary>Gets the bool payload.</summary>
    public bool BoolValue => Kind == UsdScalarKind.Boolean
        ? _boolValue
        : throw WrongKind(UsdScalarKind.Boolean);

    /// <summary>Gets the int64 payload.</summary>
    public long Int64Value => Kind == UsdScalarKind.Signed64
        ? _int64Value
        : throw WrongKind(UsdScalarKind.Signed64);

    /// <summary>Gets the double payload.</summary>
    public double DoubleValue => Kind == UsdScalarKind.Number
        ? _doubleValue
        : throw WrongKind(UsdScalarKind.Number);

    /// <summary>Gets the string payload.</summary>
    public string StringValue => Kind == UsdScalarKind.Text
        ? _textValue!
        : throw WrongKind(UsdScalarKind.Text);

    /// <summary>Gets the token payload.</summary>
    public string TokenValue => Kind == UsdScalarKind.Token
        ? _textValue!
        : throw WrongKind(UsdScalarKind.Token);

    /// <summary>Gets the vec3f payload.</summary>
    public UsdVec3f Vec3fValue => Kind == UsdScalarKind.Vector3
        ? _vec3fValue
        : throw WrongKind(UsdScalarKind.Vector3);

    /// <summary>Gets the color3f payload.</summary>
    public UsdVec3f Color3fValue => Kind == UsdScalarKind.Color3
        ? _vec3fValue
        : throw WrongKind(UsdScalarKind.Color3);

    /// <summary>Gets the matrix4d payload.</summary>
    public UsdMatrix4d Matrix4dValue => Kind == UsdScalarKind.Matrix4d
        ? _matrix4dValue
        : throw WrongKind(UsdScalarKind.Matrix4d);

    /// <summary>Gets the int32-array payload.</summary>
    public int[] Int32ArrayValue => Kind == UsdScalarKind.Int32Array
        ? _int32ArrayValue!
        : throw WrongKind(UsdScalarKind.Int32Array);

    /// <summary>Gets the float-array payload.</summary>
    public float[] FloatArrayValue => Kind == UsdScalarKind.FloatArray
        ? _floatArrayValue!
        : throw WrongKind(UsdScalarKind.FloatArray);

    /// <summary>Gets the double-array payload.</summary>
    public double[] DoubleArrayValue => Kind == UsdScalarKind.DoubleArray
        ? _doubleArrayValue!
        : throw WrongKind(UsdScalarKind.DoubleArray);

    /// <summary>Gets the vec2f-array payload.</summary>
    public UsdVec2f[] Vec2fArrayValue => Kind == UsdScalarKind.Vec2fArray
        ? _vec2fArrayValue!
        : throw WrongKind(UsdScalarKind.Vec2fArray);

    /// <summary>Gets the vec3f-array payload.</summary>
    public UsdVec3f[] Vec3fArrayValue => Kind == UsdScalarKind.Vec3fArray
        ? _vec3fArrayValue!
        : throw WrongKind(UsdScalarKind.Vec3fArray);

    internal static UsdScalarValue FromNative(OpenUsdNativeScalarResult value)
    {
        UsdScalarKind kind = value.Kind switch
        {
            OpenUsdNativeScalarKind.Boolean => UsdScalarKind.Boolean,
            OpenUsdNativeScalarKind.Signed64 => UsdScalarKind.Signed64,
            OpenUsdNativeScalarKind.Number => UsdScalarKind.Number,
            OpenUsdNativeScalarKind.Text => UsdScalarKind.Text,
            OpenUsdNativeScalarKind.Token => UsdScalarKind.Token,
            OpenUsdNativeScalarKind.Vector3 => UsdScalarKind.Vector3,
            OpenUsdNativeScalarKind.Color3 => UsdScalarKind.Color3,
            OpenUsdNativeScalarKind.Matrix4d => UsdScalarKind.Matrix4d,
            _ => throw new InvalidOperationException("The native scalar kind is not supported.")
        };
        return new UsdScalarValue(
            kind,
            value.BoolValue,
            value.Int64Value,
            value.DoubleValue,
            value.TextValue,
            UsdVec3f.FromNative(value.Vec3fValue),
            UsdMatrix4d.FromNative(value.Matrix4dValue));
    }

    internal static UsdScalarValue FromInt32Array(int[] value) => new(
        UsdScalarKind.Int32Array, false, 0, 0, null, default, default, int32ArrayValue: value);

    internal static UsdScalarValue FromFloatArray(float[] value) => new(
        UsdScalarKind.FloatArray, false, 0, 0, null, default, default, floatArrayValue: value);

    internal static UsdScalarValue FromDoubleArray(double[] value) => new(
        UsdScalarKind.DoubleArray, false, 0, 0, null, default, default, doubleArrayValue: value);

    internal static UsdScalarValue FromVec2fArray(UsdVec2f[] value) => new(
        UsdScalarKind.Vec2fArray, false, 0, 0, null, default, default, vec2fArrayValue: value);

    internal static UsdScalarValue FromVec3fArray(UsdVec3f[] value) => new(
        UsdScalarKind.Vec3fArray, false, 0, 0, null, default, default, vec3fArrayValue: value);

    private InvalidOperationException WrongKind(UsdScalarKind expected) =>
        new($"The scalar contains {Kind}, not {expected}.");
}
