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
    float3 objectPosition_0 [[user(TEXCOORD_2)]];
    float3 normal_0 [[user(NORMAL)]];
    float4 tint_0 [[user(COLOR)]];
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
};

struct _MatrixStorage_float4x4natural_0
{
    array<packed_float4, int(4)> data_0;
};

struct _Array_natural_vectorx3Cfloatx2C4x3E8_0
{
    array<packed_float4, int(8)> data_1;
};

struct FrameParameters_natural_0
{
    _MatrixStorage_float4x4natural_0 clipToEye_0;
    packed_uint4 clipPlaneCount_0;
    _Array_natural_vectorx3Cfloatx2C4x3E8_0 clipPlanes_0;
    packed_float4 ambientLight_0;
    _Array_natural_vectorx3Cfloatx2C4x3E8_0 lightPositionType_0;
    _Array_natural_vectorx3Cfloatx2C4x3E8_0 lightDirectionRadius_0;
    _Array_natural_vectorx3Cfloatx2C4x3E8_0 lightColorIntensity_0;
    _Array_natural_vectorx3Cfloatx2C4x3E8_0 lightControls_0;
    _Array_natural_vectorx3Cfloatx2C4x3E8_0 lightTangentShapeX_0;
    _Array_natural_vectorx3Cfloatx2C4x3E8_0 lightBitangentShapeY_0;
    _MatrixStorage_float4x4natural_0 eyeToWorld_0;
};

struct KernelContext_0
{
    SurfaceParameters_natural_0 device* surfaceParameters_0;
    FrameParameters_natural_0 device* frameParameters_0;
    texture3d<float, access::sample> volumeDensityTexture_0;
    sampler volumeDensitySampler_0;
};

