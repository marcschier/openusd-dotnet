// Copyright (c) marcschier. Licensed under the MIT License.

#include "ptex-slot-lookup.slang"

struct PtexProbeInput
{
    float3 position : POSITION;
    float3 normal : NORMAL;
    float2 ordinaryUv : TEXCOORD0;
};

struct PtexProbeOutput
{
    float4 position : SV_Position;
    nointerpolation uint face : TEXCOORD6;
    float2 local : TEXCOORD7;
    nointerpolation uint valid : TEXCOORD8;
    nointerpolation uint lookupBytes : TEXCOORD9;
    float2 ordinaryUv : TEXCOORD0;
};

struct PtexProbeInstance
{
    float4x4 objectToClip;
    uint4 lookupMetadata;
};

[[vk::binding(6, 0)]]
StructuredBuffer<PtexProbeInstance> ptexProbeInstances : register(t6);

[shader("vertex")]
PtexProbeOutput ptexSlotProbeVertexMain(
    PtexProbeInput input,
    uint vertexId : SV_VertexID,
    uint instanceId : SV_InstanceID)
{
    PtexProbeOutput output;
    output.position = mul(ptexProbeInstances[instanceId].objectToClip, float4(input.position, 1));
    output.lookupBytes = ptexProbeInstances[instanceId].lookupMetadata.x;
    PtexCornerTransport corner;
    output.valid = LoadPtexCorner(vertexId, output.lookupBytes, corner) ? 1 : 0;
    output.face = corner.face;
    output.local = corner.local;
    output.ordinaryUv = input.ordinaryUv;
    return output;
}

[shader("fragment")]
float4 ptexSlotProbeFragmentMain(PtexProbeOutput input) : SV_Target0
{
    if (input.valid == 0)
        return float4(1, 0, 1, 1);
    PtexCornerTransport corner;
    corner.face = input.face;
    corner.local = input.local;
    float2 first, second;
    if (!MapPtexSlot(0, corner, input.lookupBytes, first) ||
        !MapPtexSlot(1, corner, input.lookupBytes, second))
        return float4(1, 0, 1, 1);
    // Red/green carry slot zero; blue/alpha carry slot one.
    return float4(first, second);
}
