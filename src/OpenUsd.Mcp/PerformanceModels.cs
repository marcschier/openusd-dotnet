// Copyright (c) marcschier. Licensed under the MIT License.

namespace OpenUsd.Mcp;

internal sealed record PerformanceSnapshot
{
    public PerformanceSnapshot(
        double frameMilliseconds,
        bool drawSucceeded,
        double finitePixelRatio,
        double backgroundPixelRatio,
        long drawCalls,
        long triangleCount,
        long resourceCount,
        long residentBytes)
    {
        AnalysisNumericValidation.RequireFinite(
            frameMilliseconds,
            nameof(frameMilliseconds));
        ArgumentOutOfRangeException.ThrowIfNegative(frameMilliseconds);
        AnalysisNumericValidation.RequireInRange(
            finitePixelRatio,
            0,
            1,
            nameof(finitePixelRatio));
        AnalysisNumericValidation.RequireInRange(
            backgroundPixelRatio,
            0,
            1,
            nameof(backgroundPixelRatio));
        ArgumentOutOfRangeException.ThrowIfNegative(drawCalls);
        ArgumentOutOfRangeException.ThrowIfNegative(triangleCount);
        ArgumentOutOfRangeException.ThrowIfNegative(resourceCount);
        ArgumentOutOfRangeException.ThrowIfNegative(residentBytes);
        FrameMilliseconds = frameMilliseconds;
        DrawSucceeded = drawSucceeded;
        FinitePixelRatio = finitePixelRatio;
        BackgroundPixelRatio = backgroundPixelRatio;
        DrawCalls = drawCalls;
        TriangleCount = triangleCount;
        ResourceCount = resourceCount;
        ResidentBytes = residentBytes;
    }

    public double FrameMilliseconds { get; }

    public bool DrawSucceeded { get; }

    public double FinitePixelRatio { get; }

    public double BackgroundPixelRatio { get; }

    public long DrawCalls { get; }

    public long TriangleCount { get; }

    public long ResourceCount { get; }

    public long ResidentBytes { get; }
}

internal sealed record PerformanceThresholds
{
    public PerformanceThresholds(
        double frameMilliseconds = 33.333,
        long drawCalls = 2_000,
        long triangleCount = 5_000_000,
        long resourceCount = 10_000,
        long residentBytes = 2L * 1024 * 1024 * 1024)
    {
        AnalysisNumericValidation.RequireFinite(
            frameMilliseconds,
            nameof(frameMilliseconds));
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(frameMilliseconds);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(drawCalls);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(triangleCount);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(resourceCount);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(residentBytes);
        FrameMilliseconds = frameMilliseconds;
        DrawCalls = drawCalls;
        TriangleCount = triangleCount;
        ResourceCount = resourceCount;
        ResidentBytes = residentBytes;
    }

    public double FrameMilliseconds { get; }

    public long DrawCalls { get; }

    public long TriangleCount { get; }

    public long ResourceCount { get; }

    public long ResidentBytes { get; }
}
