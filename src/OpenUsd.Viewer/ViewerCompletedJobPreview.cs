// Copyright (c) marcschier. Licensed under the MIT License.

using System.Globalization;
using OpenUsd.Rendering;

namespace OpenUsd.Viewer;

internal sealed class ViewerCompletedFramePreview(RenderDiskFrameResult frame, PngRgba8Image image) : IDisposable
{
    internal int Index { get; } = frame.Index;
    internal double TimeCode { get; } = frame.State.Time.TimeCode;
    internal string FileName { get; } = frame.FileName;
    internal ViewportDimensions SourceDimensions { get; } = frame.State.Viewport;
    internal int Width { get; } = image.Width;
    internal int Height { get; } = image.Height;
    internal byte[] Pixels { get; private set; } = image.Pixels;

    public void Dispose() => Pixels = [];
}

internal sealed class ViewerCompletedJobPreview : IDisposable
{
    internal const int MaximumFrames = 16;
    internal const int ThumbnailSize = 256;
    private readonly List<ViewerCompletedFramePreview> _frames = [];

    private ViewerCompletedJobPreview()
    {
    }

    internal IReadOnlyList<ViewerCompletedFramePreview> Frames => _frames;

    internal static Task<ViewerCompletedJobPreview> LoadAsync(
        RenderDiskJobResult job, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(job);
        return Task.Run(() => Load(job, cancellationToken), cancellationToken);
    }

    private static ViewerCompletedJobPreview Load(RenderDiskJobResult job, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        int total = job.Frames.Count;
        if (total < 1 || total > RenderDiskJobLimits.Default.MaximumFrames)
        {
            throw new InvalidDataException("A completed preview requires a job with 1-4096 recorded frames.");
        }
        if (!Path.IsPathFullyQualified(job.OutputDirectory) || job.OutputDirectory.Length > 4096)
        {
            throw new InvalidDataException("The completed output requires a bounded absolute local directory.");
        }
        string directory = Path.TrimEndingDirectorySeparator(OperatingSystem.IsWindows()
            ? ViewerWindowsFiles.NormalizePath(job.OutputDirectory) : Path.GetFullPath(job.OutputDirectory));
        if (string.IsNullOrEmpty(Path.GetFileName(directory)))
        {
            throw new InvalidDataException("The completed output must be a named job directory.");
        }
        RejectReparsePoints(directory);
        using IDisposable? parent = OperatingSystem.IsWindows() ? ViewerWindowsFiles.OpenDirectory(directory) : null;
        var preview = new ViewerCompletedJobPreview();
        bool completed = false;
        try
        {
            int count = Math.Min(MaximumFrames, total);
            for (int index = 0; index < count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                int selected = count == 1 ? 0 : index * (total - 1) / (count - 1);
                RenderDiskFrameResult frame = job.Frames[selected];
                string expectedName = $"frame-{selected.ToString("D6", CultureInfo.InvariantCulture)}.png";
                if (frame.Index != selected || frame.FileName != expectedName ||
                    !double.IsFinite(frame.State.Time.TimeCode))
                {
                    throw new InvalidDataException(
                        "The completed frame identity, time or generated PNG name is invalid.");
                }
                string path = Path.Combine(directory, expectedName);
                RejectReparsePoints(path);
                using FileStream input = OperatingSystem.IsWindows()
                    ? ViewerWindowsFiles.OpenRead(path)
                    : new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024,
                        FileOptions.SequentialScan);
                PngRgba8Image image = CompletedRenderFrameReader.ReadThumbnail(
                    input, frame, ThumbnailSize, ThumbnailSize, cancellationToken);
                RejectReparsePoints(path);
                preview._frames.Add(new ViewerCompletedFramePreview(frame, image));
            }
            cancellationToken.ThrowIfCancellationRequested();
            completed = true;
            return preview;
        }
        finally
        {
            if (!completed)
            {
                preview.Dispose();
            }
        }
    }

    private static void RejectReparsePoints(string path)
    {
        for (string? current = path; current is not null; current = Path.GetDirectoryName(current))
        {
            if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
            {
                throw new NotSupportedException(
                    "Completed previews do not follow reparse points or filesystem aliases.");
            }
        }
    }

    public void Dispose()
    {
        foreach (ViewerCompletedFramePreview frame in _frames)
        {
            frame.Dispose();
        }
        _frames.Clear();
    }
}
