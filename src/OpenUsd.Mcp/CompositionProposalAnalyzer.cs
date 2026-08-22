// Copyright (c) marcschier. Licensed under the MIT License.

using System.Globalization;

namespace OpenUsd.Mcp;

internal sealed class CompositionProposalAnalyzer : IProposalAnalyzer
{
    public AnalysisCategory Category => AnalysisCategory.Composition;

    public IEnumerable<ProposalDraft> Analyze(AnalysisInput input)
    {
        ArgumentNullException.ThrowIfNull(input);

        CompositionSnapshot snapshot = input.Composition;
        int mutedLayers = snapshot.Layers.Count(static layer => layer.IsMuted);
        int anonymousLayers = snapshot.Layers.Count(static layer => layer.IsAnonymous);
        int maxDepth = snapshot.PcpNodes.Count == 0
            ? 0
            : snapshot.PcpNodes.Max(static node => node.Depth);
        int speclessNodes = snapshot.PcpNodes.Count(static node => !node.HasSpecs);
        string arcSummary = string.Join(
            ",",
            snapshot.PcpNodes
                .GroupBy(static node => node.ArcType, StringComparer.Ordinal)
                .OrderBy(static group => group.Key, StringComparer.Ordinal)
                .Select(group => $"{group.Key}:{group.Count().ToString(CultureInfo.InvariantCulture)}"));

        yield return new ProposalDraft(
            AnalysisCategory.Composition,
            "composition.summary",
            "Composition snapshot summary",
            ProposalApplicability.DiagnosticOnly,
            ProposalRisk.Low,
            "Summarizes detached layer and Pcp-node data without opening or traversing a native stage.",
            new ProposalPayload("inspect-composition-summary"),
            [
                Value("anonymousLayers", anonymousLayers),
                new("arcCounts", arcSummary),
                Value("layers", snapshot.Layers.Count),
                Value("maxPcpDepth", maxDepth),
                Value("mutedLayers", mutedLayers),
                Value("pcpNodes", snapshot.PcpNodes.Count),
                Value("speclessPcpNodes", speclessNodes),
            ]);

        if (mutedLayers > 0)
        {
            yield return new ProposalDraft(
                AnalysisCategory.Composition,
                "composition.muted-layers",
                "Review muted composition layers",
                ProposalApplicability.DiagnosticOnly,
                ProposalRisk.Medium,
                "Muted layers are session state and must be inspected outside overlay authorship.",
                new ProposalPayload(
                    "inspect-muted-layers",
                    [
                        new(
                            "identifiers",
                            string.Join(
                                "\n",
                                snapshot.Layers
                                    .Where(static layer => layer.IsMuted)
                                    .Select(static layer => layer.Identifier))),
                    ]),
                [Value("mutedLayers", mutedLayers)]);
        }

        if (maxDepth > 12)
        {
            yield return new ProposalDraft(
                AnalysisCategory.Composition,
                "composition.deep-graph",
                "Consider flattening a deep composition graph",
                ProposalApplicability.FlattenOnly,
                ProposalRisk.High,
                "The Pcp graph is deep enough that a reviewed flattening workflow may simplify delivery.",
                new ProposalPayload(
                    "flatten-composition",
                    [new("maximumObservedDepth", maxDepth.ToString(CultureInfo.InvariantCulture))]),
                [Value("maxPcpDepth", maxDepth)]);
        }
    }

    private static ProposalEvidence Value(string name, int value) =>
        new(name, value.ToString(CultureInfo.InvariantCulture));
}
