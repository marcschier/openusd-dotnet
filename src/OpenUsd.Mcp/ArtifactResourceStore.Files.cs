// Copyright (c) marcschier. Licensed under the MIT License.

namespace OpenUsd.Mcp;

public sealed partial class ArtifactResourceStore
{
    // Serialize file commit and cleanup so a failed import cannot delete another import's adopted content.
    private readonly SemaphoreSlim _filePublicationGate = new(1, 1);

    /// <summary>Verifies and admits every file before atomically registering the complete batch.</summary>
    public ValueTask<IReadOnlyList<ArtifactResourceDescriptor>> AddVerifiedFilesAsync(
        IReadOnlyList<ArtifactResourceFileWrite> artifacts,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(artifacts);
        cancellationToken.ThrowIfCancellationRequested();
        int count = artifacts.Count;
        if (count > _maximumResourceCount)
        {
            throw new ArtifactResourceStoreCapacityException("The file batch exceeds the resource count limit.");
        }
        if (count == 0)
        {
            return ValueTask.FromResult<IReadOnlyList<ArtifactResourceDescriptor>>([]);
        }
        var requests = new FileImportRequest[count];
        for (int index = 0; index < count; index++)
        {
            ArtifactResourceFileWrite artifact = artifacts[index];
            ArgumentNullException.ThrowIfNull(artifact);
            ValidateFileExpectation(artifact.ExpectedByteLength, artifact.ExpectedSha256);
            requests[index] = new FileImportRequest(
                artifact.Id, artifact.MediaType, artifact.SourcePath,
                artifact.ExpectedByteLength, artifact.ExpectedSha256);
        }
        return AddFilesCoreAsync(requests, cancellationToken);
    }

