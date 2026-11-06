// Copyright (c) marcschier. Licensed under the MIT License.

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using OpenUsd.Interop;

namespace OpenUsd.Rendering.Silk;

internal sealed class SilkDecodedPtexFaces(
    SilkPtexDecodeInfo info,
    SilkPtexFaceLayout[] faces,
    byte[] pixels,
    IDisposable? reservation) : IDisposable
{
    private IDisposable? _reservation = reservation;

    internal SilkPtexDecodeInfo Info { get; } = info;
    internal SilkPtexFaceLayout[] Faces { get; private set; } = faces;
    internal byte[] Pixels { get; private set; } = pixels;
    internal bool IsDisposed { get; private set; }

    public void Dispose()
    {
        IsDisposed = true;
        Faces = [];
        Pixels = [];
        Interlocked.Exchange(ref _reservation, null)?.Dispose();
    }
}

[StructLayout(LayoutKind.Sequential)]
internal struct SilkPtexFaceLayout
{
    internal uint StructSize;
    internal uint FaceId;
    internal uint Width;
    internal uint Height;
    internal uint RowStrideBytes;
    internal uint Flags;
    internal ulong DataOffsetBytes;
    internal int AdjacentFaceId0;
    internal int AdjacentFaceId1;
    internal int AdjacentFaceId2;
    internal int AdjacentFaceId3;
    internal uint AdjacentEdgeId0;
    internal uint AdjacentEdgeId1;
    internal uint AdjacentEdgeId2;
    internal uint AdjacentEdgeId3;

    internal readonly int GetAdjacentFaceId(int edge) => edge switch
    {
        0 => AdjacentFaceId0,
        1 => AdjacentFaceId1,
        2 => AdjacentFaceId2,
        3 => AdjacentFaceId3,
        _ => throw new ArgumentOutOfRangeException(nameof(edge))
    };

    internal readonly uint GetAdjacentEdgeId(int edge) => edge switch
    {
        0 => AdjacentEdgeId0,
        1 => AdjacentEdgeId1,
        2 => AdjacentEdgeId2,
        3 => AdjacentEdgeId3,
        _ => throw new ArgumentOutOfRangeException(nameof(edge))
    };
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct SilkPtexDecodeInfo
{
    internal uint StructSize;
    internal uint Version;
    internal uint MeshType;
    internal uint DataType;
    internal uint ChannelCount;
    internal int AlphaChannel;
    internal uint FileFaceCount;
    internal uint RequestedFaceCount;
    internal ulong RequiredByteCount;

    internal static SilkPtexDecodeInfo Create() => new()
    {
        StructSize = (uint)sizeof(SilkPtexDecodeInfo),
        Version = 1
    };
}

internal readonly record struct SilkPtexFaceDescription(
    SilkPtexDecodeInfo Info,
    SilkPtexFaceLayout[] Faces);

internal static unsafe partial class SilkNativePtexDecoder
{
    internal const int MaximumRequestedFaces = 65_536;
    private const int ErrorBufferSize = 1024;

    internal static SilkPtexFaceDescription Describe(
        string asset,
        ReadOnlySpan<uint> faceIds,
        uint maximumFaceDimension,
        ulong maximumDecodedBytes)
    {
        ValidateRequest(asset, faceIds, maximumFaceDimension);
        var faces = new SilkPtexFaceLayout[faceIds.Length];
        Span<byte> errorBytes = stackalloc byte[ErrorBufferSize];
        fixed (byte* error = errorBytes)
        fixed (uint* ids = faceIds)
        fixed (SilkPtexFaceLayout* faceBuffer = faces)
        {
            var errorBuffer = new NativeErrorBuffer(error, (nuint)errorBytes.Length);
            SilkPtexDecodeInfo info = SilkPtexDecodeInfo.Create();
            OpenUsdNativeStatus status = DescribeFaces(
                asset,
                ids,
                (uint)faceIds.Length,
                maximumFaceDimension,
                maximumDecodedBytes,
                faceBuffer,
                (uint)faces.Length,
                ref info,
                ref errorBuffer);
            if (status != OpenUsdNativeStatus.Ok)
            {
                ThrowIfFailed(status, errorBytes, errorBuffer);
            }

            ValidateInfo(info, faceIds.Length, maximumDecodedBytes);
            ValidateFaces(faces, faceIds, info, maximumFaceDimension, checked((int)info.RequiredByteCount));
            return new SilkPtexFaceDescription(info, faces);
        }
    }

