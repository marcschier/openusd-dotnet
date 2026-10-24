// Copyright (c) marcschier. Licensed under the MIT License.

namespace OpenUsd.Mcp;

/// <summary>Explicit root-layer units, axis and timeline for a new workspace overlay.</summary>
/// <remarks>These opinions interpret existing coordinates; they never convert or modify source data.</remarks>
public sealed record WorkspaceStageSettings
{
    /// <summary>Initializes a complete set of finite stage settings.</summary>
    public WorkspaceStageSettings(
        double metersPerUnit, double kilogramsPerUnit, string upAxis,
        double startTimeCode, double endTimeCode, double timeCodesPerSecond, double framesPerSecond)
    {
        RequirePositive(metersPerUnit, nameof(metersPerUnit));
        RequirePositive(kilogramsPerUnit, nameof(kilogramsPerUnit));
        RequirePositive(timeCodesPerSecond, nameof(timeCodesPerSecond));
        RequirePositive(framesPerSecond, nameof(framesPerSecond));
        if (upAxis is not ("Y" or "Z"))
        {
            throw new ArgumentException("The USD stage up axis must be Y or Z.", nameof(upAxis));
        }
        if (!double.IsFinite(startTimeCode))
        {
            throw new ArgumentOutOfRangeException(nameof(startTimeCode), "The start time must be finite.");
        }
        if (!double.IsFinite(endTimeCode) || endTimeCode < startTimeCode ||
            !double.IsFinite(endTimeCode - startTimeCode))
        {
            throw new ArgumentOutOfRangeException(nameof(endTimeCode),
                "The end time must be finite, at or after the start, with a finite duration.");
        }
        MetersPerUnit = metersPerUnit;
        KilogramsPerUnit = kilogramsPerUnit;
        UpAxis = upAxis;
        StartTimeCode = startTimeCode;
        EndTimeCode = endTimeCode;
        TimeCodesPerSecond = timeCodesPerSecond;
        FramesPerSecond = framesPerSecond;
    }

    /// <summary>Gets metres per existing coordinate unit.</summary>
    public double MetersPerUnit { get; }
    /// <summary>Gets kilograms per existing mass unit.</summary>
    public double KilogramsPerUnit { get; }
    /// <summary>Gets the Y or Z up axis.</summary>
    public string UpAxis { get; }
    /// <summary>Gets the inclusive authored start time code.</summary>
    public double StartTimeCode { get; }
    /// <summary>Gets the inclusive authored end time code.</summary>
    public double EndTimeCode { get; }
    /// <summary>Gets time codes per second, also used by fixed-step physics recording.</summary>
    public double TimeCodesPerSecond { get; }
    /// <summary>Gets the advisory playback rate.</summary>
    public double FramesPerSecond { get; }

    internal string CreateLayerText() => FormattableString.Invariant($"""
        #usda 1.0
        (
            metersPerUnit = {MetersPerUnit:R}
            kilogramsPerUnit = {KilogramsPerUnit:R}
            upAxis = "{UpAxis}"
            startTimeCode = {StartTimeCode:R}
            endTimeCode = {EndTimeCode:R}
            timeCodesPerSecond = {TimeCodesPerSecond:R}
            framesPerSecond = {FramesPerSecond:R}
        )
        """) + "\n";

    private static void RequirePositive(double value, string name)
    {
        if (!double.IsFinite(value) || value <= 0)
        {
            throw new ArgumentOutOfRangeException(name, "The stage setting must be positive and finite.");
        }
    }
}
