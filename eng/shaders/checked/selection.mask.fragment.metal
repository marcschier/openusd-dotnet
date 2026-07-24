#include <metal_stdlib>
#include <metal_math>
#include <metal_texture>
using namespace metal;
struct pixelOutput_0
{
    float4 output_0 [[color(0)]];
};

[[fragment]] pixelOutput_0 selectionMaskFragmentMain(float4 position_0 [[position]])
{
    pixelOutput_0 _S1 = { float4(1.0f, 1.0f, 1.0f, 1.0f) };
    return _S1;
}

