// Copyright (c) marcschier. Licensed under the MIT License.

// Uncompiled candidate include; not part of the production shader manifest.
// Slang maps the Metal resource index from its target-specific register.
// Qualification must verify emitted buffer(2); this declaration is not evidence.
[[vk::binding(49, 0)]]
#if OPENUSD_TARGET_METAL
ByteAddressBuffer ptexSlotLookup : register(t2);
#else
ByteAddressBuffer ptexSlotLookup : register(t31);
#endif

struct PtexCornerTransport
{
    nointerpolation uint face : TEXCOORD6;
    float2 local : TEXCOORD7;
};

bool ReadPtexHeader(
    uint bytes, out uint cornerCount, out uint faceCount, out uint slotCount, out uint transformWord)
{
    cornerCount = 0;
    faceCount = 0;
    slotCount = 0;
    transformWord = 0;
    if (bytes < 16 || (bytes & 3) != 0)
        return false;
    uint4 header = ptexSlotLookup.Load4(0);
    uint words = bytes / 4;
    if (header.x == 0 || header.y == 0 || header.z == 0 || header.z > 16 ||
        header.x > (words - 4) / 4)
        return false;
    uint start = 4 + header.x * 4;
    if (header.w != start || header.y > (words - start) / (header.z * 4))
        return false;
    cornerCount = header.x;
    faceCount = header.y;
    slotCount = header.z;
    transformWord = start;
    return true;
}

// Call from a vertex entry using SV_VertexID, not SV_InstanceID. The host
// requires identity corner indices, zero firstIndex and zero baseVertex.
bool LoadPtexCorner(uint vertexId, uint bytes, out PtexCornerTransport corner)
{
    corner.face = 0;
    corner.local = float2(0, 0);
    uint corners, faces, slots, transforms;
    if (!ReadPtexHeader(bytes, corners, faces, slots, transforms) || vertexId >= corners)
        return false;
    uint4 record = ptexSlotLookup.Load4((4 + vertexId * 4) * 4);
    if (record.x >= faces)
        return false;
    corner.face = record.x;
    corner.local = asfloat(record.yz);
    return all(isfinite(corner.local)) && all(corner.local >= 0) && all(corner.local <= 1);
}

// Call from a fragment entry; face is flat, local is perspective-interpolated.
// Each material parameter binds its own existing texture/sampler and slot index.
bool MapPtexSlot(uint slot, PtexCornerTransport corner, uint bytes, out float2 atlasUv)
{
    atlasUv = float2(0, 0);
    uint corners, faces, slots, transforms;
    if (!ReadPtexHeader(bytes, corners, faces, slots, transforms) ||
        slot >= slots || corner.face >= faces)
        return false;
    float4 transform = asfloat(ptexSlotLookup.Load4(
        (transforms + (slot * faces + corner.face) * 4) * 4));
    atlasUv = transform.xy + corner.local * transform.zw;
    return all(isfinite(atlasUv));
}
