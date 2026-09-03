// Copyright (c) marcschier. Licensed under the MIT License.

// The depth-only shadow caster vertex stage. It is its own program rather than a
// permutation of the mesh family because a shadow pass binds no material, no
// frame constants and no surface constants, and a backend pipeline layout must
// declare every resource its binary references: folding the caster into the mesh
// binary would force every ordinary mesh pipeline to declare shadow bindings it
// never reads.
//
// The caster transform arrives already composed into light clip space, in the
// same instance table layout the mesh vertex stage reads, so an instanced batch
// casts its shadow with exactly one draw and no second transform path.

struct ShadowVertexInput
{
    float3 position : POSITION;
    float3 normal : NORMAL;
};

struct ShadowVertexOutput
{
    float4 position : SV_Position;
};

struct ShadowInstanceParameters
{
    float4x4 objectToLightClip;
    float4 reserved;
};

[[vk::binding(6, 0)]]
StructuredBuffer<ShadowInstanceParameters> shadowInstanceParameters : register(t6);

[shader("vertex")]
ShadowVertexOutput shadowVertexMain(
    ShadowVertexInput input,
    uint instanceId : SV_InstanceID)
{
    ShadowInstanceParameters instance = shadowInstanceParameters[instanceId];
    ShadowVertexOutput output;
    output.position = mul(instance.objectToLightClip, float4(input.position, 1.0));
    return output;
}
