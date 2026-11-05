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
    [[flat]] float pointDepthScale_0 [[user(TEXCOORD_3)]];
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
};

[[fragment]] ShadowTransmissionOutput_0 shadowTransmissionConstantFirstFragmentMain(pixelInput_0 _S1 [[stage_in]], float4 position_0 [[position]], SurfaceParameters_natural_0 device* surfaceParameters_1 [[buffer(7)]])
{
    thread KernelContext_0 kernelContext_0;
    (&kernelContext_0)->surfaceParameters_0 = surfaceParameters_1;
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
    float opacity_0 = saturate(saturate((float4(((&kernelContext_0)->surfaceParameters_0+int(0))->diffuseOpacity_0) ).w));
    if(opacity_0 <= 0.0f)
    {
        discard_fragment();
    }
    thread ShadowTransmissionOutput_0 output_0;
    (&output_0)->depth_0 = _S4;
    (&output_0)->color_0 = float4(_S4, 1.0f - opacity_0, 0.0f, 1.0f);
    return output_0;
}
