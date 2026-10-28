// Copyright (c) marcschier. Licensed under the MIT License.

using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using Avalonia.Automation;
using Avalonia.Controls;
using OpenUsd.Editing;

namespace OpenUsd.Viewer;

internal sealed partial class PropertyEditWindow
{
    private readonly List<TextBox> _componentInputs = [];
    private string? _componentType;

    private void ShowComponentCapture(string typeName, UsdLayerEditValue value)
    {
        string[] labels = typeName switch
        {
            "float2" or "texCoord2f" => ["X", "Y"],
            "color3f" or "color3d" => ["R", "G", "B"],
            "color4f" => ["R", "G", "B", "A"],
            "float4" => ["X", "Y", "Z", "W"],
            "float3" or "double3" or "point3f" or "point3d" or "vector3f" or "vector3d" or
                "normal3f" or "normal3d" => ["X", "Y", "Z"],
            _ => []
        };
        if (_componentType != typeName)
        {
            _componentType = typeName;
            PropertyComponentValues.Children.Clear();
            _componentInputs.Clear();
            foreach (string label in labels)
            {
                var input = new TextBox { Name = $"PropertyComponent{label}", MaxLength = 4096 };
                ViewerTextBoxScrolling.ContainCaretRequests(input);
                input.GotFocus += (_, _) => input.BringIntoView();
                AutomationProperties.SetName(input, $"Review component {label}");
                AutomationProperties.SetAutomationId(input, $"edit.propertyComponent:{label}");
                input.PropertyChanged += (_, change) =>
                {
                    if (change.Property == TextBox.TextProperty)
                    {
                        UpdateAvailability();
                    }
                };
                var row = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), ColumnSpacing = 8 };
                row.Children.Add(new TextBlock
                {
                    Text = label,
                    VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
                });
                Grid.SetColumn(input, 1);
                row.Children.Add(input);
                PropertyComponentValues.Children.Add(row);
                _componentInputs.Add(input);
            }
        }
        if (labels.Length == 0)
        {
            return;
        }
        bool hasValue = value.Kind is not (UsdLayerEditValueKind.Absent or UsdLayerEditValueKind.Block);
        Span<double> components = stackalloc double[labels.Length];
        if (hasValue && !ViewerNumericComponents.TryParse(
            ViewerPropertyEditParser.Format(value), components, out string error))
        {
            throw new InvalidDataException($"The authored component value cannot be displayed: {error}");
        }
        for (int index = 0; index < _componentInputs.Count; index++)
        {
            _componentInputs[index].Text = hasValue
                ? components[index].ToString("R", CultureInfo.InvariantCulture) : string.Empty;
        }
        PropertyInputHint.Text =
            $"Enter every {typeName} component. Empty fields have no draft value, not zero. " +
            "Only Set authors a value; use Clear or Block for those operations. " +
            "Values are not clamped, normalized, colour-transformed or converted between units.";
    }

    private bool TryReadComponentValue(
        [NotNullWhen(true)] out UsdLayerEditValue? value, out string error)
    {
        value = null;
        Span<double> component = stackalloc double[1];
        foreach (TextBox input in _componentInputs)
        {
            if (!ViewerNumericComponents.TryParse(input.Text ?? string.Empty, component, out error))
            {
                return false;
            }
        }
        return ViewerPropertyEditParser.TryParse(
            _componentType ?? throw new InvalidOperationException("No component declaration has been captured."),
            string.Join(", ", _componentInputs.Select(static input => input.Text ?? string.Empty)),
            out value, out error);
    }
}
