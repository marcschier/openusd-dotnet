// Copyright (c) marcschier. Licensed under the MIT License.

using System.Globalization;

namespace OpenUsd;

/// <summary>A four-component single-precision vector.</summary>
public readonly struct UsdVec4f : IEquatable<UsdVec4f>, IUsdDetachedResult
{
    /// <summary>Initializes a new vector.</summary>
    public UsdVec4f(float x, float y, float z, float w)
    {
        X = x;
        Y = y;
        Z = z;
        W = w;
    }

    /// <summary>Gets the first component.</summary>
    public float X { get; }

    /// <summary>Gets the second component.</summary>
    public float Y { get; }

    /// <summary>Gets the third component.</summary>
    public float Z { get; }

    /// <summary>Gets the fourth component.</summary>
    public float W { get; }

    /// <inheritdoc/>
    public bool Equals(UsdVec4f other) =>
        X.Equals(other.X) && Y.Equals(other.Y) && Z.Equals(other.Z) && W.Equals(other.W);

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is UsdVec4f other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode() => HashCode.Combine(X, Y, Z, W);

    /// <inheritdoc/>
    public override string ToString() =>
        string.Create(CultureInfo.InvariantCulture, $"({X}, {Y}, {Z}, {W})");

    /// <summary>Returns whether two vectors are equal.</summary>
    public static bool operator ==(UsdVec4f left, UsdVec4f right) => left.Equals(right);

    /// <summary>Returns whether two vectors are not equal.</summary>
    public static bool operator !=(UsdVec4f left, UsdVec4f right) => !left.Equals(right);
}