    private async ValueTask<IReadOnlyList<ArtifactResourceDescriptor>> AddFilesCoreAsync(
        FileImportRequest[] requests, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        cancellationToken.ThrowIfCancellationRequested();
        string storageRoot = _fileStorageRoot ??
            throw new InvalidOperationException("File-backed artifact storage is not configured.");
        var pending = new List<PendingFile>(requests.Length);
        bool registered = false;
        await _filePublicationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var uris = new HashSet<Uri>();
            long additionalBytes = 0;
            foreach (FileImportRequest request in requests)
            {
                cancellationToken.ThrowIfCancellationRequested();
                ArgumentException.ThrowIfNullOrWhiteSpace(request.MediaType);
                ArgumentException.ThrowIfNullOrWhiteSpace(request.SourcePath);
                Uri uri = ArtifactResourceUri.Create(request.Id);
                if (!uris.Add(uri))
                {
                    throw new InvalidOperationException($"A file batch contains duplicate resource URI '{uri}'.");
                }
                string sourcePath = Path.GetFullPath(request.SourcePath);
                var file = new PendingFile(request, uri, sourcePath);
                pending.Add(file);
                file.Length = file.Source.Length;
                if (request.RequiredLength is { } length && length != file.Length)
                {
                    throw new ArtifactResourceIntegrityException("The generated artifact file length has changed.");
                }
                additionalBytes = checked(additionalBytes + file.Length);
            }
            lock (_gate)
            {
                foreach (PendingFile file in pending)
                {
                    EnsureResourceDoesNotExist(file.Uri);
                }
                EnsureCapacity(pending.Count, additionalBytes);
            }

            WorkspacePathContainment.CreateDirectorySafely(storageRoot);
            WorkspacePathContainment.RejectReparsePoints(storageRoot, storageRoot);
            foreach (PendingFile file in pending)
            {
                file.TemporaryPath = Path.Combine(storageRoot, $".pending-{Guid.NewGuid():N}");
                FileContentMetadata metadata = await CopyAndHashAsync(
                    file.Source, file.SourcePath, file.TemporaryPath, file.Length, cancellationToken)
                    .ConfigureAwait(false);
                if (metadata.ByteLength != file.Length ||
                    (file.Request.RequiredSha256 is { } expected &&
                     !string.Equals(metadata.Sha256, expected, StringComparison.OrdinalIgnoreCase)))
                {
                    throw new ArtifactResourceIntegrityException("The generated artifact content has changed.");
                }
                file.Metadata = metadata;
            }

            var descriptors = new ArtifactResourceDescriptor[pending.Count];
            var additions = new Entry[pending.Count];
            for (int index = 0; index < pending.Count; index++)
            {
                PendingFile file = pending[index];
                FileContentMetadata metadata = file.Metadata!;
                string contentDirectory = Path.Combine(storageRoot, "sha256", metadata.Sha256[..2]);
                WorkspacePathContainment.CreateContainedDirectory(storageRoot, contentDirectory);
                file.ContentPath = Path.Combine(contentDirectory, metadata.Sha256);
                WorkspacePathContainment.RejectReparsePoints(storageRoot, file.ContentPath);
                if (File.Exists(file.ContentPath))
                {
                    await VerifyFileAsync(file.ContentPath, metadata, cancellationToken).ConfigureAwait(false);
                }
                else
                {
                    try
                    {
                        File.Move(file.TemporaryPath!, file.ContentPath);
                        file.CreatedContentFile = true;
                    }
                    catch (IOException) when (File.Exists(file.ContentPath))
                    {
                        await VerifyFileAsync(file.ContentPath, metadata, cancellationToken).ConfigureAwait(false);
                    }
                }
                DeleteFileIfPresent(file.TemporaryPath!);
                descriptors[index] = new ArtifactResourceDescriptor(
                    file.Request.Id, file.Uri, file.Request.MediaType, metadata.ByteLength, metadata.Sha256);
                additions[index] = new Entry(descriptors[index], null, file.ContentPath);
            }
            var result = new OwnedReadOnlyList<ArtifactResourceDescriptor>(descriptors);
            cancellationToken.ThrowIfCancellationRequested();
            lock (_gate)
            {
                foreach (PendingFile file in pending)
                {
                    EnsureResourceDoesNotExist(file.Uri);
                }
                EnsureCapacity(pending.Count, additionalBytes);
                _entries.EnsureCapacity(_entries.Count + pending.Count);
                foreach (Entry addition in additions)
                {
                    _entries.Add(addition.Descriptor.ResourceUri, addition);
                }
                _totalBytes += additionalBytes;
                registered = true;
            }
            return result;
        }
        finally
        {
            try
            {
                foreach (PendingFile file in pending)
                {
                    file.Source.Dispose();
                }
                foreach (PendingFile file in pending)
                {
                    if (file.TemporaryPath is { } temporary)
                    {
                        DeleteFileIfPresent(temporary);
                    }
                    if (!registered && file.CreatedContentFile &&
                        file.ContentPath is { } content && !IsFileReferenced(content))
                    {
                        DeleteFileIfPresent(content);
                    }
                }
            }
            finally
            {
                _filePublicationGate.Release();
            }
        }
    }

    private static void ValidateFileExpectation(long expectedByteLength, string expectedSha256)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(expectedByteLength);
        ArgumentNullException.ThrowIfNull(expectedSha256);
        if (expectedSha256.Length != 64 || expectedSha256.Any(static value => !char.IsAsciiHexDigit(value)))
        {
            throw new ArgumentException(
                "A complete SHA256 is required for verified publication.", nameof(expectedSha256));
        }
    }

    private sealed record FileImportRequest(
        string Id, string MediaType, string SourcePath, long? RequiredLength, string? RequiredSha256);

    private sealed class PendingFile(FileImportRequest request, Uri uri, string sourcePath)
    {
        internal FileImportRequest Request { get; } = request;
        internal Uri Uri { get; } = uri;
        internal string SourcePath { get; } = sourcePath;
        internal FileStream Source { get; } = new(
            sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read,
            FileBufferSize, FileOptions.Asynchronous | FileOptions.SequentialScan);
        internal long Length { get; set; }
        internal string? TemporaryPath { get; set; }
        internal string? ContentPath { get; set; }
        internal FileContentMetadata? Metadata { get; set; }
        internal bool CreatedContentFile { get; set; }
    }
}
