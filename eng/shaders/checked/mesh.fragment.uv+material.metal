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
    float3 worldNormal_0 [[user(TEXCOORD_3)]];
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
    packed_float4 textureControls_0;
    packed_float4 uvTransformRow0_0;
    packed_float4 uvTransformRow1_0;
    packed_float4 compositeControls_0;
    packed_float4 domeLinkControls_0;
};

struct _MatrixStorage_float4x4natural_0
{
    array<packed_float4, int(4)> data_0;
};

struct _Array_natural_vectorx3Cfloatx2C4x3E8_0
{
    array<packed_float4, int(8)> data_1;
};

struct _Array_natural_matrixx3Cfloatx2C4x2C4x3E4_0
{
    array<_MatrixStorage_float4x4natural_0, int(4)> data_2;
};

struct _Array_natural_vectorx3Cfloatx2C4x3E4_0
{
    array<packed_float4, int(4)> data_3;
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
    _Array_natural_matrixx3Cfloatx2C4x2C4x3E4_0 shadowWorldToLightClip_0;
    _Array_natural_vectorx3Cfloatx2C4x3E4_0 shadowTile_0;
    _Array_natural_vectorx3Cfloatx2C4x3E4_0 shadowControls_0;
    _Array_natural_vectorx3Cfloatx2C4x3E8_0 shadowSlots_0;
    packed_float4 environmentControls_0;
    packed_float4 domeControls_0;
    _Array_natural_vectorx3Cfloatx2C4x3E8_0 domeAmbient_0;
    _Array_natural_vectorx3Cfloatx2C4x3E8_0 domeEnvironment_0;
};

struct KernelContext_0
{
    SurfaceParameters_natural_0 device* surfaceParameters_0;
    FrameParameters_natural_0 device* frameParameters_0;
    texture2d<float, access::sample> baseColorTexture_0;
    sampler baseColorSampler_0;
    texture2d<float, access::sample> compositeTexture_0;
    sampler compositeSampler_0;
    texture2d<float, access::sample> roughnessMetallicTexture_0;
    sampler roughnessMetallicSampler_0;
    texture2d<float, access::sample> metallicTexture_0;
    sampler metallicSampler_0;
    texture2d<float, access::sample> emissiveTexture_0;
    sampler emissiveSampler_0;
    texture2d<float, access::sample> opacityTexture_0;
    sampler opacitySampler_0;
    texture2d<float, access::sample> occlusionTexture_0;
    sampler occlusionSampler_0;
    texture2d<float, access::sample> specularColorTexture_0;
    sampler specularColorSampler_0;
    texture2d<float, access::sample> clearcoatTexture_0;
    sampler clearcoatSampler_0;
    texture2d<float, access::sample> clearcoatRoughnessTexture_0;
    sampler clearcoatRoughnessSampler_0;
    texture2d<float, access::sample> iorTexture_0;
    sampler iorSampler_0;
    texture2d<float, access::sample> shadowAtlas_0;
    sampler shadowSampler_0;
    texture2d<float, access::sample> environmentBrdf_0;
    sampler environmentBrdfSampler_0;
    texture2d<float, access::sample> environmentIrradiance_0;
    sampler environmentSampler_0;
    texture2d<float, access::sample> environmentSpecular_0;
};