[[fragment]] pixelOutput_0 volumeFragmentMain(pixelInput_0 _S1 [[stage_in]], bool isFrontFace_0 [[front_facing]], float4 position_0 [[position]], SurfaceParameters_natural_0 device* surfaceParameters_1 [[buffer(7)]], FrameParameters_natural_0 device* frameParameters_1 [[buffer(8)]], texture3d<float, access::sample> volumeDensityTexture_1 [[texture(9)]], sampler volumeDensitySampler_1 [[sampler(4)]])
{
    uint4 _S2;
    float _S3;
    thread KernelContext_0 kernelContext_0;
    (&kernelContext_0)->surfaceParameters_0 = surfaceParameters_1;
    (&kernelContext_0)->frameParameters_0 = frameParameters_1;
    (&kernelContext_0)->volumeDensityTexture_0 = volumeDensityTexture_1;
    (&kernelContext_0)->volumeDensitySampler_0 = volumeDensitySampler_1;
    SurfaceParameters_natural_0 device* _S4 = surfaceParameters_1+int(0);
    FrameParameters_natural_0 device* _S5 = frameParameters_1+int(0);
    for(;;)
    {
        uint4 _S6 = uint4(_S5->clipPlaneCount_0) ;
        _S2 = _S6;
        uint _S7 = min(_S6.x, 8U);
        uint index_0 = 0U;
        for(;;)
        {
            if(index_0 < _S7)
            {
            }
            else
            {
                break;
            }
            float4 _S8 = float4((&_S5->clipPlanes_0)->data_1[index_0]) ;
            if((dot(_S8.xyz, _S1.eyePosition_0) + _S8.w) < 0.0f)
            {
                discard_fragment();
            }
            index_0 = index_0 + 1U;
        }
        break;
    }
    float4 _S9 = float4(_S4->clearcoatShaded_0) ;
    bool shaded_0 = (_S9.z) >= 0.5f;
    float3 diffuseColor_0;
    if(shaded_0)
    {
        diffuseColor_0 = (float4(_S4->diffuseOpacity_0) ).xyz;
    }
    else
    {
        diffuseColor_0 = _S1.tint_0.xyz;
    }
    float opacity_0;
    if(shaded_0)
    {
        opacity_0 = (float4(_S4->diffuseOpacity_0) ).w;
    }
    else
    {
        opacity_0 = _S1.tint_0.w;
    }
    float4 _S10 = float4(_S4->emissiveOcclusion_0) ;
    float3 emissiveColor_0 = _S10.xyz;
    float _S11 = _S10.w;
    float4 _S12 = float4(_S4->reserved_0) ;
    uint step_0;
    float density_0;
    if((_S12.x) >= 0.5f)
    {
        if((_S12.w) >= 0.5f)
        {
            float _S13 = (float4(_S4->textureControls_0) ).z;
            for(;;)
            {
                float2 _S14 = saturate(_S1.objectPosition_0.xy + float2(0.5f, 0.5f));
                uint stepCount_0 = uint(clamp(_S13, 1.0f, 512.0f));
                step_0 = 0U;
                density_0 = 0.0f;
                for(;;)
                {
                    if(step_0 < stepCount_0)
                    {
                    }
                    else
                    {
                        break;
                    }
                    float density_1 = density_0 + (((&kernelContext_0)->volumeDensityTexture_0).sample(((&kernelContext_0)->volumeDensitySampler_0), (float3(_S14, (float(step_0) + 0.5f) / float(stepCount_0)))).x);
                    step_0 = step_0 + 1U;
                    density_0 = density_1;
                }
                _S3 = density_0 / float(stepCount_0);
                break;
            }
            density_0 = _S3 * max(0.0f, _S12.y);
        }
        else
        {
            density_0 = max(0.0f, _S12.y);
        }
        pixelOutput_0 _S15 = { float4(diffuseColor_0 * float3((1.0f - exp(- density_0 * max(0.0f, _S12.z)))) , 1.0f) };
        return _S15;
    }
    float4 _S16 = float4(_S4->metallicRoughnessThresholdWorkflow_0) ;
    float metallic_0 = saturate(_S16.x);
    float roughness_0 = clamp(_S16.y, 0.00999999977648258f, 1.0f);
    float4 _S17 = float4(_S4->specularIor_0) ;
    float3 specularColor_0 = _S17.xyz;
    float ior_0 = _S17.w;
    float _S18 = _S9.x;
    float clearcoatRoughness_0 = _S9.y;
    float opacityThreshold_0 = _S16.z;
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
    float3 _S19;
    if(lengthSquared_0 > 0.00100000004749745f)
    {
        _S19 = - _S1.eyePosition_0 * float3(rsqrt(lengthSquared_0)) ;
    }
    else
    {
        _S19 = float3(0.0f, 0.0f, 1.0f);
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
    float _S20 = saturate(abs(dot(normal_2, _S19)) + 0.00000999999974738f);
    float _S21 = max(0.00100000004749745f, roughness_0);
    float _S22 = max(0.00100000004749745f, clearcoatRoughness_0);
    float reflectanceRatio_0 = (1.0f - ior_0) / (1.0f + ior_0);
    float3 _S23 = float3(3.14159274101257324f) ;
    float3 diffuse_0 = diffuseColor_0 / _S23;
    float3 normalIncidence_0;
    float3 grazingIncidence_0;
    float3 diffuse_1;
    if((_S16.w) >= 0.5f)
    {
        float3 _S24 = float3(1.0f, 1.0f, 1.0f);
        normalIncidence_0 = specularColor_0;
        grazingIncidence_0 = _S24;
        diffuse_1 = diffuse_0;
    }
    else
    {
        float3 _S25 = float3(metallic_0) ;
        float3 specularTint_0 = mix(float3(1.0f, 1.0f, 1.0f), diffuseColor_0, _S25);
        float3 diffuse_2 = diffuse_0 * float3((1.0f - metallic_0)) ;
        normalIncidence_0 = mix(float3((reflectanceRatio_0 * reflectanceRatio_0))  * specularTint_0, specularTint_0, _S25);
        grazingIncidence_0 = specularTint_0;
        diffuse_1 = diffuse_2;
    }
    float4 _S26 = float4(_S5->ambientLight_0) ;
    float _S27 = _S26.w;
    if(_S27 > 0.5f)
    {
        hasSceneLighting_0 = true;
    }
    else
    {
        float3 _S28 = _S26.xyz;
        hasSceneLighting_0 = (dot(_S28, _S28)) > 0.0f;
    }
    uint _S29 = min(uint(_S27), 8U);
    matrix<float,int(4),int(4)>  _S30 = matrix<float,int(4),int(4)> (_S5->eyeToWorld_0.data_0[int(0)][int(0)], _S5->eyeToWorld_0.data_0[int(0)][int(1)], _S5->eyeToWorld_0.data_0[int(0)][int(2)], _S5->eyeToWorld_0.data_0[int(0)][int(3)], _S5->eyeToWorld_0.data_0[int(1)][int(0)], _S5->eyeToWorld_0.data_0[int(1)][int(1)], _S5->eyeToWorld_0.data_0[int(1)][int(2)], _S5->eyeToWorld_0.data_0[int(1)][int(3)], _S5->eyeToWorld_0.data_0[int(2)][int(0)], _S5->eyeToWorld_0.data_0[int(2)][int(1)], _S5->eyeToWorld_0.data_0[int(2)][int(2)], _S5->eyeToWorld_0.data_0[int(2)][int(3)], _S5->eyeToWorld_0.data_0[int(3)][int(0)], _S5->eyeToWorld_0.data_0[int(3)][int(1)], _S5->eyeToWorld_0.data_0[int(3)][int(2)], _S5->eyeToWorld_0.data_0[int(3)][int(3)]);
    float3 _S31 = normalize((((float4(_S19, 0.0f)) * (_S30))).xyz);
    float3 _S32 = (((float4(_S1.eyePosition_0, 1.0f)) * (_S30))).xyz;
    float4 _S33 = float4(_S4->lightColorAmbient_0) ;
    float3 color_0 = diffuseColor_0 * float3(_S33.w)  + diffuseColor_0 * _S26.xyz;
    bool _S34 = !hasSceneLighting_0;
    if(_S34)
    {
        step_0 = 1U;
    }
    else
    {
        step_0 = _S29;
    }
    uint lightIndex_0 = 0U;
    float3 color_1 = color_0;
    for(;;)
    {
        if(lightIndex_0 < step_0)
        {
        }
        else
        {
            break;
        }
        bool _S35 = lightIndex_0 == 0U;
        if(_S35)
        {
            hasSceneLighting_0 = _S34;
        }
        else
        {
            hasSceneLighting_0 = false;
        }
        if(hasSceneLighting_0)
        {
            density_0 = 1.0f;
        }
        else
        {
            density_0 = (float4((&_S5->lightPositionType_0)->data_1[lightIndex_0]) ).w;
        }
        bool _S36;
        if(_S35)
        {
            _S36 = _S34;
        }
        else
        {
            _S36 = false;
        }
        float3 lightDirection_0;
        if(_S36)
        {
            lightDirection_0 = normalize((float4(_S4->lightDirectionIntensity_0) ).xyz);
        }
        else
        {
            lightDirection_0 = normalize((float4((&_S5->lightDirectionRadius_0)->data_1[lightIndex_0]) ).xyz);
        }
        bool _S37;
        if(_S35)
        {
            _S37 = _S34;
        }
        else
        {
            _S37 = false;
        }
        float _S38;
        if(_S37)
        {
            _S38 = (float4(_S4->lightDirectionIntensity_0) ).w;
        }
        else
        {
            _S38 = (float4((&_S5->lightColorIntensity_0)->data_1[lightIndex_0]) ).w;
        }
        bool _S39;
        if(_S35)
        {
            _S39 = _S34;
        }
        else
        {
            _S39 = false;
        }
        if(_S39)
        {
            diffuseColor_0 = _S33.xyz;
        }
        else
        {
            diffuseColor_0 = (float4((&_S5->lightColorIntensity_0)->data_1[lightIndex_0]) ).xyz;
        }
        bool _S40;
        if(_S35)
        {
            _S40 = _S34;
        }
        else
        {
            _S40 = false;
        }
        float _S41;
        if(_S40)
        {
            _S41 = 1.0f;
        }
        else
        {
            _S41 = (float4((&_S5->lightControls_0)->data_1[lightIndex_0]) ).x;
        }
        bool _S42;
        if(_S35)
        {
            _S42 = _S34;
        }
        else
        {
            _S42 = false;
        }
        float _S43;
        if(_S42)
        {
            _S43 = 1.0f;
        }
        else
        {
            _S43 = (float4((&_S5->lightControls_0)->data_1[lightIndex_0]) ).y;
        }
        bool _S44;
        if(_S35)
        {
            _S44 = _S34;
        }
        else
        {
            _S44 = false;
        }
        float3 lightTangent_0;
        if(_S44)
        {
            lightTangent_0 = float3(1.0f, 0.0f, 0.0f);
        }
        else
        {
            lightTangent_0 = normalize((float4((&_S5->lightTangentShapeX_0)->data_1[lightIndex_0]) ).xyz);
        }
        bool _S45;
        if(_S35)
        {
            _S45 = _S34;
        }
        else
        {
            _S45 = false;
        }
        float3 lightBitangent_0;
        if(_S45)
        {
            lightBitangent_0 = float3(0.0f, 1.0f, 0.0f);
        }
        else
        {
            lightBitangent_0 = normalize((float4((&_S5->lightBitangentShapeY_0)->data_1[lightIndex_0]) ).xyz);
        }
        bool _S46;
        if(_S35)
        {
            _S46 = _S34;
        }
        else
        {
            _S46 = false;
        }
        float shapeX_0;
        if(_S46)
        {
            shapeX_0 = 0.0f;
        }
        else
        {
            shapeX_0 = (float4((&_S5->lightTangentShapeX_0)->data_1[lightIndex_0]) ).w;
        }
        bool _S47;
        if(_S35)
        {
            _S47 = _S34;
        }
        else
        {
            _S47 = false;
        }
        float shapeY_0;
        if(_S47)
        {
            shapeY_0 = 0.0f;
        }
        else
        {
            shapeY_0 = (float4((&_S5->lightBitangentShapeY_0)->data_1[lightIndex_0]) ).w;
        }
        bool _S48;
        if(_S35)
        {
            _S48 = _S34;
        }
        else
        {
            _S48 = false;
        }
        float lightRadius_0;
        if(_S48)
        {
            lightRadius_0 = 0.0f;
        }
        else
        {
            lightRadius_0 = (float4((&_S5->lightDirectionRadius_0)->data_1[lightIndex_0]) ).w;
        }
        bool _S49;
        if(_S35)
        {
            _S49 = _S34;
        }
        else
        {
            _S49 = false;
        }
        float3 _S50;
        if(_S49)
        {
            _S50 = _S19;
        }
        else
        {
            _S50 = _S31;
        }
        thread array<float3, int(5)> sampleOffsets_0;
        float3 _S51 = float3(0.0f, 0.0f, 0.0f);
        sampleOffsets_0[int(0)] = _S51;
        sampleOffsets_0[int(1)] = _S51;
        sampleOffsets_0[int(2)] = _S51;
        sampleOffsets_0[int(3)] = _S51;
        sampleOffsets_0[int(4)] = _S51;
        float sampleCount_0;
        if(density_0 == 3.0f)
        {
            float3 halfWidth_0 = lightTangent_0 * float3((shapeX_0 * 0.5f)) ;
            float3 halfHeight_0 = lightBitangent_0 * float3((shapeY_0 * 0.5f)) ;
            sampleOffsets_0[int(1)] = halfWidth_0 + halfHeight_0;
            sampleOffsets_0[int(2)] = halfWidth_0 - halfHeight_0;
            float3 _S52 = - halfWidth_0;
            sampleOffsets_0[int(3)] = _S52 + halfHeight_0;
            sampleOffsets_0[int(4)] = _S52 - halfHeight_0;
            sampleCount_0 = 5.0f;
        }
        else
        {
            if(density_0 == 4.0f)
            {
                sampleOffsets_0[int(1)] = lightTangent_0 * float3(lightRadius_0) ;
                sampleOffsets_0[int(2)] = - lightTangent_0 * float3(lightRadius_0) ;
                sampleOffsets_0[int(3)] = lightBitangent_0 * float3(lightRadius_0) ;
                sampleOffsets_0[int(4)] = - lightBitangent_0 * float3(lightRadius_0) ;
                sampleCount_0 = 5.0f;
            }
            else
            {
                if(density_0 == 5.0f)
                {
                    float3 halfLength_0 = lightDirection_0 * float3((shapeX_0 * 0.5f)) ;
                    sampleOffsets_0[int(1)] = halfLength_0;
                    sampleOffsets_0[int(2)] = - halfLength_0;
                    sampleCount_0 = 3.0f;
                }
                else
                {
                    sampleCount_0 = 1.0f;
                }
            }
        }
        uint sampleIndex_0 = 0U;
        float3 color_2 = color_1;
        for(;;)
        {
            if(sampleIndex_0 < 5U)
            {
            }
            else
            {
                break;
            }
            if(float(sampleIndex_0) >= sampleCount_0)
            {
                sampleIndex_0 = sampleIndex_0 + 1U;
                continue;
            }
            float sampleIntensity_0 = _S38 / sampleCount_0;
            float3 sampleDirection_0;
            float emissionScale_0;
            float sampleIntensity_1;
            if(density_0 >= 2.0f)
            {
                float3 toLight_0 = (float4((&_S5->lightPositionType_0)->data_1[lightIndex_0]) ).xyz + sampleOffsets_0[sampleIndex_0] - _S32;
                float _S53 = max(dot(toLight_0, toLight_0), 0.00100000004749745f);
                float3 sampleDirection_1 = toLight_0 * float3(rsqrt(_S53)) ;
                float sampleIntensity_2 = sampleIntensity_0 / _S53;
                if(density_0 == 4.0f)
                {
                    emissionScale_0 = saturate(dot(lightDirection_0, - sampleDirection_1));
                }
                else
                {
                    emissionScale_0 = 1.0f;
                }
                sampleDirection_0 = sampleDirection_1;
                sampleIntensity_1 = sampleIntensity_2;
            }
            else
            {
                sampleDirection_0 = lightDirection_0;
                emissionScale_0 = 1.0f;
                sampleIntensity_1 = sampleIntensity_0;
            }
            float3 half_0 = normalize(sampleDirection_0 + _S50);
            float normalDotLight_0 = saturate(dot(normal_2, sampleDirection_0));
            float normalDotHalf_0 = saturate(dot(normal_2, half_0));
            float3 _S54 = float3(pow(max(0.0f, 1.0f - saturate(dot(_S50, half_0))), 5.0f)) ;
            float3 _S55 = mix(normalIncidence_0, grazingIncidence_0, _S54);
            float3 directDiffuse_0 = diffuse_1 * (float3(1.0f)  - _S55);
            float alpha_0 = _S21 * _S21;
            float alphaSquared_0 = alpha_0 * alpha_0;
            float _S56 = normalDotHalf_0 * normalDotHalf_0;
            float denominator_0 = _S56 * (alphaSquared_0 - 1.0f) + 1.0f;
            float k_0 = alpha_0 * 0.5f;
            float _S57 = 1.0f - k_0;
            float3 _S58 = float3((4.0f * normalDotLight_0 * _S20 + 0.00100000004749745f)) ;
            float3 _S59 = _S55 * float3((_S20 / (_S20 * _S57 + k_0) * (normalDotLight_0 / (normalDotLight_0 * _S57 + k_0))))  * float3(((alphaSquared_0 + 0.00100000004749745f) / (denominator_0 * denominator_0 * 3.14159274101257324f)))  / _S58;
            float3 directSpecular_0;
            if(_S18 > 0.0f)
            {
                float alpha_1 = _S22 * _S22;
                float alphaSquared_1 = alpha_1 * alpha_1;
                float denominator_1 = _S56 * (alphaSquared_1 - 1.0f) + 1.0f;
                float k_1 = alpha_1 * 0.5f;
                float _S60 = 1.0f - k_1;
                directSpecular_0 = _S59 + float3(_S18)  * (mix(float3((reflectanceRatio_0 * reflectanceRatio_0)) , float3(1.0f, 1.0f, 1.0f), _S54) * float3((_S20 / (_S20 * _S60 + k_1) * (normalDotLight_0 / (normalDotLight_0 * _S60 + k_1))))  * float3(((alphaSquared_1 + 0.00100000004749745f) / (denominator_1 * denominator_1 * 3.14159274101257324f)))  / _S58);
            }
            else
            {
                directSpecular_0 = _S59;
            }
            float3 _S61 = diffuseColor_0 * float3(sampleIntensity_1) ;
            color_2 = color_2 + float3((_S11 * emissionScale_0 * normalDotLight_0))  * (directDiffuse_0 * float3(_S41)  * (_S61 * _S23) + directSpecular_0 * float3(_S43)  * _S61);
            sampleIndex_0 = sampleIndex_0 + 1U;
        }
        lightIndex_0 = lightIndex_0 + 1U;
        color_1 = color_2;
    }
    float3 color_3 = (color_1 + emissiveColor_0) * float3(exp2((as_type<float>((_S2.z))))) ;
    if((_S2.y) == 1U)
    {
        color_1 = color_3 / (float3(1.0f)  + max(color_3, float3(0.0f, 0.0f, 0.0f)));
    }
    else
    {
        color_1 = color_3;
    }
    pixelOutput_0 _S62 = { float4(color_1, opacity_0) };
    return _S62;
}
