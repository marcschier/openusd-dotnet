// Copyright (c) marcschier. Licensed under the MIT License.

using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Platform.Storage;

namespace OpenUsd.Viewer;

internal sealed partial class CaptureComparisonWindow : Window
{
    private readonly TaskCompletionSource _closed = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Func<string, string, CancellationToken, Task<ViewerCapturePair>> _loadPair =
        ViewerCaptureComparison.LoadAsync;
    private CancellationTokenSource? _loadCancellation;
    private Task? _loadTask;
    private WriteableBitmap? _beforeBitmap;
    private WriteableBitmap? _afterBitmap;
    private string? _beforePath;
    private string? _afterPath;
    private ViewerCapturePresentation? _presentation;
    private bool _closing;
    private bool _picking;
    private bool _restoreOperationFocus;

    internal CaptureComparisonWindow()
    {
        InitializeComponent();
        ViewerWindowTheme.Attach(this);
        ViewerTextBoxScrolling.ContainCaretRequests(ComparisonNotes);
        ChooseBeforeButton.Click += async (_, _) => await ChooseAsync(before: true);
        ChooseAfterButton.Click += async (_, _) => await ChooseAsync(before: false);
        CompareAgainButton.Click += async (_, _) => await LoadSelectedPairAsync();
        OpenPresentationButton.Click += async (_, _) => await ChoosePresentationAsync(save: false);
        SavePresentationButton.Click += async (_, _) => await ChoosePresentationAsync(save: true);
        CancelComparisonButton.Click += (_, _) => CancelLoading();
        CloseComparisonButton.Click += (_, _) => Close();
        ComparisonMode.SelectionChanged += (_, _) => ApplyComparisonMode();
        KeyDown += OnComparisonKeyDown;
        Opened += (_, _) => ChooseBeforeButton.Focus();
        Closing += OnComparisonClosing;
        Closed += (_, _) =>
        {
            DisposeImages();
            _closed.TrySetResult();
        };
    }

    internal CaptureComparisonWindow(Func<string, string, CancellationToken, Task<ViewerCapturePair>> loadPair)
        : this()
    {
        ArgumentNullException.ThrowIfNull(loadPair);
        _loadPair = loadPair;
    }

    internal Task CloseAsync()
    {
        Close();
        return _closed.Task;
    }

    internal Task LoadPairAsync(string beforePath, string afterPath)
    {
        if (_loadCancellation is not null || _closing)
        {
            throw new InvalidOperationException("The comparison window is already loading or closing.");
        }
        _beforePath = beforePath;
        _afterPath = afterPath;
        _loadTask = LoadCoreAsync(beforePath, afterPath);
        return _loadTask;
    }

    internal Task LoadPresentationAsync(string path)
    {
        RequireAvailable();
        _loadTask = LoadCoreAsync(path, path, presentationFile: true);
        return _loadTask;
    }

    internal Task SavePresentationAsync(string path)
    {
        RequireAvailable();
        ViewerCapturePresentation presentation = _presentation ??
            throw new InvalidOperationException("Load a complete comparison before saving it.");
        _loadTask = SavePresentationCoreAsync(path, presentation with
        {
            DisplayMode = ComparisonMode.SelectedIndex,
            Notes = ComparisonNotes.Text ?? string.Empty
        });
        return _loadTask;
    }

    private void RequireAvailable()
    {
        if (_loadCancellation is not null || _closing)
        {
            throw new InvalidOperationException("The comparison window is already loading, saving or closing.");
        }
    }

