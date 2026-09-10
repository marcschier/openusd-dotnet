// Copyright (c) marcschier. Licensed under the MIT License.

using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Platform.Storage;
using OpenUsd.Interop;
using OpenUsd.Rendering;
using OpenUsd.Rendering.Silk;

namespace OpenUsd.Viewer;

internal sealed partial class AuthoredRenderProductWindow : Window, IAsyncDisposable
{
    private readonly Func<string?, string?, CancellationToken, ValueTask<ViewerAuthoredRenderProductSnapshot>> _query;
    private readonly Func<ViewerAuthoredRenderProductRequest, Action<ViewerAuthoredRenderProductProgress>,
        CancellationToken, Task<RenderDiskJobResult>> _render;
    private readonly Func<string?> _getBackendUnsupportedReason;
    private readonly ViewerCompletedJobResults _results;
    private readonly object _operationGate = new();
    private readonly TaskCompletionSource _closed = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly CancellationTokenSource _windowLifetime = new();
    private CancellationTokenSource? _cancellation;
    private CancellationTokenSource? _refreshCancellation;
    private Task _runTask = Task.CompletedTask;
    private Task _refreshTask = Task.CompletedTask;
    private Task _pickTask = Task.CompletedTask;
    private int _queryGeneration;
    private bool _closing;
    private bool _picking;
    private bool _requestValid;
    private bool _updatingSettingsPath;
    private bool _refreshing;
    private ViewerAuthoredRenderProductSnapshot _snapshot =
        ViewerAuthoredRenderProductSnapshot.Empty("Render products have not been queried.");

    internal AuthoredRenderProductWindow(
        double start,
        double end,
        string renderer,
        Func<string?, string?, CancellationToken, ValueTask<ViewerAuthoredRenderProductSnapshot>> query,
        Func<ViewerAuthoredRenderProductRequest, Action<ViewerAuthoredRenderProductProgress>,
            CancellationToken, Task<RenderDiskJobResult>> render,
        Func<string?>? getBackendUnsupportedReason = null)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(render);
        _query = query;
        _render = render;
        _getBackendUnsupportedReason = getBackendUnsupportedReason ?? (static () => null);
        InitializeComponent();
        ViewerWindowTheme.Attach(this);
        _results = new ViewerCompletedJobResults(this, ProductViewResultsButton);
        ProductRenderer.Text = renderer;
        ProductStartTime.Text = ViewerTimelineMath.Format(start);
        ProductEndTime.Text = ViewerTimelineMath.Format(end);
        ProductOutputFolder.Text = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
        ProductSettingsPath.TextChanged += (_, _) =>
        {
            if (_closing || _closed.Task.IsCompleted)
            {
                return;
            }
            if (!_updatingSettingsPath)
            {
                Interlocked.Increment(ref _queryGeneration);
                _refreshCancellation?.Cancel();
            }
            ValidateRequest();
        };
        ProductSelector.SelectionChanged += (_, _) => ValidateRequest();
        ProductStartTime.TextChanged += (_, _) => ValidateRequest();
        ProductEndTime.TextChanged += (_, _) => ValidateRequest();
        ProductStep.TextChanged += (_, _) => ValidateRequest();
        ProductOutputFolder.TextChanged += (_, _) => ValidateRequest();
        ProductHdrFormat.SelectionChanged += (_, _) => ValidateRequest();
        ProductRefreshButton.Click += async (_, _) =>
        {
            await StartRefreshProductsAsync();
        };
        ProductBrowseButton.Click += async (_, _) =>
        {
            if (_picking || IsRunning || _closing)
            {
                return;
            }
            _pickTask = ChooseOutputAsync();
            await _pickTask;
        };
        ProductRenderButton.Click += async (_, _) =>
        {
            if (IsRunning || _refreshing || _picking || _closing || !_requestValid)
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
        ProductCancelButton.Click += (_, _) => Cancel();
        ProductCloseButton.Click += (_, _) => Close();
        Opened += async (_, _) =>
        {
            _refreshTask = StartRefreshProductsAsync();
            await _refreshTask;
            if (!_closing && !_closed.Task.IsCompleted)
            {
                ProductSelector.Focus();
            }
        };
        KeyDown += OnProductKeyDown;
        Closing += OnProductClosing;
        Closed += (_, _) =>
        {
            _windowLifetime.Cancel();
            _refreshCancellation?.Dispose();
            _refreshCancellation = null;
            _windowLifetime.Dispose();
            _closed.TrySetResult();
        };
        ValidateRequest();
    }

