#include <metal_stdlib>
#include <metal_math>
#include <metal_texture>
using namespace metal;
struct pixelOutput_0
{
    float4 output_0 [[color(0)]];
};

struct SLANG_ParameterGroup_PickParameters_0
{
    uint4 pickToken_0;
};

[[fragment]] pixelOutput_0 pickFragmentMain(uint primitiveId_0 [[primitive_id]], float4 position_0 [[position]], SLANG_ParameterGroup_PickParameters_0 constant* PickParameters_0 [[buffer(1)]])
{
    uint token_0 = PickParameters_0->pickToken_0.x + primitiveId_0;
    pixelOutput_0 _S1 = { float4(float(token_0 & 255U), float((token_0 >> 8U) & 255U), float((token_0 >> 16U) & 255U), float((token_0 >> 24U) & 255U)) / float4(255.0f)  };
    return _S1;
}

