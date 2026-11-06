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
    float3 eyePosition_0 [[user(TEXCOORD)]];
    float3 worldNormal_0 [[user(TEXCOORD_1)]];
};

struct KernelContext_0
{
    uint32_t device* frameParameters_0;
};

[[fragment]] pixelOutput_0 normalFragmentMain(pixelInput_0 _S1 [[stage_in]], bool isFrontFace_0 [[front_facing]], float4 position_0 [[position]], uint32_t device* frameParameters_1 [[buffer(8)]])
{
    thread KernelContext_0 kernelContext_0;
    (&kernelContext_0)->frameParameters_0 = frameParameters_1;
    uint _S2 = as_type<uint>(frameParameters_1[(64U)>>2]);
    uint _S3 = as_type<uint>((&kernelContext_0)->frameParameters_0[(68U)>>2]);
    uint _S4 = as_type<uint>((&kernelContext_0)->frameParameters_0[(72U)>>2]);
    uint _S5 = as_type<uint>((&kernelContext_0)->frameParameters_0[(76U)>>2]);
    uint _S6 = min(_S2, 8U);
    uint index_0 = 0U;
    for(;;)
    {
        if(index_0 < _S6)
        {
        }
        else
        {
            break;
        }
        uint _S7 = 80U + index_0 * 16U;
        float _S8 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S7)>>2]);
        float _S9 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S7 + 4U)>>2]);
        float _S10 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S7 + 8U)>>2]);
        float _S11 = as_type<float>((&kernelContext_0)->frameParameters_0[(_S7 + 12U)>>2]);
        if((dot(float3(_S8, _S9, _S10), _S1.eyePosition_0) + _S11) < 0.0f)
        {
            discard_fragment();
        }
        index_0 = index_0 + 1U;
    }
    float lengthSquared_0 = dot(_S1.worldNormal_0, _S1.worldNormal_0);
    if(!(lengthSquared_0 > 1.00000000317107685e-30f))
    {
        pixelOutput_0 _S12 = { float4(0.0f, 0.0f, 0.0f, 1.0f) };
        return _S12;
    }
    float3 normal_0 = _S1.worldNormal_0 * float3(rsqrt(lengthSquared_0)) ;
    float3 normal_1;
    if(isFrontFace_0)
    {
        normal_1 = normal_0;
    }
    else
    {
        normal_1 = - normal_0;
    }
    pixelOutput_0 _S13 = { float4(normal_1, 1.0f) };
    return _S13;
}
