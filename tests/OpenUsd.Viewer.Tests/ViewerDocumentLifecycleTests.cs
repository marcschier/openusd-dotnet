// Copyright (c) marcschier. Licensed under the MIT License.

namespace OpenUsd.Viewer.Tests;

public sealed class ViewerDocumentLifecycleTests
{
    [Test]
    public async Task ConfirmedReviewAbsenceAllowsACleanCloseWithoutCreatingAnEditingLayer()
    {
        ViewerDocumentState state = CreateDocument(false, false) with
        {
            Review = null,
            ReviewAbsenceConfirmed = true
        };
        int transitions = 0;
        ViewerDocumentTransitionResult result = await ViewerDocumentLifecycle.RunAsync(
            ViewerDocumentTransition.Close, () => state,
            (_, _, _) => throw new InvalidOperationException("A clean document must not prompt."),
            (_, _) => throw new InvalidOperationException("A clean document must not save."),
            (_, _, _) =>
            {
                transitions++;
                return Task.CompletedTask;
            }, CancellationToken.None);

        await Assert.That(result.Status).IsEqualTo(ViewerDocumentTransitionStatus.Completed);
        await Assert.That(transitions).IsEqualTo(1);
        await Assert.That(state.Review).IsNull();
    }

    [Test]
    public async Task DirtyCloseCancelKeepsTheCurrentDocumentUsableWithoutSavingOrDisposingIt()
    {
        ViewerDocumentState state = CreateDocument(reviewDirty: true, sourceDirty: false);
        object liveDocument = new();
        object? activeDocument = liveDocument;
        int saves = 0;
        int transitions = 0;

        ViewerDocumentTransitionResult result = await ViewerDocumentLifecycle.RunAsync(
            ViewerDocumentTransition.Close,
            () => state,
            (_, _, _) => Task.FromResult(ViewerDocumentUnsavedChoice.Cancel),
            (_, _) =>
            {
                saves++;
                return Task.FromResult(true);
            },
            (_, _, _) =>
            {
                transitions++;
                activeDocument = null;
                return Task.CompletedTask;
            },
            CancellationToken.None);

        await Assert.That(result.Status).IsEqualTo(ViewerDocumentTransitionStatus.Cancelled);
        await Assert.That(activeDocument).IsSameReferenceAs(liveDocument);
        await Assert.That(saves).IsEqualTo(0);
        await Assert.That(transitions).IsEqualTo(0);
        await Assert.That(state.Review!.HasChanges).IsTrue();
    }

    [Test]
    public async Task UnavailableReviewStateCannotBeTreatedAsACleanDocument()
    {
        ViewerDocumentState state = CreateDocument(false, false) with { Review = null };
        int transitions = 0;

        ViewerDocumentTransitionResult result = await ViewerDocumentLifecycle.RunAsync(
            ViewerDocumentTransition.Reload,
            () => state,
            (_, _, _) => Task.FromResult(ViewerDocumentUnsavedChoice.Discard),
            (_, _) => Task.FromResult(true),
            (_, _, _) =>
            {
                transitions++;
                return Task.CompletedTask;
            },
            CancellationToken.None);

        await Assert.That(result.Status).IsEqualTo(ViewerDocumentTransitionStatus.Blocked);
        await Assert.That(transitions).IsEqualTo(0);
        await Assert.That(result.Message).Contains("review");
    }

    [Test]
    public async Task ADecisionCannotDiscardEditsThatArrivedWhileThePromptWasOpen()
    {
        ViewerDocumentState current = CreateDocument(true, false);
        int transitions = 0;

        ViewerDocumentTransitionResult result = await ViewerDocumentLifecycle.RunAsync(
            ViewerDocumentTransition.Replace,
            () => current,
            (_, _, _) =>
            {
                current = current with { Review = current.Review! with { Revision = 4 } };
                return Task.FromResult(ViewerDocumentUnsavedChoice.Discard);
            },
            (_, _) => Task.FromResult(true),
            (_, _, _) =>
            {
                transitions++;
                return Task.CompletedTask;
            },
            CancellationToken.None);

        await Assert.That(result.Status).IsEqualTo(ViewerDocumentTransitionStatus.Blocked);
        await Assert.That(transitions).IsEqualTo(0);
        await Assert.That(current.Review!.Revision).IsEqualTo(4UL);
    }

    [Test]
    public async Task SavingReviewDoesNotDiscardOrSilentlySaveSeparatelyDirtySourceOpinions()
    {
        ViewerDocumentState current = CreateDocument(true, true) with { SourceEditOptIn = true };
        int reviewSaves = 0;
        int transitions = 0;

        ViewerDocumentTransitionResult result = await ViewerDocumentLifecycle.RunAsync(
            ViewerDocumentTransition.Close,
            () => current,
            (_, _, _) => Task.FromResult(ViewerDocumentUnsavedChoice.SaveReview),
            (_, _) =>
            {
                reviewSaves++;
                current = current with { Review = current.Review! with { HasChanges = false } };
                return Task.FromResult(true);
            },
            (_, _, _) =>
            {
                transitions++;
                return Task.CompletedTask;
            },
            CancellationToken.None);

        await Assert.That(result.Status).IsEqualTo(ViewerDocumentTransitionStatus.Blocked);
        await Assert.That(reviewSaves).IsEqualTo(1);
        await Assert.That(transitions).IsEqualTo(0);
        await Assert.That(current.Source.HasChanges).IsTrue();
        await Assert.That(current.Review!.HasChanges).IsFalse();
    }

    private static ViewerDocumentState CreateDocument(bool reviewDirty, bool sourceDirty) => new(
        Guid.NewGuid(),
        new ViewerDocumentLayerState("source", ViewerDocumentLayerRole.Source,
            Path.Combine(AppContext.BaseDirectory, "source.usda"), 2, sourceDirty, true),
        new ViewerDocumentLayerState("review", ViewerDocumentLayerRole.Review, null, 3, reviewDirty, true),
        "review");
}
