// Copyright (c) marcschier. Licensed under the MIT License.

using System.Buffers.Binary;
using System.Text;
using OpenUsd.Interop;

namespace OpenUsd.Editing;

internal static class UsdReviewDocumentCodec
{
    internal const int MaximumEnvelopeBytes = 24 * 1024 * 1024;
    internal const int MaximumDocumentBytes = 16 * 1024 * 1024;
    internal const int MaximumDependencies = 1024;
    private static readonly StringComparer PathComparer =
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    internal static UsdReviewSourceBinding DecodeSourceBinding(ReadOnlySpan<byte> bytes, string expectedSourcePath)
    {
        var reader = new ReviewReader(bytes);
        reader.Header(0x31425352);
        ulong stageId = reader.U64();
        if (stageId == 0)
        {
            throw InvalidPacket("zero process-local source stage identity");
        }
        string root = reader.FilePath();
        string fingerprint = reader.Sha256();
        string anchor = reader.FilePath();
        UsdReviewDependency[] dependencies = reader.Dependencies();
        reader.End();
        RequireSamePath(root, expectedSourcePath, "source root differs from the requested stage");
        return new UsdReviewSourceBinding(stageId, root, fingerprint, anchor, dependencies, bytes);
    }

    internal static UsdReviewDocument DecodeCapturedDocument(
        ReadOnlySpan<byte> envelope, UsdReviewSourceBinding source, string targetDocumentPath) =>
        DecodeDocument(envelope, source.SourceRootPath, targetDocumentPath, source, default, captured: true);

    internal static UsdReviewDocument DecodeReadDocument(
        ReadOnlySpan<byte> envelope, ReadOnlySpan<byte> requestedBytes, string expectedSourcePath) =>
        DecodeDocument(envelope, expectedSourcePath, null, null, requestedBytes, captured: false);

    internal static UsdReviewDocumentInfo DecodeInspection(ReadOnlySpan<byte> envelope, int documentByteLength)
    {
        var reader = new ReviewReader(envelope);
        reader.Header(0x31494452);
        uint length = reader.U32();
        if (length is 0 or > MaximumDocumentBytes || length != documentByteLength)
        {
            throw InvalidPacket("inspection document length differs from the request");
        }
        string id = ReadDocumentId(ref reader);
        string root = reader.FilePath();
        string fingerprint = reader.Sha256();
        string identifier = reader.Text();
        if (identifier.Length == 0)
        {
            throw InvalidPacket("empty original review target identifier");
        }
        string target = reader.FilePath();
        string anchor = reader.FilePath();
        UsdReviewDependency[] dependencies = reader.Dependencies();
        reader.End();
        return new UsdReviewDocumentInfo((int)length, id, root, fingerprint, identifier, target, anchor, dependencies);
    }

    internal static UsdReviewDocumentImportResult DecodeImportResult(
        OpenUsdNativeLayerEditResult native, UsdReviewSourceBinding source)
    {
        if (native.Outcome is < 0 or > 3 || (native.Outcome == 0) != (native.Packet is not null))
        {
            throw InvalidPacket("import outcome and state ownership disagree");
        }
        UsdLayerEditingState? state = native.Packet is null ? null : UsdLayerEditCodec.DecodeState(native.Packet);
        if (state is not null && (state.Identity.StageId != source.StageId || !state.CanAttemptAuthoredEdits))
        {
            throw InvalidPacket("import state is not the source stage's editable local review target");
        }
        return new UsdReviewDocumentImportResult((UsdLayerEditOutcome)native.Outcome, state, native.Diagnostic);
    }

    internal static string FullSourcePath(string path, string paramName)
    {
        _ = UsdEditingValidation.Text(path, paramName);
        ArgumentException.ThrowIfNullOrWhiteSpace(path, paramName);
        string fullPath = Path.GetFullPath(path);
        _ = UsdEditingValidation.Text(fullPath, paramName);
        return fullPath;
    }

    internal static void ValidateTargetPath(string targetDocumentPath)
    {
        _ = UsdEditingValidation.Text(targetDocumentPath, nameof(targetDocumentPath));
        if (!Path.IsPathFullyQualified(targetDocumentPath))
        {
            throw new ArgumentException("Portable review publication requires an absolute filesystem path.",
                nameof(targetDocumentPath));
        }
    }

