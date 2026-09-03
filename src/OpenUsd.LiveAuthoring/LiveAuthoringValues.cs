// Copyright (c) marcschier. Licensed under the MIT License.

using System.Diagnostics.CodeAnalysis;

namespace OpenUsd.LiveAuthoring;

/// <summary>Identifies the payload carried by a <see cref="LiveAttributeValue"/>.</summary>
[SuppressMessage(
    "Naming",
    "CA1720:Identifier contains type name",
    Justification = "The names describe the exact OpenUSD scalar or array representation.")]
public enum LiveAttributeKind
{
    /// <summary>A Boolean value.</summary>
    Boolean,

    /// <summary>A signed 64-bit integer.</summary>
    Int64,

    /// <summary>A double-precision value.</summary>
    Double,

    /// <summary>A string value.</summary>
    String,

    /// <summary>An OpenUSD token value.</summary>
    Token,

    /// <summary>A three-component single-precision vector.</summary>
    Vec3f,

    /// <summary>A row-major 4x4 double-precision matrix.</summary>
    Matrix4d,

    /// <summary>A 32-bit signed integer array.</summary>
    Int32Array,

    /// <summary>A single-precision float array.</summary>
    FloatArray,

    /// <summary>A double-precision array.</summary>
    DoubleArray,

    /// <summary>A two-component single-precision vector array.</summary>
    Vec2fArray,

    /// <summary>A three-component single-precision vector array.</summary>
    Vec3fArray,

    /// <summary>A three-component single-precision color array.</summary>
    Color3fArray,

    /// <summary>A Boolean array.</summary>
    BooleanArray,

    /// <summary>An OpenUSD token array.</summary>
    TokenArray,

    /// <summary>A string array.</summary>
    StringArray
}

/// <summary>
/// A NativeAOT-safe, bounded discriminated value covering the scalar and array shapes needed by
/// transform and telemetry workflows. Every payload is carried by an existing OpenUSD typed API:
/// there is no boxed <see cref="object"/> payload and no reflection-driven dispatch.
/// </summary>
[SuppressMessage(
    "Naming",
    "CA1720:Identifier contains type name",
    Justification = "The properties describe the exact OpenUSD scalar or array representation.")]
