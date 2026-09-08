// Copyright (c) marcschier. Licensed under the MIT License.

using Avalonia.Controls;
using Avalonia.Input;

namespace OpenUsd.Viewer;

internal enum ViewerDocumentActionChoice
{
    Cancel,
    Accept,
    Alternate
}

internal sealed partial class DocumentActionWindow : Window
{
    internal DocumentActionWindow(string heading, string details, string accept, string? alternate = null)
    {
        InitializeComponent();
        ViewerWindowTheme.Attach(this);
        DocumentActionHeading.Text = heading;
        DocumentActionDetails.Text = details;
        AcceptDocumentActionButton.Content = accept;
        AlternateDocumentActionButton.Content = alternate;
        AlternateDocumentActionButton.IsVisible = alternate is not null;
        AcceptDocumentActionButton.Click += (_, _) =>
        {
            Choice = ViewerDocumentActionChoice.Accept;
            Close();
        };
        AlternateDocumentActionButton.Click += (_, _) =>
        {
            Choice = ViewerDocumentActionChoice.Alternate;
            Close();
        };
        CancelDocumentActionButton.Click += (_, _) => Close();
        Opened += (_, _) => CancelDocumentActionButton.Focus();
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                e.Handled = true;
                Close();
            }
        };
    }

    internal ViewerDocumentActionChoice Choice { get; private set; }
}
