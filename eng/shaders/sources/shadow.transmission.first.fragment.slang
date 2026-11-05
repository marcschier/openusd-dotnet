// Copyright (c) marcschier. Licensed under the MIT License.

#include "shadow.mask.fragment.slang"

[shader("fragment")]
ShadowTransmissionOutput shadowTransmissionFirstFragmentMain(ShadowMaskedVertexOutput input)
{
    float depth = ShadowStoredDepth(input);
    if (PointShadowDepthOutsideRange(depth, input.pointDepthScale))
    {
        discard;
    }
    float opacity = saturate(SampleShadowOpacity(input));
    if (opacity <= 0.0)
    {
        discard;
    }
    ShadowTransmissionOutput output;
    output.depth = depth;
    output.color = float4(output.depth, 1.0 - opacity, 0.0, 1.0);
    return output;
}
