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
};

[[fragment]] pixelOutput_0 fragmentMain_uv_basecolor_normal_roughmetal_metallic_emissive(pixelInput_0 _S1 [[stage_in]], bool isFrontFace_0 [[front_facing]], float4 position_0 [[position]], SurfaceParameters_natural_0 device* surfaceParameters_1 [[buffer(7)]], FrameParameters_natural_0 device* frameParameters_1 [[buffer(8)]], texture2d<float, access::sample> baseColorTexture_1 [[texture(0)]], sampler baseColorSampler_1 [[sampler(0)]], texture2d<float, access::sample> normalTexture_1 [[texture(1)]], sampler normalSampler_1 [[sampler(1)]], texture2d<float, access::sample> roughnessMetallicTexture_1 [[texture(2)]], sampler roughnessMetallicSampler_1 [[sampler(2)]], texture2d<float, access::sample> metallicTexture_1 [[texture(4)]], sampler metallicSampler_1 [[sampler(5)]], texture2d<float, access::sample> emissiveTexture_1 [[texture(3)]], sampler emissiveSampler_1 [[sampler(3)]])
{
    uint4 _S2;
    bool hasSceneLighting_0;
    float4 _S3;
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
    float _S9 = (float4(surface_0.emissiveOcclusion_0) ).w;
    float4 _S10 = float4(surface_0.reserved_0) ;
    if((_S10.x) >= 0.5f)
    {
        pixelOutput_0 _S11 = { float4(diffuseColor_0 * float3((1.0f - exp(- max(0.0f, _S10.y) * max(0.0f, _S10.z)))) , 1.0f) };
        return _S11;
    }
    uint _S12 = uint(round(_S10.w));
    bool _S13 = (_S12 & 1U) != 0U;
    for(;;)
    {
        if(!_S13)
        {
            _S3 = (((&kernelContext_0)->baseColorTexture_0).sample(((&kernelContext_0)->baseColorSampler_0), (_S1.texCoord_0)));
            break;
        }
        texture2d<float, access::sample> _S14 = (&kernelContext_0)->baseColorTexture_0;
        thread uint atlasWidth_0;
        thread uint atlasHeight_0;
        (*((&atlasWidth_0)) = (_S14).get_width(0)),(*((&atlasHeight_0)) = (_S14).get_height(0));
        int3 _S15 = int3(int(0), int(0), int(0));
        float4 metadata_0 = round((((&kernelContext_0)->baseColorTexture_0).read(vec<uint,2>(((_S15)).xy), uint(((_S15)).z))) * float4(255.0f) );
        int2 _S16 = int2(metadata_0.zw);
        int2 tile_0 = int2(floor(_S1.texCoord_0)) - int2(metadata_0.xy);
        if(any(tile_0 < (int2(int(0)) )))
        {
            hasSceneLighting_0 = true;
        }
        else
        {
            hasSceneLighting_0 = any(tile_0 >= _S16);
        }
        if(hasSceneLighting_0)
        {
            int3 _S17 = int3(int(min(1U, atlasWidth_0 - 1U)), int(0), int(0));
            _S3 = (((&kernelContext_0)->baseColorTexture_0).read(vec<uint,2>(((_S17)).xy), uint(((_S17)).z)));
            break;
        }
        uint _S18 = atlasWidth_0 / uint(_S16.x);
        float _S19 = float(_S18);
        uint _S20 = (atlasHeight_0 - 1U) / uint(_S16.y);
        float2 cellSize_0 = float2(_S19, float(_S20));
        _S3 = (((&kernelContext_0)->baseColorTexture_0).sample(((&kernelContext_0)->baseColorSampler_0), ((float2(tile_0) * cellSize_0 + float2(1.5f, 2.5f) + fract(_S1.texCoord_0) * max(cellSize_0 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_0), float(atlasHeight_0)))));
        break;
    }
    float3 diffuseColor_1 = _S3.xyz;
    float opacity_1 = opacity_0 * _S3.w;
    bool _S21 = (_S12 & 2U) != 0U;
    for(;;)
    {
        if(!_S21)
        {
            _S3 = (((&kernelContext_0)->normalTexture_0).sample(((&kernelContext_0)->normalSampler_0), (_S1.texCoord_0)));
            break;
        }
        texture2d<float, access::sample> _S22 = (&kernelContext_0)->normalTexture_0;
        thread uint atlasWidth_1;
        thread uint atlasHeight_1;
        (*((&atlasWidth_1)) = (_S22).get_width(0)),(*((&atlasHeight_1)) = (_S22).get_height(0));
        int3 _S23 = int3(int(0), int(0), int(0));
        float4 metadata_1 = round((((&kernelContext_0)->normalTexture_0).read(vec<uint,2>(((_S23)).xy), uint(((_S23)).z))) * float4(255.0f) );
        int2 _S24 = int2(metadata_1.zw);
        int2 tile_1 = int2(floor(_S1.texCoord_0)) - int2(metadata_1.xy);
        if(any(tile_1 < (int2(int(0)) )))
        {
            hasSceneLighting_0 = true;
        }
        else
        {
            hasSceneLighting_0 = any(tile_1 >= _S24);
        }
        if(hasSceneLighting_0)
        {
            int3 _S25 = int3(int(min(1U, atlasWidth_1 - 1U)), int(0), int(0));
            _S3 = (((&kernelContext_0)->normalTexture_0).read(vec<uint,2>(((_S25)).xy), uint(((_S25)).z)));
            break;
        }
        uint _S26 = atlasWidth_1 / uint(_S24.x);
        float _S27 = float(_S26);
        uint _S28 = (atlasHeight_1 - 1U) / uint(_S24.y);
        float2 cellSize_1 = float2(_S27, float(_S28));
        _S3 = (((&kernelContext_0)->normalTexture_0).sample(((&kernelContext_0)->normalSampler_0), ((float2(tile_1) * cellSize_1 + float2(1.5f, 2.5f) + fract(_S1.texCoord_0) * max(cellSize_1 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_1), float(atlasHeight_1)))));
        break;
    }
    float3 _S29 = float3(1.0f) ;
    float3 sampledNormal_0 = _S3.xyz * float3(2.0f)  - _S29;
    float3 tangent_1 = normalize(_S1.tangent_0.xyz);
    float3 shadingNormal_0 = normalize(tangent_1 * float3(sampledNormal_0.x)  + cross(normalize(_S1.normal_0), tangent_1) * float3(_S1.tangent_0.w)  * float3(sampledNormal_0.y)  + _S1.normal_0 * float3(sampledNormal_0.z) );
    bool _S30 = (_S12 & 4U) != 0U;
    for(;;)
    {
        if(!_S30)
        {
            _S3 = (((&kernelContext_0)->roughnessMetallicTexture_0).sample(((&kernelContext_0)->roughnessMetallicSampler_0), (_S1.texCoord_0)));
            break;
        }
        texture2d<float, access::sample> _S31 = (&kernelContext_0)->roughnessMetallicTexture_0;
        thread uint atlasWidth_2;
        thread uint atlasHeight_2;
        (*((&atlasWidth_2)) = (_S31).get_width(0)),(*((&atlasHeight_2)) = (_S31).get_height(0));
        int3 _S32 = int3(int(0), int(0), int(0));
        float4 metadata_2 = round((((&kernelContext_0)->roughnessMetallicTexture_0).read(vec<uint,2>(((_S32)).xy), uint(((_S32)).z))) * float4(255.0f) );
        int2 _S33 = int2(metadata_2.zw);
        int2 tile_2 = int2(floor(_S1.texCoord_0)) - int2(metadata_2.xy);
        if(any(tile_2 < (int2(int(0)) )))
        {
            hasSceneLighting_0 = true;
        }
        else
        {
            hasSceneLighting_0 = any(tile_2 >= _S33);
        }
        if(hasSceneLighting_0)
        {
            int3 _S34 = int3(int(min(1U, atlasWidth_2 - 1U)), int(0), int(0));
            _S3 = (((&kernelContext_0)->roughnessMetallicTexture_0).read(vec<uint,2>(((_S34)).xy), uint(((_S34)).z)));
            break;
        }
        uint _S35 = atlasWidth_2 / uint(_S33.x);
        float _S36 = float(_S35);
        uint _S37 = (atlasHeight_2 - 1U) / uint(_S33.y);
        float2 cellSize_2 = float2(_S36, float(_S37));
        _S3 = (((&kernelContext_0)->roughnessMetallicTexture_0).sample(((&kernelContext_0)->roughnessMetallicSampler_0), ((float2(tile_2) * cellSize_2 + float2(1.5f, 2.5f) + fract(_S1.texCoord_0) * max(cellSize_2 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_2), float(atlasHeight_2)))));
        break;
    }
    float roughness_0 = clamp(_S3.x, 0.00999999977648258f, 1.0f);
    bool _S38 = (_S12 & 16U) != 0U;
    for(;;)
    {
        if(!_S38)
        {
            _S3 = (((&kernelContext_0)->metallicTexture_0).sample(((&kernelContext_0)->metallicSampler_0), (_S1.texCoord_0)));
            break;
        }
        texture2d<float, access::sample> _S39 = (&kernelContext_0)->metallicTexture_0;
        thread uint atlasWidth_3;
        thread uint atlasHeight_3;
        (*((&atlasWidth_3)) = (_S39).get_width(0)),(*((&atlasHeight_3)) = (_S39).get_height(0));
        int3 _S40 = int3(int(0), int(0), int(0));
        float4 metadata_3 = round((((&kernelContext_0)->metallicTexture_0).read(vec<uint,2>(((_S40)).xy), uint(((_S40)).z))) * float4(255.0f) );
        int2 _S41 = int2(metadata_3.zw);
        int2 tile_3 = int2(floor(_S1.texCoord_0)) - int2(metadata_3.xy);
        if(any(tile_3 < (int2(int(0)) )))
        {
            hasSceneLighting_0 = true;
        }
        else
        {
            hasSceneLighting_0 = any(tile_3 >= _S41);
        }
        if(hasSceneLighting_0)
        {
            int3 _S42 = int3(int(min(1U, atlasWidth_3 - 1U)), int(0), int(0));
            _S3 = (((&kernelContext_0)->metallicTexture_0).read(vec<uint,2>(((_S42)).xy), uint(((_S42)).z)));
            break;
        }
        uint _S43 = atlasWidth_3 / uint(_S41.x);
        float _S44 = float(_S43);
        uint _S45 = (atlasHeight_3 - 1U) / uint(_S41.y);
        float2 cellSize_3 = float2(_S44, float(_S45));
        _S3 = (((&kernelContext_0)->metallicTexture_0).sample(((&kernelContext_0)->metallicSampler_0), ((float2(tile_3) * cellSize_3 + float2(1.5f, 2.5f) + fract(_S1.texCoord_0) * max(cellSize_3 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_3), float(atlasHeight_3)))));
        break;
    }
    float metallic_0 = saturate(_S3.x);
    bool _S46 = (_S12 & 8U) != 0U;
    for(;;)
    {
        if(!_S46)
        {
            _S3 = (((&kernelContext_0)->emissiveTexture_0).sample(((&kernelContext_0)->emissiveSampler_0), (_S1.texCoord_0)));
            break;
        }
        texture2d<float, access::sample> _S47 = (&kernelContext_0)->emissiveTexture_0;
        thread uint atlasWidth_4;
        thread uint atlasHeight_4;
        (*((&atlasWidth_4)) = (_S47).get_width(0)),(*((&atlasHeight_4)) = (_S47).get_height(0));
        int3 _S48 = int3(int(0), int(0), int(0));
        float4 metadata_4 = round((((&kernelContext_0)->emissiveTexture_0).read(vec<uint,2>(((_S48)).xy), uint(((_S48)).z))) * float4(255.0f) );
        int2 _S49 = int2(metadata_4.zw);
        int2 tile_4 = int2(floor(_S1.texCoord_0)) - int2(metadata_4.xy);
        if(any(tile_4 < (int2(int(0)) )))
        {
            hasSceneLighting_0 = true;
        }
        else
        {
            hasSceneLighting_0 = any(tile_4 >= _S49);
        }
        if(hasSceneLighting_0)
        {
            int3 _S50 = int3(int(min(1U, atlasWidth_4 - 1U)), int(0), int(0));
            _S3 = (((&kernelContext_0)->emissiveTexture_0).read(vec<uint,2>(((_S50)).xy), uint(((_S50)).z)));
            break;
        }
        uint _S51 = atlasWidth_4 / uint(_S49.x);
        float _S52 = float(_S51);
        uint _S53 = (atlasHeight_4 - 1U) / uint(_S49.y);
        float2 cellSize_4 = float2(_S52, float(_S53));
        _S3 = (((&kernelContext_0)->emissiveTexture_0).sample(((&kernelContext_0)->emissiveSampler_0), ((float2(tile_4) * cellSize_4 + float2(1.5f, 2.5f) + fract(_S1.texCoord_0) * max(cellSize_4 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_4), float(atlasHeight_4)))));
        break;
    }
    float3 emissiveColor_0 = _S3.xyz;
    float4 _S54 = float4(surface_0.metallicRoughnessThresholdWorkflow_0) ;
    float opacityThreshold_0 = _S54.z;
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
    float _S55 = saturate(abs(dot(normal_2, diffuseColor_0)) + 0.00000999999974738f);
    float _S56 = max(0.00100000004749745f, roughness_0);
    float _S57 = _S8.x;
    float _S58 = max(0.00100000004749745f, _S8.y);
    float4 _S59 = float4(surface_0.specularIor_0) ;
    float _S60 = _S59.w;
    float reflectanceRatio_0 = (1.0f - _S60) / (1.0f + _S60);
    float3 _S61 = float3(3.14159274101257324f) ;
    float3 diffuse_0 = diffuseColor_1 / _S61;
    float3 normalIncidence_0;
    float3 grazingIncidence_0;
    float3 diffuse_1;
    if((_S54.w) >= 0.5f)
    {
        float3 _S62 = float3(1.0f, 1.0f, 1.0f);
        normalIncidence_0 = _S59.xyz;
        grazingIncidence_0 = _S62;
        diffuse_1 = diffuse_0;
    }
    else
    {
        float3 _S63 = float3(metallic_0) ;
        float3 specularTint_0 = mix(float3(1.0f, 1.0f, 1.0f), diffuseColor_1, _S63);
        float3 diffuse_2 = diffuse_0 * float3((1.0f - metallic_0)) ;
        normalIncidence_0 = mix(float3((reflectanceRatio_0 * reflectanceRatio_0))  * specularTint_0, specularTint_0, _S63);
        grazingIncidence_0 = specularTint_0;
        diffuse_1 = diffuse_2;
    }
    float4 _S64 = float4(_S4->ambientLight_0) ;
    float _S65 = _S64.w;
    if(_S65 > 0.5f)
    {
        hasSceneLighting_0 = true;
    }
    else
    {
        float3 _S66 = _S64.xyz;
        hasSceneLighting_0 = (dot(_S66, _S66)) > 0.0f;
    }
    uint _S67 = min(uint(_S65), 8U);
    matrix<float,int(4),int(4)>  _S68 = matrix<float,int(4),int(4)> (_S4->eyeToWorld_0.data_0[int(0)][int(0)], _S4->eyeToWorld_0.data_0[int(0)][int(1)], _S4->eyeToWorld_0.data_0[int(0)][int(2)], _S4->eyeToWorld_0.data_0[int(0)][int(3)], _S4->eyeToWorld_0.data_0[int(1)][int(0)], _S4->eyeToWorld_0.data_0[int(1)][int(1)], _S4->eyeToWorld_0.data_0[int(1)][int(2)], _S4->eyeToWorld_0.data_0[int(1)][int(3)], _S4->eyeToWorld_0.data_0[int(2)][int(0)], _S4->eyeToWorld_0.data_0[int(2)][int(1)], _S4->eyeToWorld_0.data_0[int(2)][int(2)], _S4->eyeToWorld_0.data_0[int(2)][int(3)], _S4->eyeToWorld_0.data_0[int(3)][int(0)], _S4->eyeToWorld_0.data_0[int(3)][int(1)], _S4->eyeToWorld_0.data_0[int(3)][int(2)], _S4->eyeToWorld_0.data_0[int(3)][int(3)]);
    float3 _S69 = normalize((((float4(diffuseColor_0, 0.0f)) * (_S68))).xyz);
    float3 _S70 = (((float4(_S1.eyePosition_0, 1.0f)) * (_S68))).xyz;
    float4 _S71 = float4(surface_0.lightColorAmbient_0) ;
    float3 color_0 = diffuseColor_1 * float3(_S71.w)  + diffuseColor_1 * _S64.xyz;
    bool _S72 = !hasSceneLighting_0;
    uint lightCount_0;
    if(_S72)
    {
        lightCount_0 = 1U;
    }
    else
    {
        lightCount_0 = _S67;
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
        bool _S73 = lightIndex_0 == 0U;
        if(_S73)
        {
            hasSceneLighting_0 = _S72;
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
        bool _S74;
        if(_S73)
        {
            _S74 = _S72;
        }
        else
        {
            _S74 = false;
        }
        float3 lightDirection_0;
        if(_S74)
        {
            lightDirection_0 = normalize((float4(surface_0.lightDirectionIntensity_0) ).xyz);
        }
        else
        {
            lightDirection_0 = normalize((float4((&_S4->lightDirectionRadius_0)->data_1[lightIndex_0]) ).xyz);
        }
        bool _S75;
        if(_S73)
        {
            _S75 = _S72;
        }
        else
        {
            _S75 = false;
        }
        if(_S75)
        {
            opacity_0 = (float4(surface_0.lightDirectionIntensity_0) ).w;
        }
        else
        {
            opacity_0 = (float4((&_S4->lightColorIntensity_0)->data_1[lightIndex_0]) ).w;
        }
        bool _S76;
        if(_S73)
        {
            _S76 = _S72;
        }
        else
        {
            _S76 = false;
        }
        float3 _S77;
        if(_S76)
        {
            _S77 = _S71.xyz;
        }
        else
        {
            _S77 = (float4((&_S4->lightColorIntensity_0)->data_1[lightIndex_0]) ).xyz;
        }
        bool _S78;
        if(_S73)
        {
            _S78 = _S72;
        }
        else
        {
            _S78 = false;
        }
        float _S79;
        if(_S78)
        {
            _S79 = 1.0f;
        }
        else
        {
            _S79 = (float4((&_S4->lightControls_0)->data_1[lightIndex_0]) ).x;
        }
        bool _S80;
        if(_S73)
        {
            _S80 = _S72;
        }
        else
        {
            _S80 = false;
        }
        float _S81;
        if(_S80)
        {
            _S81 = 1.0f;
        }
        else
        {
            _S81 = (float4((&_S4->lightControls_0)->data_1[lightIndex_0]) ).y;
        }
        bool _S82;
        if(_S73)
        {
            _S82 = _S72;
        }
        else
        {
            _S82 = false;
        }
        float3 lightTangent_0;
        if(_S82)
        {
            lightTangent_0 = float3(1.0f, 0.0f, 0.0f);
        }
        else
        {
            lightTangent_0 = normalize((float4((&_S4->lightTangentShapeX_0)->data_1[lightIndex_0]) ).xyz);
        }
        bool _S83;
        if(_S73)
        {
            _S83 = _S72;
        }
        else
        {
            _S83 = false;
        }
        float3 lightBitangent_0;
        if(_S83)
        {
            lightBitangent_0 = float3(0.0f, 1.0f, 0.0f);
        }
        else
        {
            lightBitangent_0 = normalize((float4((&_S4->lightBitangentShapeY_0)->data_1[lightIndex_0]) ).xyz);
        }
        bool _S84;
        if(_S73)
        {
            _S84 = _S72;
        }
        else
        {
            _S84 = false;
        }
        float shapeX_0;
        if(_S84)
        {
            shapeX_0 = 0.0f;
        }
        else
        {
            shapeX_0 = (float4((&_S4->lightTangentShapeX_0)->data_1[lightIndex_0]) ).w;
        }
        bool _S85;
        if(_S73)
        {
            _S85 = _S72;
        }
        else
        {
            _S85 = false;
        }
        float shapeY_0;
        if(_S85)
        {
            shapeY_0 = 0.0f;
        }
        else
        {
            shapeY_0 = (float4((&_S4->lightBitangentShapeY_0)->data_1[lightIndex_0]) ).w;
        }
        bool _S86;
        if(_S73)
        {
            _S86 = _S72;
        }
        else
        {
            _S86 = false;
        }
        float lightRadius_0;
        if(_S86)
        {
            lightRadius_0 = 0.0f;
        }
        else
        {
            lightRadius_0 = (float4((&_S4->lightDirectionRadius_0)->data_1[lightIndex_0]) ).w;
        }
        bool _S87;
        if(_S73)
        {
            _S87 = _S72;
        }
        else
        {
            _S87 = false;
        }
        float3 _S88;
        if(_S87)
        {
            _S88 = diffuseColor_0;
        }
        else
        {
            _S88 = _S69;
        }
        thread array<float3, int(5)> sampleOffsets_0;
        float3 _S89 = float3(0.0f, 0.0f, 0.0f);
        sampleOffsets_0[int(0)] = _S89;
        sampleOffsets_0[int(1)] = _S89;
        sampleOffsets_0[int(2)] = _S89;
        sampleOffsets_0[int(3)] = _S89;
        sampleOffsets_0[int(4)] = _S89;
        float sampleCount_0;
        if(lightType_0 == 3.0f)
        {
            float3 halfWidth_0 = lightTangent_0 * float3((shapeX_0 * 0.5f)) ;
            float3 halfHeight_0 = lightBitangent_0 * float3((shapeY_0 * 0.5f)) ;
            sampleOffsets_0[int(1)] = halfWidth_0 + halfHeight_0;
            sampleOffsets_0[int(2)] = halfWidth_0 - halfHeight_0;
            float3 _S90 = - halfWidth_0;
            sampleOffsets_0[int(3)] = _S90 + halfHeight_0;
            sampleOffsets_0[int(4)] = _S90 - halfHeight_0;
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
                float3 toLight_0 = (float4((&_S4->lightPositionType_0)->data_1[lightIndex_0]) ).xyz + sampleOffsets_0[sampleIndex_0] - _S70;
                float _S91 = max(dot(toLight_0, toLight_0), 0.00100000004749745f);
                float3 sampleDirection_1 = toLight_0 * float3(rsqrt(_S91)) ;
                float sampleIntensity_2 = sampleIntensity_0 / _S91;
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
            float3 half_0 = normalize(sampleDirection_0 + _S88);
            float normalDotLight_0 = saturate(dot(normal_2, sampleDirection_0));
            float normalDotHalf_0 = saturate(dot(normal_2, half_0));
            float3 _S92 = float3(pow(max(0.0f, 1.0f - saturate(dot(_S88, half_0))), 5.0f)) ;
            float3 _S93 = mix(normalIncidence_0, grazingIncidence_0, _S92);
            float3 directDiffuse_0 = diffuse_1 * (_S29 - _S93);
            float alpha_0 = _S56 * _S56;
            float alphaSquared_0 = alpha_0 * alpha_0;
            float _S94 = normalDotHalf_0 * normalDotHalf_0;
            float denominator_0 = _S94 * (alphaSquared_0 - 1.0f) + 1.0f;
            float k_0 = alpha_0 * 0.5f;
            float _S95 = 1.0f - k_0;
            float3 _S96 = float3((4.0f * normalDotLight_0 * _S55 + 0.00100000004749745f)) ;
            float3 _S97 = _S93 * float3((_S55 / (_S55 * _S95 + k_0) * (normalDotLight_0 / (normalDotLight_0 * _S95 + k_0))))  * float3(((alphaSquared_0 + 0.00100000004749745f) / (denominator_0 * denominator_0 * 3.14159274101257324f)))  / _S96;
            float3 directSpecular_0;
            if(_S57 > 0.0f)
            {
                float alpha_1 = _S58 * _S58;
                float alphaSquared_1 = alpha_1 * alpha_1;
                float denominator_1 = _S94 * (alphaSquared_1 - 1.0f) + 1.0f;
                float k_1 = alpha_1 * 0.5f;
                float _S98 = 1.0f - k_1;
                directSpecular_0 = _S97 + float3(_S57)  * (mix(float3((reflectanceRatio_0 * reflectanceRatio_0)) , float3(1.0f, 1.0f, 1.0f), _S92) * float3((_S55 / (_S55 * _S98 + k_1) * (normalDotLight_0 / (normalDotLight_0 * _S98 + k_1))))  * float3(((alphaSquared_1 + 0.00100000004749745f) / (denominator_1 * denominator_1 * 3.14159274101257324f)))  / _S96);
            }
            else
            {
                directSpecular_0 = _S97;
            }
            float3 _S99 = _S77 * float3(sampleIntensity_1) ;
            color_2 = color_2 + float3((_S9 * emissionScale_0 * normalDotLight_0))  * (directDiffuse_0 * float3(_S79)  * (_S99 * _S61) + directSpecular_0 * float3(_S81)  * _S99);
            sampleIndex_0 = sampleIndex_0 + 1U;
        }
        lightIndex_0 = lightIndex_0 + 1U;
        color_1 = color_2;
    }
    float3 color_3 = (color_1 + emissiveColor_0) * float3(exp2((as_type<float>((_S2.z))))) ;
    if((_S2.y) == 1U)
    {
        color_1 = color_3 / (_S29 + max(color_3, float3(0.0f, 0.0f, 0.0f)));
    }
    else
    {
        color_1 = color_3;
    }
    pixelOutput_0 _S100 = { float4(color_1, opacity_1) };
    return _S100;
}

