// Copyright (c) marcschier. Licensed under the MIT License.

struct PrimitiveIdVertexOutput
{
    float4 position : SV_Position;
};

[shader("fragment")]
float4 primitiveIdProbeFragmentMain(
    PrimitiveIdVertexOutput input,
    uint primitiveId : SV_PrimitiveID) : SV_Target0
{
    return float4(
        float(primitiveId & 0xffu),
        float((primitiveId >> 8u) & 0xffu),
        float((primitiveId >> 16u) & 0xffu),
        float((primitiveId >> 24u) & 0xffu)) / 255.0;
}
