// Copyright (c) marcschier. Licensed under the MIT License.

using System.Collections.ObjectModel;

namespace OpenUsd.Rendering.Silk;

internal readonly record struct SilkPtexAtlasTile(
    uint FaceId,
    uint ContentX,
    uint ContentY,
    uint ContentWidth,
    uint ContentHeight,
    float U0,
    float V0,
    float U1,
    float V1,
    int AdjacentFaceId0,
    int AdjacentFaceId1,
    int AdjacentFaceId2,
    int AdjacentFaceId3,
    uint AdjacentEdgeId0,
    uint AdjacentEdgeId1,
    uint AdjacentEdgeId2,
    uint AdjacentEdgeId3);

internal sealed class SilkPackedPtexAtlas : IDisposable
{
    internal static readonly IReadOnlyDictionary<uint, SilkPtexAtlasTile> EmptyTiles =
        new ReadOnlyDictionary<uint, SilkPtexAtlasTile>(new Dictionary<uint, SilkPtexAtlasTile>());

    private IDisposable? _reservation;

    internal SilkPackedPtexAtlas(
        uint width,
        uint height,
        byte[] pixels,
        IReadOnlyDictionary<uint, SilkPtexAtlasTile> tiles,
        IDisposable? reservation)
    {
        Width = width;
        Height = height;
        Pixels = pixels;
        Tiles = tiles;
        _reservation = reservation;
    }

    internal uint Width { get; }
    internal uint Height { get; }
    internal byte[] Pixels { get; private set; }
    internal IReadOnlyDictionary<uint, SilkPtexAtlasTile> Tiles { get; private set; }

    public void Dispose()
    {
        Pixels = [];
        Tiles = EmptyTiles;
        Interlocked.Exchange(ref _reservation, null)?.Dispose();
    }
}

internal static unsafe class SilkPtexAtlasPacker
{
    private const uint GutterWidth = 1;
    private const uint BytesPerPixel = 4;

    internal static SilkPackedPtexAtlas Pack(
        SilkDecodedPtexFaces decoded,
        uint maximumDimension,
        SilkCpuTextureBudget? budget = null)
    {
        ArgumentNullException.ThrowIfNull(decoded);
        ArgumentOutOfRangeException.ThrowIfZero(maximumDimension);

        SilkPtexDecodeInfo info = decoded.Info;
        ValidateSupportedFormat(info);
        List<FaceSource> faces = ValidateFaces(decoded);
        if (faces.Count == 0)
        {
            return new SilkPackedPtexAtlas(
                0,
                0,
                [],
                SilkPackedPtexAtlas.EmptyTiles,
                null);
        }

        List<PendingTile> pendingTiles = PlanTiles(faces, maximumDimension, out uint width, out uint height);
        ulong byteCount = checked((ulong)width * height * BytesPerPixel);
        if (byteCount > int.MaxValue)
        {
            throw new InvalidDataException("The packed Ptex atlas exceeds the addressable pixel-buffer size.");
        }

        IDisposable? reservation = budget?.Reserve(byteCount);
        try
        {
            byte[] pixels = new byte[checked((int)byteCount)];
            var tiles = new Dictionary<uint, SilkPtexAtlasTile>(pendingTiles.Count);
            foreach (PendingTile pending in pendingTiles)
            {
                WriteTile(pixels, width, height, info.ChannelCount, pending);
                uint contentX = checked(pending.X + GutterWidth);
                uint contentY = checked(pending.Y + GutterWidth);
                uint contentEndX = checked(contentX + pending.Source.Layout.Width);
                uint contentEndY = checked(contentY + pending.Source.Layout.Height);
                SilkPtexFaceLayout face = pending.Source.Layout;
                tiles.Add(
                    face.FaceId,
                    new SilkPtexAtlasTile(
                        face.FaceId,
                        contentX,
                        contentY,
                        face.Width,
                        face.Height,
                        (float)contentX / width,
                        (float)contentY / height,
                        (float)contentEndX / width,
                        (float)contentEndY / height,
                        face.AdjacentFaceId0,
                        face.AdjacentFaceId1,
                        face.AdjacentFaceId2,
                        face.AdjacentFaceId3,
                        face.AdjacentEdgeId0,
                        face.AdjacentEdgeId1,
                        face.AdjacentEdgeId2,
                        face.AdjacentEdgeId3));
            }

            var result = new SilkPackedPtexAtlas(
                width,
                height,
                pixels,
                new ReadOnlyDictionary<uint, SilkPtexAtlasTile>(tiles),
                reservation);
            reservation = null;
            return result;
        }
        finally
        {
            reservation?.Dispose();
        }
    }

