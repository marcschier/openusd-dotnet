// Copyright (c) marcschier. Licensed under the MIT License.

// Separate unqualified sampling candidate; never substituted for the UV probe.
#include "ptex-slot-probe.slang"

[[vk::binding(1, 0)]]
SamplerState ptexBaseColorSampler : register(s0);
[[vk::binding(2, 0)]]
Texture2D<float4> ptexBaseColorTexture : register(t0);
[[vk::binding(11, 0)]]
SamplerState ptexRoughnessSampler : register(s2);
[[vk::binding(4, 0)]]
Texture2D<float4> ptexRoughnessTexture : register(t2);

bool SamplePtexSlot(
    uint slot, PtexCornerTransport corner, uint lookupBytes,
    Texture2D<float4> atlas, SamplerState atlasSampler,
    out float4 value)
{
    value = float4(0, 0, 0, 0);
    float2 uv;
    if (!MapPtexSlot(slot, corner, lookupBytes, uv))
        return false;
    value = atlas.Sample(atlasSampler, uv);
    return all(isfinite(value));
}

[shader("fragment")]
float4 ptexSlotSamplingFragmentMain(PtexProbeOutput input) : SV_Target0
{
    if (input.valid == 0)
        return float4(1, 0, 1, 1);
    PtexCornerTransport corner;
    corner.face = input.face;
    corner.local = input.local;
    float4 baseColor, roughness;
    if (!SamplePtexSlot(0, corner, input.lookupBytes, ptexBaseColorTexture, ptexBaseColorSampler, baseColor) ||
        !SamplePtexSlot(1, corner, input.lookupBytes, ptexRoughnessTexture, ptexRoughnessSampler, roughness))
        return float4(1, 0, 1, 1);
    return float4(baseColor.rgb, roughness.r);
}
