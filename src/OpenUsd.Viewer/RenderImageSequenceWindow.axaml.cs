// Copyright (c) marcschier. Licensed under the MIT License.

using System.Globalization;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Platform.Storage;
using OpenUsd.Interop;
using OpenUsd.Rendering;
using OpenUsd.Rendering.Silk;
using OpenUsd.Rendering.Storm;

namespace OpenUsd.Viewer;

internal sealed partial class RenderImageSequenceWindow : Window, IAsyncDisposable
{
    private readonly Func<ViewerRenderSequenceRange, string, ViewerRenderSequenceOutputOptions,
        Action<ViewerRenderSequenceProgress>, CancellationToken, Task<RenderDiskJobResult>> _render;
    private readonly Func<ViewerRenderSequenceOutputOptions, string?> _getUnsupportedReason;
    private readonly Func<ViewerPhysicsSequenceRequest, Action<ViewerRenderSequenceProgress>,
        CancellationToken, Task<RenderDiskJobResult>>? _renderPhysics;
    private readonly Func<ViewerRenderSequenceOutputOptions, string?>? _getPhysicsUnsupportedReason;
    private readonly ViewerCompletedJobResults _results;
    private string? _unsupportedReason;
    private readonly TaskCompletionSource _closed = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly CancellationTokenSource _windowLifetime = new();
    private CancellationTokenSource? _cancellation;
    private Task _runTask = Task.CompletedTask;
    private bool _closing;
    private bool _picking;
    private bool _requestValid;
    private bool _restoreOperationFocus;

    internal RenderImageSequenceWindow(
        double start, double end, string renderer,
        Func<ViewerRenderSequenceOutputOptions, string?> getUnsupportedReason,
        Func<ViewerRenderSequenceRange, string, ViewerRenderSequenceOutputOptions,
            Action<ViewerRenderSequenceProgress>, CancellationToken, Task<RenderDiskJobResult>> render,
        Func<ViewerPhysicsSequenceRequest, Action<ViewerRenderSequenceProgress>,
            CancellationToken, Task<RenderDiskJobResult>>? renderPhysics = null,
        Func<ViewerRenderSequenceOutputOptions, string?>? getPhysicsUnsupportedReason = null,
        bool physicsMode = false,
        Action<RenderDiskJobResult, string>? completed = null)
    {
        ArgumentNullException.ThrowIfNull(render);
        ArgumentNullException.ThrowIfNull(getUnsupportedReason);
        _render = render;
        _getUnsupportedReason = getUnsupportedReason;
        _renderPhysics = renderPhysics;
        _getPhysicsUnsupportedReason = getPhysicsUnsupportedReason;
        InitializeComponent();
        ViewerWindowTheme.Attach(this);
        _results = new ViewerCompletedJobResults(this, SequenceViewResultsButton, completed);
        SequenceRenderer.Text = renderer;
        SequenceStartTime.Text = ViewerTimelineMath.Format(start);
        SequenceEndTime.Text = ViewerTimelineMath.Format(end);
        SequenceOutputFolder.Text = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
        SequenceStartTime.TextChanged += (_, _) => ValidateRequest();
        SequenceEndTime.TextChanged += (_, _) => ValidateRequest();
        SequenceStep.TextChanged += (_, _) => ValidateRequest();
        SequenceOutputFolder.TextChanged += (_, _) => ValidateRequest();
        SequencePhysicsFrames.TextChanged += (_, _) => ValidateRequest();
        SequencePhysicsSteps.TextChanged += (_, _) => ValidateRequest();
        SequencePhysicsScenario.TextChanged += (_, _) => ValidateRequest();
        SequencePhysicsMode.IsCheckedChanged += (_, _) => ValidateRequest();
        SequencePhysicsMode.IsVisible = renderPhysics is not null;
        SequencePhysicsMode.IsChecked = physicsMode && renderPhysics is not null;
        SequenceHdrColorData.IsCheckedChanged += (_, _) => ValidateRequest();
        SequenceDepthData.IsCheckedChanged += (_, _) => ValidateRequest();
        SequenceHdrFormat.SelectionChanged += (_, _) => ValidateRequest();
        SequenceBrowseButton.Click += async (_, _) => await ChooseOutputAsync();
        SequenceRenderButton.Click += async (_, _) =>
        {
            if (IsRunning || _closing)
            {
                return;
            }
            var drained = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _runTask = drained.Task;
            try
            {
                await RunAsync();
            }
            finally
            {
                drained.TrySetResult();
            }
        };
        SequenceCancelButton.Click += (_, _) => Cancel();
        SequenceCloseButton.Click += (_, _) => Close();
        Opened += (_, _) =>
        {
            if (IsPhysicsMode)
            {
                SequencePhysicsFrames.Focus();
            }
            else
            {
                SequenceStartTime.Focus();
            }
        };
        KeyDown += OnSequenceKeyDown;
        Closing += OnSequenceClosing;
        Closed += (_, _) =>
        {
            _windowLifetime.Cancel();
            _windowLifetime.Dispose();
            _closed.TrySetResult();
        };
        ValidateRequest();
    }

