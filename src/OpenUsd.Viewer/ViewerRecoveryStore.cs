// Copyright (c) marcschier. Licensed under the MIT License.

using System.Security.Cryptography;
using System.Text;
using OpenUsd.Editing;

namespace OpenUsd.Viewer;

internal sealed class ViewerRecoveryStore : IDisposable
{
    internal const int MaximumFileBytes = ViewerDocumentRecovery.MaximumPayloadBytes + (128 * 1024);
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private readonly SemaphoreSlim _gate = new(1, 1);
    private bool _disposed;

    internal ViewerRecoveryStore(string rootPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootPath);
        if (!Path.IsPathFullyQualified(rootPath))
        {
            throw new ArgumentException("The recovery cache root must be absolute.", nameof(rootPath));
        }
        RootPath = Path.GetFullPath(rootPath);
    }

    internal string RootPath { get; }

    internal string GetCheckpointPath(string key) => GetPath(key);

    internal async Task DeleteVerifiedAsync(
        UsdReviewDocument document, ViewerFileIdentity expected, CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("Verified recovery cleanup currently requires Windows.");
        }
        ObjectDisposedException.ThrowIf(_disposed, this);
        using ViewerReviewFileGuards guards = await ViewerReviewFileGuards.AcquireAsync(
            document, expected.FullPath, cancellationToken).ConfigureAwait(false);
        using ViewerReviewPublicationGate gate =
            await ViewerReviewPublicationGate.AcquireAsync(guards.Key, cancellationToken).ConfigureAwait(false);
        FileStream stream;
        try
        {
            stream = ViewerWindowsFiles.OpenReadForDelete(
                Path.Combine(guards.PhysicalDirectory, guards.Name), expected.FullPath);
        }
        catch (FileNotFoundException)
        {
            return;
        }
        await using (stream.ConfigureAwait(false))
        {
            ViewerFileIdentity current = await guards.ReadOpenedDestinationAsync(stream, cancellationToken)
                .ConfigureAwait(false);
            if (!expected.Matches(current))
            {
                throw new IOException("The recovery file changed; its replacement was not removed.");
            }
            cancellationToken.ThrowIfCancellationRequested();
            ViewerWindowsFiles.DeleteOwnedStaging(stream.SafeFileHandle);
        }
    }

    internal async Task<ViewerFileIdentity> SaveVerifiedAsync(
        ViewerDocumentRecovery checkpoint, CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("Verified review recovery currently requires Windows.");
        }
        ObjectDisposedException.ThrowIf(_disposed, this);
        UsdReviewDocument document = checkpoint.NativeDocument ??
            throw new InvalidOperationException("Recovery publication requires a newly captured native document.");
        string path = GetPath(checkpoint.Binding.ReviewIdentity);
        ViewerFileIdentity expected = await ViewerReviewDocumentPublication.ObserveAsync(
            document, path, cancellationToken).ConfigureAwait(false);
        if (expected.Exists)
        {
            ViewerDocumentRecovery previous = await LoadAsync(checkpoint.Binding.ReviewIdentity, cancellationToken)
                .ConfigureAwait(false) ?? throw new IOException("The recovery file disappeared during validation.");
            byte[] priorBytes = Encode(previous);
            if (priorBytes.LongLength != expected.Length ||
                Convert.ToHexString(SHA256.HashData(priorBytes)) != expected.Sha256)
            {
                throw new IOException("The recovery file changed during validation.");
            }
            if (previous.PayloadFormat != "URD1" ||
                UsdReviewDocument.Inspect(previous.CopyPayload()).DocumentId != document.DocumentId)
            {
                throw new IOException(
                    "A different recovery lineage exists; resolve it before replacing its checkpoint.");
            }
        }
        await using ViewerReviewDocumentPublication publication =
            await ViewerReviewDocumentPublication.StageRecoveryAsync(checkpoint, expected, cancellationToken)
                .ConfigureAwait(false);
        return await publication.PublishAsync(cancellationToken).ConfigureAwait(false);
    }

    internal async Task SaveAsync(ViewerDocumentRecovery checkpoint, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(checkpoint);
        ObjectDisposedException.ThrowIf(_disposed, this);
        cancellationToken.ThrowIfCancellationRequested();
        if (checkpoint.Version != ViewerDocumentRecovery.CurrentVersion)
        {
            throw new InvalidDataException("The recovery cache cannot write an unsupported checkpoint version.");
        }
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            byte[] body = Encode(checkpoint);
            Directory.CreateDirectory(RootPath);
            string destination = GetPath(checkpoint.Binding.ReviewIdentity);
            string temporary = Path.Combine(RootPath, $"{Guid.NewGuid():N}.tmp");
            try
            {
                await using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write,
                    FileShare.None, 4096, FileOptions.Asynchronous | FileOptions.WriteThrough))
                {
                    await stream.WriteAsync(body, cancellationToken).ConfigureAwait(false);
                    await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
                }
                cancellationToken.ThrowIfCancellationRequested();
                File.Move(temporary, destination, overwrite: true);
            }
            finally
            {
                if (File.Exists(temporary))
                {
                    File.Delete(temporary);
                }
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    internal async Task<ViewerDocumentRecovery?> LoadAsync(
        string reviewIdentity, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        string path = GetPath(reviewIdentity);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using FileStream? stream = OpenExisting(path);
            if (stream is null)
            {
                return null;
            }
            if (stream.Length < 64 || stream.Length > MaximumFileBytes)
            {
                throw new InvalidDataException("The recovery file exceeds its bounds or is truncated.");
            }
            byte[] bytes = new byte[checked((int)stream.Length)];
            await stream.ReadExactlyAsync(bytes, cancellationToken).ConfigureAwait(false);
            byte[] tail = new byte[1];
            if (await stream.ReadAsync(tail, cancellationToken).ConfigureAwait(false) != 0)
            {
                throw new InvalidDataException("The recovery file changed while it was being read.");
            }
            cancellationToken.ThrowIfCancellationRequested();
            ViewerDocumentRecovery checkpoint = Deserialize(bytes);
            if (checkpoint.Binding.ReviewIdentity != reviewIdentity)
            {
                throw new InvalidDataException("The recovery file belongs to a different review identity.");
            }
            return checkpoint;
        }
        finally
        {
            _gate.Release();
        }
    }

    private static FileStream? OpenExisting(string path)
    {
        try
        {
            return new FileStream(path, FileMode.Open, FileAccess.Read,
                FileShare.Read, 4096, FileOptions.Asynchronous | FileOptions.SequentialScan);
        }
        catch (FileNotFoundException)
        {
            return null;
        }
        catch (DirectoryNotFoundException)
        {
            return null;
        }
    }

    private string GetPath(string reviewIdentity)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reviewIdentity);
        if (reviewIdentity.Length > 4096 || reviewIdentity.Contains('\0', StringComparison.Ordinal))
        {
            throw new ArgumentException("The review identity exceeds its recovery bound.", nameof(reviewIdentity));
        }
        string key = Convert.ToHexString(SHA256.HashData(StrictUtf8.GetBytes(reviewIdentity)));
        return Path.Combine(RootPath, $"{key}.review-recovery");
    }

    private static byte[] Serialize(ViewerDocumentRecovery checkpoint)
    {
        byte[] payload = checkpoint.CopyPayload();
        using var stream = new MemoryStream(checked(payload.Length + (64 * 1024)));
        using var writer = new BinaryWriter(stream, StrictUtf8, leaveOpen: true);
        writer.Write("OURECOV1"u8);
        writer.Write(checkpoint.Version);
        WriteText(writer, checkpoint.Binding.SourceIdentity);
        WriteText(writer, checkpoint.Binding.SourceFingerprint);
        WriteText(writer, checkpoint.Binding.ReviewIdentity);
        WriteText(writer, checkpoint.Binding.AssetAnchorIdentity);
        WriteText(writer, checkpoint.PayloadFormat);
        writer.Write(payload.Length);
        writer.Write(payload);
        if (stream.Length + 32 > MaximumFileBytes)
        {
            throw new InvalidDataException("The recovery checkpoint exceeds the cache file budget.");
        }
        return stream.ToArray();
    }

    internal static byte[] Encode(ViewerDocumentRecovery checkpoint)
    {
        byte[] body = Serialize(checkpoint);
        byte[] digest = SHA256.HashData(body);
        int length = body.Length;
        Array.Resize(ref body, checked(length + digest.Length));
        digest.CopyTo(body, length);
        return body;
    }

    private static ViewerDocumentRecovery Deserialize(byte[] bytes)
    {
        int bodyLength = bytes.Length - 32;
        if (!CryptographicOperations.FixedTimeEquals(
            SHA256.HashData(bytes.AsSpan(0, bodyLength)), bytes.AsSpan(bodyLength)))
        {
            throw new InvalidDataException("The recovery file checksum does not match its contents.");
        }
        using var stream = new MemoryStream(bytes, 0, bodyLength, writable: false);
        using var reader = new BinaryReader(stream, StrictUtf8, leaveOpen: false);
        if (!ReadBytes(reader, 8).AsSpan().SequenceEqual("OURECOV1"u8))
        {
            throw new InvalidDataException("The recovery file header is invalid.");
        }
        int version = reader.ReadInt32();
        if (version != ViewerDocumentRecovery.CurrentVersion)
        {
            throw new InvalidDataException("The recovery file version is unsupported.");
        }
        string source = ReadText(reader);
        string fingerprint = ReadText(reader);
        string review = ReadText(reader);
        string anchor = ReadText(reader);
        string format = ReadText(reader);
        int length = reader.ReadInt32();
        if (length <= 0 || length > ViewerDocumentRecovery.MaximumPayloadBytes ||
            length != stream.Length - stream.Position)
        {
            throw new InvalidDataException("The recovery payload length is invalid.");
        }
        byte[] payload = ReadBytes(reader, length);
        try
        {
            return new ViewerDocumentRecovery(version,
                new ViewerRecoveryBinding(source, fingerprint, review, anchor),
                format, ViewerDocumentLayerRole.Review, payload);
        }
        catch (ArgumentException exception)
        {
            throw new InvalidDataException("The recovery file contains invalid identity metadata.", exception);
        }
    }

    private static void WriteText(BinaryWriter writer, string value)
    {
        int length = StrictUtf8.GetByteCount(value);
        if (length > 16 * 1024)
        {
            throw new InvalidDataException("Recovery metadata exceeds its byte bound.");
        }
        writer.Write(length);
        writer.Write(StrictUtf8.GetBytes(value));
    }

    private static string ReadText(BinaryReader reader)
    {
        int length = reader.ReadInt32();
        if (length < 0 || length > 16 * 1024)
        {
            throw new InvalidDataException("Recovery metadata length exceeds its safety bound.");
        }
        try
        {
            return StrictUtf8.GetString(ReadBytes(reader, length));
        }
        catch (DecoderFallbackException exception)
        {
            throw new InvalidDataException("Recovery metadata is not valid UTF-8.", exception);
        }
    }

    private static byte[] ReadBytes(BinaryReader reader, int length)
    {
        byte[] bytes = reader.ReadBytes(length);
        if (bytes.Length != length)
        {
            throw new InvalidDataException("The recovery file is truncated.");
        }
        return bytes;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;
        _gate.Dispose();
    }
}
