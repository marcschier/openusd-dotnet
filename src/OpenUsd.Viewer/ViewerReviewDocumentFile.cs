// Copyright (c) marcschier. Licensed under the MIT License.

using System.Security.Cryptography;
using Microsoft.Win32.SafeHandles;
using OpenUsd.Editing;

namespace OpenUsd.Viewer;

internal sealed class ViewerReviewDocumentFile : IAsyncDisposable
{
    private readonly FileStream _stream;
    private readonly SafeFileHandle _parent;
    private readonly byte[] _bytes;

    private ViewerReviewDocumentFile(
        FileStream stream, SafeFileHandle parent, byte[] bytes,
        ViewerFileIdentity identity, UsdReviewDocumentInfo info)
    {
        _stream = stream;
        _parent = parent;
        _bytes = bytes;
        Identity = identity;
        Info = info;
    }

    internal ViewerFileIdentity Identity { get; }
    internal UsdReviewDocumentInfo Info { get; }

    internal static async Task<ViewerReviewDocumentFile> InspectAsync(
        string path, CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("Portable review opening currently requires Windows.");
        }
        string fullPath = ViewerWindowsFiles.NormalizePath(path);
        string parentPath = Path.GetDirectoryName(fullPath) ??
            throw new InvalidDataException("The review file has no parent directory.");
        SafeFileHandle parent = ViewerWindowsFiles.OpenDirectory(parentPath);
        FileStream? stream = null;
        try
        {
            stream = ViewerWindowsFiles.OpenRead(fullPath);
            long length = stream.Length;
            if (length is <= 0 or > ViewerAuthoredEditController.MaximumReviewDocumentBytes)
            {
                throw new InvalidDataException("The review document exceeds the 24 MiB read bound.");
            }
            byte[] bytes = new byte[checked((int)length)];
            await stream.ReadExactlyAsync(bytes, cancellationToken).ConfigureAwait(false);
            if (stream.Length != length)
            {
                throw new IOException("The review file changed while it was read.");
            }
            UsdReviewDocumentInfo info = await Task.Run(() => UsdReviewDocument.Inspect(bytes), cancellationToken)
                .ConfigureAwait(false);
            var identity = new ViewerFileIdentity(fullPath, true, length, Convert.ToHexString(SHA256.HashData(bytes)),
                ViewerWindowsFiles.GetIdentity(stream.SafeFileHandle), ViewerWindowsFiles.GetIdentity(parent));
            return new ViewerReviewDocumentFile(stream, parent, bytes, identity, info);
        }
        catch
        {
            stream?.Dispose();
            parent.Dispose();
            throw;
        }
    }

    internal Task<UsdReviewDocument> ReadAsync(string expectedSourcePath, CancellationToken cancellationToken) =>
        Task.Run(() => UsdReviewDocument.Read(_bytes, expectedSourcePath), cancellationToken);

    public async ValueTask DisposeAsync()
    {
        try
        {
            await _stream.DisposeAsync().ConfigureAwait(false);
        }
        finally
        {
            _parent.Dispose();
        }
    }
}
