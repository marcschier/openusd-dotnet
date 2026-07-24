// Copyright (c) marcschier. Licensed under the MIT License.

using System.Text.Json.Nodes;
using OpenUsd.D3D12CompositionSmoke;

namespace OpenUsd.Viewer.Tests;

public sealed class SmokeEvidenceTests
{
    [Test]
    public async Task PixelEvidenceArtifactRoundTripsMeasuredValues()
    {
        SmokePixelEvidenceArtifact expected = CreateValidArtifact();

        SmokePixelEvidenceArtifact actual =
            SmokePixelEvidenceArtifact.ParseAndValidate(expected.ToJson());

        await Assert.That(actual.Initial.Sha256).IsEqualTo(expected.Initial.Sha256);
        await Assert.That(actual.Edited.Sha256).IsEqualTo(expected.Edited.Sha256);
        await Assert.That(actual.ChangedPixels).IsEqualTo(2_000);
        await Assert.That(actual.Lifecycle.LastRetiredGenerationId).IsEqualTo(4);
        await Assert.That(actual.Lifecycle.StaleImportReuseCount).IsEqualTo(0);
    }

    [Test]
    public async Task HardCodedLifecycleBooleansCannotReplaceMeasuredCounters()
    {
        JsonObject root = JsonNode.Parse(CreateValidArtifact().ToJson())!.AsObject();
        JsonObject lifecycle = root["Lifecycle"]!.AsObject();
        lifecycle.Clear();
        lifecycle["oldUpdateCompleted"] = true;
        lifecycle["oldRetired"] = true;
        lifecycle["staleImportReuse"] = false;

        Exception? failure = CaptureFailure(() =>
            SmokePixelEvidenceArtifact.ParseAndValidate(root.ToJsonString()));

        await Assert.That(failure).IsTypeOf<InvalidDataException>();
    }

    private static SmokePixelEvidenceArtifact CreateValidArtifact() =>
        new(
            1,
            SmokePixelEvidenceArtifact.RequiredCaptureApi,
            Capture("initial", new string('A', 64), 2_000, 30, 35, 40),
            Capture("edited", new string('B', 64), 2_500, 45, 25, 30),
            2_000,
            3.25,
            new SmokeLifecycleEvidence(
                OldGenerationId: 4,
                NewGenerationId: 5,
                UpdateStartedBefore: 8,
                UpdateStartedAfter: 16,
                UpdateCompletedBefore: 8,
                UpdateCompletedAfter: 16,
                RetirementStartedBefore: 0,
                RetirementStartedAfter: 1,
                RetirementCompletedBefore: 0,
                RetirementCompletedAfter: 1,
                LastRetiredGenerationId: 4,
                ImportedDisposalsBefore: 0,
                ImportedDisposalsAfter: 3,
                StaleImportReuseCount: 0),
            new SmokeTeardownEvidence(0, 0, 0));

    private static SmokePixelCaptureEvidence Capture(
        string phase,
        string hash,
        long scenePixels,
        double red,
        double green,
        double blue) =>
        new(
            phase,
            hash,
            200,
            100,
            "FF05080D",
            scenePixels,
            red,
            green,
            blue,
            ["FF000000", "FF112233", "FF445566", "FF778899"]);

    private static Exception? CaptureFailure(Action action)
    {
        try
        {
            action();
            return null;
        }
        catch (Exception exception)
        {
            return exception;
        }
    }
}
