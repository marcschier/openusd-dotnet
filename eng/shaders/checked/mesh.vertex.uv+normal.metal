#include <metal_stdlib>
#include <metal_math>
#include <metal_texture>
using namespace metal;
struct vertexMain_uv_normal_Result_0
{
    float4 position_0 [[position]];
    float3 eyePosition_0 [[user(TEXCOORD_1)]];
    float3 objectPosition_0 [[user(TEXCOORD_2)]];
    float3 normal_0 [[user(NORMAL)]];
    float4 tint_0 [[user(COLOR)]];
    float3 worldNormal_0 [[user(TEXCOORD_3)]];
    float2 texCoord_0 [[user(TEXCOORD)]];
    float4 tangent_0 [[user(TANGENT)]];
    float4 worldTangent_0 [[user(TEXCOORD_4)]];
};

struct vertexInput_0
{
    float3 position_1 [[attribute(0)]];
    float3 normal_1 [[attribute(1)]];
    float2 texCoord_1 [[attribute(2)]];
    float4 tangent_1 [[attribute(3)]];
};

struct _MatrixStorage_float4x4natural_0
{
    array<packed_float4, int(4)> data_0;
};

struct InstanceParameters_natural_0
{
    _MatrixStorage_float4x4natural_0 objectToClip_0;
    packed_float4 tint_1;
};

struct KernelContext_0
{
    InstanceParameters_natural_0 device* instanceParameters_0;
    uint32_t device* frameParameters_0;
};

struct VertexFrameMatrices_0
{
    matrix<float,int(4),int(4)>  clipToEye_0;
    matrix<float,int(4),int(4)>  eyeToWorld_0;
};

struct VertexOutput_0
{
    float4 position_2;
    float3 eyePosition_1;
    float3 objectPosition_1;
    float3 normal_2;
    float4 tint_2;
    float3 worldNormal_1;
    float2 texCoord_2;
    float4 tangent_2;
    float4 worldTangent_1;
};

