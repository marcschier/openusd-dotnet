// Copyright (c) marcschier. Licensed under the MIT License.

#include "shadow.mask.fragment.slang"

[[vk::binding(41, 0)]]
Texture2D<float4> previousShadowLayer : register(t24);

[shader("fragment")]
float4 shadowTransmissionFragmentMain(ShadowMaskedVertexOutput input) : SV_Target0
{
    float opacity = saturate(SampleShadowOpacity(input));
    float previousDepth = previousShadowLayer.Load(int3(int2(input.position.xy), 0)).x;
    if (opacity <= 0.0 || previousDepth < 0.0 || input.position.z <= previousDepth)
    {
        discard;
    }
    return float4(input.position.z, 1.0 - opacity, 0.0, 1.0);
}

[shader("fragment")]
float4 shadowTransmissionProbeFragmentMain(ShadowMaskedVertexOutput input) : SV_Target0
{
    float opacity = saturate(SampleShadowOpacity(input));
    float nearestDepth = previousShadowLayer.Load(int3(int2(input.position.xy), 0)).x;
    if (opacity <= 0.0 || input.position.z != nearestDepth)
    {
        discard;
    }
    // Straight-alpha-over records 1 - 2^-N in alpha, independently of draw order.
    return float4(0.0, 0.0, 0.0, 0.5);
}
