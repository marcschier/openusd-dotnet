#include <metal_stdlib>
#include <metal_math>
#include <metal_texture>
using namespace metal;
struct pixelOutput_0
{
    float4 output_0 [[color(0)]];
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

[[fragment]] pixelOutput_0 shadowTransmissionConstantFragmentMain(float4 position_0 [[position]], SurfaceParameters_natural_0 device* surfaceParameters_1 [[buffer(7)]], texture2d<float, access::sample> previousShadowLayer_1 [[texture(24)]])
{
    thread KernelContext_0 kernelContext_0;
    (&kernelContext_0)->surfaceParameters_0 = surfaceParameters_1;
    (&kernelContext_0)->previousShadowLayer_0 = previousShadowLayer_1;
    float opacity_0 = saturate(saturate((float4((surfaceParameters_1+int(0))->diffuseOpacity_0) ).w));
    int3 _S1 = int3(int2(position_0.xy), int(0));
    float previousDepth_0 = ((previousShadowLayer_1).read(vec<uint,2>(((_S1)).xy), uint(((_S1)).z))).x;
    bool _S2;
    if(opacity_0 <= 0.0f)
    {
        _S2 = true;
    }
    else
    {
        _S2 = previousDepth_0 < 0.0f;
    }
    if(_S2)
    {
        _S2 = true;
    }
    else
    {
        _S2 = (position_0.z) <= previousDepth_0;
    }
    if(_S2)
    {
        discard_fragment();
    }
    pixelOutput_0 _S3 = { float4(position_0.z, 1.0f - opacity_0, 0.0f, 1.0f) };
    return _S3;
}
