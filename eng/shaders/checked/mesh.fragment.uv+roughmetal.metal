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
    texture2d<float, access::sample> roughnessMetallicTexture_0;
    sampler materialSampler_0;
};

[[fragment]] pixelOutput_0 fragmentMain_uv_roughmetal(pixelInput_0 _S1 [[stage_in]], bool isFrontFace_0 [[front_facing]], float4 position_0 [[position]], SurfaceParameters_natural_0 device* surfaceParameters_1 [[buffer(7)]], FrameParameters_natural_0 device* frameParameters_1 [[buffer(8)]], texture2d<float, access::sample> roughnessMetallicTexture_1 [[texture(2)]], sampler materialSampler_1 [[sampler(0)]])
{
    thread KernelContext_0 kernelContext_0;
    (&kernelContext_0)->surfaceParameters_0 = surfaceParameters_1;
    (&kernelContext_0)->frameParameters_0 = frameParameters_1;
    (&kernelContext_0)->roughnessMetallicTexture_0 = roughnessMetallicTexture_1;
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
    float4 _S6 = float4(surface_0.emissiveOcclusion_0) ;
    float3 emissiveColor_0 = _S6.xyz;
    float _S7 = _S6.w;
    float4 _S8 = float4(surface_0.metallicRoughnessThresholdWorkflow_0) ;
    float3 sampledRoughnessMetallic_0 = (((&kernelContext_0)->roughnessMetallicTexture_0).sample(((&kernelContext_0)->materialSampler_0), (_S1.texCoord_0))).xyz;
    float roughness_0 = clamp(clamp(_S8.y, 0.00999999977648258f, 1.0f) * sampledRoughnessMetallic_0.y, 0.00999999977648258f, 1.0f);
    float metallic_0 = saturate(saturate(_S8.x) * sampledRoughnessMetallic_0.z);
    float opacityThreshold_0 = _S8.z;
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
    float3 _S9;
    if(lengthSquared_0 > 0.00100000004749745f)
    {
        _S9 = - _S1.eyePosition_0 * float3(rsqrt(lengthSquared_0)) ;
    }
    else
    {
        _S9 = float3(0.0f, 0.0f, 1.0f);
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
    float _S10 = saturate(abs(dot(normal_2, _S9)) + 0.00000999999974738f);
    float _S11 = max(0.00100000004749745f, roughness_0);
    float _S12 = _S5.x;
    float _S13 = max(0.00100000004749745f, _S5.y);
    float4 _S14 = float4(surface_0.specularIor_0) ;
    float _S15 = _S14.w;
    float reflectanceRatio_0 = (1.0f - _S15) / (1.0f + _S15);
    float3 _S16 = float3(3.14159274101257324f) ;
    float3 diffuse_0 = diffuseColor_0 / _S16;
    float3 normalIncidence_0;
    float3 grazingIncidence_0;
    float3 diffuse_1;
    if((_S8.w) >= 0.5f)
    {
        float3 _S17 = float3(1.0f, 1.0f, 1.0f);
        normalIncidence_0 = _S14.xyz;
        grazingIncidence_0 = _S17;
        diffuse_1 = diffuse_0;
    }
    else
    {
        float3 _S18 = float3(metallic_0) ;
        float3 specularTint_0 = mix(float3(1.0f, 1.0f, 1.0f), diffuseColor_0, _S18);
        float3 diffuse_2 = diffuse_0 * float3((1.0f - metallic_0)) ;
        normalIncidence_0 = mix(float3((reflectanceRatio_0 * reflectanceRatio_0))  * specularTint_0, specularTint_0, _S18);
        grazingIncidence_0 = specularTint_0;
        diffuse_1 = diffuse_2;
    }
    float4 _S19 = float4(_S2->ambientLight_0) ;
    float _S20 = _S19.w;
    if(_S20 > 0.5f)
    {
        hasSceneLighting_0 = true;
    }
    else
    {
        float3 _S21 = _S19.xyz;
        hasSceneLighting_0 = (dot(_S21, _S21)) > 0.0f;
    }
    uint _S22 = min(uint(_S20), 4U);
    matrix<float,int(4),int(4)>  _S23 = matrix<float,int(4),int(4)> (_S2->eyeToWorld_0.data_0[int(0)][int(0)], _S2->eyeToWorld_0.data_0[int(0)][int(1)], _S2->eyeToWorld_0.data_0[int(0)][int(2)], _S2->eyeToWorld_0.data_0[int(0)][int(3)], _S2->eyeToWorld_0.data_0[int(1)][int(0)], _S2->eyeToWorld_0.data_0[int(1)][int(1)], _S2->eyeToWorld_0.data_0[int(1)][int(2)], _S2->eyeToWorld_0.data_0[int(1)][int(3)], _S2->eyeToWorld_0.data_0[int(2)][int(0)], _S2->eyeToWorld_0.data_0[int(2)][int(1)], _S2->eyeToWorld_0.data_0[int(2)][int(2)], _S2->eyeToWorld_0.data_0[int(2)][int(3)], _S2->eyeToWorld_0.data_0[int(3)][int(0)], _S2->eyeToWorld_0.data_0[int(3)][int(1)], _S2->eyeToWorld_0.data_0[int(3)][int(2)], _S2->eyeToWorld_0.data_0[int(3)][int(3)]);
    float3 _S24 = normalize((((float4(_S9, 0.0f)) * (_S23))).xyz);
    float3 _S25 = (((float4(_S1.eyePosition_0, 1.0f)) * (_S23))).xyz;
    float4 _S26 = float4(surface_0.lightColorAmbient_0) ;
    float3 color_0 = diffuseColor_0 * float3(_S26.w)  + diffuseColor_0 * _S19.xyz;
    bool _S27 = !hasSceneLighting_0;
    uint lightCount_0;
    if(_S27)
    {
        lightCount_0 = 1U;
    }
    else
    {
        lightCount_0 = _S22;
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
        bool _S28 = lightIndex_0 == 0U;
        if(_S28)
        {
            hasSceneLighting_0 = _S27;
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
        bool _S29;
        if(_S28)
        {
            _S29 = _S27;
        }
        else
        {
            _S29 = false;
        }
        if(_S29)
        {
            diffuseColor_0 = normalize((float4(surface_0.lightDirectionIntensity_0) ).xyz);
        }
        else
        {
            diffuseColor_0 = normalize((float4((&_S2->lightDirectionRadius_0)->data_2[lightIndex_0]) ).xyz);
        }
        bool _S30;
        if(_S28)
        {
            _S30 = _S27;
        }
        else
        {
            _S30 = false;
        }
        float lightIntensity_0;
        if(_S30)
        {
            lightIntensity_0 = (float4(surface_0.lightDirectionIntensity_0) ).w;
        }
        else
        {
            lightIntensity_0 = (float4((&_S2->lightColorIntensity_0)->data_2[lightIndex_0]) ).w;
        }
        bool _S31;
        if(_S28)
        {
            _S31 = _S27;
        }
        else
        {
            _S31 = false;
        }
        float3 lightColor_0;
        if(_S31)
        {
            lightColor_0 = _S26.xyz;
        }
        else
        {
            lightColor_0 = (float4((&_S2->lightColorIntensity_0)->data_2[lightIndex_0]) ).xyz;
        }
        bool _S32;
        if(_S28)
        {
            _S32 = _S27;
        }
        else
        {
            _S32 = false;
        }
        float diffuseScale_0;
        if(_S32)
        {
            diffuseScale_0 = 1.0f;
        }
        else
        {
            diffuseScale_0 = (float4((&_S2->lightControls_0)->data_2[lightIndex_0]) ).x;
        }
        bool _S33;
        if(_S28)
        {
            _S33 = _S27;
        }
        else
        {
            _S33 = false;
        }
        float specularScale_0;
        if(_S33)
        {
            specularScale_0 = 1.0f;
        }
        else
        {
            specularScale_0 = (float4((&_S2->lightControls_0)->data_2[lightIndex_0]) ).y;
        }
        bool _S34;
        if(_S28)
        {
            _S34 = _S27;
        }
        else
        {
            _S34 = false;
        }
        float3 lightEye_0;
        if(_S34)
        {
            lightEye_0 = _S9;
        }
        else
        {
            lightEye_0 = _S24;
        }
        float3 lightDirection_0;
        float lightIntensity_1;
        if(lightType_0 >= 2.0f)
        {
            float3 toLight_0 = (float4((&_S2->lightPositionType_0)->data_2[lightIndex_0]) ).xyz - _S25;
            float _S35 = max(dot(toLight_0, toLight_0), 0.00100000004749745f);
            float lightIntensity_2 = lightIntensity_0 / _S35;
            lightDirection_0 = toLight_0 * float3(rsqrt(_S35)) ;
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
        float3 _S36 = float3(pow(max(0.0f, 1.0f - saturate(dot(lightEye_0, half_0))), 5.0f)) ;
        float3 _S37 = mix(normalIncidence_0, grazingIncidence_0, _S36);
        float3 directDiffuse_0 = diffuse_1 * (float3(1.0f)  - _S37);
        float alpha_0 = _S11 * _S11;
        float alphaSquared_0 = alpha_0 * alpha_0;
        float _S38 = normalDotHalf_0 * normalDotHalf_0;
        float denominator_0 = _S38 * (alphaSquared_0 - 1.0f) + 1.0f;
        float k_0 = alpha_0 * 0.5f;
        float _S39 = 1.0f - k_0;
        float3 _S40 = float3((4.0f * normalDotLight_0 * _S10 + 0.00100000004749745f)) ;
        float3 _S41 = _S37 * float3((_S10 / (_S10 * _S39 + k_0) * (normalDotLight_0 / (normalDotLight_0 * _S39 + k_0))))  * float3(((alphaSquared_0 + 0.00100000004749745f) / (denominator_0 * denominator_0 * 3.14159274101257324f)))  / _S40;
        float3 directSpecular_0;
        if(_S12 > 0.0f)
        {
            float alpha_1 = _S13 * _S13;
            float alphaSquared_1 = alpha_1 * alpha_1;
            float denominator_1 = _S38 * (alphaSquared_1 - 1.0f) + 1.0f;
            float k_1 = alpha_1 * 0.5f;
            float _S42 = 1.0f - k_1;
            directSpecular_0 = _S41 + float3(_S12)  * (mix(float3((reflectanceRatio_0 * reflectanceRatio_0)) , float3(1.0f, 1.0f, 1.0f), _S36) * float3((_S10 / (_S10 * _S42 + k_1) * (normalDotLight_0 / (normalDotLight_0 * _S42 + k_1))))  * float3(((alphaSquared_1 + 0.00100000004749745f) / (denominator_1 * denominator_1 * 3.14159274101257324f)))  / _S40);
        }
        else
        {
            directSpecular_0 = _S41;
        }
        float3 _S43 = lightColor_0 * float3(lightIntensity_1) ;
        float3 color_2 = color_1 + float3((_S7 * normalDotLight_0))  * (directDiffuse_0 * float3(diffuseScale_0)  * (_S43 * _S16) + directSpecular_0 * float3(specularScale_0)  * _S43);
        lightIndex_0 = lightIndex_0 + 1U;
        color_1 = color_2;
    }
    pixelOutput_0 _S44 = { float4(color_1 + emissiveColor_0, opacity_0) };
    return _S44;
}

