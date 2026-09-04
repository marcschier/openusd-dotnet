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
    float4 tangent_0 [[user(TANGENT)]];
    float4 worldTangent_0 [[user(TEXCOORD_4)]];
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
    texture2d<float, access::sample> shadowAtlas_0;
    sampler shadowSampler_0;
    texture2d<float, access::sample> environmentBrdf_0;
    sampler environmentBrdfSampler_0;
    texture2d<float, access::sample> environmentIrradiance_0;
    sampler environmentSampler_0;
    texture2d<float, access::sample> environmentSpecular_0;
};

[[fragment]] pixelOutput_0 fragmentMain_uv_material_normal(pixelInput_0 _S1 [[stage_in]], bool isFrontFace_0 [[front_facing]], float4 position_0 [[position]], SurfaceParameters_natural_0 device* surfaceParameters_1 [[buffer(7)]], FrameParameters_natural_0 device* frameParameters_1 [[buffer(8)]], texture2d<float, access::sample> baseColorTexture_1 [[texture(0)]], sampler baseColorSampler_1 [[sampler(0)]], texture2d<float, access::sample> compositeTexture_1 [[texture(15)]], sampler compositeSampler_1 [[sampler(12)]], texture2d<float, access::sample> normalTexture_1 [[texture(1)]], sampler normalSampler_1 [[sampler(1)]], texture2d<float, access::sample> roughnessMetallicTexture_1 [[texture(2)]], sampler roughnessMetallicSampler_1 [[sampler(2)]], texture2d<float, access::sample> metallicTexture_1 [[texture(4)]], sampler metallicSampler_1 [[sampler(5)]], texture2d<float, access::sample> emissiveTexture_1 [[texture(3)]], sampler emissiveSampler_1 [[sampler(3)]], texture2d<float, access::sample> opacityTexture_1 [[texture(5)]], sampler opacitySampler_1 [[sampler(6)]], texture2d<float, access::sample> occlusionTexture_1 [[texture(10)]], sampler occlusionSampler_1 [[sampler(7)]], texture2d<float, access::sample> specularColorTexture_1 [[texture(11)]], sampler specularColorSampler_1 [[sampler(8)]], texture2d<float, access::sample> clearcoatTexture_1 [[texture(12)]], sampler clearcoatSampler_1 [[sampler(9)]], texture2d<float, access::sample> clearcoatRoughnessTexture_1 [[texture(13)]], sampler clearcoatRoughnessSampler_1 [[sampler(10)]], texture2d<float, access::sample> iorTexture_1 [[texture(14)]], sampler iorSampler_1 [[sampler(11)]], texture2d<float, access::sample> shadowAtlas_1 [[texture(16)]], sampler shadowSampler_1 [[sampler(13)]], texture2d<float, access::sample> environmentBrdf_1 [[texture(19)]], sampler environmentBrdfSampler_1 [[sampler(15)]], texture2d<float, access::sample> environmentIrradiance_1 [[texture(17)]], sampler environmentSampler_1 [[sampler(14)]], texture2d<float, access::sample> environmentSpecular_1 [[texture(18)]])
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
    bool _S47 = (udimMask_0 & 4U) != 0U;
    for(;;)
    {
        if(!_S47)
        {
            _S28 = (((&kernelContext_0)->normalTexture_0).sample(((&kernelContext_0)->normalSampler_0), (_S27)));
            break;
        }
        texture2d<float, access::sample> _S48 = (&kernelContext_0)->normalTexture_0;
        thread uint atlasWidth_2;
        thread uint atlasHeight_2;
        (*((&atlasWidth_2)) = (_S48).get_width(0)),(*((&atlasHeight_2)) = (_S48).get_height(0));
        int3 _S49 = int3(int(0), int(0), int(0));
        float4 metadata_2 = round((((&kernelContext_0)->normalTexture_0).read(vec<uint,2>(((_S49)).xy), uint(((_S49)).z))) * float4(255.0f) );
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
            _S28 = (((&kernelContext_0)->normalTexture_0).read(vec<uint,2>(((_S51)).xy), uint(((_S51)).z)));
            break;
        }
        uint _S52 = atlasWidth_2 / uint(_S50.x);
        float _S53 = float(_S52);
        uint _S54 = (atlasHeight_2 - 1U) / uint(_S50.y);
        float2 cellSize_2 = float2(_S53, float(_S54));
        _S28 = (((&kernelContext_0)->normalTexture_0).sample(((&kernelContext_0)->normalSampler_0), ((float2(tile_2) * cellSize_2 + float2(1.5f, 2.5f) + fract(_S27) * max(cellSize_2 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_2), float(atlasHeight_2)))));
        break;
    }
    float3 _S55 = float3(1.0f) ;
    float3 sampledNormal_0 = _S28.xyz * float3(2.0f)  - _S55;
    float3 tangent_1 = normalize(_S1.tangent_0.xyz);
    float3 _S56 = float3(sampledNormal_0.x) ;
    float3 _S57 = float3(sampledNormal_0.y) ;
    float3 _S58 = float3(sampledNormal_0.z) ;
    float3 shadingNormal_0 = normalize(tangent_1 * _S56 + cross(normalize(_S1.normal_0), tangent_1) * float3(_S1.tangent_0.w)  * _S57 + _S1.normal_0 * _S58);
    float3 worldNormalBasis_0 = normalize(_S1.worldNormal_0);
    float3 worldTangentBasis_0 = normalize(_S1.worldTangent_0.xyz);
    float3 worldTangentBasis_1 = normalize(worldTangentBasis_0 - worldNormalBasis_0 * float3(dot(worldNormalBasis_0, worldTangentBasis_0)) );
    float3 worldShadingNormal_0 = normalize(worldTangentBasis_1 * _S56 + cross(worldNormalBasis_0, worldTangentBasis_1) * float3(_S1.worldTangent_0.w)  * _S57 + worldNormalBasis_0 * _S58);
    float roughness_0;
    if((textureMask_0 & 8U) != 0U)
    {
        bool _S59 = (udimMask_0 & 8U) != 0U;
        for(;;)
        {
            if(!_S59)
            {
                _S28 = (((&kernelContext_0)->roughnessMetallicTexture_0).sample(((&kernelContext_0)->roughnessMetallicSampler_0), (_S27)));
                break;
            }
            texture2d<float, access::sample> _S60 = (&kernelContext_0)->roughnessMetallicTexture_0;
            thread uint atlasWidth_3;
            thread uint atlasHeight_3;
            (*((&atlasWidth_3)) = (_S60).get_width(0)),(*((&atlasHeight_3)) = (_S60).get_height(0));
            int3 _S61 = int3(int(0), int(0), int(0));
            float4 metadata_3 = round((((&kernelContext_0)->roughnessMetallicTexture_0).read(vec<uint,2>(((_S61)).xy), uint(((_S61)).z))) * float4(255.0f) );
            int2 _S62 = int2(metadata_3.zw);
            int2 tile_3 = int2(floor(_S27)) - int2(metadata_3.xy);
            if(any(tile_3 < (int2(int(0)) )))
            {
                hasSceneLighting_0 = true;
            }
            else
            {
                hasSceneLighting_0 = any(tile_3 >= _S62);
            }
            if(hasSceneLighting_0)
            {
                int3 _S63 = int3(int(min(1U, atlasWidth_3 - 1U)), int(0), int(0));
                _S28 = (((&kernelContext_0)->roughnessMetallicTexture_0).read(vec<uint,2>(((_S63)).xy), uint(((_S63)).z)));
                break;
            }
            uint _S64 = atlasWidth_3 / uint(_S62.x);
            float _S65 = float(_S64);
            uint _S66 = (atlasHeight_3 - 1U) / uint(_S62.y);
            float2 cellSize_3 = float2(_S65, float(_S66));
            _S28 = (((&kernelContext_0)->roughnessMetallicTexture_0).sample(((&kernelContext_0)->roughnessMetallicSampler_0), ((float2(tile_3) * cellSize_3 + float2(1.5f, 2.5f) + fract(_S27) * max(cellSize_3 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_3), float(atlasHeight_3)))));
            break;
        }
        for(;;)
        {
            float4 _S67 = float4(_S5->compositeControls_0) ;
            if((_S67.x) != 8.0f)
            {
                break;
            }
            bool _S68 = (_S67.w) >= 0.5f;
            for(;;)
            {
                if(!_S68)
                {
                    _S29 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), (_S27)));
                    break;
                }
                texture2d<float, access::sample> _S69 = (&kernelContext_0)->compositeTexture_0;
                thread uint atlasWidth_4;
                thread uint atlasHeight_4;
                (*((&atlasWidth_4)) = (_S69).get_width(0)),(*((&atlasHeight_4)) = (_S69).get_height(0));
                int3 _S70 = int3(int(0), int(0), int(0));
                float4 metadata_4 = round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S70)).xy), uint(((_S70)).z))) * float4(255.0f) );
                int2 _S71 = int2(metadata_4.zw);
                int2 tile_4 = int2(floor(_S27)) - int2(metadata_4.xy);
                if(any(tile_4 < (int2(int(0)) )))
                {
                    hasSceneLighting_0 = true;
                }
                else
                {
                    hasSceneLighting_0 = any(tile_4 >= _S71);
                }
                if(hasSceneLighting_0)
                {
                    int3 _S72 = int3(int(min(1U, atlasWidth_4 - 1U)), int(0), int(0));
                    _S29 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S72)).xy), uint(((_S72)).z)));
                    break;
                }
                uint _S73 = atlasWidth_4 / uint(_S71.x);
                float _S74 = float(_S73);
                uint _S75 = (atlasHeight_4 - 1U) / uint(_S71.y);
                float2 cellSize_4 = float2(_S74, float(_S75));
                _S29 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), ((float2(tile_4) * cellSize_4 + float2(1.5f, 2.5f) + fract(_S27) * max(cellSize_4 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_4), float(atlasHeight_4)))));
                break;
            }
            uint operation_1 = uint(round(_S67.y));
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
                float factor_1 = _S67.z;
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
        bool _S76 = (udimMask_0 & 32U) != 0U;
        for(;;)
        {
            if(!_S76)
            {
                _S28 = (((&kernelContext_0)->metallicTexture_0).sample(((&kernelContext_0)->metallicSampler_0), (_S27)));
                break;
            }
            texture2d<float, access::sample> _S77 = (&kernelContext_0)->metallicTexture_0;
            thread uint atlasWidth_5;
            thread uint atlasHeight_5;
            (*((&atlasWidth_5)) = (_S77).get_width(0)),(*((&atlasHeight_5)) = (_S77).get_height(0));
            int3 _S78 = int3(int(0), int(0), int(0));
            float4 metadata_5 = round((((&kernelContext_0)->metallicTexture_0).read(vec<uint,2>(((_S78)).xy), uint(((_S78)).z))) * float4(255.0f) );
            int2 _S79 = int2(metadata_5.zw);
            int2 tile_5 = int2(floor(_S27)) - int2(metadata_5.xy);
            if(any(tile_5 < (int2(int(0)) )))
            {
                hasSceneLighting_0 = true;
            }
            else
            {
                hasSceneLighting_0 = any(tile_5 >= _S79);
            }
            if(hasSceneLighting_0)
            {
                int3 _S80 = int3(int(min(1U, atlasWidth_5 - 1U)), int(0), int(0));
                _S28 = (((&kernelContext_0)->metallicTexture_0).read(vec<uint,2>(((_S80)).xy), uint(((_S80)).z)));
                break;
            }
            uint _S81 = atlasWidth_5 / uint(_S79.x);
            float _S82 = float(_S81);
            uint _S83 = (atlasHeight_5 - 1U) / uint(_S79.y);
            float2 cellSize_5 = float2(_S82, float(_S83));
            _S28 = (((&kernelContext_0)->metallicTexture_0).sample(((&kernelContext_0)->metallicSampler_0), ((float2(tile_5) * cellSize_5 + float2(1.5f, 2.5f) + fract(_S27) * max(cellSize_5 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_5), float(atlasHeight_5)))));
            break;
        }
        for(;;)
        {
            float4 _S84 = float4(_S5->compositeControls_0) ;
            if((_S84.x) != 32.0f)
            {
                break;
            }
            bool _S85 = (_S84.w) >= 0.5f;
            for(;;)
            {
                if(!_S85)
                {
                    _S29 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), (_S27)));
                    break;
                }
                texture2d<float, access::sample> _S86 = (&kernelContext_0)->compositeTexture_0;
                thread uint atlasWidth_6;
                thread uint atlasHeight_6;
                (*((&atlasWidth_6)) = (_S86).get_width(0)),(*((&atlasHeight_6)) = (_S86).get_height(0));
                int3 _S87 = int3(int(0), int(0), int(0));
                float4 metadata_6 = round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S87)).xy), uint(((_S87)).z))) * float4(255.0f) );
                int2 _S88 = int2(metadata_6.zw);
                int2 tile_6 = int2(floor(_S27)) - int2(metadata_6.xy);
                if(any(tile_6 < (int2(int(0)) )))
                {
                    hasSceneLighting_0 = true;
                }
                else
                {
                    hasSceneLighting_0 = any(tile_6 >= _S88);
                }
                if(hasSceneLighting_0)
                {
                    int3 _S89 = int3(int(min(1U, atlasWidth_6 - 1U)), int(0), int(0));
                    _S29 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S89)).xy), uint(((_S89)).z)));
                    break;
                }
                uint _S90 = atlasWidth_6 / uint(_S88.x);
                float _S91 = float(_S90);
                uint _S92 = (atlasHeight_6 - 1U) / uint(_S88.y);
                float2 cellSize_6 = float2(_S91, float(_S92));
                _S29 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), ((float2(tile_6) * cellSize_6 + float2(1.5f, 2.5f) + fract(_S27) * max(cellSize_6 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_6), float(atlasHeight_6)))));
                break;
            }
            uint operation_2 = uint(round(_S84.y));
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
                float factor_2 = _S84.z;
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
        bool _S93 = (udimMask_0 & 16U) != 0U;
        for(;;)
        {
            if(!_S93)
            {
                _S28 = (((&kernelContext_0)->emissiveTexture_0).sample(((&kernelContext_0)->emissiveSampler_0), (_S27)));
                break;
            }
            texture2d<float, access::sample> _S94 = (&kernelContext_0)->emissiveTexture_0;
            thread uint atlasWidth_7;
            thread uint atlasHeight_7;
            (*((&atlasWidth_7)) = (_S94).get_width(0)),(*((&atlasHeight_7)) = (_S94).get_height(0));
            int3 _S95 = int3(int(0), int(0), int(0));
            float4 metadata_7 = round((((&kernelContext_0)->emissiveTexture_0).read(vec<uint,2>(((_S95)).xy), uint(((_S95)).z))) * float4(255.0f) );
            int2 _S96 = int2(metadata_7.zw);
            int2 tile_7 = int2(floor(_S27)) - int2(metadata_7.xy);
            if(any(tile_7 < (int2(int(0)) )))
            {
                hasSceneLighting_0 = true;
            }
            else
            {
                hasSceneLighting_0 = any(tile_7 >= _S96);
            }
            if(hasSceneLighting_0)
            {
                int3 _S97 = int3(int(min(1U, atlasWidth_7 - 1U)), int(0), int(0));
                _S28 = (((&kernelContext_0)->emissiveTexture_0).read(vec<uint,2>(((_S97)).xy), uint(((_S97)).z)));
                break;
            }
            uint _S98 = atlasWidth_7 / uint(_S96.x);
            float _S99 = float(_S98);
            uint _S100 = (atlasHeight_7 - 1U) / uint(_S96.y);
            float2 cellSize_7 = float2(_S99, float(_S100));
            _S28 = (((&kernelContext_0)->emissiveTexture_0).sample(((&kernelContext_0)->emissiveSampler_0), ((float2(tile_7) * cellSize_7 + float2(1.5f, 2.5f) + fract(_S27) * max(cellSize_7 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_7), float(atlasHeight_7)))));
            break;
        }
        for(;;)
        {
            float4 _S101 = float4(_S5->compositeControls_0) ;
            if((_S101.x) != 16.0f)
            {
                break;
            }
            bool _S102 = (_S101.w) >= 0.5f;
            for(;;)
            {
                if(!_S102)
                {
                    _S29 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), (_S27)));
                    break;
                }
                texture2d<float, access::sample> _S103 = (&kernelContext_0)->compositeTexture_0;
                thread uint atlasWidth_8;
                thread uint atlasHeight_8;
                (*((&atlasWidth_8)) = (_S103).get_width(0)),(*((&atlasHeight_8)) = (_S103).get_height(0));
                int3 _S104 = int3(int(0), int(0), int(0));
                float4 metadata_8 = round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S104)).xy), uint(((_S104)).z))) * float4(255.0f) );
                int2 _S105 = int2(metadata_8.zw);
                int2 tile_8 = int2(floor(_S27)) - int2(metadata_8.xy);
                if(any(tile_8 < (int2(int(0)) )))
                {
                    hasSceneLighting_0 = true;
                }
                else
                {
                    hasSceneLighting_0 = any(tile_8 >= _S105);
                }
                if(hasSceneLighting_0)
                {
                    int3 _S106 = int3(int(min(1U, atlasWidth_8 - 1U)), int(0), int(0));
                    _S29 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S106)).xy), uint(((_S106)).z)));
                    break;
                }
                uint _S107 = atlasWidth_8 / uint(_S105.x);
                float _S108 = float(_S107);
                uint _S109 = (atlasHeight_8 - 1U) / uint(_S105.y);
                float2 cellSize_8 = float2(_S108, float(_S109));
                _S29 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), ((float2(tile_8) * cellSize_8 + float2(1.5f, 2.5f) + fract(_S27) * max(cellSize_8 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_8), float(atlasHeight_8)))));
                break;
            }
            uint operation_3 = uint(round(_S101.y));
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
                float factor_3 = _S101.z;
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
        bool _S110 = (udimMask_0 & 64U) != 0U;
        for(;;)
        {
            if(!_S110)
            {
                _S28 = (((&kernelContext_0)->opacityTexture_0).sample(((&kernelContext_0)->opacitySampler_0), (_S27)));
                break;
            }
            texture2d<float, access::sample> _S111 = (&kernelContext_0)->opacityTexture_0;
            thread uint atlasWidth_9;
            thread uint atlasHeight_9;
            (*((&atlasWidth_9)) = (_S111).get_width(0)),(*((&atlasHeight_9)) = (_S111).get_height(0));
            int3 _S112 = int3(int(0), int(0), int(0));
            float4 metadata_9 = round((((&kernelContext_0)->opacityTexture_0).read(vec<uint,2>(((_S112)).xy), uint(((_S112)).z))) * float4(255.0f) );
            int2 _S113 = int2(metadata_9.zw);
            int2 tile_9 = int2(floor(_S27)) - int2(metadata_9.xy);
            if(any(tile_9 < (int2(int(0)) )))
            {
                hasSceneLighting_0 = true;
            }
            else
            {
                hasSceneLighting_0 = any(tile_9 >= _S113);
            }
            if(hasSceneLighting_0)
            {
                int3 _S114 = int3(int(min(1U, atlasWidth_9 - 1U)), int(0), int(0));
                _S28 = (((&kernelContext_0)->opacityTexture_0).read(vec<uint,2>(((_S114)).xy), uint(((_S114)).z)));
                break;
            }
            uint _S115 = atlasWidth_9 / uint(_S113.x);
            float _S116 = float(_S115);
            uint _S117 = (atlasHeight_9 - 1U) / uint(_S113.y);
            float2 cellSize_9 = float2(_S116, float(_S117));
            _S28 = (((&kernelContext_0)->opacityTexture_0).sample(((&kernelContext_0)->opacitySampler_0), ((float2(tile_9) * cellSize_9 + float2(1.5f, 2.5f) + fract(_S27) * max(cellSize_9 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_9), float(atlasHeight_9)))));
            break;
        }
        for(;;)
        {
            float4 _S118 = float4(_S5->compositeControls_0) ;
            if((_S118.x) != 64.0f)
            {
                break;
            }
            bool _S119 = (_S118.w) >= 0.5f;
            for(;;)
            {
                if(!_S119)
                {
                    _S29 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), (_S27)));
                    break;
                }
                texture2d<float, access::sample> _S120 = (&kernelContext_0)->compositeTexture_0;
                thread uint atlasWidth_10;
                thread uint atlasHeight_10;
                (*((&atlasWidth_10)) = (_S120).get_width(0)),(*((&atlasHeight_10)) = (_S120).get_height(0));
                int3 _S121 = int3(int(0), int(0), int(0));
                float4 metadata_10 = round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S121)).xy), uint(((_S121)).z))) * float4(255.0f) );
                int2 _S122 = int2(metadata_10.zw);
                int2 tile_10 = int2(floor(_S27)) - int2(metadata_10.xy);
                if(any(tile_10 < (int2(int(0)) )))
                {
                    hasSceneLighting_0 = true;
                }
                else
                {
                    hasSceneLighting_0 = any(tile_10 >= _S122);
                }
                if(hasSceneLighting_0)
                {
                    int3 _S123 = int3(int(min(1U, atlasWidth_10 - 1U)), int(0), int(0));
                    _S29 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S123)).xy), uint(((_S123)).z)));
                    break;
                }
                uint _S124 = atlasWidth_10 / uint(_S122.x);
                float _S125 = float(_S124);
                uint _S126 = (atlasHeight_10 - 1U) / uint(_S122.y);
                float2 cellSize_10 = float2(_S125, float(_S126));
                _S29 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), ((float2(tile_10) * cellSize_10 + float2(1.5f, 2.5f) + fract(_S27) * max(cellSize_10 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_10), float(atlasHeight_10)))));
                break;
            }
            uint operation_4 = uint(round(_S118.y));
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
                float factor_4 = _S118.z;
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
        bool _S127 = (udimMask_0 & 128U) != 0U;
        for(;;)
        {
            if(!_S127)
            {
                _S28 = (((&kernelContext_0)->occlusionTexture_0).sample(((&kernelContext_0)->occlusionSampler_0), (_S27)));
                break;
            }
            texture2d<float, access::sample> _S128 = (&kernelContext_0)->occlusionTexture_0;
            thread uint atlasWidth_11;
            thread uint atlasHeight_11;
            (*((&atlasWidth_11)) = (_S128).get_width(0)),(*((&atlasHeight_11)) = (_S128).get_height(0));
            int3 _S129 = int3(int(0), int(0), int(0));
            float4 metadata_11 = round((((&kernelContext_0)->occlusionTexture_0).read(vec<uint,2>(((_S129)).xy), uint(((_S129)).z))) * float4(255.0f) );
            int2 _S130 = int2(metadata_11.zw);
            int2 tile_11 = int2(floor(_S27)) - int2(metadata_11.xy);
            if(any(tile_11 < (int2(int(0)) )))
            {
                hasSceneLighting_0 = true;
            }
            else
            {
                hasSceneLighting_0 = any(tile_11 >= _S130);
            }
            if(hasSceneLighting_0)
            {
                int3 _S131 = int3(int(min(1U, atlasWidth_11 - 1U)), int(0), int(0));
                _S28 = (((&kernelContext_0)->occlusionTexture_0).read(vec<uint,2>(((_S131)).xy), uint(((_S131)).z)));
                break;
            }
            uint _S132 = atlasWidth_11 / uint(_S130.x);
            float _S133 = float(_S132);
            uint _S134 = (atlasHeight_11 - 1U) / uint(_S130.y);
            float2 cellSize_11 = float2(_S133, float(_S134));
            _S28 = (((&kernelContext_0)->occlusionTexture_0).sample(((&kernelContext_0)->occlusionSampler_0), ((float2(tile_11) * cellSize_11 + float2(1.5f, 2.5f) + fract(_S27) * max(cellSize_11 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_11), float(atlasHeight_11)))));
            break;
        }
        for(;;)
        {
            float4 _S135 = float4(_S5->compositeControls_0) ;
            if((_S135.x) != 128.0f)
            {
                break;
            }
            bool _S136 = (_S135.w) >= 0.5f;
            for(;;)
            {
                if(!_S136)
                {
                    _S29 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), (_S27)));
                    break;
                }
                texture2d<float, access::sample> _S137 = (&kernelContext_0)->compositeTexture_0;
                thread uint atlasWidth_12;
                thread uint atlasHeight_12;
                (*((&atlasWidth_12)) = (_S137).get_width(0)),(*((&atlasHeight_12)) = (_S137).get_height(0));
                int3 _S138 = int3(int(0), int(0), int(0));
                float4 metadata_12 = round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S138)).xy), uint(((_S138)).z))) * float4(255.0f) );
                int2 _S139 = int2(metadata_12.zw);
                int2 tile_12 = int2(floor(_S27)) - int2(metadata_12.xy);
                if(any(tile_12 < (int2(int(0)) )))
                {
                    hasSceneLighting_0 = true;
                }
                else
                {
                    hasSceneLighting_0 = any(tile_12 >= _S139);
                }
                if(hasSceneLighting_0)
                {
                    int3 _S140 = int3(int(min(1U, atlasWidth_12 - 1U)), int(0), int(0));
                    _S29 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S140)).xy), uint(((_S140)).z)));
                    break;
                }
                uint _S141 = atlasWidth_12 / uint(_S139.x);
                float _S142 = float(_S141);
                uint _S143 = (atlasHeight_12 - 1U) / uint(_S139.y);
                float2 cellSize_12 = float2(_S142, float(_S143));
                _S29 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), ((float2(tile_12) * cellSize_12 + float2(1.5f, 2.5f) + fract(_S27) * max(cellSize_12 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_12), float(atlasHeight_12)))));
                break;
            }
            uint operation_5 = uint(round(_S135.y));
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
                float factor_5 = _S135.z;
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
        bool _S144 = (udimMask_0 & 256U) != 0U;
        for(;;)
        {
            if(!_S144)
            {
                _S28 = (((&kernelContext_0)->specularColorTexture_0).sample(((&kernelContext_0)->specularColorSampler_0), (_S27)));
                break;
            }
            texture2d<float, access::sample> _S145 = (&kernelContext_0)->specularColorTexture_0;
            thread uint atlasWidth_13;
            thread uint atlasHeight_13;
            (*((&atlasWidth_13)) = (_S145).get_width(0)),(*((&atlasHeight_13)) = (_S145).get_height(0));
            int3 _S146 = int3(int(0), int(0), int(0));
            float4 metadata_13 = round((((&kernelContext_0)->specularColorTexture_0).read(vec<uint,2>(((_S146)).xy), uint(((_S146)).z))) * float4(255.0f) );
            int2 _S147 = int2(metadata_13.zw);
            int2 tile_13 = int2(floor(_S27)) - int2(metadata_13.xy);
            if(any(tile_13 < (int2(int(0)) )))
            {
                hasSceneLighting_0 = true;
            }
            else
            {
                hasSceneLighting_0 = any(tile_13 >= _S147);
            }
            if(hasSceneLighting_0)
            {
                int3 _S148 = int3(int(min(1U, atlasWidth_13 - 1U)), int(0), int(0));
                _S28 = (((&kernelContext_0)->specularColorTexture_0).read(vec<uint,2>(((_S148)).xy), uint(((_S148)).z)));
                break;
            }
            uint _S149 = atlasWidth_13 / uint(_S147.x);
            float _S150 = float(_S149);
            uint _S151 = (atlasHeight_13 - 1U) / uint(_S147.y);
            float2 cellSize_13 = float2(_S150, float(_S151));
            _S28 = (((&kernelContext_0)->specularColorTexture_0).sample(((&kernelContext_0)->specularColorSampler_0), ((float2(tile_13) * cellSize_13 + float2(1.5f, 2.5f) + fract(_S27) * max(cellSize_13 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_13), float(atlasHeight_13)))));
            break;
        }
        for(;;)
        {
            float4 _S152 = float4(_S5->compositeControls_0) ;
            if((_S152.x) != 256.0f)
            {
                break;
            }
            bool _S153 = (_S152.w) >= 0.5f;
            for(;;)
            {
                if(!_S153)
                {
                    _S29 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), (_S27)));
                    break;
                }
                texture2d<float, access::sample> _S154 = (&kernelContext_0)->compositeTexture_0;
                thread uint atlasWidth_14;
                thread uint atlasHeight_14;
                (*((&atlasWidth_14)) = (_S154).get_width(0)),(*((&atlasHeight_14)) = (_S154).get_height(0));
                int3 _S155 = int3(int(0), int(0), int(0));
                float4 metadata_14 = round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S155)).xy), uint(((_S155)).z))) * float4(255.0f) );
                int2 _S156 = int2(metadata_14.zw);
                int2 tile_14 = int2(floor(_S27)) - int2(metadata_14.xy);
                if(any(tile_14 < (int2(int(0)) )))
                {
                    hasSceneLighting_0 = true;
                }
                else
                {
                    hasSceneLighting_0 = any(tile_14 >= _S156);
                }
                if(hasSceneLighting_0)
                {
                    int3 _S157 = int3(int(min(1U, atlasWidth_14 - 1U)), int(0), int(0));
                    _S29 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S157)).xy), uint(((_S157)).z)));
                    break;
                }
                uint _S158 = atlasWidth_14 / uint(_S156.x);
                float _S159 = float(_S158);
                uint _S160 = (atlasHeight_14 - 1U) / uint(_S156.y);
                float2 cellSize_14 = float2(_S159, float(_S160));
                _S29 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), ((float2(tile_14) * cellSize_14 + float2(1.5f, 2.5f) + fract(_S27) * max(cellSize_14 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_14), float(atlasHeight_14)))));
                break;
            }
            uint operation_6 = uint(round(_S152.y));
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
                float factor_6 = _S152.z;
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
        bool _S161 = (udimMask_0 & 512U) != 0U;
        for(;;)
        {
            if(!_S161)
            {
                _S28 = (((&kernelContext_0)->clearcoatTexture_0).sample(((&kernelContext_0)->clearcoatSampler_0), (_S27)));
                break;
            }
            texture2d<float, access::sample> _S162 = (&kernelContext_0)->clearcoatTexture_0;
            thread uint atlasWidth_15;
            thread uint atlasHeight_15;
            (*((&atlasWidth_15)) = (_S162).get_width(0)),(*((&atlasHeight_15)) = (_S162).get_height(0));
            int3 _S163 = int3(int(0), int(0), int(0));
            float4 metadata_15 = round((((&kernelContext_0)->clearcoatTexture_0).read(vec<uint,2>(((_S163)).xy), uint(((_S163)).z))) * float4(255.0f) );
            int2 _S164 = int2(metadata_15.zw);
            int2 tile_15 = int2(floor(_S27)) - int2(metadata_15.xy);
            if(any(tile_15 < (int2(int(0)) )))
            {
                hasSceneLighting_0 = true;
            }
            else
            {
                hasSceneLighting_0 = any(tile_15 >= _S164);
            }
            if(hasSceneLighting_0)
            {
                int3 _S165 = int3(int(min(1U, atlasWidth_15 - 1U)), int(0), int(0));
                _S28 = (((&kernelContext_0)->clearcoatTexture_0).read(vec<uint,2>(((_S165)).xy), uint(((_S165)).z)));
                break;
            }
            uint _S166 = atlasWidth_15 / uint(_S164.x);
            float _S167 = float(_S166);
            uint _S168 = (atlasHeight_15 - 1U) / uint(_S164.y);
            float2 cellSize_15 = float2(_S167, float(_S168));
            _S28 = (((&kernelContext_0)->clearcoatTexture_0).sample(((&kernelContext_0)->clearcoatSampler_0), ((float2(tile_15) * cellSize_15 + float2(1.5f, 2.5f) + fract(_S27) * max(cellSize_15 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_15), float(atlasHeight_15)))));
            break;
        }
        for(;;)
        {
            float4 _S169 = float4(_S5->compositeControls_0) ;
            if((_S169.x) != 512.0f)
            {
                break;
            }
            bool _S170 = (_S169.w) >= 0.5f;
            for(;;)
            {
                if(!_S170)
                {
                    _S29 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), (_S27)));
                    break;
                }
                texture2d<float, access::sample> _S171 = (&kernelContext_0)->compositeTexture_0;
                thread uint atlasWidth_16;
                thread uint atlasHeight_16;
                (*((&atlasWidth_16)) = (_S171).get_width(0)),(*((&atlasHeight_16)) = (_S171).get_height(0));
                int3 _S172 = int3(int(0), int(0), int(0));
                float4 metadata_16 = round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S172)).xy), uint(((_S172)).z))) * float4(255.0f) );
                int2 _S173 = int2(metadata_16.zw);
                int2 tile_16 = int2(floor(_S27)) - int2(metadata_16.xy);
                if(any(tile_16 < (int2(int(0)) )))
                {
                    hasSceneLighting_0 = true;
                }
                else
                {
                    hasSceneLighting_0 = any(tile_16 >= _S173);
                }
                if(hasSceneLighting_0)
                {
                    int3 _S174 = int3(int(min(1U, atlasWidth_16 - 1U)), int(0), int(0));
                    _S29 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S174)).xy), uint(((_S174)).z)));
                    break;
                }
                uint _S175 = atlasWidth_16 / uint(_S173.x);
                float _S176 = float(_S175);
                uint _S177 = (atlasHeight_16 - 1U) / uint(_S173.y);
                float2 cellSize_16 = float2(_S176, float(_S177));
                _S29 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), ((float2(tile_16) * cellSize_16 + float2(1.5f, 2.5f) + fract(_S27) * max(cellSize_16 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_16), float(atlasHeight_16)))));
                break;
            }
            uint operation_7 = uint(round(_S169.y));
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
                float factor_7 = _S169.z;
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
        bool _S178 = (udimMask_0 & 1024U) != 0U;
        for(;;)
        {
            if(!_S178)
            {
                _S28 = (((&kernelContext_0)->clearcoatRoughnessTexture_0).sample(((&kernelContext_0)->clearcoatRoughnessSampler_0), (_S27)));
                break;
            }
            texture2d<float, access::sample> _S179 = (&kernelContext_0)->clearcoatRoughnessTexture_0;
            thread uint atlasWidth_17;
            thread uint atlasHeight_17;
            (*((&atlasWidth_17)) = (_S179).get_width(0)),(*((&atlasHeight_17)) = (_S179).get_height(0));
            int3 _S180 = int3(int(0), int(0), int(0));
            float4 metadata_17 = round((((&kernelContext_0)->clearcoatRoughnessTexture_0).read(vec<uint,2>(((_S180)).xy), uint(((_S180)).z))) * float4(255.0f) );
            int2 _S181 = int2(metadata_17.zw);
            int2 tile_17 = int2(floor(_S27)) - int2(metadata_17.xy);
            if(any(tile_17 < (int2(int(0)) )))
            {
                hasSceneLighting_0 = true;
            }
            else
            {
                hasSceneLighting_0 = any(tile_17 >= _S181);
            }
            if(hasSceneLighting_0)
            {
                int3 _S182 = int3(int(min(1U, atlasWidth_17 - 1U)), int(0), int(0));
                _S28 = (((&kernelContext_0)->clearcoatRoughnessTexture_0).read(vec<uint,2>(((_S182)).xy), uint(((_S182)).z)));
                break;
            }
            uint _S183 = atlasWidth_17 / uint(_S181.x);
            float _S184 = float(_S183);
            uint _S185 = (atlasHeight_17 - 1U) / uint(_S181.y);
            float2 cellSize_17 = float2(_S184, float(_S185));
            _S28 = (((&kernelContext_0)->clearcoatRoughnessTexture_0).sample(((&kernelContext_0)->clearcoatRoughnessSampler_0), ((float2(tile_17) * cellSize_17 + float2(1.5f, 2.5f) + fract(_S27) * max(cellSize_17 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_17), float(atlasHeight_17)))));
            break;
        }
        for(;;)
        {
            float4 _S186 = float4(_S5->compositeControls_0) ;
            if((_S186.x) != 1024.0f)
            {
                break;
            }
            bool _S187 = (_S186.w) >= 0.5f;
            for(;;)
            {
                if(!_S187)
                {
                    _S29 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), (_S27)));
                    break;
                }
                texture2d<float, access::sample> _S188 = (&kernelContext_0)->compositeTexture_0;
                thread uint atlasWidth_18;
                thread uint atlasHeight_18;
                (*((&atlasWidth_18)) = (_S188).get_width(0)),(*((&atlasHeight_18)) = (_S188).get_height(0));
                int3 _S189 = int3(int(0), int(0), int(0));
                float4 metadata_18 = round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S189)).xy), uint(((_S189)).z))) * float4(255.0f) );
                int2 _S190 = int2(metadata_18.zw);
                int2 tile_18 = int2(floor(_S27)) - int2(metadata_18.xy);
                if(any(tile_18 < (int2(int(0)) )))
                {
                    hasSceneLighting_0 = true;
                }
                else
                {
                    hasSceneLighting_0 = any(tile_18 >= _S190);
                }
                if(hasSceneLighting_0)
                {
                    int3 _S191 = int3(int(min(1U, atlasWidth_18 - 1U)), int(0), int(0));
                    _S29 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S191)).xy), uint(((_S191)).z)));
                    break;
                }
                uint _S192 = atlasWidth_18 / uint(_S190.x);
                float _S193 = float(_S192);
                uint _S194 = (atlasHeight_18 - 1U) / uint(_S190.y);
                float2 cellSize_18 = float2(_S193, float(_S194));
                _S29 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), ((float2(tile_18) * cellSize_18 + float2(1.5f, 2.5f) + fract(_S27) * max(cellSize_18 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_18), float(atlasHeight_18)))));
                break;
            }
            uint operation_8 = uint(round(_S186.y));
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
                float factor_8 = _S186.z;
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
        bool _S195 = (udimMask_0 & 2048U) != 0U;
        for(;;)
        {
            if(!_S195)
            {
                _S28 = (((&kernelContext_0)->iorTexture_0).sample(((&kernelContext_0)->iorSampler_0), (_S27)));
                break;
            }
            texture2d<float, access::sample> _S196 = (&kernelContext_0)->iorTexture_0;
            thread uint atlasWidth_19;
            thread uint atlasHeight_19;
            (*((&atlasWidth_19)) = (_S196).get_width(0)),(*((&atlasHeight_19)) = (_S196).get_height(0));
            int3 _S197 = int3(int(0), int(0), int(0));
            float4 metadata_19 = round((((&kernelContext_0)->iorTexture_0).read(vec<uint,2>(((_S197)).xy), uint(((_S197)).z))) * float4(255.0f) );
            int2 _S198 = int2(metadata_19.zw);
            int2 tile_19 = int2(floor(_S27)) - int2(metadata_19.xy);
            if(any(tile_19 < (int2(int(0)) )))
            {
                hasSceneLighting_0 = true;
            }
            else
            {
                hasSceneLighting_0 = any(tile_19 >= _S198);
            }
            if(hasSceneLighting_0)
            {
                int3 _S199 = int3(int(min(1U, atlasWidth_19 - 1U)), int(0), int(0));
                _S28 = (((&kernelContext_0)->iorTexture_0).read(vec<uint,2>(((_S199)).xy), uint(((_S199)).z)));
                break;
            }
            uint _S200 = atlasWidth_19 / uint(_S198.x);
            float _S201 = float(_S200);
            uint _S202 = (atlasHeight_19 - 1U) / uint(_S198.y);
            float2 cellSize_19 = float2(_S201, float(_S202));
            _S28 = (((&kernelContext_0)->iorTexture_0).sample(((&kernelContext_0)->iorSampler_0), ((float2(tile_19) * cellSize_19 + float2(1.5f, 2.5f) + fract(_S27) * max(cellSize_19 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_19), float(atlasHeight_19)))));
            break;
        }
        for(;;)
        {
            float4 _S203 = float4(_S5->compositeControls_0) ;
            if((_S203.x) != 2048.0f)
            {
                break;
            }
            bool _S204 = (_S203.w) >= 0.5f;
            for(;;)
            {
                if(!_S204)
                {
                    _S29 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), (_S27)));
                    break;
                }
                texture2d<float, access::sample> _S205 = (&kernelContext_0)->compositeTexture_0;
                thread uint atlasWidth_20;
                thread uint atlasHeight_20;
                (*((&atlasWidth_20)) = (_S205).get_width(0)),(*((&atlasHeight_20)) = (_S205).get_height(0));
                int3 _S206 = int3(int(0), int(0), int(0));
                float4 metadata_20 = round((((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S206)).xy), uint(((_S206)).z))) * float4(255.0f) );
                int2 _S207 = int2(metadata_20.zw);
                int2 tile_20 = int2(floor(_S27)) - int2(metadata_20.xy);
                if(any(tile_20 < (int2(int(0)) )))
                {
                    hasSceneLighting_0 = true;
                }
                else
                {
                    hasSceneLighting_0 = any(tile_20 >= _S207);
                }
                if(hasSceneLighting_0)
                {
                    int3 _S208 = int3(int(min(1U, atlasWidth_20 - 1U)), int(0), int(0));
                    _S29 = (((&kernelContext_0)->compositeTexture_0).read(vec<uint,2>(((_S208)).xy), uint(((_S208)).z)));
                    break;
                }
                uint _S209 = atlasWidth_20 / uint(_S207.x);
                float _S210 = float(_S209);
                uint _S211 = (atlasHeight_20 - 1U) / uint(_S207.y);
                float2 cellSize_20 = float2(_S210, float(_S211));
                _S29 = (((&kernelContext_0)->compositeTexture_0).sample(((&kernelContext_0)->compositeSampler_0), ((float2(tile_20) * cellSize_20 + float2(1.5f, 2.5f) + fract(_S27) * max(cellSize_20 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_20), float(atlasHeight_20)))));
                break;
            }
            uint operation_9 = uint(round(_S203.y));
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
                float factor_9 = _S203.z;
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
    float3 normal_1 = normalize(shadingNormal_0);
    float3 worldNormal_1 = normalize(worldShadingNormal_0);
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
    float _S212 = saturate(abs(dot(normal_2, irradiance_0)) + 0.00000999999974738f);
    float _S213 = max(0.00100000004749745f, roughness_0);
    float _S214 = max(0.00100000004749745f, clearcoatRoughness_0);
    float reflectanceRatio_0 = (1.0f - ior_0) / (1.0f + ior_0);
    float3 _S215 = float3(3.14159274101257324f) ;
    float3 diffuse_0 = diffuseColor_0 / _S215;
    float3 normalIncidence_0;
    float3 grazingIncidence_0;
    float3 diffuse_1;
    if((_S16.w) >= 0.5f)
    {
        float3 _S216 = float3(1.0f, 1.0f, 1.0f);
        normalIncidence_0 = specularColor_0;
        grazingIncidence_0 = _S216;
        diffuse_1 = diffuse_0;
    }
    else
    {
        float3 _S217 = float3(metallic_0) ;
        float3 specularTint_0 = mix(float3(1.0f, 1.0f, 1.0f), diffuseColor_0, _S217);
        float3 diffuse_2 = diffuse_0 * float3((1.0f - metallic_0)) ;
        normalIncidence_0 = mix(float3((reflectanceRatio_0 * reflectanceRatio_0))  * specularTint_0, specularTint_0, _S217);
        grazingIncidence_0 = specularTint_0;
        diffuse_1 = diffuse_2;
    }
    float4 _S218 = float4(_S6->ambientLight_0) ;
    float _S219 = _S218.w;
    if(_S219 > 0.5f)
    {
        hasSceneLighting_0 = true;
    }
    else
    {
        float3 _S220 = _S218.xyz;
        hasSceneLighting_0 = (dot(_S220, _S220)) > 0.0f;
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
    uint _S221 = min(uint(_S219), 8U);
    matrix<float,int(4),int(4)>  _S222 = matrix<float,int(4),int(4)> (_S6->eyeToWorld_0.data_0[int(0)][int(0)], _S6->eyeToWorld_0.data_0[int(0)][int(1)], _S6->eyeToWorld_0.data_0[int(0)][int(2)], _S6->eyeToWorld_0.data_0[int(0)][int(3)], _S6->eyeToWorld_0.data_0[int(1)][int(0)], _S6->eyeToWorld_0.data_0[int(1)][int(1)], _S6->eyeToWorld_0.data_0[int(1)][int(2)], _S6->eyeToWorld_0.data_0[int(1)][int(3)], _S6->eyeToWorld_0.data_0[int(2)][int(0)], _S6->eyeToWorld_0.data_0[int(2)][int(1)], _S6->eyeToWorld_0.data_0[int(2)][int(2)], _S6->eyeToWorld_0.data_0[int(2)][int(3)], _S6->eyeToWorld_0.data_0[int(3)][int(0)], _S6->eyeToWorld_0.data_0[int(3)][int(1)], _S6->eyeToWorld_0.data_0[int(3)][int(2)], _S6->eyeToWorld_0.data_0[int(3)][int(3)]);
    float3 sceneEye_0 = normalize((((float4(irradiance_0, 0.0f)) * (_S222))).xyz);
    float3 worldPosition_0 = (((float4(_S1.eyePosition_0, 1.0f)) * (_S222))).xyz;
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
    float4 _S223 = float4(_S5->lightColorAmbient_0) ;
    float3 color_0 = diffuseColor_0 * float3(_S223.w) ;
    uint domeLinkMask_0 = uint(max((float4(_S5->domeLinkControls_0) ).x, 0.0f));
    float4 _S224 = float4(_S6->domeControls_0) ;
    uint _S225 = min(uint(max(_S224.x, 0.0f)), 8U);
    uint allDomes_0 = (1U << _S225) - 1U;
    bool allDomesLinked_0 = (domeLinkMask_0 & allDomes_0) == allDomes_0;
    float3 _S226 = _S218.xyz;
    uint lightCount_0;
    float3 domeAmbient_1;
    if(!allDomesLinked_0)
    {
        float3 _S227 = float3(0.0f, 0.0f, 0.0f);
        lightCount_0 = 0U;
        domeAmbient_1 = _S227;
        for(;;)
        {
            if(lightCount_0 < _S225)
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
        domeAmbient_1 = _S226;
    }
    float3 color_1 = color_0 + diffuseColor_0 * domeAmbient_1;
    bool _S228 = !hasSceneLighting_0;
    if(_S228)
    {
        lightCount_0 = 1U;
    }
    else
    {
        lightCount_0 = _S221;
    }
    uint _S229 = uint(max(_S10.w, 0.0f));
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
        bool _S230;
        if(hasSceneLighting_0)
        {
            _S230 = (_S229 & (1U << lightIndex_0)) == 0U;
        }
        else
        {
            _S230 = false;
        }
        if(_S230)
        {
            lightIndex_0 = lightIndex_0 + 1U;
            continue;
        }
        bool _S231 = lightIndex_0 == 0U;
        bool _S232;
        if(_S231)
        {
            _S232 = _S228;
        }
        else
        {
            _S232 = false;
        }
        float lightType_0;
        if(_S232)
        {
            lightType_0 = 1.0f;
        }
        else
        {
            lightType_0 = (float4((&_S6->lightPositionType_0)->data_1[lightIndex_0]) ).w;
        }
        bool _S233;
        if(_S231)
        {
            _S233 = _S228;
        }
        else
        {
            _S233 = false;
        }
        if(_S233)
        {
            lightDirection_0 = normalize((float4(_S5->lightDirectionIntensity_0) ).xyz);
        }
        else
        {
            lightDirection_0 = normalize((float4((&_S6->lightDirectionRadius_0)->data_1[lightIndex_0]) ).xyz);
        }
        bool _S234;
        if(_S231)
        {
            _S234 = _S228;
        }
        else
        {
            _S234 = false;
        }
        if(_S234)
        {
            roughness_0 = (float4(_S5->lightDirectionIntensity_0) ).w;
        }
        else
        {
            roughness_0 = (float4((&_S6->lightColorIntensity_0)->data_1[lightIndex_0]) ).w;
        }
        bool _S235;
        if(_S231)
        {
            _S235 = _S228;
        }
        else
        {
            _S235 = false;
        }
        if(_S235)
        {
            diffuseColor_0 = _S223.xyz;
        }
        else
        {
            diffuseColor_0 = (float4((&_S6->lightColorIntensity_0)->data_1[lightIndex_0]) ).xyz;
        }
        bool _S236;
        if(_S231)
        {
            _S236 = _S228;
        }
        else
        {
            _S236 = false;
        }
        if(_S236)
        {
            metallic_0 = 1.0f;
        }
        else
        {
            metallic_0 = (float4((&_S6->lightControls_0)->data_1[lightIndex_0]) ).x;
        }
        bool _S237;
        if(_S231)
        {
            _S237 = _S228;
        }
        else
        {
            _S237 = false;
        }
        if(_S237)
        {
            clearcoatRoughness_0 = 1.0f;
        }
        else
        {
            clearcoatRoughness_0 = (float4((&_S6->lightControls_0)->data_1[lightIndex_0]) ).y;
        }
        bool _S238;
        if(_S231)
        {
            _S238 = _S228;
        }
        else
        {
            _S238 = false;
        }
        if(_S238)
        {
            lightTangent_0 = float3(1.0f, 0.0f, 0.0f);
        }
        else
        {
            lightTangent_0 = normalize((float4((&_S6->lightTangentShapeX_0)->data_1[lightIndex_0]) ).xyz);
        }
        bool _S239;
        if(_S231)
        {
            _S239 = _S228;
        }
        else
        {
            _S239 = false;
        }
        if(_S239)
        {
            lightBitangent_0 = float3(0.0f, 1.0f, 0.0f);
        }
        else
        {
            lightBitangent_0 = normalize((float4((&_S6->lightBitangentShapeY_0)->data_1[lightIndex_0]) ).xyz);
        }
        bool _S240;
        if(_S231)
        {
            _S240 = _S228;
        }
        else
        {
            _S240 = false;
        }
        float shapeX_0;
        if(_S240)
        {
            shapeX_0 = 0.0f;
        }
        else
        {
            shapeX_0 = (float4((&_S6->lightTangentShapeX_0)->data_1[lightIndex_0]) ).w;
        }
        bool _S241;
        if(_S231)
        {
            _S241 = _S228;
        }
        else
        {
            _S241 = false;
        }
        float shapeY_0;
        if(_S241)
        {
            shapeY_0 = 0.0f;
        }
        else
        {
            shapeY_0 = (float4((&_S6->lightBitangentShapeY_0)->data_1[lightIndex_0]) ).w;
        }
        bool _S242;
        if(_S231)
        {
            _S242 = _S228;
        }
        else
        {
            _S242 = false;
        }
        float lightRadius_0;
        if(_S242)
        {
            lightRadius_0 = 0.0f;
        }
        else
        {
            lightRadius_0 = (float4((&_S6->lightDirectionRadius_0)->data_1[lightIndex_0]) ).w;
        }
        bool _S243;
        if(_S231)
        {
            _S243 = _S228;
        }
        else
        {
            _S243 = false;
        }
        if(_S243)
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
                    float4 _S244 = float4((&_S6->shadowControls_0)->data_3[shadowSlot_0]) ;
                    float4 _S245 = float4((&_S6->shadowTile_0)->data_3[shadowSlot_0]) ;
                    if((dot(specularColor_0, lightDirection_0)) < 0.0f)
                    {
                        color_3 = - specularColor_0;
                    }
                    else
                    {
                        color_3 = specularColor_0;
                    }
                    float slope_0 = clamp(1.0f - saturate(dot(color_3, lightDirection_0)), 0.0f, 1.0f);
                    float4 lightClip_0 = (((float4(worldPosition_0 + color_3 * float3((_S244.y * slope_0)) , 1.0f)) * (matrix<float,int(4),int(4)> ((&_S6->shadowWorldToLightClip_0)->data_2[shadowSlot_0].data_0[int(0)][int(0)], (&_S6->shadowWorldToLightClip_0)->data_2[shadowSlot_0].data_0[int(0)][int(1)], (&_S6->shadowWorldToLightClip_0)->data_2[shadowSlot_0].data_0[int(0)][int(2)], (&_S6->shadowWorldToLightClip_0)->data_2[shadowSlot_0].data_0[int(0)][int(3)], (&_S6->shadowWorldToLightClip_0)->data_2[shadowSlot_0].data_0[int(1)][int(0)], (&_S6->shadowWorldToLightClip_0)->data_2[shadowSlot_0].data_0[int(1)][int(1)], (&_S6->shadowWorldToLightClip_0)->data_2[shadowSlot_0].data_0[int(1)][int(2)], (&_S6->shadowWorldToLightClip_0)->data_2[shadowSlot_0].data_0[int(1)][int(3)], (&_S6->shadowWorldToLightClip_0)->data_2[shadowSlot_0].data_0[int(2)][int(0)], (&_S6->shadowWorldToLightClip_0)->data_2[shadowSlot_0].data_0[int(2)][int(1)], (&_S6->shadowWorldToLightClip_0)->data_2[shadowSlot_0].data_0[int(2)][int(2)], (&_S6->shadowWorldToLightClip_0)->data_2[shadowSlot_0].data_0[int(2)][int(3)], (&_S6->shadowWorldToLightClip_0)->data_2[shadowSlot_0].data_0[int(3)][int(0)], (&_S6->shadowWorldToLightClip_0)->data_2[shadowSlot_0].data_0[int(3)][int(1)], (&_S6->shadowWorldToLightClip_0)->data_2[shadowSlot_0].data_0[int(3)][int(2)], (&_S6->shadowWorldToLightClip_0)->data_2[shadowSlot_0].data_0[int(3)][int(3)]))));
                    float _S246 = lightClip_0.w;
                    if(_S246 <= 0.0f)
                    {
                        ior_0 = 1.0f;
                        break;
                    }
                    float3 ndc_0 = lightClip_0.xyz / float3(_S246) ;
                    bool _S247;
                    if((abs(ndc_0.x)) > 1.0f)
                    {
                        _S247 = true;
                    }
                    else
                    {
                        _S247 = (abs(ndc_0.y)) > 1.0f;
                    }
                    bool _S248;
                    if(_S247)
                    {
                        _S248 = true;
                    }
                    else
                    {
                        _S248 = (ndc_0.z) < 0.0f;
                    }
                    bool _S249;
                    if(_S248)
                    {
                        _S249 = true;
                    }
                    else
                    {
                        _S249 = (ndc_0.z) > 1.0f;
                    }
                    if(_S249)
                    {
                        ior_0 = 1.0f;
                        break;
                    }
                    float2 _S250 = _S245.xy;
                    float2 _S251 = _S245.zw;
                    float2 _S252 = _S250 + (ndc_0.xy * float2(0.5f, -0.5f) + float2(0.5f, 0.5f)) * _S251;
                    float texel_0 = _S244.w;
                    float _S253 = max(_S244.z, 0.0f);
                    float _S254 = ndc_0.z - _S244.x * (1.0f + 2.0f * slope_0);
                    float2 _S255 = float2((texel_0 * 0.5f)) ;
                    float2 _S256 = _S250 + _S255;
                    float2 _S257 = _S250 + _S251 - _S255;
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
                            if(_S254 <= (((&kernelContext_0)->shadowAtlas_0).sample(((&kernelContext_0)->shadowSampler_0), (clamp(_S252 + float2(float(x_0), float(y_0)) * float2((_S253 * texel_0)) , _S256, _S257)), level((0.0f))).x))
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
        float3 _S258 = float3(0.0f, 0.0f, 0.0f);
        sampleOffsets_0[int(0)] = _S258;
        sampleOffsets_0[int(1)] = _S258;
        sampleOffsets_0[int(2)] = _S258;
        sampleOffsets_0[int(3)] = _S258;
        sampleOffsets_0[int(4)] = _S258;
        float sampleCount_0;
        if(lightType_0 == 3.0f)
        {
            float3 halfWidth_0 = lightTangent_0 * float3((shapeX_0 * 0.5f)) ;
            float3 halfHeight_0 = lightBitangent_0 * float3((shapeY_0 * 0.5f)) ;
            sampleOffsets_0[int(1)] = halfWidth_0 + halfHeight_0;
            sampleOffsets_0[int(2)] = halfWidth_0 - halfHeight_0;
            float3 _S259 = - halfWidth_0;
            sampleOffsets_0[int(3)] = _S259 + halfHeight_0;
            sampleOffsets_0[int(4)] = _S259 - halfHeight_0;
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
                float _S260 = max(dot(toLight_0, toLight_0), 0.00100000004749745f);
                float3 sampleDirection_1 = toLight_0 * float3(rsqrt(_S260)) ;
                float sampleIntensity_2 = sampleIntensity_0 / _S260;
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
            float3 _S261 = float3(pow(max(0.0f, 1.0f - saturate(dot(domeAmbient_1, half_0))), 5.0f)) ;
            float3 _S262 = mix(normalIncidence_0, grazingIncidence_0, _S261);
            float3 directDiffuse_0 = diffuse_1 * (_S55 - _S262);
            float _S263 = max(_S213, 0.00100000004749745f);
            float alpha_0 = _S263 * _S263;
            float alphaSquared_0 = alpha_0 * alpha_0;
            float lobeCosineSquared_0 = saturate(normalDotHalf_0 * normalDotHalf_0);
            float lobeComplement_0 = 1.0f - lobeCosineSquared_0;
            float denominator_0 = lobeCosineSquared_0 * alphaSquared_0 + lobeComplement_0;
            float k_0 = alpha_0 * 0.5f;
            float _S264 = 1.0f - k_0;
            float3 _S265 = float3(max(4.0f * normalDotLight_0 * _S212, 1.00000000317107685e-30f)) ;
            float3 _S266 = _S262 * float3((_S212 / (_S212 * _S264 + k_0) * (normalDotLight_0 / (normalDotLight_0 * _S264 + k_0))))  * float3((alphaSquared_0 / max(3.14159274101257324f * denominator_0 * denominator_0, 1.00000000317107685e-30f)))  / _S265;
            float3 directSpecular_0;
            if(clearcoatAmount_0 > 0.0f)
            {
                float _S267 = max(_S214, 0.00100000004749745f);
                float alpha_1 = _S267 * _S267;
                float alphaSquared_1 = alpha_1 * alpha_1;
                float denominator_1 = lobeCosineSquared_0 * alphaSquared_1 + lobeComplement_0;
                float k_1 = alpha_1 * 0.5f;
                float _S268 = 1.0f - k_1;
                directSpecular_0 = _S266 + float3(clearcoatAmount_0)  * (mix(float3((reflectanceRatio_0 * reflectanceRatio_0)) , float3(1.0f, 1.0f, 1.0f), _S261) * float3((_S212 / (_S212 * _S268 + k_1) * (normalDotLight_0 / (normalDotLight_0 * _S268 + k_1))))  * float3((alphaSquared_1 / max(3.14159274101257324f * denominator_1 * denominator_1, 1.00000000317107685e-30f)))  / _S265);
            }
            else
            {
                directSpecular_0 = _S266;
            }
            float3 _S269 = diffuseColor_0 * float3(sampleIntensity_1) ;
            color_3 = color_3 + float3((shadowVisibility_0 * occlusion_0 * emissionScale_0 * normalDotLight_0))  * (directDiffuse_0 * float3(metallic_0)  * (_S269 * _S215) + directSpecular_0 * float3(clearcoatRoughness_0)  * _S269);
            sampleIndex_0 = sampleIndex_0 + 1U;
        }
        color_2 = color_3;
        lightIndex_0 = lightIndex_0 + 1U;
    }
    float4 _S270 = float4(_S6->environmentControls_0) ;
    if((_S270.x) >= 0.5f)
    {
        float _S271 = saturate(saturate(abs(dot(worldNormal_2, sceneEye_0)) + 0.00000999999974738f));
        float _S272 = saturate(_S213);
        float2 _S273 = (((&kernelContext_0)->environmentBrdf_0).sample(((&kernelContext_0)->environmentBrdfSampler_0), (float2(_S271, _S272)), level((0.0f)))).xy;
        float3 specularWeight_0 = normalIncidence_0 * float3(_S273.x)  + grazingIncidence_0 * float3(_S273.y) ;
        float3 diffuseWeight_0 = saturate(float3(1.0f, 1.0f, 1.0f) - specularWeight_0);
        float3 reflectionDirection_0 = reflect(- sceneEye_0, worldNormal_2);
        float _S274 = max(_S224.y, 1.0f);
        float3 _S275 = float3(0.0f, 0.0f, 0.0f);
        if(allDomesLinked_0)
        {
            hasSceneLighting_0 = true;
        }
        else
        {
            hasSceneLighting_0 = _S274 <= 1.0f;
        }
        if(hasSceneLighting_0)
        {
            float composedGroup_0 = _S224.z;
            float _S276 = _S224.w;
            for(;;)
            {
                bool _S277 = _S274 <= 1.0f;
                _S3 = _S277;
                if(_S277)
                {
                    float3 unit_0 = normalize(worldNormal_2);
                    diffuseColor_0 = (((&kernelContext_0)->environmentIrradiance_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(fract((atan2(unit_0.z, unit_0.x) + 1.57079637050628662f) / 6.28318548202514648f), acos(clamp(unit_0.y, -1.0f, 1.0f)) / 3.14159274101257324f)), level((0.0f)))).xyz;
                    break;
                }
                float3 unit_1 = normalize(worldNormal_2);
                float inset_0 = 0.5f / max(_S276, 1.0f);
                diffuseColor_0 = (((&kernelContext_0)->environmentIrradiance_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(fract((atan2(unit_1.z, unit_1.x) + 1.57079637050628662f) / 6.28318548202514648f), (composedGroup_0 + clamp(acos(clamp(unit_1.y, -1.0f, 1.0f)) / 3.14159274101257324f, inset_0, 1.0f - inset_0)) / _S274)), level((0.0f)))).xyz;
                break;
            }
            float _S278 = _S270.y;
            float _S279 = _S270.z;
            for(;;)
            {
                if(_S3)
                {
                    float3 unit_2 = normalize(reflectionDirection_0);
                    float u_0 = fract((atan2(unit_2.z, unit_2.x) + 1.57079637050628662f) / 6.28318548202514648f);
                    float _S280 = max(_S278, 1.0f);
                    float _S281 = _S280 - 1.0f;
                    float slice_0 = _S272 * max(_S281, 0.0f);
                    float lower_0 = floor(slice_0);
                    float inset_1 = 0.5f / max(_S279, 1.0f);
                    float v_0 = clamp(acos(clamp(unit_2.y, -1.0f, 1.0f)) / 3.14159274101257324f, inset_1, 1.0f - inset_1);
                    specularColor_0 = mix((((&kernelContext_0)->environmentSpecular_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(u_0, (lower_0 + v_0) / _S280)), level((0.0f)))).xyz, (((&kernelContext_0)->environmentSpecular_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(u_0, (min(lower_0 + 1.0f, _S281) + v_0) / _S280)), level((0.0f)))).xyz, float3((slice_0 - lower_0)) );
                    break;
                }
                float3 unit_3 = normalize(reflectionDirection_0);
                float u_1 = fract((atan2(unit_3.z, unit_3.x) + 1.57079637050628662f) / 6.28318548202514648f);
                float _S282 = max(_S278, 1.0f);
                float total_0 = _S282 * _S274;
                float _S283 = _S282 - 1.0f;
                float slice_1 = _S272 * max(_S283, 0.0f);
                float lower_1 = floor(slice_1);
                float inset_2 = 0.5f / max(_S279, 1.0f);
                float v_1 = clamp(acos(clamp(unit_3.y, -1.0f, 1.0f)) / 3.14159274101257324f, inset_2, 1.0f - inset_2);
                float base_0 = composedGroup_0 * _S282;
                specularColor_0 = mix((((&kernelContext_0)->environmentSpecular_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(u_1, (base_0 + lower_1 + v_1) / total_0)), level((0.0f)))).xyz, (((&kernelContext_0)->environmentSpecular_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(u_1, (base_0 + min(lower_1 + 1.0f, _S283) + v_1) / total_0)), level((0.0f)))).xyz, float3((slice_1 - lower_1)) );
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
                        float _S284 = max(_S278, 1.0f);
                        float _S285 = _S284 - 1.0f;
                        float slice_2 = saturate(_S214) * max(_S285, 0.0f);
                        float lower_2 = floor(slice_2);
                        float inset_3 = 0.5f / max(_S279, 1.0f);
                        float v_2 = clamp(acos(clamp(unit_4.y, -1.0f, 1.0f)) / 3.14159274101257324f, inset_3, 1.0f - inset_3);
                        irradiance_0 = mix((((&kernelContext_0)->environmentSpecular_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(u_2, (lower_2 + v_2) / _S284)), level((0.0f)))).xyz, (((&kernelContext_0)->environmentSpecular_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(u_2, (min(lower_2 + 1.0f, _S285) + v_2) / _S284)), level((0.0f)))).xyz, float3((slice_2 - lower_2)) );
                        break;
                    }
                    float3 unit_5 = normalize(reflectionDirection_0);
                    float u_3 = fract((atan2(unit_5.z, unit_5.x) + 1.57079637050628662f) / 6.28318548202514648f);
                    float _S286 = max(_S278, 1.0f);
                    float total_1 = _S286 * _S274;
                    float _S287 = _S286 - 1.0f;
                    float slice_3 = saturate(_S214) * max(_S287, 0.0f);
                    float lower_3 = floor(slice_3);
                    float inset_4 = 0.5f / max(_S279, 1.0f);
                    float v_3 = clamp(acos(clamp(unit_5.y, -1.0f, 1.0f)) / 3.14159274101257324f, inset_4, 1.0f - inset_4);
                    float base_1 = composedGroup_0 * _S286;
                    irradiance_0 = mix((((&kernelContext_0)->environmentSpecular_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(u_3, (base_1 + lower_3 + v_3) / total_1)), level((0.0f)))).xyz, (((&kernelContext_0)->environmentSpecular_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(u_3, (base_1 + min(lower_3 + 1.0f, _S287) + v_3) / total_1)), level((0.0f)))).xyz, float3((slice_3 - lower_3)) );
                    break;
                }
                lightTangent_0 = irradiance_0;
            }
            else
            {
                lightTangent_0 = _S275;
            }
            irradiance_0 = diffuseColor_0;
            lightDirection_0 = specularColor_0;
        }
        else
        {
            sampleIndex_0 = 0U;
            irradiance_0 = _S275;
            lightDirection_0 = _S275;
            lightTangent_0 = _S275;
            for(;;)
            {
                if(sampleIndex_0 < _S225)
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
                float _S288 = _S224.w;
                for(;;)
                {
                    bool _S289 = _S274 <= 1.0f;
                    _S4 = _S289;
                    if(_S289)
                    {
                        float3 unit_6 = normalize(worldNormal_2);
                        diffuseColor_0 = (((&kernelContext_0)->environmentIrradiance_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(fract((atan2(unit_6.z, unit_6.x) + 1.57079637050628662f) / 6.28318548202514648f), acos(clamp(unit_6.y, -1.0f, 1.0f)) / 3.14159274101257324f)), level((0.0f)))).xyz;
                        break;
                    }
                    float3 unit_7 = normalize(worldNormal_2);
                    float inset_5 = 0.5f / max(_S288, 1.0f);
                    diffuseColor_0 = (((&kernelContext_0)->environmentIrradiance_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(fract((atan2(unit_7.z, unit_7.x) + 1.57079637050628662f) / 6.28318548202514648f), (domeGroup_0 + clamp(acos(clamp(unit_7.y, -1.0f, 1.0f)) / 3.14159274101257324f, inset_5, 1.0f - inset_5)) / _S274)), level((0.0f)))).xyz;
                    break;
                }
                float3 irradiance_1 = irradiance_0 + diffuseColor_0;
                float _S290 = _S270.y;
                float _S291 = _S270.z;
                for(;;)
                {
                    if(_S4)
                    {
                        float3 unit_8 = normalize(reflectionDirection_0);
                        float u_4 = fract((atan2(unit_8.z, unit_8.x) + 1.57079637050628662f) / 6.28318548202514648f);
                        float _S292 = max(_S290, 1.0f);
                        float _S293 = _S292 - 1.0f;
                        float slice_4 = _S272 * max(_S293, 0.0f);
                        float lower_4 = floor(slice_4);
                        float inset_6 = 0.5f / max(_S291, 1.0f);
                        float v_4 = clamp(acos(clamp(unit_8.y, -1.0f, 1.0f)) / 3.14159274101257324f, inset_6, 1.0f - inset_6);
                        specularColor_0 = mix((((&kernelContext_0)->environmentSpecular_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(u_4, (lower_4 + v_4) / _S292)), level((0.0f)))).xyz, (((&kernelContext_0)->environmentSpecular_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(u_4, (min(lower_4 + 1.0f, _S293) + v_4) / _S292)), level((0.0f)))).xyz, float3((slice_4 - lower_4)) );
                        break;
                    }
                    float3 unit_9 = normalize(reflectionDirection_0);
                    float u_5 = fract((atan2(unit_9.z, unit_9.x) + 1.57079637050628662f) / 6.28318548202514648f);
                    float _S294 = max(_S290, 1.0f);
                    float total_2 = _S294 * _S274;
                    float _S295 = _S294 - 1.0f;
                    float slice_5 = _S272 * max(_S295, 0.0f);
                    float lower_5 = floor(slice_5);
                    float inset_7 = 0.5f / max(_S291, 1.0f);
                    float v_5 = clamp(acos(clamp(unit_9.y, -1.0f, 1.0f)) / 3.14159274101257324f, inset_7, 1.0f - inset_7);
                    float base_2 = domeGroup_0 * _S294;
                    specularColor_0 = mix((((&kernelContext_0)->environmentSpecular_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(u_5, (base_2 + lower_5 + v_5) / total_2)), level((0.0f)))).xyz, (((&kernelContext_0)->environmentSpecular_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(u_5, (base_2 + min(lower_5 + 1.0f, _S295) + v_5) / total_2)), level((0.0f)))).xyz, float3((slice_5 - lower_5)) );
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
                            float _S296 = max(_S290, 1.0f);
                            float _S297 = _S296 - 1.0f;
                            float slice_6 = saturate(_S214) * max(_S297, 0.0f);
                            float lower_6 = floor(slice_6);
                            float inset_8 = 0.5f / max(_S291, 1.0f);
                            float v_6 = clamp(acos(clamp(unit_10.y, -1.0f, 1.0f)) / 3.14159274101257324f, inset_8, 1.0f - inset_8);
                            normal_2 = mix((((&kernelContext_0)->environmentSpecular_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(u_6, (lower_6 + v_6) / _S296)), level((0.0f)))).xyz, (((&kernelContext_0)->environmentSpecular_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(u_6, (min(lower_6 + 1.0f, _S297) + v_6) / _S296)), level((0.0f)))).xyz, float3((slice_6 - lower_6)) );
                            break;
                        }
                        float3 unit_11 = normalize(reflectionDirection_0);
                        float u_7 = fract((atan2(unit_11.z, unit_11.x) + 1.57079637050628662f) / 6.28318548202514648f);
                        float _S298 = max(_S290, 1.0f);
                        float total_3 = _S298 * _S274;
                        float _S299 = _S298 - 1.0f;
                        float slice_7 = saturate(_S214) * max(_S299, 0.0f);
                        float lower_7 = floor(slice_7);
                        float inset_9 = 0.5f / max(_S291, 1.0f);
                        float v_7 = clamp(acos(clamp(unit_11.y, -1.0f, 1.0f)) / 3.14159274101257324f, inset_9, 1.0f - inset_9);
                        float base_3 = domeGroup_0 * _S298;
                        normal_2 = mix((((&kernelContext_0)->environmentSpecular_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(u_7, (base_3 + lower_7 + v_7) / total_3)), level((0.0f)))).xyz, (((&kernelContext_0)->environmentSpecular_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(u_7, (base_3 + min(lower_7 + 1.0f, _S299) + v_7) / total_3)), level((0.0f)))).xyz, float3((slice_7 - lower_7)) );
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
            float2 _S300 = (((&kernelContext_0)->environmentBrdf_0).sample(((&kernelContext_0)->environmentBrdfSampler_0), (float2(_S271, saturate(_S214))), level((0.0f)))).xy;
            color_2 = color_4 + float3((occlusion_0 * clearcoatAmount_0))  * lightTangent_0 * (float3((reflectanceRatio_0 * reflectanceRatio_0))  * float3(_S300.x)  + float3(_S300.y) );
        }
        else
        {
            color_2 = color_4;
        }
    }
    float3 color_5 = (color_2 + unlitColor_0) * float3(exp2((as_type<float>((_S2.z))))) ;
    if((_S2.y) == 1U)
    {
        color_2 = color_5 / (_S55 + max(color_5, float3(0.0f, 0.0f, 0.0f)));
    }
    else
    {
        color_2 = color_5;
    }
    pixelOutput_0 _S301 = { float4(color_2, opacity_0) };
    return _S301;
}
