// Copyright (c) marcschier. Licensed under the MIT License.

#ifndef VERTEX_ENTRY_POINT
#define VERTEX_ENTRY_POINT vertexMain
#endif
#ifndef FRAGMENT_ENTRY_POINT
#define FRAGMENT_ENTRY_POINT fragmentMain
#endif
#ifndef HAS_UV
#define HAS_UV 0
#endif
#ifndef VERTEX_STAGE
#define VERTEX_STAGE 0
#endif
#ifndef FRAGMENT_STAGE
#define FRAGMENT_STAGE 0
#endif
#ifndef MAP_BASE_COLOR
#define MAP_BASE_COLOR 0
#endif
#ifndef MAP_NORMAL
#define MAP_NORMAL 0
#endif
#ifndef MAP_ROUGHNESS_METALLIC
#define MAP_ROUGHNESS_METALLIC 0
#endif
#ifndef MAP_EMISSIVE
#define MAP_EMISSIVE 0
#endif

struct VertexInput
{
    float3 position : POSITION;
    float3 normal : NORMAL;
    float4 tint : COLOR0;
#if HAS_UV
    float2 texCoord : TEXCOORD0;
#endif
#if MAP_NORMAL
    float4 tangent : TANGENT;
#endif
};

struct VertexOutput
{
    float4 position : SV_Position;
    float3 normal : NORMAL;
    float4 tint : COLOR0;
#if HAS_UV
    float2 texCoord : TEXCOORD0;
#endif
#if MAP_NORMAL
    float4 tangent : TANGENT;
#endif
};

[[vk::binding(0, 0)]]
cbuffer SceneParameters : register(b0)
{
    float4x4 objectToClip;
    float4 tint;
};

struct InstanceParameters
{
    float4x4 objectToClip;
    float4 tint;
};

[[vk::binding(6, 0)]]
StructuredBuffer<InstanceParameters> instanceParameters : register(t6);

#if FRAGMENT_STAGE && (MAP_BASE_COLOR || MAP_NORMAL || MAP_ROUGHNESS_METALLIC || MAP_EMISSIVE)
[[vk::binding(1, 0)]]
SamplerState materialSampler : register(s0);
#endif
#if FRAGMENT_STAGE && MAP_BASE_COLOR
[[vk::binding(2, 0)]]
Texture2D<float4> baseColorTexture : register(t0);
#endif
#if FRAGMENT_STAGE && MAP_NORMAL
[[vk::binding(3, 0)]]
Texture2D<float4> normalTexture : register(t1);
#endif
#if FRAGMENT_STAGE && MAP_ROUGHNESS_METALLIC
[[vk::binding(4, 0)]]
Texture2D<float4> roughnessMetallicTexture : register(t2);
#endif
#if FRAGMENT_STAGE && MAP_EMISSIVE
[[vk::binding(5, 0)]]
Texture2D<float4> emissiveTexture : register(t3);
#endif

[shader("vertex")]
VertexOutput VERTEX_ENTRY_POINT(VertexInput input, uint instanceId : SV_InstanceID)
{
    InstanceParameters instance = instanceParameters[instanceId];
    VertexOutput output;
    output.position = mul(instance.objectToClip, float4(input.position, 1.0));
    output.normal = input.normal;
    output.tint = instance.tint;
#if HAS_UV
    output.texCoord = input.texCoord;
#endif
#if MAP_NORMAL
    output.tangent = input.tangent;
#endif
    return output;
}

[shader("fragment")]
float4 FRAGMENT_ENTRY_POINT(VertexOutput input) : SV_Target0
{
    // Placeholder for preview-surface-shading; this only proves fetch topology.
    float3 color = abs(input.normal);
#if FRAGMENT_STAGE && MAP_BASE_COLOR
    color *= baseColorTexture.Sample(materialSampler, input.texCoord).rgb;
#endif
#if FRAGMENT_STAGE && MAP_NORMAL
    float3 sampledNormal = normalTexture.Sample(materialSampler, input.texCoord).xyz;
    color = lerp(color, abs(sampledNormal * 2.0 - 1.0), 0.25);
#endif
#if FRAGMENT_STAGE && MAP_ROUGHNESS_METALLIC
    float roughness = roughnessMetallicTexture.Sample(materialSampler, input.texCoord).g;
    color *= lerp(0.5, 1.0, roughness);
#endif
#if FRAGMENT_STAGE && MAP_EMISSIVE
    color += emissiveTexture.Sample(materialSampler, input.texCoord).rgb;
#endif
    return float4(color, 1.0) * input.tint;
}
