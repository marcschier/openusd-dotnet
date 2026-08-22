// Copyright (c) marcschier. Licensed under the MIT License.

using System.Globalization;

namespace OpenUsd.Mcp;

internal sealed class PerformanceProposalAnalyzer : IProposalAnalyzer
{
    private readonly PerformanceThresholds _thresholds;

    public PerformanceProposalAnalyzer(PerformanceThresholds thresholds)
    {
        _thresholds = thresholds ?? throw new ArgumentNullException(nameof(thresholds));
    }

    public AnalysisCategory Category => AnalysisCategory.Performance;

    public IEnumerable<ProposalDraft> Analyze(AnalysisInput input)
    {
        ArgumentNullException.ThrowIfNull(input);

        PerformanceSnapshot snapshot = input.Performance;
        if (snapshot.FrameMilliseconds > _thresholds.FrameMilliseconds)
        {
            yield return Diagnostic(
                "performance.frame-time",
                "Frame time exceeds the interactive threshold",
                "The detached frame statistics exceed the configured frame-time budget.",
                "frameMilliseconds",
                snapshot.FrameMilliseconds,
                _thresholds.FrameMilliseconds,
                "ms");
        }

        if (snapshot.DrawCalls > _thresholds.DrawCalls)
        {
            yield return Diagnostic(
                "performance.draw-calls",
                "Draw-call count is high",
                "High draw-call counts can make traversal and command submission CPU-bound.",
                "drawCalls",
                snapshot.DrawCalls,
                _thresholds.DrawCalls,
                "calls");
        }

        if (snapshot.TriangleCount > _thresholds.TriangleCount)
        {
            yield return Diagnostic(
                "performance.triangles",
                "Triangle count is high",
                "The submitted triangle count exceeds the configured interactive budget.",
                "triangleCount",
                snapshot.TriangleCount,
                _thresholds.TriangleCount,
                "triangles");
        }

        if (snapshot.ResourceCount > _thresholds.ResourceCount)
        {
            yield return Diagnostic(
                "performance.resources",
                "Resource count is high",
                "A large resource set can increase synchronization, lookup, and residency overhead.",
                "resourceCount",
                snapshot.ResourceCount,
                _thresholds.ResourceCount,
                "resources");
        }

        if (snapshot.ResidentBytes > _thresholds.ResidentBytes)
        {
            yield return Diagnostic(
                "performance.residency",
                "Resident resource memory is high",
                "Detached statistics indicate resource residency above the configured memory budget.",
                "residentBytes",
                snapshot.ResidentBytes,
                _thresholds.ResidentBytes,
                "bytes");
        }

        if (!snapshot.DrawSucceeded)
        {
            yield return Diagnostic(
                "performance.draw-failure",
                "Draw did not complete successfully",
                "Performance measurements from a failed draw are not representative.",
                "drawSucceeded",
                0,
                1,
                "boolean");
        }
    }

    private static ProposalDraft Diagnostic(
        string code,
        string title,
        string explanation,
        string metric,
        double current,
        double expected,
        string unit) =>
        new(
            AnalysisCategory.Performance,
            code,
            title,
            ProposalApplicability.DiagnosticOnly,
            ProposalRisk.Medium,
            explanation,
            new ProposalPayload(
                "inspect-performance",
                [new("metric", metric)]),
            [
                new(metric, current.ToString("G17", CultureInfo.InvariantCulture)),
            ],
            [
                new(metric, current, expected, unit),
            ]);
}