[[vertex]] vertexMain_uv_normal_Result_0 vertexMain_uv_normal(vertexInput_0 _S1 [[stage_in]], uint instanceId_0 [[instance_id]], uint base_instance_0 [[base_instance]], InstanceParameters_natural_0 device* instanceParameters_1 [[buffer(6)]], uint32_t device* frameParameters_1 [[buffer(8)]])
{
    thread KernelContext_0 kernelContext_0;
    (&kernelContext_0)->instanceParameters_0 = instanceParameters_1;
    (&kernelContext_0)->frameParameters_0 = frameParameters_1;
    InstanceParameters_natural_0 instance_0 = instanceParameters_1[instanceId_0 - base_instance_0];
    thread VertexFrameMatrices_0 frame_0;
    float _S2 = as_type<float>(frameParameters_1[(0U)>>2]);
    float _S3 = as_type<float>((&kernelContext_0)->frameParameters_0[(4U)>>2]);
    float _S4 = as_type<float>((&kernelContext_0)->frameParameters_0[(8U)>>2]);
    float _S5 = as_type<float>((&kernelContext_0)->frameParameters_0[(12U)>>2]);
    float4 _S6 = float4(_S2, _S3, _S4, _S5);
    float _S7 = as_type<float>((&kernelContext_0)->frameParameters_0[(16U)>>2]);
    float _S8 = as_type<float>((&kernelContext_0)->frameParameters_0[(20U)>>2]);
    float _S9 = as_type<float>((&kernelContext_0)->frameParameters_0[(24U)>>2]);
    float _S10 = as_type<float>((&kernelContext_0)->frameParameters_0[(28U)>>2]);
    float4 _S11 = float4(_S7, _S8, _S9, _S10);
    float _S12 = as_type<float>((&kernelContext_0)->frameParameters_0[(32U)>>2]);
    float _S13 = as_type<float>((&kernelContext_0)->frameParameters_0[(36U)>>2]);
    float _S14 = as_type<float>((&kernelContext_0)->frameParameters_0[(40U)>>2]);
    float _S15 = as_type<float>((&kernelContext_0)->frameParameters_0[(44U)>>2]);
    float4 _S16 = float4(_S12, _S13, _S14, _S15);
    float _S17 = as_type<float>((&kernelContext_0)->frameParameters_0[(48U)>>2]);
    float _S18 = as_type<float>((&kernelContext_0)->frameParameters_0[(52U)>>2]);
    float _S19 = as_type<float>((&kernelContext_0)->frameParameters_0[(56U)>>2]);
    float _S20 = as_type<float>((&kernelContext_0)->frameParameters_0[(60U)>>2]);
    (&frame_0)->clipToEye_0 = matrix<float,int(4),int(4)> (_S6, _S11, _S16, float4(_S17, _S18, _S19, _S20));
    float _S21 = as_type<float>((&kernelContext_0)->frameParameters_0[(12512U)>>2]);
    float _S22 = as_type<float>((&kernelContext_0)->frameParameters_0[(12516U)>>2]);
    float _S23 = as_type<float>((&kernelContext_0)->frameParameters_0[(12520U)>>2]);
    float _S24 = as_type<float>((&kernelContext_0)->frameParameters_0[(12524U)>>2]);
    float4 _S25 = float4(_S21, _S22, _S23, _S24);
    float _S26 = as_type<float>((&kernelContext_0)->frameParameters_0[(12528U)>>2]);
    float _S27 = as_type<float>((&kernelContext_0)->frameParameters_0[(12532U)>>2]);
    float _S28 = as_type<float>((&kernelContext_0)->frameParameters_0[(12536U)>>2]);
    float _S29 = as_type<float>((&kernelContext_0)->frameParameters_0[(12540U)>>2]);
    float4 _S30 = float4(_S26, _S27, _S28, _S29);
    float _S31 = as_type<float>((&kernelContext_0)->frameParameters_0[(12544U)>>2]);
    float _S32 = as_type<float>((&kernelContext_0)->frameParameters_0[(12548U)>>2]);
    float _S33 = as_type<float>((&kernelContext_0)->frameParameters_0[(12552U)>>2]);
    float _S34 = as_type<float>((&kernelContext_0)->frameParameters_0[(12556U)>>2]);
    float4 _S35 = float4(_S31, _S32, _S33, _S34);
    float _S36 = as_type<float>((&kernelContext_0)->frameParameters_0[(12560U)>>2]);
    float _S37 = as_type<float>((&kernelContext_0)->frameParameters_0[(12564U)>>2]);
    float _S38 = as_type<float>((&kernelContext_0)->frameParameters_0[(12568U)>>2]);
    float _S39 = as_type<float>((&kernelContext_0)->frameParameters_0[(12572U)>>2]);
    matrix<float,int(4),int(4)>  _S40 = matrix<float,int(4),int(4)> (_S25, _S30, _S35, float4(_S36, _S37, _S38, _S39));
    (&frame_0)->eyeToWorld_0 = _S40;
    matrix<float,int(4),int(4)>  _S41 = matrix<float,int(4),int(4)> (instance_0.objectToClip_0.data_0[int(0)][int(0)], instance_0.objectToClip_0.data_0[int(0)][int(1)], instance_0.objectToClip_0.data_0[int(0)][int(2)], instance_0.objectToClip_0.data_0[int(0)][int(3)], instance_0.objectToClip_0.data_0[int(1)][int(0)], instance_0.objectToClip_0.data_0[int(1)][int(1)], instance_0.objectToClip_0.data_0[int(1)][int(2)], instance_0.objectToClip_0.data_0[int(1)][int(3)], instance_0.objectToClip_0.data_0[int(2)][int(0)], instance_0.objectToClip_0.data_0[int(2)][int(1)], instance_0.objectToClip_0.data_0[int(2)][int(2)], instance_0.objectToClip_0.data_0[int(2)][int(3)], instance_0.objectToClip_0.data_0[int(3)][int(0)], instance_0.objectToClip_0.data_0[int(3)][int(1)], instance_0.objectToClip_0.data_0[int(3)][int(2)], instance_0.objectToClip_0.data_0[int(3)][int(3)]);
    float4 clipPosition_0 = (((float4(_S1.position_1, 1.0f)) * (_S41)));
    float4 eyePosition_2 = (((clipPosition_0) * ((&frame_0)->clipToEye_0)));
    thread VertexOutput_0 output_0;
    (&output_0)->position_2 = clipPosition_0;
    (&output_0)->eyePosition_1 = eyePosition_2.xyz / float3(eyePosition_2.w) ;
    (&output_0)->objectPosition_1 = _S1.position_1;
    (&output_0)->normal_2 = _S1.normal_1;
    (&output_0)->tint_2 = float4(instance_0.tint_1) ;
    matrix<float,int(4),int(4)>  objectToWorld_0 = ((((((_S41) * ((&frame_0)->clipToEye_0)))) * (_S40)));
    float3 basisX_0 = float3(objectToWorld_0[int(0)][int(0)], objectToWorld_0[int(1)][int(0)], objectToWorld_0[int(2)][int(0)]);
    float3 basisY_0 = float3(objectToWorld_0[int(0)][int(1)], objectToWorld_0[int(1)][int(1)], objectToWorld_0[int(2)][int(1)]);
    float3 basisZ_0 = float3(objectToWorld_0[int(0)][int(2)], objectToWorld_0[int(1)][int(2)], objectToWorld_0[int(2)][int(2)]);
    (&output_0)->worldNormal_1 = cross(basisY_0, basisZ_0) * float3(_S1.normal_1.x)  + cross(basisZ_0, basisX_0) * float3(_S1.normal_1.y)  + cross(basisX_0, basisY_0) * float3(_S1.normal_1.z) ;
    (&output_0)->texCoord_2 = _S1.texCoord_1;
    (&output_0)->tangent_2 = _S1.tangent_1;
    (&output_0)->worldTangent_1 = float4(basisX_0 * float3(_S1.tangent_1.x)  + basisY_0 * float3(_S1.tangent_1.y)  + basisZ_0 * float3(_S1.tangent_1.z) , _S1.tangent_1.w);
    thread vertexMain_uv_normal_Result_0 _S42;
    (&_S42)->position_0 = output_0.position_2;
    (&_S42)->eyePosition_0 = output_0.eyePosition_1;
    (&_S42)->objectPosition_0 = output_0.objectPosition_1;
    (&_S42)->normal_0 = output_0.normal_2;
    (&_S42)->tint_0 = output_0.tint_2;
    (&_S42)->worldNormal_0 = output_0.worldNormal_1;
    (&_S42)->texCoord_0 = output_0.texCoord_2;
    (&_S42)->tangent_0 = output_0.tangent_2;
    (&_S42)->worldTangent_0 = output_0.worldTangent_1;
    return _S42;
}