    private static void ValidateSupportedFormat(SilkPtexDecodeInfo info)
    {
        if (info.StructSize != (uint)sizeof(SilkPtexDecodeInfo) ||
            info.Version != 1 ||
            info.MeshType > 1)
        {
            throw new InvalidDataException("The Ptex decoder returned invalid atlas metadata.");
        }

        if (info.DataType != 0 ||
            info.ChannelCount == 3 && info.AlphaChannel != -1 ||
            info.ChannelCount == 4 && info.AlphaChannel != 3 ||
            info.ChannelCount is not (3 or 4))
        {
            throw new NotSupportedException(
                "Ptex atlas sampling supports unsigned 8-bit RGB or RGBA with alpha in channel 3.");
        }
    }

    private static List<FaceSource> ValidateFaces(SilkDecodedPtexFaces decoded)
    {
        SilkPtexDecodeInfo info = decoded.Info;
        SilkPtexFaceLayout[] layouts = decoded.Faces;
        byte[] pixels = decoded.Pixels;
        if (info.RequestedFaceCount != layouts.Length ||
            info.RequiredByteCount != (ulong)pixels.Length ||
            (layouts.Length != 0 && info.FileFaceCount == 0))
        {
            throw new InvalidDataException("The Ptex decoder returned inconsistent atlas inputs.");
        }

        uint expectedLayoutSize = (uint)sizeof(SilkPtexFaceLayout);
        ulong expectedOffset = 0;
        var uniqueFaces = new Dictionary<uint, FaceSource>();
        foreach (SilkPtexFaceLayout layout in layouts)
        {
            ulong expectedStride = checked((ulong)layout.Width * info.ChannelCount);
            ulong faceByteCount = checked((ulong)layout.RowStrideBytes * layout.Height);
            if (layout.StructSize != expectedLayoutSize ||
                layout.FaceId >= info.FileFaceCount ||
                layout.Width == 0 ||
                layout.Height == 0 ||
                expectedStride > uint.MaxValue ||
                layout.RowStrideBytes != expectedStride ||
                layout.DataOffsetBytes != expectedOffset ||
                expectedOffset > (ulong)pixels.Length ||
                faceByteCount > (ulong)pixels.Length - expectedOffset)
            {
                throw new InvalidDataException(
                    $"The Ptex decoder returned an invalid layout for face {layout.FaceId}: " +
                    $"size={layout.StructSize}/{expectedLayoutSize}, fileFaces={info.FileFaceCount}, " +
                    $"dimensions={layout.Width}x{layout.Height}, stride={layout.RowStrideBytes}/{expectedStride}, " +
                    $"offset={layout.DataOffsetBytes}/{expectedOffset}, bytes={faceByteCount}, pixels={pixels.Length}.");
            }

            ValidateAdjacency(layout, info.FileFaceCount);
            var source = new FaceSource(layout, checked((int)expectedOffset), pixels);
            if (uniqueFaces.TryGetValue(layout.FaceId, out FaceSource previous))
            {
                if (!EquivalentFace(previous, source))
                {
                    throw new InvalidDataException(
                        "Duplicate Ptex face IDs have conflicting layouts or pixel data.");
                }
            }
            else
            {
                uniqueFaces.Add(layout.FaceId, source);
            }

            expectedOffset = checked(expectedOffset + faceByteCount);
        }

        if (expectedOffset != (ulong)pixels.Length)
        {
            throw new InvalidDataException("The Ptex face layouts do not cover the decoded pixels.");
        }

        return uniqueFaces.Values
            .OrderBy(static face => face.Layout.FaceId)
            .ToList();
    }

    private static void ValidateAdjacency(SilkPtexFaceLayout face, uint fileFaceCount)
    {
        for (int edge = 0; edge < 4; edge++)
        {
            int adjacentFace = face.GetAdjacentFaceId(edge);
            if (adjacentFace < -1 ||
                adjacentFace >= fileFaceCount ||
                adjacentFace >= 0 && face.GetAdjacentEdgeId(edge) > 3)
            {
                throw new InvalidDataException("The Ptex decoder returned invalid face adjacency.");
            }
        }
    }

