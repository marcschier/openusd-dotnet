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
    texture2d<float, access::sample> normalTexture_0;
    sampler normalSampler_0;
    texture2d<float, access::sample> shadowAtlas_0;
    sampler shadowSampler_0;
    texture2d<float, access::sample> environmentBrdf_0;
    sampler environmentBrdfSampler_0;
    texture2d<float, access::sample> environmentIrradiance_0;
    sampler environmentSampler_0;
    texture2d<float, access::sample> environmentSpecular_0;
};

[[fragment]] pixelOutput_0 fragmentMain_uv_normal(pixelInput_0 _S1 [[stage_in]], bool isFrontFace_0 [[front_facing]], float4 position_0 [[position]], SurfaceParameters_natural_0 device* surfaceParameters_1 [[buffer(7)]], FrameParameters_natural_0 device* frameParameters_1 [[buffer(8)]], texture2d<float, access::sample> normalTexture_1 [[texture(1)]], sampler normalSampler_1 [[sampler(1)]], texture2d<float, access::sample> shadowAtlas_1 [[texture(16)]], sampler shadowSampler_1 [[sampler(13)]], texture2d<float, access::sample> environmentBrdf_1 [[texture(19)]], sampler environmentBrdfSampler_1 [[sampler(15)]], texture2d<float, access::sample> environmentIrradiance_1 [[texture(17)]], sampler environmentSampler_1 [[sampler(14)]], texture2d<float, access::sample> environmentSpecular_1 [[texture(18)]])
{
    uint4 _S2;
    bool hasSceneLighting_0;
    float4 _S3;
    uint sampleIndex_0;
    float3 lightDirection_0;
    float3 lightTangent_0;
    bool _S4;
    bool _S5;
    thread KernelContext_0 kernelContext_0;
    (&kernelContext_0)->surfaceParameters_0 = surfaceParameters_1;
    (&kernelContext_0)->frameParameters_0 = frameParameters_1;
    (&kernelContext_0)->normalTexture_0 = normalTexture_1;
    (&kernelContext_0)->normalSampler_0 = normalSampler_1;
    (&kernelContext_0)->shadowAtlas_0 = shadowAtlas_1;
    (&kernelContext_0)->shadowSampler_0 = shadowSampler_1;
    (&kernelContext_0)->environmentBrdf_0 = environmentBrdf_1;
    (&kernelContext_0)->environmentBrdfSampler_0 = environmentBrdfSampler_1;
    (&kernelContext_0)->environmentIrradiance_0 = environmentIrradiance_1;
    (&kernelContext_0)->environmentSampler_0 = environmentSampler_1;
    (&kernelContext_0)->environmentSpecular_0 = environmentSpecular_1;
    SurfaceParameters_natural_0 device* _S6 = surfaceParameters_1+int(0);
    FrameParameters_natural_0 device* _S7 = frameParameters_1+int(0);
    for(;;)
    {
        uint4 _S8 = uint4(_S7->clipPlaneCount_0) ;
        _S2 = _S8;
        uint _S9 = min(_S8.x, 8U);
        uint index_0 = 0U;
        for(;;)
        {
            if(index_0 < _S9)
            {
            }
            else
            {
                break;
            }
            float4 _S10 = float4((&_S7->clipPlanes_0)->data_1[index_0]) ;
            if((dot(_S10.xyz, _S1.eyePosition_0) + _S10.w) < 0.0f)
            {
                discard_fragment();
            }
            index_0 = index_0 + 1U;
        }
        break;
    }
    float4 _S11 = float4(_S6->clearcoatShaded_0) ;
    float shadedMode_0 = _S11.z;
    bool shaded_0 = shadedMode_0 >= 0.5f;
    bool unlit_0 = shadedMode_0 >= 1.5f;
    float3 diffuseColor_0;
    if(shaded_0)
    {
        diffuseColor_0 = (float4(_S6->diffuseOpacity_0) ).xyz;
    }
    else
    {
        diffuseColor_0 = _S1.tint_0.xyz;
    }
    float opacity_0;
    if(shaded_0)
    {
        opacity_0 = (float4(_S6->diffuseOpacity_0) ).w;
    }
    else
    {
        opacity_0 = _S1.tint_0.w;
    }
    float4 _S12 = float4(_S6->emissiveOcclusion_0) ;
    float3 emissiveColor_0 = _S12.xyz;
    float occlusion_0 = _S12.w;
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
    float4 _S14 = float4(_S6->reserved_0) ;
    if((_S14.x) >= 0.5f)
    {
        pixelOutput_0 _S15 = { float4(diffuseColor_0 * float3((1.0f - exp(- max(0.0f, _S14.y) * max(0.0f, _S14.z)))) , 1.0f) };
        return _S15;
    }
    float4 _S16 = float4(_S6->metallicRoughnessThresholdWorkflow_0) ;
    float metallic_0 = saturate(_S16.x);
    float roughness_0 = clamp(_S16.y, 0.00999999977648258f, 1.0f);
    float4 _S17 = float4(_S6->specularIor_0) ;
    float3 specularColor_0 = _S17.xyz;
    float ior_0 = _S17.w;
    float clearcoatAmount_0 = _S11.x;
    float clearcoatRoughness_0 = _S11.y;
    float4 _S18 = float4(_S6->uvTransformRow0_0) ;
    float4 _S19 = float4(_S6->uvTransformRow1_0) ;
    float2 _S20 = float2(dot(_S18.xy, _S1.texCoord_0) + _S18.z, dot(_S19.xy, _S1.texCoord_0) + _S19.z);
    bool _S21 = (uint(round((float4(_S6->textureControls_0) ).y)) & 4U) != 0U;
    for(;;)
    {
        if(!_S21)
        {
            _S3 = (((&kernelContext_0)->normalTexture_0).sample(((&kernelContext_0)->normalSampler_0), (_S20)));
            break;
        }
        texture2d<float, access::sample> _S22 = (&kernelContext_0)->normalTexture_0;
        thread uint atlasWidth_0;
        thread uint atlasHeight_0;
        (*((&atlasWidth_0)) = (_S22).get_width(0)),(*((&atlasHeight_0)) = (_S22).get_height(0));
        int3 _S23 = int3(int(0), int(0), int(0));
        float4 metadata_0 = round((((&kernelContext_0)->normalTexture_0).read(vec<uint,2>(((_S23)).xy), uint(((_S23)).z))) * float4(255.0f) );
        int2 _S24 = int2(metadata_0.zw);
        int2 tile_0 = int2(floor(_S20)) - int2(metadata_0.xy);
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
            _S3 = (((&kernelContext_0)->normalTexture_0).read(vec<uint,2>(((_S25)).xy), uint(((_S25)).z)));
            break;
        }
        uint _S26 = atlasWidth_0 / uint(_S24.x);
        float _S27 = float(_S26);
        uint _S28 = (atlasHeight_0 - 1U) / uint(_S24.y);
        float2 cellSize_0 = float2(_S27, float(_S28));
        _S3 = (((&kernelContext_0)->normalTexture_0).sample(((&kernelContext_0)->normalSampler_0), ((float2(tile_0) * cellSize_0 + float2(1.5f, 2.5f) + fract(_S20) * max(cellSize_0 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_0), float(atlasHeight_0)))));
        break;
    }
    float3 _S29 = float3(1.0f) ;
    float3 sampledNormal_0 = _S3.xyz * float3(2.0f)  - _S29;
    float3 tangent_1 = normalize(_S1.tangent_0.xyz);
    float3 _S30 = float3(sampledNormal_0.x) ;
    float3 _S31 = float3(sampledNormal_0.y) ;
    float3 _S32 = float3(sampledNormal_0.z) ;
    float3 shadingNormal_0 = normalize(tangent_1 * _S30 + cross(normalize(_S1.normal_0), tangent_1) * float3(_S1.tangent_0.w)  * _S31 + _S1.normal_0 * _S32);
    float3 worldNormalBasis_0 = normalize(_S1.worldNormal_0);
    float3 worldTangentBasis_0 = normalize(_S1.worldTangent_0.xyz);
    float3 worldTangentBasis_1 = normalize(worldTangentBasis_0 - worldNormalBasis_0 * float3(dot(worldNormalBasis_0, worldTangentBasis_0)) );
    float3 worldShadingNormal_0 = normalize(worldTangentBasis_1 * _S30 + cross(worldNormalBasis_0, worldTangentBasis_1) * float3(_S1.worldTangent_0.w)  * _S31 + worldNormalBasis_0 * _S32);
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
    if(lengthSquared_0 > 0.00100000004749745f)
    {
        unlitColor_0 = - _S1.eyePosition_0 * float3(rsqrt(lengthSquared_0)) ;
    }
    else
    {
        unlitColor_0 = float3(0.0f, 0.0f, 1.0f);
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
    float _S33 = saturate(abs(dot(normal_2, unlitColor_0)) + 0.00000999999974738f);
    float _S34 = max(0.00100000004749745f, roughness_0);
    float _S35 = max(0.00100000004749745f, clearcoatRoughness_0);
    float reflectanceRatio_0 = (1.0f - ior_0) / (1.0f + ior_0);
    float3 _S36 = float3(3.14159274101257324f) ;
    float3 diffuse_0 = diffuseColor_0 / _S36;
    float3 normalIncidence_0;
    float3 grazingIncidence_0;
    float3 diffuse_1;
    if((_S16.w) >= 0.5f)
    {
        float3 _S37 = float3(1.0f, 1.0f, 1.0f);
        normalIncidence_0 = specularColor_0;
        grazingIncidence_0 = _S37;
        diffuse_1 = diffuse_0;
    }
    else
    {
        float3 _S38 = float3(metallic_0) ;
        float3 specularTint_0 = mix(float3(1.0f, 1.0f, 1.0f), diffuseColor_0, _S38);
        float3 diffuse_2 = diffuse_0 * float3((1.0f - metallic_0)) ;
        normalIncidence_0 = mix(float3((reflectanceRatio_0 * reflectanceRatio_0))  * specularTint_0, specularTint_0, _S38);
        grazingIncidence_0 = specularTint_0;
        diffuse_1 = diffuse_2;
    }
    float4 _S39 = float4(_S7->ambientLight_0) ;
    float _S40 = _S39.w;
    if(_S40 > 0.5f)
    {
        hasSceneLighting_0 = true;
    }
    else
    {
        float3 _S41 = _S39.xyz;
        hasSceneLighting_0 = (dot(_S41, _S41)) > 0.0f;
    }
    if(hasSceneLighting_0)
    {
        hasSceneLighting_0 = true;
    }
    else
    {
        hasSceneLighting_0 = ((float4(_S7->environmentControls_0) ).x) >= 0.5f;
    }
    if(hasSceneLighting_0)
    {
        hasSceneLighting_0 = true;
    }
    else
    {
        hasSceneLighting_0 = ((float4(_S7->environmentControls_0) ).w) >= 0.5f;
    }
    uint _S42 = min(uint(_S40), 8U);
    matrix<float,int(4),int(4)>  _S43 = matrix<float,int(4),int(4)> (_S7->eyeToWorld_0.data_0[int(0)][int(0)], _S7->eyeToWorld_0.data_0[int(0)][int(1)], _S7->eyeToWorld_0.data_0[int(0)][int(2)], _S7->eyeToWorld_0.data_0[int(0)][int(3)], _S7->eyeToWorld_0.data_0[int(1)][int(0)], _S7->eyeToWorld_0.data_0[int(1)][int(1)], _S7->eyeToWorld_0.data_0[int(1)][int(2)], _S7->eyeToWorld_0.data_0[int(1)][int(3)], _S7->eyeToWorld_0.data_0[int(2)][int(0)], _S7->eyeToWorld_0.data_0[int(2)][int(1)], _S7->eyeToWorld_0.data_0[int(2)][int(2)], _S7->eyeToWorld_0.data_0[int(2)][int(3)], _S7->eyeToWorld_0.data_0[int(3)][int(0)], _S7->eyeToWorld_0.data_0[int(3)][int(1)], _S7->eyeToWorld_0.data_0[int(3)][int(2)], _S7->eyeToWorld_0.data_0[int(3)][int(3)]);
    float3 sceneEye_0 = normalize((((float4(unlitColor_0, 0.0f)) * (_S43))).xyz);
    float3 worldPosition_0 = (((float4(_S1.eyePosition_0, 1.0f)) * (_S43))).xyz;
    float3 worldGeometricNormal_0 = cross(dfdx(worldPosition_0), dfdy(worldPosition_0));
    float worldNormalLengthSquared_0 = dot(worldGeometricNormal_0, worldGeometricNormal_0);
    float3 prefiltered_0;
    if(worldNormalLengthSquared_0 > 9.99999968265522539e-21f)
    {
        prefiltered_0 = worldGeometricNormal_0 * float3(rsqrt(worldNormalLengthSquared_0)) ;
    }
    else
    {
        prefiltered_0 = float3(0.0f, 0.0f, 1.0f);
    }
    float4 _S44 = float4(_S6->lightColorAmbient_0) ;
    float3 color_0 = diffuseColor_0 * float3(_S44.w) ;
    uint domeLinkMask_0 = uint(max((float4(_S6->domeLinkControls_0) ).x, 0.0f));
    float4 _S45 = float4(_S7->domeControls_0) ;
    uint _S46 = min(uint(max(_S45.x, 0.0f)), 8U);
    uint allDomes_0 = (1U << _S46) - 1U;
    bool allDomesLinked_0 = (domeLinkMask_0 & allDomes_0) == allDomes_0;
    float3 _S47 = _S39.xyz;
    uint lightCount_0;
    float3 domeAmbient_1;
    if(!allDomesLinked_0)
    {
        float3 _S48 = float3(0.0f, 0.0f, 0.0f);
        lightCount_0 = 0U;
        domeAmbient_1 = _S48;
        for(;;)
        {
            if(lightCount_0 < _S46)
            {
            }
            else
            {
                break;
            }
            if((domeLinkMask_0 & (1U << lightCount_0)) != 0U)
            {
                domeAmbient_1 = domeAmbient_1 + (float4((&_S7->domeAmbient_0)->data_1[lightCount_0]) ).xyz;
            }
            lightCount_0 = lightCount_0 + 1U;
        }
    }
    else
    {
        domeAmbient_1 = _S47;
    }
    float3 color_1 = color_0 + diffuseColor_0 * domeAmbient_1;
    bool _S49 = !hasSceneLighting_0;
    if(_S49)
    {
        lightCount_0 = 1U;
    }
    else
    {
        lightCount_0 = _S42;
    }
    uint _S50 = uint(max(_S11.w, 0.0f));
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
        bool _S51;
        if(hasSceneLighting_0)
        {
            _S51 = (_S50 & (1U << lightIndex_0)) == 0U;
        }
        else
        {
            _S51 = false;
        }
        if(_S51)
        {
            lightIndex_0 = lightIndex_0 + 1U;
            continue;
        }
        bool _S52 = lightIndex_0 == 0U;
        bool _S53;
        if(_S52)
        {
            _S53 = _S49;
        }
        else
        {
            _S53 = false;
        }
        float lightType_0;
        if(_S53)
        {
            lightType_0 = 1.0f;
        }
        else
        {
            lightType_0 = (float4((&_S7->lightPositionType_0)->data_1[lightIndex_0]) ).w;
        }
        bool _S54;
        if(_S52)
        {
            _S54 = _S49;
        }
        else
        {
            _S54 = false;
        }
        if(_S54)
        {
            lightDirection_0 = normalize((float4(_S6->lightDirectionIntensity_0) ).xyz);
        }
        else
        {
            lightDirection_0 = normalize((float4((&_S7->lightDirectionRadius_0)->data_1[lightIndex_0]) ).xyz);
        }
        bool _S55;
        if(_S52)
        {
            _S55 = _S49;
        }
        else
        {
            _S55 = false;
        }
        float _S56;
        if(_S55)
        {
            _S56 = (float4(_S6->lightDirectionIntensity_0) ).w;
        }
        else
        {
            _S56 = (float4((&_S7->lightColorIntensity_0)->data_1[lightIndex_0]) ).w;
        }
        bool _S57;
        if(_S52)
        {
            _S57 = _S49;
        }
        else
        {
            _S57 = false;
        }
        if(_S57)
        {
            diffuseColor_0 = _S44.xyz;
        }
        else
        {
            diffuseColor_0 = (float4((&_S7->lightColorIntensity_0)->data_1[lightIndex_0]) ).xyz;
        }
        bool _S58;
        if(_S52)
        {
            _S58 = _S49;
        }
        else
        {
            _S58 = false;
        }
        float _S59;
        if(_S58)
        {
            _S59 = 1.0f;
        }
        else
        {
            _S59 = (float4((&_S7->lightControls_0)->data_1[lightIndex_0]) ).x;
        }
        bool _S60;
        if(_S52)
        {
            _S60 = _S49;
        }
        else
        {
            _S60 = false;
        }
        float _S61;
        if(_S60)
        {
            _S61 = 1.0f;
        }
        else
        {
            _S61 = (float4((&_S7->lightControls_0)->data_1[lightIndex_0]) ).y;
        }
        bool _S62;
        if(_S52)
        {
            _S62 = _S49;
        }
        else
        {
            _S62 = false;
        }
        if(_S62)
        {
            lightTangent_0 = float3(1.0f, 0.0f, 0.0f);
        }
        else
        {
            lightTangent_0 = normalize((float4((&_S7->lightTangentShapeX_0)->data_1[lightIndex_0]) ).xyz);
        }
        bool _S63;
        if(_S52)
        {
            _S63 = _S49;
        }
        else
        {
            _S63 = false;
        }
        float3 lightBitangent_0;
        if(_S63)
        {
            lightBitangent_0 = float3(0.0f, 1.0f, 0.0f);
        }
        else
        {
            lightBitangent_0 = normalize((float4((&_S7->lightBitangentShapeY_0)->data_1[lightIndex_0]) ).xyz);
        }
        bool _S64;
        if(_S52)
        {
            _S64 = _S49;
        }
        else
        {
            _S64 = false;
        }
        float shapeX_0;
        if(_S64)
        {
            shapeX_0 = 0.0f;
        }
        else
        {
            shapeX_0 = (float4((&_S7->lightTangentShapeX_0)->data_1[lightIndex_0]) ).w;
        }
        bool _S65;
        if(_S52)
        {
            _S65 = _S49;
        }
        else
        {
            _S65 = false;
        }
        float shapeY_0;
        if(_S65)
        {
            shapeY_0 = 0.0f;
        }
        else
        {
            shapeY_0 = (float4((&_S7->lightBitangentShapeY_0)->data_1[lightIndex_0]) ).w;
        }
        bool _S66;
        if(_S52)
        {
            _S66 = _S49;
        }
        else
        {
            _S66 = false;
        }
        float lightRadius_0;
        if(_S66)
        {
            lightRadius_0 = 0.0f;
        }
        else
        {
            lightRadius_0 = (float4((&_S7->lightDirectionRadius_0)->data_1[lightIndex_0]) ).w;
        }
        bool _S67;
        if(_S52)
        {
            _S67 = _S49;
        }
        else
        {
            _S67 = false;
        }
        if(_S67)
        {
            domeAmbient_1 = unlitColor_0;
        }
        else
        {
            domeAmbient_1 = sceneEye_0;
        }
        float3 color_3;
        float shadowVisibility_0;
        float sampleCount_0;
        if(hasSceneLighting_0)
        {
            int shadowSlot_0 = int((float4((&_S7->shadowSlots_0)->data_1[lightIndex_0]) ).x);
            if(shadowSlot_0 >= int(0))
            {
                for(;;)
                {
                    float4 _S68 = float4((&_S7->shadowControls_0)->data_3[shadowSlot_0]) ;
                    float4 _S69 = float4((&_S7->shadowTile_0)->data_3[shadowSlot_0]) ;
                    if((dot(prefiltered_0, lightDirection_0)) < 0.0f)
                    {
                        color_3 = - prefiltered_0;
                    }
                    else
                    {
                        color_3 = prefiltered_0;
                    }
                    float slope_0 = clamp(1.0f - saturate(dot(color_3, lightDirection_0)), 0.0f, 1.0f);
                    float4 lightClip_0 = (((float4(worldPosition_0 + color_3 * float3((_S68.y * slope_0)) , 1.0f)) * (matrix<float,int(4),int(4)> ((&_S7->shadowWorldToLightClip_0)->data_2[shadowSlot_0].data_0[int(0)][int(0)], (&_S7->shadowWorldToLightClip_0)->data_2[shadowSlot_0].data_0[int(0)][int(1)], (&_S7->shadowWorldToLightClip_0)->data_2[shadowSlot_0].data_0[int(0)][int(2)], (&_S7->shadowWorldToLightClip_0)->data_2[shadowSlot_0].data_0[int(0)][int(3)], (&_S7->shadowWorldToLightClip_0)->data_2[shadowSlot_0].data_0[int(1)][int(0)], (&_S7->shadowWorldToLightClip_0)->data_2[shadowSlot_0].data_0[int(1)][int(1)], (&_S7->shadowWorldToLightClip_0)->data_2[shadowSlot_0].data_0[int(1)][int(2)], (&_S7->shadowWorldToLightClip_0)->data_2[shadowSlot_0].data_0[int(1)][int(3)], (&_S7->shadowWorldToLightClip_0)->data_2[shadowSlot_0].data_0[int(2)][int(0)], (&_S7->shadowWorldToLightClip_0)->data_2[shadowSlot_0].data_0[int(2)][int(1)], (&_S7->shadowWorldToLightClip_0)->data_2[shadowSlot_0].data_0[int(2)][int(2)], (&_S7->shadowWorldToLightClip_0)->data_2[shadowSlot_0].data_0[int(2)][int(3)], (&_S7->shadowWorldToLightClip_0)->data_2[shadowSlot_0].data_0[int(3)][int(0)], (&_S7->shadowWorldToLightClip_0)->data_2[shadowSlot_0].data_0[int(3)][int(1)], (&_S7->shadowWorldToLightClip_0)->data_2[shadowSlot_0].data_0[int(3)][int(2)], (&_S7->shadowWorldToLightClip_0)->data_2[shadowSlot_0].data_0[int(3)][int(3)]))));
                    float _S70 = lightClip_0.w;
                    if(_S70 <= 0.0f)
                    {
                        shadowVisibility_0 = 1.0f;
                        break;
                    }
                    float3 ndc_0 = lightClip_0.xyz / float3(_S70) ;
                    bool _S71;
                    if((abs(ndc_0.x)) > 1.0f)
                    {
                        _S71 = true;
                    }
                    else
                    {
                        _S71 = (abs(ndc_0.y)) > 1.0f;
                    }
                    bool _S72;
                    if(_S71)
                    {
                        _S72 = true;
                    }
                    else
                    {
                        _S72 = (ndc_0.z) < 0.0f;
                    }
                    bool _S73;
                    if(_S72)
                    {
                        _S73 = true;
                    }
                    else
                    {
                        _S73 = (ndc_0.z) > 1.0f;
                    }
                    if(_S73)
                    {
                        shadowVisibility_0 = 1.0f;
                        break;
                    }
                    float2 _S74 = _S69.xy;
                    float2 _S75 = _S69.zw;
                    float2 _S76 = _S74 + (ndc_0.xy * float2(0.5f, -0.5f) + float2(0.5f, 0.5f)) * _S75;
                    float texel_0 = _S68.w;
                    float _S77 = max(_S68.z, 0.0f);
                    float _S78 = ndc_0.z - _S68.x * (1.0f + 2.0f * slope_0);
                    float2 _S79 = float2((texel_0 * 0.5f)) ;
                    float2 _S80 = _S74 + _S79;
                    float2 _S81 = _S74 + _S75 - _S79;
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
                            if(_S78 <= (((&kernelContext_0)->shadowAtlas_0).sample(((&kernelContext_0)->shadowSampler_0), (clamp(_S76 + float2(float(x_0), float(y_0)) * float2((_S77 * texel_0)) , _S80, _S81)), level((0.0f))).x))
                            {
                                sampleCount_0 = 1.0f;
                            }
                            else
                            {
                                sampleCount_0 = 0.0f;
                            }
                            float lit_0 = shadowVisibility_0 + sampleCount_0;
                            x_0 = x_0 + int(1);
                            shadowVisibility_0 = lit_0;
                        }
                        y_0 = y_0 + int(1);
                    }
                    shadowVisibility_0 = shadowVisibility_0 * 0.1111111119389534f;
                    break;
                }
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
        float3 _S82 = float3(0.0f, 0.0f, 0.0f);
        sampleOffsets_0[int(0)] = _S82;
        sampleOffsets_0[int(1)] = _S82;
        sampleOffsets_0[int(2)] = _S82;
        sampleOffsets_0[int(3)] = _S82;
        sampleOffsets_0[int(4)] = _S82;
        if(lightType_0 == 3.0f)
        {
            float3 halfWidth_0 = lightTangent_0 * float3((shapeX_0 * 0.5f)) ;
            float3 halfHeight_0 = lightBitangent_0 * float3((shapeY_0 * 0.5f)) ;
            sampleOffsets_0[int(1)] = halfWidth_0 + halfHeight_0;
            sampleOffsets_0[int(2)] = halfWidth_0 - halfHeight_0;
            float3 _S83 = - halfWidth_0;
            sampleOffsets_0[int(3)] = _S83 + halfHeight_0;
            sampleOffsets_0[int(4)] = _S83 - halfHeight_0;
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
            float sampleIntensity_0 = _S56 / sampleCount_0;
            float3 sampleDirection_0;
            float emissionScale_0;
            float sampleIntensity_1;
            if(lightType_0 >= 2.0f)
            {
                float3 toLight_0 = (float4((&_S7->lightPositionType_0)->data_1[lightIndex_0]) ).xyz + sampleOffsets_0[sampleIndex_0] - worldPosition_0;
                float _S84 = max(dot(toLight_0, toLight_0), 0.00100000004749745f);
                float3 sampleDirection_1 = toLight_0 * float3(rsqrt(_S84)) ;
                float sampleIntensity_2 = sampleIntensity_0 / _S84;
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
            float3 _S85 = float3(pow(max(0.0f, 1.0f - saturate(dot(domeAmbient_1, half_0))), 5.0f)) ;
            float3 _S86 = mix(normalIncidence_0, grazingIncidence_0, _S85);
            float3 directDiffuse_0 = diffuse_1 * (_S29 - _S86);
            float _S87 = max(_S34, 0.00100000004749745f);
            float alpha_0 = _S87 * _S87;
            float alphaSquared_0 = alpha_0 * alpha_0;
            precise float lobeCosineSquared_0 = saturate(normalDotHalf_0 * normalDotHalf_0);
            precise float scaledLobe_0 = lobeCosineSquared_0 * alphaSquared_0;
            precise float lobeComplement_0 = 1.0f - lobeCosineSquared_0;
            precise float denominator_0 = scaledLobe_0 + lobeComplement_0;
            precise float scaled_0 = 3.14159274101257324f * denominator_0 * denominator_0;
            float k_0 = alpha_0 * 0.5f;
            float _S88 = 1.0f - k_0;
            float3 _S89 = float3(max(4.0f * normalDotLight_0 * _S33, 1.00000000317107685e-30f)) ;
            float3 _S90 = _S86 * float3((_S33 / (_S33 * _S88 + k_0) * (normalDotLight_0 / (normalDotLight_0 * _S88 + k_0))))  * float3((alphaSquared_0 / max(scaled_0, 1.00000000317107685e-30f)))  / _S89;
            float3 directSpecular_0;
            if(clearcoatAmount_0 > 0.0f)
            {
                float _S91 = max(_S35, 0.00100000004749745f);
                float alpha_1 = _S91 * _S91;
                float alphaSquared_1 = alpha_1 * alpha_1;
                precise float scaledLobe_1 = lobeCosineSquared_0 * alphaSquared_1;
                precise float denominator_1 = scaledLobe_1 + lobeComplement_0;
                precise float scaled_1 = 3.14159274101257324f * denominator_1 * denominator_1;
                float k_1 = alpha_1 * 0.5f;
                float _S92 = 1.0f - k_1;
                directSpecular_0 = _S90 + float3(clearcoatAmount_0)  * (mix(float3((reflectanceRatio_0 * reflectanceRatio_0)) , float3(1.0f, 1.0f, 1.0f), _S85) * float3((_S33 / (_S33 * _S92 + k_1) * (normalDotLight_0 / (normalDotLight_0 * _S92 + k_1))))  * float3((alphaSquared_1 / max(scaled_1, 1.00000000317107685e-30f)))  / _S89);
            }
            else
            {
                directSpecular_0 = _S90;
            }
            float3 _S93 = diffuseColor_0 * float3(sampleIntensity_1) ;
            color_3 = color_3 + float3((shadowVisibility_0 * occlusion_0 * emissionScale_0 * normalDotLight_0))  * (directDiffuse_0 * float3(_S59)  * (_S93 * _S36) + directSpecular_0 * float3(_S61)  * _S93);
            sampleIndex_0 = sampleIndex_0 + 1U;
        }
        color_2 = color_3;
        lightIndex_0 = lightIndex_0 + 1U;
    }
    float4 _S94 = float4(_S7->environmentControls_0) ;
    if((_S94.x) >= 0.5f)
    {
        float _S95 = saturate(saturate(abs(dot(worldNormal_2, sceneEye_0)) + 0.00000999999974738f));
        float _S96 = saturate(_S34);
        float2 _S97 = (((&kernelContext_0)->environmentBrdf_0).sample(((&kernelContext_0)->environmentBrdfSampler_0), (float2(_S95, _S96)), level((0.0f)))).xy;
        float3 specularWeight_0 = normalIncidence_0 * float3(_S97.x)  + grazingIncidence_0 * float3(_S97.y) ;
        float3 diffuseWeight_0 = saturate(float3(1.0f, 1.0f, 1.0f) - specularWeight_0);
        float3 reflectionDirection_0 = reflect(- sceneEye_0, worldNormal_2);
        float _S98 = max(_S45.y, 1.0f);
        float3 _S99 = float3(0.0f, 0.0f, 0.0f);
        if(allDomesLinked_0)
        {
            hasSceneLighting_0 = true;
        }
        else
        {
            hasSceneLighting_0 = _S98 <= 1.0f;
        }
        if(hasSceneLighting_0)
        {
            float composedGroup_0 = _S45.z;
            float _S100 = _S45.w;
            for(;;)
            {
                bool _S101 = _S98 <= 1.0f;
                _S4 = _S101;
                if(_S101)
                {
                    float3 unit_0 = normalize(worldNormal_2);
                    diffuseColor_0 = (((&kernelContext_0)->environmentIrradiance_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(fract((atan2(unit_0.z, unit_0.x) + 1.57079637050628662f) / 6.28318548202514648f), acos(clamp(unit_0.y, -1.0f, 1.0f)) / 3.14159274101257324f)), level((0.0f)))).xyz;
                    break;
                }
                float3 unit_1 = normalize(worldNormal_2);
                float inset_0 = 0.5f / max(_S100, 1.0f);
                diffuseColor_0 = (((&kernelContext_0)->environmentIrradiance_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(fract((atan2(unit_1.z, unit_1.x) + 1.57079637050628662f) / 6.28318548202514648f), (composedGroup_0 + clamp(acos(clamp(unit_1.y, -1.0f, 1.0f)) / 3.14159274101257324f, inset_0, 1.0f - inset_0)) / _S98)), level((0.0f)))).xyz;
                break;
            }
            float _S102 = _S94.y;
            float _S103 = _S94.z;
            for(;;)
            {
                if(_S4)
                {
                    float3 unit_2 = normalize(reflectionDirection_0);
                    float u_0 = fract((atan2(unit_2.z, unit_2.x) + 1.57079637050628662f) / 6.28318548202514648f);
                    float _S104 = max(_S102, 1.0f);
                    float _S105 = _S104 - 1.0f;
                    float slice_0 = _S96 * max(_S105, 0.0f);
                    float lower_0 = floor(slice_0);
                    float inset_1 = 0.5f / max(_S103, 1.0f);
                    float v_0 = clamp(acos(clamp(unit_2.y, -1.0f, 1.0f)) / 3.14159274101257324f, inset_1, 1.0f - inset_1);
                    unlitColor_0 = mix((((&kernelContext_0)->environmentSpecular_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(u_0, (lower_0 + v_0) / _S104)), level((0.0f)))).xyz, (((&kernelContext_0)->environmentSpecular_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(u_0, (min(lower_0 + 1.0f, _S105) + v_0) / _S104)), level((0.0f)))).xyz, float3((slice_0 - lower_0)) );
                    break;
                }
                float3 unit_3 = normalize(reflectionDirection_0);
                float u_1 = fract((atan2(unit_3.z, unit_3.x) + 1.57079637050628662f) / 6.28318548202514648f);
                float _S106 = max(_S102, 1.0f);
                float total_0 = _S106 * _S98;
                float _S107 = _S106 - 1.0f;
                float slice_1 = _S96 * max(_S107, 0.0f);
                float lower_1 = floor(slice_1);
                float inset_2 = 0.5f / max(_S103, 1.0f);
                float v_1 = clamp(acos(clamp(unit_3.y, -1.0f, 1.0f)) / 3.14159274101257324f, inset_2, 1.0f - inset_2);
                float base_0 = composedGroup_0 * _S106;
                unlitColor_0 = mix((((&kernelContext_0)->environmentSpecular_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(u_1, (base_0 + lower_1 + v_1) / total_0)), level((0.0f)))).xyz, (((&kernelContext_0)->environmentSpecular_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(u_1, (base_0 + min(lower_1 + 1.0f, _S107) + v_1) / total_0)), level((0.0f)))).xyz, float3((slice_1 - lower_1)) );
                break;
            }
            if(clearcoatAmount_0 > 0.0f)
            {
                for(;;)
                {
                    if(_S4)
                    {
                        float3 unit_4 = normalize(reflectionDirection_0);
                        float u_2 = fract((atan2(unit_4.z, unit_4.x) + 1.57079637050628662f) / 6.28318548202514648f);
                        float _S108 = max(_S102, 1.0f);
                        float _S109 = _S108 - 1.0f;
                        float slice_2 = saturate(_S35) * max(_S109, 0.0f);
                        float lower_2 = floor(slice_2);
                        float inset_3 = 0.5f / max(_S103, 1.0f);
                        float v_2 = clamp(acos(clamp(unit_4.y, -1.0f, 1.0f)) / 3.14159274101257324f, inset_3, 1.0f - inset_3);
                        normal_2 = mix((((&kernelContext_0)->environmentSpecular_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(u_2, (lower_2 + v_2) / _S108)), level((0.0f)))).xyz, (((&kernelContext_0)->environmentSpecular_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(u_2, (min(lower_2 + 1.0f, _S109) + v_2) / _S108)), level((0.0f)))).xyz, float3((slice_2 - lower_2)) );
                        break;
                    }
                    float3 unit_5 = normalize(reflectionDirection_0);
                    float u_3 = fract((atan2(unit_5.z, unit_5.x) + 1.57079637050628662f) / 6.28318548202514648f);
                    float _S110 = max(_S102, 1.0f);
                    float total_1 = _S110 * _S98;
                    float _S111 = _S110 - 1.0f;
                    float slice_3 = saturate(_S35) * max(_S111, 0.0f);
                    float lower_3 = floor(slice_3);
                    float inset_4 = 0.5f / max(_S103, 1.0f);
                    float v_3 = clamp(acos(clamp(unit_5.y, -1.0f, 1.0f)) / 3.14159274101257324f, inset_4, 1.0f - inset_4);
                    float base_1 = composedGroup_0 * _S110;
                    normal_2 = mix((((&kernelContext_0)->environmentSpecular_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(u_3, (base_1 + lower_3 + v_3) / total_1)), level((0.0f)))).xyz, (((&kernelContext_0)->environmentSpecular_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(u_3, (base_1 + min(lower_3 + 1.0f, _S111) + v_3) / total_1)), level((0.0f)))).xyz, float3((slice_3 - lower_3)) );
                    break;
                }
                lightDirection_0 = normal_2;
            }
            else
            {
                lightDirection_0 = _S99;
            }
            float3 _S112 = unlitColor_0;
            unlitColor_0 = diffuseColor_0;
            prefiltered_0 = _S112;
        }
        else
        {
            sampleIndex_0 = 0U;
            unlitColor_0 = _S99;
            prefiltered_0 = _S99;
            lightDirection_0 = _S99;
            for(;;)
            {
                if(sampleIndex_0 < _S46)
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
                float domeGroup_0 = (float4((&_S7->domeEnvironment_0)->data_1[sampleIndex_0]) ).x;
                if(domeGroup_0 < 0.0f)
                {
                    sampleIndex_0 = sampleIndex_0 + 1U;
                    continue;
                }
                float _S113 = _S45.w;
                for(;;)
                {
                    bool _S114 = _S98 <= 1.0f;
                    _S5 = _S114;
                    if(_S114)
                    {
                        float3 unit_6 = normalize(worldNormal_2);
                        diffuseColor_0 = (((&kernelContext_0)->environmentIrradiance_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(fract((atan2(unit_6.z, unit_6.x) + 1.57079637050628662f) / 6.28318548202514648f), acos(clamp(unit_6.y, -1.0f, 1.0f)) / 3.14159274101257324f)), level((0.0f)))).xyz;
                        break;
                    }
                    float3 unit_7 = normalize(worldNormal_2);
                    float inset_5 = 0.5f / max(_S113, 1.0f);
                    diffuseColor_0 = (((&kernelContext_0)->environmentIrradiance_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(fract((atan2(unit_7.z, unit_7.x) + 1.57079637050628662f) / 6.28318548202514648f), (domeGroup_0 + clamp(acos(clamp(unit_7.y, -1.0f, 1.0f)) / 3.14159274101257324f, inset_5, 1.0f - inset_5)) / _S98)), level((0.0f)))).xyz;
                    break;
                }
                float3 irradiance_0 = unlitColor_0 + diffuseColor_0;
                float _S115 = _S94.y;
                float _S116 = _S94.z;
                for(;;)
                {
                    if(_S5)
                    {
                        float3 unit_8 = normalize(reflectionDirection_0);
                        float u_4 = fract((atan2(unit_8.z, unit_8.x) + 1.57079637050628662f) / 6.28318548202514648f);
                        float _S117 = max(_S115, 1.0f);
                        float _S118 = _S117 - 1.0f;
                        float slice_4 = _S96 * max(_S118, 0.0f);
                        float lower_4 = floor(slice_4);
                        float inset_6 = 0.5f / max(_S116, 1.0f);
                        float v_4 = clamp(acos(clamp(unit_8.y, -1.0f, 1.0f)) / 3.14159274101257324f, inset_6, 1.0f - inset_6);
                        normal_2 = mix((((&kernelContext_0)->environmentSpecular_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(u_4, (lower_4 + v_4) / _S117)), level((0.0f)))).xyz, (((&kernelContext_0)->environmentSpecular_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(u_4, (min(lower_4 + 1.0f, _S118) + v_4) / _S117)), level((0.0f)))).xyz, float3((slice_4 - lower_4)) );
                        break;
                    }
                    float3 unit_9 = normalize(reflectionDirection_0);
                    float u_5 = fract((atan2(unit_9.z, unit_9.x) + 1.57079637050628662f) / 6.28318548202514648f);
                    float _S119 = max(_S115, 1.0f);
                    float total_2 = _S119 * _S98;
                    float _S120 = _S119 - 1.0f;
                    float slice_5 = _S96 * max(_S120, 0.0f);
                    float lower_5 = floor(slice_5);
                    float inset_7 = 0.5f / max(_S116, 1.0f);
                    float v_5 = clamp(acos(clamp(unit_9.y, -1.0f, 1.0f)) / 3.14159274101257324f, inset_7, 1.0f - inset_7);
                    float base_2 = domeGroup_0 * _S119;
                    normal_2 = mix((((&kernelContext_0)->environmentSpecular_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(u_5, (base_2 + lower_5 + v_5) / total_2)), level((0.0f)))).xyz, (((&kernelContext_0)->environmentSpecular_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(u_5, (base_2 + min(lower_5 + 1.0f, _S120) + v_5) / total_2)), level((0.0f)))).xyz, float3((slice_5 - lower_5)) );
                    break;
                }
                float3 prefiltered_1 = prefiltered_0 + normal_2;
                if(clearcoatAmount_0 > 0.0f)
                {
                    for(;;)
                    {
                        if(_S5)
                        {
                            float3 unit_10 = normalize(reflectionDirection_0);
                            float u_6 = fract((atan2(unit_10.z, unit_10.x) + 1.57079637050628662f) / 6.28318548202514648f);
                            float _S121 = max(_S115, 1.0f);
                            float _S122 = _S121 - 1.0f;
                            float slice_6 = saturate(_S35) * max(_S122, 0.0f);
                            float lower_6 = floor(slice_6);
                            float inset_8 = 0.5f / max(_S116, 1.0f);
                            float v_6 = clamp(acos(clamp(unit_10.y, -1.0f, 1.0f)) / 3.14159274101257324f, inset_8, 1.0f - inset_8);
                            normalIncidence_0 = mix((((&kernelContext_0)->environmentSpecular_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(u_6, (lower_6 + v_6) / _S121)), level((0.0f)))).xyz, (((&kernelContext_0)->environmentSpecular_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(u_6, (min(lower_6 + 1.0f, _S122) + v_6) / _S121)), level((0.0f)))).xyz, float3((slice_6 - lower_6)) );
                            break;
                        }
                        float3 unit_11 = normalize(reflectionDirection_0);
                        float u_7 = fract((atan2(unit_11.z, unit_11.x) + 1.57079637050628662f) / 6.28318548202514648f);
                        float _S123 = max(_S115, 1.0f);
                        float total_3 = _S123 * _S98;
                        float _S124 = _S123 - 1.0f;
                        float slice_7 = saturate(_S35) * max(_S124, 0.0f);
                        float lower_7 = floor(slice_7);
                        float inset_9 = 0.5f / max(_S116, 1.0f);
                        float v_7 = clamp(acos(clamp(unit_11.y, -1.0f, 1.0f)) / 3.14159274101257324f, inset_9, 1.0f - inset_9);
                        float base_3 = domeGroup_0 * _S123;
                        normalIncidence_0 = mix((((&kernelContext_0)->environmentSpecular_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(u_7, (base_3 + lower_7 + v_7) / total_3)), level((0.0f)))).xyz, (((&kernelContext_0)->environmentSpecular_0).sample(((&kernelContext_0)->environmentSampler_0), (float2(u_7, (base_3 + min(lower_7 + 1.0f, _S124) + v_7) / total_3)), level((0.0f)))).xyz, float3((slice_7 - lower_7)) );
                        break;
                    }
                    lightTangent_0 = lightDirection_0 + normalIncidence_0;
                }
                else
                {
                    lightTangent_0 = lightDirection_0;
                }
                unlitColor_0 = irradiance_0;
                prefiltered_0 = prefiltered_1;
                lightDirection_0 = lightTangent_0;
                sampleIndex_0 = sampleIndex_0 + 1U;
            }
        }
        float3 _S125 = float3(occlusion_0) ;
        float3 color_4 = color_2 + _S125 * diffuse_1 * unlitColor_0 * diffuseWeight_0 + _S125 * prefiltered_0 * specularWeight_0;
        if(clearcoatAmount_0 > 0.0f)
        {
            float2 _S126 = (((&kernelContext_0)->environmentBrdf_0).sample(((&kernelContext_0)->environmentBrdfSampler_0), (float2(_S95, saturate(_S35))), level((0.0f)))).xy;
            color_2 = color_4 + float3((occlusion_0 * clearcoatAmount_0))  * lightDirection_0 * (float3((reflectanceRatio_0 * reflectanceRatio_0))  * float3(_S126.x)  + float3(_S126.y) );
        }
        else
        {
            color_2 = color_4;
        }
    }
    float3 color_5 = (color_2 + emissiveColor_0) * float3(exp2((as_type<float>((_S2.z))))) ;
    if((_S2.y) == 1U)
    {
        color_2 = color_5 / (_S29 + max(color_5, float3(0.0f, 0.0f, 0.0f)));
    }
    else
    {
        color_2 = color_5;
    }
    pixelOutput_0 _S127 = { float4(color_2, opacity_0) };
    return _S127;
}
