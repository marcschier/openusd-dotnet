// Copyright (c) marcschier. Licensed under the MIT License.

using System.Buffers.Binary;

namespace OpenUsd.Rendering.Silk;

/// <summary>
/// The checked GPU deformation kernel's binding layout and dispatch shape.
/// </summary>
/// <param name="Layout">The ordered slot table the kernel declares.</param>
/// <param name="ThreadGroupSizeX">The kernel's thread-group width.</param>
/// <param name="ThreadGroupSizeY">The kernel's thread-group height.</param>
/// <param name="ThreadGroupSizeZ">The kernel's thread-group depth.</param>
public readonly record struct SilkDeformComputeReflection(
    SilkComputeBindingLayoutDescriptor Layout,
    uint ThreadGroupSizeX,
    uint ThreadGroupSizeY,
    uint ThreadGroupSizeZ)
{
    /// <summary>Gets the byte size of the kernel's parameter block.</summary>
    public const uint ParameterByteSize = 32;

    /// <summary>Gets the binding of the writable interleaved vertex buffer.</summary>
    public const uint VerticesBinding = 0;

    /// <summary>Gets the binding of the bind-pose position and normal pairs.</summary>
    public const uint BindPoseBinding = 1;

    /// <summary>Gets the binding of the flattened joint index stream.</summary>
    public const uint JointIndicesBinding = 2;

    /// <summary>Gets the binding of the flattened joint weight stream.</summary>
    public const uint JointWeightsBinding = 3;

    /// <summary>Gets the binding of the row-major matrix table.</summary>
    public const uint MatricesBinding = 4;

    /// <summary>Gets the binding of the resolved sub-shape weights.</summary>
    public const uint BlendWeightsBinding = 5;

    /// <summary>Gets the binding of the per-point blend delta spans.</summary>
    public const uint BlendSpansBinding = 6;

    /// <summary>Gets the binding of the gathered blend deltas.</summary>
    public const uint BlendDeltasBinding = 7;

    /// <summary>Gets the binding of the emitted texture coordinates.</summary>
    public const uint TexCoordsBinding = 8;

    /// <summary>Gets the binding of the parameter block.</summary>
    public const uint ParametersBinding = 9;
}

/// <summary>Why a deformed prim is not eligible for the GPU deformation pass.</summary>
/// <remarks>
/// Every value here means the same thing: hdSilk's CPU-resolved points are drawn
/// instead, exactly as they were before a GPU path existed. None of them is an
/// error, and none of them may be silently swallowed -- a prim that fell back
/// without a named reason is indistinguishable from one that was never eligible.
/// </remarks>
public enum SilkDeformationGpuFallback
{
    /// <summary>The prim is deformed on the GPU.</summary>
    None = 0,

    /// <summary>The record published no bounded rig.</summary>
    NoPublishedRig = 1,

    /// <summary>
    /// The rig carries no bind normals, so the vertex builder derives normals
    /// from topology and no per-point kernel can reproduce that.
    /// </summary>
    NoBindNormals = 2,

    /// <summary>
    /// The geometry carries tangents, which are derived from deformed positions
    /// and a texture coordinate set rather than deformed per point.
    /// </summary>
    RequiresTangents = 3,

    /// <summary>The emitted topology is not an indexed triangle list.</summary>
    UnsupportedTopology = 4,

    /// <summary>The rig's bind points do not match the emitted points.</summary>
    PointCountMismatch = 5,

    /// <summary>The rig would exceed the GPU byte budget for one prim.</summary>
    ByteBudget = 6,

    /// <summary>The device reports no compute capability.</summary>
    NoComputeCapability = 7,

    /// <summary>A GPU allocation or dispatch failed and the pass gave up.</summary>
    AllocationFailed = 8,

    /// <summary>
    /// The prim's material authors a displacement that moves the surface, and the
    /// checked kernel writes no displaced position.
    /// </summary>
    /// <remarks>
    /// Displacement is defined on the deformed surface, so it has to be applied
    /// after skinning rather than to a bind pose. The checked ABI v20 kernel
    /// writes the skinned position and normal and nothing else, so a displaced
    /// rig draws hdSilk's CPU-resolved points -- the same authoritative points the
    /// kernel is held to reproduce -- with the amounts applied on top of them.
    /// That keeps the ordering this renderer claims, deform then displace, exact
    /// and identical to the CPU deformation oracle.
    /// </remarks>
    MaterialDisplacement = 9
}

