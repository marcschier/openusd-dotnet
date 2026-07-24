#include <metal_stdlib>
#include <metal_math>
#include <metal_texture>
using namespace metal;
struct SLANG_ParameterGroup_ComputeParameters_0
{
    uint elementCount_0;
    float scale_0;
};

struct KernelContext_0
{
    SLANG_ParameterGroup_ComputeParameters_0 constant* ComputeParameters_0;
    packed_float4 device* outputValues_0;
};

[[kernel]] void scaleMain(uint3 dispatchThreadId_0 [[thread_position_in_grid]], SLANG_ParameterGroup_ComputeParameters_0 constant* ComputeParameters_1 [[buffer(1)]], packed_float4 device* outputValues_1 [[buffer(0)]])
{
    thread KernelContext_0 kernelContext_0;
    (&kernelContext_0)->ComputeParameters_0 = ComputeParameters_1;
    (&kernelContext_0)->outputValues_0 = outputValues_1;
    uint index_0 = dispatchThreadId_0.x;
    if(index_0 < (ComputeParameters_1->elementCount_0))
    {
        packed_float4 device* _S1 = (&kernelContext_0)->outputValues_0+index_0;
        *_S1 = packed_float4((float4(*_S1)  * float4((&kernelContext_0)->ComputeParameters_0->scale_0, (&kernelContext_0)->ComputeParameters_0->scale_0, (&kernelContext_0)->ComputeParameters_0->scale_0, 1.0f))) ;
    }
    return;
}

