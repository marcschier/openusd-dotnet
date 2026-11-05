#include <metal_stdlib>
#include <metal_math>
#include <metal_texture>
using namespace metal;
struct ShadowTransmissionOutput_0
{
    float4 color_0 [[color(0)]];
    float depth_0 [[depth(any)]];
};

struct pixelInput_0
{
    float lightDepth_0 [[user(TEXCOORD_2)]];
    [[flat]] float2 pointDepth_0 [[user(TEXCOORD_3)]];
};

struct SurfaceParameters_natural_0
{
    packed_float4 diffuseOpacity_0;
    packed_float4 emissiveOcclusion_0;
    packed_float4 specularIor_0;
    packed_float4 metallicRoughnessThresholdWorkflow_0;
    packed_float4 clearcoatShaded_0;
    packed_float4 lightDirectionIntensity_0;
    packed_float4 lightColorAmbient_0;
    packed_float4 reserved_0;
    packed_float4 textureControls_0;
    packed_float4 uvTransformRow0_0;
    packed_float4 uvTransformRow1_0;
    packed_float4 compositeControls_0;
    packed_float4 domeLinkControls_0;
    packed_uint4 lightLinkMask_0;
    packed_uint4 shadowLinkMask_0;
    packed_float4 detailUvTransformRow0_0;
    packed_float4 detailUvTransformRow1_0;
    packed_float4 alternateUvTransformRow0_0;
    packed_float4 alternateUvTransformRow1_0;
};

struct KernelContext_0
{
    SurfaceParameters_natural_0 device* surfaceParameters_0;
    texture2d<float, access::sample> previousShadowLayer_0;
};

[[fragment]] ShadowTransmissionOutput_0 shadowTransmissionConstantFragmentMain(pixelInput_0 _S1 [[stage_in]], float4 position_0 [[position]], SurfaceParameters_natural_0 device* surfaceParameters_1 [[buffer(7)]], texture2d<float, access::sample> previousShadowLayer_1 [[texture(24)]])
{
    thread KernelContext_0 kernelContext_0;
    (&kernelContext_0)->surfaceParameters_0 = surfaceParameters_1;
    (&kernelContext_0)->previousShadowLayer_0 = previousShadowLayer_1;
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
    float opacity_0 = saturate(saturate((float4(((&kernelContext_0)->surfaceParameters_0+int(0))->diffuseOpacity_0) ).w));
    int3 _S7 = int3(int2(position_0.xy), int(0));
    float previousDepth_0 = (((&kernelContext_0)->previousShadowLayer_0).read(vec<uint,2>(((_S7)).xy), uint(((_S7)).z))).x;
    if(opacity_0 <= 0.0f)
    {
        _S6 = true;
    }
    else
    {
        _S6 = previousDepth_0 < 0.0f;
    }
    if(_S6)
    {
        _S6 = true;
    }
    else
    {
        _S6 = _S5 <= previousDepth_0;
    }
    if(_S6)
    {
        discard_fragment();
    }
    thread ShadowTransmissionOutput_0 output_0;
    (&output_0)->depth_0 = _S5;
    (&output_0)->color_0 = float4(_S5, 1.0f - opacity_0, 0.0f, 1.0f);
    return output_0;
}
