// Copyright (c) marcschier. Licensed under the MIT License.

using Avalonia.Controls;
using Avalonia.Interactivity;

namespace OpenUsd.Viewer;

public sealed partial class MainWindow
{
    private void WireThemeCommands()
    {
        WireThemeCommand(ThemeSystemMenuItem, ViewerCommandIds.ViewThemeSystem);
        WireThemeCommand(ThemeLightMenuItem, ViewerCommandIds.ViewThemeLight);
        WireThemeCommand(ThemeDarkMenuItem, ViewerCommandIds.ViewThemeDark);
        SyncThemeMenu();
    }

    private void WireThemeCommand(MenuItem item, string commandId)
    {
        item.Header = ViewerCommandCatalog.Get(commandId).Label;
        item.Tag = commandId;
        item.Click += OnThemeCommand;
    }

    private async void OnThemeCommand(object? sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (sender is not MenuItem { Tag: string commandId })
        {
            throw new ArgumentException("The theme menu item has no command identity.", nameof(sender));
        }

        // Apply only the UI theme, never ApplySettings: that path also restores render/layout state.
        _settings = ViewerTheme.ExecuteCommand(this, CaptureSettings(), commandId);
        SyncThemeMenu();
        if (!IsAutomatedViewerRun())
        {
            await SaveSettingsAsync();
        }
    }

    private void SyncThemeMenu()
    {
        ThemeSystemMenuItem.IsChecked = _settings.ThemePreference == ViewerThemePreference.System;
        ThemeLightMenuItem.IsChecked = _settings.ThemePreference == ViewerThemePreference.Light;
        ThemeDarkMenuItem.IsChecked = _settings.ThemePreference == ViewerThemePreference.Dark;
    }
}
