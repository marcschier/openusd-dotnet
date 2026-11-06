// Copyright (c) marcschier. Licensed under the MIT License.

struct FlatOrdinalInput
{
    // Keep DXIL input register packing aligned with the vertex output.
    float4 position : SV_Position;
    nointerpolation float2 ordinal : TEXCOORD0;
};

[shader("fragment")]
float4 flatOrdinalTransportFragmentMain(FlatOrdinalInput input) : SV_Target0
{
    uint ordinal = uint(input.ordinal.x);
    return float4(
        float(ordinal & 0xffu),
        float((ordinal >> 8u) & 0xffu),
        float((ordinal >> 16u) & 0xffu),
        float((ordinal >> 24u) & 0xffu)) / 255.0;
}
