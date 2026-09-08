// Copyright (c) marcschier. Licensed under the MIT License.

using System.Windows.Input;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;

namespace OpenUsd.Viewer;

internal sealed record ViewerCommandState(
    string Id,
    string Label,
    string AccessibleName,
    string? Gesture,
    bool IsEnabled,
    bool IsChecked,
    ViewerCommandCheckKind CheckKind);

/// <summary>Adapts the live menu/control actions, rather than implementing a second set of handlers.</summary>
internal sealed class ViewerCommandDispatcher : IDisposable
{
    internal const int MaximumResults = 64;
    internal const int MaximumQueryLength = 128;
    private readonly Dictionary<string, Binding> _bindings = new(StringComparer.Ordinal);

    internal void Register(ViewerCommandDescriptor descriptor, Control source)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        ArgumentNullException.ThrowIfNull(source);
        AutomationProperties.SetName(source, descriptor.AccessibleName);
        AutomationProperties.SetAutomationId(source, descriptor.Id);
        if (source is not (MenuItem or Button) ||
            descriptor.Id is ViewerCommandIds.FileRecentStages or ViewerCommandIds.CameraStageCameras or
                ViewerCommandIds.CameraSavedViews)
        {
            return;
        }
        if (!_bindings.TryGetValue(descriptor.Id, out Binding? existing) ||
            (existing.Source is not MenuItem && source is MenuItem))
        {
            existing?.Dispose();
            _bindings[descriptor.Id] = new Binding(descriptor, source);
        }
    }

    internal void Connect(Button surface, string id)
    {
        ArgumentNullException.ThrowIfNull(surface);
        _bindings[id].Connect(surface);
    }

    internal void RemoveGroup(string prefix)
    {
        foreach (string id in _bindings.Keys.Where(id => id.StartsWith(prefix, StringComparison.Ordinal)).ToArray())
        {
            _bindings[id].Dispose();
            _bindings.Remove(id);
        }
    }

    internal IReadOnlyList<ViewerCommandState> Search(string query)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(query.Length, MaximumQueryLength);
        string[] words = query.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return [.. _bindings.Values
            .Where(static binding => binding.IsVisible)
            .Select(static binding => binding.ReadState())
            .Where(state => words.All(word =>
                $"{state.Label} {state.AccessibleName} {state.Id} {state.Gesture}"
                    .Contains(word, StringComparison.OrdinalIgnoreCase)))
            .Take(MaximumResults)];
    }

    internal bool TryExecute(string id)
    {
        if (!_bindings.TryGetValue(id, out Binding? binding) || !binding.CanExecute(null))
        {
            return false;
        }
        binding.Invoke();
        return true;
    }

    internal bool CanExecute(string id) =>
        _bindings.TryGetValue(id, out Binding? binding) && binding.CanExecute(null);

    internal bool TryExecuteGesture(KeyEventArgs input)
    {
        ArgumentNullException.ThrowIfNull(input);
        foreach ((string id, Binding binding) in _bindings)
        {
            if (binding.Gesture?.Matches(input) == true)
            {
                return TryExecute(id);
            }
        }
        return false;
    }

    public void Dispose()
    {
        foreach (Binding binding in _bindings.Values)
        {
            binding.Dispose();
        }
        _bindings.Clear();
    }

    private sealed class Binding : ICommand, IDisposable
    {
        private readonly ViewerCommandDescriptor _descriptor;
        private readonly AvaloniaObject[] _observed;
        private readonly List<Button> _surfaces = [];

        internal Binding(ViewerCommandDescriptor descriptor, Control source)
        {
            _descriptor = descriptor;
            Source = source;
            // Bare viewport keys keep the existing editing, repeat and native-focus guards.
            Gesture = descriptor.Gesture is { } gesture &&
                (gesture.StartsWith("Ctrl+", StringComparison.Ordinal) || gesture == "F1")
                    ? KeyGesture.Parse(gesture)
                    : null;
            _observed = [source, .. source.GetLogicalAncestors().OfType<AvaloniaObject>()];
            foreach (AvaloniaObject element in _observed)
            {
                element.PropertyChanged += OnStateChanged;
            }
        }

        public event EventHandler? CanExecuteChanged;

        internal Control Source { get; }

        internal KeyGesture? Gesture { get; }

        internal bool IsVisible =>
            Source.IsVisible && Source.GetLogicalAncestors().OfType<Control>().All(static control => control.IsVisible);

        internal ViewerCommandState ReadState() => new(
            _descriptor.Id,
            GetLabel(),
            AutomationProperties.GetName(Source) ?? _descriptor.AccessibleName,
            _descriptor.Gesture,
            Source.IsEffectivelyEnabled &&
                Source.GetLogicalAncestors().OfType<InputElement>().All(static element => element.IsEnabled),
            Source switch
            {
                MenuItem menu => menu.IsChecked,
                ToggleButton toggle => toggle.IsChecked == true,
                _ => false
            },
            _descriptor.CheckKind);

        private string GetLabel()
        {
            string label = Source is MenuItem { Header: string header } ? header : _descriptor.Label;
            string[] parents = [.. Source.GetLogicalAncestors().OfType<MenuItem>()
                .Select(static item => item.Header?.ToString() ?? string.Empty).Reverse()];
            string group = parents.Length == 0 ? _descriptor.Group.ToString() : string.Join(" > ", parents);
            return ViewerCommandCatalog.DisplayLabel($"{group} > {label}");
        }

        public bool CanExecute(object? parameter) => IsVisible && ReadState().IsEnabled;

        public void Execute(object? parameter)
        {
            if (!CanExecute(parameter))
            {
                throw new InvalidOperationException($"Command '{_descriptor.Id}' is not available.");
            }
            Invoke();
        }

        internal void Connect(Button surface)
        {
            surface.Command = this;
            AutomationProperties.SetAutomationId(surface, surface.Name ?? $"action.{_descriptor.Id}");
            _surfaces.Add(surface);
            SyncSurface(surface);
        }

        private void OnStateChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
        {
            if (e.Property.Name is not ("IsEnabled" or "IsEffectivelyEnabled" or "IsVisible" or
                "IsChecked" or "Header" or "Name"))
            {
                return;
            }
            foreach (Button surface in _surfaces)
            {
                SyncSurface(surface);
            }
            CanExecuteChanged?.Invoke(this, EventArgs.Empty);
        }

        private void SyncSurface(Button surface)
        {
            ViewerCommandState state = ReadState();
            surface.IsEnabled = IsVisible && state.IsEnabled;
            AutomationProperties.SetName(surface, state.AccessibleName);
            ToolTip.SetTip(surface, state.Gesture is { } gesture
                ? $"{state.AccessibleName} ({gesture})"
                : state.AccessibleName);
            if (surface is ToggleButton toggle)
            {
                toggle.IsChecked = state.IsChecked;
            }
        }

        internal void Invoke()
        {
            if (Source is MenuItem menu)
            {
                menu.IsChecked = menu.ToggleType switch
                {
                    MenuItemToggleType.CheckBox => !menu.IsChecked,
                    MenuItemToggleType.Radio => true,
                    _ => menu.IsChecked
                };
                menu.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            }
            else if (Source is Button button)
            {
                if (button is ToggleButton toggle)
                {
                    toggle.IsChecked = toggle.IsChecked != true;
                }
                button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            }
        }

        public void Dispose()
        {
            foreach (AvaloniaObject element in _observed)
            {
                element.PropertyChanged -= OnStateChanged;
            }
            foreach (Button surface in _surfaces)
            {
                surface.Command = null;
            }
            _surfaces.Clear();
        }
    }
}
