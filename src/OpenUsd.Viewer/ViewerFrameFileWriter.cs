// Copyright (c) marcschier. Licensed under the MIT License.

using OpenUsd.Rendering;

namespace OpenUsd.Viewer;

internal static class ViewerFrameFileWriter
{
    internal static Task WriteAsync(
        string path, ViewerFrameCaptureResult frame, CancellationToken cancellationToken,
        long maximumBytes = ViewerCaptureComparison.MaximumFileBytes)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(frame);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumBytes);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(maximumBytes, ViewerCaptureComparison.MaximumFileBytes);
        if (!Path.IsPathFullyQualified(path))
        {
            throw new ArgumentException("A capture destination must be an absolute local path.", nameof(path));
        }
        if (frame.Width > ViewerCaptureComparison.MaximumDimension ||
            frame.Height > ViewerCaptureComparison.MaximumDimension)
        {
            throw new InvalidDataException("Capture export is limited to 8192 pixels per side.");
        }
        string destination = Path.GetFullPath(path);
        string extension = Path.GetExtension(destination);
        bool png = extension.Equals(".png", StringComparison.OrdinalIgnoreCase);
        if (!png && !extension.Equals(".bmp", StringComparison.OrdinalIgnoreCase))
        {
            throw new NotSupportedException("Choose a .png or .bmp capture destination.");
        }
        return Task.Run(() => Write(destination, frame, png, maximumBytes, cancellationToken), cancellationToken);
    }

    private static void Write(
        string destination, ViewerFrameCaptureResult frame, bool png, long maximumBytes,
        CancellationToken cancellationToken)
    {
        string directory = Path.GetDirectoryName(destination)!;
        string temporary = Path.Combine(directory, $".openusd-frame-{Guid.NewGuid():N}.tmp");
        bool created = false;
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                created = true;
                if (png)
                {
                    Rgba8RowOrder order = frame.RowOrder == ViewerFrameRowOrder.BottomUp
                        ? Rgba8RowOrder.BottomUp : Rgba8RowOrder.TopDown;
                    _ = PngRgba8Writer.Write(stream, frame.Width, frame.Height, frame.Rgba.Span,
                        order, maximumBytes, cancellationToken);
                }
                else
                {
                    ViewerFrameBitmapWriter.WriteBmp(stream, frame.Width, frame.Height,
                        frame.Rgba.Span, frame.RowOrder, maximumBytes, cancellationToken);
                }
                stream.Flush(flushToDisk: true);
            }
            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporary, destination, overwrite: true);
        }
        finally
        {
            if (created)
            {
                File.Delete(temporary);
            }
        }
    }
}
