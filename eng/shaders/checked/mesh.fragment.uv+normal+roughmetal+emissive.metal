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
    float3 normal_0 [[user(NORMAL)]];
    float4 tint_0 [[user(COLOR)]];
    float2 texCoord_0 [[user(TEXCOORD)]];
    float4 tangent_0 [[user(TANGENT)]];
};

struct KernelContext_0
{
    texture2d<float, access::sample> normalTexture_0;
    sampler materialSampler_0;
    texture2d<float, access::sample> roughnessMetallicTexture_0;
    texture2d<float, access::sample> emissiveTexture_0;
};

[[fragment]] pixelOutput_0 fragmentMain_uv_normal_roughmetal_emissive(pixelInput_0 _S1 [[stage_in]], float4 position_0 [[position]], texture2d<float, access::sample> normalTexture_1 [[texture(1)]], sampler materialSampler_1 [[sampler(0)]], texture2d<float, access::sample> roughnessMetallicTexture_1 [[texture(2)]], texture2d<float, access::sample> emissiveTexture_1 [[texture(3)]])
{
    thread KernelContext_0 kernelContext_0;
    (&kernelContext_0)->normalTexture_0 = normalTexture_1;
    (&kernelContext_0)->materialSampler_0 = materialSampler_1;
    (&kernelContext_0)->roughnessMetallicTexture_0 = roughnessMetallicTexture_1;
    (&kernelContext_0)->emissiveTexture_0 = emissiveTexture_1;
    pixelOutput_0 _S2 = { float4(mix(abs(_S1.normal_0), abs(((normalTexture_1).sample((materialSampler_1), (_S1.texCoord_0))).xyz * float3(2.0f)  - float3(1.0f) ), float3(0.25f) ) * float3(mix(0.5f, 1.0f, ((roughnessMetallicTexture_1).sample((materialSampler_1), (_S1.texCoord_0))).y))  + ((emissiveTexture_1).sample((materialSampler_1), (_S1.texCoord_0))).xyz, 1.0f) * _S1.tint_0 };
    return _S2;
}

