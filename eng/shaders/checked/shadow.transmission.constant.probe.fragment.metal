#include <metal_stdlib>
#include <metal_math>
#include <metal_texture>
using namespace metal;
struct ShadowMaskedVertexOutput_0
{
    float4 position_0;
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

float SampleShadowOpacity_0(const ShadowMaskedVertexOutput_0 thread* input_0, KernelContext_0 thread* kernelContext_0)
{
    return saturate((float4((kernelContext_0->surfaceParameters_0+int(0))->diffuseOpacity_0) ).w);
}

struct pixelOutput_0
{
    float4 output_0 [[color(0)]];
};

[[fragment]] pixelOutput_0 shadowTransmissionConstantProbeFragmentMain(float4 position_1 [[position]], SurfaceParameters_natural_0 device* surfaceParameters_1 [[buffer(7)]], texture2d<float, access::sample> previousShadowLayer_1 [[texture(24)]])
{
    thread KernelContext_0 kernelContext_1;
    (&kernelContext_1)->surfaceParameters_0 = surfaceParameters_1;
    (&kernelContext_1)->previousShadowLayer_0 = previousShadowLayer_1;
    thread ShadowMaskedVertexOutput_0 _S1;
    (&_S1)->position_0 = position_1;
    float _S2 = SampleShadowOpacity_0(&_S1, &kernelContext_1);
    int3 _S3 = int3(int2(position_1.xy), int(0));
    float nearestDepth_0 = (((&kernelContext_1)->previousShadowLayer_0).read(vec<uint,2>(((_S3)).xy), uint(((_S3)).z))).x;
    bool _S4;
    if((saturate(_S2)) <= 0.0f)
    {
        _S4 = true;
    }
    else
    {
        _S4 = (position_1.z) != nearestDepth_0;
    }
    if(_S4)
    {
        discard_fragment();
    }
    pixelOutput_0 _S5 = { float4(0.0f, 0.0f, 0.0f, 0.5f) };
    return _S5;
}
