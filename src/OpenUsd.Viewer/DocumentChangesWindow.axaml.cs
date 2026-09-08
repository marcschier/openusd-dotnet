// Copyright (c) marcschier. Licensed under the MIT License.

using Avalonia.Controls;
using Avalonia.Input;

namespace OpenUsd.Viewer;

internal sealed partial class DocumentChangesWindow : Window
{
    internal const string SaveRestriction =
        "This session has no verified source origin for portable review saving. " +
        "Cancel keeps the current document; Discard does not write any source file.";

    internal DocumentChangesWindow(
        ViewerDocumentObservation document, ViewerDocumentTransition transition,
        bool canSaveReview = false, string? saveRestriction = null)
    {
        InitializeComponent();
        ViewerWindowTheme.Attach(this);
        DocumentTransitionHeading.Text = $"Resolve unsaved changes before {transition.ToString().ToLowerInvariant()}.";
        List<string> details = [];
        if (document.State.Review is { HasChanges: true } review)
        {
            details.Add($"Unsaved review: {review.Identifier} (revision {review.Revision})");
        }
        if (document.State.Source.HasChanges)
        {
            details.Add($"Separately dirty SOURCE: {document.State.Source.Identifier}");
        }
        foreach (string layer in document.OtherChangedLayers.Take(8))
        {
            details.Add($"Other changed layer/session composition: {layer}");
        }
        if (document.OtherChangedLayers.Count > 8)
        {
            details.Add($"And {document.OtherChangedLayers.Count - 8} other changed layers.");
        }
        UnsavedDocumentDetails.Text = string.Join("\n\n", details);
        DocumentSaveRestriction.Text = canSaveReview
            ? "Save writes only the review document. Separately dirty source or session layers are not saved."
            : saveRestriction ?? SaveRestriction;
        SaveDocumentReviewButton.IsEnabled = canSaveReview;
        ToolTip.SetTip(SaveDocumentReviewButton, DocumentSaveRestriction.Text);
        SaveDocumentReviewButton.Click += (_, _) =>
        {
            Choice = ViewerDocumentUnsavedChoice.SaveReview;
            Close();
        };
        DiscardDocumentChangesButton.Click += (_, _) =>
        {
            Choice = ViewerDocumentUnsavedChoice.Discard;
            Close();
        };
        CancelDocumentTransitionButton.Click += (_, _) => Close();
        Opened += (_, _) => CancelDocumentTransitionButton.Focus();
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                e.Handled = true;
                Close();
            }
        };
    }

    internal ViewerDocumentUnsavedChoice Choice { get; private set; } = ViewerDocumentUnsavedChoice.Cancel;
}
