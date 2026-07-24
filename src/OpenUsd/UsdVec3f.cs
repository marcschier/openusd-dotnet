// Copyright (c) marcschier. Licensed under the MIT License.

using System.Globalization;
using OpenUsd.Interop;

namespace OpenUsd;

/// <summary>
/// A small, idiomatic three-component float vector used for <c>vec3f</c> and
/// <c>color3f</c> attribute values.
/// </summary>
public readonly struct UsdVec3f : IEquatable<UsdVec3f>, IUsdDetachedResult
{
    /// <summary>Initializes a new vector.</summary>
    public UsdVec3f(float x, float y, float z)
    {
        X = x;
        Y = y;
        Z = z;
    }

    /// <summary>Gets the first component.</summary>
    public float X { get; }

    /// <summary>Gets the second component.</summary>
    public float Y { get; }

    /// <summary>Gets the third component.</summary>
    public float Z { get; }

    /// <inheritdoc/>
    public bool Equals(UsdVec3f other) => X.Equals(other.X) && Y.Equals(other.Y) && Z.Equals(other.Z);

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is UsdVec3f other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode() => HashCode.Combine(X, Y, Z);

    /// <inheritdoc/>
    public override string ToString() =>
        string.Create(CultureInfo.InvariantCulture, $"({X}, {Y}, {Z})");

    /// <summary>Returns whether two vectors are equal.</summary>
    public static bool operator ==(UsdVec3f left, UsdVec3f right) => left.Equals(right);

    /// <summary>Returns whether two vectors are not equal.</summary>
    public static bool operator !=(UsdVec3f left, UsdVec3f right) => !left.Equals(right);

    internal static UsdVec3f FromNative(OpenUsdNativeVec3f value) => new(value.X, value.Y, value.Z);

    internal OpenUsdNativeVec3f ToNative() => new(X, Y, Z);
}
