// Copyright (c) marcschier. Licensed under the MIT License.

using Avalonia.Automation;
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
        !_documentEditBusy &&
        !_primCommandBusy && !_layerCommandBusy && _documentEditor?.IsSuspended != true;

    private void OnInspectorPropertyQueryChanged(object? sender, TextChangedEventArgs e)
    {
        if (!_rebuildingInspector && !_inspectorPropertiesLoading && _currentInspector is { } inspector)
        {
            _inspectorPropertyPageIndex = 0;
            ShowInspector(inspector);
        }
    }

    private void OnInspectorPropertyFilterChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (!_rebuildingInspector && !_inspectorPropertiesLoading && _currentInspector is { } inspector)
        {
            _inspectorPropertyPageIndex = 0;
            _expandedPropertyName = null;
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
            InspectorPropertyQuery.Text, _inspectorPropertyPageIndex,
            (ViewerPropertyFilter)InspectorPropertyFilter.SelectedIndex,
            (ViewerPropertyGroup)InspectorPropertyGroup.SelectedIndex, inspector.IsCamera);
        _inspectorPropertyPageIndex = _inspectorPropertyPage.PageIndex;
        return _inspectorPropertyPage;
    }

    private void UpdateInspectorPropertyControls()
    {
        UpdateShaderInspectionControls();
        bool showsProperties = InspectorTabs.SelectedItem == PropertiesTab ||
            InspectorTabs.SelectedItem == ValueTab || InspectorTabs.SelectedItem == MetadataTab;
        InspectorPropertyFilters.IsVisible = showsProperties;
        InspectorPropertyNavigation.IsVisible = showsProperties;
        InspectorPropertyTimeControls.IsVisible = showsProperties;
        bool ready = _currentInspector is not null && !_inspectorPropertiesLoading &&
            !_documentBusy && !_shutdownStarted;
        InspectorPropertyQuery.IsEnabled = ready;
        InspectorPropertyFilter.IsEnabled = ready;
        InspectorPropertyGroup.IsEnabled = ready;
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
                else if (child is Button { Tag: ViewerRelationshipSnapshot binding } bindingButton)
                {
                    bindingButton.IsEnabled = ready && _canEditReviewPrim &&
                        ViewerAuthoredEditController.IsDirectMaterialBinding(binding.Name);
                }
            }
        }
    }

    private IReadOnlyList<ViewerAttributeSnapshot> PropertyEditorAttributes(ViewerPrimInspectorSnapshot inspector) =>
        inspector.Attributes.Length <= 256
            ? inspector.Attributes
            : _inspectorPropertyPage?.Attributes ?? Array.Empty<ViewerAttributeSnapshot>();

    private void AddMaterialBindingEditRow(
        ViewerRelationshipSnapshot relationship, ViewerPrimInspectorSnapshot inspector)
    {
        if (!ViewerAuthoredEditController.IsDirectMaterialBinding(relationship.Name))
        {
            return;
        }
        var button = new Button { Content = "Edit Direct Binding...", Tag = relationship, IsEnabled = false };
        AutomationProperties.SetName(button, $"Edit direct material binding {relationship.Name} on {inspector.Path}");
        AutomationProperties.SetAutomationId(button, $"edit.binding:{inspector.Path}.{relationship.Name}");
        ToolTip.SetTip(button,
            "Capture exact review targets. Existing MaterialBindingAPI and native Material targets are required.");
        button.Click += (_, _) => ShowPropertyEditor(binding: relationship);
        var row = new StackPanel();
        row.Children.Add(button);
        ValueRows.Children.Add(row);
    }

    private static string PropertyPageSummary(ViewerPropertyPage page)
    {
        int first = page.StartIndex + 1;
        int last = page.StartIndex + page.Count;
        return FormattableString.Invariant($"Properties {first}-{last} of {page.MatchCount} matches");
    }

    private void ResetInspectorPropertyPaging()
    {
        PrepareShaderInspection(null);
        _inspectorPropertiesLoading = false;
        _inspectorPropertyPage = null;
        _inspectorPropertyPath = null;
        _inspectorPropertyPageIndex = 0;
        _inspectorTimeCode = null;
        _expandedPropertyName = null;
        InspectorPropertyQuery.Text = string.Empty;
        InspectorPropertyFilter.SelectedIndex = 0;
        InspectorPropertyGroup.SelectedIndex = 0;
        UpdateInspectorPropertyControls();
    }
}
