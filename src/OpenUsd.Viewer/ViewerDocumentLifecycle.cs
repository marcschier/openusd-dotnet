// Copyright (c) marcschier. Licensed under the MIT License.

namespace OpenUsd.Viewer;

internal enum ViewerDocumentTransition
{
    Close,
    Replace,
    Reload,
    RevertReview
}

internal enum ViewerDocumentUnsavedChoice
{
    SaveReview,
    Discard,
    Cancel
}

internal enum ViewerDocumentTransitionStatus
{
    Completed,
    Cancelled,
    Blocked
}

internal sealed record ViewerDocumentTransitionResult(ViewerDocumentTransitionStatus Status, string Message);

internal static class ViewerDocumentLifecycle
{
    internal static async Task<ViewerDocumentTransitionResult> RunAsync(
        ViewerDocumentTransition transition,
        Func<ViewerDocumentState> readState,
        Func<ViewerDocumentState, ViewerDocumentTransition, CancellationToken,
            Task<ViewerDocumentUnsavedChoice>> prompt,
        Func<ViewerDocumentState, CancellationToken, Task<bool>> saveReview,
        Func<ViewerDocumentState, ViewerDocumentTransition, CancellationToken, Task> commitTransition,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(readState);
        ArgumentNullException.ThrowIfNull(prompt);
        ArgumentNullException.ThrowIfNull(saveReview);
        ArgumentNullException.ThrowIfNull(commitTransition);
        if (!Enum.IsDefined(transition))
        {
            throw new ArgumentOutOfRangeException(nameof(transition));
        }
        cancellationToken.ThrowIfCancellationRequested();
        ViewerDocumentState expected = readState();
        if (expected.DocumentId == Guid.Empty ||
            (expected.Review is not { Role: ViewerDocumentLayerRole.Review } &&
            !(expected.Review is null && expected.ReviewAbsenceConfirmed)) ||
            expected.Source.Role != ViewerDocumentLayerRole.Source)
        {
            return Blocked("Exact source and review-layer state is required before a document transition.");
        }
        if (expected.IsBusy)
        {
            return Blocked("Wait for the current document operation or cancel it first.");
        }
        bool reviewOnly = transition == ViewerDocumentTransition.RevertReview;
        bool needsDecision = expected.Review?.HasChanges == true ||
            (!reviewOnly && (expected.Source.HasChanges || expected.HasOtherLayerChanges)) || reviewOnly;
        if (needsDecision)
        {
            ViewerDocumentUnsavedChoice choice = await prompt(expected, transition, cancellationToken)
                .ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            if (choice == ViewerDocumentUnsavedChoice.Cancel)
            {
                return new ViewerDocumentTransitionResult(
                    ViewerDocumentTransitionStatus.Cancelled, "Cancelled; the current document is unchanged.");
            }
            if (!Enum.IsDefined(choice))
            {
                throw new InvalidOperationException("The unsaved-changes prompt returned an unknown choice.");
            }
            if (readState() != expected)
            {
                return Blocked("The document changed while awaiting a decision. Review the new state and try again.");
            }
            if (choice == ViewerDocumentUnsavedChoice.SaveReview)
            {
                ViewerDocumentSaveDecision save = ViewerDocumentSavePolicy.Plan(
                    expected, ViewerDocumentSaveAction.SaveReview);
                if (!save.IsAllowed)
                {
                    return Blocked(save.Message);
                }
                if (!await saveReview(expected, cancellationToken).ConfigureAwait(false))
                {
                    return Blocked("The review save did not complete; the current document was kept.");
                }
                ViewerDocumentState current = readState();
                if (current.DocumentId != expected.DocumentId ||
                    current.Source.Identifier != expected.Source.Identifier ||
                    current.Review?.Identifier != expected.Review?.Identifier ||
                    current.IsBusy || current.Review?.HasChanges == true ||
                    (!reviewOnly && (current.Source.HasChanges || current.HasOtherLayerChanges)))
                {
                    return Blocked(
                        "The document or its layers changed, or unsaved edits remain. The current document was kept.");
                }
                expected = current;
            }
        }
        cancellationToken.ThrowIfCancellationRequested();
        if (readState() != expected)
        {
            return Blocked("The document changed before the transition; the current document was kept.");
        }

        // The live adapter must compare this ticket again while holding the document/scheduler gate.
        await commitTransition(expected, transition, cancellationToken).ConfigureAwait(false);
        return new ViewerDocumentTransitionResult(
            ViewerDocumentTransitionStatus.Completed, "Document transition complete.");
    }

    private static ViewerDocumentTransitionResult Blocked(string message) =>
        new(ViewerDocumentTransitionStatus.Blocked, message);
}
