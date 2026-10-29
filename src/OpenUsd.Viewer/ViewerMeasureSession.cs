// Copyright (c) marcschier. Licensed under the MIT License.

using System.Globalization;
using OpenUsd.Rendering;

namespace OpenUsd.Viewer;

/// <summary>
/// Holds the Tools &gt; Measure Distance state: the pending start pick and the last authoritative
/// measurement. Every number shown comes from <see cref="RenderPickMeasurement"/>; this type only
/// sequences picks and words the result or the refusal.
/// </summary>
internal sealed class ViewerMeasureSession
{
    private RenderPickResult? _start;

    internal bool IsActive { get; private set; }

    internal bool HasStart => _start is not null;

    internal RenderPickMeasurement? Measurement { get; private set; }

    internal string Activate()
    {
        IsActive = true;
        _start = null;
        Measurement = null;
        return "Measure: pick the start point (Escape cancels).";
    }

    /// <summary>Leaves measure mode and drops any pending point or measurement.</summary>
    /// <returns><see langword="true"/> when measure mode was active.</returns>
    internal bool Cancel()
    {
        bool wasActive = IsActive;
        IsActive = false;
        _start = null;
        Measurement = null;
        return wasActive;
    }

    /// <summary>Whether the result completes a pair and the stage units are therefore needed.</summary>
    internal bool NeedsUnits(in RenderPickResult result) =>
        IsActive && _start is not null && result.Status == RenderPickStatus.Hit;

    /// <summary>Applies one pick while measure mode is active and returns the status text.</summary>
    internal string Accept(in RenderPickResult result, UsdStageLinearUnits? units)
    {
        if (!IsActive)
        {
            throw new InvalidOperationException("Measure mode is not active.");
        }
        string point = _start is null ? "start" : "end";
        switch (result.Status)
        {
            case RenderPickStatus.Hit when result.Item is not null && result.WorldPosition is not null:
                break;
            case RenderPickStatus.Hit:
                return $"Measure refused: the {point} pick has no world position; no distance was measured.";
            case RenderPickStatus.Miss:
                return $"Measure refused: the {point} pick missed geometry; no distance was measured.";
            case RenderPickStatus.Stale:
                return $"Measure refused: the {point} pick was stale ({result.StaleReasons}); " +
                    "no distance was measured.";
            default:
                return $"Measure refused: picking is unsupported for this request; no distance was measured.";
        }

        if (_start is not { } start)
        {
            _start = result;
            Measurement = null;
            return $"Measure start set at {FormatItem(result.Item.Value)}; pick the end point.";
        }

        _start = null;
        if (units is not { } stageUnits)
        {
            return "Measure refused: the stage linear units are unavailable; no distance was measured. " +
                "Pick the start point again.";
        }
        if (!RenderPickMeasurement.TryCreate(
                start,
                result,
                stageUnits,
                out RenderPickMeasurement? measurement,
                out RenderPickMeasurementRefusal refusal))
        {
            return $"Measure refused: {DescribeRefusal(refusal)}; no distance was measured. " +
                "Pick the start point again.";
        }
        Measurement = measurement;
        return $"Measured {Format(measurement)} from {FormatItem(measurement.Start)} " +
            $"to {FormatItem(measurement.End)}.";
    }

    /// <summary>Refuses the end pick when the stage units could not be read.</summary>
    internal string RefuseUnits(string reason)
    {
        _start = null;
        return $"Measure refused: the stage linear units could not be read ({reason}); " +
            "no distance was measured. Pick the start point again.";
    }

    /// <summary>
    /// Drops a pending point or measurement bound to a state revision other than <paramref name="stateRevision"/>.
    /// </summary>
    /// <returns>The status text to show, or <see langword="null"/> when nothing was cleared.</returns>
    internal string? InvalidateForStateRevision(ulong stateRevision)
    {
        bool stale =
            (_start is { } start && start.StateRevision != stateRevision) ||
            (Measurement is { } measurement && measurement.StateRevision != stateRevision);
        return stale ? Invalidate("the view changed") : null;
    }

    /// <summary>Drops a pending point or measurement because the stage or its scene changed.</summary>
    /// <returns>The status text to show, or <see langword="null"/> when nothing was cleared.</returns>
    internal string? Invalidate(string reason)
    {
        if (_start is null && Measurement is null)
        {
            return null;
        }
        _start = null;
        Measurement = null;
        return IsActive
            ? $"Measurement cleared because {reason}; pick the start point again."
            : $"Measurement cleared because {reason}.";
    }

    /// <summary>
    /// Formats a measurement as meters first, then the scene-unit distance and the metersPerUnit
    /// that converted it, labelled authored or fallback.
    /// </summary>
    internal static string Format(RenderPickMeasurement measurement)
    {
        ArgumentNullException.ThrowIfNull(measurement);
        string meters = measurement.DistanceInMeters.ToString("F3", CultureInfo.InvariantCulture);
        string scene = measurement.DistanceInSceneUnits.ToString("G", CultureInfo.InvariantCulture);
        string metersPerUnit = measurement.MetersPerUnit.ToString("G", CultureInfo.InvariantCulture);
        string source = measurement.IsMetersPerUnitAuthored
            ? $"metersPerUnit {metersPerUnit} authored"
            : $"metersPerUnit not authored, fallback {metersPerUnit}";
        return $"{meters} m ({scene} scene units, {source})";
    }

    internal static string DescribeRefusal(RenderPickMeasurementRefusal refusal) => refusal switch
    {
        RenderPickMeasurementRefusal.StartNotHit => "the start pick is not a hit",
        RenderPickMeasurementRefusal.EndNotHit => "the end pick is not a hit",
        RenderPickMeasurementRefusal.StartWithoutWorldPosition => "the start pick has no world position",
        RenderPickMeasurementRefusal.EndWithoutWorldPosition => "the end pick has no world position",
        RenderPickMeasurementRefusal.NonFinitePosition => "a picked position is not finite",
        RenderPickMeasurementRefusal.StateRevisionMismatch =>
            "the two picks were made against different view states",
        RenderPickMeasurementRefusal.SceneRevisionMismatch =>
            "the two picks were made against different scene revisions",
        RenderPickMeasurementRefusal.InvalidMetersPerUnit => "the stage metersPerUnit is not usable",
        _ => $"refusal {refusal}",
    };

    private static string FormatItem(SelectionItem item) =>
        item.InstancerPath is { } instancer
            ? $"{item.PrimPath} (instance {item.InstanceIndex} of {instancer})"
            : item.PrimPath;
}
