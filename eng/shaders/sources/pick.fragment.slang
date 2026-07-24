// Copyright (c) marcschier. Licensed under the MIT License.

struct PickVertexOutput
{
    float4 position : SV_Position;
};

[[vk::binding(1, 0)]]
cbuffer PickParameters : register(b1)
{
    uint4 pickToken;
};

[shader("fragment")]
float4 pickFragmentMain(
    PickVertexOutput input,
    uint primitiveId : SV_PrimitiveID) : SV_Target0
{
    uint token = pickToken.x + primitiveId;
    return float4(
        float(token & 0xffu),
        float((token >> 8u) & 0xffu),
        float((token >> 16u) & 0xffu),
        float((token >> 24u) & 0xffu)) / 255.0;
}
