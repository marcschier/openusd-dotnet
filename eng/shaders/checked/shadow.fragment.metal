#include <metal_stdlib>
#include <metal_math>
#include <metal_texture>
using namespace metal;
struct pixelOutput_0
{
    float output_0 [[color(0)]];
};

struct pixelInput_0
{
    float lightDepth_0 [[user(TEXCOORD_2)]];
    [[flat]] float pointDepthScale_0 [[user(TEXCOORD_3)]];
};

[[fragment]] pixelOutput_0 shadowFragmentMain(pixelInput_0 _S1 [[stage_in]], float4 position_0 [[position]])
{
    float _S2 = position_0.z;
    bool _S3 = (_S1.pointDepthScale_0) > 0.0f;
    float _S4;
    if(_S3)
    {
        _S4 = _S1.lightDepth_0 * _S1.pointDepthScale_0;
    }
    else
    {
        _S4 = _S2;
    }
    bool _S5;
    if(_S3)
    {
        _S5 = _S4 > 1.0f;
    }
    else
    {
        _S5 = false;
    }
    if(_S5)
    {
        discard_fragment();
    }
    pixelOutput_0 _S6 = { _S4 };
    return _S6;
}
