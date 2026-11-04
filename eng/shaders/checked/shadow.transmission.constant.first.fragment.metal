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

[[fragment]] pixelOutput_0 shadowTransmissionConstantFirstFragmentMain(float4 position_0 [[position]], SurfaceParameters_natural_0 device* surfaceParameters_0 [[buffer(7)]])
{
    float opacity_0 = saturate(saturate((float4((surfaceParameters_0+int(0))->diffuseOpacity_0) ).w));
    if(opacity_0 <= 0.0f)
    {
        discard_fragment();
    }
    pixelOutput_0 _S1 = { float4(position_0.z, 1.0f - opacity_0, 0.0f, 1.0f) };
    return _S1;
}
