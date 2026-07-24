// Copyright (c) marcschier. Licensed under the MIT License.

using System.Globalization;
using OpenUsd.Interop;

namespace OpenUsd;

/// <summary>An immutable single-precision quaternion with scalar-first components.</summary>
public readonly struct UsdQuatf : IEquatable<UsdQuatf>, IUsdDetachedResult
{
    /// <summary>Initializes a quaternion from scalar and imaginary components.</summary>
    public UsdQuatf(float real, float x, float y, float z)
    {
        Real = real;
        X = x;
        Y = y;
        Z = z;
    }

    /// <summary>Gets the identity rotation.</summary>
    public static UsdQuatf Identity { get; } = new(1, 0, 0, 0);

    /// <summary>Gets the scalar component.</summary>
    public float Real { get; }

    /// <summary>Gets the imaginary X component.</summary>
    public float X { get; }

    /// <summary>Gets the imaginary Y component.</summary>
    public float Y { get; }

    /// <summary>Gets the imaginary Z component.</summary>
    public float Z { get; }

    /// <inheritdoc/>
    public bool Equals(UsdQuatf other) =>
        Real.Equals(other.Real) &&
        X.Equals(other.X) &&
        Y.Equals(other.Y) &&
        Z.Equals(other.Z);

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is UsdQuatf other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode() => HashCode.Combine(Real, X, Y, Z);

    /// <inheritdoc/>
    public override string ToString() =>
        string.Create(CultureInfo.InvariantCulture, $"({Real}; {X}, {Y}, {Z})");

    /// <summary>Returns whether two quaternions are equal.</summary>
    public static bool operator ==(UsdQuatf left, UsdQuatf right) => left.Equals(right);

    /// <summary>Returns whether two quaternions are not equal.</summary>
    public static bool operator !=(UsdQuatf left, UsdQuatf right) => !left.Equals(right);

    internal static UsdQuatf FromNative(OpenUsdNativeQuatf value) =>
        new(value.Real, value.X, value.Y, value.Z);

    internal OpenUsdNativeQuatf ToNative() => new(Real, X, Y, Z);
}
