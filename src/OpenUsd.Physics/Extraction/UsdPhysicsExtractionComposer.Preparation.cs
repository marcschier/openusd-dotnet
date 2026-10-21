// Copyright (c) marcschier. Licensed under the MIT License.

using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using OpenUsd.Physics.Interop;

namespace OpenUsd.Physics.Extraction;

internal static partial class UsdPhysicsExtractionComposer
{
    internal sealed record PreparedCollider(ReadOnlyMemory<PhysxVec3f>[] Hulls, string? Failure = null);

    internal static Dictionary<int, PreparedCollider> PrepareMeshes(
        UsdPhysicsExtractionPage page, CancellationToken cancellationToken,
        int firstObject = 0, int objectCount = -1,
        Dictionary<string, PreparedCollider>? cache = null)
    {
        ArgumentNullException.ThrowIfNull(page);
        var prepared = new Dictionary<int, PreparedCollider>();
        var objects = new List<int>();
        var inputs = new List<PhysxMeshInput>();
        var points = new List<PhysxVec3f>();
        var indices = new List<uint>();
        cache ??= new Dictionary<string, PreparedCollider>(StringComparer.Ordinal);
        var keys = new List<string>();
        var aliases = new List<List<int>>();
        var pending = new Dictionary<string, int>(StringComparer.Ordinal);
        int end = objectCount < 0 ? page.ObjectCount : checked(firstObject + objectCount);
        for (int index = firstObject; index < end; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            UsdPhysicsExtractionObject item = page.GetObject(index);
            if (item.HullCount != 0)
            {
                continue;
            }
            if (item.Kind != UsdPhysicsExtractionObjectKind.Collider || !item.IsEnabled ||
                item.Geometry is not (UsdPhysicsExtractionGeometryKind.Mesh or UsdPhysicsExtractionGeometryKind.ConvexMesh) ||
                !TryFind(page, item, UsdPhysicsExtractionKey.CollisionApproximation, out UsdPhysicsExtractionProperty property) ||
                !property.IsText || property.ValueCount != 1 || !IsUsableProperty(property))
            {
                continue;
            }
            string approximation = page.GetText(property.ValueStart);
            if (approximation is not ("convexHull" or "convexDecomposition"))
            {
                continue;
            }
            if ((item.Flags & UsdPhysicsExtractionObjectTraits.UnmappedOpinions) != 0)
            {
                prepared.Add(index, new PreparedCollider([],
                    "has unmapped physics opinions that cannot be silently replaced by the default cooking profile"));
                continue;
            }
            bool decomposition = approximation == "convexDecomposition";
            int triangleCount = decomposition ? item.IndexCount : 0;
            if (item.PointCount > PhysxMeshPreparation.MaxPoints || triangleCount > PhysxMeshPreparation.MaxIndices)
            {
                throw new PhysxMeshPreparationException(
                    $"Collider '{item.Path}' exceeds the bounded geometry preparation input capacity.");
            }
            string key = GeometryKey(page, item, decomposition);
            if (cache.TryGetValue(key, out PreparedCollider? reused))
            {
                prepared.Add(index, reused);
                continue;
            }
            if (pending.TryGetValue(key, out int pendingIndex))
            {
                aliases[pendingIndex].Add(index);
                continue;
            }
            if (inputs.Count == PhysxMeshPreparation.MaxBatch ||
                points.Count + item.PointCount > PhysxMeshPreparation.MaxPoints ||
                indices.Count + triangleCount > PhysxMeshPreparation.MaxIndices)
            {
                flush();
            }
            if (!TryReadMeshData(page, item, decomposition ? PhysxShapeType.TriangleMesh : PhysxShapeType.ConvexMesh,
                    out PhysxVec3f[] sourcePoints, out uint[]? sourceIndices, out string failure, minimumPoints: 3))
            {
                prepared.Add(index, new PreparedCollider([], failure));
                continue;
            }
            if (decomposition && sourcePoints.Length < 4)
            {
                prepared.Add(index, new PreparedCollider([], "declares fewer than four convex collision points"));
                continue;
            }
            objects.Add(index);
            pending.Add(key, inputs.Count);
            keys.Add(key);
            aliases.Add([index]);
            inputs.Add(new PhysxMeshInput
            {
                Approximation = decomposition ? 2u : 1u,
                PointOffset = (uint)points.Count,
                PointCount = (uint)sourcePoints.Length,
                IndexOffset = (uint)indices.Count,
                IndexCount = (uint)(sourceIndices?.Length ?? 0),
            });
            points.AddRange(sourcePoints);
            if (sourceIndices is not null)
            {
                indices.AddRange(sourceIndices);
            }

        }
        flush();
        return prepared;

        void flush()
        {
            if (inputs.Count == 0)
            {
                return;
            }
            PhysxMeshHull[] hulls;
            PhysxVec3f[] output;
            try
            {
                (hulls, output) = PhysxMeshPreparation.Compute(
                    CollectionsMarshal.AsSpan(inputs), CollectionsMarshal.AsSpan(points),
                    CollectionsMarshal.AsSpan(indices), cancellationToken);
            }
            catch (PhysxMeshPreparationException exception)
            {
                throw new PhysxMeshPreparationException(
                    $"Collider batch starting at '{page.GetObject(objects[0]).Path}': {exception.Message}", exception);
            }
            for (int input = 0; input < inputs.Count; input++)
            {
                var ranges = new List<ReadOnlyMemory<PhysxVec3f>>();
                foreach (PhysxMeshHull hull in hulls)
                {
                    if (hull.InputIndex == input)
                    {
                        ranges.Add(output.AsMemory((int)hull.PointOffset, (int)hull.PointCount));
                    }
                }
                if (ranges.Count == 0 || ranges.Count > (inputs[input].Approximation == 1 ? 1 : 256))
                {
                    throw new PhysxMeshPreparationException(
                        $"Collider '{page.GetObject(objects[input]).Path}' has no complete prepared hull set.");
                }
                var geometry = new PreparedCollider(ranges.ToArray());
                cache.Add(keys[input], geometry);
                foreach (int alias in aliases[input])
                {
                    prepared.Add(alias, geometry);
                }
            }
            objects.Clear();
            inputs.Clear();
            points.Clear();
            indices.Clear();
            keys.Clear();
            aliases.Clear();
            pending.Clear();
        }
    }

