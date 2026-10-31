// Copyright (c) marcschier. Licensed under the MIT License.

#define MAP_MATERIAL 1
#define FRAGMENT_STAGE 1

[[vk::binding(16, 0)]]
SamplerState opacitySampler : register(s6);
[[vk::binding(17, 0)]]
Texture2D<float4> opacityTexture : register(t5);
[[vk::binding(28, 0)]]
SamplerState compositeSampler : register(s12);
[[vk::binding(29, 0)]]
Texture2D<float4> compositeTexture : register(t15);

#include "material.surface.slang"

[[vk::binding(7, 0)]]
StructuredBuffer<SurfaceParameters> surfaceParameters : register(t7);

struct ShadowMaskedVertexOutput
{
    float4 position : SV_Position;
    float2 texCoord : TEXCOORD0;
    float2 texCoord1 : TEXCOORD1;
};

[shader("fragment")]
void shadowMaskedFragmentMain(ShadowMaskedVertexOutput input)
{
    SurfaceParameters surface = surfaceParameters[0];
    uint secondaryMask = (uint)round(surface.uvTransformRow1.w);
    uint alternateMask = (uint)round(surface.domeLinkControls.w);
    float2 slotTexCoord = SelectTexCoord(
        64u,
        secondaryMask,
        alternateMask,
        TransformTextureCoordinate(surface.uvTransformRow0, surface.uvTransformRow1, input.texCoord),
        TransformTextureCoordinate(surface.uvTransformRow0, surface.uvTransformRow1, input.texCoord1),
        TransformTextureCoordinate(surface.alternateUvTransformRow0, surface.alternateUvTransformRow1, input.texCoord),
        TransformTextureCoordinate(surface.alternateUvTransformRow0, surface.alternateUvTransformRow1, input.texCoord1));
    if (SampleMaterialOpacity(surface, slotTexCoord) < surface.metallicRoughnessThresholdWorkflow.z)
    {
        discard;
    }
}
