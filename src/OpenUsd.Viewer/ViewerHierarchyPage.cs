// Copyright (c) marcschier. Licensed under the MIT License.

namespace OpenUsd.Viewer;

internal sealed class ViewerHierarchyPage
{
    internal const int PageSize = 64;

    private ViewerHierarchyPage(int totalCount, int pageIndex, ViewerHierarchyTreeNode[] nodes)
    {
        TotalCount = totalCount;
        PageIndex = pageIndex;
        Nodes = Array.AsReadOnly(nodes);
    }

    internal int TotalCount { get; }
    internal int PageIndex { get; }
    internal int StartIndex => PageIndex * PageSize;
    internal bool HasPrevious => PageIndex > 0;
    internal bool HasNext => StartIndex + Nodes.Count < TotalCount;
    internal IReadOnlyList<ViewerHierarchyTreeNode> Nodes { get; }

    internal static ViewerHierarchyPage Create(
        ViewerHierarchySnapshot snapshot,
        string? parentPath,
        int pageIndex,
        string? revealPath = null)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentOutOfRangeException.ThrowIfNegative(pageIndex);
        ViewerHierarchyEntry[] children = snapshot.GetChildren(parentPath);
        int selectedPage = Math.Min(pageIndex, children.Length == 0 ? 0 : (children.Length - 1) / PageSize);
        if (revealPath is not null)
        {
            for (int index = 0; index < children.Length; index++)
            {
                if (ContainsPath(children[index].Path, revealPath))
                {
                    selectedPage = index / PageSize;
                    break;
                }
            }
        }
        int start = selectedPage * PageSize;
        var nodes = new ViewerHierarchyTreeNode[Math.Min(PageSize, children.Length - start)];
        for (int index = 0; index < nodes.Length; index++)
        {
            nodes[index] = new ViewerHierarchyTreeNode(snapshot, children[start + index]);
        }
        return new ViewerHierarchyPage(children.Length, selectedPage, nodes);
    }

    internal static bool ContainsPath(string ancestor, string path) =>
        path.StartsWith(ancestor, StringComparison.Ordinal) &&
        (path.Length == ancestor.Length || path[ancestor.Length] == '/');
}
