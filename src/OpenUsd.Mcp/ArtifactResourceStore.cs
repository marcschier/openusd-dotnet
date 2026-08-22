// Copyright (c) marcschier. Licensed under the MIT License.

using System.Security.Cryptography;

namespace OpenUsd.Mcp;

public sealed record ArtifactResourceDescriptor(
    string Id,
    Uri ResourceUri,
    string MediaType,
    long ByteLength,
    string Sha256,
    string? InlineBase64 = null)
{
    public bool IsInline => InlineBase64 is not null;
}

public sealed record ArtifactResourceStoreOptions(
    int MaximumResourceCount = 128,
    long MaximumTotalBytes = 64 * 1024 * 1024,
    int InlineThresholdBytes = 32 * 1024,
    long MaximumReadResponseBytes = 64 * 1024 * 1024,
    string? FileStorageRoot = null);

public sealed record ArtifactResourceWrite(
    string Id,
    string MediaType,
    ReadOnlyMemory<byte> Content);

public sealed record ArtifactResourceContent(
    ArtifactResourceDescriptor Descriptor,
    ReadOnlyMemory<byte> Content);

public sealed class ArtifactResourceStoreCapacityException(string message)
    : InvalidOperationException(message);

public sealed class ArtifactResourceReadLimitException(string message)
    : InvalidOperationException(message);

public sealed class ArtifactResourceIntegrityException(string message)
    : IOException(message);

public static class ArtifactResourceUri
{
    public static Uri Create(string artifactId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(artifactId);
        return new Uri(
            $"openusd://artifact/{Uri.EscapeDataString(artifactId)}",
            UriKind.Absolute);
    }
}

public interface IArtifactResourceStore
{
    ArtifactResourceDescriptor Add(
        string artifactId,
        string mediaType,
        ReadOnlyMemory<byte> content);

    IReadOnlyList<ArtifactResourceDescriptor> AddRange(
        IReadOnlyList<ArtifactResourceWrite> artifacts);

    ValueTask<ArtifactResourceDescriptor> AddFileAsync(
        string artifactId,
        string mediaType,
        string sourcePath,
        CancellationToken cancellationToken = default);

    bool TryGetDescriptor(
        Uri resourceUri,
        out ArtifactResourceDescriptor? descriptor);

    ValueTask<ArtifactResourceContent?> ReadAsync(
        Uri resourceUri,
        CancellationToken cancellationToken = default);
}

public sealed class ArtifactResourceStore : IArtifactResourceStore
{
    private const int FileBufferSize = 64 * 1024;
    private readonly Dictionary<Uri, Entry> _entries = [];
    private readonly string? _fileStorageRoot;
    private readonly object _gate = new();
    private readonly int _inlineThresholdBytes;
    private readonly long _maximumReadResponseBytes;
    private readonly int _maximumResourceCount;
    private readonly long _maximumTotalBytes;
    private long _totalBytes;

    public ArtifactResourceStore()
        : this(new ArtifactResourceStoreOptions())
    {
    }

    public ArtifactResourceStore(ArtifactResourceStoreOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(options.MaximumResourceCount);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(options.MaximumTotalBytes);
        ArgumentOutOfRangeException.ThrowIfNegative(options.InlineThresholdBytes);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(
            options.MaximumReadResponseBytes);
        _maximumResourceCount = options.MaximumResourceCount;
        _maximumTotalBytes = options.MaximumTotalBytes;
        _inlineThresholdBytes = options.InlineThresholdBytes;
        _maximumReadResponseBytes = options.MaximumReadResponseBytes;
        _fileStorageRoot = options.FileStorageRoot is null
            ? null
            : Path.TrimEndingDirectorySeparator(
                Path.GetFullPath(options.FileStorageRoot));
    }

    public int Count
    {
        get
        {
            lock (_gate)
            {
                return _entries.Count;
            }
        }
    }

    public long TotalBytes
    {
        get
        {
            lock (_gate)
            {
                return _totalBytes;
            }
        }
    }

    public ArtifactResourceDescriptor Add(
        string artifactId,
        string mediaType,
        ReadOnlyMemory<byte> content) =>
        AddRange([new ArtifactResourceWrite(artifactId, mediaType, content)])[0];