[[fragment]] pixelOutput_0 fragmentMain_uv_material(pixelInput_0 _S1 [[stage_in]], bool isFrontFace_0 [[front_facing]], float4 position_0 [[position]], SurfaceParameters_natural_0 device* surfaceParameters_1 [[buffer(7)]], FrameParameters_natural_0 device* frameParameters_1 [[buffer(8)]], texture2d<float, access::sample> baseColorTexture_1 [[texture(0)]], sampler baseColorSampler_1 [[sampler(0)]], texture2d<float, access::sample> compositeTexture_1 [[texture(15)]], sampler compositeSampler_1 [[sampler(12)]], texture2d<float, access::sample> roughnessMetallicTexture_1 [[texture(2)]], sampler roughnessMetallicSampler_1 [[sampler(2)]], texture2d<float, access::sample> metallicTexture_1 [[texture(4)]], sampler metallicSampler_1 [[sampler(5)]], texture2d<float, access::sample> emissiveTexture_1 [[texture(3)]], sampler emissiveSampler_1 [[sampler(3)]], texture2d<float, access::sample> opacityTexture_1 [[texture(5)]], sampler opacitySampler_1 [[sampler(6)]], texture2d<float, access::sample> occlusionTexture_1 [[texture(10)]], sampler occlusionSampler_1 [[sampler(7)]], texture2d<float, access::sample> specularColorTexture_1 [[texture(11)]], sampler specularColorSampler_1 [[sampler(8)]], texture2d<float, access::sample> clearcoatTexture_1 [[texture(12)]], sampler clearcoatSampler_1 [[sampler(9)]], texture2d<float, access::sample> clearcoatRoughnessTexture_1 [[texture(13)]], sampler clearcoatRoughnessSampler_1 [[sampler(10)]], texture2d<float, access::sample> iorTexture_1 [[texture(14)]], sampler iorSampler_1 [[sampler(11)]], texture2d<float, access::sample> shadowAtlas_1 [[texture(16)]], sampler shadowSampler_1 [[sampler(13)]], texture2d<float, access::sample> environmentBrdf_1 [[texture(19)]], sampler environmentBrdfSampler_1 [[sampler(15)]], texture2d<float, access::sample> environmentIrradiance_1 [[texture(17)]], sampler environmentSampler_1 [[sampler(14)]], texture2d<float, access::sample> environmentSpecular_1 [[texture(18)]])
{
    uint4 _S2;
    uint sampleIndex_0;
    float3 lightDirection_0;
    float3 lightTangent_0;
    float3 lightBitangent_0;
    bool _S3;
    bool _S4;
    thread KernelContext_0 kernelContext_0;
    (&kernelContext_0)->surfaceParameters_0 = surfaceParameters_1;
    (&kernelContext_0)->frameParameters_0 = frameParameters_1;
    (&kernelContext_0)->baseColorTexture_0 = baseColorTexture_1;
    (&kernelContext_0)->baseColorSampler_0 = baseColorSampler_1;
    (&kernelContext_0)->compositeTexture_0 = compositeTexture_1;
    (&kernelContext_0)->compositeSampler_0 = compositeSampler_1;
    (&kernelContext_0)->roughnessMetallicTexture_0 = roughnessMetallicTexture_1;
    (&kernelContext_0)->roughnessMetallicSampler_0 = roughnessMetallicSampler_1;
    (&kernelContext_0)->metallicTexture_0 = metallicTexture_1;
    (&kernelContext_0)->metallicSampler_0 = metallicSampler_1;
    (&kernelContext_0)->emissiveTexture_0 = emissiveTexture_1;
    (&kernelContext_0)->emissiveSampler_0 = emissiveSampler_1;
    (&kernelContext_0)->opacityTexture_0 = opacityTexture_1;
    (&kernelContext_0)->opacitySampler_0 = opacitySampler_1;
    (&kernelContext_0)->occlusionTexture_0 = occlusionTexture_1;
    (&kernelContext_0)->occlusionSampler_0 = occlusionSampler_1;
    (&kernelContext_0)->specularColorTexture_0 = specularColorTexture_1;
    (&kernelContext_0)->specularColorSampler_0 = specularColorSampler_1;
    (&kernelContext_0)->clearcoatTexture_0 = clearcoatTexture_1;
    (&kernelContext_0)->clearcoatSampler_0 = clearcoatSampler_1;
    (&kernelContext_0)->clearcoatRoughnessTexture_0 = clearcoatRoughnessTexture_1;
    (&kernelContext_0)->clearcoatRoughnessSampler_0 = clearcoatRoughnessSampler_1;
    (&kernelContext_0)->iorTexture_0 = iorTexture_1;
    (&kernelContext_0)->iorSampler_0 = iorSampler_1;
    (&kernelContext_0)->shadowAtlas_0 = shadowAtlas_1;
    (&kernelContext_0)->shadowSampler_0 = shadowSampler_1;
    (&kernelContext_0)->environmentBrdf_0 = environmentBrdf_1;
    (&kernelContext_0)->environmentBrdfSampler_0 = environmentBrdfSampler_1;
    (&kernelContext_0)->environmentIrradiance_0 = environmentIrradiance_1;
    (&kernelContext_0)->environmentSampler_0 = environmentSampler_1;
    (&kernelContext_0)->environmentSpecular_0 = environmentSpecular_1;
    SurfaceParameters_natural_0 device* _S5 = surfaceParameters_1+int(0);
    FrameParameters_natural_0 device* _S6 = frameParameters_1+int(0);
    for(;;)
    {
        uint4 _S7 = uint4(_S6->clipPlaneCount_0) ;
        _S2 = _S7;
        uint _S8 = min(_S7.x, 8U);
        uint index_0 = 0U;
        for(;;)
        {
            if(index_0 < _S8)
            {
            }
            else
            {
                break;
            }
            float4 _S9 = float4((&_S6->clipPlanes_0)->data_1[index_0]) ;
            if((dot(_S9.xyz, _S1.eyePosition_0) + _S9.w) < 0.0f)
            {
                discard_fragment();
            }
            index_0 = index_0 + 1U;
        }
        break;
    }
    float4 _S10 = float4(_S5->clearcoatShaded_0) ;
    float shadedMode_0 = _S10.z;
    bool shaded_0 = shadedMode_0 >= 0.5f;
    bool unlit_0 = shadedMode_0 >= 1.5f;
    float3 diffuseColor_0;
    if(shaded_0)
    {
        diffuseColor_0 = (float4(_S5->diffuseOpacity_0) ).xyz;
    }
    else
    {
        diffuseColor_0 = _S1.tint_0.xyz;
    }
    float opacity_0;
    if(shaded_0)
    {
        opacity_0 = (float4(_S5->diffuseOpacity_0) ).w;
    }
    else
    {
        opacity_0 = _S1.tint_0.w;
    }
    float4 _S11 = float4(_S5->emissiveOcclusion_0) ;
    float3 emissiveColor_0 = _S11.xyz;
    float _S12 = _S11.w;
    float3 unlitColor_0;
    if(unlit_0)
    {
        float3 unlitColor_1 = (diffuseColor_0 + emissiveColor_0) * float3(exp2((as_type<float>((_S2.z))))) ;
        if((_S2.y) == 1U)
        {
            unlitColor_0 = unlitColor_1 / (float3(1.0f)  + max(unlitColor_1, float3(0.0f, 0.0f, 0.0f)));
        }
        else
        {
            unlitColor_0 = unlitColor_1;
        }
        pixelOutput_0 _S13 = { float4(unlitColor_0, opacity_0) };
        return _S13;
    }
    float4 _S14 = float4(_S5->reserved_0) ;
    if((_S14.x) >= 0.5f)
    {
        pixelOutput_0 _S15 = { float4(diffuseColor_0 * float3((1.0f - exp(- max(0.0f, _S14.y) * max(0.0f, _S14.z)))) , 1.0f) };
        return _S15;
    }
    float4 _S16 = float4(_S5->metallicRoughnessThresholdWorkflow_0) ;
    float _S17 = saturate(_S16.x);
    float _S18 = clamp(_S16.y, 0.00999999977648258f, 1.0f);
    float4 _S19 = float4(_S5->specularIor_0) ;
    float3 _S20 = _S19.xyz;
    float _S21 = _S19.w;
    float _S22 = _S10.x;
    float _S23 = _S10.y;
    float4 _S24 = float4(_S5->textureControls_0) ;
    uint textureMask_0 = uint(round(_S24.x));
    uint udimMask_0 = uint(round(_S24.y));
    float4 _S25 = float4(_S5->uvTransformRow0_0) ;
    float4 _S26 = float4(_S5->uvTransformRow1_0) ;
    float2 _S27 = float2(dot(_S25.xy, _S1.texCoord_0) + _S25.z, dot(_S26.xy, _S1.texCoord_0) + _S26.z);
    bool hasSceneLighting_0;
    float4 _S28;
    float4 _S29;
    if((textureMask_0 & 2U) != 0U)
    {
        bool _S30 = (udimMask_0 & 2U) != 0U;
        for(;;)
        {
            if(!_S30)
            {
                _S28 = (((&kernelContext_0)->baseColorTexture_0).sample(((&kernelContext_0)->baseColorSampler_0), (_S27)));
                break;
            }
            texture2d<float, access::sample> _S31 = (&kernelContext_0)->baseColorTexture_0;
            thread uint atlasWidth_0;
            thread uint atlasHeight_0;
            (*((&atlasWidth_0)) = (_S31).get_width(0)),(*((&atlasHeight_0)) = (_S31).get_height(0));
            int3 _S32 = int3(int(0), int(0), int(0));
            float4 metadata_0 = round((((&kernelContext_0)->baseColorTexture_0).read(vec<uint,2>(((_S32)).xy), uint(((_S32)).z))) * float4(255.0f) );
            int2 _S33 = int2(metadata_0.zw);
            int2 tile_0 = int2(floor(_S27)) - int2(metadata_0.xy);
            if(any(tile_0 < (int2(int(0)) )))
            {
                hasSceneLighting_0 = true;
            }
            else
            {
                hasSceneLighting_0 = any(tile_0 >= _S33);
            }
            if(hasSceneLighting_0)
            {
                int3 _S34 = int3(int(min(1U, atlasWidth_0 - 1U)), int(0), int(0));
                _S28 = (((&kernelContext_0)->baseColorTexture_0).read(vec<uint,2>(((_S34)).xy), uint(((_S34)).z)));
                break;
            }
            uint _S35 = atlasWidth_0 / uint(_S33.x);
            float _S36 = float(_S35);
            uint _S37 = (atlasHeight_0 - 1U) / uint(_S33.y);
            float2 cellSize_0 = float2(_S36, float(_S37));
            _S28 = (((&kernelContext_0)->baseColorTexture_0).sample(((&kernelContext_0)->baseColorSampler_0), ((float2(tile_0) * cellSize_0 + float2(1.5f, 2.5f) + fract(_S27) * max(cellSize_0 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_0), float(atlasHeight_0)))));
            break;
        }
        for(;;)
        {
            float4 _S38 = float4(_S5->compositeControls_0) ;
            if((_S38.x) != 2.0f)
            {
                break;
            }
            bool _S39 = (_S38.w) >= 0.5f;
            for(;;)
            {
                if(!_S39)
                {
                    _S29 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), (_S27)));
                    break;
                }
                texture2d<float, access::sample> _S40 = (&kernelContext_0)->compositeTexture_0;
                thread uint atlasWidth_1;
                thread uint atlasHeight_1;
                (*((&atlasWidth_1)) = (_S40).get_width(0)),(*((&atlasHeight_1)) = (_S40).get_height(0));
                int3 _S41 = int3(int(0), int(0), int(0));
                float4 metadata_1 = round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S41)).xy), uint(((_S41)).z))) * float4(255.0f) );
                int2 _S42 = int2(metadata_1.zw);
                int2 tile_1 = int2(floor(_S27)) - int2(metadata_1.xy);
                if(any(tile_1 < (int2(int(0)) )))
                {
                    hasSceneLighting_0 = true;
                }
                else
                {
                    hasSceneLighting_0 = any(tile_1 >= _S42);
                }
                if(hasSceneLighting_0)
                {
                    int3 _S43 = int3(int(min(1U, atlasWidth_1 - 1U)), int(0), int(0));
                    _S29 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S43)).xy), uint(((_S43)).z)));
                    break;
                }
                uint _S44 = atlasWidth_1 / uint(_S42.x);
                float _S45 = float(_S44);
                uint _S46 = (atlasHeight_1 - 1U) / uint(_S42.y);
                float2 cellSize_1 = float2(_S45, float(_S46));
                _S29 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), ((float2(tile_1) * cellSize_1 + float2(1.5f, 2.5f) + fract(_S27) * max(cellSize_1 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_1), float(atlasHeight_1)))));
                break;
            }
            uint operation_0 = uint(round(_S38.y));
            if(operation_0 == 1U)
            {
                _S28 = _S28 * _S29;
                break;
            }
            if(operation_0 == 2U)
            {
                _S28 = _S28 + _S29;
                break;
            }
            if(operation_0 == 3U)
            {
                _S28 = _S28 - _S29;
                break;
            }
            if(operation_0 == 4U)
            {
                float factor_0 = _S38.z;
                _S28 = _S28 * float4((1.0f - factor_0))  + _S29 * float4(factor_0) ;
                break;
            }
            break;
        }
        diffuseColor_0 = _S28.xyz;
    }
    float roughness_0;
    if((textureMask_0 & 8U) != 0U)
    {
        bool _S47 = (udimMask_0 & 8U) != 0U;
        for(;;)
        {
            if(!_S47)
            {
                _S28 = (((&kernelContext_0)->roughnessMetallicTexture_0).sample(((&kernelContext_0)->roughnessMetallicSampler_0), (_S27)));
                break;
            }
            texture2d<float, access::sample> _S48 = (&kernelContext_0)->roughnessMetallicTexture_0;
            thread uint atlasWidth_2;
            thread uint atlasHeight_2;
            (*((&atlasWidth_2)) = (_S48).get_width(0)),(*((&atlasHeight_2)) = (_S48).get_height(0));
            int3 _S49 = int3(int(0), int(0), int(0));
            float4 metadata_2 = round((((&kernelContext_0)->roughnessMetallicTexture_0).read(vec<uint,2>(((_S49)).xy), uint(((_S49)).z))) * float4(255.0f) );
            int2 _S50 = int2(metadata_2.zw);
            int2 tile_2 = int2(floor(_S27)) - int2(metadata_2.xy);
            if(any(tile_2 < (int2(int(0)) )))
            {
                hasSceneLighting_0 = true;
            }
            else
            {
                hasSceneLighting_0 = any(tile_2 >= _S50);
            }
            if(hasSceneLighting_0)
            {
                int3 _S51 = int3(int(min(1U, atlasWidth_2 - 1U)), int(0), int(0));
                _S28 = (((&kernelContext_0)->roughnessMetallicTexture_0).read(vec<uint,2>(((_S51)).xy), uint(((_S51)).z)));
                break;
            }
            uint _S52 = atlasWidth_2 / uint(_S50.x);
            float _S53 = float(_S52);
            uint _S54 = (atlasHeight_2 - 1U) / uint(_S50.y);
            float2 cellSize_2 = float2(_S53, float(_S54));
            _S28 = (((&kernelContext_0)->roughnessMetallicTexture_0).sample(((&kernelContext_0)->roughnessMetallicSampler_0), ((float2(tile_2) * cellSize_2 + float2(1.5f, 2.5f) + fract(_S27) * max(cellSize_2 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_2), float(atlasHeight_2)))));
            break;
        }
        for(;;)
        {
            float4 _S55 = float4(_S5->compositeControls_0) ;
            if((_S55.x) != 8.0f)
            {
                break;
            }
            bool _S56 = (_S55.w) >= 0.5f;
            for(;;)
            {
                if(!_S56)
                {
                    _S29 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), (_S27)));
                    break;
                }
                texture2d<float, access::sample> _S57 = (&kernelContext_0)->compositeTexture_0;
                thread uint atlasWidth_3;
                thread uint atlasHeight_3;
                (*((&atlasWidth_3)) = (_S57).get_width(0)),(*((&atlasHeight_3)) = (_S57).get_height(0));
                int3 _S58 = int3(int(0), int(0), int(0));
                float4 metadata_3 = round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S58)).xy), uint(((_S58)).z))) * float4(255.0f) );
                int2 _S59 = int2(metadata_3.zw);
                int2 tile_3 = int2(floor(_S27)) - int2(metadata_3.xy);
                if(any(tile_3 < (int2(int(0)) )))
                {
                    hasSceneLighting_0 = true;
                }
                else
                {
                    hasSceneLighting_0 = any(tile_3 >= _S59);
                }
                if(hasSceneLighting_0)
                {
                    int3 _S60 = int3(int(min(1U, atlasWidth_3 - 1U)), int(0), int(0));
                    _S29 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S60)).xy), uint(((_S60)).z)));
                    break;
                }
                uint _S61 = atlasWidth_3 / uint(_S59.x);
                float _S62 = float(_S61);
                uint _S63 = (atlasHeight_3 - 1U) / uint(_S59.y);
                float2 cellSize_3 = float2(_S62, float(_S63));
                _S29 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), ((float2(tile_3) * cellSize_3 + float2(1.5f, 2.5f) + fract(_S27) * max(cellSize_3 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_3), float(atlasHeight_3)))));
                break;
            }
            uint operation_1 = uint(round(_S55.y));
            if(operation_1 == 1U)
            {
                _S28 = _S28 * _S29;
                break;
            }
            if(operation_1 == 2U)
            {
                _S28 = _S28 + _S29;
                break;
            }
            if(operation_1 == 3U)
            {
                _S28 = _S28 - _S29;
                break;
            }
            if(operation_1 == 4U)
            {
                float factor_1 = _S55.z;
                _S28 = _S28 * float4((1.0f - factor_1))  + _S29 * float4(factor_1) ;
                break;
            }
            break;
        }
        roughness_0 = clamp(_S28.x, 0.00999999977648258f, 1.0f);
    }
    else
    {
        roughness_0 = _S18;
    }
    float metallic_0;
    if((textureMask_0 & 32U) != 0U)
    {
        bool _S64 = (udimMask_0 & 32U) != 0U;
        for(;;)
        {
            if(!_S64)
            {
                _S28 = (((&kernelContext_0)->metallicTexture_0).sample(((&kernelContext_0)->metallicSampler_0), (_S27)));
                break;
            }
            texture2d<float, access::sample> _S65 = (&kernelContext_0)->metallicTexture_0;
            thread uint atlasWidth_4;
            thread uint atlasHeight_4;
            (*((&atlasWidth_4)) = (_S65).get_width(0)),(*((&atlasHeight_4)) = (_S65).get_height(0));
            int3 _S66 = int3(int(0), int(0), int(0));
            float4 metadata_4 = round((((&kernelContext_0)->metallicTexture_0).read(vec<uint,2>(((_S66)).xy), uint(((_S66)).z))) * float4(255.0f) );
            int2 _S67 = int2(metadata_4.zw);
            int2 tile_4 = int2(floor(_S27)) - int2(metadata_4.xy);
            if(any(tile_4 < (int2(int(0)) )))
            {
                hasSceneLighting_0 = true;
            }
            else
            {
                hasSceneLighting_0 = any(tile_4 >= _S67);
            }
            if(hasSceneLighting_0)
            {
                int3 _S68 = int3(int(min(1U, atlasWidth_4 - 1U)), int(0), int(0));
                _S28 = (((&kernelContext_0)->metallicTexture_0).read(vec<uint,2>(((_S68)).xy), uint(((_S68)).z)));
                break;
            }
            uint _S69 = atlasWidth_4 / uint(_S67.x);
            float _S70 = float(_S69);
            uint _S71 = (atlasHeight_4 - 1U) / uint(_S67.y);
            float2 cellSize_4 = float2(_S70, float(_S71));
            _S28 = (((&kernelContext_0)->metallicTexture_0).sample(((&kernelContext_0)->metallicSampler_0), ((float2(tile_4) * cellSize_4 + float2(1.5f, 2.5f) + fract(_S27) * max(cellSize_4 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_4), float(atlasHeight_4)))));
            break;
        }
        for(;;)
        {
            float4 _S72 = float4(_S5->compositeControls_0) ;
            if((_S72.x) != 32.0f)
            {
                break;
            }
            bool _S73 = (_S72.w) >= 0.5f;
            for(;;)
            {
                if(!_S73)
                {
                    _S29 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), (_S27)));
                    break;
                }
                texture2d<float, access::sample> _S74 = (&kernelContext_0)->compositeTexture_0;
                thread uint atlasWidth_5;
                thread uint atlasHeight_5;
                (*((&atlasWidth_5)) = (_S74).get_width(0)),(*((&atlasHeight_5)) = (_S74).get_height(0));
                int3 _S75 = int3(int(0), int(0), int(0));
                float4 metadata_5 = round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S75)).xy), uint(((_S75)).z))) * float4(255.0f) );
                int2 _S76 = int2(metadata_5.zw);
                int2 tile_5 = int2(floor(_S27)) - int2(metadata_5.xy);
                if(any(tile_5 < (int2(int(0)) )))
                {
                    hasSceneLighting_0 = true;
                }
                else
                {
                    hasSceneLighting_0 = any(tile_5 >= _S76);
                }
                if(hasSceneLighting_0)
                {
                    int3 _S77 = int3(int(min(1U, atlasWidth_5 - 1U)), int(0), int(0));
                    _S29 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S77)).xy), uint(((_S77)).z)));
                    break;
                }
                uint _S78 = atlasWidth_5 / uint(_S76.x);
                float _S79 = float(_S78);
                uint _S80 = (atlasHeight_5 - 1U) / uint(_S76.y);
                float2 cellSize_5 = float2(_S79, float(_S80));
                _S29 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), ((float2(tile_5) * cellSize_5 + float2(1.5f, 2.5f) + fract(_S27) * max(cellSize_5 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_5), float(atlasHeight_5)))));
                break;
            }
            uint operation_2 = uint(round(_S72.y));
            if(operation_2 == 1U)
            {
                _S28 = _S28 * _S29;
                break;
            }
            if(operation_2 == 2U)
            {
                _S28 = _S28 + _S29;
                break;
            }
            if(operation_2 == 3U)
            {
                _S28 = _S28 - _S29;
                break;
            }
            if(operation_2 == 4U)
            {
                float factor_2 = _S72.z;
                _S28 = _S28 * float4((1.0f - factor_2))  + _S29 * float4(factor_2) ;
                break;
            }
            break;
        }
        metallic_0 = saturate(_S28.x);
    }
    else
    {
        metallic_0 = _S17;
    }
    if((textureMask_0 & 16U) != 0U)
    {
        bool _S81 = (udimMask_0 & 16U) != 0U;
        for(;;)
        {
            if(!_S81)
            {
                _S28 = (((&kernelContext_0)->emissiveTexture_0).sample(((&kernelContext_0)->emissiveSampler_0), (_S27)));
                break;
            }
            texture2d<float, access::sample> _S82 = (&kernelContext_0)->emissiveTexture_0;
            thread uint atlasWidth_6;
            thread uint atlasHeight_6;
            (*((&atlasWidth_6)) = (_S82).get_width(0)),(*((&atlasHeight_6)) = (_S82).get_height(0));
            int3 _S83 = int3(int(0), int(0), int(0));
            float4 metadata_6 = round((((&kernelContext_0)->emissiveTexture_0).read(vec<uint,2>(((_S83)).xy), uint(((_S83)).z))) * float4(255.0f) );
            int2 _S84 = int2(metadata_6.zw);
            int2 tile_6 = int2(floor(_S27)) - int2(metadata_6.xy);
            if(any(tile_6 < (int2(int(0)) )))
            {
                hasSceneLighting_0 = true;
            }
            else
            {
                hasSceneLighting_0 = any(tile_6 >= _S84);
            }
            if(hasSceneLighting_0)
            {
                int3 _S85 = int3(int(min(1U, atlasWidth_6 - 1U)), int(0), int(0));
                _S28 = (((&kernelContext_0)->emissiveTexture_0).read(vec<uint,2>(((_S85)).xy), uint(((_S85)).z)));
                break;
            }
            uint _S86 = atlasWidth_6 / uint(_S84.x);
            float _S87 = float(_S86);
            uint _S88 = (atlasHeight_6 - 1U) / uint(_S84.y);
            float2 cellSize_6 = float2(_S87, float(_S88));
            _S28 = (((&kernelContext_0)->emissiveTexture_0).sample(((&kernelContext_0)->emissiveSampler_0), ((float2(tile_6) * cellSize_6 + float2(1.5f, 2.5f) + fract(_S27) * max(cellSize_6 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_6), float(atlasHeight_6)))));
            break;
        }
        for(;;)
        {
            float4 _S89 = float4(_S5->compositeControls_0) ;
            if((_S89.x) != 16.0f)
            {
                break;
            }
            bool _S90 = (_S89.w) >= 0.5f;
            for(;;)
            {
                if(!_S90)
                {
                    _S29 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), (_S27)));
                    break;
                }
                texture2d<float, access::sample> _S91 = (&kernelContext_0)->compositeTexture_0;
                thread uint atlasWidth_7;
                thread uint atlasHeight_7;
                (*((&atlasWidth_7)) = (_S91).get_width(0)),(*((&atlasHeight_7)) = (_S91).get_height(0));
                int3 _S92 = int3(int(0), int(0), int(0));
                float4 metadata_7 = round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S92)).xy), uint(((_S92)).z))) * float4(255.0f) );
                int2 _S93 = int2(metadata_7.zw);
                int2 tile_7 = int2(floor(_S27)) - int2(metadata_7.xy);
                if(any(tile_7 < (int2(int(0)) )))
                {
                    hasSceneLighting_0 = true;
                }
                else
                {
                    hasSceneLighting_0 = any(tile_7 >= _S93);
                }
                if(hasSceneLighting_0)
                {
                    int3 _S94 = int3(int(min(1U, atlasWidth_7 - 1U)), int(0), int(0));
                    _S29 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S94)).xy), uint(((_S94)).z)));
                    break;
                }
                uint _S95 = atlasWidth_7 / uint(_S93.x);
                float _S96 = float(_S95);
                uint _S97 = (atlasHeight_7 - 1U) / uint(_S93.y);
                float2 cellSize_7 = float2(_S96, float(_S97));
                _S29 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), ((float2(tile_7) * cellSize_7 + float2(1.5f, 2.5f) + fract(_S27) * max(cellSize_7 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_7), float(atlasHeight_7)))));
                break;
            }
            uint operation_3 = uint(round(_S89.y));
            if(operation_3 == 1U)
            {
                _S28 = _S28 * _S29;
                break;
            }
            if(operation_3 == 2U)
            {
                _S28 = _S28 + _S29;
                break;
            }
            if(operation_3 == 3U)
            {
                _S28 = _S28 - _S29;
                break;
            }
            if(operation_3 == 4U)
            {
                float factor_3 = _S89.z;
                _S28 = _S28 * float4((1.0f - factor_3))  + _S29 * float4(factor_3) ;
                break;
            }
            break;
        }
        unlitColor_0 = _S28.xyz;
    }
    else
    {
        unlitColor_0 = emissiveColor_0;
    }
    if((textureMask_0 & 64U) != 0U)
    {
        bool _S98 = (udimMask_0 & 64U) != 0U;
        for(;;)
        {
            if(!_S98)
            {
                _S28 = (((&kernelContext_0)->opacityTexture_0).sample(((&kernelContext_0)->opacitySampler_0), (_S27)));
                break;
            }
            texture2d<float, access::sample> _S99 = (&kernelContext_0)->opacityTexture_0;
            thread uint atlasWidth_8;
            thread uint atlasHeight_8;
            (*((&atlasWidth_8)) = (_S99).get_width(0)),(*((&atlasHeight_8)) = (_S99).get_height(0));
            int3 _S100 = int3(int(0), int(0), int(0));
            float4 metadata_8 = round((((&kernelContext_0)->opacityTexture_0).read(vec<uint,2>(((_S100)).xy), uint(((_S100)).z))) * float4(255.0f) );
            int2 _S101 = int2(metadata_8.zw);
            int2 tile_8 = int2(floor(_S27)) - int2(metadata_8.xy);
            if(any(tile_8 < (int2(int(0)) )))
            {
                hasSceneLighting_0 = true;
            }
            else
            {
                hasSceneLighting_0 = any(tile_8 >= _S101);
            }
            if(hasSceneLighting_0)
            {
                int3 _S102 = int3(int(min(1U, atlasWidth_8 - 1U)), int(0), int(0));
                _S28 = (((&kernelContext_0)->opacityTexture_0).read(vec<uint,2>(((_S102)).xy), uint(((_S102)).z)));
                break;
            }
            uint _S103 = atlasWidth_8 / uint(_S101.x);
            float _S104 = float(_S103);
            uint _S105 = (atlasHeight_8 - 1U) / uint(_S101.y);
            float2 cellSize_8 = float2(_S104, float(_S105));
            _S28 = (((&kernelContext_0)->opacityTexture_0).sample(((&kernelContext_0)->opacitySampler_0), ((float2(tile_8) * cellSize_8 + float2(1.5f, 2.5f) + fract(_S27) * max(cellSize_8 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_8), float(atlasHeight_8)))));
            break;
        }
        for(;;)
        {
            float4 _S106 = float4(_S5->compositeControls_0) ;
            if((_S106.x) != 64.0f)
            {
                break;
            }
            bool _S107 = (_S106.w) >= 0.5f;
            for(;;)
            {
                if(!_S107)
                {
                    _S29 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), (_S27)));
                    break;
                }
                texture2d<float, access::sample> _S108 = (&kernelContext_0)->compositeTexture_0;
                thread uint atlasWidth_9;
                thread uint atlasHeight_9;
                (*((&atlasWidth_9)) = (_S108).get_width(0)),(*((&atlasHeight_9)) = (_S108).get_height(0));
                int3 _S109 = int3(int(0), int(0), int(0));
                float4 metadata_9 = round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S109)).xy), uint(((_S109)).z))) * float4(255.0f) );
                int2 _S110 = int2(metadata_9.zw);
                int2 tile_9 = int2(floor(_S27)) - int2(metadata_9.xy);
                if(any(tile_9 < (int2(int(0)) )))
                {
                    hasSceneLighting_0 = true;
                }
                else
                {
                    hasSceneLighting_0 = any(tile_9 >= _S110);
                }
                if(hasSceneLighting_0)
                {
                    int3 _S111 = int3(int(min(1U, atlasWidth_9 - 1U)), int(0), int(0));
                    _S29 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S111)).xy), uint(((_S111)).z)));
                    break;
                }
                uint _S112 = atlasWidth_9 / uint(_S110.x);
                float _S113 = float(_S112);
                uint _S114 = (atlasHeight_9 - 1U) / uint(_S110.y);
                float2 cellSize_9 = float2(_S113, float(_S114));
                _S29 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), ((float2(tile_9) * cellSize_9 + float2(1.5f, 2.5f) + fract(_S27) * max(cellSize_9 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_9), float(atlasHeight_9)))));
                break;
            }
            uint operation_4 = uint(round(_S106.y));
            if(operation_4 == 1U)
            {
                _S28 = _S28 * _S29;
                break;
            }
            if(operation_4 == 2U)
            {
                _S28 = _S28 + _S29;
                break;
            }
            if(operation_4 == 3U)
            {
                _S28 = _S28 - _S29;
                break;
            }
            if(operation_4 == 4U)
            {
                float factor_4 = _S106.z;
                _S28 = _S28 * float4((1.0f - factor_4))  + _S29 * float4(factor_4) ;
                break;
            }
            break;
        }
        opacity_0 = saturate(_S28.x);
    }
    float occlusion_0;
    if((textureMask_0 & 128U) != 0U)
    {
        bool _S115 = (udimMask_0 & 128U) != 0U;
        for(;;)
        {
            if(!_S115)
            {
                _S28 = (((&kernelContext_0)->occlusionTexture_0).sample(((&kernelContext_0)->occlusionSampler_0), (_S27)));
                break;
            }
            texture2d<float, access::sample> _S116 = (&kernelContext_0)->occlusionTexture_0;
            thread uint atlasWidth_10;
            thread uint atlasHeight_10;
            (*((&atlasWidth_10)) = (_S116).get_width(0)),(*((&atlasHeight_10)) = (_S116).get_height(0));
            int3 _S117 = int3(int(0), int(0), int(0));
            float4 metadata_10 = round((((&kernelContext_0)->occlusionTexture_0).read(vec<uint,2>(((_S117)).xy), uint(((_S117)).z))) * float4(255.0f) );
            int2 _S118 = int2(metadata_10.zw);
            int2 tile_10 = int2(floor(_S27)) - int2(metadata_10.xy);
            if(any(tile_10 < (int2(int(0)) )))
            {
                hasSceneLighting_0 = true;
            }
            else
            {
                hasSceneLighting_0 = any(tile_10 >= _S118);
            }
            if(hasSceneLighting_0)
            {
                int3 _S119 = int3(int(min(1U, atlasWidth_10 - 1U)), int(0), int(0));
                _S28 = (((&kernelContext_0)->occlusionTexture_0).read(vec<uint,2>(((_S119)).xy), uint(((_S119)).z)));
                break;
            }
            uint _S120 = atlasWidth_10 / uint(_S118.x);
            float _S121 = float(_S120);
            uint _S122 = (atlasHeight_10 - 1U) / uint(_S118.y);
            float2 cellSize_10 = float2(_S121, float(_S122));
            _S28 = (((&kernelContext_0)->occlusionTexture_0).sample(((&kernelContext_0)->occlusionSampler_0), ((float2(tile_10) * cellSize_10 + float2(1.5f, 2.5f) + fract(_S27) * max(cellSize_10 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_10), float(atlasHeight_10)))));
            break;
        }
        for(;;)
        {
            float4 _S123 = float4(_S5->compositeControls_0) ;
            if((_S123.x) != 128.0f)
            {
                break;
            }
            bool _S124 = (_S123.w) >= 0.5f;
            for(;;)
            {
                if(!_S124)
                {
                    _S29 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), (_S27)));
                    break;
                }
                texture2d<float, access::sample> _S125 = (&kernelContext_0)->compositeTexture_0;
                thread uint atlasWidth_11;
                thread uint atlasHeight_11;
                (*((&atlasWidth_11)) = (_S125).get_width(0)),(*((&atlasHeight_11)) = (_S125).get_height(0));
                int3 _S126 = int3(int(0), int(0), int(0));
                float4 metadata_11 = round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S126)).xy), uint(((_S126)).z))) * float4(255.0f) );
                int2 _S127 = int2(metadata_11.zw);
                int2 tile_11 = int2(floor(_S27)) - int2(metadata_11.xy);
                if(any(tile_11 < (int2(int(0)) )))
                {
                    hasSceneLighting_0 = true;
                }
                else
                {
                    hasSceneLighting_0 = any(tile_11 >= _S127);
                }
                if(hasSceneLighting_0)
                {
                    int3 _S128 = int3(int(min(1U, atlasWidth_11 - 1U)), int(0), int(0));
                    _S29 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S128)).xy), uint(((_S128)).z)));
                    break;
                }
                uint _S129 = atlasWidth_11 / uint(_S127.x);
                float _S130 = float(_S129);
                uint _S131 = (atlasHeight_11 - 1U) / uint(_S127.y);
                float2 cellSize_11 = float2(_S130, float(_S131));
                _S29 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), ((float2(tile_11) * cellSize_11 + float2(1.5f, 2.5f) + fract(_S27) * max(cellSize_11 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_11), float(atlasHeight_11)))));
                break;
            }
            uint operation_5 = uint(round(_S123.y));
            if(operation_5 == 1U)
            {
                _S28 = _S28 * _S29;
                break;
            }
            if(operation_5 == 2U)
            {
                _S28 = _S28 + _S29;
                break;
            }
            if(operation_5 == 3U)
            {
                _S28 = _S28 - _S29;
                break;
            }
            if(operation_5 == 4U)
            {
                float factor_5 = _S123.z;
                _S28 = _S28 * float4((1.0f - factor_5))  + _S29 * float4(factor_5) ;
                break;
            }
            break;
        }
        occlusion_0 = saturate(_S28.x);
    }
    else
    {
        occlusion_0 = _S12;
    }
    float3 specularColor_0;
    if((textureMask_0 & 256U) != 0U)
    {
        bool _S132 = (udimMask_0 & 256U) != 0U;
        for(;;)
        {
            if(!_S132)
            {
                _S28 = (((&kernelContext_0)->specularColorTexture_0).sample(((&kernelContext_0)->specularColorSampler_0), (_S27)));
                break;
            }
            texture2d<float, access::sample> _S133 = (&kernelContext_0)->specularColorTexture_0;
            thread uint atlasWidth_12;
            thread uint atlasHeight_12;
            (*((&atlasWidth_12)) = (_S133).get_width(0)),(*((&atlasHeight_12)) = (_S133).get_height(0));
            int3 _S134 = int3(int(0), int(0), int(0));
            float4 metadata_12 = round((((&kernelContext_0)->specularColorTexture_0).read(vec<uint,2>(((_S134)).xy), uint(((_S134)).z))) * float4(255.0f) );
            int2 _S135 = int2(metadata_12.zw);
            int2 tile_12 = int2(floor(_S27)) - int2(metadata_12.xy);
            if(any(tile_12 < (int2(int(0)) )))
            {
                hasSceneLighting_0 = true;
            }
            else
            {
                hasSceneLighting_0 = any(tile_12 >= _S135);
            }
            if(hasSceneLighting_0)
            {
                int3 _S136 = int3(int(min(1U, atlasWidth_12 - 1U)), int(0), int(0));
                _S28 = (((&kernelContext_0)->specularColorTexture_0).read(vec<uint,2>(((_S136)).xy), uint(((_S136)).z)));
                break;
            }
            uint _S137 = atlasWidth_12 / uint(_S135.x);
            float _S138 = float(_S137);
            uint _S139 = (atlasHeight_12 - 1U) / uint(_S135.y);
            float2 cellSize_12 = float2(_S138, float(_S139));
            _S28 = (((&kernelContext_0)->specularColorTexture_0).sample(((&kernelContext_0)->specularColorSampler_0), ((float2(tile_12) * cellSize_12 + float2(1.5f, 2.5f) + fract(_S27) * max(cellSize_12 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_12), float(atlasHeight_12)))));
            break;
        }
        for(;;)
        {
            float4 _S140 = float4(_S5->compositeControls_0) ;
            if((_S140.x) != 256.0f)
            {
                break;
            }
            bool _S141 = (_S140.w) >= 0.5f;
            for(;;)
            {
                if(!_S141)
                {
                    _S29 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), (_S27)));
                    break;
                }
                texture2d<float, access::sample> _S142 = (&kernelContext_0)->compositeTexture_0;
                thread uint atlasWidth_13;
                thread uint atlasHeight_13;
                (*((&atlasWidth_13)) = (_S142).get_width(0)),(*((&atlasHeight_13)) = (_S142).get_height(0));
                int3 _S143 = int3(int(0), int(0), int(0));
                float4 metadata_13 = round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S143)).xy), uint(((_S143)).z))) * float4(255.0f) );
                int2 _S144 = int2(metadata_13.zw);
                int2 tile_13 = int2(floor(_S27)) - int2(metadata_13.xy);
                if(any(tile_13 < (int2(int(0)) )))
                {
                    hasSceneLighting_0 = true;
                }
                else
                {
                    hasSceneLighting_0 = any(tile_13 >= _S144);
                }
                if(hasSceneLighting_0)
                {
                    int3 _S145 = int3(int(min(1U, atlasWidth_13 - 1U)), int(0), int(0));
                    _S29 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S145)).xy), uint(((_S145)).z)));
                    break;
                }
                uint _S146 = atlasWidth_13 / uint(_S144.x);
                float _S147 = float(_S146);
                uint _S148 = (atlasHeight_13 - 1U) / uint(_S144.y);
                float2 cellSize_13 = float2(_S147, float(_S148));
                _S29 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), ((float2(tile_13) * cellSize_13 + float2(1.5f, 2.5f) + fract(_S27) * max(cellSize_13 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_13), float(atlasHeight_13)))));
                break;
            }
            uint operation_6 = uint(round(_S140.y));
            if(operation_6 == 1U)
            {
                _S28 = _S28 * _S29;
                break;
            }
            if(operation_6 == 2U)
            {
                _S28 = _S28 + _S29;
                break;
            }
            if(operation_6 == 3U)
            {
                _S28 = _S28 - _S29;
                break;
            }
            if(operation_6 == 4U)
            {
                float factor_6 = _S140.z;
                _S28 = _S28 * float4((1.0f - factor_6))  + _S29 * float4(factor_6) ;
                break;
            }
            break;
        }
        specularColor_0 = saturate(_S28.xyz);
    }
    else
    {
        specularColor_0 = _S20;
    }
    float clearcoatAmount_0;
    if((textureMask_0 & 512U) != 0U)
    {
        bool _S149 = (udimMask_0 & 512U) != 0U;
        for(;;)
        {
            if(!_S149)
            {
                _S28 = (((&kernelContext_0)->clearcoatTexture_0).sample(((&kernelContext_0)->clearcoatSampler_0), (_S27)));
                break;
            }
            texture2d<float, access::sample> _S150 = (&kernelContext_0)->clearcoatTexture_0;
            thread uint atlasWidth_14;
            thread uint atlasHeight_14;
            (*((&atlasWidth_14)) = (_S150).get_width(0)),(*((&atlasHeight_14)) = (_S150).get_height(0));
            int3 _S151 = int3(int(0), int(0), int(0));
            float4 metadata_14 = round((((&kernelContext_0)->clearcoatTexture_0).read(vec<uint,2>(((_S151)).xy), uint(((_S151)).z))) * float4(255.0f) );
            int2 _S152 = int2(metadata_14.zw);
            int2 tile_14 = int2(floor(_S27)) - int2(metadata_14.xy);
            if(any(tile_14 < (int2(int(0)) )))
            {
                hasSceneLighting_0 = true;
            }
            else
            {
                hasSceneLighting_0 = any(tile_14 >= _S152);
            }
            if(hasSceneLighting_0)
            {
                int3 _S153 = int3(int(min(1U, atlasWidth_14 - 1U)), int(0), int(0));
                _S28 = (((&kernelContext_0)->clearcoatTexture_0).read(vec<uint,2>(((_S153)).xy), uint(((_S153)).z)));
                break;
            }
            uint _S154 = atlasWidth_14 / uint(_S152.x);
            float _S155 = float(_S154);
            uint _S156 = (atlasHeight_14 - 1U) / uint(_S152.y);
            float2 cellSize_14 = float2(_S155, float(_S156));
            _S28 = (((&kernelContext_0)->clearcoatTexture_0).sample(((&kernelContext_0)->clearcoatSampler_0), ((float2(tile_14) * cellSize_14 + float2(1.5f, 2.5f) + fract(_S27) * max(cellSize_14 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_14), float(atlasHeight_14)))));
            break;
        }
        for(;;)
        {
            float4 _S157 = float4(_S5->compositeControls_0) ;
            if((_S157.x) != 512.0f)
            {
                break;
            }
            bool _S158 = (_S157.w) >= 0.5f;
            for(;;)
            {
                if(!_S158)
                {
                    _S29 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), (_S27)));
                    break;
                }
                texture2d<float, access::sample> _S159 = (&kernelContext_0)->compositeTexture_0;
                thread uint atlasWidth_15;
                thread uint atlasHeight_15;
                (*((&atlasWidth_15)) = (_S159).get_width(0)),(*((&atlasHeight_15)) = (_S159).get_height(0));
                int3 _S160 = int3(int(0), int(0), int(0));
                float4 metadata_15 = round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S160)).xy), uint(((_S160)).z))) * float4(255.0f) );
                int2 _S161 = int2(metadata_15.zw);
                int2 tile_15 = int2(floor(_S27)) - int2(metadata_15.xy);
                if(any(tile_15 < (int2(int(0)) )))
                {
                    hasSceneLighting_0 = true;
                }
                else
                {
                    hasSceneLighting_0 = any(tile_15 >= _S161);
                }
                if(hasSceneLighting_0)
                {
                    int3 _S162 = int3(int(min(1U, atlasWidth_15 - 1U)), int(0), int(0));
                    _S29 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S162)).xy), uint(((_S162)).z)));
                    break;
                }
                uint _S163 = atlasWidth_15 / uint(_S161.x);
                float _S164 = float(_S163);
                uint _S165 = (atlasHeight_15 - 1U) / uint(_S161.y);
                float2 cellSize_15 = float2(_S164, float(_S165));
                _S29 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), ((float2(tile_15) * cellSize_15 + float2(1.5f, 2.5f) + fract(_S27) * max(cellSize_15 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_15), float(atlasHeight_15)))));
                break;
            }
            uint operation_7 = uint(round(_S157.y));
            if(operation_7 == 1U)
            {
                _S28 = _S28 * _S29;
                break;
            }
            if(operation_7 == 2U)
            {
                _S28 = _S28 + _S29;
                break;
            }
            if(operation_7 == 3U)
            {
                _S28 = _S28 - _S29;
                break;
            }
            if(operation_7 == 4U)
            {
                float factor_7 = _S157.z;
                _S28 = _S28 * float4((1.0f - factor_7))  + _S29 * float4(factor_7) ;
                break;
            }
            break;
        }
        clearcoatAmount_0 = saturate(_S28.x);
    }
    else
    {
        clearcoatAmount_0 = _S22;
    }
    float clearcoatRoughness_0;
    if((textureMask_0 & 1024U) != 0U)
    {
        bool _S166 = (udimMask_0 & 1024U) != 0U;
        for(;;)
        {
            if(!_S166)
            {
                _S28 = (((&kernelContext_0)->clearcoatRoughnessTexture_0).sample(((&kernelContext_0)->clearcoatRoughnessSampler_0), (_S27)));
                break;
            }
            texture2d<float, access::sample> _S167 = (&kernelContext_0)->clearcoatRoughnessTexture_0;
            thread uint atlasWidth_16;
            thread uint atlasHeight_16;
            (*((&atlasWidth_16)) = (_S167).get_width(0)),(*((&atlasHeight_16)) = (_S167).get_height(0));
            int3 _S168 = int3(int(0), int(0), int(0));
            float4 metadata_16 = round((((&kernelContext_0)->clearcoatRoughnessTexture_0).read(vec<uint,2>(((_S168)).xy), uint(((_S168)).z))) * float4(255.0f) );
            int2 _S169 = int2(metadata_16.zw);
            int2 tile_16 = int2(floor(_S27)) - int2(metadata_16.xy);
            if(any(tile_16 < (int2(int(0)) )))
            {
                hasSceneLighting_0 = true;
            }
            else
            {
                hasSceneLighting_0 = any(tile_16 >= _S169);
            }
            if(hasSceneLighting_0)
            {
                int3 _S170 = int3(int(min(1U, atlasWidth_16 - 1U)), int(0), int(0));
                _S28 = (((&kernelContext_0)->clearcoatRoughnessTexture_0).read(vec<uint,2>(((_S170)).xy), uint(((_S170)).z)));
                break;
            }
            uint _S171 = atlasWidth_16 / uint(_S169.x);
            float _S172 = float(_S171);
            uint _S173 = (atlasHeight_16 - 1U) / uint(_S169.y);
            float2 cellSize_16 = float2(_S172, float(_S173));
            _S28 = (((&kernelContext_0)->clearcoatRoughnessTexture_0).sample(((&kernelContext_0)->clearcoatRoughnessSampler_0), ((float2(tile_16) * cellSize_16 + float2(1.5f, 2.5f) + fract(_S27) * max(cellSize_16 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_16), float(atlasHeight_16)))));
            break;
        }
        for(;;)
        {
            float4 _S174 = float4(_S5->compositeControls_0) ;
            if((_S174.x) != 1024.0f)
            {
                break;
            }
            bool _S175 = (_S174.w) >= 0.5f;
            for(;;)
            {
                if(!_S175)
                {
                    _S29 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), (_S27)));
                    break;
                }
                texture2d<float, access::sample> _S176 = (&kernelContext_0)->compositeTexture_0;
                thread uint atlasWidth_17;
                thread uint atlasHeight_17;
                (*((&atlasWidth_17)) = (_S176).get_width(0)),(*((&atlasHeight_17)) = (_S176).get_height(0));
                int3 _S177 = int3(int(0), int(0), int(0));
                float4 metadata_17 = round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S177)).xy), uint(((_S177)).z))) * float4(255.0f) );
                int2 _S178 = int2(metadata_17.zw);
                int2 tile_17 = int2(floor(_S27)) - int2(metadata_17.xy);
                if(any(tile_17 < (int2(int(0)) )))
                {
                    hasSceneLighting_0 = true;
                }
                else
                {
                    hasSceneLighting_0 = any(tile_17 >= _S178);
                }
                if(hasSceneLighting_0)
                {
                    int3 _S179 = int3(int(min(1U, atlasWidth_17 - 1U)), int(0), int(0));
                    _S29 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S179)).xy), uint(((_S179)).z)));
                    break;
                }
                uint _S180 = atlasWidth_17 / uint(_S178.x);
                float _S181 = float(_S180);
                uint _S182 = (atlasHeight_17 - 1U) / uint(_S178.y);
                float2 cellSize_17 = float2(_S181, float(_S182));
                _S29 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), ((float2(tile_17) * cellSize_17 + float2(1.5f, 2.5f) + fract(_S27) * max(cellSize_17 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_17), float(atlasHeight_17)))));
                break;
            }
            uint operation_8 = uint(round(_S174.y));
            if(operation_8 == 1U)
            {
                _S28 = _S28 * _S29;
                break;
            }
            if(operation_8 == 2U)
            {
                _S28 = _S28 + _S29;
                break;
            }
            if(operation_8 == 3U)
            {
                _S28 = _S28 - _S29;
                break;
            }
            if(operation_8 == 4U)
            {
                float factor_8 = _S174.z;
                _S28 = _S28 * float4((1.0f - factor_8))  + _S29 * float4(factor_8) ;
                break;
            }
            break;
        }
        clearcoatRoughness_0 = saturate(_S28.x);
    }
    else
    {
        clearcoatRoughness_0 = _S23;
    }
    float ior_0;
    if((textureMask_0 & 2048U) != 0U)
    {
        bool _S183 = (udimMask_0 & 2048U) != 0U;
        for(;;)
        {
            if(!_S183)
            {
                _S28 = (((&kernelContext_0)->iorTexture_0).sample(((&kernelContext_0)->iorSampler_0), (_S27)));
                break;
            }
            texture2d<float, access::sample> _S184 = (&kernelContext_0)->iorTexture_0;
            thread uint atlasWidth_18;
            thread uint atlasHeight_18;
            (*((&atlasWidth_18)) = (_S184).get_width(0)),(*((&atlasHeight_18)) = (_S184).get_height(0));
            int3 _S185 = int3(int(0), int(0), int(0));
            float4 metadata_18 = round((((&kernelContext_0)->iorTexture_0).read(vec<uint,2>(((_S185)).xy), uint(((_S185)).z))) * float4(255.0f) );
            int2 _S186 = int2(metadata_18.zw);
            int2 tile_18 = int2(floor(_S27)) - int2(metadata_18.xy);
            if(any(tile_18 < (int2(int(0)) )))
            {
                hasSceneLighting_0 = true;
            }
            else
            {
                hasSceneLighting_0 = any(tile_18 >= _S186);
            }
            if(hasSceneLighting_0)
            {
                int3 _S187 = int3(int(min(1U, atlasWidth_18 - 1U)), int(0), int(0));
                _S28 = (((&kernelContext_0)->iorTexture_0).read(vec<uint,2>(((_S187)).xy), uint(((_S187)).z)));
                break;
            }
            uint _S188 = atlasWidth_18 / uint(_S186.x);
            float _S189 = float(_S188);
            uint _S190 = (atlasHeight_18 - 1U) / uint(_S186.y);
            float2 cellSize_18 = float2(_S189, float(_S190));
            _S28 = (((&kernelContext_0)->iorTexture_0).sample(((&kernelContext_0)->iorSampler_0), ((float2(tile_18) * cellSize_18 + float2(1.5f, 2.5f) + fract(_S27) * max(cellSize_18 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_18), float(atlasHeight_18)))));
            break;
        }
        for(;;)
        {
            float4 _S191 = float4(_S5->compositeControls_0) ;
            if((_S191.x) != 2048.0f)
            {
                break;
            }
            bool _S192 = (_S191.w) >= 0.5f;
            for(;;)
            {
                if(!_S192)
                {
                    _S29 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), (_S27)));
                    break;
                }
                texture2d<float, access::sample> _S193 = (&kernelContext_0)->compositeTexture_0;
                thread uint atlasWidth_19;
                thread uint atlasHeight_19;
                (*((&atlasWidth_19)) = (_S193).get_width(0)),(*((&atlasHeight_19)) = (_S193).get_height(0));
                int3 _S194 = int3(int(0), int(0), int(0));
                float4 metadata_19 = round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S194)).xy), uint(((_S194)).z))) * float4(255.0f) );
                int2 _S195 = int2(metadata_19.zw);
                int2 tile_19 = int2(floor(_S27)) - int2(metadata_19.xy);
                if(any(tile_19 < (int2(int(0)) )))
                {
                    hasSceneLighting_0 = true;
                }
                else
                {
                    hasSceneLighting_0 = any(tile_19 >= _S195);
                }
                if(hasSceneLighting_0)
                {
                    int3 _S196 = int3(int(min(1U, atlasWidth_19 - 1U)), int(0), int(0));
                    _S29 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S196)).xy), uint(((_S196)).z)));
                    break;
                }
                uint _S197 = atlasWidth_19 / uint(_S195.x);
                float _S198 = float(_S197);
                uint _S199 = (atlasHeight_19 - 1U) / uint(_S195.y);
                float2 cellSize_19 = float2(_S198, float(_S199));
                _S29 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), ((float2(tile_19) * cellSize_19 + float2(1.5f, 2.5f) + fract(_S27) * max(cellSize_19 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_19), float(atlasHeight_19)))));
                break;
            }
            uint operation_9 = uint(round(_S191.y));
            if(operation_9 == 1U)
            {
                _S28 = _S28 * _S29;
                break;
            }
            if(operation_9 == 2U)
            {
                _S28 = _S28 + _S29;
                break;
            }
            if(operation_9 == 3U)
            {
                _S28 = _S28 - _S29;
                break;
            }
            if(operation_9 == 4U)
            {
                float factor_9 = _S191.z;
                _S28 = _S28 * float4((1.0f - factor_9))  + _S29 * float4(factor_9) ;
                break;
            }
            break;
        }
        ior_0 = _S28.x;
    }
    else
    {
        ior_0 = _S21;
    }
    float opacityThreshold_0 = _S16.z;
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
    float3 worldNormal_1 = normalize(_S1.worldNormal_0);
    float lengthSquared_0 = dot(_S1.eyePosition_0, _S1.eyePosition_0);
    float3 irradiance_0;
    if(lengthSquared_0 > 0.00100000004749745f)
    {
        irradiance_0 = - _S1.eyePosition_0 * float3(rsqrt(lengthSquared_0)) ;
    }
    else
    {
        irradiance_0 = float3(0.0f, 0.0f, 1.0f);
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
    float3 worldNormal_2;
    if(isFrontFace_0)
    {
        worldNormal_2 = worldNormal_1;
    }
    else
    {
        worldNormal_2 = - worldNormal_1;
    }
    float _S200 = saturate(abs(dot(normal_2, irradiance_0)) + 0.00000999999974738f);
    float _S201 = max(0.00100000004749745f, roughness_0);
    float _S202 = max(0.00100000004749745f, clearcoatRoughness_0);
    float reflectanceRatio_0 = (1.0f - ior_0) / (1.0f + ior_0);
    float3 _S203 = float3(3.14159274101257324f) ;
    float3 diffuse_0 = diffuseColor_0 / _S203;
    float3 normalIncidence_0;
    float3 grazingIncidence_0;
    float3 diffuse_1;
    if((_S16.w) >= 0.5f)
    {
        float3 _S204 = float3(1.0f, 1.0f, 1.0f);
        normalIncidence_0 = specularColor_0;
        grazingIncidence_0 = _S204;
        diffuse_1 = diffuse_0;
    }
    else
    {
        float3 _S205 = float3(metallic_0) ;
        float3 specularTint_0 = mix(float3(1.0f, 1.0f, 1.0f), diffuseColor_0, _S205);
        float3 diffuse_2 = diffuse_0 * float3((1.0f - metallic_0)) ;
        normalIncidence_0 = mix(float3((reflectanceRatio_0 * reflectanceRatio_0))  * specularTint_0, specularTint_0, _S205);
        grazingIncidence_0 = specularTint_0;
        diffuse_1 = diffuse_2;
    }
    float4 _S206 = float4(_S6->ambientLight_0) ;
    float _S207 = _S206.w;
    if(_S207 > 0.5f)
    {
        hasSceneLighting_0 = true;
    }
    else
    {
        float3 _S208 = _S206.xyz;
        hasSceneLighting_0 = (dot(_S208, _S208)) > 0.0f;
    }
    if(hasSceneLighting_0)
    {
        hasSceneLighting_0 = true;
    }
    else
    {
        hasSceneLighting_0 = ((float4(_S6->environmentControls_0) ).x) >= 0.5f;
    }
    if(hasSceneLighting_0)
    {
        hasSceneLighting_0 = true;
    }
    else
    {
        hasSceneLighting_0 = ((float4(_S6->environmentControls_0) ).w) >= 0.5f;
    }
    uint _S209 = min(uint(_S207), 8U);
    matrix<float,int(4),int(4)>  _S210 = matrix<float,int(4),int(4)> (_S6->eyeToWorld_0.data_0[int(0)][int(0)], _S6->eyeToWorld_0.data_0[int(0)][int(1)], _S6->eyeToWorld_0.data_0[int(0)][int(2)], _S6->eyeToWorld_0.data_0[int(0)][int(3)], _S6->eyeToWorld_0.data_0[int(1)][int(0)], _S6->eyeToWorld_0.data_0[int(1)][int(1)], _S6->eyeToWorld_0.data_0[int(1)][int(2)], _S6->eyeToWorld_0.data_0[int(1)][int(3)], _S6->eyeToWorld_0.data_0[int(2)][int(0)], _S6->eyeToWorld_0.data_0[int(2)][int(1)], _S6->eyeToWorld_0.data_0[int(2)][int(2)], _S6->eyeToWorld_0.data_0[int(2)][int(3)], _S6->eyeToWorld_0.data_0[int(3)][int(0)], _S6->eyeToWorld_0.data_0[int(3)][int(1)], _S6->eyeToWorld_0.data_0[int(3)][int(2)], _S6->eyeToWorld_0.data_0[int(3)][int(3)]);
    float3 sceneEye_0 = normalize((((float4(irradiance_0, 0.0f)) * (_S210))).xyz);
    float3 worldPosition_0 = (((float4(_S1.eyePosition_0, 1.0f)) * (_S210))).xyz;
    float3 worldGeometricNormal_0 = cross(dfdx(worldPosition_0), dfdy(worldPosition_0));
    float worldNormalLengthSquared_0 = dot(worldGeometricNormal_0, worldGeometricNormal_0);
    if(worldNormalLengthSquared_0 > 9.99999968265522539e-21f)
    {
        specularColor_0 = worldGeometricNormal_0 * float3(rsqrt(worldNormalLengthSquared_0)) ;
    }
    else
    {
        specularColor_0 = float3(0.0f, 0.0f, 1.0f);
    }
    float4 _S211 = float4(_S5->lightColorAmbient_0) ;
    float3 color_0 = diffuseColor_0 * float3(_S211.w) ;
    uint domeLinkMask_0 = uint(max((float4(_S5->domeLinkControls_0) ).x, 0.0f));
    float4 _S212 = float4(_S6->domeControls_0) ;
    uint _S213 = min(uint(max(_S212.x, 0.0f)), 8U);
    uint allDomes_0 = (1U << _S213) - 1U;
    bool allDomesLinked_0 = (domeLinkMask_0 & allDomes_0) == allDomes_0;
    float3 _S214 = _S206.xyz;
    uint lightCount_0;
    float3 domeAmbient_1;
    if(!allDomesLinked_0)
    {
        float3 _S215 = float3(0.0f, 0.0f, 0.0f);
        lightCount_0 = 0U;
        domeAmbient_1 = _S215;
        for(;;)
        {
            if(lightCount_0 < _S213)
            {
            }
            else
            {
                break;
            }
            if((domeLinkMask_0 & (1U << lightCount_0)) != 0U)
            {
                domeAmbient_1 = domeAmbient_1 + (float4((&_S6->domeAmbient_0)->data_1[lightCount_0]) ).xyz;
            }
            lightCount_0 = lightCount_0 + 1U;
        }
    }
    else
    {
        domeAmbient_1 = _S214;
    }
    float3 color_1 = color_0 + diffuseColor_0 * domeAmbient_1;
    bool _S216 = !hasSceneLighting_0;
    if(_S216)
    {
        lightCount_0 = 1U;
    }
    else
    {
        lightCount_0 = _S209;
    }
    uint _S217 = uint(max(_S10.w, 0.0f));
    uint lightIndex_0 = 0U;
    float3 color_2 = color_1;
    for(;;)
    {
        if(lightIndex_0 < lightCount_0)
        {
        }
        else
        {
            break;
        }
        bool _S218;
        if(hasSceneLighting_0)
        {
            _S218 = (_S217 & (1U << lightIndex_0)) == 0U;
        }
        else
        {
            _S218 = false;
        }
        if(_S218)
        {
            lightIndex_0 = lightIndex_0 + 1U;
            continue;
        }
        bool _S219 = lightIndex_0 == 0U;
        bool _S220;
        if(_S219)
        {
            _S220 = _S216;
        }
        else
        {
            _S220 = false;
        }
        float lightType_0;
        if(_S220)
        {
            lightType_0 = 1.0f;
        }
        else
        {
            lightType_0 = (float4((&_S6->lightPositionType_0)->data_1[lightIndex_0]) ).w;
        }
        bool _S221;
        if(_S219)
        {
            _S221 = _S216;
        }
        else
        {
            _S221 = false;
        }
        if(_S221)
        {
            lightDirection_0 = normalize((float4(_S5->lightDirectionIntensity_0) ).xyz);
        }
        else
        {
            lightDirection_0 = normalize((float4((&_S6->lightDirectionRadius_0)->data_1[lightIndex_0]) ).xyz);
        }
        bool _S222;
        if(_S219)
        {
            _S222 = _S216;
        }
        else
        {
            _S222 = false;
        }
        if(_S222)
        {
            roughness_0 = (float4(_S5->lightDirectionIntensity_0) ).w;
        }
        else
        {
            roughness_0 = (float4((&_S6->lightColorIntensity_0)->data_1[lightIndex_0]) ).w;
        }
        bool _S223;
        if(_S219)
        {
            _S223 = _S216;
        }
        else
        {
            _S223 = false;
        }
        if(_S223)
        {
            diffuseColor_0 = _S211.xyz;
        }
        else
        {
            diffuseColor_0 = (float4((&_S6->lightColorIntensity_0)->data_1[lightIndex_0]) ).xyz;
        }
        bool _S224;
        if(_S219)
        {
            _S224 = _S216;
        }
        else
        {
            _S224 = false;
        }
        if(_S224)
        {
            metallic_0 = 1.0f;
        }
        else
        {
            metallic_0 = (float4((&_S6->lightControls_0)->data_1[lightIndex_0]) ).x;
        }
        bool _S225;
        if(_S219)
        {
            _S225 = _S216;
        }
        else
        {
            _S225 = false;
        }
        if(_S225)
        {
            clearcoatRoughness_0 = 1.0f;
        }
        else
        {
            clearcoatRoughness_0 = (float4((&_S6->lightControls_0)->data_1[lightIndex_0]) ).y;
        }
        bool _S226;
        if(_S219)
        {
            _S226 = _S216;
        }
        else
        {
            _S226 = false;
        }
        if(_S226)
        {
            lightTangent_0 = float3(1.0f, 0.0f, 0.0f);
        }
        else
        {
            lightTangent_0 = normalize((float4((&_S6->lightTangentShapeX_0)->data_1[lightIndex_0]) ).xyz);
        }
        bool _S227;
        if(_S219)
        {
            _S227 = _S216;
        }
        else
        {
            _S227 = false;
        }
        if(_S227)
        {
            lightBitangent_0 = float3(0.0f, 1.0f, 0.0f);
        }
        else
        {
            lightBitangent_0 = normalize((float4((&_S6->lightBitangentShapeY_0)->data_1[lightIndex_0]) ).xyz);
        }
        bool _S228;
        if(_S219)
        {
            _S228 = _S216;
        }
        else
        {
            _S228 = false;
        }
        float shapeX_0;
        if(_S228)
        {
            shapeX_0 = 0.0f;
        }
        else
        {
            shapeX_0 = (float4((&_S6->lightTangentShapeX_0)->data_1[lightIndex_0]) ).w;
        }
        bool _S229;
        if(_S219)
        {
            _S229 = _S216;
        }
        else
        {
            _S229 = false;
        }
        float shapeY_0;
        if(_S229)
        {
            shapeY_0 = 0.0f;
        }
        else
        {
            shapeY_0 = (float4((&_S6->lightBitangentShapeY_0)->data_1[lightIndex_0]) ).w;
        }
        bool _S230;
        if(_S219)
        {
            _S230 = _S216;
        }
        else
        {
            _S230 = false;
        }
        float lightRadius_0;
        if(_S230)
        {
            lightRadius_0 = 0.0f;
        }
        else
        {
            lightRadius_0 = (float4((&_S6->lightDirectionRadius_0)->data_1[lightIndex_0]) ).w;
        }
        bool _S231;
        if(_S219)
        {
            _S231 = _S216;
        }
        else
        {
            _S231 = false;
        }
        if(_S231)
        {
            domeAmbient_1 = irradiance_0;
        }
        else
        {
            domeAmbient_1 = sceneEye_0;
        }
        float3 color_3;
        float shadowVisibility_0;
        if(hasSceneLighting_0)
        {
            int shadowSlot_0 = int((float4((&_S6->shadowSlots_0)->data_1[lightIndex_0]) ).x);
            if(shadowSlot_0 >= int(0))
            {
                for(;;)
                {
                    float4 _S232 = float4((&_S6->shadowControls_0)->data_3[shadowSlot_0]) ;
                    float4 _S233 = float4((&_S6->shadowTile_0)->data_3[shadowSlot_0]) ;
                    if((dot(specularColor_0, lightDirection_0)) < 0.0f)
                    {
                        color_3 = - specularColor_0;
                    }
                    else
                    {
                        color_3 = specularColor_0;
                    }
                    float slope_0 = clamp(1.0f - saturate(dot(color_3, lightDirection_0)), 0.0f, 1.0f);
                    float4 lightClip_0 = (((float4(worldPosition_0 + color_3 * float3((_S232.y * slope_0)) , 1.0f)) * (matrix<float,int(4),int(4)> ((&_S6->shadowWorldToLightClip_0)->data_2[shadowSlot_0].data_0[int(0)][int(0)], (&_S6->shadowWorldToLightClip_0)->data_2[shadowSlot_0].data_0[int(0)][int(1)], (&_S6->shadowWorldToLightClip_0)->data_2[shadowSlot_0].data_0[int(0)][int(2)], (&_S6->shadowWorldToLightClip_0)->data_2[shadowSlot_0].data_0[int(0)][int(3)], (&_S6->shadowWorldToLightClip_0)->data_2[shadowSlot_0].data_0[int(1)][int(0)], (&_S6->shadowWorldToLightClip_0)->data_2[shadowSlot_0].data_0[int(1)][int(1)], (&_S6->shadowWorldToLightClip_0)->data_2[shadowSlot_0].data_0[int(1)][int(2)], (&_S6->shadowWorldToLightClip_0)->data_2[shadowSlot_0].data_0[int(1)][int(3)], (&_S6->shadowWorldToLightClip_0)->data_2[shadowSlot_0].data_0[int(2)][int(0)], (&_S6->shadowWorldToLightClip_0)->data_2[shadowSlot_0].data_0[int(2)][int(1)], (&_S6->shadowWorldToLightClip_0)->data_2[shadowSlot_0].data_0[int(2)][int(2)], (&_S6->shadowWorldToLightClip_0)->data_2[shadowSlot_0].data_0[int(2)][int(3)], (&_S6->shadowWorldToLightClip_0)->data_2[shadowSlot_0].data_0[int(3)][int(0)], (&_S6->shadowWorldToLightClip_0)->data_2[shadowSlot_0].data_0[int(3)][int(1)], (&_S6->shadowWorldToLightClip_0)->data_2[shadowSlot_0].data_0[int(3)][int(2)], (&_S6->shadowWorldToLightClip_0)->data_2[shadowSlot_0].data_0[int(3)][int(3)]))));
                    float _S234 = lightClip_0.w;
                    if(_S234 <= 0.0f)
                    {
                        ior_0 = 1.0f;
                        break;
                    }
                    float3 ndc_0 = lightClip_0.xyz / float3(_S234) ;
                    bool _S235;
                    if((abs(ndc_0.x)) > 1.0f)
                    {
                        _S235 = true;
                    }
                    else
                    {
                        _S235 = (abs(ndc_0.y)) > 1.0f;
                    }
                    bool _S236;
                    if(_S235)
                    {
                        _S236 = true;
                    }
                    else
                    {
                        _S236 = (ndc_0.z) < 0.0f;
                    }
                    bool _S237;
                    if(_S236)
                    {
                        _S237 = true;
                    }
                    else
                    {
                        _S237 = (ndc_0.z) > 1.0f;
                    }
                    if(_S237)
                    {
                        ior_0 = 1.0f;
                        break;
                    }
                    float2 _S238 = _S233.xy;
                    float2 _S239 = _S233.zw;
                    float2 _S240 = _S238 + (ndc_0.xy * float2(0.5f, -0.5f) + float2(0.5f, 0.5f)) * _S239;
                    float texel_0 = _S232.w;
                    float _S241 = max(_S232.z, 0.0f);
                    float _S242 = ndc_0.z - _S232.x * (1.0f + 2.0f * slope_0);
                    float2 _S243 = float2((texel_0 * 0.5f)) ;
                    float2 _S244 = _S238 + _S243;
                    float2 _S245 = _S238 + _S239 - _S243;
                    int y_0 = int(-1);
                    shadowVisibility_0 = 0.0f;
                    for(;;)
                    {
                        if(y_0 <= int(1))
                        {
                        }
                        else
                        {
                            break;
                        }
                        int x_0 = int(-1);
                        for(;;)
                        {
                            if(x_0 <= int(1))
                            {
                            }
                            else
                            {
                                break;
                            }
                            if(_S242 <= (((&kernelContext_0)->shadowAtlas_0).sample(((&kernelContext_0)->shadowSampler_0), (clamp(_S240 + float2(float(x_0), float(y_0)) * float2((_S241 * texel_0)) , _S244, _S245)), level((0.0f))).x))
                            {
                                ior_0 = 1.0f;
                            }
                            else
                            {
                                ior_0 = 0.0f;
                            }
                            float lit_0 = shadowVisibility_0 + ior_0;
                            x_0 = x_0 + int(1);
                            shadowVisibility_0 = lit_0;
                        }
                        y_0 = y_0 + int(1);
                    }
                    ior_0 = shadowVisibility_0 * 0.1111111119389534f;
                    break;
                }
                shadowVisibility_0 = ior_0;
            }
            else
            {
                shadowVisibility_0 = 1.0f;
            }
        }
        else
        {
            shadowVisibility_0 = 1.0f;
        }
        thread array<float3, int(5)> sampleOffsets_0;
        float3 _S246 = float3(0.0f, 0.0f, 0.0f);
        sampleOffsets_0[int(0)] = _S246;
        sampleOffsets_0[int(1)] = _S246;
        sampleOffsets_0[int(2)] = _S246;
        sampleOffsets_0[int(3)] = _S246;
        sampleOffsets_0[int(4)] = _S246;
        float sampleCount_0;
        if(lightType_0 == 3.0f)
        {
            float3 halfWidth_0 = lightTangent_0 * float3((shapeX_0 * 0.5f)) ;
            float3 halfHeight_0 = lightBitangent_0 * float3((shapeY_0 * 0.5f)) ;
            sampleOffsets_0[int(1)] = halfWidth_0 + halfHeight_0;
            sampleOffsets_0[int(2)] = halfWidth_0 - halfHeight_0;
            float3 _S247 = - halfWidth_0;
            sampleOffsets_0[int(3)] = _S247 + halfHeight_0;
            sampleOffsets_0[int(4)] = _S247 - halfHeight_0;
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
        sampleIndex_0 = 0U;
        color_3 = color_2;
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
            float sampleIntensity_0 = roughness_0 / sampleCount_0;
            float3 sampleDirection_0;
            float emissionScale_0;
            float sampleIntensity_1;
            if(lightType_0 >= 2.0f)
            {
                float3 toLight_0 = (float4((&_S6->lightPositionType_0)->data_1[lightIndex_0]) ).xyz + sampleOffsets_0[sampleIndex_0] - worldPosition_0;
                float _S248 = max(dot(toLight_0, toLight_0), 0.00100000004749745f);
                float3 sampleDirection_1 = toLight_0 * float3(rsqrt(_S248)) ;
                float sampleIntensity_2 = sampleIntensity_0 / _S248;
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
            float3 half_0 = normalize(sampleDirection_0 + domeAmbient_1);
            float normalDotLight_0 = saturate(dot(normal_2, sampleDirection_0));
            float normalDotHalf_0 = saturate(dot(normal_2, half_0));
            float3 _S249 = float3(pow(max(0.0f, 1.0f - saturate(dot(domeAmbient_1, half_0))), 5.0f)) ;
            float3 _S250 = mix(normalIncidence_0, grazingIncidence_0, _S249);
            float3 directDiffuse_0 = diffuse_1 * (float3(1.0f)  - _S250);
            float _S251 = max(_S201, 0.00100000004749745f);
            float alpha_0 = _S251 * _S251;
            float alphaSquared_0 = alpha_0 * alpha_0;
            float lobeCosineSquared_0 = saturate(normalDotHalf_0 * normalDotHalf_0);
            float lobeComplement_0 = 1.0f - lobeCosineSquared_0;
            float denominator_0 = lobeCosineSquared_0 * alphaSquared_0 + lobeComplement_0;
            float k_0 = alpha_0 * 0.5f;
            float _S252 = 1.0f - k_0;
            float3 _S253 = float3(max(4.0f * normalDotLight_0 * _S200, 1.00000000317107685e-30f)) ;
            float3 _S254 = _S250 * float3((_S200 / (_S200 * _S252 + k_0) * (normalDotLight_0 / (normalDotLight_0 * _S252 + k_0))))  * float3((alphaSquared_0 / max(3.14159274101257324f * denominator_0 * denominator_0, 1.00000000317107685e-30f)))  / _S253;
            float3 directSpecular_0;
            if(clearcoatAmount_0 > 0.0f)
            {
                float _S255 = max(_S202, 0.00100000004749745f);
                float alpha_1 = _S255 * _S255;
                float alphaSquared_1 = alpha_1 * alpha_1;
                float denominator_1 = lobeCosineSquared_0 * alphaSquared_1 + lobeComplement_0;
                float k_1 = alpha_1 * 0.5f;
                float _S256 = 1.0f - k_1;
                directSpecular_0 = _S254 + float3(clearcoatAmount_0)  * (mix(float3((reflectanceRatio_0 * reflectanceRatio_0)) , float3(1.0f, 1.0f, 1.0f), _S249) * float3((_S200 / (_S200 * _S256 + k_1) * (normalDotLight_0 / (normalDotLight_0 * _S256 + k_1))))  * float3((alphaSquared_1 / max(3.14159274101257324f * denominator_1 * denominator_1, 1.00000000317107685e-30f)))  / _S253);
            }
            else
            {
                directSpecular_0 = _S254;
            }
            float3 _S257 = diffuseColor_0 * float3(sampleIntensity_1) ;
            color_3 = color_3 + float3((shadowVisibility_0 * occlusion_0 * emissionScale_0 * normalDotLight_0))  * (directDiffuse_0 * float3(metallic_0)  * (_S257 * _S203) + directSpecular_0 * float3(clearcoatRoughness_0)  * _S257);
            sampleIndex_0 = sampleIndex_0 + 1U;
        }
        color_2 = color_3;
        lightIndex_0 = lightIndex_0 + 1U;
    }
    float4 _S258 = float4(_S6->environmentControls_0) ;
    if((_S258.x) >= 0.5f)
    {
        float _S259 = saturate(saturate(abs(dot(worldNormal_2, sceneEye_0)) + 0.00000999999974738f));
        float _S260 = saturate(_S201);
        float2 _S261 = (((&kernelContext_0)->environmentBrdf_0).sample(((&kernelContext_0)->environmentBrdfSampler_0), (float2(_S259, _S260)), level((0.0f)))).xy;
        float3 specularWeight_0 = normalIncidence_0 * float3(_S261.x)  + grazingIncidence_0 * float3(_S261.y) ;
        float3 diffuseWeight_0 = saturate(float3(1.0f, 1.0f, 1.0f) - specularWeight_0);
        float3 reflectionDirection_0 = reflect(- sceneEye_0, worldNormal_2);
        float _S262 = max(_S212.y, 1.0f);
        float3 _S263 = float3(0.0f, 0.0f, 0.0f);
        if(allDomesLinked_0)
        {
            hasSceneLighting_0 = true;
        }
        else
        {
            hasSceneLighting_0 = _S262 <= 1.0f;
        }
        if(hasSceneLighting_0)
        {
            float composedGroup_0 = _S212.z;
            float _S264 = _S212.w;
            for(;;)
            {
                bool _S265 = _S262 <= 1.0f;
                _S3 = _S265;
                if(_S265)
                {
                    float3 unit_0 = normalize(worldNormal_2);
                    diffuseColor_0 = (((&kernelContext_0)->environmentIrradiance_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(fract((atan2(unit_0.z, unit_0.x) + 1.57079637050628662f) / 6.28318548202514648f), acos(clamp(unit_0.y, -1.0f, 1.0f)) / 3.14159274101257324f)), level((0.0f)))).xyz;
                    break;
                }
                float3 unit_1 = normalize(worldNormal_2);
                float inset_0 = 0.5f / max(_S264, 1.0f);
                diffuseColor_0 = (((&kernelContext_0)->environmentIrradiance_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(fract((atan2(unit_1.z, unit_1.x) + 1.57079637050628662f) / 6.28318548202514648f), (composedGroup_0 + clamp(acos(clamp(unit_1.y, -1.0f, 1.0f)) / 3.14159274101257324f, inset_0, 1.0f - inset_0)) / _S262)), level((0.0f)))).xyz;
                break;
            }
            float _S266 = _S258.y;
            float _S267 = _S258.z;
            for(;;)
            {
                if(_S3)
                {
                    float3 unit_2 = normalize(reflectionDirection_0);
                    float u_0 = fract((atan2(unit_2.z, unit_2.x) + 1.57079637050628662f) / 6.28318548202514648f);
                    float _S268 = max(_S266, 1.0f);
                    float _S269 = _S268 - 1.0f;
                    float slice_0 = _S260 * max(_S269, 0.0f);
                    float lower_0 = floor(slice_0);
                    float inset_1 = 0.5f / max(_S267, 1.0f);
                    float v_0 = clamp(acos(clamp(unit_2.y, -1.0f, 1.0f)) / 3.14159274101257324f, inset_1, 1.0f - inset_1);
                    specularColor_0 = mix((((&kernelContext_0)->environmentSpecular_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(u_0, (lower_0 + v_0) / _S268)), level((0.0f)))).xyz, (((&kernelContext_0)->environmentSpecular_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(u_0, (min(lower_0 + 1.0f, _S269) + v_0) / _S268)), level((0.0f)))).xyz, float3((slice_0 - lower_0)) );
                    break;
                }
                float3 unit_3 = normalize(reflectionDirection_0);
                float u_1 = fract((atan2(unit_3.z, unit_3.x) + 1.57079637050628662f) / 6.28318548202514648f);
                float _S270 = max(_S266, 1.0f);
                float total_0 = _S270 * _S262;
                float _S271 = _S270 - 1.0f;
                float slice_1 = _S260 * max(_S271, 0.0f);
                float lower_1 = floor(slice_1);
                float inset_2 = 0.5f / max(_S267, 1.0f);
                float v_1 = clamp(acos(clamp(unit_3.y, -1.0f, 1.0f)) / 3.14159274101257324f, inset_2, 1.0f - inset_2);
                float base_0 = composedGroup_0 * _S270;
                specularColor_0 = mix((((&kernelContext_0)->environmentSpecular_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(u_1, (base_0 + lower_1 + v_1) / total_0)), level((0.0f)))).xyz, (((&kernelContext_0)->environmentSpecular_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(u_1, (base_0 + min(lower_1 + 1.0f, _S271) + v_1) / total_0)), level((0.0f)))).xyz, float3((slice_1 - lower_1)) );
                break;
            }
            if(clearcoatAmount_0 > 0.0f)
            {
                for(;;)
                {
                    if(_S3)
                    {
                        float3 unit_4 = normalize(reflectionDirection_0);
                        float u_2 = fract((atan2(unit_4.z, unit_4.x) + 1.57079637050628662f) / 6.28318548202514648f);
                        float _S272 = max(_S266, 1.0f);
                        float _S273 = _S272 - 1.0f;
                        float slice_2 = saturate(_S202) * max(_S273, 0.0f);
                        float lower_2 = floor(slice_2);
                        float inset_3 = 0.5f / max(_S267, 1.0f);
                        float v_2 = clamp(acos(clamp(unit_4.y, -1.0f, 1.0f)) / 3.14159274101257324f, inset_3, 1.0f - inset_3);
                        irradiance_0 = mix((((&kernelContext_0)->environmentSpecular_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(u_2, (lower_2 + v_2) / _S272)), level((0.0f)))).xyz, (((&kernelContext_0)->environmentSpecular_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(u_2, (min(lower_2 + 1.0f, _S273) + v_2) / _S272)), level((0.0f)))).xyz, float3((slice_2 - lower_2)) );
                        break;
                    }
                    float3 unit_5 = normalize(reflectionDirection_0);
                    float u_3 = fract((atan2(unit_5.z, unit_5.x) + 1.57079637050628662f) / 6.28318548202514648f);
                    float _S274 = max(_S266, 1.0f);
                    float total_1 = _S274 * _S262;
                    float _S275 = _S274 - 1.0f;
                    float slice_3 = saturate(_S202) * max(_S275, 0.0f);
                    float lower_3 = floor(slice_3);
                    float inset_4 = 0.5f / max(_S267, 1.0f);
                    float v_3 = clamp(acos(clamp(unit_5.y, -1.0f, 1.0f)) / 3.14159274101257324f, inset_4, 1.0f - inset_4);
                    float base_1 = composedGroup_0 * _S274;
                    irradiance_0 = mix((((&kernelContext_0)->environmentSpecular_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(u_3, (base_1 + lower_3 + v_3) / total_1)), level((0.0f)))).xyz, (((&kernelContext_0)->environmentSpecular_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(u_3, (base_1 + min(lower_3 + 1.0f, _S275) + v_3) / total_1)), level((0.0f)))).xyz, float3((slice_3 - lower_3)) );
                    break;
                }
                lightTangent_0 = irradiance_0;
            }
            else
            {
                lightTangent_0 = _S263;
            }
            irradiance_0 = diffuseColor_0;
            lightDirection_0 = specularColor_0;
        }
        else
        {
            sampleIndex_0 = 0U;
            irradiance_0 = _S263;
            lightDirection_0 = _S263;
            lightTangent_0 = _S263;
            for(;;)
            {
                if(sampleIndex_0 < _S213)
                {
                }
                else
                {
                    break;
                }
                if((domeLinkMask_0 & (1U << sampleIndex_0)) == 0U)
                {
                    sampleIndex_0 = sampleIndex_0 + 1U;
                    continue;
                }
                float domeGroup_0 = (float4((&_S6->domeEnvironment_0)->data_1[sampleIndex_0]) ).x;
                if(domeGroup_0 < 0.0f)
                {
                    sampleIndex_0 = sampleIndex_0 + 1U;
                    continue;
                }
                float _S276 = _S212.w;
                for(;;)
                {
                    bool _S277 = _S262 <= 1.0f;
                    _S4 = _S277;
                    if(_S277)
                    {
                        float3 unit_6 = normalize(worldNormal_2);
                        diffuseColor_0 = (((&kernelContext_0)->environmentIrradiance_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(fract((atan2(unit_6.z, unit_6.x) + 1.57079637050628662f) / 6.28318548202514648f), acos(clamp(unit_6.y, -1.0f, 1.0f)) / 3.14159274101257324f)), level((0.0f)))).xyz;
                        break;
                    }
                    float3 unit_7 = normalize(worldNormal_2);
                    float inset_5 = 0.5f / max(_S276, 1.0f);
                    diffuseColor_0 = (((&kernelContext_0)->environmentIrradiance_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(fract((atan2(unit_7.z, unit_7.x) + 1.57079637050628662f) / 6.28318548202514648f), (domeGroup_0 + clamp(acos(clamp(unit_7.y, -1.0f, 1.0f)) / 3.14159274101257324f, inset_5, 1.0f - inset_5)) / _S262)), level((0.0f)))).xyz;
                    break;
                }
                float3 irradiance_1 = irradiance_0 + diffuseColor_0;
                float _S278 = _S258.y;
                float _S279 = _S258.z;
                for(;;)
                {
                    if(_S4)
                    {
                        float3 unit_8 = normalize(reflectionDirection_0);
                        float u_4 = fract((atan2(unit_8.z, unit_8.x) + 1.57079637050628662f) / 6.28318548202514648f);
                        float _S280 = max(_S278, 1.0f);
                        float _S281 = _S280 - 1.0f;
                        float slice_4 = _S260 * max(_S281, 0.0f);
                        float lower_4 = floor(slice_4);
                        float inset_6 = 0.5f / max(_S279, 1.0f);
                        float v_4 = clamp(acos(clamp(unit_8.y, -1.0f, 1.0f)) / 3.14159274101257324f, inset_6, 1.0f - inset_6);
                        specularColor_0 = mix((((&kernelContext_0)->environmentSpecular_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(u_4, (lower_4 + v_4) / _S280)), level((0.0f)))).xyz, (((&kernelContext_0)->environmentSpecular_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(u_4, (min(lower_4 + 1.0f, _S281) + v_4) / _S280)), level((0.0f)))).xyz, float3((slice_4 - lower_4)) );
                        break;
                    }
                    float3 unit_9 = normalize(reflectionDirection_0);
                    float u_5 = fract((atan2(unit_9.z, unit_9.x) + 1.57079637050628662f) / 6.28318548202514648f);
                    float _S282 = max(_S278, 1.0f);
                    float total_2 = _S282 * _S262;
                    float _S283 = _S282 - 1.0f;
                    float slice_5 = _S260 * max(_S283, 0.0f);
                    float lower_5 = floor(slice_5);
                    float inset_7 = 0.5f / max(_S279, 1.0f);
                    float v_5 = clamp(acos(clamp(unit_9.y, -1.0f, 1.0f)) / 3.14159274101257324f, inset_7, 1.0f - inset_7);
                    float base_2 = domeGroup_0 * _S282;
                    specularColor_0 = mix((((&kernelContext_0)->environmentSpecular_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(u_5, (base_2 + lower_5 + v_5) / total_2)), level((0.0f)))).xyz, (((&kernelContext_0)->environmentSpecular_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(u_5, (base_2 + min(lower_5 + 1.0f, _S283) + v_5) / total_2)), level((0.0f)))).xyz, float3((slice_5 - lower_5)) );
                    break;
                }
                float3 prefiltered_0 = lightDirection_0 + specularColor_0;
                if(clearcoatAmount_0 > 0.0f)
                {
                    for(;;)
                    {
                        if(_S4)
                        {
                            float3 unit_10 = normalize(reflectionDirection_0);
                            float u_6 = fract((atan2(unit_10.z, unit_10.x) + 1.57079637050628662f) / 6.28318548202514648f);
                            float _S284 = max(_S278, 1.0f);
                            float _S285 = _S284 - 1.0f;
                            float slice_6 = saturate(_S202) * max(_S285, 0.0f);
                            float lower_6 = floor(slice_6);
                            float inset_8 = 0.5f / max(_S279, 1.0f);
                            float v_6 = clamp(acos(clamp(unit_10.y, -1.0f, 1.0f)) / 3.14159274101257324f, inset_8, 1.0f - inset_8);
                            normal_2 = mix((((&kernelContext_0)->environmentSpecular_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(u_6, (lower_6 + v_6) / _S284)), level((0.0f)))).xyz, (((&kernelContext_0)->environmentSpecular_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(u_6, (min(lower_6 + 1.0f, _S285) + v_6) / _S284)), level((0.0f)))).xyz, float3((slice_6 - lower_6)) );
                            break;
                        }
                        float3 unit_11 = normalize(reflectionDirection_0);
                        float u_7 = fract((atan2(unit_11.z, unit_11.x) + 1.57079637050628662f) / 6.28318548202514648f);
                        float _S286 = max(_S278, 1.0f);
                        float total_3 = _S286 * _S262;
                        float _S287 = _S286 - 1.0f;
                        float slice_7 = saturate(_S202) * max(_S287, 0.0f);
                        float lower_7 = floor(slice_7);
                        float inset_9 = 0.5f / max(_S279, 1.0f);
                        float v_7 = clamp(acos(clamp(unit_11.y, -1.0f, 1.0f)) / 3.14159274101257324f, inset_9, 1.0f - inset_9);
                        float base_3 = domeGroup_0 * _S286;
                        normal_2 = mix((((&kernelContext_0)->environmentSpecular_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(u_7, (base_3 + lower_7 + v_7) / total_3)), level((0.0f)))).xyz, (((&kernelContext_0)->environmentSpecular_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(u_7, (base_3 + min(lower_7 + 1.0f, _S287) + v_7) / total_3)), level((0.0f)))).xyz, float3((slice_7 - lower_7)) );
                        break;
                    }
                    lightBitangent_0 = lightTangent_0 + normal_2;
                }
                else
                {
                    lightBitangent_0 = lightTangent_0;
                }
                irradiance_0 = irradiance_1;
                lightDirection_0 = prefiltered_0;
                lightTangent_0 = lightBitangent_0;
                sampleIndex_0 = sampleIndex_0 + 1U;
            }
        }
        float3 color_4 = color_2 + float3(occlusion_0)  * diffuse_1 * irradiance_0 * diffuseWeight_0 + float3(occlusion_0)  * lightDirection_0 * specularWeight_0;
        if(clearcoatAmount_0 > 0.0f)
        {
            float2 _S288 = (((&kernelContext_0)->environmentBrdf_0).sample(((&kernelContext_0)->environmentBrdfSampler_0), (float2(_S259, saturate(_S202))), level((0.0f)))).xy;
            color_2 = color_4 + float3((occlusion_0 * clearcoatAmount_0))  * lightTangent_0 * (float3((reflectanceRatio_0 * reflectanceRatio_0))  * float3(_S288.x)  + float3(_S288.y) );
        }
        else
        {
            color_2 = color_4;
        }
    }
    float3 color_5 = (color_2 + unlitColor_0) * float3(exp2((as_type<float>((_S2.z))))) ;
    if((_S2.y) == 1U)
    {
        color_2 = color_5 / (float3(1.0f)  + max(color_5, float3(0.0f, 0.0f, 0.0f)));
    }
    else
    {
        color_2 = color_5;
    }
    pixelOutput_0 _S289 = { float4(color_2, opacity_0) };
    return _S289;
}
