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
    float3 eyePosition_0 [[user(TEXCOORD_1)]];
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

struct _MatrixStorage_float4x4natural_0
{
    array<packed_float4, int(4)> data_0;
};

struct _Array_natural_vectorx3Cfloatx2C4x3E8_0
{
    array<packed_float4, int(8)> data_1;
};

struct _Array_natural_vectorx3Cfloatx2C4x3E4_0
{
    array<packed_float4, int(4)> data_2;
};

struct FrameParameters_natural_0
{
    _MatrixStorage_float4x4natural_0 clipToEye_0;
    packed_uint4 clipPlaneCount_0;
    _Array_natural_vectorx3Cfloatx2C4x3E8_0 clipPlanes_0;
    packed_float4 ambientLight_0;
    _Array_natural_vectorx3Cfloatx2C4x3E4_0 lightPositionType_0;
    _Array_natural_vectorx3Cfloatx2C4x3E4_0 lightDirectionRadius_0;
    _Array_natural_vectorx3Cfloatx2C4x3E4_0 lightColorIntensity_0;
    _Array_natural_vectorx3Cfloatx2C4x3E4_0 lightControls_0;
    _MatrixStorage_float4x4natural_0 eyeToWorld_0;
};

struct KernelContext_0
{
    SurfaceParameters_natural_0 device* surfaceParameters_0;
    FrameParameters_natural_0 device* frameParameters_0;
    texture2d<float, access::sample> emissiveTexture_0;
    sampler materialSampler_0;
};