    public IReadOnlyList<ArtifactResourceDescriptor> AddRange(
        IReadOnlyList<ArtifactResourceWrite> artifacts)
    {
        ArgumentNullException.ThrowIfNull(artifacts);
        if (artifacts.Count == 0)
        {
            return [];
        }

        var additions = new List<Entry>(artifacts.Count);
        var resourceUris = new HashSet<Uri>();
        long additionalBytes = 0;
        foreach (ArtifactResourceWrite artifact in artifacts)
        {
            ArgumentNullException.ThrowIfNull(artifact);
            ArgumentException.ThrowIfNullOrWhiteSpace(artifact.MediaType);
            Uri resourceUri = ArtifactResourceUri.Create(artifact.Id);
            if (!resourceUris.Add(resourceUri))
            {
                throw new InvalidOperationException(
                    $"An artifact batch contains duplicate resource URI '{resourceUri}'.");
            }

            byte[] ownedContent = artifact.Content.ToArray();
            additionalBytes = checked(additionalBytes + ownedContent.LongLength);
            var descriptor = new ArtifactResourceDescriptor(
                artifact.Id,
                resourceUri,
                artifact.MediaType,
                ownedContent.LongLength,
                Convert.ToHexString(SHA256.HashData(ownedContent)).ToLowerInvariant(),
                _inlineThresholdBytes > 0 &&
                ownedContent.Length <= _inlineThresholdBytes
                    ? Convert.ToBase64String(ownedContent)
                    : null);
            additions.Add(new Entry(descriptor, ownedContent, null));
        }

        lock (_gate)
        {
            Entry? duplicate = additions.FirstOrDefault(
                addition => _entries.ContainsKey(addition.Descriptor.ResourceUri));
            if (duplicate is not null)
            {
                throw new InvalidOperationException(
                    $"An artifact already exists at '{duplicate.Descriptor.ResourceUri}'.");
            }

            EnsureCapacity(additions.Count, additionalBytes);
            foreach (Entry addition in additions)
            {
                _entries.Add(addition.Descriptor.ResourceUri, addition);
            }

            _totalBytes += additionalBytes;
        }

        return Array.AsReadOnly(
            additions.Select(static addition => addition.Descriptor).ToArray());
    }

    public async ValueTask<ArtifactResourceDescriptor> AddFileAsync(
        string artifactId,
        string mediaType,
        string sourcePath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(mediaType);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        cancellationToken.ThrowIfCancellationRequested();
        string storageRoot = _fileStorageRoot ??
            throw new InvalidOperationException(
                "File-backed artifact storage is not configured.");
        Uri resourceUri = ArtifactResourceUri.Create(artifactId);
        string canonicalSourcePath = Path.GetFullPath(sourcePath);
        var sourceInfo = new FileInfo(canonicalSourcePath);
        if (!sourceInfo.Exists)
        {
            throw new FileNotFoundException(
                "The artifact source file does not exist.",
                canonicalSourcePath);
        }

        long expectedLength = sourceInfo.Length;
        lock (_gate)
        {
            EnsureResourceDoesNotExist(resourceUri);
            EnsureCapacity(1, expectedLength);
        }

        WorkspacePathContainment.CreateDirectorySafely(storageRoot);
        WorkspacePathContainment.RejectReparsePoints(storageRoot, storageRoot);
        string temporaryPath = Path.Combine(
            storageRoot,
            string.Concat(".pending-", Guid.NewGuid().ToString("N")));
        string? contentPath = null;
        bool createdContentFile = false;
        bool registered = false;
        try
        {
            FileContentMetadata metadata = await CopyAndHashAsync(
                    canonicalSourcePath,
                    temporaryPath,
                    cancellationToken)
                .ConfigureAwait(false);
            if (metadata.ByteLength != expectedLength)
            {
                throw new ArtifactResourceIntegrityException(
                    $"Artifact source '{canonicalSourcePath}' changed while it was copied.");
            }

            string contentDirectory = Path.Combine(
                storageRoot,
                "sha256",
                metadata.Sha256[..2]);
            WorkspacePathContainment.CreateContainedDirectory(
                storageRoot,
                contentDirectory);
            contentPath = Path.Combine(contentDirectory, metadata.Sha256);
            WorkspacePathContainment.RejectReparsePoints(storageRoot, contentPath);
            if (File.Exists(contentPath))
            {
                await VerifyFileAsync(contentPath, metadata, cancellationToken)
                    .ConfigureAwait(false);
            }
            else
            {
                try
                {
                    File.Move(temporaryPath, contentPath);
                    createdContentFile = true;
                }
                catch (IOException) when (File.Exists(contentPath))
                {
                    await VerifyFileAsync(contentPath, metadata, cancellationToken)
                        .ConfigureAwait(false);
                }
            }

            var descriptor = new ArtifactResourceDescriptor(
                artifactId,
                resourceUri,
                mediaType,
                metadata.ByteLength,
                metadata.Sha256);
            cancellationToken.ThrowIfCancellationRequested();
            lock (_gate)
            {
                EnsureResourceDoesNotExist(resourceUri);
                EnsureCapacity(1, metadata.ByteLength);
                _entries.Add(
                    descriptor.ResourceUri,
                    new Entry(descriptor, null, contentPath));
                _totalBytes += metadata.ByteLength;
                registered = true;
            }

            return descriptor;
        }
        finally
        {
            DeleteFileIfPresent(temporaryPath);
            if (createdContentFile &&
                !registered &&
                contentPath is not null &&
                !IsFileReferenced(contentPath))
            {
                DeleteFileIfPresent(contentPath);
            }
        }
    }

