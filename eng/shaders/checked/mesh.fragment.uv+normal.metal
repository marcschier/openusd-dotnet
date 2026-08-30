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
    packed_float4 textureControls_0;
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
    texture2d<float, access::sample> normalTexture_0;
    sampler normalSampler_0;
};

[[fragment]] pixelOutput_0 fragmentMain_uv_normal(pixelInput_0 _S1 [[stage_in]], bool isFrontFace_0 [[front_facing]], float4 position_0 [[position]], SurfaceParameters_natural_0 device* surfaceParameters_1 [[buffer(7)]], FrameParameters_natural_0 device* frameParameters_1 [[buffer(8)]], texture2d<float, access::sample> normalTexture_1 [[texture(1)]], sampler normalSampler_1 [[sampler(1)]])
{
    uint4 _S2;
    bool hasSceneLighting_0;
    float4 _S3;
    thread KernelContext_0 kernelContext_0;
    (&kernelContext_0)->surfaceParameters_0 = surfaceParameters_1;
    (&kernelContext_0)->frameParameters_0 = frameParameters_1;
    (&kernelContext_0)->normalTexture_0 = normalTexture_1;
    (&kernelContext_0)->normalSampler_0 = normalSampler_1;
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
    if((_S12.x) >= 0.5f)
    {
        pixelOutput_0 _S13 = { float4(diffuseColor_0 * float3((1.0f - exp(- max(0.0f, _S12.y) * max(0.0f, _S12.z)))) , 1.0f) };
        return _S13;
    }
    float4 _S14 = float4(_S4->metallicRoughnessThresholdWorkflow_0) ;
    float metallic_0 = saturate(_S14.x);
    float roughness_0 = clamp(_S14.y, 0.00999999977648258f, 1.0f);
    float4 _S15 = float4(_S4->specularIor_0) ;
    float3 specularColor_0 = _S15.xyz;
    bool _S16 = (uint(round((float4(_S4->textureControls_0) ).y)) & 4U) != 0U;
    for(;;)
    {
        if(!_S16)
        {
            _S3 = (((&kernelContext_0)->normalTexture_0).sample(((&kernelContext_0)->normalSampler_0), (_S1.texCoord_0)));
            break;
        }
        texture2d<float, access::sample> _S17 = (&kernelContext_0)->normalTexture_0;
        thread uint atlasWidth_0;
        thread uint atlasHeight_0;
        (*((&atlasWidth_0)) = (_S17).get_width(0)),(*((&atlasHeight_0)) = (_S17).get_height(0));
        int3 _S18 = int3(int(0), int(0), int(0));
        float4 metadata_0 = round((((&kernelContext_0)->normalTexture_0).read(vec<uint,2>(((_S18)).xy), uint(((_S18)).z))) * float4(255.0f) );
        int2 _S19 = int2(metadata_0.zw);
        int2 tile_0 = int2(floor(_S1.texCoord_0)) - int2(metadata_0.xy);
        if(any(tile_0 < (int2(int(0)) )))
        {
            hasSceneLighting_0 = true;
        }
        else
        {
            hasSceneLighting_0 = any(tile_0 >= _S19);
        }
        if(hasSceneLighting_0)
        {
            int3 _S20 = int3(int(min(1U, atlasWidth_0 - 1U)), int(0), int(0));
            _S3 = (((&kernelContext_0)->normalTexture_0).read(vec<uint,2>(((_S20)).xy), uint(((_S20)).z)));
            break;
        }
        uint _S21 = atlasWidth_0 / uint(_S19.x);
        float _S22 = float(_S21);
        uint _S23 = (atlasHeight_0 - 1U) / uint(_S19.y);
        float2 cellSize_0 = float2(_S22, float(_S23));
        _S3 = (((&kernelContext_0)->normalTexture_0).sample(((&kernelContext_0)->normalSampler_0), ((float2(tile_0) * cellSize_0 + float2(1.5f, 2.5f) + fract(_S1.texCoord_0) * max(cellSize_0 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_0), float(atlasHeight_0)))));
        break;
    }
    float3 _S24 = float3(1.0f) ;
    float3 sampledNormal_0 = _S3.xyz * float3(2.0f)  - _S24;
    float3 tangent_1 = normalize(_S1.tangent_0.xyz);
    float3 shadingNormal_0 = normalize(tangent_1 * float3(sampledNormal_0.x)  + cross(normalize(_S1.normal_0), tangent_1) * float3(_S1.tangent_0.w)  * float3(sampledNormal_0.y)  + _S1.normal_0 * float3(sampledNormal_0.z) );
    float opacityThreshold_0 = _S14.z;
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
    float3 normal_1 = normalize(shadingNormal_0);
    float lengthSquared_0 = dot(_S1.eyePosition_0, _S1.eyePosition_0);
    float3 _S25;
    if(lengthSquared_0 > 0.00100000004749745f)
    {
        _S25 = - _S1.eyePosition_0 * float3(rsqrt(lengthSquared_0)) ;
    }
    else
    {
        _S25 = float3(0.0f, 0.0f, 1.0f);
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
    float _S26 = saturate(abs(dot(normal_2, _S25)) + 0.00000999999974738f);
    float _S27 = max(0.00100000004749745f, roughness_0);
    float _S28 = _S9.x;
    float _S29 = max(0.00100000004749745f, _S9.y);
    float _S30 = _S15.w;
    float reflectanceRatio_0 = (1.0f - _S30) / (1.0f + _S30);
    float3 _S31 = float3(3.14159274101257324f) ;
    float3 diffuse_0 = diffuseColor_0 / _S31;
    float3 normalIncidence_0;
    float3 grazingIncidence_0;
    float3 diffuse_1;
    if((_S14.w) >= 0.5f)
    {
        float3 _S32 = float3(1.0f, 1.0f, 1.0f);
        normalIncidence_0 = specularColor_0;
        grazingIncidence_0 = _S32;
        diffuse_1 = diffuse_0;
    }
    else
    {
        float3 _S33 = float3(metallic_0) ;
        float3 specularTint_0 = mix(float3(1.0f, 1.0f, 1.0f), diffuseColor_0, _S33);
        float3 diffuse_2 = diffuse_0 * float3((1.0f - metallic_0)) ;
        normalIncidence_0 = mix(float3((reflectanceRatio_0 * reflectanceRatio_0))  * specularTint_0, specularTint_0, _S33);
        grazingIncidence_0 = specularTint_0;
        diffuse_1 = diffuse_2;
    }
    float4 _S34 = float4(_S5->ambientLight_0) ;
    float _S35 = _S34.w;
    if(_S35 > 0.5f)
    {
        hasSceneLighting_0 = true;
    }
    else
    {
        float3 _S36 = _S34.xyz;
        hasSceneLighting_0 = (dot(_S36, _S36)) > 0.0f;
    }
    uint _S37 = min(uint(_S35), 8U);
    matrix<float,int(4),int(4)>  _S38 = matrix<float,int(4),int(4)> (_S5->eyeToWorld_0.data_0[int(0)][int(0)], _S5->eyeToWorld_0.data_0[int(0)][int(1)], _S5->eyeToWorld_0.data_0[int(0)][int(2)], _S5->eyeToWorld_0.data_0[int(0)][int(3)], _S5->eyeToWorld_0.data_0[int(1)][int(0)], _S5->eyeToWorld_0.data_0[int(1)][int(1)], _S5->eyeToWorld_0.data_0[int(1)][int(2)], _S5->eyeToWorld_0.data_0[int(1)][int(3)], _S5->eyeToWorld_0.data_0[int(2)][int(0)], _S5->eyeToWorld_0.data_0[int(2)][int(1)], _S5->eyeToWorld_0.data_0[int(2)][int(2)], _S5->eyeToWorld_0.data_0[int(2)][int(3)], _S5->eyeToWorld_0.data_0[int(3)][int(0)], _S5->eyeToWorld_0.data_0[int(3)][int(1)], _S5->eyeToWorld_0.data_0[int(3)][int(2)], _S5->eyeToWorld_0.data_0[int(3)][int(3)]);
    float3 _S39 = normalize((((float4(_S25, 0.0f)) * (_S38))).xyz);
    float3 _S40 = (((float4(_S1.eyePosition_0, 1.0f)) * (_S38))).xyz;
    float4 _S41 = float4(_S4->lightColorAmbient_0) ;
    float3 color_0 = diffuseColor_0 * float3(_S41.w)  + diffuseColor_0 * _S34.xyz;
    bool _S42 = !hasSceneLighting_0;
    uint lightCount_0;
    if(_S42)
    {
        lightCount_0 = 1U;
    }
    else
    {
        lightCount_0 = _S37;
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
        bool _S43 = lightIndex_0 == 0U;
        if(_S43)
        {
            hasSceneLighting_0 = _S42;
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
            lightType_0 = (float4((&_S5->lightPositionType_0)->data_1[lightIndex_0]) ).w;
        }
        bool _S44;
        if(_S43)
        {
            _S44 = _S42;
        }
        else
        {
            _S44 = false;
        }
        float3 lightDirection_0;
        if(_S44)
        {
            lightDirection_0 = normalize((float4(_S4->lightDirectionIntensity_0) ).xyz);
        }
        else
        {
            lightDirection_0 = normalize((float4((&_S5->lightDirectionRadius_0)->data_1[lightIndex_0]) ).xyz);
        }
        bool _S45;
        if(_S43)
        {
            _S45 = _S42;
        }
        else
        {
            _S45 = false;
        }
        float _S46;
        if(_S45)
        {
            _S46 = (float4(_S4->lightDirectionIntensity_0) ).w;
        }
        else
        {
            _S46 = (float4((&_S5->lightColorIntensity_0)->data_1[lightIndex_0]) ).w;
        }
        bool _S47;
        if(_S43)
        {
            _S47 = _S42;
        }
        else
        {
            _S47 = false;
        }
        if(_S47)
        {
            diffuseColor_0 = _S41.xyz;
        }
        else
        {
            diffuseColor_0 = (float4((&_S5->lightColorIntensity_0)->data_1[lightIndex_0]) ).xyz;
        }
        bool _S48;
        if(_S43)
        {
            _S48 = _S42;
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
            _S49 = (float4((&_S5->lightControls_0)->data_1[lightIndex_0]) ).x;
        }
        bool _S50;
        if(_S43)
        {
            _S50 = _S42;
        }
        else
        {
            _S50 = false;
        }
        float _S51;
        if(_S50)
        {
            _S51 = 1.0f;
        }
        else
        {
            _S51 = (float4((&_S5->lightControls_0)->data_1[lightIndex_0]) ).y;
        }
        bool _S52;
        if(_S43)
        {
            _S52 = _S42;
        }
        else
        {
            _S52 = false;
        }
        float3 lightTangent_0;
        if(_S52)
        {
            lightTangent_0 = float3(1.0f, 0.0f, 0.0f);
        }
        else
        {
            lightTangent_0 = normalize((float4((&_S5->lightTangentShapeX_0)->data_1[lightIndex_0]) ).xyz);
        }
        bool _S53;
        if(_S43)
        {
            _S53 = _S42;
        }
        else
        {
            _S53 = false;
        }
        float3 lightBitangent_0;
        if(_S53)
        {
            lightBitangent_0 = float3(0.0f, 1.0f, 0.0f);
        }
        else
        {
            lightBitangent_0 = normalize((float4((&_S5->lightBitangentShapeY_0)->data_1[lightIndex_0]) ).xyz);
        }
        bool _S54;
        if(_S43)
        {
            _S54 = _S42;
        }
        else
        {
            _S54 = false;
        }
        float shapeX_0;
        if(_S54)
        {
            shapeX_0 = 0.0f;
        }
        else
        {
            shapeX_0 = (float4((&_S5->lightTangentShapeX_0)->data_1[lightIndex_0]) ).w;
        }
        bool _S55;
        if(_S43)
        {
            _S55 = _S42;
        }
        else
        {
            _S55 = false;
        }
        float shapeY_0;
        if(_S55)
        {
            shapeY_0 = 0.0f;
        }
        else
        {
            shapeY_0 = (float4((&_S5->lightBitangentShapeY_0)->data_1[lightIndex_0]) ).w;
        }
        bool _S56;
        if(_S43)
        {
            _S56 = _S42;
        }
        else
        {
            _S56 = false;
        }
        float lightRadius_0;
        if(_S56)
        {
            lightRadius_0 = 0.0f;
        }
        else
        {
            lightRadius_0 = (float4((&_S5->lightDirectionRadius_0)->data_1[lightIndex_0]) ).w;
        }
        bool _S57;
        if(_S43)
        {
            _S57 = _S42;
        }
        else
        {
            _S57 = false;
        }
        float3 _S58;
        if(_S57)
        {
            _S58 = _S25;
        }
        else
        {
            _S58 = _S39;
        }
        thread array<float3, int(5)> sampleOffsets_0;
        float3 _S59 = float3(0.0f, 0.0f, 0.0f);
        sampleOffsets_0[int(0)] = _S59;
        sampleOffsets_0[int(1)] = _S59;
        sampleOffsets_0[int(2)] = _S59;
        sampleOffsets_0[int(3)] = _S59;
        sampleOffsets_0[int(4)] = _S59;
        float sampleCount_0;
        if(lightType_0 == 3.0f)
        {
            float3 halfWidth_0 = lightTangent_0 * float3((shapeX_0 * 0.5f)) ;
            float3 halfHeight_0 = lightBitangent_0 * float3((shapeY_0 * 0.5f)) ;
            sampleOffsets_0[int(1)] = halfWidth_0 + halfHeight_0;
            sampleOffsets_0[int(2)] = halfWidth_0 - halfHeight_0;
            float3 _S60 = - halfWidth_0;
            sampleOffsets_0[int(3)] = _S60 + halfHeight_0;
            sampleOffsets_0[int(4)] = _S60 - halfHeight_0;
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
            float sampleIntensity_0 = _S46 / sampleCount_0;
            float3 sampleDirection_0;
            float emissionScale_0;
            float sampleIntensity_1;
            if(lightType_0 >= 2.0f)
            {
                float3 toLight_0 = (float4((&_S5->lightPositionType_0)->data_1[lightIndex_0]) ).xyz + sampleOffsets_0[sampleIndex_0] - _S40;
                float _S61 = max(dot(toLight_0, toLight_0), 0.00100000004749745f);
                float3 sampleDirection_1 = toLight_0 * float3(rsqrt(_S61)) ;
                float sampleIntensity_2 = sampleIntensity_0 / _S61;
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
            float3 half_0 = normalize(sampleDirection_0 + _S58);
            float normalDotLight_0 = saturate(dot(normal_2, sampleDirection_0));
            float normalDotHalf_0 = saturate(dot(normal_2, half_0));
            float3 _S62 = float3(pow(max(0.0f, 1.0f - saturate(dot(_S58, half_0))), 5.0f)) ;
            float3 _S63 = mix(normalIncidence_0, grazingIncidence_0, _S62);
            float3 directDiffuse_0 = diffuse_1 * (_S24 - _S63);
            float alpha_0 = _S27 * _S27;
            float alphaSquared_0 = alpha_0 * alpha_0;
            float _S64 = normalDotHalf_0 * normalDotHalf_0;
            float denominator_0 = _S64 * (alphaSquared_0 - 1.0f) + 1.0f;
            float k_0 = alpha_0 * 0.5f;
            float _S65 = 1.0f - k_0;
            float3 _S66 = float3((4.0f * normalDotLight_0 * _S26 + 0.00100000004749745f)) ;
            float3 _S67 = _S63 * float3((_S26 / (_S26 * _S65 + k_0) * (normalDotLight_0 / (normalDotLight_0 * _S65 + k_0))))  * float3(((alphaSquared_0 + 0.00100000004749745f) / (denominator_0 * denominator_0 * 3.14159274101257324f)))  / _S66;
            float3 directSpecular_0;
            if(_S28 > 0.0f)
            {
                float alpha_1 = _S29 * _S29;
                float alphaSquared_1 = alpha_1 * alpha_1;
                float denominator_1 = _S64 * (alphaSquared_1 - 1.0f) + 1.0f;
                float k_1 = alpha_1 * 0.5f;
                float _S68 = 1.0f - k_1;
                directSpecular_0 = _S67 + float3(_S28)  * (mix(float3((reflectanceRatio_0 * reflectanceRatio_0)) , float3(1.0f, 1.0f, 1.0f), _S62) * float3((_S26 / (_S26 * _S68 + k_1) * (normalDotLight_0 / (normalDotLight_0 * _S68 + k_1))))  * float3(((alphaSquared_1 + 0.00100000004749745f) / (denominator_1 * denominator_1 * 3.14159274101257324f)))  / _S66);
            }
            else
            {
                directSpecular_0 = _S67;
            }
            float3 _S69 = diffuseColor_0 * float3(sampleIntensity_1) ;
            color_2 = color_2 + float3((_S11 * emissionScale_0 * normalDotLight_0))  * (directDiffuse_0 * float3(_S49)  * (_S69 * _S31) + directSpecular_0 * float3(_S51)  * _S69);
            sampleIndex_0 = sampleIndex_0 + 1U;
        }
        lightIndex_0 = lightIndex_0 + 1U;
        color_1 = color_2;
    }
    float3 color_3 = (color_1 + emissiveColor_0) * float3(exp2((as_type<float>((_S2.z))))) ;
    if((_S2.y) == 1U)
    {
        color_1 = color_3 / (_S24 + max(color_3, float3(0.0f, 0.0f, 0.0f)));
    }
    else
    {
        color_1 = color_3;
    }
    pixelOutput_0 _S70 = { float4(color_1, opacity_0) };
    return _S70;
}

