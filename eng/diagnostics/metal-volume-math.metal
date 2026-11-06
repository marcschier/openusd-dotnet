// Copyright (c) marcschier. Licensed under the MIT License.

#include <metal_stdlib>
using namespace metal;

struct VertexOutput
{
    float4 position [[position]];
};

vertex VertexOutput volumeMathVertex(uint vertexId [[vertex_id]])
{
    float2 position = float2((vertexId << 1) & 2, vertexId & 2);
    return { float4((position * 2.0) - 1.0, 0.0, 1.0) };
}

fragment float4 volumeMathFragment(
    VertexOutput input [[stage_in]],
    constant float4* values [[buffer(0)]])
{
    uint pixel = uint(input.position.x);
    uint index = (pixel / 2u) * 2u;
    float4 field = values[index];
    float normalizedIntegral = values[index + 1u].x;
    float densityFraction = max(0.0, field.y) / field.z;
    float fieldDepth =
        field.x * (densityFraction * field.w / normalizedIntegral);
    if ((pixel & 1u) == 0u)
    {
        float referenceDepth = field.x * (field.w / normalizedIntegral);
        return float4(
            densityFraction,
            fieldDepth,
            fieldDepth / referenceDepth,
            exp(-fieldDepth));
    }

    float otherDepth = values[index + 1u].z;
    float tint = values[index + 1u].y;
    float opticalDepth = fieldDepth + otherDepth;
    float tintContribution = tint * (fieldDepth / opticalDepth);
    float otherContribution = tint * (otherDepth / opticalDepth);
    float tintedSource = tintContribution + otherContribution;
    return float4(
        fieldDepth / opticalDepth,
        tintContribution,
        tintedSource,
        exp(-fieldDepth) * tintedSource);
}
