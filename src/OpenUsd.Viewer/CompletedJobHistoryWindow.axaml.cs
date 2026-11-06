// Copyright (c) marcschier. Licensed under the MIT License.

using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Platform.Storage;
using OpenUsd.Rendering;

namespace OpenUsd.Viewer;

internal sealed partial class CompletedJobHistoryWindow : Window, IAsyncDisposable
{
    private readonly ViewerCompletedJobHistory _history;
    private readonly Func<RenderDiskJobResult, CancellationToken, Task<ViewerCompletedJobPreview>>? _load;
    private readonly Func<string, CancellationToken, Task<ViewerImportedRenderJob>> _readManifest;
    private readonly Action<string>? _rememberManifest;
    private readonly TaskCompletionSource _closed = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly CancellationTokenSource _lifetime = new();
    private CompletedRenderJobWindow? _preview;
    private Task _openTask = Task.CompletedTask;
    private bool _opening;
    private bool _closing;

    internal CompletedJobHistoryWindow(ViewerCompletedJobHistory history,
        Func<RenderDiskJobResult, CancellationToken, Task<ViewerCompletedJobPreview>>? load = null,
        Func<string, CancellationToken, Task<ViewerImportedRenderJob>>? readManifest = null,
        IReadOnlyList<string>? rememberedPaths = null,
        Action<string>? rememberManifest = null)
    {
        ArgumentNullException.ThrowIfNull(history);
        _history = history;
        _load = load;
        _readManifest = readManifest ?? ViewerImportedRenderJob.ReadAsync;
        _rememberManifest = rememberManifest;
        InitializeComponent();
        ViewerWindowTheme.Attach(this);
        HistoryJobs.SelectionChanged += (_, _) => UpdateSelection();
        HistoryRememberedJobs.SelectionChanged += (_, _) => UpdateSelection();
        HistoryTabs.SelectionChanged += (_, _) => UpdateSelection();
        HistoryViewButton.Click += async (_, _) => await OpenSelectedPreviewAsync();
        HistoryImportButton.Click += async (_, _) => await ImportManifestAsync();
        HistoryJobs.KeyDown += async (_, args) =>
        {
            if (args.Key == Key.Enter && args.KeyModifiers == KeyModifiers.None && HistoryViewButton.IsEnabled)
            {
                args.Handled = true;
                await OpenSelectedPreviewAsync();
            }
        };
        HistoryRememberedJobs.KeyDown += async (_, args) =>
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
        Closed += (_, _) =>
        {
            _lifetime.Dispose();
            _closed.TrySetResult();
        };
        SetRememberedPaths(rememberedPaths ?? []);
        Refresh();
    }

    private async Task OpenSelectedPreviewAsync()
    {
        if (_opening || _closing)
        {
            return;
        }
        if (HistoryTabs.SelectedIndex == 1)
        {
            if (HistoryRememberedJobs.SelectedItem is string path)
            {
                await ImportManifestAsync(path, remembered: true);
            }
            return;
        }
        if (HistoryJobs.SelectedItem is not ViewerCompletedJobHistoryEntry entry)
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
        if (HistoryTabs.SelectedIndex == 1)
        {
            string? path = HistoryRememberedJobs.SelectedItem as string;
            HistoryDescription.Text = path is null
                ? "No completed or explicitly imported manifest paths have been remembered."
                : "Remembered path only. This manifest and its sampled PNGs will be checked when opened.";
            HistoryOutput.Text = path ?? string.Empty;
            HistoryStatus.Text = path is null
                ? "Import an original manifest or complete a render job to remember its path."
                : "Metadata remains unverified. Missing or changed files are reported without removing this path.";
            HistoryViewButton.IsEnabled = path is not null && !_opening && !_closing;
            HistoryImportButton.IsEnabled = !_opening && !_closing;
            return;
        }
        var selected = HistoryJobs.SelectedItem as ViewerCompletedJobHistoryEntry;
        HistoryDescription.Text = selected?.Description ?? "No jobs have completed in this Viewer process.";
        HistoryOutput.Text = selected?.Job.OutputDirectory ?? string.Empty;
        HistoryStatus.Text = selected is null
            ? "Render an image sequence, authored product or physics recording to add a job."
            : $"{selected.Job.Frames.Count} original frames; {selected.Job.TotalBytes} encoded bytes. " +
                "This is historical output, not the current scene.";
        HistoryViewButton.IsEnabled = selected is not null && !_opening && !_closing;
        HistoryImportButton.IsEnabled = !_opening && !_closing;
    }

