// Copyright (c) marcschier. Licensed under the MIT License.

using OpenUsd.Geom;
using OpenUsd.Interop;

namespace OpenUsd;

/// <summary>Contains detached finite world-space oriented bounds and their transform.</summary>
public readonly record struct UsdOrientedBounds3d(
    UsdBounds3d Range,
    UsdMatrix4d Matrix) : IUsdDetachedResult
{
    /// <summary>Gets the canonical empty oriented-bounds value.</summary>
    public static UsdOrientedBounds3d Empty => default;

    /// <summary>Gets whether the query found no bounded geometry.</summary>
    public bool IsEmpty => Range.IsEmpty;

    internal static UsdOrientedBounds3d FromNative(OpenUsdNativeOrientedBounds3d bounds) =>
        bounds.IsEmpty != 0
            ? Empty
            : new UsdOrientedBounds3d(
                new UsdBounds3d(
                    new UsdVec3d(bounds.MinimumX, bounds.MinimumY, bounds.MinimumZ),
                    new UsdVec3d(bounds.MaximumX, bounds.MaximumY, bounds.MaximumZ)),
                UsdMatrix4d.FromNative(bounds.Matrix));

    internal static uint ValidatePurposeMask(UsdGeomPurposeMask purposeMask) =>
        UsdBounds3d.ValidatePurposeMask(purposeMask);

    internal static double ValidateTimeCode(double timeCode) =>
        UsdBounds3d.ValidateTimeCode(timeCode);
}
