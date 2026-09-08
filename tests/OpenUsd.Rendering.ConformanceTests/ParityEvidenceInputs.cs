// Copyright (c) marcschier. Licensed under the MIT License.

using System.Buffers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace OpenUsd.Rendering.ConformanceTests;

internal sealed record ParityInputIdentity(
    [property: JsonPropertyName("path")] string Path,
    [property: JsonPropertyName("sha256")] string Sha256,
    [property: JsonPropertyName("length")] long Length,
    [property: JsonPropertyName("hashPolicy")] string HashPolicy = "sha256-crlf-to-lf");

internal sealed record ParitySourceIdentity(
    [property: JsonPropertyName("sha256")] string Sha256,
    [property: JsonPropertyName("files")] IReadOnlyList<ParityInputIdentity> Files,
    [property: JsonPropertyName("hashPolicy")] string HashPolicy = "sha256-crlf-to-lf")
{
    [JsonPropertyName("fileCount")]
    public int FileCount => Files.Count;
}

internal static class ParityEvidenceInputs
{
    internal static ParitySourceIdentity CreateTextIdentity(
        string root,
        IReadOnlyList<string> relativePaths) => CreateIdentity(root, relativePaths, mixedContent: false);

    internal static ParitySourceIdentity CreateFixtureIdentity(
        string root,
        IReadOnlyList<string> relativeRoots,
        int maximumEntries = 4096)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        ArgumentNullException.ThrowIfNull(relativeRoots);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumEntries);
        if (relativeRoots.Count > maximumEntries)
        {
            throw new InvalidOperationException("Fixture evidence input roots exceed the entry budget.");
        }
        string fullRoot = Path.GetFullPath(root);
        var files = new SortedSet<string>(StringComparer.Ordinal);
        var pending = new Stack<string>(relativeRoots.Select(CanonicalPath));
        int admitted = relativeRoots.Count;
        while (pending.TryPop(out string? relative))
        {
            string path = Path.Combine(fullRoot, relative.Replace('/', Path.DirectorySeparatorChar));
            FileAttributes attributes = File.GetAttributes(path);
            if ((attributes & FileAttributes.ReparsePoint) != 0)
            {
                throw new InvalidOperationException($"Fixture evidence cannot follow an indirect path: {relative}");
            }
            if ((attributes & FileAttributes.Directory) != 0)
            {
                foreach (string entry in Directory.EnumerateFileSystemEntries(path))
                {
                    if (admitted == maximumEntries)
                    {
                        throw new InvalidOperationException("Fixture evidence exceeds the entry budget.");
                    }
                    admitted++;
                    pending.Push(Path.GetRelativePath(fullRoot, entry).Replace('\\', '/'));
                }
            }
            else
            {
                files.Add(relative);
            }
        }
        return CreateIdentity(fullRoot, files.ToArray(), mixedContent: true);
    }

    internal static void RequireUnchanged(ParitySourceIdentity before, ParitySourceIdentity after)
    {
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(after);
        if (before.Sha256 != after.Sha256 || before.HashPolicy != after.HashPolicy)
        {
            throw new InvalidOperationException(
                "Parity evidence inputs changed during capture; the result cannot describe one source revision.");
        }
    }

    private static ParitySourceIdentity CreateIdentity(
        string root,
        IReadOnlyList<string> relativePaths,
        bool mixedContent)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        ArgumentNullException.ThrowIfNull(relativePaths);
        if (relativePaths.Count == 0)
        {
            throw new ArgumentException("Source evidence requires at least one input.", nameof(relativePaths));
        }
        string fullRoot = Path.GetFullPath(root);
        var files = new List<ParityInputIdentity>(relativePaths.Count);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (string relativePath in relativePaths)
        {
            string relative = CanonicalPath(relativePath);
            if (!seen.Add(relative))
            {
                throw new ArgumentException($"Duplicate source evidence input: {relative}", nameof(relativePaths));
            }
            string path = Path.Combine(fullRoot, relative.Replace('/', Path.DirectorySeparatorChar));
            bool text = !mixedContent || Path.GetExtension(relative).ToLowerInvariant() is
                ".usda" or ".json" or ".md" or ".txt" or ".ocio" or ".mtlx" or ".mdl" or ".py";
            (string hash, long length) = text ? HashText(path) : HashBinary(path);
            files.Add(new ParityInputIdentity(relative, hash, length, text ? "sha256-crlf-to-lf" : "sha256-raw"));
        }

        string payload = JsonSerializer.Serialize(files);
        return new ParitySourceIdentity(
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload))),
            files.AsReadOnly(),
            mixedContent ? "sha256-per-input" : "sha256-crlf-to-lf");
    }

    private static string CanonicalPath(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        string relative = path.Replace('\\', '/');
        if (Path.IsPathRooted(relative) ||
            relative.Contains(':', StringComparison.Ordinal) ||
            relative.Split('/').Any(static segment => segment.Length == 0 || segment is "." or ".."))
        {
            throw new ArgumentException(
                $"Evidence requires a canonical repository-relative path: {path}", nameof(path));
        }
        return relative;
    }

    private static (string Hash, long Length) HashBinary(string path)
    {
        using FileStream stream = File.OpenRead(path);
        return (Convert.ToHexString(SHA256.HashData(stream)), stream.Length);
    }

    private static (string Hash, long Length) HashText(string path)
    {
        const int readSize = 81920;
        using FileStream stream = File.OpenRead(path);
        using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        byte[] buffer = ArrayPool<byte>.Shared.Rent(readSize);
        try
        {
            long length = 0;
            bool pendingCarriageReturn = false;
            int count;
            while ((count = stream.Read(buffer, 0, readSize)) != 0)
            {
                // A trailing CR belongs to the next block only when that block starts with LF.
                if (pendingCarriageReturn && buffer[0] != (byte)'\n')
                {
                    hash.AppendData("\r"u8);
                    length++;
                }
                pendingCarriageReturn = false;
                int written = 0;
                for (int index = 0; index < count; index++)
                {
                    byte value = buffer[index];
                    if (value == (byte)'\r')
                    {
                        if (index == count - 1)
                        {
                            pendingCarriageReturn = true;
                            continue;
                        }
                        if (buffer[index + 1] == (byte)'\n')
                        {
                            continue;
                        }
                    }
                    buffer[written++] = value;
                }
                hash.AppendData(buffer.AsSpan(0, written));
                length += written;
            }

            if (pendingCarriageReturn)
            {
                hash.AppendData("\r"u8);
                length++;
            }
            return (Convert.ToHexString(hash.GetHashAndReset()), length);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }
}
