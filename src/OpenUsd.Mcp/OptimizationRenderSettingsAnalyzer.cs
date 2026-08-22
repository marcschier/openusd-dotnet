// Copyright (c) marcschier. Licensed under the MIT License.

using System.Globalization;

namespace OpenUsd.Mcp;

internal sealed class OptimizationRenderSettingsAnalyzer : IProposalAnalyzer
{
    public AnalysisCategory Category => AnalysisCategory.RenderSettings;

    public IEnumerable<ProposalDraft> Analyze(AnalysisInput input)
    {
        ArgumentNullException.ThrowIfNull(input);

        RenderSettingsSnapshot settings = input.Scene.RenderSettings;
        if (settings.SamplesPerPixel < 8 || !settings.ShadowsEnabled)
        {
            int qualitySamples = Math.Max(8, settings.SamplesPerPixel);
            yield return new ProposalDraft(
                AnalysisCategory.RenderSettings,
                "render.quality-alternative",
                "Use a quality-oriented render alternative",
                ProposalApplicability.DiagnosticOnly,
                ProposalRisk.Low,
                "Review a renderer-specific quality alternative that increases sampling and enables shadows.",
                new ProposalPayload(
                    "inspect-render-settings-alternative",
                    [
                        new("alternative", "quality"),
                        new("samplesPerPixel", qualitySamples.ToString(CultureInfo.InvariantCulture)),
                        new("lightingEnabled", Format(settings.LightingEnabled)),
                        new("shadowsEnabled", "true"),
                        new("qualityPreset", "quality"),
                        new("rendererId", input.RendererId),
                    ]),
                [
                    new("currentQualityPreset", settings.QualityPreset),
                    new("currentSamplesPerPixel", settings.SamplesPerPixel.ToString(CultureInfo.InvariantCulture)),
                ],
                [
                    new(
                        "samplesPerPixel",
                        settings.SamplesPerPixel,
                        qualitySamples,
                        "samples"),
                ],
                advisory: true,
                rendererId: input.RendererId);
        }

        if (settings.SamplesPerPixel > 1 || settings.ShadowsEnabled)
        {
            yield return new ProposalDraft(
                AnalysisCategory.RenderSettings,
                "render.performance-alternative",
                "Use a performance-oriented render alternative",
                ProposalApplicability.DiagnosticOnly,
                ProposalRisk.Medium,
                "Review a renderer-specific interactive alternative that reduces sampling and disables shadows.",
                new ProposalPayload(
                    "inspect-render-settings-alternative",
                    [
                        new("alternative", "performance"),
                        new("samplesPerPixel", "1"),
                        new("lightingEnabled", Format(settings.LightingEnabled)),
                        new("shadowsEnabled", "false"),
                        new("qualityPreset", "interactive"),
                        new("rendererId", input.RendererId),
                    ]),
                [
                    new("currentQualityPreset", settings.QualityPreset),
                    new("currentSamplesPerPixel", settings.SamplesPerPixel.ToString(CultureInfo.InvariantCulture)),
                ],
                [
                    new(
                        "samplesPerPixel",
                        settings.SamplesPerPixel,
                        1,
                        "samples"),
                ],
                advisory: true,
                rendererId: input.RendererId);
        }
    }

    private static string Format(bool value) => value ? "true" : "false";
}
