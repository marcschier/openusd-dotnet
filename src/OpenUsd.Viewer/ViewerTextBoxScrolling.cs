// Copyright (c) marcschier. Licensed under the MIT License.

using Avalonia.Controls;

namespace OpenUsd.Viewer;

internal static class ViewerTextBoxScrolling
{
    internal static void ContainCaretRequests(TextBox editor)
    {
        editor.AddHandler(Control.RequestBringIntoViewEvent, (_, args) =>
        {
            // Inner caret requests must not scroll an outer panel.
            if (args.TargetObject is not null && !ReferenceEquals(args.TargetObject, editor))
            {
                args.Handled = true;
            }
        });
    }
}
