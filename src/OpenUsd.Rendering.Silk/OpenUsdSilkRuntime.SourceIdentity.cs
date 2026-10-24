// Copyright (c) marcschier. Licensed under the MIT License.

using System.Buffers;
using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using OpenUsd.Interop;

namespace OpenUsd.Rendering.Silk;

public static unsafe partial class OpenUsdSilkRuntime
{
    private const int MaximumSourceIdentityQueries = 1024;
    private const int MaximumSourceIdentityPathBytes = 4096;
    private const int MaximumSourceIdentityResultBytes = 8 * 1024 * 1024;
    private static readonly UTF8Encoding SourceIdentityEncoding = new(false, true);

    internal static IReadOnlyList<SelectionItem> ResolveSourceIdentities(
        nint session, ulong revision, ReadOnlySpan<(string Path, int InstanceIndex)> queries)
    {
        ArgumentOutOfRangeException.ThrowIfGreaterThan(queries.Length, MaximumSourceIdentityQueries);
        (string Path, int InstanceIndex)[] snapshot = queries.ToArray();
        int pathBytes = 0;
        foreach ((string path, int index) in snapshot)
        {
            ArgumentException.ThrowIfNullOrEmpty(path);
            ArgumentOutOfRangeException.ThrowIfNegative(index);
            int length = SourceIdentityEncoding.GetByteCount(path);
            if (path[0] != '/' || path.Contains('\0', StringComparison.Ordinal) ||
                length > MaximumSourceIdentityPathBytes)
            {
                throw new ArgumentException("Source queries require absolute Hydra paths of at most 4,096 UTF-8 bytes.",
                    nameof(queries));
            }
            pathBytes = checked(pathBytes + length + 1);
        }
        var nativeQueries = new NativeSourceIdentityQuery[snapshot.Length];
        byte[] paths = ArrayPool<byte>.Shared.Rent(Math.Max(pathBytes, 1));
        byte[]? output = null;
        try
        {
            output = ArrayPool<byte>.Shared.Rent(8192);
            Span<byte> errors = stackalloc byte[ErrorBufferSize];
            fixed (byte* pathPointer = paths)
            fixed (NativeSourceIdentityQuery* queryPointer = nativeQueries)
            fixed (byte* errorPointer = errors)
            {
                int offset = 0;
                for (int index = 0; index < snapshot.Length; index++)
                {
                    nativeQueries[index] = new NativeSourceIdentityQuery(
                        pathPointer + offset, snapshot[index].InstanceIndex);
                    offset += SourceIdentityEncoding.GetBytes(snapshot[index].Path, paths.AsSpan(offset));
                    paths[offset++] = 0;
                }
                var request = new NativeSourceIdentityRequest(revision, queryPointer, (uint)snapshot.Length);
                var error = new NativeErrorBuffer(errorPointer, (nuint)errors.Length);
                for (int attempt = 0; attempt < 2; attempt++)
                {
                    OpenUsdNativeStatus status;
                    nuint required;
                    fixed (byte* outputPointer = output)
                    {
                        try
                        {
                            status = SourceIdentityNativeMethods.Resolve(
                                session, in request, outputPointer, (nuint)output.Length, out required, ref error);
                        }
                        catch (EntryPointNotFoundException exception)
                        {
                            throw new NotSupportedException(
                                "Source identity resolution requires a matched native runtime exporting " +
                                "openusd_silk_session_resolve_source_identities.", exception);
                        }
                    }
                    if (status == OpenUsdNativeStatus.BufferTooSmall)
                    {
                        if (attempt != 0 || required <= (nuint)output.Length ||
                            required > MaximumSourceIdentityResultBytes)
                        {
                            throw new InvalidDataException("Native source identity result sizing is inconsistent.");
                        }
                        byte[] larger = ArrayPool<byte>.Shared.Rent(checked((int)required));
                        ArrayPool<byte>.Shared.Return(output);
                        output = larger;
                        continue;
                    }
                    ThrowIfFailed(status, errors, error);
                    if (required < 8 || required > (nuint)output.Length || required > MaximumSourceIdentityResultBytes)
                    {
                        throw new InvalidDataException("Native source identity result size is invalid.");
                    }
                    return DecodeSourceIdentities(output.AsSpan(0, (int)required), snapshot.Length);
                }
            }
            throw new InvalidDataException("Native source identity result sizing did not converge.");
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(paths);
            if (output is not null)
            {
                ArrayPool<byte>.Shared.Return(output);
            }
        }
    }

