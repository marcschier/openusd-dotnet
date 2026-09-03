#include <metal_stdlib>
#include <metal_math>
#include <metal_texture>
using namespace metal;
struct vertexMain_Result_0
{
    float4 position_0 [[position]];
    float3 eyePosition_0 [[user(TEXCOORD_1)]];
    float3 objectPosition_0 [[user(TEXCOORD_2)]];
    float3 normal_0 [[user(NORMAL)]];
    float4 tint_0 [[user(COLOR)]];
    float3 worldNormal_0 [[user(TEXCOORD_3)]];
};

struct vertexInput_0
{
    float3 position_1 [[attribute(0)]];
    float3 normal_1 [[attribute(1)]];
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

struct _Array_natural_vectorx3Cfloatx2C4x3E8_0
{
    array<packed_float4, int(8)> data_1;
};

struct _Array_natural_matrixx3Cfloatx2C4x2C4x3E4_0
{
    array<_MatrixStorage_float4x4natural_0, int(4)> data_2;
};

struct _Array_natural_vectorx3Cfloatx2C4x3E4_0
{
    array<packed_float4, int(4)> data_3;
};

struct FrameParameters_natural_0
{
    _MatrixStorage_float4x4natural_0 clipToEye_0;
    packed_uint4 clipPlaneCount_0;
    _Array_natural_vectorx3Cfloatx2C4x3E8_0 clipPlanes_0;
    packed_float4 ambientLight_0;
    _Array_natural_vectorx3Cfloatx2C4x3E8_0 lightPositionType_0;
    _Array_natural_vectorx3Cfloatx2C4x3E8_0 lightDirectionRadius_0;
    _Array_natural_vectorx3Cfloatx2C4x3E8_0 lightColorIntensity_0;
    _Array_natural_vectorx3Cfloatx2C4x3E8_0 lightControls_0;
    _Array_natural_vectorx3Cfloatx2C4x3E8_0 lightTangentShapeX_0;
    _Array_natural_vectorx3Cfloatx2C4x3E8_0 lightBitangentShapeY_0;
    _MatrixStorage_float4x4natural_0 eyeToWorld_0;
    _Array_natural_matrixx3Cfloatx2C4x2C4x3E4_0 shadowWorldToLightClip_0;
    _Array_natural_vectorx3Cfloatx2C4x3E4_0 shadowTile_0;
    _Array_natural_vectorx3Cfloatx2C4x3E4_0 shadowControls_0;
    _Array_natural_vectorx3Cfloatx2C4x3E8_0 shadowSlots_0;
    packed_float4 environmentControls_0;
    packed_float4 domeControls_0;
    _Array_natural_vectorx3Cfloatx2C4x3E8_0 domeAmbient_0;
    _Array_natural_vectorx3Cfloatx2C4x3E8_0 domeEnvironment_0;
};

struct KernelContext_0
{
    InstanceParameters_natural_0 device* instanceParameters_0;
    FrameParameters_natural_0 device* frameParameters_0;
};

struct VertexOutput_0
{
    float4 position_2;
    float3 eyePosition_1;
    float3 objectPosition_1;
    float3 normal_2;
    float4 tint_2;
    float3 worldNormal_1;
};

[[vertex]] vertexMain_Result_0 vertexMain(vertexInput_0 _S1 [[stage_in]], uint instanceId_0 [[instance_id]], InstanceParameters_natural_0 device* instanceParameters_1 [[buffer(6)]], FrameParameters_natural_0 device* frameParameters_1 [[buffer(8)]])
{
    thread KernelContext_0 kernelContext_0;
    (&kernelContext_0)->instanceParameters_0 = instanceParameters_1;
    (&kernelContext_0)->frameParameters_0 = frameParameters_1;
    InstanceParameters_natural_0 instance_0 = instanceParameters_1[instanceId_0];
    FrameParameters_natural_0 device* _S2 = frameParameters_1+int(0);
    matrix<float,int(4),int(4)>  _S3 = matrix<float,int(4),int(4)> (instance_0.objectToClip_0.data_0[int(0)][int(0)], instance_0.objectToClip_0.data_0[int(0)][int(1)], instance_0.objectToClip_0.data_0[int(0)][int(2)], instance_0.objectToClip_0.data_0[int(0)][int(3)], instance_0.objectToClip_0.data_0[int(1)][int(0)], instance_0.objectToClip_0.data_0[int(1)][int(1)], instance_0.objectToClip_0.data_0[int(1)][int(2)], instance_0.objectToClip_0.data_0[int(1)][int(3)], instance_0.objectToClip_0.data_0[int(2)][int(0)], instance_0.objectToClip_0.data_0[int(2)][int(1)], instance_0.objectToClip_0.data_0[int(2)][int(2)], instance_0.objectToClip_0.data_0[int(2)][int(3)], instance_0.objectToClip_0.data_0[int(3)][int(0)], instance_0.objectToClip_0.data_0[int(3)][int(1)], instance_0.objectToClip_0.data_0[int(3)][int(2)], instance_0.objectToClip_0.data_0[int(3)][int(3)]);
    float4 clipPosition_0 = (((float4(_S1.position_1, 1.0f)) * (_S3)));
    matrix<float,int(4),int(4)>  _S4 = matrix<float,int(4),int(4)> (_S2->clipToEye_0.data_0[int(0)][int(0)], _S2->clipToEye_0.data_0[int(0)][int(1)], _S2->clipToEye_0.data_0[int(0)][int(2)], _S2->clipToEye_0.data_0[int(0)][int(3)], _S2->clipToEye_0.data_0[int(1)][int(0)], _S2->clipToEye_0.data_0[int(1)][int(1)], _S2->clipToEye_0.data_0[int(1)][int(2)], _S2->clipToEye_0.data_0[int(1)][int(3)], _S2->clipToEye_0.data_0[int(2)][int(0)], _S2->clipToEye_0.data_0[int(2)][int(1)], _S2->clipToEye_0.data_0[int(2)][int(2)], _S2->clipToEye_0.data_0[int(2)][int(3)], _S2->clipToEye_0.data_0[int(3)][int(0)], _S2->clipToEye_0.data_0[int(3)][int(1)], _S2->clipToEye_0.data_0[int(3)][int(2)], _S2->clipToEye_0.data_0[int(3)][int(3)]);
    float4 eyePosition_2 = (((clipPosition_0) * (_S4)));
    thread VertexOutput_0 output_0;
    (&output_0)->position_2 = clipPosition_0;
    (&output_0)->eyePosition_1 = eyePosition_2.xyz / float3(eyePosition_2.w) ;
    (&output_0)->objectPosition_1 = _S1.position_1;
    (&output_0)->normal_2 = _S1.normal_1;
    (&output_0)->tint_2 = float4(instance_0.tint_1) ;
    matrix<float,int(4),int(4)>  objectToWorld_0 = ((((((_S3) * (_S4)))) * (matrix<float,int(4),int(4)> (_S2->eyeToWorld_0.data_0[int(0)][int(0)], _S2->eyeToWorld_0.data_0[int(0)][int(1)], _S2->eyeToWorld_0.data_0[int(0)][int(2)], _S2->eyeToWorld_0.data_0[int(0)][int(3)], _S2->eyeToWorld_0.data_0[int(1)][int(0)], _S2->eyeToWorld_0.data_0[int(1)][int(1)], _S2->eyeToWorld_0.data_0[int(1)][int(2)], _S2->eyeToWorld_0.data_0[int(1)][int(3)], _S2->eyeToWorld_0.data_0[int(2)][int(0)], _S2->eyeToWorld_0.data_0[int(2)][int(1)], _S2->eyeToWorld_0.data_0[int(2)][int(2)], _S2->eyeToWorld_0.data_0[int(2)][int(3)], _S2->eyeToWorld_0.data_0[int(3)][int(0)], _S2->eyeToWorld_0.data_0[int(3)][int(1)], _S2->eyeToWorld_0.data_0[int(3)][int(2)], _S2->eyeToWorld_0.data_0[int(3)][int(3)]))));
    float3 basisX_0 = float3(objectToWorld_0[int(0)][int(0)], objectToWorld_0[int(1)][int(0)], objectToWorld_0[int(2)][int(0)]);
    float3 basisY_0 = float3(objectToWorld_0[int(0)][int(1)], objectToWorld_0[int(1)][int(1)], objectToWorld_0[int(2)][int(1)]);
    float3 basisZ_0 = float3(objectToWorld_0[int(0)][int(2)], objectToWorld_0[int(1)][int(2)], objectToWorld_0[int(2)][int(2)]);
    (&output_0)->worldNormal_1 = cross(basisY_0, basisZ_0) * float3(_S1.normal_1.x)  + cross(basisZ_0, basisX_0) * float3(_S1.normal_1.y)  + cross(basisX_0, basisY_0) * float3(_S1.normal_1.z) ;
    thread vertexMain_Result_0 _S5;
    (&_S5)->position_0 = output_0.position_2;
    (&_S5)->eyePosition_0 = output_0.eyePosition_1;
    (&_S5)->objectPosition_0 = output_0.objectPosition_1;
    (&_S5)->normal_0 = output_0.normal_2;
    (&_S5)->tint_0 = output_0.tint_2;
    (&_S5)->worldNormal_0 = output_0.worldNormal_1;
    return _S5;
}