    internal static SilkPtexFaceDescription DescribeSnapshot(
        SilkPtexEncodedSource source,
        ReadOnlySpan<uint> faceIds,
        uint maximumFaceDimension,
        ulong maximumDecodedBytes)
    {
        ArgumentNullException.ThrowIfNull(source);
        ValidateRequest(source.Asset, faceIds, maximumFaceDimension);
        using SilkPtexEncodedSource.Lease lease = source.Acquire();
        ReadOnlySpan<byte> encoded = lease.Bytes;
        var faces = new SilkPtexFaceLayout[faceIds.Length];
        Span<byte> errorBytes = stackalloc byte[ErrorBufferSize];
        fixed (byte* sourceBytes = encoded)
        fixed (byte* error = errorBytes)
        fixed (uint* ids = faceIds)
        fixed (SilkPtexFaceLayout* faceBuffer = faces)
        {
            var errorBuffer = new NativeErrorBuffer(error, (nuint)errorBytes.Length);
            SilkPtexDecodeInfo info = SilkPtexDecodeInfo.Create();
            OpenUsdNativeStatus status;
            try
            {
                status = DescribeFacesFromMemory(
                    sourceBytes,
                    (ulong)encoded.Length,
                    ids,
                    (uint)faceIds.Length,
                    maximumFaceDimension,
                    maximumDecodedBytes,
                    faceBuffer,
                    (uint)faces.Length,
                    ref info,
                    ref errorBuffer);
            }
            catch (EntryPointNotFoundException exception)
            {
                throw MissingMemoryExport("openusd_silk_ptex_describe_faces_from_memory", exception);
            }
            if (status != OpenUsdNativeStatus.Ok)
            {
                ThrowIfFailed(status, errorBytes, errorBuffer, source.Asset);
            }

            ValidateInfo(info, faceIds.Length, maximumDecodedBytes);
            ValidateFaces(faces, faceIds, info, maximumFaceDimension, checked((int)info.RequiredByteCount));
            return new SilkPtexFaceDescription(info, faces);
        }
    }

    internal static SilkDecodedPtexFaces Decode(
        string asset,
        ReadOnlySpan<uint> faceIds,
        uint maximumFaceDimension,
        ulong maximumDecodedBytes,
        ulong maximumEncodedBytes,
        SilkCpuTextureBudget? budget = null)
    {
        ValidateRequest(asset, faceIds, maximumFaceDimension);
        using SilkPtexEncodedSource source = SilkPtexEncodedSource.Open(asset, maximumEncodedBytes, budget);
        return DecodeSnapshot(source, faceIds, maximumFaceDimension, maximumDecodedBytes);
    }

