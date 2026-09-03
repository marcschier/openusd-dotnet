// Copyright (c) marcschier. Licensed under the MIT License.

using OpenUsd.Rendering.Silk;

namespace OpenUsd.Rendering.Tests;

/// <summary>
/// Pins the policy that decides whether a deformed prim reaches the GPU path,
/// and the shape of the payload it builds when it does.
/// </summary>
/// <remarks>
/// Every refusal here means the CPU-resolved points are drawn instead, which is
/// exactly what happened before a GPU path existed. The policy is tested
/// separately from the kernel because a wrong refusal is invisible -- the image
/// is still correct -- so nothing else would notice a rig that quietly stopped
/// being eligible, or one that became eligible when it should not have.
/// </remarks>
public sealed class SilkDeformationGpuPolicyTests
{
    [Test]
    public async Task ASupportedRigBuildsAPayloadThatMatchesItsCounts()
    {
        SilkMeshDeformationData rig = Rig();
        SilkDeformationGpuFallback fallback = SilkDeformationGpuPayload.TryBuild(
            rig,
            vertexStrideFloats: 6,
            pointCount: rig.BindPointCount,
            hasTangents: false,
            SilkTopologyKind.TriangleList,
            out SilkDeformationGpuPayload? payload);

        await Assert.That(fallback).IsEqualTo(SilkDeformationGpuFallback.None);
        await Assert.That(payload).IsNotNull();
        await Assert.That(payload!.Identity).IsEqualTo(rig.Identity);
        await Assert.That(payload.PointCount).IsEqualTo((uint)rig.BindPointCount);
        await Assert.That(payload.VertexStrideFloats).IsEqualTo(6U);
        // Four floats per bind position and four per bind normal.
        await Assert.That(payload.BindPose.Length).IsEqualTo(rig.BindPointCount * 8);
        // Four rows for the geom bind transform, four for its inverse transpose,
        // then four rows per joint twice over.
        await Assert.That(payload.Matrices.Length)
            .IsEqualTo((8 + (rig.JointCount * 8)) * 4);
        await Assert.That(payload.Parameters.Length)
            .IsEqualTo((int)SilkDeformComputeReflection.ParameterByteSize);
    }

    [Test]
    public async Task ARigWithoutBindNormalsFallsBack()
    {
        // The vertex builder derives normals from topology when a mesh publishes
        // none, and no per-point kernel can reproduce a gather over adjacent
        // triangles, so the whole prim stays on the CPU.
        SilkMeshDeformationData rig = Rig(withBindNormals: false);
        SilkDeformationGpuFallback fallback = SilkDeformationGpuPayload.TryBuild(
            rig,
            6,
            rig.BindPointCount,
            hasTangents: false,
            SilkTopologyKind.TriangleList,
            out SilkDeformationGpuPayload? payload);

        await Assert.That(fallback).IsEqualTo(SilkDeformationGpuFallback.NoBindNormals);
        await Assert.That(payload).IsNull();
    }

    [Test]
    public async Task AGeometryThatNeedsTangentsFallsBack()
    {
        // A tangent is derived from deformed positions and a texture coordinate
        // set rather than deformed per point, so a normal-mapped prim would get
        // bind-pose tangents on a moved surface if the kernel ran.
        SilkMeshDeformationData rig = Rig();
        SilkDeformationGpuFallback fallback = SilkDeformationGpuPayload.TryBuild(
            rig,
            12,
            rig.BindPointCount,
            hasTangents: true,
            SilkTopologyKind.TriangleList,
            out _);

        await Assert.That(fallback).IsEqualTo(SilkDeformationGpuFallback.RequiresTangents);
    }

    [Test]
    [Arguments(SilkTopologyKind.LineList)]
    [Arguments(SilkTopologyKind.PointList)]
    public async Task ANonTriangleTopologyFallsBack(SilkTopologyKind topologyKind)
    {
        SilkMeshDeformationData rig = Rig();
        SilkDeformationGpuFallback fallback = SilkDeformationGpuPayload.TryBuild(
            rig,
            6,
            rig.BindPointCount,
            hasTangents: false,
            topologyKind,
            out _);

        await Assert.That(fallback)
            .IsEqualTo(SilkDeformationGpuFallback.UnsupportedTopology);
    }

    [Test]
    public async Task APointCountThatDisagreesWithTheRigFallsBack()
    {
        // A refined or expanded topology emits points the influences do not
        // address, and the producer already refuses to publish a rig for one.
        // The host refuses again rather than trusting that.
        SilkMeshDeformationData rig = Rig();
        SilkDeformationGpuFallback fallback = SilkDeformationGpuPayload.TryBuild(
            rig,
            6,
            rig.BindPointCount + 1,
            hasTangents: false,
            SilkTopologyKind.TriangleList,
            out _);

        await Assert.That(fallback)
            .IsEqualTo(SilkDeformationGpuFallback.PointCountMismatch);
    }

