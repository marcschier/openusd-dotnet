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
};

[[fragment]] pixelOutput_0 fragmentMain_uv_basecolor_normal_roughmetal(pixelInput_0 _S1 [[stage_in]], bool isFrontFace_0 [[front_facing]], float4 position_0 [[position]], SurfaceParameters_natural_0 device* surfaceParameters_1 [[buffer(7)]], FrameParameters_natural_0 device* frameParameters_1 [[buffer(8)]], texture2d<float, access::sample> baseColorTexture_1 [[texture(0)]], sampler baseColorSampler_1 [[sampler(0)]], texture2d<float, access::sample> normalTexture_1 [[texture(1)]], sampler normalSampler_1 [[sampler(1)]], texture2d<float, access::sample> roughnessMetallicTexture_1 [[texture(2)]], sampler roughnessMetallicSampler_1 [[sampler(2)]])
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
    float metallic_0 = saturate(_S13.x);
    uint _S14 = uint(round(_S11.w));
    bool _S15 = (_S14 & 1U) != 0U;
    for(;;)
    {
        if(!_S15)
        {
            _S3 = (((&kernelContext_0)->baseColorTexture_0).sample(((&kernelContext_0)->baseColorSampler_0), (_S1.texCoord_0)));
            break;
        }
        texture2d<float, access::sample> _S16 = (&kernelContext_0)->baseColorTexture_0;
        thread uint atlasWidth_0;
        thread uint atlasHeight_0;
        (*((&atlasWidth_0)) = (_S16).get_width(0)),(*((&atlasHeight_0)) = (_S16).get_height(0));
        int3 _S17 = int3(int(0), int(0), int(0));
        float4 metadata_0 = round((((&kernelContext_0)->baseColorTexture_0).read(vec<uint,2>(((_S17)).xy), uint(((_S17)).z))) * float4(255.0f) );
        int2 _S18 = int2(metadata_0.zw);
        int2 tile_0 = int2(floor(_S1.texCoord_0)) - int2(metadata_0.xy);
        if(any(tile_0 < (int2(int(0)) )))
        {
            hasSceneLighting_0 = true;
        }
        else
        {
            hasSceneLighting_0 = any(tile_0 >= _S18);
        }
        if(hasSceneLighting_0)
        {
            int3 _S19 = int3(int(min(1U, atlasWidth_0 - 1U)), int(0), int(0));
            _S3 = (((&kernelContext_0)->baseColorTexture_0).read(vec<uint,2>(((_S19)).xy), uint(((_S19)).z)));
            break;
        }
        uint _S20 = atlasWidth_0 / uint(_S18.x);
        float _S21 = float(_S20);
        uint _S22 = (atlasHeight_0 - 1U) / uint(_S18.y);
        float2 cellSize_0 = float2(_S21, float(_S22));
        _S3 = (((&kernelContext_0)->baseColorTexture_0).sample(((&kernelContext_0)->baseColorSampler_0), ((float2(tile_0) * cellSize_0 + float2(1.5f, 2.5f) + fract(_S1.texCoord_0) * max(cellSize_0 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_0), float(atlasHeight_0)))));
        break;
    }
    float3 diffuseColor_1 = _S3.xyz;
    float opacity_1 = opacity_0 * _S3.w;
    bool _S23 = (_S14 & 2U) != 0U;
    for(;;)
    {
        if(!_S23)
        {
            _S3 = (((&kernelContext_0)->normalTexture_0).sample(((&kernelContext_0)->normalSampler_0), (_S1.texCoord_0)));
            break;
        }
        texture2d<float, access::sample> _S24 = (&kernelContext_0)->normalTexture_0;
        thread uint atlasWidth_1;
        thread uint atlasHeight_1;
        (*((&atlasWidth_1)) = (_S24).get_width(0)),(*((&atlasHeight_1)) = (_S24).get_height(0));
        int3 _S25 = int3(int(0), int(0), int(0));
        float4 metadata_1 = round((((&kernelContext_0)->normalTexture_0).read(vec<uint,2>(((_S25)).xy), uint(((_S25)).z))) * float4(255.0f) );
        int2 _S26 = int2(metadata_1.zw);
        int2 tile_1 = int2(floor(_S1.texCoord_0)) - int2(metadata_1.xy);
        if(any(tile_1 < (int2(int(0)) )))
        {
            hasSceneLighting_0 = true;
        }
        else
        {
            hasSceneLighting_0 = any(tile_1 >= _S26);
        }
        if(hasSceneLighting_0)
        {
            int3 _S27 = int3(int(min(1U, atlasWidth_1 - 1U)), int(0), int(0));
            _S3 = (((&kernelContext_0)->normalTexture_0).read(vec<uint,2>(((_S27)).xy), uint(((_S27)).z)));
            break;
        }
        uint _S28 = atlasWidth_1 / uint(_S26.x);
        float _S29 = float(_S28);
        uint _S30 = (atlasHeight_1 - 1U) / uint(_S26.y);
        float2 cellSize_1 = float2(_S29, float(_S30));
        _S3 = (((&kernelContext_0)->normalTexture_0).sample(((&kernelContext_0)->normalSampler_0), ((float2(tile_1) * cellSize_1 + float2(1.5f, 2.5f) + fract(_S1.texCoord_0) * max(cellSize_1 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_1), float(atlasHeight_1)))));
        break;
    }
    float3 _S31 = float3(1.0f) ;
    float3 sampledNormal_0 = _S3.xyz * float3(2.0f)  - _S31;
    float3 tangent_1 = normalize(_S1.tangent_0.xyz);
    float3 shadingNormal_0 = normalize(tangent_1 * float3(sampledNormal_0.x)  + cross(normalize(_S1.normal_0), tangent_1) * float3(_S1.tangent_0.w)  * float3(sampledNormal_0.y)  + _S1.normal_0 * float3(sampledNormal_0.z) );
    bool _S32 = (_S14 & 4U) != 0U;
    for(;;)
    {
        if(!_S32)
        {
            _S3 = (((&kernelContext_0)->roughnessMetallicTexture_0).sample(((&kernelContext_0)->roughnessMetallicSampler_0), (_S1.texCoord_0)));
            break;
        }
        texture2d<float, access::sample> _S33 = (&kernelContext_0)->roughnessMetallicTexture_0;
        thread uint atlasWidth_2;
        thread uint atlasHeight_2;
        (*((&atlasWidth_2)) = (_S33).get_width(0)),(*((&atlasHeight_2)) = (_S33).get_height(0));
        int3 _S34 = int3(int(0), int(0), int(0));
        float4 metadata_2 = round((((&kernelContext_0)->roughnessMetallicTexture_0).read(vec<uint,2>(((_S34)).xy), uint(((_S34)).z))) * float4(255.0f) );
        int2 _S35 = int2(metadata_2.zw);
        int2 tile_2 = int2(floor(_S1.texCoord_0)) - int2(metadata_2.xy);
        if(any(tile_2 < (int2(int(0)) )))
        {
            hasSceneLighting_0 = true;
        }
        else
        {
            hasSceneLighting_0 = any(tile_2 >= _S35);
        }
        if(hasSceneLighting_0)
        {
            int3 _S36 = int3(int(min(1U, atlasWidth_2 - 1U)), int(0), int(0));
            _S3 = (((&kernelContext_0)->roughnessMetallicTexture_0).read(vec<uint,2>(((_S36)).xy), uint(((_S36)).z)));
            break;
        }
        uint _S37 = atlasWidth_2 / uint(_S35.x);
        float _S38 = float(_S37);
        uint _S39 = (atlasHeight_2 - 1U) / uint(_S35.y);
        float2 cellSize_2 = float2(_S38, float(_S39));
        _S3 = (((&kernelContext_0)->roughnessMetallicTexture_0).sample(((&kernelContext_0)->roughnessMetallicSampler_0), ((float2(tile_2) * cellSize_2 + float2(1.5f, 2.5f) + fract(_S1.texCoord_0) * max(cellSize_2 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_2), float(atlasHeight_2)))));
        break;
    }
    float roughness_0 = clamp(_S3.x, 0.00999999977648258f, 1.0f);
    float opacityThreshold_0 = _S13.z;
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
    float _S40 = saturate(abs(dot(normal_2, diffuseColor_0)) + 0.00000999999974738f);
    float _S41 = max(0.00100000004749745f, roughness_0);
    float _S42 = _S8.x;
    float _S43 = max(0.00100000004749745f, _S8.y);
    float4 _S44 = float4(surface_0.specularIor_0) ;
    float _S45 = _S44.w;
    float reflectanceRatio_0 = (1.0f - _S45) / (1.0f + _S45);
    float3 _S46 = float3(3.14159274101257324f) ;
    float3 diffuse_0 = diffuseColor_1 / _S46;
    float3 normalIncidence_0;
    float3 grazingIncidence_0;
    float3 diffuse_1;
    if((_S13.w) >= 0.5f)
    {
        float3 _S47 = float3(1.0f, 1.0f, 1.0f);
        normalIncidence_0 = _S44.xyz;
        grazingIncidence_0 = _S47;
        diffuse_1 = diffuse_0;
    }
    else
    {
        float3 _S48 = float3(metallic_0) ;
        float3 specularTint_0 = mix(float3(1.0f, 1.0f, 1.0f), diffuseColor_1, _S48);
        float3 diffuse_2 = diffuse_0 * float3((1.0f - metallic_0)) ;
        normalIncidence_0 = mix(float3((reflectanceRatio_0 * reflectanceRatio_0))  * specularTint_0, specularTint_0, _S48);
        grazingIncidence_0 = specularTint_0;
        diffuse_1 = diffuse_2;
    }
    float4 _S49 = float4(_S4->ambientLight_0) ;
    float _S50 = _S49.w;
    if(_S50 > 0.5f)
    {
        hasSceneLighting_0 = true;
    }
    else
    {
        float3 _S51 = _S49.xyz;
        hasSceneLighting_0 = (dot(_S51, _S51)) > 0.0f;
    }
    uint _S52 = min(uint(_S50), 8U);
    matrix<float,int(4),int(4)>  _S53 = matrix<float,int(4),int(4)> (_S4->eyeToWorld_0.data_0[int(0)][int(0)], _S4->eyeToWorld_0.data_0[int(0)][int(1)], _S4->eyeToWorld_0.data_0[int(0)][int(2)], _S4->eyeToWorld_0.data_0[int(0)][int(3)], _S4->eyeToWorld_0.data_0[int(1)][int(0)], _S4->eyeToWorld_0.data_0[int(1)][int(1)], _S4->eyeToWorld_0.data_0[int(1)][int(2)], _S4->eyeToWorld_0.data_0[int(1)][int(3)], _S4->eyeToWorld_0.data_0[int(2)][int(0)], _S4->eyeToWorld_0.data_0[int(2)][int(1)], _S4->eyeToWorld_0.data_0[int(2)][int(2)], _S4->eyeToWorld_0.data_0[int(2)][int(3)], _S4->eyeToWorld_0.data_0[int(3)][int(0)], _S4->eyeToWorld_0.data_0[int(3)][int(1)], _S4->eyeToWorld_0.data_0[int(3)][int(2)], _S4->eyeToWorld_0.data_0[int(3)][int(3)]);
    float3 _S54 = normalize((((float4(diffuseColor_0, 0.0f)) * (_S53))).xyz);
    float3 _S55 = (((float4(_S1.eyePosition_0, 1.0f)) * (_S53))).xyz;
    float4 _S56 = float4(surface_0.lightColorAmbient_0) ;
    float3 color_0 = diffuseColor_1 * float3(_S56.w)  + diffuseColor_1 * _S49.xyz;
    bool _S57 = !hasSceneLighting_0;
    uint lightCount_0;
    if(_S57)
    {
        lightCount_0 = 1U;
    }
    else
    {
        lightCount_0 = _S52;
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
        bool _S58 = lightIndex_0 == 0U;
        if(_S58)
        {
            hasSceneLighting_0 = _S57;
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
        bool _S59;
        if(_S58)
        {
            _S59 = _S57;
        }
        else
        {
            _S59 = false;
        }
        float3 lightDirection_0;
        if(_S59)
        {
            lightDirection_0 = normalize((float4(surface_0.lightDirectionIntensity_0) ).xyz);
        }
        else
        {
            lightDirection_0 = normalize((float4((&_S4->lightDirectionRadius_0)->data_1[lightIndex_0]) ).xyz);
        }
        bool _S60;
        if(_S58)
        {
            _S60 = _S57;
        }
        else
        {
            _S60 = false;
        }
        if(_S60)
        {
            opacity_0 = (float4(surface_0.lightDirectionIntensity_0) ).w;
        }
        else
        {
            opacity_0 = (float4((&_S4->lightColorIntensity_0)->data_1[lightIndex_0]) ).w;
        }
        bool _S61;
        if(_S58)
        {
            _S61 = _S57;
        }
        else
        {
            _S61 = false;
        }
        float3 _S62;
        if(_S61)
        {
            _S62 = _S56.xyz;
        }
        else
        {
            _S62 = (float4((&_S4->lightColorIntensity_0)->data_1[lightIndex_0]) ).xyz;
        }
        bool _S63;
        if(_S58)
        {
            _S63 = _S57;
        }
        else
        {
            _S63 = false;
        }
        float _S64;
        if(_S63)
        {
            _S64 = 1.0f;
        }
        else
        {
            _S64 = (float4((&_S4->lightControls_0)->data_1[lightIndex_0]) ).x;
        }
        bool _S65;
        if(_S58)
        {
            _S65 = _S57;
        }
        else
        {
            _S65 = false;
        }
        float _S66;
        if(_S65)
        {
            _S66 = 1.0f;
        }
        else
        {
            _S66 = (float4((&_S4->lightControls_0)->data_1[lightIndex_0]) ).y;
        }
        bool _S67;
        if(_S58)
        {
            _S67 = _S57;
        }
        else
        {
            _S67 = false;
        }
        float3 lightTangent_0;
        if(_S67)
        {
            lightTangent_0 = float3(1.0f, 0.0f, 0.0f);
        }
        else
        {
            lightTangent_0 = normalize((float4((&_S4->lightTangentShapeX_0)->data_1[lightIndex_0]) ).xyz);
        }
        bool _S68;
        if(_S58)
        {
            _S68 = _S57;
        }
        else
        {
            _S68 = false;
        }
        float3 lightBitangent_0;
        if(_S68)
        {
            lightBitangent_0 = float3(0.0f, 1.0f, 0.0f);
        }
        else
        {
            lightBitangent_0 = normalize((float4((&_S4->lightBitangentShapeY_0)->data_1[lightIndex_0]) ).xyz);
        }
        bool _S69;
        if(_S58)
        {
            _S69 = _S57;
        }
        else
        {
            _S69 = false;
        }
        float shapeX_0;
        if(_S69)
        {
            shapeX_0 = 0.0f;
        }
        else
        {
            shapeX_0 = (float4((&_S4->lightTangentShapeX_0)->data_1[lightIndex_0]) ).w;
        }
        bool _S70;
        if(_S58)
        {
            _S70 = _S57;
        }
        else
        {
            _S70 = false;
        }
        float shapeY_0;
        if(_S70)
        {
            shapeY_0 = 0.0f;
        }
        else
        {
            shapeY_0 = (float4((&_S4->lightBitangentShapeY_0)->data_1[lightIndex_0]) ).w;
        }
        bool _S71;
        if(_S58)
        {
            _S71 = _S57;
        }
        else
        {
            _S71 = false;
        }
        float lightRadius_0;
        if(_S71)
        {
            lightRadius_0 = 0.0f;
        }
        else
        {
            lightRadius_0 = (float4((&_S4->lightDirectionRadius_0)->data_1[lightIndex_0]) ).w;
        }
        bool _S72;
        if(_S58)
        {
            _S72 = _S57;
        }
        else
        {
            _S72 = false;
        }
        float3 _S73;
        if(_S72)
        {
            _S73 = diffuseColor_0;
        }
        else
        {
            _S73 = _S54;
        }
        thread array<float3, int(5)> sampleOffsets_0;
        float3 _S74 = float3(0.0f, 0.0f, 0.0f);
        sampleOffsets_0[int(0)] = _S74;
        sampleOffsets_0[int(1)] = _S74;
        sampleOffsets_0[int(2)] = _S74;
        sampleOffsets_0[int(3)] = _S74;
        sampleOffsets_0[int(4)] = _S74;
        float sampleCount_0;
        if(lightType_0 == 3.0f)
        {
            float3 halfWidth_0 = lightTangent_0 * float3((shapeX_0 * 0.5f)) ;
            float3 halfHeight_0 = lightBitangent_0 * float3((shapeY_0 * 0.5f)) ;
            sampleOffsets_0[int(1)] = halfWidth_0 + halfHeight_0;
            sampleOffsets_0[int(2)] = halfWidth_0 - halfHeight_0;
            float3 _S75 = - halfWidth_0;
            sampleOffsets_0[int(3)] = _S75 + halfHeight_0;
            sampleOffsets_0[int(4)] = _S75 - halfHeight_0;
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
                float3 toLight_0 = (float4((&_S4->lightPositionType_0)->data_1[lightIndex_0]) ).xyz + sampleOffsets_0[sampleIndex_0] - _S55;
                float _S76 = max(dot(toLight_0, toLight_0), 0.00100000004749745f);
                float3 sampleDirection_1 = toLight_0 * float3(rsqrt(_S76)) ;
                float sampleIntensity_2 = sampleIntensity_0 / _S76;
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
            float3 half_0 = normalize(sampleDirection_0 + _S73);
            float normalDotLight_0 = saturate(dot(normal_2, sampleDirection_0));
            float normalDotHalf_0 = saturate(dot(normal_2, half_0));
            float3 _S77 = float3(pow(max(0.0f, 1.0f - saturate(dot(_S73, half_0))), 5.0f)) ;
            float3 _S78 = mix(normalIncidence_0, grazingIncidence_0, _S77);
            float3 directDiffuse_0 = diffuse_1 * (_S31 - _S78);
            float alpha_0 = _S41 * _S41;
            float alphaSquared_0 = alpha_0 * alpha_0;
            float _S79 = normalDotHalf_0 * normalDotHalf_0;
            float denominator_0 = _S79 * (alphaSquared_0 - 1.0f) + 1.0f;
            float k_0 = alpha_0 * 0.5f;
            float _S80 = 1.0f - k_0;
            float3 _S81 = float3((4.0f * normalDotLight_0 * _S40 + 0.00100000004749745f)) ;
            float3 _S82 = _S78 * float3((_S40 / (_S40 * _S80 + k_0) * (normalDotLight_0 / (normalDotLight_0 * _S80 + k_0))))  * float3(((alphaSquared_0 + 0.00100000004749745f) / (denominator_0 * denominator_0 * 3.14159274101257324f)))  / _S81;
            float3 directSpecular_0;
            if(_S42 > 0.0f)
            {
                float alpha_1 = _S43 * _S43;
                float alphaSquared_1 = alpha_1 * alpha_1;
                float denominator_1 = _S79 * (alphaSquared_1 - 1.0f) + 1.0f;
                float k_1 = alpha_1 * 0.5f;
                float _S83 = 1.0f - k_1;
                directSpecular_0 = _S82 + float3(_S42)  * (mix(float3((reflectanceRatio_0 * reflectanceRatio_0)) , float3(1.0f, 1.0f, 1.0f), _S77) * float3((_S40 / (_S40 * _S83 + k_1) * (normalDotLight_0 / (normalDotLight_0 * _S83 + k_1))))  * float3(((alphaSquared_1 + 0.00100000004749745f) / (denominator_1 * denominator_1 * 3.14159274101257324f)))  / _S81);
            }
            else
            {
                directSpecular_0 = _S82;
            }
            float3 _S84 = _S62 * float3(sampleIntensity_1) ;
            color_2 = color_2 + float3((_S10 * emissionScale_0 * normalDotLight_0))  * (directDiffuse_0 * float3(_S64)  * (_S84 * _S46) + directSpecular_0 * float3(_S66)  * _S84);
            sampleIndex_0 = sampleIndex_0 + 1U;
        }
        lightIndex_0 = lightIndex_0 + 1U;
        color_1 = color_2;
    }
    float3 color_3 = (color_1 + emissiveColor_0) * float3(exp2((as_type<float>((_S2.z))))) ;
    if((_S2.y) == 1U)
    {
        color_1 = color_3 / (_S31 + max(color_3, float3(0.0f, 0.0f, 0.0f)));
    }
    else
    {
        color_1 = color_3;
    }
    pixelOutput_0 _S85 = { float4(color_1, opacity_1) };
    return _S85;
}

