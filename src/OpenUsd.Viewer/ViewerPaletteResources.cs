// Copyright (c) marcschier. Licensed under the MIT License.

using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Immutable;

namespace OpenUsd.Viewer;

internal static class ViewerPaletteResources
{
    private static readonly (string Brush, string[] Aliases)[] FluentBrushAliases =
    [
        ("ViewerSurfaceRaisedBrush",
        [
            "SystemControlFocusVisualSecondaryBrush", "ButtonBackground",
            "TextControlBackground", "TextControlBackgroundPointerOver",
            "TextControlBackgroundFocused", "ComboBoxBackground", "ComboBoxDropDownBackground",
            "MenuFlyoutPresenterBackground", "ToolTipBackground"
        ]),
        ("ViewerSurfaceChromeBrush",
        [
            "ButtonBackgroundDisabled", "AccentButtonBackgroundDisabled",
            "TextControlBackgroundDisabled", "ComboBoxBackgroundDisabled"
        ]),
        ("ViewerSelectionBrush",
        [
            "ButtonBackgroundPointerOver", "ButtonBackgroundPressed",
            "ComboBoxBackgroundPointerOver", "ComboBoxBackgroundPressed",
            "ComboBoxItemBackgroundSelected", "MenuFlyoutItemBackgroundPointerOver",
            "TreeViewItemBackgroundPointerOver", "TreeViewItemBackgroundSelected",
            "TreeViewItemBackgroundSelectedPointerOver", "TreeViewItemBackgroundSelectedPressed",
            "TabItemHeaderBackgroundSelected", "TabItemHeaderBackgroundSelectedPointerOver"
        ]),
        ("ViewerTextPrimaryBrush",
        [
            "ButtonForeground", "TextControlForeground", "TextControlForegroundPointerOver",
            "TextControlForegroundFocused", "ComboBoxForeground", "ComboBoxForegroundFocused",
            "MenuFlyoutItemForeground", "TreeViewItemForeground",
            "TabItemHeaderForegroundUnselectedPointerOver", "ToolTipForeground"
        ]),
        ("ViewerTextSecondaryBrush",
        [
            "TextControlPlaceholderForeground", "TextControlPlaceholderForegroundPointerOver",
            "TextControlPlaceholderForegroundFocused", "TabItemHeaderForegroundUnselected"
        ]),
        ("ViewerTextSelectedBrush",
        [
            "ButtonForegroundPointerOver", "ButtonForegroundPressed",
            "ComboBoxItemForegroundSelected", "MenuFlyoutItemForegroundPointerOver",
            "TreeViewItemForegroundPointerOver", "TreeViewItemForegroundSelected",
            "TreeViewItemForegroundSelectedPointerOver", "TreeViewItemForegroundSelectedPressed",
            "TabItemHeaderForegroundSelected", "TabItemHeaderForegroundSelectedPointerOver"
        ]),
        ("ViewerTextDisabledBrush",
        [
            "ButtonForegroundDisabled", "AccentButtonForegroundDisabled",
            "TextControlForegroundDisabled", "TextControlPlaceholderForegroundDisabled",
            "ComboBoxForegroundDisabled", "MenuFlyoutItemForegroundDisabled"
        ]),
        ("ViewerTextOnAccentBrush",
        [
            "AccentButtonForeground", "AccentButtonForegroundPointerOver",
            "AccentButtonForegroundPressed", "CheckBoxCheckGlyphForegroundChecked",
            "CheckBoxCheckGlyphForegroundCheckedPointerOver",
            "CheckBoxCheckGlyphForegroundCheckedPressed"
        ]),
        ("ViewerBorderBrush",
        [
            "ButtonBorderBrushDisabled", "AccentButtonBorderBrushDisabled",
            "TextControlBorderBrushDisabled", "ComboBoxBorderBrushDisabled",
            "ComboBoxDropDownBorderBrush", "MenuFlyoutPresenterBorderBrush", "ToolTipBorderBrush"
        ]),
        ("ViewerControlBorderBrush",
        [
            "ButtonBorderBrush", "TextControlBorderBrush", "ComboBoxBorderBrush"
        ]),
        ("ViewerFocusBrush",
        [
            "SystemControlFocusVisualPrimaryBrush", "ButtonBorderBrushPointerOver",
            "ButtonBorderBrushPressed", "AccentButtonBorderBrushPointerOver",
            "AccentButtonBorderBrushPressed", "TextControlBorderBrushPointerOver",
            "TextControlBorderBrushFocused", "ComboBoxBorderBrushPointerOver",
            "ComboBoxBorderBrushPressed", "TreeViewItemBorderBrushSelected"
        ]),
        ("ViewerAccentBrush",
        [
            "SystemControlHighlightAccentBrush", "AccentButtonBackground",
            "AccentButtonBorderBrush", "CheckBoxCheckBackgroundFillChecked",
            "TabItemHeaderSelectedPipeFill", "SliderThumbBackground", "SliderTrackValueFill"
        ]),
        ("ViewerAccentHoverBrush",
        [
            "AccentButtonBackgroundPointerOver", "CheckBoxCheckBackgroundFillCheckedPointerOver",
            "SliderThumbBackgroundPointerOver", "SliderTrackValueFillPointerOver"
        ]),
        ("ViewerAccentPressedBrush",
        [
            "AccentButtonBackgroundPressed", "CheckBoxCheckBackgroundFillCheckedPressed",
            "SliderThumbBackgroundPressed", "SliderTrackValueFillPressed"
        ])
    ];

    internal static void Populate(IResourceDictionary resources)
    {
        foreach (var provider in resources.ThemeDictionaries.Values)
        {
            if (provider is not ResourceDictionary palette)
            {
                throw new InvalidOperationException("Viewer palettes must be resource dictionaries.");
            }

            // Immutable brushes belong to a variant, not to the dispatcher that first loaded it.
            // DynamicResource consumers switch variants; no mutable brush bindings are shared.
            foreach (object key in palette.Keys.ToArray())
            {
                if (key is string name &&
                    name.StartsWith("Viewer", StringComparison.Ordinal) &&
                    name.EndsWith("Color", StringComparison.Ordinal) &&
                    palette[key] is Color color)
                {
                    palette.Add(string.Concat(name.AsSpan(0, name.Length - 5), "Brush"),
                        new ImmutableSolidColorBrush(color));
                }
            }
            foreach ((string brush, string[] aliases) in FluentBrushAliases)
            {
                object? value = palette[brush];
                foreach (string alias in aliases)
                {
                    palette.Add(alias, value);
                }
            }
        }
    }
}
