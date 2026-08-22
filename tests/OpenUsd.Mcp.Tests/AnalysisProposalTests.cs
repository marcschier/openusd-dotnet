// Copyright (c) marcschier. Licensed under the MIT License.

namespace OpenUsd.Mcp.Tests;

public sealed class AnalysisProposalTests
{
    [Test]
    public async Task DefaultAnalyzersProduceAllCategoriesInDeterministicOrder()
    {
        var service = new AnalysisProposalService(AnalysisDefaults.CreateAnalyzers());

        IReadOnlyList<AnalysisProposal> proposals = service.Analyze(CreateInput(
            validationIssues: ["z issue", "a issue"],
            camera: new CameraTechnicalSnapshot(0.1, 0.1, 1_000, 1, 100),
            performance: new PerformanceSnapshot(
                50,
                true,
                1,
                0.2,
                3_000,
                6_000_000,
                11_000,
                3L * 1024 * 1024 * 1024)));

        await Assert.That(proposals.Select(static proposal => proposal.Category).Distinct())
            .IsEquivalentTo(Enum.GetValues<AnalysisCategory>());
        await Assert.That(proposals)
            .IsEquivalentTo(proposals
                .OrderBy(static proposal => proposal.Category)
                .ThenBy(static proposal => proposal.Code, StringComparer.Ordinal)
                .ThenBy(static proposal => proposal.Id, StringComparer.Ordinal));
    }

    [Test]
    public async Task CameraAnalyzerScoresAndReportsEveryTechnicalFailure()
    {
        var input = CreateInput(
            camera: new CameraTechnicalSnapshot(0.1, 10, 100, 1, 120),
            performance: new PerformanceSnapshot(
                8,
                false,
                0.75,
                0.9,
                10,
                20,
                30,
                40));
        var analyzer = new AnalysisCameraAnalyzer();

        ProposalDraft[] proposals = analyzer.Analyze(input).ToArray();
        double score = AnalysisCameraAnalyzer.ComputeScore(input.Scene.Camera, input.Performance);

        await Assert.That(proposals.Select(static proposal => proposal.Code))
            .IsEquivalentTo(
            [
                "camera.framing",
                "camera.clipping",
                "camera.background",
                "camera.frame-integrity",
            ]);
        await Assert.That(score).IsGreaterThanOrEqualTo(0);
        await Assert.That(score).IsLessThan(50);
        await Assert.That(proposals.Single(static proposal => proposal.Code == "camera.clipping")
            .Applicability).IsEqualTo(ProposalApplicability.DiagnosticOnly);
        await Assert.That(proposals.Single(static proposal => proposal.Code == "camera.frame-integrity")
            .Applicability).IsEqualTo(ProposalApplicability.DiagnosticOnly);
    }

    [Test]
    public async Task ProposalIdsDependOnCoordinatesAndPayloadButNotAnalyzerOrder()
    {
        AnalysisInput input = CreateInput();
        IProposalAnalyzer[] analyzers = AnalysisDefaults.CreateAnalyzers().ToArray();
        var forward = new AnalysisProposalService(analyzers);
        var reverse = new AnalysisProposalService(analyzers.Reverse());

        string[] forwardIds = forward.Analyze(input).Select(static proposal => proposal.Id).ToArray();
        string[] reverseIds = reverse.Analyze(input).Select(static proposal => proposal.Id).ToArray();
        string[] revisedIds = forward.Analyze(input with
        {
            Coordinates = new AnalysisCoordinates(
                input.Coordinates.SessionGeneration,
                input.Coordinates.StageRevision + 1),
        }).Select(static proposal => proposal.Id).ToArray();

        await Assert.That(forwardIds).IsEquivalentTo(reverseIds);
        await Assert.That(revisedIds).IsNotEquivalentTo(forwardIds);
        await Assert.That(forwardIds.All(static id =>
            id.Length == 64 && id.All(Uri.IsHexDigit))).IsTrue();
        await Assert.That(forwardIds[0])
            .IsEqualTo("6d0137703c71581314364a7a86c1a41b14708fafc99e875d965d33972fa291dc");
    }

    [Test]
    public async Task SerializationIsStableAndUsesProtocolEnumNames()
    {
        var service = new AnalysisProposalService(AnalysisDefaults.CreateAnalyzers());
        IReadOnlyList<AnalysisProposal> proposals = service.Analyze(CreateInput());

        string first = ProposalSerialization.Serialize(proposals);
        string second = ProposalSerialization.Serialize(proposals.Reverse());

        await Assert.That(second).IsEqualTo(first);
        await Assert.That(first).Contains("\"diagnostic-only\"");
        await Assert.That(first).Contains("\"render-settings\"");
        await Assert.That(first).DoesNotContain("\"overlay-applicable\"");
    }

    [Test]
    public async Task StaleValidationChecksSessionGenerationBeforeStageRevision()
    {
        var service = new AnalysisProposalService(AnalysisDefaults.CreateAnalyzers());
        AnalysisProposal proposal = service.Analyze(CreateInput())[0];

        ProposalStaleness current = AnalysisProposalService.Validate(
            proposal,
            proposal.Coordinates);
        ProposalStaleness revised = AnalysisProposalService.Validate(
            proposal,
            proposal.Coordinates with { StageRevision = proposal.Coordinates.StageRevision + 1 });
        ProposalStaleness regenerated = AnalysisProposalService.Validate(
            proposal,
            new AnalysisCoordinates(
                proposal.Coordinates.SessionGeneration + 1,
                proposal.Coordinates.StageRevision + 1));

        await Assert.That(current.IsStale).IsFalse();
        await Assert.That(revised.Reason).IsEqualTo("The stage revision changed.");
        await Assert.That(regenerated.Reason).IsEqualTo("The session generation changed.");
    }

