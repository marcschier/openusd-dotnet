// Copyright (c) marcschier. Licensed under the MIT License.

namespace OpenUsd.Mcp.Tests;

public sealed class PerformanceProposalTests
{
    [Test]
    public async Task AnalyzerUsesInjectedThresholdsForEveryDetachedCounter()
    {
        var thresholds = new PerformanceThresholds(10, 20, 30, 40, 50);
        var analyzer = new PerformanceProposalAnalyzer(thresholds);
        AnalysisInput input = CreateInput(new PerformanceSnapshot(
            11,
            false,
            1,
            0,
            21,
            31,
            41,
            51));

        ProposalDraft[] proposals = analyzer.Analyze(input).ToArray();

        await Assert.That(proposals.Select(static proposal => proposal.Code))
            .IsEquivalentTo(
            [
                "performance.frame-time",
                "performance.draw-calls",
                "performance.triangles",
                "performance.resources",
                "performance.residency",
                "performance.draw-failure",
            ]);
        await Assert.That(proposals.All(static proposal =>
            proposal.Applicability == ProposalApplicability.DiagnosticOnly)).IsTrue();
        await Assert.That(proposals.All(static proposal =>
            proposal.ExpectedMetrics.Count == 1)).IsTrue();
    }

    [Test]
    public async Task ValuesAtThresholdDoNotProducePerformanceFindings()
    {
        var thresholds = new PerformanceThresholds(10, 20, 30, 40, 50);
        var analyzer = new PerformanceProposalAnalyzer(thresholds);
        AnalysisInput input = CreateInput(new PerformanceSnapshot(
            10,
            true,
            1,
            0,
            20,
            30,
            40,
            50));

        await Assert.That(analyzer.Analyze(input)).IsEmpty();
    }

    [Test]
    public async Task DetachedPerformanceValuesAndThresholdsRejectNonFiniteNumbers()
    {
        await Assert.That(() => new PerformanceSnapshot(
                double.NaN,
                true,
                1,
                0,
                1,
                1,
                1,
                1))
            .Throws<ArgumentOutOfRangeException>();
        await Assert.That(() => new PerformanceSnapshot(
                1,
                true,
                double.PositiveInfinity,
                0,
                1,
                1,
                1,
                1))
            .Throws<ArgumentOutOfRangeException>();
        await Assert.That(() => new PerformanceThresholds(
                double.NegativeInfinity,
                1,
                1,
                1,
                1))
            .Throws<ArgumentOutOfRangeException>();
    }

    private static AnalysisInput CreateInput(PerformanceSnapshot performance) =>
        new(
            new AnalysisCoordinates(0, 0),
            new SceneAnalysisSnapshot(
                64,
                64,
                new CameraTechnicalSnapshot(0.5, 0.1, 100, 1, 10),
                new RenderSettingsSnapshot(1, false, false, "test")),
            performance,
            new CompositionSnapshot(),
            "test-renderer");
}
