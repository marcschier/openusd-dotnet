// Copyright (c) marcschier. Licensed under the MIT License.

using System.Globalization;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using OpenUsd.Rendering;

namespace OpenUsd.Viewer;

internal sealed partial class CompletedRenderJobWindow : Window, IAsyncDisposable
{
    private readonly RenderDiskJobResult _job;
    private readonly Func<RenderDiskJobResult, CancellationToken, Task<ViewerCompletedJobPreview>> _load;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly TaskCompletionSource _closed = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly List<CompletedRenderJobTile> _tiles = new(ViewerCompletedJobPreview.MaximumFrames);
    private Task _loadTask = Task.CompletedTask;
    private bool _closing;

    internal CompletedRenderJobWindow(
        RenderDiskJobResult job, string description,
        Func<RenderDiskJobResult, CancellationToken, Task<ViewerCompletedJobPreview>>? load = null)
    {
        ArgumentNullException.ThrowIfNull(job);
        _job = job;
        _load = load ?? ViewerCompletedJobPreview.LoadAsync;
        InitializeComponent();
        ViewerWindowTheme.Attach(this);
        ResultsDescription.Text = description;
        ResultsJobIdentity.Text = Path.GetFileName(Path.TrimEndingDirectorySeparator(job.OutputDirectory));
        ResultsOutputLocation.Text = job.OutputDirectory;
        if (job.Frames.Count > 0)
        {
            ResultsSource.Text = $"Stage: {job.Frames[0].State.Stage.Identifier}";
            ToolTip.SetTip(ResultsSource, ResultsSource.Text);
        }
        ResultsCloseButton.Click += (_, _) => Close();
        KeyDown += (_, args) =>
        {
            if (args.Key == Key.Escape)
            {
                args.Handled = true;
                Close();
            }
        };
        Opened += async (_, _) =>
        {
            ResultsCloseButton.Focus();
            var drained = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _loadTask = drained.Task;
            try
            {
                await LoadAsync();
            }
            finally
            {
                drained.TrySetResult();
            }
        };
        Closing += OnResultsClosing;
        Closed += (_, _) =>
        {
            DisposeTiles();
            _lifetime.Dispose();
            _closed.TrySetResult();
        };
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

    private async Task LoadAsync()
    {
        bool published = false;
        try
        {
            using ViewerCompletedJobPreview preview = await _load(_job, _lifetime.Token);
            _lifetime.Token.ThrowIfCancellationRequested();
            if (preview.Frames.Count is < 1 or > ViewerCompletedJobPreview.MaximumFrames)
            {
                throw new InvalidDataException("A contact sheet requires 1-16 bounded thumbnails.");
            }
            foreach (ViewerCompletedFramePreview frame in preview.Frames)
            {
                _tiles.Add(new CompletedRenderJobTile(frame));
                frame.Dispose();
            }
            _lifetime.Token.ThrowIfCancellationRequested();
            ResultsFrames.ItemsSource = _tiles.ToArray();
            _lifetime.Token.ThrowIfCancellationRequested();
            ResultsFrames.SelectedIndex = 0;
            ResultsStatus.Text = $"Showing {_tiles.Count} of {_job.Frames.Count} completed frames in job order. " +
                "Frame indices are zero-based; time codes are the original USD samples.";
            published = true;
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
            DisposeTiles();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or
            InvalidDataException or ArgumentException or NotSupportedException or InvalidOperationException or
            OverflowException or ExternalException)
        {
            DisposeTiles();
            string message = $"Could not preview results: {ViewerPackageErrorFormatter.Format(exception)}";
            ViewerStartupOptions.WriteStatus(message);
            if (!_closing)
            {
                ResultsStatus.Text = message;
                ResultsStatus.Classes.Set("viewer-error", true);
            }
        }
        finally
        {
            if (!published)
            {
                DisposeTiles();
            }
            ResultsLoading.IsVisible = false;
        }
    }

    private async void OnResultsClosing(object? sender, WindowClosingEventArgs args)
    {
        bool wasClosing = _closing;
        _closing = true;
        _lifetime.Cancel();
        DisposeTiles();
        if (_loadTask.IsCompleted)
        {
            return;
        }
        args.Cancel = true;
        if (wasClosing)
        {
            return;
        }
        ResultsStatus.Text = "Closing... cancelling and draining PNG loading.";
        await _loadTask;
        Close();
    }

    private void DisposeTiles()
    {
        ResultsFrames.ItemsSource = null;
        foreach (CompletedRenderJobTile tile in _tiles)
        {
            tile.Dispose();
        }
        _tiles.Clear();
    }
}

internal sealed class CompletedRenderJobTile : IDisposable
{
    internal CompletedRenderJobTile(ViewerCompletedFramePreview frame)
    {
        if (frame.Width is < 1 or > ViewerCompletedJobPreview.ThumbnailSize ||
            frame.Height is < 1 or > ViewerCompletedJobPreview.ThumbnailSize ||
            frame.Pixels.Length != checked(frame.Width * frame.Height * 4))
        {
            throw new InvalidDataException("The completed thumbnail exceeds its pixel bounds.");
        }
        FrameIndex = frame.Index;
        TimeCode = frame.TimeCode;
        FrameLabel = $"Frame {FrameIndex.ToString(CultureInfo.InvariantCulture)}";
        TimeLabel = $"t = {ViewerTimelineMath.Format(TimeCode)}";
        DimensionsLabel = $"{frame.SourceDimensions.Width} x {frame.SourceDimensions.Height} original";
        FileName = frame.FileName;
        AccessibleName = $"{FrameLabel}, time code {ViewerTimelineMath.Format(TimeCode)}, {DimensionsLabel}, {FileName}";
        Image = CreateBitmap(frame);
    }

    public int FrameIndex { get; }
    public double TimeCode { get; }
    public string FrameLabel { get; }
    public string TimeLabel { get; }
    public string DimensionsLabel { get; }
    public string FileName { get; }
    public string AccessibleName { get; }
    public Bitmap Image { get; }

    public void Dispose() => Image.Dispose();

    private static WriteableBitmap CreateBitmap(ViewerCompletedFramePreview frame)
    {
        var bitmap = new WriteableBitmap(
            new PixelSize(frame.Width, frame.Height), new Vector(96, 96), PixelFormat.Rgba8888, AlphaFormat.Unpremul);
        bool copied = false;
        try
        {
            using ILockedFramebuffer buffer = bitmap.Lock();
            int rowBytes = checked(frame.Width * 4);
            for (int y = 0; y < frame.Height; y++)
            {
                Marshal.Copy(frame.Pixels, y * rowBytes, buffer.Address + (y * buffer.RowBytes), rowBytes);
            }
            copied = true;
            return bitmap;
        }
        finally
        {
            if (!copied)
            {
                bitmap.Dispose();
            }
        }
    }
}
