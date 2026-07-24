// Copyright (c) marcschier. Licensed under the MIT License.

using System.Globalization;
using OpenUsd.Geom;
using OpenUsd.Interop;

namespace OpenUsd;

/// <summary>Contains detached finite world-space axis-aligned bounds.</summary>
public readonly struct UsdBounds3d : IEquatable<UsdBounds3d>, IUsdDetachedResult
{
    private readonly bool _hasValue;

    /// <summary>Initializes non-empty finite bounds with ordered minimum and maximum corners.</summary>
    public UsdBounds3d(UsdVec3d min, UsdVec3d max)
    {
        if (!IsFinite(min))
        {
            throw new ArgumentOutOfRangeException(
                nameof(min),
                "The minimum corner must contain only finite values.");
        }
        if (!IsFinite(max))
        {
            throw new ArgumentOutOfRangeException(
                nameof(max),
                "The maximum corner must contain only finite values.");
        }
        if (min.X > max.X || min.Y > max.Y || min.Z > max.Z)
        {
            throw new ArgumentException(
                "Each minimum component must be less than or equal to its maximum component.",
                nameof(max));
        }

        var size = new UsdVec3d(max.X - min.X, max.Y - min.Y, max.Z - min.Z);
        if (!IsFinite(size))
        {
            throw new ArgumentOutOfRangeException(
                nameof(max),
                "The bounds size must be finite.");
        }

        Min = min;
        Max = max;
        _hasValue = true;
    }

    /// <summary>Gets the canonical empty bounds value.</summary>
    public static UsdBounds3d Empty => default;

    /// <summary>Gets whether the query found no bounded geometry.</summary>
    public bool IsEmpty => !_hasValue;

    /// <summary>Gets the minimum corner, or zero for <see cref="Empty"/>.</summary>
    public UsdVec3d Min { get; }

    /// <summary>Gets the maximum corner, or zero for <see cref="Empty"/>.</summary>
    public UsdVec3d Max { get; }

    /// <summary>Gets the center, or zero for <see cref="Empty"/>.</summary>
    public UsdVec3d Center => IsEmpty
        ? default
        : new UsdVec3d(
            Midpoint(Min.X, Max.X),
            Midpoint(Min.Y, Max.Y),
            Midpoint(Min.Z, Max.Z));

    /// <summary>Gets the component-wise size, or zero for <see cref="Empty"/>.</summary>
    public UsdVec3d Size => IsEmpty
        ? default
        : new UsdVec3d(Max.X - Min.X, Max.Y - Min.Y, Max.Z - Min.Z);

    /// <inheritdoc/>
    public bool Equals(UsdBounds3d other) =>
        _hasValue == other._hasValue &&
        Min == other.Min &&
        Max == other.Max;

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is UsdBounds3d other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode() => HashCode.Combine(_hasValue, Min, Max);

    /// <inheritdoc/>
    public override string ToString() =>
        IsEmpty
            ? "Empty"
            : string.Create(CultureInfo.InvariantCulture, $"[{Min} .. {Max}]");

    /// <summary>Returns whether two bounds values are equal.</summary>
    public static bool operator ==(UsdBounds3d left, UsdBounds3d right) => left.Equals(right);

    /// <summary>Returns whether two bounds values are not equal.</summary>
    public static bool operator !=(UsdBounds3d left, UsdBounds3d right) => !left.Equals(right);

    internal static UsdBounds3d FromNative(OpenUsdNativeBounds3d bounds) =>
        bounds.IsEmpty != 0
            ? Empty
            : new UsdBounds3d(
                new UsdVec3d(bounds.MinimumX, bounds.MinimumY, bounds.MinimumZ),
                new UsdVec3d(bounds.MaximumX, bounds.MaximumY, bounds.MaximumZ));

    internal static uint ValidatePurposeMask(UsdGeomPurposeMask purposeMask)
    {
        if ((purposeMask & ~UsdGeomPurposeMask.All) != 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(purposeMask),
                "The purpose mask contains unsupported bits.");
        }
        return (uint)purposeMask;
    }

    internal static double ValidateTimeCode(double timeCode)
    {
        if (!double.IsFinite(timeCode))
        {
            throw new ArgumentOutOfRangeException(
                nameof(timeCode),
                "The time code must be finite.");
        }
        return timeCode;
    }

    private static bool IsFinite(UsdVec3d value) =>
        double.IsFinite(value.X) &&
        double.IsFinite(value.Y) &&
        double.IsFinite(value.Z);

    private static double Midpoint(double minimum, double maximum) =>
        (minimum / 2) + (maximum / 2);
}
