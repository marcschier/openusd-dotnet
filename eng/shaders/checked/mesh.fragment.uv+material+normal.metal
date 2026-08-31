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
    texture2d<float, access::sample> baseColorTexture_0;
    sampler baseColorSampler_0;
    texture2d<float, access::sample> compositeTexture_0;
    sampler compositeSampler_0;
    texture2d<float, access::sample> normalTexture_0;
    sampler normalSampler_0;
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
};

[[fragment]] pixelOutput_0 fragmentMain_uv_material_normal(pixelInput_0 _S1 [[stage_in]], bool isFrontFace_0 [[front_facing]], float4 position_0 [[position]], SurfaceParameters_natural_0 device* surfaceParameters_1 [[buffer(7)]], FrameParameters_natural_0 device* frameParameters_1 [[buffer(8)]], texture2d<float, access::sample> baseColorTexture_1 [[texture(0)]], sampler baseColorSampler_1 [[sampler(0)]], texture2d<float, access::sample> compositeTexture_1 [[texture(15)]], sampler compositeSampler_1 [[sampler(12)]], texture2d<float, access::sample> normalTexture_1 [[texture(1)]], sampler normalSampler_1 [[sampler(1)]], texture2d<float, access::sample> roughnessMetallicTexture_1 [[texture(2)]], sampler roughnessMetallicSampler_1 [[sampler(2)]], texture2d<float, access::sample> metallicTexture_1 [[texture(4)]], sampler metallicSampler_1 [[sampler(5)]], texture2d<float, access::sample> emissiveTexture_1 [[texture(3)]], sampler emissiveSampler_1 [[sampler(3)]], texture2d<float, access::sample> opacityTexture_1 [[texture(5)]], sampler opacitySampler_1 [[sampler(6)]], texture2d<float, access::sample> occlusionTexture_1 [[texture(10)]], sampler occlusionSampler_1 [[sampler(7)]], texture2d<float, access::sample> specularColorTexture_1 [[texture(11)]], sampler specularColorSampler_1 [[sampler(8)]], texture2d<float, access::sample> clearcoatTexture_1 [[texture(12)]], sampler clearcoatSampler_1 [[sampler(9)]], texture2d<float, access::sample> clearcoatRoughnessTexture_1 [[texture(13)]], sampler clearcoatRoughnessSampler_1 [[sampler(10)]], texture2d<float, access::sample> iorTexture_1 [[texture(14)]], sampler iorSampler_1 [[sampler(11)]])
{
    uint4 _S2;
    thread KernelContext_0 kernelContext_0;
    (&kernelContext_0)->surfaceParameters_0 = surfaceParameters_1;
    (&kernelContext_0)->frameParameters_0 = frameParameters_1;
    (&kernelContext_0)->baseColorTexture_0 = baseColorTexture_1;
    (&kernelContext_0)->baseColorSampler_0 = baseColorSampler_1;
    (&kernelContext_0)->compositeTexture_0 = compositeTexture_1;
    (&kernelContext_0)->compositeSampler_0 = compositeSampler_1;
    (&kernelContext_0)->normalTexture_0 = normalTexture_1;
    (&kernelContext_0)->normalSampler_0 = normalSampler_1;
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
    SurfaceParameters_natural_0 device* _S3 = surfaceParameters_1+int(0);
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
    float4 _S8 = float4(_S3->clearcoatShaded_0) ;
    bool shaded_0 = (_S8.z) >= 0.5f;
    float3 diffuseColor_0;
    if(shaded_0)
    {
        diffuseColor_0 = (float4(_S3->diffuseOpacity_0) ).xyz;
    }
    else
    {
        diffuseColor_0 = _S1.tint_0.xyz;
    }
    float opacity_0;
    if(shaded_0)
    {
        opacity_0 = (float4(_S3->diffuseOpacity_0) ).w;
    }
    else
    {
        opacity_0 = _S1.tint_0.w;
    }
    float4 _S9 = float4(_S3->emissiveOcclusion_0) ;
    float3 _S10 = _S9.xyz;
    float _S11 = _S9.w;
    float4 _S12 = float4(_S3->reserved_0) ;
    if((_S12.x) >= 0.5f)
    {
        pixelOutput_0 _S13 = { float4(diffuseColor_0 * float3((1.0f - exp(- max(0.0f, _S12.y) * max(0.0f, _S12.z)))) , 1.0f) };
        return _S13;
    }
    float4 _S14 = float4(_S3->metallicRoughnessThresholdWorkflow_0) ;
    float _S15 = saturate(_S14.x);
    float _S16 = clamp(_S14.y, 0.00999999977648258f, 1.0f);
    float4 _S17 = float4(_S3->specularIor_0) ;
    float3 _S18 = _S17.xyz;
    float _S19 = _S17.w;
    float _S20 = _S8.x;
    float _S21 = _S8.y;
    float4 _S22 = float4(_S3->textureControls_0) ;
    uint textureMask_0 = uint(round(_S22.x));
    uint udimMask_0 = uint(round(_S22.y));
    float4 _S23 = float4(_S3->uvTransformRow0_0) ;
    float4 _S24 = float4(_S3->uvTransformRow1_0) ;
    float2 _S25 = float2(dot(_S23.xy, _S1.texCoord_0) + _S23.z, dot(_S24.xy, _S1.texCoord_0) + _S24.z);
    bool hasSceneLighting_0;
    float4 _S26;
    float4 _S27;
    if((textureMask_0 & 2U) != 0U)
    {
        bool _S28 = (udimMask_0 & 2U) != 0U;
        for(;;)
        {
            if(!_S28)
            {
                _S26 = (((&kernelContext_0)->baseColorTexture_0).sample(((&kernelContext_0)->baseColorSampler_0), (_S25)));
                break;
            }
            texture2d<float, access::sample> _S29 = (&kernelContext_0)->baseColorTexture_0;
            thread uint atlasWidth_0;
            thread uint atlasHeight_0;
            (*((&atlasWidth_0)) = (_S29).get_width(0)),(*((&atlasHeight_0)) = (_S29).get_height(0));
            int3 _S30 = int3(int(0), int(0), int(0));
            float4 metadata_0 = round((((&kernelContext_0)->baseColorTexture_0).read(vec<uint,2>(((_S30)).xy), uint(((_S30)).z))) * float4(255.0f) );
            int2 _S31 = int2(metadata_0.zw);
            int2 tile_0 = int2(floor(_S25)) - int2(metadata_0.xy);
            if(any(tile_0 < (int2(int(0)) )))
            {
                hasSceneLighting_0 = true;
            }
            else
            {
                hasSceneLighting_0 = any(tile_0 >= _S31);
            }
            if(hasSceneLighting_0)
            {
                int3 _S32 = int3(int(min(1U, atlasWidth_0 - 1U)), int(0), int(0));
                _S26 = (((&kernelContext_0)->baseColorTexture_0).read(vec<uint,2>(((_S32)).xy), uint(((_S32)).z)));
                break;
            }
            uint _S33 = atlasWidth_0 / uint(_S31.x);
            float _S34 = float(_S33);
            uint _S35 = (atlasHeight_0 - 1U) / uint(_S31.y);
            float2 cellSize_0 = float2(_S34, float(_S35));
            _S26 = (((&kernelContext_0)->baseColorTexture_0).sample(((&kernelContext_0)->baseColorSampler_0), ((float2(tile_0) * cellSize_0 + float2(1.5f, 2.5f) + fract(_S25) * max(cellSize_0 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_0), float(atlasHeight_0)))));
            break;
        }
        for(;;)
        {
            float4 _S36 = float4(_S3->compositeControls_0) ;
            if((_S36.x) != 2.0f)
            {
                break;
            }
            bool _S37 = (_S36.w) >= 0.5f;
            for(;;)
            {
                if(!_S37)
                {
                    _S27 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), (_S25)));
                    break;
                }
                texture2d<float, access::sample> _S38 = (&kernelContext_0)->compositeTexture_0;
                thread uint atlasWidth_1;
                thread uint atlasHeight_1;
                (*((&atlasWidth_1)) = (_S38).get_width(0)),(*((&atlasHeight_1)) = (_S38).get_height(0));
                int3 _S39 = int3(int(0), int(0), int(0));
                float4 metadata_1 = round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S39)).xy), uint(((_S39)).z))) * float4(255.0f) );
                int2 _S40 = int2(metadata_1.zw);
                int2 tile_1 = int2(floor(_S25)) - int2(metadata_1.xy);
                if(any(tile_1 < (int2(int(0)) )))
                {
                    hasSceneLighting_0 = true;
                }
                else
                {
                    hasSceneLighting_0 = any(tile_1 >= _S40);
                }
                if(hasSceneLighting_0)
                {
                    int3 _S41 = int3(int(min(1U, atlasWidth_1 - 1U)), int(0), int(0));
                    _S27 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S41)).xy), uint(((_S41)).z)));
                    break;
                }
                uint _S42 = atlasWidth_1 / uint(_S40.x);
                float _S43 = float(_S42);
                uint _S44 = (atlasHeight_1 - 1U) / uint(_S40.y);
                float2 cellSize_1 = float2(_S43, float(_S44));
                _S27 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), ((float2(tile_1) * cellSize_1 + float2(1.5f, 2.5f) + fract(_S25) * max(cellSize_1 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_1), float(atlasHeight_1)))));
                break;
            }
            uint operation_0 = uint(round(_S36.y));
            if(operation_0 == 1U)
            {
                _S26 = _S26 * _S27;
                break;
            }
            if(operation_0 == 2U)
            {
                _S26 = _S26 + _S27;
                break;
            }
            if(operation_0 == 3U)
            {
                _S26 = _S26 - _S27;
                break;
            }
            if(operation_0 == 4U)
            {
                float factor_0 = _S36.z;
                _S26 = _S26 * float4((1.0f - factor_0))  + _S27 * float4(factor_0) ;
                break;
            }
            break;
        }
        diffuseColor_0 = _S26.xyz;
    }
    bool _S45 = (udimMask_0 & 4U) != 0U;
    for(;;)
    {
        if(!_S45)
        {
            _S26 = (((&kernelContext_0)->normalTexture_0).sample(((&kernelContext_0)->normalSampler_0), (_S25)));
            break;
        }
        texture2d<float, access::sample> _S46 = (&kernelContext_0)->normalTexture_0;
        thread uint atlasWidth_2;
        thread uint atlasHeight_2;
        (*((&atlasWidth_2)) = (_S46).get_width(0)),(*((&atlasHeight_2)) = (_S46).get_height(0));
        int3 _S47 = int3(int(0), int(0), int(0));
        float4 metadata_2 = round((((&kernelContext_0)->normalTexture_0).read(vec<uint,2>(((_S47)).xy), uint(((_S47)).z))) * float4(255.0f) );
        int2 _S48 = int2(metadata_2.zw);
        int2 tile_2 = int2(floor(_S25)) - int2(metadata_2.xy);
        if(any(tile_2 < (int2(int(0)) )))
        {
            hasSceneLighting_0 = true;
        }
        else
        {
            hasSceneLighting_0 = any(tile_2 >= _S48);
        }
        if(hasSceneLighting_0)
        {
            int3 _S49 = int3(int(min(1U, atlasWidth_2 - 1U)), int(0), int(0));
            _S26 = (((&kernelContext_0)->normalTexture_0).read(vec<uint,2>(((_S49)).xy), uint(((_S49)).z)));
            break;
        }
        uint _S50 = atlasWidth_2 / uint(_S48.x);
        float _S51 = float(_S50);
        uint _S52 = (atlasHeight_2 - 1U) / uint(_S48.y);
        float2 cellSize_2 = float2(_S51, float(_S52));
        _S26 = (((&kernelContext_0)->normalTexture_0).sample(((&kernelContext_0)->normalSampler_0), ((float2(tile_2) * cellSize_2 + float2(1.5f, 2.5f) + fract(_S25) * max(cellSize_2 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_2), float(atlasHeight_2)))));
        break;
    }
    float3 _S53 = float3(1.0f) ;
    float3 sampledNormal_0 = _S26.xyz * float3(2.0f)  - _S53;
    float3 tangent_1 = normalize(_S1.tangent_0.xyz);
    float3 shadingNormal_0 = normalize(tangent_1 * float3(sampledNormal_0.x)  + cross(normalize(_S1.normal_0), tangent_1) * float3(_S1.tangent_0.w)  * float3(sampledNormal_0.y)  + _S1.normal_0 * float3(sampledNormal_0.z) );
    float roughness_0;
    if((textureMask_0 & 8U) != 0U)
    {
        bool _S54 = (udimMask_0 & 8U) != 0U;
        for(;;)
        {
            if(!_S54)
            {
                _S26 = (((&kernelContext_0)->roughnessMetallicTexture_0).sample(((&kernelContext_0)->roughnessMetallicSampler_0), (_S25)));
                break;
            }
            texture2d<float, access::sample> _S55 = (&kernelContext_0)->roughnessMetallicTexture_0;
            thread uint atlasWidth_3;
            thread uint atlasHeight_3;
            (*((&atlasWidth_3)) = (_S55).get_width(0)),(*((&atlasHeight_3)) = (_S55).get_height(0));
            int3 _S56 = int3(int(0), int(0), int(0));
            float4 metadata_3 = round((((&kernelContext_0)->roughnessMetallicTexture_0).read(vec<uint,2>(((_S56)).xy), uint(((_S56)).z))) * float4(255.0f) );
            int2 _S57 = int2(metadata_3.zw);
            int2 tile_3 = int2(floor(_S25)) - int2(metadata_3.xy);
            if(any(tile_3 < (int2(int(0)) )))
            {
                hasSceneLighting_0 = true;
            }
            else
            {
                hasSceneLighting_0 = any(tile_3 >= _S57);
            }
            if(hasSceneLighting_0)
            {
                int3 _S58 = int3(int(min(1U, atlasWidth_3 - 1U)), int(0), int(0));
                _S26 = (((&kernelContext_0)->roughnessMetallicTexture_0).read(vec<uint,2>(((_S58)).xy), uint(((_S58)).z)));
                break;
            }
            uint _S59 = atlasWidth_3 / uint(_S57.x);
            float _S60 = float(_S59);
            uint _S61 = (atlasHeight_3 - 1U) / uint(_S57.y);
            float2 cellSize_3 = float2(_S60, float(_S61));
            _S26 = (((&kernelContext_0)->roughnessMetallicTexture_0).sample(((&kernelContext_0)->roughnessMetallicSampler_0), ((float2(tile_3) * cellSize_3 + float2(1.5f, 2.5f) + fract(_S25) * max(cellSize_3 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_3), float(atlasHeight_3)))));
            break;
        }
        for(;;)
        {
            float4 _S62 = float4(_S3->compositeControls_0) ;
            if((_S62.x) != 8.0f)
            {
                break;
            }
            bool _S63 = (_S62.w) >= 0.5f;
            for(;;)
            {
                if(!_S63)
                {
                    _S27 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), (_S25)));
                    break;
                }
                texture2d<float, access::sample> _S64 = (&kernelContext_0)->compositeTexture_0;
                thread uint atlasWidth_4;
                thread uint atlasHeight_4;
                (*((&atlasWidth_4)) = (_S64).get_width(0)),(*((&atlasHeight_4)) = (_S64).get_height(0));
                int3 _S65 = int3(int(0), int(0), int(0));
                float4 metadata_4 = round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S65)).xy), uint(((_S65)).z))) * float4(255.0f) );
                int2 _S66 = int2(metadata_4.zw);
                int2 tile_4 = int2(floor(_S25)) - int2(metadata_4.xy);
                if(any(tile_4 < (int2(int(0)) )))
                {
                    hasSceneLighting_0 = true;
                }
                else
                {
                    hasSceneLighting_0 = any(tile_4 >= _S66);
                }
                if(hasSceneLighting_0)
                {
                    int3 _S67 = int3(int(min(1U, atlasWidth_4 - 1U)), int(0), int(0));
                    _S27 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S67)).xy), uint(((_S67)).z)));
                    break;
                }
                uint _S68 = atlasWidth_4 / uint(_S66.x);
                float _S69 = float(_S68);
                uint _S70 = (atlasHeight_4 - 1U) / uint(_S66.y);
                float2 cellSize_4 = float2(_S69, float(_S70));
                _S27 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), ((float2(tile_4) * cellSize_4 + float2(1.5f, 2.5f) + fract(_S25) * max(cellSize_4 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_4), float(atlasHeight_4)))));
                break;
            }
            uint operation_1 = uint(round(_S62.y));
            if(operation_1 == 1U)
            {
                _S26 = _S26 * _S27;
                break;
            }
            if(operation_1 == 2U)
            {
                _S26 = _S26 + _S27;
                break;
            }
            if(operation_1 == 3U)
            {
                _S26 = _S26 - _S27;
                break;
            }
            if(operation_1 == 4U)
            {
                float factor_1 = _S62.z;
                _S26 = _S26 * float4((1.0f - factor_1))  + _S27 * float4(factor_1) ;
                break;
            }
            break;
        }
        roughness_0 = clamp(_S26.x, 0.00999999977648258f, 1.0f);
    }
    else
    {
        roughness_0 = _S16;
    }
    float metallic_0;
    if((textureMask_0 & 32U) != 0U)
    {
        bool _S71 = (udimMask_0 & 32U) != 0U;
        for(;;)
        {
            if(!_S71)
            {
                _S26 = (((&kernelContext_0)->metallicTexture_0).sample(((&kernelContext_0)->metallicSampler_0), (_S25)));
                break;
            }
            texture2d<float, access::sample> _S72 = (&kernelContext_0)->metallicTexture_0;
            thread uint atlasWidth_5;
            thread uint atlasHeight_5;
            (*((&atlasWidth_5)) = (_S72).get_width(0)),(*((&atlasHeight_5)) = (_S72).get_height(0));
            int3 _S73 = int3(int(0), int(0), int(0));
            float4 metadata_5 = round((((&kernelContext_0)->metallicTexture_0).read(vec<uint,2>(((_S73)).xy), uint(((_S73)).z))) * float4(255.0f) );
            int2 _S74 = int2(metadata_5.zw);
            int2 tile_5 = int2(floor(_S25)) - int2(metadata_5.xy);
            if(any(tile_5 < (int2(int(0)) )))
            {
                hasSceneLighting_0 = true;
            }
            else
            {
                hasSceneLighting_0 = any(tile_5 >= _S74);
            }
            if(hasSceneLighting_0)
            {
                int3 _S75 = int3(int(min(1U, atlasWidth_5 - 1U)), int(0), int(0));
                _S26 = (((&kernelContext_0)->metallicTexture_0).read(vec<uint,2>(((_S75)).xy), uint(((_S75)).z)));
                break;
            }
            uint _S76 = atlasWidth_5 / uint(_S74.x);
            float _S77 = float(_S76);
            uint _S78 = (atlasHeight_5 - 1U) / uint(_S74.y);
            float2 cellSize_5 = float2(_S77, float(_S78));
            _S26 = (((&kernelContext_0)->metallicTexture_0).sample(((&kernelContext_0)->metallicSampler_0), ((float2(tile_5) * cellSize_5 + float2(1.5f, 2.5f) + fract(_S25) * max(cellSize_5 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_5), float(atlasHeight_5)))));
            break;
        }
        for(;;)
        {
            float4 _S79 = float4(_S3->compositeControls_0) ;
            if((_S79.x) != 32.0f)
            {
                break;
            }
            bool _S80 = (_S79.w) >= 0.5f;
            for(;;)
            {
                if(!_S80)
                {
                    _S27 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), (_S25)));
                    break;
                }
                texture2d<float, access::sample> _S81 = (&kernelContext_0)->compositeTexture_0;
                thread uint atlasWidth_6;
                thread uint atlasHeight_6;
                (*((&atlasWidth_6)) = (_S81).get_width(0)),(*((&atlasHeight_6)) = (_S81).get_height(0));
                int3 _S82 = int3(int(0), int(0), int(0));
                float4 metadata_6 = round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S82)).xy), uint(((_S82)).z))) * float4(255.0f) );
                int2 _S83 = int2(metadata_6.zw);
                int2 tile_6 = int2(floor(_S25)) - int2(metadata_6.xy);
                if(any(tile_6 < (int2(int(0)) )))
                {
                    hasSceneLighting_0 = true;
                }
                else
                {
                    hasSceneLighting_0 = any(tile_6 >= _S83);
                }
                if(hasSceneLighting_0)
                {
                    int3 _S84 = int3(int(min(1U, atlasWidth_6 - 1U)), int(0), int(0));
                    _S27 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S84)).xy), uint(((_S84)).z)));
                    break;
                }
                uint _S85 = atlasWidth_6 / uint(_S83.x);
                float _S86 = float(_S85);
                uint _S87 = (atlasHeight_6 - 1U) / uint(_S83.y);
                float2 cellSize_6 = float2(_S86, float(_S87));
                _S27 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), ((float2(tile_6) * cellSize_6 + float2(1.5f, 2.5f) + fract(_S25) * max(cellSize_6 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_6), float(atlasHeight_6)))));
                break;
            }
            uint operation_2 = uint(round(_S79.y));
            if(operation_2 == 1U)
            {
                _S26 = _S26 * _S27;
                break;
            }
            if(operation_2 == 2U)
            {
                _S26 = _S26 + _S27;
                break;
            }
            if(operation_2 == 3U)
            {
                _S26 = _S26 - _S27;
                break;
            }
            if(operation_2 == 4U)
            {
                float factor_2 = _S79.z;
                _S26 = _S26 * float4((1.0f - factor_2))  + _S27 * float4(factor_2) ;
                break;
            }
            break;
        }
        metallic_0 = saturate(_S26.x);
    }
    else
    {
        metallic_0 = _S15;
    }
    float3 emissiveColor_0;
    if((textureMask_0 & 16U) != 0U)
    {
        bool _S88 = (udimMask_0 & 16U) != 0U;
        for(;;)
        {
            if(!_S88)
            {
                _S26 = (((&kernelContext_0)->emissiveTexture_0).sample(((&kernelContext_0)->emissiveSampler_0), (_S25)));
                break;
            }
            texture2d<float, access::sample> _S89 = (&kernelContext_0)->emissiveTexture_0;
            thread uint atlasWidth_7;
            thread uint atlasHeight_7;
            (*((&atlasWidth_7)) = (_S89).get_width(0)),(*((&atlasHeight_7)) = (_S89).get_height(0));
            int3 _S90 = int3(int(0), int(0), int(0));
            float4 metadata_7 = round((((&kernelContext_0)->emissiveTexture_0).read(vec<uint,2>(((_S90)).xy), uint(((_S90)).z))) * float4(255.0f) );
            int2 _S91 = int2(metadata_7.zw);
            int2 tile_7 = int2(floor(_S25)) - int2(metadata_7.xy);
            if(any(tile_7 < (int2(int(0)) )))
            {
                hasSceneLighting_0 = true;
            }
            else
            {
                hasSceneLighting_0 = any(tile_7 >= _S91);
            }
            if(hasSceneLighting_0)
            {
                int3 _S92 = int3(int(min(1U, atlasWidth_7 - 1U)), int(0), int(0));
                _S26 = (((&kernelContext_0)->emissiveTexture_0).read(vec<uint,2>(((_S92)).xy), uint(((_S92)).z)));
                break;
            }
            uint _S93 = atlasWidth_7 / uint(_S91.x);
            float _S94 = float(_S93);
            uint _S95 = (atlasHeight_7 - 1U) / uint(_S91.y);
            float2 cellSize_7 = float2(_S94, float(_S95));
            _S26 = (((&kernelContext_0)->emissiveTexture_0).sample(((&kernelContext_0)->emissiveSampler_0), ((float2(tile_7) * cellSize_7 + float2(1.5f, 2.5f) + fract(_S25) * max(cellSize_7 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_7), float(atlasHeight_7)))));
            break;
        }
        for(;;)
        {
            float4 _S96 = float4(_S3->compositeControls_0) ;
            if((_S96.x) != 16.0f)
            {
                break;
            }
            bool _S97 = (_S96.w) >= 0.5f;
            for(;;)
            {
                if(!_S97)
                {
                    _S27 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), (_S25)));
                    break;
                }
                texture2d<float, access::sample> _S98 = (&kernelContext_0)->compositeTexture_0;
                thread uint atlasWidth_8;
                thread uint atlasHeight_8;
                (*((&atlasWidth_8)) = (_S98).get_width(0)),(*((&atlasHeight_8)) = (_S98).get_height(0));
                int3 _S99 = int3(int(0), int(0), int(0));
                float4 metadata_8 = round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S99)).xy), uint(((_S99)).z))) * float4(255.0f) );
                int2 _S100 = int2(metadata_8.zw);
                int2 tile_8 = int2(floor(_S25)) - int2(metadata_8.xy);
                if(any(tile_8 < (int2(int(0)) )))
                {
                    hasSceneLighting_0 = true;
                }
                else
                {
                    hasSceneLighting_0 = any(tile_8 >= _S100);
                }
                if(hasSceneLighting_0)
                {
                    int3 _S101 = int3(int(min(1U, atlasWidth_8 - 1U)), int(0), int(0));
                    _S27 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S101)).xy), uint(((_S101)).z)));
                    break;
                }
                uint _S102 = atlasWidth_8 / uint(_S100.x);
                float _S103 = float(_S102);
                uint _S104 = (atlasHeight_8 - 1U) / uint(_S100.y);
                float2 cellSize_8 = float2(_S103, float(_S104));
                _S27 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), ((float2(tile_8) * cellSize_8 + float2(1.5f, 2.5f) + fract(_S25) * max(cellSize_8 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_8), float(atlasHeight_8)))));
                break;
            }
            uint operation_3 = uint(round(_S96.y));
            if(operation_3 == 1U)
            {
                _S26 = _S26 * _S27;
                break;
            }
            if(operation_3 == 2U)
            {
                _S26 = _S26 + _S27;
                break;
            }
            if(operation_3 == 3U)
            {
                _S26 = _S26 - _S27;
                break;
            }
            if(operation_3 == 4U)
            {
                float factor_3 = _S96.z;
                _S26 = _S26 * float4((1.0f - factor_3))  + _S27 * float4(factor_3) ;
                break;
            }
            break;
        }
        emissiveColor_0 = _S26.xyz;
    }
    else
    {
        emissiveColor_0 = _S10;
    }
    if((textureMask_0 & 64U) != 0U)
    {
        bool _S105 = (udimMask_0 & 64U) != 0U;
        for(;;)
        {
            if(!_S105)
            {
                _S26 = (((&kernelContext_0)->opacityTexture_0).sample(((&kernelContext_0)->opacitySampler_0), (_S25)));
                break;
            }
            texture2d<float, access::sample> _S106 = (&kernelContext_0)->opacityTexture_0;
            thread uint atlasWidth_9;
            thread uint atlasHeight_9;
            (*((&atlasWidth_9)) = (_S106).get_width(0)),(*((&atlasHeight_9)) = (_S106).get_height(0));
            int3 _S107 = int3(int(0), int(0), int(0));
            float4 metadata_9 = round((((&kernelContext_0)->opacityTexture_0).read(vec<uint,2>(((_S107)).xy), uint(((_S107)).z))) * float4(255.0f) );
            int2 _S108 = int2(metadata_9.zw);
            int2 tile_9 = int2(floor(_S25)) - int2(metadata_9.xy);
            if(any(tile_9 < (int2(int(0)) )))
            {
                hasSceneLighting_0 = true;
            }
            else
            {
                hasSceneLighting_0 = any(tile_9 >= _S108);
            }
            if(hasSceneLighting_0)
            {
                int3 _S109 = int3(int(min(1U, atlasWidth_9 - 1U)), int(0), int(0));
                _S26 = (((&kernelContext_0)->opacityTexture_0).read(vec<uint,2>(((_S109)).xy), uint(((_S109)).z)));
                break;
            }
            uint _S110 = atlasWidth_9 / uint(_S108.x);
            float _S111 = float(_S110);
            uint _S112 = (atlasHeight_9 - 1U) / uint(_S108.y);
            float2 cellSize_9 = float2(_S111, float(_S112));
            _S26 = (((&kernelContext_0)->opacityTexture_0).sample(((&kernelContext_0)->opacitySampler_0), ((float2(tile_9) * cellSize_9 + float2(1.5f, 2.5f) + fract(_S25) * max(cellSize_9 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_9), float(atlasHeight_9)))));
            break;
        }
        for(;;)
        {
            float4 _S113 = float4(_S3->compositeControls_0) ;
            if((_S113.x) != 64.0f)
            {
                break;
            }
            bool _S114 = (_S113.w) >= 0.5f;
            for(;;)
            {
                if(!_S114)
                {
                    _S27 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), (_S25)));
                    break;
                }
                texture2d<float, access::sample> _S115 = (&kernelContext_0)->compositeTexture_0;
                thread uint atlasWidth_10;
                thread uint atlasHeight_10;
                (*((&atlasWidth_10)) = (_S115).get_width(0)),(*((&atlasHeight_10)) = (_S115).get_height(0));
                int3 _S116 = int3(int(0), int(0), int(0));
                float4 metadata_10 = round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S116)).xy), uint(((_S116)).z))) * float4(255.0f) );
                int2 _S117 = int2(metadata_10.zw);
                int2 tile_10 = int2(floor(_S25)) - int2(metadata_10.xy);
                if(any(tile_10 < (int2(int(0)) )))
                {
                    hasSceneLighting_0 = true;
                }
                else
                {
                    hasSceneLighting_0 = any(tile_10 >= _S117);
                }
                if(hasSceneLighting_0)
                {
                    int3 _S118 = int3(int(min(1U, atlasWidth_10 - 1U)), int(0), int(0));
                    _S27 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S118)).xy), uint(((_S118)).z)));
                    break;
                }
                uint _S119 = atlasWidth_10 / uint(_S117.x);
                float _S120 = float(_S119);
                uint _S121 = (atlasHeight_10 - 1U) / uint(_S117.y);
                float2 cellSize_10 = float2(_S120, float(_S121));
                _S27 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), ((float2(tile_10) * cellSize_10 + float2(1.5f, 2.5f) + fract(_S25) * max(cellSize_10 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_10), float(atlasHeight_10)))));
                break;
            }
            uint operation_4 = uint(round(_S113.y));
            if(operation_4 == 1U)
            {
                _S26 = _S26 * _S27;
                break;
            }
            if(operation_4 == 2U)
            {
                _S26 = _S26 + _S27;
                break;
            }
            if(operation_4 == 3U)
            {
                _S26 = _S26 - _S27;
                break;
            }
            if(operation_4 == 4U)
            {
                float factor_4 = _S113.z;
                _S26 = _S26 * float4((1.0f - factor_4))  + _S27 * float4(factor_4) ;
                break;
            }
            break;
        }
        opacity_0 = saturate(_S26.x);
    }
    float occlusion_0;
    if((textureMask_0 & 128U) != 0U)
    {
        bool _S122 = (udimMask_0 & 128U) != 0U;
        for(;;)
        {
            if(!_S122)
            {
                _S26 = (((&kernelContext_0)->occlusionTexture_0).sample(((&kernelContext_0)->occlusionSampler_0), (_S25)));
                break;
            }
            texture2d<float, access::sample> _S123 = (&kernelContext_0)->occlusionTexture_0;
            thread uint atlasWidth_11;
            thread uint atlasHeight_11;
            (*((&atlasWidth_11)) = (_S123).get_width(0)),(*((&atlasHeight_11)) = (_S123).get_height(0));
            int3 _S124 = int3(int(0), int(0), int(0));
            float4 metadata_11 = round((((&kernelContext_0)->occlusionTexture_0).read(vec<uint,2>(((_S124)).xy), uint(((_S124)).z))) * float4(255.0f) );
            int2 _S125 = int2(metadata_11.zw);
            int2 tile_11 = int2(floor(_S25)) - int2(metadata_11.xy);
            if(any(tile_11 < (int2(int(0)) )))
            {
                hasSceneLighting_0 = true;
            }
            else
            {
                hasSceneLighting_0 = any(tile_11 >= _S125);
            }
            if(hasSceneLighting_0)
            {
                int3 _S126 = int3(int(min(1U, atlasWidth_11 - 1U)), int(0), int(0));
                _S26 = (((&kernelContext_0)->occlusionTexture_0).read(vec<uint,2>(((_S126)).xy), uint(((_S126)).z)));
                break;
            }
            uint _S127 = atlasWidth_11 / uint(_S125.x);
            float _S128 = float(_S127);
            uint _S129 = (atlasHeight_11 - 1U) / uint(_S125.y);
            float2 cellSize_11 = float2(_S128, float(_S129));
            _S26 = (((&kernelContext_0)->occlusionTexture_0).sample(((&kernelContext_0)->occlusionSampler_0), ((float2(tile_11) * cellSize_11 + float2(1.5f, 2.5f) + fract(_S25) * max(cellSize_11 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_11), float(atlasHeight_11)))));
            break;
        }
        for(;;)
        {
            float4 _S130 = float4(_S3->compositeControls_0) ;
            if((_S130.x) != 128.0f)
            {
                break;
            }
            bool _S131 = (_S130.w) >= 0.5f;
            for(;;)
            {
                if(!_S131)
                {
                    _S27 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), (_S25)));
                    break;
                }
                texture2d<float, access::sample> _S132 = (&kernelContext_0)->compositeTexture_0;
                thread uint atlasWidth_12;
                thread uint atlasHeight_12;
                (*((&atlasWidth_12)) = (_S132).get_width(0)),(*((&atlasHeight_12)) = (_S132).get_height(0));
                int3 _S133 = int3(int(0), int(0), int(0));
                float4 metadata_12 = round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S133)).xy), uint(((_S133)).z))) * float4(255.0f) );
                int2 _S134 = int2(metadata_12.zw);
                int2 tile_12 = int2(floor(_S25)) - int2(metadata_12.xy);
                if(any(tile_12 < (int2(int(0)) )))
                {
                    hasSceneLighting_0 = true;
                }
                else
                {
                    hasSceneLighting_0 = any(tile_12 >= _S134);
                }
                if(hasSceneLighting_0)
                {
                    int3 _S135 = int3(int(min(1U, atlasWidth_12 - 1U)), int(0), int(0));
                    _S27 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S135)).xy), uint(((_S135)).z)));
                    break;
                }
                uint _S136 = atlasWidth_12 / uint(_S134.x);
                float _S137 = float(_S136);
                uint _S138 = (atlasHeight_12 - 1U) / uint(_S134.y);
                float2 cellSize_12 = float2(_S137, float(_S138));
                _S27 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), ((float2(tile_12) * cellSize_12 + float2(1.5f, 2.5f) + fract(_S25) * max(cellSize_12 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_12), float(atlasHeight_12)))));
                break;
            }
            uint operation_5 = uint(round(_S130.y));
            if(operation_5 == 1U)
            {
                _S26 = _S26 * _S27;
                break;
            }
            if(operation_5 == 2U)
            {
                _S26 = _S26 + _S27;
                break;
            }
            if(operation_5 == 3U)
            {
                _S26 = _S26 - _S27;
                break;
            }
            if(operation_5 == 4U)
            {
                float factor_5 = _S130.z;
                _S26 = _S26 * float4((1.0f - factor_5))  + _S27 * float4(factor_5) ;
                break;
            }
            break;
        }
        occlusion_0 = saturate(_S26.x);
    }
    else
    {
        occlusion_0 = _S11;
    }
    float3 specularColor_0;
    if((textureMask_0 & 256U) != 0U)
    {
        bool _S139 = (udimMask_0 & 256U) != 0U;
        for(;;)
        {
            if(!_S139)
            {
                _S26 = (((&kernelContext_0)->specularColorTexture_0).sample(((&kernelContext_0)->specularColorSampler_0), (_S25)));
                break;
            }
            texture2d<float, access::sample> _S140 = (&kernelContext_0)->specularColorTexture_0;
            thread uint atlasWidth_13;
            thread uint atlasHeight_13;
            (*((&atlasWidth_13)) = (_S140).get_width(0)),(*((&atlasHeight_13)) = (_S140).get_height(0));
            int3 _S141 = int3(int(0), int(0), int(0));
            float4 metadata_13 = round((((&kernelContext_0)->specularColorTexture_0).read(vec<uint,2>(((_S141)).xy), uint(((_S141)).z))) * float4(255.0f) );
            int2 _S142 = int2(metadata_13.zw);
            int2 tile_13 = int2(floor(_S25)) - int2(metadata_13.xy);
            if(any(tile_13 < (int2(int(0)) )))
            {
                hasSceneLighting_0 = true;
            }
            else
            {
                hasSceneLighting_0 = any(tile_13 >= _S142);
            }
            if(hasSceneLighting_0)
            {
                int3 _S143 = int3(int(min(1U, atlasWidth_13 - 1U)), int(0), int(0));
                _S26 = (((&kernelContext_0)->specularColorTexture_0).read(vec<uint,2>(((_S143)).xy), uint(((_S143)).z)));
                break;
            }
            uint _S144 = atlasWidth_13 / uint(_S142.x);
            float _S145 = float(_S144);
            uint _S146 = (atlasHeight_13 - 1U) / uint(_S142.y);
            float2 cellSize_13 = float2(_S145, float(_S146));
            _S26 = (((&kernelContext_0)->specularColorTexture_0).sample(((&kernelContext_0)->specularColorSampler_0), ((float2(tile_13) * cellSize_13 + float2(1.5f, 2.5f) + fract(_S25) * max(cellSize_13 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_13), float(atlasHeight_13)))));
            break;
        }
        for(;;)
        {
            float4 _S147 = float4(_S3->compositeControls_0) ;
            if((_S147.x) != 256.0f)
            {
                break;
            }
            bool _S148 = (_S147.w) >= 0.5f;
            for(;;)
            {
                if(!_S148)
                {
                    _S27 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), (_S25)));
                    break;
                }
                texture2d<float, access::sample> _S149 = (&kernelContext_0)->compositeTexture_0;
                thread uint atlasWidth_14;
                thread uint atlasHeight_14;
                (*((&atlasWidth_14)) = (_S149).get_width(0)),(*((&atlasHeight_14)) = (_S149).get_height(0));
                int3 _S150 = int3(int(0), int(0), int(0));
                float4 metadata_14 = round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S150)).xy), uint(((_S150)).z))) * float4(255.0f) );
                int2 _S151 = int2(metadata_14.zw);
                int2 tile_14 = int2(floor(_S25)) - int2(metadata_14.xy);
                if(any(tile_14 < (int2(int(0)) )))
                {
                    hasSceneLighting_0 = true;
                }
                else
                {
                    hasSceneLighting_0 = any(tile_14 >= _S151);
                }
                if(hasSceneLighting_0)
                {
                    int3 _S152 = int3(int(min(1U, atlasWidth_14 - 1U)), int(0), int(0));
                    _S27 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S152)).xy), uint(((_S152)).z)));
                    break;
                }
                uint _S153 = atlasWidth_14 / uint(_S151.x);
                float _S154 = float(_S153);
                uint _S155 = (atlasHeight_14 - 1U) / uint(_S151.y);
                float2 cellSize_14 = float2(_S154, float(_S155));
                _S27 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), ((float2(tile_14) * cellSize_14 + float2(1.5f, 2.5f) + fract(_S25) * max(cellSize_14 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_14), float(atlasHeight_14)))));
                break;
            }
            uint operation_6 = uint(round(_S147.y));
            if(operation_6 == 1U)
            {
                _S26 = _S26 * _S27;
                break;
            }
            if(operation_6 == 2U)
            {
                _S26 = _S26 + _S27;
                break;
            }
            if(operation_6 == 3U)
            {
                _S26 = _S26 - _S27;
                break;
            }
            if(operation_6 == 4U)
            {
                float factor_6 = _S147.z;
                _S26 = _S26 * float4((1.0f - factor_6))  + _S27 * float4(factor_6) ;
                break;
            }
            break;
        }
        specularColor_0 = saturate(_S26.xyz);
    }
    else
    {
        specularColor_0 = _S18;
    }
    float clearcoatAmount_0;
    if((textureMask_0 & 512U) != 0U)
    {
        bool _S156 = (udimMask_0 & 512U) != 0U;
        for(;;)
        {
            if(!_S156)
            {
                _S26 = (((&kernelContext_0)->clearcoatTexture_0).sample(((&kernelContext_0)->clearcoatSampler_0), (_S25)));
                break;
            }
            texture2d<float, access::sample> _S157 = (&kernelContext_0)->clearcoatTexture_0;
            thread uint atlasWidth_15;
            thread uint atlasHeight_15;
            (*((&atlasWidth_15)) = (_S157).get_width(0)),(*((&atlasHeight_15)) = (_S157).get_height(0));
            int3 _S158 = int3(int(0), int(0), int(0));
            float4 metadata_15 = round((((&kernelContext_0)->clearcoatTexture_0).read(vec<uint,2>(((_S158)).xy), uint(((_S158)).z))) * float4(255.0f) );
            int2 _S159 = int2(metadata_15.zw);
            int2 tile_15 = int2(floor(_S25)) - int2(metadata_15.xy);
            if(any(tile_15 < (int2(int(0)) )))
            {
                hasSceneLighting_0 = true;
            }
            else
            {
                hasSceneLighting_0 = any(tile_15 >= _S159);
            }
            if(hasSceneLighting_0)
            {
                int3 _S160 = int3(int(min(1U, atlasWidth_15 - 1U)), int(0), int(0));
                _S26 = (((&kernelContext_0)->clearcoatTexture_0).read(vec<uint,2>(((_S160)).xy), uint(((_S160)).z)));
                break;
            }
            uint _S161 = atlasWidth_15 / uint(_S159.x);
            float _S162 = float(_S161);
            uint _S163 = (atlasHeight_15 - 1U) / uint(_S159.y);
            float2 cellSize_15 = float2(_S162, float(_S163));
            _S26 = (((&kernelContext_0)->clearcoatTexture_0).sample(((&kernelContext_0)->clearcoatSampler_0), ((float2(tile_15) * cellSize_15 + float2(1.5f, 2.5f) + fract(_S25) * max(cellSize_15 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_15), float(atlasHeight_15)))));
            break;
        }
        for(;;)
        {
            float4 _S164 = float4(_S3->compositeControls_0) ;
            if((_S164.x) != 512.0f)
            {
                break;
            }
            bool _S165 = (_S164.w) >= 0.5f;
            for(;;)
            {
                if(!_S165)
                {
                    _S27 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), (_S25)));
                    break;
                }
                texture2d<float, access::sample> _S166 = (&kernelContext_0)->compositeTexture_0;
                thread uint atlasWidth_16;
                thread uint atlasHeight_16;
                (*((&atlasWidth_16)) = (_S166).get_width(0)),(*((&atlasHeight_16)) = (_S166).get_height(0));
                int3 _S167 = int3(int(0), int(0), int(0));
                float4 metadata_16 = round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S167)).xy), uint(((_S167)).z))) * float4(255.0f) );
                int2 _S168 = int2(metadata_16.zw);
                int2 tile_16 = int2(floor(_S25)) - int2(metadata_16.xy);
                if(any(tile_16 < (int2(int(0)) )))
                {
                    hasSceneLighting_0 = true;
                }
                else
                {
                    hasSceneLighting_0 = any(tile_16 >= _S168);
                }
                if(hasSceneLighting_0)
                {
                    int3 _S169 = int3(int(min(1U, atlasWidth_16 - 1U)), int(0), int(0));
                    _S27 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S169)).xy), uint(((_S169)).z)));
                    break;
                }
                uint _S170 = atlasWidth_16 / uint(_S168.x);
                float _S171 = float(_S170);
                uint _S172 = (atlasHeight_16 - 1U) / uint(_S168.y);
                float2 cellSize_16 = float2(_S171, float(_S172));
                _S27 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), ((float2(tile_16) * cellSize_16 + float2(1.5f, 2.5f) + fract(_S25) * max(cellSize_16 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_16), float(atlasHeight_16)))));
                break;
            }
            uint operation_7 = uint(round(_S164.y));
            if(operation_7 == 1U)
            {
                _S26 = _S26 * _S27;
                break;
            }
            if(operation_7 == 2U)
            {
                _S26 = _S26 + _S27;
                break;
            }
            if(operation_7 == 3U)
            {
                _S26 = _S26 - _S27;
                break;
            }
            if(operation_7 == 4U)
            {
                float factor_7 = _S164.z;
                _S26 = _S26 * float4((1.0f - factor_7))  + _S27 * float4(factor_7) ;
                break;
            }
            break;
        }
        clearcoatAmount_0 = saturate(_S26.x);
    }
    else
    {
        clearcoatAmount_0 = _S20;
    }
    float clearcoatRoughness_0;
    if((textureMask_0 & 1024U) != 0U)
    {
        bool _S173 = (udimMask_0 & 1024U) != 0U;
        for(;;)
        {
            if(!_S173)
            {
                _S26 = (((&kernelContext_0)->clearcoatRoughnessTexture_0).sample(((&kernelContext_0)->clearcoatRoughnessSampler_0), (_S25)));
                break;
            }
            texture2d<float, access::sample> _S174 = (&kernelContext_0)->clearcoatRoughnessTexture_0;
            thread uint atlasWidth_17;
            thread uint atlasHeight_17;
            (*((&atlasWidth_17)) = (_S174).get_width(0)),(*((&atlasHeight_17)) = (_S174).get_height(0));
            int3 _S175 = int3(int(0), int(0), int(0));
            float4 metadata_17 = round((((&kernelContext_0)->clearcoatRoughnessTexture_0).read(vec<uint,2>(((_S175)).xy), uint(((_S175)).z))) * float4(255.0f) );
            int2 _S176 = int2(metadata_17.zw);
            int2 tile_17 = int2(floor(_S25)) - int2(metadata_17.xy);
            if(any(tile_17 < (int2(int(0)) )))
            {
                hasSceneLighting_0 = true;
            }
            else
            {
                hasSceneLighting_0 = any(tile_17 >= _S176);
            }
            if(hasSceneLighting_0)
            {
                int3 _S177 = int3(int(min(1U, atlasWidth_17 - 1U)), int(0), int(0));
                _S26 = (((&kernelContext_0)->clearcoatRoughnessTexture_0).read(vec<uint,2>(((_S177)).xy), uint(((_S177)).z)));
                break;
            }
            uint _S178 = atlasWidth_17 / uint(_S176.x);
            float _S179 = float(_S178);
            uint _S180 = (atlasHeight_17 - 1U) / uint(_S176.y);
            float2 cellSize_17 = float2(_S179, float(_S180));
            _S26 = (((&kernelContext_0)->clearcoatRoughnessTexture_0).sample(((&kernelContext_0)->clearcoatRoughnessSampler_0), ((float2(tile_17) * cellSize_17 + float2(1.5f, 2.5f) + fract(_S25) * max(cellSize_17 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_17), float(atlasHeight_17)))));
            break;
        }
        for(;;)
        {
            float4 _S181 = float4(_S3->compositeControls_0) ;
            if((_S181.x) != 1024.0f)
            {
                break;
            }
            bool _S182 = (_S181.w) >= 0.5f;
            for(;;)
            {
                if(!_S182)
                {
                    _S27 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), (_S25)));
                    break;
                }
                texture2d<float, access::sample> _S183 = (&kernelContext_0)->compositeTexture_0;
                thread uint atlasWidth_18;
                thread uint atlasHeight_18;
                (*((&atlasWidth_18)) = (_S183).get_width(0)),(*((&atlasHeight_18)) = (_S183).get_height(0));
                int3 _S184 = int3(int(0), int(0), int(0));
                float4 metadata_18 = round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S184)).xy), uint(((_S184)).z))) * float4(255.0f) );
                int2 _S185 = int2(metadata_18.zw);
                int2 tile_18 = int2(floor(_S25)) - int2(metadata_18.xy);
                if(any(tile_18 < (int2(int(0)) )))
                {
                    hasSceneLighting_0 = true;
                }
                else
                {
                    hasSceneLighting_0 = any(tile_18 >= _S185);
                }
                if(hasSceneLighting_0)
                {
                    int3 _S186 = int3(int(min(1U, atlasWidth_18 - 1U)), int(0), int(0));
                    _S27 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S186)).xy), uint(((_S186)).z)));
                    break;
                }
                uint _S187 = atlasWidth_18 / uint(_S185.x);
                float _S188 = float(_S187);
                uint _S189 = (atlasHeight_18 - 1U) / uint(_S185.y);
                float2 cellSize_18 = float2(_S188, float(_S189));
                _S27 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), ((float2(tile_18) * cellSize_18 + float2(1.5f, 2.5f) + fract(_S25) * max(cellSize_18 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_18), float(atlasHeight_18)))));
                break;
            }
            uint operation_8 = uint(round(_S181.y));
            if(operation_8 == 1U)
            {
                _S26 = _S26 * _S27;
                break;
            }
            if(operation_8 == 2U)
            {
                _S26 = _S26 + _S27;
                break;
            }
            if(operation_8 == 3U)
            {
                _S26 = _S26 - _S27;
                break;
            }
            if(operation_8 == 4U)
            {
                float factor_8 = _S181.z;
                _S26 = _S26 * float4((1.0f - factor_8))  + _S27 * float4(factor_8) ;
                break;
            }
            break;
        }
        clearcoatRoughness_0 = saturate(_S26.x);
    }
    else
    {
        clearcoatRoughness_0 = _S21;
    }
    float ior_0;
    if((textureMask_0 & 2048U) != 0U)
    {
        bool _S190 = (udimMask_0 & 2048U) != 0U;
        for(;;)
        {
            if(!_S190)
            {
                _S26 = (((&kernelContext_0)->iorTexture_0).sample(((&kernelContext_0)->iorSampler_0), (_S25)));
                break;
            }
            texture2d<float, access::sample> _S191 = (&kernelContext_0)->iorTexture_0;
            thread uint atlasWidth_19;
            thread uint atlasHeight_19;
            (*((&atlasWidth_19)) = (_S191).get_width(0)),(*((&atlasHeight_19)) = (_S191).get_height(0));
            int3 _S192 = int3(int(0), int(0), int(0));
            float4 metadata_19 = round((((&kernelContext_0)->iorTexture_0).read(vec<uint,2>(((_S192)).xy), uint(((_S192)).z))) * float4(255.0f) );
            int2 _S193 = int2(metadata_19.zw);
            int2 tile_19 = int2(floor(_S25)) - int2(metadata_19.xy);
            if(any(tile_19 < (int2(int(0)) )))
            {
                hasSceneLighting_0 = true;
            }
            else
            {
                hasSceneLighting_0 = any(tile_19 >= _S193);
            }
            if(hasSceneLighting_0)
            {
                int3 _S194 = int3(int(min(1U, atlasWidth_19 - 1U)), int(0), int(0));
                _S26 = (((&kernelContext_0)->iorTexture_0).read(vec<uint,2>(((_S194)).xy), uint(((_S194)).z)));
                break;
            }
            uint _S195 = atlasWidth_19 / uint(_S193.x);
            float _S196 = float(_S195);
            uint _S197 = (atlasHeight_19 - 1U) / uint(_S193.y);
            float2 cellSize_19 = float2(_S196, float(_S197));
            _S26 = (((&kernelContext_0)->iorTexture_0).sample(((&kernelContext_0)->iorSampler_0), ((float2(tile_19) * cellSize_19 + float2(1.5f, 2.5f) + fract(_S25) * max(cellSize_19 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_19), float(atlasHeight_19)))));
            break;
        }
        for(;;)
        {
            float4 _S198 = float4(_S3->compositeControls_0) ;
            if((_S198.x) != 2048.0f)
            {
                break;
            }
            bool _S199 = (_S198.w) >= 0.5f;
            for(;;)
            {
                if(!_S199)
                {
                    _S27 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), (_S25)));
                    break;
                }
                texture2d<float, access::sample> _S200 = (&kernelContext_0)->compositeTexture_0;
                thread uint atlasWidth_20;
                thread uint atlasHeight_20;
                (*((&atlasWidth_20)) = (_S200).get_width(0)),(*((&atlasHeight_20)) = (_S200).get_height(0));
                int3 _S201 = int3(int(0), int(0), int(0));
                float4 metadata_20 = round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S201)).xy), uint(((_S201)).z))) * float4(255.0f) );
                int2 _S202 = int2(metadata_20.zw);
                int2 tile_20 = int2(floor(_S25)) - int2(metadata_20.xy);
                if(any(tile_20 < (int2(int(0)) )))
                {
                    hasSceneLighting_0 = true;
                }
                else
                {
                    hasSceneLighting_0 = any(tile_20 >= _S202);
                }
                if(hasSceneLighting_0)
                {
                    int3 _S203 = int3(int(min(1U, atlasWidth_20 - 1U)), int(0), int(0));
                    _S27 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S203)).xy), uint(((_S203)).z)));
                    break;
                }
                uint _S204 = atlasWidth_20 / uint(_S202.x);
                float _S205 = float(_S204);
                uint _S206 = (atlasHeight_20 - 1U) / uint(_S202.y);
                float2 cellSize_20 = float2(_S205, float(_S206));
                _S27 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), ((float2(tile_20) * cellSize_20 + float2(1.5f, 2.5f) + fract(_S25) * max(cellSize_20 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_20), float(atlasHeight_20)))));
                break;
            }
            uint operation_9 = uint(round(_S198.y));
            if(operation_9 == 1U)
            {
                _S26 = _S26 * _S27;
                break;
            }
            if(operation_9 == 2U)
            {
                _S26 = _S26 + _S27;
                break;
            }
            if(operation_9 == 3U)
            {
                _S26 = _S26 - _S27;
                break;
            }
            if(operation_9 == 4U)
            {
                float factor_9 = _S198.z;
                _S26 = _S26 * float4((1.0f - factor_9))  + _S27 * float4(factor_9) ;
                break;
            }
            break;
        }
        ior_0 = _S26.x;
    }
    else
    {
        ior_0 = _S19;
    }
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
    float3 _S207;
    if(lengthSquared_0 > 0.00100000004749745f)
    {
        _S207 = - _S1.eyePosition_0 * float3(rsqrt(lengthSquared_0)) ;
    }
    else
    {
        _S207 = float3(0.0f, 0.0f, 1.0f);
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
    float _S208 = saturate(abs(dot(normal_2, _S207)) + 0.00000999999974738f);
    float _S209 = max(0.00100000004749745f, roughness_0);
    float _S210 = max(0.00100000004749745f, clearcoatRoughness_0);
    float reflectanceRatio_0 = (1.0f - ior_0) / (1.0f + ior_0);
    float3 _S211 = float3(3.14159274101257324f) ;
    float3 diffuse_0 = diffuseColor_0 / _S211;
    float3 normalIncidence_0;
    float3 grazingIncidence_0;
    float3 diffuse_1;
    if((_S14.w) >= 0.5f)
    {
        float3 _S212 = float3(1.0f, 1.0f, 1.0f);
        normalIncidence_0 = specularColor_0;
        grazingIncidence_0 = _S212;
        diffuse_1 = diffuse_0;
    }
    else
    {
        float3 _S213 = float3(metallic_0) ;
        float3 specularTint_0 = mix(float3(1.0f, 1.0f, 1.0f), diffuseColor_0, _S213);
        float3 diffuse_2 = diffuse_0 * float3((1.0f - metallic_0)) ;
        normalIncidence_0 = mix(float3((reflectanceRatio_0 * reflectanceRatio_0))  * specularTint_0, specularTint_0, _S213);
        grazingIncidence_0 = specularTint_0;
        diffuse_1 = diffuse_2;
    }
    float4 _S214 = float4(_S4->ambientLight_0) ;
    float _S215 = _S214.w;
    if(_S215 > 0.5f)
    {
        hasSceneLighting_0 = true;
    }
    else
    {
        float3 _S216 = _S214.xyz;
        hasSceneLighting_0 = (dot(_S216, _S216)) > 0.0f;
    }
    uint _S217 = min(uint(_S215), 8U);
    matrix<float,int(4),int(4)>  _S218 = matrix<float,int(4),int(4)> (_S4->eyeToWorld_0.data_0[int(0)][int(0)], _S4->eyeToWorld_0.data_0[int(0)][int(1)], _S4->eyeToWorld_0.data_0[int(0)][int(2)], _S4->eyeToWorld_0.data_0[int(0)][int(3)], _S4->eyeToWorld_0.data_0[int(1)][int(0)], _S4->eyeToWorld_0.data_0[int(1)][int(1)], _S4->eyeToWorld_0.data_0[int(1)][int(2)], _S4->eyeToWorld_0.data_0[int(1)][int(3)], _S4->eyeToWorld_0.data_0[int(2)][int(0)], _S4->eyeToWorld_0.data_0[int(2)][int(1)], _S4->eyeToWorld_0.data_0[int(2)][int(2)], _S4->eyeToWorld_0.data_0[int(2)][int(3)], _S4->eyeToWorld_0.data_0[int(3)][int(0)], _S4->eyeToWorld_0.data_0[int(3)][int(1)], _S4->eyeToWorld_0.data_0[int(3)][int(2)], _S4->eyeToWorld_0.data_0[int(3)][int(3)]);
    float3 _S219 = normalize((((float4(_S207, 0.0f)) * (_S218))).xyz);
    float3 _S220 = (((float4(_S1.eyePosition_0, 1.0f)) * (_S218))).xyz;
    float4 _S221 = float4(_S3->lightColorAmbient_0) ;
    float3 color_0 = diffuseColor_0 * float3(_S221.w)  + diffuseColor_0 * _S214.xyz;
    bool _S222 = !hasSceneLighting_0;
    uint lightCount_0;
    if(_S222)
    {
        lightCount_0 = 1U;
    }
    else
    {
        lightCount_0 = _S217;
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
        bool _S223 = lightIndex_0 == 0U;
        if(_S223)
        {
            hasSceneLighting_0 = _S222;
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
        bool _S224;
        if(_S223)
        {
            _S224 = _S222;
        }
        else
        {
            _S224 = false;
        }
        float3 lightDirection_0;
        if(_S224)
        {
            lightDirection_0 = normalize((float4(_S3->lightDirectionIntensity_0) ).xyz);
        }
        else
        {
            lightDirection_0 = normalize((float4((&_S4->lightDirectionRadius_0)->data_1[lightIndex_0]) ).xyz);
        }
        bool _S225;
        if(_S223)
        {
            _S225 = _S222;
        }
        else
        {
            _S225 = false;
        }
        if(_S225)
        {
            roughness_0 = (float4(_S3->lightDirectionIntensity_0) ).w;
        }
        else
        {
            roughness_0 = (float4((&_S4->lightColorIntensity_0)->data_1[lightIndex_0]) ).w;
        }
        bool _S226;
        if(_S223)
        {
            _S226 = _S222;
        }
        else
        {
            _S226 = false;
        }
        if(_S226)
        {
            diffuseColor_0 = _S221.xyz;
        }
        else
        {
            diffuseColor_0 = (float4((&_S4->lightColorIntensity_0)->data_1[lightIndex_0]) ).xyz;
        }
        bool _S227;
        if(_S223)
        {
            _S227 = _S222;
        }
        else
        {
            _S227 = false;
        }
        if(_S227)
        {
            metallic_0 = 1.0f;
        }
        else
        {
            metallic_0 = (float4((&_S4->lightControls_0)->data_1[lightIndex_0]) ).x;
        }
        bool _S228;
        if(_S223)
        {
            _S228 = _S222;
        }
        else
        {
            _S228 = false;
        }
        if(_S228)
        {
            clearcoatRoughness_0 = 1.0f;
        }
        else
        {
            clearcoatRoughness_0 = (float4((&_S4->lightControls_0)->data_1[lightIndex_0]) ).y;
        }
        bool _S229;
        if(_S223)
        {
            _S229 = _S222;
        }
        else
        {
            _S229 = false;
        }
        float3 lightTangent_0;
        if(_S229)
        {
            lightTangent_0 = float3(1.0f, 0.0f, 0.0f);
        }
        else
        {
            lightTangent_0 = normalize((float4((&_S4->lightTangentShapeX_0)->data_1[lightIndex_0]) ).xyz);
        }
        bool _S230;
        if(_S223)
        {
            _S230 = _S222;
        }
        else
        {
            _S230 = false;
        }
        float3 lightBitangent_0;
        if(_S230)
        {
            lightBitangent_0 = float3(0.0f, 1.0f, 0.0f);
        }
        else
        {
            lightBitangent_0 = normalize((float4((&_S4->lightBitangentShapeY_0)->data_1[lightIndex_0]) ).xyz);
        }
        bool _S231;
        if(_S223)
        {
            _S231 = _S222;
        }
        else
        {
            _S231 = false;
        }
        float shapeX_0;
        if(_S231)
        {
            shapeX_0 = 0.0f;
        }
        else
        {
            shapeX_0 = (float4((&_S4->lightTangentShapeX_0)->data_1[lightIndex_0]) ).w;
        }
        bool _S232;
        if(_S223)
        {
            _S232 = _S222;
        }
        else
        {
            _S232 = false;
        }
        float shapeY_0;
        if(_S232)
        {
            shapeY_0 = 0.0f;
        }
        else
        {
            shapeY_0 = (float4((&_S4->lightBitangentShapeY_0)->data_1[lightIndex_0]) ).w;
        }
        bool _S233;
        if(_S223)
        {
            _S233 = _S222;
        }
        else
        {
            _S233 = false;
        }
        float lightRadius_0;
        if(_S233)
        {
            lightRadius_0 = 0.0f;
        }
        else
        {
            lightRadius_0 = (float4((&_S4->lightDirectionRadius_0)->data_1[lightIndex_0]) ).w;
        }
        bool _S234;
        if(_S223)
        {
            _S234 = _S222;
        }
        else
        {
            _S234 = false;
        }
        if(_S234)
        {
            specularColor_0 = _S207;
        }
        else
        {
            specularColor_0 = _S219;
        }
        thread array<float3, int(5)> sampleOffsets_0;
        float3 _S235 = float3(0.0f, 0.0f, 0.0f);
        sampleOffsets_0[int(0)] = _S235;
        sampleOffsets_0[int(1)] = _S235;
        sampleOffsets_0[int(2)] = _S235;
        sampleOffsets_0[int(3)] = _S235;
        sampleOffsets_0[int(4)] = _S235;
        float sampleCount_0;
        if(lightType_0 == 3.0f)
        {
            float3 halfWidth_0 = lightTangent_0 * float3((shapeX_0 * 0.5f)) ;
            float3 halfHeight_0 = lightBitangent_0 * float3((shapeY_0 * 0.5f)) ;
            sampleOffsets_0[int(1)] = halfWidth_0 + halfHeight_0;
            sampleOffsets_0[int(2)] = halfWidth_0 - halfHeight_0;
            float3 _S236 = - halfWidth_0;
            sampleOffsets_0[int(3)] = _S236 + halfHeight_0;
            sampleOffsets_0[int(4)] = _S236 - halfHeight_0;
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
            float sampleIntensity_0 = roughness_0 / sampleCount_0;
            float3 sampleDirection_0;
            float emissionScale_0;
            float sampleIntensity_1;
            if(lightType_0 >= 2.0f)
            {
                float3 toLight_0 = (float4((&_S4->lightPositionType_0)->data_1[lightIndex_0]) ).xyz + sampleOffsets_0[sampleIndex_0] - _S220;
                float _S237 = max(dot(toLight_0, toLight_0), 0.00100000004749745f);
                float3 sampleDirection_1 = toLight_0 * float3(rsqrt(_S237)) ;
                float sampleIntensity_2 = sampleIntensity_0 / _S237;
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
            float3 half_0 = normalize(sampleDirection_0 + specularColor_0);
            float normalDotLight_0 = saturate(dot(normal_2, sampleDirection_0));
            float normalDotHalf_0 = saturate(dot(normal_2, half_0));
            float3 _S238 = float3(pow(max(0.0f, 1.0f - saturate(dot(specularColor_0, half_0))), 5.0f)) ;
            float3 _S239 = mix(normalIncidence_0, grazingIncidence_0, _S238);
            float3 directDiffuse_0 = diffuse_1 * (_S53 - _S239);
            float alpha_0 = _S209 * _S209;
            float alphaSquared_0 = alpha_0 * alpha_0;
            float _S240 = normalDotHalf_0 * normalDotHalf_0;
            float denominator_0 = _S240 * (alphaSquared_0 - 1.0f) + 1.0f;
            float k_0 = alpha_0 * 0.5f;
            float _S241 = 1.0f - k_0;
            float3 _S242 = float3((4.0f * normalDotLight_0 * _S208 + 0.00100000004749745f)) ;
            float3 _S243 = _S239 * float3((_S208 / (_S208 * _S241 + k_0) * (normalDotLight_0 / (normalDotLight_0 * _S241 + k_0))))  * float3(((alphaSquared_0 + 0.00100000004749745f) / (denominator_0 * denominator_0 * 3.14159274101257324f)))  / _S242;
            float3 directSpecular_0;
            if(clearcoatAmount_0 > 0.0f)
            {
                float alpha_1 = _S210 * _S210;
                float alphaSquared_1 = alpha_1 * alpha_1;
                float denominator_1 = _S240 * (alphaSquared_1 - 1.0f) + 1.0f;
                float k_1 = alpha_1 * 0.5f;
                float _S244 = 1.0f - k_1;
                directSpecular_0 = _S243 + float3(clearcoatAmount_0)  * (mix(float3((reflectanceRatio_0 * reflectanceRatio_0)) , float3(1.0f, 1.0f, 1.0f), _S238) * float3((_S208 / (_S208 * _S244 + k_1) * (normalDotLight_0 / (normalDotLight_0 * _S244 + k_1))))  * float3(((alphaSquared_1 + 0.00100000004749745f) / (denominator_1 * denominator_1 * 3.14159274101257324f)))  / _S242);
            }
            else
            {
                directSpecular_0 = _S243;
            }
            float3 _S245 = diffuseColor_0 * float3(sampleIntensity_1) ;
            color_2 = color_2 + float3((occlusion_0 * emissionScale_0 * normalDotLight_0))  * (directDiffuse_0 * float3(metallic_0)  * (_S245 * _S211) + directSpecular_0 * float3(clearcoatRoughness_0)  * _S245);
            sampleIndex_0 = sampleIndex_0 + 1U;
        }
        lightIndex_0 = lightIndex_0 + 1U;
        color_1 = color_2;
    }
    float3 color_3 = (color_1 + emissiveColor_0) * float3(exp2((as_type<float>((_S2.z))))) ;
    if((_S2.y) == 1U)
    {
        color_1 = color_3 / (_S53 + max(color_3, float3(0.0f, 0.0f, 0.0f)));
    }
    else
    {
        color_1 = color_3;
    }
    pixelOutput_0 _S246 = { float4(color_1, opacity_0) };
    return _S246;
}
