#include <metal_stdlib>
#include <metal_math>
#include <metal_texture>
using namespace metal;
struct SLANG_ParameterGroup_DeformParameters_0
{
    uint pointCount_0;
    uint influencesPerPoint_0;
    uint vertexStrideFloats_0;
    uint hasBindNormals_0;
    uint jointCount_0;
    uint normalMatrixRow_0;
    uint blendDeltaCount_0;
    uint deformReserved_0;
};

struct KernelContext_0
{
    SLANG_ParameterGroup_DeformParameters_0 constant* DeformParameters_0;
    packed_float4 device* bindPose_0;
    packed_uint2 device* blendSpans_0;
    packed_float4 device* blendDeltas_0;
    float device* blendWeights_0;
    packed_float4 device* deformMatrices_0;
    float device* jointWeights_0;
    uint device* jointIndices_0;
    float device* deformedVertices_0;
    packed_float2 device* texCoords_0;
};

float3 DeformTransformPoint_0(uint row_0, float3 point_0, KernelContext_0 thread* kernelContext_0)
{
    return float3(point_0.x)  * (float4(*(kernelContext_0->deformMatrices_0+row_0)) ).xyz + float3(point_0.y)  * (float4(*(kernelContext_0->deformMatrices_0+(row_0 + 1U))) ).xyz + float3(point_0.z)  * (float4(*(kernelContext_0->deformMatrices_0+(row_0 + 2U))) ).xyz + (float4(*(kernelContext_0->deformMatrices_0+(row_0 + 3U))) ).xyz;
}

float3 DeformTransformDirection_0(uint row_1, float3 direction_0, KernelContext_0 thread* kernelContext_1)
{
    return float3(direction_0.x)  * (float4(*(kernelContext_1->deformMatrices_0+row_1)) ).xyz + float3(direction_0.y)  * (float4(*(kernelContext_1->deformMatrices_0+(row_1 + 1U))) ).xyz + float3(direction_0.z)  * (float4(*(kernelContext_1->deformMatrices_0+(row_1 + 2U))) ).xyz;
}

