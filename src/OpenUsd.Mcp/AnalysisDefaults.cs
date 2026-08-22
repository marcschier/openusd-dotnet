// Copyright (c) marcschier. Licensed under the MIT License.

namespace OpenUsd.Mcp;

internal static class AnalysisDefaults
{
    public static IReadOnlyList<IProposalAnalyzer> CreateAnalyzers(
        PerformanceThresholds? performanceThresholds = null) =>
        Array.AsReadOnly<IProposalAnalyzer>(
        [
            new AnalysisCameraAnalyzer(),
            new AnalysisLightingAnalyzer(),
            new OptimizationRenderSettingsAnalyzer(),
            new PerformanceProposalAnalyzer(performanceThresholds ?? new PerformanceThresholds()),
            new CompositionProposalAnalyzer(),
            new AnalysisValidationAnalyzer(),
        ]);
}
