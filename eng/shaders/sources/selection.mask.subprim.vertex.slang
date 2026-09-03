// Copyright (c) marcschier. Licensed under the MIT License.

struct VertexInput
{
    float3 position : POSITION;
    float3 normal : NORMAL;
};

struct SelectionMaskSubprimVertexOutput
{
    float4 position : SV_Position;
#if !OPENUSD_TARGET_DXIL
    // Vulkan and Metal leave the rasterized point size undefined unless the
    // vertex stage writes it. A selected point whose mask primitive covered no
    // pixel would produce no outline at all, which is indistinguishable from
    // the point not being selected. Direct3D has no programmable point size --
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

/// The normalized-device depth a selected edge or point is pulled toward the
/// viewer, identical to the value the subprim pick stage uses.
///
/// The mask and the pick must agree exactly. If the mask separated a coincident
/// component by a different amount than the pick did, a component the user could
/// pick would not be the component that got outlined at the same pixels.
static const float SubprimDepthOffset = 1.0e-4;

/// Rasterizes the mask for one selected authored edge or point.
///
/// A selected line or point is coplanar with the surface it came from, so in the
/// depth-tested visible-only mask it loses its own less-equal test at an
/// arbitrary subset of the pixels it covers and the outline flickers or
/// disappears. The separation is applied here, in clip space, for exactly the
/// reasons the pick stage applies it there: a rasterizer depth bias is defined
/// for filled primitives, varies with surface slope, and would differ per
/// backend. The x-ray mask runs the same stage with its depth test disabled, so
/// one stage covers both modes.
[shader("vertex")]
SelectionMaskSubprimVertexOutput selectionMaskSubprimVertexMain(VertexInput input)
{
    SelectionMaskSubprimVertexOutput output;
    float4 clip = mul(objectToClip, float4(input.position, 1.0));
    clip.z -= SubprimDepthOffset * clip.w;
    output.position = clip;
#if !OPENUSD_TARGET_DXIL
    output.pointSize = 1.0;
#endif
    return output;
}
