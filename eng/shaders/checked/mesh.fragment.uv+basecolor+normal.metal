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
    _Array_natural_vectorx3Cfloatx2C4x3E4_0 lightTangentShapeX_0;
    _Array_natural_vectorx3Cfloatx2C4x3E4_0 lightBitangentShapeY_0;
    _MatrixStorage_float4x4natural_0 eyeToWorld_0;
};

struct KernelContext_0
{
    SurfaceParameters_natural_0 device* surfaceParameters_0;
    FrameParameters_natural_0 device* frameParameters_0;
    texture2d<float, access::sample> baseColorTexture_0;
    sampler materialSampler_0;
    texture2d<float, access::sample> normalTexture_0;
};

[[fragment]] pixelOutput_0 fragmentMain_uv_basecolor_normal(pixelInput_0 _S1 [[stage_in]], bool isFrontFace_0 [[front_facing]], float4 position_0 [[position]], SurfaceParameters_natural_0 device* surfaceParameters_1 [[buffer(7)]], FrameParameters_natural_0 device* frameParameters_1 [[buffer(8)]], texture2d<float, access::sample> baseColorTexture_1 [[texture(0)]], sampler materialSampler_1 [[sampler(0)]], texture2d<float, access::sample> normalTexture_1 [[texture(1)]])
{
    thread KernelContext_0 kernelContext_0;
    (&kernelContext_0)->surfaceParameters_0 = surfaceParameters_1;
    (&kernelContext_0)->frameParameters_0 = frameParameters_1;
    (&kernelContext_0)->baseColorTexture_0 = baseColorTexture_1;
    (&kernelContext_0)->materialSampler_0 = materialSampler_1;
    (&kernelContext_0)->normalTexture_0 = normalTexture_1;
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
    float4 _S8 = float4(surface_0.reserved_0) ;
    if((_S8.x) >= 0.5f)
    {
        pixelOutput_0 _S9 = { float4(diffuseColor_0 * float3((1.0f - exp(- max(0.0f, _S8.y) * max(0.0f, _S8.z)))) , 1.0f) };
        return _S9;
    }
    float4 _S10 = float4(surface_0.metallicRoughnessThresholdWorkflow_0) ;
    float metallic_0 = saturate(_S10.x);
    float roughness_0 = clamp(_S10.y, 0.00999999977648258f, 1.0f);
    float4 sampledBaseColor_0 = (((&kernelContext_0)->baseColorTexture_0).sample(((&kernelContext_0)->materialSampler_0), (_S1.texCoord_0)));
    float3 diffuseColor_1 = sampledBaseColor_0.xyz;
    float opacity_1 = opacity_0 * sampledBaseColor_0.w;
    float3 _S11 = float3(1.0f) ;
    float3 sampledNormal_0 = (((&kernelContext_0)->normalTexture_0).sample(((&kernelContext_0)->materialSampler_0), (_S1.texCoord_0))).xyz * float3(2.0f)  - _S11;
    float3 tangent_1 = normalize(_S1.tangent_0.xyz);
    float3 shadingNormal_0 = normalize(tangent_1 * float3(sampledNormal_0.x)  + cross(normalize(_S1.normal_0), tangent_1) * float3(_S1.tangent_0.w)  * float3(sampledNormal_0.y)  + _S1.normal_0 * float3(sampledNormal_0.z) );
    float opacityThreshold_0 = _S10.z;
    bool hasSceneLighting_0;
    if(opacityThreshold_0 > 0.0f)
    {
        hasSceneLighting_0 = opacity_1 < opacityThreshold_0;
    }
    else
    {
        hasSceneLighting_0 = false;
    }
    if(hasSceneLighting_0)
    {
        discard_fragment();
    }
    float3 normal_1 = normalize(shadingNormal_0);
    float lengthSquared_0 = dot(_S1.eyePosition_0, _S1.eyePosition_0);
    if(lengthSquared_0 > 0.00100000004749745f)
    {
        diffuseColor_0 = - _S1.eyePosition_0 * float3(rsqrt(lengthSquared_0)) ;
    }
    else
    {
        diffuseColor_0 = float3(0.0f, 0.0f, 1.0f);
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
    float _S12 = saturate(abs(dot(normal_2, diffuseColor_0)) + 0.00000999999974738f);
    float _S13 = max(0.00100000004749745f, roughness_0);
    float _S14 = _S5.x;
    float _S15 = max(0.00100000004749745f, _S5.y);
    float4 _S16 = float4(surface_0.specularIor_0) ;
    float _S17 = _S16.w;
    float reflectanceRatio_0 = (1.0f - _S17) / (1.0f + _S17);
    float3 _S18 = float3(3.14159274101257324f) ;
    float3 diffuse_0 = diffuseColor_1 / _S18;
    float3 normalIncidence_0;
    float3 grazingIncidence_0;
    float3 diffuse_1;
    if((_S10.w) >= 0.5f)
    {
        float3 _S19 = float3(1.0f, 1.0f, 1.0f);
        normalIncidence_0 = _S16.xyz;
        grazingIncidence_0 = _S19;
        diffuse_1 = diffuse_0;
    }
    else
    {
        float3 _S20 = float3(metallic_0) ;
        float3 specularTint_0 = mix(float3(1.0f, 1.0f, 1.0f), diffuseColor_1, _S20);
        float3 diffuse_2 = diffuse_0 * float3((1.0f - metallic_0)) ;
        normalIncidence_0 = mix(float3((reflectanceRatio_0 * reflectanceRatio_0))  * specularTint_0, specularTint_0, _S20);
        grazingIncidence_0 = specularTint_0;
        diffuse_1 = diffuse_2;
    }
    float4 _S21 = float4(_S2->ambientLight_0) ;
    float _S22 = _S21.w;
    if(_S22 > 0.5f)
    {
        hasSceneLighting_0 = true;
    }
    else
    {
        float3 _S23 = _S21.xyz;
        hasSceneLighting_0 = (dot(_S23, _S23)) > 0.0f;
    }
    uint _S24 = min(uint(_S22), 4U);
    matrix<float,int(4),int(4)>  _S25 = matrix<float,int(4),int(4)> (_S2->eyeToWorld_0.data_0[int(0)][int(0)], _S2->eyeToWorld_0.data_0[int(0)][int(1)], _S2->eyeToWorld_0.data_0[int(0)][int(2)], _S2->eyeToWorld_0.data_0[int(0)][int(3)], _S2->eyeToWorld_0.data_0[int(1)][int(0)], _S2->eyeToWorld_0.data_0[int(1)][int(1)], _S2->eyeToWorld_0.data_0[int(1)][int(2)], _S2->eyeToWorld_0.data_0[int(1)][int(3)], _S2->eyeToWorld_0.data_0[int(2)][int(0)], _S2->eyeToWorld_0.data_0[int(2)][int(1)], _S2->eyeToWorld_0.data_0[int(2)][int(2)], _S2->eyeToWorld_0.data_0[int(2)][int(3)], _S2->eyeToWorld_0.data_0[int(3)][int(0)], _S2->eyeToWorld_0.data_0[int(3)][int(1)], _S2->eyeToWorld_0.data_0[int(3)][int(2)], _S2->eyeToWorld_0.data_0[int(3)][int(3)]);
    float3 _S26 = normalize((((float4(diffuseColor_0, 0.0f)) * (_S25))).xyz);
    float3 _S27 = (((float4(_S1.eyePosition_0, 1.0f)) * (_S25))).xyz;
    float4 _S28 = float4(surface_0.lightColorAmbient_0) ;
    float3 color_0 = diffuseColor_1 * float3(_S28.w)  + diffuseColor_1 * _S21.xyz;
    bool _S29 = !hasSceneLighting_0;
    uint lightCount_0;
    if(_S29)
    {
        lightCount_0 = 1U;
    }
    else
    {
        lightCount_0 = _S24;
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
        bool _S30 = lightIndex_0 == 0U;
        if(_S30)
        {
            hasSceneLighting_0 = _S29;
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
        bool _S31;
        if(_S30)
        {
            _S31 = _S29;
        }
        else
        {
            _S31 = false;
        }
        float3 lightDirection_0;
        if(_S31)
        {
            lightDirection_0 = normalize((float4(surface_0.lightDirectionIntensity_0) ).xyz);
        }
        else
        {
            lightDirection_0 = normalize((float4((&_S2->lightDirectionRadius_0)->data_2[lightIndex_0]) ).xyz);
        }
        bool _S32;
        if(_S30)
        {
            _S32 = _S29;
        }
        else
        {
            _S32 = false;
        }
        if(_S32)
        {
            opacity_0 = (float4(surface_0.lightDirectionIntensity_0) ).w;
        }
        else
        {
            opacity_0 = (float4((&_S2->lightColorIntensity_0)->data_2[lightIndex_0]) ).w;
        }
        bool _S33;
        if(_S30)
        {
            _S33 = _S29;
        }
        else
        {
            _S33 = false;
        }
        float3 _S34;
        if(_S33)
        {
            _S34 = _S28.xyz;
        }
        else
        {
            _S34 = (float4((&_S2->lightColorIntensity_0)->data_2[lightIndex_0]) ).xyz;
        }
        bool _S35;
        if(_S30)
        {
            _S35 = _S29;
        }
        else
        {
            _S35 = false;
        }
        float _S36;
        if(_S35)
        {
            _S36 = 1.0f;
        }
        else
        {
            _S36 = (float4((&_S2->lightControls_0)->data_2[lightIndex_0]) ).x;
        }
        bool _S37;
        if(_S30)
        {
            _S37 = _S29;
        }
        else
        {
            _S37 = false;
        }
        float _S38;
        if(_S37)
        {
            _S38 = 1.0f;
        }
        else
        {
            _S38 = (float4((&_S2->lightControls_0)->data_2[lightIndex_0]) ).y;
        }
        bool _S39;
        if(_S30)
        {
            _S39 = _S29;
        }
        else
        {
            _S39 = false;
        }
        float3 lightTangent_0;
        if(_S39)
        {
            lightTangent_0 = float3(1.0f, 0.0f, 0.0f);
        }
        else
        {
            lightTangent_0 = normalize((float4((&_S2->lightTangentShapeX_0)->data_2[lightIndex_0]) ).xyz);
        }
        bool _S40;
        if(_S30)
        {
            _S40 = _S29;
        }
        else
        {
            _S40 = false;
        }
        float3 lightBitangent_0;
        if(_S40)
        {
            lightBitangent_0 = float3(0.0f, 1.0f, 0.0f);
        }
        else
        {
            lightBitangent_0 = normalize((float4((&_S2->lightBitangentShapeY_0)->data_2[lightIndex_0]) ).xyz);
        }
        bool _S41;
        if(_S30)
        {
            _S41 = _S29;
        }
        else
        {
            _S41 = false;
        }
        float shapeX_0;
        if(_S41)
        {
            shapeX_0 = 0.0f;
        }
        else
        {
            shapeX_0 = (float4((&_S2->lightTangentShapeX_0)->data_2[lightIndex_0]) ).w;
        }
        bool _S42;
        if(_S30)
        {
            _S42 = _S29;
        }
        else
        {
            _S42 = false;
        }
        float shapeY_0;
        if(_S42)
        {
            shapeY_0 = 0.0f;
        }
        else
        {
            shapeY_0 = (float4((&_S2->lightBitangentShapeY_0)->data_2[lightIndex_0]) ).w;
        }
        bool _S43;
        if(_S30)
        {
            _S43 = _S29;
        }
        else
        {
            _S43 = false;
        }
        float lightRadius_0;
        if(_S43)
        {
            lightRadius_0 = 0.0f;
        }
        else
        {
            lightRadius_0 = (float4((&_S2->lightDirectionRadius_0)->data_2[lightIndex_0]) ).w;
        }
        bool _S44;
        if(_S30)
        {
            _S44 = _S29;
        }
        else
        {
            _S44 = false;
        }
        float3 _S45;
        if(_S44)
        {
            _S45 = diffuseColor_0;
        }
        else
        {
            _S45 = _S26;
        }
        thread array<float3, int(5)> sampleOffsets_0;
        float3 _S46 = float3(0.0f, 0.0f, 0.0f);
        sampleOffsets_0[int(0)] = _S46;
        sampleOffsets_0[int(1)] = _S46;
        sampleOffsets_0[int(2)] = _S46;
        sampleOffsets_0[int(3)] = _S46;
        sampleOffsets_0[int(4)] = _S46;
        float sampleCount_0;
        if(lightType_0 == 3.0f)
        {
            float3 halfWidth_0 = lightTangent_0 * float3((shapeX_0 * 0.5f)) ;
            float3 halfHeight_0 = lightBitangent_0 * float3((shapeY_0 * 0.5f)) ;
            sampleOffsets_0[int(1)] = halfWidth_0 + halfHeight_0;
            sampleOffsets_0[int(2)] = halfWidth_0 - halfHeight_0;
            float3 _S47 = - halfWidth_0;
            sampleOffsets_0[int(3)] = _S47 + halfHeight_0;
            sampleOffsets_0[int(4)] = _S47 - halfHeight_0;
            sampleCount_0 = 5.0f;
        }
        else
        {
            if(lightType_0 == 4.0f)
            {
                sampleOffsets_0[int(1)] = lightTangent_0 * float3(lightRadius_0) ;
                sampleOffsets_0[int(2)] = - lightTangent_0 * float3(lightRadius_0) ;
                sampleOffsets_0[int(3)] = lightBitangent_0 * float3(lightRadius_0) ;
                sampleOffsets_0[int(4)] = - lightBitangent_0 * float3(lightRadius_0) ;
                sampleCount_0 = 5.0f;
            }
            else
            {
                if(lightType_0 == 5.0f)
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
            float sampleIntensity_0 = opacity_0 / sampleCount_0;
            float3 sampleDirection_0;
            float emissionScale_0;
            float sampleIntensity_1;
            if(lightType_0 >= 2.0f)
            {
                float3 toLight_0 = (float4((&_S2->lightPositionType_0)->data_2[lightIndex_0]) ).xyz + sampleOffsets_0[sampleIndex_0] - _S27;
                float _S48 = max(dot(toLight_0, toLight_0), 0.00100000004749745f);
                float3 sampleDirection_1 = toLight_0 * float3(rsqrt(_S48)) ;
                float sampleIntensity_2 = sampleIntensity_0 / _S48;
                if(lightType_0 == 4.0f)
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
            float3 half_0 = normalize(sampleDirection_0 + _S45);
            float normalDotLight_0 = saturate(dot(normal_2, sampleDirection_0));
            float normalDotHalf_0 = saturate(dot(normal_2, half_0));
            float3 _S49 = float3(pow(max(0.0f, 1.0f - saturate(dot(_S45, half_0))), 5.0f)) ;
            float3 _S50 = mix(normalIncidence_0, grazingIncidence_0, _S49);
            float3 directDiffuse_0 = diffuse_1 * (_S11 - _S50);
            float alpha_0 = _S13 * _S13;
            float alphaSquared_0 = alpha_0 * alpha_0;
            float _S51 = normalDotHalf_0 * normalDotHalf_0;
            float denominator_0 = _S51 * (alphaSquared_0 - 1.0f) + 1.0f;
            float k_0 = alpha_0 * 0.5f;
            float _S52 = 1.0f - k_0;
            float3 _S53 = float3((4.0f * normalDotLight_0 * _S12 + 0.00100000004749745f)) ;
            float3 _S54 = _S50 * float3((_S12 / (_S12 * _S52 + k_0) * (normalDotLight_0 / (normalDotLight_0 * _S52 + k_0))))  * float3(((alphaSquared_0 + 0.00100000004749745f) / (denominator_0 * denominator_0 * 3.14159274101257324f)))  / _S53;
            float3 directSpecular_0;
            if(_S14 > 0.0f)
            {
                float alpha_1 = _S15 * _S15;
                float alphaSquared_1 = alpha_1 * alpha_1;
                float denominator_1 = _S51 * (alphaSquared_1 - 1.0f) + 1.0f;
                float k_1 = alpha_1 * 0.5f;
                float _S55 = 1.0f - k_1;
                directSpecular_0 = _S54 + float3(_S14)  * (mix(float3((reflectanceRatio_0 * reflectanceRatio_0)) , float3(1.0f, 1.0f, 1.0f), _S49) * float3((_S12 / (_S12 * _S55 + k_1) * (normalDotLight_0 / (normalDotLight_0 * _S55 + k_1))))  * float3(((alphaSquared_1 + 0.00100000004749745f) / (denominator_1 * denominator_1 * 3.14159274101257324f)))  / _S53);
            }
            else
            {
                directSpecular_0 = _S54;
            }
            float3 _S56 = _S34 * float3(sampleIntensity_1) ;
            color_2 = color_2 + float3((_S7 * emissionScale_0 * normalDotLight_0))  * (directDiffuse_0 * float3(_S36)  * (_S56 * _S18) + directSpecular_0 * float3(_S38)  * _S56);
            sampleIndex_0 = sampleIndex_0 + 1U;
        }
        lightIndex_0 = lightIndex_0 + 1U;
        color_1 = color_2;
    }
    pixelOutput_0 _S57 = { float4(color_1 + emissiveColor_0, opacity_1) };
    return _S57;
}

