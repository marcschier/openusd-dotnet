// Copyright (c) marcschier. Licensed under the MIT License.

using Avalonia.Threading;
using OpenUsd.Rendering;

namespace OpenUsd.Viewer;

internal sealed class ViewerRenderSequenceRange
{
    internal ViewerRenderSequenceRange(double start, double end, double step)
    {
        if (!double.IsFinite(start) || !double.IsFinite(end) || !double.IsFinite(step) ||
            step <= 0 || end < start)
        {
            throw new ArgumentException("Use finite time codes, an end at or after the start, and a positive step.");
        }
        double intervals = (end - start) / step;
        double count = Math.Floor(Math.BitIncrement(intervals)) + 1;
        if (!double.IsFinite(count) || count > RenderDiskJobLimits.Default.MaximumFrames)
        {
            throw new ArgumentException("An image sequence must contain between 1 and 4096 frames.");
        }
        var times = new double[(int)count];
        for (int index = 0; index < times.Length; index++)
        {
            double time = Math.FusedMultiplyAdd(index, step, start);
            if (time > end && time <= Math.BitIncrement(end))
            {
                time = end;
            }
            if (!double.IsFinite(time) || time > end || (index > 0 && time <= times[index - 1]))
            {
                throw new ArgumentException("The step must produce distinct finite time codes within the range.");
            }
            times[index] = time;
        }
        Times = Array.AsReadOnly(times);
    }

    internal IReadOnlyList<double> Times { get; }

    internal static string CreateOutputDirectory(string parent)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(parent);
        if (!Path.IsPathFullyQualified(parent))
        {
            throw new ArgumentException("Choose an absolute local output folder.", nameof(parent));
        }
        string fullPath = Path.GetFullPath(parent);
        if (!Directory.Exists(fullPath))
        {
            throw new DirectoryNotFoundException("The chosen output folder must already exist.");
        }
        return Path.Combine(fullPath, $"render-sequence-{Guid.NewGuid():N}");
    }
}

internal readonly record struct ViewerRenderSequenceOutputOptions(
    bool IncludeDeviceDepth, bool IncludeHdrColor,
    RenderHdrColorFormat HdrColorFormat = RenderHdrColorFormat.RawRgba16Float)
{
    internal bool HasAdditionalPlanes => IncludeDeviceDepth || IncludeHdrColor;

    internal string? GetAdmissionUnsupportedReason(ViewportDimensions viewport)
    {
        if (HdrColorFormat is not (RenderHdrColorFormat.RawRgba16Float or RenderHdrColorFormat.Exr))
        {
            return "Choose raw half-float or EXR for the HDR output format.";
        }
        if (HdrColorFormat == RenderHdrColorFormat.Exr)
        {
            if (!IncludeHdrColor)
            {
                return "EXR output requires HDR color data.";
            }
            if (!ExrRgba16FloatWriter.IsSupported)
            {
                return "EXR output currently requires the Windows x64 Core encoder.";
            }
        }
        if (viewport.Width is < 1 or > 8192 || viewport.Height is < 1 or > 8192)
        {
            return "Image sequences require viewport dimensions between 1 and 8192 pixels per side.";
        }
        int bytesPerPixel = HasAdditionalPlanes ? 20 : 4;
        long bytes = (long)viewport.Width * viewport.Height * bytesPerPixel;
        return bytes > RenderDiskJobLimits.Default.MaximumFrameBytes
            ? $"The selected outputs exceed the 64 MiB capture limit ({bytesPerPixel} bytes/pixel). " +
                "Reduce the viewport size or turn off the optional data outputs."
            : null;
    }
}

internal readonly record struct ViewerRenderSequenceProgress(
    string Phase, int CompletedFrames, int TotalFrames, double TimeCode);

internal static class ViewerRenderSequenceRunner
{
    internal static Task<RenderDiskJobResult> ExecuteAsync(
        RenderDiskJobRequest request,
        Func<StageRenderState, CancellationToken, ValueTask<ViewerFrameCaptureResult>> capture,
        Func<ValueTask> restore,
        Action<ViewerRenderSequenceProgress> progress,
        CancellationToken cancellationToken) =>
        Task.Factory.StartNew(
            () => RenderDiskJob.Execute(
                request, new FrameSource(capture, restore, progress, request.Frames.Count), cancellationToken),
            CancellationToken.None,
            TaskCreationOptions.LongRunning,
            TaskScheduler.Default);

    private sealed class FrameSource(
        Func<StageRenderState, CancellationToken, ValueTask<ViewerFrameCaptureResult>> capture,
        Func<ValueTask> restore,
        Action<ViewerRenderSequenceProgress> progress,
        int totalFrames) : IRenderJobFrameSource
    {
        private int _completed;

        public RenderJobImage Render(StageRenderState state, CancellationToken cancellationToken)
        {
            if (Dispatcher.UIThread.CheckAccess())
            {
                throw new InvalidOperationException("Disk encoding must not run on the Viewer UI thread.");
            }
            // Only the dedicated encoding worker waits synchronously. Graphics and state work
            // enter the existing dispatcher, including after any asynchronous stage query.
            Task<RenderJobImage> frame = Dispatcher.UIThread.InvokeAsync(async () =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                progress(new ViewerRenderSequenceProgress("Rendering", _completed, totalFrames, state.Time.TimeCode));
                ViewerFrameCaptureResult image = await capture(state, cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                _completed++;
                if (_completed == totalFrames)
                {
                    // Restoration is part of the source's success boundary, before the engine
                    // can encode its final frame and atomically publish a completed directory.
                    await restore();
                }
                progress(new ViewerRenderSequenceProgress("Encoding", _completed, totalFrames, state.Time.TimeCode));
                return new RenderJobImage(image.Width, image.Height, image.Rgba, image.RowOrder switch
                {
                    ViewerFrameRowOrder.TopDown => Rgba8RowOrder.TopDown,
                    ViewerFrameRowOrder.BottomUp => Rgba8RowOrder.BottomUp,
                    _ => throw new InvalidDataException("The capture has an unknown RGBA row order.")
                })
                {
                    Diagnostics = image.Diagnostics,
                    DeviceDepth = image.DeviceDepth,
                    HdrColor = image.HdrColor
                };
            }, DispatcherPriority.Normal, cancellationToken).GetAwaiter().GetResult();
            return frame.GetAwaiter().GetResult();
        }
    }
}
