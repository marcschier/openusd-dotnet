// Copyright (c) marcschier. Licensed under the MIT License.

using System.Text.Json;

namespace OpenUsd.Viewer;

internal sealed class ViewerRecentRenderManifestStore : IDisposable
{
    internal const int MaximumEntries = 8;
    internal const int MaximumFileBytes = 256 * 1024;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly StringComparer _pathComparer =
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
    private bool _disposed;

    internal ViewerRecentRenderManifestStore(string rootPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootPath);
        RootPath = Path.GetFullPath(rootPath);
        StorePath = Path.Combine(RootPath, "recent-render-manifests.json");
    }

    internal string RootPath { get; }
    internal string StorePath { get; }

    internal async Task<IReadOnlyList<string>> LoadAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await ReadAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    internal async Task<IReadOnlyList<string>> AddAsync(
        string manifestPath, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        string canonical = ViewerImportedRenderJob.NormalizeManifestPath(manifestPath);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            Directory.CreateDirectory(RootPath);
            ViewerCompletedJobPreview.RejectReparsePoints(RootPath);
            // The sidecar stays in place; ownership is the OS lock, released even if its process exits.
            using var writeLock = new FileStream(
                StorePath + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None,
                bufferSize: 1, FileOptions.None);
            IReadOnlyList<string> current = await ReadAsync(cancellationToken).ConfigureAwait(false);
            string[] revised = [canonical, .. current.Where(path => !_pathComparer.Equals(path, canonical))
                .Take(MaximumEntries - 1)];
            await PersistAsync(revised, cancellationToken).ConfigureAwait(false);
            return Array.AsReadOnly(revised);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<IReadOnlyList<string>> ReadAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        FileStream input;
        try
        {
            ViewerCompletedJobPreview.RejectReparsePoints(StorePath);
            input = new FileStream(StorePath, FileMode.Open, FileAccess.Read, FileShare.Read,
                bufferSize: 4096, FileOptions.Asynchronous | FileOptions.SequentialScan);
        }
        catch (FileNotFoundException)
        {
            return [];
        }
        catch (DirectoryNotFoundException)
        {
            return [];
        }
        await using (input.ConfigureAwait(false))
        {
            if (input.Length is < 1 or > MaximumFileBytes)
            {
                throw new InvalidDataException(
                    "The remembered render manifest index must contain 1 byte through 256 KiB.");
            }
            byte[] bytes = new byte[checked((int)input.Length)];
            await input.ReadExactlyAsync(bytes, cancellationToken).ConfigureAwait(false);
            if (input.Length != bytes.Length || input.ReadByte() != -1)
            {
                throw new InvalidDataException("The remembered render manifest index changed while being read.");
            }
            try
            {
                using JsonDocument document = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 4 });
                JsonElement root = document.RootElement;
                string[] properties = root.EnumerateObject().Select(static item => item.Name).ToArray();
                if (!properties.Order(StringComparer.Ordinal).SequenceEqual(["paths", "schemaVersion"]) ||
                    root.GetProperty("schemaVersion").GetInt32() != 1)
                {
                    throw new InvalidDataException("The remembered render manifest index has an unsupported schema.");
                }
                JsonElement paths = root.GetProperty("paths");
                if (paths.GetArrayLength() > MaximumEntries)
                {
                    throw new InvalidDataException("The remembered render manifest index exceeds eight paths.");
                }
                var result = new List<string>(MaximumEntries);
                var seen = new HashSet<string>(_pathComparer);
                foreach (JsonElement path in paths.EnumerateArray())
                {
                    string canonical = ViewerImportedRenderJob.NormalizeManifestPath(
                        path.GetString() ?? string.Empty);
                    if (!seen.Add(canonical))
                    {
                        throw new InvalidDataException(
                            "The remembered render manifest index contains duplicate paths.");
                    }
                    result.Add(canonical);
                }
                return Array.AsReadOnly(result.ToArray());
            }
            catch (Exception exception) when (exception is JsonException or InvalidOperationException or
                KeyNotFoundException or FormatException or OverflowException)
            {
                throw new InvalidDataException("The remembered render manifest index is malformed.", exception);
            }
        }
    }

    private async Task PersistAsync(IReadOnlyList<string> paths, CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream();
        using (var json = new Utf8JsonWriter(buffer))
        {
            json.WriteStartObject();
            json.WriteNumber("schemaVersion", 1);
            json.WriteStartArray("paths");
            foreach (string path in paths)
            {
                json.WriteStringValue(path);
            }
            json.WriteEndArray();
            json.WriteEndObject();
        }
        if (buffer.Length > MaximumFileBytes)
        {
            throw new InvalidDataException("The remembered render manifest index exceeds 256 KiB.");
        }
        string temporary = StorePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        bool created = false;
        try
        {
            await using (var output = new FileStream(
                temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096,
                FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                created = true;
                await output.WriteAsync(buffer.GetBuffer().AsMemory(0, checked((int)buffer.Length)),
                    cancellationToken).ConfigureAwait(false);
                await output.FlushAsync(cancellationToken).ConfigureAwait(false);
            }
            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporary, StorePath, overwrite: true);
        }
        finally
        {
            if (created)
            {
                File.Delete(temporary);
            }
        }
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _disposed = true;
            _gate.Dispose();
        }
    }
}
