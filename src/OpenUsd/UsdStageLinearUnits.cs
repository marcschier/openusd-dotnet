// Copyright (c) marcschier. Licensed under the MIT License.

namespace OpenUsd;

/// <summary>Identifies the composed UsdGeom stage up axis.</summary>
public enum UsdStageUpAxis
{
    /// <summary>The positive Y axis points up.</summary>
    Y = 1,

    /// <summary>The positive Z axis points up.</summary>
    Z = 2,
}

/// <summary>
/// The composed UsdGeom linear-unit metadata of a stage: <c>metersPerUnit</c> and <c>upAxis</c>,
/// with whether each value is authored or reports the UsdGeom fallback.
/// </summary>
/// <param name="MetersPerUnit">
/// The positive, finite number of meters per scene unit. Unauthored stages report the UsdGeom
/// fallback of 0.01 (centimeters).
/// </param>
/// <param name="IsMetersPerUnitAuthored">
/// Whether <c>metersPerUnit</c> is authored on the stage's root or session layer.
/// </param>
/// <param name="UpAxis">The composed up axis; unauthored stages report the configured UsdGeom fallback.</param>
/// <param name="IsUpAxisAuthored">Whether <c>upAxis</c> is authored on the stage's root or session layer.</param>
public readonly record struct UsdStageLinearUnits(
    double MetersPerUnit,
    bool IsMetersPerUnitAuthored,
    UsdStageUpAxis UpAxis,
    bool IsUpAxisAuthored)
{
    /// <summary>Gets the UsdGeom fallback <c>metersPerUnit</c> reported when the stage does not author one.</summary>
    public const double FallbackMetersPerUnit = 0.01;
}
