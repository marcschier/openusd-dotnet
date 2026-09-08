// Copyright (c) marcschier. Licensed under the MIT License.

using System.Runtime.Versioning;
using System.Security.Cryptography;
using Microsoft.Win32.SafeHandles;
using OpenUsd.Editing;

namespace OpenUsd.Viewer;

[SupportedOSPlatform("windows")]
internal sealed class ViewerReviewFileGuards : IDisposable
{
    private const int MaximumFiles = 1024;
    private const int MaximumDirectories = 4096;
    private const long MaximumSourceBytes = 16 * 1024 * 1024;
    private const long MaximumAggregateBytes = 64 * 1024 * 1024;
    private readonly Dictionary<string, SafeFileHandle> _directories = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, ViewerFileIdentity> _sourcePaths = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<FileStream> _sources = [];
    private readonly HashSet<ViewerPhysicalFileIdentity> _sourceIdentities = [];
    private readonly byte[] _buffer = new byte[64 * 1024];
    private long _sourceBytes;
    private bool _disposed;

    private ViewerReviewFileGuards(string destination)
    {
        Destination = destination;
        Name = Path.GetFileName(destination);
        if (Name.Length is <= 0 or > 255)
        {
            throw new NotSupportedException("Choose a review file name of at most 255 characters.");
        }
        string directory = Path.GetDirectoryName(destination) ??
            throw new ArgumentException("The review destination has no directory.", nameof(destination));
        try
        {
            RetainDirectories(directory);
            Parent = _directories[directory];
            ParentIdentity = ViewerWindowsFiles.GetIdentity(Parent);
            PhysicalDirectory = ViewerWindowsFiles.GetPhysicalDirectory(Parent);
            Key = new ViewerReviewPublicationKey(ParentIdentity, Name.ToUpperInvariant());
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    internal string Destination { get; }
    internal string Name { get; }
    internal SafeFileHandle Parent { get; }
    internal ViewerPhysicalFileIdentity ParentIdentity { get; }
    internal string PhysicalDirectory { get; }
    internal ViewerReviewPublicationKey Key { get; }

    internal static async Task<ViewerReviewFileGuards> AcquireAsync(
        UsdReviewDocument document, string destination, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(document);
        string path = ViewerWindowsFiles.NormalizePath(destination);
        if (!string.Equals(path, ViewerWindowsFiles.NormalizePath(document.TargetDocumentPath),
            StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("The publication path differs from the native capture request.");
        }
        if (document.Dependencies.Count > MaximumFiles)
        {
            throw new NotSupportedException("The review dependency manifest exceeds the publication guard bound.");
        }
        var guards = new ViewerReviewFileGuards(path);
        try
        {
            await guards.RetainSourceAsync(
                document.SourceRootPath, document.SourceFingerprint, null, cancellationToken)
                .ConfigureAwait(false);
            foreach (UsdReviewDependency dependency in document.Dependencies)
            {
                await guards.RetainSourceAsync(
                    dependency.Path, dependency.Sha256, dependency.ByteLength, cancellationToken)
                    .ConfigureAwait(false);
            }
            return guards;
        }
        catch
        {
            guards.Dispose();
            throw;
        }
    }

    internal async Task<ViewerFileIdentity> ObserveDestinationAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        cancellationToken.ThrowIfCancellationRequested();
        FileStream stream;
        try
        {
            stream = ViewerWindowsFiles.OpenRead(Path.Combine(PhysicalDirectory, Name), Destination);
        }
        catch (FileNotFoundException)
        {
            return new ViewerFileIdentity(Destination, false, 0, string.Empty, ParentIdentity: ParentIdentity);
        }
        await using (stream.ConfigureAwait(false))
        {
            return await ReadOpenedDestinationAsync(stream, cancellationToken).ConfigureAwait(false);
        }
    }

    internal Task<ViewerFileIdentity> ReadOpenedDestinationAsync(
        FileStream stream, CancellationToken cancellationToken)
    {
        ViewerPhysicalFileIdentity identity = ViewerWindowsFiles.GetIdentity(stream.SafeFileHandle);
        if (_sourceIdentities.Contains(identity))
        {
            throw new IOException("The review destination aliases a source or dependency file.");
        }
        return ReadIdentityAsync(stream, Destination,
            ViewerAuthoredEditController.MaximumReviewDocumentBytes, ParentIdentity, cancellationToken);
    }

    internal FileStream CreateStaging()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return ViewerWindowsFiles.CreateStaging(PhysicalDirectory, $".review-{Guid.NewGuid():N}.tmp");
    }

    private async Task RetainSourceAsync(
        string recordedPath, string expectedHash, ulong? expectedLength, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string path = ViewerWindowsFiles.NormalizePath(recordedPath);
        if (_sourcePaths.TryGetValue(path, out ViewerFileIdentity? existing))
        {
            if (!string.Equals(existing.Sha256, expectedHash, StringComparison.OrdinalIgnoreCase) ||
                (expectedLength.HasValue && expectedLength.Value != (ulong)existing.Length))
            {
                throw new InvalidDataException("The native dependency manifest contains inconsistent identities.");
            }
            return;
        }
        if (_sources.Count >= MaximumFiles || expectedLength > (ulong)MaximumSourceBytes)
        {
            throw new NotSupportedException("The source/dependency files exceed the publication guard bounds.");
        }
        string parent = Path.GetDirectoryName(path) ??
            throw new InvalidDataException("A native source path has no directory.");
        RetainDirectories(parent);
        FileStream stream = ViewerWindowsFiles.OpenRead(path);
        _sources.Add(stream);
        ViewerFileIdentity actual = await ReadIdentityAsync(
            stream, path, MaximumSourceBytes, null, cancellationToken).ConfigureAwait(false);
        if (!string.Equals(actual.Sha256, expectedHash, StringComparison.OrdinalIgnoreCase) ||
            (expectedLength.HasValue && expectedLength.Value != (ulong)actual.Length))
        {
            throw new IOException("A source or dependency changed after capture. No review file was published.");
        }
        _sourceBytes = checked(_sourceBytes + actual.Length);
        if (_sourceBytes > MaximumAggregateBytes)
        {
            throw new NotSupportedException("Source/dependency reads exceed the 64 MiB publication guard bound.");
        }
        _sourcePaths.Add(path, actual);
        _sourceIdentities.Add(actual.PhysicalIdentity ??
            throw new InvalidDataException("A source guard has no physical file identity."));
    }

    private void RetainDirectories(string directory)
    {
        var pending = new Stack<string>();
        string? current = directory;
        while (current is not null && !_directories.ContainsKey(current))
        {
            if (pending.Count + _directories.Count >= MaximumDirectories)
            {
                throw new NotSupportedException("The filesystem ancestor guard count exceeds its safety bound.");
            }
            pending.Push(current);
            current = Path.GetDirectoryName(current);
        }
        while (pending.TryPop(out string? path))
        {
            _directories.Add(path, ViewerWindowsFiles.OpenDirectory(path));
        }
    }

    private async Task<ViewerFileIdentity> ReadIdentityAsync(
        FileStream stream, string path, long maximumBytes,
        ViewerPhysicalFileIdentity? parentIdentity, CancellationToken cancellationToken)
    {
        long length = stream.Length;
        if (length > maximumBytes)
        {
            throw new InvalidDataException("A guarded file exceeds the read budget.");
        }
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        long total = 0;
        while (true)
        {
            int requested = (int)Math.Max(1, Math.Min(_buffer.Length, maximumBytes - total));
            int read = await stream.ReadAsync(_buffer.AsMemory(0, requested), cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                break;
            }
            if (read > maximumBytes - total)
            {
                throw new InvalidDataException("A guarded file grew beyond the read budget.");
            }
            hash.AppendData(_buffer, 0, read);
            total += read;
        }
        cancellationToken.ThrowIfCancellationRequested();
        if (total != length || stream.Length != length)
        {
            throw new IOException("A file changed during the guarded read.");
        }
        return new ViewerFileIdentity(path, true, total, Convert.ToHexString(hash.GetHashAndReset()),
            ViewerWindowsFiles.GetIdentity(stream.SafeFileHandle), parentIdentity);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;
        for (int index = _sources.Count - 1; index >= 0; index--)
        {
            _sources[index].Dispose();
        }
        foreach (SafeFileHandle directory in _directories.Values.Reverse())
        {
            directory.Dispose();
        }
    }
}
