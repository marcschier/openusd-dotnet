#include <metal_stdlib>
#include <metal_math>
#include <metal_texture>
using namespace metal;
struct selectionMaskVertexMain_Result_0
{
    float4 position_0 [[position]];
};

struct vertexInput_0
{
    float3 position_1 [[attribute(0)]];
    float3 normal_0 [[attribute(1)]];
};

struct SLANG_ParameterGroup_SceneParameters_0
{
    matrix<float,int(4),int(4)>  objectToClip_0;
    float4 tint_0;
};

struct SelectionMaskVertexOutput_0
{
    float4 position_2;
};

[[vertex]] selectionMaskVertexMain_Result_0 selectionMaskVertexMain(vertexInput_0 _S1 [[stage_in]], SLANG_ParameterGroup_SceneParameters_0 constant* SceneParameters_0 [[buffer(0)]])
{
    thread SelectionMaskVertexOutput_0 output_0;
    (&output_0)->position_2 = (((float4(_S1.position_1, 1.0f)) * (SceneParameters_0->objectToClip_0)));
    thread selectionMaskVertexMain_Result_0 _S2;
    (&_S2)->position_0 = output_0.position_2;
    return _S2;
}
