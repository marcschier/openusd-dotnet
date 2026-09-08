// Copyright (c) marcschier. Licensed under the MIT License.

namespace OpenUsd.Viewer;

internal static class ViewerSampleScene
{
    internal static async Task<string> PrepareAsync(string settingsRoot, CancellationToken cancellationToken)
    {
        using Stream resource = typeof(ViewerSampleScene).Assembly.GetManifestResourceStream(
            "OpenUsd.Viewer.Assets.ReviewScene.usda") ??
            throw new InvalidDataException("The bundled review sample is missing from the Viewer package.");
        if (resource.Length > 16 * 1024)
        {
            throw new InvalidDataException("The bundled review sample exceeds its 16 KiB resource budget.");
        }
        string directory = Path.Combine(settingsRoot, "samples");
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "review-scene-v1.usda");
        string temporary = Path.Combine(directory, $"review-scene-{Guid.NewGuid():N}.tmp");
        try
        {
            await using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write,
                FileShare.None, 4096, FileOptions.Asynchronous))
            {
                await resource.CopyToAsync(stream, cancellationToken).ConfigureAwait(false);
            }
            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporary, path, overwrite: true);
            return path;
        }
        finally
        {
            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }
        }
    }
}
