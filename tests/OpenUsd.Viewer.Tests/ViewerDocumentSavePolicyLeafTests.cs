// Copyright (c) marcschier. Licensed under the MIT License.

namespace OpenUsd.Viewer.Tests;

public sealed class ViewerDocumentSavePolicyLeafTests
{
    [Test]
    public async Task EligibleSourceSaveSelectsTheWritableOptedInSourceAndRequestsIdentityCheck()
    {
        string sourcePath = Path.Combine(Path.GetTempPath(), "selected-root.usda");
        var source = new ViewerDocumentLayerState(
            "root-source-layer", ViewerDocumentLayerRole.Source, sourcePath, 17, true, true);
        var review = new ViewerDocumentLayerState(
            "review-layer", ViewerDocumentLayerRole.Review,
            Path.Combine(Path.GetTempPath(), "review.usda"), 23, true, true);
        var document = new ViewerDocumentState(
            Guid.NewGuid(), source, review, review.Identifier, SourceEditOptIn: true);

        ViewerDocumentSaveDecision decision =
            ViewerDocumentSavePolicy.Plan(document, ViewerDocumentSaveAction.SaveSource);

        await Assert.That(decision.IsAllowed).IsTrue();
        await Assert.That(decision.Role).IsEqualTo(ViewerDocumentLayerRole.Source);
        await Assert.That(decision.LayerIdentifier).IsEqualTo("root-source-layer");
        await Assert.That(decision.DestinationPath).IsEqualTo(sourcePath);
        await Assert.That(decision.RequiresDestination).IsFalse();
        await Assert.That(decision.RequiresExternalChangeCheck).IsTrue();
        await Assert.That(decision.RequiresAssetAnchorValidation).IsFalse();
    }

    [Test]
    public async Task SourceSaveWithoutOptInDoesNotFallBackToWritableReviewLayer()
    {
        var source = new ViewerDocumentLayerState(
            "source", ViewerDocumentLayerRole.Source,
            Path.Combine(Path.GetTempPath(), "source.usda"), 11, true, true);
        var review = new ViewerDocumentLayerState(
            "review", ViewerDocumentLayerRole.Review,
            Path.Combine(Path.GetTempPath(), "review.usda"), 12, true, true);
        var document = new ViewerDocumentState(
            Guid.NewGuid(), source, review, review.Identifier, SourceEditOptIn: false);

        ViewerDocumentSaveDecision decision =
            ViewerDocumentSavePolicy.Plan(document, ViewerDocumentSaveAction.SaveSource);

        await Assert.That(decision.IsAllowed).IsFalse();
        await Assert.That(decision.Role).IsEqualTo(ViewerDocumentLayerRole.Source);
        await Assert.That(decision.LayerIdentifier).IsNull();
        await Assert.That(decision.DestinationPath).IsNull();
        await Assert.That(decision.Message).IsEqualTo(
            "Source-layer saving requires explicit source-edit opt-in.");
    }

    [Test]
    public async Task SourceWithoutChangesIsRefusedEvenWhenWritableAndExplicitlyOptedIn()
    {
        var source = new ViewerDocumentLayerState(
            "source", ViewerDocumentLayerRole.Source,
            Path.Combine(Path.GetTempPath(), "source.usda"), 9, false, true);
        var document = new ViewerDocumentState(
            Guid.NewGuid(), source, null, source.Identifier, SourceEditOptIn: true);

        ViewerDocumentSaveDecision decision =
            ViewerDocumentSavePolicy.Plan(document, ViewerDocumentSaveAction.SaveSource);

        await Assert.That(decision.IsAllowed).IsFalse();
        await Assert.That(decision.Role).IsEqualTo(ViewerDocumentLayerRole.Source);
        await Assert.That(decision.LayerIdentifier).IsNull();
        await Assert.That(decision.DestinationPath).IsNull();
        await Assert.That(decision.RequiresDestination).IsFalse();
        await Assert.That(decision.RequiresExternalChangeCheck).IsFalse();
        await Assert.That(decision.Message).IsEqualTo("The source layer has no unsaved changes.");
    }

