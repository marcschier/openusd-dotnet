// Copyright (c) marcschier. Licensed under the MIT License.

using System.Security.Cryptography;

namespace OpenUsd.Viewer;

internal sealed class ViewerSourceLayerPublication : IAsyncDisposable
{
    private readonly string _destination;
    private readonly string _name;
    private readonly string _stagingPath;
    private readonly Microsoft.Win32.SafeHandles.SafeFileHandle _parent;
    private readonly ViewerPhysicalFileIdentity _parentIdentity;
    private readonly FileStream _source;
    private readonly ViewerFileIdentity _sourceIdentity;
    private readonly string _backupPath;
    private readonly FileStream _backup;
    private readonly ViewerFileIdentity _backupIdentity;
    private ViewerPhysicalFileIdentity _stagingPhysicalIdentity;
    private FileStream? _staging;
    private ViewerFileIdentity? _stagingIdentity;
    private bool _published;
    private bool _stagingNoLongerNamed;
    private bool _backupNoLongerNamed;
    private bool _rollbackFailed;
    private bool _disposed;

    private ViewerSourceLayerPublication(
        string destination,
        string stagingPath,
        Microsoft.Win32.SafeHandles.SafeFileHandle parent,
        ViewerPhysicalFileIdentity parentIdentity,
        FileStream source,
        ViewerFileIdentity sourceIdentity,
        string backupPath,
        FileStream backup,
        ViewerFileIdentity backupIdentity,
        ViewerPhysicalFileIdentity stagingPhysicalIdentity)
    {
        _destination = destination;
        _name = Path.GetFileName(destination);
        _stagingPath = stagingPath;
        _parent = parent;
        _parentIdentity = parentIdentity;
        _source = source;
        _sourceIdentity = sourceIdentity;
        _backupPath = backupPath;
        _backup = backup;
        _backupIdentity = backupIdentity;
        _stagingPhysicalIdentity = stagingPhysicalIdentity;
    }

    internal string StagingPath => _stagingPath;
    internal string BackupPath => _backupPath;

