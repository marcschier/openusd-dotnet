// Copyright (c) marcschier. Licensed under the MIT License.

namespace OpenUsd.Physics;

/// <summary>
/// Identifies the outcome of a <see cref="UsdPhysicsSession.BakeAsync"/> call.
/// </summary>
public enum UsdPhysicsBakeStatus
{
    /// <summary>The requested range was baked and written to the target layer.</summary>
    Completed,

    /// <summary>The bake was canceled before completion; the target layer was not modified.</summary>
    Canceled,

    /// <summary>The bake failed; the target layer was not modified.</summary>
    Failed,

    /// <summary>
    /// No active backend can produce simulated results to bake. The target layer was not modified.
    /// </summary>
    NotSupported
}

/// <summary>
/// Describes an immutable request to bake a simulated range into a file-backed animation layer.
/// </summary>
/// <remarks>
/// This request describes the session-level convenience entry point. The transactional bake it
/// drives is implemented by <c>OpenUsd.Physics.Baking.UsdPhysicsBaker</c>, which preflights the
/// destination layer, authors standard USD transform, point, velocity, and extent time samples plus
/// project-owned simulation state, and restores the destination exactly on any failure or
/// cancellation. Use <c>UsdPhysicsBakeSpec</c> directly when the caller needs preflight results,
/// progress, per-record diagnostics, or a sample stride other than one time code.
/// </remarks>
public sealed record UsdPhysicsBakeRequest
{
    /// <summary>Initializes a validated bake request.</summary>
    public UsdPhysicsBakeRequest(
        string targetLayerPath,
        double startTimeCode,
        double endTimeCode,
        double sampleStepSeconds)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetLayerPath);
        if (!double.IsFinite(startTimeCode))
        {
            throw new ArgumentOutOfRangeException(nameof(startTimeCode), "The start time code must be finite.");
        }
        if (!double.IsFinite(endTimeCode) || endTimeCode < startTimeCode)
        {
            throw new ArgumentOutOfRangeException(
                nameof(endTimeCode),
                "The end time code must be finite and not precede the start time code.");
        }
        if (!double.IsFinite(sampleStepSeconds) || sampleStepSeconds <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(sampleStepSeconds),
                "The sample step must be finite and positive.");
        }

        TargetLayerPath = targetLayerPath;
        StartTimeCode = startTimeCode;
        EndTimeCode = endTimeCode;
        SampleStepSeconds = sampleStepSeconds;
    }

    /// <summary>Gets the identifier of the file-backed animation layer to author or replace.</summary>
    public string TargetLayerPath { get; }

    /// <summary>Gets the inclusive first authored time code to sample.</summary>
    public double StartTimeCode { get; }

    /// <summary>Gets the inclusive last authored time code to sample.</summary>
    public double EndTimeCode { get; }

    /// <summary>Gets the simulation-time seconds between successive samples.</summary>
    public double SampleStepSeconds { get; }
}

/// <summary>
/// Reports the immutable result of one <see cref="UsdPhysicsSession.BakeAsync"/> call.
/// </summary>
public sealed record UsdPhysicsBakeResult(
    UsdPhysicsBakeStatus Status,
    int SampleCount,
    UsdPhysicsDiagnostics Diagnostics) : IUsdDetachedResult
{
    /// <summary>Gets the number of samples written to the target layer, or zero if none were.</summary>
    public int SampleCount { get; } = SampleCount >= 0
        ? SampleCount
        : throw new ArgumentOutOfRangeException(
            nameof(SampleCount), SampleCount, "The sample count must not be negative.");

    /// <summary>Gets the diagnostics produced while baking.</summary>
    public UsdPhysicsDiagnostics Diagnostics { get; } =
        Diagnostics ?? throw new ArgumentNullException(nameof(Diagnostics));
}
