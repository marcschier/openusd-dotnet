// Copyright (c) marcschier. Licensed under the MIT License.

namespace OpenUsd.Viewer;

internal interface IViewerEditHistoryView
{
    bool CanUndo { get; }

    bool CanRedo { get; }

    int UndoDepth { get; }

    int RedoDepth { get; }

    string UndoDescription { get; }

    string RedoDescription { get; }
}
