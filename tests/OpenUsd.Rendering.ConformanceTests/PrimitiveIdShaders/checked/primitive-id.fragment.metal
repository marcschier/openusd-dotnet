#include <metal_stdlib>
#include <metal_math>
#include <metal_texture>
using namespace metal;
struct pixelOutput_0
{
    float4 output_0 [[color(0)]];
};

[[host_name("main")]] [[fragment]] pixelOutput_0 primitiveIdProbeFragmentMain(uint primitiveId_0 [[primitive_id]], float4 position_0 [[position]])
{
    pixelOutput_0 _S1 = { float4(float(primitiveId_0 & 255U), float((primitiveId_0 >> 8U) & 255U), float((primitiveId_0 >> 16U) & 255U), float((primitiveId_0 >> 24U) & 255U)) / float4(255.0f)  };
    return _S1;
}