    [Test]
    public async Task AMissingRigFallsBack()
    {
        SilkDeformationGpuFallback fallback = SilkDeformationGpuPayload.TryBuild(
            null,
            6,
            3,
            hasTangents: false,
            SilkTopologyKind.TriangleList,
            out SilkDeformationGpuPayload? payload);

        await Assert.That(fallback).IsEqualTo(SilkDeformationGpuFallback.NoPublishedRig);
        await Assert.That(payload).IsNull();
    }

    [Test]
    public async Task ARigPastTheGpuByteBudgetFallsBackBeforeAllocating()
    {
        // The GPU payload is a different size from the published block: the
        // influences are re-laid out and the matrix table carries the
        // precomputed normal matrices too. A rig inside every wire budget can
        // therefore still be outside this one, and the refusal has to happen
        // from the counts rather than after the arrays exist.
        int points = 3_000_000;
        SilkMeshDeformationData rig = Rig(pointCount: points, influencesPerPoint: 8);
        SilkDeformationGpuFallback fallback = SilkDeformationGpuPayload.TryBuild(
            rig,
            6,
            points,
            hasTangents: false,
            SilkTopologyKind.TriangleList,
            out SilkDeformationGpuPayload? payload);

        await Assert.That(fallback).IsEqualTo(SilkDeformationGpuFallback.ByteBudget);
        await Assert.That(payload).IsNull();
    }

    [Test]
    public async Task TheBlendRegroupingPreservesRangeThenDeltaOrder()
    {
        // The CPU oracle scatters deltas range by range and delta by delta; the
        // GPU gathers them per point. Floating-point addition is not
        // associative, so the gathered run for a point has to hold the same
        // terms in the same order. Two ranges are made to address one point in
        // a deliberately interleaved layout so a regrouping that sorted by
        // delta index alone would come out backwards.
        SilkMeshDeformationData rig = SilkDeformationRigFixture.Build(
            bindPoints: [0, 0, 0, 1, 1, 1],
            bindNormals: [0, 0, 1, 0, 0, 1],
            influencesPerPoint: 1,
            jointIndices: [0, 0],
            jointWeights: [1, 1],
            jointMatrices: Identity(),
            geomBindTransform: Identity(),
            blendRanges: [(0, 2, 2.0f), (2, 1, 3.0f)],
            blendDeltaPoints: [1, 0, 0],
            blendDeltaPositions: [9, 0, 0, 5, 0, 0, 7, 0, 0],
            blendDeltaNormals: new float[9]);

        _ = SilkDeformationGpuPayload.TryBuild(
            rig,
            6,
            rig.BindPointCount,
            hasTangents: false,
            SilkTopologyKind.TriangleList,
            out SilkDeformationGpuPayload? payload);

        // Point zero receives range zero's second delta first, then range one's
        // only delta, which is the CPU scatter order.
        await Assert.That(payload!.BlendSpans[0]).IsEqualTo(0U);
        await Assert.That(payload.BlendSpans[1]).IsEqualTo(2U);
        await Assert.That(payload.BlendDeltas[0]).IsEqualTo(5.0f);
        await Assert.That(
            BitConverter.SingleToInt32Bits(payload.BlendDeltas[3])).IsEqualTo(0);
        await Assert.That(payload.BlendDeltas[8]).IsEqualTo(7.0f);
        await Assert.That(
            BitConverter.SingleToInt32Bits(payload.BlendDeltas[11])).IsEqualTo(1);

        // Point one receives range zero's first delta.
        await Assert.That(payload.BlendSpans[2]).IsEqualTo(2U);
        await Assert.That(payload.BlendSpans[3]).IsEqualTo(1U);
        await Assert.That(payload.BlendDeltas[16]).IsEqualTo(9.0f);
    }

    [Test]
    public async Task OverlappingBlendRangesAreChargedForEveryDeltaTheyGather()
    {
        // A range addresses a span of the delta array; nothing stops two ranges
        // addressing the same span. The gathered array then holds one entry per
        // (range, delta) pair rather than one per stored delta, so the budget
        // has to be charged the sum of the ranges' counts. Two ranges over the
        // same two deltas gather four.
        SilkMeshDeformationData rig = SilkDeformationRigFixture.Build(
            bindPoints: [0, 0, 0, 1, 1, 1],
            bindNormals: [0, 0, 1, 0, 0, 1],
            influencesPerPoint: 1,
            jointIndices: [0, 0],
            jointWeights: [1, 1],
            jointMatrices: Identity(),
            geomBindTransform: Identity(),
            blendRanges: [(0, 2, 1.0f), (0, 2, 1.0f)],
            blendDeltaPoints: [0, 1],
            blendDeltaPositions: [1, 0, 0, 2, 0, 0],
            blendDeltaNormals: new float[6]);

        SilkDeformationGpuFallback fallback = SilkDeformationGpuPayload.TryBuild(
            rig,
            6,
            rig.BindPointCount,
            hasTangents: false,
            SilkTopologyKind.TriangleList,
            out SilkDeformationGpuPayload? payload);

        await Assert.That(fallback).IsEqualTo(SilkDeformationGpuFallback.None);
        // Two stored deltas, four gathered ones: the array the kernel indexes is
        // twice the stored array, which is exactly what the budget must charge.
        await Assert.That(rig.BlendDeltaPoints.Length).IsEqualTo(2);
        await Assert.That(payload!.BlendDeltaCount).IsEqualTo(4U);
        await Assert.That(payload.BlendDeltas.Length).IsEqualTo(4 * 8);
        await Assert.That(payload.BlendSpans[1]).IsEqualTo(2U);
        await Assert.That(payload.BlendSpans[3]).IsEqualTo(2U);
    }

