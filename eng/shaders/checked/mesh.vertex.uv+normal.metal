#include <metal_stdlib>
#include <metal_math>
#include <metal_texture>
using namespace metal;
struct vertexMain_uv_normal_Result_0
{
    float4 position_0 [[position]];
    float3 normal_0 [[user(NORMAL)]];
    float2 texCoord_0 [[user(TEXCOORD)]];
    float4 tangent_0 [[user(TANGENT)]];
};

struct vertexInput_0
{
    float3 position_1 [[attribute(0)]];
    float3 normal_1 [[attribute(1)]];
    float2 texCoord_1 [[attribute(2)]];
    float4 tangent_1 [[attribute(3)]];
};

struct SLANG_ParameterGroup_SceneParameters_0
{
    matrix<float,int(4),int(4)>  objectToClip_0;
    float4 tint_0;
};

struct VertexOutput_0
{
    float4 position_2;
    float3 normal_2;
    float2 texCoord_2;
    float4 tangent_2;
};

[[vertex]] vertexMain_uv_normal_Result_0 vertexMain_uv_normal(vertexInput_0 _S1 [[stage_in]], SLANG_ParameterGroup_SceneParameters_0 constant* SceneParameters_0 [[buffer(0)]])
{
    thread VertexOutput_0 output_0;
    (&output_0)->position_2 = (((float4(_S1.position_1, 1.0f)) * (SceneParameters_0->objectToClip_0)));
    (&output_0)->normal_2 = _S1.normal_1;
    (&output_0)->texCoord_2 = _S1.texCoord_1;
    (&output_0)->tangent_2 = _S1.tangent_1;
    thread vertexMain_uv_normal_Result_0 _S2;
    (&_S2)->position_0 = output_0.position_2;
    (&_S2)->normal_0 = output_0.normal_2;
    (&_S2)->texCoord_0 = output_0.texCoord_2;
    (&_S2)->tangent_0 = output_0.tangent_2;
    return _S2;
}

