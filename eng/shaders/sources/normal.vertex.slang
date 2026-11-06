// Copyright (c) marcschier. Licensed under the MIT License.

// The world-space normal capture vertex stage. It is its own program rather than
// a permutation of the mesh family because the normal pass binds no material,
// no surface constants, no shadow atlas and no environment, and a backend
// pipeline layout must declare every resource its binary references: folding the
// normal output into the mesh binary would force every ordinary mesh pipeline to
// carry a second fragment output and every normal pipeline to declare material
// bindings it never reads.
//
// It consumes only the position and normal prefix every retained vertex layout
// starts with, so one program serves plain, textured and normal-mapped meshes
// alike, and it reproduces the mesh vertex stage's object-to-world recovery and
// cofactor normal transform exactly.

struct NormalVertexInput
{
    float3 position : POSITION;
    float3 normal : NORMAL;
};

struct NormalVertexOutput
{
    float4 position : SV_Position;
    float3 eyePosition : TEXCOORD0;
    float3 worldNormal : TEXCOORD1;
#if !OPENUSD_TARGET_DXIL
    // Vulkan and Metal leave the rasterized point size undefined unless the
    // vertex stage writes it, and a UsdGeomPoints resource drawn as a point list
    // must cover the same one-pixel footprint the colour and pick passes do.
    // Direct3D points are always one pixel and DXIL rejects the semantic.
    float pointSize : SV_PointSize;
#endif
};

struct NormalInstanceParameters
{
    float4x4 objectToClip;
    float4 tint;
};

[[vk::binding(6, 0)]]
StructuredBuffer<NormalInstanceParameters> normalInstanceParameters : register(t6);

// The same raw frame block the mesh stages read. Only the two inverse matrices
// are loaded, at their fixed byte offsets in the SilkFrameUniformWriter layout.
[[vk::binding(8, 0)]]
ByteAddressBuffer frameParameters : register(t8);

static const uint FrameClipToEyeOffset = 0u;
static const uint FrameEyeToWorldOffset = 12512u;

[shader("vertex")]
NormalVertexOutput normalVertexMain(
    NormalVertexInput input,
    uint instanceId : SV_InstanceID)
{
    NormalInstanceParameters instance = normalInstanceParameters[instanceId];
    float4x4 clipToEye = frameParameters.Load<float4x4>(FrameClipToEyeOffset);
    float4x4 eyeToWorld = frameParameters.Load<float4x4>(FrameEyeToWorldOffset);

    NormalVertexOutput output;
    float4 clipPosition = mul(instance.objectToClip, float4(input.position, 1.0));
    float4 eyePosition = mul(clipToEye, clipPosition);
    output.position = clipPosition;
    output.eyePosition = eyePosition.xyz / eyePosition.w;

    // The object-to-world transform recovered from the instance projection, as
    // the mesh vertex stage does: clipToEye undoes the projection objectToClip
    // applied, including any device clip-space mirror, and eyeToWorld undoes
    // the view.
    float4x4 objectToWorld = mul(eyeToWorld, mul(clipToEye, instance.objectToClip));
    float3 basisX = float3(objectToWorld[0][0], objectToWorld[1][0], objectToWorld[2][0]);
    float3 basisY = float3(objectToWorld[0][1], objectToWorld[1][1], objectToWorld[2][1]);
    float3 basisZ = float3(objectToWorld[0][2], objectToWorld[1][2], objectToWorld[2][2]);

    // The cofactor matrix: the inverse transpose scaled by the determinant. The
    // fragment normalizes, and the front-face flip resolves a mirroring sign.
    output.worldNormal =
        (cross(basisY, basisZ) * input.normal.x) +
        (cross(basisZ, basisX) * input.normal.y) +
        (cross(basisX, basisY) * input.normal.z);
#if !OPENUSD_TARGET_DXIL
    output.pointSize = 1.0;
#endif
    return output;
}