    internal static string GeometryKey(UsdPhysicsExtractionPage page, UsdPhysicsExtractionObject item, bool decomposition)
    {
        // Cache scope fixes the SDK and cooking implementation; the key carries every varying input.
        using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Span<byte> header = stackalloc byte[24];
        BinaryPrimitives.WriteInt64LittleEndian(header, BitConverter.DoubleToInt64Bits(page.MetersPerUnit));
        BinaryPrimitives.WriteInt32LittleEndian(header[8..], item.PointCount);
        BinaryPrimitives.WriteInt32LittleEndian(header[12..], decomposition ? item.IndexCount : 0);
        BinaryPrimitives.WriteInt32LittleEndian(header[16..], decomposition ? 2 : 1);
        BinaryPrimitives.WriteUInt32LittleEndian(header[20..], PhysxAbi.Version);
        hash.AppendData(header);
        hash.AppendData(page.SectionBytes(PhysicsExtractAbi.SectionPoints)
            .Slice(item.PointStart * PhysicsExtractAbi.PointRecordBytes, item.PointCount * PhysicsExtractAbi.PointRecordBytes));
        if (decomposition)
        {
            hash.AppendData(page.SectionBytes(PhysicsExtractAbi.SectionIndices)
                .Slice(item.IndexStart * PhysicsExtractAbi.IndexRecordBytes, item.IndexCount * PhysicsExtractAbi.IndexRecordBytes));
        }
        Span<byte> digest = stackalloc byte[32];
        hash.GetHashAndReset(digest);
        return Convert.ToHexString(digest);
    }

    private static PreparedCollider ReadPreparedCollider(UsdPhysicsExtractionPage page, UsdPhysicsExtractionObject item)
    {
        var hulls = new ReadOnlyMemory<PhysxVec3f>[item.HullCount];
        for (int hull = 0; hull < hulls.Length; hull++)
        {
            (int first, int count) = page.GetHull(item.HullStart + hull);
            var points = new PhysxVec3f[count];
            for (int point = 0; point < count; point++)
            {
                (float x, float y, float z) = page.GetPoint(first + point);
                points[point] = new PhysxVec3f(x, y, z);
            }
            hulls[hull] = points;
        }
        return new PreparedCollider(hulls);
    }
}
