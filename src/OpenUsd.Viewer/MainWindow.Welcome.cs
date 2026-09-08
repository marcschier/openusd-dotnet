// Copyright (c) marcschier. Licensed under the MIT License.

using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;

namespace OpenUsd.Viewer;

public sealed partial class MainWindow
{
    private const string RecentCommandPrefix = "file.recent:";
    private int _recentStageCount;
    private bool _workspaceCaptureBusy;

    private void InitializeWelcome()
    {
        OpenSampleMenuItem.Click += OnOpenSampleClick;
        OpenSampleMenuItem.Bind(IsEnabledProperty, OpenStageMenuItem.GetObservable(IsEnabledProperty));
        _commands.Connect(WelcomeOpenButton, ViewerCommandIds.FileOpenStage);
        _commands.Connect(WelcomeSampleButton, ViewerCommandIds.FileOpenSample);
        _commands.Connect(WelcomeCommandsButton, ViewerCommandIds.ViewCommandPalette);
        _commands.Connect(WelcomeCompareButton, ViewerCommandIds.FileCompareCaptures);
        _commands.Connect(WelcomeShortcutsButton, ViewerCommandIds.HelpShortcuts);
        WelcomeRuntimeHint.Text = string.IsNullOrWhiteSpace(ViewerStartupOptions.PluginPath)
            ? "Storm needs the OpenUSD plugin directory supplied by the desktop bundle or embedding host. " +
                "Rendering failures appear here; use Render > Renderer to choose another available backend."
            : "Rendering uses the installed native OpenUSD runtime and graphics driver. " +
                "If a backend is unavailable, choose another under Render > Renderer.";
        UpdateDocumentPresentation();
    }

    private async void OnOpenSampleClick(object? sender, RoutedEventArgs e)
    {
        e.Handled = true;
        try
        {
            string path = await ViewerSampleScene.PrepareAsync(_settingsStore.RootPath, _viewerLifetime.Token);
            await OpenStageAndReportAsync(path);
        }
        catch (OperationCanceledException) when (_viewerLifetime.IsCancellationRequested)
        {
        }
        catch (IOException exception)
        {
            ShowError($"The bundled sample could not be prepared: {exception.Message}");
        }
        catch (UnauthorizedAccessException exception)
        {
            ShowError($"The bundled sample could not be prepared: {exception.Message}");
        }
    }

    private void UpdateDocumentPresentation()
    {
        // No welcome overlay may share airspace with a live Storm native child.
        bool showViewport = _coordinator is not null || _documentBusy;
        WelcomePanel.IsVisible = false;
        MainContentGrid.IsVisible = showViewport;
        WelcomePanel.IsVisible = !showViewport;
        DocumentTimelineHost.IsVisible = showViewport;
        if (showViewport)
        {
            UpdateLayout();
        }
        RecentStagesMenu.IsEnabled = _recentStageCount > 0 && !_documentBusy;
        UpdateOperationChrome();
    }

    private void UpdateOperationChrome()
    {
        UpdateDocumentCommands();
        WorkspaceProgress.IsVisible = _documentBusy || _workspaceCaptureBusy || _validationBusy;
        WorkspaceProgress.SetValue(ToolTip.TipProperty,
            _workspaceCaptureBusy ? "Capturing a frame..." :
            _validationBusy ? "Validating stage..." : ViewerStatus.Text);
    }

    private void RefreshWelcomeRecentItems(string[] paths)
    {
        _recentStageCount = paths.Length;
        WelcomeRecentItems.Children.Clear();
        WelcomeRecentState.IsVisible = paths.Length == 0;
        foreach (string path in paths)
        {
            var title = new TextBlock { Text = Path.GetFileName(path), TextTrimming = TextTrimming.CharacterEllipsis };
            var directory = new TextBlock
            {
                Text = Path.GetDirectoryName(path),
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            directory.Classes.Add("viewer-caption");
            var button = new Button
            {
                HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Stretch,
                Content = new StackPanel { Spacing = 4, Children = { title, directory } }
            };
            _commands.Connect(button, RecentCommandPrefix + path);
            WelcomeRecentItems.Children.Add(button);
        }
        RecentStagesMenu.IsEnabled = paths.Length > 0 && !_documentBusy;
    }
}