    internal bool IsRunning => _cancellation is not null;

    internal void UpdateContext(string description)
    {
        ProductRenderer.Text = description;
        ValidateRequest();
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

    private Task StartRefreshProductsAsync()
    {
        lock (_operationGate)
        {
            if (IsRunning || _closing)
            {
                return Task.CompletedTask;
            }
            if (!_refreshTask.IsCompleted)
            {
                return _refreshTask;
            }
            _refreshTask = RefreshProductsAsync();
            return _refreshTask;
        }
    }

    private async Task RefreshProductsAsync()
    {
        if (IsRunning || _closing)
        {
            return;
        }
        int generation = Interlocked.Increment(ref _queryGeneration);
        string? settingsPath = ReadSettingsPath();
        string? selected = (ProductSelector.SelectedItem as ViewerAuthoredRenderProductItem)?.Path;
        _refreshCancellation?.Dispose();
        _refreshCancellation = CancellationTokenSource.CreateLinkedTokenSource(_windowLifetime.Token);
        CancellationToken token = _refreshCancellation.Token;
        _refreshing = true;
        ProductStatus.Text = "Reading authored render settings...";
        SetRunningControls();
        try
        {
            ViewerAuthoredRenderProductSnapshot snapshot =
                await _query(settingsPath, selected, token);
            if (_closing || generation != Volatile.Read(ref _queryGeneration) ||
                !string.Equals(settingsPath, ReadSettingsPath(), StringComparison.Ordinal))
            {
                return;
            }
            _snapshot = snapshot;
            _updatingSettingsPath = true;
            try
            {
                ProductSettingsPath.Text = _snapshot.SettingsPath;
            }
            finally
            {
                _updatingSettingsPath = false;
            }
            ProductSelector.ItemsSource = _snapshot.Products;
            ProductSelector.SelectedItem =
                FirstProduct(_snapshot.Products, product => product.Path == selected) ??
                FirstProduct(_snapshot.Products, static product => product.CanRender) ??
                (_snapshot.Products.Count == 0 ? null : _snapshot.Products[0]);
        }
        catch (OperationCanceledException) when (_windowLifetime.IsCancellationRequested)
        {
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or
            NotSupportedException or OpenUsdNativeException)
        {
            if (!_closing && generation == Volatile.Read(ref _queryGeneration))
            {
                _snapshot = ViewerAuthoredRenderProductSnapshot.Empty(exception.Message);
                ProductSelector.ItemsSource = Array.Empty<ViewerAuthoredRenderProductItem>();
            }
        }
        finally
        {
            _refreshing = false;
            if (!_closing)
            {
                ValidateRequest();
            }
        }
    }

    private ViewerAuthoredRenderProductRequest ReadRequest()
    {
        if (!string.Equals(ReadSettingsPath(), _snapshot.SettingsPath, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Refresh the selected settings before rendering.");
        }
        if (_getBackendUnsupportedReason() is { } backendReason)
        {
            throw new NotSupportedException(backendReason);
        }
        if (ProductSelector.SelectedItem is not ViewerAuthoredRenderProductItem product)
        {
            throw new ArgumentException("Choose one authored render product.");
        }
        if (product.UnsupportedReason is { } unsupported)
        {
            throw new NotSupportedException(unsupported);
        }
        return new ViewerAuthoredRenderProductRequest(
            ReadRange(),
            ProductOutputFolder.Text ?? string.Empty,
            ReadSettingsPath(),
            product.Path,
            ReadHdrFormat());
    }

    private static ViewerAuthoredRenderProductItem? FirstProduct(
        IReadOnlyList<ViewerAuthoredRenderProductItem> products,
        Predicate<ViewerAuthoredRenderProductItem> predicate)
    {
        for (int index = 0; index < products.Count; index++)
        {
            if (predicate(products[index]))
            {
                return products[index];
            }
        }
        return null;
    }

    private string? ReadSettingsPath()
    {
        string? value = ProductSettingsPath.Text;
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private ViewerRenderSequenceRange ReadRange()
    {
        if (!ViewerTimelineMath.TryParse(ProductStartTime.Text, out double start) ||
            !ViewerTimelineMath.TryParse(ProductEndTime.Text, out double end) ||
            !ViewerTimelineMath.TryParse(ProductStep.Text, out double step))
        {
            throw new ArgumentException("Enter finite time codes and a positive step using invariant numeric syntax.");
        }
        return new ViewerRenderSequenceRange(start, end, step);
    }

    private RenderHdrColorFormat ReadHdrFormat() => ProductHdrFormat.SelectedIndex switch
    {
        0 => RenderHdrColorFormat.RawRgba16Float,
        1 => RenderHdrColorFormat.Exr,
        _ => throw new ArgumentException("Choose an HDR color output format.")
    };

    private void ValidateRequest(bool preserveStatus = false)
    {
        if (IsRunning || _closing)
        {
            return;
        }
        bool valid = false;
        string status;
        try
        {
            ViewerRenderSequenceRange range = ReadRange();
            ProductFrameCount.Text = $"{range.Times.Count} frame(s)";
            ProductOutputScope.Text = "PNG display companion + authored raw planes; " +
                (ReadHdrFormat() == RenderHdrColorFormat.Exr
                    ? "HDR color encoded as EXR when the product has a color variable"
                    : "HDR color encoded as top-down raw half RGBA when requested") +
                "; depth is top-down float32 normalized device depth.";
            ProductLimits.Text = "Limits: 4096 frames; product raster between 1 and 8192 pixels per side; " +
                "64 MiB capture and encoded files/frame; 4 GiB including manifest. Generated filenames never use " +
                "the authored product name as filesystem authority.";
            if (_getBackendUnsupportedReason() is { } backendReason)
            {
                status = backendReason;
            }
            else if (_snapshot.Error is { Length: > 0 } error)
            {
                status = error;
            }
            else if (!string.Equals(ReadSettingsPath(), _snapshot.SettingsPath, StringComparison.Ordinal))
            {
                status = "Refresh products for the selected render settings path before rendering.";
            }
            else if (ProductSelector.SelectedItem is not ViewerAuthoredRenderProductItem item)
            {
                status = "Choose one authored render product.";
            }
            else
            {
                ProductDetails.Text =
                    $"Camera {item.CameraPath}; raster {item.Dimensions.Width} x {item.Dimensions.Height}; " +
                    $"variables {item.VariableSummary}.";
                if (item.UnsupportedReason is { } unsupported)
                {
                    status = unsupported;
                }
                else if (ReadHdrFormat() == RenderHdrColorFormat.Exr &&
                    item.ExrUnsupportedReason is { } exrUnsupported)
                {
                    status = exrUnsupported;
                }
                else if (ViewerAuthoredRenderProductSelection.ValidateOutputParent(
                    ProductOutputFolder.Text ?? string.Empty) is { } outputUnsupported)
                {
                    status = outputUnsupported;
                }
                else
                {
                    status = "Ready. Unsupported products stay visible but cannot render as beauty.";
                    valid = true;
                }
            }
        }
        catch (ArgumentException exception)
        {
            ProductFrameCount.Text = "Invalid frame range";
            ProductDetails.Text = string.Empty;
            status = exception.Message;
        }
        catch (NotSupportedException exception)
        {
            status = exception.Message;
        }
        if (!preserveStatus)
        {
            ProductStatus.Text = status;
        }
        _requestValid = valid;
        SetRunningControls();
    }

    private async Task RunAsync()
    {
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(_windowLifetime.Token);
        _cancellation = cancellation;
        ProductOutputLocation.Text = string.Empty;
        ProductStatus.Text = "Preparing authored product...";
        ProductStatus.Classes.Set("viewer-warning", false);
        SetRunningControls();
        try
        {
            await _results.ClearAsync();
            cancellation.Token.ThrowIfCancellationRequested();
            ViewerAuthoredRenderProductRequest request = ReadRequest();
            ProductProgress.Maximum = request.Range.Times.Count;
            ProductProgress.Value = 0;
            RenderDiskJobResult result = await _render(request, ReportProgress, cancellation.Token);
            cancellation.Token.ThrowIfCancellationRequested();
            ProductOutputLocation.Text = result.OutputDirectory;
            ProductProgress.Value = result.Frames.Count;
            int hdrFiles = result.Frames.Count(static frame => frame.HdrColorFileName is not null);
            int depthFiles = result.Frames.Count(static frame => frame.DepthFileName is not null);
            string diagnostics = result.Diagnostics.Count == 0
                ? string.Empty
                : $" {result.Diagnostics.Count} renderer diagnostic(s) retained; see manifest.json.";
            ProductStatus.Text = $"Completed {result.Frames.Count} frame(s): {result.Frames.Count} PNG display " +
                $"companion(s), {hdrFiles} HDR plane(s), {depthFiles} normalized depth plane(s) + manifest.json." +
                $"{diagnostics} The pre-job Viewer state has been restored.";
            ProductStatus.Classes.Set("viewer-warning", result.Diagnostics.Any(static diagnostic =>
                diagnostic.Severity is RenderDiagnosticSeverity.Warning or RenderDiagnosticSeverity.Error));
            if (!_closing)
            {
                _results.Publish(result, $"Authored RenderProduct: {request.ProductPath}");
            }
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            ProductStatus.Text = "Cancelled. No RenderProduct output was published; the pre-job Viewer state has been restored.";
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or
            InvalidOperationException or InvalidDataException or NotSupportedException or ArgumentException or
            OpenUsdNativeException or OpenUsdSilkException or TimeoutException or OverflowException or
            AggregateException)
        {
            ProductStatus.Text = $"Product render failed: {ViewerPackageErrorFormatter.Format(exception)}";
            ViewerStartupOptions.WriteStatus(ProductStatus.Text);
        }
        finally
        {
            _cancellation = null;
            if (!_closing)
            {
                ValidateRequest(preserveStatus: true);
                SetRunningControls();
            }
        }
    }

    private void ReportProgress(ViewerAuthoredRenderProductProgress progress)
    {
        ProductProgress.Value = progress.Phase == "Preparing" ? 0 : progress.CompletedFrames;
        ProductStatus.Text = $"{progress.Phase}: {progress.CompletedFrames}/{progress.TotalFrames} frames; " +
            $"time {ViewerTimelineMath.Format(progress.TimeCode)}.";
    }

    private void SetRunningControls()
    {
        bool ready = !IsRunning && !_refreshing && !_closing && !_picking;
        ProductSettingsPath.IsEnabled = ready;
        ProductRefreshButton.IsEnabled = ready;
        ProductSelector.IsEnabled = ready;
        ProductRangeControls.IsEnabled = ready;
        ProductOutputControls.IsEnabled = ready;
        ProductOutputFolder.IsEnabled = ready;
        ProductBrowseButton.IsEnabled = ready;
        ProductRenderButton.IsEnabled = ready && _requestValid;
        ProductCancelButton.IsEnabled = IsRunning && !_closing;
    }

    private async Task ChooseOutputAsync()
    {
        CancellationToken cancellationToken = _windowLifetime.Token;
        _picking = true;
        SetRunningControls();
        try
        {
            IReadOnlyList<IStorageFolder> folders = await StorageProvider.OpenFolderPickerAsync(
                new FolderPickerOpenOptions { Title = "Choose RenderProduct output folder", AllowMultiple = false })
                .WaitAsync(cancellationToken);
            if (folders.Count != 0 && !_closing)
            {
                ProductOutputFolder.Text = folders[0].TryGetLocalPath() ??
                    throw new NotSupportedException("Choose a local filesystem folder.");
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            if (!_closing)
            {
                ProductStatus.Text = $"The folder picker failed: {exception.Message}";
            }
        }
        finally
        {
            _picking = false;
            if (!_closing)
            {
                SetRunningControls();
            }
        }
    }

    private void Cancel()
    {
        _cancellation?.Cancel();
        if (IsRunning)
        {
            ProductStatus.Text = "Cancelling... waiting for the in-flight product frame and Viewer restoration.";
            ProductCancelButton.IsEnabled = false;
        }
    }

    private async void OnProductClosing(object? sender, WindowClosingEventArgs e)
    {
        bool wasClosing = _closing;
        _closing = true;
        _windowLifetime.Cancel();
        _refreshCancellation?.Cancel();
        Task results = _results.ClearAsync();
        if (_runTask.IsCompleted && _refreshTask.IsCompleted && _pickTask.IsCompleted && results.IsCompleted)
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
        await Task.WhenAll(_runTask, _refreshTask, _pickTask, results);
        Close();
    }

    private void OnProductKeyDown(object? sender, KeyEventArgs e)
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
}
