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
    float2 textureCoordinate_0 [[user(TEXCOORD)]];
};

struct SLANG_ParameterGroup_SelectionOutlineParameters_0
{
    float4 outlineColor_0;
    float2 inverseViewportSize_0;
    float outlineWidthPixels_0;
    float depthEpsilon_0;
};

struct KernelContext_0
{
    texture2d<float, access::sample> selectionMask_0;
    sampler selectionSampler_0;
    texture2d<float, access::sample> visibleDepth_0;
    SLANG_ParameterGroup_SelectionOutlineParameters_0 constant* SelectionOutlineParameters_0;
};

[[fragment]] pixelOutput_0 selectionOutlineFragmentMain(pixelInput_0 _S1 [[stage_in]], float4 position_0 [[position]], texture2d<float, access::sample> selectionMask_1 [[texture(0)]], sampler selectionSampler_1 [[sampler(0)]], texture2d<float, access::sample> visibleDepth_1 [[texture(1)]], SLANG_ParameterGroup_SelectionOutlineParameters_0 constant* SelectionOutlineParameters_1 [[buffer(0)]])
{
    thread KernelContext_0 kernelContext_0;
    (&kernelContext_0)->selectionMask_0 = selectionMask_1;
    (&kernelContext_0)->selectionSampler_0 = selectionSampler_1;
    (&kernelContext_0)->visibleDepth_0 = visibleDepth_1;
    (&kernelContext_0)->SelectionOutlineParameters_0 = SelectionOutlineParameters_1;
    if((((selectionMask_1).sample((selectionSampler_1), (_S1.textureCoordinate_0), level((0.0f)))).x) >= 0.5f)
    {
        pixelOutput_0 _S2 = { float4(0.0f, 0.0f, 0.0f, 0.0f) };
        return _S2;
    }
    float _S3 = (((&kernelContext_0)->visibleDepth_0).sample(((&kernelContext_0)->selectionSampler_0), (_S1.textureCoordinate_0), level((0.0f))).x);
    int radius_0 = int(ceil((&kernelContext_0)->SelectionOutlineParameters_0->outlineWidthPixels_0));
    int _S4 = - radius_0;
    float edge_0 = 0.0f;
    int y_0 = _S4;
    for(;;)
    {
        if(y_0 <= radius_0)
        {
        }
        else
        {
            break;
        }
        float edge_1 = edge_0;
        int x_0 = _S4;
        for(;;)
        {
            if(x_0 <= radius_0)
            {
            }
            else
            {
                break;
            }
            float2 offset_0 = float2(float(x_0), float(y_0));
            if((dot(offset_0, offset_0)) > ((&kernelContext_0)->SelectionOutlineParameters_0->outlineWidthPixels_0 * (&kernelContext_0)->SelectionOutlineParameters_0->outlineWidthPixels_0))
            {
                x_0 = x_0 + int(1);
                continue;
            }
            float2 sampleCoordinate_0 = _S1.textureCoordinate_0 + offset_0 * (&kernelContext_0)->SelectionOutlineParameters_0->inverseViewportSize_0;
            if(((((&kernelContext_0)->selectionMask_0).sample(((&kernelContext_0)->selectionSampler_0), (sampleCoordinate_0), level((0.0f)))).x) < 0.5f)
            {
                x_0 = x_0 + int(1);
                continue;
            }
            float edge_2;
            if((_S3 + (&kernelContext_0)->SelectionOutlineParameters_0->depthEpsilon_0) >= (((&kernelContext_0)->visibleDepth_0).sample(((&kernelContext_0)->selectionSampler_0), (sampleCoordinate_0), level((0.0f))).x))
            {
                edge_2 = 1.0f;
            }
            else
            {
                edge_2 = edge_1;
            }
            edge_1 = edge_2;
            x_0 = x_0 + int(1);
        }
        int y_1 = y_0 + int(1);
        edge_0 = edge_1;
        y_0 = y_1;
    }
    pixelOutput_0 _S5 = { float4((&kernelContext_0)->SelectionOutlineParameters_0->outlineColor_0.xyz, (&kernelContext_0)->SelectionOutlineParameters_0->outlineColor_0.w * edge_0) };
    return _S5;
}

