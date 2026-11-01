// Copyright (c) marcschier. Licensed under the MIT License.

namespace OpenUsd.Rendering.Silk;

internal sealed class SilkPtexFaceNeighborhood
{
    private readonly uint[] _faceIds;
    private readonly SilkPtexFaceLayout[] _faces;
    private readonly SilkPtexDecodeInfo _fileInfo;
    private readonly uint _maximumFaceDimension;
    private readonly ulong _maximumDecodedBytes;
    private readonly ulong _decodedBytes;

    private SilkPtexFaceNeighborhood(
        uint[] faceIds,
        SilkPtexFaceLayout[] faces,
        SilkPtexDecodeInfo fileInfo,
        uint maximumFaceDimension,
        ulong maximumDecodedBytes,
        ulong decodedBytes)
    {
        _faceIds = faceIds;
        _faces = faces;
        _fileInfo = fileInfo;
        _maximumFaceDimension = maximumFaceDimension;
        _maximumDecodedBytes = maximumDecodedBytes;
        _decodedBytes = decodedBytes;
    }

    internal ReadOnlySpan<uint> FaceIds => _faceIds;

    internal static SilkPtexFaceNeighborhood Discover(
        ReadOnlySpan<uint> sampledFaceIds,
        uint maximumFaceDimension,
        ulong maximumDecodedBytes,
        Func<uint[], SilkPtexFaceDescription> describe)
    {
        ArgumentNullException.ThrowIfNull(describe);
        ArgumentOutOfRangeException.ThrowIfZero(maximumFaceDimension);
        if (sampledFaceIds.Length > SilkNativePtexDecoder.MaximumRequestedFaces)
        {
            throw new ArgumentOutOfRangeException(nameof(sampledFaceIds));
        }

        var selected = new HashSet<uint>();
        foreach (uint faceId in sampledFaceIds)
        {
            selected.Add(faceId);
        }
        uint[] sampled = Sort(selected);
        var layouts = new Dictionary<uint, SilkPtexFaceLayout>();
        SilkPtexDecodeInfo fileInfo = default;
        bool haveFileInfo = false;
        ulong decodedBytes = 0;

        void describeBatch(uint[] ids)
        {
            SilkPtexFaceDescription description = describe(ids);
            SilkNativePtexDecoder.ValidateInfo(description.Info, ids.Length, maximumDecodedBytes);
            if (description.Faces.Length != ids.Length)
            {
                throw new InvalidDataException("The Ptex description has an invalid face count.");
            }
            SilkNativePtexDecoder.ValidateFaces(
                description.Faces,
                ids,
                description.Info,
                maximumFaceDimension,
                checked((int)description.Info.RequiredByteCount));
            if (haveFileInfo && !SilkNativePtexDecoder.SameFileMetadata(fileInfo, description.Info))
            {
                throw new InvalidDataException("Ptex file metadata changed while discovering faces.");
            }
            fileInfo = description.Info;
            haveFileInfo = true;
            if (description.Info.RequiredByteCount > maximumDecodedBytes - decodedBytes ||
                description.Info.RequiredByteCount > (ulong)int.MaxValue - decodedBytes)
            {
                throw new InvalidDataException("The Ptex face neighborhood exceeds the decoded-byte limit.");
            }
            decodedBytes += description.Info.RequiredByteCount;
            foreach (SilkPtexFaceLayout face in description.Faces)
            {
                layouts.Add(face.FaceId, face);
            }
        }

        describeBatch(sampled);
        var pending = new HashSet<uint>();
        foreach (uint faceId in sampled)
        {
            SilkPtexFaceLayout source = layouts[faceId];
            SilkPtexFaceTopology.ValidateSupportedFace(source);
            for (int edge = 0; edge < 4; edge++)
            {
                int adjacentId = source.GetAdjacentFaceId(edge);
                if (adjacentId < 0)
                {
                    continue;
                }
                SilkPtexFaceTopology.RequireQuadAdjacency(fileInfo.MeshType);
                AddFace((uint)adjacentId, selected, pending);
            }
        }

        if (pending.Count != 0)
        {
            describeBatch(Sort(pending));
            pending.Clear();
        }

        foreach (uint faceId in sampled)
        {
            SilkPtexFaceLayout source = layouts[faceId];
            for (int edge = 0; edge < 4; edge++)
            {
                _ = TryGetAdjacent(source, edge, layouts, fileInfo, out _);
            }
            CollectCorners(source, layouts, fileInfo, selected, pending, allowMissing: true);
        }
        if (pending.Count != 0)
        {
            describeBatch(Sort(pending));
            pending.Clear();
        }
        foreach (uint faceId in sampled)
        {
            CollectCorners(layouts[faceId], layouts, fileInfo, selected, pending, allowMissing: false);
        }

        uint[] faceIds = Sort(selected);
        var faces = new SilkPtexFaceLayout[faceIds.Length];
        for (int index = 0; index < faceIds.Length; index++)
        {
            faces[index] = layouts[faceIds[index]];
        }
        return new SilkPtexFaceNeighborhood(
            faceIds, faces, fileInfo, maximumFaceDimension, maximumDecodedBytes, decodedBytes);
    }

