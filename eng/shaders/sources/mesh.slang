// Copyright (c) marcschier. Licensed under the MIT License.

struct VertexInput
{
    float3 position : POSITION;
    float3 normal : NORMAL;
};

struct VertexOutput
{
    float4 position : SV_Position;
    float3 normal : NORMAL;
};

[[vk::binding(0, 0)]]
cbuffer SceneParameters : register(b0)
{
    float4x4 objectToClip;
    float4 tint;
};

[shader("vertex")]
VertexOutput vertexMain(VertexInput input)
{
    VertexOutput output;
    output.position = mul(objectToClip, float4(input.position, 1.0));
    output.normal = input.normal;
    return output;
}

[shader("fragment")]
float4 fragmentMain(VertexOutput input) : SV_Target0
{
    return float4(abs(input.normal), 1.0) * tint;
}
