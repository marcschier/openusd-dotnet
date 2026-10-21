#include <metal_stdlib>
#include <metal_math>
#include <metal_texture>
using namespace metal;
float3 DecodeIndexedRgb_0(float4 sampled_0, texture2d<float, access::sample> values_0)
{
    float _S1 = sampled_0.w;
    if(_S1 > 1.0f)
    {
        return sampled_0.xyz;
    }
    thread uint width_0;
    thread uint height_0;
    (*((&width_0)) = (values_0).get_width(0)),(*((&height_0)) = (values_0).get_height(0));
    uint3 _S2 = uint3(round(sampled_0.xyz * float3(255.0f) ));
    uint index_0 = ((_S2.x) | ((_S2.y) << 8U)) | ((_S2.z) << 16U);
    uint3 _S3;
    if(_S1 == 0.0f)
    {
        _S3 = _S2;
    }
    else
    {
        _S3 = uint3(index_0, index_0, index_0);
    }
    uint3 address_0 = _S3 * uint3(3U)  + uint3(0U, 1U, 2U);
    uint _S4 = address_0.x;
    uint _S5 = _S4 % width_0;
    int _S6 = int(_S5);
    uint _S7 = _S4 / width_0;
    int3 _S8 = int3(_S6, int(_S7), int(0));
    float _S9 = ((values_0).read(vec<uint,2>(((_S8)).xy), uint(((_S8)).z))).x;
    uint _S10 = address_0.y;
    uint _S11 = _S10 % width_0;
    int _S12 = int(_S11);
    uint _S13 = _S10 / width_0;
    int3 _S14 = int3(_S12, int(_S13), int(0));
    float _S15 = ((values_0).read(vec<uint,2>(((_S14)).xy), uint(((_S14)).z))).x;
    uint _S16 = address_0.z;
    uint _S17 = _S16 % width_0;
    int _S18 = int(_S17);
    uint _S19 = _S16 / width_0;
    int3 _S20 = int3(_S18, int(_S19), int(0));
    return float3(_S9, _S15, ((values_0).read(vec<uint,2>(((_S20)).xy), uint(((_S20)).z))).x);
}

float3 OrientMappedNormal_0(float3 mapped_0, float2 row0_0, float2 row1_0)
{
    thread float3 _S21 = mapped_0;
    if(any(row1_0 != float2(0.0f, 0.0f)))
    {
        float _S22 = row1_0.x;
        float _S23 = row1_0.y;
        float2 tangent_0 = normalize(float2(_S23, - _S22) / float2(max(abs(_S22), abs(_S23))) );
        float2 bitangent_0 = float2(- tangent_0.y, tangent_0.x);
        float2 tangent_1;
        if((dot(row0_0, tangent_0)) < 0.0f)
        {
            tangent_1 = - tangent_0;
        }
        else
        {
            tangent_1 = tangent_0;
        }
        _S21.xy = tangent_1 * float2(_S21.x)  + bitangent_0 * float2(_S21.y) ;
    }
    return _S21;
}

float3 CombineDetailNormal_0(float3 primary_0, float3 detail_0)
{
    float3 t_0 = normalize(primary_0) + float3(0.0f, 0.0f, 1.0f);
    float3 u_0 = normalize(detail_0) * float3(-1.0f, -1.0f, 1.0f);
    float _S24 = t_0.z;
    if(_S24 == 0.0f)
    {
        return float3(- u_0.x, u_0.y, - u_0.z);
    }
    return normalize(t_0 * float3((dot(t_0, u_0) / _S24))  - u_0);
}

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
    packed_uint4 lightLinkMask_0;
    packed_uint4 shadowLinkMask_0;
    packed_float4 detailUvTransformRow0_0;
    packed_float4 detailUvTransformRow1_0;
};

struct KernelContext_0
{
    SurfaceParameters_natural_0 device* surfaceParameters_0;
    uint32_t device* frameParameters_0;
    texture2d<float, access::sample> baseColorTexture_0;
    sampler baseColorSampler_0;
    texture2d<float, access::sample> diffuseValuesTexture_0;
    texture2d<float, access::sample> compositeTexture_0;
    sampler compositeSampler_0;
    texture2d<float, access::sample> normalTexture_0;
    sampler normalSampler_0;
    texture2d<float, access::sample> normalValuesTexture_0;
    texture2d<float, access::sample> detailNormalTexture_0;
    sampler detailNormalSampler_0;
    texture2d<float, access::sample> detailNormalValuesTexture_0;
    texture2d<float, access::sample> roughnessMetallicTexture_0;
    sampler roughnessMetallicSampler_0;
    texture2d<float, access::sample> metallicTexture_0;
    sampler metallicSampler_0;
    texture2d<float, access::sample> emissiveTexture_0;
    sampler emissiveSampler_0;
    texture2d<float, access::sample> emissiveValuesTexture_0;
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

float4 ReadFrameLight_0(uint field_0, uint index_1, KernelContext_0 thread* kernelContext_0)
{
    uint _S25 = 224U + field_0 * 2048U + index_1 * 16U;
    float _S26 = as_type<float>(kernelContext_0->frameParameters_0[(_S25)>>2]);
    float _S27 = as_type<float>(kernelContext_0->frameParameters_0[(_S25 + 4U)>>2]);
    float _S28 = as_type<float>(kernelContext_0->frameParameters_0[(_S25 + 8U)>>2]);
    float _S29 = as_type<float>(kernelContext_0->frameParameters_0[(_S25 + 12U)>>2]);
    return float4(_S26, _S27, _S28, _S29);
}

float4 ReadFrameShadowSlot_0(uint index_2, KernelContext_0 thread* kernelContext_1)
{
    uint _S30 = 12960U + index_2 * 16U;
    float _S31 = as_type<float>(kernelContext_1->frameParameters_0[(_S30)>>2]);
    float _S32 = as_type<float>(kernelContext_1->frameParameters_0[(_S30 + 4U)>>2]);
    float _S33 = as_type<float>(kernelContext_1->frameParameters_0[(_S30 + 8U)>>2]);
    float _S34 = as_type<float>(kernelContext_1->frameParameters_0[(_S30 + 12U)>>2]);
    return float4(_S31, _S32, _S33, _S34);
}

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
    float4 tangent_2 [[user(TANGENT)]];
    float4 worldTangent_0 [[user(TEXCOORD_4)]];
};

struct FrameVectorView_0
{
    uint byteOffset_0;
};

struct FragmentFrameParameters_0
{
    uint4 clipPlaneCount_0;
    array<float4, int(8)> clipPlanes_0;
    float4 ambientLight_0;
    FrameVectorView_0 lightPositionType_0;
    FrameVectorView_0 lightDirectionRadius_0;
    FrameVectorView_0 lightColorIntensity_0;
    FrameVectorView_0 lightControls_0;
    FrameVectorView_0 lightTangentShapeX_0;
    FrameVectorView_0 lightBitangentShapeY_0;
    matrix<float,int(4),int(4)>  eyeToWorld_0;
    array<matrix<float,int(4),int(4)> , int(4)> shadowWorldToLightClip_0;
    array<float4, int(4)> shadowTile_0;
    array<float4, int(4)> shadowControls_0;
    FrameVectorView_0 shadowSlots_0;
    float4 environmentControls_0;
    float4 domeControls_0;
    array<float4, int(8)> domeAmbient_0;
    array<float4, int(8)> domeEnvironment_0;
};