    private async Task ChoosePresentationAsync(bool save)
    {
        _picking = true;
        UpdateAvailability();
        try
        {
            var type = new FilePickerFileType("Saved capture comparison") { Patterns = ["*.usdcompare.json"] };
            IStorageFile? file;
            if (save)
            {
                file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
                {
                    Title = "Save historical capture comparison to a new file",
                    SuggestedFileName = "comparison.usdcompare.json",
                    FileTypeChoices = [type],
                    DefaultExtension = "usdcompare.json"
                });
            }
            else
            {
                IReadOnlyList<IStorageFile> files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
                {
                    Title = "Open historical capture comparison",
                    AllowMultiple = false,
                    FileTypeFilter = [type]
                });
                file = files.Count == 0 ? null : files[0];
            }
            if (_closing)
            {
                return;
            }
            if (file is null)
            {
                SetStatus("Comparison file selection cancelled. The current comparison was kept.");
                return;
            }
            string? path = file.TryGetLocalPath();
            if (path is null)
            {
                SetStatus("Choose a local comparison file; remote storage is not supported.", error: true);
                return;
            }
            await (save ? SavePresentationAsync(path) : LoadPresentationAsync(path));
        }
        catch (IOException exception)
        {
            SetStatus($"The comparison file picker failed: {exception.Message}", error: true);
        }
        catch (UnauthorizedAccessException exception)
        {
            SetStatus($"The comparison file picker failed: {exception.Message}", error: true);
        }
        catch (NotSupportedException exception)
        {
            SetStatus($"The comparison file picker is unavailable: {exception.Message}", error: true);
        }
        finally
        {
            _picking = false;
            UpdateAvailability();
        }
    }

    private async Task SavePresentationCoreAsync(string path, ViewerCapturePresentation presentation)
    {
        using var cancellation = new CancellationTokenSource();
        _loadCancellation = cancellation;
        UpdateAvailability();
        SetStatus("Saving historical comparison references and decoded pixel checksums...");
        try
        {
            await Task.Run(() => ViewerCapturePresentationFile.SaveAsync(path, presentation, cancellation.Token),
                cancellation.Token);
            SetStatus("Comparison saved. Keep both capture files at their original paths. " +
                "Reopening verifies decoded pixels; this does not establish current scene or camera state.");
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            SetStatus("Comparison save cancelled. The displayed images were kept.");
        }
        catch (InvalidDataException exception)
        {
            SetStatus($"Could not save comparison: {exception.Message}", error: true);
        }
        catch (IOException exception)
        {
            SetStatus($"Could not save comparison: {exception.Message} Choose a new destination.", error: true);
        }
        catch (UnauthorizedAccessException exception)
        {
            SetStatus($"Could not save comparison: {exception.Message}", error: true);
        }
        finally
        {
            _loadCancellation = null;
            UpdateAvailability();
        }
    }

    private async Task ChooseAsync(bool before)
    {
        _picking = true;
        UpdateAvailability();
        try
        {
            IReadOnlyList<IStorageFile> files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = before ? "Choose before capture (A)" : "Choose after capture (B)",
                AllowMultiple = false,
                FileTypeFilter = [new FilePickerFileType("Viewer capture") { Patterns = ["*.png", "*.bmp"] }]
            });
            if (_closing)
            {
                return;
            }
            if (files.Count == 0)
            {
                SetStatus("File selection cancelled. The current comparison was kept.");
                return;
            }
            string? path = files[0].TryGetLocalPath();
            if (path is null)
            {
                SetStatus(
                    "Choose a local PNG or BMP file; remote storage-provider items cannot be compared.", error: true);
                return;
            }
            if (before)
            {
                _beforePath = path;
                BeforeName.Text = Path.GetFileName(path);
            }
            else
            {
                _afterPath = path;
                AfterName.Text = Path.GetFileName(path);
            }
            await LoadSelectedPairAsync();
        }
        catch (IOException exception)
        {
            SetStatus($"The capture picker failed: {exception.Message}", error: true);
        }
        catch (UnauthorizedAccessException exception)
        {
            SetStatus($"The capture picker failed: {exception.Message}", error: true);
        }
        catch (NotSupportedException exception)
        {
            SetStatus($"The capture picker is unavailable: {exception.Message}", error: true);
        }
        finally
        {
            _picking = false;
            UpdateAvailability();
        }
    }

    private Task LoadSelectedPairAsync()
    {
        if (_beforePath is { } before && _afterPath is { } after)
        {
            return LoadPairAsync(before, after);
        }
        SetStatus("Choose the other capture to start comparing.");
        return Task.CompletedTask;
    }

    private async Task LoadCoreAsync(string beforePath, string afterPath, bool presentationFile = false)
    {
        using var cancellation = new CancellationTokenSource();
        _loadCancellation = cancellation;
        CaptureReviewIdentity? previousBefore = null;
        CaptureReviewIdentity? previousAfter = null;
        string previousNotes = ComparisonNotes.Text ?? string.Empty;
        if (!presentationFile && previousNotes.Length != 0 && _presentation is { } previous)
        {
            previousBefore = CaptureReviewIdentity.From(previous.BeforePath, previous.Images.Before);
            previousAfter = CaptureReviewIdentity.From(previous.AfterPath, previous.Images.After);
        }
        DisposeImages();
        UpdateAvailability();
        BeforeName.Text = Path.GetFileName(beforePath);
        AfterName.Text = Path.GetFileName(afterPath);
        ToolTip.SetTip(BeforeName, beforePath);
        ToolTip.SetTip(AfterName, afterPath);
        SetStatus("Loading captures... Cancel stops reading; a partial pair is never displayed.");
        try
        {
            ViewerCapturePresentation presentation = presentationFile
                ? await ViewerCapturePresentationFile.LoadAsync(beforePath, cancellation.Token)
                : new ViewerCapturePresentation(Path.GetFullPath(beforePath), Path.GetFullPath(afterPath),
                    await _loadPair(beforePath, afterPath, cancellation.Token), ComparisonMode.SelectedIndex);
            ViewerCapturePair pair = presentation.Images;
            cancellation.Token.ThrowIfCancellationRequested();
            if (previousBefore is not null &&
                previousBefore == CaptureReviewIdentity.From(presentation.BeforePath, pair.Before) &&
                previousAfter == CaptureReviewIdentity.From(presentation.AfterPath, pair.After))
            {
                presentation = presentation with { Notes = previousNotes };
            }
            _beforeBitmap = CreateBitmap(pair.Before);
            _afterBitmap = CreateBitmap(pair.After);
            _presentation = presentation;
            _beforePath = presentation.BeforePath;
            _afterPath = presentation.AfterPath;
            BeforeName.Text = Path.GetFileName(_beforePath);
            AfterName.Text = Path.GetFileName(_afterPath);
            ToolTip.SetTip(BeforeName, _beforePath);
            ToolTip.SetTip(AfterName, _afterPath);
            ComparisonMode.SelectedIndex = presentation.DisplayMode;
            ComparisonNotes.Text = presentation.Notes;
            ComparisonNotesExpander.IsExpanded = presentation.Notes.Length != 0;
            BeforeImage.Source = _beforeBitmap;
            AfterImage.Source = _afterBitmap;
            SetStatus(
                $"A: {pair.Before.Width} x {pair.Before.Height}; B: {pair.After.Width} x {pair.After.Height}. " +
                "Historical pixels only; no current scene/camera identity is asserted. " +
                "Render/OCIO settings are unchanged.");
            ApplyComparisonMode();
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            DisposeImages();
            SetStatus("Comparison loading cancelled. Choose captures or select Compare to retry.");
        }
        catch (InvalidDataException exception)
        {
            DisposeImages();
            SetStatus($"Could not compare captures: {exception.Message}", error: true);
        }
        catch (IOException exception)
        {
            DisposeImages();
            SetStatus($"Could not compare captures: {exception.Message}", error: true);
        }
        catch (UnauthorizedAccessException exception)
        {
            DisposeImages();
            SetStatus($"Could not read captures: {exception.Message}", error: true);
        }
        finally
        {
            _loadCancellation = null;
            UpdateAvailability();
        }
    }

    private sealed record CaptureReviewIdentity(string Path, int Width, int Height, string PixelSha256)
    {
        internal static CaptureReviewIdentity From(string path, ViewerCaptureImage image) =>
            new(path, image.Width, image.Height, Convert.ToHexString(SHA256.HashData(image.Rgba)));
    }

    private static WriteableBitmap CreateBitmap(ViewerCaptureImage image)
    {
        var bitmap = new WriteableBitmap(
            new PixelSize(image.Width, image.Height), new Vector(96, 96), PixelFormat.Rgba8888, AlphaFormat.Unpremul);
        using ILockedFramebuffer buffer = bitmap.Lock();
        int rowBytes = checked(image.Width * 4);
        for (int y = 0; y < image.Height; y++)
        {
            Marshal.Copy(image.Rgba, y * rowBytes, buffer.Address + (y * buffer.RowBytes), rowBytes);
        }
        return bitmap;
    }

    private void ApplyComparisonMode()
    {
        bool before = ComparisonMode.SelectedIndex != 2;
        bool after = ComparisonMode.SelectedIndex != 1;
        BeforePane.IsVisible = before;
        AfterPane.IsVisible = after;
        ComparisonImages.ColumnDefinitions[0].Width = before ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
        ComparisonImages.ColumnDefinitions[1].Width = after ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
    }

    private void UpdateAvailability()
    {
        bool ownsFocus = IsActive || IsKeyboardFocusWithin;
        _restoreOperationFocus |= CancelComparisonButton.IsKeyboardFocusWithin;
        bool available = _loadCancellation is null && !_closing && !_picking;
        ChooseBeforeButton.IsEnabled = available;
        ChooseAfterButton.IsEnabled = available;
        OpenPresentationButton.IsEnabled = available;
        SavePresentationButton.IsEnabled = available && _presentation is not null;
        ComparisonNotes.IsEnabled = available && _presentation is not null;
        CompareAgainButton.IsEnabled = available && _beforePath is not null && _afterPath is not null;
        ComparisonMode.IsEnabled = available && _beforeBitmap is not null && _afterBitmap is not null;
        CancelComparisonButton.IsEnabled = _loadCancellation is not null && !_closing;
        if (ownsFocus && !_closing)
        {
            if (CancelComparisonButton.IsEnabled)
            {
                CancelComparisonButton.Focus();
            }
            else if (available && _restoreOperationFocus)
            {
                (CompareAgainButton.IsEnabled ? CompareAgainButton : ChooseBeforeButton).Focus();
                _restoreOperationFocus = false;
            }
        }
    }

    private void SetStatus(string message, bool error = false)
    {
        ComparisonStatus.Text = message;
        ComparisonStatus.Classes.Set("viewer-error", error);
    }

    private void CancelLoading()
    {
        if (_loadCancellation is not { } cancellation)
        {
            SetStatus("There is no capture load to cancel.");
            return;
        }
        // Cancellation may complete the reader inline and publish the final status.
        SetStatus("Cancelling capture loading...");
        cancellation.Cancel();
    }

    private void OnComparisonKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            e.Handled = true;
            Close();
        }
    }

    private async void OnComparisonClosing(object? sender, WindowClosingEventArgs e)
    {
        if (_closed.Task.IsCompleted)
        {
            return;
        }
        _closing = true;
        _loadCancellation?.Cancel();
        if (_loadTask is { IsCompleted: false } pending)
        {
            e.Cancel = true;
            await pending;
            if (!_closed.Task.IsCompleted)
            {
                Close();
            }
        }
    }

    private void DisposeImages()
    {
        BeforeImage.Source = null;
        AfterImage.Source = null;
        _beforeBitmap?.Dispose();
        _afterBitmap?.Dispose();
        _beforeBitmap = null;
        _afterBitmap = null;
        _presentation = null;
        ComparisonNotes.Text = string.Empty;
        ComparisonNotesExpander.IsExpanded = false;
    }
}
