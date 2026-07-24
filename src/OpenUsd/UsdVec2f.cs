// Copyright (c) marcschier. Licensed under the MIT License.

using System.Globalization;
using OpenUsd.Interop;

namespace OpenUsd;

/// <summary>A two-component single-precision vector.</summary>
public readonly struct UsdVec2f : IEquatable<UsdVec2f>, IUsdDetachedResult
{
    /// <summary>Initializes a new vector.</summary>
    public UsdVec2f(float x, float y)
    {
        X = x;
        Y = y;
    }

    /// <summary>Gets the first component.</summary>
    public float X { get; }

    /// <summary>Gets the second component.</summary>
    public float Y { get; }

    /// <inheritdoc/>
    public bool Equals(UsdVec2f other) => X.Equals(other.X) && Y.Equals(other.Y);

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is UsdVec2f other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode() => HashCode.Combine(X, Y);

    /// <inheritdoc/>
    public override string ToString() =>
        string.Create(CultureInfo.InvariantCulture, $"({X}, {Y})");

    /// <summary>Returns whether two vectors are equal.</summary>
    public static bool operator ==(UsdVec2f left, UsdVec2f right) => left.Equals(right);

    /// <summary>Returns whether two vectors are not equal.</summary>
    public static bool operator !=(UsdVec2f left, UsdVec2f right) => !left.Equals(right);

    internal static UsdVec2f FromNative(OpenUsdNativeVec2f value) => new(value.X, value.Y);

    internal OpenUsdNativeVec2f ToNative() => new(X, Y);
}
