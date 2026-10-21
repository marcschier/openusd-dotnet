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
    uint _S21 = 224U + field_0 * 2048U + index_1 * 16U;
    float _S22 = as_type<float>(kernelContext_0->frameParameters_0[(_S21)>>2]);
    float _S23 = as_type<float>(kernelContext_0->frameParameters_0[(_S21 + 4U)>>2]);
    float _S24 = as_type<float>(kernelContext_0->frameParameters_0[(_S21 + 8U)>>2]);
    float _S25 = as_type<float>(kernelContext_0->frameParameters_0[(_S21 + 12U)>>2]);
    return float4(_S22, _S23, _S24, _S25);
}

float4 ReadFrameShadowSlot_0(uint index_2, KernelContext_0 thread* kernelContext_1)
{
    uint _S26 = 12960U + index_2 * 16U;
    float _S27 = as_type<float>(kernelContext_1->frameParameters_0[(_S26)>>2]);
    float _S28 = as_type<float>(kernelContext_1->frameParameters_0[(_S26 + 4U)>>2]);
    float _S29 = as_type<float>(kernelContext_1->frameParameters_0[(_S26 + 8U)>>2]);
    float _S30 = as_type<float>(kernelContext_1->frameParameters_0[(_S26 + 12U)>>2]);
    return float4(_S27, _S28, _S29, _S30);
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

[[fragment]] pixelOutput_0 fragmentMain_uv_material(pixelInput_0 _S31 [[stage_in]], bool isFrontFace_0 [[front_facing]], float4 position_0 [[position]], SurfaceParameters_natural_0 device* surfaceParameters_1 [[buffer(7)]], uint32_t device* frameParameters_1 [[buffer(8)]], texture2d<float, access::sample> baseColorTexture_1 [[texture(0)]], sampler baseColorSampler_1 [[sampler(0)]], texture2d<float, access::sample> diffuseValuesTexture_1 [[texture(22)]], texture2d<float, access::sample> compositeTexture_1 [[texture(15)]], sampler compositeSampler_1 [[sampler(12)]], texture2d<float, access::sample> roughnessMetallicTexture_1 [[texture(2)]], sampler roughnessMetallicSampler_1 [[sampler(2)]], texture2d<float, access::sample> metallicTexture_1 [[texture(4)]], sampler metallicSampler_1 [[sampler(5)]], texture2d<float, access::sample> emissiveTexture_1 [[texture(3)]], sampler emissiveSampler_1 [[sampler(3)]], texture2d<float, access::sample> emissiveValuesTexture_1 [[texture(23)]], texture2d<float, access::sample> opacityTexture_1 [[texture(5)]], sampler opacitySampler_1 [[sampler(6)]], texture2d<float, access::sample> occlusionTexture_1 [[texture(10)]], sampler occlusionSampler_1 [[sampler(7)]], texture2d<float, access::sample> specularColorTexture_1 [[texture(11)]], sampler specularColorSampler_1 [[sampler(8)]], texture2d<float, access::sample> clearcoatTexture_1 [[texture(12)]], sampler clearcoatSampler_1 [[sampler(9)]], texture2d<float, access::sample> clearcoatRoughnessTexture_1 [[texture(13)]], sampler clearcoatRoughnessSampler_1 [[sampler(10)]], texture2d<float, access::sample> iorTexture_1 [[texture(14)]], sampler iorSampler_1 [[sampler(11)]], texture2d<float, access::sample> shadowAtlas_1 [[texture(16)]], sampler shadowSampler_1 [[sampler(13)]], texture2d<float, access::sample> environmentBrdf_1 [[texture(19)]], sampler environmentBrdfSampler_1 [[sampler(15)]], texture2d<float, access::sample> environmentIrradiance_1 [[texture(17)]], sampler environmentSampler_1 [[sampler(14)]], texture2d<float, access::sample> environmentSpecular_1 [[texture(18)]])
{
    uint index_3;
    FragmentFrameParameters_0 _S32;
    uint4 _S33;
    uint sampleIndex_0;
    float3 lightDirection_0;
    float3 lightTangent_0;
    float3 lightBitangent_0;
    float3 color_0;
    bool _S34;
    bool _S35;
    thread KernelContext_0 kernelContext_2;
    (&kernelContext_2)->surfaceParameters_0 = surfaceParameters_1;
    (&kernelContext_2)->frameParameters_0 = frameParameters_1;
    (&kernelContext_2)->baseColorTexture_0 = baseColorTexture_1;
    (&kernelContext_2)->baseColorSampler_0 = baseColorSampler_1;
    (&kernelContext_2)->diffuseValuesTexture_0 = diffuseValuesTexture_1;
    (&kernelContext_2)->compositeTexture_0 = compositeTexture_1;
    (&kernelContext_2)->compositeSampler_0 = compositeSampler_1;
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
    SurfaceParameters_natural_0 device* _S36 = surfaceParameters_1+int(0);
    for(;;)
    {
        thread FragmentFrameParameters_0 frame_0;
        uint _S37 = as_type<uint>((&kernelContext_2)->frameParameters_0[(64U)>>2]);
        uint _S38 = as_type<uint>((&kernelContext_2)->frameParameters_0[(68U)>>2]);
        uint _S39 = as_type<uint>((&kernelContext_2)->frameParameters_0[(72U)>>2]);
        uint _S40 = as_type<uint>((&kernelContext_2)->frameParameters_0[(76U)>>2]);
        (&frame_0)->clipPlaneCount_0 = uint4(_S37, _S38, _S39, _S40);
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
            uint _S41 = 80U + index_3 * 16U;
            float _S42 = as_type<float>((&kernelContext_2)->frameParameters_0[(_S41)>>2]);
            float _S43 = as_type<float>((&kernelContext_2)->frameParameters_0[(_S41 + 4U)>>2]);
            float _S44 = as_type<float>((&kernelContext_2)->frameParameters_0[(_S41 + 8U)>>2]);
            float _S45 = as_type<float>((&kernelContext_2)->frameParameters_0[(_S41 + 12U)>>2]);
            (&frame_0)->clipPlanes_0[index_3] = float4(_S42, _S43, _S44, _S45);
            index_3 = index_3 + 1U;
        }
        float _S46 = as_type<float>((&kernelContext_2)->frameParameters_0[(208U)>>2]);
        float _S47 = as_type<float>((&kernelContext_2)->frameParameters_0[(212U)>>2]);
        float _S48 = as_type<float>((&kernelContext_2)->frameParameters_0[(216U)>>2]);
        float _S49 = as_type<float>((&kernelContext_2)->frameParameters_0[(220U)>>2]);
        (&frame_0)->ambientLight_0 = float4(_S46, _S47, _S48, _S49);
        (&(&frame_0)->lightPositionType_0)->byteOffset_0 = 224U;
        (&(&frame_0)->lightDirectionRadius_0)->byteOffset_0 = 2272U;
        (&(&frame_0)->lightColorIntensity_0)->byteOffset_0 = 4320U;
        (&(&frame_0)->lightControls_0)->byteOffset_0 = 6368U;
        (&(&frame_0)->lightTangentShapeX_0)->byteOffset_0 = 8416U;
        (&(&frame_0)->lightBitangentShapeY_0)->byteOffset_0 = 10464U;
        (&(&frame_0)->shadowSlots_0)->byteOffset_0 = 12960U;
        float _S50 = as_type<float>((&kernelContext_2)->frameParameters_0[(12512U)>>2]);
        float _S51 = as_type<float>((&kernelContext_2)->frameParameters_0[(12516U)>>2]);
        float _S52 = as_type<float>((&kernelContext_2)->frameParameters_0[(12520U)>>2]);
        float _S53 = as_type<float>((&kernelContext_2)->frameParameters_0[(12524U)>>2]);
        float4 _S54 = float4(_S50, _S51, _S52, _S53);
        float _S55 = as_type<float>((&kernelContext_2)->frameParameters_0[(12528U)>>2]);
        float _S56 = as_type<float>((&kernelContext_2)->frameParameters_0[(12532U)>>2]);
        float _S57 = as_type<float>((&kernelContext_2)->frameParameters_0[(12536U)>>2]);
        float _S58 = as_type<float>((&kernelContext_2)->frameParameters_0[(12540U)>>2]);
        float4 _S59 = float4(_S55, _S56, _S57, _S58);
        float _S60 = as_type<float>((&kernelContext_2)->frameParameters_0[(12544U)>>2]);
        float _S61 = as_type<float>((&kernelContext_2)->frameParameters_0[(12548U)>>2]);
        float _S62 = as_type<float>((&kernelContext_2)->frameParameters_0[(12552U)>>2]);
        float _S63 = as_type<float>((&kernelContext_2)->frameParameters_0[(12556U)>>2]);
        float4 _S64 = float4(_S60, _S61, _S62, _S63);
        float _S65 = as_type<float>((&kernelContext_2)->frameParameters_0[(12560U)>>2]);
        float _S66 = as_type<float>((&kernelContext_2)->frameParameters_0[(12564U)>>2]);
        float _S67 = as_type<float>((&kernelContext_2)->frameParameters_0[(12568U)>>2]);
        float _S68 = as_type<float>((&kernelContext_2)->frameParameters_0[(12572U)>>2]);
        (&frame_0)->eyeToWorld_0 = matrix<float,int(4),int(4)> (_S54, _S59, _S64, float4(_S65, _S66, _S67, _S68));
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
            uint _S69 = 12576U + index_3 * 64U;
            float _S70 = as_type<float>((&kernelContext_2)->frameParameters_0[(_S69)>>2]);
            float _S71 = as_type<float>((&kernelContext_2)->frameParameters_0[(_S69 + 4U)>>2]);
            float _S72 = as_type<float>((&kernelContext_2)->frameParameters_0[(_S69 + 8U)>>2]);
            float _S73 = as_type<float>((&kernelContext_2)->frameParameters_0[(_S69 + 12U)>>2]);
            float4 _S74 = float4(_S70, _S71, _S72, _S73);
            float _S75 = as_type<float>((&kernelContext_2)->frameParameters_0[(_S69 + 16U)>>2]);
            float _S76 = as_type<float>((&kernelContext_2)->frameParameters_0[(_S69 + 20U)>>2]);
            float _S77 = as_type<float>((&kernelContext_2)->frameParameters_0[(_S69 + 24U)>>2]);
            float _S78 = as_type<float>((&kernelContext_2)->frameParameters_0[(_S69 + 28U)>>2]);
            float4 _S79 = float4(_S75, _S76, _S77, _S78);
            float _S80 = as_type<float>((&kernelContext_2)->frameParameters_0[(_S69 + 32U)>>2]);
            float _S81 = as_type<float>((&kernelContext_2)->frameParameters_0[(_S69 + 36U)>>2]);
            float _S82 = as_type<float>((&kernelContext_2)->frameParameters_0[(_S69 + 40U)>>2]);
            float _S83 = as_type<float>((&kernelContext_2)->frameParameters_0[(_S69 + 44U)>>2]);
            float4 _S84 = float4(_S80, _S81, _S82, _S83);
            float _S85 = as_type<float>((&kernelContext_2)->frameParameters_0[(_S69 + 48U)>>2]);
            float _S86 = as_type<float>((&kernelContext_2)->frameParameters_0[(_S69 + 52U)>>2]);
            float _S87 = as_type<float>((&kernelContext_2)->frameParameters_0[(_S69 + 56U)>>2]);
            float _S88 = as_type<float>((&kernelContext_2)->frameParameters_0[(_S69 + 60U)>>2]);
            (&frame_0)->shadowWorldToLightClip_0[index_3] = matrix<float,int(4),int(4)> (_S74, _S79, _S84, float4(_S85, _S86, _S87, _S88));
            uint _S89 = index_3 * 16U;
            uint _S90 = 12832U + _S89;
            float _S91 = as_type<float>((&kernelContext_2)->frameParameters_0[(_S90)>>2]);
            float _S92 = as_type<float>((&kernelContext_2)->frameParameters_0[(_S90 + 4U)>>2]);
            float _S93 = as_type<float>((&kernelContext_2)->frameParameters_0[(_S90 + 8U)>>2]);
            float _S94 = as_type<float>((&kernelContext_2)->frameParameters_0[(_S90 + 12U)>>2]);
            (&frame_0)->shadowTile_0[index_3] = float4(_S91, _S92, _S93, _S94);
            uint _S95 = 12896U + _S89;
            float _S96 = as_type<float>((&kernelContext_2)->frameParameters_0[(_S95)>>2]);
            float _S97 = as_type<float>((&kernelContext_2)->frameParameters_0[(_S95 + 4U)>>2]);
            float _S98 = as_type<float>((&kernelContext_2)->frameParameters_0[(_S95 + 8U)>>2]);
            float _S99 = as_type<float>((&kernelContext_2)->frameParameters_0[(_S95 + 12U)>>2]);
            (&frame_0)->shadowControls_0[index_3] = float4(_S96, _S97, _S98, _S99);
            index_3 = index_3 + 1U;
        }
        float _S100 = as_type<float>((&kernelContext_2)->frameParameters_0[(15008U)>>2]);
        float _S101 = as_type<float>((&kernelContext_2)->frameParameters_0[(15012U)>>2]);
        float _S102 = as_type<float>((&kernelContext_2)->frameParameters_0[(15016U)>>2]);
        float _S103 = as_type<float>((&kernelContext_2)->frameParameters_0[(15020U)>>2]);
        (&frame_0)->environmentControls_0 = float4(_S100, _S101, _S102, _S103);
        float _S104 = as_type<float>((&kernelContext_2)->frameParameters_0[(15024U)>>2]);
        float _S105 = as_type<float>((&kernelContext_2)->frameParameters_0[(15028U)>>2]);
        float _S106 = as_type<float>((&kernelContext_2)->frameParameters_0[(15032U)>>2]);
        float _S107 = as_type<float>((&kernelContext_2)->frameParameters_0[(15036U)>>2]);
        (&frame_0)->domeControls_0 = float4(_S104, _S105, _S106, _S107);
        float _S108 = as_type<float>((&kernelContext_2)->frameParameters_0[(15040U)>>2]);
        float _S109 = as_type<float>((&kernelContext_2)->frameParameters_0[(15044U)>>2]);
        float _S110 = as_type<float>((&kernelContext_2)->frameParameters_0[(15048U)>>2]);
        float _S111 = as_type<float>((&kernelContext_2)->frameParameters_0[(15052U)>>2]);
        float4 _S112 = float4(_S108, _S109, _S110, _S111);
        float _S113 = as_type<float>((&kernelContext_2)->frameParameters_0[(15056U)>>2]);
        float _S114 = as_type<float>((&kernelContext_2)->frameParameters_0[(15060U)>>2]);
        float _S115 = as_type<float>((&kernelContext_2)->frameParameters_0[(15064U)>>2]);
        float _S116 = as_type<float>((&kernelContext_2)->frameParameters_0[(15068U)>>2]);
        float4 _S117 = float4(_S113, _S114, _S115, _S116);
        float _S118 = as_type<float>((&kernelContext_2)->frameParameters_0[(15072U)>>2]);
        float _S119 = as_type<float>((&kernelContext_2)->frameParameters_0[(15076U)>>2]);
        float _S120 = as_type<float>((&kernelContext_2)->frameParameters_0[(15080U)>>2]);
        float _S121 = as_type<float>((&kernelContext_2)->frameParameters_0[(15084U)>>2]);
        float4 _S122 = float4(_S118, _S119, _S120, _S121);
        float _S123 = as_type<float>((&kernelContext_2)->frameParameters_0[(15088U)>>2]);
        float _S124 = as_type<float>((&kernelContext_2)->frameParameters_0[(15092U)>>2]);
        float _S125 = as_type<float>((&kernelContext_2)->frameParameters_0[(15096U)>>2]);
        float _S126 = as_type<float>((&kernelContext_2)->frameParameters_0[(15100U)>>2]);
        float4 _S127 = float4(_S123, _S124, _S125, _S126);
        float _S128 = as_type<float>((&kernelContext_2)->frameParameters_0[(15104U)>>2]);
        float _S129 = as_type<float>((&kernelContext_2)->frameParameters_0[(15108U)>>2]);
        float _S130 = as_type<float>((&kernelContext_2)->frameParameters_0[(15112U)>>2]);
        float _S131 = as_type<float>((&kernelContext_2)->frameParameters_0[(15116U)>>2]);
        float4 _S132 = float4(_S128, _S129, _S130, _S131);
        float _S133 = as_type<float>((&kernelContext_2)->frameParameters_0[(15120U)>>2]);
        float _S134 = as_type<float>((&kernelContext_2)->frameParameters_0[(15124U)>>2]);
        float _S135 = as_type<float>((&kernelContext_2)->frameParameters_0[(15128U)>>2]);
        float _S136 = as_type<float>((&kernelContext_2)->frameParameters_0[(15132U)>>2]);
        float4 _S137 = float4(_S133, _S134, _S135, _S136);
        float _S138 = as_type<float>((&kernelContext_2)->frameParameters_0[(15136U)>>2]);
        float _S139 = as_type<float>((&kernelContext_2)->frameParameters_0[(15140U)>>2]);
        float _S140 = as_type<float>((&kernelContext_2)->frameParameters_0[(15144U)>>2]);
        float _S141 = as_type<float>((&kernelContext_2)->frameParameters_0[(15148U)>>2]);
        float4 _S142 = float4(_S138, _S139, _S140, _S141);
        float _S143 = as_type<float>((&kernelContext_2)->frameParameters_0[(15152U)>>2]);
        float _S144 = as_type<float>((&kernelContext_2)->frameParameters_0[(15156U)>>2]);
        float _S145 = as_type<float>((&kernelContext_2)->frameParameters_0[(15160U)>>2]);
        float _S146 = as_type<float>((&kernelContext_2)->frameParameters_0[(15164U)>>2]);
        float4 _S147 = float4(_S143, _S144, _S145, _S146);
        float _S148 = as_type<float>((&kernelContext_2)->frameParameters_0[(15168U)>>2]);
        float _S149 = as_type<float>((&kernelContext_2)->frameParameters_0[(15172U)>>2]);
        float _S150 = as_type<float>((&kernelContext_2)->frameParameters_0[(15176U)>>2]);
        float _S151 = as_type<float>((&kernelContext_2)->frameParameters_0[(15180U)>>2]);
        float4 _S152 = float4(_S148, _S149, _S150, _S151);
        float _S153 = as_type<float>((&kernelContext_2)->frameParameters_0[(15184U)>>2]);
        float _S154 = as_type<float>((&kernelContext_2)->frameParameters_0[(15188U)>>2]);
        float _S155 = as_type<float>((&kernelContext_2)->frameParameters_0[(15192U)>>2]);
        float _S156 = as_type<float>((&kernelContext_2)->frameParameters_0[(15196U)>>2]);
        float4 _S157 = float4(_S153, _S154, _S155, _S156);
        float _S158 = as_type<float>((&kernelContext_2)->frameParameters_0[(15200U)>>2]);
        float _S159 = as_type<float>((&kernelContext_2)->frameParameters_0[(15204U)>>2]);
        float _S160 = as_type<float>((&kernelContext_2)->frameParameters_0[(15208U)>>2]);
        float _S161 = as_type<float>((&kernelContext_2)->frameParameters_0[(15212U)>>2]);
        float4 _S162 = float4(_S158, _S159, _S160, _S161);
        float _S163 = as_type<float>((&kernelContext_2)->frameParameters_0[(15216U)>>2]);
        float _S164 = as_type<float>((&kernelContext_2)->frameParameters_0[(15220U)>>2]);
        float _S165 = as_type<float>((&kernelContext_2)->frameParameters_0[(15224U)>>2]);
        float _S166 = as_type<float>((&kernelContext_2)->frameParameters_0[(15228U)>>2]);
        float4 _S167 = float4(_S163, _S164, _S165, _S166);
        float _S168 = as_type<float>((&kernelContext_2)->frameParameters_0[(15232U)>>2]);
        float _S169 = as_type<float>((&kernelContext_2)->frameParameters_0[(15236U)>>2]);
        float _S170 = as_type<float>((&kernelContext_2)->frameParameters_0[(15240U)>>2]);
        float _S171 = as_type<float>((&kernelContext_2)->frameParameters_0[(15244U)>>2]);
        float4 _S172 = float4(_S168, _S169, _S170, _S171);
        float _S173 = as_type<float>((&kernelContext_2)->frameParameters_0[(15248U)>>2]);
        float _S174 = as_type<float>((&kernelContext_2)->frameParameters_0[(15252U)>>2]);
        float _S175 = as_type<float>((&kernelContext_2)->frameParameters_0[(15256U)>>2]);
        float _S176 = as_type<float>((&kernelContext_2)->frameParameters_0[(15260U)>>2]);
        float4 _S177 = float4(_S173, _S174, _S175, _S176);
        float _S178 = as_type<float>((&kernelContext_2)->frameParameters_0[(15264U)>>2]);
        float _S179 = as_type<float>((&kernelContext_2)->frameParameters_0[(15268U)>>2]);
        float _S180 = as_type<float>((&kernelContext_2)->frameParameters_0[(15272U)>>2]);
        float _S181 = as_type<float>((&kernelContext_2)->frameParameters_0[(15276U)>>2]);
        float4 _S182 = float4(_S178, _S179, _S180, _S181);
        float _S183 = as_type<float>((&kernelContext_2)->frameParameters_0[(15280U)>>2]);
        float _S184 = as_type<float>((&kernelContext_2)->frameParameters_0[(15284U)>>2]);
        float _S185 = as_type<float>((&kernelContext_2)->frameParameters_0[(15288U)>>2]);
        float _S186 = as_type<float>((&kernelContext_2)->frameParameters_0[(15292U)>>2]);
        float4 _S187 = float4(_S183, _S184, _S185, _S186);
        (&frame_0)->domeAmbient_0[int(0)] = _S112;
        (&frame_0)->domeAmbient_0[int(1)] = _S117;
        (&frame_0)->domeAmbient_0[int(2)] = _S122;
        (&frame_0)->domeAmbient_0[int(3)] = _S127;
        (&frame_0)->domeAmbient_0[int(4)] = _S132;
        (&frame_0)->domeAmbient_0[int(5)] = _S137;
        (&frame_0)->domeAmbient_0[int(6)] = _S142;
        (&frame_0)->domeAmbient_0[int(7)] = _S147;
        (&frame_0)->domeEnvironment_0[int(0)] = _S152;
        (&frame_0)->domeEnvironment_0[int(1)] = _S157;
        (&frame_0)->domeEnvironment_0[int(2)] = _S162;
        (&frame_0)->domeEnvironment_0[int(3)] = _S167;
        (&frame_0)->domeEnvironment_0[int(4)] = _S172;
        (&frame_0)->domeEnvironment_0[int(5)] = _S177;
        (&frame_0)->domeEnvironment_0[int(6)] = _S182;
        (&frame_0)->domeEnvironment_0[int(7)] = _S187;
        _S32 = frame_0;
        break;
    }
    for(;;)
    {
        _S33 = _S32.clipPlaneCount_0;
        uint _S188 = min(_S32.clipPlaneCount_0.x, 8U);
        index_3 = 0U;
        for(;;)
        {
            if(index_3 < _S188)
            {
            }
            else
            {
                break;
            }
            if((dot(_S32.clipPlanes_0[index_3].xyz, _S31.eyePosition_0) + _S32.clipPlanes_0[index_3].w) < 0.0f)
            {
                discard_fragment();
            }
            index_3 = index_3 + 1U;
        }
        break;
    }
    float4 _S189 = float4(_S36->clearcoatShaded_0) ;
    float shadedMode_0 = _S189.z;
    bool shaded_0 = shadedMode_0 >= 0.5f;
    bool unlit_0 = shadedMode_0 >= 1.5f;
    float3 diffuseColor_0;
    if(shaded_0)
    {
        diffuseColor_0 = (float4(_S36->diffuseOpacity_0) ).xyz;
    }
    else
    {
        diffuseColor_0 = _S31.tint_0.xyz;
    }
    float opacity_0;
    if(shaded_0)
    {
        opacity_0 = (float4(_S36->diffuseOpacity_0) ).w;
    }
    else
    {
        opacity_0 = _S31.tint_0.w;
    }
    float4 _S190 = float4(_S36->emissiveOcclusion_0) ;
    float3 emissiveColor_0 = _S190.xyz;
    float _S191 = _S190.w;
    float3 unlitColor_0;
    if(unlit_0)
    {
        float3 unlitColor_1 = (diffuseColor_0 + emissiveColor_0) * float3(exp2((as_type<float>((_S33.z))))) ;
        if((_S33.y) == 1U)
        {
            unlitColor_0 = unlitColor_1 / (float3(1.0f)  + max(unlitColor_1, float3(0.0f, 0.0f, 0.0f)));
        }
        else
        {
            unlitColor_0 = unlitColor_1;
        }
        pixelOutput_0 _S192 = { float4(unlitColor_0, opacity_0) };
        return _S192;
    }
    float4 _S193 = float4(_S36->reserved_0) ;
    if((_S193.x) >= 0.5f)
    {
        pixelOutput_0 _S194 = { float4(diffuseColor_0 * float3((1.0f - exp(- max(0.0f, _S193.y) * max(0.0f, _S193.z)))) , 1.0f) };
        return _S194;
    }
    float4 _S195 = float4(_S36->metallicRoughnessThresholdWorkflow_0) ;
    float _S196 = saturate(_S195.x);
    float _S197 = clamp(_S195.y, 0.00999999977648258f, 1.0f);
    float4 _S198 = float4(_S36->specularIor_0) ;
    float3 _S199 = _S198.xyz;
    float _S200 = _S198.w;
    float _S201 = _S189.x;
    float _S202 = _S189.y;
    float4 _S203 = float4(_S36->textureControls_0) ;
    uint textureMask_0 = uint(round(_S203.x));
    uint udimMask_0 = uint(round(_S203.y));
    float4 _S204 = float4(_S36->uvTransformRow0_0) ;
    float4 _S205 = float4(_S36->uvTransformRow1_0) ;
    float2 _S206 = float2(dot(_S204.xy, _S31.texCoord_0) + _S204.z, dot(_S205.xy, _S31.texCoord_0) + _S205.z);
    bool hasSceneLighting_0;
    float4 _S207;
    if((textureMask_0 & 2U) != 0U)
    {
        thread float4 diffuseSample_0;
        bool _S208 = (udimMask_0 & 2U) != 0U;
        for(;;)
        {
            if(!_S208)
            {
                _S207 = (((&kernelContext_2)->baseColorTexture_0).sample(((&kernelContext_2)->baseColorSampler_0), (_S206)));
                break;
            }
            texture2d<float, access::sample> _S209 = (&kernelContext_2)->baseColorTexture_0;
            thread uint atlasWidth_0;
            thread uint atlasHeight_0;
            (*((&atlasWidth_0)) = (_S209).get_width(0)),(*((&atlasHeight_0)) = (_S209).get_height(0));
            int3 _S210 = int3(int(0), int(0), int(0));
            float4 metadata_0 = round((((&kernelContext_2)->baseColorTexture_0).read(vec<uint,2>(((_S210)).xy), uint(((_S210)).z))) * float4(255.0f) );
            if((metadata_0.x) == 255.0f)
            {
                uint _S211 = atlasWidth_0;
                uint _S212 = 2U % atlasWidth_0;
                int _S213 = int(_S212);
                uint _S214 = 2U / _S211;
                int3 _S215 = int3(_S213, int(_S214), int(0));
                int _S216 = int(uint(round((((&kernelContext_2)->baseColorTexture_0).read(vec<uint,2>(((_S215)).xy), uint(((_S215)).z))).x * 255.0f)));
                uint _S217 = atlasWidth_0;
                uint _S218 = 3U % atlasWidth_0;
                int _S219 = int(_S218);
                uint _S220 = 3U / _S217;
                int3 _S221 = int3(_S219, int(_S220), int(0));
                int2 minimumTile_0 = int2(_S216, int(uint(round((((&kernelContext_2)->baseColorTexture_0).read(vec<uint,2>(((_S221)).xy), uint(((_S221)).z))).x * 255.0f))));
                uint _S222 = atlasWidth_0;
                uint _S223 = 4U % atlasWidth_0;
                int _S224 = int(_S223);
                uint _S225 = 4U / _S222;
                int3 _S226 = int3(_S224, int(_S225), int(0));
                uint _S227 = uint(round((((&kernelContext_2)->baseColorTexture_0).read(vec<uint,2>(((_S226)).xy), uint(((_S226)).z))).x * 255.0f));
                uint _S228 = atlasWidth_0;
                uint _S229 = 5U % atlasWidth_0;
                int _S230 = int(_S229);
                uint _S231 = 5U / _S228;
                int3 _S232 = int3(_S230, int(_S231), int(0));
                uint _S233 = uint(round((((&kernelContext_2)->baseColorTexture_0).read(vec<uint,2>(((_S232)).xy), uint(((_S232)).z))).x * 255.0f));
                uint2 grid_0 = uint2(_S227, _S233);
                int2 tile_0 = int2(floor(_S206)) - minimumTile_0;
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
                    int3 _S234 = int3(int(1), int(0), int(0));
                    _S207 = (((&kernelContext_2)->baseColorTexture_0).read(vec<uint,2>(((_S234)).xy), uint(((_S234)).z)));
                    break;
                }
                uint offset_0 = 10U + 2U * (uint(tile_0.y) * _S227 + uint(tile_0.x));
                uint _S235 = atlasWidth_0;
                uint _S236 = offset_0 % atlasWidth_0;
                int _S237 = int(_S236);
                uint _S238 = offset_0 / _S235;
                int3 _S239 = int3(_S237, int(_S238), int(0));
                uint _S240 = uint(round((((&kernelContext_2)->baseColorTexture_0).read(vec<uint,2>(((_S239)).xy), uint(((_S239)).z))).x * 255.0f));
                uint _S241 = atlasWidth_0;
                uint _S242 = offset_0 + 1U;
                uint _S243 = _S242 % atlasWidth_0;
                int _S244 = int(_S243);
                uint _S245 = _S242 / _S241;
                int3 _S246 = int3(_S244, int(_S245), int(0));
                uint slot_0 = _S240 | (uint(round((((&kernelContext_2)->baseColorTexture_0).read(vec<uint,2>(((_S246)).xy), uint(((_S246)).z))).x * 255.0f)) << 8U);
                if(slot_0 == 0U)
                {
                    int3 _S247 = int3(int(1), int(0), int(0));
                    _S207 = (((&kernelContext_2)->baseColorTexture_0).read(vec<uint,2>(((_S247)).xy), uint(((_S247)).z)));
                    break;
                }
                uint slot_1 = slot_0 - 1U;
                uint _S248 = atlasWidth_0;
                uint _S249 = 6U % atlasWidth_0;
                int _S250 = int(_S249);
                uint _S251 = 6U / _S248;
                int3 _S252 = int3(_S250, int(_S251), int(0));
                uint _S253 = uint(round((((&kernelContext_2)->baseColorTexture_0).read(vec<uint,2>(((_S252)).xy), uint(((_S252)).z))).x * 255.0f));
                uint _S254 = atlasWidth_0;
                uint _S255 = 7U % atlasWidth_0;
                int _S256 = int(_S255);
                uint _S257 = 7U / _S254;
                int3 _S258 = int3(_S256, int(_S257), int(0));
                uint columns_0 = _S253 | (uint(round((((&kernelContext_2)->baseColorTexture_0).read(vec<uint,2>(((_S258)).xy), uint(((_S258)).z))).x * 255.0f)) << 8U);
                uint _S259 = atlasWidth_0;
                uint _S260 = 8U % atlasWidth_0;
                int _S261 = int(_S260);
                uint _S262 = 8U / _S259;
                int3 _S263 = int3(_S261, int(_S262), int(0));
                uint _S264 = uint(round((((&kernelContext_2)->baseColorTexture_0).read(vec<uint,2>(((_S263)).xy), uint(((_S263)).z))).x * 255.0f));
                uint _S265 = atlasWidth_0;
                uint _S266 = 9U % atlasWidth_0;
                int _S267 = int(_S266);
                uint _S268 = 9U / _S265;
                int3 _S269 = int3(_S267, int(_S268), int(0));
                uint rows_0 = ((_S264 | (uint(round((((&kernelContext_2)->baseColorTexture_0).read(vec<uint,2>(((_S269)).xy), uint(((_S269)).z))).x * 255.0f)) << 8U)) + columns_0 - 1U) / columns_0;
                uint metadataRows_0 = (10U + 2U * _S227 * _S233 + atlasWidth_0 - 1U) / atlasWidth_0;
                uint _S270 = atlasWidth_0 / columns_0;
                float _S271 = float(_S270);
                uint _S272 = (atlasHeight_0 - metadataRows_0) / rows_0;
                float2 cellSize_0 = float2(_S271, float(_S272));
                uint _S273 = slot_1 % columns_0;
                float _S274 = float(_S273);
                uint _S275 = slot_1 / columns_0;
                _S207 = (((&kernelContext_2)->baseColorTexture_0).sample(((&kernelContext_2)->baseColorSampler_0), ((float2(_S274, float(_S275)) * cellSize_0 + float2(1.5f, float(metadataRows_0) + 1.5f) + fract(_S206) * max(cellSize_0 - float2(3.0f) , float2(0.0f) )) / float2(float(atlasWidth_0), float(atlasHeight_0)))));
                break;
            }
            int2 _S276 = int2(metadata_0.zw);
            int2 tile_1 = int2(floor(_S206)) - int2(metadata_0.xy);
            if(any(tile_1 < (int2(int(0)) )))
            {
                hasSceneLighting_0 = true;
            }
            else
            {
                hasSceneLighting_0 = any(tile_1 >= _S276);
            }
            if(hasSceneLighting_0)
            {
                int3 _S277 = int3(int(min(1U, atlasWidth_0 - 1U)), int(0), int(0));
                _S207 = (((&kernelContext_2)->baseColorTexture_0).read(vec<uint,2>(((_S277)).xy), uint(((_S277)).z)));
                break;
            }
            uint _S278 = atlasWidth_0 / uint(_S276.x);
            float _S279 = float(_S278);
            uint _S280 = (atlasHeight_0 - 1U) / uint(_S276.y);
            float2 cellSize_1 = float2(_S279, float(_S280));
            _S207 = (((&kernelContext_2)->baseColorTexture_0).sample(((&kernelContext_2)->baseColorSampler_0), ((float2(tile_1) * cellSize_1 + float2(1.5f, 2.5f) + fract(_S206) * max(cellSize_1 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_0), float(atlasHeight_0)))));
            break;
        }
        diffuseSample_0 = _S207;
        float4 _S281 = float4(_S36->compositeControls_0) ;
        float _S282 = _S281.x;
        if(_S282 == 0.0f)
        {
            hasSceneLighting_0 = (_S281.w) == 2.0f;
        }
        else
        {
            hasSceneLighting_0 = false;
        }
        if(hasSceneLighting_0)
        {
            texture2d<float, access::sample> _S283 = (&kernelContext_2)->baseColorTexture_0;
            thread uint sourceWidth_0;
            thread uint sourceHeight_0;
            (*((&sourceWidth_0)) = (_S283).get_width(0)),(*((&sourceHeight_0)) = (_S283).get_height(0));
            texture2d<float, access::sample> _S284 = (&kernelContext_2)->diffuseValuesTexture_0;
            thread uint valueWidth_0;
            thread uint valueHeight_0;
            (*((&valueWidth_0)) = (_S284).get_width(0)),(*((&valueHeight_0)) = (_S284).get_height(0));
            if(valueWidth_0 != sourceWidth_0)
            {
                float3 _S285 = DecodeIndexedRgb_0(diffuseSample_0, (&kernelContext_2)->diffuseValuesTexture_0);
                diffuseSample_0.xyz = _S285;
            }
            else
            {
                diffuseSample_0.z = (((&kernelContext_2)->diffuseValuesTexture_0).sample(((&kernelContext_2)->baseColorSampler_0), (_S206))).x;
            }
        }
        float4 _S286 = diffuseSample_0;
        for(;;)
        {
            if(_S282 != 2.0f)
            {
                _S207 = _S286;
                break;
            }
            bool _S287 = (_S281.w) >= 0.5f;
            for(;;)
            {
                if(!_S287)
                {
                    _S207 = (((&kernelContext_2)->compositeTexture_0).sample(((&kernelContext_2)->compositeSampler_0), (_S206)));
                    break;
                }
                texture2d<float, access::sample> _S288 = (&kernelContext_2)->compositeTexture_0;
                thread uint atlasWidth_1;
                thread uint atlasHeight_1;
                (*((&atlasWidth_1)) = (_S288).get_width(0)),(*((&atlasHeight_1)) = (_S288).get_height(0));
                int3 _S289 = int3(int(0), int(0), int(0));
                float4 metadata_1 = round((((&kernelContext_2)->compositeTexture_0).read(vec<uint,2>(((_S289)).xy), uint(((_S289)).z))) * float4(255.0f) );
                if((metadata_1.x) == 255.0f)
                {
                    uint _S290 = atlasWidth_1;
                    uint _S291 = 2U % atlasWidth_1;
                    int _S292 = int(_S291);
                    uint _S293 = 2U / _S290;
                    int3 _S294 = int3(_S292, int(_S293), int(0));
                    int _S295 = int(uint(round((((&kernelContext_2)->compositeTexture_0).read(vec<uint,2>(((_S294)).xy), uint(((_S294)).z))).x * 255.0f)));
                    uint _S296 = atlasWidth_1;
                    uint _S297 = 3U % atlasWidth_1;
                    int _S298 = int(_S297);
                    uint _S299 = 3U / _S296;
                    int3 _S300 = int3(_S298, int(_S299), int(0));
                    int2 minimumTile_1 = int2(_S295, int(uint(round((((&kernelContext_2)->compositeTexture_0).read(vec<uint,2>(((_S300)).xy), uint(((_S300)).z))).x * 255.0f))));
                    uint _S301 = atlasWidth_1;
                    uint _S302 = 4U % atlasWidth_1;
                    int _S303 = int(_S302);
                    uint _S304 = 4U / _S301;
                    int3 _S305 = int3(_S303, int(_S304), int(0));
                    uint _S306 = uint(round((((&kernelContext_2)->compositeTexture_0).read(vec<uint,2>(((_S305)).xy), uint(((_S305)).z))).x * 255.0f));
                    uint _S307 = atlasWidth_1;
                    uint _S308 = 5U % atlasWidth_1;
                    int _S309 = int(_S308);
                    uint _S310 = 5U / _S307;
                    int3 _S311 = int3(_S309, int(_S310), int(0));
                    uint _S312 = uint(round((((&kernelContext_2)->compositeTexture_0).read(vec<uint,2>(((_S311)).xy), uint(((_S311)).z))).x * 255.0f));
                    uint2 grid_1 = uint2(_S306, _S312);
                    int2 tile_2 = int2(floor(_S206)) - minimumTile_1;
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
                        int3 _S313 = int3(int(1), int(0), int(0));
                        _S207 = (((&kernelContext_2)->compositeTexture_0).read(vec<uint,2>(((_S313)).xy), uint(((_S313)).z)));
                        break;
                    }
                    uint offset_1 = 10U + 2U * (uint(tile_2.y) * _S306 + uint(tile_2.x));
                    uint _S314 = atlasWidth_1;
                    uint _S315 = offset_1 % atlasWidth_1;
                    int _S316 = int(_S315);
                    uint _S317 = offset_1 / _S314;
                    int3 _S318 = int3(_S316, int(_S317), int(0));
                    uint _S319 = uint(round((((&kernelContext_2)->compositeTexture_0).read(vec<uint,2>(((_S318)).xy), uint(((_S318)).z))).x * 255.0f));
                    uint _S320 = atlasWidth_1;
                    uint _S321 = offset_1 + 1U;
                    uint _S322 = _S321 % atlasWidth_1;
                    int _S323 = int(_S322);
                    uint _S324 = _S321 / _S320;
                    int3 _S325 = int3(_S323, int(_S324), int(0));
                    uint slot_2 = _S319 | (uint(round((((&kernelContext_2)->compositeTexture_0).read(vec<uint,2>(((_S325)).xy), uint(((_S325)).z))).x * 255.0f)) << 8U);
                    if(slot_2 == 0U)
                    {
                        int3 _S326 = int3(int(1), int(0), int(0));
                        _S207 = (((&kernelContext_2)->compositeTexture_0).read(vec<uint,2>(((_S326)).xy), uint(((_S326)).z)));
                        break;
                    }
                    uint slot_3 = slot_2 - 1U;
                    uint _S327 = atlasWidth_1;
                    uint _S328 = 6U % atlasWidth_1;
                    int _S329 = int(_S328);
                    uint _S330 = 6U / _S327;
                    int3 _S331 = int3(_S329, int(_S330), int(0));
                    uint _S332 = uint(round((((&kernelContext_2)->compositeTexture_0).read(vec<uint,2>(((_S331)).xy), uint(((_S331)).z))).x * 255.0f));
                    uint _S333 = atlasWidth_1;
                    uint _S334 = 7U % atlasWidth_1;
                    int _S335 = int(_S334);
                    uint _S336 = 7U / _S333;
                    int3 _S337 = int3(_S335, int(_S336), int(0));
                    uint columns_1 = _S332 | (uint(round((((&kernelContext_2)->compositeTexture_0).read(vec<uint,2>(((_S337)).xy), uint(((_S337)).z))).x * 255.0f)) << 8U);
                    uint _S338 = atlasWidth_1;
                    uint _S339 = 8U % atlasWidth_1;
                    int _S340 = int(_S339);
                    uint _S341 = 8U / _S338;
                    int3 _S342 = int3(_S340, int(_S341), int(0));
                    uint _S343 = uint(round((((&kernelContext_2)->compositeTexture_0).read(vec<uint,2>(((_S342)).xy), uint(((_S342)).z))).x * 255.0f));
                    uint _S344 = atlasWidth_1;
                    uint _S345 = 9U % atlasWidth_1;
                    int _S346 = int(_S345);
                    uint _S347 = 9U / _S344;
                    int3 _S348 = int3(_S346, int(_S347), int(0));
                    uint rows_1 = ((_S343 | (uint(round((((&kernelContext_2)->compositeTexture_0).read(vec<uint,2>(((_S348)).xy), uint(((_S348)).z))).x * 255.0f)) << 8U)) + columns_1 - 1U) / columns_1;
                    uint metadataRows_1 = (10U + 2U * _S306 * _S312 + atlasWidth_1 - 1U) / atlasWidth_1;
                    uint _S349 = atlasWidth_1 / columns_1;
                    float _S350 = float(_S349);
                    uint _S351 = (atlasHeight_1 - metadataRows_1) / rows_1;
                    float2 cellSize_2 = float2(_S350, float(_S351));
                    uint _S352 = slot_3 % columns_1;
                    float _S353 = float(_S352);
                    uint _S354 = slot_3 / columns_1;
                    _S207 = (((&kernelContext_2)->compositeTexture_0).sample(((&kernelContext_2)->compositeSampler_0), ((float2(_S353, float(_S354)) * cellSize_2 + float2(1.5f, float(metadataRows_1) + 1.5f) + fract(_S206) * max(cellSize_2 - float2(3.0f) , float2(0.0f) )) / float2(float(atlasWidth_1), float(atlasHeight_1)))));
                    break;
                }
                int2 _S355 = int2(metadata_1.zw);
                int2 tile_3 = int2(floor(_S206)) - int2(metadata_1.xy);
                if(any(tile_3 < (int2(int(0)) )))
                {
                    hasSceneLighting_0 = true;
                }
                else
                {
                    hasSceneLighting_0 = any(tile_3 >= _S355);
                }
                if(hasSceneLighting_0)
                {
                    int3 _S356 = int3(int(min(1U, atlasWidth_1 - 1U)), int(0), int(0));
                    _S207 = (((&kernelContext_2)->compositeTexture_0).read(vec<uint,2>(((_S356)).xy), uint(((_S356)).z)));
                    break;
                }
                uint _S357 = atlasWidth_1 / uint(_S355.x);
                float _S358 = float(_S357);
                uint _S359 = (atlasHeight_1 - 1U) / uint(_S355.y);
                float2 cellSize_3 = float2(_S358, float(_S359));
                _S207 = (((&kernelContext_2)->compositeTexture_0).sample(((&kernelContext_2)->compositeSampler_0), ((float2(tile_3) * cellSize_3 + float2(1.5f, 2.5f) + fract(_S206) * max(cellSize_3 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_1), float(atlasHeight_1)))));
                break;
            }
            uint operation_0 = uint(round(_S281.y));
            if(operation_0 == 1U)
            {
                _S207 = _S286 * _S207;
                break;
            }
            if(operation_0 == 2U)
            {
                _S207 = _S286 + _S207;
                break;
            }
            if(operation_0 == 3U)
            {
                _S207 = _S286 - _S207;
                break;
            }
            if(operation_0 == 4U)
            {
                float factor_0 = _S281.z;
                _S207 = _S286 * float4((1.0f - factor_0))  + _S207 * float4(factor_0) ;
                break;
            }
            _S207 = _S286;
            break;
        }
        diffuseColor_0 = _S207.xyz;
    }
    float roughness_0;
    float4 _S360;
    if((textureMask_0 & 8U) != 0U)
    {
        bool _S361 = (udimMask_0 & 8U) != 0U;
        for(;;)
        {
            if(!_S361)
            {
                _S207 = (((&kernelContext_2)->roughnessMetallicTexture_0).sample(((&kernelContext_2)->roughnessMetallicSampler_0), (_S206)));
                break;
            }
            texture2d<float, access::sample> _S362 = (&kernelContext_2)->roughnessMetallicTexture_0;
            thread uint atlasWidth_2;
            thread uint atlasHeight_2;
            (*((&atlasWidth_2)) = (_S362).get_width(0)),(*((&atlasHeight_2)) = (_S362).get_height(0));
            int3 _S363 = int3(int(0), int(0), int(0));
            float4 metadata_2 = round((((&kernelContext_2)->roughnessMetallicTexture_0).read(vec<uint,2>(((_S363)).xy), uint(((_S363)).z))) * float4(255.0f) );
            if((metadata_2.x) == 255.0f)
            {
                uint _S364 = atlasWidth_2;
                uint _S365 = 2U % atlasWidth_2;
                int _S366 = int(_S365);
                uint _S367 = 2U / _S364;
                int3 _S368 = int3(_S366, int(_S367), int(0));
                int _S369 = int(uint(round((((&kernelContext_2)->roughnessMetallicTexture_0).read(vec<uint,2>(((_S368)).xy), uint(((_S368)).z))).x * 255.0f)));
                uint _S370 = atlasWidth_2;
                uint _S371 = 3U % atlasWidth_2;
                int _S372 = int(_S371);
                uint _S373 = 3U / _S370;
                int3 _S374 = int3(_S372, int(_S373), int(0));
                int2 minimumTile_2 = int2(_S369, int(uint(round((((&kernelContext_2)->roughnessMetallicTexture_0).read(vec<uint,2>(((_S374)).xy), uint(((_S374)).z))).x * 255.0f))));
                uint _S375 = atlasWidth_2;
                uint _S376 = 4U % atlasWidth_2;
                int _S377 = int(_S376);
                uint _S378 = 4U / _S375;
                int3 _S379 = int3(_S377, int(_S378), int(0));
                uint _S380 = uint(round((((&kernelContext_2)->roughnessMetallicTexture_0).read(vec<uint,2>(((_S379)).xy), uint(((_S379)).z))).x * 255.0f));
                uint _S381 = atlasWidth_2;
                uint _S382 = 5U % atlasWidth_2;
                int _S383 = int(_S382);
                uint _S384 = 5U / _S381;
                int3 _S385 = int3(_S383, int(_S384), int(0));
                uint _S386 = uint(round((((&kernelContext_2)->roughnessMetallicTexture_0).read(vec<uint,2>(((_S385)).xy), uint(((_S385)).z))).x * 255.0f));
                uint2 grid_2 = uint2(_S380, _S386);
                int2 tile_4 = int2(floor(_S206)) - minimumTile_2;
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
                    int3 _S387 = int3(int(1), int(0), int(0));
                    _S207 = (((&kernelContext_2)->roughnessMetallicTexture_0).read(vec<uint,2>(((_S387)).xy), uint(((_S387)).z)));
                    break;
                }
                uint offset_2 = 10U + 2U * (uint(tile_4.y) * _S380 + uint(tile_4.x));
                uint _S388 = atlasWidth_2;
                uint _S389 = offset_2 % atlasWidth_2;
                int _S390 = int(_S389);
                uint _S391 = offset_2 / _S388;
                int3 _S392 = int3(_S390, int(_S391), int(0));
                uint _S393 = uint(round((((&kernelContext_2)->roughnessMetallicTexture_0).read(vec<uint,2>(((_S392)).xy), uint(((_S392)).z))).x * 255.0f));
                uint _S394 = atlasWidth_2;
                uint _S395 = offset_2 + 1U;
                uint _S396 = _S395 % atlasWidth_2;
                int _S397 = int(_S396);
                uint _S398 = _S395 / _S394;
                int3 _S399 = int3(_S397, int(_S398), int(0));
                uint slot_4 = _S393 | (uint(round((((&kernelContext_2)->roughnessMetallicTexture_0).read(vec<uint,2>(((_S399)).xy), uint(((_S399)).z))).x * 255.0f)) << 8U);
                if(slot_4 == 0U)
                {
                    int3 _S400 = int3(int(1), int(0), int(0));
                    _S207 = (((&kernelContext_2)->roughnessMetallicTexture_0).read(vec<uint,2>(((_S400)).xy), uint(((_S400)).z)));
                    break;
                }
                uint slot_5 = slot_4 - 1U;
                uint _S401 = atlasWidth_2;
                uint _S402 = 6U % atlasWidth_2;
                int _S403 = int(_S402);
                uint _S404 = 6U / _S401;
                int3 _S405 = int3(_S403, int(_S404), int(0));
                uint _S406 = uint(round((((&kernelContext_2)->roughnessMetallicTexture_0).read(vec<uint,2>(((_S405)).xy), uint(((_S405)).z))).x * 255.0f));
                uint _S407 = atlasWidth_2;
                uint _S408 = 7U % atlasWidth_2;
                int _S409 = int(_S408);
                uint _S410 = 7U / _S407;
                int3 _S411 = int3(_S409, int(_S410), int(0));
                uint columns_2 = _S406 | (uint(round((((&kernelContext_2)->roughnessMetallicTexture_0).read(vec<uint,2>(((_S411)).xy), uint(((_S411)).z))).x * 255.0f)) << 8U);
                uint _S412 = atlasWidth_2;
                uint _S413 = 8U % atlasWidth_2;
                int _S414 = int(_S413);
                uint _S415 = 8U / _S412;
                int3 _S416 = int3(_S414, int(_S415), int(0));
                uint _S417 = uint(round((((&kernelContext_2)->roughnessMetallicTexture_0).read(vec<uint,2>(((_S416)).xy), uint(((_S416)).z))).x * 255.0f));
                uint _S418 = atlasWidth_2;
                uint _S419 = 9U % atlasWidth_2;
                int _S420 = int(_S419);
                uint _S421 = 9U / _S418;
                int3 _S422 = int3(_S420, int(_S421), int(0));
                uint rows_2 = ((_S417 | (uint(round((((&kernelContext_2)->roughnessMetallicTexture_0).read(vec<uint,2>(((_S422)).xy), uint(((_S422)).z))).x * 255.0f)) << 8U)) + columns_2 - 1U) / columns_2;
                uint metadataRows_2 = (10U + 2U * _S380 * _S386 + atlasWidth_2 - 1U) / atlasWidth_2;
                uint _S423 = atlasWidth_2 / columns_2;
                float _S424 = float(_S423);
                uint _S425 = (atlasHeight_2 - metadataRows_2) / rows_2;
                float2 cellSize_4 = float2(_S424, float(_S425));
                uint _S426 = slot_5 % columns_2;
                float _S427 = float(_S426);
                uint _S428 = slot_5 / columns_2;
                _S207 = (((&kernelContext_2)->roughnessMetallicTexture_0).sample(((&kernelContext_2)->roughnessMetallicSampler_0), ((float2(_S427, float(_S428)) * cellSize_4 + float2(1.5f, float(metadataRows_2) + 1.5f) + fract(_S206) * max(cellSize_4 - float2(3.0f) , float2(0.0f) )) / float2(float(atlasWidth_2), float(atlasHeight_2)))));
                break;
            }
            int2 _S429 = int2(metadata_2.zw);
            int2 tile_5 = int2(floor(_S206)) - int2(metadata_2.xy);
            if(any(tile_5 < (int2(int(0)) )))
            {
                hasSceneLighting_0 = true;
            }
            else
            {
                hasSceneLighting_0 = any(tile_5 >= _S429);
            }
            if(hasSceneLighting_0)
            {
                int3 _S430 = int3(int(min(1U, atlasWidth_2 - 1U)), int(0), int(0));
                _S207 = (((&kernelContext_2)->roughnessMetallicTexture_0).read(vec<uint,2>(((_S430)).xy), uint(((_S430)).z)));
                break;
            }
            uint _S431 = atlasWidth_2 / uint(_S429.x);
            float _S432 = float(_S431);
            uint _S433 = (atlasHeight_2 - 1U) / uint(_S429.y);
            float2 cellSize_5 = float2(_S432, float(_S433));
            _S207 = (((&kernelContext_2)->roughnessMetallicTexture_0).sample(((&kernelContext_2)->roughnessMetallicSampler_0), ((float2(tile_5) * cellSize_5 + float2(1.5f, 2.5f) + fract(_S206) * max(cellSize_5 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_2), float(atlasHeight_2)))));
            break;
        }
        for(;;)
        {
            float4 _S434 = float4(_S36->compositeControls_0) ;
            if((_S434.x) != 8.0f)
            {
                break;
            }
            bool _S435 = (_S434.w) >= 0.5f;
            for(;;)
            {
                if(!_S435)
                {
                    _S360 = (((&kernelContext_2)->compositeTexture_0).sample(((&kernelContext_2)->compositeSampler_0), (_S206)));
                    break;
                }
                texture2d<float, access::sample> _S436 = (&kernelContext_2)->compositeTexture_0;
                thread uint atlasWidth_3;
                thread uint atlasHeight_3;
                (*((&atlasWidth_3)) = (_S436).get_width(0)),(*((&atlasHeight_3)) = (_S436).get_height(0));
                int3 _S437 = int3(int(0), int(0), int(0));
                float4 metadata_3 = round((((&kernelContext_2)->compositeTexture_0).read(vec<uint,2>(((_S437)).xy), uint(((_S437)).z))) * float4(255.0f) );
                if((metadata_3.x) == 255.0f)
                {
                    uint _S438 = atlasWidth_3;
                    uint _S439 = 2U % atlasWidth_3;
                    int _S440 = int(_S439);
                    uint _S441 = 2U / _S438;
                    int3 _S442 = int3(_S440, int(_S441), int(0));
                    int _S443 = int(uint(round((((&kernelContext_2)->compositeTexture_0).read(vec<uint,2>(((_S442)).xy), uint(((_S442)).z))).x * 255.0f)));
                    uint _S444 = atlasWidth_3;
                    uint _S445 = 3U % atlasWidth_3;
                    int _S446 = int(_S445);
                    uint _S447 = 3U / _S444;
                    int3 _S448 = int3(_S446, int(_S447), int(0));
                    int2 minimumTile_3 = int2(_S443, int(uint(round((((&kernelContext_2)->compositeTexture_0).read(vec<uint,2>(((_S448)).xy), uint(((_S448)).z))).x * 255.0f))));
                    uint _S449 = atlasWidth_3;
                    uint _S450 = 4U % atlasWidth_3;
                    int _S451 = int(_S450);
                    uint _S452 = 4U / _S449;
                    int3 _S453 = int3(_S451, int(_S452), int(0));
                    uint _S454 = uint(round((((&kernelContext_2)->compositeTexture_0).read(vec<uint,2>(((_S453)).xy), uint(((_S453)).z))).x * 255.0f));
                    uint _S455 = atlasWidth_3;
                    uint _S456 = 5U % atlasWidth_3;
                    int _S457 = int(_S456);
                    uint _S458 = 5U / _S455;
                    int3 _S459 = int3(_S457, int(_S458), int(0));
                    uint _S460 = uint(round((((&kernelContext_2)->compositeTexture_0).read(vec<uint,2>(((_S459)).xy), uint(((_S459)).z))).x * 255.0f));
                    uint2 grid_3 = uint2(_S454, _S460);
                    int2 tile_6 = int2(floor(_S206)) - minimumTile_3;
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
                        int3 _S461 = int3(int(1), int(0), int(0));
                        _S360 = (((&kernelContext_2)->compositeTexture_0).read(vec<uint,2>(((_S461)).xy), uint(((_S461)).z)));
                        break;
                    }
                    uint offset_3 = 10U + 2U * (uint(tile_6.y) * _S454 + uint(tile_6.x));
                    uint _S462 = atlasWidth_3;
                    uint _S463 = offset_3 % atlasWidth_3;
                    int _S464 = int(_S463);
                    uint _S465 = offset_3 / _S462;
                    int3 _S466 = int3(_S464, int(_S465), int(0));
                    uint _S467 = uint(round((((&kernelContext_2)->compositeTexture_0).read(vec<uint,2>(((_S466)).xy), uint(((_S466)).z))).x * 255.0f));
                    uint _S468 = atlasWidth_3;
                    uint _S469 = offset_3 + 1U;
                    uint _S470 = _S469 % atlasWidth_3;
                    int _S471 = int(_S470);
                    uint _S472 = _S469 / _S468;
                    int3 _S473 = int3(_S471, int(_S472), int(0));
                    uint slot_6 = _S467 | (uint(round((((&kernelContext_2)->compositeTexture_0).read(vec<uint,2>(((_S473)).xy), uint(((_S473)).z))).x * 255.0f)) << 8U);
                    if(slot_6 == 0U)
                    {
                        int3 _S474 = int3(int(1), int(0), int(0));
                        _S360 = (((&kernelContext_2)->compositeTexture_0).read(vec<uint,2>(((_S474)).xy), uint(((_S474)).z)));
                        break;
                    }
                    uint slot_7 = slot_6 - 1U;
                    uint _S475 = atlasWidth_3;
                    uint _S476 = 6U % atlasWidth_3;
                    int _S477 = int(_S476);
                    uint _S478 = 6U / _S475;
                    int3 _S479 = int3(_S477, int(_S478), int(0));
                    uint _S480 = uint(round((((&kernelContext_2)->compositeTexture_0).read(vec<uint,2>(((_S479)).xy), uint(((_S479)).z))).x * 255.0f));
                    uint _S481 = atlasWidth_3;
                    uint _S482 = 7U % atlasWidth_3;
                    int _S483 = int(_S482);
                    uint _S484 = 7U / _S481;
                    int3 _S485 = int3(_S483, int(_S484), int(0));
                    uint columns_3 = _S480 | (uint(round((((&kernelContext_2)->compositeTexture_0).read(vec<uint,2>(((_S485)).xy), uint(((_S485)).z))).x * 255.0f)) << 8U);
                    uint _S486 = atlasWidth_3;
                    uint _S487 = 8U % atlasWidth_3;
                    int _S488 = int(_S487);
                    uint _S489 = 8U / _S486;
                    int3 _S490 = int3(_S488, int(_S489), int(0));
                    uint _S491 = uint(round((((&kernelContext_2)->compositeTexture_0).read(vec<uint,2>(((_S490)).xy), uint(((_S490)).z))).x * 255.0f));
                    uint _S492 = atlasWidth_3;
                    uint _S493 = 9U % atlasWidth_3;
                    int _S494 = int(_S493);
                    uint _S495 = 9U / _S492;
                    int3 _S496 = int3(_S494, int(_S495), int(0));
                    uint rows_3 = ((_S491 | (uint(round((((&kernelContext_2)->compositeTexture_0).read(vec<uint,2>(((_S496)).xy), uint(((_S496)).z))).x * 255.0f)) << 8U)) + columns_3 - 1U) / columns_3;
                    uint metadataRows_3 = (10U + 2U * _S454 * _S460 + atlasWidth_3 - 1U) / atlasWidth_3;
                    uint _S497 = atlasWidth_3 / columns_3;
                    float _S498 = float(_S497);
                    uint _S499 = (atlasHeight_3 - metadataRows_3) / rows_3;
                    float2 cellSize_6 = float2(_S498, float(_S499));
                    uint _S500 = slot_7 % columns_3;
                    float _S501 = float(_S500);
                    uint _S502 = slot_7 / columns_3;
                    _S360 = (((&kernelContext_2)->compositeTexture_0).sample(((&kernelContext_2)->compositeSampler_0), ((float2(_S501, float(_S502)) * cellSize_6 + float2(1.5f, float(metadataRows_3) + 1.5f) + fract(_S206) * max(cellSize_6 - float2(3.0f) , float2(0.0f) )) / float2(float(atlasWidth_3), float(atlasHeight_3)))));
                    break;
                }
                int2 _S503 = int2(metadata_3.zw);
                int2 tile_7 = int2(floor(_S206)) - int2(metadata_3.xy);
                if(any(tile_7 < (int2(int(0)) )))
                {
                    hasSceneLighting_0 = true;
                }
                else
                {
                    hasSceneLighting_0 = any(tile_7 >= _S503);
                }
                if(hasSceneLighting_0)
                {
                    int3 _S504 = int3(int(min(1U, atlasWidth_3 - 1U)), int(0), int(0));
                    _S360 = (((&kernelContext_2)->compositeTexture_0).read(vec<uint,2>(((_S504)).xy), uint(((_S504)).z)));
                    break;
                }
                uint _S505 = atlasWidth_3 / uint(_S503.x);
                float _S506 = float(_S505);
                uint _S507 = (atlasHeight_3 - 1U) / uint(_S503.y);
                float2 cellSize_7 = float2(_S506, float(_S507));
                _S360 = (((&kernelContext_2)->compositeTexture_0).sample(((&kernelContext_2)->compositeSampler_0), ((float2(tile_7) * cellSize_7 + float2(1.5f, 2.5f) + fract(_S206) * max(cellSize_7 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_3), float(atlasHeight_3)))));
                break;
            }
            uint operation_1 = uint(round(_S434.y));
            if(operation_1 == 1U)
            {
                _S207 = _S207 * _S360;
                break;
            }
            if(operation_1 == 2U)
            {
                _S207 = _S207 + _S360;
                break;
            }
            if(operation_1 == 3U)
            {
                _S207 = _S207 - _S360;
                break;
            }
            if(operation_1 == 4U)
            {
                float factor_1 = _S434.z;
                _S207 = _S207 * float4((1.0f - factor_1))  + _S360 * float4(factor_1) ;
                break;
            }
            break;
        }
        roughness_0 = clamp(_S207.x, 0.00999999977648258f, 1.0f);
    }
    else
    {
        roughness_0 = _S197;
    }
    float metallic_0;
    if((textureMask_0 & 32U) != 0U)
    {
        bool _S508 = (udimMask_0 & 32U) != 0U;
        for(;;)
        {
            if(!_S508)
            {
                _S207 = (((&kernelContext_2)->metallicTexture_0).sample(((&kernelContext_2)->metallicSampler_0), (_S206)));
                break;
            }
            texture2d<float, access::sample> _S509 = (&kernelContext_2)->metallicTexture_0;
            thread uint atlasWidth_4;
            thread uint atlasHeight_4;
            (*((&atlasWidth_4)) = (_S509).get_width(0)),(*((&atlasHeight_4)) = (_S509).get_height(0));
            int3 _S510 = int3(int(0), int(0), int(0));
            float4 metadata_4 = round((((&kernelContext_2)->metallicTexture_0).read(vec<uint,2>(((_S510)).xy), uint(((_S510)).z))) * float4(255.0f) );
            if((metadata_4.x) == 255.0f)
            {
                uint _S511 = atlasWidth_4;
                uint _S512 = 2U % atlasWidth_4;
                int _S513 = int(_S512);
                uint _S514 = 2U / _S511;
                int3 _S515 = int3(_S513, int(_S514), int(0));
                int _S516 = int(uint(round((((&kernelContext_2)->metallicTexture_0).read(vec<uint,2>(((_S515)).xy), uint(((_S515)).z))).x * 255.0f)));
                uint _S517 = atlasWidth_4;
                uint _S518 = 3U % atlasWidth_4;
                int _S519 = int(_S518);
                uint _S520 = 3U / _S517;
                int3 _S521 = int3(_S519, int(_S520), int(0));
                int2 minimumTile_4 = int2(_S516, int(uint(round((((&kernelContext_2)->metallicTexture_0).read(vec<uint,2>(((_S521)).xy), uint(((_S521)).z))).x * 255.0f))));
                uint _S522 = atlasWidth_4;
                uint _S523 = 4U % atlasWidth_4;
                int _S524 = int(_S523);
                uint _S525 = 4U / _S522;
                int3 _S526 = int3(_S524, int(_S525), int(0));
                uint _S527 = uint(round((((&kernelContext_2)->metallicTexture_0).read(vec<uint,2>(((_S526)).xy), uint(((_S526)).z))).x * 255.0f));
                uint _S528 = atlasWidth_4;
                uint _S529 = 5U % atlasWidth_4;
                int _S530 = int(_S529);
                uint _S531 = 5U / _S528;
                int3 _S532 = int3(_S530, int(_S531), int(0));
                uint _S533 = uint(round((((&kernelContext_2)->metallicTexture_0).read(vec<uint,2>(((_S532)).xy), uint(((_S532)).z))).x * 255.0f));
                uint2 grid_4 = uint2(_S527, _S533);
                int2 tile_8 = int2(floor(_S206)) - minimumTile_4;
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
                    int3 _S534 = int3(int(1), int(0), int(0));
                    _S207 = (((&kernelContext_2)->metallicTexture_0).read(vec<uint,2>(((_S534)).xy), uint(((_S534)).z)));
                    break;
                }
                uint offset_4 = 10U + 2U * (uint(tile_8.y) * _S527 + uint(tile_8.x));
                uint _S535 = atlasWidth_4;
                uint _S536 = offset_4 % atlasWidth_4;
                int _S537 = int(_S536);
                uint _S538 = offset_4 / _S535;
                int3 _S539 = int3(_S537, int(_S538), int(0));
                uint _S540 = uint(round((((&kernelContext_2)->metallicTexture_0).read(vec<uint,2>(((_S539)).xy), uint(((_S539)).z))).x * 255.0f));
                uint _S541 = atlasWidth_4;
                uint _S542 = offset_4 + 1U;
                uint _S543 = _S542 % atlasWidth_4;
                int _S544 = int(_S543);
                uint _S545 = _S542 / _S541;
                int3 _S546 = int3(_S544, int(_S545), int(0));
                uint slot_8 = _S540 | (uint(round((((&kernelContext_2)->metallicTexture_0).read(vec<uint,2>(((_S546)).xy), uint(((_S546)).z))).x * 255.0f)) << 8U);
                if(slot_8 == 0U)
                {
                    int3 _S547 = int3(int(1), int(0), int(0));
                    _S207 = (((&kernelContext_2)->metallicTexture_0).read(vec<uint,2>(((_S547)).xy), uint(((_S547)).z)));
                    break;
                }
                uint slot_9 = slot_8 - 1U;
                uint _S548 = atlasWidth_4;
                uint _S549 = 6U % atlasWidth_4;
                int _S550 = int(_S549);
                uint _S551 = 6U / _S548;
                int3 _S552 = int3(_S550, int(_S551), int(0));
                uint _S553 = uint(round((((&kernelContext_2)->metallicTexture_0).read(vec<uint,2>(((_S552)).xy), uint(((_S552)).z))).x * 255.0f));
                uint _S554 = atlasWidth_4;
                uint _S555 = 7U % atlasWidth_4;
                int _S556 = int(_S555);
                uint _S557 = 7U / _S554;
                int3 _S558 = int3(_S556, int(_S557), int(0));
                uint columns_4 = _S553 | (uint(round((((&kernelContext_2)->metallicTexture_0).read(vec<uint,2>(((_S558)).xy), uint(((_S558)).z))).x * 255.0f)) << 8U);
                uint _S559 = atlasWidth_4;
                uint _S560 = 8U % atlasWidth_4;
                int _S561 = int(_S560);
                uint _S562 = 8U / _S559;
                int3 _S563 = int3(_S561, int(_S562), int(0));
                uint _S564 = uint(round((((&kernelContext_2)->metallicTexture_0).read(vec<uint,2>(((_S563)).xy), uint(((_S563)).z))).x * 255.0f));
                uint _S565 = atlasWidth_4;
                uint _S566 = 9U % atlasWidth_4;
                int _S567 = int(_S566);
                uint _S568 = 9U / _S565;
                int3 _S569 = int3(_S567, int(_S568), int(0));
                uint rows_4 = ((_S564 | (uint(round((((&kernelContext_2)->metallicTexture_0).read(vec<uint,2>(((_S569)).xy), uint(((_S569)).z))).x * 255.0f)) << 8U)) + columns_4 - 1U) / columns_4;
                uint metadataRows_4 = (10U + 2U * _S527 * _S533 + atlasWidth_4 - 1U) / atlasWidth_4;
                uint _S570 = atlasWidth_4 / columns_4;
                float _S571 = float(_S570);
                uint _S572 = (atlasHeight_4 - metadataRows_4) / rows_4;
                float2 cellSize_8 = float2(_S571, float(_S572));
                uint _S573 = slot_9 % columns_4;
                float _S574 = float(_S573);
                uint _S575 = slot_9 / columns_4;
                _S207 = (((&kernelContext_2)->metallicTexture_0).sample(((&kernelContext_2)->metallicSampler_0), ((float2(_S574, float(_S575)) * cellSize_8 + float2(1.5f, float(metadataRows_4) + 1.5f) + fract(_S206) * max(cellSize_8 - float2(3.0f) , float2(0.0f) )) / float2(float(atlasWidth_4), float(atlasHeight_4)))));
                break;
            }
            int2 _S576 = int2(metadata_4.zw);
            int2 tile_9 = int2(floor(_S206)) - int2(metadata_4.xy);
            if(any(tile_9 < (int2(int(0)) )))
            {
                hasSceneLighting_0 = true;
            }
            else
            {
                hasSceneLighting_0 = any(tile_9 >= _S576);
            }
            if(hasSceneLighting_0)
            {
                int3 _S577 = int3(int(min(1U, atlasWidth_4 - 1U)), int(0), int(0));
                _S207 = (((&kernelContext_2)->metallicTexture_0).read(vec<uint,2>(((_S577)).xy), uint(((_S577)).z)));
                break;
            }
            uint _S578 = atlasWidth_4 / uint(_S576.x);
            float _S579 = float(_S578);
            uint _S580 = (atlasHeight_4 - 1U) / uint(_S576.y);
            float2 cellSize_9 = float2(_S579, float(_S580));
            _S207 = (((&kernelContext_2)->metallicTexture_0).sample(((&kernelContext_2)->metallicSampler_0), ((float2(tile_9) * cellSize_9 + float2(1.5f, 2.5f) + fract(_S206) * max(cellSize_9 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_4), float(atlasHeight_4)))));
            break;
        }
        for(;;)
        {
            float4 _S581 = float4(_S36->compositeControls_0) ;
            if((_S581.x) != 32.0f)
            {
                break;
            }
            bool _S582 = (_S581.w) >= 0.5f;
            for(;;)
            {
                if(!_S582)
                {
                    _S360 = (((&kernelContext_2)->compositeTexture_0).sample(((&kernelContext_2)->compositeSampler_0), (_S206)));
                    break;
                }
                texture2d<float, access::sample> _S583 = (&kernelContext_2)->compositeTexture_0;
                thread uint atlasWidth_5;
                thread uint atlasHeight_5;
                (*((&atlasWidth_5)) = (_S583).get_width(0)),(*((&atlasHeight_5)) = (_S583).get_height(0));
                int3 _S584 = int3(int(0), int(0), int(0));
                float4 metadata_5 = round((((&kernelContext_2)->compositeTexture_0).read(vec<uint,2>(((_S584)).xy), uint(((_S584)).z))) * float4(255.0f) );
                if((metadata_5.x) == 255.0f)
                {
                    uint _S585 = atlasWidth_5;
                    uint _S586 = 2U % atlasWidth_5;
                    int _S587 = int(_S586);
                    uint _S588 = 2U / _S585;
                    int3 _S589 = int3(_S587, int(_S588), int(0));
                    int _S590 = int(uint(round((((&kernelContext_2)->compositeTexture_0).read(vec<uint,2>(((_S589)).xy), uint(((_S589)).z))).x * 255.0f)));
                    uint _S591 = atlasWidth_5;
                    uint _S592 = 3U % atlasWidth_5;
                    int _S593 = int(_S592);
                    uint _S594 = 3U / _S591;
                    int3 _S595 = int3(_S593, int(_S594), int(0));
                    int2 minimumTile_5 = int2(_S590, int(uint(round((((&kernelContext_2)->compositeTexture_0).read(vec<uint,2>(((_S595)).xy), uint(((_S595)).z))).x * 255.0f))));
                    uint _S596 = atlasWidth_5;
                    uint _S597 = 4U % atlasWidth_5;
                    int _S598 = int(_S597);
                    uint _S599 = 4U / _S596;
                    int3 _S600 = int3(_S598, int(_S599), int(0));
                    uint _S601 = uint(round((((&kernelContext_2)->compositeTexture_0).read(vec<uint,2>(((_S600)).xy), uint(((_S600)).z))).x * 255.0f));
                    uint _S602 = atlasWidth_5;
                    uint _S603 = 5U % atlasWidth_5;
                    int _S604 = int(_S603);
                    uint _S605 = 5U / _S602;
                    int3 _S606 = int3(_S604, int(_S605), int(0));
                    uint _S607 = uint(round((((&kernelContext_2)->compositeTexture_0).read(vec<uint,2>(((_S606)).xy), uint(((_S606)).z))).x * 255.0f));
                    uint2 grid_5 = uint2(_S601, _S607);
                    int2 tile_10 = int2(floor(_S206)) - minimumTile_5;
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
                        int3 _S608 = int3(int(1), int(0), int(0));
                        _S360 = (((&kernelContext_2)->compositeTexture_0).read(vec<uint,2>(((_S608)).xy), uint(((_S608)).z)));
                        break;
                    }
                    uint offset_5 = 10U + 2U * (uint(tile_10.y) * _S601 + uint(tile_10.x));
                    uint _S609 = atlasWidth_5;
                    uint _S610 = offset_5 % atlasWidth_5;
                    int _S611 = int(_S610);
                    uint _S612 = offset_5 / _S609;
                    int3 _S613 = int3(_S611, int(_S612), int(0));
                    uint _S614 = uint(round((((&kernelContext_2)->compositeTexture_0).read(vec<uint,2>(((_S613)).xy), uint(((_S613)).z))).x * 255.0f));
                    uint _S615 = atlasWidth_5;
                    uint _S616 = offset_5 + 1U;
                    uint _S617 = _S616 % atlasWidth_5;
                    int _S618 = int(_S617);
                    uint _S619 = _S616 / _S615;
                    int3 _S620 = int3(_S618, int(_S619), int(0));
                    uint slot_10 = _S614 | (uint(round((((&kernelContext_2)->compositeTexture_0).read(vec<uint,2>(((_S620)).xy), uint(((_S620)).z))).x * 255.0f)) << 8U);
                    if(slot_10 == 0U)
                    {
                        int3 _S621 = int3(int(1), int(0), int(0));
                        _S360 = (((&kernelContext_2)->compositeTexture_0).read(vec<uint,2>(((_S621)).xy), uint(((_S621)).z)));
                        break;
                    }
                    uint slot_11 = slot_10 - 1U;
                    uint _S622 = atlasWidth_5;
                    uint _S623 = 6U % atlasWidth_5;
                    int _S624 = int(_S623);
                    uint _S625 = 6U / _S622;
                    int3 _S626 = int3(_S624, int(_S625), int(0));
                    uint _S627 = uint(round((((&kernelContext_2)->compositeTexture_0).read(vec<uint,2>(((_S626)).xy), uint(((_S626)).z))).x * 255.0f));
                    uint _S628 = atlasWidth_5;
                    uint _S629 = 7U % atlasWidth_5;
                    int _S630 = int(_S629);
                    uint _S631 = 7U / _S628;
                    int3 _S632 = int3(_S630, int(_S631), int(0));
                    uint columns_5 = _S627 | (uint(round((((&kernelContext_2)->compositeTexture_0).read(vec<uint,2>(((_S632)).xy), uint(((_S632)).z))).x * 255.0f)) << 8U);
                    uint _S633 = atlasWidth_5;
                    uint _S634 = 8U % atlasWidth_5;
                    int _S635 = int(_S634);
                    uint _S636 = 8U / _S633;
                    int3 _S637 = int3(_S635, int(_S636), int(0));
                    uint _S638 = uint(round((((&kernelContext_2)->compositeTexture_0).read(vec<uint,2>(((_S637)).xy), uint(((_S637)).z))).x * 255.0f));
                    uint _S639 = atlasWidth_5;
                    uint _S640 = 9U % atlasWidth_5;
                    int _S641 = int(_S640);
                    uint _S642 = 9U / _S639;
                    int3 _S643 = int3(_S641, int(_S642), int(0));
                    uint rows_5 = ((_S638 | (uint(round((((&kernelContext_2)->compositeTexture_0).read(vec<uint,2>(((_S643)).xy), uint(((_S643)).z))).x * 255.0f)) << 8U)) + columns_5 - 1U) / columns_5;
                    uint metadataRows_5 = (10U + 2U * _S601 * _S607 + atlasWidth_5 - 1U) / atlasWidth_5;
                    uint _S644 = atlasWidth_5 / columns_5;
                    float _S645 = float(_S644);
                    uint _S646 = (atlasHeight_5 - metadataRows_5) / rows_5;
                    float2 cellSize_10 = float2(_S645, float(_S646));
                    uint _S647 = slot_11 % columns_5;
                    float _S648 = float(_S647);
                    uint _S649 = slot_11 / columns_5;
                    _S360 = (((&kernelContext_2)->compositeTexture_0).sample(((&kernelContext_2)->compositeSampler_0), ((float2(_S648, float(_S649)) * cellSize_10 + float2(1.5f, float(metadataRows_5) + 1.5f) + fract(_S206) * max(cellSize_10 - float2(3.0f) , float2(0.0f) )) / float2(float(atlasWidth_5), float(atlasHeight_5)))));
                    break;
                }
                int2 _S650 = int2(metadata_5.zw);
                int2 tile_11 = int2(floor(_S206)) - int2(metadata_5.xy);
                if(any(tile_11 < (int2(int(0)) )))
                {
                    hasSceneLighting_0 = true;
                }
                else
                {
                    hasSceneLighting_0 = any(tile_11 >= _S650);
                }
                if(hasSceneLighting_0)
                {
                    int3 _S651 = int3(int(min(1U, atlasWidth_5 - 1U)), int(0), int(0));
                    _S360 = (((&kernelContext_2)->compositeTexture_0).read(vec<uint,2>(((_S651)).xy), uint(((_S651)).z)));
                    break;
                }
                uint _S652 = atlasWidth_5 / uint(_S650.x);
                float _S653 = float(_S652);
                uint _S654 = (atlasHeight_5 - 1U) / uint(_S650.y);
                float2 cellSize_11 = float2(_S653, float(_S654));
                _S360 = (((&kernelContext_2)->compositeTexture_0).sample(((&kernelContext_2)->compositeSampler_0), ((float2(tile_11) * cellSize_11 + float2(1.5f, 2.5f) + fract(_S206) * max(cellSize_11 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_5), float(atlasHeight_5)))));
                break;
            }
            uint operation_2 = uint(round(_S581.y));
            if(operation_2 == 1U)
            {
                _S207 = _S207 * _S360;
                break;
            }
            if(operation_2 == 2U)
            {
                _S207 = _S207 + _S360;
                break;
            }
            if(operation_2 == 3U)
            {
                _S207 = _S207 - _S360;
                break;
            }
            if(operation_2 == 4U)
            {
                float factor_2 = _S581.z;
                _S207 = _S207 * float4((1.0f - factor_2))  + _S360 * float4(factor_2) ;
                break;
            }
            break;
        }
        metallic_0 = saturate(_S207.x);
    }
    else
    {
        metallic_0 = _S196;
    }
    if((textureMask_0 & 16U) != 0U)
    {
        thread float4 emissionSample_0;
        bool _S655 = (udimMask_0 & 16U) != 0U;
        for(;;)
        {
            if(!_S655)
            {
                _S207 = (((&kernelContext_2)->emissiveTexture_0).sample(((&kernelContext_2)->emissiveSampler_0), (_S206)));
                break;
            }
            texture2d<float, access::sample> _S656 = (&kernelContext_2)->emissiveTexture_0;
            thread uint atlasWidth_6;
            thread uint atlasHeight_6;
            (*((&atlasWidth_6)) = (_S656).get_width(0)),(*((&atlasHeight_6)) = (_S656).get_height(0));
            int3 _S657 = int3(int(0), int(0), int(0));
            float4 metadata_6 = round((((&kernelContext_2)->emissiveTexture_0).read(vec<uint,2>(((_S657)).xy), uint(((_S657)).z))) * float4(255.0f) );
            if((metadata_6.x) == 255.0f)
            {
                uint _S658 = atlasWidth_6;
                uint _S659 = 2U % atlasWidth_6;
                int _S660 = int(_S659);
                uint _S661 = 2U / _S658;
                int3 _S662 = int3(_S660, int(_S661), int(0));
                int _S663 = int(uint(round((((&kernelContext_2)->emissiveTexture_0).read(vec<uint,2>(((_S662)).xy), uint(((_S662)).z))).x * 255.0f)));
                uint _S664 = atlasWidth_6;
                uint _S665 = 3U % atlasWidth_6;
                int _S666 = int(_S665);
                uint _S667 = 3U / _S664;
                int3 _S668 = int3(_S666, int(_S667), int(0));
                int2 minimumTile_6 = int2(_S663, int(uint(round((((&kernelContext_2)->emissiveTexture_0).read(vec<uint,2>(((_S668)).xy), uint(((_S668)).z))).x * 255.0f))));
                uint _S669 = atlasWidth_6;
                uint _S670 = 4U % atlasWidth_6;
                int _S671 = int(_S670);
                uint _S672 = 4U / _S669;
                int3 _S673 = int3(_S671, int(_S672), int(0));
                uint _S674 = uint(round((((&kernelContext_2)->emissiveTexture_0).read(vec<uint,2>(((_S673)).xy), uint(((_S673)).z))).x * 255.0f));
                uint _S675 = atlasWidth_6;
                uint _S676 = 5U % atlasWidth_6;
                int _S677 = int(_S676);
                uint _S678 = 5U / _S675;
                int3 _S679 = int3(_S677, int(_S678), int(0));
                uint _S680 = uint(round((((&kernelContext_2)->emissiveTexture_0).read(vec<uint,2>(((_S679)).xy), uint(((_S679)).z))).x * 255.0f));
                uint2 grid_6 = uint2(_S674, _S680);
                int2 tile_12 = int2(floor(_S206)) - minimumTile_6;
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
                    int3 _S681 = int3(int(1), int(0), int(0));
                    _S207 = (((&kernelContext_2)->emissiveTexture_0).read(vec<uint,2>(((_S681)).xy), uint(((_S681)).z)));
                    break;
                }
                uint offset_6 = 10U + 2U * (uint(tile_12.y) * _S674 + uint(tile_12.x));
                uint _S682 = atlasWidth_6;
                uint _S683 = offset_6 % atlasWidth_6;
                int _S684 = int(_S683);
                uint _S685 = offset_6 / _S682;
                int3 _S686 = int3(_S684, int(_S685), int(0));
                uint _S687 = uint(round((((&kernelContext_2)->emissiveTexture_0).read(vec<uint,2>(((_S686)).xy), uint(((_S686)).z))).x * 255.0f));
                uint _S688 = atlasWidth_6;
                uint _S689 = offset_6 + 1U;
                uint _S690 = _S689 % atlasWidth_6;
                int _S691 = int(_S690);
                uint _S692 = _S689 / _S688;
                int3 _S693 = int3(_S691, int(_S692), int(0));
                uint slot_12 = _S687 | (uint(round((((&kernelContext_2)->emissiveTexture_0).read(vec<uint,2>(((_S693)).xy), uint(((_S693)).z))).x * 255.0f)) << 8U);
                if(slot_12 == 0U)
                {
                    int3 _S694 = int3(int(1), int(0), int(0));
                    _S207 = (((&kernelContext_2)->emissiveTexture_0).read(vec<uint,2>(((_S694)).xy), uint(((_S694)).z)));
                    break;
                }
                uint slot_13 = slot_12 - 1U;
                uint _S695 = atlasWidth_6;
                uint _S696 = 6U % atlasWidth_6;
                int _S697 = int(_S696);
                uint _S698 = 6U / _S695;
                int3 _S699 = int3(_S697, int(_S698), int(0));
                uint _S700 = uint(round((((&kernelContext_2)->emissiveTexture_0).read(vec<uint,2>(((_S699)).xy), uint(((_S699)).z))).x * 255.0f));
                uint _S701 = atlasWidth_6;
                uint _S702 = 7U % atlasWidth_6;
                int _S703 = int(_S702);
                uint _S704 = 7U / _S701;
                int3 _S705 = int3(_S703, int(_S704), int(0));
                uint columns_6 = _S700 | (uint(round((((&kernelContext_2)->emissiveTexture_0).read(vec<uint,2>(((_S705)).xy), uint(((_S705)).z))).x * 255.0f)) << 8U);
                uint _S706 = atlasWidth_6;
                uint _S707 = 8U % atlasWidth_6;
                int _S708 = int(_S707);
                uint _S709 = 8U / _S706;
                int3 _S710 = int3(_S708, int(_S709), int(0));
                uint _S711 = uint(round((((&kernelContext_2)->emissiveTexture_0).read(vec<uint,2>(((_S710)).xy), uint(((_S710)).z))).x * 255.0f));
                uint _S712 = atlasWidth_6;
                uint _S713 = 9U % atlasWidth_6;
                int _S714 = int(_S713);
                uint _S715 = 9U / _S712;
                int3 _S716 = int3(_S714, int(_S715), int(0));
                uint rows_6 = ((_S711 | (uint(round((((&kernelContext_2)->emissiveTexture_0).read(vec<uint,2>(((_S716)).xy), uint(((_S716)).z))).x * 255.0f)) << 8U)) + columns_6 - 1U) / columns_6;
                uint metadataRows_6 = (10U + 2U * _S674 * _S680 + atlasWidth_6 - 1U) / atlasWidth_6;
                uint _S717 = atlasWidth_6 / columns_6;
                float _S718 = float(_S717);
                uint _S719 = (atlasHeight_6 - metadataRows_6) / rows_6;
                float2 cellSize_12 = float2(_S718, float(_S719));
                uint _S720 = slot_13 % columns_6;
                float _S721 = float(_S720);
                uint _S722 = slot_13 / columns_6;
                _S207 = (((&kernelContext_2)->emissiveTexture_0).sample(((&kernelContext_2)->emissiveSampler_0), ((float2(_S721, float(_S722)) * cellSize_12 + float2(1.5f, float(metadataRows_6) + 1.5f) + fract(_S206) * max(cellSize_12 - float2(3.0f) , float2(0.0f) )) / float2(float(atlasWidth_6), float(atlasHeight_6)))));
                break;
            }
            int2 _S723 = int2(metadata_6.zw);
            int2 tile_13 = int2(floor(_S206)) - int2(metadata_6.xy);
            if(any(tile_13 < (int2(int(0)) )))
            {
                hasSceneLighting_0 = true;
            }
            else
            {
                hasSceneLighting_0 = any(tile_13 >= _S723);
            }
            if(hasSceneLighting_0)
            {
                int3 _S724 = int3(int(min(1U, atlasWidth_6 - 1U)), int(0), int(0));
                _S207 = (((&kernelContext_2)->emissiveTexture_0).read(vec<uint,2>(((_S724)).xy), uint(((_S724)).z)));
                break;
            }
            uint _S725 = atlasWidth_6 / uint(_S723.x);
            float _S726 = float(_S725);
            uint _S727 = (atlasHeight_6 - 1U) / uint(_S723.y);
            float2 cellSize_13 = float2(_S726, float(_S727));
            _S207 = (((&kernelContext_2)->emissiveTexture_0).sample(((&kernelContext_2)->emissiveSampler_0), ((float2(tile_13) * cellSize_13 + float2(1.5f, 2.5f) + fract(_S206) * max(cellSize_13 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_6), float(atlasHeight_6)))));
            break;
        }
        emissionSample_0 = _S207;
        if((_S204.w) != 0.0f)
        {
            texture2d<float, access::sample> _S728 = (&kernelContext_2)->emissiveTexture_0;
            thread uint sourceWidth_1;
            thread uint sourceHeight_1;
            (*((&sourceWidth_1)) = (_S728).get_width(0)),(*((&sourceHeight_1)) = (_S728).get_height(0));
            texture2d<float, access::sample> _S729 = (&kernelContext_2)->emissiveValuesTexture_0;
            thread uint valueWidth_1;
            thread uint valueHeight_1;
            (*((&valueWidth_1)) = (_S729).get_width(0)),(*((&valueHeight_1)) = (_S729).get_height(0));
            if(valueWidth_1 != sourceWidth_1)
            {
                float3 _S730 = DecodeIndexedRgb_0(emissionSample_0, (&kernelContext_2)->emissiveValuesTexture_0);
                emissionSample_0.xyz = _S730;
            }
            else
            {
                emissionSample_0.z = (((&kernelContext_2)->emissiveValuesTexture_0).sample(((&kernelContext_2)->emissiveSampler_0), (_S206))).x;
            }
        }
        float4 _S731 = emissionSample_0;
        for(;;)
        {
            float4 _S732 = float4(_S36->compositeControls_0) ;
            if((_S732.x) != 16.0f)
            {
                _S207 = _S731;
                break;
            }
            bool _S733 = (_S732.w) >= 0.5f;
            for(;;)
            {
                if(!_S733)
                {
                    _S207 = (((&kernelContext_2)->compositeTexture_0).sample(((&kernelContext_2)->compositeSampler_0), (_S206)));
                    break;
                }
                texture2d<float, access::sample> _S734 = (&kernelContext_2)->compositeTexture_0;
                thread uint atlasWidth_7;
                thread uint atlasHeight_7;
                (*((&atlasWidth_7)) = (_S734).get_width(0)),(*((&atlasHeight_7)) = (_S734).get_height(0));
                int3 _S735 = int3(int(0), int(0), int(0));
                float4 metadata_7 = round((((&kernelContext_2)->compositeTexture_0).read(vec<uint,2>(((_S735)).xy), uint(((_S735)).z))) * float4(255.0f) );
                if((metadata_7.x) == 255.0f)
                {
                    uint _S736 = atlasWidth_7;
                    uint _S737 = 2U % atlasWidth_7;
                    int _S738 = int(_S737);
                    uint _S739 = 2U / _S736;
                    int3 _S740 = int3(_S738, int(_S739), int(0));
                    int _S741 = int(uint(round((((&kernelContext_2)->compositeTexture_0).read(vec<uint,2>(((_S740)).xy), uint(((_S740)).z))).x * 255.0f)));
                    uint _S742 = atlasWidth_7;
                    uint _S743 = 3U % atlasWidth_7;
                    int _S744 = int(_S743);
                    uint _S745 = 3U / _S742;
                    int3 _S746 = int3(_S744, int(_S745), int(0));
                    int2 minimumTile_7 = int2(_S741, int(uint(round((((&kernelContext_2)->compositeTexture_0).read(vec<uint,2>(((_S746)).xy), uint(((_S746)).z))).x * 255.0f))));
                    uint _S747 = atlasWidth_7;
                    uint _S748 = 4U % atlasWidth_7;
                    int _S749 = int(_S748);
                    uint _S750 = 4U / _S747;
                    int3 _S751 = int3(_S749, int(_S750), int(0));
                    uint _S752 = uint(round((((&kernelContext_2)->compositeTexture_0).read(vec<uint,2>(((_S751)).xy), uint(((_S751)).z))).x * 255.0f));
                    uint _S753 = atlasWidth_7;
                    uint _S754 = 5U % atlasWidth_7;
                    int _S755 = int(_S754);
                    uint _S756 = 5U / _S753;
                    int3 _S757 = int3(_S755, int(_S756), int(0));
                    uint _S758 = uint(round((((&kernelContext_2)->compositeTexture_0).read(vec<uint,2>(((_S757)).xy), uint(((_S757)).z))).x * 255.0f));
                    uint2 grid_7 = uint2(_S752, _S758);
                    int2 tile_14 = int2(floor(_S206)) - minimumTile_7;
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
                        int3 _S759 = int3(int(1), int(0), int(0));
                        _S207 = (((&kernelContext_2)->compositeTexture_0).read(vec<uint,2>(((_S759)).xy), uint(((_S759)).z)));
                        break;
                    }
                    uint offset_7 = 10U + 2U * (uint(tile_14.y) * _S752 + uint(tile_14.x));
                    uint _S760 = atlasWidth_7;
                    uint _S761 = offset_7 % atlasWidth_7;
                    int _S762 = int(_S761);
                    uint _S763 = offset_7 / _S760;
                    int3 _S764 = int3(_S762, int(_S763), int(0));
                    uint _S765 = uint(round((((&kernelContext_2)->compositeTexture_0).read(vec<uint,2>(((_S764)).xy), uint(((_S764)).z))).x * 255.0f));
                    uint _S766 = atlasWidth_7;
                    uint _S767 = offset_7 + 1U;
                    uint _S768 = _S767 % atlasWidth_7;
                    int _S769 = int(_S768);
                    uint _S770 = _S767 / _S766;
                    int3 _S771 = int3(_S769, int(_S770), int(0));
                    uint slot_14 = _S765 | (uint(round((((&kernelContext_2)->compositeTexture_0).read(vec<uint,2>(((_S771)).xy), uint(((_S771)).z))).x * 255.0f)) << 8U);
                    if(slot_14 == 0U)
                    {
                        int3 _S772 = int3(int(1), int(0), int(0));
                        _S207 = (((&kernelContext_2)->compositeTexture_0).read(vec<uint,2>(((_S772)).xy), uint(((_S772)).z)));
                        break;
                    }
                    uint slot_15 = slot_14 - 1U;
                    uint _S773 = atlasWidth_7;
                    uint _S774 = 6U % atlasWidth_7;
                    int _S775 = int(_S774);
                    uint _S776 = 6U / _S773;
                    int3 _S777 = int3(_S775, int(_S776), int(0));
                    uint _S778 = uint(round((((&kernelContext_2)->compositeTexture_0).read(vec<uint,2>(((_S777)).xy), uint(((_S777)).z))).x * 255.0f));
                    uint _S779 = atlasWidth_7;
                    uint _S780 = 7U % atlasWidth_7;
                    int _S781 = int(_S780);
                    uint _S782 = 7U / _S779;
                    int3 _S783 = int3(_S781, int(_S782), int(0));
                    uint columns_7 = _S778 | (uint(round((((&kernelContext_2)->compositeTexture_0).read(vec<uint,2>(((_S783)).xy), uint(((_S783)).z))).x * 255.0f)) << 8U);
                    uint _S784 = atlasWidth_7;
                    uint _S785 = 8U % atlasWidth_7;
                    int _S786 = int(_S785);
                    uint _S787 = 8U / _S784;
                    int3 _S788 = int3(_S786, int(_S787), int(0));
                    uint _S789 = uint(round((((&kernelContext_2)->compositeTexture_0).read(vec<uint,2>(((_S788)).xy), uint(((_S788)).z))).x * 255.0f));
                    uint _S790 = atlasWidth_7;
                    uint _S791 = 9U % atlasWidth_7;
                    int _S792 = int(_S791);
                    uint _S793 = 9U / _S790;
                    int3 _S794 = int3(_S792, int(_S793), int(0));
                    uint rows_7 = ((_S789 | (uint(round((((&kernelContext_2)->compositeTexture_0).read(vec<uint,2>(((_S794)).xy), uint(((_S794)).z))).x * 255.0f)) << 8U)) + columns_7 - 1U) / columns_7;
                    uint metadataRows_7 = (10U + 2U * _S752 * _S758 + atlasWidth_7 - 1U) / atlasWidth_7;
                    uint _S795 = atlasWidth_7 / columns_7;
                    float _S796 = float(_S795);
                    uint _S797 = (atlasHeight_7 - metadataRows_7) / rows_7;
                    float2 cellSize_14 = float2(_S796, float(_S797));
                    uint _S798 = slot_15 % columns_7;
                    float _S799 = float(_S798);
                    uint _S800 = slot_15 / columns_7;
                    _S207 = (((&kernelContext_2)->compositeTexture_0).sample(((&kernelContext_2)->compositeSampler_0), ((float2(_S799, float(_S800)) * cellSize_14 + float2(1.5f, float(metadataRows_7) + 1.5f) + fract(_S206) * max(cellSize_14 - float2(3.0f) , float2(0.0f) )) / float2(float(atlasWidth_7), float(atlasHeight_7)))));
                    break;
                }
                int2 _S801 = int2(metadata_7.zw);
                int2 tile_15 = int2(floor(_S206)) - int2(metadata_7.xy);
                if(any(tile_15 < (int2(int(0)) )))
                {
                    hasSceneLighting_0 = true;
                }
                else
                {
                    hasSceneLighting_0 = any(tile_15 >= _S801);
                }
                if(hasSceneLighting_0)
                {
                    int3 _S802 = int3(int(min(1U, atlasWidth_7 - 1U)), int(0), int(0));
                    _S207 = (((&kernelContext_2)->compositeTexture_0).read(vec<uint,2>(((_S802)).xy), uint(((_S802)).z)));
                    break;
                }
                uint _S803 = atlasWidth_7 / uint(_S801.x);
                float _S804 = float(_S803);
                uint _S805 = (atlasHeight_7 - 1U) / uint(_S801.y);
                float2 cellSize_15 = float2(_S804, float(_S805));
                _S207 = (((&kernelContext_2)->compositeTexture_0).sample(((&kernelContext_2)->compositeSampler_0), ((float2(tile_15) * cellSize_15 + float2(1.5f, 2.5f) + fract(_S206) * max(cellSize_15 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_7), float(atlasHeight_7)))));
                break;
            }
            uint operation_3 = uint(round(_S732.y));
            if(operation_3 == 1U)
            {
                _S207 = _S731 * _S207;
                break;
            }
            if(operation_3 == 2U)
            {
                _S207 = _S731 + _S207;
                break;
            }
            if(operation_3 == 3U)
            {
                _S207 = _S731 - _S207;
                break;
            }
            if(operation_3 == 4U)
            {
                float factor_3 = _S732.z;
                _S207 = _S731 * float4((1.0f - factor_3))  + _S207 * float4(factor_3) ;
                break;
            }
            _S207 = _S731;
            break;
        }
        unlitColor_0 = _S207.xyz;
    }
    else
    {
        unlitColor_0 = emissiveColor_0;
    }
    if((textureMask_0 & 64U) != 0U)
    {
        bool _S806 = (udimMask_0 & 64U) != 0U;
        for(;;)
        {
            if(!_S806)
            {
                _S207 = (((&kernelContext_2)->opacityTexture_0).sample(((&kernelContext_2)->opacitySampler_0), (_S206)));
                break;
            }
            texture2d<float, access::sample> _S807 = (&kernelContext_2)->opacityTexture_0;
            thread uint atlasWidth_8;
            thread uint atlasHeight_8;
            (*((&atlasWidth_8)) = (_S807).get_width(0)),(*((&atlasHeight_8)) = (_S807).get_height(0));
            int3 _S808 = int3(int(0), int(0), int(0));
            float4 metadata_8 = round((((&kernelContext_2)->opacityTexture_0).read(vec<uint,2>(((_S808)).xy), uint(((_S808)).z))) * float4(255.0f) );
            if((metadata_8.x) == 255.0f)
            {
                uint _S809 = atlasWidth_8;
                uint _S810 = 2U % atlasWidth_8;
                int _S811 = int(_S810);
                uint _S812 = 2U / _S809;
                int3 _S813 = int3(_S811, int(_S812), int(0));
                int _S814 = int(uint(round((((&kernelContext_2)->opacityTexture_0).read(vec<uint,2>(((_S813)).xy), uint(((_S813)).z))).x * 255.0f)));
                uint _S815 = atlasWidth_8;
                uint _S816 = 3U % atlasWidth_8;
                int _S817 = int(_S816);
                uint _S818 = 3U / _S815;
                int3 _S819 = int3(_S817, int(_S818), int(0));
                int2 minimumTile_8 = int2(_S814, int(uint(round((((&kernelContext_2)->opacityTexture_0).read(vec<uint,2>(((_S819)).xy), uint(((_S819)).z))).x * 255.0f))));
                uint _S820 = atlasWidth_8;
                uint _S821 = 4U % atlasWidth_8;
                int _S822 = int(_S821);
                uint _S823 = 4U / _S820;
                int3 _S824 = int3(_S822, int(_S823), int(0));
                uint _S825 = uint(round((((&kernelContext_2)->opacityTexture_0).read(vec<uint,2>(((_S824)).xy), uint(((_S824)).z))).x * 255.0f));
                uint _S826 = atlasWidth_8;
                uint _S827 = 5U % atlasWidth_8;
                int _S828 = int(_S827);
                uint _S829 = 5U / _S826;
                int3 _S830 = int3(_S828, int(_S829), int(0));
                uint _S831 = uint(round((((&kernelContext_2)->opacityTexture_0).read(vec<uint,2>(((_S830)).xy), uint(((_S830)).z))).x * 255.0f));
                uint2 grid_8 = uint2(_S825, _S831);
                int2 tile_16 = int2(floor(_S206)) - minimumTile_8;
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
                    int3 _S832 = int3(int(1), int(0), int(0));
                    _S207 = (((&kernelContext_2)->opacityTexture_0).read(vec<uint,2>(((_S832)).xy), uint(((_S832)).z)));
                    break;
                }
                uint offset_8 = 10U + 2U * (uint(tile_16.y) * _S825 + uint(tile_16.x));
                uint _S833 = atlasWidth_8;
                uint _S834 = offset_8 % atlasWidth_8;
                int _S835 = int(_S834);
                uint _S836 = offset_8 / _S833;
                int3 _S837 = int3(_S835, int(_S836), int(0));
                uint _S838 = uint(round((((&kernelContext_2)->opacityTexture_0).read(vec<uint,2>(((_S837)).xy), uint(((_S837)).z))).x * 255.0f));
                uint _S839 = atlasWidth_8;
                uint _S840 = offset_8 + 1U;
                uint _S841 = _S840 % atlasWidth_8;
                int _S842 = int(_S841);
                uint _S843 = _S840 / _S839;
                int3 _S844 = int3(_S842, int(_S843), int(0));
                uint slot_16 = _S838 | (uint(round((((&kernelContext_2)->opacityTexture_0).read(vec<uint,2>(((_S844)).xy), uint(((_S844)).z))).x * 255.0f)) << 8U);
                if(slot_16 == 0U)
                {
                    int3 _S845 = int3(int(1), int(0), int(0));
                    _S207 = (((&kernelContext_2)->opacityTexture_0).read(vec<uint,2>(((_S845)).xy), uint(((_S845)).z)));
                    break;
                }
                uint slot_17 = slot_16 - 1U;
                uint _S846 = atlasWidth_8;
                uint _S847 = 6U % atlasWidth_8;
                int _S848 = int(_S847);
                uint _S849 = 6U / _S846;
                int3 _S850 = int3(_S848, int(_S849), int(0));
                uint _S851 = uint(round((((&kernelContext_2)->opacityTexture_0).read(vec<uint,2>(((_S850)).xy), uint(((_S850)).z))).x * 255.0f));
                uint _S852 = atlasWidth_8;
                uint _S853 = 7U % atlasWidth_8;
                int _S854 = int(_S853);
                uint _S855 = 7U / _S852;
                int3 _S856 = int3(_S854, int(_S855), int(0));
                uint columns_8 = _S851 | (uint(round((((&kernelContext_2)->opacityTexture_0).read(vec<uint,2>(((_S856)).xy), uint(((_S856)).z))).x * 255.0f)) << 8U);
                uint _S857 = atlasWidth_8;
                uint _S858 = 8U % atlasWidth_8;
                int _S859 = int(_S858);
                uint _S860 = 8U / _S857;
                int3 _S861 = int3(_S859, int(_S860), int(0));
                uint _S862 = uint(round((((&kernelContext_2)->opacityTexture_0).read(vec<uint,2>(((_S861)).xy), uint(((_S861)).z))).x * 255.0f));
                uint _S863 = atlasWidth_8;
                uint _S864 = 9U % atlasWidth_8;
                int _S865 = int(_S864);
                uint _S866 = 9U / _S863;
                int3 _S867 = int3(_S865, int(_S866), int(0));
                uint rows_8 = ((_S862 | (uint(round((((&kernelContext_2)->opacityTexture_0).read(vec<uint,2>(((_S867)).xy), uint(((_S867)).z))).x * 255.0f)) << 8U)) + columns_8 - 1U) / columns_8;
                uint metadataRows_8 = (10U + 2U * _S825 * _S831 + atlasWidth_8 - 1U) / atlasWidth_8;
                uint _S868 = atlasWidth_8 / columns_8;
                float _S869 = float(_S868);
                uint _S870 = (atlasHeight_8 - metadataRows_8) / rows_8;
                float2 cellSize_16 = float2(_S869, float(_S870));
                uint _S871 = slot_17 % columns_8;
                float _S872 = float(_S871);
                uint _S873 = slot_17 / columns_8;
                _S207 = (((&kernelContext_2)->opacityTexture_0).sample(((&kernelContext_2)->opacitySampler_0), ((float2(_S872, float(_S873)) * cellSize_16 + float2(1.5f, float(metadataRows_8) + 1.5f) + fract(_S206) * max(cellSize_16 - float2(3.0f) , float2(0.0f) )) / float2(float(atlasWidth_8), float(atlasHeight_8)))));
                break;
            }
            int2 _S874 = int2(metadata_8.zw);
            int2 tile_17 = int2(floor(_S206)) - int2(metadata_8.xy);
            if(any(tile_17 < (int2(int(0)) )))
            {
                hasSceneLighting_0 = true;
            }
            else
            {
                hasSceneLighting_0 = any(tile_17 >= _S874);
            }
            if(hasSceneLighting_0)
            {
                int3 _S875 = int3(int(min(1U, atlasWidth_8 - 1U)), int(0), int(0));
                _S207 = (((&kernelContext_2)->opacityTexture_0).read(vec<uint,2>(((_S875)).xy), uint(((_S875)).z)));
                break;
            }
            uint _S876 = atlasWidth_8 / uint(_S874.x);
            float _S877 = float(_S876);
            uint _S878 = (atlasHeight_8 - 1U) / uint(_S874.y);
            float2 cellSize_17 = float2(_S877, float(_S878));
            _S207 = (((&kernelContext_2)->opacityTexture_0).sample(((&kernelContext_2)->opacitySampler_0), ((float2(tile_17) * cellSize_17 + float2(1.5f, 2.5f) + fract(_S206) * max(cellSize_17 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_8), float(atlasHeight_8)))));
            break;
        }
        for(;;)
        {
            float4 _S879 = float4(_S36->compositeControls_0) ;
            if((_S879.x) != 64.0f)
            {
                break;
            }
            bool _S880 = (_S879.w) >= 0.5f;
            for(;;)
            {
                if(!_S880)
                {
                    _S360 = (((&kernelContext_2)->compositeTexture_0).sample(((&kernelContext_2)->compositeSampler_0), (_S206)));
                    break;
                }
                texture2d<float, access::sample> _S881 = (&kernelContext_2)->compositeTexture_0;
                thread uint atlasWidth_9;
                thread uint atlasHeight_9;
                (*((&atlasWidth_9)) = (_S881).get_width(0)),(*((&atlasHeight_9)) = (_S881).get_height(0));
                int3 _S882 = int3(int(0), int(0), int(0));
                float4 metadata_9 = round((((&kernelContext_2)->compositeTexture_0).read(vec<uint,2>(((_S882)).xy), uint(((_S882)).z))) * float4(255.0f) );
                if((metadata_9.x) == 255.0f)
                {
                    uint _S883 = atlasWidth_9;
                    uint _S884 = 2U % atlasWidth_9;
                    int _S885 = int(_S884);
                    uint _S886 = 2U / _S883;
                    int3 _S887 = int3(_S885, int(_S886), int(0));
                    int _S888 = int(uint(round((((&kernelContext_2)->compositeTexture_0).read(vec<uint,2>(((_S887)).xy), uint(((_S887)).z))).x * 255.0f)));
                    uint _S889 = atlasWidth_9;
                    uint _S890 = 3U % atlasWidth_9;
                    int _S891 = int(_S890);
                    uint _S892 = 3U / _S889;
                    int3 _S893 = int3(_S891, int(_S892), int(0));
                    int2 minimumTile_9 = int2(_S888, int(uint(round((((&kernelContext_2)->compositeTexture_0).read(vec<uint,2>(((_S893)).xy), uint(((_S893)).z))).x * 255.0f))));
                    uint _S894 = atlasWidth_9;
                    uint _S895 = 4U % atlasWidth_9;
                    int _S896 = int(_S895);
                    uint _S897 = 4U / _S894;
                    int3 _S898 = int3(_S896, int(_S897), int(0));
                    uint _S899 = uint(round((((&kernelContext_2)->compositeTexture_0).read(vec<uint,2>(((_S898)).xy), uint(((_S898)).z))).x * 255.0f));
                    uint _S900 = atlasWidth_9;
                    uint _S901 = 5U % atlasWidth_9;
                    int _S902 = int(_S901);
                    uint _S903 = 5U / _S900;
                    int3 _S904 = int3(_S902, int(_S903), int(0));
                    uint _S905 = uint(round((((&kernelContext_2)->compositeTexture_0).read(vec<uint,2>(((_S904)).xy), uint(((_S904)).z))).x * 255.0f));
                    uint2 grid_9 = uint2(_S899, _S905);
                    int2 tile_18 = int2(floor(_S206)) - minimumTile_9;
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
                        int3 _S906 = int3(int(1), int(0), int(0));
                        _S360 = (((&kernelContext_2)->compositeTexture_0).read(vec<uint,2>(((_S906)).xy), uint(((_S906)).z)));
                        break;
                    }
                    uint offset_9 = 10U + 2U * (uint(tile_18.y) * _S899 + uint(tile_18.x));
                    uint _S907 = atlasWidth_9;
                    uint _S908 = offset_9 % atlasWidth_9;
                    int _S909 = int(_S908);
                    uint _S910 = offset_9 / _S907;
                    int3 _S911 = int3(_S909, int(_S910), int(0));
                    uint _S912 = uint(round((((&kernelContext_2)->compositeTexture_0).read(vec<uint,2>(((_S911)).xy), uint(((_S911)).z))).x * 255.0f));
                    uint _S913 = atlasWidth_9;
                    uint _S914 = offset_9 + 1U;
                    uint _S915 = _S914 % atlasWidth_9;
                    int _S916 = int(_S915);
                    uint _S917 = _S914 / _S913;
                    int3 _S918 = int3(_S916, int(_S917), int(0));
                    uint slot_18 = _S912 | (uint(round((((&kernelContext_2)->compositeTexture_0).read(vec<uint,2>(((_S918)).xy), uint(((_S918)).z))).x * 255.0f)) << 8U);
                    if(slot_18 == 0U)
                    {
                        int3 _S919 = int3(int(1), int(0), int(0));
                        _S360 = (((&kernelContext_2)->compositeTexture_0).read(vec<uint,2>(((_S919)).xy), uint(((_S919)).z)));
                        break;
                    }
                    uint slot_19 = slot_18 - 1U;
                    uint _S920 = atlasWidth_9;
                    uint _S921 = 6U % atlasWidth_9;
                    int _S922 = int(_S921);
                    uint _S923 = 6U / _S920;
                    int3 _S924 = int3(_S922, int(_S923), int(0));
                    uint _S925 = uint(round((((&kernelContext_2)->compositeTexture_0).read(vec<uint,2>(((_S924)).xy), uint(((_S924)).z))).x * 255.0f));
                    uint _S926 = atlasWidth_9;
                    uint _S927 = 7U % atlasWidth_9;
                    int _S928 = int(_S927);
                    uint _S929 = 7U / _S926;
                    int3 _S930 = int3(_S928, int(_S929), int(0));
                    uint columns_9 = _S925 | (uint(round((((&kernelContext_2)->compositeTexture_0).read(vec<uint,2>(((_S930)).xy), uint(((_S930)).z))).x * 255.0f)) << 8U);
                    uint _S931 = atlasWidth_9;
                    uint _S932 = 8U % atlasWidth_9;
                    int _S933 = int(_S932);
                    uint _S934 = 8U / _S931;
                    int3 _S935 = int3(_S933, int(_S934), int(0));
                    uint _S936 = uint(round((((&kernelContext_2)->compositeTexture_0).read(vec<uint,2>(((_S935)).xy), uint(((_S935)).z))).x * 255.0f));
                    uint _S937 = atlasWidth_9;
                    uint _S938 = 9U % atlasWidth_9;
                    int _S939 = int(_S938);
                    uint _S940 = 9U / _S937;
                    int3 _S941 = int3(_S939, int(_S940), int(0));
                    uint rows_9 = ((_S936 | (uint(round((((&kernelContext_2)->compositeTexture_0).read(vec<uint,2>(((_S941)).xy), uint(((_S941)).z))).x * 255.0f)) << 8U)) + columns_9 - 1U) / columns_9;
                    uint metadataRows_9 = (10U + 2U * _S899 * _S905 + atlasWidth_9 - 1U) / atlasWidth_9;
                    uint _S942 = atlasWidth_9 / columns_9;
                    float _S943 = float(_S942);
                    uint _S944 = (atlasHeight_9 - metadataRows_9) / rows_9;
                    float2 cellSize_18 = float2(_S943, float(_S944));
                    uint _S945 = slot_19 % columns_9;
                    float _S946 = float(_S945);
                    uint _S947 = slot_19 / columns_9;
                    _S360 = (((&kernelContext_2)->compositeTexture_0).sample(((&kernelContext_2)->compositeSampler_0), ((float2(_S946, float(_S947)) * cellSize_18 + float2(1.5f, float(metadataRows_9) + 1.5f) + fract(_S206) * max(cellSize_18 - float2(3.0f) , float2(0.0f) )) / float2(float(atlasWidth_9), float(atlasHeight_9)))));
                    break;
                }
                int2 _S948 = int2(metadata_9.zw);
                int2 tile_19 = int2(floor(_S206)) - int2(metadata_9.xy);
                if(any(tile_19 < (int2(int(0)) )))
                {
                    hasSceneLighting_0 = true;
                }
                else
                {
                    hasSceneLighting_0 = any(tile_19 >= _S948);
                }
                if(hasSceneLighting_0)
                {
                    int3 _S949 = int3(int(min(1U, atlasWidth_9 - 1U)), int(0), int(0));
                    _S360 = (((&kernelContext_2)->compositeTexture_0).read(vec<uint,2>(((_S949)).xy), uint(((_S949)).z)));
                    break;
                }
                uint _S950 = atlasWidth_9 / uint(_S948.x);
                float _S951 = float(_S950);
                uint _S952 = (atlasHeight_9 - 1U) / uint(_S948.y);
                float2 cellSize_19 = float2(_S951, float(_S952));
                _S360 = (((&kernelContext_2)->compositeTexture_0).sample(((&kernelContext_2)->compositeSampler_0), ((float2(tile_19) * cellSize_19 + float2(1.5f, 2.5f) + fract(_S206) * max(cellSize_19 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_9), float(atlasHeight_9)))));
                break;
            }
            uint operation_4 = uint(round(_S879.y));
            if(operation_4 == 1U)
            {
                _S207 = _S207 * _S360;
                break;
            }
            if(operation_4 == 2U)
            {
                _S207 = _S207 + _S360;
                break;
            }
            if(operation_4 == 3U)
            {
                _S207 = _S207 - _S360;
                break;
            }
            if(operation_4 == 4U)
            {
                float factor_4 = _S879.z;
                _S207 = _S207 * float4((1.0f - factor_4))  + _S360 * float4(factor_4) ;
                break;
            }
            break;
        }
        opacity_0 = saturate(_S207.x);
    }
    float occlusion_0;
    if((textureMask_0 & 128U) != 0U)
    {
        bool _S953 = (udimMask_0 & 128U) != 0U;
        for(;;)
        {
            if(!_S953)
            {
                _S207 = (((&kernelContext_2)->occlusionTexture_0).sample(((&kernelContext_2)->occlusionSampler_0), (_S206)));
                break;
            }
            texture2d<float, access::sample> _S954 = (&kernelContext_2)->occlusionTexture_0;
            thread uint atlasWidth_10;
            thread uint atlasHeight_10;
            (*((&atlasWidth_10)) = (_S954).get_width(0)),(*((&atlasHeight_10)) = (_S954).get_height(0));
            int3 _S955 = int3(int(0), int(0), int(0));
            float4 metadata_10 = round((((&kernelContext_2)->occlusionTexture_0).read(vec<uint,2>(((_S955)).xy), uint(((_S955)).z))) * float4(255.0f) );
            if((metadata_10.x) == 255.0f)
            {
                uint _S956 = atlasWidth_10;
                uint _S957 = 2U % atlasWidth_10;
                int _S958 = int(_S957);
                uint _S959 = 2U / _S956;
                int3 _S960 = int3(_S958, int(_S959), int(0));
                int _S961 = int(uint(round((((&kernelContext_2)->occlusionTexture_0).read(vec<uint,2>(((_S960)).xy), uint(((_S960)).z))).x * 255.0f)));
                uint _S962 = atlasWidth_10;
                uint _S963 = 3U % atlasWidth_10;
                int _S964 = int(_S963);
                uint _S965 = 3U / _S962;
                int3 _S966 = int3(_S964, int(_S965), int(0));
                int2 minimumTile_10 = int2(_S961, int(uint(round((((&kernelContext_2)->occlusionTexture_0).read(vec<uint,2>(((_S966)).xy), uint(((_S966)).z))).x * 255.0f))));
                uint _S967 = atlasWidth_10;
                uint _S968 = 4U % atlasWidth_10;
                int _S969 = int(_S968);
                uint _S970 = 4U / _S967;
                int3 _S971 = int3(_S969, int(_S970), int(0));
                uint _S972 = uint(round((((&kernelContext_2)->occlusionTexture_0).read(vec<uint,2>(((_S971)).xy), uint(((_S971)).z))).x * 255.0f));
                uint _S973 = atlasWidth_10;
                uint _S974 = 5U % atlasWidth_10;
                int _S975 = int(_S974);
                uint _S976 = 5U / _S973;
                int3 _S977 = int3(_S975, int(_S976), int(0));
                uint _S978 = uint(round((((&kernelContext_2)->occlusionTexture_0).read(vec<uint,2>(((_S977)).xy), uint(((_S977)).z))).x * 255.0f));
                uint2 grid_10 = uint2(_S972, _S978);
                int2 tile_20 = int2(floor(_S206)) - minimumTile_10;
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
                    int3 _S979 = int3(int(1), int(0), int(0));
                    _S207 = (((&kernelContext_2)->occlusionTexture_0).read(vec<uint,2>(((_S979)).xy), uint(((_S979)).z)));
                    break;
                }
                uint offset_10 = 10U + 2U * (uint(tile_20.y) * _S972 + uint(tile_20.x));
                uint _S980 = atlasWidth_10;
                uint _S981 = offset_10 % atlasWidth_10;
                int _S982 = int(_S981);
                uint _S983 = offset_10 / _S980;
                int3 _S984 = int3(_S982, int(_S983), int(0));
                uint _S985 = uint(round((((&kernelContext_2)->occlusionTexture_0).read(vec<uint,2>(((_S984)).xy), uint(((_S984)).z))).x * 255.0f));
                uint _S986 = atlasWidth_10;
                uint _S987 = offset_10 + 1U;
                uint _S988 = _S987 % atlasWidth_10;
                int _S989 = int(_S988);
                uint _S990 = _S987 / _S986;
                int3 _S991 = int3(_S989, int(_S990), int(0));
                uint slot_20 = _S985 | (uint(round((((&kernelContext_2)->occlusionTexture_0).read(vec<uint,2>(((_S991)).xy), uint(((_S991)).z))).x * 255.0f)) << 8U);
                if(slot_20 == 0U)
                {
                    int3 _S992 = int3(int(1), int(0), int(0));
                    _S207 = (((&kernelContext_2)->occlusionTexture_0).read(vec<uint,2>(((_S992)).xy), uint(((_S992)).z)));
                    break;
                }
                uint slot_21 = slot_20 - 1U;
                uint _S993 = atlasWidth_10;
                uint _S994 = 6U % atlasWidth_10;
                int _S995 = int(_S994);
                uint _S996 = 6U / _S993;
                int3 _S997 = int3(_S995, int(_S996), int(0));
                uint _S998 = uint(round((((&kernelContext_2)->occlusionTexture_0).read(vec<uint,2>(((_S997)).xy), uint(((_S997)).z))).x * 255.0f));
                uint _S999 = atlasWidth_10;
                uint _S1000 = 7U % atlasWidth_10;
                int _S1001 = int(_S1000);
                uint _S1002 = 7U / _S999;
                int3 _S1003 = int3(_S1001, int(_S1002), int(0));
                uint columns_10 = _S998 | (uint(round((((&kernelContext_2)->occlusionTexture_0).read(vec<uint,2>(((_S1003)).xy), uint(((_S1003)).z))).x * 255.0f)) << 8U);
                uint _S1004 = atlasWidth_10;
                uint _S1005 = 8U % atlasWidth_10;
                int _S1006 = int(_S1005);
                uint _S1007 = 8U / _S1004;
                int3 _S1008 = int3(_S1006, int(_S1007), int(0));
                uint _S1009 = uint(round((((&kernelContext_2)->occlusionTexture_0).read(vec<uint,2>(((_S1008)).xy), uint(((_S1008)).z))).x * 255.0f));
                uint _S1010 = atlasWidth_10;
                uint _S1011 = 9U % atlasWidth_10;
                int _S1012 = int(_S1011);
                uint _S1013 = 9U / _S1010;
                int3 _S1014 = int3(_S1012, int(_S1013), int(0));
                uint rows_10 = ((_S1009 | (uint(round((((&kernelContext_2)->occlusionTexture_0).read(vec<uint,2>(((_S1014)).xy), uint(((_S1014)).z))).x * 255.0f)) << 8U)) + columns_10 - 1U) / columns_10;
                uint metadataRows_10 = (10U + 2U * _S972 * _S978 + atlasWidth_10 - 1U) / atlasWidth_10;
                uint _S1015 = atlasWidth_10 / columns_10;
                float _S1016 = float(_S1015);
                uint _S1017 = (atlasHeight_10 - metadataRows_10) / rows_10;
                float2 cellSize_20 = float2(_S1016, float(_S1017));
                uint _S1018 = slot_21 % columns_10;
                float _S1019 = float(_S1018);
                uint _S1020 = slot_21 / columns_10;
                _S207 = (((&kernelContext_2)->occlusionTexture_0).sample(((&kernelContext_2)->occlusionSampler_0), ((float2(_S1019, float(_S1020)) * cellSize_20 + float2(1.5f, float(metadataRows_10) + 1.5f) + fract(_S206) * max(cellSize_20 - float2(3.0f) , float2(0.0f) )) / float2(float(atlasWidth_10), float(atlasHeight_10)))));
                break;
            }
            int2 _S1021 = int2(metadata_10.zw);
            int2 tile_21 = int2(floor(_S206)) - int2(metadata_10.xy);
            if(any(tile_21 < (int2(int(0)) )))
            {
                hasSceneLighting_0 = true;
            }
            else
            {
                hasSceneLighting_0 = any(tile_21 >= _S1021);
            }
            if(hasSceneLighting_0)
            {
                int3 _S1022 = int3(int(min(1U, atlasWidth_10 - 1U)), int(0), int(0));
                _S207 = (((&kernelContext_2)->occlusionTexture_0).read(vec<uint,2>(((_S1022)).xy), uint(((_S1022)).z)));
                break;
            }
            uint _S1023 = atlasWidth_10 / uint(_S1021.x);
            float _S1024 = float(_S1023);
            uint _S1025 = (atlasHeight_10 - 1U) / uint(_S1021.y);
            float2 cellSize_21 = float2(_S1024, float(_S1025));
            _S207 = (((&kernelContext_2)->occlusionTexture_0).sample(((&kernelContext_2)->occlusionSampler_0), ((float2(tile_21) * cellSize_21 + float2(1.5f, 2.5f) + fract(_S206) * max(cellSize_21 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_10), float(atlasHeight_10)))));
            break;
        }
        for(;;)
        {
            float4 _S1026 = float4(_S36->compositeControls_0) ;
            if((_S1026.x) != 128.0f)
            {
                break;
            }
            bool _S1027 = (_S1026.w) >= 0.5f;
            for(;;)
            {
                if(!_S1027)
                {
                    _S360 = (((&kernelContext_2)->compositeTexture_0).sample(((&kernelContext_2)->compositeSampler_0), (_S206)));
                    break;
                }
                texture2d<float, access::sample> _S1028 = (&kernelContext_2)->compositeTexture_0;
                thread uint atlasWidth_11;
                thread uint atlasHeight_11;
                (*((&atlasWidth_11)) = (_S1028).get_width(0)),(*((&atlasHeight_11)) = (_S1028).get_height(0));
                int3 _S1029 = int3(int(0), int(0), int(0));
                float4 metadata_11 = round((((&kernelContext_2)->compositeTexture_0).read(vec<uint,2>(((_S1029)).xy), uint(((_S1029)).z))) * float4(255.0f) );
                if((metadata_11.x) == 255.0f)
                {
                    uint _S1030 = atlasWidth_11;
                    uint _S1031 = 2U % atlasWidth_11;
                    int _S1032 = int(_S1031);
                    uint _S1033 = 2U / _S1030;
                    int3 _S1034 = int3(_S1032, int(_S1033), int(0));
                    int _S1035 = int(uint(round((((&kernelContext_2)->compositeTexture_0).read(vec<uint,2>(((_S1034)).xy), uint(((_S1034)).z))).x * 255.0f)));
                    uint _S1036 = atlasWidth_11;
                    uint _S1037 = 3U % atlasWidth_11;
                    int _S1038 = int(_S1037);
                    uint _S1039 = 3U / _S1036;
                    int3 _S1040 = int3(_S1038, int(_S1039), int(0));
                    int2 minimumTile_11 = int2(_S1035, int(uint(round((((&kernelContext_2)->compositeTexture_0).read(vec<uint,2>(((_S1040)).xy), uint(((_S1040)).z))).x * 255.0f))));
                    uint _S1041 = atlasWidth_11;
                    uint _S1042 = 4U % atlasWidth_11;
                    int _S1043 = int(_S1042);
                    uint _S1044 = 4U / _S1041;
                    int3 _S1045 = int3(_S1043, int(_S1044), int(0));
                    uint _S1046 = uint(round((((&kernelContext_2)->compositeTexture_0).read(vec<uint,2>(((_S1045)).xy), uint(((_S1045)).z))).x * 255.0f));
                    uint _S1047 = atlasWidth_11;
                    uint _S1048 = 5U % atlasWidth_11;
                    int _S1049 = int(_S1048);
                    uint _S1050 = 5U / _S1047;
                    int3 _S1051 = int3(_S1049, int(_S1050), int(0));
                    uint _S1052 = uint(round((((&kernelContext_2)->compositeTexture_0).read(vec<uint,2>(((_S1051)).xy), uint(((_S1051)).z))).x * 255.0f));
                    uint2 grid_11 = uint2(_S1046, _S1052);
                    int2 tile_22 = int2(floor(_S206)) - minimumTile_11;
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
                        int3 _S1053 = int3(int(1), int(0), int(0));
                        _S360 = (((&kernelContext_2)->compositeTexture_0).read(vec<uint,2>(((_S1053)).xy), uint(((_S1053)).z)));
                        break;
                    }
                    uint offset_11 = 10U + 2U * (uint(tile_22.y) * _S1046 + uint(tile_22.x));
                    uint _S1054 = atlasWidth_11;
                    uint _S1055 = offset_11 % atlasWidth_11;
                    int _S1056 = int(_S1055);
                    uint _S1057 = offset_11 / _S1054;
                    int3 _S1058 = int3(_S1056, int(_S1057), int(0));
                    uint _S1059 = uint(round((((&kernelContext_2)->compositeTexture_0).read(vec<uint,2>(((_S1058)).xy), uint(((_S1058)).z))).x * 255.0f));
                    uint _S1060 = atlasWidth_11;
                    uint _S1061 = offset_11 + 1U;
                    uint _S1062 = _S1061 % atlasWidth_11;
                    int _S1063 = int(_S1062);
                    uint _S1064 = _S1061 / _S1060;
                    int3 _S1065 = int3(_S1063, int(_S1064), int(0));
                    uint slot_22 = _S1059 | (uint(round((((&kernelContext_2)->compositeTexture_0).read(vec<uint,2>(((_S1065)).xy), uint(((_S1065)).z))).x * 255.0f)) << 8U);
                    if(slot_22 == 0U)
                    {
                        int3 _S1066 = int3(int(1), int(0), int(0));
                        _S360 = (((&kernelContext_2)->compositeTexture_0).read(vec<uint,2>(((_S1066)).xy), uint(((_S1066)).z)));
                        break;
                    }
                    uint slot_23 = slot_22 - 1U;
                    uint _S1067 = atlasWidth_11;
                    uint _S1068 = 6U % atlasWidth_11;
                    int _S1069 = int(_S1068);
                    uint _S1070 = 6U / _S1067;
                    int3 _S1071 = int3(_S1069, int(_S1070), int(0));
                    uint _S1072 = uint(round((((&kernelContext_2)->compositeTexture_0).read(vec<uint,2>(((_S1071)).xy), uint(((_S1071)).z))).x * 255.0f));
                    uint _S1073 = atlasWidth_11;
                    uint _S1074 = 7U % atlasWidth_11;
                    int _S1075 = int(_S1074);
                    uint _S1076 = 7U / _S1073;
                    int3 _S1077 = int3(_S1075, int(_S1076), int(0));
                    uint columns_11 = _S1072 | (uint(round((((&kernelContext_2)->compositeTexture_0).read(vec<uint,2>(((_S1077)).xy), uint(((_S1077)).z))).x * 255.0f)) << 8U);
                    uint _S1078 = atlasWidth_11;
                    uint _S1079 = 8U % atlasWidth_11;
                    int _S1080 = int(_S1079);
                    uint _S1081 = 8U / _S1078;
                    int3 _S1082 = int3(_S1080, int(_S1081), int(0));
                    uint _S1083 = uint(round((((&kernelContext_2)->compositeTexture_0).read(vec<uint,2>(((_S1082)).xy), uint(((_S1082)).z))).x * 255.0f));
                    uint _S1084 = atlasWidth_11;
                    uint _S1085 = 9U % atlasWidth_11;
                    int _S1086 = int(_S1085);
                    uint _S1087 = 9U / _S1084;
                    int3 _S1088 = int3(_S1086, int(_S1087), int(0));
                    uint rows_11 = ((_S1083 | (uint(round((((&kernelContext_2)->compositeTexture_0).read(vec<uint,2>(((_S1088)).xy), uint(((_S1088)).z))).x * 255.0f)) << 8U)) + columns_11 - 1U) / columns_11;
                    uint metadataRows_11 = (10U + 2U * _S1046 * _S1052 + atlasWidth_11 - 1U) / atlasWidth_11;
                    uint _S1089 = atlasWidth_11 / columns_11;
                    float _S1090 = float(_S1089);
                    uint _S1091 = (atlasHeight_11 - metadataRows_11) / rows_11;
                    float2 cellSize_22 = float2(_S1090, float(_S1091));
                    uint _S1092 = slot_23 % columns_11;
                    float _S1093 = float(_S1092);
                    uint _S1094 = slot_23 / columns_11;
                    _S360 = (((&kernelContext_2)->compositeTexture_0).sample(((&kernelContext_2)->compositeSampler_0), ((float2(_S1093, float(_S1094)) * cellSize_22 + float2(1.5f, float(metadataRows_11) + 1.5f) + fract(_S206) * max(cellSize_22 - float2(3.0f) , float2(0.0f) )) / float2(float(atlasWidth_11), float(atlasHeight_11)))));
                    break;
                }
                int2 _S1095 = int2(metadata_11.zw);
                int2 tile_23 = int2(floor(_S206)) - int2(metadata_11.xy);
                if(any(tile_23 < (int2(int(0)) )))
                {
                    hasSceneLighting_0 = true;
                }
                else
                {
                    hasSceneLighting_0 = any(tile_23 >= _S1095);
                }
                if(hasSceneLighting_0)
                {
                    int3 _S1096 = int3(int(min(1U, atlasWidth_11 - 1U)), int(0), int(0));
                    _S360 = (((&kernelContext_2)->compositeTexture_0).read(vec<uint,2>(((_S1096)).xy), uint(((_S1096)).z)));
                    break;
                }
                uint _S1097 = atlasWidth_11 / uint(_S1095.x);
                float _S1098 = float(_S1097);
                uint _S1099 = (atlasHeight_11 - 1U) / uint(_S1095.y);
                float2 cellSize_23 = float2(_S1098, float(_S1099));
                _S360 = (((&kernelContext_2)->compositeTexture_0).sample(((&kernelContext_2)->compositeSampler_0), ((float2(tile_23) * cellSize_23 + float2(1.5f, 2.5f) + fract(_S206) * max(cellSize_23 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_11), float(atlasHeight_11)))));
                break;
            }
            uint operation_5 = uint(round(_S1026.y));
            if(operation_5 == 1U)
            {
                _S207 = _S207 * _S360;
                break;
            }
            if(operation_5 == 2U)
            {
                _S207 = _S207 + _S360;
                break;
            }
            if(operation_5 == 3U)
            {
                _S207 = _S207 - _S360;
                break;
            }
            if(operation_5 == 4U)
            {
                float factor_5 = _S1026.z;
                _S207 = _S207 * float4((1.0f - factor_5))  + _S360 * float4(factor_5) ;
                break;
            }
            break;
        }
        occlusion_0 = saturate(_S207.x);
    }
    else
    {
        occlusion_0 = _S191;
    }
    float3 specularColor_0;
    if((textureMask_0 & 256U) != 0U)
    {
        bool _S1100 = (udimMask_0 & 256U) != 0U;
        for(;;)
        {
            if(!_S1100)
            {
                _S207 = (((&kernelContext_2)->specularColorTexture_0).sample(((&kernelContext_2)->specularColorSampler_0), (_S206)));
                break;
            }
            texture2d<float, access::sample> _S1101 = (&kernelContext_2)->specularColorTexture_0;
            thread uint atlasWidth_12;
            thread uint atlasHeight_12;
            (*((&atlasWidth_12)) = (_S1101).get_width(0)),(*((&atlasHeight_12)) = (_S1101).get_height(0));
            int3 _S1102 = int3(int(0), int(0), int(0));
            float4 metadata_12 = round((((&kernelContext_2)->specularColorTexture_0).read(vec<uint,2>(((_S1102)).xy), uint(((_S1102)).z))) * float4(255.0f) );
            if((metadata_12.x) == 255.0f)
            {
                uint _S1103 = atlasWidth_12;
                uint _S1104 = 2U % atlasWidth_12;
                int _S1105 = int(_S1104);
                uint _S1106 = 2U / _S1103;
                int3 _S1107 = int3(_S1105, int(_S1106), int(0));
                int _S1108 = int(uint(round((((&kernelContext_2)->specularColorTexture_0).read(vec<uint,2>(((_S1107)).xy), uint(((_S1107)).z))).x * 255.0f)));
                uint _S1109 = atlasWidth_12;
                uint _S1110 = 3U % atlasWidth_12;
                int _S1111 = int(_S1110);
                uint _S1112 = 3U / _S1109;
                int3 _S1113 = int3(_S1111, int(_S1112), int(0));
                int2 minimumTile_12 = int2(_S1108, int(uint(round((((&kernelContext_2)->specularColorTexture_0).read(vec<uint,2>(((_S1113)).xy), uint(((_S1113)).z))).x * 255.0f))));
                uint _S1114 = atlasWidth_12;
                uint _S1115 = 4U % atlasWidth_12;
                int _S1116 = int(_S1115);
                uint _S1117 = 4U / _S1114;
                int3 _S1118 = int3(_S1116, int(_S1117), int(0));
                uint _S1119 = uint(round((((&kernelContext_2)->specularColorTexture_0).read(vec<uint,2>(((_S1118)).xy), uint(((_S1118)).z))).x * 255.0f));
                uint _S1120 = atlasWidth_12;
                uint _S1121 = 5U % atlasWidth_12;
                int _S1122 = int(_S1121);
                uint _S1123 = 5U / _S1120;
                int3 _S1124 = int3(_S1122, int(_S1123), int(0));
                uint _S1125 = uint(round((((&kernelContext_2)->specularColorTexture_0).read(vec<uint,2>(((_S1124)).xy), uint(((_S1124)).z))).x * 255.0f));
                uint2 grid_12 = uint2(_S1119, _S1125);
                int2 tile_24 = int2(floor(_S206)) - minimumTile_12;
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
                    int3 _S1126 = int3(int(1), int(0), int(0));
                    _S207 = (((&kernelContext_2)->specularColorTexture_0).read(vec<uint,2>(((_S1126)).xy), uint(((_S1126)).z)));
                    break;
                }
                uint offset_12 = 10U + 2U * (uint(tile_24.y) * _S1119 + uint(tile_24.x));
                uint _S1127 = atlasWidth_12;
                uint _S1128 = offset_12 % atlasWidth_12;
                int _S1129 = int(_S1128);
                uint _S1130 = offset_12 / _S1127;
                int3 _S1131 = int3(_S1129, int(_S1130), int(0));
                uint _S1132 = uint(round((((&kernelContext_2)->specularColorTexture_0).read(vec<uint,2>(((_S1131)).xy), uint(((_S1131)).z))).x * 255.0f));
                uint _S1133 = atlasWidth_12;
                uint _S1134 = offset_12 + 1U;
                uint _S1135 = _S1134 % atlasWidth_12;
                int _S1136 = int(_S1135);
                uint _S1137 = _S1134 / _S1133;
                int3 _S1138 = int3(_S1136, int(_S1137), int(0));
                uint slot_24 = _S1132 | (uint(round((((&kernelContext_2)->specularColorTexture_0).read(vec<uint,2>(((_S1138)).xy), uint(((_S1138)).z))).x * 255.0f)) << 8U);
                if(slot_24 == 0U)
                {
                    int3 _S1139 = int3(int(1), int(0), int(0));
                    _S207 = (((&kernelContext_2)->specularColorTexture_0).read(vec<uint,2>(((_S1139)).xy), uint(((_S1139)).z)));
                    break;
                }
                uint slot_25 = slot_24 - 1U;
                uint _S1140 = atlasWidth_12;
                uint _S1141 = 6U % atlasWidth_12;
                int _S1142 = int(_S1141);
                uint _S1143 = 6U / _S1140;
                int3 _S1144 = int3(_S1142, int(_S1143), int(0));
                uint _S1145 = uint(round((((&kernelContext_2)->specularColorTexture_0).read(vec<uint,2>(((_S1144)).xy), uint(((_S1144)).z))).x * 255.0f));
                uint _S1146 = atlasWidth_12;
                uint _S1147 = 7U % atlasWidth_12;
                int _S1148 = int(_S1147);
                uint _S1149 = 7U / _S1146;
                int3 _S1150 = int3(_S1148, int(_S1149), int(0));
                uint columns_12 = _S1145 | (uint(round((((&kernelContext_2)->specularColorTexture_0).read(vec<uint,2>(((_S1150)).xy), uint(((_S1150)).z))).x * 255.0f)) << 8U);
                uint _S1151 = atlasWidth_12;
                uint _S1152 = 8U % atlasWidth_12;
                int _S1153 = int(_S1152);
                uint _S1154 = 8U / _S1151;
                int3 _S1155 = int3(_S1153, int(_S1154), int(0));
                uint _S1156 = uint(round((((&kernelContext_2)->specularColorTexture_0).read(vec<uint,2>(((_S1155)).xy), uint(((_S1155)).z))).x * 255.0f));
                uint _S1157 = atlasWidth_12;
                uint _S1158 = 9U % atlasWidth_12;
                int _S1159 = int(_S1158);
                uint _S1160 = 9U / _S1157;
                int3 _S1161 = int3(_S1159, int(_S1160), int(0));
                uint rows_12 = ((_S1156 | (uint(round((((&kernelContext_2)->specularColorTexture_0).read(vec<uint,2>(((_S1161)).xy), uint(((_S1161)).z))).x * 255.0f)) << 8U)) + columns_12 - 1U) / columns_12;
                uint metadataRows_12 = (10U + 2U * _S1119 * _S1125 + atlasWidth_12 - 1U) / atlasWidth_12;
                uint _S1162 = atlasWidth_12 / columns_12;
                float _S1163 = float(_S1162);
                uint _S1164 = (atlasHeight_12 - metadataRows_12) / rows_12;
                float2 cellSize_24 = float2(_S1163, float(_S1164));
                uint _S1165 = slot_25 % columns_12;
                float _S1166 = float(_S1165);
                uint _S1167 = slot_25 / columns_12;
                _S207 = (((&kernelContext_2)->specularColorTexture_0).sample(((&kernelContext_2)->specularColorSampler_0), ((float2(_S1166, float(_S1167)) * cellSize_24 + float2(1.5f, float(metadataRows_12) + 1.5f) + fract(_S206) * max(cellSize_24 - float2(3.0f) , float2(0.0f) )) / float2(float(atlasWidth_12), float(atlasHeight_12)))));
                break;
            }
            int2 _S1168 = int2(metadata_12.zw);
            int2 tile_25 = int2(floor(_S206)) - int2(metadata_12.xy);
            if(any(tile_25 < (int2(int(0)) )))
            {
                hasSceneLighting_0 = true;
            }
            else
            {
                hasSceneLighting_0 = any(tile_25 >= _S1168);
            }
            if(hasSceneLighting_0)
            {
                int3 _S1169 = int3(int(min(1U, atlasWidth_12 - 1U)), int(0), int(0));
                _S207 = (((&kernelContext_2)->specularColorTexture_0).read(vec<uint,2>(((_S1169)).xy), uint(((_S1169)).z)));
                break;
            }
            uint _S1170 = atlasWidth_12 / uint(_S1168.x);
            float _S1171 = float(_S1170);
            uint _S1172 = (atlasHeight_12 - 1U) / uint(_S1168.y);
            float2 cellSize_25 = float2(_S1171, float(_S1172));
            _S207 = (((&kernelContext_2)->specularColorTexture_0).sample(((&kernelContext_2)->specularColorSampler_0), ((float2(tile_25) * cellSize_25 + float2(1.5f, 2.5f) + fract(_S206) * max(cellSize_25 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_12), float(atlasHeight_12)))));
            break;
        }
        for(;;)
        {
            float4 _S1173 = float4(_S36->compositeControls_0) ;
            if((_S1173.x) != 256.0f)
            {
                break;
            }
            bool _S1174 = (_S1173.w) >= 0.5f;
            for(;;)
            {
                if(!_S1174)
                {
                    _S360 = (((&kernelContext_2)->compositeTexture_0).sample(((&kernelContext_2)->compositeSampler_0), (_S206)));
                    break;
                }
                texture2d<float, access::sample> _S1175 = (&kernelContext_2)->compositeTexture_0;
                thread uint atlasWidth_13;
                thread uint atlasHeight_13;
                (*((&atlasWidth_13)) = (_S1175).get_width(0)),(*((&atlasHeight_13)) = (_S1175).get_height(0));
                int3 _S1176 = int3(int(0), int(0), int(0));
                float4 metadata_13 = round((((&kernelContext_2)->compositeTexture_0).read(vec<uint,2>(((_S1176)).xy), uint(((_S1176)).z))) * float4(255.0f) );
                if((metadata_13.x) == 255.0f)
                {
                    uint _S1177 = atlasWidth_13;
                    uint _S1178 = 2U % atlasWidth_13;
                    int _S1179 = int(_S1178);
                    uint _S1180 = 2U / _S1177;
                    int3 _S1181 = int3(_S1179, int(_S1180), int(0));
                    int _S1182 = int(uint(round((((&kernelContext_2)->compositeTexture_0).read(vec<uint,2>(((_S1181)).xy), uint(((_S1181)).z))).x * 255.0f)));
                    uint _S1183 = atlasWidth_13;
                    uint _S1184 = 3U % atlasWidth_13;
                    int _S1185 = int(_S1184);
                    uint _S1186 = 3U / _S1183;
                    int3 _S1187 = int3(_S1185, int(_S1186), int(0));
                    int2 minimumTile_13 = int2(_S1182, int(uint(round((((&kernelContext_2)->compositeTexture_0).read(vec<uint,2>(((_S1187)).xy), uint(((_S1187)).z))).x * 255.0f))));
                    uint _S1188 = atlasWidth_13;
                    uint _S1189 = 4U % atlasWidth_13;
                    int _S1190 = int(_S1189);
                    uint _S1191 = 4U / _S1188;
                    int3 _S1192 = int3(_S1190, int(_S1191), int(0));
                    uint _S1193 = uint(round((((&kernelContext_2)->compositeTexture_0).read(vec<uint,2>(((_S1192)).xy), uint(((_S1192)).z))).x * 255.0f));
                    uint _S1194 = atlasWidth_13;
                    uint _S1195 = 5U % atlasWidth_13;
                    int _S1196 = int(_S1195);
                    uint _S1197 = 5U / _S1194;
                    int3 _S1198 = int3(_S1196, int(_S1197), int(0));
                    uint _S1199 = uint(round((((&kernelContext_2)->compositeTexture_0).read(vec<uint,2>(((_S1198)).xy), uint(((_S1198)).z))).x * 255.0f));
                    uint2 grid_13 = uint2(_S1193, _S1199);
                    int2 tile_26 = int2(floor(_S206)) - minimumTile_13;
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
                        int3 _S1200 = int3(int(1), int(0), int(0));
                        _S360 = (((&kernelContext_2)->compositeTexture_0).read(vec<uint,2>(((_S1200)).xy), uint(((_S1200)).z)));
                        break;
                    }
                    uint offset_13 = 10U + 2U * (uint(tile_26.y) * _S1193 + uint(tile_26.x));
                    uint _S1201 = atlasWidth_13;
                    uint _S1202 = offset_13 % atlasWidth_13;
                    int _S1203 = int(_S1202);
                    uint _S1204 = offset_13 / _S1201;
                    int3 _S1205 = int3(_S1203, int(_S1204), int(0));
                    uint _S1206 = uint(round((((&kernelContext_2)->compositeTexture_0).read(vec<uint,2>(((_S1205)).xy), uint(((_S1205)).z))).x * 255.0f));
                    uint _S1207 = atlasWidth_13;
                    uint _S1208 = offset_13 + 1U;
                    uint _S1209 = _S1208 % atlasWidth_13;
                    int _S1210 = int(_S1209);
                    uint _S1211 = _S1208 / _S1207;
                    int3 _S1212 = int3(_S1210, int(_S1211), int(0));
                    uint slot_26 = _S1206 | (uint(round((((&kernelContext_2)->compositeTexture_0).read(vec<uint,2>(((_S1212)).xy), uint(((_S1212)).z))).x * 255.0f)) << 8U);
                    if(slot_26 == 0U)
                    {
                        int3 _S1213 = int3(int(1), int(0), int(0));
                        _S360 = (((&kernelContext_2)->compositeTexture_0).read(vec<uint,2>(((_S1213)).xy), uint(((_S1213)).z)));
                        break;
                    }
                    uint slot_27 = slot_26 - 1U;
                    uint _S1214 = atlasWidth_13;
                    uint _S1215 = 6U % atlasWidth_13;
                    int _S1216 = int(_S1215);
                    uint _S1217 = 6U / _S1214;
                    int3 _S1218 = int3(_S1216, int(_S1217), int(0));
                    uint _S1219 = uint(round((((&kernelContext_2)->compositeTexture_0).read(vec<uint,2>(((_S1218)).xy), uint(((_S1218)).z))).x * 255.0f));
                    uint _S1220 = atlasWidth_13;
                    uint _S1221 = 7U % atlasWidth_13;
                    int _S1222 = int(_S1221);
                    uint _S1223 = 7U / _S1220;
                    int3 _S1224 = int3(_S1222, int(_S1223), int(0));
                    uint columns_13 = _S1219 | (uint(round((((&kernelContext_2)->compositeTexture_0).read(vec<uint,2>(((_S1224)).xy), uint(((_S1224)).z))).x * 255.0f)) << 8U);
                    uint _S1225 = atlasWidth_13;
                    uint _S1226 = 8U % atlasWidth_13;
                    int _S1227 = int(_S1226);
                    uint _S1228 = 8U / _S1225;
                    int3 _S1229 = int3(_S1227, int(_S1228), int(0));
                    uint _S1230 = uint(round((((&kernelContext_2)->compositeTexture_0).read(vec<uint,2>(((_S1229)).xy), uint(((_S1229)).z))).x * 255.0f));
                    uint _S1231 = atlasWidth_13;
                    uint _S1232 = 9U % atlasWidth_13;
                    int _S1233 = int(_S1232);
                    uint _S1234 = 9U / _S1231;
                    int3 _S1235 = int3(_S1233, int(_S1234), int(0));
                    uint rows_13 = ((_S1230 | (uint(round((((&kernelContext_2)->compositeTexture_0).read(vec<uint,2>(((_S1235)).xy), uint(((_S1235)).z))).x * 255.0f)) << 8U)) + columns_13 - 1U) / columns_13;
                    uint metadataRows_13 = (10U + 2U * _S1193 * _S1199 + atlasWidth_13 - 1U) / atlasWidth_13;
                    uint _S1236 = atlasWidth_13 / columns_13;
                    float _S1237 = float(_S1236);
                    uint _S1238 = (atlasHeight_13 - metadataRows_13) / rows_13;
                    float2 cellSize_26 = float2(_S1237, float(_S1238));
                    uint _S1239 = slot_27 % columns_13;
                    float _S1240 = float(_S1239);
                    uint _S1241 = slot_27 / columns_13;
                    _S360 = (((&kernelContext_2)->compositeTexture_0).sample(((&kernelContext_2)->compositeSampler_0), ((float2(_S1240, float(_S1241)) * cellSize_26 + float2(1.5f, float(metadataRows_13) + 1.5f) + fract(_S206) * max(cellSize_26 - float2(3.0f) , float2(0.0f) )) / float2(float(atlasWidth_13), float(atlasHeight_13)))));
                    break;
                }
                int2 _S1242 = int2(metadata_13.zw);
                int2 tile_27 = int2(floor(_S206)) - int2(metadata_13.xy);
                if(any(tile_27 < (int2(int(0)) )))
                {
                    hasSceneLighting_0 = true;
                }
                else
                {
                    hasSceneLighting_0 = any(tile_27 >= _S1242);
                }
                if(hasSceneLighting_0)
                {
                    int3 _S1243 = int3(int(min(1U, atlasWidth_13 - 1U)), int(0), int(0));
                    _S360 = (((&kernelContext_2)->compositeTexture_0).read(vec<uint,2>(((_S1243)).xy), uint(((_S1243)).z)));
                    break;
                }
                uint _S1244 = atlasWidth_13 / uint(_S1242.x);
                float _S1245 = float(_S1244);
                uint _S1246 = (atlasHeight_13 - 1U) / uint(_S1242.y);
                float2 cellSize_27 = float2(_S1245, float(_S1246));
                _S360 = (((&kernelContext_2)->compositeTexture_0).sample(((&kernelContext_2)->compositeSampler_0), ((float2(tile_27) * cellSize_27 + float2(1.5f, 2.5f) + fract(_S206) * max(cellSize_27 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_13), float(atlasHeight_13)))));
                break;
            }
            uint operation_6 = uint(round(_S1173.y));
            if(operation_6 == 1U)
            {
                _S207 = _S207 * _S360;
                break;
            }
            if(operation_6 == 2U)
            {
                _S207 = _S207 + _S360;
                break;
            }
            if(operation_6 == 3U)
            {
                _S207 = _S207 - _S360;
                break;
            }
            if(operation_6 == 4U)
            {
                float factor_6 = _S1173.z;
                _S207 = _S207 * float4((1.0f - factor_6))  + _S360 * float4(factor_6) ;
                break;
            }
            break;
        }
        specularColor_0 = saturate(_S207.xyz);
    }
    else
    {
        specularColor_0 = _S199;
    }
    float clearcoatAmount_0;
    if((textureMask_0 & 512U) != 0U)
    {
        bool _S1247 = (udimMask_0 & 512U) != 0U;
        for(;;)
        {
            if(!_S1247)
            {
                _S207 = (((&kernelContext_2)->clearcoatTexture_0).sample(((&kernelContext_2)->clearcoatSampler_0), (_S206)));
                break;
            }
            texture2d<float, access::sample> _S1248 = (&kernelContext_2)->clearcoatTexture_0;
            thread uint atlasWidth_14;
            thread uint atlasHeight_14;
            (*((&atlasWidth_14)) = (_S1248).get_width(0)),(*((&atlasHeight_14)) = (_S1248).get_height(0));
            int3 _S1249 = int3(int(0), int(0), int(0));
            float4 metadata_14 = round((((&kernelContext_2)->clearcoatTexture_0).read(vec<uint,2>(((_S1249)).xy), uint(((_S1249)).z))) * float4(255.0f) );
            if((metadata_14.x) == 255.0f)
            {
                uint _S1250 = atlasWidth_14;
                uint _S1251 = 2U % atlasWidth_14;
                int _S1252 = int(_S1251);
                uint _S1253 = 2U / _S1250;
                int3 _S1254 = int3(_S1252, int(_S1253), int(0));
                int _S1255 = int(uint(round((((&kernelContext_2)->clearcoatTexture_0).read(vec<uint,2>(((_S1254)).xy), uint(((_S1254)).z))).x * 255.0f)));
                uint _S1256 = atlasWidth_14;
                uint _S1257 = 3U % atlasWidth_14;
                int _S1258 = int(_S1257);
                uint _S1259 = 3U / _S1256;
                int3 _S1260 = int3(_S1258, int(_S1259), int(0));
                int2 minimumTile_14 = int2(_S1255, int(uint(round((((&kernelContext_2)->clearcoatTexture_0).read(vec<uint,2>(((_S1260)).xy), uint(((_S1260)).z))).x * 255.0f))));
                uint _S1261 = atlasWidth_14;
                uint _S1262 = 4U % atlasWidth_14;
                int _S1263 = int(_S1262);
                uint _S1264 = 4U / _S1261;
                int3 _S1265 = int3(_S1263, int(_S1264), int(0));
                uint _S1266 = uint(round((((&kernelContext_2)->clearcoatTexture_0).read(vec<uint,2>(((_S1265)).xy), uint(((_S1265)).z))).x * 255.0f));
                uint _S1267 = atlasWidth_14;
                uint _S1268 = 5U % atlasWidth_14;
                int _S1269 = int(_S1268);
                uint _S1270 = 5U / _S1267;
                int3 _S1271 = int3(_S1269, int(_S1270), int(0));
                uint _S1272 = uint(round((((&kernelContext_2)->clearcoatTexture_0).read(vec<uint,2>(((_S1271)).xy), uint(((_S1271)).z))).x * 255.0f));
                uint2 grid_14 = uint2(_S1266, _S1272);
                int2 tile_28 = int2(floor(_S206)) - minimumTile_14;
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
                    int3 _S1273 = int3(int(1), int(0), int(0));
                    _S207 = (((&kernelContext_2)->clearcoatTexture_0).read(vec<uint,2>(((_S1273)).xy), uint(((_S1273)).z)));
                    break;
                }
                uint offset_14 = 10U + 2U * (uint(tile_28.y) * _S1266 + uint(tile_28.x));
                uint _S1274 = atlasWidth_14;
                uint _S1275 = offset_14 % atlasWidth_14;
                int _S1276 = int(_S1275);
                uint _S1277 = offset_14 / _S1274;
                int3 _S1278 = int3(_S1276, int(_S1277), int(0));
                uint _S1279 = uint(round((((&kernelContext_2)->clearcoatTexture_0).read(vec<uint,2>(((_S1278)).xy), uint(((_S1278)).z))).x * 255.0f));
                uint _S1280 = atlasWidth_14;
                uint _S1281 = offset_14 + 1U;
                uint _S1282 = _S1281 % atlasWidth_14;
                int _S1283 = int(_S1282);
                uint _S1284 = _S1281 / _S1280;
                int3 _S1285 = int3(_S1283, int(_S1284), int(0));
                uint slot_28 = _S1279 | (uint(round((((&kernelContext_2)->clearcoatTexture_0).read(vec<uint,2>(((_S1285)).xy), uint(((_S1285)).z))).x * 255.0f)) << 8U);
                if(slot_28 == 0U)
                {
                    int3 _S1286 = int3(int(1), int(0), int(0));
                    _S207 = (((&kernelContext_2)->clearcoatTexture_0).read(vec<uint,2>(((_S1286)).xy), uint(((_S1286)).z)));
                    break;
                }
                uint slot_29 = slot_28 - 1U;
                uint _S1287 = atlasWidth_14;
                uint _S1288 = 6U % atlasWidth_14;
                int _S1289 = int(_S1288);
                uint _S1290 = 6U / _S1287;
                int3 _S1291 = int3(_S1289, int(_S1290), int(0));
                uint _S1292 = uint(round((((&kernelContext_2)->clearcoatTexture_0).read(vec<uint,2>(((_S1291)).xy), uint(((_S1291)).z))).x * 255.0f));
                uint _S1293 = atlasWidth_14;
                uint _S1294 = 7U % atlasWidth_14;
                int _S1295 = int(_S1294);
                uint _S1296 = 7U / _S1293;
                int3 _S1297 = int3(_S1295, int(_S1296), int(0));
                uint columns_14 = _S1292 | (uint(round((((&kernelContext_2)->clearcoatTexture_0).read(vec<uint,2>(((_S1297)).xy), uint(((_S1297)).z))).x * 255.0f)) << 8U);
                uint _S1298 = atlasWidth_14;
                uint _S1299 = 8U % atlasWidth_14;
                int _S1300 = int(_S1299);
                uint _S1301 = 8U / _S1298;
                int3 _S1302 = int3(_S1300, int(_S1301), int(0));
                uint _S1303 = uint(round((((&kernelContext_2)->clearcoatTexture_0).read(vec<uint,2>(((_S1302)).xy), uint(((_S1302)).z))).x * 255.0f));
                uint _S1304 = atlasWidth_14;
                uint _S1305 = 9U % atlasWidth_14;
                int _S1306 = int(_S1305);
                uint _S1307 = 9U / _S1304;
                int3 _S1308 = int3(_S1306, int(_S1307), int(0));
                uint rows_14 = ((_S1303 | (uint(round((((&kernelContext_2)->clearcoatTexture_0).read(vec<uint,2>(((_S1308)).xy), uint(((_S1308)).z))).x * 255.0f)) << 8U)) + columns_14 - 1U) / columns_14;
                uint metadataRows_14 = (10U + 2U * _S1266 * _S1272 + atlasWidth_14 - 1U) / atlasWidth_14;
                uint _S1309 = atlasWidth_14 / columns_14;
                float _S1310 = float(_S1309);
                uint _S1311 = (atlasHeight_14 - metadataRows_14) / rows_14;
                float2 cellSize_28 = float2(_S1310, float(_S1311));
                uint _S1312 = slot_29 % columns_14;
                float _S1313 = float(_S1312);
                uint _S1314 = slot_29 / columns_14;
                _S207 = (((&kernelContext_2)->clearcoatTexture_0).sample(((&kernelContext_2)->clearcoatSampler_0), ((float2(_S1313, float(_S1314)) * cellSize_28 + float2(1.5f, float(metadataRows_14) + 1.5f) + fract(_S206) * max(cellSize_28 - float2(3.0f) , float2(0.0f) )) / float2(float(atlasWidth_14), float(atlasHeight_14)))));
                break;
            }
            int2 _S1315 = int2(metadata_14.zw);
            int2 tile_29 = int2(floor(_S206)) - int2(metadata_14.xy);
            if(any(tile_29 < (int2(int(0)) )))
            {
                hasSceneLighting_0 = true;
            }
            else
            {
                hasSceneLighting_0 = any(tile_29 >= _S1315);
            }
            if(hasSceneLighting_0)
            {
                int3 _S1316 = int3(int(min(1U, atlasWidth_14 - 1U)), int(0), int(0));
                _S207 = (((&kernelContext_2)->clearcoatTexture_0).read(vec<uint,2>(((_S1316)).xy), uint(((_S1316)).z)));
                break;
            }
            uint _S1317 = atlasWidth_14 / uint(_S1315.x);
            float _S1318 = float(_S1317);
            uint _S1319 = (atlasHeight_14 - 1U) / uint(_S1315.y);
            float2 cellSize_29 = float2(_S1318, float(_S1319));
            _S207 = (((&kernelContext_2)->clearcoatTexture_0).sample(((&kernelContext_2)->clearcoatSampler_0), ((float2(tile_29) * cellSize_29 + float2(1.5f, 2.5f) + fract(_S206) * max(cellSize_29 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_14), float(atlasHeight_14)))));
            break;
        }
        for(;;)
        {
            float4 _S1320 = float4(_S36->compositeControls_0) ;
            if((_S1320.x) != 512.0f)
            {
                break;
            }
            bool _S1321 = (_S1320.w) >= 0.5f;
            for(;;)
            {
                if(!_S1321)
                {
                    _S360 = (((&kernelContext_2)->compositeTexture_0).sample(((&kernelContext_2)->compositeSampler_0), (_S206)));
                    break;
                }
                texture2d<float, access::sample> _S1322 = (&kernelContext_2)->compositeTexture_0;
                thread uint atlasWidth_15;
                thread uint atlasHeight_15;
                (*((&atlasWidth_15)) = (_S1322).get_width(0)),(*((&atlasHeight_15)) = (_S1322).get_height(0));
                int3 _S1323 = int3(int(0), int(0), int(0));
                float4 metadata_15 = round((((&kernelContext_2)->compositeTexture_0).read(vec<uint,2>(((_S1323)).xy), uint(((_S1323)).z))) * float4(255.0f) );
                if((metadata_15.x) == 255.0f)
                {
                    uint _S1324 = atlasWidth_15;
                    uint _S1325 = 2U % atlasWidth_15;
                    int _S1326 = int(_S1325);
                    uint _S1327 = 2U / _S1324;
                    int3 _S1328 = int3(_S1326, int(_S1327), int(0));
                    int _S1329 = int(uint(round((((&kernelContext_2)->compositeTexture_0).read(vec<uint,2>(((_S1328)).xy), uint(((_S1328)).z))).x * 255.0f)));
                    uint _S1330 = atlasWidth_15;
                    uint _S1331 = 3U % atlasWidth_15;
                    int _S1332 = int(_S1331);
                    uint _S1333 = 3U / _S1330;
                    int3 _S1334 = int3(_S1332, int(_S1333), int(0));
                    int2 minimumTile_15 = int2(_S1329, int(uint(round((((&kernelContext_2)->compositeTexture_0).read(vec<uint,2>(((_S1334)).xy), uint(((_S1334)).z))).x * 255.0f))));
                    uint _S1335 = atlasWidth_15;
                    uint _S1336 = 4U % atlasWidth_15;
                    int _S1337 = int(_S1336);
                    uint _S1338 = 4U / _S1335;
                    int3 _S1339 = int3(_S1337, int(_S1338), int(0));
                    uint _S1340 = uint(round((((&kernelContext_2)->compositeTexture_0).read(vec<uint,2>(((_S1339)).xy), uint(((_S1339)).z))).x * 255.0f));
                    uint _S1341 = atlasWidth_15;
                    uint _S1342 = 5U % atlasWidth_15;
                    int _S1343 = int(_S1342);
                    uint _S1344 = 5U / _S1341;
                    int3 _S1345 = int3(_S1343, int(_S1344), int(0));
                    uint _S1346 = uint(round((((&kernelContext_2)->compositeTexture_0).read(vec<uint,2>(((_S1345)).xy), uint(((_S1345)).z))).x * 255.0f));
                    uint2 grid_15 = uint2(_S1340, _S1346);
                    int2 tile_30 = int2(floor(_S206)) - minimumTile_15;
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
                        int3 _S1347 = int3(int(1), int(0), int(0));
                        _S360 = (((&kernelContext_2)->compositeTexture_0).read(vec<uint,2>(((_S1347)).xy), uint(((_S1347)).z)));
                        break;
                    }
                    uint offset_15 = 10U + 2U * (uint(tile_30.y) * _S1340 + uint(tile_30.x));
                    uint _S1348 = atlasWidth_15;
                    uint _S1349 = offset_15 % atlasWidth_15;
                    int _S1350 = int(_S1349);
                    uint _S1351 = offset_15 / _S1348;
                    int3 _S1352 = int3(_S1350, int(_S1351), int(0));
                    uint _S1353 = uint(round((((&kernelContext_2)->compositeTexture_0).read(vec<uint,2>(((_S1352)).xy), uint(((_S1352)).z))).x * 255.0f));
                    uint _S1354 = atlasWidth_15;
                    uint _S1355 = offset_15 + 1U;
                    uint _S1356 = _S1355 % atlasWidth_15;
                    int _S1357 = int(_S1356);
                    uint _S1358 = _S1355 / _S1354;
                    int3 _S1359 = int3(_S1357, int(_S1358), int(0));
                    uint slot_30 = _S1353 | (uint(round((((&kernelContext_2)->compositeTexture_0).read(vec<uint,2>(((_S1359)).xy), uint(((_S1359)).z))).x * 255.0f)) << 8U);
                    if(slot_30 == 0U)
                    {
                        int3 _S1360 = int3(int(1), int(0), int(0));
                        _S360 = (((&kernelContext_2)->compositeTexture_0).read(vec<uint,2>(((_S1360)).xy), uint(((_S1360)).z)));
                        break;
                    }
                    uint slot_31 = slot_30 - 1U;
                    uint _S1361 = atlasWidth_15;
                    uint _S1362 = 6U % atlasWidth_15;
                    int _S1363 = int(_S1362);
                    uint _S1364 = 6U / _S1361;
                    int3 _S1365 = int3(_S1363, int(_S1364), int(0));
                    uint _S1366 = uint(round((((&kernelContext_2)->compositeTexture_0).read(vec<uint,2>(((_S1365)).xy), uint(((_S1365)).z))).x * 255.0f));
                    uint _S1367 = atlasWidth_15;
                    uint _S1368 = 7U % atlasWidth_15;
                    int _S1369 = int(_S1368);
                    uint _S1370 = 7U / _S1367;
                    int3 _S1371 = int3(_S1369, int(_S1370), int(0));
                    uint columns_15 = _S1366 | (uint(round((((&kernelContext_2)->compositeTexture_0).read(vec<uint,2>(((_S1371)).xy), uint(((_S1371)).z))).x * 255.0f)) << 8U);
                    uint _S1372 = atlasWidth_15;
                    uint _S1373 = 8U % atlasWidth_15;
                    int _S1374 = int(_S1373);
                    uint _S1375 = 8U / _S1372;
                    int3 _S1376 = int3(_S1374, int(_S1375), int(0));
                    uint _S1377 = uint(round((((&kernelContext_2)->compositeTexture_0).read(vec<uint,2>(((_S1376)).xy), uint(((_S1376)).z))).x * 255.0f));
                    uint _S1378 = atlasWidth_15;
                    uint _S1379 = 9U % atlasWidth_15;
                    int _S1380 = int(_S1379);
                    uint _S1381 = 9U / _S1378;
                    int3 _S1382 = int3(_S1380, int(_S1381), int(0));
                    uint rows_15 = ((_S1377 | (uint(round((((&kernelContext_2)->compositeTexture_0).read(vec<uint,2>(((_S1382)).xy), uint(((_S1382)).z))).x * 255.0f)) << 8U)) + columns_15 - 1U) / columns_15;
                    uint metadataRows_15 = (10U + 2U * _S1340 * _S1346 + atlasWidth_15 - 1U) / atlasWidth_15;
                    uint _S1383 = atlasWidth_15 / columns_15;
                    float _S1384 = float(_S1383);
                    uint _S1385 = (atlasHeight_15 - metadataRows_15) / rows_15;
                    float2 cellSize_30 = float2(_S1384, float(_S1385));
                    uint _S1386 = slot_31 % columns_15;
                    float _S1387 = float(_S1386);
                    uint _S1388 = slot_31 / columns_15;
                    _S360 = (((&kernelContext_2)->compositeTexture_0).sample(((&kernelContext_2)->compositeSampler_0), ((float2(_S1387, float(_S1388)) * cellSize_30 + float2(1.5f, float(metadataRows_15) + 1.5f) + fract(_S206) * max(cellSize_30 - float2(3.0f) , float2(0.0f) )) / float2(float(atlasWidth_15), float(atlasHeight_15)))));
                    break;
                }
                int2 _S1389 = int2(metadata_15.zw);
                int2 tile_31 = int2(floor(_S206)) - int2(metadata_15.xy);
                if(any(tile_31 < (int2(int(0)) )))
                {
                    hasSceneLighting_0 = true;
                }
                else
                {
                    hasSceneLighting_0 = any(tile_31 >= _S1389);
                }
                if(hasSceneLighting_0)
                {
                    int3 _S1390 = int3(int(min(1U, atlasWidth_15 - 1U)), int(0), int(0));
                    _S360 = (((&kernelContext_2)->compositeTexture_0).read(vec<uint,2>(((_S1390)).xy), uint(((_S1390)).z)));
                    break;
                }
                uint _S1391 = atlasWidth_15 / uint(_S1389.x);
                float _S1392 = float(_S1391);
                uint _S1393 = (atlasHeight_15 - 1U) / uint(_S1389.y);
                float2 cellSize_31 = float2(_S1392, float(_S1393));
                _S360 = (((&kernelContext_2)->compositeTexture_0).sample(((&kernelContext_2)->compositeSampler_0), ((float2(tile_31) * cellSize_31 + float2(1.5f, 2.5f) + fract(_S206) * max(cellSize_31 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_15), float(atlasHeight_15)))));
                break;
            }
            uint operation_7 = uint(round(_S1320.y));
            if(operation_7 == 1U)
            {
                _S207 = _S207 * _S360;
                break;
            }
            if(operation_7 == 2U)
            {
                _S207 = _S207 + _S360;
                break;
            }
            if(operation_7 == 3U)
            {
                _S207 = _S207 - _S360;
                break;
            }
            if(operation_7 == 4U)
            {
                float factor_7 = _S1320.z;
                _S207 = _S207 * float4((1.0f - factor_7))  + _S360 * float4(factor_7) ;
                break;
            }
            break;
        }
        clearcoatAmount_0 = saturate(_S207.x);
    }
    else
    {
        clearcoatAmount_0 = _S201;
    }
    float clearcoatRoughness_0;
    if((textureMask_0 & 1024U) != 0U)
    {
        bool _S1394 = (udimMask_0 & 1024U) != 0U;
        for(;;)
        {
            if(!_S1394)
            {
                _S207 = (((&kernelContext_2)->clearcoatRoughnessTexture_0).sample(((&kernelContext_2)->clearcoatRoughnessSampler_0), (_S206)));
                break;
            }
            texture2d<float, access::sample> _S1395 = (&kernelContext_2)->clearcoatRoughnessTexture_0;
            thread uint atlasWidth_16;
            thread uint atlasHeight_16;
            (*((&atlasWidth_16)) = (_S1395).get_width(0)),(*((&atlasHeight_16)) = (_S1395).get_height(0));
            int3 _S1396 = int3(int(0), int(0), int(0));
            float4 metadata_16 = round((((&kernelContext_2)->clearcoatRoughnessTexture_0).read(vec<uint,2>(((_S1396)).xy), uint(((_S1396)).z))) * float4(255.0f) );
            if((metadata_16.x) == 255.0f)
            {
                uint _S1397 = atlasWidth_16;
                uint _S1398 = 2U % atlasWidth_16;
                int _S1399 = int(_S1398);
                uint _S1400 = 2U / _S1397;
                int3 _S1401 = int3(_S1399, int(_S1400), int(0));
                int _S1402 = int(uint(round((((&kernelContext_2)->clearcoatRoughnessTexture_0).read(vec<uint,2>(((_S1401)).xy), uint(((_S1401)).z))).x * 255.0f)));
                uint _S1403 = atlasWidth_16;
                uint _S1404 = 3U % atlasWidth_16;
                int _S1405 = int(_S1404);
                uint _S1406 = 3U / _S1403;
                int3 _S1407 = int3(_S1405, int(_S1406), int(0));
                int2 minimumTile_16 = int2(_S1402, int(uint(round((((&kernelContext_2)->clearcoatRoughnessTexture_0).read(vec<uint,2>(((_S1407)).xy), uint(((_S1407)).z))).x * 255.0f))));
                uint _S1408 = atlasWidth_16;
                uint _S1409 = 4U % atlasWidth_16;
                int _S1410 = int(_S1409);
                uint _S1411 = 4U / _S1408;
                int3 _S1412 = int3(_S1410, int(_S1411), int(0));
                uint _S1413 = uint(round((((&kernelContext_2)->clearcoatRoughnessTexture_0).read(vec<uint,2>(((_S1412)).xy), uint(((_S1412)).z))).x * 255.0f));
                uint _S1414 = atlasWidth_16;
                uint _S1415 = 5U % atlasWidth_16;
                int _S1416 = int(_S1415);
                uint _S1417 = 5U / _S1414;
                int3 _S1418 = int3(_S1416, int(_S1417), int(0));
                uint _S1419 = uint(round((((&kernelContext_2)->clearcoatRoughnessTexture_0).read(vec<uint,2>(((_S1418)).xy), uint(((_S1418)).z))).x * 255.0f));
                uint2 grid_16 = uint2(_S1413, _S1419);
                int2 tile_32 = int2(floor(_S206)) - minimumTile_16;
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
                    int3 _S1420 = int3(int(1), int(0), int(0));
                    _S207 = (((&kernelContext_2)->clearcoatRoughnessTexture_0).read(vec<uint,2>(((_S1420)).xy), uint(((_S1420)).z)));
                    break;
                }
                uint offset_16 = 10U + 2U * (uint(tile_32.y) * _S1413 + uint(tile_32.x));
                uint _S1421 = atlasWidth_16;
                uint _S1422 = offset_16 % atlasWidth_16;
                int _S1423 = int(_S1422);
                uint _S1424 = offset_16 / _S1421;
                int3 _S1425 = int3(_S1423, int(_S1424), int(0));
                uint _S1426 = uint(round((((&kernelContext_2)->clearcoatRoughnessTexture_0).read(vec<uint,2>(((_S1425)).xy), uint(((_S1425)).z))).x * 255.0f));
                uint _S1427 = atlasWidth_16;
                uint _S1428 = offset_16 + 1U;
                uint _S1429 = _S1428 % atlasWidth_16;
                int _S1430 = int(_S1429);
                uint _S1431 = _S1428 / _S1427;
                int3 _S1432 = int3(_S1430, int(_S1431), int(0));
                uint slot_32 = _S1426 | (uint(round((((&kernelContext_2)->clearcoatRoughnessTexture_0).read(vec<uint,2>(((_S1432)).xy), uint(((_S1432)).z))).x * 255.0f)) << 8U);
                if(slot_32 == 0U)
                {
                    int3 _S1433 = int3(int(1), int(0), int(0));
                    _S207 = (((&kernelContext_2)->clearcoatRoughnessTexture_0).read(vec<uint,2>(((_S1433)).xy), uint(((_S1433)).z)));
                    break;
                }
                uint slot_33 = slot_32 - 1U;
                uint _S1434 = atlasWidth_16;
                uint _S1435 = 6U % atlasWidth_16;
                int _S1436 = int(_S1435);
                uint _S1437 = 6U / _S1434;
                int3 _S1438 = int3(_S1436, int(_S1437), int(0));
                uint _S1439 = uint(round((((&kernelContext_2)->clearcoatRoughnessTexture_0).read(vec<uint,2>(((_S1438)).xy), uint(((_S1438)).z))).x * 255.0f));
                uint _S1440 = atlasWidth_16;
                uint _S1441 = 7U % atlasWidth_16;
                int _S1442 = int(_S1441);
                uint _S1443 = 7U / _S1440;
                int3 _S1444 = int3(_S1442, int(_S1443), int(0));
                uint columns_16 = _S1439 | (uint(round((((&kernelContext_2)->clearcoatRoughnessTexture_0).read(vec<uint,2>(((_S1444)).xy), uint(((_S1444)).z))).x * 255.0f)) << 8U);
                uint _S1445 = atlasWidth_16;
                uint _S1446 = 8U % atlasWidth_16;
                int _S1447 = int(_S1446);
                uint _S1448 = 8U / _S1445;
                int3 _S1449 = int3(_S1447, int(_S1448), int(0));
                uint _S1450 = uint(round((((&kernelContext_2)->clearcoatRoughnessTexture_0).read(vec<uint,2>(((_S1449)).xy), uint(((_S1449)).z))).x * 255.0f));
                uint _S1451 = atlasWidth_16;
                uint _S1452 = 9U % atlasWidth_16;
                int _S1453 = int(_S1452);
                uint _S1454 = 9U / _S1451;
                int3 _S1455 = int3(_S1453, int(_S1454), int(0));
                uint rows_16 = ((_S1450 | (uint(round((((&kernelContext_2)->clearcoatRoughnessTexture_0).read(vec<uint,2>(((_S1455)).xy), uint(((_S1455)).z))).x * 255.0f)) << 8U)) + columns_16 - 1U) / columns_16;
                uint metadataRows_16 = (10U + 2U * _S1413 * _S1419 + atlasWidth_16 - 1U) / atlasWidth_16;
                uint _S1456 = atlasWidth_16 / columns_16;
                float _S1457 = float(_S1456);
                uint _S1458 = (atlasHeight_16 - metadataRows_16) / rows_16;
                float2 cellSize_32 = float2(_S1457, float(_S1458));
                uint _S1459 = slot_33 % columns_16;
                float _S1460 = float(_S1459);
                uint _S1461 = slot_33 / columns_16;
                _S207 = (((&kernelContext_2)->clearcoatRoughnessTexture_0).sample(((&kernelContext_2)->clearcoatRoughnessSampler_0), ((float2(_S1460, float(_S1461)) * cellSize_32 + float2(1.5f, float(metadataRows_16) + 1.5f) + fract(_S206) * max(cellSize_32 - float2(3.0f) , float2(0.0f) )) / float2(float(atlasWidth_16), float(atlasHeight_16)))));
                break;
            }
            int2 _S1462 = int2(metadata_16.zw);
            int2 tile_33 = int2(floor(_S206)) - int2(metadata_16.xy);
            if(any(tile_33 < (int2(int(0)) )))
            {
                hasSceneLighting_0 = true;
            }
            else
            {
                hasSceneLighting_0 = any(tile_33 >= _S1462);
            }
            if(hasSceneLighting_0)
            {
                int3 _S1463 = int3(int(min(1U, atlasWidth_16 - 1U)), int(0), int(0));
                _S207 = (((&kernelContext_2)->clearcoatRoughnessTexture_0).read(vec<uint,2>(((_S1463)).xy), uint(((_S1463)).z)));
                break;
            }
            uint _S1464 = atlasWidth_16 / uint(_S1462.x);
            float _S1465 = float(_S1464);
            uint _S1466 = (atlasHeight_16 - 1U) / uint(_S1462.y);
            float2 cellSize_33 = float2(_S1465, float(_S1466));
            _S207 = (((&kernelContext_2)->clearcoatRoughnessTexture_0).sample(((&kernelContext_2)->clearcoatRoughnessSampler_0), ((float2(tile_33) * cellSize_33 + float2(1.5f, 2.5f) + fract(_S206) * max(cellSize_33 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_16), float(atlasHeight_16)))));
            break;
        }
        for(;;)
        {
            float4 _S1467 = float4(_S36->compositeControls_0) ;
            if((_S1467.x) != 1024.0f)
            {
                break;
            }
            bool _S1468 = (_S1467.w) >= 0.5f;
            for(;;)
            {
                if(!_S1468)
                {
                    _S360 = (((&kernelContext_2)->compositeTexture_0).sample(((&kernelContext_2)->compositeSampler_0), (_S206)));
                    break;
                }
                texture2d<float, access::sample> _S1469 = (&kernelContext_2)->compositeTexture_0;
                thread uint atlasWidth_17;
                thread uint atlasHeight_17;
                (*((&atlasWidth_17)) = (_S1469).get_width(0)),(*((&atlasHeight_17)) = (_S1469).get_height(0));
                int3 _S1470 = int3(int(0), int(0), int(0));
                float4 metadata_17 = round((((&kernelContext_2)->compositeTexture_0).read(vec<uint,2>(((_S1470)).xy), uint(((_S1470)).z))) * float4(255.0f) );
                if((metadata_17.x) == 255.0f)
                {
                    uint _S1471 = atlasWidth_17;
                    uint _S1472 = 2U % atlasWidth_17;
                    int _S1473 = int(_S1472);
                    uint _S1474 = 2U / _S1471;
                    int3 _S1475 = int3(_S1473, int(_S1474), int(0));
                    int _S1476 = int(uint(round((((&kernelContext_2)->compositeTexture_0).read(vec<uint,2>(((_S1475)).xy), uint(((_S1475)).z))).x * 255.0f)));
                    uint _S1477 = atlasWidth_17;
                    uint _S1478 = 3U % atlasWidth_17;
                    int _S1479 = int(_S1478);
                    uint _S1480 = 3U / _S1477;
                    int3 _S1481 = int3(_S1479, int(_S1480), int(0));
                    int2 minimumTile_17 = int2(_S1476, int(uint(round((((&kernelContext_2)->compositeTexture_0).read(vec<uint,2>(((_S1481)).xy), uint(((_S1481)).z))).x * 255.0f))));
                    uint _S1482 = atlasWidth_17;
                    uint _S1483 = 4U % atlasWidth_17;
                    int _S1484 = int(_S1483);
                    uint _S1485 = 4U / _S1482;
                    int3 _S1486 = int3(_S1484, int(_S1485), int(0));
                    uint _S1487 = uint(round((((&kernelContext_2)->compositeTexture_0).read(vec<uint,2>(((_S1486)).xy), uint(((_S1486)).z))).x * 255.0f));
                    uint _S1488 = atlasWidth_17;
                    uint _S1489 = 5U % atlasWidth_17;
                    int _S1490 = int(_S1489);
                    uint _S1491 = 5U / _S1488;
                    int3 _S1492 = int3(_S1490, int(_S1491), int(0));
                    uint _S1493 = uint(round((((&kernelContext_2)->compositeTexture_0).read(vec<uint,2>(((_S1492)).xy), uint(((_S1492)).z))).x * 255.0f));
                    uint2 grid_17 = uint2(_S1487, _S1493);
                    int2 tile_34 = int2(floor(_S206)) - minimumTile_17;
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
                        int3 _S1494 = int3(int(1), int(0), int(0));
                        _S360 = (((&kernelContext_2)->compositeTexture_0).read(vec<uint,2>(((_S1494)).xy), uint(((_S1494)).z)));
                        break;
                    }
                    uint offset_17 = 10U + 2U * (uint(tile_34.y) * _S1487 + uint(tile_34.x));
                    uint _S1495 = atlasWidth_17;
                    uint _S1496 = offset_17 % atlasWidth_17;
                    int _S1497 = int(_S1496);
                    uint _S1498 = offset_17 / _S1495;
                    int3 _S1499 = int3(_S1497, int(_S1498), int(0));
                    uint _S1500 = uint(round((((&kernelContext_2)->compositeTexture_0).read(vec<uint,2>(((_S1499)).xy), uint(((_S1499)).z))).x * 255.0f));
                    uint _S1501 = atlasWidth_17;
                    uint _S1502 = offset_17 + 1U;
                    uint _S1503 = _S1502 % atlasWidth_17;
                    int _S1504 = int(_S1503);
                    uint _S1505 = _S1502 / _S1501;
                    int3 _S1506 = int3(_S1504, int(_S1505), int(0));
                    uint slot_34 = _S1500 | (uint(round((((&kernelContext_2)->compositeTexture_0).read(vec<uint,2>(((_S1506)).xy), uint(((_S1506)).z))).x * 255.0f)) << 8U);
                    if(slot_34 == 0U)
                    {
                        int3 _S1507 = int3(int(1), int(0), int(0));
                        _S360 = (((&kernelContext_2)->compositeTexture_0).read(vec<uint,2>(((_S1507)).xy), uint(((_S1507)).z)));
                        break;
                    }
                    uint slot_35 = slot_34 - 1U;
                    uint _S1508 = atlasWidth_17;
                    uint _S1509 = 6U % atlasWidth_17;
                    int _S1510 = int(_S1509);
                    uint _S1511 = 6U / _S1508;
                    int3 _S1512 = int3(_S1510, int(_S1511), int(0));
                    uint _S1513 = uint(round((((&kernelContext_2)->compositeTexture_0).read(vec<uint,2>(((_S1512)).xy), uint(((_S1512)).z))).x * 255.0f));
                    uint _S1514 = atlasWidth_17;
                    uint _S1515 = 7U % atlasWidth_17;
                    int _S1516 = int(_S1515);
                    uint _S1517 = 7U / _S1514;
                    int3 _S1518 = int3(_S1516, int(_S1517), int(0));
                    uint columns_17 = _S1513 | (uint(round((((&kernelContext_2)->compositeTexture_0).read(vec<uint,2>(((_S1518)).xy), uint(((_S1518)).z))).x * 255.0f)) << 8U);
                    uint _S1519 = atlasWidth_17;
                    uint _S1520 = 8U % atlasWidth_17;
                    int _S1521 = int(_S1520);
                    uint _S1522 = 8U / _S1519;
                    int3 _S1523 = int3(_S1521, int(_S1522), int(0));
                    uint _S1524 = uint(round((((&kernelContext_2)->compositeTexture_0).read(vec<uint,2>(((_S1523)).xy), uint(((_S1523)).z))).x * 255.0f));
                    uint _S1525 = atlasWidth_17;
                    uint _S1526 = 9U % atlasWidth_17;
                    int _S1527 = int(_S1526);
                    uint _S1528 = 9U / _S1525;
                    int3 _S1529 = int3(_S1527, int(_S1528), int(0));
                    uint rows_17 = ((_S1524 | (uint(round((((&kernelContext_2)->compositeTexture_0).read(vec<uint,2>(((_S1529)).xy), uint(((_S1529)).z))).x * 255.0f)) << 8U)) + columns_17 - 1U) / columns_17;
                    uint metadataRows_17 = (10U + 2U * _S1487 * _S1493 + atlasWidth_17 - 1U) / atlasWidth_17;
                    uint _S1530 = atlasWidth_17 / columns_17;
                    float _S1531 = float(_S1530);
                    uint _S1532 = (atlasHeight_17 - metadataRows_17) / rows_17;
                    float2 cellSize_34 = float2(_S1531, float(_S1532));
                    uint _S1533 = slot_35 % columns_17;
                    float _S1534 = float(_S1533);
                    uint _S1535 = slot_35 / columns_17;
                    _S360 = (((&kernelContext_2)->compositeTexture_0).sample(((&kernelContext_2)->compositeSampler_0), ((float2(_S1534, float(_S1535)) * cellSize_34 + float2(1.5f, float(metadataRows_17) + 1.5f) + fract(_S206) * max(cellSize_34 - float2(3.0f) , float2(0.0f) )) / float2(float(atlasWidth_17), float(atlasHeight_17)))));
                    break;
                }
                int2 _S1536 = int2(metadata_17.zw);
                int2 tile_35 = int2(floor(_S206)) - int2(metadata_17.xy);
                if(any(tile_35 < (int2(int(0)) )))
                {
                    hasSceneLighting_0 = true;
                }
                else
                {
                    hasSceneLighting_0 = any(tile_35 >= _S1536);
                }
                if(hasSceneLighting_0)
                {
                    int3 _S1537 = int3(int(min(1U, atlasWidth_17 - 1U)), int(0), int(0));
                    _S360 = (((&kernelContext_2)->compositeTexture_0).read(vec<uint,2>(((_S1537)).xy), uint(((_S1537)).z)));
                    break;
                }
                uint _S1538 = atlasWidth_17 / uint(_S1536.x);
                float _S1539 = float(_S1538);
                uint _S1540 = (atlasHeight_17 - 1U) / uint(_S1536.y);
                float2 cellSize_35 = float2(_S1539, float(_S1540));
                _S360 = (((&kernelContext_2)->compositeTexture_0).sample(((&kernelContext_2)->compositeSampler_0), ((float2(tile_35) * cellSize_35 + float2(1.5f, 2.5f) + fract(_S206) * max(cellSize_35 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_17), float(atlasHeight_17)))));
                break;
            }
            uint operation_8 = uint(round(_S1467.y));
            if(operation_8 == 1U)
            {
                _S207 = _S207 * _S360;
                break;
            }
            if(operation_8 == 2U)
            {
                _S207 = _S207 + _S360;
                break;
            }
            if(operation_8 == 3U)
            {
                _S207 = _S207 - _S360;
                break;
            }
            if(operation_8 == 4U)
            {
                float factor_8 = _S1467.z;
                _S207 = _S207 * float4((1.0f - factor_8))  + _S360 * float4(factor_8) ;
                break;
            }
            break;
        }
        clearcoatRoughness_0 = saturate(_S207.x);
    }
    else
    {
        clearcoatRoughness_0 = _S202;
    }
    float ior_0;
    if((textureMask_0 & 2048U) != 0U)
    {
        bool _S1541 = (udimMask_0 & 2048U) != 0U;
        for(;;)
        {
            if(!_S1541)
            {
                _S207 = (((&kernelContext_2)->iorTexture_0).sample(((&kernelContext_2)->iorSampler_0), (_S206)));
                break;
            }
            texture2d<float, access::sample> _S1542 = (&kernelContext_2)->iorTexture_0;
            thread uint atlasWidth_18;
            thread uint atlasHeight_18;
            (*((&atlasWidth_18)) = (_S1542).get_width(0)),(*((&atlasHeight_18)) = (_S1542).get_height(0));
            int3 _S1543 = int3(int(0), int(0), int(0));
            float4 metadata_18 = round((((&kernelContext_2)->iorTexture_0).read(vec<uint,2>(((_S1543)).xy), uint(((_S1543)).z))) * float4(255.0f) );
            if((metadata_18.x) == 255.0f)
            {
                uint _S1544 = atlasWidth_18;
                uint _S1545 = 2U % atlasWidth_18;
                int _S1546 = int(_S1545);
                uint _S1547 = 2U / _S1544;
                int3 _S1548 = int3(_S1546, int(_S1547), int(0));
                int _S1549 = int(uint(round((((&kernelContext_2)->iorTexture_0).read(vec<uint,2>(((_S1548)).xy), uint(((_S1548)).z))).x * 255.0f)));
                uint _S1550 = atlasWidth_18;
                uint _S1551 = 3U % atlasWidth_18;
                int _S1552 = int(_S1551);
                uint _S1553 = 3U / _S1550;
                int3 _S1554 = int3(_S1552, int(_S1553), int(0));
                int2 minimumTile_18 = int2(_S1549, int(uint(round((((&kernelContext_2)->iorTexture_0).read(vec<uint,2>(((_S1554)).xy), uint(((_S1554)).z))).x * 255.0f))));
                uint _S1555 = atlasWidth_18;
                uint _S1556 = 4U % atlasWidth_18;
                int _S1557 = int(_S1556);
                uint _S1558 = 4U / _S1555;
                int3 _S1559 = int3(_S1557, int(_S1558), int(0));
                uint _S1560 = uint(round((((&kernelContext_2)->iorTexture_0).read(vec<uint,2>(((_S1559)).xy), uint(((_S1559)).z))).x * 255.0f));
                uint _S1561 = atlasWidth_18;
                uint _S1562 = 5U % atlasWidth_18;
                int _S1563 = int(_S1562);
                uint _S1564 = 5U / _S1561;
                int3 _S1565 = int3(_S1563, int(_S1564), int(0));
                uint _S1566 = uint(round((((&kernelContext_2)->iorTexture_0).read(vec<uint,2>(((_S1565)).xy), uint(((_S1565)).z))).x * 255.0f));
                uint2 grid_18 = uint2(_S1560, _S1566);
                int2 tile_36 = int2(floor(_S206)) - minimumTile_18;
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
                    int3 _S1567 = int3(int(1), int(0), int(0));
                    _S207 = (((&kernelContext_2)->iorTexture_0).read(vec<uint,2>(((_S1567)).xy), uint(((_S1567)).z)));
                    break;
                }
                uint offset_18 = 10U + 2U * (uint(tile_36.y) * _S1560 + uint(tile_36.x));
                uint _S1568 = atlasWidth_18;
                uint _S1569 = offset_18 % atlasWidth_18;
                int _S1570 = int(_S1569);
                uint _S1571 = offset_18 / _S1568;
                int3 _S1572 = int3(_S1570, int(_S1571), int(0));
                uint _S1573 = uint(round((((&kernelContext_2)->iorTexture_0).read(vec<uint,2>(((_S1572)).xy), uint(((_S1572)).z))).x * 255.0f));
                uint _S1574 = atlasWidth_18;
                uint _S1575 = offset_18 + 1U;
                uint _S1576 = _S1575 % atlasWidth_18;
                int _S1577 = int(_S1576);
                uint _S1578 = _S1575 / _S1574;
                int3 _S1579 = int3(_S1577, int(_S1578), int(0));
                uint slot_36 = _S1573 | (uint(round((((&kernelContext_2)->iorTexture_0).read(vec<uint,2>(((_S1579)).xy), uint(((_S1579)).z))).x * 255.0f)) << 8U);
                if(slot_36 == 0U)
                {
                    int3 _S1580 = int3(int(1), int(0), int(0));
                    _S207 = (((&kernelContext_2)->iorTexture_0).read(vec<uint,2>(((_S1580)).xy), uint(((_S1580)).z)));
                    break;
                }
                uint slot_37 = slot_36 - 1U;
                uint _S1581 = atlasWidth_18;
                uint _S1582 = 6U % atlasWidth_18;
                int _S1583 = int(_S1582);
                uint _S1584 = 6U / _S1581;
                int3 _S1585 = int3(_S1583, int(_S1584), int(0));
                uint _S1586 = uint(round((((&kernelContext_2)->iorTexture_0).read(vec<uint,2>(((_S1585)).xy), uint(((_S1585)).z))).x * 255.0f));
                uint _S1587 = atlasWidth_18;
                uint _S1588 = 7U % atlasWidth_18;
                int _S1589 = int(_S1588);
                uint _S1590 = 7U / _S1587;
                int3 _S1591 = int3(_S1589, int(_S1590), int(0));
                uint columns_18 = _S1586 | (uint(round((((&kernelContext_2)->iorTexture_0).read(vec<uint,2>(((_S1591)).xy), uint(((_S1591)).z))).x * 255.0f)) << 8U);
                uint _S1592 = atlasWidth_18;
                uint _S1593 = 8U % atlasWidth_18;
                int _S1594 = int(_S1593);
                uint _S1595 = 8U / _S1592;
                int3 _S1596 = int3(_S1594, int(_S1595), int(0));
                uint _S1597 = uint(round((((&kernelContext_2)->iorTexture_0).read(vec<uint,2>(((_S1596)).xy), uint(((_S1596)).z))).x * 255.0f));
                uint _S1598 = atlasWidth_18;
                uint _S1599 = 9U % atlasWidth_18;
                int _S1600 = int(_S1599);
                uint _S1601 = 9U / _S1598;
                int3 _S1602 = int3(_S1600, int(_S1601), int(0));
                uint rows_18 = ((_S1597 | (uint(round((((&kernelContext_2)->iorTexture_0).read(vec<uint,2>(((_S1602)).xy), uint(((_S1602)).z))).x * 255.0f)) << 8U)) + columns_18 - 1U) / columns_18;
                uint metadataRows_18 = (10U + 2U * _S1560 * _S1566 + atlasWidth_18 - 1U) / atlasWidth_18;
                uint _S1603 = atlasWidth_18 / columns_18;
                float _S1604 = float(_S1603);
                uint _S1605 = (atlasHeight_18 - metadataRows_18) / rows_18;
                float2 cellSize_36 = float2(_S1604, float(_S1605));
                uint _S1606 = slot_37 % columns_18;
                float _S1607 = float(_S1606);
                uint _S1608 = slot_37 / columns_18;
                _S207 = (((&kernelContext_2)->iorTexture_0).sample(((&kernelContext_2)->iorSampler_0), ((float2(_S1607, float(_S1608)) * cellSize_36 + float2(1.5f, float(metadataRows_18) + 1.5f) + fract(_S206) * max(cellSize_36 - float2(3.0f) , float2(0.0f) )) / float2(float(atlasWidth_18), float(atlasHeight_18)))));
                break;
            }
            int2 _S1609 = int2(metadata_18.zw);
            int2 tile_37 = int2(floor(_S206)) - int2(metadata_18.xy);
            if(any(tile_37 < (int2(int(0)) )))
            {
                hasSceneLighting_0 = true;
            }
            else
            {
                hasSceneLighting_0 = any(tile_37 >= _S1609);
            }
            if(hasSceneLighting_0)
            {
                int3 _S1610 = int3(int(min(1U, atlasWidth_18 - 1U)), int(0), int(0));
                _S207 = (((&kernelContext_2)->iorTexture_0).read(vec<uint,2>(((_S1610)).xy), uint(((_S1610)).z)));
                break;
            }
            uint _S1611 = atlasWidth_18 / uint(_S1609.x);
            float _S1612 = float(_S1611);
            uint _S1613 = (atlasHeight_18 - 1U) / uint(_S1609.y);
            float2 cellSize_37 = float2(_S1612, float(_S1613));
            _S207 = (((&kernelContext_2)->iorTexture_0).sample(((&kernelContext_2)->iorSampler_0), ((float2(tile_37) * cellSize_37 + float2(1.5f, 2.5f) + fract(_S206) * max(cellSize_37 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_18), float(atlasHeight_18)))));
            break;
        }
        for(;;)
        {
            float4 _S1614 = float4(_S36->compositeControls_0) ;
            if((_S1614.x) != 2048.0f)
            {
                break;
            }
            bool _S1615 = (_S1614.w) >= 0.5f;
            for(;;)
            {
                if(!_S1615)
                {
                    _S360 = (((&kernelContext_2)->compositeTexture_0).sample(((&kernelContext_2)->compositeSampler_0), (_S206)));
                    break;
                }
                texture2d<float, access::sample> _S1616 = (&kernelContext_2)->compositeTexture_0;
                thread uint atlasWidth_19;
                thread uint atlasHeight_19;
                (*((&atlasWidth_19)) = (_S1616).get_width(0)),(*((&atlasHeight_19)) = (_S1616).get_height(0));
                int3 _S1617 = int3(int(0), int(0), int(0));
                float4 metadata_19 = round((((&kernelContext_2)->compositeTexture_0).read(vec<uint,2>(((_S1617)).xy), uint(((_S1617)).z))) * float4(255.0f) );
                if((metadata_19.x) == 255.0f)
                {
                    uint _S1618 = atlasWidth_19;
                    uint _S1619 = 2U % atlasWidth_19;
                    int _S1620 = int(_S1619);
                    uint _S1621 = 2U / _S1618;
                    int3 _S1622 = int3(_S1620, int(_S1621), int(0));
                    int _S1623 = int(uint(round((((&kernelContext_2)->compositeTexture_0).read(vec<uint,2>(((_S1622)).xy), uint(((_S1622)).z))).x * 255.0f)));
                    uint _S1624 = atlasWidth_19;
                    uint _S1625 = 3U % atlasWidth_19;
                    int _S1626 = int(_S1625);
                    uint _S1627 = 3U / _S1624;
                    int3 _S1628 = int3(_S1626, int(_S1627), int(0));
                    int2 minimumTile_19 = int2(_S1623, int(uint(round((((&kernelContext_2)->compositeTexture_0).read(vec<uint,2>(((_S1628)).xy), uint(((_S1628)).z))).x * 255.0f))));
                    uint _S1629 = atlasWidth_19;
                    uint _S1630 = 4U % atlasWidth_19;
                    int _S1631 = int(_S1630);
                    uint _S1632 = 4U / _S1629;
                    int3 _S1633 = int3(_S1631, int(_S1632), int(0));
                    uint _S1634 = uint(round((((&kernelContext_2)->compositeTexture_0).read(vec<uint,2>(((_S1633)).xy), uint(((_S1633)).z))).x * 255.0f));
                    uint _S1635 = atlasWidth_19;
                    uint _S1636 = 5U % atlasWidth_19;
                    int _S1637 = int(_S1636);
                    uint _S1638 = 5U / _S1635;
                    int3 _S1639 = int3(_S1637, int(_S1638), int(0));
                    uint _S1640 = uint(round((((&kernelContext_2)->compositeTexture_0).read(vec<uint,2>(((_S1639)).xy), uint(((_S1639)).z))).x * 255.0f));
                    uint2 grid_19 = uint2(_S1634, _S1640);
                    int2 tile_38 = int2(floor(_S206)) - minimumTile_19;
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
                        int3 _S1641 = int3(int(1), int(0), int(0));
                        _S360 = (((&kernelContext_2)->compositeTexture_0).read(vec<uint,2>(((_S1641)).xy), uint(((_S1641)).z)));
                        break;
                    }
                    uint offset_19 = 10U + 2U * (uint(tile_38.y) * _S1634 + uint(tile_38.x));
                    uint _S1642 = atlasWidth_19;
                    uint _S1643 = offset_19 % atlasWidth_19;
                    int _S1644 = int(_S1643);
                    uint _S1645 = offset_19 / _S1642;
                    int3 _S1646 = int3(_S1644, int(_S1645), int(0));
                    uint _S1647 = uint(round((((&kernelContext_2)->compositeTexture_0).read(vec<uint,2>(((_S1646)).xy), uint(((_S1646)).z))).x * 255.0f));
                    uint _S1648 = atlasWidth_19;
                    uint _S1649 = offset_19 + 1U;
                    uint _S1650 = _S1649 % atlasWidth_19;
                    int _S1651 = int(_S1650);
                    uint _S1652 = _S1649 / _S1648;
                    int3 _S1653 = int3(_S1651, int(_S1652), int(0));
                    uint slot_38 = _S1647 | (uint(round((((&kernelContext_2)->compositeTexture_0).read(vec<uint,2>(((_S1653)).xy), uint(((_S1653)).z))).x * 255.0f)) << 8U);
                    if(slot_38 == 0U)
                    {
                        int3 _S1654 = int3(int(1), int(0), int(0));
                        _S360 = (((&kernelContext_2)->compositeTexture_0).read(vec<uint,2>(((_S1654)).xy), uint(((_S1654)).z)));
                        break;
                    }
                    uint slot_39 = slot_38 - 1U;
                    uint _S1655 = atlasWidth_19;
                    uint _S1656 = 6U % atlasWidth_19;
                    int _S1657 = int(_S1656);
                    uint _S1658 = 6U / _S1655;
                    int3 _S1659 = int3(_S1657, int(_S1658), int(0));
                    uint _S1660 = uint(round((((&kernelContext_2)->compositeTexture_0).read(vec<uint,2>(((_S1659)).xy), uint(((_S1659)).z))).x * 255.0f));
                    uint _S1661 = atlasWidth_19;
                    uint _S1662 = 7U % atlasWidth_19;
                    int _S1663 = int(_S1662);
                    uint _S1664 = 7U / _S1661;
                    int3 _S1665 = int3(_S1663, int(_S1664), int(0));
                    uint columns_19 = _S1660 | (uint(round((((&kernelContext_2)->compositeTexture_0).read(vec<uint,2>(((_S1665)).xy), uint(((_S1665)).z))).x * 255.0f)) << 8U);
                    uint _S1666 = atlasWidth_19;
                    uint _S1667 = 8U % atlasWidth_19;
                    int _S1668 = int(_S1667);
                    uint _S1669 = 8U / _S1666;
                    int3 _S1670 = int3(_S1668, int(_S1669), int(0));
                    uint _S1671 = uint(round((((&kernelContext_2)->compositeTexture_0).read(vec<uint,2>(((_S1670)).xy), uint(((_S1670)).z))).x * 255.0f));
                    uint _S1672 = atlasWidth_19;
                    uint _S1673 = 9U % atlasWidth_19;
                    int _S1674 = int(_S1673);
                    uint _S1675 = 9U / _S1672;
                    int3 _S1676 = int3(_S1674, int(_S1675), int(0));
                    uint rows_19 = ((_S1671 | (uint(round((((&kernelContext_2)->compositeTexture_0).read(vec<uint,2>(((_S1676)).xy), uint(((_S1676)).z))).x * 255.0f)) << 8U)) + columns_19 - 1U) / columns_19;
                    uint metadataRows_19 = (10U + 2U * _S1634 * _S1640 + atlasWidth_19 - 1U) / atlasWidth_19;
                    uint _S1677 = atlasWidth_19 / columns_19;
                    float _S1678 = float(_S1677);
                    uint _S1679 = (atlasHeight_19 - metadataRows_19) / rows_19;
                    float2 cellSize_38 = float2(_S1678, float(_S1679));
                    uint _S1680 = slot_39 % columns_19;
                    float _S1681 = float(_S1680);
                    uint _S1682 = slot_39 / columns_19;
                    _S360 = (((&kernelContext_2)->compositeTexture_0).sample(((&kernelContext_2)->compositeSampler_0), ((float2(_S1681, float(_S1682)) * cellSize_38 + float2(1.5f, float(metadataRows_19) + 1.5f) + fract(_S206) * max(cellSize_38 - float2(3.0f) , float2(0.0f) )) / float2(float(atlasWidth_19), float(atlasHeight_19)))));
                    break;
                }
                int2 _S1683 = int2(metadata_19.zw);
                int2 tile_39 = int2(floor(_S206)) - int2(metadata_19.xy);
                if(any(tile_39 < (int2(int(0)) )))
                {
                    hasSceneLighting_0 = true;
                }
                else
                {
                    hasSceneLighting_0 = any(tile_39 >= _S1683);
                }
                if(hasSceneLighting_0)
                {
                    int3 _S1684 = int3(int(min(1U, atlasWidth_19 - 1U)), int(0), int(0));
                    _S360 = (((&kernelContext_2)->compositeTexture_0).read(vec<uint,2>(((_S1684)).xy), uint(((_S1684)).z)));
                    break;
                }
                uint _S1685 = atlasWidth_19 / uint(_S1683.x);
                float _S1686 = float(_S1685);
                uint _S1687 = (atlasHeight_19 - 1U) / uint(_S1683.y);
                float2 cellSize_39 = float2(_S1686, float(_S1687));
                _S360 = (((&kernelContext_2)->compositeTexture_0).sample(((&kernelContext_2)->compositeSampler_0), ((float2(tile_39) * cellSize_39 + float2(1.5f, 2.5f) + fract(_S206) * max(cellSize_39 - float2(2.0f)  - float2(1.0f) , float2(0.0f) )) / float2(float(atlasWidth_19), float(atlasHeight_19)))));
                break;
            }
            uint operation_9 = uint(round(_S1614.y));
            if(operation_9 == 1U)
            {
                _S207 = _S207 * _S360;
                break;
            }
            if(operation_9 == 2U)
            {
                _S207 = _S207 + _S360;
                break;
            }
            if(operation_9 == 3U)
            {
                _S207 = _S207 - _S360;
                break;
            }
            if(operation_9 == 4U)
            {
                float factor_9 = _S1614.z;
                _S207 = _S207 * float4((1.0f - factor_9))  + _S360 * float4(factor_9) ;
                break;
            }
            break;
        }
        ior_0 = _S207.x;
    }
    else
    {
        ior_0 = _S200;
    }
    float opacityThreshold_0 = _S195.z;
    if(opacityThreshold_0 > 0.0f)
    {
        if(opacity_0 < opacityThreshold_0)
        {
            discard_fragment();
        }
        opacity_0 = 1.0f;
    }
    float3 normal_1 = normalize(_S31.normal_0);
    float3 worldNormal_1 = normalize(_S31.worldNormal_0);
    float lengthSquared_0 = dot(_S31.eyePosition_0, _S31.eyePosition_0);
    float3 diffuseFresnel_0;
    if(lengthSquared_0 > 0.00100000004749745f)
    {
        diffuseFresnel_0 = - _S31.eyePosition_0 * float3(rsqrt(lengthSquared_0)) ;
    }
    else
    {
        diffuseFresnel_0 = float3(0.0f, 0.0f, 1.0f);
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
    float _S1688 = saturate(abs(dot(normal_2, diffuseFresnel_0)) + 0.00000999999974738f);
    float _S1689 = max(0.00100000004749745f, roughness_0);
    float _S1690 = max(0.00100000004749745f, clearcoatRoughness_0);
    float reflectanceRatio_0 = (1.0f - ior_0) / (1.0f + ior_0);
    float3 _S1691 = float3(3.14159274101257324f) ;
    float3 diffuse_0 = diffuseColor_0 / _S1691;
    bool omniPbrSpecular_0 = (_S203.w) >= 0.5f;
    float dielectricLevel_0 = _S189.w;
    float3 normalIncidence_0;
    float3 grazingIncidence_0;
    float3 diffuse_1;
    if(omniPbrSpecular_0)
    {
        float3 _S1692 = float3(dielectricLevel_0) ;
        float3 _S1693 = float3(metallic_0) ;
        float3 _S1694 = mix(_S1692, diffuseColor_0, _S1693);
        float3 diffuse_2 = diffuse_0 * float3((1.0f - metallic_0)) ;
        normalIncidence_0 = mix(float3(0.07999999821186066f, 0.07999999821186066f, 0.07999999821186066f) * _S1692, diffuseColor_0, _S1693);
        grazingIncidence_0 = _S1694;
        diffuse_1 = diffuse_2;
    }
    else
    {
        if((_S195.w) >= 0.5f)
        {
            float3 _S1695 = float3(1.0f, 1.0f, 1.0f);
            normalIncidence_0 = specularColor_0;
            grazingIncidence_0 = _S1695;
            diffuse_1 = diffuse_0;
        }
        else
        {
            float3 _S1696 = float3(metallic_0) ;
            float3 specularTint_0 = mix(float3(1.0f, 1.0f, 1.0f), diffuseColor_0, _S1696);
            float3 diffuse_3 = diffuse_0 * float3((1.0f - metallic_0)) ;
            normalIncidence_0 = mix(float3((reflectanceRatio_0 * reflectanceRatio_0))  * specularTint_0, specularTint_0, _S1696);
            grazingIncidence_0 = specularTint_0;
            diffuse_1 = diffuse_3;
        }
    }
    FragmentFrameParameters_0 _S1697 = _S32;
    float _S1698 = _S32.ambientLight_0.w;
    if(_S1698 > 0.5f)
    {
        hasSceneLighting_0 = true;
    }
    else
    {
        float3 _S1699 = _S1697.ambientLight_0.xyz;
        hasSceneLighting_0 = (dot(_S1699, _S1699)) > 0.0f;
    }
    if(hasSceneLighting_0)
    {
        hasSceneLighting_0 = true;
    }
    else
    {
        hasSceneLighting_0 = (_S32.environmentControls_0.x) >= 0.5f;
    }
    if(hasSceneLighting_0)
    {
        hasSceneLighting_0 = true;
    }
    else
    {
        hasSceneLighting_0 = (_S32.environmentControls_0.w) >= 0.5f;
    }
    uint _S1700 = min(uint(_S1698), 128U);
    float3 sceneEye_0 = normalize((((float4(diffuseFresnel_0, 0.0f)) * (_S32.eyeToWorld_0))).xyz);
    float3 worldPosition_0 = (((float4(_S31.eyePosition_0, 1.0f)) * (_S32.eyeToWorld_0))).xyz;
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
    float4 _S1701 = float4(_S36->lightColorAmbient_0) ;
    float3 color_1 = diffuseColor_0 * float3(_S1701.w) ;
    uint domeLinkMask_0 = uint(max((float4(_S36->domeLinkControls_0) ).x, 0.0f));
    FragmentFrameParameters_0 _S1702 = _S32;
    uint _S1703 = min(uint(max(_S32.domeControls_0.x, 0.0f)), 8U);
    uint allDomes_0 = (1U << _S1703) - 1U;
    bool allDomesLinked_0 = (domeLinkMask_0 & allDomes_0) == allDomes_0;
    float3 _S1704 = _S1697.ambientLight_0.xyz;
    uint lightCount_0;
    float3 domeAmbient_1;
    if(!allDomesLinked_0)
    {
        float3 _S1705 = float3(0.0f, 0.0f, 0.0f);
        lightCount_0 = 0U;
        domeAmbient_1 = _S1705;
        for(;;)
        {
            if(lightCount_0 < _S1703)
            {
            }
            else
            {
                break;
            }
            if((domeLinkMask_0 & (1U << lightCount_0)) != 0U)
            {
                domeAmbient_1 = domeAmbient_1 + _S32.domeAmbient_0[lightCount_0].xyz;
            }
            lightCount_0 = lightCount_0 + 1U;
        }
    }
    else
    {
        domeAmbient_1 = _S1704;
    }
    float3 color_2 = color_1 + diffuseColor_0 * domeAmbient_1;
    bool _S1706 = !hasSceneLighting_0;
    if(_S1706)
    {
        lightCount_0 = 1U;
    }
    else
    {
        lightCount_0 = _S1700;
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
        bool _S1707;
        if(hasSceneLighting_0)
        {
            _S1707 = (((uint4(_S36->lightLinkMask_0) )[lightIndex_0 / 32U]) & (1U << (lightIndex_0 % 32U))) == 0U;
        }
        else
        {
            _S1707 = false;
        }
        if(_S1707)
        {
            lightIndex_0 = lightIndex_0 + 1U;
            continue;
        }
        bool _S1708 = lightIndex_0 == 0U;
        bool _S1709;
        if(_S1708)
        {
            _S1709 = _S1706;
        }
        else
        {
            _S1709 = false;
        }
        float lightType_0;
        if(_S1709)
        {
            lightType_0 = 1.0f;
        }
        else
        {
            float4 _S1710 = ReadFrameLight_0(0U, lightIndex_0, &kernelContext_2);
            lightType_0 = _S1710.w;
        }
        bool _S1711;
        if(_S1708)
        {
            _S1711 = _S1706;
        }
        else
        {
            _S1711 = false;
        }
        if(_S1711)
        {
            lightDirection_0 = normalize((float4(_S36->lightDirectionIntensity_0) ).xyz);
        }
        else
        {
            float4 _S1712 = ReadFrameLight_0(1U, lightIndex_0, &kernelContext_2);
            lightDirection_0 = normalize(_S1712.xyz);
        }
        bool _S1713;
        if(_S1708)
        {
            _S1713 = _S1706;
        }
        else
        {
            _S1713 = false;
        }
        if(_S1713)
        {
            roughness_0 = (float4(_S36->lightDirectionIntensity_0) ).w;
        }
        else
        {
            float4 _S1714 = ReadFrameLight_0(2U, lightIndex_0, &kernelContext_2);
            roughness_0 = _S1714.w;
        }
        bool _S1715;
        if(_S1708)
        {
            _S1715 = _S1706;
        }
        else
        {
            _S1715 = false;
        }
        if(_S1715)
        {
            diffuseColor_0 = _S1701.xyz;
        }
        else
        {
            float4 _S1716 = ReadFrameLight_0(2U, lightIndex_0, &kernelContext_2);
            diffuseColor_0 = _S1716.xyz;
        }
        bool _S1717;
        if(_S1708)
        {
            _S1717 = _S1706;
        }
        else
        {
            _S1717 = false;
        }
        if(_S1717)
        {
            metallic_0 = 1.0f;
        }
        else
        {
            float4 _S1718 = ReadFrameLight_0(3U, lightIndex_0, &kernelContext_2);
            metallic_0 = _S1718.x;
        }
        bool _S1719;
        if(_S1708)
        {
            _S1719 = _S1706;
        }
        else
        {
            _S1719 = false;
        }
        if(_S1719)
        {
            clearcoatRoughness_0 = 1.0f;
        }
        else
        {
            float4 _S1720 = ReadFrameLight_0(3U, lightIndex_0, &kernelContext_2);
            clearcoatRoughness_0 = _S1720.y;
        }
        bool _S1721;
        if(_S1708)
        {
            _S1721 = _S1706;
        }
        else
        {
            _S1721 = false;
        }
        if(_S1721)
        {
            lightTangent_0 = float3(1.0f, 0.0f, 0.0f);
        }
        else
        {
            float4 _S1722 = ReadFrameLight_0(4U, lightIndex_0, &kernelContext_2);
            lightTangent_0 = normalize(_S1722.xyz);
        }
        bool _S1723;
        if(_S1708)
        {
            _S1723 = _S1706;
        }
        else
        {
            _S1723 = false;
        }
        if(_S1723)
        {
            lightBitangent_0 = float3(0.0f, 1.0f, 0.0f);
        }
        else
        {
            float4 _S1724 = ReadFrameLight_0(5U, lightIndex_0, &kernelContext_2);
            lightBitangent_0 = normalize(_S1724.xyz);
        }
        bool _S1725;
        if(_S1708)
        {
            _S1725 = _S1706;
        }
        else
        {
            _S1725 = false;
        }
        float shapeX_0;
        if(_S1725)
        {
            shapeX_0 = 0.0f;
        }
        else
        {
            float4 _S1726 = ReadFrameLight_0(4U, lightIndex_0, &kernelContext_2);
            shapeX_0 = _S1726.w;
        }
        bool _S1727;
        if(_S1708)
        {
            _S1727 = _S1706;
        }
        else
        {
            _S1727 = false;
        }
        float shapeY_0;
        if(_S1727)
        {
            shapeY_0 = 0.0f;
        }
        else
        {
            float4 _S1728 = ReadFrameLight_0(5U, lightIndex_0, &kernelContext_2);
            shapeY_0 = _S1728.w;
        }
        bool _S1729;
        if(_S1708)
        {
            _S1729 = _S1706;
        }
        else
        {
            _S1729 = false;
        }
        float lightRadius_0;
        if(_S1729)
        {
            lightRadius_0 = 0.0f;
        }
        else
        {
            float4 _S1730 = ReadFrameLight_0(1U, lightIndex_0, &kernelContext_2);
            lightRadius_0 = _S1730.w;
        }
        bool _S1731;
        if(_S1708)
        {
            _S1731 = _S1706;
        }
        else
        {
            _S1731 = false;
        }
        if(_S1731)
        {
            domeAmbient_1 = diffuseFresnel_0;
        }
        else
        {
            domeAmbient_1 = sceneEye_0;
        }
        float shadowVisibility_0;
        if(hasSceneLighting_0)
        {
            float4 _S1732 = ReadFrameShadowSlot_0(lightIndex_0, &kernelContext_2);
            int shadowSlot_0 = int(_S1732.x);
            if(shadowSlot_0 >= int(0))
            {
                for(;;)
                {
                    FragmentFrameParameters_0 _S1733 = _S32;
                    FragmentFrameParameters_0 _S1734 = _S32;
                    if((dot(specularColor_0, lightDirection_0)) < 0.0f)
                    {
                        color_0 = - specularColor_0;
                    }
                    else
                    {
                        color_0 = specularColor_0;
                    }
                    float slope_0 = clamp(1.0f - saturate(dot(color_0, lightDirection_0)), 0.0f, 1.0f);
                    float4 lightClip_0 = (((float4(worldPosition_0 + color_0 * float3((_S1733.shadowControls_0[shadowSlot_0].y * slope_0)) , 1.0f)) * (_S32.shadowWorldToLightClip_0[shadowSlot_0])));
                    float _S1735 = lightClip_0.w;
                    if(_S1735 <= 0.0f)
                    {
                        ior_0 = 1.0f;
                        break;
                    }
                    float3 ndc_0 = lightClip_0.xyz / float3(_S1735) ;
                    bool _S1736;
                    if((abs(ndc_0.x)) > 1.0f)
                    {
                        _S1736 = true;
                    }
                    else
                    {
                        _S1736 = (abs(ndc_0.y)) > 1.0f;
                    }
                    bool _S1737;
                    if(_S1736)
                    {
                        _S1737 = true;
                    }
                    else
                    {
                        _S1737 = (ndc_0.z) < 0.0f;
                    }
                    bool _S1738;
                    if(_S1737)
                    {
                        _S1738 = true;
                    }
                    else
                    {
                        _S1738 = (ndc_0.z) > 1.0f;
                    }
                    if(_S1738)
                    {
                        ior_0 = 1.0f;
                        break;
                    }
                    float2 _S1739 = _S1734.shadowTile_0[shadowSlot_0].xy;
                    float2 _S1740 = _S1734.shadowTile_0[shadowSlot_0].zw;
                    float2 _S1741 = _S1739 + (ndc_0.xy * float2(0.5f, -0.5f) + float2(0.5f, 0.5f)) * _S1740;
                    float texel_0 = _S1733.shadowControls_0[shadowSlot_0].w;
                    float _S1742 = max(_S1733.shadowControls_0[shadowSlot_0].z, 0.0f);
                    float _S1743 = ndc_0.z - _S1733.shadowControls_0[shadowSlot_0].x * (1.0f + 2.0f * slope_0);
                    float2 _S1744 = float2((texel_0 * 0.5f)) ;
                    float2 _S1745 = _S1739 + _S1744;
                    float2 _S1746 = _S1739 + _S1740 - _S1744;
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
                            if(_S1743 <= (((&kernelContext_2)->shadowAtlas_0).sample(((&kernelContext_2)->shadowSampler_0), (clamp(_S1741 + float2(float(x_0), float(y_0)) * float2((_S1742 * texel_0)) , _S1745, _S1746)), level((0.0f))).x))
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
        float3 _S1747 = float3(0.0f, 0.0f, 0.0f);
        sampleOffsets_0[int(0)] = _S1747;
        sampleOffsets_0[int(1)] = _S1747;
        sampleOffsets_0[int(2)] = _S1747;
        sampleOffsets_0[int(3)] = _S1747;
        sampleOffsets_0[int(4)] = _S1747;
        float sampleCount_0;
        if(lightType_0 == 3.0f)
        {
            float3 halfWidth_0 = lightTangent_0 * float3((shapeX_0 * 0.5f)) ;
            float3 halfHeight_0 = lightBitangent_0 * float3((shapeY_0 * 0.5f)) ;
            sampleOffsets_0[int(1)] = halfWidth_0 + halfHeight_0;
            sampleOffsets_0[int(2)] = halfWidth_0 - halfHeight_0;
            float3 _S1748 = - halfWidth_0;
            sampleOffsets_0[int(3)] = _S1748 + halfHeight_0;
            sampleOffsets_0[int(4)] = _S1748 - halfHeight_0;
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
            float3 sampleDirection_0;
            float emissionScale_0;
            float sampleIntensity_1;
            if(lightType_0 >= 2.0f)
            {
                float4 _S1749 = ReadFrameLight_0(0U, lightIndex_0, &kernelContext_2);
                float3 toLight_0 = _S1749.xyz + sampleOffsets_0[sampleIndex_0] - worldPosition_0;
                float _S1750 = max(dot(toLight_0, toLight_0), 0.00100000004749745f);
                float3 sampleDirection_1 = toLight_0 * float3(rsqrt(_S1750)) ;
                float sampleIntensity_2 = sampleIntensity_0 / _S1750;
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
            float _S1751 = pow(max(0.0f, 1.0f - saturate(dot(domeAmbient_1, half_0))), 5.0f);
            float3 diffuseFresnel_1;
            if(omniPbrSpecular_0)
            {
                diffuseFresnel_1 = float3(dielectricLevel_0)  * float3(mix(0.07999999821186066f, 1.0f, _S1751)) ;
            }
            else
            {
                diffuseFresnel_1 = mix(normalIncidence_0, grazingIncidence_0, float3(_S1751) );
            }
            float3 directDiffuse_0 = diffuse_1 * (float3(1.0f)  - diffuseFresnel_1);
            float3 _S1752 = float3(_S1751) ;
            float _S1753 = max(_S1689, 0.00100000004749745f);
            float alpha_0 = _S1753 * _S1753;
            float alphaSquared_0 = alpha_0 * alpha_0;
            float lobeCosineSquared_0 = saturate(normalDotHalf_0 * normalDotHalf_0);
            float lobeComplement_0 = 1.0f - lobeCosineSquared_0;
            float denominator_0 = lobeCosineSquared_0 * alphaSquared_0 + lobeComplement_0;
            float k_0 = alpha_0 * 0.5f;
            float _S1754 = 1.0f - k_0;
            float3 _S1755 = float3(max(4.0f * normalDotLight_0 * _S1688, 1.00000000317107685e-30f)) ;
            float3 _S1756 = mix(normalIncidence_0, grazingIncidence_0, _S1752) * float3((_S1688 / (_S1688 * _S1754 + k_0) * (normalDotLight_0 / (normalDotLight_0 * _S1754 + k_0))))  * float3((alphaSquared_0 / max(3.14159274101257324f * denominator_0 * denominator_0, 1.00000000317107685e-30f)))  / _S1755;
            float3 directSpecular_0;
            if(clearcoatAmount_0 > 0.0f)
            {
                float _S1757 = max(_S1690, 0.00100000004749745f);
                float alpha_1 = _S1757 * _S1757;
                float alphaSquared_1 = alpha_1 * alpha_1;
                float denominator_1 = lobeCosineSquared_0 * alphaSquared_1 + lobeComplement_0;
                float k_1 = alpha_1 * 0.5f;
                float _S1758 = 1.0f - k_1;
                directSpecular_0 = _S1756 + float3(clearcoatAmount_0)  * (mix(float3((reflectanceRatio_0 * reflectanceRatio_0)) , float3(1.0f, 1.0f, 1.0f), _S1752) * float3((_S1688 / (_S1688 * _S1758 + k_1) * (normalDotLight_0 / (normalDotLight_0 * _S1758 + k_1))))  * float3((alphaSquared_1 / max(3.14159274101257324f * denominator_1 * denominator_1, 1.00000000317107685e-30f)))  / _S1755);
            }
            else
            {
                directSpecular_0 = _S1756;
            }
            float3 _S1759 = diffuseColor_0 * float3(sampleIntensity_1) ;
            color_0 = color_0 + float3((shadowVisibility_0 * occlusion_0 * emissionScale_0 * normalDotLight_0))  * (directDiffuse_0 * float3(metallic_0)  * (_S1759 * _S1691) + directSpecular_0 * float3(clearcoatRoughness_0)  * _S1759);
            sampleIndex_0 = sampleIndex_0 + 1U;
        }
        color_3 = color_0;
        lightIndex_0 = lightIndex_0 + 1U;
    }
    FragmentFrameParameters_0 _S1760 = _S32;
    if((_S32.environmentControls_0.x) >= 0.5f)
    {
        float _S1761 = saturate(saturate(abs(dot(worldNormal_2, sceneEye_0)) + 0.00000999999974738f));
        float _S1762 = saturate(_S1689);
        float2 _S1763 = (((&kernelContext_2)->environmentBrdf_0).sample(((&kernelContext_2)->environmentBrdfSampler_0), (float2(_S1761, _S1762)), level((0.0f)))).xy;
        float _S1764 = _S1763.x;
        float _S1765 = _S1763.y;
        float3 specularWeight_0 = normalIncidence_0 * float3(_S1764)  + grazingIncidence_0 * float3(_S1765) ;
        if(omniPbrSpecular_0)
        {
            diffuseFresnel_0 = float3(dielectricLevel_0)  * float3((0.07999999821186066f * _S1764 + _S1765)) ;
        }
        else
        {
            diffuseFresnel_0 = specularWeight_0;
        }
        float3 diffuseWeight_0 = saturate(float3(1.0f, 1.0f, 1.0f) - diffuseFresnel_0);
        float3 reflectionDirection_0 = reflect(- sceneEye_0, worldNormal_2);
        float _S1766 = max(_S1702.domeControls_0.y, 1.0f);
        float3 _S1767 = float3(0.0f, 0.0f, 0.0f);
        if(allDomesLinked_0)
        {
            hasSceneLighting_0 = true;
        }
        else
        {
            hasSceneLighting_0 = _S1766 <= 1.0f;
        }
        if(hasSceneLighting_0)
        {
            float composedGroup_0 = _S1702.domeControls_0.z;
            float _S1768 = _S1702.domeControls_0.w;
            for(;;)
            {
                bool _S1769 = _S1766 <= 1.0f;
                _S34 = _S1769;
                if(_S1769)
                {
                    float3 unit_0 = normalize(worldNormal_2);
                    diffuseColor_0 = (((&kernelContext_2)->environmentIrradiance_0).sample(((&kernelContext_2)->environmentSampler_0), (float2(fract((atan2(unit_0.z, unit_0.x) + 1.57079637050628662f) / 6.28318548202514648f), acos(clamp(unit_0.y, -1.0f, 1.0f)) / 3.14159274101257324f)), level((0.0f)))).xyz;
                    break;
                }
                float3 unit_1 = normalize(worldNormal_2);
                float inset_0 = 0.5f / max(_S1768, 1.0f);
                diffuseColor_0 = (((&kernelContext_2)->environmentIrradiance_0).sample(((&kernelContext_2)->environmentSampler_0), (float2(fract((atan2(unit_1.z, unit_1.x) + 1.57079637050628662f) / 6.28318548202514648f), (composedGroup_0 + clamp(acos(clamp(unit_1.y, -1.0f, 1.0f)) / 3.14159274101257324f, inset_0, 1.0f - inset_0)) / _S1766)), level((0.0f)))).xyz;
                break;
            }
            float _S1770 = _S1760.environmentControls_0.y;
            float _S1771 = _S1760.environmentControls_0.z;
            for(;;)
            {
                if(_S34)
                {
                    float3 unit_2 = normalize(reflectionDirection_0);
                    float u_0 = fract((atan2(unit_2.z, unit_2.x) + 1.57079637050628662f) / 6.28318548202514648f);
                    float _S1772 = max(_S1770, 1.0f);
                    float _S1773 = _S1772 - 1.0f;
                    float slice_0 = _S1762 * max(_S1773, 0.0f);
                    float lower_0 = floor(slice_0);
                    float inset_1 = 0.5f / max(_S1771, 1.0f);
                    float v_0 = clamp(acos(clamp(unit_2.y, -1.0f, 1.0f)) / 3.14159274101257324f, inset_1, 1.0f - inset_1);
                    specularColor_0 = mix((((&kernelContext_2)->environmentSpecular_0).sample(((&kernelContext_2)->environmentSampler_0), (float2(u_0, (lower_0 + v_0) / _S1772)), level((0.0f)))).xyz, (((&kernelContext_2)->environmentSpecular_0).sample(((&kernelContext_2)->environmentSampler_0), (float2(u_0, (min(lower_0 + 1.0f, _S1773) + v_0) / _S1772)), level((0.0f)))).xyz, float3((slice_0 - lower_0)) );
                    break;
                }
                float3 unit_3 = normalize(reflectionDirection_0);
                float u_1 = fract((atan2(unit_3.z, unit_3.x) + 1.57079637050628662f) / 6.28318548202514648f);
                float _S1774 = max(_S1770, 1.0f);
                float total_0 = _S1774 * _S1766;
                float _S1775 = _S1774 - 1.0f;
                float slice_1 = _S1762 * max(_S1775, 0.0f);
                float lower_1 = floor(slice_1);
                float inset_2 = 0.5f / max(_S1771, 1.0f);
                float v_1 = clamp(acos(clamp(unit_3.y, -1.0f, 1.0f)) / 3.14159274101257324f, inset_2, 1.0f - inset_2);
                float base_0 = composedGroup_0 * _S1774;
                specularColor_0 = mix((((&kernelContext_2)->environmentSpecular_0).sample(((&kernelContext_2)->environmentSampler_0), (float2(u_1, (base_0 + lower_1 + v_1) / total_0)), level((0.0f)))).xyz, (((&kernelContext_2)->environmentSpecular_0).sample(((&kernelContext_2)->environmentSampler_0), (float2(u_1, (base_0 + min(lower_1 + 1.0f, _S1775) + v_1) / total_0)), level((0.0f)))).xyz, float3((slice_1 - lower_1)) );
                break;
            }
            if(clearcoatAmount_0 > 0.0f)
            {
                for(;;)
                {
                    if(_S34)
                    {
                        float3 unit_4 = normalize(reflectionDirection_0);
                        float u_2 = fract((atan2(unit_4.z, unit_4.x) + 1.57079637050628662f) / 6.28318548202514648f);
                        float _S1776 = max(_S1770, 1.0f);
                        float _S1777 = _S1776 - 1.0f;
                        float slice_2 = saturate(_S1690) * max(_S1777, 0.0f);
                        float lower_2 = floor(slice_2);
                        float inset_3 = 0.5f / max(_S1771, 1.0f);
                        float v_2 = clamp(acos(clamp(unit_4.y, -1.0f, 1.0f)) / 3.14159274101257324f, inset_3, 1.0f - inset_3);
                        diffuseFresnel_0 = mix((((&kernelContext_2)->environmentSpecular_0).sample(((&kernelContext_2)->environmentSampler_0), (float2(u_2, (lower_2 + v_2) / _S1776)), level((0.0f)))).xyz, (((&kernelContext_2)->environmentSpecular_0).sample(((&kernelContext_2)->environmentSampler_0), (float2(u_2, (min(lower_2 + 1.0f, _S1777) + v_2) / _S1776)), level((0.0f)))).xyz, float3((slice_2 - lower_2)) );
                        break;
                    }
                    float3 unit_5 = normalize(reflectionDirection_0);
                    float u_3 = fract((atan2(unit_5.z, unit_5.x) + 1.57079637050628662f) / 6.28318548202514648f);
                    float _S1778 = max(_S1770, 1.0f);
                    float total_1 = _S1778 * _S1766;
                    float _S1779 = _S1778 - 1.0f;
                    float slice_3 = saturate(_S1690) * max(_S1779, 0.0f);
                    float lower_3 = floor(slice_3);
                    float inset_4 = 0.5f / max(_S1771, 1.0f);
                    float v_3 = clamp(acos(clamp(unit_5.y, -1.0f, 1.0f)) / 3.14159274101257324f, inset_4, 1.0f - inset_4);
                    float base_1 = composedGroup_0 * _S1778;
                    diffuseFresnel_0 = mix((((&kernelContext_2)->environmentSpecular_0).sample(((&kernelContext_2)->environmentSampler_0), (float2(u_3, (base_1 + lower_3 + v_3) / total_1)), level((0.0f)))).xyz, (((&kernelContext_2)->environmentSpecular_0).sample(((&kernelContext_2)->environmentSampler_0), (float2(u_3, (base_1 + min(lower_3 + 1.0f, _S1779) + v_3) / total_1)), level((0.0f)))).xyz, float3((slice_3 - lower_3)) );
                    break;
                }
                lightBitangent_0 = diffuseFresnel_0;
            }
            else
            {
                lightBitangent_0 = _S1767;
            }
            lightDirection_0 = diffuseColor_0;
            lightTangent_0 = specularColor_0;
        }
        else
        {
            sampleIndex_0 = 0U;
            lightDirection_0 = _S1767;
            lightTangent_0 = _S1767;
            lightBitangent_0 = _S1767;
            for(;;)
            {
                if(sampleIndex_0 < _S1703)
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
                float domeGroup_0 = _S32.domeEnvironment_0[sampleIndex_0].x;
                if(domeGroup_0 < 0.0f)
                {
                    sampleIndex_0 = sampleIndex_0 + 1U;
                    continue;
                }
                float _S1780 = _S1702.domeControls_0.w;
                for(;;)
                {
                    bool _S1781 = _S1766 <= 1.0f;
                    _S35 = _S1781;
                    if(_S1781)
                    {
                        float3 unit_6 = normalize(worldNormal_2);
                        diffuseColor_0 = (((&kernelContext_2)->environmentIrradiance_0).sample(((&kernelContext_2)->environmentSampler_0), (float2(fract((atan2(unit_6.z, unit_6.x) + 1.57079637050628662f) / 6.28318548202514648f), acos(clamp(unit_6.y, -1.0f, 1.0f)) / 3.14159274101257324f)), level((0.0f)))).xyz;
                        break;
                    }
                    float3 unit_7 = normalize(worldNormal_2);
                    float inset_5 = 0.5f / max(_S1780, 1.0f);
                    diffuseColor_0 = (((&kernelContext_2)->environmentIrradiance_0).sample(((&kernelContext_2)->environmentSampler_0), (float2(fract((atan2(unit_7.z, unit_7.x) + 1.57079637050628662f) / 6.28318548202514648f), (domeGroup_0 + clamp(acos(clamp(unit_7.y, -1.0f, 1.0f)) / 3.14159274101257324f, inset_5, 1.0f - inset_5)) / _S1766)), level((0.0f)))).xyz;
                    break;
                }
                float3 irradiance_0 = lightDirection_0 + diffuseColor_0;
                float _S1782 = _S1760.environmentControls_0.y;
                float _S1783 = _S1760.environmentControls_0.z;
                for(;;)
                {
                    if(_S35)
                    {
                        float3 unit_8 = normalize(reflectionDirection_0);
                        float u_4 = fract((atan2(unit_8.z, unit_8.x) + 1.57079637050628662f) / 6.28318548202514648f);
                        float _S1784 = max(_S1782, 1.0f);
                        float _S1785 = _S1784 - 1.0f;
                        float slice_4 = _S1762 * max(_S1785, 0.0f);
                        float lower_4 = floor(slice_4);
                        float inset_6 = 0.5f / max(_S1783, 1.0f);
                        float v_4 = clamp(acos(clamp(unit_8.y, -1.0f, 1.0f)) / 3.14159274101257324f, inset_6, 1.0f - inset_6);
                        specularColor_0 = mix((((&kernelContext_2)->environmentSpecular_0).sample(((&kernelContext_2)->environmentSampler_0), (float2(u_4, (lower_4 + v_4) / _S1784)), level((0.0f)))).xyz, (((&kernelContext_2)->environmentSpecular_0).sample(((&kernelContext_2)->environmentSampler_0), (float2(u_4, (min(lower_4 + 1.0f, _S1785) + v_4) / _S1784)), level((0.0f)))).xyz, float3((slice_4 - lower_4)) );
                        break;
                    }
                    float3 unit_9 = normalize(reflectionDirection_0);
                    float u_5 = fract((atan2(unit_9.z, unit_9.x) + 1.57079637050628662f) / 6.28318548202514648f);
                    float _S1786 = max(_S1782, 1.0f);
                    float total_2 = _S1786 * _S1766;
                    float _S1787 = _S1786 - 1.0f;
                    float slice_5 = _S1762 * max(_S1787, 0.0f);
                    float lower_5 = floor(slice_5);
                    float inset_7 = 0.5f / max(_S1783, 1.0f);
                    float v_5 = clamp(acos(clamp(unit_9.y, -1.0f, 1.0f)) / 3.14159274101257324f, inset_7, 1.0f - inset_7);
                    float base_2 = domeGroup_0 * _S1786;
                    specularColor_0 = mix((((&kernelContext_2)->environmentSpecular_0).sample(((&kernelContext_2)->environmentSampler_0), (float2(u_5, (base_2 + lower_5 + v_5) / total_2)), level((0.0f)))).xyz, (((&kernelContext_2)->environmentSpecular_0).sample(((&kernelContext_2)->environmentSampler_0), (float2(u_5, (base_2 + min(lower_5 + 1.0f, _S1787) + v_5) / total_2)), level((0.0f)))).xyz, float3((slice_5 - lower_5)) );
                    break;
                }
                float3 prefiltered_0 = lightTangent_0 + specularColor_0;
                if(clearcoatAmount_0 > 0.0f)
                {
                    for(;;)
                    {
                        if(_S35)
                        {
                            float3 unit_10 = normalize(reflectionDirection_0);
                            float u_6 = fract((atan2(unit_10.z, unit_10.x) + 1.57079637050628662f) / 6.28318548202514648f);
                            float _S1788 = max(_S1782, 1.0f);
                            float _S1789 = _S1788 - 1.0f;
                            float slice_6 = saturate(_S1690) * max(_S1789, 0.0f);
                            float lower_6 = floor(slice_6);
                            float inset_8 = 0.5f / max(_S1783, 1.0f);
                            float v_6 = clamp(acos(clamp(unit_10.y, -1.0f, 1.0f)) / 3.14159274101257324f, inset_8, 1.0f - inset_8);
                            diffuseFresnel_0 = mix((((&kernelContext_2)->environmentSpecular_0).sample(((&kernelContext_2)->environmentSampler_0), (float2(u_6, (lower_6 + v_6) / _S1788)), level((0.0f)))).xyz, (((&kernelContext_2)->environmentSpecular_0).sample(((&kernelContext_2)->environmentSampler_0), (float2(u_6, (min(lower_6 + 1.0f, _S1789) + v_6) / _S1788)), level((0.0f)))).xyz, float3((slice_6 - lower_6)) );
                            break;
                        }
                        float3 unit_11 = normalize(reflectionDirection_0);
                        float u_7 = fract((atan2(unit_11.z, unit_11.x) + 1.57079637050628662f) / 6.28318548202514648f);
                        float _S1790 = max(_S1782, 1.0f);
                        float total_3 = _S1790 * _S1766;
                        float _S1791 = _S1790 - 1.0f;
                        float slice_7 = saturate(_S1690) * max(_S1791, 0.0f);
                        float lower_7 = floor(slice_7);
                        float inset_9 = 0.5f / max(_S1783, 1.0f);
                        float v_7 = clamp(acos(clamp(unit_11.y, -1.0f, 1.0f)) / 3.14159274101257324f, inset_9, 1.0f - inset_9);
                        float base_3 = domeGroup_0 * _S1790;
                        diffuseFresnel_0 = mix((((&kernelContext_2)->environmentSpecular_0).sample(((&kernelContext_2)->environmentSampler_0), (float2(u_7, (base_3 + lower_7 + v_7) / total_3)), level((0.0f)))).xyz, (((&kernelContext_2)->environmentSpecular_0).sample(((&kernelContext_2)->environmentSampler_0), (float2(u_7, (base_3 + min(lower_7 + 1.0f, _S1791) + v_7) / total_3)), level((0.0f)))).xyz, float3((slice_7 - lower_7)) );
                        break;
                    }
                    color_0 = lightBitangent_0 + diffuseFresnel_0;
                }
                else
                {
                    color_0 = lightBitangent_0;
                }
                lightDirection_0 = irradiance_0;
                lightTangent_0 = prefiltered_0;
                lightBitangent_0 = color_0;
                sampleIndex_0 = sampleIndex_0 + 1U;
            }
        }
        float3 color_4 = color_3 + float3(occlusion_0)  * diffuse_1 * lightDirection_0 * diffuseWeight_0 + float3(occlusion_0)  * lightTangent_0 * specularWeight_0;
        if(clearcoatAmount_0 > 0.0f)
        {
            float2 _S1792 = (((&kernelContext_2)->environmentBrdf_0).sample(((&kernelContext_2)->environmentBrdfSampler_0), (float2(_S1761, saturate(_S1690))), level((0.0f)))).xy;
            color_3 = color_4 + float3((occlusion_0 * clearcoatAmount_0))  * lightBitangent_0 * (float3((reflectanceRatio_0 * reflectanceRatio_0))  * float3(_S1792.x)  + float3(_S1792.y) );
        }
        else
        {
            color_3 = color_4;
        }
    }
    float3 color_5 = (color_3 + unlitColor_0) * float3(exp2((as_type<float>((_S33.z))))) ;
    if((_S33.y) == 1U)
    {
        color_3 = color_5 / (float3(1.0f)  + max(color_5, float3(0.0f, 0.0f, 0.0f)));
    }
    else
    {
        color_3 = color_5;
    }
    pixelOutput_0 _S1793 = { float4(color_3, opacity_0) };
    return _S1793;
}