/// <summary>
/// The bounded, backend-neutral GPU inputs one deformed prototype needs.
/// </summary>
/// <remarks>
/// <para>
/// This is the whole host half of the ABI v20 GPU path. It turns the published
/// rig into the nine buffers the checked kernel declares, once per deformation
/// identity, and every array is produced in one bulk pass: nothing here crosses
/// a native boundary, and nothing is written one element at a time from managed
/// code into GPU memory.
/// </para>
/// <para>
/// Two transformations happen here rather than in the kernel, both because the
/// CPU oracle does them the same way and agreement matters more than symmetry.
/// The inverse-transpose normal matrices are computed in double precision by
/// the same arithmetic <see cref="SilkDeformationEvaluator"/> uses, so a
/// near-singular joint cannot be inverted two different ways. The sparse blend
/// deltas are regrouped per point, preserving their (range, delta) order, so a
/// point accumulates its contributions in exactly the order the CPU scatter
/// produced them -- floating-point addition is not associative, and a regrouping
/// that reordered them would drift from the oracle by more than the tolerance
/// on a rig with many overlapping shapes.
/// </para>
/// </remarks>
public sealed class SilkDeformationGpuPayload
{
    private SilkDeformationGpuPayload(
        ulong identity,
        uint pointCount,
        uint influencesPerPoint,
        uint vertexStrideFloats,
        uint jointCount,
        uint blendDeltaCount,
        float[] bindPose,
        uint[] jointIndices,
        float[] jointWeights,
        float[] matrices,
        float[] blendWeights,
        uint[] blendSpans,
        float[] blendDeltas,
        float[] texCoords,
        byte[] parameters)
    {
        Identity = identity;
        PointCount = pointCount;
        InfluencesPerPoint = influencesPerPoint;
        VertexStrideFloats = vertexStrideFloats;
        JointCount = jointCount;
        BlendDeltaCount = blendDeltaCount;
        BindPose = bindPose;
        JointIndices = jointIndices;
        JointWeights = jointWeights;
        Matrices = matrices;
        BlendWeights = blendWeights;
        BlendSpans = blendSpans;
        BlendDeltas = blendDeltas;
        TexCoords = texCoords;
        Parameters = parameters;
    }

    /// <summary>Gets the largest number of bytes one prim's GPU rig may occupy.</summary>
    /// <remarks>
    /// The ABI already bounds the published block, but the GPU payload is a
    /// different size: influences and blend deltas are re-laid out for aligned
    /// access, and the matrix table carries the precomputed normal matrices as
    /// well. The bound is checked from the counts before a single array is
    /// allocated, so a rig inside every wire budget but outside this one falls
    /// back rather than allocating hundreds of megabytes.
    /// </remarks>
    public const long MaximumByteCount = 96L * 1024 * 1024;

    /// <summary>
    /// The largest number of gathered blend deltas one prototype may produce.
    /// </summary>
    /// <remarks>
    /// A gathered delta occupies eight floats in <see cref="BlendDeltas"/>, so
    /// this bound is what keeps that array's element count inside
    /// <see cref="int.MaxValue"/> as well as inside
    /// <see cref="MaximumByteCount"/>. It is enforced while the ranges are
    /// summed rather than afterwards, so a page whose ranges overlap enough to
    /// overflow the sum itself is refused before the sum can wrap.
    /// </remarks>
    public const long MaximumGatheredDeltas = MaximumByteCount / 32;

    /// <summary>Gets the deformation identity this payload was built from.</summary>
    public ulong Identity { get; }

    /// <summary>Gets the number of deformed points.</summary>
    public uint PointCount { get; }

    /// <summary>Gets the fixed influence stream width.</summary>
    public uint InfluencesPerPoint { get; }

    /// <summary>Gets the interleaved vertex stride in floats.</summary>
    public uint VertexStrideFloats { get; }

