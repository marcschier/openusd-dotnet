#include <metal_stdlib>
#include <metal_math>
#include <metal_texture>
using namespace metal;
struct vertexMain_uv_Result_0
{
    float4 position_0 [[position]];
    float3 normal_0 [[user(NORMAL)]];
    float4 tint_0 [[user(COLOR)]];
    float2 texCoord_0 [[user(TEXCOORD)]];
};

struct vertexInput_0
{
    float3 position_1 [[attribute(0)]];
    float3 normal_1 [[attribute(1)]];
    float2 texCoord_1 [[attribute(2)]];
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

struct VertexOutput_0
{
    float4 position_2;
    float3 normal_2;
    float4 tint_2;
    float2 texCoord_2;
};

[[vertex]] vertexMain_uv_Result_0 vertexMain_uv(vertexInput_0 _S1 [[stage_in]], uint instanceId_0 [[instance_id]], InstanceParameters_natural_0 device* instanceParameters_0 [[buffer(6)]])
{
    InstanceParameters_natural_0 instance_0 = instanceParameters_0[instanceId_0];
    thread VertexOutput_0 output_0;
    (&output_0)->position_2 = (((float4(_S1.position_1, 1.0f)) * (matrix<float,int(4),int(4)> (instance_0.objectToClip_0.data_0[int(0)][int(0)], instance_0.objectToClip_0.data_0[int(0)][int(1)], instance_0.objectToClip_0.data_0[int(0)][int(2)], instance_0.objectToClip_0.data_0[int(0)][int(3)], instance_0.objectToClip_0.data_0[int(1)][int(0)], instance_0.objectToClip_0.data_0[int(1)][int(1)], instance_0.objectToClip_0.data_0[int(1)][int(2)], instance_0.objectToClip_0.data_0[int(1)][int(3)], instance_0.objectToClip_0.data_0[int(2)][int(0)], instance_0.objectToClip_0.data_0[int(2)][int(1)], instance_0.objectToClip_0.data_0[int(2)][int(2)], instance_0.objectToClip_0.data_0[int(2)][int(3)], instance_0.objectToClip_0.data_0[int(3)][int(0)], instance_0.objectToClip_0.data_0[int(3)][int(1)], instance_0.objectToClip_0.data_0[int(3)][int(2)], instance_0.objectToClip_0.data_0[int(3)][int(3)]))));
    (&output_0)->normal_2 = _S1.normal_1;
    (&output_0)->tint_2 = float4(instance_0.tint_1) ;
    (&output_0)->texCoord_2 = _S1.texCoord_1;
    thread vertexMain_uv_Result_0 _S2;
    (&_S2)->position_0 = output_0.position_2;
    (&_S2)->normal_0 = output_0.normal_2;
    (&_S2)->tint_0 = output_0.tint_2;
    (&_S2)->texCoord_0 = output_0.texCoord_2;
    return _S2;
}

