// Copyright (c) marcschier. Licensed under the MIT License.

// The GPU half of the ABI v20 bounded deformation block.
//
// This kernel is held to the managed SilkDeformationEvaluator, which is itself
// held to hdSilk's own CPU deformation, so the arithmetic here mirrors that
// evaluator statement for statement rather than being written in whichever form
// reads best in HLSL. In particular:
//
//  * Matrices are read as explicit row-major rows out of a structured buffer
//    and applied by hand. No float4x4 and no mul() appear anywhere, because
//    those carry a row/column convention that differs between Slang targets and
//    between HLSL compilation flags, and a silently transposed skinning matrix
//    is exactly the class of defect a pixel gate would find hard to attribute.
//    USD composes row vectors, so a point multiplies a matrix from the left.
//  * Influences accumulate in ascending influence order, skipping zero weights,
//    because floating-point addition is not associative and the CPU oracle
//    accumulates in that order.
//  * Blend-shape deltas are gathered per point from a span the producer already
//    sorted by (range, delta), so a point sees its contributions in the same
//    order the CPU scatter produced them.
//
// The kernel writes positions and normals into the retained interleaved vertex
// buffer in place. It never writes texture coordinates or tangents: a tangent is
// derived from deformed positions and a UV set rather than deformed on its own,
// so a geometry that needs one is refused by the host and deformed on the CPU.

// Direct3D register numbers deliberately equal the Vulkan bindings, which is
// what lets one ordered slot table drive the root signature, the descriptor set
// layout and the Metal buffer indices without a per-backend mapping.

[[vk::binding(0, 0)]]
RWStructuredBuffer<float> deformedVertices : register(u0);

// Two entries per point: the bind-pose position, then the bind-pose normal.
[[vk::binding(1, 0)]]
StructuredBuffer<float4> bindPose : register(t1);

// influencesPerPoint entries per point, flattened.
[[vk::binding(2, 0)]]
StructuredBuffer<uint> jointIndices : register(t2);

[[vk::binding(3, 0)]]
StructuredBuffer<float> jointWeights : register(t3);

// Row-major rows, in one bounded table:
//   rows 0..3                              the geom bind transform
//   rows 4..6                              its inverse transpose, three-by-three
//   rows 8 + 4j .. 8 + 4j + 3              joint j
//   rows normalMatrixRow + 4j .. + 2       joint j's inverse transpose
// The inverse transposes are computed by the host, in double precision, by the
// same code path the CPU oracle uses, so the kernel and the oracle can never
// disagree about a near-singular joint.
[[vk::binding(4, 0)]]
StructuredBuffer<float4> deformMatrices : register(t4);

// One resolved sub-shape weight per blend range.
[[vk::binding(5, 0)]]
StructuredBuffer<float> blendWeights : register(t5);

// One span per point: x is the first delta, y is the delta count.
[[vk::binding(6, 0)]]
StructuredBuffer<uint2> blendSpans : register(t6);

// Two entries per delta: the position offset with its range index in w, then
// the normal offset. The range index travels as a bit pattern rather than as a
// float value so it stays exact for every count this ABI permits.
[[vk::binding(7, 0)]]
StructuredBuffer<float4> blendDeltas : register(t7);

// One entry per point when the emitted vertex carries a texture coordinate set,
// and one unused entry otherwise. The kernel produces the whole vertex rather
// than patching part of one, because the destination lives on the device heap
// and no host can seed it: Direct3D 12 refuses an unordered-access view over an
// upload allocation, and a device allocation cannot be mapped.
[[vk::binding(8, 0)]]
StructuredBuffer<float2> texCoords : register(t8);

[[vk::binding(9, 0)]]
cbuffer DeformParameters : register(b9)
{
    uint pointCount;
    uint influencesPerPoint;
    uint vertexStrideFloats;
    uint hasBindNormals;
    uint jointCount;
    uint normalMatrixRow;
    uint blendDeltaCount;
    uint deformReserved;
};

static const uint GeomBindRow = 0;
static const uint GeomBindNormalRow = 4;
static const uint JointMatrixRow = 8;

