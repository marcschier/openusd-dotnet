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
    texture2d<float, access::sample> baseColorTexture_0;
    sampler baseColorSampler_0;
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
};

[[fragment]] pixelOutput_0 fragmentMain_uv_material_normal(pixelInput_0 _S1 [[stage_in]], bool isFrontFace_0 [[front_facing]], float4 position_0 [[position]], SurfaceParameters_natural_0 device* surfaceParameters_1 [[buffer(7)]], FrameParameters_natural_0 device* frameParameters_1 [[buffer(8)]], texture2d<float, access::sample> baseColorTexture_1 [[texture(0)]], sampler baseColorSampler_1 [[sampler(0)]], texture2d<float, access::sample> normalTexture_1 [[texture(1)]], sampler normalSampler_1 [[sampler(1)]], texture2d<float, access::sample> roughnessMetallicTexture_1 [[texture(2)]], sampler roughnessMetallicSampler_1 [[sampler(2)]], texture2d<float, access::sample> metallicTexture_1 [[texture(4)]], sampler metallicSampler_1 [[sampler(5)]], texture2d<float, access::sample> emissiveTexture_1 [[texture(3)]], sampler emissiveSampler_1 [[sampler(3)]], texture2d<float, access::sample> opacityTexture_1 [[texture(5)]], sampler opacitySampler_1 [[sampler(6)]], texture2d<float, access::sample> occlusionTexture_1 [[texture(10)]], sampler occlusionSampler_1 [[sampler(7)]], texture2d<float, access::sample> specularColorTexture_1 [[texture(11)]], sampler specularColorSampler_1 [[sampler(8)]], texture2d<float, access::sample> clearcoatTexture_1 [[texture(12)]], sampler clearcoatSampler_1 [[sampler(9)]], texture2d<float, access::sample> clearcoatRoughnessTexture_1 [[texture(13)]], sampler clearcoatRoughnessSampler_1 [[sampler(10)]])
{
    uint4 _S2;
    thread KernelContext_0 kernelContext_0;
    (&kernelContext_0)->surfaceParameters_0 = surfaceParameters_1;
    (&kernelContext_0)->frameParameters_0 = frameParameters_1;
    (&kernelContext_0)->baseColorTexture_0 = baseColorTexture_1;
    (&kernelContext_0)->baseColorSampler_0 = baseColorSampler_1;
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
    float _S19 = _S8.x;
    float _S20 = _S8.y;
    float4 _S21 = float4(_S3->textureControls_0) ;
    uint textureMask_0 = uint(round(_S21.x));
    uint udimMask_0 = uint(round(_S21.y));
    bool hasSceneLighting_0;
    float4 _S22;
    if((textureMask_0 & 2U) != 0U)
    {
        bool _S23 = (udimMask_0 & 2U) != 0U;
        for(;;)
        {
            if(!_S23)
            {
                _S22 = (((&kernelContext_0)->baseColorTexture_0).sample(((&kernelContext_0)->baseColorSampler_0), (_S1.texCoord_0)));
                break;
            }
            texture2d<float, access::sample> _S24 = (&kernelContext_0)->baseColorTexture_0;
            thread uint atlasWidth_0;
            thread uint atlasHeight_0;
            (*((&atlasWidth_0)) = (_S24).get_width(0)),(*((&atlasHeight_0)) = (_S24).get_height(0));
            int3 _S25 = int3(int(0), int(0), int(0));
            float4 metadata_0 = round((((&kernelContext_0)->baseColorTexture_0).read(vec<uint,2>(((_S25)).xy), uint(((_S25)).z))) * float4(255.0f) );
            int2 _S26 = int2(metadata_0.zw);
            int2 tile_0 = int2(floor(_S1.texCoord_0)) - int2(metadata_0.xy);
            if(any(tile_0 < (int2(int(0)) )))
            {
                hasSceneLighting_0 = true;
            }
            else
            {
                hasSceneLighting_0 = any(tile_0 >= _S26);
            }
            if(hasSceneLighting_0)
            {
                int3 _S27 = int3(int(min(1U, atlasWidth_0 - 1U)), int(0), int(0));
                _S22 = (((&kernelContext_0)->baseColorTexture_0).read(vec<uint,2>(((_S27)).xy), uint(((_S27)).z)));
                break;
            }
            uint _S28 = atlasWidth_0 / uint(_S26.x);
            float _S29 = float(_S28);
            uint _S30 = (atlasHeight_0 - 1U) / uint(_S26.y);
            float2 cellSize_0 = float2(_S29, float(_S30));
            _S22 = (((&kernelContext_0)->baseColorTexture_0).sample(((&kernelContext_0)->baseColorSampler_0), ((float2(tile_0) * cellSize_0 + float2(1.5f, 2.5f) + fract(_S1.texCoord_0) * max(cellSize_0 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_0), float(atlasHeight_0)))));
            break;
        }
        diffuseColor_0 = _S22.xyz;
    }
    bool _S31 = (udimMask_0 & 4U) != 0U;
    for(;;)
    {
        if(!_S31)
        {
            _S22 = (((&kernelContext_0)->normalTexture_0).sample(((&kernelContext_0)->normalSampler_0), (_S1.texCoord_0)));
            break;
        }
        texture2d<float, access::sample> _S32 = (&kernelContext_0)->normalTexture_0;
        thread uint atlasWidth_1;
        thread uint atlasHeight_1;
        (*((&atlasWidth_1)) = (_S32).get_width(0)),(*((&atlasHeight_1)) = (_S32).get_height(0));
        int3 _S33 = int3(int(0), int(0), int(0));
        float4 metadata_1 = round((((&kernelContext_0)->normalTexture_0).read(vec<uint,2>(((_S33)).xy), uint(((_S33)).z))) * float4(255.0f) );
        int2 _S34 = int2(metadata_1.zw);
        int2 tile_1 = int2(floor(_S1.texCoord_0)) - int2(metadata_1.xy);
        if(any(tile_1 < (int2(int(0)) )))
        {
            hasSceneLighting_0 = true;
        }
        else
        {
            hasSceneLighting_0 = any(tile_1 >= _S34);
        }
        if(hasSceneLighting_0)
        {
            int3 _S35 = int3(int(min(1U, atlasWidth_1 - 1U)), int(0), int(0));
            _S22 = (((&kernelContext_0)->normalTexture_0).read(vec<uint,2>(((_S35)).xy), uint(((_S35)).z)));
            break;
        }
        uint _S36 = atlasWidth_1 / uint(_S34.x);
        float _S37 = float(_S36);
        uint _S38 = (atlasHeight_1 - 1U) / uint(_S34.y);
        float2 cellSize_1 = float2(_S37, float(_S38));
        _S22 = (((&kernelContext_0)->normalTexture_0).sample(((&kernelContext_0)->normalSampler_0), ((float2(tile_1) * cellSize_1 + float2(1.5f, 2.5f) + fract(_S1.texCoord_0) * max(cellSize_1 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_1), float(atlasHeight_1)))));
        break;
    }
    float3 _S39 = float3(1.0f) ;
    float3 sampledNormal_0 = _S22.xyz * float3(2.0f)  - _S39;
    float3 tangent_1 = normalize(_S1.tangent_0.xyz);
    float3 shadingNormal_0 = normalize(tangent_1 * float3(sampledNormal_0.x)  + cross(normalize(_S1.normal_0), tangent_1) * float3(_S1.tangent_0.w)  * float3(sampledNormal_0.y)  + _S1.normal_0 * float3(sampledNormal_0.z) );
    float roughness_0;
    if((textureMask_0 & 8U) != 0U)
    {
        bool _S40 = (udimMask_0 & 8U) != 0U;
        for(;;)
        {
            if(!_S40)
            {
                _S22 = (((&kernelContext_0)->roughnessMetallicTexture_0).sample(((&kernelContext_0)->roughnessMetallicSampler_0), (_S1.texCoord_0)));
                break;
            }
            texture2d<float, access::sample> _S41 = (&kernelContext_0)->roughnessMetallicTexture_0;
            thread uint atlasWidth_2;
            thread uint atlasHeight_2;
            (*((&atlasWidth_2)) = (_S41).get_width(0)),(*((&atlasHeight_2)) = (_S41).get_height(0));
            int3 _S42 = int3(int(0), int(0), int(0));
            float4 metadata_2 = round((((&kernelContext_0)->roughnessMetallicTexture_0).read(vec<uint,2>(((_S42)).xy), uint(((_S42)).z))) * float4(255.0f) );
            int2 _S43 = int2(metadata_2.zw);
            int2 tile_2 = int2(floor(_S1.texCoord_0)) - int2(metadata_2.xy);
            if(any(tile_2 < (int2(int(0)) )))
            {
                hasSceneLighting_0 = true;
            }
            else
            {
                hasSceneLighting_0 = any(tile_2 >= _S43);
            }
            if(hasSceneLighting_0)
            {
                int3 _S44 = int3(int(min(1U, atlasWidth_2 - 1U)), int(0), int(0));
                _S22 = (((&kernelContext_0)->roughnessMetallicTexture_0).read(vec<uint,2>(((_S44)).xy), uint(((_S44)).z)));
                break;
            }
            uint _S45 = atlasWidth_2 / uint(_S43.x);
            float _S46 = float(_S45);
            uint _S47 = (atlasHeight_2 - 1U) / uint(_S43.y);
            float2 cellSize_2 = float2(_S46, float(_S47));
            _S22 = (((&kernelContext_0)->roughnessMetallicTexture_0).sample(((&kernelContext_0)->roughnessMetallicSampler_0), ((float2(tile_2) * cellSize_2 + float2(1.5f, 2.5f) + fract(_S1.texCoord_0) * max(cellSize_2 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_2), float(atlasHeight_2)))));
            break;
        }
        roughness_0 = clamp(_S22.x, 0.00999999977648258f, 1.0f);
    }
    else
    {
        roughness_0 = _S16;
    }
    float metallic_0;
    if((textureMask_0 & 32U) != 0U)
    {
        bool _S48 = (udimMask_0 & 32U) != 0U;
        for(;;)
        {
            if(!_S48)
            {
                _S22 = (((&kernelContext_0)->metallicTexture_0).sample(((&kernelContext_0)->metallicSampler_0), (_S1.texCoord_0)));
                break;
            }
            texture2d<float, access::sample> _S49 = (&kernelContext_0)->metallicTexture_0;
            thread uint atlasWidth_3;
            thread uint atlasHeight_3;
            (*((&atlasWidth_3)) = (_S49).get_width(0)),(*((&atlasHeight_3)) = (_S49).get_height(0));
            int3 _S50 = int3(int(0), int(0), int(0));
            float4 metadata_3 = round((((&kernelContext_0)->metallicTexture_0).read(vec<uint,2>(((_S50)).xy), uint(((_S50)).z))) * float4(255.0f) );
            int2 _S51 = int2(metadata_3.zw);
            int2 tile_3 = int2(floor(_S1.texCoord_0)) - int2(metadata_3.xy);
            if(any(tile_3 < (int2(int(0)) )))
            {
                hasSceneLighting_0 = true;
            }
            else
            {
                hasSceneLighting_0 = any(tile_3 >= _S51);
            }
            if(hasSceneLighting_0)
            {
                int3 _S52 = int3(int(min(1U, atlasWidth_3 - 1U)), int(0), int(0));
                _S22 = (((&kernelContext_0)->metallicTexture_0).read(vec<uint,2>(((_S52)).xy), uint(((_S52)).z)));
                break;
            }
            uint _S53 = atlasWidth_3 / uint(_S51.x);
            float _S54 = float(_S53);
            uint _S55 = (atlasHeight_3 - 1U) / uint(_S51.y);
            float2 cellSize_3 = float2(_S54, float(_S55));
            _S22 = (((&kernelContext_0)->metallicTexture_0).sample(((&kernelContext_0)->metallicSampler_0), ((float2(tile_3) * cellSize_3 + float2(1.5f, 2.5f) + fract(_S1.texCoord_0) * max(cellSize_3 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_3), float(atlasHeight_3)))));
            break;
        }
        metallic_0 = saturate(_S22.x);
    }
    else
    {
        metallic_0 = _S15;
    }
    float3 emissiveColor_0;
    if((textureMask_0 & 16U) != 0U)
    {
        bool _S56 = (udimMask_0 & 16U) != 0U;
        for(;;)
        {
            if(!_S56)
            {
                _S22 = (((&kernelContext_0)->emissiveTexture_0).sample(((&kernelContext_0)->emissiveSampler_0), (_S1.texCoord_0)));
                break;
            }
            texture2d<float, access::sample> _S57 = (&kernelContext_0)->emissiveTexture_0;
            thread uint atlasWidth_4;
            thread uint atlasHeight_4;
            (*((&atlasWidth_4)) = (_S57).get_width(0)),(*((&atlasHeight_4)) = (_S57).get_height(0));
            int3 _S58 = int3(int(0), int(0), int(0));
            float4 metadata_4 = round((((&kernelContext_0)->emissiveTexture_0).read(vec<uint,2>(((_S58)).xy), uint(((_S58)).z))) * float4(255.0f) );
            int2 _S59 = int2(metadata_4.zw);
            int2 tile_4 = int2(floor(_S1.texCoord_0)) - int2(metadata_4.xy);
            if(any(tile_4 < (int2(int(0)) )))
            {
                hasSceneLighting_0 = true;
            }
            else
            {
                hasSceneLighting_0 = any(tile_4 >= _S59);
            }
            if(hasSceneLighting_0)
            {
                int3 _S60 = int3(int(min(1U, atlasWidth_4 - 1U)), int(0), int(0));
                _S22 = (((&kernelContext_0)->emissiveTexture_0).read(vec<uint,2>(((_S60)).xy), uint(((_S60)).z)));
                break;
            }
            uint _S61 = atlasWidth_4 / uint(_S59.x);
            float _S62 = float(_S61);
            uint _S63 = (atlasHeight_4 - 1U) / uint(_S59.y);
            float2 cellSize_4 = float2(_S62, float(_S63));
            _S22 = (((&kernelContext_0)->emissiveTexture_0).sample(((&kernelContext_0)->emissiveSampler_0), ((float2(tile_4) * cellSize_4 + float2(1.5f, 2.5f) + fract(_S1.texCoord_0) * max(cellSize_4 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_4), float(atlasHeight_4)))));
            break;
        }
        emissiveColor_0 = _S22.xyz;
    }
    else
    {
        emissiveColor_0 = _S10;
    }
    if((textureMask_0 & 64U) != 0U)
    {
        bool _S64 = (udimMask_0 & 64U) != 0U;
        for(;;)
        {
            if(!_S64)
            {
                _S22 = (((&kernelContext_0)->opacityTexture_0).sample(((&kernelContext_0)->opacitySampler_0), (_S1.texCoord_0)));
                break;
            }
            texture2d<float, access::sample> _S65 = (&kernelContext_0)->opacityTexture_0;
            thread uint atlasWidth_5;
            thread uint atlasHeight_5;
            (*((&atlasWidth_5)) = (_S65).get_width(0)),(*((&atlasHeight_5)) = (_S65).get_height(0));
            int3 _S66 = int3(int(0), int(0), int(0));
            float4 metadata_5 = round((((&kernelContext_0)->opacityTexture_0).read(vec<uint,2>(((_S66)).xy), uint(((_S66)).z))) * float4(255.0f) );
            int2 _S67 = int2(metadata_5.zw);
            int2 tile_5 = int2(floor(_S1.texCoord_0)) - int2(metadata_5.xy);
            if(any(tile_5 < (int2(int(0)) )))
            {
                hasSceneLighting_0 = true;
            }
            else
            {
                hasSceneLighting_0 = any(tile_5 >= _S67);
            }
            if(hasSceneLighting_0)
            {
                int3 _S68 = int3(int(min(1U, atlasWidth_5 - 1U)), int(0), int(0));
                _S22 = (((&kernelContext_0)->opacityTexture_0).read(vec<uint,2>(((_S68)).xy), uint(((_S68)).z)));
                break;
            }
            uint _S69 = atlasWidth_5 / uint(_S67.x);
            float _S70 = float(_S69);
            uint _S71 = (atlasHeight_5 - 1U) / uint(_S67.y);
            float2 cellSize_5 = float2(_S70, float(_S71));
            _S22 = (((&kernelContext_0)->opacityTexture_0).sample(((&kernelContext_0)->opacitySampler_0), ((float2(tile_5) * cellSize_5 + float2(1.5f, 2.5f) + fract(_S1.texCoord_0) * max(cellSize_5 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_5), float(atlasHeight_5)))));
            break;
        }
        opacity_0 = saturate(_S22.x);
    }
    float occlusion_0;
    if((textureMask_0 & 128U) != 0U)
    {
        bool _S72 = (udimMask_0 & 128U) != 0U;
        for(;;)
        {
            if(!_S72)
            {
                _S22 = (((&kernelContext_0)->occlusionTexture_0).sample(((&kernelContext_0)->occlusionSampler_0), (_S1.texCoord_0)));
                break;
            }
            texture2d<float, access::sample> _S73 = (&kernelContext_0)->occlusionTexture_0;
            thread uint atlasWidth_6;
            thread uint atlasHeight_6;
            (*((&atlasWidth_6)) = (_S73).get_width(0)),(*((&atlasHeight_6)) = (_S73).get_height(0));
            int3 _S74 = int3(int(0), int(0), int(0));
            float4 metadata_6 = round((((&kernelContext_0)->occlusionTexture_0).read(vec<uint,2>(((_S74)).xy), uint(((_S74)).z))) * float4(255.0f) );
            int2 _S75 = int2(metadata_6.zw);
            int2 tile_6 = int2(floor(_S1.texCoord_0)) - int2(metadata_6.xy);
            if(any(tile_6 < (int2(int(0)) )))
            {
                hasSceneLighting_0 = true;
            }
            else
            {
                hasSceneLighting_0 = any(tile_6 >= _S75);
            }
            if(hasSceneLighting_0)
            {
                int3 _S76 = int3(int(min(1U, atlasWidth_6 - 1U)), int(0), int(0));
                _S22 = (((&kernelContext_0)->occlusionTexture_0).read(vec<uint,2>(((_S76)).xy), uint(((_S76)).z)));
                break;
            }
            uint _S77 = atlasWidth_6 / uint(_S75.x);
            float _S78 = float(_S77);
            uint _S79 = (atlasHeight_6 - 1U) / uint(_S75.y);
            float2 cellSize_6 = float2(_S78, float(_S79));
            _S22 = (((&kernelContext_0)->occlusionTexture_0).sample(((&kernelContext_0)->occlusionSampler_0), ((float2(tile_6) * cellSize_6 + float2(1.5f, 2.5f) + fract(_S1.texCoord_0) * max(cellSize_6 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_6), float(atlasHeight_6)))));
            break;
        }
        occlusion_0 = saturate(_S22.x);
    }
    else
    {
        occlusion_0 = _S11;
    }
    float3 specularColor_0;
    if((textureMask_0 & 256U) != 0U)
    {
        bool _S80 = (udimMask_0 & 256U) != 0U;
        for(;;)
        {
            if(!_S80)
            {
                _S22 = (((&kernelContext_0)->specularColorTexture_0).sample(((&kernelContext_0)->specularColorSampler_0), (_S1.texCoord_0)));
                break;
            }
            texture2d<float, access::sample> _S81 = (&kernelContext_0)->specularColorTexture_0;
            thread uint atlasWidth_7;
            thread uint atlasHeight_7;
            (*((&atlasWidth_7)) = (_S81).get_width(0)),(*((&atlasHeight_7)) = (_S81).get_height(0));
            int3 _S82 = int3(int(0), int(0), int(0));
            float4 metadata_7 = round((((&kernelContext_0)->specularColorTexture_0).read(vec<uint,2>(((_S82)).xy), uint(((_S82)).z))) * float4(255.0f) );
            int2 _S83 = int2(metadata_7.zw);
            int2 tile_7 = int2(floor(_S1.texCoord_0)) - int2(metadata_7.xy);
            if(any(tile_7 < (int2(int(0)) )))
            {
                hasSceneLighting_0 = true;
            }
            else
            {
                hasSceneLighting_0 = any(tile_7 >= _S83);
            }
            if(hasSceneLighting_0)
            {
                int3 _S84 = int3(int(min(1U, atlasWidth_7 - 1U)), int(0), int(0));
                _S22 = (((&kernelContext_0)->specularColorTexture_0).read(vec<uint,2>(((_S84)).xy), uint(((_S84)).z)));
                break;
            }
            uint _S85 = atlasWidth_7 / uint(_S83.x);
            float _S86 = float(_S85);
            uint _S87 = (atlasHeight_7 - 1U) / uint(_S83.y);
            float2 cellSize_7 = float2(_S86, float(_S87));
            _S22 = (((&kernelContext_0)->specularColorTexture_0).sample(((&kernelContext_0)->specularColorSampler_0), ((float2(tile_7) * cellSize_7 + float2(1.5f, 2.5f) + fract(_S1.texCoord_0) * max(cellSize_7 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_7), float(atlasHeight_7)))));
            break;
        }
        specularColor_0 = saturate(_S22.xyz);
    }
    else
    {
        specularColor_0 = _S18;
    }
    float clearcoatAmount_0;
    if((textureMask_0 & 512U) != 0U)
    {
        bool _S88 = (udimMask_0 & 512U) != 0U;
        for(;;)
        {
            if(!_S88)
            {
                _S22 = (((&kernelContext_0)->clearcoatTexture_0).sample(((&kernelContext_0)->clearcoatSampler_0), (_S1.texCoord_0)));
                break;
            }
            texture2d<float, access::sample> _S89 = (&kernelContext_0)->clearcoatTexture_0;
            thread uint atlasWidth_8;
            thread uint atlasHeight_8;
            (*((&atlasWidth_8)) = (_S89).get_width(0)),(*((&atlasHeight_8)) = (_S89).get_height(0));
            int3 _S90 = int3(int(0), int(0), int(0));
            float4 metadata_8 = round((((&kernelContext_0)->clearcoatTexture_0).read(vec<uint,2>(((_S90)).xy), uint(((_S90)).z))) * float4(255.0f) );
            int2 _S91 = int2(metadata_8.zw);
            int2 tile_8 = int2(floor(_S1.texCoord_0)) - int2(metadata_8.xy);
            if(any(tile_8 < (int2(int(0)) )))
            {
                hasSceneLighting_0 = true;
            }
            else
            {
                hasSceneLighting_0 = any(tile_8 >= _S91);
            }
            if(hasSceneLighting_0)
            {
                int3 _S92 = int3(int(min(1U, atlasWidth_8 - 1U)), int(0), int(0));
                _S22 = (((&kernelContext_0)->clearcoatTexture_0).read(vec<uint,2>(((_S92)).xy), uint(((_S92)).z)));
                break;
            }
            uint _S93 = atlasWidth_8 / uint(_S91.x);
            float _S94 = float(_S93);
            uint _S95 = (atlasHeight_8 - 1U) / uint(_S91.y);
            float2 cellSize_8 = float2(_S94, float(_S95));
            _S22 = (((&kernelContext_0)->clearcoatTexture_0).sample(((&kernelContext_0)->clearcoatSampler_0), ((float2(tile_8) * cellSize_8 + float2(1.5f, 2.5f) + fract(_S1.texCoord_0) * max(cellSize_8 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_8), float(atlasHeight_8)))));
            break;
        }
        clearcoatAmount_0 = saturate(_S22.x);
    }
    else
    {
        clearcoatAmount_0 = _S19;
    }
    float clearcoatRoughness_0;
    if((textureMask_0 & 1024U) != 0U)
    {
        bool _S96 = (udimMask_0 & 1024U) != 0U;
        for(;;)
        {
            if(!_S96)
            {
                _S22 = (((&kernelContext_0)->clearcoatRoughnessTexture_0).sample(((&kernelContext_0)->clearcoatRoughnessSampler_0), (_S1.texCoord_0)));
                break;
            }
            texture2d<float, access::sample> _S97 = (&kernelContext_0)->clearcoatRoughnessTexture_0;
            thread uint atlasWidth_9;
            thread uint atlasHeight_9;
            (*((&atlasWidth_9)) = (_S97).get_width(0)),(*((&atlasHeight_9)) = (_S97).get_height(0));
            int3 _S98 = int3(int(0), int(0), int(0));
            float4 metadata_9 = round((((&kernelContext_0)->clearcoatRoughnessTexture_0).read(vec<uint,2>(((_S98)).xy), uint(((_S98)).z))) * float4(255.0f) );
            int2 _S99 = int2(metadata_9.zw);
            int2 tile_9 = int2(floor(_S1.texCoord_0)) - int2(metadata_9.xy);
            if(any(tile_9 < (int2(int(0)) )))
            {
                hasSceneLighting_0 = true;
            }
            else
            {
                hasSceneLighting_0 = any(tile_9 >= _S99);
            }
            if(hasSceneLighting_0)
            {
                int3 _S100 = int3(int(min(1U, atlasWidth_9 - 1U)), int(0), int(0));
                _S22 = (((&kernelContext_0)->clearcoatRoughnessTexture_0).read(vec<uint,2>(((_S100)).xy), uint(((_S100)).z)));
                break;
            }
            uint _S101 = atlasWidth_9 / uint(_S99.x);
            float _S102 = float(_S101);
            uint _S103 = (atlasHeight_9 - 1U) / uint(_S99.y);
            float2 cellSize_9 = float2(_S102, float(_S103));
            _S22 = (((&kernelContext_0)->clearcoatRoughnessTexture_0).sample(((&kernelContext_0)->clearcoatRoughnessSampler_0), ((float2(tile_9) * cellSize_9 + float2(1.5f, 2.5f) + fract(_S1.texCoord_0) * max(cellSize_9 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_9), float(atlasHeight_9)))));
            break;
        }
        clearcoatRoughness_0 = saturate(_S22.x);
    }
    else
    {
        clearcoatRoughness_0 = _S20;
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
    float3 _S104;
    if(lengthSquared_0 > 0.00100000004749745f)
    {
        _S104 = - _S1.eyePosition_0 * float3(rsqrt(lengthSquared_0)) ;
    }
    else
    {
        _S104 = float3(0.0f, 0.0f, 1.0f);
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
    float _S105 = saturate(abs(dot(normal_2, _S104)) + 0.00000999999974738f);
    float _S106 = max(0.00100000004749745f, roughness_0);
    float _S107 = max(0.00100000004749745f, clearcoatRoughness_0);
    float _S108 = _S17.w;
    float reflectanceRatio_0 = (1.0f - _S108) / (1.0f + _S108);
    float3 _S109 = float3(3.14159274101257324f) ;
    float3 diffuse_0 = diffuseColor_0 / _S109;
    float3 normalIncidence_0;
    float3 grazingIncidence_0;
    float3 diffuse_1;
    if((_S14.w) >= 0.5f)
    {
        float3 _S110 = float3(1.0f, 1.0f, 1.0f);
        normalIncidence_0 = specularColor_0;
        grazingIncidence_0 = _S110;
        diffuse_1 = diffuse_0;
    }
    else
    {
        float3 _S111 = float3(metallic_0) ;
        float3 specularTint_0 = mix(float3(1.0f, 1.0f, 1.0f), diffuseColor_0, _S111);
        float3 diffuse_2 = diffuse_0 * float3((1.0f - metallic_0)) ;
        normalIncidence_0 = mix(float3((reflectanceRatio_0 * reflectanceRatio_0))  * specularTint_0, specularTint_0, _S111);
        grazingIncidence_0 = specularTint_0;
        diffuse_1 = diffuse_2;
    }
    float4 _S112 = float4(_S4->ambientLight_0) ;
    float _S113 = _S112.w;
    if(_S113 > 0.5f)
    {
        hasSceneLighting_0 = true;
    }
    else
    {
        float3 _S114 = _S112.xyz;
        hasSceneLighting_0 = (dot(_S114, _S114)) > 0.0f;
    }
    uint _S115 = min(uint(_S113), 8U);
    matrix<float,int(4),int(4)>  _S116 = matrix<float,int(4),int(4)> (_S4->eyeToWorld_0.data_0[int(0)][int(0)], _S4->eyeToWorld_0.data_0[int(0)][int(1)], _S4->eyeToWorld_0.data_0[int(0)][int(2)], _S4->eyeToWorld_0.data_0[int(0)][int(3)], _S4->eyeToWorld_0.data_0[int(1)][int(0)], _S4->eyeToWorld_0.data_0[int(1)][int(1)], _S4->eyeToWorld_0.data_0[int(1)][int(2)], _S4->eyeToWorld_0.data_0[int(1)][int(3)], _S4->eyeToWorld_0.data_0[int(2)][int(0)], _S4->eyeToWorld_0.data_0[int(2)][int(1)], _S4->eyeToWorld_0.data_0[int(2)][int(2)], _S4->eyeToWorld_0.data_0[int(2)][int(3)], _S4->eyeToWorld_0.data_0[int(3)][int(0)], _S4->eyeToWorld_0.data_0[int(3)][int(1)], _S4->eyeToWorld_0.data_0[int(3)][int(2)], _S4->eyeToWorld_0.data_0[int(3)][int(3)]);
    float3 _S117 = normalize((((float4(_S104, 0.0f)) * (_S116))).xyz);
    float3 _S118 = (((float4(_S1.eyePosition_0, 1.0f)) * (_S116))).xyz;
    float4 _S119 = float4(_S3->lightColorAmbient_0) ;
    float3 color_0 = diffuseColor_0 * float3(_S119.w)  + diffuseColor_0 * _S112.xyz;
    bool _S120 = !hasSceneLighting_0;
    uint lightCount_0;
    if(_S120)
    {
        lightCount_0 = 1U;
    }
    else
    {
        lightCount_0 = _S115;
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
        bool _S121 = lightIndex_0 == 0U;
        if(_S121)
        {
            hasSceneLighting_0 = _S120;
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
        bool _S122;
        if(_S121)
        {
            _S122 = _S120;
        }
        else
        {
            _S122 = false;
        }
        float3 lightDirection_0;
        if(_S122)
        {
            lightDirection_0 = normalize((float4(_S3->lightDirectionIntensity_0) ).xyz);
        }
        else
        {
            lightDirection_0 = normalize((float4((&_S4->lightDirectionRadius_0)->data_1[lightIndex_0]) ).xyz);
        }
        bool _S123;
        if(_S121)
        {
            _S123 = _S120;
        }
        else
        {
            _S123 = false;
        }
        if(_S123)
        {
            roughness_0 = (float4(_S3->lightDirectionIntensity_0) ).w;
        }
        else
        {
            roughness_0 = (float4((&_S4->lightColorIntensity_0)->data_1[lightIndex_0]) ).w;
        }
        bool _S124;
        if(_S121)
        {
            _S124 = _S120;
        }
        else
        {
            _S124 = false;
        }
        if(_S124)
        {
            diffuseColor_0 = _S119.xyz;
        }
        else
        {
            diffuseColor_0 = (float4((&_S4->lightColorIntensity_0)->data_1[lightIndex_0]) ).xyz;
        }
        bool _S125;
        if(_S121)
        {
            _S125 = _S120;
        }
        else
        {
            _S125 = false;
        }
        if(_S125)
        {
            metallic_0 = 1.0f;
        }
        else
        {
            metallic_0 = (float4((&_S4->lightControls_0)->data_1[lightIndex_0]) ).x;
        }
        bool _S126;
        if(_S121)
        {
            _S126 = _S120;
        }
        else
        {
            _S126 = false;
        }
        if(_S126)
        {
            clearcoatRoughness_0 = 1.0f;
        }
        else
        {
            clearcoatRoughness_0 = (float4((&_S4->lightControls_0)->data_1[lightIndex_0]) ).y;
        }
        bool _S127;
        if(_S121)
        {
            _S127 = _S120;
        }
        else
        {
            _S127 = false;
        }
        float3 lightTangent_0;
        if(_S127)
        {
            lightTangent_0 = float3(1.0f, 0.0f, 0.0f);
        }
        else
        {
            lightTangent_0 = normalize((float4((&_S4->lightTangentShapeX_0)->data_1[lightIndex_0]) ).xyz);
        }
        bool _S128;
        if(_S121)
        {
            _S128 = _S120;
        }
        else
        {
            _S128 = false;
        }
        float3 lightBitangent_0;
        if(_S128)
        {
            lightBitangent_0 = float3(0.0f, 1.0f, 0.0f);
        }
        else
        {
            lightBitangent_0 = normalize((float4((&_S4->lightBitangentShapeY_0)->data_1[lightIndex_0]) ).xyz);
        }
        bool _S129;
        if(_S121)
        {
            _S129 = _S120;
        }
        else
        {
            _S129 = false;
        }
        float shapeX_0;
        if(_S129)
        {
            shapeX_0 = 0.0f;
        }
        else
        {
            shapeX_0 = (float4((&_S4->lightTangentShapeX_0)->data_1[lightIndex_0]) ).w;
        }
        bool _S130;
        if(_S121)
        {
            _S130 = _S120;
        }
        else
        {
            _S130 = false;
        }
        float shapeY_0;
        if(_S130)
        {
            shapeY_0 = 0.0f;
        }
        else
        {
            shapeY_0 = (float4((&_S4->lightBitangentShapeY_0)->data_1[lightIndex_0]) ).w;
        }
        bool _S131;
        if(_S121)
        {
            _S131 = _S120;
        }
        else
        {
            _S131 = false;
        }
        float lightRadius_0;
        if(_S131)
        {
            lightRadius_0 = 0.0f;
        }
        else
        {
            lightRadius_0 = (float4((&_S4->lightDirectionRadius_0)->data_1[lightIndex_0]) ).w;
        }
        bool _S132;
        if(_S121)
        {
            _S132 = _S120;
        }
        else
        {
            _S132 = false;
        }
        if(_S132)
        {
            specularColor_0 = _S104;
        }
        else
        {
            specularColor_0 = _S117;
        }
        thread array<float3, int(5)> sampleOffsets_0;
        float3 _S133 = float3(0.0f, 0.0f, 0.0f);
        sampleOffsets_0[int(0)] = _S133;
        sampleOffsets_0[int(1)] = _S133;
        sampleOffsets_0[int(2)] = _S133;
        sampleOffsets_0[int(3)] = _S133;
        sampleOffsets_0[int(4)] = _S133;
        float sampleCount_0;
        if(lightType_0 == 3.0f)
        {
            float3 halfWidth_0 = lightTangent_0 * float3((shapeX_0 * 0.5f)) ;
            float3 halfHeight_0 = lightBitangent_0 * float3((shapeY_0 * 0.5f)) ;
            sampleOffsets_0[int(1)] = halfWidth_0 + halfHeight_0;
            sampleOffsets_0[int(2)] = halfWidth_0 - halfHeight_0;
            float3 _S134 = - halfWidth_0;
            sampleOffsets_0[int(3)] = _S134 + halfHeight_0;
            sampleOffsets_0[int(4)] = _S134 - halfHeight_0;
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
                float3 toLight_0 = (float4((&_S4->lightPositionType_0)->data_1[lightIndex_0]) ).xyz + sampleOffsets_0[sampleIndex_0] - _S118;
                float _S135 = max(dot(toLight_0, toLight_0), 0.00100000004749745f);
                float3 sampleDirection_1 = toLight_0 * float3(rsqrt(_S135)) ;
                float sampleIntensity_2 = sampleIntensity_0 / _S135;
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
            float3 _S136 = float3(pow(max(0.0f, 1.0f - saturate(dot(specularColor_0, half_0))), 5.0f)) ;
            float3 _S137 = mix(normalIncidence_0, grazingIncidence_0, _S136);
            float3 directDiffuse_0 = diffuse_1 * (_S39 - _S137);
            float alpha_0 = _S106 * _S106;
            float alphaSquared_0 = alpha_0 * alpha_0;
            float _S138 = normalDotHalf_0 * normalDotHalf_0;
            float denominator_0 = _S138 * (alphaSquared_0 - 1.0f) + 1.0f;
            float k_0 = alpha_0 * 0.5f;
            float _S139 = 1.0f - k_0;
            float3 _S140 = float3((4.0f * normalDotLight_0 * _S105 + 0.00100000004749745f)) ;
            float3 _S141 = _S137 * float3((_S105 / (_S105 * _S139 + k_0) * (normalDotLight_0 / (normalDotLight_0 * _S139 + k_0))))  * float3(((alphaSquared_0 + 0.00100000004749745f) / (denominator_0 * denominator_0 * 3.14159274101257324f)))  / _S140;
            float3 directSpecular_0;
            if(clearcoatAmount_0 > 0.0f)
            {
                float alpha_1 = _S107 * _S107;
                float alphaSquared_1 = alpha_1 * alpha_1;
                float denominator_1 = _S138 * (alphaSquared_1 - 1.0f) + 1.0f;
                float k_1 = alpha_1 * 0.5f;
                float _S142 = 1.0f - k_1;
                directSpecular_0 = _S141 + float3(clearcoatAmount_0)  * (mix(float3((reflectanceRatio_0 * reflectanceRatio_0)) , float3(1.0f, 1.0f, 1.0f), _S136) * float3((_S105 / (_S105 * _S142 + k_1) * (normalDotLight_0 / (normalDotLight_0 * _S142 + k_1))))  * float3(((alphaSquared_1 + 0.00100000004749745f) / (denominator_1 * denominator_1 * 3.14159274101257324f)))  / _S140);
            }
            else
            {
                directSpecular_0 = _S141;
            }
            float3 _S143 = diffuseColor_0 * float3(sampleIntensity_1) ;
            color_2 = color_2 + float3((occlusion_0 * emissionScale_0 * normalDotLight_0))  * (directDiffuse_0 * float3(metallic_0)  * (_S143 * _S109) + directSpecular_0 * float3(clearcoatRoughness_0)  * _S143);
            sampleIndex_0 = sampleIndex_0 + 1U;
        }
        lightIndex_0 = lightIndex_0 + 1U;
        color_1 = color_2;
    }
    float3 color_3 = (color_1 + emissiveColor_0) * float3(exp2((as_type<float>((_S2.z))))) ;
    if((_S2.y) == 1U)
    {
        color_1 = color_3 / (_S39 + max(color_3, float3(0.0f, 0.0f, 0.0f)));
    }
    else
    {
        color_1 = color_3;
    }
    pixelOutput_0 _S144 = { float4(color_1, opacity_0) };
    return _S144;
}

