// Copyright (c) marcschier. Licensed under the MIT License.

namespace OpenUsd.Mcp.Tests;

public sealed class CompositionProposalTests
{
    [Test]
    public async Task SnapshotSortsAndDefensivelyCopiesLayerAndPcpData()
    {
        var layers = new List<CompositionLayerSnapshot>
        {
            new("z.usda", false, false, 1),
            new("a.usda", true, false, 2),
        };
        var nodes = new List<CompositionPcpNodeSnapshot>
        {
            new("/Z", "reference", 2, true, false),
            new("/A", "root", 0, true, false),
        };

        var snapshot = new CompositionSnapshot(layers, nodes);
        layers.Clear();
        nodes.Clear();

        await Assert.That(snapshot.Layers.Select(static layer => layer.Identifier))
            .IsEquivalentTo(["a.usda", "z.usda"]);
        await Assert.That(snapshot.PcpNodes.Select(static node => node.Path))
            .IsEquivalentTo(["/A", "/Z"]);
    }

    [Test]
    public async Task AnalyzerSummarizesArcsAndClassifiesMutingAndFlattening()
    {
        var snapshot = new CompositionSnapshot(
        [
            new CompositionLayerSnapshot("root.usda", false, false, 100),
            new CompositionLayerSnapshot("payload.usda", true, false, 20),
        ],
        [
            new CompositionPcpNodeSnapshot("/World", "root", 0, true, false),
            new CompositionPcpNodeSnapshot("/World/A", "reference", 13, true, false),
            new CompositionPcpNodeSnapshot("/World/B", "reference", 3, false, true),
        ]);
        var input = CreateInput(snapshot);
        var analyzer = new CompositionProposalAnalyzer();

        ProposalDraft[] proposals = analyzer.Analyze(input).ToArray();
        ProposalDraft summary = proposals.Single(
            static proposal => proposal.Code == "composition.summary");

        await Assert.That(summary.Evidence.Single(
            static evidence => evidence.Name == "arcCounts").Value)
            .IsEqualTo("reference:2,root:1");
        await Assert.That(proposals.Single(
            static proposal => proposal.Code == "composition.muted-layers").Applicability)
            .IsEqualTo(ProposalApplicability.DiagnosticOnly);
        await Assert.That(proposals.Single(
            static proposal => proposal.Code == "composition.muted-layers").Payload.Operation)
            .IsEqualTo("inspect-muted-layers");
        await Assert.That(proposals.Single(
            static proposal => proposal.Code == "composition.deep-graph").Applicability)
            .IsEqualTo(ProposalApplicability.FlattenOnly);
    }

    [Test]
    public async Task LightingAdviceIsAdvisoryRendererIdentifiedAndNeverApplicable()
    {
        AnalysisInput input = CreateInput(new CompositionSnapshot());
        var analyzer = new AnalysisLightingAnalyzer();

        ProposalDraft proposal = analyzer.Analyze(input).Single();

        await Assert.That(proposal.Advisory).IsTrue();
        await Assert.That(proposal.RendererId).IsEqualTo("renderer-under-test");
        await Assert.That(proposal.Applicability)
            .IsEqualTo(ProposalApplicability.DiagnosticOnly);
        await Assert.That(proposal.Payload.Arguments["rendererId"])
            .IsEqualTo("renderer-under-test");
    }

    [Test]
    public async Task RenderAdviceIsAdvisoryRendererIdentifiedAndNeverApplicable()
    {
        AnalysisInput input = CreateInput(new CompositionSnapshot());
        var analyzer = new OptimizationRenderSettingsAnalyzer();

        ProposalDraft[] proposals = analyzer.Analyze(input).ToArray();

        await Assert.That(proposals).IsNotEmpty();
        await Assert.That(proposals.All(static proposal => proposal.Advisory)).IsTrue();
        await Assert.That(proposals.All(static proposal =>
            proposal.RendererId == "renderer-under-test")).IsTrue();
        await Assert.That(proposals.All(static proposal =>
            proposal.Applicability == ProposalApplicability.DiagnosticOnly)).IsTrue();
        await Assert.That(proposals.All(static proposal =>
            proposal.Payload.Arguments["rendererId"] == "renderer-under-test")).IsTrue();
    }

    private static AnalysisInput CreateInput(CompositionSnapshot composition) =>
        new(
            new AnalysisCoordinates(1, 2),
            new SceneAnalysisSnapshot(
                100,
                100,
                new CameraTechnicalSnapshot(0.5, 0.1, 100, 1, 10),
                new RenderSettingsSnapshot(2, true, false, "balanced")),
            new PerformanceSnapshot(1, true, 1, 0, 1, 1, 1, 1),
            composition,
            "renderer-under-test");
}
