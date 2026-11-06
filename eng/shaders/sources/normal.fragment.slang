// Copyright (c) marcschier. Licensed under the MIT License.

// Writes the unit world-space shading normal of the front-most surface into a
// float colour target for the RenderProduct normal plane.
//
// The normal is the interpolated vertex normal, not the normal-mapped one: the
// pass binds no material. It is flipped toward the viewer on rasterized back
// faces by the same coverage-facing rule the mesh fragment stage applies, so
// the plane matches the normal the colour pass shaded with before any normal
// map. A fragment whose interpolated normal is degenerate writes zero, the same
// value as the cleared background, rather than an undefined direction.

struct NormalVertexOutput
{
    float4 position : SV_Position;
    float3 eyePosition : TEXCOORD0;
    float3 worldNormal : TEXCOORD1;
};

[[vk::binding(8, 0)]]
ByteAddressBuffer frameParameters : register(t8);

static const uint FrameClipPlaneCountOffset = 64u;
static const uint FrameClipPlanesOffset = 80u;
static const uint MaximumClipPlanes = 8u;

[shader("fragment")]
float4 normalFragmentMain(
    NormalVertexOutput input,
    bool isFrontFace : SV_IsFrontFace) : SV_Target0
{
    // The authored clip planes remove the same fragments the colour pass removes,
    // so the plane never reports a surface the beauty image does not show.
    uint count = min(frameParameters.Load<uint4>(FrameClipPlaneCountOffset).x, MaximumClipPlanes);
    for (uint index = 0u; index < count; index++)
    {
        float4 plane = frameParameters.Load<float4>(FrameClipPlanesOffset + index * 16u);
        if (dot(plane.xyz, input.eyePosition) + plane.w < 0.0)
        {
            discard;
        }
    }

    float lengthSquared = dot(input.worldNormal, input.worldNormal);
    if (!(lengthSquared > 1.0e-30))
    {
        return float4(0.0, 0.0, 0.0, 1.0);
    }
    float3 normal = input.worldNormal * rsqrt(lengthSquared);
    normal = isFrontFace ? normal : -normal;
    return float4(normal, 1.0);
}
