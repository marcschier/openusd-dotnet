// Copyright (c) marcschier. Licensed under the MIT License.

using Avalonia.Controls;
using Avalonia.Input;
using OpenUsd.Interop;

namespace OpenUsd.Viewer;

internal sealed partial class CameraBookmarksWindow : Window, IAsyncDisposable
{
    private readonly Func<CancellationToken, Task<ViewerCameraBookmarkCapture>> _read;
    private readonly Func<string, CancellationToken, Task<ViewerCameraBookmark>> _captureCurrent;
    private readonly Func<ViewerCameraBookmarkCapture, IReadOnlyList<ViewerCameraBookmark>,
        string, CancellationToken, Task> _apply;
    private readonly Func<ViewerCameraBookmark, CancellationToken, Task> _recall;
    private readonly Func<ViewerCameraBookmarkAvailability> _availability;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly TaskCompletionSource _closed = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private ViewerCameraBookmarkCapture? _capture;
    private CancellationTokenSource? _operation;
    private Task _runTask = Task.CompletedTask;
    private bool _closing;

    internal CameraBookmarksWindow(
        Func<CancellationToken, Task<ViewerCameraBookmarkCapture>> read,
        Func<string, CancellationToken, Task<ViewerCameraBookmark>> captureCurrent,
        Func<ViewerCameraBookmarkCapture, IReadOnlyList<ViewerCameraBookmark>, string, CancellationToken, Task> apply,
        Func<ViewerCameraBookmark, CancellationToken, Task> recall,
        Func<ViewerCameraBookmarkAvailability> availability)
    {
        _read = read;
        _captureCurrent = captureCurrent;
        _apply = apply;
        _recall = recall;
        _availability = availability;
        InitializeComponent();
        ViewerWindowTheme.Attach(this);
        SavedViewsList.SelectionChanged += (_, _) =>
        {
            if (Selected is { } selected)
            {
                SavedViewName.Text = selected.Name;
            }
            UpdateContext();
        };
        SaveCurrentViewButton.Click += async (_, _) => await BeginAsync(AddAsync);
        RenameSavedViewButton.Click += async (_, _) => await BeginAsync(RenameAsync);
        RemoveSavedViewButton.Click += async (_, _) => await BeginAsync(RemoveAsync);
        RecallSavedViewButton.Click += async (_, _) => await BeginAsync(RecallAsync);
        SavedViewsRefreshButton.Click += async (_, _) => await BeginAsync(token => LoadAsync(null, token));
        SavedViewsCancelButton.Click += (_, _) => Cancel();
        SavedViewsCloseButton.Click += (_, _) => Close();
        Opened += async (_, _) =>
        {
            await BeginAsync(token => LoadAsync(null, token));
            if (!_closing)
            {
                SavedViewName.Focus();
            }
        };
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                if (IsRunning)
                {
                    Cancel();
                }
                else
                {
                    Close();
                }
                e.Handled = true;
            }
        };
        Closing += OnClosing;
        Closed += (_, _) =>
        {
            _lifetime.Cancel();
            _lifetime.Dispose();
            _closed.TrySetResult();
        };
        UpdateContext();
    }

    internal bool IsRunning => _operation is not null;
    private ViewerCameraBookmark? Selected => (SavedViewsList.SelectedItem as ListBoxItem)?.Tag as ViewerCameraBookmark;

    internal void UpdateContext()
    {
        if (_closed.Task.IsCompleted)
        {
            return;
        }
        ViewerCameraBookmarkAvailability available = _availability();
        SavedViewsSource.Text = available.SourceDescription;
        SavedViewsCaptureReason.Text = available.CaptureReason;
        bool ready = !IsRunning && !_closing;
        bool selected = _capture is not null && Selected is not null;
        SavedViewName.IsEnabled = ready;
        SavedViewsList.IsEnabled = ready;
        SavedViewsRefreshButton.IsEnabled = ready;
        SaveCurrentViewButton.IsEnabled = ready && _capture is not null && available.CanCapture;
        RenameSavedViewButton.IsEnabled = ready && selected && available.CanEdit;
        RemoveSavedViewButton.IsEnabled = ready && selected && available.CanEdit;
        RecallSavedViewButton.IsEnabled = ready && selected && available.CanRecall;
        SavedViewsCancelButton.IsEnabled = IsRunning && !_closing;
    }

    internal Task RefreshAsync() => IsRunning || _closing || _closed.Task.IsCompleted
        ? Task.CompletedTask
        : BeginAsync(token => LoadAsync(Selected?.Id, token));

    internal void Cancel()
    {
        _operation?.Cancel();
        if (IsRunning)
        {
            SavedViewsStatus.Text = "Cancelling; waiting for the operation and any view restoration.";
            SavedViewsCancelButton.IsEnabled = false;
        }
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

    private Task BeginAsync(Func<CancellationToken, Task> operation)
    {
        if (IsRunning || _closing)
        {
            return Task.CompletedTask;
        }
        _runTask = RunAsync(operation);
        return _runTask;
    }

    private async Task RunAsync(Func<CancellationToken, Task> operation)
    {
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        _operation = cancellation;
        UpdateContext();
        try
        {
            await operation(cancellation.Token);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            SavedViewsStatus.Text = "Cancelled. The current review and any uncommitted view were kept.";
        }
        catch (Exception exception) when (exception is OpenUsdNativeException or IOException or
            UnauthorizedAccessException or InvalidOperationException or NotSupportedException or ArgumentException or
            OpenUsd.Rendering.Silk.OpenUsdSilkException or OpenUsd.Rendering.Storm.OpenUsdStormException or
            TimeoutException)
        {
            SavedViewsStatus.Text = $"Saved view refused: {ViewerPackageErrorFormatter.Format(exception)}";
        }
        finally
        {
            _operation = null;
            UpdateContext();
        }
    }

    private async Task LoadAsync(Guid? selectedId, CancellationToken token)
    {
        _capture = null;
        ViewerCameraBookmarkCapture capture = await _read(token);
        _capture = capture;
        ListBoxItem[] items = [.. capture.Items.Select(static item => new ListBoxItem
        {
            Content = item.Name,
            Tag = item
        })];
        SavedViewsList.ItemsSource = items;
        SavedViewsList.SelectedItem = items.FirstOrDefault(item =>
            ((ViewerCameraBookmark)item.Tag!).Id == selectedId);
        SavedViewsStatus.Text = "Ready.";
    }

    private async Task AddAsync(CancellationToken token)
    {
        ViewerCameraBookmarkCapture before = _capture ?? throw new InvalidOperationException("Refresh saved views.");
        ViewerCameraBookmark bookmark = await _captureCurrent(SavedViewName.Text ?? string.Empty, token);
        await _apply(before, [.. before.Items, bookmark], $"Add saved view {bookmark.Name}", token);
        await LoadAsync(bookmark.Id, token);
        SavedViewsStatus.Text = "Saved view added to unsaved review data. Use Save Review Document to persist it.";
    }

    private async Task RenameAsync(CancellationToken token)
    {
        ViewerCameraBookmarkCapture before = _capture ?? throw new InvalidOperationException("Refresh saved views.");
        ViewerCameraBookmark selected = Selected ?? throw new InvalidOperationException("Select a saved view.");
        ViewerCameraBookmark renamed = selected.Rename(SavedViewName.Text ?? string.Empty);
        await _apply(before, [.. before.Items.Select(item => item.Id == selected.Id ? renamed : item)],
            $"Rename saved view {selected.Name}", token);
        await LoadAsync(selected.Id, token);
        SavedViewsStatus.Text = "Saved view renamed in unsaved review data.";
    }

    private async Task RemoveAsync(CancellationToken token)
    {
        ViewerCameraBookmarkCapture before = _capture ?? throw new InvalidOperationException("Refresh saved views.");
        ViewerCameraBookmark selected = Selected ?? throw new InvalidOperationException("Select a saved view.");
        await _apply(before, [.. before.Items.Where(item => item.Id != selected.Id)],
            $"Remove saved view {selected.Name}", token);
        await LoadAsync(null, token);
        SavedViewsStatus.Text = "Saved view removed from unsaved review data. Undo restores it.";
    }

    private async Task RecallAsync(CancellationToken token)
    {
        ViewerCameraBookmark selected = Selected ?? throw new InvalidOperationException("Select a saved view.");
        await _recall(selected, token);
        SavedViewsStatus.Text = "Saved view recalled exactly. Playback is paused; the review was not edited.";
    }

    private async void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        bool wasClosing = _closing;
        _closing = true;
        _lifetime.Cancel();
        if (_runTask.IsCompleted)
        {
            return;
        }
        e.Cancel = true;
        if (wasClosing)
        {
            return;
        }
        Cancel();
        UpdateContext();
        await _runTask;
        Close();
    }
}
