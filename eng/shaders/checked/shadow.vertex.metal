#include <metal_stdlib>
#include <metal_math>
#include <metal_texture>
using namespace metal;
struct shadowVertexMain_Result_0
{
    float4 position_0 [[position]];
};

struct vertexInput_0
{
    float3 position_1 [[attribute(0)]];
    float3 normal_0 [[attribute(1)]];
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

struct ShadowVertexOutput_0
{
    float4 position_2;
};

[[vertex]] shadowVertexMain_Result_0 shadowVertexMain(vertexInput_0 _S1 [[stage_in]], uint instanceId_0 [[instance_id]], ShadowInstanceParameters_natural_0 device* shadowInstanceParameters_0 [[buffer(6)]])
{
    ShadowInstanceParameters_natural_0 instance_0 = shadowInstanceParameters_0[instanceId_0];
    thread ShadowVertexOutput_0 output_0;
    (&output_0)->position_2 = (((float4(_S1.position_1, 1.0f)) * (matrix<float,int(4),int(4)> (instance_0.objectToLightClip_0.data_0[int(0)][int(0)], instance_0.objectToLightClip_0.data_0[int(0)][int(1)], instance_0.objectToLightClip_0.data_0[int(0)][int(2)], instance_0.objectToLightClip_0.data_0[int(0)][int(3)], instance_0.objectToLightClip_0.data_0[int(1)][int(0)], instance_0.objectToLightClip_0.data_0[int(1)][int(1)], instance_0.objectToLightClip_0.data_0[int(1)][int(2)], instance_0.objectToLightClip_0.data_0[int(1)][int(3)], instance_0.objectToLightClip_0.data_0[int(2)][int(0)], instance_0.objectToLightClip_0.data_0[int(2)][int(1)], instance_0.objectToLightClip_0.data_0[int(2)][int(2)], instance_0.objectToLightClip_0.data_0[int(2)][int(3)], instance_0.objectToLightClip_0.data_0[int(3)][int(0)], instance_0.objectToLightClip_0.data_0[int(3)][int(1)], instance_0.objectToLightClip_0.data_0[int(3)][int(2)], instance_0.objectToLightClip_0.data_0[int(3)][int(3)]))));
    thread shadowVertexMain_Result_0 _S2;
    (&_S2)->position_0 = output_0.position_2;
    return _S2;
}