    internal SilkDecodedPtexFaces Decode(Func<uint[], SilkDecodedPtexFaces> decode)
    {
        ArgumentNullException.ThrowIfNull(decode);
        SilkDecodedPtexFaces? decoded = decode((uint[])_faceIds.Clone());
        ArgumentNullException.ThrowIfNull(decoded);
        try
        {
            ValidateDecoded(decoded);
            SilkDecodedPtexFaces result = decoded;
            decoded = null;
            return result;
        }
        finally
        {
            decoded?.Dispose();
        }
    }

    private void ValidateDecoded(SilkDecodedPtexFaces decoded)
    {
        SilkNativePtexDecoder.ValidateInfo(decoded.Info, _faceIds.Length, _maximumDecodedBytes);
        if (!SilkNativePtexDecoder.SameFileMetadata(_fileInfo, decoded.Info) ||
            decoded.Info.RequiredByteCount != _decodedBytes ||
            decoded.Info.RequiredByteCount != (ulong)decoded.Pixels.Length ||
            decoded.Faces.Length != _faces.Length)
        {
            throw new InvalidDataException("Ptex metadata changed between discovery and decoding.");
        }
        SilkNativePtexDecoder.ValidateFaces(
            decoded.Faces, _faceIds, decoded.Info, _maximumFaceDimension, decoded.Pixels.Length);
        for (int index = 0; index < _faces.Length; index++)
        {
            if (!SameFace(_faces[index], decoded.Faces[index]))
            {
                throw new InvalidDataException(
                    $"Ptex face {_faceIds[index]} changed between discovery and decoding.");
            }
        }
    }

    private static void CollectCorners(
        SilkPtexFaceLayout source,
        Dictionary<uint, SilkPtexFaceLayout> layouts,
        SilkPtexDecodeInfo fileInfo,
        HashSet<uint> selected,
        HashSet<uint> pending,
        bool allowMissing)
    {
        for (int y = 0; y < 2; y++)
        {
            for (int x = 0; x < 2; x++)
            {
                double u = x == 0 ? -0.5 / source.Width : 1 + 0.5 / source.Width;
                double v = y == 0 ? -0.5 / source.Height : 1 + 0.5 / source.Height;
                SilkPtexFaceLayout current = source;
                int crossings = 0;
                while (true)
                {
                    int edge = SilkPtexFaceTopology.GetOutsideEdge(u, v);
                    if (edge < 0)
                    {
                        break;
                    }
                    int adjacentId = current.GetAdjacentFaceId(edge);
                    if (adjacentId < 0)
                    {
                        SilkPtexFaceTopology.ClampToEdge(ref u, ref v, edge);
                        continue;
                    }
                    if (++crossings > 2)
                    {
                        throw new InvalidDataException(
                            "Ptex adjacency traversal exceeded the supported face neighborhood.");
                    }
                    if (!layouts.ContainsKey((uint)adjacentId) && allowMissing)
                    {
                        AddFace((uint)adjacentId, selected, pending);
                        break;
                    }
                    _ = TryGetAdjacent(current, edge, layouts, fileInfo, out SilkPtexFaceLayout adjacent);
                    SilkPtexFaceTopology.CrossAdjacentEdge(
                        ref u, ref v, edge, checked((int)current.GetAdjacentEdgeId(edge)));
                    current = adjacent;
                }
            }
        }
    }

