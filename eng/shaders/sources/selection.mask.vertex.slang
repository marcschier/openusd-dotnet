// Copyright (c) marcschier. Licensed under the MIT License.

struct VertexInput
{
    float3 position : POSITION;
    float3 normal : NORMAL;
};

struct SelectionMaskVertexOutput
{
    float4 position : SV_Position;
};

[[vk::binding(0, 0)]]
cbuffer SceneParameters : register(b0)
{
    float4x4 objectToClip;
    float4 tint;
};

[shader("vertex")]
SelectionMaskVertexOutput selectionMaskVertexMain(VertexInput input)
{
    SelectionMaskVertexOutput output;
    output.position = mul(objectToClip, float4(input.position, 1.0));
    return output;
}