    internal static IReadOnlyList<SelectionItem> DecodeSourceIdentities(ReadOnlySpan<byte> bytes, int expectedCount)
    {
        if (ReadSourceIdentityUInt32(ref bytes) != 1 ||
            ReadSourceIdentityUInt32(ref bytes) != expectedCount ||
            expectedCount is < 0 or > MaximumSourceIdentityQueries)
        {
            throw new InvalidDataException("Native source identity result header is invalid.");
        }
        var result = new SelectionItem[expectedCount];
        for (int index = 0; index < result.Length; index++)
        {
            uint length = ReadSourceIdentityUInt32(ref bytes);
            uint count = ReadSourceIdentityUInt32(ref bytes);
            if (count > SilkMeshUpsertCommand.MaximumInstancerContextEntries)
            {
                throw new InvalidDataException("Native source identity context exceeds its level limit.");
            }
            string path = ReadSourceIdentityPath(ref bytes, length);
            var context = new SelectionInstancerEntry[count];
            for (int level = 0; level < context.Length; level++)
            {
                uint instance = ReadSourceIdentityUInt32(ref bytes);
                uint contextLength = ReadSourceIdentityUInt32(ref bytes);
                if (instance > int.MaxValue)
                {
                    throw new InvalidDataException("Native source identity context has an invalid instance index.");
                }
                context[level] = new SelectionInstancerEntry(
                    ReadSourceIdentityPath(ref bytes, contextLength), (int)instance);
            }
            result[index] = SelectionItem.FromInstancerContext(path, context);
        }
        if (!bytes.IsEmpty)
        {
            throw new InvalidDataException("Native source identity result has trailing bytes.");
        }
        return Array.AsReadOnly(result);
    }

    private static uint ReadSourceIdentityUInt32(ref ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < sizeof(uint))
        {
            throw new InvalidDataException("Native source identity result is truncated.");
        }
        uint value = BinaryPrimitives.ReadUInt32LittleEndian(bytes);
        bytes = bytes[sizeof(uint)..];
        return value;
    }

    private static string ReadSourceIdentityPath(ref ReadOnlySpan<byte> bytes, uint length)
    {
        if (length is 0 or > MaximumSourceIdentityPathBytes || length > bytes.Length)
        {
            throw new InvalidDataException("Native source identity path length is invalid.");
        }
        string path;
        try
        {
            path = SourceIdentityEncoding.GetString(bytes[..(int)length]);
        }
        catch (DecoderFallbackException exception)
        {
            throw new InvalidDataException("Native source identity path is not valid UTF-8.", exception);
        }
        bytes = bytes[(int)length..];
        if (path[0] != '/' || path.Contains('\0', StringComparison.Ordinal))
        {
            throw new InvalidDataException("Native source identity path is not an absolute prim path.");
        }
        return path;
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct NativeSourceIdentityQuery(byte* path, int instanceIndex)
    {
        internal readonly byte* Path = path;
        internal readonly int InstanceIndex = instanceIndex;
        internal readonly uint Reserved = 0;
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct NativeSourceIdentityRequest(
        ulong revision, NativeSourceIdentityQuery* queries, uint count)
    {
        internal readonly uint StructSize = (uint)sizeof(NativeSourceIdentityRequest);
        internal readonly uint Version = 1;
        internal readonly ulong PageRevision = revision;
        internal readonly NativeSourceIdentityQuery* Queries = queries;
        internal readonly uint QueryCount = count;
        internal readonly uint MaximumResultBytes = MaximumSourceIdentityResultBytes;
    }

    private static partial class SourceIdentityNativeMethods
    {
        [LibraryImport(LibraryName, EntryPoint = "openusd_silk_session_resolve_source_identities")]
        [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
        internal static partial OpenUsdNativeStatus Resolve(
            nint session, in NativeSourceIdentityRequest request, byte* buffer, nuint capacity,
            out nuint required, ref NativeErrorBuffer error);
    }
}