    private static bool EquivalentFace(FaceSource left, FaceSource right)
    {
        SilkPtexFaceLayout a = left.Layout;
        SilkPtexFaceLayout b = right.Layout;
        int byteCount = checked((int)((ulong)a.RowStrideBytes * a.Height));
        return a.Width == b.Width &&
            a.Height == b.Height &&
            a.RowStrideBytes == b.RowStrideBytes &&
            a.Flags == b.Flags &&
            a.AdjacentFaceId0 == b.AdjacentFaceId0 &&
            a.AdjacentFaceId1 == b.AdjacentFaceId1 &&
            a.AdjacentFaceId2 == b.AdjacentFaceId2 &&
            a.AdjacentFaceId3 == b.AdjacentFaceId3 &&
            a.AdjacentEdgeId0 == b.AdjacentEdgeId0 &&
            a.AdjacentEdgeId1 == b.AdjacentEdgeId1 &&
            a.AdjacentEdgeId2 == b.AdjacentEdgeId2 &&
            a.AdjacentEdgeId3 == b.AdjacentEdgeId3 &&
            left.Pixels.AsSpan(left.Offset, byteCount).SequenceEqual(
                right.Pixels.AsSpan(right.Offset, byteCount));
    }

    private static List<PendingTile> PlanTiles(
        IReadOnlyList<FaceSource> faces,
        uint maximumDimension,
        out uint atlasWidth,
        out uint atlasHeight)
    {
        var pendingTiles = new List<PendingTile>(faces.Count);
        uint x = 0;
        uint y = 0;
        uint rowHeight = 0;
        atlasWidth = 0;

        foreach (FaceSource face in faces)
        {
            uint outerWidth;
            uint outerHeight;
            try
            {
                outerWidth = checked(face.Layout.Width + 2 * GutterWidth);
                outerHeight = checked(face.Layout.Height + 2 * GutterWidth);
            }
            catch (OverflowException exception)
            {
                throw new InvalidDataException("A Ptex face exceeds the supported atlas dimensions.", exception);
            }

            if (outerWidth > maximumDimension || outerHeight > maximumDimension)
            {
                throw new InvalidOperationException(
                    "A Ptex face and its gutter exceed the maximum atlas dimension.");
            }

            if (x != 0 && outerWidth > maximumDimension - x)
            {
                y = checked(y + rowHeight);
                x = 0;
                rowHeight = 0;
            }
            if (outerHeight > maximumDimension - y)
            {
                throw new InvalidOperationException(
                    "The packed Ptex atlas exceeds the maximum dimension.");
            }

            pendingTiles.Add(new PendingTile(face, x, y));
            x = checked(x + outerWidth);
            rowHeight = Math.Max(rowHeight, outerHeight);
            atlasWidth = Math.Max(atlasWidth, x);
        }

        atlasHeight = checked(y + rowHeight);
        return pendingTiles;
    }

    private static void WriteTile(
        byte[] destination,
        uint atlasWidth,
        uint atlasHeight,
        uint channelCount,
        PendingTile pending)
    {
        SilkPtexFaceLayout face = pending.Source.Layout;
        int channels = checked((int)channelCount);
        int contentX = checked((int)(pending.X + GutterWidth));
        int contentY = checked((int)(pending.Y + GutterWidth));
        int width = checked((int)face.Width);
        int height = checked((int)face.Height);
        int rowStride = checked((int)face.RowStrideBytes);
        int atlasStride = checked((int)atlasWidth * (int)BytesPerPixel);
        int checkedAtlasHeight = checked((int)atlasHeight);
        int gutterX = contentX - 1;
        int gutterY = contentY - 1;

        for (int y = -1; y <= height; y++)
        {
            int sourceY = Math.Clamp(y, 0, height - 1);
            for (int x = -1; x <= width; x++)
            {
                int sourceX = Math.Clamp(x, 0, width - 1);
                int sourceOffset = checked(
                    pending.Source.Offset + sourceY * rowStride + sourceX * channels);
                int atlasX = checked(gutterX + x + 1);
                int atlasY = checked(gutterY + y + 1);
                if ((uint)atlasY >= (uint)checkedAtlasHeight)
                {
                    throw new InvalidDataException("A Ptex tile gutter exceeds the planned atlas.");
                }
                int destinationOffset = checked(atlasY * atlasStride + atlasX * (int)BytesPerPixel);
                destination[destinationOffset] = pending.Source.Pixels[sourceOffset];
                destination[destinationOffset + 1] = pending.Source.Pixels[sourceOffset + 1];
                destination[destinationOffset + 2] = pending.Source.Pixels[sourceOffset + 2];
                destination[destinationOffset + 3] = channels == 4
                    ? pending.Source.Pixels[sourceOffset + 3]
                    : byte.MaxValue;
            }
        }
    }

    private readonly record struct FaceSource(SilkPtexFaceLayout Layout, int Offset, byte[] Pixels);

    private readonly record struct PendingTile(FaceSource Source, uint X, uint Y);
}
