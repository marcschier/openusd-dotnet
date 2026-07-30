#include <metal_stdlib>
#include <metal_math>
#include <metal_texture>
using namespace metal;
struct pixelOutput_0
{
    float4 output_0 [[color(0)]];
};

struct pixelInput_0
{
    float3 normal_0 [[user(NORMAL)]];
    float2 texCoord_0 [[user(TEXCOORD)]];
};

struct SLANG_ParameterGroup_SceneParameters_0
{
    matrix<float,int(4),int(4)>  objectToClip_0;
    float4 tint_0;
};

[[fragment]] pixelOutput_0 fragmentMain_uv(pixelInput_0 _S1 [[stage_in]], float4 position_0 [[position]], SLANG_ParameterGroup_SceneParameters_0 constant* SceneParameters_0 [[buffer(0)]])
{
    pixelOutput_0 _S2 = { float4(abs(_S1.normal_0), 1.0f) * SceneParameters_0->tint_0 };
    return _S2;
}

