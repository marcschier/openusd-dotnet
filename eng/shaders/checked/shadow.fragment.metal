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
    [[flat]] float2 pointDepth_0 [[user(TEXCOORD_3)]];
};

[[fragment]] pixelOutput_0 shadowFragmentMain(pixelInput_0 _S1 [[stage_in]], float4 position_0 [[position]])
{
    float _S2 = position_0.z;
    float _S3 = _S1.pointDepth_0.x;
    bool _S4 = _S3 > 0.0f;
    float _S5;
    if(_S4)
    {
        _S5 = _S1.lightDepth_0 * _S3;
    }
    else
    {
        _S5 = _S2;
    }
    bool _S6;
    if(_S4)
    {
        _S6 = _S5 > (_S1.pointDepth_0.y);
    }
    else
    {
        _S6 = false;
    }
    if(_S6)
    {
        discard_fragment();
    }
    pixelOutput_0 _S7 = { _S5 };
    return _S7;
}