    [Test]
    public async Task DirtyOtherLayerRefusesSourcePublicationWithoutRedirectingToReview()
    {
        var source = new ViewerDocumentLayerState(
            "explicit-source", ViewerDocumentLayerRole.Source,
            Path.Combine(Path.GetTempPath(), "source.usda"), 4, true, true);
        var review = new ViewerDocumentLayerState(
            "dirty-review", ViewerDocumentLayerRole.Review,
            Path.Combine(Path.GetTempPath(), "review.usda"), 8, true, true);
        var document = new ViewerDocumentState(
            Guid.NewGuid(), source, review, review.Identifier,
            SourceEditOptIn: true, HasOtherLayerChanges: true);

        ViewerDocumentSaveDecision decision =
            ViewerDocumentSavePolicy.Plan(document, ViewerDocumentSaveAction.SaveSource);

        await Assert.That(decision.IsAllowed).IsFalse();
        await Assert.That(decision.Role).IsEqualTo(ViewerDocumentLayerRole.Source);
        await Assert.That(decision.LayerIdentifier).IsNull();
        await Assert.That(decision.DestinationPath).IsNull();
        await Assert.That(decision.Message)
            .IsEqualTo("Resolve changes in other layers before publishing the source layer.");
    }

    [Test]
    [Arguments(0)]
    [Arguments(2)]
    public async Task NonSourceLayerCannotBeUsedAsTheRequestedSourceTarget(int roleValue)
    {
        ViewerDocumentLayerRole role = (ViewerDocumentLayerRole)roleValue;
        var wrongTarget = new ViewerDocumentLayerState(
            "not-the-source", role, Path.Combine(Path.GetTempPath(), "alternate.usda"), 3, true, true);
        var review = new ViewerDocumentLayerState(
            "review", ViewerDocumentLayerRole.Review, null, 2, true, true);
        var document = new ViewerDocumentState(
            Guid.NewGuid(), wrongTarget, review, wrongTarget.Identifier, SourceEditOptIn: true);

        ViewerDocumentSaveDecision decision =
            ViewerDocumentSavePolicy.Plan(document, ViewerDocumentSaveAction.SaveSource);

        await Assert.That(decision.IsAllowed).IsFalse();
        await Assert.That(decision.Role).IsEqualTo(ViewerDocumentLayerRole.Source);
        await Assert.That(decision.LayerIdentifier).IsNull();
        await Assert.That(decision.DestinationPath).IsNull();
        await Assert.That(decision.Message).IsEqualTo(
            "The exact requested layer is unavailable; another layer will not be substituted.");
    }

    [Test]
    public async Task SaveReviewWithNoReviewTargetDoesNotImplicitlySaveTheAvailableSource()
    {
        var source = new ViewerDocumentLayerState(
            "source", ViewerDocumentLayerRole.Source,
            Path.Combine(Path.GetTempPath(), "source.usda"), 7, true, true);
        var document = new ViewerDocumentState(
            Guid.NewGuid(), source, null, source.Identifier, SourceEditOptIn: true);

        ViewerDocumentSaveDecision decision =
            ViewerDocumentSavePolicy.Plan(document, ViewerDocumentSaveAction.SaveReview);

        await Assert.That(decision.IsAllowed).IsFalse();
        await Assert.That(decision.Role).IsEqualTo(ViewerDocumentLayerRole.Review);
        await Assert.That(decision.LayerIdentifier).IsNull();
        await Assert.That(decision.DestinationPath).IsNull();
        await Assert.That(decision.RequiresDestination).IsFalse();
        await Assert.That(decision.Message).IsEqualTo(
            "The exact requested layer is unavailable; another layer will not be substituted.");
    }

    [Test]
    public async Task MissingDocumentRefusesSourceSaveWithNoTargetOrDestination()
    {
        ViewerDocumentSaveDecision decision =
            ViewerDocumentSavePolicy.Plan(null, ViewerDocumentSaveAction.SaveSource);

        await Assert.That(decision.IsAllowed).IsFalse();
        await Assert.That(decision.Role).IsEqualTo(ViewerDocumentLayerRole.Source);
        await Assert.That(decision.LayerIdentifier).IsNull();
        await Assert.That(decision.DestinationPath).IsNull();
        await Assert.That(decision.RequiresDestination).IsFalse();
        await Assert.That(decision.RequiresExternalChangeCheck).IsFalse();
        await Assert.That(decision.Message).IsEqualTo("Open a document before saving a layer.");
    }
}
