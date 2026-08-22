// Copyright (c) marcschier. Licensed under the MIT License.

using System.Security.Cryptography;
using System.Text;

namespace OpenUsd.Mcp.Tests;

public sealed class FinalizationReportWriterTests
{
    [Test]
    public async Task ReportsAndManifestAreDeterministicAcrossInputOrder()
    {
        WorkspaceSessionSnapshot session = CreateSession();
        var firstAnalysis = new FinalizationAnalysis(
            [
                new FinalizationValidationFinding("z", "warning", "last"),
                new FinalizationValidationFinding("a", "error", "first"),
            ],
            [
                new FinalizationStatistic("triangles", "20"),
                new FinalizationStatistic("prims", "10"),
            ],
            ["proposal-z", "proposal-a"]);
        var secondAnalysis = new FinalizationAnalysis(
            firstAnalysis.Validation.Reverse(),
            firstAnalysis.Statistics.Reverse(),
            firstAnalysis.AppliedProposalIds.Reverse());
        FinalizationArtifactRecord[] artifacts =
        [
            new(
                "hero-still",
                "presentation/hero-still.png",
                "image/png",
                FinalizationArtifactStatus.Created,
                4,
                "abcd",
                new Uri("openusd://artifact/hero"),
                null),
            new(
                "final-stage",
                "final-stage.usda",
                "model/vnd.usda",
                FinalizationArtifactStatus.Failed,
                null,
                null,
                null,
                "export failed"),
        ];
        FinalizationFailure[] failures =
        [
            new("z", "last"),
            new("a", "first"),
        ];

        byte[] firstJson = FinalizationReportWriter.CreateAnalysisJson(
            session,
            firstAnalysis,
            artifacts);
        byte[] secondJson = FinalizationReportWriter.CreateAnalysisJson(
            session,
            secondAnalysis,
            artifacts.Reverse());
        byte[] firstMarkdown = FinalizationReportWriter.CreateMarkdown(
            session,
            firstAnalysis,
            artifacts);
        byte[] secondMarkdown = FinalizationReportWriter.CreateMarkdown(
            session,
            secondAnalysis,
            artifacts.Reverse());
        byte[] firstManifest = FinalizationReportWriter.CreateManifestJson(
            session,
            artifacts,
            failures);
        byte[] secondManifest = FinalizationReportWriter.CreateManifestJson(
            session,
            artifacts.Reverse(),
            failures.Reverse());

        await Assert.That(secondJson).IsEquivalentTo(firstJson);
        await Assert.That(secondMarkdown).IsEquivalentTo(firstMarkdown);
        await Assert.That(secondManifest).IsEquivalentTo(firstManifest);
        string manifestText = Encoding.UTF8.GetString(firstManifest);
        await Assert.That(manifestText).Contains("\"sha256\": \"abcd\"");
        await Assert.That(manifestText).Contains("\"mediaType\": \"image/png\"");
        await Assert.That(manifestText).Contains("\"byteLength\": 4");
        await Assert.That(manifestText).Contains(
            FinalizationReportWriter.ContainmentCaveat);
        await Assert.That(Hash(firstJson))
            .IsEqualTo("8254379f0c106d75a8ef54142b3ebe7ebd17223c72ede09717943e355cbbff5b");
        await Assert.That(Hash(firstMarkdown))
            .IsEqualTo("cec2e5657a3cac7a5573007c11495d9d5b0a3294ed65caaa95f7c97e501cc737");
        await Assert.That(Hash(firstManifest))
            .IsEqualTo("520306e808f119567a25c7daa0e4b219432ea3e20d77d3e9f8f2a6bc1b39608c");
    }

    private static WorkspaceSessionSnapshot CreateSession()
    {
        DateTimeOffset createdAt = DateTimeOffset.Parse(
            "2026-08-21T12:00:00Z",
            System.Globalization.CultureInfo.InvariantCulture);
        var info = new WorkspaceSessionInfo(
            "session",
            2,
            3,
            @"C:\source\scene.usda",
            @"C:\output\session",
            @"C:\output\session\overlay.usda",
            createdAt);
        var manifest = new WorkspaceSessionManifest(
            info.SessionId,
            info.SourcePath,
            info.OverlayPath,
            info.Generation,
            info.StageRevision,
            createdAt,
            [],
            []);
        return new WorkspaceSessionSnapshot(info, manifest);
    }

    private static string Hash(ReadOnlySpan<byte> content) =>
        Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant();
}