float3 DeformTransformPoint(uint row, float3 point)
{
    float4 r0 = deformMatrices[row];
    float4 r1 = deformMatrices[row + 1];
    float4 r2 = deformMatrices[row + 2];
    float4 r3 = deformMatrices[row + 3];
    return (point.x * r0.xyz) + (point.y * r1.xyz) + (point.z * r2.xyz) + r3.xyz;
}

float3 DeformTransformDirection(uint row, float3 direction)
{
    float4 r0 = deformMatrices[row];
    float4 r1 = deformMatrices[row + 1];
    float4 r2 = deformMatrices[row + 2];
    return (direction.x * r0.xyz) + (direction.y * r1.xyz) + (direction.z * r2.xyz);
}

[shader("compute")]
[numthreads(64, 1, 1)]
void deformMain(uint3 dispatchThreadId : SV_DispatchThreadID)
{
    uint pointIndex = dispatchThreadId.x;
    if (pointIndex >= pointCount)
    {
        return;
    }

    float3 bindPoint = bindPose[pointIndex * 2].xyz;
    float3 bindNormal = bindPose[(pointIndex * 2) + 1].xyz;

    float3 pointOffset = float3(0.0, 0.0, 0.0);
    float3 normalOffset = float3(0.0, 0.0, 0.0);
    uint2 span = blendSpans[pointIndex];
    for (uint entry = 0; entry < span.y; ++entry)
    {
        uint delta = span.x + entry;
        if (delta >= blendDeltaCount)
        {
            break;
        }
        float4 offsets = blendDeltas[delta * 2];
        float4 normals = blendDeltas[(delta * 2) + 1];
        float weight = blendWeights[asuint(offsets.w)];
        pointOffset += weight * offsets.xyz;
        normalOffset += weight * normals.xyz;
    }

    float3 bound = DeformTransformPoint(GeomBindRow, bindPoint + pointOffset);
    float3 boundNormal = DeformTransformDirection(
        GeomBindNormalRow,
        bindNormal + normalOffset);

    float3 skinned = float3(0.0, 0.0, 0.0);
    float3 skinnedNormal = float3(0.0, 0.0, 0.0);
    for (uint influence = 0; influence < influencesPerPoint; ++influence)
    {
        uint slot = (pointIndex * influencesPerPoint) + influence;
        float weight = jointWeights[slot];
        if (weight == 0.0)
        {
            continue;
        }
        uint joint = jointIndices[slot];
        if (joint >= jointCount)
        {
            continue;
        }
        skinned += DeformTransformPoint(JointMatrixRow + (joint * 4), bound) * weight;
        if (hasBindNormals != 0)
        {
            skinnedNormal += DeformTransformDirection(
                normalMatrixRow + (joint * 4),
                boundNormal) * weight;
        }
    }

    uint base = pointIndex * vertexStrideFloats;
    deformedVertices[base] = skinned.x;
    deformedVertices[base + 1] = skinned.y;
    deformedVertices[base + 2] = skinned.z;

    if (hasBindNormals == 0)
    {
        if (vertexStrideFloats >= 8)
        {
            float2 flatTexCoord = texCoords[pointIndex];
            deformedVertices[base + 6] = flatTexCoord.x;
            deformedVertices[base + 7] = flatTexCoord.y;
        }
        return;
    }

    // The canonical fallback for a normal that carries no direction. Every
    // consumer of this ABI publishes the same one, so a rig that collapses a
    // normal collapses it identically on the CPU and on the GPU.
    float lengthSquared = dot(skinnedNormal, skinnedNormal);
    float3 normal = float3(0.0, 0.0, 1.0);
    if (isfinite(lengthSquared) && lengthSquared > 1.0e-30)
    {
        normal = skinnedNormal * rsqrt(lengthSquared);
    }
    deformedVertices[base + 3] = normal.x;
    deformedVertices[base + 4] = normal.y;
    deformedVertices[base + 5] = normal.z;

    if (vertexStrideFloats >= 8)
    {
        float2 texCoord = texCoords[pointIndex];
        deformedVertices[base + 6] = texCoord.x;
        deformedVertices[base + 7] = texCoord.y;
    }
}
