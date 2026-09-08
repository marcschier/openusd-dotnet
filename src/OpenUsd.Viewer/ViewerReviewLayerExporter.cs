// Copyright (c) marcschier. Licensed under the MIT License.

using OpenUsd.Editing;

namespace OpenUsd.Viewer;

internal static class ViewerReviewLayerExporter
{
    internal static async Task ExportNewAsync(
        UsdLayerCheckpoint checkpoint, string sourcePath, string destinationPath, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(checkpoint);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);
        if (!Path.IsPathFullyQualified(sourcePath) || !Path.IsPathFullyQualified(destinationPath))
        {
            throw new ArgumentException("Source and export destinations must be absolute local paths.");
        }
        string destination = Path.GetFullPath(destinationPath);
        StringComparison comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (string.Equals(Path.GetFullPath(sourcePath), destination, comparison))
        {
            throw new ArgumentException("A review delta cannot overwrite its source file.", nameof(destinationPath));
        }
        cancellationToken.ThrowIfCancellationRequested();
        byte[] bytes = await Task.Run(() => checkpoint.ExportBytes(destination), cancellationToken)
            .ConfigureAwait(false);
        if (bytes.Length is <= 0 or > 4 * 1024 * 1024)
        {
            throw new InvalidDataException("The native review export exceeds its 4 MiB publication bound.");
        }
        cancellationToken.ThrowIfCancellationRequested();
        string directory = Path.GetDirectoryName(destination) ??
            throw new ArgumentException("The export destination has no directory.", nameof(destinationPath));
        string temporary = Path.Combine(directory, $".review-{Guid.NewGuid():N}.tmp");
        bool created = false;
        try
        {
            await using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write,
                FileShare.None, 4096, FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                created = true;
                await stream.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            }
            cancellationToken.ThrowIfCancellationRequested();
            // Create-only publication also refuses existing hard-link/symlink aliases and foreign edits.
            File.Move(temporary, destination, overwrite: false);
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