    [Test]
    public async Task MaliciouslyOverlappedBlendRangesFallBackBeforeAllocating()
    {
        // Every count here is inside its wire budget: sixty-four ranges is the
        // maximum the ABI permits and sixty thousand stored deltas is far under
        // the million it permits. What is not bounded by any wire budget is
        // their product -- each range addresses the whole delta array -- so the
        // gathered array would hold 3,840,000 entries, over thirty megabytes of
        // deltas alone, from a block under two megabytes. Charging only the
        // stored deltas would have let this through at one sixty-fourth of its
        // real cost.
        const int storedDeltas = 60_000;
        var ranges = new (uint First, uint Count, float Weight)[
            SilkDeformationLimits.MaximumBlendRanges];
        for (int range = 0; range < ranges.Length; range++)
        {
            ranges[range] = (0, storedDeltas, 1.0f);
        }
        uint[] deltaPoints = new uint[storedDeltas];
        SilkMeshDeformationData rig = SilkDeformationRigFixture.Build(
            bindPoints: [0, 0, 0, 1, 1, 1],
            bindNormals: [0, 0, 1, 0, 0, 1],
            influencesPerPoint: 1,
            jointIndices: [0, 0],
            jointWeights: [1, 1],
            jointMatrices: Identity(),
            geomBindTransform: Identity(),
            blendRanges: ranges,
            blendDeltaPoints: deltaPoints,
            blendDeltaPositions: new float[storedDeltas * 3],
            blendDeltaNormals: new float[storedDeltas * 3]);

        long gathered = (long)ranges.Length * storedDeltas;
        long storedBytes = (long)rig.BlendDeltaPoints.Length * 32;
        await Assert.That(gathered)
            .IsGreaterThan(SilkDeformationGpuPayload.MaximumGatheredDeltas);
        // The stored deltas alone are comfortably inside the byte budget, so a
        // budget charged from them would not have refused this rig.
        await Assert.That(storedBytes)
            .IsLessThan(SilkDeformationGpuPayload.MaximumByteCount);

        SilkDeformationGpuFallback fallback = SilkDeformationGpuPayload.TryBuild(
            rig,
            6,
            rig.BindPointCount,
            hasTangents: false,
            SilkTopologyKind.TriangleList,
            out SilkDeformationGpuPayload? payload);

        await Assert.That(fallback).IsEqualTo(SilkDeformationGpuFallback.ByteBudget);
        await Assert.That(payload).IsNull();
    }

    [Test]
    public async Task TheGatheredDeltaBoundKeepsEveryDerivedArrayRepresentable()
    {
        // The gathered bound is what stops the sum overflowing and what keeps
        // the eight-float delta array inside an int-indexed allocation, so both
        // relationships are pinned rather than left to the constant's value.
        long gatheredBytes = SilkDeformationGpuPayload.MaximumGatheredDeltas * 32;
        long gatheredFloats = SilkDeformationGpuPayload.MaximumGatheredDeltas * 8;
        await Assert.That(gatheredBytes)
            .IsLessThanOrEqualTo(SilkDeformationGpuPayload.MaximumByteCount);
        await Assert.That(gatheredFloats).IsLessThan(int.MaxValue);

        // The sum itself cannot overflow before the bound catches it: the ABI
        // permits at most sixty-four ranges of at most a million deltas each,
        // and that product is orders of magnitude inside a signed 64-bit sum.
        long worstCase = (long)SilkDeformationLimits.MaximumBlendRanges *
            SilkDeformationLimits.MaximumBlendDeltas;
        await Assert.That(worstCase).IsLessThan(long.MaxValue / 32);
        await Assert.That(worstCase)
            .IsGreaterThan(SilkDeformationGpuPayload.MaximumGatheredDeltas);
    }

    private static SilkMeshDeformationData Rig(
        bool withBindNormals = true,
        int pointCount = 3,
        int influencesPerPoint = 1)
    {
        float[] bindPoints = new float[pointCount * 3];
        float[] bindNormals = new float[pointCount * 3];
        uint[] jointIndices = new uint[pointCount * influencesPerPoint];
        float[] jointWeights = new float[pointCount * influencesPerPoint];
        for (int point = 0; point < pointCount; point++)
        {
            bindPoints[point * 3] = point;
            bindNormals[(point * 3) + 2] = 1;
            jointWeights[point * influencesPerPoint] = 1;
        }
        return SilkDeformationRigFixture.Build(
            bindPoints,
            withBindNormals ? bindNormals : null,
            influencesPerPoint,
            jointIndices,
            jointWeights,
            Identity(),
            Identity());
    }

    private static float[] Identity() =>
    [
        1, 0, 0, 0,
        0, 1, 0, 0,
        0, 0, 1, 0,
        0, 0, 0, 1
    ];
}