[[kernel]] void deformMain(uint3 dispatchThreadId_0 [[thread_position_in_grid]], SLANG_ParameterGroup_DeformParameters_0 constant* DeformParameters_1 [[buffer(9)]], packed_float4 device* bindPose_1 [[buffer(1)]], packed_uint2 device* blendSpans_1 [[buffer(6)]], packed_float4 device* blendDeltas_1 [[buffer(7)]], float device* blendWeights_1 [[buffer(5)]], packed_float4 device* deformMatrices_1 [[buffer(4)]], float device* jointWeights_1 [[buffer(3)]], uint device* jointIndices_1 [[buffer(2)]], float device* deformedVertices_1 [[buffer(0)]], packed_float2 device* texCoords_1 [[buffer(8)]])
{
    float3 skinnedNormal_0;
    thread KernelContext_0 kernelContext_2;
    (&kernelContext_2)->DeformParameters_0 = DeformParameters_1;
    (&kernelContext_2)->bindPose_0 = bindPose_1;
    (&kernelContext_2)->blendSpans_0 = blendSpans_1;
    (&kernelContext_2)->blendDeltas_0 = blendDeltas_1;
    (&kernelContext_2)->blendWeights_0 = blendWeights_1;
    (&kernelContext_2)->deformMatrices_0 = deformMatrices_1;
    (&kernelContext_2)->jointWeights_0 = jointWeights_1;
    (&kernelContext_2)->jointIndices_0 = jointIndices_1;
    (&kernelContext_2)->deformedVertices_0 = deformedVertices_1;
    (&kernelContext_2)->texCoords_0 = texCoords_1;
    uint pointIndex_0 = dispatchThreadId_0.x;
    if(pointIndex_0 >= (DeformParameters_1->pointCount_0))
    {
        return;
    }
    uint _S1 = pointIndex_0 * 2U;
    float3 bindPoint_0 = (float4(*((&kernelContext_2)->bindPose_0+_S1)) ).xyz;
    float3 bindNormal_0 = (float4(*((&kernelContext_2)->bindPose_0+(_S1 + 1U))) ).xyz;
    float3 _S2 = float3(0.0f, 0.0f, 0.0f);
    uint2 _S3 = uint2(*((&kernelContext_2)->blendSpans_0+pointIndex_0)) ;
    uint entry_0 = 0U;
    float3 pointOffset_0 = _S2;
    float3 normalOffset_0 = _S2;
    for(;;)
    {
        if(entry_0 < (_S3.y))
        {
        }
        else
        {
            break;
        }
        uint delta_0 = _S3.x + entry_0;
        if(delta_0 >= ((&kernelContext_2)->DeformParameters_0->blendDeltaCount_0))
        {
            break;
        }
        uint _S4 = delta_0 * 2U;
        float4 _S5 = float4(*((&kernelContext_2)->blendDeltas_0+_S4)) ;
        float3 _S6 = float3((&kernelContext_2)->blendWeights_0[(as_type<uint>((_S5.w)))]) ;
        float3 pointOffset_1 = pointOffset_0 + _S6 * _S5.xyz;
        float3 normalOffset_1 = normalOffset_0 + _S6 * (float4(*((&kernelContext_2)->blendDeltas_0+(_S4 + 1U))) ).xyz;
        entry_0 = entry_0 + 1U;
        pointOffset_0 = pointOffset_1;
        normalOffset_0 = normalOffset_1;
    }
    float3 _S7 = DeformTransformPoint_0(0U, bindPoint_0 + pointOffset_0, &kernelContext_2);
    float3 _S8 = DeformTransformDirection_0(4U, bindNormal_0 + normalOffset_0, &kernelContext_2);
    uint influence_0 = 0U;
    float3 skinned_0 = _S2;
    float3 skinnedNormal_1 = _S2;
    for(;;)
    {
        if(influence_0 < ((&kernelContext_2)->DeformParameters_0->influencesPerPoint_0))
        {
        }
        else
        {
            break;
        }
        uint slot_0 = pointIndex_0 * (&kernelContext_2)->DeformParameters_0->influencesPerPoint_0 + influence_0;
        float weight_0 = (&kernelContext_2)->jointWeights_0[slot_0];
        if(weight_0 == 0.0f)
        {
            influence_0 = influence_0 + 1U;
            continue;
        }
        uint joint_0 = (&kernelContext_2)->jointIndices_0[slot_0];
        if(joint_0 >= ((&kernelContext_2)->DeformParameters_0->jointCount_0))
        {
            influence_0 = influence_0 + 1U;
            continue;
        }
        uint _S9 = joint_0 * 4U;
        float3 _S10 = DeformTransformPoint_0(8U + _S9, _S7, &kernelContext_2);
        float3 _S11 = float3(weight_0) ;
        float3 skinned_1 = skinned_0 + _S10 * _S11;
        if(((&kernelContext_2)->DeformParameters_0->hasBindNormals_0) != 0U)
        {
            float3 _S12 = DeformTransformDirection_0((&kernelContext_2)->DeformParameters_0->normalMatrixRow_0 + _S9, _S8, &kernelContext_2);
            skinnedNormal_0 = skinnedNormal_1 + _S12 * _S11;
        }
        else
        {
            skinnedNormal_0 = skinnedNormal_1;
        }
        skinned_0 = skinned_1;
        skinnedNormal_1 = skinnedNormal_0;
        influence_0 = influence_0 + 1U;
    }
    uint base_0 = pointIndex_0 * (&kernelContext_2)->DeformParameters_0->vertexStrideFloats_0;
    *((&kernelContext_2)->deformedVertices_0+base_0) = skinned_0.x;
    *((&kernelContext_2)->deformedVertices_0+(base_0 + 1U)) = skinned_0.y;
    *((&kernelContext_2)->deformedVertices_0+(base_0 + 2U)) = skinned_0.z;
    if(((&kernelContext_2)->DeformParameters_0->hasBindNormals_0) == 0U)
    {
        if(((&kernelContext_2)->DeformParameters_0->vertexStrideFloats_0) >= 8U)
        {
            float2 _S13 = float2(*((&kernelContext_2)->texCoords_0+pointIndex_0)) ;
            *((&kernelContext_2)->deformedVertices_0+(base_0 + 6U)) = _S13.x;
            *((&kernelContext_2)->deformedVertices_0+(base_0 + 7U)) = _S13.y;
        }
        return;
    }
    float lengthSquared_0 = dot(skinnedNormal_1, skinnedNormal_1);
    float3 _S14 = float3(0.0f, 0.0f, 1.0f);
    bool _S15;
    if(isfinite(lengthSquared_0))
    {
        _S15 = lengthSquared_0 > 1.00000000317107685e-30f;
    }
    else
    {
        _S15 = false;
    }
    if(_S15)
    {
        skinnedNormal_0 = skinnedNormal_1 * float3(rsqrt(lengthSquared_0)) ;
    }
    else
    {
        skinnedNormal_0 = _S14;
    }
    *((&kernelContext_2)->deformedVertices_0+(base_0 + 3U)) = skinnedNormal_0.x;
    *((&kernelContext_2)->deformedVertices_0+(base_0 + 4U)) = skinnedNormal_0.y;
    *((&kernelContext_2)->deformedVertices_0+(base_0 + 5U)) = skinnedNormal_0.z;
    if(((&kernelContext_2)->DeformParameters_0->vertexStrideFloats_0) >= 8U)
    {
        float2 _S16 = float2(*((&kernelContext_2)->texCoords_0+pointIndex_0)) ;
        *((&kernelContext_2)->deformedVertices_0+(base_0 + 6U)) = _S16.x;
        *((&kernelContext_2)->deformedVertices_0+(base_0 + 7U)) = _S16.y;
    }
    return;
}
