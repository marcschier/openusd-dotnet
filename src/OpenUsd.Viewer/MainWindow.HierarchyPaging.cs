// Copyright (c) marcschier. Licensed under the MIT License.

using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace OpenUsd.Viewer;

public sealed partial class MainWindow
{
    private const int AutomaticHierarchyItemBudget = 512;
    private const int MaximumHierarchyItems = 4096;
    private readonly Dictionary<string, int> _hierarchyPages = new(StringComparer.Ordinal);
    private readonly HashSet<string> _expandedHierarchyPaths = new(StringComparer.Ordinal);
    private readonly Dictionary<string, TreeViewItem> _hierarchyItems = new(StringComparer.Ordinal);
    private readonly Dictionary<string, HierarchyBranchPager> _hierarchyBranchPagers = new(StringComparer.Ordinal);
    private ViewerHierarchyPage? _hierarchyRootPage;
    private int _automaticHierarchyItemsRemaining;
    private bool _revealHierarchySelection;
    private bool _automaticHierarchyLimited;
    private bool _hierarchyDisplayLimited;

    private void OnHierarchyRootPrevious(object? sender, RoutedEventArgs e) => ChangeHierarchyRootPage(-1);

    private void OnHierarchyRootNext(object? sender, RoutedEventArgs e) => ChangeHierarchyRootPage(1);

    private void ChangeHierarchyRootPage(int direction)
    {
        if (_documentBusy || _shutdownStarted || _hierarchyRootPage is not { } page ||
            (direction < 0 ? !page.HasPrevious : !page.HasNext))
        {
            return;
        }
        _hierarchyPages[string.Empty] = page.PageIndex + direction;
        RenderHierarchy(revealSelection: false);
    }