[[fragment]] pixelOutput_0 fragmentMain_uv_material_normal(pixelInput_0 _S35 [[stage_in]], bool isFrontFace_0 [[front_facing]], float4 position_0 [[position]], SurfaceParameters_natural_0 device* surfaceParameters_1 [[buffer(7)]], uint32_t device* frameParameters_1 [[buffer(8)]], texture2d<float, access::sample> baseColorTexture_1 [[texture(0)]], sampler baseColorSampler_1 [[sampler(0)]], texture2d<float, access::sample> diffuseValuesTexture_1 [[texture(22)]], texture2d<float, access::sample> compositeTexture_1 [[texture(15)]], sampler compositeSampler_1 [[sampler(12)]], texture2d<float, access::sample> normalTexture_1 [[texture(1)]], sampler normalSampler_1 [[sampler(1)]], texture2d<float, access::sample> normalValuesTexture_1 [[texture(20)]], texture2d<float, access::sample> detailNormalTexture_1 [[texture(9)]], sampler detailNormalSampler_1 [[sampler(4)]], texture2d<float, access::sample> detailNormalValuesTexture_1 [[texture(21)]], texture2d<float, access::sample> roughnessMetallicTexture_1 [[texture(2)]], sampler roughnessMetallicSampler_1 [[sampler(2)]], texture2d<float, access::sample> metallicTexture_1 [[texture(4)]], sampler metallicSampler_1 [[sampler(5)]], texture2d<float, access::sample> emissiveTexture_1 [[texture(3)]], sampler emissiveSampler_1 [[sampler(3)]], texture2d<float, access::sample> emissiveValuesTexture_1 [[texture(23)]], texture2d<float, access::sample> opacityTexture_1 [[texture(5)]], sampler opacitySampler_1 [[sampler(6)]], texture2d<float, access::sample> occlusionTexture_1 [[texture(10)]], sampler occlusionSampler_1 [[sampler(7)]], texture2d<float, access::sample> specularColorTexture_1 [[texture(11)]], sampler specularColorSampler_1 [[sampler(8)]], texture2d<float, access::sample> clearcoatTexture_1 [[texture(12)]], sampler clearcoatSampler_1 [[sampler(9)]], texture2d<float, access::sample> clearcoatRoughnessTexture_1 [[texture(13)]], sampler clearcoatRoughnessSampler_1 [[sampler(10)]], texture2d<float, access::sample> iorTexture_1 [[texture(14)]], sampler iorSampler_1 [[sampler(11)]], texture2d<float, access::sample> shadowAtlas_1 [[texture(16)]], sampler shadowSampler_1 [[sampler(13)]], texture2d<float, access::sample> environmentBrdf_1 [[texture(19)]], sampler environmentBrdfSampler_1 [[sampler(15)]], texture2d<float, access::sample> environmentIrradiance_1 [[texture(17)]], sampler environmentSampler_1 [[sampler(14)]], texture2d<float, access::sample> environmentSpecular_1 [[texture(18)]])
{
    uint index_3;
    FragmentFrameParameters_0 _S36;
    uint4 _S37;
    uint sampleIndex_0;
    float3 lightDirection_0;
    float3 lightTangent_0;
    float3 lightBitangent_0;
    float3 color_0;
    float3 sampleDirection_0;
    bool _S38;
    bool _S39;
    thread KernelContext_0 kernelContext_2;
    (&kernelContext_2)->surfaceParameters_0 = surfaceParameters_1;
    (&kernelContext_2)->frameParameters_0 = frameParameters_1;
    (&kernelContext_2)->baseColorTexture_0 = baseColorTexture_1;
    (&kernelContext_2)->baseColorSampler_0 = baseColorSampler_1;
    (&kernelContext_2)->diffuseValuesTexture_0 = diffuseValuesTexture_1;
    (&kernelContext_2)->compositeTexture_0 = compositeTexture_1;
    (&kernelContext_2)->compositeSampler_0 = compositeSampler_1;
    (&kernelContext_2)->normalTexture_0 = normalTexture_1;
    (&kernelContext_2)->normalSampler_0 = normalSampler_1;
    (&kernelContext_2)->normalValuesTexture_0 = normalValuesTexture_1;
    (&kernelContext_2)->detailNormalTexture_0 = detailNormalTexture_1;
    (&kernelContext_2)->detailNormalSampler_0 = detailNormalSampler_1;
    (&kernelContext_2)->detailNormalValuesTexture_0 = detailNormalValuesTexture_1;
    (&kernelContext_2)->roughnessMetallicTexture_0 = roughnessMetallicTexture_1;
    (&kernelContext_2)->roughnessMetallicSampler_0 = roughnessMetallicSampler_1;
    (&kernelContext_2)->metallicTexture_0 = metallicTexture_1;
    (&kernelContext_2)->metallicSampler_0 = metallicSampler_1;
    (&kernelContext_2)->emissiveTexture_0 = emissiveTexture_1;
    (&kernelContext_2)->emissiveSampler_0 = emissiveSampler_1;
    (&kernelContext_2)->emissiveValuesTexture_0 = emissiveValuesTexture_1;
    (&kernelContext_2)->opacityTexture_0 = opacityTexture_1;
    (&kernelContext_2)->opacitySampler_0 = opacitySampler_1;
    (&kernelContext_2)->occlusionTexture_0 = occlusionTexture_1;
    (&kernelContext_2)->occlusionSampler_0 = occlusionSampler_1;
    (&kernelContext_2)->specularColorTexture_0 = specularColorTexture_1;
    (&kernelContext_2)->specularColorSampler_0 = specularColorSampler_1;
    (&kernelContext_2)->clearcoatTexture_0 = clearcoatTexture_1;
    (&kernelContext_2)->clearcoatSampler_0 = clearcoatSampler_1;
    (&kernelContext_2)->clearcoatRoughnessTexture_0 = clearcoatRoughnessTexture_1;
    (&kernelContext_2)->clearcoatRoughnessSampler_0 = clearcoatRoughnessSampler_1;
    (&kernelContext_2)->iorTexture_0 = iorTexture_1;
    (&kernelContext_2)->iorSampler_0 = iorSampler_1;
    (&kernelContext_2)->shadowAtlas_0 = shadowAtlas_1;
    (&kernelContext_2)->shadowSampler_0 = shadowSampler_1;
    (&kernelContext_2)->environmentBrdf_0 = environmentBrdf_1;
    (&kernelContext_2)->environmentBrdfSampler_0 = environmentBrdfSampler_1;
    (&kernelContext_2)->environmentIrradiance_0 = environmentIrradiance_1;
    (&kernelContext_2)->environmentSampler_0 = environmentSampler_1;
    (&kernelContext_2)->environmentSpecular_0 = environmentSpecular_1;
    SurfaceParameters_natural_0 device* _S40 = surfaceParameters_1+int(0);
    for(;;)
    {
        thread FragmentFrameParameters_0 frame_0;
        uint _S41 = as_type<uint>((&kernelContext_2)->frameParameters_0[(64U)>>2]);
        uint _S42 = as_type<uint>((&kernelContext_2)->frameParameters_0[(68U)>>2]);
        uint _S43 = as_type<uint>((&kernelContext_2)->frameParameters_0[(72U)>>2]);
        uint _S44 = as_type<uint>((&kernelContext_2)->frameParameters_0[(76U)>>2]);
        (&frame_0)->clipPlaneCount_0 = uint4(_S41, _S42, _S43, _S44);
        index_3 = 0U;
        for(;;)
        {
            if(index_3 < 8U)
            {
            }
            else
            {
                break;
            }
            uint _S45 = 80U + index_3 * 16U;
            float _S46 = as_type<float>((&kernelContext_2)->frameParameters_0[(_S45)>>2]);
            float _S47 = as_type<float>((&kernelContext_2)->frameParameters_0[(_S45 + 4U)>>2]);
            float _S48 = as_type<float>((&kernelContext_2)->frameParameters_0[(_S45 + 8U)>>2]);
            float _S49 = as_type<float>((&kernelContext_2)->frameParameters_0[(_S45 + 12U)>>2]);
            (&frame_0)->clipPlanes_0[index_3] = float4(_S46, _S47, _S48, _S49);
            index_3 = index_3 + 1U;
        }
        float _S50 = as_type<float>((&kernelContext_2)->frameParameters_0[(208U)>>2]);
        float _S51 = as_type<float>((&kernelContext_2)->frameParameters_0[(212U)>>2]);
        float _S52 = as_type<float>((&kernelContext_2)->frameParameters_0[(216U)>>2]);
        float _S53 = as_type<float>((&kernelContext_2)->frameParameters_0[(220U)>>2]);
        (&frame_0)->ambientLight_0 = float4(_S50, _S51, _S52, _S53);
        (&(&frame_0)->lightPositionType_0)->byteOffset_0 = 224U;
        (&(&frame_0)->lightDirectionRadius_0)->byteOffset_0 = 2272U;
        (&(&frame_0)->lightColorIntensity_0)->byteOffset_0 = 4320U;
        (&(&frame_0)->lightControls_0)->byteOffset_0 = 6368U;
        (&(&frame_0)->lightTangentShapeX_0)->byteOffset_0 = 8416U;
        (&(&frame_0)->lightBitangentShapeY_0)->byteOffset_0 = 10464U;
        (&(&frame_0)->shadowSlots_0)->byteOffset_0 = 12960U;
        float _S54 = as_type<float>((&kernelContext_2)->frameParameters_0[(12512U)>>2]);
        float _S55 = as_type<float>((&kernelContext_2)->frameParameters_0[(12516U)>>2]);
        float _S56 = as_type<float>((&kernelContext_2)->frameParameters_0[(12520U)>>2]);
        float _S57 = as_type<float>((&kernelContext_2)->frameParameters_0[(12524U)>>2]);
        float4 _S58 = float4(_S54, _S55, _S56, _S57);
        float _S59 = as_type<float>((&kernelContext_2)->frameParameters_0[(12528U)>>2]);
        float _S60 = as_type<float>((&kernelContext_2)->frameParameters_0[(12532U)>>2]);
        float _S61 = as_type<float>((&kernelContext_2)->frameParameters_0[(12536U)>>2]);
        float _S62 = as_type<float>((&kernelContext_2)->frameParameters_0[(12540U)>>2]);
        float4 _S63 = float4(_S59, _S60, _S61, _S62);
        float _S64 = as_type<float>((&kernelContext_2)->frameParameters_0[(12544U)>>2]);
        float _S65 = as_type<float>((&kernelContext_2)->frameParameters_0[(12548U)>>2]);
        float _S66 = as_type<float>((&kernelContext_2)->frameParameters_0[(12552U)>>2]);
        float _S67 = as_type<float>((&kernelContext_2)->frameParameters_0[(12556U)>>2]);
        float4 _S68 = float4(_S64, _S65, _S66, _S67);
        float _S69 = as_type<float>((&kernelContext_2)->frameParameters_0[(12560U)>>2]);
        float _S70 = as_type<float>((&kernelContext_2)->frameParameters_0[(12564U)>>2]);
        float _S71 = as_type<float>((&kernelContext_2)->frameParameters_0[(12568U)>>2]);
        float _S72 = as_type<float>((&kernelContext_2)->frameParameters_0[(12572U)>>2]);
        (&frame_0)->eyeToWorld_0 = matrix<float,int(4),int(4)> (_S58, _S63, _S68, float4(_S69, _S70, _S71, _S72));
        index_3 = 0U;
        for(;;)
        {
            if(index_3 < 4U)
            {
            }
            else
            {
                break;
            }
            uint _S73 = 12576U + index_3 * 64U;
            float _S74 = as_type<float>((&kernelContext_2)->frameParameters_0[(_S73)>>2]);
            float _S75 = as_type<float>((&kernelContext_2)->frameParameters_0[(_S73 + 4U)>>2]);
            float _S76 = as_type<float>((&kernelContext_2)->frameParameters_0[(_S73 + 8U)>>2]);
            float _S77 = as_type<float>((&kernelContext_2)->frameParameters_0[(_S73 + 12U)>>2]);
            float4 _S78 = float4(_S74, _S75, _S76, _S77);
            float _S79 = as_type<float>((&kernelContext_2)->frameParameters_0[(_S73 + 16U)>>2]);
            float _S80 = as_type<float>((&kernelContext_2)->frameParameters_0[(_S73 + 20U)>>2]);
            float _S81 = as_type<float>((&kernelContext_2)->frameParameters_0[(_S73 + 24U)>>2]);
            float _S82 = as_type<float>((&kernelContext_2)->frameParameters_0[(_S73 + 28U)>>2]);
            float4 _S83 = float4(_S79, _S80, _S81, _S82);
            float _S84 = as_type<float>((&kernelContext_2)->frameParameters_0[(_S73 + 32U)>>2]);
            float _S85 = as_type<float>((&kernelContext_2)->frameParameters_0[(_S73 + 36U)>>2]);
            float _S86 = as_type<float>((&kernelContext_2)->frameParameters_0[(_S73 + 40U)>>2]);
            float _S87 = as_type<float>((&kernelContext_2)->frameParameters_0[(_S73 + 44U)>>2]);
            float4 _S88 = float4(_S84, _S85, _S86, _S87);
            float _S89 = as_type<float>((&kernelContext_2)->frameParameters_0[(_S73 + 48U)>>2]);
            float _S90 = as_type<float>((&kernelContext_2)->frameParameters_0[(_S73 + 52U)>>2]);
            float _S91 = as_type<float>((&kernelContext_2)->frameParameters_0[(_S73 + 56U)>>2]);
            float _S92 = as_type<float>((&kernelContext_2)->frameParameters_0[(_S73 + 60U)>>2]);
            (&frame_0)->shadowWorldToLightClip_0[index_3] = matrix<float,int(4),int(4)> (_S78, _S83, _S88, float4(_S89, _S90, _S91, _S92));
            uint _S93 = index_3 * 16U;
            uint _S94 = 12832U + _S93;
            float _S95 = as_type<float>((&kernelContext_2)->frameParameters_0[(_S94)>>2]);
            float _S96 = as_type<float>((&kernelContext_2)->frameParameters_0[(_S94 + 4U)>>2]);
            float _S97 = as_type<float>((&kernelContext_2)->frameParameters_0[(_S94 + 8U)>>2]);
            float _S98 = as_type<float>((&kernelContext_2)->frameParameters_0[(_S94 + 12U)>>2]);
            (&frame_0)->shadowTile_0[index_3] = float4(_S95, _S96, _S97, _S98);
            uint _S99 = 12896U + _S93;
            float _S100 = as_type<float>((&kernelContext_2)->frameParameters_0[(_S99)>>2]);
            float _S101 = as_type<float>((&kernelContext_2)->frameParameters_0[(_S99 + 4U)>>2]);
            float _S102 = as_type<float>((&kernelContext_2)->frameParameters_0[(_S99 + 8U)>>2]);
            float _S103 = as_type<float>((&kernelContext_2)->frameParameters_0[(_S99 + 12U)>>2]);
            (&frame_0)->shadowControls_0[index_3] = float4(_S100, _S101, _S102, _S103);
            index_3 = index_3 + 1U;
        }
        float _S104 = as_type<float>((&kernelContext_2)->frameParameters_0[(15008U)>>2]);
        float _S105 = as_type<float>((&kernelContext_2)->frameParameters_0[(15012U)>>2]);
        float _S106 = as_type<float>((&kernelContext_2)->frameParameters_0[(15016U)>>2]);
        float _S107 = as_type<float>((&kernelContext_2)->frameParameters_0[(15020U)>>2]);
        (&frame_0)->environmentControls_0 = float4(_S104, _S105, _S106, _S107);
        float _S108 = as_type<float>((&kernelContext_2)->frameParameters_0[(15024U)>>2]);
        float _S109 = as_type<float>((&kernelContext_2)->frameParameters_0[(15028U)>>2]);
        float _S110 = as_type<float>((&kernelContext_2)->frameParameters_0[(15032U)>>2]);
        float _S111 = as_type<float>((&kernelContext_2)->frameParameters_0[(15036U)>>2]);
        (&frame_0)->domeControls_0 = float4(_S108, _S109, _S110, _S111);
        float _S112 = as_type<float>((&kernelContext_2)->frameParameters_0[(15040U)>>2]);
        float _S113 = as_type<float>((&kernelContext_2)->frameParameters_0[(15044U)>>2]);
        float _S114 = as_type<float>((&kernelContext_2)->frameParameters_0[(15048U)>>2]);
        float _S115 = as_type<float>((&kernelContext_2)->frameParameters_0[(15052U)>>2]);
        float4 _S116 = float4(_S112, _S113, _S114, _S115);
        float _S117 = as_type<float>((&kernelContext_2)->frameParameters_0[(15056U)>>2]);
        float _S118 = as_type<float>((&kernelContext_2)->frameParameters_0[(15060U)>>2]);
        float _S119 = as_type<float>((&kernelContext_2)->frameParameters_0[(15064U)>>2]);
        float _S120 = as_type<float>((&kernelContext_2)->frameParameters_0[(15068U)>>2]);
        float4 _S121 = float4(_S117, _S118, _S119, _S120);
        float _S122 = as_type<float>((&kernelContext_2)->frameParameters_0[(15072U)>>2]);
        float _S123 = as_type<float>((&kernelContext_2)->frameParameters_0[(15076U)>>2]);
        float _S124 = as_type<float>((&kernelContext_2)->frameParameters_0[(15080U)>>2]);
        float _S125 = as_type<float>((&kernelContext_2)->frameParameters_0[(15084U)>>2]);
        float4 _S126 = float4(_S122, _S123, _S124, _S125);
        float _S127 = as_type<float>((&kernelContext_2)->frameParameters_0[(15088U)>>2]);
        float _S128 = as_type<float>((&kernelContext_2)->frameParameters_0[(15092U)>>2]);
        float _S129 = as_type<float>((&kernelContext_2)->frameParameters_0[(15096U)>>2]);
        float _S130 = as_type<float>((&kernelContext_2)->frameParameters_0[(15100U)>>2]);
        float4 _S131 = float4(_S127, _S128, _S129, _S130);
        float _S132 = as_type<float>((&kernelContext_2)->frameParameters_0[(15104U)>>2]);
        float _S133 = as_type<float>((&kernelContext_2)->frameParameters_0[(15108U)>>2]);
        float _S134 = as_type<float>((&kernelContext_2)->frameParameters_0[(15112U)>>2]);
        float _S135 = as_type<float>((&kernelContext_2)->frameParameters_0[(15116U)>>2]);
        float4 _S136 = float4(_S132, _S133, _S134, _S135);
        float _S137 = as_type<float>((&kernelContext_2)->frameParameters_0[(15120U)>>2]);
        float _S138 = as_type<float>((&kernelContext_2)->frameParameters_0[(15124U)>>2]);
        float _S139 = as_type<float>((&kernelContext_2)->frameParameters_0[(15128U)>>2]);
        float _S140 = as_type<float>((&kernelContext_2)->frameParameters_0[(15132U)>>2]);
        float4 _S141 = float4(_S137, _S138, _S139, _S140);
        float _S142 = as_type<float>((&kernelContext_2)->frameParameters_0[(15136U)>>2]);
        float _S143 = as_type<float>((&kernelContext_2)->frameParameters_0[(15140U)>>2]);
        float _S144 = as_type<float>((&kernelContext_2)->frameParameters_0[(15144U)>>2]);
        float _S145 = as_type<float>((&kernelContext_2)->frameParameters_0[(15148U)>>2]);
        float4 _S146 = float4(_S142, _S143, _S144, _S145);
        float _S147 = as_type<float>((&kernelContext_2)->frameParameters_0[(15152U)>>2]);
        float _S148 = as_type<float>((&kernelContext_2)->frameParameters_0[(15156U)>>2]);
        float _S149 = as_type<float>((&kernelContext_2)->frameParameters_0[(15160U)>>2]);
        float _S150 = as_type<float>((&kernelContext_2)->frameParameters_0[(15164U)>>2]);
        float4 _S151 = float4(_S147, _S148, _S149, _S150);
        float _S152 = as_type<float>((&kernelContext_2)->frameParameters_0[(15168U)>>2]);
        float _S153 = as_type<float>((&kernelContext_2)->frameParameters_0[(15172U)>>2]);
        float _S154 = as_type<float>((&kernelContext_2)->frameParameters_0[(15176U)>>2]);
        float _S155 = as_type<float>((&kernelContext_2)->frameParameters_0[(15180U)>>2]);
        float4 _S156 = float4(_S152, _S153, _S154, _S155);
        float _S157 = as_type<float>((&kernelContext_2)->frameParameters_0[(15184U)>>2]);
        float _S158 = as_type<float>((&kernelContext_2)->frameParameters_0[(15188U)>>2]);
        float _S159 = as_type<float>((&kernelContext_2)->frameParameters_0[(15192U)>>2]);
        float _S160 = as_type<float>((&kernelContext_2)->frameParameters_0[(15196U)>>2]);
        float4 _S161 = float4(_S157, _S158, _S159, _S160);
        float _S162 = as_type<float>((&kernelContext_2)->frameParameters_0[(15200U)>>2]);
        float _S163 = as_type<float>((&kernelContext_2)->frameParameters_0[(15204U)>>2]);
        float _S164 = as_type<float>((&kernelContext_2)->frameParameters_0[(15208U)>>2]);
        float _S165 = as_type<float>((&kernelContext_2)->frameParameters_0[(15212U)>>2]);
        float4 _S166 = float4(_S162, _S163, _S164, _S165);
        float _S167 = as_type<float>((&kernelContext_2)->frameParameters_0[(15216U)>>2]);
        float _S168 = as_type<float>((&kernelContext_2)->frameParameters_0[(15220U)>>2]);
        float _S169 = as_type<float>((&kernelContext_2)->frameParameters_0[(15224U)>>2]);
        float _S170 = as_type<float>((&kernelContext_2)->frameParameters_0[(15228U)>>2]);
        float4 _S171 = float4(_S167, _S168, _S169, _S170);
        float _S172 = as_type<float>((&kernelContext_2)->frameParameters_0[(15232U)>>2]);
        float _S173 = as_type<float>((&kernelContext_2)->frameParameters_0[(15236U)>>2]);
        float _S174 = as_type<float>((&kernelContext_2)->frameParameters_0[(15240U)>>2]);
        float _S175 = as_type<float>((&kernelContext_2)->frameParameters_0[(15244U)>>2]);
        float4 _S176 = float4(_S172, _S173, _S174, _S175);
        float _S177 = as_type<float>((&kernelContext_2)->frameParameters_0[(15248U)>>2]);
        float _S178 = as_type<float>((&kernelContext_2)->frameParameters_0[(15252U)>>2]);
        float _S179 = as_type<float>((&kernelContext_2)->frameParameters_0[(15256U)>>2]);
        float _S180 = as_type<float>((&kernelContext_2)->frameParameters_0[(15260U)>>2]);
        float4 _S181 = float4(_S177, _S178, _S179, _S180);
        float _S182 = as_type<float>((&kernelContext_2)->frameParameters_0[(15264U)>>2]);
        float _S183 = as_type<float>((&kernelContext_2)->frameParameters_0[(15268U)>>2]);
        float _S184 = as_type<float>((&kernelContext_2)->frameParameters_0[(15272U)>>2]);
        float _S185 = as_type<float>((&kernelContext_2)->frameParameters_0[(15276U)>>2]);
        float4 _S186 = float4(_S182, _S183, _S184, _S185);
        float _S187 = as_type<float>((&kernelContext_2)->frameParameters_0[(15280U)>>2]);
        float _S188 = as_type<float>((&kernelContext_2)->frameParameters_0[(15284U)>>2]);
        float _S189 = as_type<float>((&kernelContext_2)->frameParameters_0[(15288U)>>2]);
        float _S190 = as_type<float>((&kernelContext_2)->frameParameters_0[(15292U)>>2]);
        float4 _S191 = float4(_S187, _S188, _S189, _S190);
        (&frame_0)->domeAmbient_0[int(0)] = _S116;
        (&frame_0)->domeAmbient_0[int(1)] = _S121;
        (&frame_0)->domeAmbient_0[int(2)] = _S126;
        (&frame_0)->domeAmbient_0[int(3)] = _S131;
        (&frame_0)->domeAmbient_0[int(4)] = _S136;
        (&frame_0)->domeAmbient_0[int(5)] = _S141;
        (&frame_0)->domeAmbient_0[int(6)] = _S146;
        (&frame_0)->domeAmbient_0[int(7)] = _S151;
        (&frame_0)->domeEnvironment_0[int(0)] = _S156;
        (&frame_0)->domeEnvironment_0[int(1)] = _S161;
        (&frame_0)->domeEnvironment_0[int(2)] = _S166;
        (&frame_0)->domeEnvironment_0[int(3)] = _S171;
        (&frame_0)->domeEnvironment_0[int(4)] = _S176;
        (&frame_0)->domeEnvironment_0[int(5)] = _S181;
        (&frame_0)->domeEnvironment_0[int(6)] = _S186;
        (&frame_0)->domeEnvironment_0[int(7)] = _S191;
        _S36 = frame_0;
        break;
    }
    for(;;)
    {
        _S37 = _S36.clipPlaneCount_0;
        uint _S192 = min(_S36.clipPlaneCount_0.x, 8U);
        index_3 = 0U;
        for(;;)
        {
            if(index_3 < _S192)
            {
            }
            else
            {
                break;
            }
            if((dot(_S36.clipPlanes_0[index_3].xyz, _S35.eyePosition_0) + _S36.clipPlanes_0[index_3].w) < 0.0f)
            {
                discard_fragment();
            }
            index_3 = index_3 + 1U;
        }
        break;
    }
    float4 _S193 = float4(_S40->clearcoatShaded_0) ;
    float shadedMode_0 = _S193.z;
    bool shaded_0 = shadedMode_0 >= 0.5f;
    bool unlit_0 = shadedMode_0 >= 1.5f;
    float3 diffuseColor_0;
    if(shaded_0)
    {
        diffuseColor_0 = (float4(_S40->diffuseOpacity_0) ).xyz;
    }
    else
    {
        diffuseColor_0 = _S35.tint_0.xyz;
    }
    float opacity_0;
    if(shaded_0)
    {
        opacity_0 = (float4(_S40->diffuseOpacity_0) ).w;
    }
    else
    {
        opacity_0 = _S35.tint_0.w;
    }
    float4 _S194 = float4(_S40->emissiveOcclusion_0) ;
    float3 emissiveColor_0 = _S194.xyz;
    float _S195 = _S194.w;
    float3 unlitColor_0;
    if(unlit_0)
    {
        float3 unlitColor_1 = (diffuseColor_0 + emissiveColor_0) * float3(exp2((as_type<float>((_S37.z))))) ;
        if((_S37.y) == 1U)
        {
            unlitColor_0 = unlitColor_1 / (float3(1.0f)  + max(unlitColor_1, float3(0.0f, 0.0f, 0.0f)));
        }
        else
        {
            unlitColor_0 = unlitColor_1;
        }
        pixelOutput_0 _S196 = { float4(unlitColor_0, opacity_0) };
        return _S196;
    }
    float4 _S197 = float4(_S40->reserved_0) ;
    if((_S197.x) >= 0.5f)
    {
        pixelOutput_0 _S198 = { float4(diffuseColor_0 * float3((1.0f - exp(- max(0.0f, _S197.y) * max(0.0f, _S197.z)))) , 1.0f) };
        return _S198;
    }
    float4 _S199 = float4(_S40->metallicRoughnessThresholdWorkflow_0) ;
    float _S200 = saturate(_S199.x);
    float _S201 = clamp(_S199.y, 0.00999999977648258f, 1.0f);
    float4 _S202 = float4(_S40->specularIor_0) ;
    float3 _S203 = _S202.xyz;
    float _S204 = _S202.w;
    float _S205 = _S193.x;
    float _S206 = _S193.y;
    float4 _S207 = float4(_S40->textureControls_0) ;
    uint textureMask_0 = uint(round(_S207.x));
    uint udimMask_0 = uint(round(_S207.y));
    float4 _S208 = float4(_S40->uvTransformRow0_0) ;
    float2 _S209 = _S208.xy;
    float4 _S210 = float4(_S40->uvTransformRow1_0) ;
    float2 _S211 = _S210.xy;
    float2 _S212 = float2(dot(_S209, _S35.texCoord_0) + _S208.z, dot(_S211, _S35.texCoord_0) + _S210.z);
    bool hasSceneLighting_0;
    float4 _S213;
    if((textureMask_0 & 2U) != 0U)
    {
        thread float4 diffuseSample_0;
        bool _S214 = (udimMask_0 & 2U) != 0U;
        for(;;)
        {
            if(!_S214)
            {
                _S213 = (((&kernelContext_2)->baseColorTexture_0).sample(((&kernelContext_2)->baseColorSampler_0), (_S212)));
                break;
            }
            texture2d<float, access::sample> _S215 = (&kernelContext_2)->baseColorTexture_0;
            thread uint atlasWidth_0;
            thread uint atlasHeight_0;
            (*((&atlasWidth_0)) = (_S215).get_width(0)),(*((&atlasHeight_0)) = (_S215).get_height(0));
            int3 _S216 = int3(int(0), int(0), int(0));
            float4 metadata_0 = round((((&kernelContext_2)->baseColorTexture_0).read(vec<uint,2>(((_S216)).xy), uint(((_S216)).z))) * float4(255.0f) );
            if((metadata_0.x) == 255.0f)
            {
                uint _S217 = atlasWidth_0;
                uint _S218 = 2U % atlasWidth_0;
                int _S219 = int(_S218);
                uint _S220 = 2U / _S217;
                int3 _S221 = int3(_S219, int(_S220), int(0));
                int _S222 = int(uint(round((((&kernelContext_2)->baseColorTexture_0).read(vec<uint,2>(((_S221)).xy), uint(((_S221)).z))).x * 255.0f)));
                uint _S223 = atlasWidth_0;
                uint _S224 = 3U % atlasWidth_0;
                int _S225 = int(_S224);
                uint _S226 = 3U / _S223;
                int3 _S227 = int3(_S225, int(_S226), int(0));
                int2 minimumTile_0 = int2(_S222, int(uint(round((((&kernelContext_2)->baseColorTexture_0).read(vec<uint,2>(((_S227)).xy), uint(((_S227)).z))).x * 255.0f))));
                uint _S228 = atlasWidth_0;
                uint _S229 = 4U % atlasWidth_0;
                int _S230 = int(_S229);
                uint _S231 = 4U / _S228;
                int3 _S232 = int3(_S230, int(_S231), int(0));
                uint _S233 = uint(round((((&kernelContext_2)->baseColorTexture_0).read(vec<uint,2>(((_S232)).xy), uint(((_S232)).z))).x * 255.0f));
                uint _S234 = atlasWidth_0;
                uint _S235 = 5U % atlasWidth_0;
                int _S236 = int(_S235);
                uint _S237 = 5U / _S234;
                int3 _S238 = int3(_S236, int(_S237), int(0));
                uint _S239 = uint(round((((&kernelContext_2)->baseColorTexture_0).read(vec<uint,2>(((_S238)).xy), uint(((_S238)).z))).x * 255.0f));
                uint2 grid_0 = uint2(_S233, _S239);
                int2 tile_0 = int2(floor(_S212)) - minimumTile_0;
                if(any(tile_0 < (int2(int(0)) )))
                {
                    hasSceneLighting_0 = true;
                }
                else
                {
                    hasSceneLighting_0 = any(tile_0 >= int2(grid_0));
                }
                if(hasSceneLighting_0)
                {
                    int3 _S240 = int3(int(1), int(0), int(0));
                    _S213 = (((&kernelContext_2)->baseColorTexture_0).read(vec<uint,2>(((_S240)).xy), uint(((_S240)).z)));
                    break;
                }
                uint offset_0 = 10U + 2U * (uint(tile_0.y) * _S233 + uint(tile_0.x));
                uint _S241 = atlasWidth_0;
                uint _S242 = offset_0 % atlasWidth_0;
                int _S243 = int(_S242);
                uint _S244 = offset_0 / _S241;
                int3 _S245 = int3(_S243, int(_S244), int(0));
                uint _S246 = uint(round((((&kernelContext_2)->baseColorTexture_0).read(vec<uint,2>(((_S245)).xy), uint(((_S245)).z))).x * 255.0f));
                uint _S247 = atlasWidth_0;
                uint _S248 = offset_0 + 1U;
                uint _S249 = _S248 % atlasWidth_0;
                int _S250 = int(_S249);
                uint _S251 = _S248 / _S247;
                int3 _S252 = int3(_S250, int(_S251), int(0));
                uint slot_0 = _S246 | (uint(round((((&kernelContext_2)->baseColorTexture_0).read(vec<uint,2>(((_S252)).xy), uint(((_S252)).z))).x * 255.0f)) << 8U);
                if(slot_0 == 0U)
                {
                    int3 _S253 = int3(int(1), int(0), int(0));
                    _S213 = (((&kernelContext_2)->baseColorTexture_0).read(vec<uint,2>(((_S253)).xy), uint(((_S253)).z)));
                    break;
                }
                uint slot_1 = slot_0 - 1U;
                uint _S254 = atlasWidth_0;
                uint _S255 = 6U % atlasWidth_0;
                int _S256 = int(_S255);
                uint _S257 = 6U / _S254;
                int3 _S258 = int3(_S256, int(_S257), int(0));
                uint _S259 = uint(round((((&kernelContext_2)->baseColorTexture_0).read(vec<uint,2>(((_S258)).xy), uint(((_S258)).z))).x * 255.0f));
                uint _S260 = atlasWidth_0;
                uint _S261 = 7U % atlasWidth_0;
                int _S262 = int(_S261);
                uint _S263 = 7U / _S260;
                int3 _S264 = int3(_S262, int(_S263), int(0));
                uint columns_0 = _S259 | (uint(round((((&kernelContext_2)->baseColorTexture_0).read(vec<uint,2>(((_S264)).xy), uint(((_S264)).z))).x * 255.0f)) << 8U);
                uint _S265 = atlasWidth_0;
                uint _S266 = 8U % atlasWidth_0;
                int _S267 = int(_S266);
                uint _S268 = 8U / _S265;
                int3 _S269 = int3(_S267, int(_S268), int(0));
                uint _S270 = uint(round((((&kernelContext_2)->baseColorTexture_0).read(vec<uint,2>(((_S269)).xy), uint(((_S269)).z))).x * 255.0f));
                uint _S271 = atlasWidth_0;
                uint _S272 = 9U % atlasWidth_0;
                int _S273 = int(_S272);
                uint _S274 = 9U / _S271;
                int3 _S275 = int3(_S273, int(_S274), int(0));
                uint rows_0 = ((_S270 | (uint(round((((&kernelContext_2)->baseColorTexture_0).read(vec<uint,2>(((_S275)).xy), uint(((_S275)).z))).x * 255.0f)) << 8U)) + columns_0 - 1U) / columns_0;
                uint metadataRows_0 = (10U + 2U * _S233 * _S239 + atlasWidth_0 - 1U) / atlasWidth_0;
                uint _S276 = atlasWidth_0 / columns_0;
                float _S277 = float(_S276);
                uint _S278 = (atlasHeight_0 - metadataRows_0) / rows_0;
                float2 cellSize_0 = float2(_S277, float(_S278));
                uint _S279 = slot_1 % columns_0;
                float _S280 = float(_S279);
                uint _S281 = slot_1 / columns_0;
                _S213 = (((&kernelContext_2)->baseColorTexture_0).sample(((&kernelContext_2)->baseColorSampler_0), ((float2(_S280, float(_S281)) * cellSize_0 + float2(1.5f, float(metadataRows_0) + 1.5f) + fract(_S212) * max(cellSize_0 - float2(3.0f) , float2(0.0f) )) / float2(float(atlasWidth_0), float(atlasHeight_0)))));
                break;
            }
            int2 _S282 = int2(metadata_0.zw);
            int2 tile_1 = int2(floor(_S212)) - int2(metadata_0.xy);
            if(any(tile_1 < (int2(int(0)) )))
            {
                hasSceneLighting_0 = true;
            }
            else
            {
                hasSceneLighting_0 = any(tile_1 >= _S282);
            }
            if(hasSceneLighting_0)
            {
                int3 _S283 = int3(int(min(1U, atlasWidth_0 - 1U)), int(0), int(0));
                _S213 = (((&kernelContext_2)->baseColorTexture_0).read(vec<uint,2>(((_S283)).xy), uint(((_S283)).z)));
                break;
            }
            uint _S284 = atlasWidth_0 / uint(_S282.x);
            float _S285 = float(_S284);
            uint _S286 = (atlasHeight_0 - 1U) / uint(_S282.y);
            float2 cellSize_1 = float2(_S285, float(_S286));
            _S213 = (((&kernelContext_2)->baseColorTexture_0).sample(((&kernelContext_2)->baseColorSampler_0), ((float2(tile_1) * cellSize_1 + float2(1.5f, 2.5f) + fract(_S212) * max(cellSize_1 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_0), float(atlasHeight_0)))));
            break;
        }
        diffuseSample_0 = _S213;
        float4 _S287 = float4(_S40->compositeControls_0) ;
        float _S288 = _S287.x;
        if(_S288 == 0.0f)
        {
            hasSceneLighting_0 = (_S287.w) == 2.0f;
        }
        else
        {
            hasSceneLighting_0 = false;
        }
        if(hasSceneLighting_0)
        {
            texture2d<float, access::sample> _S289 = (&kernelContext_2)->baseColorTexture_0;
            thread uint sourceWidth_0;
            thread uint sourceHeight_0;
            (*((&sourceWidth_0)) = (_S289).get_width(0)),(*((&sourceHeight_0)) = (_S289).get_height(0));
            texture2d<float, access::sample> _S290 = (&kernelContext_2)->diffuseValuesTexture_0;
            thread uint valueWidth_0;
            thread uint valueHeight_0;
            (*((&valueWidth_0)) = (_S290).get_width(0)),(*((&valueHeight_0)) = (_S290).get_height(0));
            if(valueWidth_0 != sourceWidth_0)
            {
                float3 _S291 = DecodeIndexedRgb_0(diffuseSample_0, (&kernelContext_2)->diffuseValuesTexture_0);
                diffuseSample_0.xyz = _S291;
            }
            else
            {
                diffuseSample_0.z = (((&kernelContext_2)->diffuseValuesTexture_0).sample(((&kernelContext_2)->baseColorSampler_0), (_S212))).x;
            }
        }
        float4 _S292 = diffuseSample_0;
        for(;;)
        {
            if(_S288 != 2.0f)
            {
                _S213 = _S292;
                break;
            }
            bool _S293 = (_S287.w) >= 0.5f;
            for(;;)
            {
                if(!_S293)
                {
                    _S213 = (((&kernelContext_2)->compositeTexture_0).sample(((&kernelContext_2)->compositeSampler_0), (_S212)));
                    break;
                }
                texture2d<float, access::sample> _S294 = (&kernelContext_2)->compositeTexture_0;
                thread uint atlasWidth_1;
                thread uint atlasHeight_1;
                (*((&atlasWidth_1)) = (_S294).get_width(0)),(*((&atlasHeight_1)) = (_S294).get_height(0));
                int3 _S295 = int3(int(0), int(0), int(0));
                float4 metadata_1 = round((((&kernelContext_2)->compositeTexture_0).read(vec<uint,2>(((_S295)).xy), uint(((_S295)).z))) * float4(255.0f) );
                if((metadata_1.x) == 255.0f)
                {
                    uint _S296 = atlasWidth_1;
                    uint _S297 = 2U % atlasWidth_1;
                    int _S298 = int(_S297);
                    uint _S299 = 2U / _S296;
                    int3 _S300 = int3(_S298, int(_S299), int(0));
                    int _S301 = int(uint(round((((&kernelContext_2)->compositeTexture_0).read(vec<uint,2>(((_S300)).xy), uint(((_S300)).z))).x * 255.0f)));
                    uint _S302 = atlasWidth_1;
                    uint _S303 = 3U % atlasWidth_1;
                    int _S304 = int(_S303);
                    uint _S305 = 3U / _S302;
                    int3 _S306 = int3(_S304, int(_S305), int(0));
                    int2 minimumTile_1 = int2(_S301, int(uint(round((((&kernelContext_2)->compositeTexture_0).read(vec<uint,2>(((_S306)).xy), uint(((_S306)).z))).x * 255.0f))));
                    uint _S307 = atlasWidth_1;
                    uint _S308 = 4U % atlasWidth_1;
                    int _S309 = int(_S308);
                    uint _S310 = 4U / _S307;
                    int3 _S311 = int3(_S309, int(_S310), int(0));
                    uint _S312 = uint(round((((&kernelContext_2)->compositeTexture_0).read(vec<uint,2>(((_S311)).xy), uint(((_S311)).z))).x * 255.0f));
                    uint _S313 = atlasWidth_1;
                    uint _S314 = 5U % atlasWidth_1;
                    int _S315 = int(_S314);
                    uint _S316 = 5U / _S313;
                    int3 _S317 = int3(_S315, int(_S316), int(0));
                    uint _S318 = uint(round((((&kernelContext_2)->compositeTexture_0).read(vec<uint,2>(((_S317)).xy), uint(((_S317)).z))).x * 255.0f));
                    uint2 grid_1 = uint2(_S312, _S318);
                    int2 tile_2 = int2(floor(_S212)) - minimumTile_1;
                    if(any(tile_2 < (int2(int(0)) )))
                    {
                        hasSceneLighting_0 = true;
                    }
                    else
                    {
                        hasSceneLighting_0 = any(tile_2 >= int2(grid_1));
                    }
                    if(hasSceneLighting_0)
                    {
                        int3 _S319 = int3(int(1), int(0), int(0));
                        _S213 = (((&kernelContext_2)->compositeTexture_0).read(vec<uint,2>(((_S319)).xy), uint(((_S319)).z)));
                        break;
                    }
                    uint offset_1 = 10U + 2U * (uint(tile_2.y) * _S312 + uint(tile_2.x));
                    uint _S320 = atlasWidth_1;
                    uint _S321 = offset_1 % atlasWidth_1;
                    int _S322 = int(_S321);
                    uint _S323 = offset_1 / _S320;
                    int3 _S324 = int3(_S322, int(_S323), int(0));
                    uint _S325 = uint(round((((&kernelContext_2)->compositeTexture_0).read(vec<uint,2>(((_S324)).xy), uint(((_S324)).z))).x * 255.0f));
                    uint _S326 = atlasWidth_1;
                    uint _S327 = offset_1 + 1U;
                    uint _S328 = _S327 % atlasWidth_1;
                    int _S329 = int(_S328);
                    uint _S330 = _S327 / _S326;
                    int3 _S331 = int3(_S329, int(_S330), int(0));
                    uint slot_2 = _S325 | (uint(round((((&kernelContext_2)->compositeTexture_0).read(vec<uint,2>(((_S331)).xy), uint(((_S331)).z))).x * 255.0f)) << 8U);
                    if(slot_2 == 0U)
                    {
                        int3 _S332 = int3(int(1), int(0), int(0));
                        _S213 = (((&kernelContext_2)->compositeTexture_0).read(vec<uint,2>(((_S332)).xy), uint(((_S332)).z)));
                        break;
                    }
                    uint slot_3 = slot_2 - 1U;
                    uint _S333 = atlasWidth_1;
                    uint _S334 = 6U % atlasWidth_1;
                    int _S335 = int(_S334);
                    uint _S336 = 6U / _S333;
                    int3 _S337 = int3(_S335, int(_S336), int(0));
                    uint _S338 = uint(round((((&kernelContext_2)->compositeTexture_0).read(vec<uint,2>(((_S337)).xy), uint(((_S337)).z))).x * 255.0f));
                    uint _S339 = atlasWidth_1;
                    uint _S340 = 7U % atlasWidth_1;
                    int _S341 = int(_S340);
                    uint _S342 = 7U / _S339;
                    int3 _S343 = int3(_S341, int(_S342), int(0));
                    uint columns_1 = _S338 | (uint(round((((&kernelContext_2)->compositeTexture_0).read(vec<uint,2>(((_S343)).xy), uint(((_S343)).z))).x * 255.0f)) << 8U);
                    uint _S344 = atlasWidth_1;
                    uint _S345 = 8U % atlasWidth_1;
                    int _S346 = int(_S345);
                    uint _S347 = 8U / _S344;
                    int3 _S348 = int3(_S346, int(_S347), int(0));
                    uint _S349 = uint(round((((&kernelContext_2)->compositeTexture_0).read(vec<uint,2>(((_S348)).xy), uint(((_S348)).z))).x * 255.0f));
                    uint _S350 = atlasWidth_1;
                    uint _S351 = 9U % atlasWidth_1;
                    int _S352 = int(_S351);
                    uint _S353 = 9U / _S350;
                    int3 _S354 = int3(_S352, int(_S353), int(0));
                    uint rows_1 = ((_S349 | (uint(round((((&kernelContext_2)->compositeTexture_0).read(vec<uint,2>(((_S354)).xy), uint(((_S354)).z))).x * 255.0f)) << 8U)) + columns_1 - 1U) / columns_1;
                    uint metadataRows_1 = (10U + 2U * _S312 * _S318 + atlasWidth_1 - 1U) / atlasWidth_1;
                    uint _S355 = atlasWidth_1 / columns_1;
                    float _S356 = float(_S355);
                    uint _S357 = (atlasHeight_1 - metadataRows_1) / rows_1;
                    float2 cellSize_2 = float2(_S356, float(_S357));
                    uint _S358 = slot_3 % columns_1;
                    float _S359 = float(_S358);
                    uint _S360 = slot_3 / columns_1;
                    _S213 = (((&kernelContext_2)->compositeTexture_0).sample(((&kernelContext_2)->compositeSampler_0), ((float2(_S359, float(_S360)) * cellSize_2 + float2(1.5f, float(metadataRows_1) + 1.5f) + fract(_S212) * max(cellSize_2 - float2(3.0f) , float2(0.0f) )) / float2(float(atlasWidth_1), float(atlasHeight_1)))));
                    break;
                }
                int2 _S361 = int2(metadata_1.zw);
                int2 tile_3 = int2(floor(_S212)) - int2(metadata_1.xy);
                if(any(tile_3 < (int2(int(0)) )))
                {
                    hasSceneLighting_0 = true;
                }
                else
                {
                    hasSceneLighting_0 = any(tile_3 >= _S361);
                }
                if(hasSceneLighting_0)
                {
                    int3 _S362 = int3(int(min(1U, atlasWidth_1 - 1U)), int(0), int(0));
                    _S213 = (((&kernelContext_2)->compositeTexture_0).read(vec<uint,2>(((_S362)).xy), uint(((_S362)).z)));
                    break;
                }
                uint _S363 = atlasWidth_1 / uint(_S361.x);
                float _S364 = float(_S363);
                uint _S365 = (atlasHeight_1 - 1U) / uint(_S361.y);
                float2 cellSize_3 = float2(_S364, float(_S365));
                _S213 = (((&kernelContext_2)->compositeTexture_0).sample(((&kernelContext_2)->compositeSampler_0), ((float2(tile_3) * cellSize_3 + float2(1.5f, 2.5f) + fract(_S212) * max(cellSize_3 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_1), float(atlasHeight_1)))));
                break;
            }
            uint operation_0 = uint(round(_S287.y));
            if(operation_0 == 1U)
            {
                _S213 = _S292 * _S213;
                break;
            }
            if(operation_0 == 2U)
            {
                _S213 = _S292 + _S213;
                break;
            }
            if(operation_0 == 3U)
            {
                _S213 = _S292 - _S213;
                break;
            }
            if(operation_0 == 4U)
            {
                float factor_0 = _S287.z;
                _S213 = _S292 * float4((1.0f - factor_0))  + _S213 * float4(factor_0) ;
                break;
            }
            _S213 = _S292;
            break;
        }
        diffuseColor_0 = _S213.xyz;
    }
    thread float4 baseNormalSample_0;
    bool _S366 = (udimMask_0 & 4U) != 0U;
    for(;;)
    {
        if(!_S366)
        {
            _S213 = (((&kernelContext_2)->normalTexture_0).sample(((&kernelContext_2)->normalSampler_0), (_S212)));
            break;
        }
        texture2d<float, access::sample> _S367 = (&kernelContext_2)->normalTexture_0;
        thread uint atlasWidth_2;
        thread uint atlasHeight_2;
        (*((&atlasWidth_2)) = (_S367).get_width(0)),(*((&atlasHeight_2)) = (_S367).get_height(0));
        int3 _S368 = int3(int(0), int(0), int(0));
        float4 metadata_2 = round((((&kernelContext_2)->normalTexture_0).read(vec<uint,2>(((_S368)).xy), uint(((_S368)).z))) * float4(255.0f) );
        if((metadata_2.x) == 255.0f)
        {
            uint _S369 = atlasWidth_2;
            uint _S370 = 2U % atlasWidth_2;
            int _S371 = int(_S370);
            uint _S372 = 2U / _S369;
            int3 _S373 = int3(_S371, int(_S372), int(0));
            int _S374 = int(uint(round((((&kernelContext_2)->normalTexture_0).read(vec<uint,2>(((_S373)).xy), uint(((_S373)).z))).x * 255.0f)));
            uint _S375 = atlasWidth_2;
            uint _S376 = 3U % atlasWidth_2;
            int _S377 = int(_S376);
            uint _S378 = 3U / _S375;
            int3 _S379 = int3(_S377, int(_S378), int(0));
            int2 minimumTile_2 = int2(_S374, int(uint(round((((&kernelContext_2)->normalTexture_0).read(vec<uint,2>(((_S379)).xy), uint(((_S379)).z))).x * 255.0f))));
            uint _S380 = atlasWidth_2;
            uint _S381 = 4U % atlasWidth_2;
            int _S382 = int(_S381);
            uint _S383 = 4U / _S380;
            int3 _S384 = int3(_S382, int(_S383), int(0));
            uint _S385 = uint(round((((&kernelContext_2)->normalTexture_0).read(vec<uint,2>(((_S384)).xy), uint(((_S384)).z))).x * 255.0f));
            uint _S386 = atlasWidth_2;
            uint _S387 = 5U % atlasWidth_2;
            int _S388 = int(_S387);
            uint _S389 = 5U / _S386;
            int3 _S390 = int3(_S388, int(_S389), int(0));
            uint _S391 = uint(round((((&kernelContext_2)->normalTexture_0).read(vec<uint,2>(((_S390)).xy), uint(((_S390)).z))).x * 255.0f));
            uint2 grid_2 = uint2(_S385, _S391);
            int2 tile_4 = int2(floor(_S212)) - minimumTile_2;
            if(any(tile_4 < (int2(int(0)) )))
            {
                hasSceneLighting_0 = true;
            }
            else
            {
                hasSceneLighting_0 = any(tile_4 >= int2(grid_2));
            }
            if(hasSceneLighting_0)
            {
                int3 _S392 = int3(int(1), int(0), int(0));
                _S213 = (((&kernelContext_2)->normalTexture_0).read(vec<uint,2>(((_S392)).xy), uint(((_S392)).z)));
                break;
            }
            uint offset_2 = 10U + 2U * (uint(tile_4.y) * _S385 + uint(tile_4.x));
            uint _S393 = atlasWidth_2;
            uint _S394 = offset_2 % atlasWidth_2;
            int _S395 = int(_S394);
            uint _S396 = offset_2 / _S393;
            int3 _S397 = int3(_S395, int(_S396), int(0));
            uint _S398 = uint(round((((&kernelContext_2)->normalTexture_0).read(vec<uint,2>(((_S397)).xy), uint(((_S397)).z))).x * 255.0f));
            uint _S399 = atlasWidth_2;
            uint _S400 = offset_2 + 1U;
            uint _S401 = _S400 % atlasWidth_2;
            int _S402 = int(_S401);
            uint _S403 = _S400 / _S399;
            int3 _S404 = int3(_S402, int(_S403), int(0));
            uint slot_4 = _S398 | (uint(round((((&kernelContext_2)->normalTexture_0).read(vec<uint,2>(((_S404)).xy), uint(((_S404)).z))).x * 255.0f)) << 8U);
            if(slot_4 == 0U)
            {
                int3 _S405 = int3(int(1), int(0), int(0));
                _S213 = (((&kernelContext_2)->normalTexture_0).read(vec<uint,2>(((_S405)).xy), uint(((_S405)).z)));
                break;
            }
            uint slot_5 = slot_4 - 1U;
            uint _S406 = atlasWidth_2;
            uint _S407 = 6U % atlasWidth_2;
            int _S408 = int(_S407);
            uint _S409 = 6U / _S406;
            int3 _S410 = int3(_S408, int(_S409), int(0));
            uint _S411 = uint(round((((&kernelContext_2)->normalTexture_0).read(vec<uint,2>(((_S410)).xy), uint(((_S410)).z))).x * 255.0f));
            uint _S412 = atlasWidth_2;
            uint _S413 = 7U % atlasWidth_2;
            int _S414 = int(_S413);
            uint _S415 = 7U / _S412;
            int3 _S416 = int3(_S414, int(_S415), int(0));
            uint columns_2 = _S411 | (uint(round((((&kernelContext_2)->normalTexture_0).read(vec<uint,2>(((_S416)).xy), uint(((_S416)).z))).x * 255.0f)) << 8U);
            uint _S417 = atlasWidth_2;
            uint _S418 = 8U % atlasWidth_2;
            int _S419 = int(_S418);
            uint _S420 = 8U / _S417;
            int3 _S421 = int3(_S419, int(_S420), int(0));
            uint _S422 = uint(round((((&kernelContext_2)->normalTexture_0).read(vec<uint,2>(((_S421)).xy), uint(((_S421)).z))).x * 255.0f));
            uint _S423 = atlasWidth_2;
            uint _S424 = 9U % atlasWidth_2;
            int _S425 = int(_S424);
            uint _S426 = 9U / _S423;
            int3 _S427 = int3(_S425, int(_S426), int(0));
            uint rows_2 = ((_S422 | (uint(round((((&kernelContext_2)->normalTexture_0).read(vec<uint,2>(((_S427)).xy), uint(((_S427)).z))).x * 255.0f)) << 8U)) + columns_2 - 1U) / columns_2;
            uint metadataRows_2 = (10U + 2U * _S385 * _S391 + atlasWidth_2 - 1U) / atlasWidth_2;
            uint _S428 = atlasWidth_2 / columns_2;
            float _S429 = float(_S428);
            uint _S430 = (atlasHeight_2 - metadataRows_2) / rows_2;
            float2 cellSize_4 = float2(_S429, float(_S430));
            uint _S431 = slot_5 % columns_2;
            float _S432 = float(_S431);
            uint _S433 = slot_5 / columns_2;
            _S213 = (((&kernelContext_2)->normalTexture_0).sample(((&kernelContext_2)->normalSampler_0), ((float2(_S432, float(_S433)) * cellSize_4 + float2(1.5f, float(metadataRows_2) + 1.5f) + fract(_S212) * max(cellSize_4 - float2(3.0f) , float2(0.0f) )) / float2(float(atlasWidth_2), float(atlasHeight_2)))));
            break;
        }
        int2 _S434 = int2(metadata_2.zw);
        int2 tile_5 = int2(floor(_S212)) - int2(metadata_2.xy);
        if(any(tile_5 < (int2(int(0)) )))
        {
            hasSceneLighting_0 = true;
        }
        else
        {
            hasSceneLighting_0 = any(tile_5 >= _S434);
        }
        if(hasSceneLighting_0)
        {
            int3 _S435 = int3(int(min(1U, atlasWidth_2 - 1U)), int(0), int(0));
            _S213 = (((&kernelContext_2)->normalTexture_0).read(vec<uint,2>(((_S435)).xy), uint(((_S435)).z)));
            break;
        }
        uint _S436 = atlasWidth_2 / uint(_S434.x);
        float _S437 = float(_S436);
        uint _S438 = (atlasHeight_2 - 1U) / uint(_S434.y);
        float2 cellSize_5 = float2(_S437, float(_S438));
        _S213 = (((&kernelContext_2)->normalTexture_0).sample(((&kernelContext_2)->normalSampler_0), ((float2(tile_5) * cellSize_5 + float2(1.5f, 2.5f) + fract(_S212) * max(cellSize_5 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_2), float(atlasHeight_2)))));
        break;
    }
    baseNormalSample_0 = _S213;
    float4 _S439 = float4(_S40->detailUvTransformRow0_0) ;
    if((_S439.w) != 0.0f)
    {
        float3 _S440 = DecodeIndexedRgb_0(baseNormalSample_0, (&kernelContext_2)->normalValuesTexture_0);
        baseNormalSample_0.xyz = _S440;
    }
    if((textureMask_0 & 4U) == 0U)
    {
        unlitColor_0 = float3(0.0f, 0.0f, 1.0f);
    }
    else
    {
        unlitColor_0 = baseNormalSample_0.xyz * float3(2.0f)  - float3(1.0f) ;
    }
    float3 sampledNormal_0 = OrientMappedNormal_0(unlitColor_0, _S209, _S211);
    if((textureMask_0 & 4096U) != 0U)
    {
        float2 _S441 = _S439.xy;
        float4 _S442 = float4(_S40->detailUvTransformRow1_0) ;
        float2 _S443 = _S442.xy;
        float2 detailUv_0 = float2(dot(_S441, _S35.texCoord_0) + _S439.z, dot(_S443, _S35.texCoord_0) + _S442.z);
        thread float4 detailSample_0;
        bool _S444 = (udimMask_0 & 4096U) != 0U;
        for(;;)
        {
            if(!_S444)
            {
                _S213 = (((&kernelContext_2)->detailNormalTexture_0).sample(((&kernelContext_2)->detailNormalSampler_0), (detailUv_0)));
                break;
            }
            texture2d<float, access::sample> _S445 = (&kernelContext_2)->detailNormalTexture_0;
            thread uint atlasWidth_3;
            thread uint atlasHeight_3;
            (*((&atlasWidth_3)) = (_S445).get_width(0)),(*((&atlasHeight_3)) = (_S445).get_height(0));
            int3 _S446 = int3(int(0), int(0), int(0));
            float4 metadata_3 = round((((&kernelContext_2)->detailNormalTexture_0).read(vec<uint,2>(((_S446)).xy), uint(((_S446)).z))) * float4(255.0f) );
            if((metadata_3.x) == 255.0f)
            {
                uint _S447 = atlasWidth_3;
                uint _S448 = 2U % atlasWidth_3;
                int _S449 = int(_S448);
                uint _S450 = 2U / _S447;
                int3 _S451 = int3(_S449, int(_S450), int(0));
                int _S452 = int(uint(round((((&kernelContext_2)->detailNormalTexture_0).read(vec<uint,2>(((_S451)).xy), uint(((_S451)).z))).x * 255.0f)));
                uint _S453 = atlasWidth_3;
                uint _S454 = 3U % atlasWidth_3;
                int _S455 = int(_S454);
                uint _S456 = 3U / _S453;
                int3 _S457 = int3(_S455, int(_S456), int(0));
                int2 minimumTile_3 = int2(_S452, int(uint(round((((&kernelContext_2)->detailNormalTexture_0).read(vec<uint,2>(((_S457)).xy), uint(((_S457)).z))).x * 255.0f))));
                uint _S458 = atlasWidth_3;
                uint _S459 = 4U % atlasWidth_3;
                int _S460 = int(_S459);
                uint _S461 = 4U / _S458;
                int3 _S462 = int3(_S460, int(_S461), int(0));
                uint _S463 = uint(round((((&kernelContext_2)->detailNormalTexture_0).read(vec<uint,2>(((_S462)).xy), uint(((_S462)).z))).x * 255.0f));
                uint _S464 = atlasWidth_3;
                uint _S465 = 5U % atlasWidth_3;
                int _S466 = int(_S465);
                uint _S467 = 5U / _S464;
                int3 _S468 = int3(_S466, int(_S467), int(0));
                uint _S469 = uint(round((((&kernelContext_2)->detailNormalTexture_0).read(vec<uint,2>(((_S468)).xy), uint(((_S468)).z))).x * 255.0f));
                uint2 grid_3 = uint2(_S463, _S469);
                int2 tile_6 = int2(floor(detailUv_0)) - minimumTile_3;
                if(any(tile_6 < (int2(int(0)) )))
                {
                    hasSceneLighting_0 = true;
                }
                else
                {
                    hasSceneLighting_0 = any(tile_6 >= int2(grid_3));
                }
                if(hasSceneLighting_0)
                {
                    int3 _S470 = int3(int(1), int(0), int(0));
                    _S213 = (((&kernelContext_2)->detailNormalTexture_0).read(vec<uint,2>(((_S470)).xy), uint(((_S470)).z)));
                    break;
                }
                uint offset_3 = 10U + 2U * (uint(tile_6.y) * _S463 + uint(tile_6.x));
                uint _S471 = atlasWidth_3;
                uint _S472 = offset_3 % atlasWidth_3;
                int _S473 = int(_S472);
                uint _S474 = offset_3 / _S471;
                int3 _S475 = int3(_S473, int(_S474), int(0));
                uint _S476 = uint(round((((&kernelContext_2)->detailNormalTexture_0).read(vec<uint,2>(((_S475)).xy), uint(((_S475)).z))).x * 255.0f));
                uint _S477 = atlasWidth_3;
                uint _S478 = offset_3 + 1U;
                uint _S479 = _S478 % atlasWidth_3;
                int _S480 = int(_S479);
                uint _S481 = _S478 / _S477;
                int3 _S482 = int3(_S480, int(_S481), int(0));
                uint slot_6 = _S476 | (uint(round((((&kernelContext_2)->detailNormalTexture_0).read(vec<uint,2>(((_S482)).xy), uint(((_S482)).z))).x * 255.0f)) << 8U);
                if(slot_6 == 0U)
                {
                    int3 _S483 = int3(int(1), int(0), int(0));
                    _S213 = (((&kernelContext_2)->detailNormalTexture_0).read(vec<uint,2>(((_S483)).xy), uint(((_S483)).z)));
                    break;
                }
                uint slot_7 = slot_6 - 1U;
                uint _S484 = atlasWidth_3;
                uint _S485 = 6U % atlasWidth_3;
                int _S486 = int(_S485);
                uint _S487 = 6U / _S484;
                int3 _S488 = int3(_S486, int(_S487), int(0));
                uint _S489 = uint(round((((&kernelContext_2)->detailNormalTexture_0).read(vec<uint,2>(((_S488)).xy), uint(((_S488)).z))).x * 255.0f));
                uint _S490 = atlasWidth_3;
                uint _S491 = 7U % atlasWidth_3;
                int _S492 = int(_S491);
                uint _S493 = 7U / _S490;
                int3 _S494 = int3(_S492, int(_S493), int(0));
                uint columns_3 = _S489 | (uint(round((((&kernelContext_2)->detailNormalTexture_0).read(vec<uint,2>(((_S494)).xy), uint(((_S494)).z))).x * 255.0f)) << 8U);
                uint _S495 = atlasWidth_3;
                uint _S496 = 8U % atlasWidth_3;
                int _S497 = int(_S496);
                uint _S498 = 8U / _S495;
                int3 _S499 = int3(_S497, int(_S498), int(0));
                uint _S500 = uint(round((((&kernelContext_2)->detailNormalTexture_0).read(vec<uint,2>(((_S499)).xy), uint(((_S499)).z))).x * 255.0f));
                uint _S501 = atlasWidth_3;
                uint _S502 = 9U % atlasWidth_3;
                int _S503 = int(_S502);
                uint _S504 = 9U / _S501;
                int3 _S505 = int3(_S503, int(_S504), int(0));
                uint rows_3 = ((_S500 | (uint(round((((&kernelContext_2)->detailNormalTexture_0).read(vec<uint,2>(((_S505)).xy), uint(((_S505)).z))).x * 255.0f)) << 8U)) + columns_3 - 1U) / columns_3;
                uint metadataRows_3 = (10U + 2U * _S463 * _S469 + atlasWidth_3 - 1U) / atlasWidth_3;
                uint _S506 = atlasWidth_3 / columns_3;
                float _S507 = float(_S506);
                uint _S508 = (atlasHeight_3 - metadataRows_3) / rows_3;
                float2 cellSize_6 = float2(_S507, float(_S508));
                uint _S509 = slot_7 % columns_3;
                float _S510 = float(_S509);
                uint _S511 = slot_7 / columns_3;
                _S213 = (((&kernelContext_2)->detailNormalTexture_0).sample(((&kernelContext_2)->detailNormalSampler_0), ((float2(_S510, float(_S511)) * cellSize_6 + float2(1.5f, float(metadataRows_3) + 1.5f) + fract(detailUv_0) * max(cellSize_6 - float2(3.0f) , float2(0.0f) )) / float2(float(atlasWidth_3), float(atlasHeight_3)))));
                break;
            }
            int2 _S512 = int2(metadata_3.zw);
            int2 tile_7 = int2(floor(detailUv_0)) - int2(metadata_3.xy);
            if(any(tile_7 < (int2(int(0)) )))
            {
                hasSceneLighting_0 = true;
            }
            else
            {
                hasSceneLighting_0 = any(tile_7 >= _S512);
            }
            if(hasSceneLighting_0)
            {
                int3 _S513 = int3(int(min(1U, atlasWidth_3 - 1U)), int(0), int(0));
                _S213 = (((&kernelContext_2)->detailNormalTexture_0).read(vec<uint,2>(((_S513)).xy), uint(((_S513)).z)));
                break;
            }
            uint _S514 = atlasWidth_3 / uint(_S512.x);
            float _S515 = float(_S514);
            uint _S516 = (atlasHeight_3 - 1U) / uint(_S512.y);
            float2 cellSize_7 = float2(_S515, float(_S516));
            _S213 = (((&kernelContext_2)->detailNormalTexture_0).sample(((&kernelContext_2)->detailNormalSampler_0), ((float2(tile_7) * cellSize_7 + float2(1.5f, 2.5f) + fract(detailUv_0) * max(cellSize_7 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_3), float(atlasHeight_3)))));
            break;
        }
        detailSample_0 = _S213;
        if((_S442.w) != 0.0f)
        {
            float3 _S517 = DecodeIndexedRgb_0(detailSample_0, (&kernelContext_2)->detailNormalValuesTexture_0);
            detailSample_0.xyz = _S517;
        }
        unlitColor_0 = CombineDetailNormal_0(sampledNormal_0, OrientMappedNormal_0(detailSample_0.xyz * float3(2.0f)  - float3(1.0f) , _S441, _S443));
    }
    else
    {
        unlitColor_0 = sampledNormal_0;
    }
    float3 tangent_3 = normalize(_S35.tangent_2.xyz);
    float3 _S518 = float3(unlitColor_0.x) ;
    float3 _S519 = float3(unlitColor_0.y) ;
    float3 _S520 = float3(unlitColor_0.z) ;
    float3 shadingNormal_0 = normalize(tangent_3 * _S518 + cross(normalize(_S35.normal_0), tangent_3) * float3(_S35.tangent_2.w)  * _S519 + _S35.normal_0 * _S520);
    float3 worldNormalBasis_0 = normalize(_S35.worldNormal_0);
    float3 worldTangentBasis_0 = normalize(_S35.worldTangent_0.xyz);
    float3 worldTangentBasis_1 = normalize(worldTangentBasis_0 - worldNormalBasis_0 * float3(dot(worldNormalBasis_0, worldTangentBasis_0)) );
    float3 worldShadingNormal_0 = normalize(worldTangentBasis_1 * _S518 + cross(worldNormalBasis_0, worldTangentBasis_1) * float3(_S35.worldTangent_0.w)  * _S519 + worldNormalBasis_0 * _S520);
    float roughness_0;
    float4 _S521;
    if((textureMask_0 & 8U) != 0U)
    {
        bool _S522 = (udimMask_0 & 8U) != 0U;
        for(;;)
        {
            if(!_S522)
            {
                _S213 = (((&kernelContext_2)->roughnessMetallicTexture_0).sample(((&kernelContext_2)->roughnessMetallicSampler_0), (_S212)));
                break;
            }
            texture2d<float, access::sample> _S523 = (&kernelContext_2)->roughnessMetallicTexture_0;
            thread uint atlasWidth_4;
            thread uint atlasHeight_4;
            (*((&atlasWidth_4)) = (_S523).get_width(0)),(*((&atlasHeight_4)) = (_S523).get_height(0));
            int3 _S524 = int3(int(0), int(0), int(0));
            float4 metadata_4 = round((((&kernelContext_2)->roughnessMetallicTexture_0).read(vec<uint,2>(((_S524)).xy), uint(((_S524)).z))) * float4(255.0f) );
            if((metadata_4.x) == 255.0f)
            {
                uint _S525 = atlasWidth_4;
                uint _S526 = 2U % atlasWidth_4;
                int _S527 = int(_S526);
                uint _S528 = 2U / _S525;
                int3 _S529 = int3(_S527, int(_S528), int(0));
                int _S530 = int(uint(round((((&kernelContext_2)->roughnessMetallicTexture_0).read(vec<uint,2>(((_S529)).xy), uint(((_S529)).z))).x * 255.0f)));
                uint _S531 = atlasWidth_4;
                uint _S532 = 3U % atlasWidth_4;
                int _S533 = int(_S532);
                uint _S534 = 3U / _S531;
                int3 _S535 = int3(_S533, int(_S534), int(0));
                int2 minimumTile_4 = int2(_S530, int(uint(round((((&kernelContext_2)->roughnessMetallicTexture_0).read(vec<uint,2>(((_S535)).xy), uint(((_S535)).z))).x * 255.0f))));
                uint _S536 = atlasWidth_4;
                uint _S537 = 4U % atlasWidth_4;
                int _S538 = int(_S537);
                uint _S539 = 4U / _S536;
                int3 _S540 = int3(_S538, int(_S539), int(0));
                uint _S541 = uint(round((((&kernelContext_2)->roughnessMetallicTexture_0).read(vec<uint,2>(((_S540)).xy), uint(((_S540)).z))).x * 255.0f));
                uint _S542 = atlasWidth_4;
                uint _S543 = 5U % atlasWidth_4;
                int _S544 = int(_S543);
                uint _S545 = 5U / _S542;
                int3 _S546 = int3(_S544, int(_S545), int(0));
                uint _S547 = uint(round((((&kernelContext_2)->roughnessMetallicTexture_0).read(vec<uint,2>(((_S546)).xy), uint(((_S546)).z))).x * 255.0f));
                uint2 grid_4 = uint2(_S541, _S547);
                int2 tile_8 = int2(floor(_S212)) - minimumTile_4;
                if(any(tile_8 < (int2(int(0)) )))
                {
                    hasSceneLighting_0 = true;
                }
                else
                {
                    hasSceneLighting_0 = any(tile_8 >= int2(grid_4));
                }
                if(hasSceneLighting_0)
                {
                    int3 _S548 = int3(int(1), int(0), int(0));
                    _S213 = (((&kernelContext_2)->roughnessMetallicTexture_0).read(vec<uint,2>(((_S548)).xy), uint(((_S548)).z)));
                    break;
                }
                uint offset_4 = 10U + 2U * (uint(tile_8.y) * _S541 + uint(tile_8.x));
                uint _S549 = atlasWidth_4;
                uint _S550 = offset_4 % atlasWidth_4;
                int _S551 = int(_S550);
                uint _S552 = offset_4 / _S549;
                int3 _S553 = int3(_S551, int(_S552), int(0));
                uint _S554 = uint(round((((&kernelContext_2)->roughnessMetallicTexture_0).read(vec<uint,2>(((_S553)).xy), uint(((_S553)).z))).x * 255.0f));
                uint _S555 = atlasWidth_4;
                uint _S556 = offset_4 + 1U;
                uint _S557 = _S556 % atlasWidth_4;
                int _S558 = int(_S557);
                uint _S559 = _S556 / _S555;
                int3 _S560 = int3(_S558, int(_S559), int(0));
                uint slot_8 = _S554 | (uint(round((((&kernelContext_2)->roughnessMetallicTexture_0).read(vec<uint,2>(((_S560)).xy), uint(((_S560)).z))).x * 255.0f)) << 8U);
                if(slot_8 == 0U)
                {
                    int3 _S561 = int3(int(1), int(0), int(0));
                    _S213 = (((&kernelContext_2)->roughnessMetallicTexture_0).read(vec<uint,2>(((_S561)).xy), uint(((_S561)).z)));
                    break;
                }
                uint slot_9 = slot_8 - 1U;
                uint _S562 = atlasWidth_4;
                uint _S563 = 6U % atlasWidth_4;
                int _S564 = int(_S563);
                uint _S565 = 6U / _S562;
                int3 _S566 = int3(_S564, int(_S565), int(0));
                uint _S567 = uint(round((((&kernelContext_2)->roughnessMetallicTexture_0).read(vec<uint,2>(((_S566)).xy), uint(((_S566)).z))).x * 255.0f));
                uint _S568 = atlasWidth_4;
                uint _S569 = 7U % atlasWidth_4;
                int _S570 = int(_S569);
                uint _S571 = 7U / _S568;
                int3 _S572 = int3(_S570, int(_S571), int(0));
                uint columns_4 = _S567 | (uint(round((((&kernelContext_2)->roughnessMetallicTexture_0).read(vec<uint,2>(((_S572)).xy), uint(((_S572)).z))).x * 255.0f)) << 8U);
                uint _S573 = atlasWidth_4;
                uint _S574 = 8U % atlasWidth_4;
                int _S575 = int(_S574);
                uint _S576 = 8U / _S573;
                int3 _S577 = int3(_S575, int(_S576), int(0));
                uint _S578 = uint(round((((&kernelContext_2)->roughnessMetallicTexture_0).read(vec<uint,2>(((_S577)).xy), uint(((_S577)).z))).x * 255.0f));
                uint _S579 = atlasWidth_4;
                uint _S580 = 9U % atlasWidth_4;
                int _S581 = int(_S580);
                uint _S582 = 9U / _S579;
                int3 _S583 = int3(_S581, int(_S582), int(0));
                uint rows_4 = ((_S578 | (uint(round((((&kernelContext_2)->roughnessMetallicTexture_0).read(vec<uint,2>(((_S583)).xy), uint(((_S583)).z))).x * 255.0f)) << 8U)) + columns_4 - 1U) / columns_4;
                uint metadataRows_4 = (10U + 2U * _S541 * _S547 + atlasWidth_4 - 1U) / atlasWidth_4;
                uint _S584 = atlasWidth_4 / columns_4;
                float _S585 = float(_S584);
                uint _S586 = (atlasHeight_4 - metadataRows_4) / rows_4;
                float2 cellSize_8 = float2(_S585, float(_S586));
                uint _S587 = slot_9 % columns_4;
                float _S588 = float(_S587);
                uint _S589 = slot_9 / columns_4;
                _S213 = (((&kernelContext_2)->roughnessMetallicTexture_0).sample(((&kernelContext_2)->roughnessMetallicSampler_0), ((float2(_S588, float(_S589)) * cellSize_8 + float2(1.5f, float(metadataRows_4) + 1.5f) + fract(_S212) * max(cellSize_8 - float2(3.0f) , float2(0.0f) )) / float2(float(atlasWidth_4), float(atlasHeight_4)))));
                break;
            }
            int2 _S590 = int2(metadata_4.zw);
            int2 tile_9 = int2(floor(_S212)) - int2(metadata_4.xy);
            if(any(tile_9 < (int2(int(0)) )))
            {
                hasSceneLighting_0 = true;
            }
            else
            {
                hasSceneLighting_0 = any(tile_9 >= _S590);
            }
            if(hasSceneLighting_0)
            {
                int3 _S591 = int3(int(min(1U, atlasWidth_4 - 1U)), int(0), int(0));
                _S213 = (((&kernelContext_2)->roughnessMetallicTexture_0).read(vec<uint,2>(((_S591)).xy), uint(((_S591)).z)));
                break;
            }
            uint _S592 = atlasWidth_4 / uint(_S590.x);
            float _S593 = float(_S592);
            uint _S594 = (atlasHeight_4 - 1U) / uint(_S590.y);
            float2 cellSize_9 = float2(_S593, float(_S594));
            _S213 = (((&kernelContext_2)->roughnessMetallicTexture_0).sample(((&kernelContext_2)->roughnessMetallicSampler_0), ((float2(tile_9) * cellSize_9 + float2(1.5f, 2.5f) + fract(_S212) * max(cellSize_9 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_4), float(atlasHeight_4)))));
            break;
        }
        for(;;)
        {
            float4 _S595 = float4(_S40->compositeControls_0) ;
            if((_S595.x) != 8.0f)
            {
                break;
            }
            bool _S596 = (_S595.w) >= 0.5f;
            for(;;)
            {
                if(!_S596)
                {
                    _S521 = (((&kernelContext_2)->compositeTexture_0).sample(((&kernelContext_2)->compositeSampler_0), (_S212)));
                    break;
                }
                texture2d<float, access::sample> _S597 = (&kernelContext_2)->compositeTexture_0;
                thread uint atlasWidth_5;
                thread uint atlasHeight_5;
                (*((&atlasWidth_5)) = (_S597).get_width(0)),(*((&atlasHeight_5)) = (_S597).get_height(0));
                int3 _S598 = int3(int(0), int(0), int(0));
                float4 metadata_5 = round((((&kernelContext_2)->compositeTexture_0).read(vec<uint,2>(((_S598)).xy), uint(((_S598)).z))) * float4(255.0f) );
                if((metadata_5.x) == 255.0f)
                {
                    uint _S599 = atlasWidth_5;
                    uint _S600 = 2U % atlasWidth_5;
                    int _S601 = int(_S600);
                    uint _S602 = 2U / _S599;
                    int3 _S603 = int3(_S601, int(_S602), int(0));
                    int _S604 = int(uint(round((((&kernelContext_2)->compositeTexture_0).read(vec<uint,2>(((_S603)).xy), uint(((_S603)).z))).x * 255.0f)));
                    uint _S605 = atlasWidth_5;
                    uint _S606 = 3U % atlasWidth_5;
                    int _S607 = int(_S606);
                    uint _S608 = 3U / _S605;
                    int3 _S609 = int3(_S607, int(_S608), int(0));
                    int2 minimumTile_5 = int2(_S604, int(uint(round((((&kernelContext_2)->compositeTexture_0).read(vec<uint,2>(((_S609)).xy), uint(((_S609)).z))).x * 255.0f))));
                    uint _S610 = atlasWidth_5;
                    uint _S611 = 4U % atlasWidth_5;
                    int _S612 = int(_S611);
                    uint _S613 = 4U / _S610;
                    int3 _S614 = int3(_S612, int(_S613), int(0));
                    uint _S615 = uint(round((((&kernelContext_2)->compositeTexture_0).read(vec<uint,2>(((_S614)).xy), uint(((_S614)).z))).x * 255.0f));
                    uint _S616 = atlasWidth_5;
                    uint _S617 = 5U % atlasWidth_5;
                    int _S618 = int(_S617);
                    uint _S619 = 5U / _S616;
                    int3 _S620 = int3(_S618, int(_S619), int(0));
                    uint _S621 = uint(round((((&kernelContext_2)->compositeTexture_0).read(vec<uint,2>(((_S620)).xy), uint(((_S620)).z))).x * 255.0f));
                    uint2 grid_5 = uint2(_S615, _S621);
                    int2 tile_10 = int2(floor(_S212)) - minimumTile_5;
                    if(any(tile_10 < (int2(int(0)) )))
                    {
                        hasSceneLighting_0 = true;
                    }
                    else
                    {
                        hasSceneLighting_0 = any(tile_10 >= int2(grid_5));
                    }
                    if(hasSceneLighting_0)
                    {
                        int3 _S622 = int3(int(1), int(0), int(0));
                        _S521 = (((&kernelContext_2)->compositeTexture_0).read(vec<uint,2>(((_S622)).xy), uint(((_S622)).z)));
                        break;
                    }
                    uint offset_5 = 10U + 2U * (uint(tile_10.y) * _S615 + uint(tile_10.x));
                    uint _S623 = atlasWidth_5;
                    uint _S624 = offset_5 % atlasWidth_5;
                    int _S625 = int(_S624);
                    uint _S626 = offset_5 / _S623;
                    int3 _S627 = int3(_S625, int(_S626), int(0));
                    uint _S628 = uint(round((((&kernelContext_2)->compositeTexture_0).read(vec<uint,2>(((_S627)).xy), uint(((_S627)).z))).x * 255.0f));
                    uint _S629 = atlasWidth_5;
                    uint _S630 = offset_5 + 1U;
                    uint _S631 = _S630 % atlasWidth_5;
                    int _S632 = int(_S631);
                    uint _S633 = _S630 / _S629;
                    int3 _S634 = int3(_S632, int(_S633), int(0));
                    uint slot_10 = _S628 | (uint(round((((&kernelContext_2)->compositeTexture_0).read(vec<uint,2>(((_S634)).xy), uint(((_S634)).z))).x * 255.0f)) << 8U);
                    if(slot_10 == 0U)
                    {
                        int3 _S635 = int3(int(1), int(0), int(0));
                        _S521 = (((&kernelContext_2)->compositeTexture_0).read(vec<uint,2>(((_S635)).xy), uint(((_S635)).z)));
                        break;
                    }
                    uint slot_11 = slot_10 - 1U;
                    uint _S636 = atlasWidth_5;
                    uint _S637 = 6U % atlasWidth_5;
                    int _S638 = int(_S637);
                    uint _S639 = 6U / _S636;
                    int3 _S640 = int3(_S638, int(_S639), int(0));
                    uint _S641 = uint(round((((&kernelContext_2)->compositeTexture_0).read(vec<uint,2>(((_S640)).xy), uint(((_S640)).z))).x * 255.0f));
                    uint _S642 = atlasWidth_5;
                    uint _S643 = 7U % atlasWidth_5;
                    int _S644 = int(_S643);
                    uint _S645 = 7U / _S642;
                    int3 _S646 = int3(_S644, int(_S645), int(0));
                    uint columns_5 = _S641 | (uint(round((((&kernelContext_2)->compositeTexture_0).read(vec<uint,2>(((_S646)).xy), uint(((_S646)).z))).x * 255.0f)) << 8U);
                    uint _S647 = atlasWidth_5;
                    uint _S648 = 8U % atlasWidth_5;
                    int _S649 = int(_S648);
                    uint _S650 = 8U / _S647;
                    int3 _S651 = int3(_S649, int(_S650), int(0));
                    uint _S652 = uint(round((((&kernelContext_2)->compositeTexture_0).read(vec<uint,2>(((_S651)).xy), uint(((_S651)).z))).x * 255.0f));
                    uint _S653 = atlasWidth_5;
                    uint _S654 = 9U % atlasWidth_5;
                    int _S655 = int(_S654);
                    uint _S656 = 9U / _S653;
                    int3 _S657 = int3(_S655, int(_S656), int(0));
                    uint rows_5 = ((_S652 | (uint(round((((&kernelContext_2)->compositeTexture_0).read(vec<uint,2>(((_S657)).xy), uint(((_S657)).z))).x * 255.0f)) << 8U)) + columns_5 - 1U) / columns_5;
                    uint metadataRows_5 = (10U + 2U * _S615 * _S621 + atlasWidth_5 - 1U) / atlasWidth_5;
                    uint _S658 = atlasWidth_5 / columns_5;
                    float _S659 = float(_S658);
                    uint _S660 = (atlasHeight_5 - metadataRows_5) / rows_5;
                    float2 cellSize_10 = float2(_S659, float(_S660));
                    uint _S661 = slot_11 % columns_5;
                    float _S662 = float(_S661);
                    uint _S663 = slot_11 / columns_5;
                    _S521 = (((&kernelContext_2)->compositeTexture_0).sample(((&kernelContext_2)->compositeSampler_0), ((float2(_S662, float(_S663)) * cellSize_10 + float2(1.5f, float(metadataRows_5) + 1.5f) + fract(_S212) * max(cellSize_10 - float2(3.0f) , float2(0.0f) )) / float2(float(atlasWidth_5), float(atlasHeight_5)))));
                    break;
                }
                int2 _S664 = int2(metadata_5.zw);
                int2 tile_11 = int2(floor(_S212)) - int2(metadata_5.xy);
                if(any(tile_11 < (int2(int(0)) )))
                {
                    hasSceneLighting_0 = true;
                }
                else
                {
                    hasSceneLighting_0 = any(tile_11 >= _S664);
                }
                if(hasSceneLighting_0)
                {
                    int3 _S665 = int3(int(min(1U, atlasWidth_5 - 1U)), int(0), int(0));
                    _S521 = (((&kernelContext_2)->compositeTexture_0).read(vec<uint,2>(((_S665)).xy), uint(((_S665)).z)));
                    break;
                }
                uint _S666 = atlasWidth_5 / uint(_S664.x);
                float _S667 = float(_S666);
                uint _S668 = (atlasHeight_5 - 1U) / uint(_S664.y);
                float2 cellSize_11 = float2(_S667, float(_S668));
                _S521 = (((&kernelContext_2)->compositeTexture_0).sample(((&kernelContext_2)->compositeSampler_0), ((float2(tile_11) * cellSize_11 + float2(1.5f, 2.5f) + fract(_S212) * max(cellSize_11 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_5), float(atlasHeight_5)))));
                break;
            }
            uint operation_1 = uint(round(_S595.y));
            if(operation_1 == 1U)
            {
                _S213 = _S213 * _S521;
                break;
            }
            if(operation_1 == 2U)
            {
                _S213 = _S213 + _S521;
                break;
            }
            if(operation_1 == 3U)
            {
                _S213 = _S213 - _S521;
                break;
            }
            if(operation_1 == 4U)
            {
                float factor_1 = _S595.z;
                _S213 = _S213 * float4((1.0f - factor_1))  + _S521 * float4(factor_1) ;
                break;
            }
            break;
        }
        roughness_0 = clamp(_S213.x, 0.00999999977648258f, 1.0f);
    }
    else
    {
        roughness_0 = _S201;
    }
    float metallic_0;
    if((textureMask_0 & 32U) != 0U)
    {
        bool _S669 = (udimMask_0 & 32U) != 0U;
        for(;;)
        {
            if(!_S669)
            {
                _S213 = (((&kernelContext_2)->metallicTexture_0).sample(((&kernelContext_2)->metallicSampler_0), (_S212)));
                break;
            }
            texture2d<float, access::sample> _S670 = (&kernelContext_2)->metallicTexture_0;
            thread uint atlasWidth_6;
            thread uint atlasHeight_6;
            (*((&atlasWidth_6)) = (_S670).get_width(0)),(*((&atlasHeight_6)) = (_S670).get_height(0));
            int3 _S671 = int3(int(0), int(0), int(0));
            float4 metadata_6 = round((((&kernelContext_2)->metallicTexture_0).read(vec<uint,2>(((_S671)).xy), uint(((_S671)).z))) * float4(255.0f) );
            if((metadata_6.x) == 255.0f)
            {
                uint _S672 = atlasWidth_6;
                uint _S673 = 2U % atlasWidth_6;
                int _S674 = int(_S673);
                uint _S675 = 2U / _S672;
                int3 _S676 = int3(_S674, int(_S675), int(0));
                int _S677 = int(uint(round((((&kernelContext_2)->metallicTexture_0).read(vec<uint,2>(((_S676)).xy), uint(((_S676)).z))).x * 255.0f)));
                uint _S678 = atlasWidth_6;
                uint _S679 = 3U % atlasWidth_6;
                int _S680 = int(_S679);
                uint _S681 = 3U / _S678;
                int3 _S682 = int3(_S680, int(_S681), int(0));
                int2 minimumTile_6 = int2(_S677, int(uint(round((((&kernelContext_2)->metallicTexture_0).read(vec<uint,2>(((_S682)).xy), uint(((_S682)).z))).x * 255.0f))));
                uint _S683 = atlasWidth_6;
                uint _S684 = 4U % atlasWidth_6;
                int _S685 = int(_S684);
                uint _S686 = 4U / _S683;
                int3 _S687 = int3(_S685, int(_S686), int(0));
                uint _S688 = uint(round((((&kernelContext_2)->metallicTexture_0).read(vec<uint,2>(((_S687)).xy), uint(((_S687)).z))).x * 255.0f));
                uint _S689 = atlasWidth_6;
                uint _S690 = 5U % atlasWidth_6;
                int _S691 = int(_S690);
                uint _S692 = 5U / _S689;
                int3 _S693 = int3(_S691, int(_S692), int(0));
                uint _S694 = uint(round((((&kernelContext_2)->metallicTexture_0).read(vec<uint,2>(((_S693)).xy), uint(((_S693)).z))).x * 255.0f));
                uint2 grid_6 = uint2(_S688, _S694);
                int2 tile_12 = int2(floor(_S212)) - minimumTile_6;
                if(any(tile_12 < (int2(int(0)) )))
                {
                    hasSceneLighting_0 = true;
                }
                else
                {
                    hasSceneLighting_0 = any(tile_12 >= int2(grid_6));
                }
                if(hasSceneLighting_0)
                {
                    int3 _S695 = int3(int(1), int(0), int(0));
                    _S213 = (((&kernelContext_2)->metallicTexture_0).read(vec<uint,2>(((_S695)).xy), uint(((_S695)).z)));
                    break;
                }
                uint offset_6 = 10U + 2U * (uint(tile_12.y) * _S688 + uint(tile_12.x));
                uint _S696 = atlasWidth_6;
                uint _S697 = offset_6 % atlasWidth_6;
                int _S698 = int(_S697);
                uint _S699 = offset_6 / _S696;
                int3 _S700 = int3(_S698, int(_S699), int(0));
                uint _S701 = uint(round((((&kernelContext_2)->metallicTexture_0).read(vec<uint,2>(((_S700)).xy), uint(((_S700)).z))).x * 255.0f));
                uint _S702 = atlasWidth_6;
                uint _S703 = offset_6 + 1U;
                uint _S704 = _S703 % atlasWidth_6;
                int _S705 = int(_S704);
                uint _S706 = _S703 / _S702;
                int3 _S707 = int3(_S705, int(_S706), int(0));
                uint slot_12 = _S701 | (uint(round((((&kernelContext_2)->metallicTexture_0).read(vec<uint,2>(((_S707)).xy), uint(((_S707)).z))).x * 255.0f)) << 8U);
                if(slot_12 == 0U)
                {
                    int3 _S708 = int3(int(1), int(0), int(0));
                    _S213 = (((&kernelContext_2)->metallicTexture_0).read(vec<uint,2>(((_S708)).xy), uint(((_S708)).z)));
                    break;
                }
                uint slot_13 = slot_12 - 1U;
                uint _S709 = atlasWidth_6;
                uint _S710 = 6U % atlasWidth_6;
                int _S711 = int(_S710);
                uint _S712 = 6U / _S709;
                int3 _S713 = int3(_S711, int(_S712), int(0));
                uint _S714 = uint(round((((&kernelContext_2)->metallicTexture_0).read(vec<uint,2>(((_S713)).xy), uint(((_S713)).z))).x * 255.0f));
                uint _S715 = atlasWidth_6;
                uint _S716 = 7U % atlasWidth_6;
                int _S717 = int(_S716);
                uint _S718 = 7U / _S715;
                int3 _S719 = int3(_S717, int(_S718), int(0));
                uint columns_6 = _S714 | (uint(round((((&kernelContext_2)->metallicTexture_0).read(vec<uint,2>(((_S719)).xy), uint(((_S719)).z))).x * 255.0f)) << 8U);
                uint _S720 = atlasWidth_6;
                uint _S721 = 8U % atlasWidth_6;
                int _S722 = int(_S721);
                uint _S723 = 8U / _S720;
                int3 _S724 = int3(_S722, int(_S723), int(0));
                uint _S725 = uint(round((((&kernelContext_2)->metallicTexture_0).read(vec<uint,2>(((_S724)).xy), uint(((_S724)).z))).x * 255.0f));
                uint _S726 = atlasWidth_6;
                uint _S727 = 9U % atlasWidth_6;
                int _S728 = int(_S727);
                uint _S729 = 9U / _S726;
                int3 _S730 = int3(_S728, int(_S729), int(0));
                uint rows_6 = ((_S725 | (uint(round((((&kernelContext_2)->metallicTexture_0).read(vec<uint,2>(((_S730)).xy), uint(((_S730)).z))).x * 255.0f)) << 8U)) + columns_6 - 1U) / columns_6;
                uint metadataRows_6 = (10U + 2U * _S688 * _S694 + atlasWidth_6 - 1U) / atlasWidth_6;
                uint _S731 = atlasWidth_6 / columns_6;
                float _S732 = float(_S731);
                uint _S733 = (atlasHeight_6 - metadataRows_6) / rows_6;
                float2 cellSize_12 = float2(_S732, float(_S733));
                uint _S734 = slot_13 % columns_6;
                float _S735 = float(_S734);
                uint _S736 = slot_13 / columns_6;
                _S213 = (((&kernelContext_2)->metallicTexture_0).sample(((&kernelContext_2)->metallicSampler_0), ((float2(_S735, float(_S736)) * cellSize_12 + float2(1.5f, float(metadataRows_6) + 1.5f) + fract(_S212) * max(cellSize_12 - float2(3.0f) , float2(0.0f) )) / float2(float(atlasWidth_6), float(atlasHeight_6)))));
                break;
            }
            int2 _S737 = int2(metadata_6.zw);
            int2 tile_13 = int2(floor(_S212)) - int2(metadata_6.xy);
            if(any(tile_13 < (int2(int(0)) )))
            {
                hasSceneLighting_0 = true;
            }
            else
            {
                hasSceneLighting_0 = any(tile_13 >= _S737);
            }
            if(hasSceneLighting_0)
            {
                int3 _S738 = int3(int(min(1U, atlasWidth_6 - 1U)), int(0), int(0));
                _S213 = (((&kernelContext_2)->metallicTexture_0).read(vec<uint,2>(((_S738)).xy), uint(((_S738)).z)));
                break;
            }
            uint _S739 = atlasWidth_6 / uint(_S737.x);
            float _S740 = float(_S739);
            uint _S741 = (atlasHeight_6 - 1U) / uint(_S737.y);
            float2 cellSize_13 = float2(_S740, float(_S741));
            _S213 = (((&kernelContext_2)->metallicTexture_0).sample(((&kernelContext_2)->metallicSampler_0), ((float2(tile_13) * cellSize_13 + float2(1.5f, 2.5f) + fract(_S212) * max(cellSize_13 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_6), float(atlasHeight_6)))));
            break;
        }
        for(;;)
        {
            float4 _S742 = float4(_S40->compositeControls_0) ;
            if((_S742.x) != 32.0f)
            {
                break;
            }
            bool _S743 = (_S742.w) >= 0.5f;
            for(;;)
            {
                if(!_S743)
                {
                    _S521 = (((&kernelContext_2)->compositeTexture_0).sample(((&kernelContext_2)->compositeSampler_0), (_S212)));
                    break;
                }
                texture2d<float, access::sample> _S744 = (&kernelContext_2)->compositeTexture_0;
                thread uint atlasWidth_7;
                thread uint atlasHeight_7;
                (*((&atlasWidth_7)) = (_S744).get_width(0)),(*((&atlasHeight_7)) = (_S744).get_height(0));
                int3 _S745 = int3(int(0), int(0), int(0));
                float4 metadata_7 = round((((&kernelContext_2)->compositeTexture_0).read(vec<uint,2>(((_S745)).xy), uint(((_S745)).z))) * float4(255.0f) );
                if((metadata_7.x) == 255.0f)
                {
                    uint _S746 = atlasWidth_7;
                    uint _S747 = 2U % atlasWidth_7;
                    int _S748 = int(_S747);
                    uint _S749 = 2U / _S746;
                    int3 _S750 = int3(_S748, int(_S749), int(0));
                    int _S751 = int(uint(round((((&kernelContext_2)->compositeTexture_0).read(vec<uint,2>(((_S750)).xy), uint(((_S750)).z))).x * 255.0f)));
                    uint _S752 = atlasWidth_7;
                    uint _S753 = 3U % atlasWidth_7;
                    int _S754 = int(_S753);
                    uint _S755 = 3U / _S752;
                    int3 _S756 = int3(_S754, int(_S755), int(0));
                    int2 minimumTile_7 = int2(_S751, int(uint(round((((&kernelContext_2)->compositeTexture_0).read(vec<uint,2>(((_S756)).xy), uint(((_S756)).z))).x * 255.0f))));
                    uint _S757 = atlasWidth_7;
                    uint _S758 = 4U % atlasWidth_7;
                    int _S759 = int(_S758);
                    uint _S760 = 4U / _S757;
                    int3 _S761 = int3(_S759, int(_S760), int(0));
                    uint _S762 = uint(round((((&kernelContext_2)->compositeTexture_0).read(vec<uint,2>(((_S761)).xy), uint(((_S761)).z))).x * 255.0f));
                    uint _S763 = atlasWidth_7;
                    uint _S764 = 5U % atlasWidth_7;
                    int _S765 = int(_S764);
                    uint _S766 = 5U / _S763;
                    int3 _S767 = int3(_S765, int(_S766), int(0));
                    uint _S768 = uint(round((((&kernelContext_2)->compositeTexture_0).read(vec<uint,2>(((_S767)).xy), uint(((_S767)).z))).x * 255.0f));
                    uint2 grid_7 = uint2(_S762, _S768);
                    int2 tile_14 = int2(floor(_S212)) - minimumTile_7;
                    if(any(tile_14 < (int2(int(0)) )))
                    {
                        hasSceneLighting_0 = true;
                    }
                    else
                    {
                        hasSceneLighting_0 = any(tile_14 >= int2(grid_7));
                    }
                    if(hasSceneLighting_0)
                    {
                        int3 _S769 = int3(int(1), int(0), int(0));
                        _S521 = (((&kernelContext_2)->compositeTexture_0).read(vec<uint,2>(((_S769)).xy), uint(((_S769)).z)));
                        break;
                    }
                    uint offset_7 = 10U + 2U * (uint(tile_14.y) * _S762 + uint(tile_14.x));
                    uint _S770 = atlasWidth_7;
                    uint _S771 = offset_7 % atlasWidth_7;
                    int _S772 = int(_S771);
                    uint _S773 = offset_7 / _S770;
                    int3 _S774 = int3(_S772, int(_S773), int(0));
                    uint _S775 = uint(round((((&kernelContext_2)->compositeTexture_0).read(vec<uint,2>(((_S774)).xy), uint(((_S774)).z))).x * 255.0f));
                    uint _S776 = atlasWidth_7;
                    uint _S777 = offset_7 + 1U;
                    uint _S778 = _S777 % atlasWidth_7;
                    int _S779 = int(_S778);
                    uint _S780 = _S777 / _S776;
                    int3 _S781 = int3(_S779, int(_S780), int(0));
                    uint slot_14 = _S775 | (uint(round((((&kernelContext_2)->compositeTexture_0).read(vec<uint,2>(((_S781)).xy), uint(((_S781)).z))).x * 255.0f)) << 8U);
                    if(slot_14 == 0U)
                    {
                        int3 _S782 = int3(int(1), int(0), int(0));
                        _S521 = (((&kernelContext_2)->compositeTexture_0).read(vec<uint,2>(((_S782)).xy), uint(((_S782)).z)));
                        break;
                    }
                    uint slot_15 = slot_14 - 1U;
                    uint _S783 = atlasWidth_7;
                    uint _S784 = 6U % atlasWidth_7;
                    int _S785 = int(_S784);
                    uint _S786 = 6U / _S783;
                    int3 _S787 = int3(_S785, int(_S786), int(0));
                    uint _S788 = uint(round((((&kernelContext_2)->compositeTexture_0).read(vec<uint,2>(((_S787)).xy), uint(((_S787)).z))).x * 255.0f));
                    uint _S789 = atlasWidth_7;
                    uint _S790 = 7U % atlasWidth_7;
                    int _S791 = int(_S790);
                    uint _S792 = 7U / _S789;
                    int3 _S793 = int3(_S791, int(_S792), int(0));
                    uint columns_7 = _S788 | (uint(round((((&kernelContext_2)->compositeTexture_0).read(vec<uint,2>(((_S793)).xy), uint(((_S793)).z))).x * 255.0f)) << 8U);
                    uint _S794 = atlasWidth_7;
                    uint _S795 = 8U % atlasWidth_7;
                    int _S796 = int(_S795);
                    uint _S797 = 8U / _S794;
                    int3 _S798 = int3(_S796, int(_S797), int(0));
                    uint _S799 = uint(round((((&kernelContext_2)->compositeTexture_0).read(vec<uint,2>(((_S798)).xy), uint(((_S798)).z))).x * 255.0f));
                    uint _S800 = atlasWidth_7;
                    uint _S801 = 9U % atlasWidth_7;
                    int _S802 = int(_S801);
                    uint _S803 = 9U / _S800;
                    int3 _S804 = int3(_S802, int(_S803), int(0));
                    uint rows_7 = ((_S799 | (uint(round((((&kernelContext_2)->compositeTexture_0).read(vec<uint,2>(((_S804)).xy), uint(((_S804)).z))).x * 255.0f)) << 8U)) + columns_7 - 1U) / columns_7;
                    uint metadataRows_7 = (10U + 2U * _S762 * _S768 + atlasWidth_7 - 1U) / atlasWidth_7;
                    uint _S805 = atlasWidth_7 / columns_7;
                    float _S806 = float(_S805);
                    uint _S807 = (atlasHeight_7 - metadataRows_7) / rows_7;
                    float2 cellSize_14 = float2(_S806, float(_S807));
                    uint _S808 = slot_15 % columns_7;
                    float _S809 = float(_S808);
                    uint _S810 = slot_15 / columns_7;
                    _S521 = (((&kernelContext_2)->compositeTexture_0).sample(((&kernelContext_2)->compositeSampler_0), ((float2(_S809, float(_S810)) * cellSize_14 + float2(1.5f, float(metadataRows_7) + 1.5f) + fract(_S212) * max(cellSize_14 - float2(3.0f) , float2(0.0f) )) / float2(float(atlasWidth_7), float(atlasHeight_7)))));
                    break;
                }
                int2 _S811 = int2(metadata_7.zw);
                int2 tile_15 = int2(floor(_S212)) - int2(metadata_7.xy);
                if(any(tile_15 < (int2(int(0)) )))
                {
                    hasSceneLighting_0 = true;
                }
                else
                {
                    hasSceneLighting_0 = any(tile_15 >= _S811);
                }
                if(hasSceneLighting_0)
                {
                    int3 _S812 = int3(int(min(1U, atlasWidth_7 - 1U)), int(0), int(0));
                    _S521 = (((&kernelContext_2)->compositeTexture_0).read(vec<uint,2>(((_S812)).xy), uint(((_S812)).z)));
                    break;
                }
                uint _S813 = atlasWidth_7 / uint(_S811.x);
                float _S814 = float(_S813);
                uint _S815 = (atlasHeight_7 - 1U) / uint(_S811.y);
                float2 cellSize_15 = float2(_S814, float(_S815));
                _S521 = (((&kernelContext_2)->compositeTexture_0).sample(((&kernelContext_2)->compositeSampler_0), ((float2(tile_15) * cellSize_15 + float2(1.5f, 2.5f) + fract(_S212) * max(cellSize_15 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_7), float(atlasHeight_7)))));
                break;
            }
            uint operation_2 = uint(round(_S742.y));
            if(operation_2 == 1U)
            {
                _S213 = _S213 * _S521;
                break;
            }
            if(operation_2 == 2U)
            {
                _S213 = _S213 + _S521;
                break;
            }
            if(operation_2 == 3U)
            {
                _S213 = _S213 - _S521;
                break;
            }
            if(operation_2 == 4U)
            {
                float factor_2 = _S742.z;
                _S213 = _S213 * float4((1.0f - factor_2))  + _S521 * float4(factor_2) ;
                break;
            }
            break;
        }
        metallic_0 = saturate(_S213.x);
    }
    else
    {
        metallic_0 = _S200;
    }
    float3 emissiveColor_1;
    if((textureMask_0 & 16U) != 0U)
    {
        thread float4 emissionSample_0;
        bool _S816 = (udimMask_0 & 16U) != 0U;
        for(;;)
        {
            if(!_S816)
            {
                _S213 = (((&kernelContext_2)->emissiveTexture_0).sample(((&kernelContext_2)->emissiveSampler_0), (_S212)));
                break;
            }
            texture2d<float, access::sample> _S817 = (&kernelContext_2)->emissiveTexture_0;
            thread uint atlasWidth_8;
            thread uint atlasHeight_8;
            (*((&atlasWidth_8)) = (_S817).get_width(0)),(*((&atlasHeight_8)) = (_S817).get_height(0));
            int3 _S818 = int3(int(0), int(0), int(0));
            float4 metadata_8 = round((((&kernelContext_2)->emissiveTexture_0).read(vec<uint,2>(((_S818)).xy), uint(((_S818)).z))) * float4(255.0f) );
            if((metadata_8.x) == 255.0f)
            {
                uint _S819 = atlasWidth_8;
                uint _S820 = 2U % atlasWidth_8;
                int _S821 = int(_S820);
                uint _S822 = 2U / _S819;
                int3 _S823 = int3(_S821, int(_S822), int(0));
                int _S824 = int(uint(round((((&kernelContext_2)->emissiveTexture_0).read(vec<uint,2>(((_S823)).xy), uint(((_S823)).z))).x * 255.0f)));
                uint _S825 = atlasWidth_8;
                uint _S826 = 3U % atlasWidth_8;
                int _S827 = int(_S826);
                uint _S828 = 3U / _S825;
                int3 _S829 = int3(_S827, int(_S828), int(0));
                int2 minimumTile_8 = int2(_S824, int(uint(round((((&kernelContext_2)->emissiveTexture_0).read(vec<uint,2>(((_S829)).xy), uint(((_S829)).z))).x * 255.0f))));
                uint _S830 = atlasWidth_8;
                uint _S831 = 4U % atlasWidth_8;
                int _S832 = int(_S831);
                uint _S833 = 4U / _S830;
                int3 _S834 = int3(_S832, int(_S833), int(0));
                uint _S835 = uint(round((((&kernelContext_2)->emissiveTexture_0).read(vec<uint,2>(((_S834)).xy), uint(((_S834)).z))).x * 255.0f));
                uint _S836 = atlasWidth_8;
                uint _S837 = 5U % atlasWidth_8;
                int _S838 = int(_S837);
                uint _S839 = 5U / _S836;
                int3 _S840 = int3(_S838, int(_S839), int(0));
                uint _S841 = uint(round((((&kernelContext_2)->emissiveTexture_0).read(vec<uint,2>(((_S840)).xy), uint(((_S840)).z))).x * 255.0f));
                uint2 grid_8 = uint2(_S835, _S841);
                int2 tile_16 = int2(floor(_S212)) - minimumTile_8;
                if(any(tile_16 < (int2(int(0)) )))
                {
                    hasSceneLighting_0 = true;
                }
                else
                {
                    hasSceneLighting_0 = any(tile_16 >= int2(grid_8));
                }
                if(hasSceneLighting_0)
                {
                    int3 _S842 = int3(int(1), int(0), int(0));
                    _S213 = (((&kernelContext_2)->emissiveTexture_0).read(vec<uint,2>(((_S842)).xy), uint(((_S842)).z)));
                    break;
                }
                uint offset_8 = 10U + 2U * (uint(tile_16.y) * _S835 + uint(tile_16.x));
                uint _S843 = atlasWidth_8;
                uint _S844 = offset_8 % atlasWidth_8;
                int _S845 = int(_S844);
                uint _S846 = offset_8 / _S843;
                int3 _S847 = int3(_S845, int(_S846), int(0));
                uint _S848 = uint(round((((&kernelContext_2)->emissiveTexture_0).read(vec<uint,2>(((_S847)).xy), uint(((_S847)).z))).x * 255.0f));
                uint _S849 = atlasWidth_8;
                uint _S850 = offset_8 + 1U;
                uint _S851 = _S850 % atlasWidth_8;
                int _S852 = int(_S851);
                uint _S853 = _S850 / _S849;
                int3 _S854 = int3(_S852, int(_S853), int(0));
                uint slot_16 = _S848 | (uint(round((((&kernelContext_2)->emissiveTexture_0).read(vec<uint,2>(((_S854)).xy), uint(((_S854)).z))).x * 255.0f)) << 8U);
                if(slot_16 == 0U)
                {
                    int3 _S855 = int3(int(1), int(0), int(0));
                    _S213 = (((&kernelContext_2)->emissiveTexture_0).read(vec<uint,2>(((_S855)).xy), uint(((_S855)).z)));
                    break;
                }
                uint slot_17 = slot_16 - 1U;
                uint _S856 = atlasWidth_8;
                uint _S857 = 6U % atlasWidth_8;
                int _S858 = int(_S857);
                uint _S859 = 6U / _S856;
                int3 _S860 = int3(_S858, int(_S859), int(0));
                uint _S861 = uint(round((((&kernelContext_2)->emissiveTexture_0).read(vec<uint,2>(((_S860)).xy), uint(((_S860)).z))).x * 255.0f));
                uint _S862 = atlasWidth_8;
                uint _S863 = 7U % atlasWidth_8;
                int _S864 = int(_S863);
                uint _S865 = 7U / _S862;
                int3 _S866 = int3(_S864, int(_S865), int(0));
                uint columns_8 = _S861 | (uint(round((((&kernelContext_2)->emissiveTexture_0).read(vec<uint,2>(((_S866)).xy), uint(((_S866)).z))).x * 255.0f)) << 8U);
                uint _S867 = atlasWidth_8;
                uint _S868 = 8U % atlasWidth_8;
                int _S869 = int(_S868);
                uint _S870 = 8U / _S867;
                int3 _S871 = int3(_S869, int(_S870), int(0));
                uint _S872 = uint(round((((&kernelContext_2)->emissiveTexture_0).read(vec<uint,2>(((_S871)).xy), uint(((_S871)).z))).x * 255.0f));
                uint _S873 = atlasWidth_8;
                uint _S874 = 9U % atlasWidth_8;
                int _S875 = int(_S874);
                uint _S876 = 9U / _S873;
                int3 _S877 = int3(_S875, int(_S876), int(0));
                uint rows_8 = ((_S872 | (uint(round((((&kernelContext_2)->emissiveTexture_0).read(vec<uint,2>(((_S877)).xy), uint(((_S877)).z))).x * 255.0f)) << 8U)) + columns_8 - 1U) / columns_8;
                uint metadataRows_8 = (10U + 2U * _S835 * _S841 + atlasWidth_8 - 1U) / atlasWidth_8;
                uint _S878 = atlasWidth_8 / columns_8;
                float _S879 = float(_S878);
                uint _S880 = (atlasHeight_8 - metadataRows_8) / rows_8;
                float2 cellSize_16 = float2(_S879, float(_S880));
                uint _S881 = slot_17 % columns_8;
                float _S882 = float(_S881);
                uint _S883 = slot_17 / columns_8;
                _S213 = (((&kernelContext_2)->emissiveTexture_0).sample(((&kernelContext_2)->emissiveSampler_0), ((float2(_S882, float(_S883)) * cellSize_16 + float2(1.5f, float(metadataRows_8) + 1.5f) + fract(_S212) * max(cellSize_16 - float2(3.0f) , float2(0.0f) )) / float2(float(atlasWidth_8), float(atlasHeight_8)))));
                break;
            }
            int2 _S884 = int2(metadata_8.zw);
            int2 tile_17 = int2(floor(_S212)) - int2(metadata_8.xy);
            if(any(tile_17 < (int2(int(0)) )))
            {
                hasSceneLighting_0 = true;
            }
            else
            {
                hasSceneLighting_0 = any(tile_17 >= _S884);
            }
            if(hasSceneLighting_0)
            {
                int3 _S885 = int3(int(min(1U, atlasWidth_8 - 1U)), int(0), int(0));
                _S213 = (((&kernelContext_2)->emissiveTexture_0).read(vec<uint,2>(((_S885)).xy), uint(((_S885)).z)));
                break;
            }
            uint _S886 = atlasWidth_8 / uint(_S884.x);
            float _S887 = float(_S886);
            uint _S888 = (atlasHeight_8 - 1U) / uint(_S884.y);
            float2 cellSize_17 = float2(_S887, float(_S888));
            _S213 = (((&kernelContext_2)->emissiveTexture_0).sample(((&kernelContext_2)->emissiveSampler_0), ((float2(tile_17) * cellSize_17 + float2(1.5f, 2.5f) + fract(_S212) * max(cellSize_17 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_8), float(atlasHeight_8)))));
            break;
        }
        emissionSample_0 = _S213;
        if((_S208.w) != 0.0f)
        {
            texture2d<float, access::sample> _S889 = (&kernelContext_2)->emissiveTexture_0;
            thread uint sourceWidth_1;
            thread uint sourceHeight_1;
            (*((&sourceWidth_1)) = (_S889).get_width(0)),(*((&sourceHeight_1)) = (_S889).get_height(0));
            texture2d<float, access::sample> _S890 = (&kernelContext_2)->emissiveValuesTexture_0;
            thread uint valueWidth_1;
            thread uint valueHeight_1;
            (*((&valueWidth_1)) = (_S890).get_width(0)),(*((&valueHeight_1)) = (_S890).get_height(0));
            if(valueWidth_1 != sourceWidth_1)
            {
                float3 _S891 = DecodeIndexedRgb_0(emissionSample_0, (&kernelContext_2)->emissiveValuesTexture_0);
                emissionSample_0.xyz = _S891;
            }
            else
            {
                emissionSample_0.z = (((&kernelContext_2)->emissiveValuesTexture_0).sample(((&kernelContext_2)->emissiveSampler_0), (_S212))).x;
            }
        }
        float4 _S892 = emissionSample_0;
        for(;;)
        {
            float4 _S893 = float4(_S40->compositeControls_0) ;
            if((_S893.x) != 16.0f)
            {
                _S213 = _S892;
                break;
            }
            bool _S894 = (_S893.w) >= 0.5f;
            for(;;)
            {
                if(!_S894)
                {
                    _S213 = (((&kernelContext_2)->compositeTexture_0).sample(((&kernelContext_2)->compositeSampler_0), (_S212)));
                    break;
                }
                texture2d<float, access::sample> _S895 = (&kernelContext_2)->compositeTexture_0;
                thread uint atlasWidth_9;
                thread uint atlasHeight_9;
                (*((&atlasWidth_9)) = (_S895).get_width(0)),(*((&atlasHeight_9)) = (_S895).get_height(0));
                int3 _S896 = int3(int(0), int(0), int(0));
                float4 metadata_9 = round((((&kernelContext_2)->compositeTexture_0).read(vec<uint,2>(((_S896)).xy), uint(((_S896)).z))) * float4(255.0f) );
                if((metadata_9.x) == 255.0f)
                {
                    uint _S897 = atlasWidth_9;
                    uint _S898 = 2U % atlasWidth_9;
                    int _S899 = int(_S898);
                    uint _S900 = 2U / _S897;
                    int3 _S901 = int3(_S899, int(_S900), int(0));
                    int _S902 = int(uint(round((((&kernelContext_2)->compositeTexture_0).read(vec<uint,2>(((_S901)).xy), uint(((_S901)).z))).x * 255.0f)));
                    uint _S903 = atlasWidth_9;
                    uint _S904 = 3U % atlasWidth_9;
                    int _S905 = int(_S904);
                    uint _S906 = 3U / _S903;
                    int3 _S907 = int3(_S905, int(_S906), int(0));
                    int2 minimumTile_9 = int2(_S902, int(uint(round((((&kernelContext_2)->compositeTexture_0).read(vec<uint,2>(((_S907)).xy), uint(((_S907)).z))).x * 255.0f))));
                    uint _S908 = atlasWidth_9;
                    uint _S909 = 4U % atlasWidth_9;
                    int _S910 = int(_S909);
                    uint _S911 = 4U / _S908;
                    int3 _S912 = int3(_S910, int(_S911), int(0));
                    uint _S913 = uint(round((((&kernelContext_2)->compositeTexture_0).read(vec<uint,2>(((_S912)).xy), uint(((_S912)).z))).x * 255.0f));
                    uint _S914 = atlasWidth_9;
                    uint _S915 = 5U % atlasWidth_9;
                    int _S916 = int(_S915);
                    uint _S917 = 5U / _S914;
                    int3 _S918 = int3(_S916, int(_S917), int(0));
                    uint _S919 = uint(round((((&kernelContext_2)->compositeTexture_0).read(vec<uint,2>(((_S918)).xy), uint(((_S918)).z))).x * 255.0f));
                    uint2 grid_9 = uint2(_S913, _S919);
                    int2 tile_18 = int2(floor(_S212)) - minimumTile_9;
                    if(any(tile_18 < (int2(int(0)) )))
                    {
                        hasSceneLighting_0 = true;
                    }
                    else
                    {
                        hasSceneLighting_0 = any(tile_18 >= int2(grid_9));
                    }
                    if(hasSceneLighting_0)
                    {
                        int3 _S920 = int3(int(1), int(0), int(0));
                        _S213 = (((&kernelContext_2)->compositeTexture_0).read(vec<uint,2>(((_S920)).xy), uint(((_S920)).z)));
                        break;
                    }
                    uint offset_9 = 10U + 2U * (uint(tile_18.y) * _S913 + uint(tile_18.x));
                    uint _S921 = atlasWidth_9;
                    uint _S922 = offset_9 % atlasWidth_9;
                    int _S923 = int(_S922);
                    uint _S924 = offset_9 / _S921;
                    int3 _S925 = int3(_S923, int(_S924), int(0));
                    uint _S926 = uint(round((((&kernelContext_2)->compositeTexture_0).read(vec<uint,2>(((_S925)).xy), uint(((_S925)).z))).x * 255.0f));
                    uint _S927 = atlasWidth_9;
                    uint _S928 = offset_9 + 1U;
                    uint _S929 = _S928 % atlasWidth_9;
                    int _S930 = int(_S929);
                    uint _S931 = _S928 / _S927;
                    int3 _S932 = int3(_S930, int(_S931), int(0));
                    uint slot_18 = _S926 | (uint(round((((&kernelContext_2)->compositeTexture_0).read(vec<uint,2>(((_S932)).xy), uint(((_S932)).z))).x * 255.0f)) << 8U);
                    if(slot_18 == 0U)
                    {
                        int3 _S933 = int3(int(1), int(0), int(0));
                        _S213 = (((&kernelContext_2)->compositeTexture_0).read(vec<uint,2>(((_S933)).xy), uint(((_S933)).z)));
                        break;
                    }
                    uint slot_19 = slot_18 - 1U;
                    uint _S934 = atlasWidth_9;
                    uint _S935 = 6U % atlasWidth_9;
                    int _S936 = int(_S935);
                    uint _S937 = 6U / _S934;
                    int3 _S938 = int3(_S936, int(_S937), int(0));
                    uint _S939 = uint(round((((&kernelContext_2)->compositeTexture_0).read(vec<uint,2>(((_S938)).xy), uint(((_S938)).z))).x * 255.0f));
                    uint _S940 = atlasWidth_9;
                    uint _S941 = 7U % atlasWidth_9;
                    int _S942 = int(_S941);
                    uint _S943 = 7U / _S940;
                    int3 _S944 = int3(_S942, int(_S943), int(0));
                    uint columns_9 = _S939 | (uint(round((((&kernelContext_2)->compositeTexture_0).read(vec<uint,2>(((_S944)).xy), uint(((_S944)).z))).x * 255.0f)) << 8U);
                    uint _S945 = atlasWidth_9;
                    uint _S946 = 8U % atlasWidth_9;
                    int _S947 = int(_S946);
                    uint _S948 = 8U / _S945;
                    int3 _S949 = int3(_S947, int(_S948), int(0));
                    uint _S950 = uint(round((((&kernelContext_2)->compositeTexture_0).read(vec<uint,2>(((_S949)).xy), uint(((_S949)).z))).x * 255.0f));
                    uint _S951 = atlasWidth_9;
                    uint _S952 = 9U % atlasWidth_9;
                    int _S953 = int(_S952);
                    uint _S954 = 9U / _S951;
                    int3 _S955 = int3(_S953, int(_S954), int(0));
                    uint rows_9 = ((_S950 | (uint(round((((&kernelContext_2)->compositeTexture_0).read(vec<uint,2>(((_S955)).xy), uint(((_S955)).z))).x * 255.0f)) << 8U)) + columns_9 - 1U) / columns_9;
                    uint metadataRows_9 = (10U + 2U * _S913 * _S919 + atlasWidth_9 - 1U) / atlasWidth_9;
                    uint _S956 = atlasWidth_9 / columns_9;
                    float _S957 = float(_S956);
                    uint _S958 = (atlasHeight_9 - metadataRows_9) / rows_9;
                    float2 cellSize_18 = float2(_S957, float(_S958));
                    uint _S959 = slot_19 % columns_9;
                    float _S960 = float(_S959);
                    uint _S961 = slot_19 / columns_9;
                    _S213 = (((&kernelContext_2)->compositeTexture_0).sample(((&kernelContext_2)->compositeSampler_0), ((float2(_S960, float(_S961)) * cellSize_18 + float2(1.5f, float(metadataRows_9) + 1.5f) + fract(_S212) * max(cellSize_18 - float2(3.0f) , float2(0.0f) )) / float2(float(atlasWidth_9), float(atlasHeight_9)))));
                    break;
                }
                int2 _S962 = int2(metadata_9.zw);
                int2 tile_19 = int2(floor(_S212)) - int2(metadata_9.xy);
                if(any(tile_19 < (int2(int(0)) )))
                {
                    hasSceneLighting_0 = true;
                }
                else
                {
                    hasSceneLighting_0 = any(tile_19 >= _S962);
                }
                if(hasSceneLighting_0)
                {
                    int3 _S963 = int3(int(min(1U, atlasWidth_9 - 1U)), int(0), int(0));
                    _S213 = (((&kernelContext_2)->compositeTexture_0).read(vec<uint,2>(((_S963)).xy), uint(((_S963)).z)));
                    break;
                }
                uint _S964 = atlasWidth_9 / uint(_S962.x);
                float _S965 = float(_S964);
                uint _S966 = (atlasHeight_9 - 1U) / uint(_S962.y);
                float2 cellSize_19 = float2(_S965, float(_S966));
                _S213 = (((&kernelContext_2)->compositeTexture_0).sample(((&kernelContext_2)->compositeSampler_0), ((float2(tile_19) * cellSize_19 + float2(1.5f, 2.5f) + fract(_S212) * max(cellSize_19 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_9), float(atlasHeight_9)))));
                break;
            }
            uint operation_3 = uint(round(_S893.y));
            if(operation_3 == 1U)
            {
                _S213 = _S892 * _S213;
                break;
            }
            if(operation_3 == 2U)
            {
                _S213 = _S892 + _S213;
                break;
            }
            if(operation_3 == 3U)
            {
                _S213 = _S892 - _S213;
                break;
            }
            if(operation_3 == 4U)
            {
                float factor_3 = _S893.z;
                _S213 = _S892 * float4((1.0f - factor_3))  + _S213 * float4(factor_3) ;
                break;
            }
            _S213 = _S892;
            break;
        }
        emissiveColor_1 = _S213.xyz;
    }
    else
    {
        emissiveColor_1 = emissiveColor_0;
    }
    if((textureMask_0 & 64U) != 0U)
    {
        bool _S967 = (udimMask_0 & 64U) != 0U;
        for(;;)
        {
            if(!_S967)
            {
                _S213 = (((&kernelContext_2)->opacityTexture_0).sample(((&kernelContext_2)->opacitySampler_0), (_S212)));
                break;
            }
            texture2d<float, access::sample> _S968 = (&kernelContext_2)->opacityTexture_0;
            thread uint atlasWidth_10;
            thread uint atlasHeight_10;
            (*((&atlasWidth_10)) = (_S968).get_width(0)),(*((&atlasHeight_10)) = (_S968).get_height(0));
            int3 _S969 = int3(int(0), int(0), int(0));
            float4 metadata_10 = round((((&kernelContext_2)->opacityTexture_0).read(vec<uint,2>(((_S969)).xy), uint(((_S969)).z))) * float4(255.0f) );
            if((metadata_10.x) == 255.0f)
            {
                uint _S970 = atlasWidth_10;
                uint _S971 = 2U % atlasWidth_10;
                int _S972 = int(_S971);
                uint _S973 = 2U / _S970;
                int3 _S974 = int3(_S972, int(_S973), int(0));
                int _S975 = int(uint(round((((&kernelContext_2)->opacityTexture_0).read(vec<uint,2>(((_S974)).xy), uint(((_S974)).z))).x * 255.0f)));
                uint _S976 = atlasWidth_10;
                uint _S977 = 3U % atlasWidth_10;
                int _S978 = int(_S977);
                uint _S979 = 3U / _S976;
                int3 _S980 = int3(_S978, int(_S979), int(0));
                int2 minimumTile_10 = int2(_S975, int(uint(round((((&kernelContext_2)->opacityTexture_0).read(vec<uint,2>(((_S980)).xy), uint(((_S980)).z))).x * 255.0f))));
                uint _S981 = atlasWidth_10;
                uint _S982 = 4U % atlasWidth_10;
                int _S983 = int(_S982);
                uint _S984 = 4U / _S981;
                int3 _S985 = int3(_S983, int(_S984), int(0));
                uint _S986 = uint(round((((&kernelContext_2)->opacityTexture_0).read(vec<uint,2>(((_S985)).xy), uint(((_S985)).z))).x * 255.0f));
                uint _S987 = atlasWidth_10;
                uint _S988 = 5U % atlasWidth_10;
                int _S989 = int(_S988);
                uint _S990 = 5U / _S987;
                int3 _S991 = int3(_S989, int(_S990), int(0));
                uint _S992 = uint(round((((&kernelContext_2)->opacityTexture_0).read(vec<uint,2>(((_S991)).xy), uint(((_S991)).z))).x * 255.0f));
                uint2 grid_10 = uint2(_S986, _S992);
                int2 tile_20 = int2(floor(_S212)) - minimumTile_10;
                if(any(tile_20 < (int2(int(0)) )))
                {
                    hasSceneLighting_0 = true;
                }
                else
                {
                    hasSceneLighting_0 = any(tile_20 >= int2(grid_10));
                }
                if(hasSceneLighting_0)
                {
                    int3 _S993 = int3(int(1), int(0), int(0));
                    _S213 = (((&kernelContext_2)->opacityTexture_0).read(vec<uint,2>(((_S993)).xy), uint(((_S993)).z)));
                    break;
                }
                uint offset_10 = 10U + 2U * (uint(tile_20.y) * _S986 + uint(tile_20.x));
                uint _S994 = atlasWidth_10;
                uint _S995 = offset_10 % atlasWidth_10;
                int _S996 = int(_S995);
                uint _S997 = offset_10 / _S994;
                int3 _S998 = int3(_S996, int(_S997), int(0));
                uint _S999 = uint(round((((&kernelContext_2)->opacityTexture_0).read(vec<uint,2>(((_S998)).xy), uint(((_S998)).z))).x * 255.0f));
                uint _S1000 = atlasWidth_10;
                uint _S1001 = offset_10 + 1U;
                uint _S1002 = _S1001 % atlasWidth_10;
                int _S1003 = int(_S1002);
                uint _S1004 = _S1001 / _S1000;
                int3 _S1005 = int3(_S1003, int(_S1004), int(0));
                uint slot_20 = _S999 | (uint(round((((&kernelContext_2)->opacityTexture_0).read(vec<uint,2>(((_S1005)).xy), uint(((_S1005)).z))).x * 255.0f)) << 8U);
                if(slot_20 == 0U)
                {
                    int3 _S1006 = int3(int(1), int(0), int(0));
                    _S213 = (((&kernelContext_2)->opacityTexture_0).read(vec<uint,2>(((_S1006)).xy), uint(((_S1006)).z)));
                    break;
                }
                uint slot_21 = slot_20 - 1U;
                uint _S1007 = atlasWidth_10;
                uint _S1008 = 6U % atlasWidth_10;
                int _S1009 = int(_S1008);
                uint _S1010 = 6U / _S1007;
                int3 _S1011 = int3(_S1009, int(_S1010), int(0));
                uint _S1012 = uint(round((((&kernelContext_2)->opacityTexture_0).read(vec<uint,2>(((_S1011)).xy), uint(((_S1011)).z))).x * 255.0f));
                uint _S1013 = atlasWidth_10;
                uint _S1014 = 7U % atlasWidth_10;
                int _S1015 = int(_S1014);
                uint _S1016 = 7U / _S1013;
                int3 _S1017 = int3(_S1015, int(_S1016), int(0));
                uint columns_10 = _S1012 | (uint(round((((&kernelContext_2)->opacityTexture_0).read(vec<uint,2>(((_S1017)).xy), uint(((_S1017)).z))).x * 255.0f)) << 8U);
                uint _S1018 = atlasWidth_10;
                uint _S1019 = 8U % atlasWidth_10;
                int _S1020 = int(_S1019);
                uint _S1021 = 8U / _S1018;
                int3 _S1022 = int3(_S1020, int(_S1021), int(0));
                uint _S1023 = uint(round((((&kernelContext_2)->opacityTexture_0).read(vec<uint,2>(((_S1022)).xy), uint(((_S1022)).z))).x * 255.0f));
                uint _S1024 = atlasWidth_10;
                uint _S1025 = 9U % atlasWidth_10;
                int _S1026 = int(_S1025);
                uint _S1027 = 9U / _S1024;
                int3 _S1028 = int3(_S1026, int(_S1027), int(0));
                uint rows_10 = ((_S1023 | (uint(round((((&kernelContext_2)->opacityTexture_0).read(vec<uint,2>(((_S1028)).xy), uint(((_S1028)).z))).x * 255.0f)) << 8U)) + columns_10 - 1U) / columns_10;
                uint metadataRows_10 = (10U + 2U * _S986 * _S992 + atlasWidth_10 - 1U) / atlasWidth_10;
                uint _S1029 = atlasWidth_10 / columns_10;
                float _S1030 = float(_S1029);
                uint _S1031 = (atlasHeight_10 - metadataRows_10) / rows_10;
                float2 cellSize_20 = float2(_S1030, float(_S1031));
                uint _S1032 = slot_21 % columns_10;
                float _S1033 = float(_S1032);
                uint _S1034 = slot_21 / columns_10;
                _S213 = (((&kernelContext_2)->opacityTexture_0).sample(((&kernelContext_2)->opacitySampler_0), ((float2(_S1033, float(_S1034)) * cellSize_20 + float2(1.5f, float(metadataRows_10) + 1.5f) + fract(_S212) * max(cellSize_20 - float2(3.0f) , float2(0.0f) )) / float2(float(atlasWidth_10), float(atlasHeight_10)))));
                break;
            }
            int2 _S1035 = int2(metadata_10.zw);
            int2 tile_21 = int2(floor(_S212)) - int2(metadata_10.xy);
            if(any(tile_21 < (int2(int(0)) )))
            {
                hasSceneLighting_0 = true;
            }
            else
            {
                hasSceneLighting_0 = any(tile_21 >= _S1035);
            }
            if(hasSceneLighting_0)
            {
                int3 _S1036 = int3(int(min(1U, atlasWidth_10 - 1U)), int(0), int(0));
                _S213 = (((&kernelContext_2)->opacityTexture_0).read(vec<uint,2>(((_S1036)).xy), uint(((_S1036)).z)));
                break;
            }
            uint _S1037 = atlasWidth_10 / uint(_S1035.x);
            float _S1038 = float(_S1037);
            uint _S1039 = (atlasHeight_10 - 1U) / uint(_S1035.y);
            float2 cellSize_21 = float2(_S1038, float(_S1039));
            _S213 = (((&kernelContext_2)->opacityTexture_0).sample(((&kernelContext_2)->opacitySampler_0), ((float2(tile_21) * cellSize_21 + float2(1.5f, 2.5f) + fract(_S212) * max(cellSize_21 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_10), float(atlasHeight_10)))));
            break;
        }
        for(;;)
        {
            float4 _S1040 = float4(_S40->compositeControls_0) ;
            if((_S1040.x) != 64.0f)
            {
                break;
            }
            bool _S1041 = (_S1040.w) >= 0.5f;
            for(;;)
            {
                if(!_S1041)
                {
                    _S521 = (((&kernelContext_2)->compositeTexture_0).sample(((&kernelContext_2)->compositeSampler_0), (_S212)));
                    break;
                }
                texture2d<float, access::sample> _S1042 = (&kernelContext_2)->compositeTexture_0;
                thread uint atlasWidth_11;
                thread uint atlasHeight_11;
                (*((&atlasWidth_11)) = (_S1042).get_width(0)),(*((&atlasHeight_11)) = (_S1042).get_height(0));
                int3 _S1043 = int3(int(0), int(0), int(0));
                float4 metadata_11 = round((((&kernelContext_2)->compositeTexture_0).read(vec<uint,2>(((_S1043)).xy), uint(((_S1043)).z))) * float4(255.0f) );
                if((metadata_11.x) == 255.0f)
                {
                    uint _S1044 = atlasWidth_11;
                    uint _S1045 = 2U % atlasWidth_11;
                    int _S1046 = int(_S1045);
                    uint _S1047 = 2U / _S1044;
                    int3 _S1048 = int3(_S1046, int(_S1047), int(0));
                    int _S1049 = int(uint(round((((&kernelContext_2)->compositeTexture_0).read(vec<uint,2>(((_S1048)).xy), uint(((_S1048)).z))).x * 255.0f)));
                    uint _S1050 = atlasWidth_11;
                    uint _S1051 = 3U % atlasWidth_11;
                    int _S1052 = int(_S1051);
                    uint _S1053 = 3U / _S1050;
                    int3 _S1054 = int3(_S1052, int(_S1053), int(0));
                    int2 minimumTile_11 = int2(_S1049, int(uint(round((((&kernelContext_2)->compositeTexture_0).read(vec<uint,2>(((_S1054)).xy), uint(((_S1054)).z))).x * 255.0f))));
                    uint _S1055 = atlasWidth_11;
                    uint _S1056 = 4U % atlasWidth_11;
                    int _S1057 = int(_S1056);
                    uint _S1058 = 4U / _S1055;
                    int3 _S1059 = int3(_S1057, int(_S1058), int(0));
                    uint _S1060 = uint(round((((&kernelContext_2)->compositeTexture_0).read(vec<uint,2>(((_S1059)).xy), uint(((_S1059)).z))).x * 255.0f));
                    uint _S1061 = atlasWidth_11;
                    uint _S1062 = 5U % atlasWidth_11;
                    int _S1063 = int(_S1062);
                    uint _S1064 = 5U / _S1061;
                    int3 _S1065 = int3(_S1063, int(_S1064), int(0));
                    uint _S1066 = uint(round((((&kernelContext_2)->compositeTexture_0).read(vec<uint,2>(((_S1065)).xy), uint(((_S1065)).z))).x * 255.0f));
                    uint2 grid_11 = uint2(_S1060, _S1066);
                    int2 tile_22 = int2(floor(_S212)) - minimumTile_11;
                    if(any(tile_22 < (int2(int(0)) )))
                    {
                        hasSceneLighting_0 = true;
                    }
                    else
                    {
                        hasSceneLighting_0 = any(tile_22 >= int2(grid_11));
                    }
                    if(hasSceneLighting_0)
                    {
                        int3 _S1067 = int3(int(1), int(0), int(0));
                        _S521 = (((&kernelContext_2)->compositeTexture_0).read(vec<uint,2>(((_S1067)).xy), uint(((_S1067)).z)));
                        break;
                    }
                    uint offset_11 = 10U + 2U * (uint(tile_22.y) * _S1060 + uint(tile_22.x));
                    uint _S1068 = atlasWidth_11;
                    uint _S1069 = offset_11 % atlasWidth_11;
                    int _S1070 = int(_S1069);
                    uint _S1071 = offset_11 / _S1068;
                    int3 _S1072 = int3(_S1070, int(_S1071), int(0));
                    uint _S1073 = uint(round((((&kernelContext_2)->compositeTexture_0).read(vec<uint,2>(((_S1072)).xy), uint(((_S1072)).z))).x * 255.0f));
                    uint _S1074 = atlasWidth_11;
                    uint _S1075 = offset_11 + 1U;
                    uint _S1076 = _S1075 % atlasWidth_11;
                    int _S1077 = int(_S1076);
                    uint _S1078 = _S1075 / _S1074;
                    int3 _S1079 = int3(_S1077, int(_S1078), int(0));
                    uint slot_22 = _S1073 | (uint(round((((&kernelContext_2)->compositeTexture_0).read(vec<uint,2>(((_S1079)).xy), uint(((_S1079)).z))).x * 255.0f)) << 8U);
                    if(slot_22 == 0U)
                    {
                        int3 _S1080 = int3(int(1), int(0), int(0));
                        _S521 = (((&kernelContext_2)->compositeTexture_0).read(vec<uint,2>(((_S1080)).xy), uint(((_S1080)).z)));
                        break;
                    }
                    uint slot_23 = slot_22 - 1U;
                    uint _S1081 = atlasWidth_11;
                    uint _S1082 = 6U % atlasWidth_11;
                    int _S1083 = int(_S1082);
                    uint _S1084 = 6U / _S1081;
                    int3 _S1085 = int3(_S1083, int(_S1084), int(0));
                    uint _S1086 = uint(round((((&kernelContext_2)->compositeTexture_0).read(vec<uint,2>(((_S1085)).xy), uint(((_S1085)).z))).x * 255.0f));
                    uint _S1087 = atlasWidth_11;
                    uint _S1088 = 7U % atlasWidth_11;
                    int _S1089 = int(_S1088);
                    uint _S1090 = 7U / _S1087;
                    int3 _S1091 = int3(_S1089, int(_S1090), int(0));
                    uint columns_11 = _S1086 | (uint(round((((&kernelContext_2)->compositeTexture_0).read(vec<uint,2>(((_S1091)).xy), uint(((_S1091)).z))).x * 255.0f)) << 8U);
                    uint _S1092 = atlasWidth_11;
                    uint _S1093 = 8U % atlasWidth_11;
                    int _S1094 = int(_S1093);
                    uint _S1095 = 8U / _S1092;
                    int3 _S1096 = int3(_S1094, int(_S1095), int(0));
                    uint _S1097 = uint(round((((&kernelContext_2)->compositeTexture_0).read(vec<uint,2>(((_S1096)).xy), uint(((_S1096)).z))).x * 255.0f));
                    uint _S1098 = atlasWidth_11;
                    uint _S1099 = 9U % atlasWidth_11;
                    int _S1100 = int(_S1099);
                    uint _S1101 = 9U / _S1098;
                    int3 _S1102 = int3(_S1100, int(_S1101), int(0));
                    uint rows_11 = ((_S1097 | (uint(round((((&kernelContext_2)->compositeTexture_0).read(vec<uint,2>(((_S1102)).xy), uint(((_S1102)).z))).x * 255.0f)) << 8U)) + columns_11 - 1U) / columns_11;
                    uint metadataRows_11 = (10U + 2U * _S1060 * _S1066 + atlasWidth_11 - 1U) / atlasWidth_11;
                    uint _S1103 = atlasWidth_11 / columns_11;
                    float _S1104 = float(_S1103);
                    uint _S1105 = (atlasHeight_11 - metadataRows_11) / rows_11;
                    float2 cellSize_22 = float2(_S1104, float(_S1105));
                    uint _S1106 = slot_23 % columns_11;
                    float _S1107 = float(_S1106);
                    uint _S1108 = slot_23 / columns_11;
                    _S521 = (((&kernelContext_2)->compositeTexture_0).sample(((&kernelContext_2)->compositeSampler_0), ((float2(_S1107, float(_S1108)) * cellSize_22 + float2(1.5f, float(metadataRows_11) + 1.5f) + fract(_S212) * max(cellSize_22 - float2(3.0f) , float2(0.0f) )) / float2(float(atlasWidth_11), float(atlasHeight_11)))));
                    break;
                }
                int2 _S1109 = int2(metadata_11.zw);
                int2 tile_23 = int2(floor(_S212)) - int2(metadata_11.xy);
                if(any(tile_23 < (int2(int(0)) )))
                {
                    hasSceneLighting_0 = true;
                }
                else
                {
                    hasSceneLighting_0 = any(tile_23 >= _S1109);
                }
                if(hasSceneLighting_0)
                {
                    int3 _S1110 = int3(int(min(1U, atlasWidth_11 - 1U)), int(0), int(0));
                    _S521 = (((&kernelContext_2)->compositeTexture_0).read(vec<uint,2>(((_S1110)).xy), uint(((_S1110)).z)));
                    break;
                }
                uint _S1111 = atlasWidth_11 / uint(_S1109.x);
                float _S1112 = float(_S1111);
                uint _S1113 = (atlasHeight_11 - 1U) / uint(_S1109.y);
                float2 cellSize_23 = float2(_S1112, float(_S1113));
                _S521 = (((&kernelContext_2)->compositeTexture_0).sample(((&kernelContext_2)->compositeSampler_0), ((float2(tile_23) * cellSize_23 + float2(1.5f, 2.5f) + fract(_S212) * max(cellSize_23 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_11), float(atlasHeight_11)))));
                break;
            }
            uint operation_4 = uint(round(_S1040.y));
            if(operation_4 == 1U)
            {
                _S213 = _S213 * _S521;
                break;
            }
            if(operation_4 == 2U)
            {
                _S213 = _S213 + _S521;
                break;
            }
            if(operation_4 == 3U)
            {
                _S213 = _S213 - _S521;
                break;
            }
            if(operation_4 == 4U)
            {
                float factor_4 = _S1040.z;
                _S213 = _S213 * float4((1.0f - factor_4))  + _S521 * float4(factor_4) ;
                break;
            }
            break;
        }
        opacity_0 = saturate(_S213.x);
    }
    float occlusion_0;
    if((textureMask_0 & 128U) != 0U)
    {
        bool _S1114 = (udimMask_0 & 128U) != 0U;
        for(;;)
        {
            if(!_S1114)
            {
                _S213 = (((&kernelContext_2)->occlusionTexture_0).sample(((&kernelContext_2)->occlusionSampler_0), (_S212)));
                break;
            }
            texture2d<float, access::sample> _S1115 = (&kernelContext_2)->occlusionTexture_0;
            thread uint atlasWidth_12;
            thread uint atlasHeight_12;
            (*((&atlasWidth_12)) = (_S1115).get_width(0)),(*((&atlasHeight_12)) = (_S1115).get_height(0));
            int3 _S1116 = int3(int(0), int(0), int(0));
            float4 metadata_12 = round((((&kernelContext_2)->occlusionTexture_0).read(vec<uint,2>(((_S1116)).xy), uint(((_S1116)).z))) * float4(255.0f) );
            if((metadata_12.x) == 255.0f)
            {
                uint _S1117 = atlasWidth_12;
                uint _S1118 = 2U % atlasWidth_12;
                int _S1119 = int(_S1118);
                uint _S1120 = 2U / _S1117;
                int3 _S1121 = int3(_S1119, int(_S1120), int(0));
                int _S1122 = int(uint(round((((&kernelContext_2)->occlusionTexture_0).read(vec<uint,2>(((_S1121)).xy), uint(((_S1121)).z))).x * 255.0f)));
                uint _S1123 = atlasWidth_12;
                uint _S1124 = 3U % atlasWidth_12;
                int _S1125 = int(_S1124);
                uint _S1126 = 3U / _S1123;
                int3 _S1127 = int3(_S1125, int(_S1126), int(0));
                int2 minimumTile_12 = int2(_S1122, int(uint(round((((&kernelContext_2)->occlusionTexture_0).read(vec<uint,2>(((_S1127)).xy), uint(((_S1127)).z))).x * 255.0f))));
                uint _S1128 = atlasWidth_12;
                uint _S1129 = 4U % atlasWidth_12;
                int _S1130 = int(_S1129);
                uint _S1131 = 4U / _S1128;
                int3 _S1132 = int3(_S1130, int(_S1131), int(0));
                uint _S1133 = uint(round((((&kernelContext_2)->occlusionTexture_0).read(vec<uint,2>(((_S1132)).xy), uint(((_S1132)).z))).x * 255.0f));
                uint _S1134 = atlasWidth_12;
                uint _S1135 = 5U % atlasWidth_12;
                int _S1136 = int(_S1135);
                uint _S1137 = 5U / _S1134;
                int3 _S1138 = int3(_S1136, int(_S1137), int(0));
                uint _S1139 = uint(round((((&kernelContext_2)->occlusionTexture_0).read(vec<uint,2>(((_S1138)).xy), uint(((_S1138)).z))).x * 255.0f));
                uint2 grid_12 = uint2(_S1133, _S1139);
                int2 tile_24 = int2(floor(_S212)) - minimumTile_12;
                if(any(tile_24 < (int2(int(0)) )))
                {
                    hasSceneLighting_0 = true;
                }
                else
                {
                    hasSceneLighting_0 = any(tile_24 >= int2(grid_12));
                }
                if(hasSceneLighting_0)
                {
                    int3 _S1140 = int3(int(1), int(0), int(0));
                    _S213 = (((&kernelContext_2)->occlusionTexture_0).read(vec<uint,2>(((_S1140)).xy), uint(((_S1140)).z)));
                    break;
                }
                uint offset_12 = 10U + 2U * (uint(tile_24.y) * _S1133 + uint(tile_24.x));
                uint _S1141 = atlasWidth_12;
                uint _S1142 = offset_12 % atlasWidth_12;
                int _S1143 = int(_S1142);
                uint _S1144 = offset_12 / _S1141;
                int3 _S1145 = int3(_S1143, int(_S1144), int(0));
                uint _S1146 = uint(round((((&kernelContext_2)->occlusionTexture_0).read(vec<uint,2>(((_S1145)).xy), uint(((_S1145)).z))).x * 255.0f));
                uint _S1147 = atlasWidth_12;
                uint _S1148 = offset_12 + 1U;
                uint _S1149 = _S1148 % atlasWidth_12;
                int _S1150 = int(_S1149);
                uint _S1151 = _S1148 / _S1147;
                int3 _S1152 = int3(_S1150, int(_S1151), int(0));
                uint slot_24 = _S1146 | (uint(round((((&kernelContext_2)->occlusionTexture_0).read(vec<uint,2>(((_S1152)).xy), uint(((_S1152)).z))).x * 255.0f)) << 8U);
                if(slot_24 == 0U)
                {
                    int3 _S1153 = int3(int(1), int(0), int(0));
                    _S213 = (((&kernelContext_2)->occlusionTexture_0).read(vec<uint,2>(((_S1153)).xy), uint(((_S1153)).z)));
                    break;
                }
                uint slot_25 = slot_24 - 1U;
                uint _S1154 = atlasWidth_12;
                uint _S1155 = 6U % atlasWidth_12;
                int _S1156 = int(_S1155);
                uint _S1157 = 6U / _S1154;
                int3 _S1158 = int3(_S1156, int(_S1157), int(0));
                uint _S1159 = uint(round((((&kernelContext_2)->occlusionTexture_0).read(vec<uint,2>(((_S1158)).xy), uint(((_S1158)).z))).x * 255.0f));
                uint _S1160 = atlasWidth_12;
                uint _S1161 = 7U % atlasWidth_12;
                int _S1162 = int(_S1161);
                uint _S1163 = 7U / _S1160;
                int3 _S1164 = int3(_S1162, int(_S1163), int(0));
                uint columns_12 = _S1159 | (uint(round((((&kernelContext_2)->occlusionTexture_0).read(vec<uint,2>(((_S1164)).xy), uint(((_S1164)).z))).x * 255.0f)) << 8U);
                uint _S1165 = atlasWidth_12;
                uint _S1166 = 8U % atlasWidth_12;
                int _S1167 = int(_S1166);
                uint _S1168 = 8U / _S1165;
                int3 _S1169 = int3(_S1167, int(_S1168), int(0));
                uint _S1170 = uint(round((((&kernelContext_2)->occlusionTexture_0).read(vec<uint,2>(((_S1169)).xy), uint(((_S1169)).z))).x * 255.0f));
                uint _S1171 = atlasWidth_12;
                uint _S1172 = 9U % atlasWidth_12;
                int _S1173 = int(_S1172);
                uint _S1174 = 9U / _S1171;
                int3 _S1175 = int3(_S1173, int(_S1174), int(0));
                uint rows_12 = ((_S1170 | (uint(round((((&kernelContext_2)->occlusionTexture_0).read(vec<uint,2>(((_S1175)).xy), uint(((_S1175)).z))).x * 255.0f)) << 8U)) + columns_12 - 1U) / columns_12;
                uint metadataRows_12 = (10U + 2U * _S1133 * _S1139 + atlasWidth_12 - 1U) / atlasWidth_12;
                uint _S1176 = atlasWidth_12 / columns_12;
                float _S1177 = float(_S1176);
                uint _S1178 = (atlasHeight_12 - metadataRows_12) / rows_12;
                float2 cellSize_24 = float2(_S1177, float(_S1178));
                uint _S1179 = slot_25 % columns_12;
                float _S1180 = float(_S1179);
                uint _S1181 = slot_25 / columns_12;
                _S213 = (((&kernelContext_2)->occlusionTexture_0).sample(((&kernelContext_2)->occlusionSampler_0), ((float2(_S1180, float(_S1181)) * cellSize_24 + float2(1.5f, float(metadataRows_12) + 1.5f) + fract(_S212) * max(cellSize_24 - float2(3.0f) , float2(0.0f) )) / float2(float(atlasWidth_12), float(atlasHeight_12)))));
                break;
            }
            int2 _S1182 = int2(metadata_12.zw);
            int2 tile_25 = int2(floor(_S212)) - int2(metadata_12.xy);
            if(any(tile_25 < (int2(int(0)) )))
            {
                hasSceneLighting_0 = true;
            }
            else
            {
                hasSceneLighting_0 = any(tile_25 >= _S1182);
            }
            if(hasSceneLighting_0)
            {
                int3 _S1183 = int3(int(min(1U, atlasWidth_12 - 1U)), int(0), int(0));
                _S213 = (((&kernelContext_2)->occlusionTexture_0).read(vec<uint,2>(((_S1183)).xy), uint(((_S1183)).z)));
                break;
            }
            uint _S1184 = atlasWidth_12 / uint(_S1182.x);
            float _S1185 = float(_S1184);
            uint _S1186 = (atlasHeight_12 - 1U) / uint(_S1182.y);
            float2 cellSize_25 = float2(_S1185, float(_S1186));
            _S213 = (((&kernelContext_2)->occlusionTexture_0).sample(((&kernelContext_2)->occlusionSampler_0), ((float2(tile_25) * cellSize_25 + float2(1.5f, 2.5f) + fract(_S212) * max(cellSize_25 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_12), float(atlasHeight_12)))));
            break;
        }
        for(;;)
        {
            float4 _S1187 = float4(_S40->compositeControls_0) ;
            if((_S1187.x) != 128.0f)
            {
                break;
            }
            bool _S1188 = (_S1187.w) >= 0.5f;
            for(;;)
            {
                if(!_S1188)
                {
                    _S521 = (((&kernelContext_2)->compositeTexture_0).sample(((&kernelContext_2)->compositeSampler_0), (_S212)));
                    break;
                }
                texture2d<float, access::sample> _S1189 = (&kernelContext_2)->compositeTexture_0;
                thread uint atlasWidth_13;
                thread uint atlasHeight_13;
                (*((&atlasWidth_13)) = (_S1189).get_width(0)),(*((&atlasHeight_13)) = (_S1189).get_height(0));
                int3 _S1190 = int3(int(0), int(0), int(0));
                float4 metadata_13 = round((((&kernelContext_2)->compositeTexture_0).read(vec<uint,2>(((_S1190)).xy), uint(((_S1190)).z))) * float4(255.0f) );
                if((metadata_13.x) == 255.0f)
                {
                    uint _S1191 = atlasWidth_13;
                    uint _S1192 = 2U % atlasWidth_13;
                    int _S1193 = int(_S1192);
                    uint _S1194 = 2U / _S1191;
                    int3 _S1195 = int3(_S1193, int(_S1194), int(0));
                    int _S1196 = int(uint(round((((&kernelContext_2)->compositeTexture_0).read(vec<uint,2>(((_S1195)).xy), uint(((_S1195)).z))).x * 255.0f)));
                    uint _S1197 = atlasWidth_13;
                    uint _S1198 = 3U % atlasWidth_13;
                    int _S1199 = int(_S1198);
                    uint _S1200 = 3U / _S1197;
                    int3 _S1201 = int3(_S1199, int(_S1200), int(0));
                    int2 minimumTile_13 = int2(_S1196, int(uint(round((((&kernelContext_2)->compositeTexture_0).read(vec<uint,2>(((_S1201)).xy), uint(((_S1201)).z))).x * 255.0f))));
                    uint _S1202 = atlasWidth_13;
                    uint _S1203 = 4U % atlasWidth_13;
                    int _S1204 = int(_S1203);
                    uint _S1205 = 4U / _S1202;
                    int3 _S1206 = int3(_S1204, int(_S1205), int(0));
                    uint _S1207 = uint(round((((&kernelContext_2)->compositeTexture_0).read(vec<uint,2>(((_S1206)).xy), uint(((_S1206)).z))).x * 255.0f));
                    uint _S1208 = atlasWidth_13;
                    uint _S1209 = 5U % atlasWidth_13;
                    int _S1210 = int(_S1209);
                    uint _S1211 = 5U / _S1208;
                    int3 _S1212 = int3(_S1210, int(_S1211), int(0));
                    uint _S1213 = uint(round((((&kernelContext_2)->compositeTexture_0).read(vec<uint,2>(((_S1212)).xy), uint(((_S1212)).z))).x * 255.0f));
                    uint2 grid_13 = uint2(_S1207, _S1213);
                    int2 tile_26 = int2(floor(_S212)) - minimumTile_13;
                    if(any(tile_26 < (int2(int(0)) )))
                    {
                        hasSceneLighting_0 = true;
                    }
                    else
                    {
                        hasSceneLighting_0 = any(tile_26 >= int2(grid_13));
                    }
                    if(hasSceneLighting_0)
                    {
                        int3 _S1214 = int3(int(1), int(0), int(0));
                        _S521 = (((&kernelContext_2)->compositeTexture_0).read(vec<uint,2>(((_S1214)).xy), uint(((_S1214)).z)));
                        break;
                    }
                    uint offset_13 = 10U + 2U * (uint(tile_26.y) * _S1207 + uint(tile_26.x));
                    uint _S1215 = atlasWidth_13;
                    uint _S1216 = offset_13 % atlasWidth_13;
                    int _S1217 = int(_S1216);
                    uint _S1218 = offset_13 / _S1215;
                    int3 _S1219 = int3(_S1217, int(_S1218), int(0));
                    uint _S1220 = uint(round((((&kernelContext_2)->compositeTexture_0).read(vec<uint,2>(((_S1219)).xy), uint(((_S1219)).z))).x * 255.0f));
                    uint _S1221 = atlasWidth_13;
                    uint _S1222 = offset_13 + 1U;
                    uint _S1223 = _S1222 % atlasWidth_13;
                    int _S1224 = int(_S1223);
                    uint _S1225 = _S1222 / _S1221;
                    int3 _S1226 = int3(_S1224, int(_S1225), int(0));
                    uint slot_26 = _S1220 | (uint(round((((&kernelContext_2)->compositeTexture_0).read(vec<uint,2>(((_S1226)).xy), uint(((_S1226)).z))).x * 255.0f)) << 8U);
                    if(slot_26 == 0U)
                    {
                        int3 _S1227 = int3(int(1), int(0), int(0));
                        _S521 = (((&kernelContext_2)->compositeTexture_0).read(vec<uint,2>(((_S1227)).xy), uint(((_S1227)).z)));
                        break;
                    }
                    uint slot_27 = slot_26 - 1U;
                    uint _S1228 = atlasWidth_13;
                    uint _S1229 = 6U % atlasWidth_13;
                    int _S1230 = int(_S1229);
                    uint _S1231 = 6U / _S1228;
                    int3 _S1232 = int3(_S1230, int(_S1231), int(0));
                    uint _S1233 = uint(round((((&kernelContext_2)->compositeTexture_0).read(vec<uint,2>(((_S1232)).xy), uint(((_S1232)).z))).x * 255.0f));
                    uint _S1234 = atlasWidth_13;
                    uint _S1235 = 7U % atlasWidth_13;
                    int _S1236 = int(_S1235);
                    uint _S1237 = 7U / _S1234;
                    int3 _S1238 = int3(_S1236, int(_S1237), int(0));
                    uint columns_13 = _S1233 | (uint(round((((&kernelContext_2)->compositeTexture_0).read(vec<uint,2>(((_S1238)).xy), uint(((_S1238)).z))).x * 255.0f)) << 8U);
                    uint _S1239 = atlasWidth_13;
                    uint _S1240 = 8U % atlasWidth_13;
                    int _S1241 = int(_S1240);
                    uint _S1242 = 8U / _S1239;
                    int3 _S1243 = int3(_S1241, int(_S1242), int(0));
                    uint _S1244 = uint(round((((&kernelContext_2)->compositeTexture_0).read(vec<uint,2>(((_S1243)).xy), uint(((_S1243)).z))).x * 255.0f));
                    uint _S1245 = atlasWidth_13;
                    uint _S1246 = 9U % atlasWidth_13;
                    int _S1247 = int(_S1246);
                    uint _S1248 = 9U / _S1245;
                    int3 _S1249 = int3(_S1247, int(_S1248), int(0));
                    uint rows_13 = ((_S1244 | (uint(round((((&kernelContext_2)->compositeTexture_0).read(vec<uint,2>(((_S1249)).xy), uint(((_S1249)).z))).x * 255.0f)) << 8U)) + columns_13 - 1U) / columns_13;
                    uint metadataRows_13 = (10U + 2U * _S1207 * _S1213 + atlasWidth_13 - 1U) / atlasWidth_13;
                    uint _S1250 = atlasWidth_13 / columns_13;
                    float _S1251 = float(_S1250);
                    uint _S1252 = (atlasHeight_13 - metadataRows_13) / rows_13;
                    float2 cellSize_26 = float2(_S1251, float(_S1252));
                    uint _S1253 = slot_27 % columns_13;
                    float _S1254 = float(_S1253);
                    uint _S1255 = slot_27 / columns_13;
                    _S521 = (((&kernelContext_2)->compositeTexture_0).sample(((&kernelContext_2)->compositeSampler_0), ((float2(_S1254, float(_S1255)) * cellSize_26 + float2(1.5f, float(metadataRows_13) + 1.5f) + fract(_S212) * max(cellSize_26 - float2(3.0f) , float2(0.0f) )) / float2(float(atlasWidth_13), float(atlasHeight_13)))));
                    break;
                }
                int2 _S1256 = int2(metadata_13.zw);
                int2 tile_27 = int2(floor(_S212)) - int2(metadata_13.xy);
                if(any(tile_27 < (int2(int(0)) )))
                {
                    hasSceneLighting_0 = true;
                }
                else
                {
                    hasSceneLighting_0 = any(tile_27 >= _S1256);
                }
                if(hasSceneLighting_0)
                {
                    int3 _S1257 = int3(int(min(1U, atlasWidth_13 - 1U)), int(0), int(0));
                    _S521 = (((&kernelContext_2)->compositeTexture_0).read(vec<uint,2>(((_S1257)).xy), uint(((_S1257)).z)));
                    break;
                }
                uint _S1258 = atlasWidth_13 / uint(_S1256.x);
                float _S1259 = float(_S1258);
                uint _S1260 = (atlasHeight_13 - 1U) / uint(_S1256.y);
                float2 cellSize_27 = float2(_S1259, float(_S1260));
                _S521 = (((&kernelContext_2)->compositeTexture_0).sample(((&kernelContext_2)->compositeSampler_0), ((float2(tile_27) * cellSize_27 + float2(1.5f, 2.5f) + fract(_S212) * max(cellSize_27 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_13), float(atlasHeight_13)))));
                break;
            }
            uint operation_5 = uint(round(_S1187.y));
            if(operation_5 == 1U)
            {
                _S213 = _S213 * _S521;
                break;
            }
            if(operation_5 == 2U)
            {
                _S213 = _S213 + _S521;
                break;
            }
            if(operation_5 == 3U)
            {
                _S213 = _S213 - _S521;
                break;
            }
            if(operation_5 == 4U)
            {
                float factor_5 = _S1187.z;
                _S213 = _S213 * float4((1.0f - factor_5))  + _S521 * float4(factor_5) ;
                break;
            }
            break;
        }
        occlusion_0 = saturate(_S213.x);
    }
    else
    {
        occlusion_0 = _S195;
    }
    float3 specularColor_0;
    if((textureMask_0 & 256U) != 0U)
    {
        bool _S1261 = (udimMask_0 & 256U) != 0U;
        for(;;)
        {
            if(!_S1261)
            {
                _S213 = (((&kernelContext_2)->specularColorTexture_0).sample(((&kernelContext_2)->specularColorSampler_0), (_S212)));
                break;
            }
            texture2d<float, access::sample> _S1262 = (&kernelContext_2)->specularColorTexture_0;
            thread uint atlasWidth_14;
            thread uint atlasHeight_14;
            (*((&atlasWidth_14)) = (_S1262).get_width(0)),(*((&atlasHeight_14)) = (_S1262).get_height(0));
            int3 _S1263 = int3(int(0), int(0), int(0));
            float4 metadata_14 = round((((&kernelContext_2)->specularColorTexture_0).read(vec<uint,2>(((_S1263)).xy), uint(((_S1263)).z))) * float4(255.0f) );
            if((metadata_14.x) == 255.0f)
            {
                uint _S1264 = atlasWidth_14;
                uint _S1265 = 2U % atlasWidth_14;
                int _S1266 = int(_S1265);
                uint _S1267 = 2U / _S1264;
                int3 _S1268 = int3(_S1266, int(_S1267), int(0));
                int _S1269 = int(uint(round((((&kernelContext_2)->specularColorTexture_0).read(vec<uint,2>(((_S1268)).xy), uint(((_S1268)).z))).x * 255.0f)));
                uint _S1270 = atlasWidth_14;
                uint _S1271 = 3U % atlasWidth_14;
                int _S1272 = int(_S1271);
                uint _S1273 = 3U / _S1270;
                int3 _S1274 = int3(_S1272, int(_S1273), int(0));
                int2 minimumTile_14 = int2(_S1269, int(uint(round((((&kernelContext_2)->specularColorTexture_0).read(vec<uint,2>(((_S1274)).xy), uint(((_S1274)).z))).x * 255.0f))));
                uint _S1275 = atlasWidth_14;
                uint _S1276 = 4U % atlasWidth_14;
                int _S1277 = int(_S1276);
                uint _S1278 = 4U / _S1275;
                int3 _S1279 = int3(_S1277, int(_S1278), int(0));
                uint _S1280 = uint(round((((&kernelContext_2)->specularColorTexture_0).read(vec<uint,2>(((_S1279)).xy), uint(((_S1279)).z))).x * 255.0f));
                uint _S1281 = atlasWidth_14;
                uint _S1282 = 5U % atlasWidth_14;
                int _S1283 = int(_S1282);
                uint _S1284 = 5U / _S1281;
                int3 _S1285 = int3(_S1283, int(_S1284), int(0));
                uint _S1286 = uint(round((((&kernelContext_2)->specularColorTexture_0).read(vec<uint,2>(((_S1285)).xy), uint(((_S1285)).z))).x * 255.0f));
                uint2 grid_14 = uint2(_S1280, _S1286);
                int2 tile_28 = int2(floor(_S212)) - minimumTile_14;
                if(any(tile_28 < (int2(int(0)) )))
                {
                    hasSceneLighting_0 = true;
                }
                else
                {
                    hasSceneLighting_0 = any(tile_28 >= int2(grid_14));
                }
                if(hasSceneLighting_0)
                {
                    int3 _S1287 = int3(int(1), int(0), int(0));
                    _S213 = (((&kernelContext_2)->specularColorTexture_0).read(vec<uint,2>(((_S1287)).xy), uint(((_S1287)).z)));
                    break;
                }
                uint offset_14 = 10U + 2U * (uint(tile_28.y) * _S1280 + uint(tile_28.x));
                uint _S1288 = atlasWidth_14;
                uint _S1289 = offset_14 % atlasWidth_14;
                int _S1290 = int(_S1289);
                uint _S1291 = offset_14 / _S1288;
                int3 _S1292 = int3(_S1290, int(_S1291), int(0));
                uint _S1293 = uint(round((((&kernelContext_2)->specularColorTexture_0).read(vec<uint,2>(((_S1292)).xy), uint(((_S1292)).z))).x * 255.0f));
                uint _S1294 = atlasWidth_14;
                uint _S1295 = offset_14 + 1U;
                uint _S1296 = _S1295 % atlasWidth_14;
                int _S1297 = int(_S1296);
                uint _S1298 = _S1295 / _S1294;
                int3 _S1299 = int3(_S1297, int(_S1298), int(0));
                uint slot_28 = _S1293 | (uint(round((((&kernelContext_2)->specularColorTexture_0).read(vec<uint,2>(((_S1299)).xy), uint(((_S1299)).z))).x * 255.0f)) << 8U);
                if(slot_28 == 0U)
                {
                    int3 _S1300 = int3(int(1), int(0), int(0));
                    _S213 = (((&kernelContext_2)->specularColorTexture_0).read(vec<uint,2>(((_S1300)).xy), uint(((_S1300)).z)));
                    break;
                }
                uint slot_29 = slot_28 - 1U;
                uint _S1301 = atlasWidth_14;
                uint _S1302 = 6U % atlasWidth_14;
                int _S1303 = int(_S1302);
                uint _S1304 = 6U / _S1301;
                int3 _S1305 = int3(_S1303, int(_S1304), int(0));
                uint _S1306 = uint(round((((&kernelContext_2)->specularColorTexture_0).read(vec<uint,2>(((_S1305)).xy), uint(((_S1305)).z))).x * 255.0f));
                uint _S1307 = atlasWidth_14;
                uint _S1308 = 7U % atlasWidth_14;
                int _S1309 = int(_S1308);
                uint _S1310 = 7U / _S1307;
                int3 _S1311 = int3(_S1309, int(_S1310), int(0));
                uint columns_14 = _S1306 | (uint(round((((&kernelContext_2)->specularColorTexture_0).read(vec<uint,2>(((_S1311)).xy), uint(((_S1311)).z))).x * 255.0f)) << 8U);
                uint _S1312 = atlasWidth_14;
                uint _S1313 = 8U % atlasWidth_14;
                int _S1314 = int(_S1313);
                uint _S1315 = 8U / _S1312;
                int3 _S1316 = int3(_S1314, int(_S1315), int(0));
                uint _S1317 = uint(round((((&kernelContext_2)->specularColorTexture_0).read(vec<uint,2>(((_S1316)).xy), uint(((_S1316)).z))).x * 255.0f));
                uint _S1318 = atlasWidth_14;
                uint _S1319 = 9U % atlasWidth_14;
                int _S1320 = int(_S1319);
                uint _S1321 = 9U / _S1318;
                int3 _S1322 = int3(_S1320, int(_S1321), int(0));
                uint rows_14 = ((_S1317 | (uint(round((((&kernelContext_2)->specularColorTexture_0).read(vec<uint,2>(((_S1322)).xy), uint(((_S1322)).z))).x * 255.0f)) << 8U)) + columns_14 - 1U) / columns_14;
                uint metadataRows_14 = (10U + 2U * _S1280 * _S1286 + atlasWidth_14 - 1U) / atlasWidth_14;
                uint _S1323 = atlasWidth_14 / columns_14;
                float _S1324 = float(_S1323);
                uint _S1325 = (atlasHeight_14 - metadataRows_14) / rows_14;
                float2 cellSize_28 = float2(_S1324, float(_S1325));
                uint _S1326 = slot_29 % columns_14;
                float _S1327 = float(_S1326);
                uint _S1328 = slot_29 / columns_14;
                _S213 = (((&kernelContext_2)->specularColorTexture_0).sample(((&kernelContext_2)->specularColorSampler_0), ((float2(_S1327, float(_S1328)) * cellSize_28 + float2(1.5f, float(metadataRows_14) + 1.5f) + fract(_S212) * max(cellSize_28 - float2(3.0f) , float2(0.0f) )) / float2(float(atlasWidth_14), float(atlasHeight_14)))));
                break;
            }
            int2 _S1329 = int2(metadata_14.zw);
            int2 tile_29 = int2(floor(_S212)) - int2(metadata_14.xy);
            if(any(tile_29 < (int2(int(0)) )))
            {
                hasSceneLighting_0 = true;
            }
            else
            {
                hasSceneLighting_0 = any(tile_29 >= _S1329);
            }
            if(hasSceneLighting_0)
            {
                int3 _S1330 = int3(int(min(1U, atlasWidth_14 - 1U)), int(0), int(0));
                _S213 = (((&kernelContext_2)->specularColorTexture_0).read(vec<uint,2>(((_S1330)).xy), uint(((_S1330)).z)));
                break;
            }
            uint _S1331 = atlasWidth_14 / uint(_S1329.x);
            float _S1332 = float(_S1331);
            uint _S1333 = (atlasHeight_14 - 1U) / uint(_S1329.y);
            float2 cellSize_29 = float2(_S1332, float(_S1333));
            _S213 = (((&kernelContext_2)->specularColorTexture_0).sample(((&kernelContext_2)->specularColorSampler_0), ((float2(tile_29) * cellSize_29 + float2(1.5f, 2.5f) + fract(_S212) * max(cellSize_29 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_14), float(atlasHeight_14)))));
            break;
        }
        for(;;)
        {
            float4 _S1334 = float4(_S40->compositeControls_0) ;
            if((_S1334.x) != 256.0f)
            {
                break;
            }
            bool _S1335 = (_S1334.w) >= 0.5f;
            for(;;)
            {
                if(!_S1335)
                {
                    _S521 = (((&kernelContext_2)->compositeTexture_0).sample(((&kernelContext_2)->compositeSampler_0), (_S212)));
                    break;
                }
                texture2d<float, access::sample> _S1336 = (&kernelContext_2)->compositeTexture_0;
                thread uint atlasWidth_15;
                thread uint atlasHeight_15;
                (*((&atlasWidth_15)) = (_S1336).get_width(0)),(*((&atlasHeight_15)) = (_S1336).get_height(0));
                int3 _S1337 = int3(int(0), int(0), int(0));
                float4 metadata_15 = round((((&kernelContext_2)->compositeTexture_0).read(vec<uint,2>(((_S1337)).xy), uint(((_S1337)).z))) * float4(255.0f) );
                if((metadata_15.x) == 255.0f)
                {
                    uint _S1338 = atlasWidth_15;
                    uint _S1339 = 2U % atlasWidth_15;
                    int _S1340 = int(_S1339);
                    uint _S1341 = 2U / _S1338;
                    int3 _S1342 = int3(_S1340, int(_S1341), int(0));
                    int _S1343 = int(uint(round((((&kernelContext_2)->compositeTexture_0).read(vec<uint,2>(((_S1342)).xy), uint(((_S1342)).z))).x * 255.0f)));
                    uint _S1344 = atlasWidth_15;
                    uint _S1345 = 3U % atlasWidth_15;
                    int _S1346 = int(_S1345);
                    uint _S1347 = 3U / _S1344;
                    int3 _S1348 = int3(_S1346, int(_S1347), int(0));
                    int2 minimumTile_15 = int2(_S1343, int(uint(round((((&kernelContext_2)->compositeTexture_0).read(vec<uint,2>(((_S1348)).xy), uint(((_S1348)).z))).x * 255.0f))));
                    uint _S1349 = atlasWidth_15;
                    uint _S1350 = 4U % atlasWidth_15;
                    int _S1351 = int(_S1350);
                    uint _S1352 = 4U / _S1349;
                    int3 _S1353 = int3(_S1351, int(_S1352), int(0));
                    uint _S1354 = uint(round((((&kernelContext_2)->compositeTexture_0).read(vec<uint,2>(((_S1353)).xy), uint(((_S1353)).z))).x * 255.0f));
                    uint _S1355 = atlasWidth_15;
                    uint _S1356 = 5U % atlasWidth_15;
                    int _S1357 = int(_S1356);
                    uint _S1358 = 5U / _S1355;
                    int3 _S1359 = int3(_S1357, int(_S1358), int(0));
                    uint _S1360 = uint(round((((&kernelContext_2)->compositeTexture_0).read(vec<uint,2>(((_S1359)).xy), uint(((_S1359)).z))).x * 255.0f));
                    uint2 grid_15 = uint2(_S1354, _S1360);
                    int2 tile_30 = int2(floor(_S212)) - minimumTile_15;
                    if(any(tile_30 < (int2(int(0)) )))
                    {
                        hasSceneLighting_0 = true;
                    }
                    else
                    {
                        hasSceneLighting_0 = any(tile_30 >= int2(grid_15));
                    }
                    if(hasSceneLighting_0)
                    {
                        int3 _S1361 = int3(int(1), int(0), int(0));
                        _S521 = (((&kernelContext_2)->compositeTexture_0).read(vec<uint,2>(((_S1361)).xy), uint(((_S1361)).z)));
                        break;
                    }
                    uint offset_15 = 10U + 2U * (uint(tile_30.y) * _S1354 + uint(tile_30.x));
                    uint _S1362 = atlasWidth_15;
                    uint _S1363 = offset_15 % atlasWidth_15;
                    int _S1364 = int(_S1363);
                    uint _S1365 = offset_15 / _S1362;
                    int3 _S1366 = int3(_S1364, int(_S1365), int(0));
                    uint _S1367 = uint(round((((&kernelContext_2)->compositeTexture_0).read(vec<uint,2>(((_S1366)).xy), uint(((_S1366)).z))).x * 255.0f));
                    uint _S1368 = atlasWidth_15;
                    uint _S1369 = offset_15 + 1U;
                    uint _S1370 = _S1369 % atlasWidth_15;
                    int _S1371 = int(_S1370);
                    uint _S1372 = _S1369 / _S1368;
                    int3 _S1373 = int3(_S1371, int(_S1372), int(0));
                    uint slot_30 = _S1367 | (uint(round((((&kernelContext_2)->compositeTexture_0).read(vec<uint,2>(((_S1373)).xy), uint(((_S1373)).z))).x * 255.0f)) << 8U);
                    if(slot_30 == 0U)
                    {
                        int3 _S1374 = int3(int(1), int(0), int(0));
                        _S521 = (((&kernelContext_2)->compositeTexture_0).read(vec<uint,2>(((_S1374)).xy), uint(((_S1374)).z)));
                        break;
                    }
                    uint slot_31 = slot_30 - 1U;
                    uint _S1375 = atlasWidth_15;
                    uint _S1376 = 6U % atlasWidth_15;
                    int _S1377 = int(_S1376);
                    uint _S1378 = 6U / _S1375;
                    int3 _S1379 = int3(_S1377, int(_S1378), int(0));
                    uint _S1380 = uint(round((((&kernelContext_2)->compositeTexture_0).read(vec<uint,2>(((_S1379)).xy), uint(((_S1379)).z))).x * 255.0f));
                    uint _S1381 = atlasWidth_15;
                    uint _S1382 = 7U % atlasWidth_15;
                    int _S1383 = int(_S1382);
                    uint _S1384 = 7U / _S1381;
                    int3 _S1385 = int3(_S1383, int(_S1384), int(0));
                    uint columns_15 = _S1380 | (uint(round((((&kernelContext_2)->compositeTexture_0).read(vec<uint,2>(((_S1385)).xy), uint(((_S1385)).z))).x * 255.0f)) << 8U);
                    uint _S1386 = atlasWidth_15;
                    uint _S1387 = 8U % atlasWidth_15;
                    int _S1388 = int(_S1387);
                    uint _S1389 = 8U / _S1386;
                    int3 _S1390 = int3(_S1388, int(_S1389), int(0));
                    uint _S1391 = uint(round((((&kernelContext_2)->compositeTexture_0).read(vec<uint,2>(((_S1390)).xy), uint(((_S1390)).z))).x * 255.0f));
                    uint _S1392 = atlasWidth_15;
                    uint _S1393 = 9U % atlasWidth_15;
                    int _S1394 = int(_S1393);
                    uint _S1395 = 9U / _S1392;
                    int3 _S1396 = int3(_S1394, int(_S1395), int(0));
                    uint rows_15 = ((_S1391 | (uint(round((((&kernelContext_2)->compositeTexture_0).read(vec<uint,2>(((_S1396)).xy), uint(((_S1396)).z))).x * 255.0f)) << 8U)) + columns_15 - 1U) / columns_15;
                    uint metadataRows_15 = (10U + 2U * _S1354 * _S1360 + atlasWidth_15 - 1U) / atlasWidth_15;
                    uint _S1397 = atlasWidth_15 / columns_15;
                    float _S1398 = float(_S1397);
                    uint _S1399 = (atlasHeight_15 - metadataRows_15) / rows_15;
                    float2 cellSize_30 = float2(_S1398, float(_S1399));
                    uint _S1400 = slot_31 % columns_15;
                    float _S1401 = float(_S1400);
                    uint _S1402 = slot_31 / columns_15;
                    _S521 = (((&kernelContext_2)->compositeTexture_0).sample(((&kernelContext_2)->compositeSampler_0), ((float2(_S1401, float(_S1402)) * cellSize_30 + float2(1.5f, float(metadataRows_15) + 1.5f) + fract(_S212) * max(cellSize_30 - float2(3.0f) , float2(0.0f) )) / float2(float(atlasWidth_15), float(atlasHeight_15)))));
                    break;
                }
                int2 _S1403 = int2(metadata_15.zw);
                int2 tile_31 = int2(floor(_S212)) - int2(metadata_15.xy);
                if(any(tile_31 < (int2(int(0)) )))
                {
                    hasSceneLighting_0 = true;
                }
                else
                {
                    hasSceneLighting_0 = any(tile_31 >= _S1403);
                }
                if(hasSceneLighting_0)
                {
                    int3 _S1404 = int3(int(min(1U, atlasWidth_15 - 1U)), int(0), int(0));
                    _S521 = (((&kernelContext_2)->compositeTexture_0).read(vec<uint,2>(((_S1404)).xy), uint(((_S1404)).z)));
                    break;
                }
                uint _S1405 = atlasWidth_15 / uint(_S1403.x);
                float _S1406 = float(_S1405);
                uint _S1407 = (atlasHeight_15 - 1U) / uint(_S1403.y);
                float2 cellSize_31 = float2(_S1406, float(_S1407));
                _S521 = (((&kernelContext_2)->compositeTexture_0).sample(((&kernelContext_2)->compositeSampler_0), ((float2(tile_31) * cellSize_31 + float2(1.5f, 2.5f) + fract(_S212) * max(cellSize_31 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_15), float(atlasHeight_15)))));
                break;
            }
            uint operation_6 = uint(round(_S1334.y));
            if(operation_6 == 1U)
            {
                _S213 = _S213 * _S521;
                break;
            }
            if(operation_6 == 2U)
            {
                _S213 = _S213 + _S521;
                break;
            }
            if(operation_6 == 3U)
            {
                _S213 = _S213 - _S521;
                break;
            }
            if(operation_6 == 4U)
            {
                float factor_6 = _S1334.z;
                _S213 = _S213 * float4((1.0f - factor_6))  + _S521 * float4(factor_6) ;
                break;
            }
            break;
        }
        specularColor_0 = saturate(_S213.xyz);
    }
    else
    {
        specularColor_0 = _S203;
    }
    float clearcoatAmount_0;
    if((textureMask_0 & 512U) != 0U)
    {
        bool _S1408 = (udimMask_0 & 512U) != 0U;
        for(;;)
        {
            if(!_S1408)
            {
                _S213 = (((&kernelContext_2)->clearcoatTexture_0).sample(((&kernelContext_2)->clearcoatSampler_0), (_S212)));
                break;
            }
            texture2d<float, access::sample> _S1409 = (&kernelContext_2)->clearcoatTexture_0;
            thread uint atlasWidth_16;
            thread uint atlasHeight_16;
            (*((&atlasWidth_16)) = (_S1409).get_width(0)),(*((&atlasHeight_16)) = (_S1409).get_height(0));
            int3 _S1410 = int3(int(0), int(0), int(0));
            float4 metadata_16 = round((((&kernelContext_2)->clearcoatTexture_0).read(vec<uint,2>(((_S1410)).xy), uint(((_S1410)).z))) * float4(255.0f) );
            if((metadata_16.x) == 255.0f)
            {
                uint _S1411 = atlasWidth_16;
                uint _S1412 = 2U % atlasWidth_16;
                int _S1413 = int(_S1412);
                uint _S1414 = 2U / _S1411;
                int3 _S1415 = int3(_S1413, int(_S1414), int(0));
                int _S1416 = int(uint(round((((&kernelContext_2)->clearcoatTexture_0).read(vec<uint,2>(((_S1415)).xy), uint(((_S1415)).z))).x * 255.0f)));
                uint _S1417 = atlasWidth_16;
                uint _S1418 = 3U % atlasWidth_16;
                int _S1419 = int(_S1418);
                uint _S1420 = 3U / _S1417;
                int3 _S1421 = int3(_S1419, int(_S1420), int(0));
                int2 minimumTile_16 = int2(_S1416, int(uint(round((((&kernelContext_2)->clearcoatTexture_0).read(vec<uint,2>(((_S1421)).xy), uint(((_S1421)).z))).x * 255.0f))));
                uint _S1422 = atlasWidth_16;
                uint _S1423 = 4U % atlasWidth_16;
                int _S1424 = int(_S1423);
                uint _S1425 = 4U / _S1422;
                int3 _S1426 = int3(_S1424, int(_S1425), int(0));
                uint _S1427 = uint(round((((&kernelContext_2)->clearcoatTexture_0).read(vec<uint,2>(((_S1426)).xy), uint(((_S1426)).z))).x * 255.0f));
                uint _S1428 = atlasWidth_16;
                uint _S1429 = 5U % atlasWidth_16;
                int _S1430 = int(_S1429);
                uint _S1431 = 5U / _S1428;
                int3 _S1432 = int3(_S1430, int(_S1431), int(0));
                uint _S1433 = uint(round((((&kernelContext_2)->clearcoatTexture_0).read(vec<uint,2>(((_S1432)).xy), uint(((_S1432)).z))).x * 255.0f));
                uint2 grid_16 = uint2(_S1427, _S1433);
                int2 tile_32 = int2(floor(_S212)) - minimumTile_16;
                if(any(tile_32 < (int2(int(0)) )))
                {
                    hasSceneLighting_0 = true;
                }
                else
                {
                    hasSceneLighting_0 = any(tile_32 >= int2(grid_16));
                }
                if(hasSceneLighting_0)
                {
                    int3 _S1434 = int3(int(1), int(0), int(0));
                    _S213 = (((&kernelContext_2)->clearcoatTexture_0).read(vec<uint,2>(((_S1434)).xy), uint(((_S1434)).z)));
                    break;
                }
                uint offset_16 = 10U + 2U * (uint(tile_32.y) * _S1427 + uint(tile_32.x));
                uint _S1435 = atlasWidth_16;
                uint _S1436 = offset_16 % atlasWidth_16;
                int _S1437 = int(_S1436);
                uint _S1438 = offset_16 / _S1435;
                int3 _S1439 = int3(_S1437, int(_S1438), int(0));
                uint _S1440 = uint(round((((&kernelContext_2)->clearcoatTexture_0).read(vec<uint,2>(((_S1439)).xy), uint(((_S1439)).z))).x * 255.0f));
                uint _S1441 = atlasWidth_16;
                uint _S1442 = offset_16 + 1U;
                uint _S1443 = _S1442 % atlasWidth_16;
                int _S1444 = int(_S1443);
                uint _S1445 = _S1442 / _S1441;
                int3 _S1446 = int3(_S1444, int(_S1445), int(0));
                uint slot_32 = _S1440 | (uint(round((((&kernelContext_2)->clearcoatTexture_0).read(vec<uint,2>(((_S1446)).xy), uint(((_S1446)).z))).x * 255.0f)) << 8U);
                if(slot_32 == 0U)
                {
                    int3 _S1447 = int3(int(1), int(0), int(0));
                    _S213 = (((&kernelContext_2)->clearcoatTexture_0).read(vec<uint,2>(((_S1447)).xy), uint(((_S1447)).z)));
                    break;
                }
                uint slot_33 = slot_32 - 1U;
                uint _S1448 = atlasWidth_16;
                uint _S1449 = 6U % atlasWidth_16;
                int _S1450 = int(_S1449);
                uint _S1451 = 6U / _S1448;
                int3 _S1452 = int3(_S1450, int(_S1451), int(0));
                uint _S1453 = uint(round((((&kernelContext_2)->clearcoatTexture_0).read(vec<uint,2>(((_S1452)).xy), uint(((_S1452)).z))).x * 255.0f));
                uint _S1454 = atlasWidth_16;
                uint _S1455 = 7U % atlasWidth_16;
                int _S1456 = int(_S1455);
                uint _S1457 = 7U / _S1454;
                int3 _S1458 = int3(_S1456, int(_S1457), int(0));
                uint columns_16 = _S1453 | (uint(round((((&kernelContext_2)->clearcoatTexture_0).read(vec<uint,2>(((_S1458)).xy), uint(((_S1458)).z))).x * 255.0f)) << 8U);
                uint _S1459 = atlasWidth_16;
                uint _S1460 = 8U % atlasWidth_16;
                int _S1461 = int(_S1460);
                uint _S1462 = 8U / _S1459;
                int3 _S1463 = int3(_S1461, int(_S1462), int(0));
                uint _S1464 = uint(round((((&kernelContext_2)->clearcoatTexture_0).read(vec<uint,2>(((_S1463)).xy), uint(((_S1463)).z))).x * 255.0f));
                uint _S1465 = atlasWidth_16;
                uint _S1466 = 9U % atlasWidth_16;
                int _S1467 = int(_S1466);
                uint _S1468 = 9U / _S1465;
                int3 _S1469 = int3(_S1467, int(_S1468), int(0));
                uint rows_16 = ((_S1464 | (uint(round((((&kernelContext_2)->clearcoatTexture_0).read(vec<uint,2>(((_S1469)).xy), uint(((_S1469)).z))).x * 255.0f)) << 8U)) + columns_16 - 1U) / columns_16;
                uint metadataRows_16 = (10U + 2U * _S1427 * _S1433 + atlasWidth_16 - 1U) / atlasWidth_16;
                uint _S1470 = atlasWidth_16 / columns_16;
                float _S1471 = float(_S1470);
                uint _S1472 = (atlasHeight_16 - metadataRows_16) / rows_16;
                float2 cellSize_32 = float2(_S1471, float(_S1472));
                uint _S1473 = slot_33 % columns_16;
                float _S1474 = float(_S1473);
                uint _S1475 = slot_33 / columns_16;
                _S213 = (((&kernelContext_2)->clearcoatTexture_0).sample(((&kernelContext_2)->clearcoatSampler_0), ((float2(_S1474, float(_S1475)) * cellSize_32 + float2(1.5f, float(metadataRows_16) + 1.5f) + fract(_S212) * max(cellSize_32 - float2(3.0f) , float2(0.0f) )) / float2(float(atlasWidth_16), float(atlasHeight_16)))));
                break;
            }
            int2 _S1476 = int2(metadata_16.zw);
            int2 tile_33 = int2(floor(_S212)) - int2(metadata_16.xy);
            if(any(tile_33 < (int2(int(0)) )))
            {
                hasSceneLighting_0 = true;
            }
            else
            {
                hasSceneLighting_0 = any(tile_33 >= _S1476);
            }
            if(hasSceneLighting_0)
            {
                int3 _S1477 = int3(int(min(1U, atlasWidth_16 - 1U)), int(0), int(0));
                _S213 = (((&kernelContext_2)->clearcoatTexture_0).read(vec<uint,2>(((_S1477)).xy), uint(((_S1477)).z)));
                break;
            }
            uint _S1478 = atlasWidth_16 / uint(_S1476.x);
            float _S1479 = float(_S1478);
            uint _S1480 = (atlasHeight_16 - 1U) / uint(_S1476.y);
            float2 cellSize_33 = float2(_S1479, float(_S1480));
            _S213 = (((&kernelContext_2)->clearcoatTexture_0).sample(((&kernelContext_2)->clearcoatSampler_0), ((float2(tile_33) * cellSize_33 + float2(1.5f, 2.5f) + fract(_S212) * max(cellSize_33 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_16), float(atlasHeight_16)))));
            break;
        }
        for(;;)
        {
            float4 _S1481 = float4(_S40->compositeControls_0) ;
            if((_S1481.x) != 512.0f)
            {
                break;
            }
            bool _S1482 = (_S1481.w) >= 0.5f;
            for(;;)
            {
                if(!_S1482)
                {
                    _S521 = (((&kernelContext_2)->compositeTexture_0).sample(((&kernelContext_2)->compositeSampler_0), (_S212)));
                    break;
                }
                texture2d<float, access::sample> _S1483 = (&kernelContext_2)->compositeTexture_0;
                thread uint atlasWidth_17;
                thread uint atlasHeight_17;
                (*((&atlasWidth_17)) = (_S1483).get_width(0)),(*((&atlasHeight_17)) = (_S1483).get_height(0));
                int3 _S1484 = int3(int(0), int(0), int(0));
                float4 metadata_17 = round((((&kernelContext_2)->compositeTexture_0).read(vec<uint,2>(((_S1484)).xy), uint(((_S1484)).z))) * float4(255.0f) );
                if((metadata_17.x) == 255.0f)
                {
                    uint _S1485 = atlasWidth_17;
                    uint _S1486 = 2U % atlasWidth_17;
                    int _S1487 = int(_S1486);
                    uint _S1488 = 2U / _S1485;
                    int3 _S1489 = int3(_S1487, int(_S1488), int(0));
                    int _S1490 = int(uint(round((((&kernelContext_2)->compositeTexture_0).read(vec<uint,2>(((_S1489)).xy), uint(((_S1489)).z))).x * 255.0f)));
                    uint _S1491 = atlasWidth_17;
                    uint _S1492 = 3U % atlasWidth_17;
                    int _S1493 = int(_S1492);
                    uint _S1494 = 3U / _S1491;
                    int3 _S1495 = int3(_S1493, int(_S1494), int(0));
                    int2 minimumTile_17 = int2(_S1490, int(uint(round((((&kernelContext_2)->compositeTexture_0).read(vec<uint,2>(((_S1495)).xy), uint(((_S1495)).z))).x * 255.0f))));
                    uint _S1496 = atlasWidth_17;
                    uint _S1497 = 4U % atlasWidth_17;
                    int _S1498 = int(_S1497);
                    uint _S1499 = 4U / _S1496;
                    int3 _S1500 = int3(_S1498, int(_S1499), int(0));
                    uint _S1501 = uint(round((((&kernelContext_2)->compositeTexture_0).read(vec<uint,2>(((_S1500)).xy), uint(((_S1500)).z))).x * 255.0f));
                    uint _S1502 = atlasWidth_17;
                    uint _S1503 = 5U % atlasWidth_17;
                    int _S1504 = int(_S1503);
                    uint _S1505 = 5U / _S1502;
                    int3 _S1506 = int3(_S1504, int(_S1505), int(0));
                    uint _S1507 = uint(round((((&kernelContext_2)->compositeTexture_0).read(vec<uint,2>(((_S1506)).xy), uint(((_S1506)).z))).x * 255.0f));
                    uint2 grid_17 = uint2(_S1501, _S1507);
                    int2 tile_34 = int2(floor(_S212)) - minimumTile_17;
                    if(any(tile_34 < (int2(int(0)) )))
                    {
                        hasSceneLighting_0 = true;
                    }
                    else
                    {
                        hasSceneLighting_0 = any(tile_34 >= int2(grid_17));
                    }
                    if(hasSceneLighting_0)
                    {
                        int3 _S1508 = int3(int(1), int(0), int(0));
                        _S521 = (((&kernelContext_2)->compositeTexture_0).read(vec<uint,2>(((_S1508)).xy), uint(((_S1508)).z)));
                        break;
                    }
                    uint offset_17 = 10U + 2U * (uint(tile_34.y) * _S1501 + uint(tile_34.x));
                    uint _S1509 = atlasWidth_17;
                    uint _S1510 = offset_17 % atlasWidth_17;
                    int _S1511 = int(_S1510);
                    uint _S1512 = offset_17 / _S1509;
                    int3 _S1513 = int3(_S1511, int(_S1512), int(0));
                    uint _S1514 = uint(round((((&kernelContext_2)->compositeTexture_0).read(vec<uint,2>(((_S1513)).xy), uint(((_S1513)).z))).x * 255.0f));
                    uint _S1515 = atlasWidth_17;
                    uint _S1516 = offset_17 + 1U;
                    uint _S1517 = _S1516 % atlasWidth_17;
                    int _S1518 = int(_S1517);
                    uint _S1519 = _S1516 / _S1515;
                    int3 _S1520 = int3(_S1518, int(_S1519), int(0));
                    uint slot_34 = _S1514 | (uint(round((((&kernelContext_2)->compositeTexture_0).read(vec<uint,2>(((_S1520)).xy), uint(((_S1520)).z))).x * 255.0f)) << 8U);
                    if(slot_34 == 0U)
                    {
                        int3 _S1521 = int3(int(1), int(0), int(0));
                        _S521 = (((&kernelContext_2)->compositeTexture_0).read(vec<uint,2>(((_S1521)).xy), uint(((_S1521)).z)));
                        break;
                    }
                    uint slot_35 = slot_34 - 1U;
                    uint _S1522 = atlasWidth_17;
                    uint _S1523 = 6U % atlasWidth_17;
                    int _S1524 = int(_S1523);
                    uint _S1525 = 6U / _S1522;
                    int3 _S1526 = int3(_S1524, int(_S1525), int(0));
                    uint _S1527 = uint(round((((&kernelContext_2)->compositeTexture_0).read(vec<uint,2>(((_S1526)).xy), uint(((_S1526)).z))).x * 255.0f));
                    uint _S1528 = atlasWidth_17;
                    uint _S1529 = 7U % atlasWidth_17;
                    int _S1530 = int(_S1529);
                    uint _S1531 = 7U / _S1528;
                    int3 _S1532 = int3(_S1530, int(_S1531), int(0));
                    uint columns_17 = _S1527 | (uint(round((((&kernelContext_2)->compositeTexture_0).read(vec<uint,2>(((_S1532)).xy), uint(((_S1532)).z))).x * 255.0f)) << 8U);
                    uint _S1533 = atlasWidth_17;
                    uint _S1534 = 8U % atlasWidth_17;
                    int _S1535 = int(_S1534);
                    uint _S1536 = 8U / _S1533;
                    int3 _S1537 = int3(_S1535, int(_S1536), int(0));
                    uint _S1538 = uint(round((((&kernelContext_2)->compositeTexture_0).read(vec<uint,2>(((_S1537)).xy), uint(((_S1537)).z))).x * 255.0f));
                    uint _S1539 = atlasWidth_17;
                    uint _S1540 = 9U % atlasWidth_17;
                    int _S1541 = int(_S1540);
                    uint _S1542 = 9U / _S1539;
                    int3 _S1543 = int3(_S1541, int(_S1542), int(0));
                    uint rows_17 = ((_S1538 | (uint(round((((&kernelContext_2)->compositeTexture_0).read(vec<uint,2>(((_S1543)).xy), uint(((_S1543)).z))).x * 255.0f)) << 8U)) + columns_17 - 1U) / columns_17;
                    uint metadataRows_17 = (10U + 2U * _S1501 * _S1507 + atlasWidth_17 - 1U) / atlasWidth_17;
                    uint _S1544 = atlasWidth_17 / columns_17;
                    float _S1545 = float(_S1544);
                    uint _S1546 = (atlasHeight_17 - metadataRows_17) / rows_17;
                    float2 cellSize_34 = float2(_S1545, float(_S1546));
                    uint _S1547 = slot_35 % columns_17;
                    float _S1548 = float(_S1547);
                    uint _S1549 = slot_35 / columns_17;
                    _S521 = (((&kernelContext_2)->compositeTexture_0).sample(((&kernelContext_2)->compositeSampler_0), ((float2(_S1548, float(_S1549)) * cellSize_34 + float2(1.5f, float(metadataRows_17) + 1.5f) + fract(_S212) * max(cellSize_34 - float2(3.0f) , float2(0.0f) )) / float2(float(atlasWidth_17), float(atlasHeight_17)))));
                    break;
                }
                int2 _S1550 = int2(metadata_17.zw);
                int2 tile_35 = int2(floor(_S212)) - int2(metadata_17.xy);
                if(any(tile_35 < (int2(int(0)) )))
                {
                    hasSceneLighting_0 = true;
                }
                else
                {
                    hasSceneLighting_0 = any(tile_35 >= _S1550);
                }
                if(hasSceneLighting_0)
                {
                    int3 _S1551 = int3(int(min(1U, atlasWidth_17 - 1U)), int(0), int(0));
                    _S521 = (((&kernelContext_2)->compositeTexture_0).read(vec<uint,2>(((_S1551)).xy), uint(((_S1551)).z)));
                    break;
                }
                uint _S1552 = atlasWidth_17 / uint(_S1550.x);
                float _S1553 = float(_S1552);
                uint _S1554 = (atlasHeight_17 - 1U) / uint(_S1550.y);
                float2 cellSize_35 = float2(_S1553, float(_S1554));
                _S521 = (((&kernelContext_2)->compositeTexture_0).sample(((&kernelContext_2)->compositeSampler_0), ((float2(tile_35) * cellSize_35 + float2(1.5f, 2.5f) + fract(_S212) * max(cellSize_35 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_17), float(atlasHeight_17)))));
                break;
            }
            uint operation_7 = uint(round(_S1481.y));
            if(operation_7 == 1U)
            {
                _S213 = _S213 * _S521;
                break;
            }
            if(operation_7 == 2U)
            {
                _S213 = _S213 + _S521;
                break;
            }
            if(operation_7 == 3U)
            {
                _S213 = _S213 - _S521;
                break;
            }
            if(operation_7 == 4U)
            {
                float factor_7 = _S1481.z;
                _S213 = _S213 * float4((1.0f - factor_7))  + _S521 * float4(factor_7) ;
                break;
            }
            break;
        }
        clearcoatAmount_0 = saturate(_S213.x);
    }
    else
    {
        clearcoatAmount_0 = _S205;
    }
    float clearcoatRoughness_0;
    if((textureMask_0 & 1024U) != 0U)
    {
        bool _S1555 = (udimMask_0 & 1024U) != 0U;
        for(;;)
        {
            if(!_S1555)
            {
                _S213 = (((&kernelContext_2)->clearcoatRoughnessTexture_0).sample(((&kernelContext_2)->clearcoatRoughnessSampler_0), (_S212)));
                break;
            }
            texture2d<float, access::sample> _S1556 = (&kernelContext_2)->clearcoatRoughnessTexture_0;
            thread uint atlasWidth_18;
            thread uint atlasHeight_18;
            (*((&atlasWidth_18)) = (_S1556).get_width(0)),(*((&atlasHeight_18)) = (_S1556).get_height(0));
            int3 _S1557 = int3(int(0), int(0), int(0));
            float4 metadata_18 = round((((&kernelContext_2)->clearcoatRoughnessTexture_0).read(vec<uint,2>(((_S1557)).xy), uint(((_S1557)).z))) * float4(255.0f) );
            if((metadata_18.x) == 255.0f)
            {
                uint _S1558 = atlasWidth_18;
                uint _S1559 = 2U % atlasWidth_18;
                int _S1560 = int(_S1559);
                uint _S1561 = 2U / _S1558;
                int3 _S1562 = int3(_S1560, int(_S1561), int(0));
                int _S1563 = int(uint(round((((&kernelContext_2)->clearcoatRoughnessTexture_0).read(vec<uint,2>(((_S1562)).xy), uint(((_S1562)).z))).x * 255.0f)));
                uint _S1564 = atlasWidth_18;
                uint _S1565 = 3U % atlasWidth_18;
                int _S1566 = int(_S1565);
                uint _S1567 = 3U / _S1564;
                int3 _S1568 = int3(_S1566, int(_S1567), int(0));
                int2 minimumTile_18 = int2(_S1563, int(uint(round((((&kernelContext_2)->clearcoatRoughnessTexture_0).read(vec<uint,2>(((_S1568)).xy), uint(((_S1568)).z))).x * 255.0f))));
                uint _S1569 = atlasWidth_18;
                uint _S1570 = 4U % atlasWidth_18;
                int _S1571 = int(_S1570);
                uint _S1572 = 4U / _S1569;
                int3 _S1573 = int3(_S1571, int(_S1572), int(0));
                uint _S1574 = uint(round((((&kernelContext_2)->clearcoatRoughnessTexture_0).read(vec<uint,2>(((_S1573)).xy), uint(((_S1573)).z))).x * 255.0f));
                uint _S1575 = atlasWidth_18;
                uint _S1576 = 5U % atlasWidth_18;
                int _S1577 = int(_S1576);
                uint _S1578 = 5U / _S1575;
                int3 _S1579 = int3(_S1577, int(_S1578), int(0));
                uint _S1580 = uint(round((((&kernelContext_2)->clearcoatRoughnessTexture_0).read(vec<uint,2>(((_S1579)).xy), uint(((_S1579)).z))).x * 255.0f));
                uint2 grid_18 = uint2(_S1574, _S1580);
                int2 tile_36 = int2(floor(_S212)) - minimumTile_18;
                if(any(tile_36 < (int2(int(0)) )))
                {
                    hasSceneLighting_0 = true;
                }
                else
                {
                    hasSceneLighting_0 = any(tile_36 >= int2(grid_18));
                }
                if(hasSceneLighting_0)
                {
                    int3 _S1581 = int3(int(1), int(0), int(0));
                    _S213 = (((&kernelContext_2)->clearcoatRoughnessTexture_0).read(vec<uint,2>(((_S1581)).xy), uint(((_S1581)).z)));
                    break;
                }
                uint offset_18 = 10U + 2U * (uint(tile_36.y) * _S1574 + uint(tile_36.x));
                uint _S1582 = atlasWidth_18;
                uint _S1583 = offset_18 % atlasWidth_18;
                int _S1584 = int(_S1583);
                uint _S1585 = offset_18 / _S1582;
                int3 _S1586 = int3(_S1584, int(_S1585), int(0));
                uint _S1587 = uint(round((((&kernelContext_2)->clearcoatRoughnessTexture_0).read(vec<uint,2>(((_S1586)).xy), uint(((_S1586)).z))).x * 255.0f));
                uint _S1588 = atlasWidth_18;
                uint _S1589 = offset_18 + 1U;
                uint _S1590 = _S1589 % atlasWidth_18;
                int _S1591 = int(_S1590);
                uint _S1592 = _S1589 / _S1588;
                int3 _S1593 = int3(_S1591, int(_S1592), int(0));
                uint slot_36 = _S1587 | (uint(round((((&kernelContext_2)->clearcoatRoughnessTexture_0).read(vec<uint,2>(((_S1593)).xy), uint(((_S1593)).z))).x * 255.0f)) << 8U);
                if(slot_36 == 0U)
                {
                    int3 _S1594 = int3(int(1), int(0), int(0));
                    _S213 = (((&kernelContext_2)->clearcoatRoughnessTexture_0).read(vec<uint,2>(((_S1594)).xy), uint(((_S1594)).z)));
                    break;
                }
                uint slot_37 = slot_36 - 1U;
                uint _S1595 = atlasWidth_18;
                uint _S1596 = 6U % atlasWidth_18;
                int _S1597 = int(_S1596);
                uint _S1598 = 6U / _S1595;
                int3 _S1599 = int3(_S1597, int(_S1598), int(0));
                uint _S1600 = uint(round((((&kernelContext_2)->clearcoatRoughnessTexture_0).read(vec<uint,2>(((_S1599)).xy), uint(((_S1599)).z))).x * 255.0f));
                uint _S1601 = atlasWidth_18;
                uint _S1602 = 7U % atlasWidth_18;
                int _S1603 = int(_S1602);
                uint _S1604 = 7U / _S1601;
                int3 _S1605 = int3(_S1603, int(_S1604), int(0));
                uint columns_18 = _S1600 | (uint(round((((&kernelContext_2)->clearcoatRoughnessTexture_0).read(vec<uint,2>(((_S1605)).xy), uint(((_S1605)).z))).x * 255.0f)) << 8U);
                uint _S1606 = atlasWidth_18;
                uint _S1607 = 8U % atlasWidth_18;
                int _S1608 = int(_S1607);
                uint _S1609 = 8U / _S1606;
                int3 _S1610 = int3(_S1608, int(_S1609), int(0));
                uint _S1611 = uint(round((((&kernelContext_2)->clearcoatRoughnessTexture_0).read(vec<uint,2>(((_S1610)).xy), uint(((_S1610)).z))).x * 255.0f));
                uint _S1612 = atlasWidth_18;
                uint _S1613 = 9U % atlasWidth_18;
                int _S1614 = int(_S1613);
                uint _S1615 = 9U / _S1612;
                int3 _S1616 = int3(_S1614, int(_S1615), int(0));
                uint rows_18 = ((_S1611 | (uint(round((((&kernelContext_2)->clearcoatRoughnessTexture_0).read(vec<uint,2>(((_S1616)).xy), uint(((_S1616)).z))).x * 255.0f)) << 8U)) + columns_18 - 1U) / columns_18;
                uint metadataRows_18 = (10U + 2U * _S1574 * _S1580 + atlasWidth_18 - 1U) / atlasWidth_18;
                uint _S1617 = atlasWidth_18 / columns_18;
                float _S1618 = float(_S1617);
                uint _S1619 = (atlasHeight_18 - metadataRows_18) / rows_18;
                float2 cellSize_36 = float2(_S1618, float(_S1619));
                uint _S1620 = slot_37 % columns_18;
                float _S1621 = float(_S1620);
                uint _S1622 = slot_37 / columns_18;
                _S213 = (((&kernelContext_2)->clearcoatRoughnessTexture_0).sample(((&kernelContext_2)->clearcoatRoughnessSampler_0), ((float2(_S1621, float(_S1622)) * cellSize_36 + float2(1.5f, float(metadataRows_18) + 1.5f) + fract(_S212) * max(cellSize_36 - float2(3.0f) , float2(0.0f) )) / float2(float(atlasWidth_18), float(atlasHeight_18)))));
                break;
            }
            int2 _S1623 = int2(metadata_18.zw);
            int2 tile_37 = int2(floor(_S212)) - int2(metadata_18.xy);
            if(any(tile_37 < (int2(int(0)) )))
            {
                hasSceneLighting_0 = true;
            }
            else
            {
                hasSceneLighting_0 = any(tile_37 >= _S1623);
            }
            if(hasSceneLighting_0)
            {
                int3 _S1624 = int3(int(min(1U, atlasWidth_18 - 1U)), int(0), int(0));
                _S213 = (((&kernelContext_2)->clearcoatRoughnessTexture_0).read(vec<uint,2>(((_S1624)).xy), uint(((_S1624)).z)));
                break;
            }
            uint _S1625 = atlasWidth_18 / uint(_S1623.x);
            float _S1626 = float(_S1625);
            uint _S1627 = (atlasHeight_18 - 1U) / uint(_S1623.y);
            float2 cellSize_37 = float2(_S1626, float(_S1627));
            _S213 = (((&kernelContext_2)->clearcoatRoughnessTexture_0).sample(((&kernelContext_2)->clearcoatRoughnessSampler_0), ((float2(tile_37) * cellSize_37 + float2(1.5f, 2.5f) + fract(_S212) * max(cellSize_37 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_18), float(atlasHeight_18)))));
            break;
        }
        for(;;)
        {
            float4 _S1628 = float4(_S40->compositeControls_0) ;
            if((_S1628.x) != 1024.0f)
            {
                break;
            }
            bool _S1629 = (_S1628.w) >= 0.5f;
            for(;;)
            {
                if(!_S1629)
                {
                    _S521 = (((&kernelContext_2)->compositeTexture_0).sample(((&kernelContext_2)->compositeSampler_0), (_S212)));
                    break;
                }
                texture2d<float, access::sample> _S1630 = (&kernelContext_2)->compositeTexture_0;
                thread uint atlasWidth_19;
                thread uint atlasHeight_19;
                (*((&atlasWidth_19)) = (_S1630).get_width(0)),(*((&atlasHeight_19)) = (_S1630).get_height(0));
                int3 _S1631 = int3(int(0), int(0), int(0));
                float4 metadata_19 = round((((&kernelContext_2)->compositeTexture_0).read(vec<uint,2>(((_S1631)).xy), uint(((_S1631)).z))) * float4(255.0f) );
                if((metadata_19.x) == 255.0f)
                {
                    uint _S1632 = atlasWidth_19;
                    uint _S1633 = 2U % atlasWidth_19;
                    int _S1634 = int(_S1633);
                    uint _S1635 = 2U / _S1632;
                    int3 _S1636 = int3(_S1634, int(_S1635), int(0));
                    int _S1637 = int(uint(round((((&kernelContext_2)->compositeTexture_0).read(vec<uint,2>(((_S1636)).xy), uint(((_S1636)).z))).x * 255.0f)));
                    uint _S1638 = atlasWidth_19;
                    uint _S1639 = 3U % atlasWidth_19;
                    int _S1640 = int(_S1639);
                    uint _S1641 = 3U / _S1638;
                    int3 _S1642 = int3(_S1640, int(_S1641), int(0));
                    int2 minimumTile_19 = int2(_S1637, int(uint(round((((&kernelContext_2)->compositeTexture_0).read(vec<uint,2>(((_S1642)).xy), uint(((_S1642)).z))).x * 255.0f))));
                    uint _S1643 = atlasWidth_19;
                    uint _S1644 = 4U % atlasWidth_19;
                    int _S1645 = int(_S1644);
                    uint _S1646 = 4U / _S1643;
                    int3 _S1647 = int3(_S1645, int(_S1646), int(0));
                    uint _S1648 = uint(round((((&kernelContext_2)->compositeTexture_0).read(vec<uint,2>(((_S1647)).xy), uint(((_S1647)).z))).x * 255.0f));
                    uint _S1649 = atlasWidth_19;
                    uint _S1650 = 5U % atlasWidth_19;
                    int _S1651 = int(_S1650);
                    uint _S1652 = 5U / _S1649;
                    int3 _S1653 = int3(_S1651, int(_S1652), int(0));
                    uint _S1654 = uint(round((((&kernelContext_2)->compositeTexture_0).read(vec<uint,2>(((_S1653)).xy), uint(((_S1653)).z))).x * 255.0f));
                    uint2 grid_19 = uint2(_S1648, _S1654);
                    int2 tile_38 = int2(floor(_S212)) - minimumTile_19;
                    if(any(tile_38 < (int2(int(0)) )))
                    {
                        hasSceneLighting_0 = true;
                    }
                    else
                    {
                        hasSceneLighting_0 = any(tile_38 >= int2(grid_19));
                    }
                    if(hasSceneLighting_0)
                    {
                        int3 _S1655 = int3(int(1), int(0), int(0));
                        _S521 = (((&kernelContext_2)->compositeTexture_0).read(vec<uint,2>(((_S1655)).xy), uint(((_S1655)).z)));
                        break;
                    }
                    uint offset_19 = 10U + 2U * (uint(tile_38.y) * _S1648 + uint(tile_38.x));
                    uint _S1656 = atlasWidth_19;
                    uint _S1657 = offset_19 % atlasWidth_19;
                    int _S1658 = int(_S1657);
                    uint _S1659 = offset_19 / _S1656;
                    int3 _S1660 = int3(_S1658, int(_S1659), int(0));
                    uint _S1661 = uint(round((((&kernelContext_2)->compositeTexture_0).read(vec<uint,2>(((_S1660)).xy), uint(((_S1660)).z))).x * 255.0f));
                    uint _S1662 = atlasWidth_19;
                    uint _S1663 = offset_19 + 1U;
                    uint _S1664 = _S1663 % atlasWidth_19;
                    int _S1665 = int(_S1664);
                    uint _S1666 = _S1663 / _S1662;
                    int3 _S1667 = int3(_S1665, int(_S1666), int(0));
                    uint slot_38 = _S1661 | (uint(round((((&kernelContext_2)->compositeTexture_0).read(vec<uint,2>(((_S1667)).xy), uint(((_S1667)).z))).x * 255.0f)) << 8U);
                    if(slot_38 == 0U)
                    {
                        int3 _S1668 = int3(int(1), int(0), int(0));
                        _S521 = (((&kernelContext_2)->compositeTexture_0).read(vec<uint,2>(((_S1668)).xy), uint(((_S1668)).z)));
                        break;
                    }
                    uint slot_39 = slot_38 - 1U;
                    uint _S1669 = atlasWidth_19;
                    uint _S1670 = 6U % atlasWidth_19;
                    int _S1671 = int(_S1670);
                    uint _S1672 = 6U / _S1669;
                    int3 _S1673 = int3(_S1671, int(_S1672), int(0));
                    uint _S1674 = uint(round((((&kernelContext_2)->compositeTexture_0).read(vec<uint,2>(((_S1673)).xy), uint(((_S1673)).z))).x * 255.0f));
                    uint _S1675 = atlasWidth_19;
                    uint _S1676 = 7U % atlasWidth_19;
                    int _S1677 = int(_S1676);
                    uint _S1678 = 7U / _S1675;
                    int3 _S1679 = int3(_S1677, int(_S1678), int(0));
                    uint columns_19 = _S1674 | (uint(round((((&kernelContext_2)->compositeTexture_0).read(vec<uint,2>(((_S1679)).xy), uint(((_S1679)).z))).x * 255.0f)) << 8U);
                    uint _S1680 = atlasWidth_19;
                    uint _S1681 = 8U % atlasWidth_19;
                    int _S1682 = int(_S1681);
                    uint _S1683 = 8U / _S1680;
                    int3 _S1684 = int3(_S1682, int(_S1683), int(0));
                    uint _S1685 = uint(round((((&kernelContext_2)->compositeTexture_0).read(vec<uint,2>(((_S1684)).xy), uint(((_S1684)).z))).x * 255.0f));
                    uint _S1686 = atlasWidth_19;
                    uint _S1687 = 9U % atlasWidth_19;
                    int _S1688 = int(_S1687);
                    uint _S1689 = 9U / _S1686;
                    int3 _S1690 = int3(_S1688, int(_S1689), int(0));
                    uint rows_19 = ((_S1685 | (uint(round((((&kernelContext_2)->compositeTexture_0).read(vec<uint,2>(((_S1690)).xy), uint(((_S1690)).z))).x * 255.0f)) << 8U)) + columns_19 - 1U) / columns_19;
                    uint metadataRows_19 = (10U + 2U * _S1648 * _S1654 + atlasWidth_19 - 1U) / atlasWidth_19;
                    uint _S1691 = atlasWidth_19 / columns_19;
                    float _S1692 = float(_S1691);
                    uint _S1693 = (atlasHeight_19 - metadataRows_19) / rows_19;
                    float2 cellSize_38 = float2(_S1692, float(_S1693));
                    uint _S1694 = slot_39 % columns_19;
                    float _S1695 = float(_S1694);
                    uint _S1696 = slot_39 / columns_19;
                    _S521 = (((&kernelContext_2)->compositeTexture_0).sample(((&kernelContext_2)->compositeSampler_0), ((float2(_S1695, float(_S1696)) * cellSize_38 + float2(1.5f, float(metadataRows_19) + 1.5f) + fract(_S212) * max(cellSize_38 - float2(3.0f) , float2(0.0f) )) / float2(float(atlasWidth_19), float(atlasHeight_19)))));
                    break;
                }
                int2 _S1697 = int2(metadata_19.zw);
                int2 tile_39 = int2(floor(_S212)) - int2(metadata_19.xy);
                if(any(tile_39 < (int2(int(0)) )))
                {
                    hasSceneLighting_0 = true;
                }
                else
                {
                    hasSceneLighting_0 = any(tile_39 >= _S1697);
                }
                if(hasSceneLighting_0)
                {
                    int3 _S1698 = int3(int(min(1U, atlasWidth_19 - 1U)), int(0), int(0));
                    _S521 = (((&kernelContext_2)->compositeTexture_0).read(vec<uint,2>(((_S1698)).xy), uint(((_S1698)).z)));
                    break;
                }
                uint _S1699 = atlasWidth_19 / uint(_S1697.x);
                float _S1700 = float(_S1699);
                uint _S1701 = (atlasHeight_19 - 1U) / uint(_S1697.y);
                float2 cellSize_39 = float2(_S1700, float(_S1701));
                _S521 = (((&kernelContext_2)->compositeTexture_0).sample(((&kernelContext_2)->compositeSampler_0), ((float2(tile_39) * cellSize_39 + float2(1.5f, 2.5f) + fract(_S212) * max(cellSize_39 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_19), float(atlasHeight_19)))));
                break;
            }
            uint operation_8 = uint(round(_S1628.y));
            if(operation_8 == 1U)
            {
                _S213 = _S213 * _S521;
                break;
            }
            if(operation_8 == 2U)
            {
                _S213 = _S213 + _S521;
                break;
            }
            if(operation_8 == 3U)
            {
                _S213 = _S213 - _S521;
                break;
            }
            if(operation_8 == 4U)
            {
                float factor_8 = _S1628.z;
                _S213 = _S213 * float4((1.0f - factor_8))  + _S521 * float4(factor_8) ;
                break;
            }
            break;
        }
        clearcoatRoughness_0 = saturate(_S213.x);
    }
    else
    {
        clearcoatRoughness_0 = _S206;
    }
    float ior_0;
    if((textureMask_0 & 2048U) != 0U)
    {
        bool _S1702 = (udimMask_0 & 2048U) != 0U;
        for(;;)
        {
            if(!_S1702)
            {
                _S213 = (((&kernelContext_2)->iorTexture_0).sample(((&kernelContext_2)->iorSampler_0), (_S212)));
                break;
            }
            texture2d<float, access::sample> _S1703 = (&kernelContext_2)->iorTexture_0;
            thread uint atlasWidth_20;
            thread uint atlasHeight_20;
            (*((&atlasWidth_20)) = (_S1703).get_width(0)),(*((&atlasHeight_20)) = (_S1703).get_height(0));
            int3 _S1704 = int3(int(0), int(0), int(0));
            float4 metadata_20 = round((((&kernelContext_2)->iorTexture_0).read(vec<uint,2>(((_S1704)).xy), uint(((_S1704)).z))) * float4(255.0f) );
            if((metadata_20.x) == 255.0f)
            {
                uint _S1705 = atlasWidth_20;
                uint _S1706 = 2U % atlasWidth_20;
                int _S1707 = int(_S1706);
                uint _S1708 = 2U / _S1705;
                int3 _S1709 = int3(_S1707, int(_S1708), int(0));
                int _S1710 = int(uint(round((((&kernelContext_2)->iorTexture_0).read(vec<uint,2>(((_S1709)).xy), uint(((_S1709)).z))).x * 255.0f)));
                uint _S1711 = atlasWidth_20;
                uint _S1712 = 3U % atlasWidth_20;
                int _S1713 = int(_S1712);
                uint _S1714 = 3U / _S1711;
                int3 _S1715 = int3(_S1713, int(_S1714), int(0));
                int2 minimumTile_20 = int2(_S1710, int(uint(round((((&kernelContext_2)->iorTexture_0).read(vec<uint,2>(((_S1715)).xy), uint(((_S1715)).z))).x * 255.0f))));
                uint _S1716 = atlasWidth_20;
                uint _S1717 = 4U % atlasWidth_20;
                int _S1718 = int(_S1717);
                uint _S1719 = 4U / _S1716;
                int3 _S1720 = int3(_S1718, int(_S1719), int(0));
                uint _S1721 = uint(round((((&kernelContext_2)->iorTexture_0).read(vec<uint,2>(((_S1720)).xy), uint(((_S1720)).z))).x * 255.0f));
                uint _S1722 = atlasWidth_20;
                uint _S1723 = 5U % atlasWidth_20;
                int _S1724 = int(_S1723);
                uint _S1725 = 5U / _S1722;
                int3 _S1726 = int3(_S1724, int(_S1725), int(0));
                uint _S1727 = uint(round((((&kernelContext_2)->iorTexture_0).read(vec<uint,2>(((_S1726)).xy), uint(((_S1726)).z))).x * 255.0f));
                uint2 grid_20 = uint2(_S1721, _S1727);
                int2 tile_40 = int2(floor(_S212)) - minimumTile_20;
                if(any(tile_40 < (int2(int(0)) )))
                {
                    hasSceneLighting_0 = true;
                }
                else
                {
                    hasSceneLighting_0 = any(tile_40 >= int2(grid_20));
                }
                if(hasSceneLighting_0)
                {
                    int3 _S1728 = int3(int(1), int(0), int(0));
                    _S213 = (((&kernelContext_2)->iorTexture_0).read(vec<uint,2>(((_S1728)).xy), uint(((_S1728)).z)));
                    break;
                }
                uint offset_20 = 10U + 2U * (uint(tile_40.y) * _S1721 + uint(tile_40.x));
                uint _S1729 = atlasWidth_20;
                uint _S1730 = offset_20 % atlasWidth_20;
                int _S1731 = int(_S1730);
                uint _S1732 = offset_20 / _S1729;
                int3 _S1733 = int3(_S1731, int(_S1732), int(0));
                uint _S1734 = uint(round((((&kernelContext_2)->iorTexture_0).read(vec<uint,2>(((_S1733)).xy), uint(((_S1733)).z))).x * 255.0f));
                uint _S1735 = atlasWidth_20;
                uint _S1736 = offset_20 + 1U;
                uint _S1737 = _S1736 % atlasWidth_20;
                int _S1738 = int(_S1737);
                uint _S1739 = _S1736 / _S1735;
                int3 _S1740 = int3(_S1738, int(_S1739), int(0));
                uint slot_40 = _S1734 | (uint(round((((&kernelContext_2)->iorTexture_0).read(vec<uint,2>(((_S1740)).xy), uint(((_S1740)).z))).x * 255.0f)) << 8U);
                if(slot_40 == 0U)
                {
                    int3 _S1741 = int3(int(1), int(0), int(0));
                    _S213 = (((&kernelContext_2)->iorTexture_0).read(vec<uint,2>(((_S1741)).xy), uint(((_S1741)).z)));
                    break;
                }
                uint slot_41 = slot_40 - 1U;
                uint _S1742 = atlasWidth_20;
                uint _S1743 = 6U % atlasWidth_20;
                int _S1744 = int(_S1743);
                uint _S1745 = 6U / _S1742;
                int3 _S1746 = int3(_S1744, int(_S1745), int(0));
                uint _S1747 = uint(round((((&kernelContext_2)->iorTexture_0).read(vec<uint,2>(((_S1746)).xy), uint(((_S1746)).z))).x * 255.0f));
                uint _S1748 = atlasWidth_20;
                uint _S1749 = 7U % atlasWidth_20;
                int _S1750 = int(_S1749);
                uint _S1751 = 7U / _S1748;
                int3 _S1752 = int3(_S1750, int(_S1751), int(0));
                uint columns_20 = _S1747 | (uint(round((((&kernelContext_2)->iorTexture_0).read(vec<uint,2>(((_S1752)).xy), uint(((_S1752)).z))).x * 255.0f)) << 8U);
                uint _S1753 = atlasWidth_20;
                uint _S1754 = 8U % atlasWidth_20;
                int _S1755 = int(_S1754);
                uint _S1756 = 8U / _S1753;
                int3 _S1757 = int3(_S1755, int(_S1756), int(0));
                uint _S1758 = uint(round((((&kernelContext_2)->iorTexture_0).read(vec<uint,2>(((_S1757)).xy), uint(((_S1757)).z))).x * 255.0f));
                uint _S1759 = atlasWidth_20;
                uint _S1760 = 9U % atlasWidth_20;
                int _S1761 = int(_S1760);
                uint _S1762 = 9U / _S1759;
                int3 _S1763 = int3(_S1761, int(_S1762), int(0));
                uint rows_20 = ((_S1758 | (uint(round((((&kernelContext_2)->iorTexture_0).read(vec<uint,2>(((_S1763)).xy), uint(((_S1763)).z))).x * 255.0f)) << 8U)) + columns_20 - 1U) / columns_20;
                uint metadataRows_20 = (10U + 2U * _S1721 * _S1727 + atlasWidth_20 - 1U) / atlasWidth_20;
                uint _S1764 = atlasWidth_20 / columns_20;
                float _S1765 = float(_S1764);
                uint _S1766 = (atlasHeight_20 - metadataRows_20) / rows_20;
                float2 cellSize_40 = float2(_S1765, float(_S1766));
                uint _S1767 = slot_41 % columns_20;
                float _S1768 = float(_S1767);
                uint _S1769 = slot_41 / columns_20;
                _S213 = (((&kernelContext_2)->iorTexture_0).sample(((&kernelContext_2)->iorSampler_0), ((float2(_S1768, float(_S1769)) * cellSize_40 + float2(1.5f, float(metadataRows_20) + 1.5f) + fract(_S212) * max(cellSize_40 - float2(3.0f) , float2(0.0f) )) / float2(float(atlasWidth_20), float(atlasHeight_20)))));
                break;
            }
            int2 _S1770 = int2(metadata_20.zw);
            int2 tile_41 = int2(floor(_S212)) - int2(metadata_20.xy);
            if(any(tile_41 < (int2(int(0)) )))
            {
                hasSceneLighting_0 = true;
            }
            else
            {
                hasSceneLighting_0 = any(tile_41 >= _S1770);
            }
            if(hasSceneLighting_0)
            {
                int3 _S1771 = int3(int(min(1U, atlasWidth_20 - 1U)), int(0), int(0));
                _S213 = (((&kernelContext_2)->iorTexture_0).read(vec<uint,2>(((_S1771)).xy), uint(((_S1771)).z)));
                break;
            }
            uint _S1772 = atlasWidth_20 / uint(_S1770.x);
            float _S1773 = float(_S1772);
            uint _S1774 = (atlasHeight_20 - 1U) / uint(_S1770.y);
            float2 cellSize_41 = float2(_S1773, float(_S1774));
            _S213 = (((&kernelContext_2)->iorTexture_0).sample(((&kernelContext_2)->iorSampler_0), ((float2(tile_41) * cellSize_41 + float2(1.5f, 2.5f) + fract(_S212) * max(cellSize_41 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_20), float(atlasHeight_20)))));
            break;
        }
        for(;;)
        {
            float4 _S1775 = float4(_S40->compositeControls_0) ;
            if((_S1775.x) != 2048.0f)
            {
                break;
            }
            bool _S1776 = (_S1775.w) >= 0.5f;
            for(;;)
            {
                if(!_S1776)
                {
                    _S521 = (((&kernelContext_2)->compositeTexture_0).sample(((&kernelContext_2)->compositeSampler_0), (_S212)));
                    break;
                }
                texture2d<float, access::sample> _S1777 = (&kernelContext_2)->compositeTexture_0;
                thread uint atlasWidth_21;
                thread uint atlasHeight_21;
                (*((&atlasWidth_21)) = (_S1777).get_width(0)),(*((&atlasHeight_21)) = (_S1777).get_height(0));
                int3 _S1778 = int3(int(0), int(0), int(0));
                float4 metadata_21 = round((((&kernelContext_2)->compositeTexture_0).read(vec<uint,2>(((_S1778)).xy), uint(((_S1778)).z))) * float4(255.0f) );
                if((metadata_21.x) == 255.0f)
                {
                    uint _S1779 = atlasWidth_21;
                    uint _S1780 = 2U % atlasWidth_21;
                    int _S1781 = int(_S1780);
                    uint _S1782 = 2U / _S1779;
                    int3 _S1783 = int3(_S1781, int(_S1782), int(0));
                    int _S1784 = int(uint(round((((&kernelContext_2)->compositeTexture_0).read(vec<uint,2>(((_S1783)).xy), uint(((_S1783)).z))).x * 255.0f)));
                    uint _S1785 = atlasWidth_21;
                    uint _S1786 = 3U % atlasWidth_21;
                    int _S1787 = int(_S1786);
                    uint _S1788 = 3U / _S1785;
                    int3 _S1789 = int3(_S1787, int(_S1788), int(0));
                    int2 minimumTile_21 = int2(_S1784, int(uint(round((((&kernelContext_2)->compositeTexture_0).read(vec<uint,2>(((_S1789)).xy), uint(((_S1789)).z))).x * 255.0f))));
                    uint _S1790 = atlasWidth_21;
                    uint _S1791 = 4U % atlasWidth_21;
                    int _S1792 = int(_S1791);
                    uint _S1793 = 4U / _S1790;
                    int3 _S1794 = int3(_S1792, int(_S1793), int(0));
                    uint _S1795 = uint(round((((&kernelContext_2)->compositeTexture_0).read(vec<uint,2>(((_S1794)).xy), uint(((_S1794)).z))).x * 255.0f));
                    uint _S1796 = atlasWidth_21;
                    uint _S1797 = 5U % atlasWidth_21;
                    int _S1798 = int(_S1797);
                    uint _S1799 = 5U / _S1796;
                    int3 _S1800 = int3(_S1798, int(_S1799), int(0));
                    uint _S1801 = uint(round((((&kernelContext_2)->compositeTexture_0).read(vec<uint,2>(((_S1800)).xy), uint(((_S1800)).z))).x * 255.0f));
                    uint2 grid_21 = uint2(_S1795, _S1801);
                    int2 tile_42 = int2(floor(_S212)) - minimumTile_21;
                    if(any(tile_42 < (int2(int(0)) )))
                    {
                        hasSceneLighting_0 = true;
                    }
                    else
                    {
                        hasSceneLighting_0 = any(tile_42 >= int2(grid_21));
                    }
                    if(hasSceneLighting_0)
                    {
                        int3 _S1802 = int3(int(1), int(0), int(0));
                        _S521 = (((&kernelContext_2)->compositeTexture_0).read(vec<uint,2>(((_S1802)).xy), uint(((_S1802)).z)));
                        break;
                    }
                    uint offset_21 = 10U + 2U * (uint(tile_42.y) * _S1795 + uint(tile_42.x));
                    uint _S1803 = atlasWidth_21;
                    uint _S1804 = offset_21 % atlasWidth_21;
                    int _S1805 = int(_S1804);
                    uint _S1806 = offset_21 / _S1803;
                    int3 _S1807 = int3(_S1805, int(_S1806), int(0));
                    uint _S1808 = uint(round((((&kernelContext_2)->compositeTexture_0).read(vec<uint,2>(((_S1807)).xy), uint(((_S1807)).z))).x * 255.0f));
                    uint _S1809 = atlasWidth_21;
                    uint _S1810 = offset_21 + 1U;
                    uint _S1811 = _S1810 % atlasWidth_21;
                    int _S1812 = int(_S1811);
                    uint _S1813 = _S1810 / _S1809;
                    int3 _S1814 = int3(_S1812, int(_S1813), int(0));
                    uint slot_42 = _S1808 | (uint(round((((&kernelContext_2)->compositeTexture_0).read(vec<uint,2>(((_S1814)).xy), uint(((_S1814)).z))).x * 255.0f)) << 8U);
                    if(slot_42 == 0U)
                    {
                        int3 _S1815 = int3(int(1), int(0), int(0));
                        _S521 = (((&kernelContext_2)->compositeTexture_0).read(vec<uint,2>(((_S1815)).xy), uint(((_S1815)).z)));
                        break;
                    }
                    uint slot_43 = slot_42 - 1U;
                    uint _S1816 = atlasWidth_21;
                    uint _S1817 = 6U % atlasWidth_21;
                    int _S1818 = int(_S1817);
                    uint _S1819 = 6U / _S1816;
                    int3 _S1820 = int3(_S1818, int(_S1819), int(0));
                    uint _S1821 = uint(round((((&kernelContext_2)->compositeTexture_0).read(vec<uint,2>(((_S1820)).xy), uint(((_S1820)).z))).x * 255.0f));
                    uint _S1822 = atlasWidth_21;
                    uint _S1823 = 7U % atlasWidth_21;
                    int _S1824 = int(_S1823);
                    uint _S1825 = 7U / _S1822;
                    int3 _S1826 = int3(_S1824, int(_S1825), int(0));
                    uint columns_21 = _S1821 | (uint(round((((&kernelContext_2)->compositeTexture_0).read(vec<uint,2>(((_S1826)).xy), uint(((_S1826)).z))).x * 255.0f)) << 8U);
                    uint _S1827 = atlasWidth_21;
                    uint _S1828 = 8U % atlasWidth_21;
                    int _S1829 = int(_S1828);
                    uint _S1830 = 8U / _S1827;
                    int3 _S1831 = int3(_S1829, int(_S1830), int(0));
                    uint _S1832 = uint(round((((&kernelContext_2)->compositeTexture_0).read(vec<uint,2>(((_S1831)).xy), uint(((_S1831)).z))).x * 255.0f));
                    uint _S1833 = atlasWidth_21;
                    uint _S1834 = 9U % atlasWidth_21;
                    int _S1835 = int(_S1834);
                    uint _S1836 = 9U / _S1833;
                    int3 _S1837 = int3(_S1835, int(_S1836), int(0));
                    uint rows_21 = ((_S1832 | (uint(round((((&kernelContext_2)->compositeTexture_0).read(vec<uint,2>(((_S1837)).xy), uint(((_S1837)).z))).x * 255.0f)) << 8U)) + columns_21 - 1U) / columns_21;
                    uint metadataRows_21 = (10U + 2U * _S1795 * _S1801 + atlasWidth_21 - 1U) / atlasWidth_21;
                    uint _S1838 = atlasWidth_21 / columns_21;
                    float _S1839 = float(_S1838);
                    uint _S1840 = (atlasHeight_21 - metadataRows_21) / rows_21;
                    float2 cellSize_42 = float2(_S1839, float(_S1840));
                    uint _S1841 = slot_43 % columns_21;
                    float _S1842 = float(_S1841);
                    uint _S1843 = slot_43 / columns_21;
                    _S521 = (((&kernelContext_2)->compositeTexture_0).sample(((&kernelContext_2)->compositeSampler_0), ((float2(_S1842, float(_S1843)) * cellSize_42 + float2(1.5f, float(metadataRows_21) + 1.5f) + fract(_S212) * max(cellSize_42 - float2(3.0f) , float2(0.0f) )) / float2(float(atlasWidth_21), float(atlasHeight_21)))));
                    break;
                }
                int2 _S1844 = int2(metadata_21.zw);
                int2 tile_43 = int2(floor(_S212)) - int2(metadata_21.xy);
                if(any(tile_43 < (int2(int(0)) )))
                {
                    hasSceneLighting_0 = true;
                }
                else
                {
                    hasSceneLighting_0 = any(tile_43 >= _S1844);
                }
                if(hasSceneLighting_0)
                {
                    int3 _S1845 = int3(int(min(1U, atlasWidth_21 - 1U)), int(0), int(0));
                    _S521 = (((&kernelContext_2)->compositeTexture_0).read(vec<uint,2>(((_S1845)).xy), uint(((_S1845)).z)));
                    break;
                }
                uint _S1846 = atlasWidth_21 / uint(_S1844.x);
                float _S1847 = float(_S1846);
                uint _S1848 = (atlasHeight_21 - 1U) / uint(_S1844.y);
                float2 cellSize_43 = float2(_S1847, float(_S1848));
                _S521 = (((&kernelContext_2)->compositeTexture_0).sample(((&kernelContext_2)->compositeSampler_0), ((float2(tile_43) * cellSize_43 + float2(1.5f, 2.5f) + fract(_S212) * max(cellSize_43 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_21), float(atlasHeight_21)))));
                break;
            }
            uint operation_9 = uint(round(_S1775.y));
            if(operation_9 == 1U)
            {
                _S213 = _S213 * _S521;
                break;
            }
            if(operation_9 == 2U)
            {
                _S213 = _S213 + _S521;
                break;
            }
            if(operation_9 == 3U)
            {
                _S213 = _S213 - _S521;
                break;
            }
            if(operation_9 == 4U)
            {
                float factor_9 = _S1775.z;
                _S213 = _S213 * float4((1.0f - factor_9))  + _S521 * float4(factor_9) ;
                break;
            }
            break;
        }
        ior_0 = _S213.x;
    }
    else
    {
        ior_0 = _S204;
    }
    float opacityThreshold_0 = _S199.z;
    if(opacityThreshold_0 > 0.0f)
    {
        if(opacity_0 < opacityThreshold_0)
        {
            discard_fragment();
        }
        opacity_0 = 1.0f;
    }
    float3 normal_1 = normalize(shadingNormal_0);
    float3 worldNormal_1 = normalize(worldShadingNormal_0);
    float lengthSquared_0 = dot(_S35.eyePosition_0, _S35.eyePosition_0);
    if(lengthSquared_0 > 0.00100000004749745f)
    {
        unlitColor_0 = - _S35.eyePosition_0 * float3(rsqrt(lengthSquared_0)) ;
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
    float _S1849 = saturate(abs(dot(normal_2, unlitColor_0)) + 0.00000999999974738f);
    float _S1850 = max(0.00100000004749745f, roughness_0);
    float _S1851 = max(0.00100000004749745f, clearcoatRoughness_0);
    float reflectanceRatio_0 = (1.0f - ior_0) / (1.0f + ior_0);
    float3 _S1852 = float3(3.14159274101257324f) ;
    float3 diffuse_0 = diffuseColor_0 / _S1852;
    bool omniPbrSpecular_0 = (_S207.w) >= 0.5f;
    float dielectricLevel_0 = _S193.w;
    float3 normalIncidence_0;
    float3 grazingIncidence_0;
    float3 diffuse_1;
    if(omniPbrSpecular_0)
    {
        float3 _S1853 = float3(dielectricLevel_0) ;
        float3 _S1854 = float3(metallic_0) ;
        float3 _S1855 = mix(_S1853, diffuseColor_0, _S1854);
        float3 diffuse_2 = diffuse_0 * float3((1.0f - metallic_0)) ;
        normalIncidence_0 = mix(float3(0.07999999821186066f, 0.07999999821186066f, 0.07999999821186066f) * _S1853, diffuseColor_0, _S1854);
        grazingIncidence_0 = _S1855;
        diffuse_1 = diffuse_2;
    }
    else
    {
        if((_S199.w) >= 0.5f)
        {
            float3 _S1856 = float3(1.0f, 1.0f, 1.0f);
            normalIncidence_0 = specularColor_0;
            grazingIncidence_0 = _S1856;
            diffuse_1 = diffuse_0;
        }
        else
        {
            float3 _S1857 = float3(metallic_0) ;
            float3 specularTint_0 = mix(float3(1.0f, 1.0f, 1.0f), diffuseColor_0, _S1857);
            float3 diffuse_3 = diffuse_0 * float3((1.0f - metallic_0)) ;
            normalIncidence_0 = mix(float3((reflectanceRatio_0 * reflectanceRatio_0))  * specularTint_0, specularTint_0, _S1857);
            grazingIncidence_0 = specularTint_0;
            diffuse_1 = diffuse_3;
        }
    }
    FragmentFrameParameters_0 _S1858 = _S36;
    float _S1859 = _S36.ambientLight_0.w;
    if(_S1859 > 0.5f)
    {
        hasSceneLighting_0 = true;
    }
    else
    {
        float3 _S1860 = _S1858.ambientLight_0.xyz;
        hasSceneLighting_0 = (dot(_S1860, _S1860)) > 0.0f;
    }
    if(hasSceneLighting_0)
    {
        hasSceneLighting_0 = true;
    }
    else
    {
        hasSceneLighting_0 = (_S36.environmentControls_0.x) >= 0.5f;
    }
    if(hasSceneLighting_0)
    {
        hasSceneLighting_0 = true;
    }
    else
    {
        hasSceneLighting_0 = (_S36.environmentControls_0.w) >= 0.5f;
    }
    uint _S1861 = min(uint(_S1859), 128U);
    float3 sceneEye_0 = normalize((((float4(unlitColor_0, 0.0f)) * (_S36.eyeToWorld_0))).xyz);
    float3 worldPosition_0 = (((float4(_S35.eyePosition_0, 1.0f)) * (_S36.eyeToWorld_0))).xyz;
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
    float4 _S1862 = float4(_S40->lightColorAmbient_0) ;
    float3 color_1 = diffuseColor_0 * float3(_S1862.w) ;
    uint domeLinkMask_0 = uint(max((float4(_S40->domeLinkControls_0) ).x, 0.0f));
    FragmentFrameParameters_0 _S1863 = _S36;
    uint _S1864 = min(uint(max(_S36.domeControls_0.x, 0.0f)), 8U);
    uint allDomes_0 = (1U << _S1864) - 1U;
    bool allDomesLinked_0 = (domeLinkMask_0 & allDomes_0) == allDomes_0;
    float3 _S1865 = _S1858.ambientLight_0.xyz;
    uint lightCount_0;
    float3 domeAmbient_1;
    if(!allDomesLinked_0)
    {
        float3 _S1866 = float3(0.0f, 0.0f, 0.0f);
        lightCount_0 = 0U;
        domeAmbient_1 = _S1866;
        for(;;)
        {
            if(lightCount_0 < _S1864)
            {
            }
            else
            {
                break;
            }
            if((domeLinkMask_0 & (1U << lightCount_0)) != 0U)
            {
                domeAmbient_1 = domeAmbient_1 + _S36.domeAmbient_0[lightCount_0].xyz;
            }
            lightCount_0 = lightCount_0 + 1U;
        }
    }
    else
    {
        domeAmbient_1 = _S1865;
    }
    float3 color_2 = color_1 + diffuseColor_0 * domeAmbient_1;
    bool _S1867 = !hasSceneLighting_0;
    if(_S1867)
    {
        lightCount_0 = 1U;
    }
    else
    {
        lightCount_0 = _S1861;
    }
    uint lightIndex_0 = 0U;
    float3 color_3 = color_2;
    for(;;)
    {
        if(lightIndex_0 < lightCount_0)
        {
        }
        else
        {
            break;
        }
        bool _S1868;
        if(hasSceneLighting_0)
        {
            _S1868 = (((uint4(_S40->lightLinkMask_0) )[lightIndex_0 / 32U]) & (1U << (lightIndex_0 % 32U))) == 0U;
        }
        else
        {
            _S1868 = false;
        }
        if(_S1868)
        {
            lightIndex_0 = lightIndex_0 + 1U;
            continue;
        }
        bool _S1869 = lightIndex_0 == 0U;
        bool _S1870;
        if(_S1869)
        {
            _S1870 = _S1867;
        }
        else
        {
            _S1870 = false;
        }
        float lightType_0;
        if(_S1870)
        {
            lightType_0 = 1.0f;
        }
        else
        {
            float4 _S1871 = ReadFrameLight_0(0U, lightIndex_0, &kernelContext_2);
            lightType_0 = _S1871.w;
        }
        bool _S1872;
        if(_S1869)
        {
            _S1872 = _S1867;
        }
        else
        {
            _S1872 = false;
        }
        if(_S1872)
        {
            lightDirection_0 = normalize((float4(_S40->lightDirectionIntensity_0) ).xyz);
        }
        else
        {
            float4 _S1873 = ReadFrameLight_0(1U, lightIndex_0, &kernelContext_2);
            lightDirection_0 = normalize(_S1873.xyz);
        }
        bool _S1874;
        if(_S1869)
        {
            _S1874 = _S1867;
        }
        else
        {
            _S1874 = false;
        }
        if(_S1874)
        {
            roughness_0 = (float4(_S40->lightDirectionIntensity_0) ).w;
        }
        else
        {
            float4 _S1875 = ReadFrameLight_0(2U, lightIndex_0, &kernelContext_2);
            roughness_0 = _S1875.w;
        }
        bool _S1876;
        if(_S1869)
        {
            _S1876 = _S1867;
        }
        else
        {
            _S1876 = false;
        }
        if(_S1876)
        {
            diffuseColor_0 = _S1862.xyz;
        }
        else
        {
            float4 _S1877 = ReadFrameLight_0(2U, lightIndex_0, &kernelContext_2);
            diffuseColor_0 = _S1877.xyz;
        }
        bool _S1878;
        if(_S1869)
        {
            _S1878 = _S1867;
        }
        else
        {
            _S1878 = false;
        }
        if(_S1878)
        {
            metallic_0 = 1.0f;
        }
        else
        {
            float4 _S1879 = ReadFrameLight_0(3U, lightIndex_0, &kernelContext_2);
            metallic_0 = _S1879.x;
        }
        bool _S1880;
        if(_S1869)
        {
            _S1880 = _S1867;
        }
        else
        {
            _S1880 = false;
        }
        if(_S1880)
        {
            clearcoatRoughness_0 = 1.0f;
        }
        else
        {
            float4 _S1881 = ReadFrameLight_0(3U, lightIndex_0, &kernelContext_2);
            clearcoatRoughness_0 = _S1881.y;
        }
        bool _S1882;
        if(_S1869)
        {
            _S1882 = _S1867;
        }
        else
        {
            _S1882 = false;
        }
        if(_S1882)
        {
            lightTangent_0 = float3(1.0f, 0.0f, 0.0f);
        }
        else
        {
            float4 _S1883 = ReadFrameLight_0(4U, lightIndex_0, &kernelContext_2);
            lightTangent_0 = normalize(_S1883.xyz);
        }
        bool _S1884;
        if(_S1869)
        {
            _S1884 = _S1867;
        }
        else
        {
            _S1884 = false;
        }
        if(_S1884)
        {
            lightBitangent_0 = float3(0.0f, 1.0f, 0.0f);
        }
        else
        {
            float4 _S1885 = ReadFrameLight_0(5U, lightIndex_0, &kernelContext_2);
            lightBitangent_0 = normalize(_S1885.xyz);
        }
        bool _S1886;
        if(_S1869)
        {
            _S1886 = _S1867;
        }
        else
        {
            _S1886 = false;
        }
        float shapeX_0;
        if(_S1886)
        {
            shapeX_0 = 0.0f;
        }
        else
        {
            float4 _S1887 = ReadFrameLight_0(4U, lightIndex_0, &kernelContext_2);
            shapeX_0 = _S1887.w;
        }
        bool _S1888;
        if(_S1869)
        {
            _S1888 = _S1867;
        }
        else
        {
            _S1888 = false;
        }
        float shapeY_0;
        if(_S1888)
        {
            shapeY_0 = 0.0f;
        }
        else
        {
            float4 _S1889 = ReadFrameLight_0(5U, lightIndex_0, &kernelContext_2);
            shapeY_0 = _S1889.w;
        }
        bool _S1890;
        if(_S1869)
        {
            _S1890 = _S1867;
        }
        else
        {
            _S1890 = false;
        }
        float lightRadius_0;
        if(_S1890)
        {
            lightRadius_0 = 0.0f;
        }
        else
        {
            float4 _S1891 = ReadFrameLight_0(1U, lightIndex_0, &kernelContext_2);
            lightRadius_0 = _S1891.w;
        }
        bool _S1892;
        if(_S1869)
        {
            _S1892 = _S1867;
        }
        else
        {
            _S1892 = false;
        }
        if(_S1892)
        {
            domeAmbient_1 = unlitColor_0;
        }
        else
        {
            domeAmbient_1 = sceneEye_0;
        }
        float shadowVisibility_0;
        if(hasSceneLighting_0)
        {
            float4 _S1893 = ReadFrameShadowSlot_0(lightIndex_0, &kernelContext_2);
            int shadowSlot_0 = int(_S1893.x);
            if(shadowSlot_0 >= int(0))
            {
                for(;;)
                {
                    FragmentFrameParameters_0 _S1894 = _S36;
                    FragmentFrameParameters_0 _S1895 = _S36;
                    if((dot(specularColor_0, lightDirection_0)) < 0.0f)
                    {
                        color_0 = - specularColor_0;
                    }
                    else
                    {
                        color_0 = specularColor_0;
                    }
                    float slope_0 = clamp(1.0f - saturate(dot(color_0, lightDirection_0)), 0.0f, 1.0f);
                    float4 lightClip_0 = (((float4(worldPosition_0 + color_0 * float3((_S1894.shadowControls_0[shadowSlot_0].y * slope_0)) , 1.0f)) * (_S36.shadowWorldToLightClip_0[shadowSlot_0])));
                    float _S1896 = lightClip_0.w;
                    if(_S1896 <= 0.0f)
                    {
                        ior_0 = 1.0f;
                        break;
                    }
                    float3 ndc_0 = lightClip_0.xyz / float3(_S1896) ;
                    bool _S1897;
                    if((abs(ndc_0.x)) > 1.0f)
                    {
                        _S1897 = true;
                    }
                    else
                    {
                        _S1897 = (abs(ndc_0.y)) > 1.0f;
                    }
                    bool _S1898;
                    if(_S1897)
                    {
                        _S1898 = true;
                    }
                    else
                    {
                        _S1898 = (ndc_0.z) < 0.0f;
                    }
                    bool _S1899;
                    if(_S1898)
                    {
                        _S1899 = true;
                    }
                    else
                    {
                        _S1899 = (ndc_0.z) > 1.0f;
                    }
                    if(_S1899)
                    {
                        ior_0 = 1.0f;
                        break;
                    }
                    float2 _S1900 = _S1895.shadowTile_0[shadowSlot_0].xy;
                    float2 _S1901 = _S1895.shadowTile_0[shadowSlot_0].zw;
                    float2 _S1902 = _S1900 + (ndc_0.xy * float2(0.5f, -0.5f) + float2(0.5f, 0.5f)) * _S1901;
                    float texel_0 = _S1894.shadowControls_0[shadowSlot_0].w;
                    float _S1903 = max(_S1894.shadowControls_0[shadowSlot_0].z, 0.0f);
                    float _S1904 = ndc_0.z - _S1894.shadowControls_0[shadowSlot_0].x * (1.0f + 2.0f * slope_0);
                    float2 _S1905 = float2((texel_0 * 0.5f)) ;
                    float2 _S1906 = _S1900 + _S1905;
                    float2 _S1907 = _S1900 + _S1901 - _S1905;
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
                            if(_S1904 <= (((&kernelContext_2)->shadowAtlas_0).sample(((&kernelContext_2)->shadowSampler_0), (clamp(_S1902 + float2(float(x_0), float(y_0)) * float2((_S1903 * texel_0)) , _S1906, _S1907)), level((0.0f))).x))
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
        float3 _S1908 = float3(0.0f, 0.0f, 0.0f);
        sampleOffsets_0[int(0)] = _S1908;
        sampleOffsets_0[int(1)] = _S1908;
        sampleOffsets_0[int(2)] = _S1908;
        sampleOffsets_0[int(3)] = _S1908;
        sampleOffsets_0[int(4)] = _S1908;
        float sampleCount_0;
        if(lightType_0 == 3.0f)
        {
            float3 halfWidth_0 = lightTangent_0 * float3((shapeX_0 * 0.5f)) ;
            float3 halfHeight_0 = lightBitangent_0 * float3((shapeY_0 * 0.5f)) ;
            sampleOffsets_0[int(1)] = halfWidth_0 + halfHeight_0;
            sampleOffsets_0[int(2)] = halfWidth_0 - halfHeight_0;
            float3 _S1909 = - halfWidth_0;
            sampleOffsets_0[int(3)] = _S1909 + halfHeight_0;
            sampleOffsets_0[int(4)] = _S1909 - halfHeight_0;
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
        color_0 = color_3;
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
            float emissionScale_0;
            float sampleIntensity_1;
            if(lightType_0 >= 2.0f)
            {
                float4 _S1910 = ReadFrameLight_0(0U, lightIndex_0, &kernelContext_2);
                float3 toLight_0 = _S1910.xyz + sampleOffsets_0[sampleIndex_0] - worldPosition_0;
                float _S1911 = max(dot(toLight_0, toLight_0), 0.00100000004749745f);
                float3 sampleDirection_1 = toLight_0 * float3(rsqrt(_S1911)) ;
                float sampleIntensity_2 = sampleIntensity_0 / _S1911;
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
            float _S1912 = pow(max(0.0f, 1.0f - saturate(dot(domeAmbient_1, half_0))), 5.0f);
            float3 diffuseFresnel_0;
            if(omniPbrSpecular_0)
            {
                diffuseFresnel_0 = float3(dielectricLevel_0)  * float3(mix(0.07999999821186066f, 1.0f, _S1912)) ;
            }
            else
            {
                diffuseFresnel_0 = mix(normalIncidence_0, grazingIncidence_0, float3(_S1912) );
            }
            float3 directDiffuse_0 = diffuse_1 * (float3(1.0f)  - diffuseFresnel_0);
            float3 _S1913 = float3(_S1912) ;
            float _S1914 = max(_S1850, 0.00100000004749745f);
            float alpha_0 = _S1914 * _S1914;
            float alphaSquared_0 = alpha_0 * alpha_0;
            float lobeCosineSquared_0 = saturate(normalDotHalf_0 * normalDotHalf_0);
            float lobeComplement_0 = 1.0f - lobeCosineSquared_0;
            float denominator_0 = lobeCosineSquared_0 * alphaSquared_0 + lobeComplement_0;
            float k_0 = alpha_0 * 0.5f;
            float _S1915 = 1.0f - k_0;
            float3 _S1916 = float3(max(4.0f * normalDotLight_0 * _S1849, 1.00000000317107685e-30f)) ;
            float3 _S1917 = mix(normalIncidence_0, grazingIncidence_0, _S1913) * float3((_S1849 / (_S1849 * _S1915 + k_0) * (normalDotLight_0 / (normalDotLight_0 * _S1915 + k_0))))  * float3((alphaSquared_0 / max(3.14159274101257324f * denominator_0 * denominator_0, 1.00000000317107685e-30f)))  / _S1916;
            float3 directSpecular_0;
            if(clearcoatAmount_0 > 0.0f)
            {
                float _S1918 = max(_S1851, 0.00100000004749745f);
                float alpha_1 = _S1918 * _S1918;
                float alphaSquared_1 = alpha_1 * alpha_1;
                float denominator_1 = lobeCosineSquared_0 * alphaSquared_1 + lobeComplement_0;
                float k_1 = alpha_1 * 0.5f;
                float _S1919 = 1.0f - k_1;
                directSpecular_0 = _S1917 + float3(clearcoatAmount_0)  * (mix(float3((reflectanceRatio_0 * reflectanceRatio_0)) , float3(1.0f, 1.0f, 1.0f), _S1913) * float3((_S1849 / (_S1849 * _S1919 + k_1) * (normalDotLight_0 / (normalDotLight_0 * _S1919 + k_1))))  * float3((alphaSquared_1 / max(3.14159274101257324f * denominator_1 * denominator_1, 1.00000000317107685e-30f)))  / _S1916);
            }
            else
            {
                directSpecular_0 = _S1917;
            }
            float3 _S1920 = diffuseColor_0 * float3(sampleIntensity_1) ;
            color_0 = color_0 + float3((shadowVisibility_0 * occlusion_0 * emissionScale_0 * normalDotLight_0))  * (directDiffuse_0 * float3(metallic_0)  * (_S1920 * _S1852) + directSpecular_0 * float3(clearcoatRoughness_0)  * _S1920);
            sampleIndex_0 = sampleIndex_0 + 1U;
        }
        color_3 = color_0;
        lightIndex_0 = lightIndex_0 + 1U;
    }
    FragmentFrameParameters_0 _S1921 = _S36;
    if((_S36.environmentControls_0.x) >= 0.5f)
    {
        float _S1922 = saturate(saturate(abs(dot(worldNormal_2, sceneEye_0)) + 0.00000999999974738f));
        float _S1923 = saturate(_S1850);
        float2 _S1924 = (((&kernelContext_2)->environmentBrdf_0).sample(((&kernelContext_2)->environmentBrdfSampler_0), (float2(_S1922, _S1923)), level((0.0f)))).xy;
        float _S1925 = _S1924.x;
        float _S1926 = _S1924.y;
        float3 specularWeight_0 = normalIncidence_0 * float3(_S1925)  + grazingIncidence_0 * float3(_S1926) ;
        if(omniPbrSpecular_0)
        {
            lightDirection_0 = float3(dielectricLevel_0)  * float3((0.07999999821186066f * _S1925 + _S1926)) ;
        }
        else
        {
            lightDirection_0 = specularWeight_0;
        }
        float3 diffuseWeight_0 = saturate(float3(1.0f, 1.0f, 1.0f) - lightDirection_0);
        float3 reflectionDirection_0 = reflect(- sceneEye_0, worldNormal_2);
        float _S1927 = max(_S1863.domeControls_0.y, 1.0f);
        float3 _S1928 = float3(0.0f, 0.0f, 0.0f);
        if(allDomesLinked_0)
        {
            hasSceneLighting_0 = true;
        }
        else
        {
            hasSceneLighting_0 = _S1927 <= 1.0f;
        }
        if(hasSceneLighting_0)
        {
            float composedGroup_0 = _S1863.domeControls_0.z;
            float _S1929 = _S1863.domeControls_0.w;
            for(;;)
            {
                bool _S1930 = _S1927 <= 1.0f;
                _S38 = _S1930;
                if(_S1930)
                {
                    float3 unit_0 = normalize(worldNormal_2);
                    diffuseColor_0 = (((&kernelContext_2)->environmentIrradiance_0).sample(((&kernelContext_2)->environmentSampler_0), (float2(fract((atan2(unit_0.z, unit_0.x) + 1.57079637050628662f) / 6.28318548202514648f), acos(clamp(unit_0.y, -1.0f, 1.0f)) / 3.14159274101257324f)), level((0.0f)))).xyz;
                    break;
                }
                float3 unit_1 = normalize(worldNormal_2);
                float inset_0 = 0.5f / max(_S1929, 1.0f);
                diffuseColor_0 = (((&kernelContext_2)->environmentIrradiance_0).sample(((&kernelContext_2)->environmentSampler_0), (float2(fract((atan2(unit_1.z, unit_1.x) + 1.57079637050628662f) / 6.28318548202514648f), (composedGroup_0 + clamp(acos(clamp(unit_1.y, -1.0f, 1.0f)) / 3.14159274101257324f, inset_0, 1.0f - inset_0)) / _S1927)), level((0.0f)))).xyz;
                break;
            }
            float _S1931 = _S1921.environmentControls_0.y;
            float _S1932 = _S1921.environmentControls_0.z;
            for(;;)
            {
                if(_S38)
                {
                    float3 unit_2 = normalize(reflectionDirection_0);
                    float u_1 = fract((atan2(unit_2.z, unit_2.x) + 1.57079637050628662f) / 6.28318548202514648f);
                    float _S1933 = max(_S1931, 1.0f);
                    float _S1934 = _S1933 - 1.0f;
                    float slice_0 = _S1923 * max(_S1934, 0.0f);
                    float lower_0 = floor(slice_0);
                    float inset_1 = 0.5f / max(_S1932, 1.0f);
                    float v_0 = clamp(acos(clamp(unit_2.y, -1.0f, 1.0f)) / 3.14159274101257324f, inset_1, 1.0f - inset_1);
                    unlitColor_0 = mix((((&kernelContext_2)->environmentSpecular_0).sample(((&kernelContext_2)->environmentSampler_0), (float2(u_1, (lower_0 + v_0) / _S1933)), level((0.0f)))).xyz, (((&kernelContext_2)->environmentSpecular_0).sample(((&kernelContext_2)->environmentSampler_0), (float2(u_1, (min(lower_0 + 1.0f, _S1934) + v_0) / _S1933)), level((0.0f)))).xyz, float3((slice_0 - lower_0)) );
                    break;
                }
                float3 unit_3 = normalize(reflectionDirection_0);
                float u_2 = fract((atan2(unit_3.z, unit_3.x) + 1.57079637050628662f) / 6.28318548202514648f);
                float _S1935 = max(_S1931, 1.0f);
                float total_0 = _S1935 * _S1927;
                float _S1936 = _S1935 - 1.0f;
                float slice_1 = _S1923 * max(_S1936, 0.0f);
                float lower_1 = floor(slice_1);
                float inset_2 = 0.5f / max(_S1932, 1.0f);
                float v_1 = clamp(acos(clamp(unit_3.y, -1.0f, 1.0f)) / 3.14159274101257324f, inset_2, 1.0f - inset_2);
                float base_0 = composedGroup_0 * _S1935;
                unlitColor_0 = mix((((&kernelContext_2)->environmentSpecular_0).sample(((&kernelContext_2)->environmentSampler_0), (float2(u_2, (base_0 + lower_1 + v_1) / total_0)), level((0.0f)))).xyz, (((&kernelContext_2)->environmentSpecular_0).sample(((&kernelContext_2)->environmentSampler_0), (float2(u_2, (base_0 + min(lower_1 + 1.0f, _S1936) + v_1) / total_0)), level((0.0f)))).xyz, float3((slice_1 - lower_1)) );
                break;
            }
            if(clearcoatAmount_0 > 0.0f)
            {
                for(;;)
                {
                    if(_S38)
                    {
                        float3 unit_4 = normalize(reflectionDirection_0);
                        float u_3 = fract((atan2(unit_4.z, unit_4.x) + 1.57079637050628662f) / 6.28318548202514648f);
                        float _S1937 = max(_S1931, 1.0f);
                        float _S1938 = _S1937 - 1.0f;
                        float slice_2 = saturate(_S1851) * max(_S1938, 0.0f);
                        float lower_2 = floor(slice_2);
                        float inset_3 = 0.5f / max(_S1932, 1.0f);
                        float v_2 = clamp(acos(clamp(unit_4.y, -1.0f, 1.0f)) / 3.14159274101257324f, inset_3, 1.0f - inset_3);
                        specularColor_0 = mix((((&kernelContext_2)->environmentSpecular_0).sample(((&kernelContext_2)->environmentSampler_0), (float2(u_3, (lower_2 + v_2) / _S1937)), level((0.0f)))).xyz, (((&kernelContext_2)->environmentSpecular_0).sample(((&kernelContext_2)->environmentSampler_0), (float2(u_3, (min(lower_2 + 1.0f, _S1938) + v_2) / _S1937)), level((0.0f)))).xyz, float3((slice_2 - lower_2)) );
                        break;
                    }
                    float3 unit_5 = normalize(reflectionDirection_0);
                    float u_4 = fract((atan2(unit_5.z, unit_5.x) + 1.57079637050628662f) / 6.28318548202514648f);
                    float _S1939 = max(_S1931, 1.0f);
                    float total_1 = _S1939 * _S1927;
                    float _S1940 = _S1939 - 1.0f;
                    float slice_3 = saturate(_S1851) * max(_S1940, 0.0f);
                    float lower_3 = floor(slice_3);
                    float inset_4 = 0.5f / max(_S1932, 1.0f);
                    float v_3 = clamp(acos(clamp(unit_5.y, -1.0f, 1.0f)) / 3.14159274101257324f, inset_4, 1.0f - inset_4);
                    float base_1 = composedGroup_0 * _S1939;
                    specularColor_0 = mix((((&kernelContext_2)->environmentSpecular_0).sample(((&kernelContext_2)->environmentSampler_0), (float2(u_4, (base_1 + lower_3 + v_3) / total_1)), level((0.0f)))).xyz, (((&kernelContext_2)->environmentSpecular_0).sample(((&kernelContext_2)->environmentSampler_0), (float2(u_4, (base_1 + min(lower_3 + 1.0f, _S1940) + v_3) / total_1)), level((0.0f)))).xyz, float3((slice_3 - lower_3)) );
                    break;
                }
                color_0 = specularColor_0;
            }
            else
            {
                color_0 = _S1928;
            }
            lightTangent_0 = diffuseColor_0;
            lightBitangent_0 = unlitColor_0;
        }
        else
        {
            sampleIndex_0 = 0U;
            lightTangent_0 = _S1928;
            lightBitangent_0 = _S1928;
            color_0 = _S1928;
            for(;;)
            {
                if(sampleIndex_0 < _S1864)
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
                float domeGroup_0 = _S36.domeEnvironment_0[sampleIndex_0].x;
                if(domeGroup_0 < 0.0f)
                {
                    sampleIndex_0 = sampleIndex_0 + 1U;
                    continue;
                }
                float _S1941 = _S1863.domeControls_0.w;
                for(;;)
                {
                    bool _S1942 = _S1927 <= 1.0f;
                    _S39 = _S1942;
                    if(_S1942)
                    {
                        float3 unit_6 = normalize(worldNormal_2);
                        diffuseColor_0 = (((&kernelContext_2)->environmentIrradiance_0).sample(((&kernelContext_2)->environmentSampler_0), (float2(fract((atan2(unit_6.z, unit_6.x) + 1.57079637050628662f) / 6.28318548202514648f), acos(clamp(unit_6.y, -1.0f, 1.0f)) / 3.14159274101257324f)), level((0.0f)))).xyz;
                        break;
                    }
                    float3 unit_7 = normalize(worldNormal_2);
                    float inset_5 = 0.5f / max(_S1941, 1.0f);
                    diffuseColor_0 = (((&kernelContext_2)->environmentIrradiance_0).sample(((&kernelContext_2)->environmentSampler_0), (float2(fract((atan2(unit_7.z, unit_7.x) + 1.57079637050628662f) / 6.28318548202514648f), (domeGroup_0 + clamp(acos(clamp(unit_7.y, -1.0f, 1.0f)) / 3.14159274101257324f, inset_5, 1.0f - inset_5)) / _S1927)), level((0.0f)))).xyz;
                    break;
                }
                float3 irradiance_0 = lightTangent_0 + diffuseColor_0;
                float _S1943 = _S1921.environmentControls_0.y;
                float _S1944 = _S1921.environmentControls_0.z;
                for(;;)
                {
                    if(_S39)
                    {
                        float3 unit_8 = normalize(reflectionDirection_0);
                        float u_5 = fract((atan2(unit_8.z, unit_8.x) + 1.57079637050628662f) / 6.28318548202514648f);
                        float _S1945 = max(_S1943, 1.0f);
                        float _S1946 = _S1945 - 1.0f;
                        float slice_4 = _S1923 * max(_S1946, 0.0f);
                        float lower_4 = floor(slice_4);
                        float inset_6 = 0.5f / max(_S1944, 1.0f);
                        float v_4 = clamp(acos(clamp(unit_8.y, -1.0f, 1.0f)) / 3.14159274101257324f, inset_6, 1.0f - inset_6);
                        unlitColor_0 = mix((((&kernelContext_2)->environmentSpecular_0).sample(((&kernelContext_2)->environmentSampler_0), (float2(u_5, (lower_4 + v_4) / _S1945)), level((0.0f)))).xyz, (((&kernelContext_2)->environmentSpecular_0).sample(((&kernelContext_2)->environmentSampler_0), (float2(u_5, (min(lower_4 + 1.0f, _S1946) + v_4) / _S1945)), level((0.0f)))).xyz, float3((slice_4 - lower_4)) );
                        break;
                    }
                    float3 unit_9 = normalize(reflectionDirection_0);
                    float u_6 = fract((atan2(unit_9.z, unit_9.x) + 1.57079637050628662f) / 6.28318548202514648f);
                    float _S1947 = max(_S1943, 1.0f);
                    float total_2 = _S1947 * _S1927;
                    float _S1948 = _S1947 - 1.0f;
                    float slice_5 = _S1923 * max(_S1948, 0.0f);
                    float lower_5 = floor(slice_5);
                    float inset_7 = 0.5f / max(_S1944, 1.0f);
                    float v_5 = clamp(acos(clamp(unit_9.y, -1.0f, 1.0f)) / 3.14159274101257324f, inset_7, 1.0f - inset_7);
                    float base_2 = domeGroup_0 * _S1947;
                    unlitColor_0 = mix((((&kernelContext_2)->environmentSpecular_0).sample(((&kernelContext_2)->environmentSampler_0), (float2(u_6, (base_2 + lower_5 + v_5) / total_2)), level((0.0f)))).xyz, (((&kernelContext_2)->environmentSpecular_0).sample(((&kernelContext_2)->environmentSampler_0), (float2(u_6, (base_2 + min(lower_5 + 1.0f, _S1948) + v_5) / total_2)), level((0.0f)))).xyz, float3((slice_5 - lower_5)) );
                    break;
                }
                float3 prefiltered_0 = lightBitangent_0 + unlitColor_0;
                if(clearcoatAmount_0 > 0.0f)
                {
                    for(;;)
                    {
                        if(_S39)
                        {
                            float3 unit_10 = normalize(reflectionDirection_0);
                            float u_7 = fract((atan2(unit_10.z, unit_10.x) + 1.57079637050628662f) / 6.28318548202514648f);
                            float _S1949 = max(_S1943, 1.0f);
                            float _S1950 = _S1949 - 1.0f;
                            float slice_6 = saturate(_S1851) * max(_S1950, 0.0f);
                            float lower_6 = floor(slice_6);
                            float inset_8 = 0.5f / max(_S1944, 1.0f);
                            float v_6 = clamp(acos(clamp(unit_10.y, -1.0f, 1.0f)) / 3.14159274101257324f, inset_8, 1.0f - inset_8);
                            specularColor_0 = mix((((&kernelContext_2)->environmentSpecular_0).sample(((&kernelContext_2)->environmentSampler_0), (float2(u_7, (lower_6 + v_6) / _S1949)), level((0.0f)))).xyz, (((&kernelContext_2)->environmentSpecular_0).sample(((&kernelContext_2)->environmentSampler_0), (float2(u_7, (min(lower_6 + 1.0f, _S1950) + v_6) / _S1949)), level((0.0f)))).xyz, float3((slice_6 - lower_6)) );
                            break;
                        }
                        float3 unit_11 = normalize(reflectionDirection_0);
                        float u_8 = fract((atan2(unit_11.z, unit_11.x) + 1.57079637050628662f) / 6.28318548202514648f);
                        float _S1951 = max(_S1943, 1.0f);
                        float total_3 = _S1951 * _S1927;
                        float _S1952 = _S1951 - 1.0f;
                        float slice_7 = saturate(_S1851) * max(_S1952, 0.0f);
                        float lower_7 = floor(slice_7);
                        float inset_9 = 0.5f / max(_S1944, 1.0f);
                        float v_7 = clamp(acos(clamp(unit_11.y, -1.0f, 1.0f)) / 3.14159274101257324f, inset_9, 1.0f - inset_9);
                        float base_3 = domeGroup_0 * _S1951;
                        specularColor_0 = mix((((&kernelContext_2)->environmentSpecular_0).sample(((&kernelContext_2)->environmentSampler_0), (float2(u_8, (base_3 + lower_7 + v_7) / total_3)), level((0.0f)))).xyz, (((&kernelContext_2)->environmentSpecular_0).sample(((&kernelContext_2)->environmentSampler_0), (float2(u_8, (base_3 + min(lower_7 + 1.0f, _S1952) + v_7) / total_3)), level((0.0f)))).xyz, float3((slice_7 - lower_7)) );
                        break;
                    }
                    sampleDirection_0 = color_0 + specularColor_0;
                }
                else
                {
                    sampleDirection_0 = color_0;
                }
                lightTangent_0 = irradiance_0;
                lightBitangent_0 = prefiltered_0;
                color_0 = sampleDirection_0;
                sampleIndex_0 = sampleIndex_0 + 1U;
            }
        }
        float3 color_4 = color_3 + float3(occlusion_0)  * diffuse_1 * lightTangent_0 * diffuseWeight_0 + float3(occlusion_0)  * lightBitangent_0 * specularWeight_0;
        if(clearcoatAmount_0 > 0.0f)
        {
            float2 _S1953 = (((&kernelContext_2)->environmentBrdf_0).sample(((&kernelContext_2)->environmentBrdfSampler_0), (float2(_S1922, saturate(_S1851))), level((0.0f)))).xy;
            color_3 = color_4 + float3((occlusion_0 * clearcoatAmount_0))  * color_0 * (float3((reflectanceRatio_0 * reflectanceRatio_0))  * float3(_S1953.x)  + float3(_S1953.y) );
        }
        else
        {
            color_3 = color_4;
        }
    }
    float3 color_5 = (color_3 + emissiveColor_1) * float3(exp2((as_type<float>((_S37.z))))) ;
    if((_S37.y) == 1U)
    {
        color_3 = color_5 / (float3(1.0f)  + max(color_5, float3(0.0f, 0.0f, 0.0f)));
    }
    else
    {
        color_3 = color_5;
    }
    pixelOutput_0 _S1954 = { float4(color_3, opacity_0) };
    return _S1954;
}
