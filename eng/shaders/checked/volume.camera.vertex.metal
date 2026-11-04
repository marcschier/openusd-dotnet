#include <metal_stdlib>
#include <metal_math>
#include <metal_texture>
using namespace metal;
struct volumeCameraVertexMain_Result_0
{
    float4 position_0 [[position]];
};

struct vertexInput_0
{
    float3 position_1 [[attribute(0)]];
    float3 normal_0 [[attribute(1)]];
};

struct VolumeCameraVertexOutput_0
{
    float4 position_2;
};

[[vertex]] volumeCameraVertexMain_Result_0 volumeCameraVertexMain(vertexInput_0 _S1 [[stage_in]])
{
    thread VolumeCameraVertexOutput_0 output_0;
    (&output_0)->position_2 = float4(_S1.position_1, 1.0f);
    thread volumeCameraVertexMain_Result_0 _S2;
    (&_S2)->position_0 = output_0.position_2;
    return _S2;
}
