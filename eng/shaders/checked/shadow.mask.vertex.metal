#include <metal_stdlib>
#include <metal_math>
#include <metal_texture>
using namespace metal;
struct shadowMaskedVertexMain_Result_0
{
    float4 position_0 [[position]];
    float2 texCoord_0 [[user(TEXCOORD)]];
    float2 texCoord1_0 [[user(TEXCOORD_1)]];
    float lightDepth_0 [[user(TEXCOORD_2)]];
    float pointDepthScale_0 [[user(TEXCOORD_3)]];
};

struct vertexInput_0
{
    float3 position_1 [[attribute(0)]];
    float3 normal_0 [[attribute(1)]];
    float2 texCoord_1 [[attribute(2)]];
    float2 texCoord1_1 [[attribute(3)]];
};

struct _MatrixStorage_float4x4natural_0
{
    array<packed_float4, int(4)> data_0;
};

struct ShadowInstanceParameters_natural_0
{
    _MatrixStorage_float4x4natural_0 objectToLightClip_0;
    packed_float4 reserved_0;
};

struct ShadowMaskedVertexOutput_0
{
    float4 position_2;
    float2 texCoord_2;
    float2 texCoord1_2;
    float lightDepth_1;
    [[flat]] float pointDepthScale_1;
};

[[vertex]] shadowMaskedVertexMain_Result_0 shadowMaskedVertexMain(vertexInput_0 _S1 [[stage_in]], uint instanceId_0 [[instance_id]], uint base_instance_0 [[base_instance]], ShadowInstanceParameters_natural_0 device* shadowInstanceParameters_0 [[buffer(6)]])
{
    ShadowInstanceParameters_natural_0 instance_0 = shadowInstanceParameters_0[instanceId_0 - base_instance_0];
    thread ShadowMaskedVertexOutput_0 output_0;
    float4 _S2 = (((float4(_S1.position_1, 1.0f)) * (matrix<float,int(4),int(4)> (instance_0.objectToLightClip_0.data_0[int(0)][int(0)], instance_0.objectToLightClip_0.data_0[int(0)][int(1)], instance_0.objectToLightClip_0.data_0[int(0)][int(2)], instance_0.objectToLightClip_0.data_0[int(0)][int(3)], instance_0.objectToLightClip_0.data_0[int(1)][int(0)], instance_0.objectToLightClip_0.data_0[int(1)][int(1)], instance_0.objectToLightClip_0.data_0[int(1)][int(2)], instance_0.objectToLightClip_0.data_0[int(1)][int(3)], instance_0.objectToLightClip_0.data_0[int(2)][int(0)], instance_0.objectToLightClip_0.data_0[int(2)][int(1)], instance_0.objectToLightClip_0.data_0[int(2)][int(2)], instance_0.objectToLightClip_0.data_0[int(2)][int(3)], instance_0.objectToLightClip_0.data_0[int(3)][int(0)], instance_0.objectToLightClip_0.data_0[int(3)][int(1)], instance_0.objectToLightClip_0.data_0[int(3)][int(2)], instance_0.objectToLightClip_0.data_0[int(3)][int(3)]))));
    (&output_0)->position_2 = _S2;
    (&output_0)->lightDepth_1 = _S2.w;
    (&output_0)->pointDepthScale_1 = (float4(instance_0.reserved_0) ).x;
    (&output_0)->texCoord_2 = _S1.texCoord_1;
    (&output_0)->texCoord1_2 = _S1.texCoord1_1;
    thread shadowMaskedVertexMain_Result_0 _S3;
    (&_S3)->position_0 = output_0.position_2;
    (&_S3)->texCoord_0 = output_0.texCoord_2;
    (&_S3)->texCoord1_0 = output_0.texCoord1_2;
    (&_S3)->lightDepth_0 = output_0.lightDepth_1;
    (&_S3)->pointDepthScale_0 = output_0.pointDepthScale_1;
    return _S3;
}
