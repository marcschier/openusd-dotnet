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
    texture2d<float, access::sample> baseColorTexture_0;
    sampler baseColorSampler_0;
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
};

[[fragment]] pixelOutput_0 fragmentMain_uv_material(pixelInput_0 _S1 [[stage_in]], bool isFrontFace_0 [[front_facing]], float4 position_0 [[position]], SurfaceParameters_natural_0 device* surfaceParameters_1 [[buffer(7)]], FrameParameters_natural_0 device* frameParameters_1 [[buffer(8)]], texture2d<float, access::sample> baseColorTexture_1 [[texture(0)]], sampler baseColorSampler_1 [[sampler(0)]], texture2d<float, access::sample> roughnessMetallicTexture_1 [[texture(2)]], sampler roughnessMetallicSampler_1 [[sampler(2)]], texture2d<float, access::sample> metallicTexture_1 [[texture(4)]], sampler metallicSampler_1 [[sampler(5)]], texture2d<float, access::sample> emissiveTexture_1 [[texture(3)]], sampler emissiveSampler_1 [[sampler(3)]], texture2d<float, access::sample> opacityTexture_1 [[texture(5)]], sampler opacitySampler_1 [[sampler(6)]], texture2d<float, access::sample> occlusionTexture_1 [[texture(10)]], sampler occlusionSampler_1 [[sampler(7)]], texture2d<float, access::sample> specularColorTexture_1 [[texture(11)]], sampler specularColorSampler_1 [[sampler(8)]])
{
    uint4 _S2;
    thread KernelContext_0 kernelContext_0;
    (&kernelContext_0)->surfaceParameters_0 = surfaceParameters_1;
    (&kernelContext_0)->frameParameters_0 = frameParameters_1;
    (&kernelContext_0)->baseColorTexture_0 = baseColorTexture_1;
    (&kernelContext_0)->baseColorSampler_0 = baseColorSampler_1;
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
    float4 _S19 = float4(_S3->textureControls_0) ;
    uint textureMask_0 = uint(round(_S19.x));
    uint udimMask_0 = uint(round(_S19.y));
    bool hasSceneLighting_0;
    float4 _S20;
    if((textureMask_0 & 2U) != 0U)
    {
        bool _S21 = (udimMask_0 & 2U) != 0U;
        for(;;)
        {
            if(!_S21)
            {
                _S20 = (((&kernelContext_0)->baseColorTexture_0).sample(((&kernelContext_0)->baseColorSampler_0), (_S1.texCoord_0)));
                break;
            }
            texture2d<float, access::sample> _S22 = (&kernelContext_0)->baseColorTexture_0;
            thread uint atlasWidth_0;
            thread uint atlasHeight_0;
            (*((&atlasWidth_0)) = (_S22).get_width(0)),(*((&atlasHeight_0)) = (_S22).get_height(0));
            int3 _S23 = int3(int(0), int(0), int(0));
            float4 metadata_0 = round((((&kernelContext_0)->baseColorTexture_0).read(vec<uint,2>(((_S23)).xy), uint(((_S23)).z))) * float4(255.0f) );
            int2 _S24 = int2(metadata_0.zw);
            int2 tile_0 = int2(floor(_S1.texCoord_0)) - int2(metadata_0.xy);
            if(any(tile_0 < (int2(int(0)) )))
            {
                hasSceneLighting_0 = true;
            }
            else
            {
                hasSceneLighting_0 = any(tile_0 >= _S24);
            }
            if(hasSceneLighting_0)
            {
                int3 _S25 = int3(int(min(1U, atlasWidth_0 - 1U)), int(0), int(0));
                _S20 = (((&kernelContext_0)->baseColorTexture_0).read(vec<uint,2>(((_S25)).xy), uint(((_S25)).z)));
                break;
            }
            uint _S26 = atlasWidth_0 / uint(_S24.x);
            float _S27 = float(_S26);
            uint _S28 = (atlasHeight_0 - 1U) / uint(_S24.y);
            float2 cellSize_0 = float2(_S27, float(_S28));
            _S20 = (((&kernelContext_0)->baseColorTexture_0).sample(((&kernelContext_0)->baseColorSampler_0), ((float2(tile_0) * cellSize_0 + float2(1.5f, 2.5f) + fract(_S1.texCoord_0) * max(cellSize_0 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_0), float(atlasHeight_0)))));
            break;
        }
        diffuseColor_0 = _S20.xyz;
    }
    float roughness_0;
    if((textureMask_0 & 8U) != 0U)
    {
        bool _S29 = (udimMask_0 & 8U) != 0U;
        for(;;)
        {
            if(!_S29)
            {
                _S20 = (((&kernelContext_0)->roughnessMetallicTexture_0).sample(((&kernelContext_0)->roughnessMetallicSampler_0), (_S1.texCoord_0)));
                break;
            }
            texture2d<float, access::sample> _S30 = (&kernelContext_0)->roughnessMetallicTexture_0;
            thread uint atlasWidth_1;
            thread uint atlasHeight_1;
            (*((&atlasWidth_1)) = (_S30).get_width(0)),(*((&atlasHeight_1)) = (_S30).get_height(0));
            int3 _S31 = int3(int(0), int(0), int(0));
            float4 metadata_1 = round((((&kernelContext_0)->roughnessMetallicTexture_0).read(vec<uint,2>(((_S31)).xy), uint(((_S31)).z))) * float4(255.0f) );
            int2 _S32 = int2(metadata_1.zw);
            int2 tile_1 = int2(floor(_S1.texCoord_0)) - int2(metadata_1.xy);
            if(any(tile_1 < (int2(int(0)) )))
            {
                hasSceneLighting_0 = true;
            }
            else
            {
                hasSceneLighting_0 = any(tile_1 >= _S32);
            }
            if(hasSceneLighting_0)
            {
                int3 _S33 = int3(int(min(1U, atlasWidth_1 - 1U)), int(0), int(0));
                _S20 = (((&kernelContext_0)->roughnessMetallicTexture_0).read(vec<uint,2>(((_S33)).xy), uint(((_S33)).z)));
                break;
            }
            uint _S34 = atlasWidth_1 / uint(_S32.x);
            float _S35 = float(_S34);
            uint _S36 = (atlasHeight_1 - 1U) / uint(_S32.y);
            float2 cellSize_1 = float2(_S35, float(_S36));
            _S20 = (((&kernelContext_0)->roughnessMetallicTexture_0).sample(((&kernelContext_0)->roughnessMetallicSampler_0), ((float2(tile_1) * cellSize_1 + float2(1.5f, 2.5f) + fract(_S1.texCoord_0) * max(cellSize_1 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_1), float(atlasHeight_1)))));
            break;
        }
        roughness_0 = clamp(_S20.x, 0.00999999977648258f, 1.0f);
    }
    else
    {
        roughness_0 = _S16;
    }
    float metallic_0;
    if((textureMask_0 & 32U) != 0U)
    {
        bool _S37 = (udimMask_0 & 32U) != 0U;
        for(;;)
        {
            if(!_S37)
            {
                _S20 = (((&kernelContext_0)->metallicTexture_0).sample(((&kernelContext_0)->metallicSampler_0), (_S1.texCoord_0)));
                break;
            }
            texture2d<float, access::sample> _S38 = (&kernelContext_0)->metallicTexture_0;
            thread uint atlasWidth_2;
            thread uint atlasHeight_2;
            (*((&atlasWidth_2)) = (_S38).get_width(0)),(*((&atlasHeight_2)) = (_S38).get_height(0));
            int3 _S39 = int3(int(0), int(0), int(0));
            float4 metadata_2 = round((((&kernelContext_0)->metallicTexture_0).read(vec<uint,2>(((_S39)).xy), uint(((_S39)).z))) * float4(255.0f) );
            int2 _S40 = int2(metadata_2.zw);
            int2 tile_2 = int2(floor(_S1.texCoord_0)) - int2(metadata_2.xy);
            if(any(tile_2 < (int2(int(0)) )))
            {
                hasSceneLighting_0 = true;
            }
            else
            {
                hasSceneLighting_0 = any(tile_2 >= _S40);
            }
            if(hasSceneLighting_0)
            {
                int3 _S41 = int3(int(min(1U, atlasWidth_2 - 1U)), int(0), int(0));
                _S20 = (((&kernelContext_0)->metallicTexture_0).read(vec<uint,2>(((_S41)).xy), uint(((_S41)).z)));
                break;
            }
            uint _S42 = atlasWidth_2 / uint(_S40.x);
            float _S43 = float(_S42);
            uint _S44 = (atlasHeight_2 - 1U) / uint(_S40.y);
            float2 cellSize_2 = float2(_S43, float(_S44));
            _S20 = (((&kernelContext_0)->metallicTexture_0).sample(((&kernelContext_0)->metallicSampler_0), ((float2(tile_2) * cellSize_2 + float2(1.5f, 2.5f) + fract(_S1.texCoord_0) * max(cellSize_2 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_2), float(atlasHeight_2)))));
            break;
        }
        metallic_0 = saturate(_S20.x);
    }
    else
    {
        metallic_0 = _S15;
    }
    float3 emissiveColor_0;
    if((textureMask_0 & 16U) != 0U)
    {
        bool _S45 = (udimMask_0 & 16U) != 0U;
        for(;;)
        {
            if(!_S45)
            {
                _S20 = (((&kernelContext_0)->emissiveTexture_0).sample(((&kernelContext_0)->emissiveSampler_0), (_S1.texCoord_0)));
                break;
            }
            texture2d<float, access::sample> _S46 = (&kernelContext_0)->emissiveTexture_0;
            thread uint atlasWidth_3;
            thread uint atlasHeight_3;
            (*((&atlasWidth_3)) = (_S46).get_width(0)),(*((&atlasHeight_3)) = (_S46).get_height(0));
            int3 _S47 = int3(int(0), int(0), int(0));
            float4 metadata_3 = round((((&kernelContext_0)->emissiveTexture_0).read(vec<uint,2>(((_S47)).xy), uint(((_S47)).z))) * float4(255.0f) );
            int2 _S48 = int2(metadata_3.zw);
            int2 tile_3 = int2(floor(_S1.texCoord_0)) - int2(metadata_3.xy);
            if(any(tile_3 < (int2(int(0)) )))
            {
                hasSceneLighting_0 = true;
            }
            else
            {
                hasSceneLighting_0 = any(tile_3 >= _S48);
            }
            if(hasSceneLighting_0)
            {
                int3 _S49 = int3(int(min(1U, atlasWidth_3 - 1U)), int(0), int(0));
                _S20 = (((&kernelContext_0)->emissiveTexture_0).read(vec<uint,2>(((_S49)).xy), uint(((_S49)).z)));
                break;
            }
            uint _S50 = atlasWidth_3 / uint(_S48.x);
            float _S51 = float(_S50);
            uint _S52 = (atlasHeight_3 - 1U) / uint(_S48.y);
            float2 cellSize_3 = float2(_S51, float(_S52));
            _S20 = (((&kernelContext_0)->emissiveTexture_0).sample(((&kernelContext_0)->emissiveSampler_0), ((float2(tile_3) * cellSize_3 + float2(1.5f, 2.5f) + fract(_S1.texCoord_0) * max(cellSize_3 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_3), float(atlasHeight_3)))));
            break;
        }
        emissiveColor_0 = _S20.xyz;
    }
    else
    {
        emissiveColor_0 = _S10;
    }
    if((textureMask_0 & 64U) != 0U)
    {
        bool _S53 = (udimMask_0 & 64U) != 0U;
        for(;;)
        {
            if(!_S53)
            {
                _S20 = (((&kernelContext_0)->opacityTexture_0).sample(((&kernelContext_0)->opacitySampler_0), (_S1.texCoord_0)));
                break;
            }
            texture2d<float, access::sample> _S54 = (&kernelContext_0)->opacityTexture_0;
            thread uint atlasWidth_4;
            thread uint atlasHeight_4;
            (*((&atlasWidth_4)) = (_S54).get_width(0)),(*((&atlasHeight_4)) = (_S54).get_height(0));
            int3 _S55 = int3(int(0), int(0), int(0));
            float4 metadata_4 = round((((&kernelContext_0)->opacityTexture_0).read(vec<uint,2>(((_S55)).xy), uint(((_S55)).z))) * float4(255.0f) );
            int2 _S56 = int2(metadata_4.zw);
            int2 tile_4 = int2(floor(_S1.texCoord_0)) - int2(metadata_4.xy);
            if(any(tile_4 < (int2(int(0)) )))
            {
                hasSceneLighting_0 = true;
            }
            else
            {
                hasSceneLighting_0 = any(tile_4 >= _S56);
            }
            if(hasSceneLighting_0)
            {
                int3 _S57 = int3(int(min(1U, atlasWidth_4 - 1U)), int(0), int(0));
                _S20 = (((&kernelContext_0)->opacityTexture_0).read(vec<uint,2>(((_S57)).xy), uint(((_S57)).z)));
                break;
            }
            uint _S58 = atlasWidth_4 / uint(_S56.x);
            float _S59 = float(_S58);
            uint _S60 = (atlasHeight_4 - 1U) / uint(_S56.y);
            float2 cellSize_4 = float2(_S59, float(_S60));
            _S20 = (((&kernelContext_0)->opacityTexture_0).sample(((&kernelContext_0)->opacitySampler_0), ((float2(tile_4) * cellSize_4 + float2(1.5f, 2.5f) + fract(_S1.texCoord_0) * max(cellSize_4 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_4), float(atlasHeight_4)))));
            break;
        }
        opacity_0 = saturate(_S20.x);
    }
    float occlusion_0;
    if((textureMask_0 & 128U) != 0U)
    {
        bool _S61 = (udimMask_0 & 128U) != 0U;
        for(;;)
        {
            if(!_S61)
            {
                _S20 = (((&kernelContext_0)->occlusionTexture_0).sample(((&kernelContext_0)->occlusionSampler_0), (_S1.texCoord_0)));
                break;
            }
            texture2d<float, access::sample> _S62 = (&kernelContext_0)->occlusionTexture_0;
            thread uint atlasWidth_5;
            thread uint atlasHeight_5;
            (*((&atlasWidth_5)) = (_S62).get_width(0)),(*((&atlasHeight_5)) = (_S62).get_height(0));
            int3 _S63 = int3(int(0), int(0), int(0));
            float4 metadata_5 = round((((&kernelContext_0)->occlusionTexture_0).read(vec<uint,2>(((_S63)).xy), uint(((_S63)).z))) * float4(255.0f) );
            int2 _S64 = int2(metadata_5.zw);
            int2 tile_5 = int2(floor(_S1.texCoord_0)) - int2(metadata_5.xy);
            if(any(tile_5 < (int2(int(0)) )))
            {
                hasSceneLighting_0 = true;
            }
            else
            {
                hasSceneLighting_0 = any(tile_5 >= _S64);
            }
            if(hasSceneLighting_0)
            {
                int3 _S65 = int3(int(min(1U, atlasWidth_5 - 1U)), int(0), int(0));
                _S20 = (((&kernelContext_0)->occlusionTexture_0).read(vec<uint,2>(((_S65)).xy), uint(((_S65)).z)));
                break;
            }
            uint _S66 = atlasWidth_5 / uint(_S64.x);
            float _S67 = float(_S66);
            uint _S68 = (atlasHeight_5 - 1U) / uint(_S64.y);
            float2 cellSize_5 = float2(_S67, float(_S68));
            _S20 = (((&kernelContext_0)->occlusionTexture_0).sample(((&kernelContext_0)->occlusionSampler_0), ((float2(tile_5) * cellSize_5 + float2(1.5f, 2.5f) + fract(_S1.texCoord_0) * max(cellSize_5 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_5), float(atlasHeight_5)))));
            break;
        }
        occlusion_0 = saturate(_S20.x);
    }
    else
    {
        occlusion_0 = _S11;
    }
    float3 specularColor_0;
    if((textureMask_0 & 256U) != 0U)
    {
        bool _S69 = (udimMask_0 & 256U) != 0U;
        for(;;)
        {
            if(!_S69)
            {
                _S20 = (((&kernelContext_0)->specularColorTexture_0).sample(((&kernelContext_0)->specularColorSampler_0), (_S1.texCoord_0)));
                break;
            }
            texture2d<float, access::sample> _S70 = (&kernelContext_0)->specularColorTexture_0;
            thread uint atlasWidth_6;
            thread uint atlasHeight_6;
            (*((&atlasWidth_6)) = (_S70).get_width(0)),(*((&atlasHeight_6)) = (_S70).get_height(0));
            int3 _S71 = int3(int(0), int(0), int(0));
            float4 metadata_6 = round((((&kernelContext_0)->specularColorTexture_0).read(vec<uint,2>(((_S71)).xy), uint(((_S71)).z))) * float4(255.0f) );
            int2 _S72 = int2(metadata_6.zw);
            int2 tile_6 = int2(floor(_S1.texCoord_0)) - int2(metadata_6.xy);
            if(any(tile_6 < (int2(int(0)) )))
            {
                hasSceneLighting_0 = true;
            }
            else
            {
                hasSceneLighting_0 = any(tile_6 >= _S72);
            }
            if(hasSceneLighting_0)
            {
                int3 _S73 = int3(int(min(1U, atlasWidth_6 - 1U)), int(0), int(0));
                _S20 = (((&kernelContext_0)->specularColorTexture_0).read(vec<uint,2>(((_S73)).xy), uint(((_S73)).z)));
                break;
            }
            uint _S74 = atlasWidth_6 / uint(_S72.x);
            float _S75 = float(_S74);
            uint _S76 = (atlasHeight_6 - 1U) / uint(_S72.y);
            float2 cellSize_6 = float2(_S75, float(_S76));
            _S20 = (((&kernelContext_0)->specularColorTexture_0).sample(((&kernelContext_0)->specularColorSampler_0), ((float2(tile_6) * cellSize_6 + float2(1.5f, 2.5f) + fract(_S1.texCoord_0) * max(cellSize_6 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_6), float(atlasHeight_6)))));
            break;
        }
        specularColor_0 = saturate(_S20.xyz);
    }
    else
    {
        specularColor_0 = _S18;
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
    float3 normal_1 = normalize(_S1.normal_0);
    float lengthSquared_0 = dot(_S1.eyePosition_0, _S1.eyePosition_0);
    float3 _S77;
    if(lengthSquared_0 > 0.00100000004749745f)
    {
        _S77 = - _S1.eyePosition_0 * float3(rsqrt(lengthSquared_0)) ;
    }
    else
    {
        _S77 = float3(0.0f, 0.0f, 1.0f);
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
    float _S78 = saturate(abs(dot(normal_2, _S77)) + 0.00000999999974738f);
    float _S79 = max(0.00100000004749745f, roughness_0);
    float _S80 = _S8.x;
    float _S81 = max(0.00100000004749745f, _S8.y);
    float _S82 = _S17.w;
    float reflectanceRatio_0 = (1.0f - _S82) / (1.0f + _S82);
    float3 _S83 = float3(3.14159274101257324f) ;
    float3 diffuse_0 = diffuseColor_0 / _S83;
    float3 normalIncidence_0;
    float3 grazingIncidence_0;
    float3 diffuse_1;
    if((_S14.w) >= 0.5f)
    {
        float3 _S84 = float3(1.0f, 1.0f, 1.0f);
        normalIncidence_0 = specularColor_0;
        grazingIncidence_0 = _S84;
        diffuse_1 = diffuse_0;
    }
    else
    {
        float3 _S85 = float3(metallic_0) ;
        float3 specularTint_0 = mix(float3(1.0f, 1.0f, 1.0f), diffuseColor_0, _S85);
        float3 diffuse_2 = diffuse_0 * float3((1.0f - metallic_0)) ;
        normalIncidence_0 = mix(float3((reflectanceRatio_0 * reflectanceRatio_0))  * specularTint_0, specularTint_0, _S85);
        grazingIncidence_0 = specularTint_0;
        diffuse_1 = diffuse_2;
    }
    float4 _S86 = float4(_S4->ambientLight_0) ;
    float _S87 = _S86.w;
    if(_S87 > 0.5f)
    {
        hasSceneLighting_0 = true;
    }
    else
    {
        float3 _S88 = _S86.xyz;
        hasSceneLighting_0 = (dot(_S88, _S88)) > 0.0f;
    }
    uint _S89 = min(uint(_S87), 8U);
    matrix<float,int(4),int(4)>  _S90 = matrix<float,int(4),int(4)> (_S4->eyeToWorld_0.data_0[int(0)][int(0)], _S4->eyeToWorld_0.data_0[int(0)][int(1)], _S4->eyeToWorld_0.data_0[int(0)][int(2)], _S4->eyeToWorld_0.data_0[int(0)][int(3)], _S4->eyeToWorld_0.data_0[int(1)][int(0)], _S4->eyeToWorld_0.data_0[int(1)][int(1)], _S4->eyeToWorld_0.data_0[int(1)][int(2)], _S4->eyeToWorld_0.data_0[int(1)][int(3)], _S4->eyeToWorld_0.data_0[int(2)][int(0)], _S4->eyeToWorld_0.data_0[int(2)][int(1)], _S4->eyeToWorld_0.data_0[int(2)][int(2)], _S4->eyeToWorld_0.data_0[int(2)][int(3)], _S4->eyeToWorld_0.data_0[int(3)][int(0)], _S4->eyeToWorld_0.data_0[int(3)][int(1)], _S4->eyeToWorld_0.data_0[int(3)][int(2)], _S4->eyeToWorld_0.data_0[int(3)][int(3)]);
    float3 _S91 = normalize((((float4(_S77, 0.0f)) * (_S90))).xyz);
    float3 _S92 = (((float4(_S1.eyePosition_0, 1.0f)) * (_S90))).xyz;
    float4 _S93 = float4(_S3->lightColorAmbient_0) ;
    float3 color_0 = diffuseColor_0 * float3(_S93.w)  + diffuseColor_0 * _S86.xyz;
    bool _S94 = !hasSceneLighting_0;
    uint lightCount_0;
    if(_S94)
    {
        lightCount_0 = 1U;
    }
    else
    {
        lightCount_0 = _S89;
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
        bool _S95 = lightIndex_0 == 0U;
        if(_S95)
        {
            hasSceneLighting_0 = _S94;
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
        bool _S96;
        if(_S95)
        {
            _S96 = _S94;
        }
        else
        {
            _S96 = false;
        }
        float3 lightDirection_0;
        if(_S96)
        {
            lightDirection_0 = normalize((float4(_S3->lightDirectionIntensity_0) ).xyz);
        }
        else
        {
            lightDirection_0 = normalize((float4((&_S4->lightDirectionRadius_0)->data_1[lightIndex_0]) ).xyz);
        }
        bool _S97;
        if(_S95)
        {
            _S97 = _S94;
        }
        else
        {
            _S97 = false;
        }
        if(_S97)
        {
            roughness_0 = (float4(_S3->lightDirectionIntensity_0) ).w;
        }
        else
        {
            roughness_0 = (float4((&_S4->lightColorIntensity_0)->data_1[lightIndex_0]) ).w;
        }
        bool _S98;
        if(_S95)
        {
            _S98 = _S94;
        }
        else
        {
            _S98 = false;
        }
        if(_S98)
        {
            diffuseColor_0 = _S93.xyz;
        }
        else
        {
            diffuseColor_0 = (float4((&_S4->lightColorIntensity_0)->data_1[lightIndex_0]) ).xyz;
        }
        bool _S99;
        if(_S95)
        {
            _S99 = _S94;
        }
        else
        {
            _S99 = false;
        }
        if(_S99)
        {
            metallic_0 = 1.0f;
        }
        else
        {
            metallic_0 = (float4((&_S4->lightControls_0)->data_1[lightIndex_0]) ).x;
        }
        bool _S100;
        if(_S95)
        {
            _S100 = _S94;
        }
        else
        {
            _S100 = false;
        }
        float _S101;
        if(_S100)
        {
            _S101 = 1.0f;
        }
        else
        {
            _S101 = (float4((&_S4->lightControls_0)->data_1[lightIndex_0]) ).y;
        }
        bool _S102;
        if(_S95)
        {
            _S102 = _S94;
        }
        else
        {
            _S102 = false;
        }
        float3 lightTangent_0;
        if(_S102)
        {
            lightTangent_0 = float3(1.0f, 0.0f, 0.0f);
        }
        else
        {
            lightTangent_0 = normalize((float4((&_S4->lightTangentShapeX_0)->data_1[lightIndex_0]) ).xyz);
        }
        bool _S103;
        if(_S95)
        {
            _S103 = _S94;
        }
        else
        {
            _S103 = false;
        }
        float3 lightBitangent_0;
        if(_S103)
        {
            lightBitangent_0 = float3(0.0f, 1.0f, 0.0f);
        }
        else
        {
            lightBitangent_0 = normalize((float4((&_S4->lightBitangentShapeY_0)->data_1[lightIndex_0]) ).xyz);
        }
        bool _S104;
        if(_S95)
        {
            _S104 = _S94;
        }
        else
        {
            _S104 = false;
        }
        float shapeX_0;
        if(_S104)
        {
            shapeX_0 = 0.0f;
        }
        else
        {
            shapeX_0 = (float4((&_S4->lightTangentShapeX_0)->data_1[lightIndex_0]) ).w;
        }
        bool _S105;
        if(_S95)
        {
            _S105 = _S94;
        }
        else
        {
            _S105 = false;
        }
        float shapeY_0;
        if(_S105)
        {
            shapeY_0 = 0.0f;
        }
        else
        {
            shapeY_0 = (float4((&_S4->lightBitangentShapeY_0)->data_1[lightIndex_0]) ).w;
        }
        bool _S106;
        if(_S95)
        {
            _S106 = _S94;
        }
        else
        {
            _S106 = false;
        }
        float lightRadius_0;
        if(_S106)
        {
            lightRadius_0 = 0.0f;
        }
        else
        {
            lightRadius_0 = (float4((&_S4->lightDirectionRadius_0)->data_1[lightIndex_0]) ).w;
        }
        bool _S107;
        if(_S95)
        {
            _S107 = _S94;
        }
        else
        {
            _S107 = false;
        }
        if(_S107)
        {
            specularColor_0 = _S77;
        }
        else
        {
            specularColor_0 = _S91;
        }
        thread array<float3, int(5)> sampleOffsets_0;
        float3 _S108 = float3(0.0f, 0.0f, 0.0f);
        sampleOffsets_0[int(0)] = _S108;
        sampleOffsets_0[int(1)] = _S108;
        sampleOffsets_0[int(2)] = _S108;
        sampleOffsets_0[int(3)] = _S108;
        sampleOffsets_0[int(4)] = _S108;
        float sampleCount_0;
        if(lightType_0 == 3.0f)
        {
            float3 halfWidth_0 = lightTangent_0 * float3((shapeX_0 * 0.5f)) ;
            float3 halfHeight_0 = lightBitangent_0 * float3((shapeY_0 * 0.5f)) ;
            sampleOffsets_0[int(1)] = halfWidth_0 + halfHeight_0;
            sampleOffsets_0[int(2)] = halfWidth_0 - halfHeight_0;
            float3 _S109 = - halfWidth_0;
            sampleOffsets_0[int(3)] = _S109 + halfHeight_0;
            sampleOffsets_0[int(4)] = _S109 - halfHeight_0;
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
                float3 toLight_0 = (float4((&_S4->lightPositionType_0)->data_1[lightIndex_0]) ).xyz + sampleOffsets_0[sampleIndex_0] - _S92;
                float _S110 = max(dot(toLight_0, toLight_0), 0.00100000004749745f);
                float3 sampleDirection_1 = toLight_0 * float3(rsqrt(_S110)) ;
                float sampleIntensity_2 = sampleIntensity_0 / _S110;
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
            float3 _S111 = float3(pow(max(0.0f, 1.0f - saturate(dot(specularColor_0, half_0))), 5.0f)) ;
            float3 _S112 = mix(normalIncidence_0, grazingIncidence_0, _S111);
            float3 directDiffuse_0 = diffuse_1 * (float3(1.0f)  - _S112);
            float alpha_0 = _S79 * _S79;
            float alphaSquared_0 = alpha_0 * alpha_0;
            float _S113 = normalDotHalf_0 * normalDotHalf_0;
            float denominator_0 = _S113 * (alphaSquared_0 - 1.0f) + 1.0f;
            float k_0 = alpha_0 * 0.5f;
            float _S114 = 1.0f - k_0;
            float3 _S115 = float3((4.0f * normalDotLight_0 * _S78 + 0.00100000004749745f)) ;
            float3 _S116 = _S112 * float3((_S78 / (_S78 * _S114 + k_0) * (normalDotLight_0 / (normalDotLight_0 * _S114 + k_0))))  * float3(((alphaSquared_0 + 0.00100000004749745f) / (denominator_0 * denominator_0 * 3.14159274101257324f)))  / _S115;
            float3 directSpecular_0;
            if(_S80 > 0.0f)
            {
                float alpha_1 = _S81 * _S81;
                float alphaSquared_1 = alpha_1 * alpha_1;
                float denominator_1 = _S113 * (alphaSquared_1 - 1.0f) + 1.0f;
                float k_1 = alpha_1 * 0.5f;
                float _S117 = 1.0f - k_1;
                directSpecular_0 = _S116 + float3(_S80)  * (mix(float3((reflectanceRatio_0 * reflectanceRatio_0)) , float3(1.0f, 1.0f, 1.0f), _S111) * float3((_S78 / (_S78 * _S117 + k_1) * (normalDotLight_0 / (normalDotLight_0 * _S117 + k_1))))  * float3(((alphaSquared_1 + 0.00100000004749745f) / (denominator_1 * denominator_1 * 3.14159274101257324f)))  / _S115);
            }
            else
            {
                directSpecular_0 = _S116;
            }
            float3 _S118 = diffuseColor_0 * float3(sampleIntensity_1) ;
            color_2 = color_2 + float3((occlusion_0 * emissionScale_0 * normalDotLight_0))  * (directDiffuse_0 * float3(metallic_0)  * (_S118 * _S83) + directSpecular_0 * float3(_S101)  * _S118);
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
    pixelOutput_0 _S119 = { float4(color_1, opacity_0) };
    return _S119;
}

