#include <metal_stdlib>
#include <metal_math>
#include <metal_texture>
using namespace metal;
struct vertexMain_Result_0
{
    float4 position_0 [[position]];
    float3 eyePosition_0 [[user(TEXCOORD_1)]];
    float3 normal_0 [[user(NORMAL)]];
    float4 tint_0 [[user(COLOR)]];
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

struct FrameParameters_natural_0
{
    _MatrixStorage_float4x4natural_0 clipToEye_0;
    packed_uint4 clipPlaneCount_0;
    _Array_natural_vectorx3Cfloatx2C4x3E8_0 clipPlanes_0;
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
    float3 normal_2;
    float4 tint_2;
};

[[vertex]] vertexMain_Result_0 vertexMain(vertexInput_0 _S1 [[stage_in]], uint instanceId_0 [[instance_id]], InstanceParameters_natural_0 device* instanceParameters_1 [[buffer(6)]], FrameParameters_natural_0 device* frameParameters_1 [[buffer(8)]])
{
    thread KernelContext_0 kernelContext_0;
    (&kernelContext_0)->instanceParameters_0 = instanceParameters_1;
    (&kernelContext_0)->frameParameters_0 = frameParameters_1;
    InstanceParameters_natural_0 instance_0 = instanceParameters_1[instanceId_0];
    float4 clipPosition_0 = (((float4(_S1.position_1, 1.0f)) * (matrix<float,int(4),int(4)> (instance_0.objectToClip_0.data_0[int(0)][int(0)], instance_0.objectToClip_0.data_0[int(0)][int(1)], instance_0.objectToClip_0.data_0[int(0)][int(2)], instance_0.objectToClip_0.data_0[int(0)][int(3)], instance_0.objectToClip_0.data_0[int(1)][int(0)], instance_0.objectToClip_0.data_0[int(1)][int(1)], instance_0.objectToClip_0.data_0[int(1)][int(2)], instance_0.objectToClip_0.data_0[int(1)][int(3)], instance_0.objectToClip_0.data_0[int(2)][int(0)], instance_0.objectToClip_0.data_0[int(2)][int(1)], instance_0.objectToClip_0.data_0[int(2)][int(2)], instance_0.objectToClip_0.data_0[int(2)][int(3)], instance_0.objectToClip_0.data_0[int(3)][int(0)], instance_0.objectToClip_0.data_0[int(3)][int(1)], instance_0.objectToClip_0.data_0[int(3)][int(2)], instance_0.objectToClip_0.data_0[int(3)][int(3)]))));
    float4 eyePosition_2 = (((clipPosition_0) * (matrix<float,int(4),int(4)> ((frameParameters_1+int(0))->clipToEye_0.data_0[int(0)][int(0)], (frameParameters_1+int(0))->clipToEye_0.data_0[int(0)][int(1)], (frameParameters_1+int(0))->clipToEye_0.data_0[int(0)][int(2)], (frameParameters_1+int(0))->clipToEye_0.data_0[int(0)][int(3)], (frameParameters_1+int(0))->clipToEye_0.data_0[int(1)][int(0)], (frameParameters_1+int(0))->clipToEye_0.data_0[int(1)][int(1)], (frameParameters_1+int(0))->clipToEye_0.data_0[int(1)][int(2)], (frameParameters_1+int(0))->clipToEye_0.data_0[int(1)][int(3)], (frameParameters_1+int(0))->clipToEye_0.data_0[int(2)][int(0)], (frameParameters_1+int(0))->clipToEye_0.data_0[int(2)][int(1)], (frameParameters_1+int(0))->clipToEye_0.data_0[int(2)][int(2)], (frameParameters_1+int(0))->clipToEye_0.data_0[int(2)][int(3)], (frameParameters_1+int(0))->clipToEye_0.data_0[int(3)][int(0)], (frameParameters_1+int(0))->clipToEye_0.data_0[int(3)][int(1)], (frameParameters_1+int(0))->clipToEye_0.data_0[int(3)][int(2)], (frameParameters_1+int(0))->clipToEye_0.data_0[int(3)][int(3)]))));
    thread VertexOutput_0 output_0;
    (&output_0)->position_2 = clipPosition_0;
    (&output_0)->eyePosition_1 = eyePosition_2.xyz / float3(eyePosition_2.w) ;
    (&output_0)->normal_2 = _S1.normal_1;
    (&output_0)->tint_2 = float4(instance_0.tint_1) ;
    thread vertexMain_Result_0 _S2;
    (&_S2)->position_0 = output_0.position_2;
    (&_S2)->eyePosition_0 = output_0.eyePosition_1;
    (&_S2)->normal_0 = output_0.normal_2;
    (&_S2)->tint_0 = output_0.tint_2;
    return _S2;
}

