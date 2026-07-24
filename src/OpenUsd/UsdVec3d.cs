// Copyright (c) marcschier. Licensed under the MIT License.

using System.Globalization;

namespace OpenUsd;

/// <summary>An immutable three-component double-precision vector.</summary>
public readonly record struct UsdVec3d(
    double X,
    double Y,
    double Z) : IUsdDetachedResult
{
    /// <inheritdoc/>
    public override string ToString() =>
        string.Create(CultureInfo.InvariantCulture, $"({X}, {Y}, {Z})");
}
