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
    [[flat]] float2 ordinal_0 [[user(TEXCOORD)]];
};

[[host_name("main")]] [[fragment]] pixelOutput_0 flatOrdinalTransportFragmentMain(pixelInput_0 _S1 [[stage_in]], float4 position_0 [[position]])
{
    uint ordinal_1 = uint(_S1.ordinal_0.x);
    pixelOutput_0 _S2 = { float4(float(ordinal_1 & 255U), float((ordinal_1 >> 8U) & 255U), float((ordinal_1 >> 16U) & 255U), float((ordinal_1 >> 24U) & 255U)) / float4(255.0f)  };
    return _S2;
}