    internal static async Task<ViewerSourceLayerPublication> BeginAsync(
        string sourcePath,
        string expectedSourceFingerprint,
        CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("Guarded source-layer publication currently requires Windows.");
        }
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedSourceFingerprint);
        cancellationToken.ThrowIfCancellationRequested();

        string destination = ViewerWindowsFiles.NormalizePath(sourcePath);
        string directory = Path.GetDirectoryName(destination) ??
            throw new ArgumentException("The source layer has no parent directory.", nameof(sourcePath));
        string name = Path.GetFileName(destination);
        if (name.Length is <= 0 or > 255)
        {
            throw new NotSupportedException("The source layer file name exceeds the publication bound.");
        }

        Microsoft.Win32.SafeHandles.SafeFileHandle parent =
            ViewerWindowsFiles.OpenDirectoryForPublication(directory);
        FileStream? source = null;
        FileStream? backup = null;
        string? backupPath = null;
        bool backupCreated = false;
        string? stagingPath = null;
        try
        {
            ViewerPhysicalFileIdentity parentIdentity = ViewerWindowsFiles.GetIdentity(parent);
            source = ViewerWindowsFiles.OpenReadForReplacement(destination, destination);
            ViewerFileIdentity sourceIdentity = await ReadIdentityAsync(
                source, destination, parentIdentity, cancellationToken).ConfigureAwait(false);
            if (!string.Equals(sourceIdentity.Sha256, expectedSourceFingerprint, StringComparison.OrdinalIgnoreCase))
            {
                throw new IOException(
                    "The source layer changed after it was verified. Reopen the document before saving.");
            }

            string backupName = $".openusd-source-backup-{Guid.NewGuid():N}.tmp";
            backupPath = Path.Combine(directory, backupName);
            ViewerWindowsFiles.CreateHardLink(backupPath, destination);
            backupCreated = true;
            backup = ViewerWindowsFiles.OpenReadForReplacement(backupPath, backupPath);
            ViewerFileIdentity backupIdentity = await ReadIdentityAsync(
                backup, backupPath, parentIdentity, cancellationToken).ConfigureAwait(false);
            if (backupIdentity.PhysicalIdentity != sourceIdentity.PhysicalIdentity ||
                backupIdentity.Length != sourceIdentity.Length ||
                !string.Equals(backupIdentity.Sha256, sourceIdentity.Sha256, StringComparison.Ordinal))
            {
                throw new IOException("The source layer changed while its rollback link was created.");
            }

            string exportExtension = SelectExportExtension(destination, source);
            string stagingName = $".openusd-source-{Guid.NewGuid():N}{exportExtension}";
            ViewerPhysicalFileIdentity stagingPhysicalIdentity;
            using (FileStream staging = ViewerWindowsFiles.CreateStaging(directory, stagingName))
            {
                stagingPhysicalIdentity = ViewerWindowsFiles.GetIdentity(staging.SafeFileHandle);
            }
            stagingPath = Path.Combine(directory, stagingName);
            return new ViewerSourceLayerPublication(
                destination, stagingPath, parent, parentIdentity, source, sourceIdentity,
                backupPath, backup, backupIdentity, stagingPhysicalIdentity);
        }
        catch
        {
            backup?.Dispose();
            source?.Dispose();
            parent.Dispose();
            if (backupCreated && backupPath is not null && File.Exists(backupPath))
            {
                File.Delete(backupPath);
            }
            if (stagingPath is not null && File.Exists(stagingPath))
            {
                File.Delete(stagingPath);
            }
            throw;
        }
    }

    internal async Task CompleteStagingAsync(CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("Guarded source-layer publication currently requires Windows.");
        }
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_staging is not null)
        {
            throw new InvalidOperationException("The source layer was already staged.");
        }
        cancellationToken.ThrowIfCancellationRequested();
        FileStream staging = ViewerWindowsFiles.OpenReadForReplacement(_stagingPath, _stagingPath);
        try
        {
            ViewerFileIdentity identity = await ReadIdentityAsync(
                staging, _stagingPath, _parentIdentity, cancellationToken).ConfigureAwait(false);
            _stagingPhysicalIdentity = identity.PhysicalIdentity ??
                throw new InvalidDataException("The staged source layer has no physical identity.");
            if (identity.Length <= 0 ||
                identity.PhysicalIdentity == _sourceIdentity.PhysicalIdentity)
            {
                throw new InvalidDataException("The staged source layer is empty or aliases its destination.");
            }
            _staging = staging;
            _stagingIdentity = identity;
        }
        catch
        {
            staging.Dispose();
            throw;
        }
    }

    internal ViewerFileIdentity Publish()
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("Guarded source-layer publication currently requires Windows.");
        }
        ObjectDisposedException.ThrowIf(_disposed, this);
        FileStream staging = _staging ??
            throw new InvalidOperationException("The exported source layer has not been validated.");
        ViewerFileIdentity expected = _stagingIdentity ??
            throw new InvalidOperationException("The staged source layer has no observed identity.");

        ViewerFileIdentity heldSource = ReadIdentity(_source, _destination, _parentIdentity);
        ViewerFileIdentity currentSource = Observe(_destination, _parentIdentity);
        ViewerFileIdentity heldBackup = ReadIdentity(_backup, _backupPath, _parentIdentity);
        if (!_sourceIdentity.Matches(heldSource) || !_sourceIdentity.Matches(currentSource) ||
            heldBackup.PhysicalIdentity != _backupIdentity.PhysicalIdentity ||
            heldBackup.Length != _backupIdentity.Length ||
            !string.Equals(heldBackup.Sha256, _backupIdentity.Sha256, StringComparison.Ordinal))
        {
            throw new IOException("The source layer changed before publication. No source bytes were replaced.");
        }
        ViewerWindowsFiles.RenameReplacingOpenTarget(
            _stagingPath, _stagingPath, _stagingPhysicalIdentity, _parent, _name);
        _published = true;
        _stagingNoLongerNamed = true;
        _staging.Dispose();
        _staging = null;
        return expected with { FullPath = _destination, ParentIdentity = _parentIdentity };
    }

    internal void Rollback()
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("Guarded source-layer publication currently requires Windows.");
        }
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!_published)
        {
            return;
        }
        try
        {
            ViewerWindowsFiles.RenameReplacingOpenTarget(
                _backupPath, _backupPath, _backupIdentity.PhysicalIdentity ??
                    throw new InvalidDataException("The rollback link has no physical identity."),
                _parent, _name);
            _backupNoLongerNamed = true;
            _published = false;
        }
        catch
        {
            _rollbackFailed = true;
            throw;
        }
    }

    public ValueTask DisposeAsync()
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("Guarded source-layer publication currently requires Windows.");
        }
        if (_disposed)
        {
            return ValueTask.CompletedTask;
        }
        _disposed = true;
        try
        {
            if (!_stagingNoLongerNamed)
            {
                ViewerWindowsFiles.DeleteOwnedPublicationFile(
                    _stagingPath, _stagingPath, _stagingPhysicalIdentity);
            }
            if (!_backupNoLongerNamed && !_rollbackFailed)
            {
                ViewerWindowsFiles.DeleteOwnedPublicationFile(
                    _backupPath, _backupPath, _backupIdentity.PhysicalIdentity ??
                        throw new InvalidDataException("The rollback link has no physical identity."));
            }
        }
        finally
        {
            _staging?.Dispose();
            _backup.Dispose();
            _source.Dispose();
            _parent.Dispose();
        }
        return ValueTask.CompletedTask;
    }

    private static async Task<ViewerFileIdentity> ReadIdentityAsync(
        FileStream stream,
        string path,
        ViewerPhysicalFileIdentity parentIdentity,
        CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("Guarded source-layer publication currently requires Windows.");
        }
        stream.Position = 0;
        long length = stream.Length;
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        byte[] buffer = new byte[64 * 1024];
        long total = 0;
        while (true)
        {
            int read = await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                break;
            }
            total = checked(total + read);
            hash.AppendData(buffer, 0, read);
        }
        cancellationToken.ThrowIfCancellationRequested();
        if (total != length || stream.Length != length)
        {
            throw new IOException("A source layer changed while its file identity was captured.");
        }
        return new ViewerFileIdentity(
            path, true, total, Convert.ToHexString(hash.GetHashAndReset()),
            ViewerWindowsFiles.GetIdentity(stream.SafeFileHandle), parentIdentity);
    }

    private static ViewerFileIdentity ReadIdentity(
        FileStream stream, string path, ViewerPhysicalFileIdentity parentIdentity)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("Guarded source-layer publication currently requires Windows.");
        }
        stream.Position = 0;
        long length = stream.Length;
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        byte[] buffer = new byte[64 * 1024];
        long total = 0;
        while (true)
        {
            int read = stream.Read(buffer, 0, buffer.Length);
            if (read == 0)
            {
                break;
            }
            total = checked(total + read);
            hash.AppendData(buffer, 0, read);
        }
        if (total != length || stream.Length != length)
        {
            throw new IOException("A source layer changed while its file identity was captured.");
        }
        return new ViewerFileIdentity(
            path, true, total, Convert.ToHexString(hash.GetHashAndReset()),
            ViewerWindowsFiles.GetIdentity(stream.SafeFileHandle), parentIdentity);
    }

    private static ViewerFileIdentity Observe(string path, ViewerPhysicalFileIdentity parentIdentity)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("Guarded source-layer publication currently requires Windows.");
        }
        using FileStream stream = ViewerWindowsFiles.OpenReadForReplacement(path, path);
        return ReadIdentity(stream, path, parentIdentity);
    }

    private static string SelectExportExtension(string sourcePath, FileStream source)
    {
        string extension = Path.GetExtension(sourcePath);
        if (extension.Equals(".usda", StringComparison.OrdinalIgnoreCase))
        {
            return ".usda";
        }
        if (extension.Equals(".usdc", StringComparison.OrdinalIgnoreCase))
        {
            return ".usdc";
        }
        if (!extension.Equals(".usd", StringComparison.OrdinalIgnoreCase))
        {
            throw new NotSupportedException("Source publication supports .usd, .usda, and .usdc layers only.");
        }

        source.Position = 0;
        Span<byte> header = stackalloc byte[8];
        int read = source.Read(header);
        source.Position = 0;
        if (read >= 5 && header[..5].SequenceEqual("#usda"u8))
        {
            return ".usda";
        }
        if (read == 8 && header.SequenceEqual("PXR-USDC"u8))
        {
            return ".usdc";
        }
        throw new NotSupportedException(
            "The .usd source format could not be identified without changing its encoding.");
    }

}
