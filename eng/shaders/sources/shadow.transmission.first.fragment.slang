// Copyright (c) marcschier. Licensed under the MIT License.

#include "shadow.mask.fragment.slang"

[shader("fragment")]
float4 shadowTransmissionFirstFragmentMain(ShadowMaskedVertexOutput input) : SV_Target0
{
    float opacity = saturate(SampleShadowOpacity(input));
    if (opacity <= 0.0)
    {
        discard;
    }
    return float4(input.position.z, 1.0 - opacity, 0.0, 1.0);
}
