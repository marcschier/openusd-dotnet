// Copyright (c) marcschier. Licensed under the MIT License.

using System.Globalization;
using System.Numerics;
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
        ResultsCloseButton.Click += (_, _) => Close();
        ResultsFrames.SelectionChanged += (_, _) =>
        {
            var selected = ResultsFrames.SelectedItem as CompletedRenderJobTile;
            ResultsSource.Text = selected is null
                ? "Select a completed frame to inspect its original stage."
                : $"Stage: {selected.StageIdentifier}";
            ToolTip.SetTip(ResultsSource, selected is null ? null : ResultsSource.Text);
            ResultsRequestContext.Text = (ResultsFrames.SelectedItem as CompletedRenderJobTile)?.RequestContext ??
                "Select a completed frame to inspect its render request.";
            ResultsFrameContext.Text = (ResultsFrames.SelectedItem as CompletedRenderJobTile)?.RecordedContext ??
                "Select a completed frame to inspect its recorded context.";
            ResultsCameraContext.Text = (ResultsFrames.SelectedItem as CompletedRenderJobTile)?.CameraContext ??
                "Select a completed frame to inspect its camera request.";
        };
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
        if (_closed.Task.IsCompleted)
        {
            return;
        }
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
        if (!_closed.Task.IsCompleted)
        {
            Close();
        }
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
    private readonly StageRenderState _requestedState;

    internal CompletedRenderJobTile(ViewerCompletedFramePreview frame)
    {
        _requestedState = frame.RequestedState;
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
        CameraContext = string.Join("\n",
            "Recorded camera request; not effective backend state or independent source verification.",
            $"Mode: {frame.Camera.Mode}",
            frame.Camera.Mode == CameraMode.Matrices
                ? $"View (row-major): {FormatMatrix(frame.Camera.View)}\n" +
                    $"Projection (row-major): {FormatMatrix(frame.Camera.Projection)}"
                : "The renderer-selected camera was not recorded.");
        for (int index = 0; index < frame.Camera.ClipPlanes.Count; index++)
        {
            CameraContext += FormattableString.Invariant(
                $"\nClip plane {index} (view-space): {FormatVector(frame.Camera.ClipPlanes[index])}");
        }
        if (frame.Camera.Mode == CameraMode.Matrices && frame.Camera.ClipPlanes.Count == 0)
        {
            CameraContext += "\nNo clip planes were requested.";
        }
        RecordedContext = frame.Simulation is { } simulation
            ? string.Join("\n",
                $"Epoch: {simulation.Epoch:D}",
                $"Scenario: {simulation.ScenarioIdentity}",
                FormattableString.Invariant($"Source revision: {simulation.SourceRevision}"),
                FormattableString.Invariant($"Publication revision: {simulation.PublicationRevision}"),
                FormattableString.Invariant($"Step: {simulation.StepIndex}"),
                FormattableString.Invariant($"Simulation seconds: {simulation.SimulationSeconds:R}"),
                FormattableString.Invariant($"Time code: {simulation.TimeCode:R}"),
                FormattableString.Invariant($"Fixed step seconds: {simulation.FixedStepSeconds:R}"),
                FormattableString.Invariant($"Binding revision: {simulation.BindingRevision}"),
                FormattableString.Invariant($"Bodies: {simulation.BodyCount}"),
                FormattableString.Invariant($"Deformations: {simulation.DeformationCount}"),
                $"Review: {simulation.ReviewIdentity ?? "not recorded"}",
                "Host attestations; not independent source verification.")
            : "No simulation publication was recorded for this frame.";
        AccessibleName =
            $"{FrameLabel}, time code {ViewerTimelineMath.Format(TimeCode)}, {DimensionsLabel}, {FileName}";
        Image = CreateBitmap(frame);
    }

    public int FrameIndex { get; }
    public double TimeCode { get; }
    public string FrameLabel { get; }
    public string TimeLabel { get; }
    public string DimensionsLabel { get; }
    public string FileName { get; }
    public string RecordedContext { get; }
    public string CameraContext { get; }
    public string StageIdentifier => _requestedState.Stage.Identifier;
    public string RequestContext => FormatRequest(_requestedState);
    public string AccessibleName { get; }
    public Bitmap Image { get; }

    public void Dispose() => Image.Dispose();

    private static string FormatRequest(StageRenderState state)
    {
        RenderSettings settings = state.RenderSettings;
        var lines = new List<string>
        {
            "Recorded request; not effective backend state or independent source verification.",
            $"Purposes: {state.Display.Purposes}",
            $"Visibility: {state.Display.Visibility}",
            $"Draw mode: {state.Display.DrawMode}",
            FormattableString.Invariant($"Samples per pixel: {settings.SamplesPerPixel}"),
            $"Lighting: {settings.EnableLighting}",
            $"Shadows: {settings.EnableShadows}",
            $"Backface culling: {settings.BackfaceCulling}",
            $"Scene materials: {settings.UseSceneMaterials}",
            $"Complexity: {settings.Complexity}",
            $"Output transform: {settings.OutputTransform}",
            FormattableString.Invariant($"Exposure: {settings.Exposure:R}"),
            $"Clear RGBA: {FormatVector(settings.ClearColor)}"
        };
        if (settings.DisplayTransform is { } transform)
        {
            lines.Add($"OCIO config request: {transform.ConfigPath}");
            lines.Add($"Source colour space: {transform.SourceColorSpace}");
            lines.Add($"Display: {transform.Display ?? "config default requested"}");
            lines.Add($"View: {transform.View ?? "display default requested"}");
            lines.Add($"Look: {transform.Look ?? "none requested"}");
            lines.Add(FormattableString.Invariant($"Lattice size: {transform.LatticeSize}"));
            lines.Add(FormattableString.Invariant(
                $"Shaper log2: {transform.ShaperMinimumLog2:R} {transform.ShaperMaximumLog2:R}"));
        }
        else
        {
            lines.Add("No OCIO display transform was requested.");
        }
        lines.Add(FormattableString.Invariant($"Selection items: {state.Selection.Items.Count}"));
        for (int index = 0; index < state.Selection.Items.Count; index++)
        {
            SelectionItem item = state.Selection.Items[index];
            lines.Add(FormattableString.Invariant($"Item {index}: {item.PrimPath}"));
            lines.Add(item.ElementIndex is { } element
                ? FormattableString.Invariant($"Element: {item.ElementKind} {element}")
                : "Element: None");
            for (int entryIndex = 0; entryIndex < item.InstancerContext.Count; entryIndex++)
            {
                SelectionInstancerEntry entry = item.InstancerContext[entryIndex];
                lines.Add(FormattableString.Invariant(
                    $"Instancer {entryIndex}: {entry.InstancerPath}; instance {entry.InstanceIndex}"));
            }
        }
        return string.Join("\n", lines);
    }

    private static string FormatMatrix(Matrix4x4 matrix) => string.Join("; ",
        FormatVector(new Vector4(matrix.M11, matrix.M12, matrix.M13, matrix.M14)),
        FormatVector(new Vector4(matrix.M21, matrix.M22, matrix.M23, matrix.M24)),
        FormatVector(new Vector4(matrix.M31, matrix.M32, matrix.M33, matrix.M34)),
        FormatVector(new Vector4(matrix.M41, matrix.M42, matrix.M43, matrix.M44)));

    private static string FormatVector(Vector4 vector) => FormattableString.Invariant(
        $"{vector.X:R} {vector.Y:R} {vector.Z:R} {vector.W:R}");

    private static WriteableBitmap CreateBitmap(ViewerCompletedFramePreview frame)
    {
        var bitmap = new WriteableBitmap(
            new PixelSize(frame.Width, frame.Height), new Avalonia.Vector(96, 96),
            PixelFormat.Rgba8888, AlphaFormat.Unpremul);
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
