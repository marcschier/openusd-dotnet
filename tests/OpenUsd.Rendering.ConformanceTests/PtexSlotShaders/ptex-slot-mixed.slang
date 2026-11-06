// Copyright (c) marcschier. Licensed under the MIT License.

#include "ptex-slot-sampling.slang"
#define MAP_MATERIAL 0
#include "../../../eng/shaders/sources/material.surface.slang"

[shader("fragment")]
float4 ptexSlotMixedFragmentMain(PtexProbeOutput input) : SV_Target0
{
    if (input.valid == 0)
        return float4(1, 0, 1, 1);
    PtexCornerTransport corner;
    corner.face = input.face;
    corner.local = input.local;
    float4 baseColor;
    if (!SamplePtexSlot(0, corner, input.lookupBytes, ptexBaseColorTexture, ptexBaseColorSampler, baseColor))
        return float4(1, 0, 1, 1);
    float4 ordinary = SampleMaterialTexture(
        ptexRoughnessTexture, ptexRoughnessSampler, input.ordinaryUv, false, 0);
    return float4(baseColor.rg, ordinary.rg);
}
