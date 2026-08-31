#include <metal_stdlib>
#include <metal_math>
#include <metal_texture>
using namespace metal;
struct selectionOutlineVertexMain_Result_0
{
    float4 position_0 [[position]];
    float2 textureCoordinate_0 [[user(TEXCOORD)]];
};

struct SelectionOutlineVertexOutput_0
{
    float4 position_1;
    float2 textureCoordinate_1;
};

[[vertex]] selectionOutlineVertexMain_Result_0 selectionOutlineVertexMain(uint vertexId_0 [[vertex_id]])
{
    float2 textureCoordinate_2 = float2(float((vertexId_0 << 1U) & 2U), float(vertexId_0 & 2U));
    thread SelectionOutlineVertexOutput_0 output_0;
    (&output_0)->position_1 = float4(textureCoordinate_2 * float2(2.0f, -2.0f) + float2(-1.0f, 1.0f), 0.0f, 1.0f);
    (&output_0)->textureCoordinate_1 = textureCoordinate_2;
    thread selectionOutlineVertexMain_Result_0 _S1;
    (&_S1)->position_0 = output_0.position_1;
    (&_S1)->textureCoordinate_0 = output_0.textureCoordinate_1;
    return _S1;
}
