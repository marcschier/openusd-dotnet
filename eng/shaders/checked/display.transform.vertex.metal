#include <metal_stdlib>
#include <metal_math>
#include <metal_texture>
using namespace metal;
struct displayTransformVertexMain_Result_0
{
    float4 position_0 [[position]];
    float2 textureCoordinate_0 [[user(TEXCOORD)]];
};

struct DisplayTransformVertexOutput_0
{
    float4 position_1;
    float2 textureCoordinate_1;
};

[[vertex]] displayTransformVertexMain_Result_0 displayTransformVertexMain(uint vertexId_0 [[vertex_id]])
{
    float2 textureCoordinate_2 = float2(float((vertexId_0 << 1U) & 2U), float(vertexId_0 & 2U));
    thread DisplayTransformVertexOutput_0 output_0;
    (&output_0)->position_1 = float4(textureCoordinate_2 * float2(2.0f, -2.0f) + float2(-1.0f, 1.0f), 0.0f, 1.0f);
    (&output_0)->textureCoordinate_1 = textureCoordinate_2;
    thread displayTransformVertexMain_Result_0 _S1;
    (&_S1)->position_0 = output_0.position_1;
    (&_S1)->textureCoordinate_0 = output_0.textureCoordinate_1;
    return _S1;
}
