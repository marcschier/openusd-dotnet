// Copyright (c) marcschier. Licensed under the MIT License.

using Avalonia.Controls;
using Avalonia.Input;
using OpenUsd.Rendering;

namespace OpenUsd.Viewer;

internal sealed partial class CompletedJobHistoryWindow : Window, IAsyncDisposable
{
    private readonly ViewerCompletedJobHistory _history;
    private readonly Func<RenderDiskJobResult, CancellationToken, Task<ViewerCompletedJobPreview>>? _load;
    private readonly TaskCompletionSource _closed = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private CompletedRenderJobWindow? _preview;
    private Task _openTask = Task.CompletedTask;
    private bool _opening;
    private bool _closing;

    internal CompletedJobHistoryWindow(ViewerCompletedJobHistory history,
        Func<RenderDiskJobResult, CancellationToken, Task<ViewerCompletedJobPreview>>? load = null)
    {
        ArgumentNullException.ThrowIfNull(history);
        _history = history;
        _load = load;
        InitializeComponent();
        ViewerWindowTheme.Attach(this);
        HistoryJobs.SelectionChanged += (_, _) => UpdateSelection();
        HistoryViewButton.Click += async (_, _) => await OpenSelectedPreviewAsync();
        HistoryJobs.KeyDown += async (_, args) =>
        {
            if (args.Key == Key.Enter && args.KeyModifiers == KeyModifiers.None && HistoryViewButton.IsEnabled)
            {
                args.Handled = true;
                await OpenSelectedPreviewAsync();
            }
        };
        HistoryCloseButton.Click += (_, _) => Close();
        Opened += (_, _) => HistoryJobs.Focus();
        KeyDown += (_, args) =>
        {
            if (args.Key == Key.Escape)
            {
                args.Handled = true;
                Close();
            }
        };
        Closing += OnClosing;
        Closed += (_, _) => _closed.TrySetResult();
        Refresh();
    }

    private async Task OpenSelectedPreviewAsync()
    {
        if (_opening || _closing || HistoryJobs.SelectedItem is not ViewerCompletedJobHistoryEntry entry)
        {
            return;
        }
        _opening = true;
        UpdateSelection();
        _openTask = OpenPreviewAsync(entry);
        try
        {
            await _openTask;
        }
        finally
        {
            _opening = false;
            UpdateSelection();
        }
    }

    internal void Refresh()
    {
        if (_closing)
        {
            return;
        }
        object? selected = HistoryJobs.SelectedItem;
        IReadOnlyList<ViewerCompletedJobHistoryEntry> snapshot = _history.Snapshot();
        HistoryJobs.ItemsSource = snapshot;
        HistoryJobs.SelectedItem = snapshot.FirstOrDefault(entry => ReferenceEquals(entry, selected)) ??
            (snapshot.Count == 0 ? null : snapshot[0]);
        UpdateSelection();
    }

    private void UpdateSelection()
    {
        var selected = HistoryJobs.SelectedItem as ViewerCompletedJobHistoryEntry;
        HistoryDescription.Text = selected?.Description ?? "No jobs have completed in this Viewer process.";
        HistoryOutput.Text = selected?.Job.OutputDirectory ?? string.Empty;
        HistoryStatus.Text = selected is null
            ? "Render an image sequence, authored product or physics recording to add a job."
            : $"{selected.Job.Frames.Count} original frames; {selected.Job.TotalBytes} encoded bytes. " +
                "This is historical output, not the current scene.";
        HistoryViewButton.IsEnabled = selected is not null && !_opening && !_closing;
    }

    private async Task OpenPreviewAsync(ViewerCompletedJobHistoryEntry entry)
    {
        if (_preview is { } previous)
        {
            await previous.CloseAsync();
        }
        if (_closing)
        {
            return;
        }
        var window = new CompletedRenderJobWindow(entry.Job, entry.Description, _load);
        _preview = window;
        window.Closed += (_, _) =>
        {
            if (ReferenceEquals(_preview, window))
            {
                _preview = null;
                if (!_closing && IsVisible)
                {
                    Activate();
                    HistoryViewButton.Focus();
                }
            }
        };
        window.Show(this);
    }

    internal Task CloseAsync()
    {
        if (!_closed.Task.IsCompleted)
        {
            Close();
        }
        return _closed.Task;
    }

    public ValueTask DisposeAsync() => new(CloseAsync());

    private async void OnClosing(object? sender, WindowClosingEventArgs args)
    {
        if (_closed.Task.IsCompleted || (_closing && _openTask.IsCompleted && _preview is null))
        {
            return;
        }
        args.Cancel = true;
        if (_closing)
        {
            return;
        }
        _closing = true;
        UpdateSelection();
        await _openTask;
        if (_preview is { } preview)
        {
            await preview.CloseAsync();
        }
        Close();
    }
}
