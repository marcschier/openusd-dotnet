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
    public async Task BusyDocumentRefusesReviewAndOptedInSourceSaves()
    {
        var source = new ViewerDocumentLayerState("source", ViewerDocumentLayerRole.Source,
            Path.Combine(AppContext.BaseDirectory, "source.usda"), 1, true, true);
        var review = new ViewerDocumentLayerState("review", ViewerDocumentLayerRole.Review,
            Path.Combine(AppContext.BaseDirectory, "review.usda"), 2, true, true);
        var document = new ViewerDocumentState(
            Guid.NewGuid(), source, review, review.Identifier, SourceEditOptIn: true, IsBusy: true);

        foreach (ViewerDocumentSaveAction action in new[]
        {
            ViewerDocumentSaveAction.SaveReview,
            ViewerDocumentSaveAction.SaveSource
        })
        {
            ViewerDocumentSaveDecision decision = ViewerDocumentSavePolicy.Plan(document, action);

            await Assert.That(decision.IsAllowed).IsFalse();
            await Assert.That(decision.LayerIdentifier).IsNull();
            await Assert.That(decision.Message).Contains("current document operation");
        }
    }

    [Test]
    public async Task ReadOnlyRequestedLayerIsRefusedWithoutSubstitutingTheWritableLayer()
    {
        string sourcePath = Path.Combine(AppContext.BaseDirectory, "source.usda");
        string reviewPath = Path.Combine(AppContext.BaseDirectory, "review.usda");
        var writableSource = new ViewerDocumentLayerState(
            "source", ViewerDocumentLayerRole.Source, sourcePath, 1, false, true);
        var readOnlyReview = new ViewerDocumentLayerState(
            "review", ViewerDocumentLayerRole.Review, reviewPath, 2, true, false);
        var reviewDocument = new ViewerDocumentState(
            Guid.NewGuid(), writableSource, readOnlyReview, readOnlyReview.Identifier);
        var readOnlySource = writableSource with { IsWritable = false };
        var writableReview = readOnlyReview with { IsWritable = true };
        var sourceDocument = new ViewerDocumentState(
            Guid.NewGuid(), readOnlySource, writableReview, writableReview.Identifier, SourceEditOptIn: true);

        ViewerDocumentSaveDecision reviewDecision = ViewerDocumentSavePolicy.Plan(
            reviewDocument, ViewerDocumentSaveAction.SaveReview);
        ViewerDocumentSaveDecision sourceDecision = ViewerDocumentSavePolicy.Plan(
            sourceDocument, ViewerDocumentSaveAction.SaveSource);

        foreach (ViewerDocumentSaveDecision decision in new[] { reviewDecision, sourceDecision })
        {
            await Assert.That(decision.IsAllowed).IsFalse();
            await Assert.That(decision.LayerIdentifier).IsNull();
            await Assert.That(decision.Message).Contains("requested layer is read-only");
        }
    }

    [Test]
    public async Task RelativeLayerPathsAreRefusedForReviewAndSourcePublication()
    {
        var source = new ViewerDocumentLayerState(
            "source", ViewerDocumentLayerRole.Source, "relative/source.usda", 1, true, true);
        var review = new ViewerDocumentLayerState(
            "review", ViewerDocumentLayerRole.Review, "relative/review.usda", 2, true, true);
        var document = new ViewerDocumentState(
            Guid.NewGuid(), source, review, review.Identifier, SourceEditOptIn: true);

        ViewerDocumentSaveDecision reviewDecision = ViewerDocumentSavePolicy.Plan(
            document, ViewerDocumentSaveAction.SaveReview);
        ViewerDocumentSaveDecision sourceDecision = ViewerDocumentSavePolicy.Plan(
            document, ViewerDocumentSaveAction.SaveSource);

        foreach (ViewerDocumentSaveDecision decision in new[] { reviewDecision, sourceDecision })
        {
            await Assert.That(decision.IsAllowed).IsFalse();
            await Assert.That(decision.LayerIdentifier).IsNull();
            await Assert.That(decision.Message).Contains("must be resolved");
        }
    }

    [Test]
    public async Task AnonymousSourceCannotBeSavedEvenWhenSourceEditingIsOptedIn()
    {
        var source = new ViewerDocumentLayerState("anonymous-source", ViewerDocumentLayerRole.Source,
            null, 1, true, true);
        var review = new ViewerDocumentLayerState("review", ViewerDocumentLayerRole.Review,
            Path.Combine(AppContext.BaseDirectory, "review.usda"), 2, false, true);
        var document = new ViewerDocumentState(
            Guid.NewGuid(), source, review, review.Identifier, SourceEditOptIn: true);

        ViewerDocumentSaveDecision decision = ViewerDocumentSavePolicy.Plan(
            document, ViewerDocumentSaveAction.SaveSource);

        await Assert.That(decision.IsAllowed).IsFalse();
        await Assert.That(decision.Role).IsEqualTo(ViewerDocumentLayerRole.Source);
        await Assert.That(decision.LayerIdentifier).IsNull();
        await Assert.That(decision.Message).Contains("anonymous source");
    }

    [Test]
    public async Task InvalidSaveActionIsRejected()
    {
        var source = new ViewerDocumentLayerState("source", ViewerDocumentLayerRole.Source,
            Path.Combine(AppContext.BaseDirectory, "source.usda"), 1, false, true);
        var review = new ViewerDocumentLayerState("review", ViewerDocumentLayerRole.Review,
            Path.Combine(AppContext.BaseDirectory, "review.usda"), 2, false, true);
        var document = new ViewerDocumentState(Guid.NewGuid(), source, review, review.Identifier);

        await Assert.That(() => ViewerDocumentSavePolicy.Plan(
            document, (ViewerDocumentSaveAction)int.MaxValue)).Throws<ArgumentOutOfRangeException>();
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
