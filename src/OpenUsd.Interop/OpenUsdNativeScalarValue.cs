// Copyright (c) marcschier. Licensed under the MIT License.

using System.Runtime.InteropServices;

namespace OpenUsd.Interop;

/// <summary>Discriminates a scalar value returned by the native property API.</summary>
internal enum OpenUsdNativeScalarKind
{
    /// <summary>A boolean value.</summary>
    Boolean = 0,

    /// <summary>A signed 64-bit integer value.</summary>
    Signed64 = 1,

    /// <summary>A double-precision number.</summary>
    Number = 2,

    /// <summary>A UTF-8 text value.</summary>
    Text = 3,

    /// <summary>A token value.</summary>
    Token = 4,

    /// <summary>A three-component float vector.</summary>
    Vector3 = 5,

    /// <summary>A three-component float color.</summary>
    Color3 = 6,

    /// <summary>A row-major 4x4 double matrix.</summary>
    Matrix4d = 7
}

[StructLayout(LayoutKind.Sequential)]
internal struct OpenUsdNativeScalarValue
{
    internal uint StructSize;
    internal int KindValue;
    internal int BoolValueRaw;
    internal long Int64ValueRaw;
    internal double DoubleValueRaw;
    internal OpenUsdNativeVec3f Vec3fValueRaw;
    internal OpenUsdNativeMatrix4d Matrix4dValueRaw;
}

/// <summary>Contains one explicitly tagged scalar returned by the native property API.</summary>
internal readonly struct OpenUsdNativeScalarResult
{
    internal OpenUsdNativeScalarResult(OpenUsdNativeScalarValue value, string? textValue)
    {
        Kind = (OpenUsdNativeScalarKind)value.KindValue;
        BoolValue = value.BoolValueRaw != 0;
        Int64Value = value.Int64ValueRaw;
        DoubleValue = value.DoubleValueRaw;
        TextValue = textValue;
        Vec3fValue = value.Vec3fValueRaw;
        Matrix4dValue = value.Matrix4dValueRaw;
    }

    /// <summary>Gets the scalar kind.</summary>
    public OpenUsdNativeScalarKind Kind { get; }

    /// <summary>Gets the bool payload.</summary>
    public bool BoolValue { get; }

    /// <summary>Gets the int64 payload.</summary>
    public long Int64Value { get; }

    /// <summary>Gets the double payload.</summary>
    public double DoubleValue { get; }

    /// <summary>Gets the string or token payload.</summary>
    public string? TextValue { get; }

    /// <summary>Gets the vec3f or color3f payload.</summary>
    public OpenUsdNativeVec3f Vec3fValue { get; }

    /// <summary>Gets the matrix4d payload.</summary>
    public OpenUsdNativeMatrix4d Matrix4dValue { get; }
}
