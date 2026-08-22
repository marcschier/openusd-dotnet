// Copyright (c) marcschier. Licensed under the MIT License.

namespace OpenUsd.Mcp;

internal sealed class AnalysisLightingAnalyzer : IProposalAnalyzer
{
    public AnalysisCategory Category => AnalysisCategory.Lighting;

    public IEnumerable<ProposalDraft> Analyze(AnalysisInput input)
    {
        ArgumentNullException.ThrowIfNull(input);

        RenderSettingsSnapshot settings = input.Scene.RenderSettings;
        string advice = !settings.LightingEnabled
            ? "Lighting is disabled; enable it before judging authored lights."
            : !settings.ShadowsEnabled
                ? "Shadows are disabled; enable them when evaluating light direction and contact."
                : "Review exposure and contrast in the identified renderer before changing authored lights.";

        yield return new ProposalDraft(
            AnalysisCategory.Lighting,
            "lighting.renderer-advice",
            "Renderer-specific lighting advice",
            ProposalApplicability.DiagnosticOnly,
            ProposalRisk.Low,
            advice,
            new ProposalPayload(
                "review-lighting",
                [new("rendererId", input.RendererId)]),
            [
                new("lightingEnabled", settings.LightingEnabled ? "true" : "false"),
                new("rendererId", input.RendererId),
                new("shadowsEnabled", settings.ShadowsEnabled ? "true" : "false"),
            ],
            advisory: true,
            rendererId: input.RendererId);
    }
}
