// Copyright (c) marcschier. Licensed under the MIT License.

struct FlatOrdinalVertexInput
{
    float3 position : POSITION;
    float3 normal : NORMAL;
    float2 ordinal : TEXCOORD0;
    float2 secondary : TEXCOORD1;
};

struct FlatOrdinalVertexOutput
{
    float4 position : SV_Position;
    nointerpolation float2 ordinal : TEXCOORD0;
};

struct FlatOrdinalInstanceParameters
{
    float4x4 objectToLightClip;
    float4 reserved;
};

[[vk::binding(6, 0)]]
StructuredBuffer<FlatOrdinalInstanceParameters> flatOrdinalInstances : register(t6);

[shader("vertex")]
FlatOrdinalVertexOutput flatOrdinalTransportVertexMain(
    FlatOrdinalVertexInput input,
    uint instanceId : SV_InstanceID)
{
    FlatOrdinalVertexOutput output;
    output.position = mul(flatOrdinalInstances[instanceId].objectToLightClip, float4(input.position, 1.0));
    output.ordinal = input.ordinal;
    return output;
}
