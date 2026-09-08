// Copyright (c) marcschier. Licensed under the MIT License.

using System.Globalization;
using Avalonia.Controls;
using Avalonia.Input;
using OpenUsd.Editing;
using OpenUsd.Interop;

namespace OpenUsd.Viewer;

internal sealed partial class PropertyEditWindow : Window, IAsyncDisposable
{
    private readonly ViewerAuthoredEditController _editor;
    private readonly IViewerAssetFilePicker _assetFilePicker;
    private readonly string _primPath;
    private readonly ViewerAttributeSnapshot[] _attributes;
    private readonly double _timeCode;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly TaskCompletionSource _closed = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private ViewerPropertyEditCapture? _capture;
    private Task _operation = Task.CompletedTask;
    private bool _busy;
    private bool _closing;
    private bool _allowClose;

    internal PropertyEditWindow(
        ViewerAuthoredEditController editor, string primPath,
        IReadOnlyList<ViewerAttributeSnapshot> attributes, double timeCode, string? selectedName = null,
        IViewerAssetFilePicker? assetFilePicker = null)
    {
        ArgumentNullException.ThrowIfNull(editor);
        ArgumentException.ThrowIfNullOrWhiteSpace(primPath);
        ArgumentNullException.ThrowIfNull(attributes);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(attributes.Count, 256);
        if (!double.IsFinite(timeCode))
        {
            throw new ArgumentOutOfRangeException(nameof(timeCode));
        }
        _editor = editor;
        _assetFilePicker = assetFilePicker ?? ViewerAssetFilePicker.Default;
        _primPath = primPath;
        _timeCode = timeCode;
        _attributes = [.. attributes
            .Where(static attribute => ViewerPropertyEditParser.Supports(attribute.TypeName))
            .OrderByDescending(static attribute => attribute.HasAuthoredValue)];
        if (_attributes.Length == 0)
        {
            throw new NotSupportedException(
                "This prim has no supported scalar, vector, quaternion, matrix or asset review properties.");
        }
        InitializeComponent();
        ViewerWindowTheme.Attach(this);
        PropertyPrimPath.Text = primPath;
        PropertySelector.ItemsSource = _attributes.Select(static attribute =>
            $"{attribute.Name} ({attribute.TypeName})").ToArray();
        int selected = Array.FindIndex(_attributes, attribute => attribute.Name == selectedName);
        PropertySelector.SelectedIndex = Math.Max(0, selected);
        PropertyFieldSelector.ItemsSource = new[]
        {
            "Default opinion", $"Sample at {timeCode.ToString("R", CultureInfo.InvariantCulture)}"
        };
        PropertyFieldSelector.SelectedIndex = 0;
        PropertySelector.SelectionChanged += (_, _) => Start(CaptureAsync);
        PropertyFieldSelector.SelectionChanged += (_, _) => Start(CaptureAsync);
        RefreshPropertyButton.Click += (_, _) => Start(CaptureAsync);
        SetPropertyButton.Click += (_, _) => Start(() => ApplyAsync(UsdLayerEditOperation.Set));
        ClearPropertyButton.Click += (_, _) => Start(() => ApplyAsync(UsdLayerEditOperation.Clear));
        BlockPropertyButton.Click += (_, _) => Start(() => ApplyAsync(UsdLayerEditOperation.Block));
        ChoosePropertyAssetButton.Click += (_, _) => Start(ChooseAssetAsync);
        ClosePropertyButton.Click += (_, _) => Close();
        Opened += async (_, _) =>
        {
            Start(CaptureAsync);
            await _operation;
            if (!_closing && IsActive)
            {
                if (_capture is not null)
                {
                    PropertyValue.Focus();
                }
                else
                {
                    RefreshPropertyButton.Focus();
                }
            }
        };
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                e.Handled = true;
                Close();
            }
        };
        Closing += OnEditorClosing;
        Closed += (_, _) =>
        {
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

    private void Start(Func<Task> operation)
    {
        if (_busy || _closing)
        {
            PropertyEditStatus.Text = "Wait for the current operation or close to cancel it.";
            return;
        }
        _operation = RunAsync(operation);
    }

    private async Task RunAsync(Func<Task> operation)
    {
        _busy = true;
        UpdateAvailability();
        try
        {
            await operation();
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
            PropertyEditStatus.Text = "Editing cancelled. Any already-applied operation remains in document history.";
        }
        catch (Exception exception) when (exception is OpenUsdNativeException or InvalidDataException or
            NotSupportedException or ArgumentException or InvalidOperationException)
        {
            _capture = null;
            PropertyEditStatus.Text = $"Review edit unavailable: {exception.Message} Refresh to retry.";
        }
        finally
        {
            _busy = false;
            UpdateAvailability();
        }
    }

    private async Task CaptureAsync()
    {
        _capture = null;
        if (PropertySelector.SelectedIndex < 0 || PropertySelector.SelectedIndex >= _attributes.Length ||
            PropertyFieldSelector.SelectedIndex is not (0 or 1))
        {
            throw new InvalidOperationException("Choose a property and its default or sample field.");
        }
        ViewerAttributeSnapshot attribute = _attributes[PropertySelector.SelectedIndex];
        PropertyComposedValue.Text = $"Composed display snapshot (read-only): {attribute.Value}";
        UsdLayerEditField field = PropertyFieldSelector.SelectedIndex == 0
            ? UsdLayerEditField.Default : UsdLayerEditField.TimeSample;
        _capture = await _editor.CapturePropertyAsync(
            _primPath, attribute.Name, field, field == UsdLayerEditField.Default ? 0 : _timeCode, _lifetime.Token);
        ShowCapture();
        PropertyEditStatus.Text =
            (_editor.CanSaveReview
                ? "Verified review changes can be saved from File > Save Review Document. "
                : "This is session-only editing. File > Export Review Delta writes opinions only. ") +
            "Set replaces this field; Clear reveals weaker opinions; Block suppresses a value. " +
            "Source and simulation remain unchanged. Close to use shared document Undo/Redo.";
    }

    private async Task ApplyAsync(UsdLayerEditOperation operation)
    {
        ViewerPropertyEditCapture property = _capture ??
            throw new InvalidOperationException("Refresh the target opinion before editing.");
        UsdLayerEditAddress address = property.Capture.Snapshot.Addresses[0];
        UsdLayerEdit edit;
        if (operation == UsdLayerEditOperation.Set)
        {
            if (!ViewerPropertyEditParser.TryParse(
                property.TypeName, PropertyValue.Text ?? string.Empty, out UsdLayerEditValue? value, out string error))
            {
                PropertyEditStatus.Text = error;
                return;
            }
            edit = UsdLayerEdit.Set(address, value, property.TypeName, property.Variability, property.Custom);
        }
        else
        {
            edit = operation == UsdLayerEditOperation.Clear ? UsdLayerEdit.Clear(address) :
                UsdLayerEdit.Block(address, property.TypeName, property.Variability, property.Custom);
        }
        ViewerAuthoredEditResult result = await _editor.ApplyAsync(
            property.Capture, [edit], ViewerScalarFormatter.Bound($"{operation} {address.Path}", 512),
            gestureId: Guid.NewGuid(), cancellationToken: _lifetime.Token);
        PropertyEditStatus.Text = result.Message;
        if (result.Outcome == UsdLayerEditOutcome.Applied)
        {
            _capture = property with { Capture = property.Capture with { Snapshot = result.After! } };
            ShowCapture();
        }
        else
        {
            _capture = null;
            PropertyEditStatus.Text += " Refresh before retrying; no conflicting opinion was overwritten.";
        }
    }

    private async Task ChooseAssetAsync()
    {
        if (_capture is not { TypeName: "asset" })
        {
            PropertyEditStatus.Text = "Select and refresh an asset property before choosing a file.";
            return;
        }
        try
        {
            string? selected = await _assetFilePicker.OpenAssetAsync(this, _lifetime.Token);
            _lifetime.Token.ThrowIfCancellationRequested();
            if (selected is null)
            {
                PropertyEditStatus.Text = "Asset selection cancelled. The draft and authored opinions are unchanged.";
                return;
            }
            if (!Path.IsPathFullyQualified(selected) || !ViewerPropertyEditParser.TryParse(
                "asset", selected, out _, out _))
            {
                PropertyEditStatus.Text = "Choose an absolute local asset path within the review value limit.";
                return;
            }
            PropertyValue.Text = selected;
            PropertyEditStatus.Text = "Asset path selected. Select Set to change this review opinion.";
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or
            NotSupportedException or ArgumentException)
        {
            PropertyEditStatus.Text = $"The asset picker is unavailable: {exception.Message} The draft is unchanged.";
        }
    }

    private void ShowCapture()
    {
        ViewerPropertyEditCapture capture = _capture ??
            throw new InvalidOperationException("No review opinion has been captured.");
        UsdLayerAuthoredOpinion opinion = capture.Capture.Snapshot.Opinions[0];
        PropertyInputHint.Text = capture.TypeName switch
        {
            "asset" => "Enter the authored path, or choose a file for an absolute path. Set authors the change.",
            "float2" or "texCoord2f" => "Two components: (x, y). Values are not clamped.",
            "float4" or "color4f" => "Four components: (x, y, z, w). Values are not clamped.",
            "quatf" => "Scalar-first quaternion: (real, x, y, z). Values are not normalized.",
            "matrix4d" => "Sixteen row-major values: ((row 0), (row 1), (row 2), (row 3)). " +
                "OpenUSD translation is in the first three components of row 3.",
            _ => $"Enter a value of declared type {capture.TypeName}."
        };
        string value = opinion.Value.Kind is UsdLayerEditValueKind.Absent or UsdLayerEditValueKind.Block
            ? opinion.Value.Kind.ToString() : ViewerPropertyEditParser.Format(opinion.Value);
        PropertyTargetState.Text = $"Review target: {capture.Capture.LayerIdentifier}\n" +
            $"Revision {capture.Capture.Snapshot.Revision}; {opinion.Address.Field}: {value}";
        PropertyValue.Text = ViewerPropertyEditParser.Format(opinion.Value);
    }

    private void UpdateAvailability()
    {
        bool ready = !_busy && !_closing;
        PropertySelector.IsEnabled = ready;
        PropertyFieldSelector.IsEnabled = ready;
        PropertyValue.IsEnabled = ready;
        RefreshPropertyButton.IsEnabled = ready;
        SetPropertyButton.IsEnabled = ready && _capture is not null;
        ClearPropertyButton.IsEnabled = ready && _capture is not null;
        BlockPropertyButton.IsEnabled = ready && _capture is not null;
        ChoosePropertyAssetButton.IsVisible = _capture is { TypeName: "asset" };
        ChoosePropertyAssetButton.IsEnabled = ready && _capture is { TypeName: "asset" };
    }

    private void OnEditorClosing(object? sender, WindowClosingEventArgs e)
    {
        if (_allowClose)
        {
            return;
        }
        e.Cancel = true;
        if (_closing)
        {
            return;
        }
        _closing = true;
        _lifetime.Cancel();
        _ = FinishCloseAsync();
    }

    private async Task FinishCloseAsync()
    {
        await _operation;
        _allowClose = true;
        Close();
    }
}