    internal void SetRememberedPaths(IReadOnlyList<string> paths, string? diagnostic = null)
    {
        if (_closing)
        {
            return;
        }
        object? previous = HistoryRememberedJobs.SelectedItem;
        HistoryRememberedJobs.ItemsSource = paths;
        HistoryRememberedJobs.SelectedItem = paths.FirstOrDefault(path => Equals(path, previous)) ??
            (paths.Count == 0 ? null : paths[0]);
        HistoryRememberedStatus.Text = diagnostic;
        HistoryRememberedStatus.IsVisible = diagnostic is not null;
        UpdateSelection();
    }

    internal async Task ImportManifestAsync(string? path = null, bool remembered = false)
    {
        if (_opening || _closing)
        {
            return;
        }
        Button returnFocus = remembered ? HistoryViewButton : HistoryImportButton;
        bool restoreImportFocus = returnFocus.IsKeyboardFocusWithin;
        bool temporaryFocusMoved = false;
        EventHandler<FocusChangedEventArgs>? temporaryFocusLost = restoreImportFocus
            ? (_, _) => temporaryFocusMoved = true
            : null;
        _opening = true;
        var drained = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _openTask = drained.Task;
        string? status = null;
        try
        {
            if (temporaryFocusLost is not null)
            {
                HistoryCloseButton.LostFocus += temporaryFocusLost;
            }
            UpdateSelection();
            if (restoreImportFocus)
            {
                HistoryCloseButton.Focus();
            }
            if (path is null)
            {
                IReadOnlyList<IStorageFile> files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
                {
                    Title = "Import original completed render manifest",
                    AllowMultiple = false,
                    FileTypeFilter = [new FilePickerFileType("Render manifest") { Patterns = ["manifest.json"] }]
                });
                _lifetime.Token.ThrowIfCancellationRequested();
                if (files.Count == 0)
                {
                    status = "Manifest selection cancelled. Existing historical results were kept.";
                    return;
                }
                path = files[0].TryGetLocalPath();
                if (path is null)
                {
                    throw new NotSupportedException("Choose a local manifest; remote storage is not supported.");
                }
            }
            ViewerImportedRenderJob job = await _readManifest(path, _lifetime.Token);
            _lifetime.Token.ThrowIfCancellationRequested();
            _rememberManifest?.Invoke(job.ManifestPath);
            await ShowPreviewAsync(() => new CompletedRenderJobWindow(job), returnFocus);
            status = "Imported manifest opened separately. Process history was not changed.";
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or
            InvalidDataException or ArgumentException or NotSupportedException or InvalidOperationException)
        {
            status = $"Could not import render manifest: {ViewerPackageErrorFormatter.Format(exception)}";
            ViewerStartupOptions.WriteStatus(status);
        }
        finally
        {
            _opening = false;
            try
            {
                if (!_closing)
                {
                    UpdateSelection();
                    HistoryStatus.Text = status;
                    if (restoreImportFocus && !temporaryFocusMoved && HistoryCloseButton.IsKeyboardFocusWithin &&
                        (IsActive || IsKeyboardFocusWithin))
                    {
                        returnFocus.Focus();
                    }
                }
            }
            finally
            {
                if (temporaryFocusLost is not null)
                {
                    HistoryCloseButton.LostFocus -= temporaryFocusLost;
                }
                drained.TrySetResult();
            }
        }
    }

    private async Task OpenPreviewAsync(ViewerCompletedJobHistoryEntry entry)
    {
        await ShowPreviewAsync(
            () => new CompletedRenderJobWindow(entry.Job, entry.Description, _load), HistoryViewButton);
    }

    private async Task ShowPreviewAsync(Func<CompletedRenderJobWindow> create, Button returnFocus)
    {
        if (_preview is { } previous)
        {
            await previous.CloseAsync();
        }
        if (_closing)
        {
            return;
        }
        CompletedRenderJobWindow window = create();
        _preview = window;
        bool restoreFocus = false;
        window.Closing += (_, _) => restoreFocus = window.IsKeyboardFocusWithin;
        window.Closed += (_, _) =>
        {
            if (ReferenceEquals(_preview, window))
            {
                _preview = null;
                if (restoreFocus && !_closing && IsVisible)
                {
                    Activate();
                    (returnFocus.IsEnabled ? returnFocus : HistoryCloseButton).Focus();
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
        _lifetime.Cancel();
        UpdateSelection();
        await _openTask;
        if (_preview is { } preview)
        {
            await preview.CloseAsync();
        }
        Close();
    }
}