    private StackPanel CreatePagedHierarchyHeader(TreeViewItem item, ViewerHierarchyTreeNode node)
    {
        StackPanel identity = CreateHierarchyItemHeader(node.Entry);
        if (node.Entry.ChildCount <= ViewerHierarchyPage.PageSize)
        {
            return identity;
        }
        var previous = new Button { Content = "Previous", IsEnabled = false };
        var next = new Button { Content = "Next", IsEnabled = false };
        var state = new TextBlock
        {
            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
            Classes = { "viewer-caption" }
        };
        var panel = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"),
            ColumnSpacing = 6,
            IsVisible = false
        };
        Grid.SetColumn(state, 1);
        Grid.SetColumn(next, 2);
        panel.Children.Add(previous);
        panel.Children.Add(state);
        panel.Children.Add(next);
        AutomationProperties.SetName(previous, $"Previous child page for {node.Entry.Path}");
        AutomationProperties.SetAutomationId(previous, $"hierarchy.previous:{node.Entry.Path}");
        AutomationProperties.SetName(next, $"Next child page for {node.Entry.Path}");
        AutomationProperties.SetAutomationId(next, $"hierarchy.next:{node.Entry.Path}");
        AutomationProperties.SetName(state, $"Child results for {node.Entry.Path}");
        previous.Click += (_, _) => ChangeHierarchyChildPage(item, node, -1);
        next.Click += (_, _) => ChangeHierarchyChildPage(item, node, 1);
        _hierarchyBranchPagers[node.Entry.Path] = new HierarchyBranchPager(panel, state, previous, next);
        var header = new StackPanel { Spacing = 4 };
        header.Children.Add(identity);
        header.Children.Add(panel);
        return header;
    }

    private bool PopulateHierarchyChildren(
        TreeViewItem item,
        ViewerHierarchyTreeNode node,
        ref TreeViewItem? selectedItem)
    {
        bool containsSelection = _revealHierarchySelection && _selectionState.PrimPath is { } selected &&
            ViewerHierarchyPage.ContainsPath(node.Entry.Path, selected);
        ViewerHierarchyPage page = node.GetChildrenPage(
            _hierarchyPages.GetValueOrDefault(node.Entry.Path),
            containsSelection ? _selectionState.PrimPath : null);
        int count = page.Nodes.Count;
        if (count > MaximumHierarchyItems - _hierarchyItems.Count)
        {
            _hierarchyDisplayLimited = true;
            return false;
        }
        if (!containsSelection && count > _automaticHierarchyItemsRemaining)
        {
            _automaticHierarchyLimited = true;
            return false;
        }
        _hierarchyPages[node.Entry.Path] = page.PageIndex;
        item.ItemsSource = CreateTreeItems(page.Nodes, ref selectedItem);
        _expandedHierarchyPaths.Add(node.Entry.Path);
        if (_hierarchyBranchPagers.TryGetValue(node.Entry.Path, out HierarchyBranchPager? pager))
        {
            pager.Page = page;
            pager.State.Text = HierarchyPageSummary("Children", page);
        }
        return true;
    }

    private void InitializeHierarchyChildren(
        TreeViewItem item,
        ViewerHierarchyTreeNode node,
        ref TreeViewItem? selectedItem)
    {
        if (node.Entry.ChildCount == 0)
        {
            return;
        }
        bool containsSelection = _revealHierarchySelection && _selectionState.PrimPath is { } selected &&
            ViewerHierarchyPage.ContainsPath(node.Entry.Path, selected);
        bool expand = _expandedHierarchyPaths.Contains(node.Entry.Path) ||
            ViewerHierarchyExpansionPolicy.ShouldMaterializeChildren(
                node.Entry, _hierarchyExpandDepth, containsSelection);
        if (expand && PopulateHierarchyChildren(item, node, ref selectedItem))
        {
            item.IsExpanded = true;
        }
        else
        {
            SetHierarchyPlaceholder(item);
        }
        item.Expanded += OnTreeItemExpanded;
        item.Collapsed += OnTreeItemCollapsed;
    }

    private void ChangeHierarchyChildPage(TreeViewItem item, ViewerHierarchyTreeNode node, int direction)
    {
        if (_documentBusy || _shutdownStarted || !IsCurrentHierarchyItem(item, node) ||
            !_hierarchyBranchPagers.TryGetValue(node.Entry.Path, out HierarchyBranchPager? pager) ||
            pager.Page is not { } page || (direction < 0 ? !page.HasPrevious : !page.HasNext))
        {
            return;
        }
        _rebuildingHierarchy = true;
        try
        {
            _hierarchyPages[node.Entry.Path] = page.PageIndex + direction;
            RemoveHierarchyChildren(item);
            _automaticHierarchyItemsRemaining = AutomaticHierarchyItemBudget;
            _revealHierarchySelection = false;
            TreeViewItem? selectedItem = null;
            if (!PopulateHierarchyChildren(item, node, ref selectedItem))
            {
                SetHierarchyPlaceholder(item);
                item.IsExpanded = false;
            }
            if (selectedItem is not null)
            {
                StageHierarchy.SelectedItem = selectedItem;
            }
            UpdateHierarchyNavigation();
        }
        finally
        {
            _rebuildingHierarchy = false;
        }
    }

    private void OnTreeItemCollapsed(object? sender, RoutedEventArgs e)
    {
        if (_rebuildingHierarchy || sender is not TreeViewItem { Tag: ViewerHierarchyTreeNode node } item ||
            !ReferenceEquals(e.Source, item) || !IsCurrentHierarchyItem(item, node))
        {
            return;
        }
        _rebuildingHierarchy = true;
        try
        {
            RemoveHierarchyChildren(item);
            _expandedHierarchyPaths.Remove(node.Entry.Path);
            SetHierarchyPlaceholder(item);
            _hierarchyDisplayLimited = false;
            UpdateHierarchyNavigation();
        }
        finally
        {
            _rebuildingHierarchy = false;
        }
    }

    private void RemoveHierarchyChildren(TreeViewItem parent)
    {
        var pending = new Stack<TreeViewItem>(parent.Items.OfType<TreeViewItem>());
        while (pending.TryPop(out TreeViewItem? item))
        {
            foreach (TreeViewItem child in item.Items.OfType<TreeViewItem>())
            {
                pending.Push(child);
            }
            if (item.Tag is ViewerHierarchyTreeNode node)
            {
                _hierarchyItems.Remove(node.Entry.Path);
                _hierarchyBranchPagers.Remove(node.Entry.Path);
                _hierarchyPages.Remove(node.Entry.Path);
                _expandedHierarchyPaths.Remove(node.Entry.Path);
            }
        }
        parent.ItemsSource = null;
    }

    private bool IsCurrentHierarchyItem(TreeViewItem item, ViewerHierarchyTreeNode node) =>
        _hierarchyItems.TryGetValue(node.Entry.Path, out TreeViewItem? current) && ReferenceEquals(item, current);

    private static void SetHierarchyPlaceholder(TreeViewItem item) =>
        item.ItemsSource = new[] { new TreeViewItem { Header = "Expand to browse children", Focusable = false } };

    private void UpdateHierarchyNavigation()
    {
        bool ready = _coordinator is not null && !_documentBusy && !_shutdownStarted;
        HierarchyRootPageControls.IsVisible = _hierarchyRootPage is { TotalCount: > ViewerHierarchyPage.PageSize };
        HierarchyRootPrevious.IsEnabled = ready && _hierarchyRootPage is { HasPrevious: true };
        HierarchyRootNext.IsEnabled = ready && _hierarchyRootPage is { HasNext: true };
        HierarchyRootPageState.Text = _hierarchyRootPage is { } roots
            ? HierarchyPageSummary("Roots", roots) : string.Empty;
        foreach ((string path, HierarchyBranchPager pager) in _hierarchyBranchPagers)
        {
            pager.Panel.IsVisible = _hierarchyItems[path].IsExpanded;
            pager.Previous.IsEnabled = ready && pager.Page is { HasPrevious: true };
            pager.Next.IsEnabled = ready && pager.Page is { HasNext: true };
        }
        if (_hierarchyRootPage is not null)
        {
            HierarchyState.IsVisible = _hierarchyRootPage.TotalCount == 0 ||
                _automaticHierarchyLimited || _hierarchyDisplayLimited;
            HierarchyState.Text = _hierarchyRootPage.TotalCount == 0
                ? _hierarchy.Entries.Length == 0
                    ? "The stage contains no traversable prims."
                    : "No prims match the current filter."
                : _hierarchyDisplayLimited
                    ? "Hierarchy display limit reached. Collapse a branch or narrow the filter to continue."
                    : "Automatic expansion paused after the 512-prim budget. Expand branches as needed.";
        }
    }

    private static string HierarchyPageSummary(string label, ViewerHierarchyPage page)
    {
        int start = page.Nodes.Count == 0 ? 0 : page.StartIndex + 1;
        int end = page.StartIndex + page.Nodes.Count;
        return FormattableString.Invariant($"{label} {start}-{end} of {page.TotalCount}");
    }

    private void ResetHierarchyPaging()
    {
        _hierarchyPages.Clear();
        _expandedHierarchyPaths.Clear();
        _hierarchyItems.Clear();
        _hierarchyBranchPagers.Clear();
        _hierarchyRootPage = null;
        _automaticHierarchyLimited = false;
        _hierarchyDisplayLimited = false;
        UpdateHierarchyNavigation();
    }

    private sealed class HierarchyBranchPager(Grid panel, TextBlock state, Button previous, Button next)
    {
        internal Grid Panel { get; } = panel;
        internal TextBlock State { get; } = state;
        internal Button Previous { get; } = previous;
        internal Button Next { get; } = next;
        internal ViewerHierarchyPage? Page { get; set; }
    }
}