    internal static void ValidateDocumentBytes(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length is < 1 or > MaximumDocumentBytes)
        {
            throw new ArgumentException("A portable review document must contain 1 byte through 16 MiB.", nameof(bytes));
        }
    }

    internal static OpenUsdNativeException InvalidPacket(string detail) =>
        new(OpenUsdNativeStatus.NativeError, $"Invalid portable review metadata: {detail}.");

    private static UsdReviewDocument DecodeDocument(
        ReadOnlySpan<byte> envelope, string expectedSourcePath, string? expectedTargetPath,
        UsdReviewSourceBinding? source, ReadOnlySpan<byte> requestedBytes, bool captured)
    {
        var reader = new ReviewReader(envelope);
        reader.Header(0x31454452);
        ReadOnlySpan<byte> document = reader.Blob(MaximumDocumentBytes);
        ReadOnlySpan<byte> receipt = reader.Blob(MaximumEnvelopeBytes);
        if (document.IsEmpty || receipt.IsEmpty == captured)
        {
            throw InvalidPacket("missing document or unexpected save receipt");
        }
        string id = ReadDocumentId(ref reader);
        string root = reader.FilePath();
        string fingerprint = reader.Sha256();
        string identifier = reader.Text();
        if (identifier.Length == 0)
        {
            throw InvalidPacket("empty original review target identifier");
        }
        string target = reader.FilePath();
        string anchor = reader.FilePath();
        UsdReviewDependency[] dependencies = reader.Dependencies();
        reader.End();
        RequireSamePath(root, expectedSourcePath, "source root differs from explicit source intent");
        if (expectedTargetPath is not null)
        {
            RequireSamePath(target, expectedTargetPath, "publication target differs from the capture request");
        }
        if (source is not null)
        {
            if (!string.Equals(fingerprint, source.SourceFingerprint, StringComparison.OrdinalIgnoreCase))
            {
                throw InvalidPacket("source fingerprint differs from the capture binding");
            }
            RequireSamePath(anchor, source.AssetAnchor, "asset anchor differs from the capture binding");
        }
        if (!captured && !document.SequenceEqual(requestedBytes))
        {
            throw InvalidPacket("read returned different portable bytes");
        }
        return new UsdReviewDocument(id, root, fingerprint, identifier, target, anchor, dependencies, document, receipt);
    }

    private static string ReadDocumentId(ref ReviewReader reader)
    {
        string id = reader.Text();
        if (!Guid.TryParseExact(id, "D", out Guid parsedId) ||
            !string.Equals(id, parsedId.ToString("D"), StringComparison.Ordinal))
        {
            throw InvalidPacket("document identifier is not a canonical GUID");
        }
        return id;
    }

    private static void RequireSamePath(string actual, string expected, string detail)
    {
        try
        {
            if (PathComparer.Equals(Path.GetFullPath(actual), Path.GetFullPath(expected)))
            {
                return;
            }
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or IOException)
        {
            throw InvalidPacket("invalid filesystem path in metadata");
        }
        throw InvalidPacket(detail);
    }

    private ref struct ReviewReader
    {
        private readonly ReadOnlySpan<byte> _bytes;
        private int _position;

        internal ReviewReader(ReadOnlySpan<byte> bytes)
        {
            if (bytes.Length > MaximumEnvelopeBytes)
            {
                throw InvalidPacket("envelope exceeds 24 MiB");
            }
            _bytes = bytes;
            _position = 0;
        }

        internal uint U32() => BinaryPrimitives.ReadUInt32LittleEndian(Take(4));
        internal ulong U64() => BinaryPrimitives.ReadUInt64LittleEndian(Take(8));

        internal void Header(uint magic)
        {
            if (U32() != magic || U32() != 1)
            {
                throw InvalidPacket("invalid metadata magic or version");
            }
        }

        internal ReadOnlySpan<byte> Blob(int limit) => Take(Count(limit, 1));

        internal string Text()
        {
            ReadOnlySpan<byte> bytes = Blob(UsdEditingValidation.MaximumTextBytes);
            if (bytes.Contains((byte)0))
            {
                throw InvalidPacket("NUL in metadata text");
            }
            try
            {
                return UsdEditingValidation.Utf8.GetString(bytes);
            }
            catch (DecoderFallbackException)
            {
                throw InvalidPacket("invalid UTF-8 in metadata text");
            }
        }

        internal string FilePath()
        {
            string path = Text();
            if (!Path.IsPathFullyQualified(path))
            {
                throw InvalidPacket("metadata requires an absolute filesystem path");
            }
            return path;
        }

        internal string Sha256()
        {
            string sha256 = Text();
            if (sha256.Length != 64)
            {
                throw InvalidPacket("SHA-256 identity must have 64 hexadecimal characters");
            }
            foreach (char character in sha256)
            {
                if (character is not (>= '0' and <= '9' or >= 'a' and <= 'f' or >= 'A' and <= 'F'))
                {
                    throw InvalidPacket("invalid hexadecimal SHA-256 identity");
                }
            }
            return sha256;
        }

        internal UsdReviewDependency[] Dependencies()
        {
            int count = Count(MaximumDependencies, 20);
            var dependencies = new UsdReviewDependency[count];
            var paths = new HashSet<string>(PathComparer);
            for (int index = 0; index < count; index++)
            {
                string path = FilePath();
                string sha256 = Sha256();
                ulong byteLength = U64();
                uint kind = U32();
                if (kind > 1 || !paths.Add(path))
                {
                    throw InvalidPacket("unknown dependency kind or duplicate dependency path");
                }
                dependencies[index] = new UsdReviewDependency(path, sha256, byteLength, (UsdReviewDependencyKind)kind);
            }
            return dependencies;
        }

        internal readonly void End()
        {
            if (_position != _bytes.Length)
            {
                throw InvalidPacket("trailing metadata bytes");
            }
        }

        private int Count(int limit, int minimumItemBytes)
        {
            uint count = U32();
            if (count > limit || count > (_bytes.Length - _position) / minimumItemBytes)
            {
                throw InvalidPacket("count exceeds its budget or remaining bytes");
            }
            return (int)count;
        }

        private ReadOnlySpan<byte> Take(int count)
        {
            if (count > _bytes.Length - _position)
            {
                throw InvalidPacket("truncated metadata");
            }
            ReadOnlySpan<byte> result = _bytes.Slice(_position, count);
            _position += count;
            return result;
        }
    }
}
