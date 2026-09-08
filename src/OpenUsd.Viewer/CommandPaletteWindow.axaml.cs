// Copyright (c) marcschier. Licensed under the MIT License.

using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;

namespace OpenUsd.Viewer;

internal sealed partial class CommandPaletteWindow : Window
{
    private readonly ViewerCommandDispatcher _commands;
    private readonly Action<string> _reportStatus;
    private readonly DispatcherTimer _refresh = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private IReadOnlyList<ViewerCommandState> _results = [];

    internal CommandPaletteWindow(ViewerCommandDispatcher commands, Action<string> reportStatus)
    {
        _commands = commands;
        _reportStatus = reportStatus;
        InitializeComponent();
        ViewerWindowTheme.Attach(this);
        CommandSearch.TextChanged += (_, _) => RefreshResults();
        CommandResults.SelectionChanged += (_, _) => UpdateRunAvailability();
        CommandResults.DoubleTapped += (_, _) => RunSelected();
        RunCommandButton.Click += (_, _) => RunSelected();
        ClosePaletteButton.Click += (_, _) => Close();
        AddHandler(KeyDownEvent, OnPaletteKeyDown, RoutingStrategies.Tunnel | RoutingStrategies.Bubble);
        _refresh.Tick += OnRefresh;
        Opened += (_, _) =>
        {
            RefreshResults();
            CommandSearch.Focus();
            _refresh.Start();
        };
        Closed += (_, _) =>
        {
            _refresh.Stop();
            _refresh.Tick -= OnRefresh;
        };
    }

    private void OnRefresh(object? sender, EventArgs e) => RefreshResults();

    private void RefreshResults()
    {
        IReadOnlyList<ViewerCommandState> results = _commands.Search(CommandSearch.Text ?? string.Empty);
        if (_results.SequenceEqual(results) && CommandResults.ItemCount != 0)
        {
            return;
        }
        string? previous = (CommandResults.SelectedItem as ListBoxItem)?.Tag as string;
        _results = results;
        ListBoxItem[] items = [.. results.Select(CreateResult)];
        CommandResults.ItemsSource = items;
        CommandResults.SelectedItem = items.FirstOrDefault(item => item.IsEnabled && Equals(item.Tag, previous)) ??
            items.FirstOrDefault(static item => item.IsEnabled);
        CommandSearchStatus.Text = results.Count == 0
            ? "No matching commands. Try a menu name such as View, Camera or Render."
            : $"{results.Count} matches (up to 64). Unavailable actions stay visible but cannot run.";
        UpdateRunAvailability();
    }

    private static ListBoxItem CreateResult(ViewerCommandState state)
    {
        string status = state.CheckKind == ViewerCommandCheckKind.None ? string.Empty : state.IsChecked ? "On" : "Off";
        var content = new StackPanel { Spacing = 4 };
        content.Children.Add(new TextBlock { Text = state.Label, TextWrapping = TextWrapping.Wrap });
        var detail = new TextBlock
        {
            Text = string.Join("  ", new[] { state.Gesture, status, state.IsEnabled ? null : "Unavailable" }
                .Where(static text => !string.IsNullOrEmpty(text))),
            TextWrapping = TextWrapping.Wrap
        };
        detail.Classes.Add("viewer-caption");
        content.Children.Add(detail);
        var item = new ListBoxItem { Content = content, Tag = state.Id, IsEnabled = state.IsEnabled };
        AutomationProperties.SetName(item, state.AccessibleName);
        AutomationProperties.SetAutomationId(item, $"palette.{state.Id}");
        AutomationProperties.SetHelpText(item, detail.Text);
        return item;
    }

    private void UpdateRunAvailability() =>
        RunCommandButton.IsEnabled = CommandResults.SelectedItem is ListBoxItem { IsEnabled: true };

    private void OnPaletteKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Handled)
        {
            return;
        }
        if (e.Key == Key.Escape)
        {
            e.Handled = true;
            Close();
        }
        else if (!CommandSearch.IsKeyboardFocusWithin && !CommandResults.IsKeyboardFocusWithin)
        {
            return;
        }
        else if (e.Key == Key.Enter)
        {
            e.Handled = true;
            RunSelected();
        }
        else if (e.Key is Key.Up or Key.Down)
        {
            e.Handled = true;
            int direction = e.Key == Key.Down ? 1 : -1;
            for (int index = CommandResults.SelectedIndex + direction;
                index >= 0 && index < _results.Count;
                index += direction)
            {
                if (_results[index].IsEnabled)
                {
                    CommandResults.SelectedIndex = index;
                    if (CommandResults.SelectedItem is { } selected)
                    {
                        CommandResults.ScrollIntoView(selected);
                    }
                    break;
                }
            }
        }
    }

    private void RunSelected()
    {
        if (CommandResults.SelectedItem is not ListBoxItem { Tag: string id } || !_commands.CanExecute(id))
        {
            CommandSearchStatus.Text = "This action is not available in the current Viewer state.";
            RefreshResults();
            return;
        }
        Close();
        if (!_commands.TryExecute(id))
        {
            _reportStatus("The command is no longer available in the current Viewer state.");
        }
    }
}
