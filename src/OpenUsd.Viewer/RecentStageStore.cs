// Copyright (c) marcschier. Licensed under the MIT License.

using System.Text;

namespace OpenUsd.Viewer;

internal sealed class RecentStageStore
{
    internal const int Capacity = 10;
    private const string FileName = "recent-stages.txt";
    private readonly StringComparer _pathComparer =
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    internal RecentStageStore(string? rootPath = null)
    {
        RootPath = rootPath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "OpenUsd",
            "Viewer");
        StorePath = Path.Combine(RootPath, FileName);
    }

    internal string RootPath { get; }

    internal string StorePath { get; }

    internal async Task<IReadOnlyList<string>> LoadAsync(
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(StorePath))
        {
            return [];
        }

        string[] lines = await File.ReadAllLinesAsync(StorePath, cancellationToken)
            .ConfigureAwait(false);
        IReadOnlyList<string> cleaned = Normalize(lines, onlyExisting: true);
        if (!lines.SequenceEqual(cleaned, _pathComparer))
        {
            await PersistAsync(cleaned, cancellationToken).ConfigureAwait(false);
        }
        return cleaned;
    }

    internal async Task<IReadOnlyList<string>> AddAsync(
        string stagePath,
        CancellationToken cancellationToken = default)
    {
        string normalized = NormalizePath(stagePath);
        IReadOnlyList<string> current = await LoadAsync(cancellationToken).ConfigureAwait(false);
        IReadOnlyList<string> revised = Normalize(
            new[] { normalized }.Concat(current),
            onlyExisting: false);
        await PersistAsync(revised, cancellationToken).ConfigureAwait(false);
        return revised;
    }

    internal IReadOnlyList<string> Normalize(
        IEnumerable<string> paths,
        bool onlyExisting)
    {
        ArgumentNullException.ThrowIfNull(paths);
        var result = new List<string>(Capacity);
        var seen = new HashSet<string>(_pathComparer);
        foreach (string path in paths)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                continue;
            }

            string normalized;
            try
            {
                normalized = NormalizePath(path);
            }
            catch (ArgumentException)
            {
                continue;
            }
            catch (NotSupportedException)
            {
                continue;
            }
            catch (PathTooLongException)
            {
                continue;
            }

            if ((onlyExisting && !File.Exists(normalized)) || !seen.Add(normalized))
            {
                continue;
            }
            result.Add(normalized);
            if (result.Count == Capacity)
            {
                break;
            }
        }
        return result;
    }

    internal async Task PersistAsync(
        IReadOnlyList<string> paths,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(paths);
        Directory.CreateDirectory(RootPath);
        string temporaryPath = Path.Combine(
            RootPath,
            string.Concat(FileName, ".", Guid.NewGuid().ToString("N"), ".tmp"));
        try
        {
            await File.WriteAllLinesAsync(
                temporaryPath,
                paths,
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
                cancellationToken).ConfigureAwait(false);
            File.Move(temporaryPath, StorePath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private static string NormalizePath(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return Path.GetFullPath(path.Trim());
    }
}
