// Copyright (c) marcschier. Licensed under the MIT License.

struct VertexInput
{
    float3 position : POSITION;
    float3 normal : NORMAL;
};

struct PickSubprimVertexOutput
{
    float4 position : SV_Position;
#if !OPENUSD_TARGET_DXIL
    // Vulkan and Metal leave the rasterized point size undefined unless the
    // vertex stage writes it, and an undefined size is the difference between
    // a point pick that lands and one that silently misses. One pixel is
    // exactly the footprint a one-pixel pick scissor reads back. Direct3D has
    // no programmable point size -- its points are always one pixel -- and
    // DXIL rejects the semantic, so it is omitted there.
    float pointSize : SV_PointSize;
#endif
};

[[vk::binding(0, 0)]]
cbuffer SceneParameters : register(b0)
{
    float4x4 objectToClip;
    float4 tint;
};

/// The normalized-device depth an edge or point is pulled toward the viewer.
///
/// It has to be large enough to cover the rounding difference between a
/// triangle's plane-interpolated depth and a line's or point's depth at the
/// same pixel centre, and small enough that geometry genuinely in front of the
/// component still occludes it. A D32 float depth buffer resolves roughly
/// 6e-8 near one, so 1e-4 is three orders of magnitude above that noise and
/// far below any separation a user would read as "in front".
static const float SubprimDepthOffset = 1.0e-4;

/// Rasterizes one authored edge or point over the surface depth the pick pass
/// already wrote.
///
/// Lines and points are coplanar with the triangles they came from, so without
/// help they lose their own less-equal depth test at an arbitrary subset of the
/// pixels they cover. A rasterizer depth bias cannot fix that: Direct3D and
/// Vulkan apply polygon offset to filled primitives only, and the slope-scaled
/// term is defined from a polygon's gradients, so the same scene would pick
/// differently per backend and per surface slope. Offsetting clip-space depth
/// by a fixed normalized-device amount scaled by w is applied identically on
/// every backend, is independent of the primitive's slope, and leaves the
/// visible-surface occlusion the depth pre-pass established intact.
[shader("vertex")]
PickSubprimVertexOutput pickSubprimVertexMain(VertexInput input)
{
    PickSubprimVertexOutput output;
    float4 clip = mul(objectToClip, float4(input.position, 1.0));
    clip.z -= SubprimDepthOffset * clip.w;
    output.position = clip;
#if !OPENUSD_TARGET_DXIL
    output.pointSize = 1.0;
#endif
    return output;
}
