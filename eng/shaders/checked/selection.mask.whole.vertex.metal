#include <metal_stdlib>
#include <metal_math>
#include <metal_texture>
using namespace metal;
struct selectionMaskWholeVertexMain_Result_0
{
    float4 position_0 [[position]];
    float pointSize_0 [[point_size]];
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

struct SelectionMaskWholeVertexOutput_0
{
    float4 position_2;
    float pointSize_1;
};

[[vertex]] selectionMaskWholeVertexMain_Result_0 selectionMaskWholeVertexMain(vertexInput_0 _S1 [[stage_in]], SLANG_ParameterGroup_SceneParameters_0 constant* SceneParameters_0 [[buffer(0)]])
{
    thread SelectionMaskWholeVertexOutput_0 output_0;
    (&output_0)->position_2 = (((float4(_S1.position_1, 1.0f)) * (SceneParameters_0->objectToClip_0)));
    (&output_0)->pointSize_1 = 1.0f;
    thread selectionMaskWholeVertexMain_Result_0 _S2;
    (&_S2)->position_0 = output_0.position_2;
    (&_S2)->pointSize_0 = output_0.pointSize_1;
    return _S2;
}
