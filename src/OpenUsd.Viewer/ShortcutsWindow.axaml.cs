// Copyright (c) marcschier. Licensed under the MIT License.

using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace OpenUsd.Viewer;

/// <summary>
/// Lists the Viewer's keyboard and mouse bindings, opened from Help.
/// </summary>
/// <remarks>
/// The window is deliberately thin: every binding it shows comes from
/// <see cref="ViewerShortcutCatalog"/>, which is checked against the input
/// classifiers that interpret input at runtime. Nothing is written twice, so
/// nothing can drift out of step.
/// </remarks>
internal sealed partial class ShortcutsWindow : Window
{
    /// <summary>Creates the shortcuts window and populates it from the catalog.</summary>
    public ShortcutsWindow()
    {
        InitializeComponent();
        ShortcutItems.ItemsSource = ViewerShortcutCatalog.All;
        CloseButton.Click += (_, _) => Close();
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}