    private static bool TryGetAdjacent(
        SilkPtexFaceLayout source,
        int edge,
        Dictionary<uint, SilkPtexFaceLayout> layouts,
        SilkPtexDecodeInfo fileInfo,
        out SilkPtexFaceLayout adjacent)
    {
        int adjacentId = source.GetAdjacentFaceId(edge);
        if (adjacentId < 0)
        {
            adjacent = default;
            return false;
        }
        SilkPtexFaceTopology.RequireQuadAdjacency(fileInfo.MeshType);
        if (!layouts.TryGetValue((uint)adjacentId, out adjacent))
        {
            throw new InvalidDataException(
                $"Ptex face {source.FaceId} references an undiscovered adjacent face {adjacentId}.");
        }
        SilkPtexFaceTopology.ValidateSupportedFace(adjacent);
        SilkPtexFaceTopology.ValidateReciprocal(source, edge, adjacent);
        return true;
    }

    private static void AddFace(uint faceId, HashSet<uint> selected, HashSet<uint> pending)
    {
        if (selected.Add(faceId))
        {
            if (selected.Count > SilkNativePtexDecoder.MaximumRequestedFaces)
            {
                throw new InvalidDataException("The Ptex face neighborhood exceeds the face-count limit.");
            }
            pending.Add(faceId);
        }
    }

    private static uint[] Sort(HashSet<uint> ids)
    {
        uint[] result = [.. ids];
        Array.Sort(result);
        return result;
    }

    private static bool SameFace(SilkPtexFaceLayout left, SilkPtexFaceLayout right) =>
        left.StructSize == right.StructSize &&
        left.FaceId == right.FaceId &&
        left.Width == right.Width &&
        left.Height == right.Height &&
        left.RowStrideBytes == right.RowStrideBytes &&
        left.Flags == right.Flags &&
        left.AdjacentFaceId0 == right.AdjacentFaceId0 &&
        left.AdjacentFaceId1 == right.AdjacentFaceId1 &&
        left.AdjacentFaceId2 == right.AdjacentFaceId2 &&
        left.AdjacentFaceId3 == right.AdjacentFaceId3 &&
        left.AdjacentEdgeId0 == right.AdjacentEdgeId0 &&
        left.AdjacentEdgeId1 == right.AdjacentEdgeId1 &&
        left.AdjacentEdgeId2 == right.AdjacentEdgeId2 &&
        left.AdjacentEdgeId3 == right.AdjacentEdgeId3;
}

internal static class SilkPtexFaceTopology
{
    internal static void RequireQuadAdjacency(uint meshType)
    {
        if (meshType != 1)
        {
            throw new NotSupportedException("Ptex adjacency sampling requires quad-based face textures.");
        }
    }

    internal static void ValidateSupportedFace(SilkPtexFaceLayout face)
    {
        const uint subfaceFlag = 1u << 3;
        if ((face.Flags & subfaceFlag) != 0)
        {
            throw new NotSupportedException("Ptex subface sampling is not supported.");
        }
    }

    internal static void ValidateReciprocal(
        SilkPtexFaceLayout source,
        int edge,
        SilkPtexFaceLayout adjacent)
    {
        int adjacentEdge = checked((int)source.GetAdjacentEdgeId(edge));
        if (adjacent.GetAdjacentFaceId(adjacentEdge) != source.FaceId ||
            adjacent.GetAdjacentEdgeId(adjacentEdge) != (uint)edge)
        {
            throw new InvalidDataException(
                $"Ptex adjacency from face {source.FaceId}, edge {edge}, is not reciprocal.");
        }
    }

    internal static int GetOutsideEdge(double u, double v) =>
        u < 0 ? 3 : u > 1 ? 1 : v < 0 ? 0 : v > 1 ? 2 : -1;

    internal static void ClampToEdge(ref double u, ref double v, int edge)
    {
        switch (edge)
        {
            case 0:
                v = 0;
                break;
            case 1:
                u = 1;
                break;
            case 2:
                v = 1;
                break;
            case 3:
                u = 0;
                break;
        }
    }

    internal static void CrossAdjacentEdge(ref double u, ref double v, int edge, int adjacentEdge)
    {
        switch (edge)
        {
            case 0:
                v += 1;
                break;
            case 1:
                u -= 1;
                break;
            case 2:
                v -= 1;
                break;
            case 3:
                u += 1;
                break;
        }

        switch ((edge - adjacentEdge + 2) & 3)
        {
            case 1:
                (u, v) = (v, 1 - u);
                break;
            case 2:
                (u, v) = (1 - u, 1 - v);
                break;
            case 3:
                (u, v) = (1 - v, u);
                break;
        }
    }
}