    internal static SilkDecodedPtexFaces DecodeSnapshot(
        SilkPtexEncodedSource source,
        ReadOnlySpan<uint> faceIds,
        uint maximumFaceDimension,
        ulong maximumDecodedBytes)
    {
        ArgumentNullException.ThrowIfNull(source);
        ValidateRequest(source.Asset, faceIds, maximumFaceDimension);
        using SilkPtexEncodedSource.Lease lease = source.Acquire();
        ReadOnlySpan<byte> encoded = lease.Bytes;
        Span<byte> errorBytes = stackalloc byte[ErrorBufferSize];
        fixed (byte* sourceBytes = encoded)
        fixed (byte* error = errorBytes)
        fixed (uint* ids = faceIds)
        {
            var errorBuffer = new NativeErrorBuffer(error, (nuint)errorBytes.Length);
            SilkPtexDecodeInfo info = SilkPtexDecodeInfo.Create();
            OpenUsdNativeStatus status;
            try
            {
                status = DecodeFacesFromMemory(
                    sourceBytes,
                    (ulong)encoded.Length,
                    ids,
                    (uint)faceIds.Length,
                    maximumFaceDimension,
                    maximumDecodedBytes,
                    null,
                    0,
                    null,
                    0,
                    ref info,
                    ref errorBuffer);
            }
            catch (EntryPointNotFoundException exception)
            {
                throw MissingMemoryExport("openusd_silk_ptex_decode_faces_from_memory", exception);
            }
            if (status != OpenUsdNativeStatus.BufferTooSmall && status != OpenUsdNativeStatus.Ok)
            {
                ThrowIfFailed(status, errorBytes, errorBuffer, source.Asset);
            }

            ValidateInfo(info, faceIds.Length, maximumDecodedBytes);
            if (faceIds.Length != 0 && status != OpenUsdNativeStatus.BufferTooSmall)
            {
                throw new InvalidDataException("The native Ptex decoder did not report required output capacity.");
            }

            if (info.RequestedFaceCount == 0)
            {
                return new SilkDecodedPtexFaces(info, [], [], null);
            }

            int faceCount = checked((int)info.RequestedFaceCount);
            int pixelCount = checked((int)info.RequiredByteCount);
            IDisposable? reservation = source.Budget?.Reserve(info.RequiredByteCount);
            try
            {
                var faces = new SilkPtexFaceLayout[faceCount];
                byte[] pixels = new byte[pixelCount];
                SilkPtexDecodeInfo decodedInfo = SilkPtexDecodeInfo.Create();
                errorBuffer = new NativeErrorBuffer(error, (nuint)errorBytes.Length);
                fixed (SilkPtexFaceLayout* faceBuffer = faces)
                fixed (byte* pixelBuffer = pixels)
                {
                    try
                    {
                        status = DecodeFacesFromMemory(
                            sourceBytes,
                            (ulong)encoded.Length,
                            ids,
                            (uint)faceIds.Length,
                            maximumFaceDimension,
                            maximumDecodedBytes,
                            faceBuffer,
                            (uint)faces.Length,
                            pixelBuffer,
                            (ulong)pixels.Length,
                            ref decodedInfo,
                            ref errorBuffer);
                    }
                    catch (EntryPointNotFoundException exception)
                    {
                        throw MissingMemoryExport("openusd_silk_ptex_decode_faces_from_memory", exception);
                    }
                }
                if (status != OpenUsdNativeStatus.Ok)
                {
                    ThrowIfFailed(status, errorBytes, errorBuffer, source.Asset);
                }
                ValidateInfo(decodedInfo, faceIds.Length, maximumDecodedBytes);
                if (!SameResult(info, decodedInfo))
                {
                    throw new InvalidDataException("Ptex metadata changed between sizing and decoding.");
                }
                ValidateFaces(faces, faceIds, decodedInfo, maximumFaceDimension, pixels.Length);

                var result = new SilkDecodedPtexFaces(decodedInfo, faces, pixels, reservation);
                reservation = null;
                return result;
            }
            finally
            {
                reservation?.Dispose();
            }
        }
    }

    internal static SilkDecodedPtexFaces DecodeSamplingNeighborhood(
        string asset,
        ReadOnlySpan<uint> sampledFaceIds,
        uint maximumFaceDimension,
        ulong maximumDecodedBytes,
        ulong maximumEncodedBytes,
        SilkCpuTextureBudget? budget = null)
    {
        ValidateRequest(asset, sampledFaceIds, maximumFaceDimension);
        using SilkPtexEncodedSource source = SilkPtexEncodedSource.Open(asset, maximumEncodedBytes, budget);
        return DecodeSamplingNeighborhoodSnapshot(
            source, sampledFaceIds, maximumFaceDimension, maximumDecodedBytes);
    }

    internal static SilkDecodedPtexFaces DecodeSamplingNeighborhoodSnapshot(
        SilkPtexEncodedSource source,
        ReadOnlySpan<uint> sampledFaceIds,
        uint maximumFaceDimension,
        ulong maximumDecodedBytes)
    {
        ArgumentNullException.ThrowIfNull(source);
        ValidateRequest(source.Asset, sampledFaceIds, maximumFaceDimension);
        SilkPtexFaceNeighborhood neighborhood = SilkPtexFaceNeighborhood.Discover(
            sampledFaceIds,
            maximumFaceDimension,
            maximumDecodedBytes,
            ids => DescribeSnapshot(source, ids, maximumFaceDimension, maximumDecodedBytes));
        return neighborhood.Decode(ids =>
            DecodeSnapshot(source, ids, maximumFaceDimension, maximumDecodedBytes));
    }