    internal bool IsRunning => _cancellation is not null;

    private bool IsPhysicsMode => SequencePhysicsMode.IsChecked == true && _renderPhysics is not null;

    internal void SelectPhysicsMode(bool physicsMode)
    {
        if (!IsRunning && !_closing)
        {
            SequencePhysicsMode.IsChecked = physicsMode && _renderPhysics is not null;
        }
    }

    private string? GetUnsupportedReason(ViewerRenderSequenceOutputOptions outputs) =>
        IsPhysicsMode
            ? _getPhysicsUnsupportedReason?.Invoke(outputs)
            : _getUnsupportedReason(outputs);

    internal void UpdateContext(string description)
    {
        if (SequenceRenderer.Text == description && _unsupportedReason == GetUnsupportedReason(ReadOutputs()))
        {
            return;
        }
        SequenceRenderer.Text = description;
        ValidateRequest();
    }

    internal void Cancel()
    {
        _cancellation?.Cancel();
        if (IsRunning)
        {
            bool ownsFocus = SequenceCancelButton.IsKeyboardFocusWithin;
            _restoreOperationFocus |= ownsFocus;
            SequenceStatus.Text = "Cancelling... waiting for the in-flight frame and view restoration.";
            SequenceCancelButton.IsEnabled = false;
            if (ownsFocus)
            {
                SequenceCloseButton.Focus();
            }
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

    private ViewerRenderSequenceRange ReadRange()
    {
        if (!ViewerTimelineMath.TryParse(SequenceStartTime.Text, out double start) ||
            !ViewerTimelineMath.TryParse(SequenceEndTime.Text, out double end) ||
            !ViewerTimelineMath.TryParse(SequenceStep.Text, out double step))
        {
            throw new ArgumentException("Enter finite time codes and a positive step using invariant numeric syntax.");
        }
        return new ViewerRenderSequenceRange(start, end, step);
    }

    private ViewerRenderSequenceOutputOptions ReadOutputs()
    {
        bool hdr = SequenceHdrColorData.IsChecked == true;
        RenderHdrColorFormat format = hdr ? SequenceHdrFormat.SelectedIndex switch
        {
            0 => RenderHdrColorFormat.RawRgba16Float,
            1 => RenderHdrColorFormat.Exr,
            _ => throw new ArgumentException("Choose an HDR output format.")
        } : RenderHdrColorFormat.RawRgba16Float;
        return new ViewerRenderSequenceOutputOptions(SequenceDepthData.IsChecked == true, hdr, format);
    }

    private ViewerPhysicsSequenceRequest ReadPhysicsRequest()
    {
        if (!int.TryParse(SequencePhysicsFrames.Text, NumberStyles.None, CultureInfo.InvariantCulture,
            out int frames) ||
            !int.TryParse(SequencePhysicsSteps.Text, NumberStyles.None, CultureInfo.InvariantCulture, out int steps))
        {
            throw new ArgumentException("Enter whole frame and fixed-step counts.");
        }
        var request = new ViewerPhysicsSequenceRequest(frames, steps, SequencePhysicsScenario.Text ?? string.Empty,
            SequenceOutputFolder.Text ?? string.Empty, ReadOutputs());
        request.Validate();
        return request;
    }

    private void ValidateRequest(bool preserveStatus = false)
    {
        if (IsRunning || _closing)
        {
            return;
        }
        SequenceRangeControls.IsVisible = !IsPhysicsMode;
        SequencePhysicsControls.IsVisible = IsPhysicsMode;
        SequenceDescription.Text = IsPhysicsMode
            ? "Records every rigid pose from the current paused world. Loop and Apply Preview must be off. " +
                "Recording advances physics; cancellation does not rewind it. The view is restored before publication."
            : "The selected camera is sampled at every requested time. Playback and view edits pause until the " +
                "job drains.";
        bool valid = false;
        string status;
        try
        {
            ViewerRenderSequenceOutputOptions outputs = ReadOutputs();
            SequenceHdrFormat.IsEnabled = outputs.IncludeHdrColor;
            _unsupportedReason = GetUnsupportedReason(outputs);
            SequenceOutputScope.Text = "PNG" +
                (outputs.IncludeHdrColor
                    ? outputs.HdrColorFormat == RenderHdrColorFormat.Exr
                        ? " + HDR EXR (.hdr.exr)" : " + raw HDR (.hdr.rgba16f)"
                    : string.Empty) +
                (outputs.IncludeDeviceDepth ? " + device depth (.device-depth.f32)" : string.Empty);
            SequenceLimits.Text = "Limits: 4096 frames; 8192 pixels/side; 64 MiB capture " +
                $"({(outputs.HasAdditionalPlanes ? 20 : 4)} bytes/pixel) and encoded files/frame; " +
                "4 GiB including manifest. No partial job is published." +
                (outputs.HdrColorFormat == RenderHdrColorFormat.Exr
                    ? " Native EXR codec working memory is outside these quotas." : string.Empty);
            int count = IsPhysicsMode ? ReadPhysicsRequest().FrameCount : ReadRange().Times.Count;
            SequenceFrameCount.Text = $"{count} frame(s)";
            if (_unsupportedReason is { } unsupportedReason)
            {
                status = unsupportedReason;
            }
            else if (string.IsNullOrWhiteSpace(SequenceOutputFolder.Text) ||
                !Path.IsPathFullyQualified(SequenceOutputFolder.Text))
            {
                status = "Choose an existing absolute local output folder.";
            }
            else
            {
                status = "Ready. Files appear only after every frame and the manifest are complete.";
                valid = true;
            }
        }
        catch (ArgumentException exception)
        {
            SequenceFrameCount.Text = "Invalid frame range";
            status = exception.Message;
        }
        if (!preserveStatus)
        {
            SequenceStatus.Text = status;
        }
        _requestValid = valid;
        SequenceRenderButton.IsEnabled = valid && !_picking;
    }

    private async Task RunAsync()
    {
        ViewerRenderSequenceOutputOptions outputs = ReadOutputs();
        bool physicsMode = IsPhysicsMode;
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(_windowLifetime.Token);
        Control? operationFocus = IsKeyboardFocusWithin ? FocusManager?.GetFocusedElement() as Control : null;
        _restoreOperationFocus = false;
        _cancellation = cancellation;
        SequenceOutputLocation.Text = string.Empty;
        SequenceStatus.Classes.Set("viewer-warning", false);
        SetRunningControls();
        if (operationFocus is not null)
        {
            SequenceCancelButton.Focus();
        }
        try
        {
            await _results.ClearAsync();
            cancellation.Token.ThrowIfCancellationRequested();
            ViewerPhysicsSequenceRequest? physics = physicsMode ? ReadPhysicsRequest() : null;
            ViewerRenderSequenceRange? range = physicsMode ? null : ReadRange();
            SequenceProgress.Maximum = physics?.FrameCount ?? range!.Times.Count;
            SequenceProgress.Value = 0;
            RenderDiskJobResult result = physics is not null
                ? await _renderPhysics!(physics, ReportProgress, cancellation.Token)
                : await _render(range!, SequenceOutputFolder.Text ?? string.Empty, outputs, ReportProgress,
                    cancellation.Token);
            if (!physicsMode)
            {
                cancellation.Token.ThrowIfCancellationRequested();
            }
            SequenceOutputLocation.Text = result.OutputDirectory;
            SequenceProgress.Value = result.Frames.Count;
            string diagnostics = result.Diagnostics.Count == 0
                ? string.Empty
                : $" {result.Diagnostics.Count} renderer diagnostic(s) retained; see manifest.json.";
            int hdrFiles = result.Frames.Count(static frame => frame.HdrColorFileName is not null);
            int depthFiles = result.Frames.Count(static frame => frame.DepthFileName is not null);
            string files = $"{result.Frames.Count} PNG" +
                (hdrFiles > 0
                    ? $", {hdrFiles} HDR{(outputs.HdrColorFormat == RenderHdrColorFormat.Exr ? " EXR" : string.Empty)}"
                    : string.Empty) +
                (depthFiles > 0 ? $", {depthFiles} depth" : string.Empty);
            SequenceStatus.Text = $"Completed {result.Frames.Count} frames: {files} + manifest.json." +
                $"{diagnostics} The pre-job view has been restored." +
                (physicsMode ? " Physics remains paused at its last completed step." : string.Empty);
            SequenceStatus.Classes.Set("viewer-warning", result.Diagnostics.Any(static diagnostic =>
                diagnostic.Severity is RenderDiagnosticSeverity.Warning or RenderDiagnosticSeverity.Error));
            if (!_closing)
            {
                _results.Publish(result, physicsMode ? "Physics simulation sequence" : "Viewport image sequence");
            }
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            SequenceStatus.Text = "Cancelled. No sequence was published; the pre-job view has been restored." +
                (physicsMode ? " Physics was not rewound." : string.Empty);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or
            InvalidOperationException or InvalidDataException or NotSupportedException or ArgumentException or
            OpenUsdNativeException or OpenUsdSilkException or OpenUsdStormException or
            TimeoutException or OverflowException or ViewerPhysicsException or AggregateException)
        {
            SequenceStatus.Text = $"Sequence failed: {ViewerPackageErrorFormatter.Format(exception)}";
            ViewerStartupOptions.WriteStatus(SequenceStatus.Text);
        }
        finally
        {
            bool restoreFocus = (SequenceCancelButton.IsKeyboardFocusWithin ||
                (_restoreOperationFocus && SequenceCloseButton.IsKeyboardFocusWithin)) &&
                (IsActive || IsKeyboardFocusWithin);
            _restoreOperationFocus = false;
            _cancellation = null;
            ValidateRequest(preserveStatus: true);
            SetRunningControls();
            if (restoreFocus && !_closing && operationFocus is { IsEnabled: true, IsVisible: true })
            {
                operationFocus.Focus();
            }
        }
    }

    private void ReportProgress(ViewerRenderSequenceProgress progress)
    {
        SequenceProgress.Value = progress.Phase == "Preparing" ? 0 : progress.CompletedFrames;
        SequenceStatus.Text = $"{progress.Phase}: {progress.CompletedFrames}/{progress.TotalFrames} frames; " +
            $"time {ViewerTimelineMath.Format(progress.TimeCode)}.";
    }

    private void SetRunningControls()
    {
        bool ready = !IsRunning && !_closing && !_picking;
        SequenceRangeControls.IsEnabled = ready;
        SequencePhysicsControls.IsEnabled = ready;
        SequencePhysicsMode.IsEnabled = ready;
        SequenceOutputControls.IsEnabled = ready;
        SequenceOutputFolder.IsEnabled = ready;
        SequenceBrowseButton.IsEnabled = ready;
        SequenceRenderButton.IsEnabled = ready && _requestValid;
        SequenceCancelButton.IsEnabled = IsRunning && !_closing;
    }

    private async Task ChooseOutputAsync()
    {
        CancellationToken cancellationToken = _windowLifetime.Token;
        _picking = true;
        SetRunningControls();
        try
        {
            IReadOnlyList<IStorageFolder> folders = await StorageProvider.OpenFolderPickerAsync(
                new FolderPickerOpenOptions { Title = "Choose image sequence output folder", AllowMultiple = false })
                .WaitAsync(cancellationToken);
            if (folders.Count != 0 && !_closing)
            {
                SequenceOutputFolder.Text = folders[0].TryGetLocalPath() ??
                    throw new NotSupportedException("Choose a local filesystem folder.");
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            SequenceStatus.Text = $"The folder picker failed: {exception.Message}";
        }
        finally
        {
            _picking = false;
            SetRunningControls();
        }
    }

    private void OnSequenceKeyDown(object? sender, KeyEventArgs e)
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
    }

    private async void OnSequenceClosing(object? sender, WindowClosingEventArgs e)
    {
        if (_closed.Task.IsCompleted)
        {
            return;
        }
        bool wasClosing = _closing;
        _closing = true;
        _windowLifetime.Cancel();
        Task results = _results.ClearAsync();
        if (_runTask.IsCompleted && results.IsCompleted)
        {
            return;
        }
        e.Cancel = true;
        if (wasClosing)
        {
            return;
        }
        Cancel();
        SetRunningControls();
        await Task.WhenAll(_runTask, results);
        if (!_closed.Task.IsCompleted)
        {
            Close();
        }
    }
}