    /// <summary>Gets the number of joints in the palette.</summary>
    public uint JointCount { get; }

    /// <summary>Gets the number of gathered blend deltas.</summary>
    public uint BlendDeltaCount { get; }

    /// <summary>Gets the bind-pose position and normal pairs, four floats each.</summary>
    public float[] BindPose { get; }

    /// <summary>Gets the flattened joint index stream.</summary>
    public uint[] JointIndices { get; }

    /// <summary>Gets the flattened joint weight stream.</summary>
    public float[] JointWeights { get; }

    /// <summary>Gets the row-major matrix table.</summary>
    public float[] Matrices { get; }

    /// <summary>Gets the resolved sub-shape weights.</summary>
    public float[] BlendWeights { get; }

    /// <summary>Gets the per-point delta spans, two uints each.</summary>
    public uint[] BlendSpans { get; }

    /// <summary>Gets the gathered blend deltas, eight floats each.</summary>
    public float[] BlendDeltas { get; }

    /// <summary>Gets the emitted texture coordinates, two floats per point.</summary>
    public float[] TexCoords { get; }

    /// <summary>Gets the kernel's parameter block.</summary>
    public byte[] Parameters { get; }

    /// <summary>
    /// Decides whether a rig can be deformed by the checked kernel and, when it
    /// can, builds the buffers it needs.
    /// </summary>
    /// <param name="deformation">The published rig, or <see langword="null"/>.</param>
    /// <param name="vertexStrideFloats">The interleaved vertex stride in floats.</param>
    /// <param name="pointCount">The emitted point count.</param>
    /// <param name="hasTangents">Whether the geometry carries tangents.</param>
    /// <param name="topologyKind">The emitted topology.</param>
    /// <param name="payload">The built payload, or <see langword="null"/>.</param>
    /// <param name="texCoords">
    /// Two floats per point when the emitted vertex carries a texture
    /// coordinate set, empty otherwise. The kernel produces the whole vertex,
    /// so a layout with coordinates must supply them.
    /// </param>
    /// <returns>The reason the prim falls back, or None when it does not.</returns>
    public static SilkDeformationGpuFallback TryBuild(
        SilkMeshDeformationData? deformation,
        uint vertexStrideFloats,
        int pointCount,
        bool hasTangents,
        SilkTopologyKind topologyKind,
        out SilkDeformationGpuPayload? payload,
        ReadOnlySpan<float> texCoords = default)
    {
        payload = null;
        if (deformation is null)
        {
            return SilkDeformationGpuFallback.NoPublishedRig;
        }
        if (!deformation.HasBindNormals)
        {
            return SilkDeformationGpuFallback.NoBindNormals;
        }
        if (hasTangents)
        {
            return SilkDeformationGpuFallback.RequiresTangents;
        }
        if (topologyKind != SilkTopologyKind.TriangleList)
        {
            return SilkDeformationGpuFallback.UnsupportedTopology;
        }
        if (pointCount <= 0 || deformation.BindPointCount != pointCount)
        {
            return SilkDeformationGpuFallback.PointCountMismatch;
        }
        if (vertexStrideFloats < 6)
        {
            return SilkDeformationGpuFallback.UnsupportedTopology;
        }

        int points = deformation.BindPointCount;
        int influences = deformation.InfluencesPerPoint;
        int joints = deformation.JointCount;
        // The gathered delta count is the sum of every range's DeltaCount, not
        // the number of stored deltas: ranges may address overlapping spans of
        // the same delta array, so one stored delta can be gathered once per
        // range that references it. Sizing the budget from the stored array
        // would let a page with sixty-four ranges over one million shared
        // deltas charge one thirty-second of what it actually allocates.
        long gathered = 0;
        foreach (SilkDeformationBlendRange range in deformation.BlendRanges)
        {
            gathered += range.DeltaCount;
            if (gathered > MaximumGatheredDeltas)
            {
                return SilkDeformationGpuFallback.ByteBudget;
            }
        }
        // Sized from the counts before anything is allocated, so a rig inside
        // every wire budget but outside this one never reaches an allocator.
        long bytes;
        try
        {
            checked
            {
                bytes =
                    ((long)points * 2 * 16) +
                    ((long)points * influences * 4) +
                    ((long)points * influences * 4) +
                    ((8L + ((long)joints * 8)) * 16) +
                    ((long)deformation.BlendRanges.Count * 4) +
                    ((long)points * 8) +
                    (gathered * 32) +
                    SilkDeformComputeReflection.ParameterByteSize;
            }
        }
        catch (OverflowException)
        {
            return SilkDeformationGpuFallback.ByteBudget;
        }
        if (bytes > MaximumByteCount)
        {
            return SilkDeformationGpuFallback.ByteBudget;
        }

        if (vertexStrideFloats >= 8 && texCoords.Length != points * 2)
        {
            // The kernel writes the whole vertex, so a layout that carries a
            // texture coordinate set must supply one per point. A record that
            // could not resolve it keeps drawing the CPU-built vertices, which
            // still carry the coordinates the material samples through.
            return SilkDeformationGpuFallback.UnsupportedTopology;
        }
        payload = Build(
            deformation,
            vertexStrideFloats,
            points,
            influences,
            joints,
            checked((int)gathered),
            texCoords);
        return SilkDeformationGpuFallback.None;
    }

