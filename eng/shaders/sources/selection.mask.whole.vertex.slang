// Copyright (c) marcschier. Licensed under the MIT License.

struct VertexInput
{
    float3 position : POSITION;
    float3 normal : NORMAL;
};

struct SelectionMaskWholeVertexOutput
{
    float4 position : SV_Position;
#if !OPENUSD_TARGET_DXIL
    // Vulkan and Metal leave the rasterized point size undefined unless the
    // vertex stage writes it. A selected UsdGeomPoints resource whose mask
    // primitives covered no pixel would produce no outline at all, which is
    // indistinguishable from the prim not being selected. Direct3D has no
    // programmable point size -- its points are always one pixel -- and DXIL
    // rejects the semantic, so it is omitted there.
    float pointSize : SV_PointSize;
#endif
};

[[vk::binding(0, 0)]]
cbuffer SceneParameters : register(b0)
{
    float4x4 objectToClip;
    float4 tint;
};

/// Rasterizes the selection mask of a whole rendered resource of any topology.
///
/// This is the unbiased stage. It must agree exactly with the unbiased whole
/// pick stage: a curve or point cloud the user can pick has to be the one that
/// gets outlined at the same pixels, and a whole resource pulled toward the
/// viewer would outline through an occluder in the visible-only mode that
/// exists precisely to hide it.
[shader("vertex")]
SelectionMaskWholeVertexOutput selectionMaskWholeVertexMain(VertexInput input)
{
    SelectionMaskWholeVertexOutput output;
    output.position = mul(objectToClip, float4(input.position, 1.0));
#if !OPENUSD_TARGET_DXIL
    output.pointSize = 1.0;
#endif
    return output;
}
