#include <metal_stdlib>
#include <metal_math>
#include <metal_texture>
using namespace metal;
float SchlickFresnel_0(float eyeDotHalf_0)
{
    return pow(max(0.0f, 1.0f - eyeDotHalf_0), 5.0f);
}

float NormalDistribution_0(float specularRoughness_0, float normalDotHalf_0)
{
    float alpha_0 = specularRoughness_0 * specularRoughness_0;
    float alphaSquared_0 = alpha_0 * alpha_0;
    float denominator_0 = normalDotHalf_0 * normalDotHalf_0 * (alphaSquared_0 - 1.0f) + 1.0f;
    return (alphaSquared_0 + 0.00100000004749745f) / (denominator_0 * denominator_0 * 3.14159274101257324f);
}

float Geometric_0(float specularRoughness_1, float normalDotLight_0, float normalDotEye_0)
{
    float k_0 = specularRoughness_1 * specularRoughness_1 * 0.5f;
    float _S1 = 1.0f - k_0;
    return normalDotEye_0 / (normalDotEye_0 * _S1 + k_0) * (normalDotLight_0 / (normalDotLight_0 * _S1 + k_0));
}

float3 EvaluateDirectSpecular_0(float3 specularColorF0_0, float3 specularColorF90_0, float specularRoughness_2, float fresnel_0, float normalDotLight_1, float normalDotEye_1, float normalDotHalf_1)
{
    return mix(specularColorF0_0, specularColorF90_0, float3(fresnel_0) ) * float3(Geometric_0(specularRoughness_2, normalDotLight_1, normalDotEye_1))  * float3(NormalDistribution_0(specularRoughness_2, normalDotHalf_1))  / float3((4.0f * normalDotLight_1 * normalDotEye_1 + 0.00100000004749745f)) ;
}

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
    texture2d<float, access::sample> baseColorTexture_0;
    sampler materialSampler_0;
    texture2d<float, access::sample> roughnessMetallicTexture_0;
    texture2d<float, access::sample> emissiveTexture_0;
};