public readonly struct LiveAttributeValue : IEquatable<LiveAttributeValue>
{
    private readonly bool _boolean;
    private readonly long _int64;
    private readonly double _double;
    private readonly string? _text;
    private readonly UsdVec3f _vec3f;
    private readonly UsdMatrix4d _matrix4d;
    private readonly int[]? _int32Array;
    private readonly float[]? _floatArray;
    private readonly double[]? _doubleArray;
    private readonly UsdVec2f[]? _vec2fArray;
    private readonly UsdVec3f[]? _vec3fArray;
    private readonly bool[]? _boolArray;
    private readonly string[]? _textArray;

    private LiveAttributeValue(
        LiveAttributeKind kind,
        bool boolean = false,
        long int64Value = 0,
        double doubleValue = 0,
        string? text = null,
        UsdVec3f vec3f = default,
        UsdMatrix4d matrix4d = default,
        int[]? int32Array = null,
        float[]? floatArray = null,
        double[]? doubleArray = null,
        UsdVec2f[]? vec2fArray = null,
        UsdVec3f[]? vec3fArray = null,
        bool[]? boolArray = null,
        string[]? textArray = null)
    {
        Kind = kind;
        _boolean = boolean;
        _int64 = int64Value;
        _double = doubleValue;
        _text = text;
        _vec3f = vec3f;
        _matrix4d = matrix4d;
        _int32Array = int32Array;
        _floatArray = floatArray;
        _doubleArray = doubleArray;
        _vec2fArray = vec2fArray;
        _vec3fArray = vec3fArray;
        _boolArray = boolArray;
        _textArray = textArray;
    }

    /// <summary>Gets the active payload kind.</summary>
    public LiveAttributeKind Kind { get; }

    /// <summary>Gets the Boolean payload.</summary>
    public bool Boolean => RequireKind(LiveAttributeKind.Boolean)._boolean;

    /// <summary>Gets the int64 payload.</summary>
    public long Int64Value => RequireKind(LiveAttributeKind.Int64)._int64;

    /// <summary>Gets the double payload.</summary>
    public double DoubleValue => RequireKind(LiveAttributeKind.Double)._double;

    /// <summary>Gets the string payload.</summary>
    public string StringValue => RequireKind(LiveAttributeKind.String)._text!;

    /// <summary>Gets the token payload.</summary>
    public string TokenValue => RequireKind(LiveAttributeKind.Token)._text!;

    /// <summary>Gets the vec3f payload.</summary>
    public UsdVec3f Vec3f => RequireKind(LiveAttributeKind.Vec3f)._vec3f;

    /// <summary>Gets the matrix4d payload.</summary>
    public UsdMatrix4d Matrix4d => RequireKind(LiveAttributeKind.Matrix4d)._matrix4d;

    /// <summary>Gets the int32-array payload.</summary>
    public IReadOnlyList<int> Int32Array => RequireKind(LiveAttributeKind.Int32Array)._int32Array!;

    /// <summary>Gets the float-array payload.</summary>
    public IReadOnlyList<float> FloatArray => RequireKind(LiveAttributeKind.FloatArray)._floatArray!;

    /// <summary>Gets the double-array payload.</summary>
    public IReadOnlyList<double> DoubleArray => RequireKind(LiveAttributeKind.DoubleArray)._doubleArray!;

    /// <summary>Gets the vec2f-array payload.</summary>
    public IReadOnlyList<UsdVec2f> Vec2fArray => RequireKind(LiveAttributeKind.Vec2fArray)._vec2fArray!;

    /// <summary>Gets the vec3f-array payload.</summary>
    public IReadOnlyList<UsdVec3f> Vec3fArray => RequireKind(LiveAttributeKind.Vec3fArray)._vec3fArray!;

    /// <summary>Gets the color3f-array payload.</summary>
    public IReadOnlyList<UsdVec3f> Color3fArray => RequireKind(LiveAttributeKind.Color3fArray)._vec3fArray!;

    /// <summary>Gets the Boolean-array payload.</summary>
    public IReadOnlyList<bool> BooleanArray => RequireKind(LiveAttributeKind.BooleanArray)._boolArray!;

    /// <summary>Gets the token-array payload.</summary>
    public IReadOnlyList<string> TokenArray => RequireKind(LiveAttributeKind.TokenArray)._textArray!;

    /// <summary>Gets the string-array payload.</summary>
    public IReadOnlyList<string> StringArray => RequireKind(LiveAttributeKind.StringArray)._textArray!;

    /// <summary>Creates a Boolean value.</summary>
    public static LiveAttributeValue FromBoolean(bool value) =>
        new(LiveAttributeKind.Boolean, boolean: value);

    /// <summary>Creates an integer value.</summary>
    public static LiveAttributeValue FromInt64(long value) =>
        new(LiveAttributeKind.Int64, int64Value: value);

    /// <summary>Creates a double value.</summary>
    public static LiveAttributeValue FromDouble(double value) =>
        new(LiveAttributeKind.Double, doubleValue: value);

    /// <summary>Creates a string value.</summary>
    public static LiveAttributeValue FromString(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return new(LiveAttributeKind.String, text: value);
    }

    /// <summary>Creates a token value.</summary>
    public static LiveAttributeValue FromToken(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        return new(LiveAttributeKind.Token, text: value);
    }

    /// <summary>Creates a vec3f value.</summary>
    public static LiveAttributeValue FromVec3f(UsdVec3f value) =>
        new(LiveAttributeKind.Vec3f, vec3f: value);

    /// <summary>Creates a matrix4d value.</summary>
    public static LiveAttributeValue FromMatrix4d(UsdMatrix4d value) =>
        new(LiveAttributeKind.Matrix4d, matrix4d: value);

    /// <summary>Creates an int32-array value from a defensive copy of <paramref name="values"/>.</summary>
    public static LiveAttributeValue FromInt32Array(IReadOnlyList<int> values) =>
        new(LiveAttributeKind.Int32Array, int32Array: CopyRequired(values));

    /// <summary>Creates a float-array value from a defensive copy of <paramref name="values"/>.</summary>
    public static LiveAttributeValue FromFloatArray(IReadOnlyList<float> values) =>
        new(LiveAttributeKind.FloatArray, floatArray: CopyRequired(values));

    /// <summary>Creates a double-array value from a defensive copy of <paramref name="values"/>.</summary>
    public static LiveAttributeValue FromDoubleArray(IReadOnlyList<double> values) =>
        new(LiveAttributeKind.DoubleArray, doubleArray: CopyRequired(values));

    /// <summary>Creates a vec2f-array value from a defensive copy of <paramref name="values"/>.</summary>
    public static LiveAttributeValue FromVec2fArray(IReadOnlyList<UsdVec2f> values) =>
        new(LiveAttributeKind.Vec2fArray, vec2fArray: CopyRequired(values));

    /// <summary>Creates a vec3f-array value from a defensive copy of <paramref name="values"/>.</summary>
    public static LiveAttributeValue FromVec3fArray(IReadOnlyList<UsdVec3f> values) =>
        new(LiveAttributeKind.Vec3fArray, vec3fArray: CopyRequired(values));

    /// <summary>Creates a color3f-array value from a defensive copy of <paramref name="values"/>.</summary>
    public static LiveAttributeValue FromColor3fArray(IReadOnlyList<UsdVec3f> values) =>
        new(LiveAttributeKind.Color3fArray, vec3fArray: CopyRequired(values));

    /// <summary>Creates a Boolean-array value from a defensive copy of <paramref name="values"/>.</summary>
    public static LiveAttributeValue FromBooleanArray(IReadOnlyList<bool> values) =>
        new(LiveAttributeKind.BooleanArray, boolArray: CopyRequired(values));

    /// <summary>Creates a token-array value from a defensive copy of <paramref name="values"/>.</summary>
    public static LiveAttributeValue FromTokenArray(IReadOnlyList<string> values) =>
        new(LiveAttributeKind.TokenArray, textArray: CopyRequiredText(values));

    /// <summary>Creates a string-array value from a defensive copy of <paramref name="values"/>.</summary>
    public static LiveAttributeValue FromStringArray(IReadOnlyList<string> values) =>
        new(LiveAttributeKind.StringArray, textArray: CopyRequiredText(values));

    /// <inheritdoc/>
    public bool Equals(LiveAttributeValue other)
    {
        if (Kind != other.Kind)
        {
            return false;
        }

        return Kind switch
        {
            LiveAttributeKind.Boolean => _boolean == other._boolean,
            LiveAttributeKind.Int64 => _int64 == other._int64,
            LiveAttributeKind.Double => _double.Equals(other._double),
            LiveAttributeKind.String or LiveAttributeKind.Token =>
                string.Equals(_text, other._text, StringComparison.Ordinal),
            LiveAttributeKind.Vec3f => _vec3f.Equals(other._vec3f),
            LiveAttributeKind.Matrix4d => _matrix4d.Equals(other._matrix4d),
            LiveAttributeKind.Int32Array => _int32Array!.AsSpan().SequenceEqual(other._int32Array),
            LiveAttributeKind.FloatArray => _floatArray!.AsSpan().SequenceEqual(other._floatArray),
            LiveAttributeKind.DoubleArray => _doubleArray!.AsSpan().SequenceEqual(other._doubleArray),
            LiveAttributeKind.Vec2fArray => _vec2fArray!.AsSpan().SequenceEqual(other._vec2fArray),
            LiveAttributeKind.Vec3fArray or LiveAttributeKind.Color3fArray =>
                _vec3fArray!.AsSpan().SequenceEqual(other._vec3fArray),
            LiveAttributeKind.BooleanArray => _boolArray!.AsSpan().SequenceEqual(other._boolArray),
            LiveAttributeKind.TokenArray or LiveAttributeKind.StringArray =>
                _textArray!.AsSpan().SequenceEqual(other._textArray),
            _ => false
        };
    }

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is LiveAttributeValue other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode() => Kind switch
    {
        LiveAttributeKind.Boolean => HashCode.Combine(Kind, _boolean),
        LiveAttributeKind.Int64 => HashCode.Combine(Kind, _int64),
        LiveAttributeKind.Double => HashCode.Combine(Kind, _double),
        LiveAttributeKind.String or LiveAttributeKind.Token => HashCode.Combine(Kind, _text),
        LiveAttributeKind.Vec3f => HashCode.Combine(Kind, _vec3f),
        LiveAttributeKind.Matrix4d => HashCode.Combine(Kind, _matrix4d),
        _ => HashCode.Combine(Kind)
    };

    /// <summary>Returns whether two values are equal.</summary>
    public static bool operator ==(LiveAttributeValue left, LiveAttributeValue right) =>
        left.Equals(right);

    /// <summary>Returns whether two values are not equal.</summary>
    public static bool operator !=(LiveAttributeValue left, LiveAttributeValue right) =>
        !left.Equals(right);

    internal bool IsArray => Kind is LiveAttributeKind.Int32Array or LiveAttributeKind.FloatArray or
        LiveAttributeKind.DoubleArray or LiveAttributeKind.Vec2fArray or LiveAttributeKind.Vec3fArray or
        LiveAttributeKind.Color3fArray or LiveAttributeKind.BooleanArray or LiveAttributeKind.TokenArray or
        LiveAttributeKind.StringArray;

    private LiveAttributeValue RequireKind(LiveAttributeKind expected) => Kind == expected
        ? this
        : throw new InvalidOperationException($"The value contains {Kind}, not {expected}.");

    private static T[] CopyRequired<T>(IReadOnlyList<T> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        if (values.Count > LiveAuthoringValidation.MaxCollectionElementCount)
        {
            throw new ArgumentException(
                "An array cannot exceed " +
                $"{LiveAuthoringValidation.MaxCollectionElementCount} elements.",
                nameof(values));
        }
        var copy = new T[values.Count];
        for (int index = 0; index < values.Count; index++)
        {
            copy[index] = values[index];
        }
        return copy;
    }

    private static string[] CopyRequiredText(IReadOnlyList<string> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        if (values.Count > LiveAuthoringValidation.MaxCollectionElementCount)
        {
            throw new ArgumentException(
                "An array cannot exceed " +
                $"{LiveAuthoringValidation.MaxCollectionElementCount} elements.",
                nameof(values));
        }
        // Bounds are checked in the same pass that copies each element, so an oversized element
        // stops the copy immediately instead of finishing a large allocation only to reject it.
        var copy = new string[values.Count];
        for (int index = 0; index < values.Count; index++)
        {
            string? value = values[index];
            ArgumentNullException.ThrowIfNull(value, nameof(values));
            if (value.Length > LiveAuthoringValidation.MaxTextValueLength)
            {
                throw new ArgumentException(
                    "An array text value cannot exceed " +
                    $"{LiveAuthoringValidation.MaxTextValueLength} characters.",
                    nameof(values));
            }
            copy[index] = value;
        }
        return copy;
    }
}