[[fragment]] pixelOutput_0 fragmentMain_uv_emissive(pixelInput_0 _S1 [[stage_in]], bool isFrontFace_0 [[front_facing]], float4 position_0 [[position]], SurfaceParameters_natural_0 device* surfaceParameters_1 [[buffer(7)]], FrameParameters_natural_0 device* frameParameters_1 [[buffer(8)]], texture2d<float, access::sample> emissiveTexture_1 [[texture(3)]], sampler materialSampler_1 [[sampler(0)]])
{
    thread KernelContext_0 kernelContext_0;
    (&kernelContext_0)->surfaceParameters_0 = surfaceParameters_1;
    (&kernelContext_0)->frameParameters_0 = frameParameters_1;
    (&kernelContext_0)->emissiveTexture_0 = emissiveTexture_1;
    (&kernelContext_0)->materialSampler_0 = materialSampler_1;
    SurfaceParameters_natural_0 surface_0 = surfaceParameters_1[int(0)];
    FrameParameters_natural_0 device* _S2 = frameParameters_1+int(0);
    for(;;)
    {
        uint _S3 = min((uint4(_S2->clipPlaneCount_0) ).x, 8U);
        uint index_0 = 0U;
        for(;;)
        {
            if(index_0 < _S3)
            {
            }
            else
            {
                break;
            }
            float4 _S4 = float4((&_S2->clipPlanes_0)->data_1[index_0]) ;
            if((dot(_S4.xyz, _S1.eyePosition_0) + _S4.w) < 0.0f)
            {
                discard_fragment();
            }
            index_0 = index_0 + 1U;
        }
        break;
    }
    float4 _S5 = float4(surface_0.clearcoatShaded_0) ;
    bool shaded_0 = (_S5.z) >= 0.5f;
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
    float _S6 = (float4(surface_0.emissiveOcclusion_0) ).w;
    float4 _S7 = float4(surface_0.metallicRoughnessThresholdWorkflow_0) ;
    float metallic_0 = saturate(_S7.x);
    float roughness_0 = clamp(_S7.y, 0.00999999977648258f, 1.0f);
    float3 emissiveColor_0 = (((&kernelContext_0)->emissiveTexture_0).sample(((&kernelContext_0)->materialSampler_0), (_S1.texCoord_0))).xyz;
    float opacityThreshold_0 = _S7.z;
    bool hasSceneLighting_0;
    if(opacityThreshold_0 > 0.0f)
    {
        hasSceneLighting_0 = opacity_0 < opacityThreshold_0;
    }
    else
    {
        hasSceneLighting_0 = false;
    }
    if(hasSceneLighting_0)
    {
        discard_fragment();
    }
    float3 normal_1 = normalize(_S1.normal_0);
    float lengthSquared_0 = dot(_S1.eyePosition_0, _S1.eyePosition_0);
    float3 _S8;
    if(lengthSquared_0 > 0.00100000004749745f)
    {
        _S8 = - _S1.eyePosition_0 * float3(rsqrt(lengthSquared_0)) ;
    }
    else
    {
        _S8 = float3(0.0f, 0.0f, 1.0f);
    }
    float3 normal_2;
    if(isFrontFace_0)
    {
        normal_2 = normal_1;
    }
    else
    {
        normal_2 = - normal_1;
    }
    float _S9 = saturate(abs(dot(normal_2, _S8)) + 0.00000999999974738f);
    float _S10 = max(0.00100000004749745f, roughness_0);
    float _S11 = _S5.x;
    float _S12 = max(0.00100000004749745f, _S5.y);
    float4 _S13 = float4(surface_0.specularIor_0) ;
    float _S14 = _S13.w;
    float reflectanceRatio_0 = (1.0f - _S14) / (1.0f + _S14);
    float3 _S15 = float3(3.14159274101257324f) ;
    float3 diffuse_0 = diffuseColor_0 / _S15;
    float3 normalIncidence_0;
    float3 grazingIncidence_0;
    float3 diffuse_1;
    if((_S7.w) >= 0.5f)
    {
        float3 _S16 = float3(1.0f, 1.0f, 1.0f);
        normalIncidence_0 = _S13.xyz;
        grazingIncidence_0 = _S16;
        diffuse_1 = diffuse_0;
    }
    else
    {
        float3 _S17 = float3(metallic_0) ;
        float3 specularTint_0 = mix(float3(1.0f, 1.0f, 1.0f), diffuseColor_0, _S17);
        float3 diffuse_2 = diffuse_0 * float3((1.0f - metallic_0)) ;
        normalIncidence_0 = mix(float3((reflectanceRatio_0 * reflectanceRatio_0))  * specularTint_0, specularTint_0, _S17);
        grazingIncidence_0 = specularTint_0;
        diffuse_1 = diffuse_2;
    }
    float4 _S18 = float4(_S2->ambientLight_0) ;
    float _S19 = _S18.w;
    if(_S19 > 0.5f)
    {
        hasSceneLighting_0 = true;
    }
    else
    {
        float3 _S20 = _S18.xyz;
        hasSceneLighting_0 = (dot(_S20, _S20)) > 0.0f;
    }
    uint _S21 = min(uint(_S19), 4U);
    matrix<float,int(4),int(4)>  _S22 = matrix<float,int(4),int(4)> (_S2->eyeToWorld_0.data_0[int(0)][int(0)], _S2->eyeToWorld_0.data_0[int(0)][int(1)], _S2->eyeToWorld_0.data_0[int(0)][int(2)], _S2->eyeToWorld_0.data_0[int(0)][int(3)], _S2->eyeToWorld_0.data_0[int(1)][int(0)], _S2->eyeToWorld_0.data_0[int(1)][int(1)], _S2->eyeToWorld_0.data_0[int(1)][int(2)], _S2->eyeToWorld_0.data_0[int(1)][int(3)], _S2->eyeToWorld_0.data_0[int(2)][int(0)], _S2->eyeToWorld_0.data_0[int(2)][int(1)], _S2->eyeToWorld_0.data_0[int(2)][int(2)], _S2->eyeToWorld_0.data_0[int(2)][int(3)], _S2->eyeToWorld_0.data_0[int(3)][int(0)], _S2->eyeToWorld_0.data_0[int(3)][int(1)], _S2->eyeToWorld_0.data_0[int(3)][int(2)], _S2->eyeToWorld_0.data_0[int(3)][int(3)]);
    float3 _S23 = normalize((((float4(_S8, 0.0f)) * (_S22))).xyz);
    float3 _S24 = (((float4(_S1.eyePosition_0, 1.0f)) * (_S22))).xyz;
    float4 _S25 = float4(surface_0.lightColorAmbient_0) ;
    float3 color_0 = diffuseColor_0 * float3(_S25.w)  + diffuseColor_0 * _S18.xyz;
    bool _S26 = !hasSceneLighting_0;
    uint lightCount_0;
    if(_S26)
    {
        lightCount_0 = 1U;
    }
    else
    {
        lightCount_0 = _S21;
    }
    uint lightIndex_0 = 0U;
    float3 color_1 = color_0;
    for(;;)
    {
        if(lightIndex_0 < lightCount_0)
        {
        }
        else
        {
            break;
        }
        bool _S27 = lightIndex_0 == 0U;
        if(_S27)
        {
            hasSceneLighting_0 = _S26;
        }
        else
        {
            hasSceneLighting_0 = false;
        }
        float lightType_0;
        if(hasSceneLighting_0)
        {
            lightType_0 = 1.0f;
        }
        else
        {
            lightType_0 = (float4((&_S2->lightPositionType_0)->data_2[lightIndex_0]) ).w;
        }
        bool _S28;
        if(_S27)
        {
            _S28 = _S26;
        }
        else
        {
            _S28 = false;
        }
        if(_S28)
        {
            diffuseColor_0 = normalize((float4(surface_0.lightDirectionIntensity_0) ).xyz);
        }
        else
        {
            diffuseColor_0 = normalize((float4((&_S2->lightDirectionRadius_0)->data_2[lightIndex_0]) ).xyz);
        }
        bool _S29;
        if(_S27)
        {
            _S29 = _S26;
        }
        else
        {
            _S29 = false;
        }
        float lightIntensity_0;
        if(_S29)
        {
            lightIntensity_0 = (float4(surface_0.lightDirectionIntensity_0) ).w;
        }
        else
        {
            lightIntensity_0 = (float4((&_S2->lightColorIntensity_0)->data_2[lightIndex_0]) ).w;
        }
        bool _S30;
        if(_S27)
        {
            _S30 = _S26;
        }
        else
        {
            _S30 = false;
        }
        float3 lightColor_0;
        if(_S30)
        {
            lightColor_0 = _S25.xyz;
        }
        else
        {
            lightColor_0 = (float4((&_S2->lightColorIntensity_0)->data_2[lightIndex_0]) ).xyz;
        }
        bool _S31;
        if(_S27)
        {
            _S31 = _S26;
        }
        else
        {
            _S31 = false;
        }
        float diffuseScale_0;
        if(_S31)
        {
            diffuseScale_0 = 1.0f;
        }
        else
        {
            diffuseScale_0 = (float4((&_S2->lightControls_0)->data_2[lightIndex_0]) ).x;
        }
        bool _S32;
        if(_S27)
        {
            _S32 = _S26;
        }
        else
        {
            _S32 = false;
        }
        float specularScale_0;
        if(_S32)
        {
            specularScale_0 = 1.0f;
        }
        else
        {
            specularScale_0 = (float4((&_S2->lightControls_0)->data_2[lightIndex_0]) ).y;
        }
        bool _S33;
        if(_S27)
        {
            _S33 = _S26;
        }
        else
        {
            _S33 = false;
        }
        float3 lightEye_0;
        if(_S33)
        {
            lightEye_0 = _S8;
        }
        else
        {
            lightEye_0 = _S23;
        }
        float3 lightDirection_0;
        float lightIntensity_1;
        if(lightType_0 >= 2.0f)
        {
            float3 toLight_0 = (float4((&_S2->lightPositionType_0)->data_2[lightIndex_0]) ).xyz - _S24;
            float _S34 = max(dot(toLight_0, toLight_0), 0.00100000004749745f);
            float lightIntensity_2 = lightIntensity_0 / _S34;
            lightDirection_0 = toLight_0 * float3(rsqrt(_S34)) ;
            lightIntensity_1 = lightIntensity_2;
        }
        else
        {
            lightDirection_0 = diffuseColor_0;
            lightIntensity_1 = lightIntensity_0;
        }
        float3 half_0 = normalize(lightDirection_0 + lightEye_0);
        float normalDotLight_0 = saturate(dot(normal_2, lightDirection_0));
        float normalDotHalf_0 = saturate(dot(normal_2, half_0));
        float3 _S35 = float3(pow(max(0.0f, 1.0f - saturate(dot(lightEye_0, half_0))), 5.0f)) ;
        float3 _S36 = mix(normalIncidence_0, grazingIncidence_0, _S35);
        float3 directDiffuse_0 = diffuse_1 * (float3(1.0f)  - _S36);
        float alpha_0 = _S10 * _S10;
        float alphaSquared_0 = alpha_0 * alpha_0;
        float _S37 = normalDotHalf_0 * normalDotHalf_0;
        float denominator_0 = _S37 * (alphaSquared_0 - 1.0f) + 1.0f;
        float k_0 = alpha_0 * 0.5f;
        float _S38 = 1.0f - k_0;
        float3 _S39 = float3((4.0f * normalDotLight_0 * _S9 + 0.00100000004749745f)) ;
        float3 _S40 = _S36 * float3((_S9 / (_S9 * _S38 + k_0) * (normalDotLight_0 / (normalDotLight_0 * _S38 + k_0))))  * float3(((alphaSquared_0 + 0.00100000004749745f) / (denominator_0 * denominator_0 * 3.14159274101257324f)))  / _S39;
        float3 directSpecular_0;
        if(_S11 > 0.0f)
        {
            float alpha_1 = _S12 * _S12;
            float alphaSquared_1 = alpha_1 * alpha_1;
            float denominator_1 = _S37 * (alphaSquared_1 - 1.0f) + 1.0f;
            float k_1 = alpha_1 * 0.5f;
            float _S41 = 1.0f - k_1;
            directSpecular_0 = _S40 + float3(_S11)  * (mix(float3((reflectanceRatio_0 * reflectanceRatio_0)) , float3(1.0f, 1.0f, 1.0f), _S35) * float3((_S9 / (_S9 * _S41 + k_1) * (normalDotLight_0 / (normalDotLight_0 * _S41 + k_1))))  * float3(((alphaSquared_1 + 0.00100000004749745f) / (denominator_1 * denominator_1 * 3.14159274101257324f)))  / _S39);
        }
        else
        {
            directSpecular_0 = _S40;
        }
        float3 _S42 = lightColor_0 * float3(lightIntensity_1) ;
        float3 color_2 = color_1 + float3((_S6 * normalDotLight_0))  * (directDiffuse_0 * float3(diffuseScale_0)  * (_S42 * _S15) + directSpecular_0 * float3(specularScale_0)  * _S42);
        lightIndex_0 = lightIndex_0 + 1U;
        color_1 = color_2;
    }
    pixelOutput_0 _S43 = { float4(color_1 + emissiveColor_0, opacity_0) };
    return _S43;
}