    internal static SilkPackedPtexAtlas PrepareSamplingAtlas(
        string asset,
        SilkPtexMeshSelection selection,
        uint maximumFaceDimension,
        ulong maximumDecodedBytes,
        ulong maximumEncodedBytes,
        uint maximumAtlasDimension,
        SilkCpuTextureBudget? budget = null)
    {
        ArgumentNullException.ThrowIfNull(selection);
        ArgumentOutOfRangeException.ThrowIfZero(maximumAtlasDimension);
        if (!ReferenceEquals(selection.Budget, budget))
        {
            throw new ArgumentException(
                "Ptex selection and atlas preparation must use the same CPU texture budget.",
                nameof(budget));
        }
        using SilkPtexMeshSelection.Lease lease = selection.Acquire();
        using SilkDecodedPtexFaces decoded = DecodeSamplingNeighborhood(
            asset, lease.FaceIds, maximumFaceDimension, maximumDecodedBytes, maximumEncodedBytes, budget);
        return SilkPtexAtlasPacker.Pack(decoded, lease.FaceIds, maximumAtlasDimension, budget);
    }

    private static void ValidateRequest(
        string asset,
        ReadOnlySpan<uint> faceIds,
        uint maximumFaceDimension)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(asset);
        ArgumentOutOfRangeException.ThrowIfZero(maximumFaceDimension);
        if (faceIds.Length > MaximumRequestedFaces)
        {
            throw new ArgumentOutOfRangeException(nameof(faceIds));
        }
    }

    internal static void ValidateInfo(
        SilkPtexDecodeInfo info,
        int requestedFaceCount,
        ulong maximumDecodedBytes)
    {
        if (info.StructSize != (uint)sizeof(SilkPtexDecodeInfo) || info.Version != 1 ||
            info.RequestedFaceCount != (uint)requestedFaceCount ||
            info.RequiredByteCount > maximumDecodedBytes ||
            info.RequiredByteCount > int.MaxValue ||
            info.FileFaceCount == 0 && requestedFaceCount != 0 ||
            info.MeshType > 1 || info.DataType > 3 || info.ChannelCount == 0 ||
            info.AlphaChannel < -1 || info.AlphaChannel >= info.ChannelCount)
        {
            throw new InvalidDataException("The native Ptex decoder returned invalid metadata.");
        }
    }

    internal static void ValidateFaces(
        ReadOnlySpan<SilkPtexFaceLayout> faces,
        ReadOnlySpan<uint> requestedFaceIds,
        SilkPtexDecodeInfo info,
        uint maximumFaceDimension,
        int pixelCount)
    {
        uint bytesPerChannel = info.DataType switch
        {
            0 => 1,
            1 or 2 => 2,
            3 => 4,
            _ => throw new InvalidDataException("The native Ptex decoder returned an unknown data type.")
        };
        ulong bytesPerPixel = checked((ulong)bytesPerChannel * info.ChannelCount);
        ulong expectedOffset = 0;
        for (int index = 0; index < faces.Length; index++)
        {
            SilkPtexFaceLayout face = faces[index];
            ulong faceBytes = checked((ulong)face.RowStrideBytes * face.Height);
            if (face.StructSize != (uint)sizeof(SilkPtexFaceLayout) ||
                face.FaceId != requestedFaceIds[index] ||
                face.FaceId >= info.FileFaceCount ||
                face.Width == 0 || face.Height == 0 ||
                face.Width > maximumFaceDimension || face.Height > maximumFaceDimension ||
                face.RowStrideBytes != checked((ulong)face.Width * bytesPerPixel) ||
                face.DataOffsetBytes != expectedOffset ||
                face.DataOffsetBytes > (ulong)pixelCount ||
                faceBytes > (ulong)pixelCount - face.DataOffsetBytes)
            {
                throw new InvalidDataException("The native Ptex decoder returned an invalid face layout.");
            }
            for (int edge = 0; edge < 4; edge++)
            {
                int adjacentFace = face.GetAdjacentFaceId(edge);
                if (adjacentFace < -1 || adjacentFace >= info.FileFaceCount ||
                    adjacentFace >= 0 && face.GetAdjacentEdgeId(edge) > 3)
                {
                    throw new InvalidDataException("The native Ptex decoder returned invalid face adjacency.");
                }
            }
            expectedOffset = checked(expectedOffset + faceBytes);
        }

        if (expectedOffset != (ulong)pixelCount)
        {
            throw new InvalidDataException("The native Ptex face layouts do not cover the decoded pixels.");
        }
    }

