// Copyright (c) marcschier. Licensed under the MIT License.

namespace OpenUsd.Viewer.Tests;

public sealed class ViewerDocumentSavePolicyTests
{
    [Test]
    public async Task SaveTargetsTheNamedReviewEvenWhenTheSourceIsTheActiveEditTarget()
    {
        string sourcePath = Path.Combine(AppContext.BaseDirectory, "source.usda");
        string reviewPath = Path.Combine(AppContext.BaseDirectory, "review.usda");
        var source = new ViewerDocumentLayerState(
            "source-layer", ViewerDocumentLayerRole.Source, sourcePath, 12, true, true);
        var review = new ViewerDocumentLayerState(
            "review-layer", ViewerDocumentLayerRole.Review, reviewPath, 3, true, true);
        var state = new ViewerDocumentState(
            Guid.NewGuid(), source, review, source.Identifier, SourceEditOptIn: true);

        ViewerDocumentSaveDecision decision = ViewerDocumentSavePolicy.Plan(
            state, ViewerDocumentSaveAction.SaveReview);

        await Assert.That(decision.IsAllowed).IsTrue();
        await Assert.That(decision.LayerIdentifier).IsEqualTo("review-layer");
        await Assert.That(decision.Role).IsEqualTo(ViewerDocumentLayerRole.Review);
        await Assert.That(decision.DestinationPath).IsEqualTo(reviewPath);
        await Assert.That(decision.RequiresDestination).IsFalse();
        await Assert.That(decision.RequiresExternalChangeCheck).IsTrue();
        await Assert.That(state.Source.HasChanges).IsTrue();
    }

    [Test]
    public async Task AnonymousReviewAndSaveAsRequireAReviewDestinationAndAssetAnchorValidation()
    {
        var source = new ViewerDocumentLayerState("source", ViewerDocumentLayerRole.Source,
            Path.Combine(AppContext.BaseDirectory, "source.usda"), 1, false, true);
        var review = new ViewerDocumentLayerState("review", ViewerDocumentLayerRole.Review, null, 3, true, true);
        var state = new ViewerDocumentState(Guid.NewGuid(), source, review, source.Identifier);

        ViewerDocumentSaveDecision save = ViewerDocumentSavePolicy.Plan(state, ViewerDocumentSaveAction.SaveReview);
        ViewerDocumentSaveDecision saveAs = ViewerDocumentSavePolicy.Plan(
            state with { Review = review with { FilePath = Path.Combine(AppContext.BaseDirectory, "old.usda") } },
            ViewerDocumentSaveAction.SaveAs);

        foreach (ViewerDocumentSaveDecision decision in new[] { save, saveAs })
        {
            await Assert.That(decision.IsAllowed).IsTrue();
            await Assert.That(decision.Role).IsEqualTo(ViewerDocumentLayerRole.Review);
            await Assert.That(decision.RequiresDestination).IsTrue();
            await Assert.That(decision.DestinationPath).IsNull();
            await Assert.That(decision.RequiresAssetAnchorValidation).IsTrue();
        }
    }

    [Test]
    public async Task SourceSaveNeedsExplicitOptInAndSimulationCannotStandInForReview()
    {
        var source = new ViewerDocumentLayerState("source", ViewerDocumentLayerRole.Source,
            Path.Combine(AppContext.BaseDirectory, "source.usda"), 1, true, true);
        var simulation = new ViewerDocumentLayerState(
            "simulation", ViewerDocumentLayerRole.Simulation, null, 2, true, true);
        var state = new ViewerDocumentState(Guid.NewGuid(), source, simulation, source.Identifier);

        ViewerDocumentSaveDecision denied = ViewerDocumentSavePolicy.Plan(state, ViewerDocumentSaveAction.SaveSource);
        ViewerDocumentSaveDecision allowed = ViewerDocumentSavePolicy.Plan(
            state with { SourceEditOptIn = true }, ViewerDocumentSaveAction.SaveSource);
        ViewerDocumentSaveDecision notReview = ViewerDocumentSavePolicy.Plan(
            state, ViewerDocumentSaveAction.SaveReview);

        await Assert.That(denied.IsAllowed).IsFalse();
        await Assert.That(denied.Message).Contains("opt-in");
        await Assert.That(allowed.IsAllowed).IsTrue();
        await Assert.That(allowed.LayerIdentifier).IsEqualTo("source");
        await Assert.That(allowed.RequiresExternalChangeCheck).IsTrue();
        await Assert.That(notReview.IsAllowed).IsFalse();
        await Assert.That(notReview.LayerIdentifier).IsNull();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task ReviewSaveNeverPublishesOverTheSourceEvenWithGeneralSourceOptIn(bool sourceOptIn)
    {
        string sourcePath = Path.Combine(AppContext.BaseDirectory, "source.usda");
        string reviewAlias = Path.Combine(AppContext.BaseDirectory, "folder", "..", "source.usda");
        var source = new ViewerDocumentLayerState(
            "source-layer", ViewerDocumentLayerRole.Source, sourcePath, 1, false, true);
        var review = new ViewerDocumentLayerState(
            "review-layer", ViewerDocumentLayerRole.Review, reviewAlias, 2, true, true);
        var document = new ViewerDocumentState(
            Guid.NewGuid(), source, review, review.Identifier, sourceOptIn);

        ViewerDocumentSaveDecision decision = ViewerDocumentSavePolicy.Plan(
            document, ViewerDocumentSaveAction.SaveReview);

        await Assert.That(decision.IsAllowed).IsFalse();
        await Assert.That(decision.DestinationPath).IsNull();
        await Assert.That(decision.Message).Contains("source");
    }
}