[[fragment]] pixelOutput_0 fragmentMain_uv_basecolor_roughmetal_emissive(pixelInput_0 _S2 [[stage_in]], float4 position_0 [[position]], SurfaceParameters_natural_0 device* surfaceParameters_1 [[buffer(7)]], texture2d<float, access::sample> baseColorTexture_1 [[texture(0)]], sampler materialSampler_1 [[sampler(0)]], texture2d<float, access::sample> roughnessMetallicTexture_1 [[texture(2)]], texture2d<float, access::sample> emissiveTexture_1 [[texture(3)]])
{
    thread KernelContext_0 kernelContext_0;
    (&kernelContext_0)->surfaceParameters_0 = surfaceParameters_1;
    (&kernelContext_0)->baseColorTexture_0 = baseColorTexture_1;
    (&kernelContext_0)->materialSampler_0 = materialSampler_1;
    (&kernelContext_0)->roughnessMetallicTexture_0 = roughnessMetallicTexture_1;
    (&kernelContext_0)->emissiveTexture_0 = emissiveTexture_1;
    SurfaceParameters_natural_0 surface_0 = surfaceParameters_1[int(0)];
    float4 _S3 = float4(surface_0.clearcoatShaded_0) ;
    bool shaded_0 = (_S3.z) >= 0.5f;
    float3 diffuseColor_0;
    if(shaded_0)
    {
        diffuseColor_0 = (float4(surface_0.diffuseOpacity_0) ).xyz;
    }
    else
    {
        diffuseColor_0 = _S2.tint_0.xyz;
    }
    float opacity_0;
    if(shaded_0)
    {
        opacity_0 = (float4(surface_0.diffuseOpacity_0) ).w;
    }
    else
    {
        opacity_0 = _S2.tint_0.w;
    }
    float4 _S4 = float4(surface_0.emissiveOcclusion_0) ;
    float occlusion_0 = _S4.w;
    float4 _S5 = float4(surface_0.metallicRoughnessThresholdWorkflow_0) ;
    float4 sampledBaseColor_0 = (((&kernelContext_0)->baseColorTexture_0).sample(((&kernelContext_0)->materialSampler_0), (_S2.texCoord_0)));
    float3 diffuseColor_1 = diffuseColor_0 * sampledBaseColor_0.xyz;
    float opacity_1 = opacity_0 * sampledBaseColor_0.w;
    float3 sampledRoughnessMetallic_0 = (((&kernelContext_0)->roughnessMetallicTexture_0).sample(((&kernelContext_0)->materialSampler_0), (_S2.texCoord_0))).xyz;
    float roughness_0 = clamp(clamp(_S5.y, 0.00999999977648258f, 1.0f) * sampledRoughnessMetallic_0.y, 0.00999999977648258f, 1.0f);
    float metallic_0 = saturate(saturate(_S5.x) * sampledRoughnessMetallic_0.z);
    float3 emissiveColor_0 = _S4.xyz * (((&kernelContext_0)->emissiveTexture_0).sample(((&kernelContext_0)->materialSampler_0), (_S2.texCoord_0))).xyz;
    float opacityThreshold_0 = _S5.z;
    bool _S6;
    if(opacityThreshold_0 > 0.0f)
    {
        _S6 = opacity_1 < opacityThreshold_0;
    }
    else
    {
        _S6 = false;
    }
    if(_S6)
    {
        discard_fragment();
    }
    float3 normal_1 = normalize(_S2.normal_0);
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
    float normalDotLight_2 = saturate(dot(normal_2, lightDirection_0));
    float normalDotEye_2 = saturate(abs(dot(normal_2, eye_0)) + 0.00000999999974738f);
    float normalDotHalf_2 = saturate(dot(normal_2, half_0));
    float _S8 = max(0.00100000004749745f, roughness_0);
    float clearcoatAmount_0 = _S3.x;
    float _S9 = max(0.00100000004749745f, _S3.y);
    float fresnel_1 = SchlickFresnel_0(saturate(dot(eye_0, half_0)));
    float4 _S10 = float4(surface_0.specularIor_0) ;
    float _S11 = _S10.w;
    float reflectanceRatio_0 = (1.0f - _S11) / (1.0f + _S11);
    float3 _S12 = float3(3.14159274101257324f) ;
    float3 diffuse_0 = diffuseColor_1 / _S12;
    float3 normalIncidence_0;
    float3 grazingIncidence_0;
    float3 diffuse_1;
    if((_S5.w) >= 0.5f)
    {
        float3 _S13 = float3(1.0f, 1.0f, 1.0f);
        normalIncidence_0 = _S10.xyz;
        grazingIncidence_0 = _S13;
        diffuse_1 = diffuse_0;
    }
    else
    {
        float3 _S14 = float3(metallic_0) ;
        float3 specularTint_0 = mix(float3(1.0f, 1.0f, 1.0f), diffuseColor_1, _S14);
        float3 diffuse_2 = diffuse_0 * float3((1.0f - metallic_0)) ;
        normalIncidence_0 = mix(float3((reflectanceRatio_0 * reflectanceRatio_0))  * specularTint_0, specularTint_0, _S14);
        grazingIncidence_0 = specularTint_0;
        diffuse_1 = diffuse_2;
    }
    float3 specular_0 = EvaluateDirectSpecular_0(normalIncidence_0, grazingIncidence_0, _S8, fresnel_1, normalDotLight_2, normalDotEye_2, normalDotHalf_2);
    float3 diffuse_3 = diffuse_1 * (float3(1.0f)  - mix(normalIncidence_0, grazingIncidence_0, float3(fresnel_1) ));
    float3 specular_1;
    if(clearcoatAmount_0 > 0.0f)
    {
        specular_1 = specular_0 + float3(clearcoatAmount_0)  * EvaluateDirectSpecular_0(float3((reflectanceRatio_0 * reflectanceRatio_0)) , float3(1.0f, 1.0f, 1.0f), _S9, fresnel_1, normalDotLight_2, normalDotEye_2, normalDotHalf_2);
    }
    else
    {
        specular_1 = specular_0;
    }
    float4 _S15 = float4(surface_0.lightColorAmbient_0) ;
    pixelOutput_0 _S16 = { float4(float3((occlusion_0 * normalDotLight_2))  * (diffuse_3 + specular_1) * (_S15.xyz * float3(_S7.w)  * _S12) + diffuseColor_1 * float3(_S15.w)  + emissiveColor_0, opacity_1) };
    return _S16;
}

