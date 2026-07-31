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
};

struct KernelContext_0
{
    SurfaceParameters_natural_0 device* surfaceParameters_0;
    texture2d<float, access::sample> emissiveTexture_0;
    sampler materialSampler_0;
};

[[fragment]] pixelOutput_0 fragmentMain_uv_emissive(pixelInput_0 _S1 [[stage_in]], float4 position_0 [[position]], SurfaceParameters_natural_0 device* surfaceParameters_1 [[buffer(7)]], texture2d<float, access::sample> emissiveTexture_1 [[texture(3)]], sampler materialSampler_1 [[sampler(0)]])
{
    thread KernelContext_0 kernelContext_0;
    (&kernelContext_0)->surfaceParameters_0 = surfaceParameters_1;
    (&kernelContext_0)->emissiveTexture_0 = emissiveTexture_1;
    (&kernelContext_0)->materialSampler_0 = materialSampler_1;
    SurfaceParameters_natural_0 surface_0 = surfaceParameters_1[int(0)];
    float4 _S2 = float4(surface_0.clearcoatShaded_0) ;
    bool shaded_0 = (_S2.z) >= 0.5f;
    float3 diffuseColor_0;
    if(shaded_0)
    {
        diffuseColor_0 = (float4(surface_0.diffuseOpacity_0) ).xyz;
    }
    else
    {
        diffuseColor_0 = _S1.tint_0.xyz;
    }
    float opacity_0;
    if(shaded_0)
    {
        opacity_0 = (float4(surface_0.diffuseOpacity_0) ).w;
    }
    else
    {
        opacity_0 = _S1.tint_0.w;
    }
    float4 _S3 = float4(surface_0.emissiveOcclusion_0) ;
    float occlusion_0 = _S3.w;
    float4 _S4 = float4(surface_0.metallicRoughnessThresholdWorkflow_0) ;
    float metallic_0 = saturate(_S4.x);
    float roughness_0 = clamp(_S4.y, 0.00999999977648258f, 1.0f);
    float3 emissiveColor_0 = _S3.xyz * (((&kernelContext_0)->emissiveTexture_0).sample(((&kernelContext_0)->materialSampler_0), (_S1.texCoord_0))).xyz;
    float opacityThreshold_0 = _S4.z;
    bool _S5;
    if(opacityThreshold_0 > 0.0f)
    {
        _S5 = opacity_0 < opacityThreshold_0;
    }
    else
    {
        _S5 = false;
    }
    if(_S5)
    {
        discard_fragment();
    }
    float3 normal_1 = normalize(_S1.normal_0);
    float3 eye_0 = float3(0.0f, 0.0f, 1.0f);
    float3 normal_2;
    if((dot(normal_1, eye_0)) < 0.0f)
    {
        normal_2 = - normal_1;
    }
    else
    {
        normal_2 = normal_1;
    }
    float4 _S6 = float4(surface_0.lightDirectionIntensity_0) ;
    float3 lightDirection_0 = normalize(_S6.xyz);
    float3 half_0 = normalize(lightDirection_0 + eye_0);
    float normalDotLight_0 = saturate(dot(normal_2, lightDirection_0));
    float normalDotEye_0 = saturate(abs(dot(normal_2, eye_0)) + 0.00000999999974738f);
    float normalDotHalf_0 = saturate(dot(normal_2, half_0));
    float _S7 = max(0.00100000004749745f, roughness_0);
    float clearcoatAmount_0 = _S2.x;
    float _S8 = max(0.00100000004749745f, _S2.y);
    float _S9 = pow(max(0.0f, 1.0f - saturate(dot(eye_0, half_0))), 5.0f);
    float4 _S10 = float4(surface_0.specularIor_0) ;
    float _S11 = _S10.w;
    float reflectanceRatio_0 = (1.0f - _S11) / (1.0f + _S11);
    float3 _S12 = float3(3.14159274101257324f) ;
    float3 diffuse_0 = diffuseColor_0 / _S12;
    float3 normalIncidence_0;
    float3 grazingIncidence_0;
    float3 diffuse_1;
    if((_S4.w) >= 0.5f)
    {
        float3 _S13 = float3(1.0f, 1.0f, 1.0f);
        normalIncidence_0 = _S10.xyz;
        grazingIncidence_0 = _S13;
        diffuse_1 = diffuse_0;
    }
    else
    {
        float3 _S14 = float3(metallic_0) ;
        float3 specularTint_0 = mix(float3(1.0f, 1.0f, 1.0f), diffuseColor_0, _S14);
        float3 diffuse_2 = diffuse_0 * float3((1.0f - metallic_0)) ;
        normalIncidence_0 = mix(float3((reflectanceRatio_0 * reflectanceRatio_0))  * specularTint_0, specularTint_0, _S14);
        grazingIncidence_0 = specularTint_0;
        diffuse_1 = diffuse_2;
    }
    float3 _S15 = float3(_S9) ;
    float3 f_0 = mix(normalIncidence_0, grazingIncidence_0, _S15);
    float alpha_0 = _S7 * _S7;
    float alphaSquared_0 = alpha_0 * alpha_0;
    float _S16 = normalDotHalf_0 * normalDotHalf_0;
    float denominator_0 = _S16 * (alphaSquared_0 - 1.0f) + 1.0f;
    float k_0 = alpha_0 * 0.5f;
    float _S17 = 1.0f - k_0;
    float3 _S18 = float3((4.0f * normalDotLight_0 * normalDotEye_0 + 0.00100000004749745f)) ;
    float3 _S19 = f_0 * float3((normalDotEye_0 / (normalDotEye_0 * _S17 + k_0) * (normalDotLight_0 / (normalDotLight_0 * _S17 + k_0))))  * float3(((alphaSquared_0 + 0.00100000004749745f) / (denominator_0 * denominator_0 * 3.14159274101257324f)))  / _S18;
    float3 diffuse_3 = diffuse_1 * (float3(1.0f)  - f_0);
    float3 specular_0;
    if(clearcoatAmount_0 > 0.0f)
    {
        float alpha_1 = _S8 * _S8;
        float alphaSquared_1 = alpha_1 * alpha_1;
        float denominator_1 = _S16 * (alphaSquared_1 - 1.0f) + 1.0f;
        float k_1 = alpha_1 * 0.5f;
        float _S20 = 1.0f - k_1;
        specular_0 = _S19 + float3(clearcoatAmount_0)  * (mix(float3((reflectanceRatio_0 * reflectanceRatio_0)) , float3(1.0f, 1.0f, 1.0f), _S15) * float3((normalDotEye_0 / (normalDotEye_0 * _S20 + k_1) * (normalDotLight_0 / (normalDotLight_0 * _S20 + k_1))))  * float3(((alphaSquared_1 + 0.00100000004749745f) / (denominator_1 * denominator_1 * 3.14159274101257324f)))  / _S18);
    }
    else
    {
        specular_0 = _S19;
    }
    float4 _S21 = float4(surface_0.lightColorAmbient_0) ;
    pixelOutput_0 _S22 = { float4(float3((occlusion_0 * normalDotLight_0))  * (diffuse_3 + specular_0) * (_S21.xyz * float3(_S6.w)  * _S12) + diffuseColor_0 * float3(_S21.w)  + emissiveColor_0, opacity_0) };
    return _S22;
}