    internal static bool SameFileMetadata(SilkPtexDecodeInfo left, SilkPtexDecodeInfo right) =>
        left.StructSize == right.StructSize &&
        left.Version == right.Version &&
        left.MeshType == right.MeshType &&
        left.DataType == right.DataType &&
        left.ChannelCount == right.ChannelCount &&
        left.AlphaChannel == right.AlphaChannel &&
        left.FileFaceCount == right.FileFaceCount;

    private static bool SameResult(SilkPtexDecodeInfo left, SilkPtexDecodeInfo right) =>
        SameFileMetadata(left, right) &&
        left.RequestedFaceCount == right.RequestedFaceCount &&
        left.RequiredByteCount == right.RequiredByteCount;

    private static NotSupportedException MissingMemoryExport(
        string entryPoint,
        EntryPointNotFoundException exception) =>
        new($"Ptex snapshots require a matched native runtime exporting {entryPoint}.", exception);

    private static void ThrowIfFailed(
        OpenUsdNativeStatus status,
        ReadOnlySpan<byte> errorBytes,
        NativeErrorBuffer error,
        string? asset = null)
    {
        int length = (int)Math.Min(error.Required, (nuint)errorBytes.Length);
        string message = length == 0
            ? $"Native Ptex decode failed with status {status}."
            : Encoding.UTF8.GetString(errorBytes[..length]);
        if (error.Required > error.Capacity)
        {
            message += $" The full native diagnostic required {error.Required} bytes.";
        }
        if (asset is not null)
        {
            message = $"Ptex source '{asset}': {message}";
        }
        throw new OpenUsdSilkException(status, message);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeErrorBuffer(byte* data, nuint capacity)
    {
        internal byte* Data = data;
        internal nuint Capacity = capacity;
        internal nuint Required;
    }

    [LibraryImport(
        "openusd_hdsilk",
        EntryPoint = "openusd_silk_ptex_describe_faces",
        StringMarshalling = StringMarshalling.Utf8)]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    private static partial OpenUsdNativeStatus DescribeFaces(
        string asset,
        uint* faceIds,
        uint faceCount,
        uint maximumFaceDimension,
        ulong maximumDecodedBytes,
        SilkPtexFaceLayout* faces,
        uint faceCapacity,
        ref SilkPtexDecodeInfo info,
        ref NativeErrorBuffer error);

    [LibraryImport("openusd_hdsilk", EntryPoint = "openusd_silk_ptex_describe_faces_from_memory")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    private static partial OpenUsdNativeStatus DescribeFacesFromMemory(
        byte* source,
        ulong sourceSize,
        uint* faceIds,
        uint faceCount,
        uint maximumFaceDimension,
        ulong maximumDecodedBytes,
        SilkPtexFaceLayout* faces,
        uint faceCapacity,
        ref SilkPtexDecodeInfo info,
        ref NativeErrorBuffer error);

    [LibraryImport("openusd_hdsilk", EntryPoint = "openusd_silk_ptex_decode_faces_from_memory")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    private static partial OpenUsdNativeStatus DecodeFacesFromMemory(
        byte* source,
        ulong sourceSize,
        uint* faceIds,
        uint faceCount,
        uint maximumFaceDimension,
        ulong maximumDecodedBytes,
        SilkPtexFaceLayout* faces,
        uint faceCapacity,
        byte* pixels,
        ulong pixelCapacity,
        ref SilkPtexDecodeInfo info,
        ref NativeErrorBuffer error);
}
