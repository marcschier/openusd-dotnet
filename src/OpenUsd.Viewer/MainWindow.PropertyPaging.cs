// Copyright (c) marcschier. Licensed under the MIT License.

using Avalonia.Controls;
using Avalonia.Interactivity;

namespace OpenUsd.Viewer;

public sealed partial class MainWindow
{
    private ViewerPropertyPage? _inspectorPropertyPage;
    private string? _inspectorPropertyPath;
    private int _inspectorPropertyPageIndex;
    private bool _inspectorPropertiesLoading;
    private double? _inspectorTimeCode;
    private string? _expandedPropertyName;

    private async void OnInspectorPropertyDefaultTime(object? sender, RoutedEventArgs e) =>
        await ChangeInspectorTimeAsync(null);

    private async void OnInspectorPropertyCurrentTime(object? sender, RoutedEventArgs e)
    {
        double timeCode;
        lock (_timelineGate)
        {
            timeCode = _currentTimeCode;
        }
        await ChangeInspectorTimeAsync(timeCode);
    }

    private async Task ChangeInspectorTimeAsync(double? timeCode)
    {
        if (!CanReadInspectorSnapshot() || _currentInspector is not { } inspector ||
            _documentLifetime is not { } lifetime)
        {
            return;
        }
        _inspectorTimeCode = timeCode;
        await StartInspectorLoadAsync(inspector.Path, lifetime.Token);
    }

    private bool CanReadInspectorSnapshot() =>
        _currentInspector is not null && _coordinator is not null &&
        _documentLifetime is { IsCancellationRequested: false } &&
        !_inspectorPropertiesLoading && !_documentBusy && !_shutdownStarted &&
        !_documentSnapshotRefreshBusy && !_documentEditBusy &&
        !_primCommandBusy && !_layerCommandBusy && _documentEditor?.IsSuspended != true;

    private void OnInspectorPropertyQueryChanged(object? sender, TextChangedEventArgs e)
    {
        if (!_rebuildingInspector && !_inspectorPropertiesLoading && _currentInspector is { } inspector)
        {
            _inspectorPropertyPageIndex = 0;
            ShowInspector(inspector);
        }
    }

    private void OnInspectorPropertyPrevious(object? sender, RoutedEventArgs e)
    {
        if (InspectorPropertyPrevious.IsEnabled && _inspectorPropertyPage is { HasPrevious: true } &&
            _currentInspector is { } inspector)
        {
            _inspectorPropertyPageIndex--;
            ShowInspector(inspector);
        }
    }

    private void OnInspectorPropertyNext(object? sender, RoutedEventArgs e)
    {
        if (InspectorPropertyNext.IsEnabled && _inspectorPropertyPage is { HasNext: true } &&
            _currentInspector is { } inspector)
        {
            _inspectorPropertyPageIndex++;
            ShowInspector(inspector);
        }
    }

    private ViewerPropertyPage PrepareInspectorPropertyPage(ViewerPrimInspectorSnapshot inspector)
    {
        if (!string.Equals(_inspectorPropertyPath, inspector.Path, StringComparison.Ordinal))
        {
            _inspectorPropertyPath = inspector.Path;
            _inspectorPropertyPageIndex = 0;
            _expandedPropertyName = null;
        }
        _inspectorPropertiesLoading = false;
        _inspectorPropertyPage = ViewerPropertyPage.Create(inspector.Attributes, inspector.Relationships,
            InspectorPropertyQuery.Text, _inspectorPropertyPageIndex);
        _inspectorPropertyPageIndex = _inspectorPropertyPage.PageIndex;
        return _inspectorPropertyPage;
    }

    private void UpdateInspectorPropertyControls()
    {
        bool showsProperties = InspectorTabs.SelectedItem == PropertiesTab ||
            InspectorTabs.SelectedItem == ValueTab || InspectorTabs.SelectedItem == MetadataTab;
        InspectorPropertyQuery.IsVisible = showsProperties;
        InspectorPropertyNavigation.IsVisible = showsProperties;
        InspectorPropertyTimeControls.IsVisible = showsProperties;
        bool ready = _currentInspector is not null && !_inspectorPropertiesLoading &&
            !_documentBusy && !_shutdownStarted;
        InspectorPropertyQuery.IsEnabled = ready;
        InspectorPropertyPrevious.IsEnabled = ready && _inspectorPropertyPage is { HasPrevious: true };
        InspectorPropertyNext.IsEnabled = ready && _inspectorPropertyPage is { HasNext: true };
        bool canRead = CanReadInspectorSnapshot();
        InspectorPropertyDefaultTime.IsEnabled = canRead;
        InspectorPropertyCurrentTime.IsEnabled = canRead;
        InspectorPropertyTimeState.Text = _inspectorPropertiesLoading
            ? "Reading property snapshot..."
            : _currentInspector is { PropertySnapshot: { } snapshot }
                ? ViewerPropertyInspectionFormatter.Time(snapshot.TimeCode)
                : "No property snapshot";
        InspectorPropertyPageState.Text = _inspectorPropertiesLoading
            ? "Loading properties..."
            : _inspectorPropertyPage is not { } page
                ? "Select a prim to browse properties."
                : page.MatchCount == 0
                    ? page.TotalCount == 0
                        ? "This prim has no properties."
                        : "No properties match the current filter."
                    : PropertyPageSummary(page);

        foreach (Control control in ValueRows.Children)
        {
            if (control is not StackPanel row)
            {
                continue;
            }
            foreach (Control child in row.Children)
            {
                if (child is Button { Tag: ViewerAttributeSnapshot attribute } button)
                {
                    button.IsEnabled = ready && EditPropertyMenuItem.IsEnabled &&
                        ViewerPropertyEditParser.Supports(attribute.TypeName);
                }
            }
        }
    }

    private IReadOnlyList<ViewerAttributeSnapshot> PropertyEditorAttributes(ViewerPrimInspectorSnapshot inspector) =>
        inspector.Attributes.Length <= 256
            ? inspector.Attributes
            : _inspectorPropertyPage?.Attributes ?? Array.Empty<ViewerAttributeSnapshot>();

    private static string PropertyPageSummary(ViewerPropertyPage page)
    {
        int first = page.StartIndex + 1;
        int last = page.StartIndex + page.Count;
        return FormattableString.Invariant($"Properties {first}-{last} of {page.MatchCount} matches");
    }

    private void ResetInspectorPropertyPaging()
    {
        _inspectorPropertiesLoading = false;
        _inspectorPropertyPage = null;
        _inspectorPropertyPath = null;
        _inspectorPropertyPageIndex = 0;
        _inspectorTimeCode = null;
        _expandedPropertyName = null;
        InspectorPropertyQuery.Text = string.Empty;
        UpdateInspectorPropertyControls();
    }
}