    [Test]
    public async Task OverlaySelectionRejectsDiagnosticAndStaleProposals()
    {
        var service = new AnalysisProposalService(AnalysisDefaults.CreateAnalyzers());
        AnalysisInput input = CreateInput();
        IReadOnlyList<AnalysisProposal> proposals = service.Analyze(input);
        AnalysisProposal overlay = ProposalFactory.Create(
            input.Coordinates,
            new ProposalDraft(
                AnalysisCategory.Validation,
                "test.set-double",
                "Set a test value",
                ProposalApplicability.OverlayApplicable,
                ProposalRisk.Low,
                "Exercises the supported workspace edit proposal path.",
                new ProposalPayload(
                    "set-double",
                    [
                        new("attributeName", "size"),
                        new("primPath", "/World"),
                        new("value", "2"),
                    ])));
        AnalysisProposal diagnostic = proposals.First(
            static proposal => proposal.Applicability == ProposalApplicability.DiagnosticOnly);

        IReadOnlyList<ProposalPayload> selected =
            AnalysisProposalService.SelectOverlayPayloads(
                [.. proposals, overlay],
                [overlay.Id],
                input.Coordinates);

        await Assert.That(selected).Count().IsEqualTo(1);
        await Assert.That(() => AnalysisProposalService.SelectOverlayPayloads(
                proposals,
                [diagnostic.Id],
                input.Coordinates))
            .Throws<InvalidOperationException>();
        await Assert.That(() => AnalysisProposalService.SelectOverlayPayloads(
                [.. proposals, overlay],
                [overlay.Id],
                input.Coordinates with { StageRevision = input.Coordinates.StageRevision + 1 }))
            .Throws<InvalidOperationException>();
        await Assert.That(() => AnalysisProposalService.SelectOverlayPayloads(
                [.. proposals, overlay],
                [overlay.Id],
                input.Coordinates with
                {
                    SessionGeneration = input.Coordinates.SessionGeneration + 1,
                }))
            .Throws<InvalidOperationException>();
    }

    [Test]
    public async Task OverlayGuardMatchesTypedWorkspaceOperationsAndRejectsUnknownOperations()
    {
        await Assert.That(ProposalSupportedOperations.OverlayOperations)
            .IsEquivalentTo(
            [
                "clear-overlay-attribute",
                "define-prim",
                "set-active",
                "set-double",
            ]);

        var unsupported = new ProposalDraft(
            AnalysisCategory.Camera,
            "test.unsupported",
            "Unsupported edit",
            ProposalApplicability.OverlayApplicable,
            ProposalRisk.Low,
            "An unimplemented camera edit cannot be applied.",
            new ProposalPayload("set-camera-framing"));
        await Assert.That(() => ProposalFactory.Create(
                new AnalysisCoordinates(1, 1),
                unsupported))
            .Throws<InvalidOperationException>();
    }

    [Test]
    public async Task NumericInputsMetricsAndCameraRangesRejectNonFiniteOrInvalidValues()
    {
        await Assert.That(() => new CameraTechnicalSnapshot(
                double.NaN,
                0.1,
                100,
                1,
                10))
            .Throws<ArgumentOutOfRangeException>();
        await Assert.That(() => new CameraTechnicalSnapshot(
                0.5,
                0,
                100,
                1,
                10))
            .Throws<ArgumentOutOfRangeException>();
        await Assert.That(() => new CameraTechnicalSnapshot(
                0.5,
                1,
                1,
                1,
                10))
            .Throws<ArgumentOutOfRangeException>();
        await Assert.That(() => new CameraTechnicalSnapshot(
                0.5,
                0.1,
                100,
                -1,
                10))
            .Throws<ArgumentOutOfRangeException>();
        await Assert.That(() => new CameraTechnicalSnapshot(
                0.5,
                0.1,
                100,
                10,
                1))
            .Throws<ArgumentOutOfRangeException>();
        await Assert.That(() => new ProposalExpectedMetric(
                "metric",
                double.PositiveInfinity,
                1,
                "units"))
            .Throws<ArgumentOutOfRangeException>();
        await Assert.That(() => new ProposalPayload(
                "inspect",
                [new("value", "NaN")]))
            .Throws<ArgumentOutOfRangeException>();
    }

    private static AnalysisInput CreateInput(
        CameraTechnicalSnapshot? camera = null,
        PerformanceSnapshot? performance = null,
        IEnumerable<string>? validationIssues = null)
    {
        var scene = new SceneAnalysisSnapshot(
            1280,
            720,
            camera ?? new CameraTechnicalSnapshot(0.6, 0.1, 1_000, 1, 100),
            new RenderSettingsSnapshot(4, true, false, "balanced"),
            validationIssues);
        var composition = new CompositionSnapshot(
        [
            new CompositionLayerSnapshot("root.usda", false, false, 12),
            new CompositionLayerSnapshot("session", true, true, 1),
        ],
        [
            new CompositionPcpNodeSnapshot("/World", "root", 0, true, false),
            new CompositionPcpNodeSnapshot("/World/Model", "reference", 2, true, false),
        ]);

        return new AnalysisInput(
            new AnalysisCoordinates(3, 9),
            scene,
            performance ?? new PerformanceSnapshot(12, true, 1, 0.3, 100, 1_000, 20, 1_024),
            composition,
            "Storm/HdStorm");
    }
}