    public bool TryGetDescriptor(
        Uri resourceUri,
        out ArtifactResourceDescriptor? descriptor)
    {
        ArgumentNullException.ThrowIfNull(resourceUri);
        lock (_gate)
        {
            if (_entries.TryGetValue(resourceUri, out Entry? entry))
            {
                descriptor = entry.Descriptor;
                return true;
            }

            descriptor = null;
            return false;
        }
    }

    public async ValueTask<ArtifactResourceContent?> ReadAsync(
        Uri resourceUri,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(resourceUri);
        cancellationToken.ThrowIfCancellationRequested();
        Entry? entry;
        lock (_gate)
        {
            _entries.TryGetValue(resourceUri, out entry);
        }

        if (entry is null)
        {
            return null;
        }

        EnsureReadable(entry.Descriptor);
        if (entry.MemoryContent is not null)
        {
            return new ArtifactResourceContent(
                entry.Descriptor,
                entry.MemoryContent.ToArray());
        }

        byte[] content;
        try
        {
            content = await ReadAndVerifyFileAsync(
                    entry.FilePath!,
                    entry.Descriptor,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception exception) when (
            exception is FileNotFoundException or DirectoryNotFoundException)
        {
            throw new ArtifactResourceIntegrityException(
                $"Artifact resource '{entry.Descriptor.ResourceUri}' is no longer available.");
        }

        return new ArtifactResourceContent(entry.Descriptor, content);
    }

    private static async ValueTask<FileContentMetadata> CopyAndHashAsync(
        string sourcePath,
        string destinationPath,
        CancellationToken cancellationToken)
    {
        await using var source = new FileStream(
            sourcePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            FileBufferSize,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        long expectedLength = source.Length;
        try
        {
            await using var destination = new FileStream(
                destinationPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                FileBufferSize,
                FileOptions.Asynchronous |
                FileOptions.SequentialScan |
                FileOptions.WriteThrough);
            using SHA256 hashAlgorithm = SHA256.Create();
            using (var hashingStream = new CryptoStream(
                       destination,
                       hashAlgorithm,
                       CryptoStreamMode.Write,
                       leaveOpen: true))
            {
                await source.CopyToAsync(
                        hashingStream,
                        FileBufferSize,
                        cancellationToken)
                    .ConfigureAwait(false);
                hashingStream.FlushFinalBlock();
            }

            await destination.FlushAsync(cancellationToken).ConfigureAwait(false);
            destination.Flush(flushToDisk: true);
            if (source.Position != expectedLength ||
                source.Length != expectedLength ||
                destination.Length != expectedLength)
            {
                throw new ArtifactResourceIntegrityException(
                    $"Artifact source '{sourcePath}' changed while it was copied.");
            }

            return new FileContentMetadata(
                expectedLength,
                Convert.ToHexString(hashAlgorithm.Hash!).ToLowerInvariant());
        }
        catch
        {
            DeleteFileIfPresent(destinationPath);
            throw;
        }
    }

    private static async ValueTask<FileContentMetadata> ReadFileMetadataAsync(
        string path,
        CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            FileBufferSize,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        long expectedLength = stream.Length;
        byte[] hash = await SHA256.HashDataAsync(stream, cancellationToken)
            .ConfigureAwait(false);
        if (stream.Position != expectedLength || stream.Length != expectedLength)
        {
            throw new ArtifactResourceIntegrityException(
                $"Artifact file '{path}' changed while it was hashed.");
        }

        return new FileContentMetadata(
            expectedLength,
            Convert.ToHexString(hash).ToLowerInvariant());
    }

    private static async ValueTask VerifyFileAsync(
        string path,
        FileContentMetadata expected,
        CancellationToken cancellationToken)
    {
        FileContentMetadata actual = await ReadFileMetadataAsync(path, cancellationToken)
            .ConfigureAwait(false);
        if (actual != expected)
        {
            throw new ArtifactResourceIntegrityException(
                $"Artifact cache file '{path}' failed integrity validation.");
        }
    }

    private static async ValueTask<byte[]> ReadAndVerifyFileAsync(
        string path,
        ArtifactResourceDescriptor descriptor,
        CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            FileBufferSize,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        if (stream.Length != descriptor.ByteLength)
        {
            throw new ArtifactResourceIntegrityException(
                $"Artifact resource '{descriptor.ResourceUri}' changed after publication.");
        }

        int contentLength = checked((int)descriptor.ByteLength);
        byte[] content = GC.AllocateUninitializedArray<byte>(contentLength);
        using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        int offset = 0;
        while (offset < content.Length)
        {
            int read = await stream.ReadAsync(
                    content.AsMemory(offset),
                    cancellationToken)
                .ConfigureAwait(false);
            if (read == 0)
            {
                throw new ArtifactResourceIntegrityException(
                    $"Artifact resource '{descriptor.ResourceUri}' was truncated while reading.");
            }

            hash.AppendData(content.AsSpan(offset, read));
            offset += read;
        }

        if (stream.Position != descriptor.ByteLength ||
            stream.Length != descriptor.ByteLength)
        {
            throw new ArtifactResourceIntegrityException(
                $"Artifact resource '{descriptor.ResourceUri}' changed while reading.");
        }

        string actualHash = Convert.ToHexString(hash.GetHashAndReset())
            .ToLowerInvariant();
        if (!string.Equals(
                actualHash,
                descriptor.Sha256,
                StringComparison.Ordinal))
        {
            throw new ArtifactResourceIntegrityException(
                $"Artifact resource '{descriptor.ResourceUri}' failed integrity validation.");
        }

        return content;
    }

    private void EnsureCapacity(int additionalCount, long additionalBytes)
    {
        if (additionalCount > _maximumResourceCount - _entries.Count)
        {
            throw new ArtifactResourceStoreCapacityException(
                $"The artifact store is full (maximum {_maximumResourceCount} resources).");
        }

        if (additionalBytes > _maximumTotalBytes - _totalBytes)
        {
            throw new ArtifactResourceStoreCapacityException(
                $"The artifact store would exceed its {_maximumTotalBytes}-byte limit.");
        }
    }

    private void EnsureReadable(ArtifactResourceDescriptor descriptor)
    {
        if (descriptor.ByteLength > _maximumReadResponseBytes ||
            descriptor.ByteLength > int.MaxValue)
        {
            throw new ArtifactResourceReadLimitException(
                $"Artifact resource '{descriptor.ResourceUri}' is {descriptor.ByteLength} bytes; " +
                $"the per-read limit is {_maximumReadResponseBytes} bytes.");
        }
    }

    private void EnsureResourceDoesNotExist(Uri resourceUri)
    {
        if (_entries.ContainsKey(resourceUri))
        {
            throw new InvalidOperationException(
                $"An artifact already exists at '{resourceUri}'.");
        }
    }

    private bool IsFileReferenced(string path)
    {
        lock (_gate)
        {
            return _entries.Values.Any(
                entry => string.Equals(
                    entry.FilePath,
                    path,
                    OperatingSystem.IsWindows()
                        ? StringComparison.OrdinalIgnoreCase
                        : StringComparison.Ordinal));
        }
    }

    private static void DeleteFileIfPresent(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    private sealed record Entry(
        ArtifactResourceDescriptor Descriptor,
        byte[]? MemoryContent,
        string? FilePath);

    private sealed record FileContentMetadata(long ByteLength, string Sha256);
}
