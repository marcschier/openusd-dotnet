// Copyright (c) marcschier. Licensed under the MIT License.

using System.Diagnostics.CodeAnalysis;
using System.Numerics;

namespace OpenUsd.Rendering;

/// <summary>
/// Identifies why two picking results could not produce an authoritative measurement.
/// </summary>
public enum RenderPickMeasurementRefusal
{
    /// <summary>The measurement was produced.</summary>
    None,

    /// <summary>The start result is not a <see cref="RenderPickStatus.Hit"/>.</summary>
    StartNotHit,

    /// <summary>The end result is not a <see cref="RenderPickStatus.Hit"/>.</summary>
    EndNotHit,

    /// <summary>The start hit carries no world-space position.</summary>
    StartWithoutWorldPosition,

    /// <summary>The end hit carries no world-space position.</summary>
    EndWithoutWorldPosition,

    /// <summary>A world-space position contains a NaN or infinite component.</summary>
    NonFinitePosition,

    /// <summary>The two hits were resolved against different renderer-neutral state revisions.</summary>
    StateRevisionMismatch,

    /// <summary>The two hits were resolved against different scene-content revisions.</summary>
    SceneRevisionMismatch,

    /// <summary>The stage <c>metersPerUnit</c> is not positive and finite.</summary>
    InvalidMetersPerUnit,
}

/// <summary>
/// An immutable, units-aware straight-line measurement between two world-space pick hits that were
/// resolved against the same renderer-neutral state and scene revisions.
/// </summary>
/// <remarks>
/// World-space pick positions are expressed in stage scene units, so the scene-unit distance is
/// converted to meters with the composed stage <c>metersPerUnit</c>. The measurement is computed in
/// double precision from the single-precision hit positions. Instanced and subprim hits are accepted;
/// their authoritative identity is preserved on <see cref="Start"/> and <see cref="End"/>.
/// </remarks>
public sealed class RenderPickMeasurement
{
    private RenderPickMeasurement(
        in SelectionItem start,
        in SelectionItem end,
        Vector3 startWorldPosition,
        Vector3 endWorldPosition,
        UsdStageLinearUnits units,
        ulong stateRevision,
        ulong? sceneRevision)
    {
        Start = start;
        End = end;
        StartWorldPosition = startWorldPosition;
        EndWorldPosition = endWorldPosition;
        Units = units;
        StateRevision = stateRevision;
        SceneRevision = sceneRevision;
        double deltaX = (double)endWorldPosition.X - startWorldPosition.X;
        double deltaY = (double)endWorldPosition.Y - startWorldPosition.Y;
        double deltaZ = (double)endWorldPosition.Z - startWorldPosition.Z;
        Delta = new UsdVec3d(deltaX, deltaY, deltaZ);
        DistanceInSceneUnits = Math.Sqrt((deltaX * deltaX) + (deltaY * deltaY) + (deltaZ * deltaZ));
        DistanceInMeters = DistanceInSceneUnits * units.MetersPerUnit;
    }

    /// <summary>Gets the authoritative selection identity of the start hit.</summary>
    public SelectionItem Start { get; }

    /// <summary>Gets the authoritative selection identity of the end hit.</summary>
    public SelectionItem End { get; }

    /// <summary>Gets the world-space start position in scene units.</summary>
    public Vector3 StartWorldPosition { get; }

    /// <summary>Gets the world-space end position in scene units.</summary>
    public Vector3 EndWorldPosition { get; }

    /// <summary>Gets the world-space vector from the start to the end position in scene units.</summary>
    public UsdVec3d Delta { get; }

    /// <summary>Gets the straight-line distance in scene units.</summary>
    public double DistanceInSceneUnits { get; }

    /// <summary>
    /// Gets the straight-line distance in meters (<see cref="DistanceInSceneUnits"/> times
    /// <see cref="MetersPerUnit"/>).
    /// </summary>
    public double DistanceInMeters { get; }

    /// <summary>Gets the stage linear units used for the conversion.</summary>
    public UsdStageLinearUnits Units { get; }

    /// <summary>Gets the stage <c>metersPerUnit</c> used for the conversion.</summary>
    public double MetersPerUnit => Units.MetersPerUnit;

    /// <summary>Gets whether <see cref="MetersPerUnit"/> is authored rather than the UsdGeom fallback.</summary>
    public bool IsMetersPerUnitAuthored => Units.IsMetersPerUnitAuthored;

    /// <summary>Gets the renderer-neutral state revision both hits were resolved against.</summary>
    public ulong StateRevision { get; }

    /// <summary>Gets the scene-content revision both hits were resolved against, or null when none was bound.</summary>
    public ulong? SceneRevision { get; }

    /// <summary>
    /// Attempts to measure between two pick results bound to the same state and scene revisions.
    /// </summary>
    /// <param name="start">The first pick result.</param>
    /// <param name="end">The second pick result.</param>
    /// <param name="units">The composed stage linear units used to convert scene units to meters.</param>
    /// <param name="measurement">The measurement when the inputs are authoritative; otherwise null.</param>
    /// <param name="refusal">The refusal reason, or <see cref="RenderPickMeasurementRefusal.None"/> on success.</param>
    /// <returns><see langword="true"/> when a measurement was produced.</returns>
    public static bool TryCreate(
        in RenderPickResult start,
        in RenderPickResult end,
        UsdStageLinearUnits units,
        [NotNullWhen(true)] out RenderPickMeasurement? measurement,
        out RenderPickMeasurementRefusal refusal)
    {
        measurement = null;
        refusal = Validate(start, end, units);
        if (refusal != RenderPickMeasurementRefusal.None)
        {
            return false;
        }

        measurement = new RenderPickMeasurement(
            start.Item!.Value,
            end.Item!.Value,
            start.WorldPosition!.Value,
            end.WorldPosition!.Value,
            units,
            start.StateRevision,
            start.SceneRevision);
        return true;
    }

    private static RenderPickMeasurementRefusal Validate(
        in RenderPickResult start,
        in RenderPickResult end,
        UsdStageLinearUnits units)
    {
        if (start.Status != RenderPickStatus.Hit || start.Item is null)
        {
            return RenderPickMeasurementRefusal.StartNotHit;
        }
        if (end.Status != RenderPickStatus.Hit || end.Item is null)
        {
            return RenderPickMeasurementRefusal.EndNotHit;
        }
        if (start.WorldPosition is not { } startPosition)
        {
            return RenderPickMeasurementRefusal.StartWithoutWorldPosition;
        }
        if (end.WorldPosition is not { } endPosition)
        {
            return RenderPickMeasurementRefusal.EndWithoutWorldPosition;
        }
        if (!IsFinite(startPosition) || !IsFinite(endPosition))
        {
            return RenderPickMeasurementRefusal.NonFinitePosition;
        }
        if (start.StateRevision != end.StateRevision)
        {
            return RenderPickMeasurementRefusal.StateRevisionMismatch;
        }
        if (start.SceneRevision != end.SceneRevision)
        {
            return RenderPickMeasurementRefusal.SceneRevisionMismatch;
        }
        if (!double.IsFinite(units.MetersPerUnit) || units.MetersPerUnit <= 0)
        {
            return RenderPickMeasurementRefusal.InvalidMetersPerUnit;
        }

        return RenderPickMeasurementRefusal.None;
    }

    private static bool IsFinite(Vector3 value) =>
        float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);
}
