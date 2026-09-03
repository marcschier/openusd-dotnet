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

struct SLANG_ParameterGroup_DisplayTransformParameters_0
{
    float4 exposureShaper_0;
    float4 lutGrid_0;
};

struct KernelContext_0
{
    SLANG_ParameterGroup_DisplayTransformParameters_0 constant* DisplayTransformParameters_0;
    texture2d<float, access::sample> sceneColor_0;
    sampler displaySampler_0;
    texture2d<float, access::sample> displayLut_0;
};

[[fragment]] pixelOutput_0 displayTransformFragmentMain(pixelInput_0 _S1 [[stage_in]], float4 position_0 [[position]], SLANG_ParameterGroup_DisplayTransformParameters_0 constant* DisplayTransformParameters_1 [[buffer(0)]], texture2d<float, access::sample> sceneColor_1 [[texture(0)]], sampler displaySampler_1 [[sampler(0)]], texture2d<float, access::sample> displayLut_1 [[texture(1)]])
{
    thread KernelContext_0 kernelContext_0;
    (&kernelContext_0)->DisplayTransformParameters_0 = DisplayTransformParameters_1;
    (&kernelContext_0)->sceneColor_0 = sceneColor_1;
    (&kernelContext_0)->displaySampler_0 = displaySampler_1;
    (&kernelContext_0)->displayLut_0 = displayLut_1;
    float _S2 = _S1.textureCoordinate_0.x;
    float _S3;
    if((DisplayTransformParameters_1->lutGrid_0.w) > 0.5f)
    {
        _S3 = 1.0f - _S1.textureCoordinate_0.y;
    }
    else
    {
        _S3 = _S1.textureCoordinate_0.y;
    }
    float4 source_0 = (((&kernelContext_0)->sceneColor_0).sample(((&kernelContext_0)->displaySampler_0), (float2(_S2, _S3)), level((0.0f))));
    float shaperMinLog2_0 = (&kernelContext_0)->DisplayTransformParameters_0->exposureShaper_0.y;
    float lutMaxIndex_0 = DisplayTransformParameters_1->lutGrid_0.z;
    float inverseTileCount_0 = DisplayTransformParameters_1->lutGrid_0.y;
    float3 shaped_0 = saturate((log2(max(source_0.xyz * float3((&kernelContext_0)->DisplayTransformParameters_0->exposureShaper_0.x) , exp2(float3(shaperMinLog2_0, shaperMinLog2_0, shaperMinLog2_0)))) - float3(shaperMinLog2_0) ) / float3((&kernelContext_0)->DisplayTransformParameters_0->exposureShaper_0.z) );
    float bluePosition_0 = shaped_0.z * lutMaxIndex_0;
    float lowerTile_0 = floor(bluePosition_0);
    float tileOffset_0 = (shaped_0.x * lutMaxIndex_0 + 0.5f) * DisplayTransformParameters_1->lutGrid_0.x;
    float verticalCoordinate_0 = (shaped_0.y * lutMaxIndex_0 + 0.5f) * inverseTileCount_0;
    pixelOutput_0 _S4 = { float4(mix((((&kernelContext_0)->displayLut_0).sample(((&kernelContext_0)->displaySampler_0), (float2(lowerTile_0 * inverseTileCount_0 + tileOffset_0, verticalCoordinate_0)), level((0.0f)))).xyz, (((&kernelContext_0)->displayLut_0).sample(((&kernelContext_0)->displaySampler_0), (float2(min(lowerTile_0 + 1.0f, lutMaxIndex_0) * inverseTileCount_0 + tileOffset_0, verticalCoordinate_0)), level((0.0f)))).xyz, float3((bluePosition_0 - lowerTile_0)) ), saturate(source_0.w)) };
    return _S4;
}
