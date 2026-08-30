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
    texture2d<float, access::sample> metallicTexture_0;
    sampler metallicSampler_0;
};

[[fragment]] pixelOutput_0 fragmentMain_uv_metallic(pixelInput_0 _S1 [[stage_in]], bool isFrontFace_0 [[front_facing]], float4 position_0 [[position]], SurfaceParameters_natural_0 device* surfaceParameters_1 [[buffer(7)]], FrameParameters_natural_0 device* frameParameters_1 [[buffer(8)]], texture2d<float, access::sample> metallicTexture_1 [[texture(4)]], sampler metallicSampler_1 [[sampler(5)]])
{
    uint4 _S2;
    bool hasSceneLighting_0;
    float4 _S3;
    thread KernelContext_0 kernelContext_0;
    (&kernelContext_0)->surfaceParameters_0 = surfaceParameters_1;
    (&kernelContext_0)->frameParameters_0 = frameParameters_1;
    (&kernelContext_0)->metallicTexture_0 = metallicTexture_1;
    (&kernelContext_0)->metallicSampler_0 = metallicSampler_1;
    SurfaceParameters_natural_0 surface_0 = surfaceParameters_1[int(0)];
    FrameParameters_natural_0 device* _S4 = frameParameters_1+int(0);
    for(;;)
    {
        uint4 _S5 = uint4(_S4->clipPlaneCount_0) ;
        _S2 = _S5;
        uint _S6 = min(_S5.x, 8U);
        uint index_0 = 0U;
        for(;;)
        {
            if(index_0 < _S6)
            {
            }
            else
            {
                break;
            }
            float4 _S7 = float4((&_S4->clipPlanes_0)->data_1[index_0]) ;
            if((dot(_S7.xyz, _S1.eyePosition_0) + _S7.w) < 0.0f)
            {
                discard_fragment();
            }
            index_0 = index_0 + 1U;
        }
        break;
    }
    float4 _S8 = float4(surface_0.clearcoatShaded_0) ;
    bool shaded_0 = (_S8.z) >= 0.5f;
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
    float4 _S9 = float4(surface_0.emissiveOcclusion_0) ;
    float3 emissiveColor_0 = _S9.xyz;
    float _S10 = _S9.w;
    float4 _S11 = float4(surface_0.reserved_0) ;
    if((_S11.x) >= 0.5f)
    {
        pixelOutput_0 _S12 = { float4(diffuseColor_0 * float3((1.0f - exp(- max(0.0f, _S11.y) * max(0.0f, _S11.z)))) , 1.0f) };
        return _S12;
    }
    float4 _S13 = float4(surface_0.metallicRoughnessThresholdWorkflow_0) ;
    float roughness_0 = clamp(_S13.y, 0.00999999977648258f, 1.0f);
    bool _S14 = (uint(round(_S11.w)) & 16U) != 0U;
    for(;;)
    {
        if(!_S14)
        {
            _S3 = (((&kernelContext_0)->metallicTexture_0).sample(((&kernelContext_0)->metallicSampler_0), (_S1.texCoord_0)));
            break;
        }
        texture2d<float, access::sample> _S15 = (&kernelContext_0)->metallicTexture_0;
        thread uint atlasWidth_0;
        thread uint atlasHeight_0;
        (*((&atlasWidth_0)) = (_S15).get_width(0)),(*((&atlasHeight_0)) = (_S15).get_height(0));
        int3 _S16 = int3(int(0), int(0), int(0));
        float4 metadata_0 = round((((&kernelContext_0)->metallicTexture_0).read(vec<uint,2>(((_S16)).xy), uint(((_S16)).z))) * float4(255.0f) );
        int2 _S17 = int2(metadata_0.zw);
        int2 tile_0 = int2(floor(_S1.texCoord_0)) - int2(metadata_0.xy);
        if(any(tile_0 < (int2(int(0)) )))
        {
            hasSceneLighting_0 = true;
        }
        else
        {
            hasSceneLighting_0 = any(tile_0 >= _S17);
        }
        if(hasSceneLighting_0)
        {
            int3 _S18 = int3(int(min(1U, atlasWidth_0 - 1U)), int(0), int(0));
            _S3 = (((&kernelContext_0)->metallicTexture_0).read(vec<uint,2>(((_S18)).xy), uint(((_S18)).z)));
            break;
        }
        uint _S19 = atlasWidth_0 / uint(_S17.x);
        float _S20 = float(_S19);
        uint _S21 = (atlasHeight_0 - 1U) / uint(_S17.y);
        float2 cellSize_0 = float2(_S20, float(_S21));
        _S3 = (((&kernelContext_0)->metallicTexture_0).sample(((&kernelContext_0)->metallicSampler_0), ((float2(tile_0) * cellSize_0 + float2(1.5f, 2.5f) + fract(_S1.texCoord_0) * max(cellSize_0 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_0), float(atlasHeight_0)))));
        break;
    }
    float metallic_0 = saturate(_S3.x);
    float opacityThreshold_0 = _S13.z;
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
    float3 _S22;
    if(lengthSquared_0 > 0.00100000004749745f)
    {
        _S22 = - _S1.eyePosition_0 * float3(rsqrt(lengthSquared_0)) ;
    }
    else
    {
        _S22 = float3(0.0f, 0.0f, 1.0f);
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
    float _S23 = saturate(abs(dot(normal_2, _S22)) + 0.00000999999974738f);
    float _S24 = max(0.00100000004749745f, roughness_0);
    float _S25 = _S8.x;
    float _S26 = max(0.00100000004749745f, _S8.y);
    float4 _S27 = float4(surface_0.specularIor_0) ;
    float _S28 = _S27.w;
    float reflectanceRatio_0 = (1.0f - _S28) / (1.0f + _S28);
    float3 _S29 = float3(3.14159274101257324f) ;
    float3 diffuse_0 = diffuseColor_0 / _S29;
    float3 normalIncidence_0;
    float3 grazingIncidence_0;
    float3 diffuse_1;
    if((_S13.w) >= 0.5f)
    {
        float3 _S30 = float3(1.0f, 1.0f, 1.0f);
        normalIncidence_0 = _S27.xyz;
        grazingIncidence_0 = _S30;
        diffuse_1 = diffuse_0;
    }
    else
    {
        float3 _S31 = float3(metallic_0) ;
        float3 specularTint_0 = mix(float3(1.0f, 1.0f, 1.0f), diffuseColor_0, _S31);
        float3 diffuse_2 = diffuse_0 * float3((1.0f - metallic_0)) ;
        normalIncidence_0 = mix(float3((reflectanceRatio_0 * reflectanceRatio_0))  * specularTint_0, specularTint_0, _S31);
        grazingIncidence_0 = specularTint_0;
        diffuse_1 = diffuse_2;
    }
    float4 _S32 = float4(_S4->ambientLight_0) ;
    float _S33 = _S32.w;
    if(_S33 > 0.5f)
    {
        hasSceneLighting_0 = true;
    }
    else
    {
        float3 _S34 = _S32.xyz;
        hasSceneLighting_0 = (dot(_S34, _S34)) > 0.0f;
    }
    uint _S35 = min(uint(_S33), 8U);
    matrix<float,int(4),int(4)>  _S36 = matrix<float,int(4),int(4)> (_S4->eyeToWorld_0.data_0[int(0)][int(0)], _S4->eyeToWorld_0.data_0[int(0)][int(1)], _S4->eyeToWorld_0.data_0[int(0)][int(2)], _S4->eyeToWorld_0.data_0[int(0)][int(3)], _S4->eyeToWorld_0.data_0[int(1)][int(0)], _S4->eyeToWorld_0.data_0[int(1)][int(1)], _S4->eyeToWorld_0.data_0[int(1)][int(2)], _S4->eyeToWorld_0.data_0[int(1)][int(3)], _S4->eyeToWorld_0.data_0[int(2)][int(0)], _S4->eyeToWorld_0.data_0[int(2)][int(1)], _S4->eyeToWorld_0.data_0[int(2)][int(2)], _S4->eyeToWorld_0.data_0[int(2)][int(3)], _S4->eyeToWorld_0.data_0[int(3)][int(0)], _S4->eyeToWorld_0.data_0[int(3)][int(1)], _S4->eyeToWorld_0.data_0[int(3)][int(2)], _S4->eyeToWorld_0.data_0[int(3)][int(3)]);
    float3 _S37 = normalize((((float4(_S22, 0.0f)) * (_S36))).xyz);
    float3 _S38 = (((float4(_S1.eyePosition_0, 1.0f)) * (_S36))).xyz;
    float4 _S39 = float4(surface_0.lightColorAmbient_0) ;
    float3 color_0 = diffuseColor_0 * float3(_S39.w)  + diffuseColor_0 * _S32.xyz;
    bool _S40 = !hasSceneLighting_0;
    uint lightCount_0;
    if(_S40)
    {
        lightCount_0 = 1U;
    }
    else
    {
        lightCount_0 = _S35;
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
        bool _S41 = lightIndex_0 == 0U;
        if(_S41)
        {
            hasSceneLighting_0 = _S40;
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
            lightType_0 = (float4((&_S4->lightPositionType_0)->data_1[lightIndex_0]) ).w;
        }
        bool _S42;
        if(_S41)
        {
            _S42 = _S40;
        }
        else
        {
            _S42 = false;
        }
        float3 lightDirection_0;
        if(_S42)
        {
            lightDirection_0 = normalize((float4(surface_0.lightDirectionIntensity_0) ).xyz);
        }
        else
        {
            lightDirection_0 = normalize((float4((&_S4->lightDirectionRadius_0)->data_1[lightIndex_0]) ).xyz);
        }
        bool _S43;
        if(_S41)
        {
            _S43 = _S40;
        }
        else
        {
            _S43 = false;
        }
        float _S44;
        if(_S43)
        {
            _S44 = (float4(surface_0.lightDirectionIntensity_0) ).w;
        }
        else
        {
            _S44 = (float4((&_S4->lightColorIntensity_0)->data_1[lightIndex_0]) ).w;
        }
        bool _S45;
        if(_S41)
        {
            _S45 = _S40;
        }
        else
        {
            _S45 = false;
        }
        if(_S45)
        {
            diffuseColor_0 = _S39.xyz;
        }
        else
        {
            diffuseColor_0 = (float4((&_S4->lightColorIntensity_0)->data_1[lightIndex_0]) ).xyz;
        }
        bool _S46;
        if(_S41)
        {
            _S46 = _S40;
        }
        else
        {
            _S46 = false;
        }
        float _S47;
        if(_S46)
        {
            _S47 = 1.0f;
        }
        else
        {
            _S47 = (float4((&_S4->lightControls_0)->data_1[lightIndex_0]) ).x;
        }
        bool _S48;
        if(_S41)
        {
            _S48 = _S40;
        }
        else
        {
            _S48 = false;
        }
        float _S49;
        if(_S48)
        {
            _S49 = 1.0f;
        }
        else
        {
            _S49 = (float4((&_S4->lightControls_0)->data_1[lightIndex_0]) ).y;
        }
        bool _S50;
        if(_S41)
        {
            _S50 = _S40;
        }
        else
        {
            _S50 = false;
        }
        float3 lightTangent_0;
        if(_S50)
        {
            lightTangent_0 = float3(1.0f, 0.0f, 0.0f);
        }
        else
        {
            lightTangent_0 = normalize((float4((&_S4->lightTangentShapeX_0)->data_1[lightIndex_0]) ).xyz);
        }
        bool _S51;
        if(_S41)
        {
            _S51 = _S40;
        }
        else
        {
            _S51 = false;
        }
        float3 lightBitangent_0;
        if(_S51)
        {
            lightBitangent_0 = float3(0.0f, 1.0f, 0.0f);
        }
        else
        {
            lightBitangent_0 = normalize((float4((&_S4->lightBitangentShapeY_0)->data_1[lightIndex_0]) ).xyz);
        }
        bool _S52;
        if(_S41)
        {
            _S52 = _S40;
        }
        else
        {
            _S52 = false;
        }
        float shapeX_0;
        if(_S52)
        {
            shapeX_0 = 0.0f;
        }
        else
        {
            shapeX_0 = (float4((&_S4->lightTangentShapeX_0)->data_1[lightIndex_0]) ).w;
        }
        bool _S53;
        if(_S41)
        {
            _S53 = _S40;
        }
        else
        {
            _S53 = false;
        }
        float shapeY_0;
        if(_S53)
        {
            shapeY_0 = 0.0f;
        }
        else
        {
            shapeY_0 = (float4((&_S4->lightBitangentShapeY_0)->data_1[lightIndex_0]) ).w;
        }
        bool _S54;
        if(_S41)
        {
            _S54 = _S40;
        }
        else
        {
            _S54 = false;
        }
        float lightRadius_0;
        if(_S54)
        {
            lightRadius_0 = 0.0f;
        }
        else
        {
            lightRadius_0 = (float4((&_S4->lightDirectionRadius_0)->data_1[lightIndex_0]) ).w;
        }
        bool _S55;
        if(_S41)
        {
            _S55 = _S40;
        }
        else
        {
            _S55 = false;
        }
        float3 _S56;
        if(_S55)
        {
            _S56 = _S22;
        }
        else
        {
            _S56 = _S37;
        }
        thread array<float3, int(5)> sampleOffsets_0;
        float3 _S57 = float3(0.0f, 0.0f, 0.0f);
        sampleOffsets_0[int(0)] = _S57;
        sampleOffsets_0[int(1)] = _S57;
        sampleOffsets_0[int(2)] = _S57;
        sampleOffsets_0[int(3)] = _S57;
        sampleOffsets_0[int(4)] = _S57;
        float sampleCount_0;
        if(lightType_0 == 3.0f)
        {
            float3 halfWidth_0 = lightTangent_0 * float3((shapeX_0 * 0.5f)) ;
            float3 halfHeight_0 = lightBitangent_0 * float3((shapeY_0 * 0.5f)) ;
            sampleOffsets_0[int(1)] = halfWidth_0 + halfHeight_0;
            sampleOffsets_0[int(2)] = halfWidth_0 - halfHeight_0;
            float3 _S58 = - halfWidth_0;
            sampleOffsets_0[int(3)] = _S58 + halfHeight_0;
            sampleOffsets_0[int(4)] = _S58 - halfHeight_0;
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
            float sampleIntensity_0 = _S44 / sampleCount_0;
            float3 sampleDirection_0;
            float emissionScale_0;
            float sampleIntensity_1;
            if(lightType_0 >= 2.0f)
            {
                float3 toLight_0 = (float4((&_S4->lightPositionType_0)->data_1[lightIndex_0]) ).xyz + sampleOffsets_0[sampleIndex_0] - _S38;
                float _S59 = max(dot(toLight_0, toLight_0), 0.00100000004749745f);
                float3 sampleDirection_1 = toLight_0 * float3(rsqrt(_S59)) ;
                float sampleIntensity_2 = sampleIntensity_0 / _S59;
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
            float3 half_0 = normalize(sampleDirection_0 + _S56);
            float normalDotLight_0 = saturate(dot(normal_2, sampleDirection_0));
            float normalDotHalf_0 = saturate(dot(normal_2, half_0));
            float3 _S60 = float3(pow(max(0.0f, 1.0f - saturate(dot(_S56, half_0))), 5.0f)) ;
            float3 _S61 = mix(normalIncidence_0, grazingIncidence_0, _S60);
            float3 directDiffuse_0 = diffuse_1 * (float3(1.0f)  - _S61);
            float alpha_0 = _S24 * _S24;
            float alphaSquared_0 = alpha_0 * alpha_0;
            float _S62 = normalDotHalf_0 * normalDotHalf_0;
            float denominator_0 = _S62 * (alphaSquared_0 - 1.0f) + 1.0f;
            float k_0 = alpha_0 * 0.5f;
            float _S63 = 1.0f - k_0;
            float3 _S64 = float3((4.0f * normalDotLight_0 * _S23 + 0.00100000004749745f)) ;
            float3 _S65 = _S61 * float3((_S23 / (_S23 * _S63 + k_0) * (normalDotLight_0 / (normalDotLight_0 * _S63 + k_0))))  * float3(((alphaSquared_0 + 0.00100000004749745f) / (denominator_0 * denominator_0 * 3.14159274101257324f)))  / _S64;
            float3 directSpecular_0;
            if(_S25 > 0.0f)
            {
                float alpha_1 = _S26 * _S26;
                float alphaSquared_1 = alpha_1 * alpha_1;
                float denominator_1 = _S62 * (alphaSquared_1 - 1.0f) + 1.0f;
                float k_1 = alpha_1 * 0.5f;
                float _S66 = 1.0f - k_1;
                directSpecular_0 = _S65 + float3(_S25)  * (mix(float3((reflectanceRatio_0 * reflectanceRatio_0)) , float3(1.0f, 1.0f, 1.0f), _S60) * float3((_S23 / (_S23 * _S66 + k_1) * (normalDotLight_0 / (normalDotLight_0 * _S66 + k_1))))  * float3(((alphaSquared_1 + 0.00100000004749745f) / (denominator_1 * denominator_1 * 3.14159274101257324f)))  / _S64);
            }
            else
            {
                directSpecular_0 = _S65;
            }
            float3 _S67 = diffuseColor_0 * float3(sampleIntensity_1) ;
            color_2 = color_2 + float3((_S10 * emissionScale_0 * normalDotLight_0))  * (directDiffuse_0 * float3(_S47)  * (_S67 * _S29) + directSpecular_0 * float3(_S49)  * _S67);
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
    pixelOutput_0 _S68 = { float4(color_1, opacity_0) };
    return _S68;
}

