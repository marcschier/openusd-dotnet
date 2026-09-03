// Copyright (c) marcschier. Licensed under the MIT License.

using System.Buffers.Binary;
using OpenUsd.Rendering.Silk;

namespace OpenUsd.Rendering.Tests;

/// <summary>
/// Builds a retained deformation rig by encoding an ABI v20 deformation block
/// and decoding it through the production decoder.
/// </summary>
/// <remarks>
/// Encoding and decoding means a fixture exercises the same bytes, the same
/// bounds checks and the same identity recomputation a real page goes through,
/// so a test cannot pass against a rig shape the parser would have refused.
/// </remarks>
internal static class SilkDeformationRigFixture
{
    internal static SilkMeshDeformationData Build(
        float[] bindPoints,
        float[]? bindNormals,
        int influencesPerPoint,
        uint[] jointIndices,
        float[] jointWeights,
        float[] jointMatrices,
        float[] geomBindTransform,
        (uint First, uint Count, float Weight)[]? blendRanges = null,
        uint[]? blendDeltaPoints = null,
        float[]? blendDeltaPositions = null,
        float[]? blendDeltaNormals = null)
    {
        (uint First, uint Count, float Weight)[] ranges = blendRanges ?? [];
        uint[] deltaPoints = blendDeltaPoints ?? [];
        float[] deltaPositions = blendDeltaPositions ?? [];
        float[] deltaNormals = blendDeltaNormals ?? [];
        float[] normals = bindNormals ?? [];

        int pointCount = bindPoints.Length / 3;
        int jointCount = jointMatrices.Length / 16;
        int size = 96 +
            (bindPoints.Length * sizeof(float)) +
            (normals.Length * sizeof(float)) +
            (jointIndices.Length * sizeof(uint)) +
            (jointWeights.Length * sizeof(float)) +
            (jointMatrices.Length * sizeof(float)) +
            (ranges.Length * 16) +
            (deltaPoints.Length * 28);

        byte[] block = new byte[size];
        BinaryPrimitives.WriteUInt32LittleEndian(block, (uint)jointCount);
        BinaryPrimitives.WriteUInt32LittleEndian(block.AsSpan(4), (uint)influencesPerPoint);
        BinaryPrimitives.WriteUInt32LittleEndian(block.AsSpan(8), (uint)pointCount);
        BinaryPrimitives.WriteUInt32LittleEndian(block.AsSpan(12), (uint)ranges.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(block.AsSpan(16), (uint)deltaPoints.Length);

        int cursor = 32;
        cursor = WriteFloats(block, cursor, geomBindTransform);
        cursor = WriteFloats(block, cursor, bindPoints);
        cursor = WriteFloats(block, cursor, normals);
        foreach (uint joint in jointIndices)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(block.AsSpan(cursor), joint);
            cursor += sizeof(uint);
        }
        cursor = WriteFloats(block, cursor, jointWeights);
        cursor = WriteFloats(block, cursor, jointMatrices);
        foreach ((uint first, uint count, float weight) in ranges)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(block.AsSpan(cursor), first);
            BinaryPrimitives.WriteUInt32LittleEndian(block.AsSpan(cursor + 4), count);
            BinaryPrimitives.WriteSingleLittleEndian(block.AsSpan(cursor + 8), weight);
            cursor += 16;
        }
        for (int delta = 0; delta < deltaPoints.Length; delta++)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(block.AsSpan(cursor), deltaPoints[delta]);
            for (int component = 0; component < 3; component++)
            {
                BinaryPrimitives.WriteSingleLittleEndian(
                    block.AsSpan(cursor + 4 + (component * sizeof(float))),
                    deltaPositions[(delta * 3) + component]);
                BinaryPrimitives.WriteSingleLittleEndian(
                    block.AsSpan(cursor + 16 + (component * sizeof(float))),
                    deltaNormals[(delta * 3) + component]);
            }
            cursor += 28;
        }

        ulong identity = 14695981039346656037UL;
        for (int offset = 32; offset < cursor; offset++)
        {
            identity ^= block[offset];
            identity *= 1099511628211UL;
        }
        BinaryPrimitives.WriteUInt64LittleEndian(block.AsSpan(24), identity);

        SilkDeformationOptions options = bindNormals is null
            ? SilkDeformationOptions.None
            : SilkDeformationOptions.BindNormals;
        foreach (float offset in deltaNormals)
        {
            if (offset != 0)
            {
                options |= SilkDeformationOptions.BlendNormalOffsets;
                break;
            }
        }
        return SilkMeshDeformationData.Decode(
            block,
            options,
            SilkDeformationUnsupportedFeatures.None);
    }

    private static int WriteFloats(byte[] block, int cursor, float[] values)
    {
        foreach (float value in values)
        {
            BinaryPrimitives.WriteSingleLittleEndian(block.AsSpan(cursor), value);
            cursor += sizeof(float);
        }
        return cursor;
    }
}
