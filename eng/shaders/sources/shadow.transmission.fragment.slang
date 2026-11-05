// Copyright (c) marcschier. Licensed under the MIT License.

#include "shadow.mask.fragment.slang"

[[vk::binding(41, 0)]]
Texture2D<float4> previousShadowLayer : register(t24);

[shader("fragment")]
ShadowTransmissionOutput shadowTransmissionFragmentMain(ShadowMaskedVertexOutput input)
{
    float storedDepth = ShadowStoredDepth(input);
    if (PointShadowDepthOutsideRange(storedDepth, input.pointDepthScale))
    {
        discard;
    }
    float opacity = saturate(SampleShadowOpacity(input));
    float previousDepth = previousShadowLayer.Load(int3(int2(input.position.xy), 0)).x;
    if (opacity <= 0.0 || previousDepth < 0.0 || storedDepth <= previousDepth)
    {
        discard;
    }
    ShadowTransmissionOutput output;
    output.depth = storedDepth;
    output.color = float4(storedDepth, 1.0 - opacity, 0.0, 1.0);
    return output;
}

[shader("fragment")]
ShadowTransmissionOutput shadowTransmissionProbeFragmentMain(ShadowMaskedVertexOutput input)
{
    float storedDepth = ShadowStoredDepth(input);
    if (PointShadowDepthOutsideRange(storedDepth, input.pointDepthScale))
    {
        discard;
    }
    float opacity = saturate(SampleShadowOpacity(input));
    float nearestDepth = previousShadowLayer.Load(int3(int2(input.position.xy), 0)).x;
    if (opacity <= 0.0 || storedDepth != nearestDepth)
    {
        discard;
    }
    // Straight-alpha-over records 1 - 2^-N in alpha, independently of draw order.
    ShadowTransmissionOutput output;
    output.depth = storedDepth;
    output.color = float4(0.0, 0.0, 0.0, 0.5);
    return output;
}
