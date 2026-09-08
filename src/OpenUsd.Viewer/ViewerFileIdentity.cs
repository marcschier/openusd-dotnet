// Copyright (c) marcschier. Licensed under the MIT License.

using System.Security.Cryptography;

namespace OpenUsd.Viewer;

internal readonly record struct ViewerPhysicalFileIdentity(ulong Volume, ulong Low, ulong High);

/// <summary>An observed file identity, not a lease or an atomic compare-and-replace guarantee.</summary>
internal sealed record ViewerFileIdentity(
    string FullPath, bool Exists, long Length, string Sha256,
    ViewerPhysicalFileIdentity? PhysicalIdentity = null, ViewerPhysicalFileIdentity? ParentIdentity = null)
{
    internal static async Task<ViewerFileIdentity> CaptureAsync(
        string path, long maximumBytes, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumBytes);
        if (!Path.IsPathFullyQualified(path))
        {
            throw new ArgumentException("Document file identities require an absolute path.", nameof(path));
        }
        cancellationToken.ThrowIfCancellationRequested();
        string fullPath = Path.GetFullPath(path);
        FileStream stream;
        try
        {
            stream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read,
                64 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
        }
        catch (FileNotFoundException)
        {
            return new ViewerFileIdentity(fullPath, false, 0, string.Empty);
        }
        catch (DirectoryNotFoundException)
        {
            return new ViewerFileIdentity(fullPath, false, 0, string.Empty);
        }

        await using (stream.ConfigureAwait(false))
        {
            long originalLength = stream.Length;
            DateTime originalWrite = File.GetLastWriteTimeUtc(fullPath);
            if (originalLength > maximumBytes)
            {
                throw new InvalidDataException("The document file exceeds the fingerprint read budget.");
            }
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            byte[] buffer = new byte[64 * 1024];
            long total = 0;
            while (true)
            {
                int requested = (int)Math.Min(buffer.Length, maximumBytes - total);
                requested = Math.Max(1, requested);
                int read = await stream.ReadAsync(buffer.AsMemory(0, requested), cancellationToken)
                    .ConfigureAwait(false);
                if (read == 0)
                {
                    break;
                }
                if (read > maximumBytes - total)
                {
                    throw new InvalidDataException("The document file grew beyond the fingerprint read budget.");
                }
                hash.AppendData(buffer, 0, read);
                total += read;
            }
            cancellationToken.ThrowIfCancellationRequested();
            if (total != originalLength || stream.Length != originalLength ||
                File.GetLastWriteTimeUtc(fullPath) != originalWrite)
            {
                throw new IOException("The document file changed while its identity was being captured.");
            }
            return new ViewerFileIdentity(fullPath, true, total, Convert.ToHexString(hash.GetHashAndReset()));
        }
    }

    internal bool Matches(ViewerFileIdentity other)
    {
        ArgumentNullException.ThrowIfNull(other);
        StringComparison paths = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        return string.Equals(FullPath, other.FullPath, paths) &&
            Exists == other.Exists && Length == other.Length && Sha256 == other.Sha256 &&
            PhysicalIdentity == other.PhysicalIdentity && ParentIdentity == other.ParentIdentity;
    }
}