/// <summary>Identifies the payload carried by a <see cref="LiveMetadataValue"/>.</summary>
[SuppressMessage(
    "Naming",
    "CA1720:Identifier contains type name",
    Justification = "The names describe the exact OpenUSD metadata representation.")]
public enum LiveMetadataKind
{
    /// <summary>A Boolean metadata value.</summary>
    Boolean,

    /// <summary>A signed 64-bit integer metadata value.</summary>
    Int64,

    /// <summary>A double-precision metadata value.</summary>
    Double,

    /// <summary>A string metadata value.</summary>
    String
}

/// <summary>A NativeAOT-safe, bounded discriminated prim-metadata value.</summary>
public readonly struct LiveMetadataValue : IEquatable<LiveMetadataValue>
{
    private readonly bool _boolean;
    private readonly long _int64;
    private readonly double _double;
    private readonly string? _text;

    private LiveMetadataValue(LiveMetadataKind kind, bool boolean, long int64Value, double doubleValue, string? text)
    {
        Kind = kind;
        _boolean = boolean;
        _int64 = int64Value;
        _double = doubleValue;
        _text = text;
    }

    /// <summary>Gets the active payload kind.</summary>
    public LiveMetadataKind Kind { get; }

    /// <summary>Gets the Boolean payload.</summary>
    public bool Boolean => Kind == LiveMetadataKind.Boolean
        ? _boolean
        : throw new InvalidOperationException($"The value contains {Kind}, not {LiveMetadataKind.Boolean}.");

    /// <summary>Gets the int64 payload.</summary>
    public long Int64Value => Kind == LiveMetadataKind.Int64
        ? _int64
        : throw new InvalidOperationException($"The value contains {Kind}, not {LiveMetadataKind.Int64}.");

    /// <summary>Gets the double payload.</summary>
    public double DoubleValue => Kind == LiveMetadataKind.Double
        ? _double
        : throw new InvalidOperationException($"The value contains {Kind}, not {LiveMetadataKind.Double}.");

    /// <summary>Gets the string payload.</summary>
    public string StringValue => Kind == LiveMetadataKind.String
        ? _text!
        : throw new InvalidOperationException($"The value contains {Kind}, not {LiveMetadataKind.String}.");

    /// <summary>Creates a Boolean metadata value.</summary>
    public static LiveMetadataValue FromBoolean(bool value) => new(LiveMetadataKind.Boolean, value, 0, 0, null);

    /// <summary>Creates an integer metadata value.</summary>
    public static LiveMetadataValue FromInt64(long value) => new(LiveMetadataKind.Int64, false, value, 0, null);

    /// <summary>Creates a double metadata value.</summary>
    public static LiveMetadataValue FromDouble(double value) => new(LiveMetadataKind.Double, false, 0, value, null);

    /// <summary>Creates a string metadata value.</summary>
    public static LiveMetadataValue FromString(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return new(LiveMetadataKind.String, false, 0, 0, value);
    }

    /// <inheritdoc/>
    public bool Equals(LiveMetadataValue other) => Kind == other.Kind && Kind switch
    {
        LiveMetadataKind.Boolean => _boolean == other._boolean,
        LiveMetadataKind.Int64 => _int64 == other._int64,
        LiveMetadataKind.Double => _double.Equals(other._double),
        LiveMetadataKind.String => string.Equals(_text, other._text, StringComparison.Ordinal),
        _ => false
    };

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is LiveMetadataValue other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode() => Kind switch
    {
        LiveMetadataKind.Boolean => HashCode.Combine(Kind, _boolean),
        LiveMetadataKind.Int64 => HashCode.Combine(Kind, _int64),
        LiveMetadataKind.Double => HashCode.Combine(Kind, _double),
        LiveMetadataKind.String => HashCode.Combine(Kind, _text),
        _ => HashCode.Combine(Kind)
    };

    /// <summary>Returns whether two values are equal.</summary>
    public static bool operator ==(LiveMetadataValue left, LiveMetadataValue right) => left.Equals(right);

    /// <summary>Returns whether two values are not equal.</summary>
    public static bool operator !=(LiveMetadataValue left, LiveMetadataValue right) => !left.Equals(right);
}