    private static SilkDeformationGpuPayload Build(
        SilkMeshDeformationData deformation,
        uint vertexStrideFloats,
        int points,
        int influences,
        int joints,
        int gathered,
        ReadOnlySpan<float> texCoords)
    {
        ReadOnlySpan<float> bindPoints = deformation.BindPoints.Span;
        ReadOnlySpan<float> bindNormals = deformation.BindNormals.Span;
        float[] bindPose = new float[points * 8];
        for (int point = 0; point < points; point++)
        {
            int source = point * 3;
            int target = point * 8;
            bindPose[target] = bindPoints[source];
            bindPose[target + 1] = bindPoints[source + 1];
            bindPose[target + 2] = bindPoints[source + 2];
            bindPose[target + 4] = bindNormals[source];
            bindPose[target + 5] = bindNormals[source + 1];
            bindPose[target + 6] = bindNormals[source + 2];
        }

        uint[] jointIndices = deformation.JointIndices.ToArray();
        float[] jointWeights = deformation.JointWeights.ToArray();

        // Rows 0..3 the geom bind transform, 4..7 its inverse transpose,
        // 8 + 4j the joints, and normalMatrixRow + 4j their inverse transposes.
        int matrixRows = 8 + (joints * 8);
        float[] matrices = new float[matrixRows * 4];
        ReadOnlySpan<float> geomBind = deformation.GeomBindTransform.Span;
        for (int element = 0; element < 16; element++)
        {
            matrices[element] = geomBind[element];
        }
        Span<float> normalMatrix = stackalloc float[9];
        SilkDeformationEvaluator.WriteInverseTranspose(geomBind, normalMatrix);
        WriteThreeByThree(matrices, 4, normalMatrix);

        ReadOnlySpan<float> palette = deformation.JointMatrices.Span;
        int normalMatrixRow = 8 + (joints * 4);
        for (int joint = 0; joint < joints; joint++)
        {
            ReadOnlySpan<float> jointMatrix = palette.Slice(joint * 16, 16);
            int row = 8 + (joint * 4);
            for (int element = 0; element < 16; element++)
            {
                matrices[(row * 4) + element] = jointMatrix[element];
            }
            SilkDeformationEvaluator.WriteInverseTranspose(jointMatrix, normalMatrix);
            WriteThreeByThree(matrices, normalMatrixRow + (joint * 4), normalMatrix);
        }

        // The deltas are regrouped per point while preserving their (range,
        // delta) order, so a point accumulates exactly the terms the CPU
        // scatter produced for it, in the same order.
        IReadOnlyList<SilkDeformationBlendRange> ranges = deformation.BlendRanges;
        float[] blendWeights = new float[Math.Max(1, ranges.Count)];
        for (int range = 0; range < ranges.Count; range++)
        {
            blendWeights[range] = ranges[range].Weight;
        }
        ReadOnlySpan<uint> deltaPoints = deformation.BlendDeltaPoints.Span;
        ReadOnlySpan<float> deltaPositions = deformation.BlendDeltaPositionOffsets.Span;
        ReadOnlySpan<float> deltaNormals = deformation.BlendDeltaNormalOffsets.Span;
        int[] counts = new int[points];
        for (int range = 0; range < ranges.Count; range++)
        {
            for (int entry = 0; entry < ranges[range].DeltaCount; entry++)
            {
                counts[(int)deltaPoints[ranges[range].FirstDelta + entry]]++;
            }
        }
        uint[] blendSpans = new uint[points * 2];
        int running = 0;
        for (int point = 0; point < points; point++)
        {
            blendSpans[point * 2] = (uint)running;
            blendSpans[(point * 2) + 1] = (uint)counts[point];
            running += counts[point];
        }
        int[] cursors = new int[points];
        // The gathered total was bounded before anything was allocated; this
        // asserts the regrouping agrees with it, so a divergence between the
        // budget and the allocation is a rejected build rather than a buffer
        // the kernel would index past.
        if (running != gathered)
        {
            throw new InvalidOperationException(
                "The gathered blend delta count disagrees with the budgeted one.");
        }
        float[] blendDeltas = new float[Math.Max(1, running) * 8];
        for (int range = 0; range < ranges.Count; range++)
        {
            for (int entry = 0; entry < ranges[range].DeltaCount; entry++)
            {
                int delta = ranges[range].FirstDelta + entry;
                int point = (int)deltaPoints[delta];
                int target = (int)(blendSpans[point * 2] + cursors[point]) * 8;
                cursors[point]++;
                blendDeltas[target] = deltaPositions[delta * 3];
                blendDeltas[target + 1] = deltaPositions[(delta * 3) + 1];
                blendDeltas[target + 2] = deltaPositions[(delta * 3) + 2];
                // The range index travels as a bit pattern rather than a float
                // value, so it stays exact for every count this ABI permits.
                blendDeltas[target + 3] = BitConverter.Int32BitsToSingle(range);
                blendDeltas[target + 4] = deltaNormals[delta * 3];
                blendDeltas[target + 5] = deltaNormals[(delta * 3) + 1];
                blendDeltas[target + 6] = deltaNormals[(delta * 3) + 2];
            }
        }

        float[] coordinates = texCoords.IsEmpty ? [0, 0] : texCoords.ToArray();

        byte[] parameters = new byte[SilkDeformComputeReflection.ParameterByteSize];
        BinaryPrimitives.WriteUInt32LittleEndian(parameters, (uint)points);
        BinaryPrimitives.WriteUInt32LittleEndian(parameters.AsSpan(4), (uint)influences);
        BinaryPrimitives.WriteUInt32LittleEndian(parameters.AsSpan(8), vertexStrideFloats);
        BinaryPrimitives.WriteUInt32LittleEndian(parameters.AsSpan(12), 1);
        BinaryPrimitives.WriteUInt32LittleEndian(parameters.AsSpan(16), (uint)joints);
        BinaryPrimitives.WriteUInt32LittleEndian(
            parameters.AsSpan(20),
            (uint)normalMatrixRow);
        BinaryPrimitives.WriteUInt32LittleEndian(parameters.AsSpan(24), (uint)running);
        BinaryPrimitives.WriteUInt32LittleEndian(parameters.AsSpan(28), 0);

        return new SilkDeformationGpuPayload(
            deformation.Identity,
            (uint)points,
            (uint)influences,
            vertexStrideFloats,
            (uint)joints,
            (uint)running,
            bindPose,
            jointIndices,
            jointWeights,
            matrices,
            blendWeights,
            blendSpans,
            blendDeltas,
            coordinates,
            parameters);
    }

    private static void WriteThreeByThree(
        float[] matrices,
        int row,
        ReadOnlySpan<float> threeByThree)
    {
        for (int line = 0; line < 3; line++)
        {
            int target = (row + line) * 4;
            matrices[target] = threeByThree[line * 3];
            matrices[target + 1] = threeByThree[(line * 3) + 1];
            matrices[target + 2] = threeByThree[(line * 3) + 2];
        }
    }
}
