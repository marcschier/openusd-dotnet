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
    texture2d<float, access::sample> normalTexture_0;
    sampler materialSampler_0;
};

[[fragment]] pixelOutput_0 fragmentMain_uv_normal(pixelInput_0 _S1 [[stage_in]], float4 position_0 [[position]], SurfaceParameters_natural_0 device* surfaceParameters_1 [[buffer(7)]], texture2d<float, access::sample> normalTexture_1 [[texture(1)]], sampler materialSampler_1 [[sampler(0)]])
{
    thread KernelContext_0 kernelContext_0;
    (&kernelContext_0)->surfaceParameters_0 = surfaceParameters_1;
    (&kernelContext_0)->normalTexture_0 = normalTexture_1;
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
    float3 emissiveColor_0 = _S3.xyz;
    float occlusion_0 = _S3.w;
    float4 _S4 = float4(surface_0.metallicRoughnessThresholdWorkflow_0) ;
    float metallic_0 = saturate(_S4.x);
    float roughness_0 = clamp(_S4.y, 0.00999999977648258f, 1.0f);
    float3 _S5 = float3(1.0f) ;
    float3 sampledNormal_0 = (((&kernelContext_0)->normalTexture_0).sample(((&kernelContext_0)->materialSampler_0), (_S1.texCoord_0))).xyz * float3(2.0f)  - _S5;
    float3 tangent_1 = normalize(_S1.tangent_0.xyz);
    float3 shadingNormal_0 = normalize(tangent_1 * float3(sampledNormal_0.x)  + cross(normalize(_S1.normal_0), tangent_1) * float3(_S1.tangent_0.w)  * float3(sampledNormal_0.y)  + _S1.normal_0 * float3(sampledNormal_0.z) );
    float opacityThreshold_0 = _S4.z;
    bool _S6;
    if(opacityThreshold_0 > 0.0f)
    {
        _S6 = opacity_0 < opacityThreshold_0;
    }
    else
    {
        _S6 = false;
    }
    if(_S6)
    {
        discard_fragment();
    }
    float3 normal_1 = normalize(shadingNormal_0);
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
    float4 _S7 = float4(surface_0.lightDirectionIntensity_0) ;
    float3 lightDirection_0 = normalize(_S7.xyz);
    float3 half_0 = normalize(lightDirection_0 + eye_0);
    float normalDotLight_0 = saturate(dot(normal_2, lightDirection_0));
    float normalDotEye_0 = saturate(abs(dot(normal_2, eye_0)) + 0.00000999999974738f);
    float normalDotHalf_0 = saturate(dot(normal_2, half_0));
    float _S8 = max(0.00100000004749745f, roughness_0);
    float clearcoatAmount_0 = _S2.x;
    float _S9 = max(0.00100000004749745f, _S2.y);
    float _S10 = pow(max(0.0f, 1.0f - saturate(dot(eye_0, half_0))), 5.0f);
    float4 _S11 = float4(surface_0.specularIor_0) ;
    float _S12 = _S11.w;
    float reflectanceRatio_0 = (1.0f - _S12) / (1.0f + _S12);
    float3 _S13 = float3(3.14159274101257324f) ;
    float3 diffuse_0 = diffuseColor_0 / _S13;
    float3 normalIncidence_0;
    float3 grazingIncidence_0;
    float3 diffuse_1;
    if((_S4.w) >= 0.5f)
    {
        float3 _S14 = float3(1.0f, 1.0f, 1.0f);
        normalIncidence_0 = _S11.xyz;
        grazingIncidence_0 = _S14;
        diffuse_1 = diffuse_0;
    }
    else
    {
        float3 _S15 = float3(metallic_0) ;
        float3 specularTint_0 = mix(float3(1.0f, 1.0f, 1.0f), diffuseColor_0, _S15);
        float3 diffuse_2 = diffuse_0 * float3((1.0f - metallic_0)) ;
        normalIncidence_0 = mix(float3((reflectanceRatio_0 * reflectanceRatio_0))  * specularTint_0, specularTint_0, _S15);
        grazingIncidence_0 = specularTint_0;
        diffuse_1 = diffuse_2;
    }
    float3 _S16 = float3(_S10) ;
    float3 f_0 = mix(normalIncidence_0, grazingIncidence_0, _S16);
    float alpha_0 = _S8 * _S8;
    float alphaSquared_0 = alpha_0 * alpha_0;
    float _S17 = normalDotHalf_0 * normalDotHalf_0;
    float denominator_0 = _S17 * (alphaSquared_0 - 1.0f) + 1.0f;
    float k_0 = alpha_0 * 0.5f;
    float _S18 = 1.0f - k_0;
    float3 _S19 = float3((4.0f * normalDotLight_0 * normalDotEye_0 + 0.00100000004749745f)) ;
    float3 _S20 = f_0 * float3((normalDotEye_0 / (normalDotEye_0 * _S18 + k_0) * (normalDotLight_0 / (normalDotLight_0 * _S18 + k_0))))  * float3(((alphaSquared_0 + 0.00100000004749745f) / (denominator_0 * denominator_0 * 3.14159274101257324f)))  / _S19;
    float3 diffuse_3 = diffuse_1 * (_S5 - f_0);
    float3 specular_0;
    if(clearcoatAmount_0 > 0.0f)
    {
        float alpha_1 = _S9 * _S9;
        float alphaSquared_1 = alpha_1 * alpha_1;
        float denominator_1 = _S17 * (alphaSquared_1 - 1.0f) + 1.0f;
        float k_1 = alpha_1 * 0.5f;
        float _S21 = 1.0f - k_1;
        specular_0 = _S20 + float3(clearcoatAmount_0)  * (mix(float3((reflectanceRatio_0 * reflectanceRatio_0)) , float3(1.0f, 1.0f, 1.0f), _S16) * float3((normalDotEye_0 / (normalDotEye_0 * _S21 + k_1) * (normalDotLight_0 / (normalDotLight_0 * _S21 + k_1))))  * float3(((alphaSquared_1 + 0.00100000004749745f) / (denominator_1 * denominator_1 * 3.14159274101257324f)))  / _S19);
    }
    else
    {
        specular_0 = _S20;
    }
    float4 _S22 = float4(surface_0.lightColorAmbient_0) ;
    pixelOutput_0 _S23 = { float4(float3((occlusion_0 * normalDotLight_0))  * (diffuse_3 + specular_0) * (_S22.xyz * float3(_S7.w)  * _S13) + diffuseColor_0 * float3(_S22.w)  + emissiveColor_0, opacity_0) };
    return _S23;
}

