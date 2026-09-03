// Copyright (c) marcschier. Licensed under the MIT License.

struct VertexInput
{
    float3 position : POSITION;
    float3 normal : NORMAL;
};

struct PickWholeVertexOutput
{
    float4 position : SV_Position;
#if !OPENUSD_TARGET_DXIL
    // Vulkan and Metal leave the rasterized point size undefined unless the
    // vertex stage writes it. A whole UsdGeomPoints resource is drawn as a
    // point list in the surface pass, so without an explicit size its
    // primitives may cover no pixel at all and the prim would be both
    // unpickable and unable to occlude anything behind it. One pixel is the
    // footprint a one-pixel pick scissor reads back, and it is the same
    // footprint Storm rasterizes. Direct3D has no programmable point size --
    // its points are always one pixel -- and DXIL rejects the semantic, so it
    // is omitted there.
    float pointSize : SV_PointSize;
#endif
};

[[vk::binding(0, 0)]]
cbuffer SceneParameters : register(b0)
{
    float4x4 objectToClip;
    float4 tint;
};

/// Rasterizes a whole rendered resource of any topology into the pick pass.
///
/// This is the unbiased stage. A whole basis-curve or point resource is not
/// coincident with a surface it was triangulated from -- it *is* the surface --
/// so pulling it toward the viewer would let it win the depth test against
/// geometry genuinely in front of it, and a curve behind a wall would answer
/// the pick. Only the subprim overlay stage, which draws authored mesh edges
/// and points that really are coplanar with their own triangles, applies the
/// coincident offset.
[shader("vertex")]
PickWholeVertexOutput pickWholeVertexMain(VertexInput input)
{
    PickWholeVertexOutput output;
    output.position = mul(objectToClip, float4(input.position, 1.0));
#if !OPENUSD_TARGET_DXIL
    output.pointSize = 1.0;
#endif
    return output;
}
